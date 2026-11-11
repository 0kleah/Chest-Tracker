using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ChestTracker.Core;
using ChestTracker.Native;

namespace ChestTracker;

public sealed record RecentRow(string Title, string Detail, Visibility TreasureVisibility);
public sealed record MilestoneRow(string Target, string Normal, string Treasure, string Matches);
public sealed record MatchRow(string When, string What);

public partial class MainWindow : Window
{
    const string RepoUrl = "https://github.com/0kleah/Chest-Tracker";
    readonly AppState _app;
    OverlayWindow? _overlay;
    bool _ready;
    readonly DispatcherTimer _liveTimer;

    public MainWindow()
    {
        InitializeComponent();
        _app = ((App)Application.Current).State;

        var version = typeof(MainWindow).Assembly.GetName().Version;
        VersionText.Text = $"v{version?.Major}.{version?.Minor}";
        AboutTitle.Text = $"Chest Tracker {version?.Major}.{version?.Minor}";
        DataPathText.Text = AppState.CountsPath;

        _app.CountsChanged += () => Dispatcher.BeginInvoke(RefreshCounts);
        _app.TreasureModeChanged += () => Dispatcher.BeginInvoke(() => { TreasureSwitch.IsChecked = _app.TreasureMode; RefreshCounts(); });
        _app.Tracker.StatusChanged += () => Dispatcher.BeginInvoke(RefreshStatus);
        _app.OverlayLockToggleRequested += () => Dispatcher.BeginInvoke(() => SetOverlayLocked(!_app.Settings.OverlayLocked));

        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _liveTimer.Tick += (_, _) => RefreshStatus();
        _liveTimer.Start();

        SourceInitialized += (_, _) => Win32.UseRoundedDarkFrame(new WindowInteropHelper(this).Handle);
        StateChanged += (_, _) =>
        {
            RootBorder.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        };
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _app.Tracker.Stop();
            _overlay?.Close();
        };
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadSettingsIntoControls();
        _ready = true;
        RefreshCounts();
        RefreshStatus();
        RecalculateOdds();
        if (_app.Settings.OverlayEnabled) ShowOverlay();
        if (_app.Settings.AutoStart) _app.Tracker.Start();

