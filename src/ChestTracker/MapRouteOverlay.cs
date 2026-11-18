using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ChestTracker.Core;
using ChestTracker.Native;

namespace ChestTracker;

/// <summary>
/// Draws one of your routes over the Fortnite map while it's open. When you press your map key, Chest Tracker looks
/// at the screen (a small picture kept in memory, never saved) to find where the map is and how far it's zoomed,
/// then draws the route on top, following as you zoom and move the map, on whichever monitor Fortnite is on.
/// It stops looking as soon as the map closes. Nothing in the game is read or changed.
/// </summary>
public sealed class MapRouteOverlay
{
    const int WorkHeight = 720;       // the screen picture is shrunk to this height before looking for the map
    public static string CachePath => System.IO.Path.Combine(AppState.DataDir, "map_landmarks.bin");
    public static string CheckPicturePath => System.IO.Path.Combine(AppState.DataDir, "map_check.png");

    readonly AppState _app;
    readonly Dispatcher _ui;
    readonly object _gate = new();
    MapMatcher? _matcher;
    Task<MapMatcher?>? _building;
    CancellationTokenSource? _session;
    MapRouteWindow? _window;

    public MapRouteOverlay(AppState app, Dispatcher ui)
    {
        _app = app;
        _ui = ui;
    }

    public bool Active { get { lock (_gate) return _session is not null; } }

    // ------------------------------------------------------------------ getting ready

    /// <summary>Works out the map's landmarks in the background (once; they're saved next to the app).</summary>
    public Task<MapMatcher?> PrepareAsync()
    {
        lock (_gate)
        {
            if (_matcher is not null) return Task.FromResult<MapMatcher?>(_matcher);
            return _building ??= Task.Run(BuildMatcher);
        }
    }

    async Task<MapMatcher?> BuildMatcher()
    {
        try
        {
            var path = await _app.MapImageAsync().ConfigureAwait(false);
            if (path is null) return null;
            var info = new FileInfo(path);
            string key = $"{info.Length}-{info.LastWriteTimeUtc.Ticks}-{MapMatcher.LevelScales.Length}";
            var thread = Thread.CurrentThread;
            var before = thread.Priority;
            thread.Priority = ThreadPriority.Lowest;   // never compete with the game
            try
            {
                var m = MapMatcher.Load(CachePath, key);
                if (m is null)
                {
                    m = MapMatcher.Build(LoadGray(path));
                    try { m.Save(CachePath, key); } catch (IOException) { }
                    Memory.Trim();
                }
                lock (_gate) _matcher = m;
                return m;
            }
            finally { thread.Priority = before; }
        }
        catch (Exception) { return null; }
        finally { lock (_gate) _building = null; }
    }

    static GrayImage LoadGray(string path)
    {
        using var fs = File.OpenRead(path);
        var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var gray = new FormatConvertedBitmap(frame, PixelFormats.Gray8, null, 0);
        var px = new byte[gray.PixelWidth * gray.PixelHeight];
        gray.CopyPixels(px, gray.PixelWidth, 0);
        return new GrayImage(gray.PixelWidth, gray.PixelHeight, px);
    }

    // ------------------------------------------------------------------ which route

    /// <summary>The route to draw: the one you picked on the Routes page, or your best (most chests) this season.</summary>
    public RouteSummary? ChosenRoute()
    {
        var routes = RouteBook.All(_app.Store.Snapshot(), _app.Treasure.Map.FromUtc.ToLocalTime());
        var picked = _app.Settings.MapOverlayMatch is string id ? routes.FirstOrDefault(r => r.Match == id) : null;
        return picked ?? RouteBook.Best(routes);
    }

    // ------------------------------------------------------------------ map key pressed / closed

    volatile bool _showing;      // the route is on screen (the map was found a moment ago)
    volatile bool _searchAgain;  // the map key was pressed while the route wasn't showing: look for the map again

