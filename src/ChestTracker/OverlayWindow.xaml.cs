using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ChestTracker.Core;
using ChestTracker.Native;

namespace ChestTracker;

/// <summary>
/// Always-on-top panel over Fortnite. It never takes focus (WS_EX_NOACTIVATE), and whenever
/// Fortnite has the mouse captured it is forced click-through, so a click can't pull you out
/// of the game. In move mode (F6) you can drag it and right-click it for a menu.
/// </summary>
public partial class OverlayWindow : Window
{
    readonly AppState _app;
    readonly SolidColorBrush _countBrush = new(Color.FromRgb(0xEE, 0xF1, 0xF6));
    readonly DispatcherTimer _tick, _keepTop, _toastTimer;
    IntPtr _hwnd;
    bool _inGame;
    bool? _appliedThrough;
    long _seenTotal = -1;
    bool _dragging;
    Point _dragStart;
    double _startLeft, _startTop;

    /// <summary>Overridable for tests: does the game currently own the mouse?</summary>
    public Func<bool> GameHasMouse { get; set; } = () => Win32.FortniteFocused() && Win32.CursorCaptured();

    public OverlayWindow(AppState app)
    {
        InitializeComponent();
        _app = app;
        CountText.Foreground = _countBrush;
        Opacity = app.Settings.OverlayOpacity;
        ApplyLook();
        MoveToSaved();

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _tick.Tick += (_, _) => Tick();
        _keepTop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _keepTop.Tick += (_, _) => { if (_hwnd != IntPtr.Zero) Win32.KeepTopmost(_hwnd); };
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastBox.Visibility = Visibility.Collapsed; };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Win32.SetExStyle(_hwnd, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_LAYERED, 0);
            ApplyLock();
            _tick.Start();
            _keepTop.Start();
        };
        Closed += (_, _) => { _tick.Stop(); _keepTop.Stop(); _toastTimer.Stop(); };
        Tick();
    }

    public IntPtr Handle => _hwnd;
    public bool ClickThrough => _app.Settings.OverlayLocked || _inGame;

    // ------------------------------------------------------------------ position

    void MoveToSaved()
    {
        double left = _app.Settings.OverlayLeft ?? 24;
        double top = _app.Settings.OverlayTop ?? SystemParameters.PrimaryScreenHeight * 0.30;
        Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft,
                          SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80);
        Top = Math.Clamp(top, SystemParameters.VirtualScreenTop,
                         SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60);
    }

    public void MoveToDefault() => MoveToSaved();

    // ------------------------------------------------------------------ look (Settings → Overlay)

    /// <summary>Size, compact mode, what's shown and colours, from the settings. Call again after a change.</summary>
    public void ApplyLook()
    {
        var s = _app.Settings;
        double scale = Math.Clamp(s.OverlayScale, 0.6, 2.0);
        Scale.ScaleX = scale;
        Scale.ScaleY = scale;

        bool compact = s.OverlayCompact;
        Panel.Width = compact ? 220 : 268;
        SessionLabel.Text = compact ? "Session" : "This session";
        NextHourLabel.Text = compact ? "Next boost" : "Next Treasure Hour";
        Panel.Padding = compact ? new Thickness(14, 10, 14, 9) : new Thickness(18, 14, 18, 12);
        Panel.CornerRadius = new CornerRadius(compact ? 14 : 18);
        MoveOutline.RadiusX = MoveOutline.RadiusY = compact ? 14 : 18;
        CountText.FontSize = compact ? 28 : 38;
        ChanceText.FontSize = compact ? 15 : 17;
        HeaderLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

        static Visibility V(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        CountText.Visibility = V(s.OverlayShowCount);
        ChanceBox.Visibility = V(s.OverlayShowChance);
        OneInText.Visibility = V(s.OverlayShowOneIn && !compact);
        CountRow.Visibility = V(s.OverlayShowCount || s.OverlayShowChance);
        SessionRow.Visibility = V(s.OverlayShowSession);
        SquadTotalRow.Visibility = V(_app.ShowSquadTotal);
        SquadTotalLabel.Text = compact ? "With squad" : "With your squad";
        TodayRow.Visibility = V(s.OverlayShowToday);
        NextHourRow.Visibility = V(s.OverlayShowNextHour);
        StatusLine.Visibility = V(s.OverlayShowStatus);

        var bg = ThemeManager.Parse(s.OverlayColor, Palette.DefaultOverlay);
        var text = ThemeManager.Parse(s.OverlayTextColor, Palette.DefaultText);
        var accent = ThemeManager.Parse(s.AccentColor, Palette.DefaultAccent);
        Panel.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb(0xF2, bg.R, bg.G, bg.B), 0),
                new(Color.FromArgb(0xF2, (byte)(bg.R * 0.75), (byte)(bg.G * 0.75), (byte)(bg.B * 0.75)), 0.6),
                new(Color.FromArgb(0xF2, ThemeManager.Mix(bg, accent, 0.08).R, ThemeManager.Mix(bg, accent, 0.08).G, ThemeManager.Mix(bg, accent, 0.08).B), 1),
            }, new Point(0, 0), new Point(1, 1));
        Foreground = new SolidColorBrush(text);
        _countBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        _countBrush.Color = text;
        ApplyLock();
    }

    // ------------------------------------------------------------------ live update

    void Tick()
    {
        bool inGame;
        try { inGame = GameHasMouse(); } catch (Exception) { inGame = false; }
        if (inGame != _inGame)
        {
            _inGame = inGame;
            ApplyLock();
        }

        long total = _app.Store.Total;
        if (_seenTotal >= 0 && total > _seenTotal) Flash();
        _seenTotal = total;
        CountText.Text = total.ToString("N0");
        double chance = _app.Chance;
        ChanceText.Text = Odds.Percent(chance);
        OneInText.Text = chance > 0 ? $"1 in {1 / chance:N0} for the token" : "token chance";

        if (++_slowTick % 12 == 1) UpdateLines();

        TreasureBadge.Visibility = _app.TreasureMode && _app.Settings.OverlayShowTreasure ? Visibility.Visible : Visibility.Collapsed;
        TreasureText.Text = $"TREASURE HOURS {_app.CurrentBoost:0.##}x";
        WarnBox.Visibility = _app.Tracker.ReplayWarning is null ? Visibility.Collapsed : Visibility.Visible;

        var t = _app.Tracker;
        string key = t.State switch
        {
            TrackerState.InMatch => "Gold",
            TrackerState.Reading => "Accent",
            TrackerState.Waiting => "Green",
            TrackerState.NoReplays => "Orange",
            _ => "TextFaint",
        };
        StatusLine.Text = StatusText(_app);
        var brush = (Brush)FindResource(key);
        Dot.Fill = brush;
        DotGlow.Fill = brush;
    }

    int _slowTick;

    /// <summary>The session, today and next Treasure Hour lines (every couple of seconds is plenty).</summary>
    public void UpdateLines()
    {
        SquadTotalRow.Visibility = _app.ShowSquadTotal ? Visibility.Visible : Visibility.Collapsed;
        if (_app.ShowSquadTotal)
            SquadTotalText.Text = _app.Settings.OverlayCompact ? $"{_app.TotalWithSquad:N0}"
                                : $"{_app.TotalWithSquad:N0} chests (+{_app.Squad.TotalChests:N0})";
        SessionText.Text = _app.Settings.OverlayCompact
            ? $"{_app.SessionChests:N0} in {_app.SessionMatches} match{(_app.SessionMatches == 1 ? "" : "es")}"
            : $"{_app.SessionChests:N0} chest{(_app.SessionChests == 1 ? "" : "s")} · {_app.SessionMatches} match{(_app.SessionMatches == 1 ? "" : "es")}";
        if (_app.Settings.OverlayShowToday) TodayText.Text = $"{_app.Store.CountSince(DateTime.Today):N0} chests";
        if (_app.Settings.OverlayShowNextHour) NextHourText.Text = NextHourShort(_app);
    }

    /// <summary>"live now", "in 3h 20m", "Sat 7:00 PM".</summary>
    static string NextHourShort(AppState app)
    {
        var now = DateTime.UtcNow;
        if (app.Treasure.NextHour(now) is not { } h) return "none announced";
        if (now >= h.StartUtc) return $"live · {h.Pirate:0.##}x";
        var left = h.StartUtc - now;
        return left.TotalHours < 24 ? $"in {(int)left.TotalHours}h {left.Minutes:00}m" : h.StartUtc.ToLocalTime().ToString("ddd h:mm tt");
    }

    /// <summary>One short line about tracking, shared with the main window.</summary>
    public static string StatusText(AppState app)
    {
        var t = app.Tracker;
        string last = app.Ledger.LastCounted() is { } e
            ? $"+{e.ChestsCounted} from last match" : "";
        return t.State switch
        {
            TrackerState.InMatch => "In a match · chests add when it ends",
            TrackerState.Reading => "Reading your match…",
            TrackerState.NoReplays => "Turn on Record Replays in Fortnite",
            TrackerState.Off => "Not tracking",
            _ => last.Length > 0 ? last : "Waiting for a match",
        };
    }

    /// <summary>Shows a short line (e.g. "+9 chests · 1 sprite · 2 broken open") for 10 seconds.</summary>
    public void ShowToast(string text)
    {
        ToastText.Text = text;
        ToastBox.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    void Flash()
    {
        var gold = (Color)FindResource("GoldColor");
        var white = ThemeManager.Parse(_app.Settings.OverlayTextColor, Palette.DefaultText);
        _countBrush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(gold, white, TimeSpan.FromMilliseconds(1100)) { EasingFunction = new QuadraticEase() });
        var pulse = new DoubleAnimation(0.9, 0.25, TimeSpan.FromMilliseconds(700));
        DotGlow.BeginAnimation(OpacityProperty, pulse);
    }

    // ------------------------------------------------------------------ lock / click-through

    public void ApplyLock()
    {
        bool through = ClickThrough;
        if (_hwnd != IntPtr.Zero && through != _appliedThrough)
        {
            if (through) Win32.SetExStyle(_hwnd, Win32.WS_EX_TRANSPARENT, 0);
            else Win32.SetExStyle(_hwnd, 0, Win32.WS_EX_TRANSPARENT);
            _appliedThrough = through;
            if (through && Panel.ContextMenu is { IsOpen: true } menu) menu.IsOpen = false;
        }
        MoveOutline.Visibility = through ? Visibility.Collapsed : Visibility.Visible;
        HintText.Text = _inGame || !_app.Settings.OverlayShowHint ? "" : _app.Settings.OverlayLocked ? "F6 to move"
                      : _app.Settings.OverlayCompact ? "drag · right-click · F6" : "drag to move  ·  right-click for menu  ·  F6 lock";
        HintText.Visibility = string.IsNullOrEmpty(HintText.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ------------------------------------------------------------------ drag (manual, so it never activates)

    void Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ClickThrough) return;
        _dragging = true;
        _dragStart = PointToScreen(e.GetPosition(this));
        _startLeft = Left;
        _startTop = Top;
        Panel.CaptureMouse();
    }

    void Panel_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var now = PointToScreen(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = _startLeft + (now.X - _dragStart.X) / dpi.DpiScaleX;
        Top = _startTop + (now.Y - _dragStart.Y) / dpi.DpiScaleY;
    }

    void Panel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Panel.ReleaseMouseCapture();
        _app.Settings.OverlayLeft = Left;
        _app.Settings.OverlayTop = Top;
        _app.SaveSettings();
    }

    // ------------------------------------------------------------------ right-click menu

    void Panel_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ClickThrough) return;   // never pop a menu (it takes focus) while the game has the mouse
        var itemStyle = (Style)FindResource("DarkMenuItem");
        MenuItem Item(string header, string gesture, Action act)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture, Style = itemStyle };
            mi.Click += (_, _) => act();
            return mi;
        }
        var menu = new ContextMenu { Style = (Style)FindResource("DarkContextMenu") };
        menu.Items.Add(Item(_app.TreasureMode ? "Turn off Treasure Hours" : "Turn on Treasure Hours", "F9", _app.ToggleTreasure));
        menu.Items.Add(Item("Add a chest", "F7", () => _app.AddChest("manual")));
        menu.Items.Add(Item("Undo last chest", "F8", _app.Undo));
        menu.Items.Add(new Separator { Style = (Style)FindResource("MenuSeparator") });
        menu.Items.Add(Item("Lock in place", "F6", _app.RequestOverlayLockToggle));
        menu.Items.Add(Item("Open Chest Tracker", "", () => (Application.Current.MainWindow as MainWindow)?.BringToFront()));
        menu.Items.Add(new Separator { Style = (Style)FindResource("MenuSeparator") });
        menu.Items.Add(Item("Quit", "", () => Application.Current.Shutdown()));
        Panel.ContextMenu = menu;
        menu.PlacementTarget = Panel;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