        // ChestTracker.exe --page odds|settings opens straight to that page
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--page");
        if (i >= 0 && i + 1 < args.Length)
            (args[i + 1].ToLowerInvariant() switch { "odds" => NavOdds, "settings" => NavSettings, _ => NavHome }).IsChecked = true;
    }

    // ================================================================== refresh

    void RefreshCounts()
    {
        long normal = _app.Store.Normal, treasure = _app.Store.Treasure, total = normal + treasure;
        double chance = _app.Chance;
        CountText.Text = total.ToString("N0");
        BreakdownText.Text = $"{normal:N0} normal · {treasure:N0} in Treasure Hours";
        long today = _app.Store.CountSince(DateTime.Today);
        TodayText.Text = today == 1 ? "1 today" : $"{today:N0} today";
        ChanceText.Text = Odds.Percent(chance);
        OneInText.Text = Odds.OneIn(chance);

        // progress toward a 50% chance, measured in "normal chest equivalents"
        long half = Odds.ChestsNeeded(0.5, Odds.PerChest);
        double equivalent = normal + treasure * _app.Settings.Boost;
        double progress = Math.Min(1, equivalent / half);
        HalfProgress.Value = progress;
        ProgressText.Text = (progress * 100).ToString(progress < 0.1 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + "%";
        long left = Math.Max(0, half - (long)equivalent);
        RemainingText.Text = left == 0 ? "You've passed a coin flip's worth of chests" : $"{left:N0} more chests to get there";

        BoostText.Text = $"{_app.Settings.Boost:0.##}x";
        var rows = _app.Store.Recent(6).Select(r =>
        {
            string when = r.Entry.When is DateTime t
                ? (t.Date == DateTime.Today ? t.ToString("h:mm tt") : t.ToString("MMM d, h:mm tt"))
                : "";
            if (r.Entry.Source == "bulk")
                return new RecentRow($"+{r.Entry.Chests:N0} chests", $"{when} · previous count, added by you",
                                     r.Entry.Treasure ? Visibility.Visible : Visibility.Collapsed);
            string title = r.Entry.Kind is string kind ? $"#{r.Number:N0} · {kind}" : $"Chest #{r.Number:N0}";
            string how = r.Entry.Source switch
            {
                "replay" => r.Entry.Broken ? "broken open · from the match replay" : "opened · from the match replay",
                "manual" => "added by hand",
                _ => "auto-detected (old screen method)",
            };
            return new RecentRow(title, $"{when} · {how}", r.Entry.Treasure ? Visibility.Visible : Visibility.Collapsed);
        }).ToList();
        RecentList.ItemsSource = rows;
        RecentEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TreasureSwitch.IsChecked = _app.TreasureMode;
        RefreshMatches();
    }

    void RefreshStatus()
    {
        var t = _app.Tracker;
        Brush dot = (Brush)FindResource(t.State switch
        {
            TrackerState.InMatch => "Gold",
            TrackerState.Reading => "Accent",
            TrackerState.Waiting => "Green",
            TrackerState.NoReplays => "Orange",
            _ => "TextFaint",
        });
        string text = t.State switch
        {
            TrackerState.InMatch => "In a match",
            TrackerState.Reading => "Reading match",
            TrackerState.Waiting => "Watching for matches",
            TrackerState.NoReplays => "No replays found",
            _ => "Not tracking",
        };
        StatusDot.Fill = dot;
        StatusText.Text = text;
        StartLabel.Text = t.Running ? "Stop tracking" : "Start tracking";
        StartIcon.Text = t.Running ? "" : "";

        ReplayStatusText.Text = OverlayWindow.StatusText(_app);
        ReplayDetailText.Text = t.State switch
        {
            TrackerState.NoReplays => $"Fortnite's replay folder wasn't found ({ReplayChests.DemosFolder}). Turn on Record Replays in Fortnite and play a match.",
            TrackerState.InMatch => "Fortnite is recording this match. Its chests are read as soon as it ends.",
            _ => LastMatchText(),
        };

        if (t.Error is string err)
        {
            ErrorBanner.Visibility = Visibility.Visible;
            ErrorText.Text = err;
        }
        else ErrorBanner.Visibility = Visibility.Collapsed;
    }

    string LastMatchText()
    {
        var e = _app.Ledger.LastCounted();
        if (e is null) return "Play a match. When it ends, its chests are added here.";
        var when = e.MatchStartUtc?.ToLocalTime().ToString("h:mm tt") ?? "";
        return $"Last match ({when}): {e.ChestsCounted} chest{(e.ChestsCounted == 1 ? "" : "s")} ({e.Opened} opened, {e.Broken} broken open)" +
               (e.AmmoBoxes > 0 ? $", {e.AmmoBoxes} ammo box{(e.AmmoBoxes == 1 ? "" : "es")} not counted" : "");
    }

    void RefreshMatches()
    {
        var rows = _app.Ledger.All().Where(e => !e.FromBefore && e.MatchStartUtc is not null)
            .OrderByDescending(e => e.MatchStartUtc).Take(8).Select(e =>
            {
                var t = e.MatchStartUtc!.Value.ToLocalTime();
                string when = t.Date == DateTime.Today ? "Today " + t.ToString("h:mm tt") : t.ToString("MMM d, h:mm tt");
                string what = e.Note is string note ? $"not counted: {note}"
                    : $"{e.ChestsCounted} chest{(e.ChestsCounted == 1 ? "" : "s")} ({e.Opened} opened, {e.Broken} broken open)" +
                      (e.AmmoBoxes > 0 ? $" · {e.AmmoBoxes} ammo box{(e.AmmoBoxes == 1 ? "" : "es")} ignored" : "");
                return new MatchRow(when, what);
            }).ToList();
        MatchList.ItemsSource = rows;
        MatchesEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================== title bar / nav

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (HomePage is null || OddsPage is null || SettingsPage is null) return;
        HomePage.Visibility = NavHome.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        OddsPage.Visibility = NavOdds.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================== home actions

    void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Tracker.Running) _app.Tracker.Stop(); else _app.Tracker.Start();
    }

    void Add_Click(object sender, RoutedEventArgs e) => _app.AddChest("manual");
    void Undo_Click(object sender, RoutedEventArgs e) => _app.Undo();
    void TreasureSwitch_Click(object sender, RoutedEventArgs e) => _app.SetTreasure(TreasureSwitch.IsChecked == true);

    // ---- "Already opened some?" number box

    static bool AllDigits(string s) => s.Length > 0 && s.All(char.IsAsciiDigit);

    void Digits_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !AllDigits(e.Text);

    void Digits_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        string text = e.DataObject.GetDataPresent(DataFormats.UnicodeText) ? (string)e.DataObject.GetData(DataFormats.UnicodeText) : "";
        if (!AllDigits(text.Replace(",", "").Trim())) e.CancelCommand();
    }

    void AlreadyOpenedBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        long n = ParseLong(AlreadyOpenedBox.Text);
        AlreadyOpenedButton.IsEnabled = n > 0;
        AlreadyOpenedPlaceholder.Visibility = AlreadyOpenedBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void AlreadyOpenedBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && AlreadyOpenedButton.IsEnabled) AddAlreadyOpened_Click(sender, e);
    }

    void AddAlreadyOpened_Click(object sender, RoutedEventArgs e)
    {
        long n = ParseLong(AlreadyOpenedBox.Text);
        if (n <= 0) return;
        bool treasure = AlreadyOpenedTreasure.IsChecked == true;
        _app.AddAlreadyOpened(n, treasure);
        AlreadyOpenedBox.Text = "";
        AlreadyOpenedTreasure.IsChecked = false;
        AlreadyOpenedResult.Text = $"Added {n:N0} {(treasure ? "Treasure Hours " : "")}chest{(n == 1 ? "" : "s")}. Undo takes them back off.";
        AlreadyOpenedResult.Visibility = Visibility.Visible;
    }

    void OverlaySwitch_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.OverlayEnabled = OverlaySwitch.IsChecked == true;
        _app.SaveSettings();
        if (_app.Settings.OverlayEnabled) ShowOverlay(); else HideOverlay();
    }

    void LockSwitch_Click(object sender, RoutedEventArgs e) => SetOverlayLocked(LockSwitch.IsChecked == true);

    void ShowOverlay()
    {
        if (_overlay is not null) return;
        _overlay = new OverlayWindow(_app);
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.Show();
        OverlaySwitch.IsChecked = true;
    }

    void HideOverlay()
    {
        _overlay?.Close();
        _overlay = null;
        OverlaySwitch.IsChecked = false;
    }

    void SetOverlayLocked(bool locked)
    {
        _app.Settings.OverlayLocked = locked;
        _app.SaveSettings();
        LockSwitch.IsChecked = locked;
        _overlay?.ApplyLock();
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    // ================================================================== odds page

    void Calc_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready) RecalculateOdds();
    }

    void UseMyCount_Click(object sender, RoutedEventArgs e)
    {
        CalcNormal.Text = _app.Store.Normal.ToString(CultureInfo.InvariantCulture);
        CalcTreasure.Text = _app.Store.Treasure.ToString(CultureInfo.InvariantCulture);
        CalcBoost.Text = _app.Settings.Boost.ToString(CultureInfo.InvariantCulture);
    }

    static long ParseLong(string s) =>
        long.TryParse(s.Replace(",", "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Max(0, v) : 0;

    static double ParseDouble(string s, double fallback) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;

    void RecalculateOdds()
    {
        long normal = ParseLong(CalcNormal.Text), treasure = ParseLong(CalcTreasure.Text);
        double boost = ParseDouble(CalcBoost.Text, Odds.DefaultBoost);
        long perMatch = Math.Max(1, ParseLong(CalcPerMatch.Text));
        double chance = Odds.ChanceAtLeastOne(normal, treasure, boost);
        CalcChance.Text = Odds.Percent(chance);
        CalcOneIn.Text = $"{Odds.OneIn(chance)}  ·  {normal + treasure:N0} chests";
        CalcBoostedPer.Text = $"1 in {Odds.BaseOdds / boost:N0} in Treasure Hours";

        double pt = Odds.PerChestBoosted(boost);
        MilestoneList.ItemsSource = new[] { 0.10, 0.25, 0.50, 0.75, 0.90, 0.99 }.Select(target =>
        {
            long n = Odds.ChestsNeeded(target, Odds.PerChest);
            long t = Odds.ChestsNeeded(target, pt);
            long matches = (long)Math.Ceiling(n / (double)perMatch);
            return new MilestoneRow($"{target:P0}", $"{n:N0}", $"{t:N0}", $"~{matches:N0} matches");
        }).ToList();
    }

    // ================================================================== settings page

    void LoadSettingsIntoControls()
    {
        var s = _app.Settings;
        AutoStartSwitch.IsChecked = s.AutoStart;
        BeepSwitch.IsChecked = s.Beep;
        OverlaySwitch.IsChecked = s.OverlayEnabled;
        LockSwitch.IsChecked = s.OverlayLocked;
        var op = new[] { (Op60, 0.6), (Op75, 0.75), (Op92, 0.92), (Op100, 1.0) }
            .OrderBy(o => Math.Abs(o.Item2 - s.OverlayOpacity)).First().Item1;
        op.IsChecked = true;
    }

    void Save() { if (_ready) _app.SaveSettings(); }

    void AutoStartSwitch_Click(object sender, RoutedEventArgs e) { _app.Settings.AutoStart = AutoStartSwitch.IsChecked == true; Save(); }
    void BeepSwitch_Click(object sender, RoutedEventArgs e) { _app.Settings.Beep = BeepSwitch.IsChecked == true; Save(); }

    void Opacity_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton rb) return;
        _app.Settings.OverlayOpacity = double.Parse((string)rb.Tag, CultureInfo.InvariantCulture);
        Save();
        if (_overlay is not null) _overlay.Opacity = _app.Settings.OverlayOpacity;
    }

    void ResetOverlay_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.OverlayLeft = null;
        _app.Settings.OverlayTop = null;
        Save();
        _overlay?.MoveToDefault();
    }

    void OpenReplays_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(ReplayChests.DemosFolder))
            Process.Start(new ProcessStartInfo(ReplayChests.DemosFolder) { UseShellExecute = true });
        else
            MessageBox.Show(this, "Fortnite's replay folder doesn't exist yet. Turn on Record Replays in Fortnite and play a match.",
                            "Chest Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    async void ImportOlder_Click(object sender, RoutedEventArgs e)
    {
        ImportButton.IsEnabled = false;
        ImportResult.Visibility = Visibility.Visible;
        try
        {
            var progress = new Progress<(int Done, int Total)>(p => ImportResult.Text = $"Reading older replays… {p.Done} of {p.Total}");
            var found = await _app.Tracker.ReadOlderReplaysAsync(progress);
            var counting = found.Where(f => f.Result.Counts && f.Result.Record.Chests.Count > 0).ToList();
            int chests = counting.Sum(f => f.Result.Record.Chests.Count);
            if (chests == 0)
            {
                ImportResult.Text = found.Count == 0 ? "There are no older replays to add." : "None of the older replays have chests you opened.";
                return;
            }
            var first = counting.Min(f => f.Result.Record.MatchStartUtc)?.ToLocalTime();
            string msg = $"Found {chests:N0} chests in {counting.Count} Battle Royale matches" +
                         (first is DateTime d ? $" since {d:MMM d}" : "") + ".\n\n" +
                         "Add them to your count? Any chests already in your count during those matches (auto-detected or added " +
                         "with F7) are replaced by the exact replay count, so nothing is counted twice. Chests added in bulk with " +
                         "\"Already opened some?\" are kept, so if that number already includes these matches, choose No.";
            if (MessageBox.Show(this, msg, "Add older matches", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                ImportResult.Text = "Nothing added.";
                return;
            }
            int added = _app.Tracker.ImportOlder(found);
            ImportResult.Text = $"Added {added:N0} chests from {counting.Count} older matches.";
        }
        catch (Exception ex)
        {
            ImportResult.Text = "Couldn't read the older replays: " + ex.Message;
        }
        finally { ImportButton.IsEnabled = true; }
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AppState.DataDir) { UseShellExecute = true });

    void ResetCount_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, $"Reset your chest count ({_app.Store.Total:N0} chests) to zero? This can't be undone.",
                                     "Reset count", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) _app.ResetCount();
    }

    void GitHub_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });
}