    /// <summary>
    /// Your map key. If the route is showing, the map is closing: stop. Otherwise the map is opening: look for it (also
    /// when a look is already going on, say just after coming back to the game, so a press is never "out of step").
    /// </summary>
    public void Toggle()
    {
        if (Active)
        {
            if (_showing) Stop();
            else _searchAgain = true;
            return;
        }
        if (!_app.Settings.MapOverlay) return;
        var matcher = _matcher;
        if (matcher is null) { _ = PrepareAsync(); return; }   // first time: get ready for next time
        var route = ChosenRoute();
        if (route is null) return;
        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            if (_session is not null) return;
            _session = cts;
        }
        string label = Label(route);
        // normal priority while the map is open, so the route keeps up when you zoom (it stops when the map closes)
        new Thread(() => Run(matcher, route, label, cts)) { IsBackground = true, Name = "MapRoute", Priority = ThreadPriority.Normal }.Start();
    }

    Timer? _releaseLater;

    /// <summary>
    /// Lets go of the screen copy 90 seconds after the map closes (setting it up takes a moment, so opening the map
    /// again soon after is instant; if it isn't opened again, the memory is given back).
    /// </summary>
    void ReleaseSoon()
    {
        lock (_gate)
        {
            _releaseLater?.Dispose();
            _releaseLater = new Timer(_ => { if (!Active) ScreenCapture.Release(); }, null, TimeSpan.FromSeconds(90), Timeout.InfiniteTimeSpan);
        }
    }

    public void Stop()
    {
        CancellationTokenSource? s;
        lock (_gate) { s = _session; _session = null; }
        s?.Cancel();
        _ui.BeginInvoke(() => _window?.Hide());
    }

    string Label(RouteSummary r)
    {
        bool picked = _app.Settings.MapOverlayMatch == r.Match;
        return $"{(picked ? "Your picked route" : "Your best route")} · {r.Chests} chest{(r.Chests == 1 ? "" : "s")} · {r.Start:ddd d MMM}";
    }

    sealed record Shot(Win32.RECT Area, GrayImage Image, long TakenAt);

    static Shot? TakeShot()
    {
        long at = System.Diagnostics.Stopwatch.GetTimestamp();
        return ScreenCapture.FortniteArea() is { } area && ScreenCapture.Gray(area, WorkHeight) is { } img ? new Shot(area, img, at) : null;
    }

    void Run(MapMatcher matcher, RouteSummary route, string label, CancellationTokenSource cts)
    {
        var ct = cts.Token;
        var started = DateTime.UtcNow;
        DateTime? lastSeen = null;
        MapFix? prev = null;
        var cal = _app.Treasure.Map;
        var points = route.Points.Select(p =>
        {
            var (mx, my) = RouteBook.ToMapPixels(cal, p.X!.Value, p.Y!.Value, matcher.MapWidth, matcher.MapHeight);
            return new RoutePoint(mx, my, p.Kind?.StartsWith("Sprite", StringComparison.Ordinal) == true, p.Broken);
        }).ToList();
        Task<Shot?>? nextShot = null;
        var lookUntil = started.AddSeconds(6);   // how long to look for the map before giving up
        _searchAgain = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var tick = DateTime.UtcNow;
                if (_searchAgain) { _searchAgain = false; lookUntil = tick.AddSeconds(6); }
                var shot = nextShot is not null ? nextShot.Result : TakeShot();
                nextShot = null;
                if (shot is null)
                {
                    // Fortnite isn't in front (you tabbed out): hide the route and wait for the game to come back,
                    // then see if the map is still open
                    _showing = false;
                    _ui.BeginInvoke(() => _window?.Hide());
                    var away = DateTime.UtcNow;
                    while (!ct.IsCancellationRequested && ScreenCapture.FortniteArea() is null)
                        if (ct.WaitHandle.WaitOne(200) || DateTime.UtcNow - away > TimeSpan.FromMinutes(30)) break;
                    if (ct.IsCancellationRequested || ScreenCapture.FortniteArea() is null) break;
                    prev = null;
                    lastSeen = null;
                    lookUntil = DateTime.UtcNow.AddSeconds(3);
                    continue;
                }
                bool following = lastSeen is DateTime ls && tick - ls < TimeSpan.FromSeconds(0.6);
                // while following the map, take the next picture at the same time as this one is looked at
                if (following) nextShot = Task.Run(TakeShot);
                MapFix? fix = matcher.Find(shot.Image, following ? prev : null);
                if (ct.IsCancellationRequested) break;
                if (fix is { } f)
                {
                    prev = f;
                    lastSeen = DateTime.UtcNow;
                    _showing = true;
                    QueueDraw(new Frame(shot.Area, f, (shot.Area.Bottom - shot.Area.Top) / (double)shot.Image.Height, points, label, shot.TakenAt, ct));
                }
                else
                {
                    var nowT = DateTime.UtcNow;
                    // a missed picture (mid-zoom, say) keeps the route where it was for a moment
                    if (_showing && lastSeen is DateTime seen && nowT - seen > TimeSpan.FromSeconds(0.5))
                    {
                        _showing = false;
                        _ui.BeginInvoke(() => _window?.Hide());
                    }
                    // the map was closed (not seen for 1.5 s), or never showed up: stop looking
                    bool gone = lastSeen is not DateTime last || nowT - last > TimeSpan.FromSeconds(1.5);
                    if (gone && nowT > lookUntil) break;
                }
                // up to 60 times a second while following the map, 10 while looking for it
                int wait = (lastSeen is not null && DateTime.UtcNow - lastSeen < TimeSpan.FromSeconds(0.6) ? 16 : 100)
                           - (int)(DateTime.UtcNow - tick).TotalMilliseconds;
                if (wait > 0 && ct.WaitHandle.WaitOne(wait)) break;
            }
        }
        catch (Exception) { }
        finally
        {
            _showing = false;
            lock (_gate) { if (_session == cts) _session = null; }
            _ui.BeginInvoke(() => _window?.Hide());
            try { nextShot?.Wait(500); } catch (Exception) { }
            ReleaseSoon();
        }
    }

    sealed record Frame(Win32.RECT Area, MapFix Fix, double ToPhysical, IReadOnlyList<RoutePoint> Points, string Label, long TakenAt, CancellationToken Token);

    Frame? _latest;
    int _drawQueued;

    /// <summary>Draws the newest position only: if the screen is busy, older ones are skipped rather than queued up.</summary>
    void QueueDraw(Frame frame)
    {
        Volatile.Write(ref _latest, frame);
        if (Interlocked.Exchange(ref _drawQueued, 1) == 0)
            _ui.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _drawQueued, 0);
                var f = Volatile.Read(ref _latest);
                if (f is null || f.Token.IsCancellationRequested) return;
                _window ??= new MapRouteWindow();
                _window.Draw(f.Area, f.Fix, f.ToPhysical, f.Points, f.Label, f.TakenAt);
            });
    }

    // ------------------------------------------------------------------ "Check it works"

    /// <summary>
    /// Waits up to <paramref name="seconds"/> for Fortnite to be in front with the map open, then says whether the map
    /// was found. One picture of what it saw (greyscale, with the route drawn on if found) is saved as map_check.png,
    /// replacing the last one.
    /// </summary>
    public async Task<string> CheckAsync(int seconds, IProgress<int>? countdown = null)
    {
        var matcher = await PrepareAsync().ConfigureAwait(false);
        if (matcher is null) return "Couldn't get the map picture (are you online?). It's needed once to know what the map looks like.";
        var route = ChosenRoute();
        try { return await CheckLoopAsync(matcher, route, seconds, countdown).ConfigureAwait(false); }
        finally { ReleaseSoon(); }
    }

    async Task<string> CheckLoopAsync(MapMatcher matcher, RouteSummary? route, int seconds, IProgress<int>? countdown)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        GrayImage? lastImg = null;
        while (DateTime.UtcNow < until)
        {
            countdown?.Report((int)Math.Ceiling((until - DateTime.UtcNow).TotalSeconds));
            if (ScreenCapture.FortniteArea() is { } area && ScreenCapture.Gray(area, WorkHeight) is { } img)
            {
                lastImg = img;
                var fix = await Task.Run(() => matcher.Find(img)).ConfigureAwait(false);
                if (fix is { } f)
                {
                    _ui.Invoke(() => SaveCheck(img, f, route, matcher));
                    return "Found your map. Your route will show on it when you press your map key in a match (map_check.png shows what it saw).";
                }
            }
            await Task.Delay(500).ConfigureAwait(false);
        }
        if (lastImg is null) return "Fortnite wasn't in front. Press Check, switch to Fortnite and open your map within the time.";
        _ui.Invoke(() => SaveCheck(lastImg, null, route, matcher));
        return "Couldn't find the map on your screen. Open the full map (not the minimap) and try again. What it saw is saved as map_check.png next to the app.";
    }

    void SaveCheck(GrayImage img, MapFix? fix, RouteSummary? route, MapMatcher matcher)
    {
        try
        {
            var src = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Gray8, null, img.Pixels, img.Width);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(src, new Rect(0, 0, img.Width, img.Height));
                if (fix is { } f && route is not null)
                {
                    var pen = new Pen(Brushes.Gold, 3);
                    Point? last = null;
                    foreach (var p in route.Points)
                    {
                        var (mx, my) = RouteBook.ToMapPixels(_app.Treasure.Map, p.X!.Value, p.Y!.Value, matcher.MapWidth, matcher.MapHeight);
                        var at = new Point(f.Scale * mx + f.OffsetX, f.Scale * my + f.OffsetY);
                        if (last is Point l) dc.DrawLine(pen, l, at);
                        dc.DrawEllipse(Brushes.Gold, null, at, 6, 6);
                        last = at;
                    }
                }
            }
            var rtb = new RenderTargetBitmap(img.Width, img.Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(CheckPicturePath);
            enc.Save(fs);
        }
        catch (Exception) { }
    }
}

