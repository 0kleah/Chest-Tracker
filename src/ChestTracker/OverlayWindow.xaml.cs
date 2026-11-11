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
    readonly DispatcherTimer _tick, _keepTop;
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
        MoveToSaved();

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _tick.Tick += (_, _) => Tick();
        _keepTop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _keepTop.Tick += (_, _) => { if (_hwnd != IntPtr.Zero) Win32.KeepTopmost(_hwnd); };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Win32.SetExStyle(_hwnd, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_LAYERED, 0);
            ApplyLock();
            _tick.Start();
            _keepTop.Start();
        };
        Closed += (_, _) => { _tick.Stop(); _keepTop.Stop(); };
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

        TreasureBadge.Visibility = _app.TreasureMode ? Visibility.Visible : Visibility.Collapsed;
        TreasureText.Text = $"TREASURE HOURS {_app.Settings.Boost:0.##}x";

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

    void Flash()
    {
        var gold = (Color)FindResource("GoldColor");
        var white = (Color)FindResource("WhiteColor");
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
        HintText.Text = _inGame ? "" : _app.Settings.OverlayLocked ? "F6 to move" : "drag to move  ·  right-click for menu  ·  F6 lock";
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