public readonly record struct RoutePoint(double MapX, double MapY, bool Sprite, bool Broken);

/// <summary>
/// A see-through, click-through window over the game with the route drawn on it. It's only as big as the route needs
/// (a big see-through window is slow to redraw), and the route glides at the screen's refresh rate between map
/// positions, a little ahead in the direction you're zooming or moving, so it keeps up with the map instead of jumping.
/// </summary>
sealed class MapRouteWindow : Window
{
    readonly Canvas _canvas = new();
    IntPtr _hwnd;
    Win32.RECT _area;                  // the game's picture area (physical pixels)
    Win32.RECT _win;                   // where this window is now (physical pixels)
    // the route's shapes, made once and then only moved
    IReadOnlyList<RoutePoint>? _points;
    string? _label;
    Polyline? _shadow, _line;
    readonly List<FrameworkElement> _dots = new();
    Border? _tag;
    DateTime _keptOnTop;
    // where the map is and what's on screen now (physical pixels from the game area's corner)
    readonly RouteMotion _motion = new();
    long _lastFrame;
    bool _hooked;

    public MapRouteWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Title = "Chest Tracker route";
        Content = _canvas;
        _canvas.IsHitTestVisible = false;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Win32.SetExStyle(_hwnd, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT, 0);
            Win32.ExcludeFromCapture(_hwnd);   // so it never sees its own drawing when looking for the map
        };
        DpiChanged += (_, _) => { if (_hwnd != IntPtr.Zero && _win.Right > _win.Left) Win32.PlaceTopmost(_hwnd, _win); };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) return;
            Unhook();
            _motion.Reset();
        };
    }

    /// <summary>Makes the route's shapes (once per route).</summary>
    void Build(IReadOnlyList<RoutePoint> points, string label)
    {
        _points = points;
        _label = label;
        _canvas.Children.Clear();
        _dots.Clear();
        var gold = (Brush)Application.Current.FindResource("Gold");
        _shadow = new Polyline { Stroke = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), StrokeThickness = 8, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _line = new Polyline { Stroke = gold, StrokeThickness = 4, StrokeLineJoin = PenLineJoin.Round, StrokeDashArray = new DoubleCollection { 2.5, 1.5 } };
        _canvas.Children.Add(_shadow);
        _canvas.Children.Add(_line);
        var orange = (Brush)Application.Current.FindResource("Orange");
        var sprite = new SolidColorBrush(Color.FromRgb(0xB5, 0x7B, 0xFF));
        var ink = new SolidColorBrush(Color.FromRgb(0x1B, 0x13, 0x00));
        var edge = new SolidColorBrush(Color.FromArgb(0xE0, 0, 0, 0));
        for (int i = 0; i < points.Count; i++)
        {
            double size = i == 0 ? 30 : 24;
            var dot = new Grid { Width = size, Height = size };
            dot.Children.Add(new Ellipse { Fill = points[i].Sprite ? sprite : points[i].Broken ? orange : gold, Stroke = edge, StrokeThickness = 2 });
            dot.Children.Add(new TextBlock
            {
                Text = (i + 1).ToString(CultureInfo.InvariantCulture), FontSize = i + 1 >= 10 ? 11.5 : 13, FontWeight = FontWeights.Bold,
                Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            _dots.Add(dot);
            _canvas.Children.Add(dot);
        }
        _tag = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x10, 0x12, 0x18)), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 6, 12, 6), BorderBrush = gold, BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold },
        };
        _tag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _canvas.Children.Add(_tag);
        // follow the route's middle and size on screen
        double minX = points.Min(p => p.MapX), maxX = points.Max(p => p.MapX), minY = points.Min(p => p.MapY), maxY = points.Max(p => p.MapY);
        _motion.SetRoute((minX + maxX) / 2, (minY + maxY) / 2, Math.Max(maxX - minX, maxY - minY));
        _area = default;   // place the window (and the label) again
    }

    /// <summary>The newest map position (from a screen picture taken at <paramref name="takenAt"/>, a Stopwatch time).</summary>
    public void Draw(Win32.RECT area, MapFix fix, double toPhysical, IReadOnlyList<RoutePoint> points, string label, long takenAt)
    {
        if (!ReferenceEquals(points, _points) || label != _label) Build(points, label);
        if (!IsVisible) Show();
        if (_hwnd == IntPtr.Zero) return;
        if (area.Left != _area.Left || area.Top != _area.Top || area.Right != _area.Right || area.Bottom != _area.Bottom)
        {
            // one window over the whole game, placed once (moving or resizing it while the route moves made it jump)
            _area = area;
            _win = area;
            _motion.Reset();
            Win32.PlaceTopmost(_hwnd, _win);
            var dpi = VisualTreeHelper.GetDpi(this);
            if (_tag is not null)
            {
                Canvas.SetLeft(_tag, Math.Max(8, ((area.Right - area.Left) / dpi.DpiScaleX - _tag.DesiredSize.Width) / 2));
                Canvas.SetTop(_tag, 14);
            }
        }
        _motion.Target(fix.Scale * toPhysical, fix.OffsetX * toPhysical, fix.OffsetY * toPhysical, takenAt);
        if (!_hooked) { CompositionTarget.Rendering += OnRendering; _hooked = true; }
        if (!_motion.Shown) Step(System.Diagnostics.Stopwatch.GetTimestamp());   // show it straight away the first time
    }

    void Unhook()
    {
        if (!_hooked) return;
        CompositionTarget.Rendering -= OnRendering;
        _hooked = false;
    }

    void OnRendering(object? sender, EventArgs e)
    {
        Step(System.Diagnostics.Stopwatch.GetTimestamp());   // every screen frame (it skips the redraw if nothing moved)
    }

    void Step(long now)
    {
        _lastFrame = now;
        if (_points is not null && _motion.Step(now)) Apply();
    }

    /// <summary>Moves the route to where it is on screen now.</summary>
    void Apply()
    {
        if (_points is null || _shadow is null || _line is null || _hwnd == IntPtr.Zero) return;
        int n = _points.Count;
        var dpi = VisualTreeHelper.GetDpi(this);
        // physical pixels from the game area's corner -> window units (the window is exactly over the game area)
        double kx = 1 / dpi.DpiScaleX, ky = 1 / dpi.DpiScaleY;
        var line = new PointCollection(n);
        for (int i = 0; i < n; i++)
        {
            var at = new Point((_motion.S * _points[i].MapX + _motion.X) * kx, (_motion.S * _points[i].MapY + _motion.Y) * ky);
            line.Add(at);
            var dot = _dots[i];
            Canvas.SetLeft(dot, at.X - dot.Width / 2);
            Canvas.SetTop(dot, at.Y - dot.Height / 2);
        }
        line.Freeze();
        _shadow.Points = line;
        _line.Points = line;
        if (DateTime.UtcNow - _keptOnTop > TimeSpan.FromSeconds(1))
        {
            _keptOnTop = DateTime.UtcNow;
            Win32.KeepTopmost(_hwnd);
        }
    }
}
