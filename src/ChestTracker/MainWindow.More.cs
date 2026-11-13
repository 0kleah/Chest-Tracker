using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ChestTracker.Core;
using ChestTracker.Native;

namespace ChestTracker;

public sealed record MatchHistoryRow(string Id, string When, string Detail, string Chests);
public sealed record FindRow(string Id, string Icon, string Name, string Detail);
public sealed record SquadRow(string Key, string Name, string Badge, Brush BadgeBack, Brush BadgeFore, string FriendAction,
                              string Detail, string Kinds, string Finds, Visibility FindsVisibility, string Chests);
public sealed record EffRow(string Name, string Matches, string Five, string Rate);

/// <summary>Matches, Finds and Squad pages; efficiency stats; tray, reminders; overlay look and colours.</summary>
public partial class MainWindow
{
    // ================================================================== shared map picture

    BitmapImage? _mapBitmap;
    Task<BitmapImage?>? _mapLoad;

    /// <summary>The map picture at 1000 px, loaded once and shared by the Map and Matches pages.</summary>
    Task<BitmapImage?> MapBitmapAsync()
    {
        if (_mapBitmap is not null) return Task.FromResult<BitmapImage?>(_mapBitmap);
        return _mapLoad ??= LoadMapAsync();
    }

    async Task<BitmapImage?> LoadMapAsync()
    {
        try
        {
            var path = await _app.MapImageAsync();
            if (path is null) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // read it all now so the file isn't kept open
                bmp.DecodePixelWidth = 1000;                   // plenty for the screen, and a quarter of the memory
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                _mapBitmap = bmp;
                return bmp;
            }
            catch (Exception)
            {
                try { File.Delete(path); } catch (IOException) { }
                return null;
            }
        }
        finally { _mapLoad = null; }
    }

    /// <summary>Where a world position is on the 1000 x 1000 map.</summary>
    Point MapPoint(float x, float y)
    {
        var cal = _app.Treasure.Map;
        double k = 1000 / cal.Size;
        return new Point((cal.CenterX + x * cal.Scale) * k, (cal.CenterY + y * cal.Scale) * k);
    }

    // ================================================================== matches

    List<ReplayLedger.Entry> _matchEntries = new();

    void RefreshMatchHistory()
    {
        _matchEntries = _app.Ledger.All()
            .Where(e => !e.FromBefore && e.Note is null && e.MatchStartUtc is not null && !string.IsNullOrEmpty(e.SessionId))
            .OrderByDescending(e => e.MatchStartUtc).ToList();
        string? selected = (MatchHistoryList.SelectedItem as MatchHistoryRow)?.Id;
        var rows = _matchEntries.Select(e =>
        {
            var t = e.MatchStartUtc!.Value.ToLocalTime();
            var bits = new List<string>();
            if (e.Placement is int pl && pl > 0) bits.Add(e.Players is int n && n > 0 ? $"#{pl} of {n}" : $"#{pl}");
            if (e.Eliminations is int el) bits.Add($"{el} elim{(el == 1 ? "" : "s")}");
            if (Mode(e.Playlist) is string mode) bits.Add(mode);
            if (e.Broken > 0) bits.Add($"{e.Broken} broken open");
            if (e.Prizes is { Count: > 0 } pz)
                bits.Add(pz.Any(x => FindNames.IsToken(x.PrizeId)) ? "TOKEN FOUND!" : $"{pz.Count} golden prize{(pz.Count == 1 ? "" : "s")} seen");
            return new MatchHistoryRow(e.SessionId!, t.Date == DateTime.Today ? $"Today {t:h:mm tt}" : t.ToString("ddd d MMM, h:mm tt"),
                                       bits.Count == 0 ? "Battle Royale" : string.Join(" · ", bits), e.ChestsCounted.ToString("N0"));
        }).ToList();
        MatchHistoryList.ItemsSource = rows;
        MatchHistoryEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var again = rows.FirstOrDefault(r => r.Id == selected) ?? rows.FirstOrDefault();
        MatchHistoryList.SelectedItem = again;
        if (again is null) _ = DrawRouteAsync(null);
    }

    static string? Mode(string? playlist)
    {
        var p = (playlist ?? "").ToLowerInvariant();
        if (p.Length == 0) return null;
        string kind = p.Contains("solo") ? "Solo" : p.Contains("duo") ? "Duos" : p.Contains("trio") ? "Trios" : p.Contains("squad") ? "Squads" : "";
        string extra = p.Contains("zerobuild") || p.Contains("nobuild") || p.Contains("_nb") ? "Zero Build " : "";
        return kind.Length > 0 ? extra + kind : null;
    }

    void MatchHistory_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _ = DrawRouteAsync(MatchHistoryList.SelectedItem as MatchHistoryRow);

    async Task DrawRouteAsync(MatchHistoryRow? row)
    {
        RouteCanvas.Children.Clear();
        var entry = row is null ? null : _matchEntries.FirstOrDefault(m => m.SessionId == row.Id);
        MatchPlaced.Text = entry?.Placement is int pl && pl > 0 ? $"#{pl}" : "–";
        MatchElims.Text = entry?.Eliminations?.ToString() ?? "–";
        MatchChests.Text = entry?.ChestsCounted.ToString("N0") ?? "–";
        MatchLength.Text = entry?.DurationSeconds is int d ? $"{d / 60}:{d % 60:00}" : "–";
        if (entry?.Prizes is { Count: > 0 } prizes)
        {
            PrizesText.Text = string.Join("\n", prizes.Select(p =>
            {
                string who = p.Who switch
                {
                    "me" => "You",
                    "teammate" => string.IsNullOrEmpty(p.Player) ? "A teammate" : $"{p.Player} (teammate)",
                    _ => string.IsNullOrEmpty(p.Player) ? "Another player" : p.Player,
                };
                return $"{(FindNames.IsToken(p.PrizeId) ? "TOKEN: " : "")}{who} got {FindNames.PrizeName(p.PrizeId)} at {(int)p.TimeSeconds / 60}:{(int)p.TimeSeconds % 60:00}";
            }));
            PrizesText.Foreground = prizes.Any(p => FindNames.IsToken(p.PrizeId)) ? (Brush)FindResource("Gold") : (Brush)FindResource("Text");
        }
        else
        {
            PrizesText.Text = entry is null ? "" : entry.DetailsVersion < Tracker.DetailsVersion ? "Still reading this match's replay…" : "None seen near you in this match.";
            PrizesText.Foreground = (Brush)FindResource("TextDim");
        }
        if (RouteImage.Source is null)
        {
            RouteMessage.Text = "Loading the map…";
            RouteMessage.Visibility = Visibility.Visible;
            RouteImage.Source = await MapBitmapAsync();
            RouteMessage.Visibility = RouteImage.Source is null ? Visibility.Visible : Visibility.Collapsed;
            if (RouteImage.Source is null) RouteMessage.Text = "Couldn't download the map picture (are you online?).";
        }
        if (entry is null) return;

        var chests = _app.Store.Snapshot().Where(c => c.Match == entry.SessionId && c.Source == "replay").OrderBy(c => c.When).ToList();
        var points = chests.Where(c => c.X is not null).Select(c => (Chest: c, At: MapPoint(c.X!.Value, c.Y!.Value))).ToList();
        if (points.Count == 0)
        {
            RouteMessage.Text = "No chest positions for this match (its replay is gone).";
            RouteMessage.Visibility = Visibility.Visible;
            return;
        }
        if (RouteImage.Source is not null) RouteMessage.Visibility = Visibility.Collapsed;

        var gold = (Brush)FindResource("Gold");
        var line = new Polyline { Stroke = gold, StrokeThickness = 3.5, Opacity = 0.85, StrokeLineJoin = PenLineJoin.Round, StrokeDashArray = new DoubleCollection { 3, 2 } };
        foreach (var p in points) line.Points.Add(p.At);
        RouteCanvas.Children.Add(line);
        var orange = (Brush)FindResource("Orange");
        var sprite = new SolidColorBrush(Color.FromRgb(0xB5, 0x7B, 0xFF));
        int n = 0;
        foreach (var (c, at) in points)
        {
            n++;
            var dot = new Grid { Width = 30, Height = 30, ToolTip = $"#{n} · {c.Kind ?? "Chest"} · {(c.Broken ? "broken open" : "opened")} · {c.When:h:mm:ss tt}" };
            dot.Children.Add(new Ellipse
            {
                Fill = c.Kind?.StartsWith("Sprite", StringComparison.Ordinal) == true ? sprite : c.Broken ? orange : gold,
                Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)), StrokeThickness = 1.5,
            });
            dot.Children.Add(new TextBlock
            {
                Text = n.ToString(CultureInfo.InvariantCulture), FontSize = n >= 10 ? 13 : 15, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x13, 0x00)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            Canvas.SetLeft(dot, at.X - 15);
            Canvas.SetTop(dot, at.Y - 15);
            RouteCanvas.Children.Add(dot);
        }
    }

    // ================================================================== finds

    List<(string Name, string Category, int? Week)> _catalog = new();
    string? _editingFind;

    void RefreshFinds()
    {
        var items = _app.Finds.All();
        FindsList.ItemsSource = items.Select(f => new FindRow(f.Id, IconFor(f.Category), f.Name, FindDetail(f))).ToList();
        FindsEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FindsTotal.Text = items.Count.ToString("N0");
        int gizmos = items.Count(i => i.Category == "Gizmo");
        FindsTotalHint.Text = $"{gizmos} gizmo{(gizmos == 1 ? "" : "s")}";
        FindsCosmetics.Text = items.Count(i => i.Category is not ("Gizmo" or "Other" or "Pirate Shop Access")).ToString("N0");
        var token = items.FirstOrDefault(i => i.Category == "Pirate Shop Access");
        FindsToken.Text = token is null ? "Not yet" : "Found!";
        FindsToken.Foreground = token is null ? (Brush)FindResource("Text") : (Brush)FindResource("Gold");
        FindsTokenHint.Text = token is null ? $"{Odds.Percent(_app.Chance)} chance so far" : token.WhenUtc is DateTime w ? w.ToLocalTime().ToString("ddd d MMM yyyy") : "added by you";

        if (_catalog.Count == 0 || FindPick.Items.Count == 0) FillFindForm();
    }

    static string IconFor(string category) => category switch
    {
        "Pirate Shop Access" => "",
        "Outfit" => "",
        "Pickaxe" => "",
        "Back Bling" => "",
        "Car Decal" => "",
        "Jam Track" => "",
        "Loading Screen" => "",
        "Gizmo" => "",
        _ => "",
    };

    string FindDetail(FoundItem f)
    {
        var bits = new List<string> { f.Source == "replay" ? "Found by the tracker" : "Added by you" };
        if (f.Category != "Other") bits.Add(f.Category);
        if (f.Chest is string chest && f.Source == "replay") bits.Add(f.Broken ? $"{chest}, broken open" : chest);
        if (f.WhenUtc is DateTime w) bits.Add(w.ToLocalTime().ToString("ddd d MMM, h:mm tt"));
        else if (f.Week is int wk) bits.Add($"week {wk}");
        var text = string.Join(" · ", bits);
        return f.Unsure ? text + " · the exact prize isn't in the replay, press Change if this is wrong" : text;
    }

    void FillFindForm()
    {
        _catalog = FindNames.Catalog(_app.Treasure);
        FindPick.Items.Clear();
        foreach (var c in _catalog) FindPick.Items.Add(c.Week is int w ? $"{c.Name} · week {w}" : c.Name);
        FindPick.Items.Add("Something else…");
        FindPick.SelectedIndex = 0;
        FindWhen.Items.Clear();
        FindWhen.Items.Add("I don't remember");
        foreach (var w in _app.Treasure.Weeks)
            FindWhen.Items.Add($"Week {w.Number} ({w.StartUtc.ToLocalTime():d MMM} – {w.EndUtc.ToLocalTime():d MMM})");
        FindWhen.SelectedIndex = 0;
    }

    void FindPick_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool custom = FindPick.SelectedIndex == _catalog.Count;
        FindCustomRow.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        if (FindPick.SelectedIndex >= 0 && FindPick.SelectedIndex < _catalog.Count && _catalog[FindPick.SelectedIndex].Week is int wk)
            FindWhen.SelectedIndex = Math.Min(wk, FindWhen.Items.Count - 1);
    }

    (string Name, string Category)? PickedFind()
    {
        if (FindPick.SelectedIndex < 0) return null;
        if (FindPick.SelectedIndex < _catalog.Count) return (_catalog[FindPick.SelectedIndex].Name, _catalog[FindPick.SelectedIndex].Category);
        var name = FindCustomName.Text.Trim();
        return name.Length == 0 ? null : (name, FindNames.CategoryOf(name));
    }

    void FindAdd_Click(object sender, RoutedEventArgs e)
    {
        if (PickedFind() is not { } pick)
        {
            FindResult.Text = "Type what you found first.";
            FindResult.Visibility = Visibility.Visible;
            return;
        }
        if (_editingFind is string id)
        {
            _app.Finds.Update(id, pick.Name, pick.Category);
            FindResult.Text = $"Changed to {pick.Name}.";
        }
        else
        {
            bool already = pick.Category != "Gizmo" && _app.Finds.All().Any(f => string.Equals(f.Name, pick.Name, StringComparison.OrdinalIgnoreCase));
            int? week = FindWhen.SelectedIndex > 0 ? _app.Treasure.Weeks[FindWhen.SelectedIndex - 1].Number : null;
            _app.Finds.Add(new FoundItem { Name = pick.Name, Category = pick.Category, Source = "manual", Week = week });
            FindResult.Text = already
                ? $"Added {pick.Name}. (It was already in your list; apart from gizmos you can only get each prize once, so remove one if it's a double.)"
                : $"Added {pick.Name}.";
        }
        FindResult.Visibility = Visibility.Visible;
        FindCancel_Click(sender, e);
        RefreshFinds();
    }

    void FindEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string id || _app.Finds.All().FirstOrDefault(f => f.Id == id) is not { } item) return;
        _editingFind = id;
        FindFormTitle.Text = $"Change \"{item.Name}\"";
        FindAddButton.Content = "Save";
        FindCancel.Visibility = Visibility.Visible;
        int i = _catalog.FindIndex(c => string.Equals(c.Name, item.Name, StringComparison.OrdinalIgnoreCase));
        FindPick.SelectedIndex = i >= 0 ? i : _catalog.Count;
        if (i < 0) FindCustomName.Text = item.Name;
        FindResult.Visibility = Visibility.Collapsed;
        FindFormTitle.BringIntoView();
    }

    void FindCancel_Click(object sender, RoutedEventArgs e)
    {
        _editingFind = null;
        FindFormTitle.Text = "Add something the tracker didn't pick up";
        FindAddButton.Content = "Add";
        FindCancel.Visibility = Visibility.Collapsed;
        FindCustomName.Text = "";
        if (FindPick.Items.Count > 0) FindPick.SelectedIndex = 0;
    }

    void FindRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string id || _app.Finds.All().FirstOrDefault(f => f.Id == id) is not { } item) return;
        if (MessageBox.Show(this, $"Remove \"{item.Name}\" from your finds?", "Chest Tracker", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _app.Finds.Remove(id);
        RefreshFinds();
    }

    // ================================================================== squad

    void RefreshSquad()
    {
        SquadTrackSwitch.IsChecked = _app.Settings.SquadTracking;
        SquadTotalSwitch.IsChecked = _app.Settings.SquadInTotal;
        SquadTotalSwitch.IsEnabled = _app.Settings.SquadTracking;
        var people = _app.Squad.All();
        var gold = (Brush)FindResource("Gold");
        var goldSoft = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xCD, 0x50));
        var dim = (Brush)FindResource("TextDim");
        var dimSoft = (Brush)FindResource("Surface2");
        SquadList.ItemsSource = people.Select(p => new SquadRow(
            p.Name,
            p.Bot ? $"{p.Name} (bot)" : p.Name,
            p.Bot ? "BOT" : p.IsFriend ? "FRIEND" : "RANDOM",
            p.IsFriend && !p.Bot ? goldSoft : dimSoft,
            p.IsFriend && !p.Bot ? gold : dim,
            p.Bot ? "" : p.IsFriend ? "Not a friend?" : "Mark as friend",
            $"{p.Matches} match{(p.Matches == 1 ? "" : "es")} together"
                + (p.PartyMatches > 0 ? $" ({p.PartyMatches} in your party)" : p.Bot ? "" : " (random fill)")
                + $" · last {p.LastSeenUtc?.ToLocalTime().ToString("d MMM") ?? "?"}" + (p.Broken > 0 ? $" · {p.Broken} broken open" : ""),
            p.Kinds.Count == 0 ? "No chests seen near you" : string.Join(" · ", p.Kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Value} {k.Key}{(k.Value == 1 ? "" : "s")}")),
            p.Finds.Count == 0 ? "" : "Found: " + string.Join("; ", p.Finds),
            p.Finds.Count == 0 ? Visibility.Collapsed : Visibility.Visible,
            p.Chests.ToString("N0"))).ToList();
        SquadEmpty.Visibility = people.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SquadEmpty.Text = _app.Settings.SquadTracking ? "No teammates yet. Play a match with friends (or wait while saved replays are read)." : "Turn on squad tracking and play with friends. They'll show up here.";
    }

    void SquadTrackSwitch_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.SquadTracking = SquadTrackSwitch.IsChecked == true;
        Save();
        if (_app.Settings.SquadTracking)
        {
            _app.Tracker.RequestEnrich();
            SquadStatus.Text = "On. Teammates from matches Fortnite still has replays of are being added (a few seconds per match).";
        }
        else SquadStatus.Text = "Off. Your list is kept; nothing new is added.";
        SquadStatus.Visibility = Visibility.Visible;
        RefreshSquad();
        SquadChanged();
    }

    void SquadFriend_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string name) return;
        var p = _app.Squad.All().FirstOrDefault(x => x.Name == name);
        if (p is null) return;
        _app.Squad.SetFriend(name, !p.IsFriend);
        RefreshSquad();
    }

    void SquadTotalSwitch_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.SquadInTotal = SquadTotalSwitch.IsChecked == true;
        Save();
        SquadChanged();
    }

    /// <summary>Squad settings or numbers changed: update the squad line on Home and the overlay.</summary>
    void SquadChanged()
    {
        RefreshCounts();
        _overlay?.ApplyLook();
        _overlay?.UpdateLines();
    }

    void SquadClear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Clear your squad list? Matches whose replays Fortnite still has will be added again while squad tracking is on.",
                            "Chest Tracker", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _app.Squad.Clear();
        foreach (var en in _app.Ledger.All()) en.SquadTried = false;
        _app.Ledger.Save();
        if (_app.Settings.SquadTracking) _app.Tracker.RequestEnrich();
        RefreshSquad();
        SquadChanged();
    }

    void SquadTabSwitch_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.SquadTab = SquadTabSwitch.IsChecked == true;
        Save();
        NavSquad.Visibility = _app.Settings.SquadTab ? Visibility.Visible : Visibility.Collapsed;
        if (!_app.Settings.SquadTab && NavSquad.IsChecked == true) NavSettings.IsChecked = true;
        SquadChanged();
    }

    // ================================================================== efficiency (Stats page)

    void RefreshEfficiency(ChestStats st)
    {
        var starts = _app.Ledger.All().Where(e => e.SessionId is not null && e.MatchStartUtc is not null)
                         .GroupBy(e => e.SessionId!).ToDictionary(g => g.Key, g => g.First().MatchStartUtc!.Value.ToLocalTime());
        var all = ChestStats.EfficiencyOf("All", st.Matches, starts);
        EffFirst.Text = all.Matches == 0 || all.FirstChestSeconds <= 0 ? "–" : $"{(int)all.FirstChestSeconds / 60}:{(int)all.FirstChestSeconds % 60:00}";
        EffFive.Text = all.Matches == 0 ? "–" : all.FirstFive.ToString("0.0", CultureInfo.CurrentCulture);
        EffRate.Text = all.Matches == 0 ? "–" : all.PerMinute.ToString("0.00", CultureInfo.CurrentCulture);
        var spots = ChestStats.EfficiencyBySpot(st.Matches, _app.Places, starts).Take(10).ToList();
        EffList.ItemsSource = spots.Select(s => new EffRow(s.Name, s.Matches.ToString(), s.FirstFive.ToString("0.0", CultureInfo.CurrentCulture),
                                                           s.PerMinute.ToString("0.00", CultureInfo.CurrentCulture))).ToList();
        EffEmpty.Visibility = spots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================== tray, start with Windows, reminders

    TrayIcon? _tray;
    bool _exiting;
    bool _startHidden;
    readonly HashSet<DateTime> _reminded = new();
    DispatcherTimer? _reminderTimer;

    /// <summary>Started by Windows (--tray): stay hidden in the tray.</summary>
    public void StartHidden()
    {
        _startHidden = true;
        Opacity = 0;
        ShowActivated = false;
    }

    void SetUpBackground()
    {
        UpdateTray();
        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _reminderTimer.Tick += (_, _) => CheckReminders();
        _reminderTimer.Start();
        if (_startHidden)
        {
            Hide();
            Opacity = 1;
            ShowActivated = true;
            Memory.Trim();
        }
    }

    /// <summary>The tray icon is there while tray mode or reminders are on (reminders show from it).</summary>
    void UpdateTray()
    {
        bool want = _app.Settings.TrayMode || _app.Settings.Reminders || _startHidden;
        if (want && _tray is null)
        {
            _tray = new TrayIcon("Chest Tracker");
            _tray.Clicked += () => Dispatcher.BeginInvoke(BringToFront);
            _tray.MenuRequested += () => Dispatcher.BeginInvoke(ShowTrayMenu);
        }
        else if (!want && _tray is not null)
        {
            _tray.Dispose();
            _tray = null;
        }
        _tray?.SetTooltip($"Chest Tracker · {_app.Store.Total:N0} chests · {Odds.Percent(_app.Chance)} token chance");
    }

    void ShowTrayMenu()
    {
        var itemStyle = (Style)FindResource("DarkMenuItem");
        MenuItem Item(string header, Action act)
        {
            var mi = new MenuItem { Header = header, Style = itemStyle };
            mi.Click += (_, _) => act();
            return mi;
        }
        var menu = new ContextMenu { Style = (Style)FindResource("DarkContextMenu"), Placement = PlacementMode.MousePoint };
        menu.Items.Add(Item("Open Chest Tracker", BringToFront));
        menu.Items.Add(Item(_app.TreasureMode ? "Turn off Treasure Hours" : "Turn on Treasure Hours", _app.ToggleTreasure));
        menu.Items.Add(new Separator { Style = (Style)FindResource("MenuSeparator") });
        menu.Items.Add(Item("Quit", QuitApp));
        _tray?.PrepareMenu();
        menu.IsOpen = true;
    }

    public void QuitApp()
    {
        _exiting = true;
        Application.Current.Shutdown();
    }

    void OnClosingWindow(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting || !_app.Settings.TrayMode) return;
        e.Cancel = true;   // keep counting in the tray
        Hide();
        ReleaseMap();
        Memory.Trim();
        UpdateTray();
    }

    void CheckReminders()
    {
        _tray?.SetTooltip($"Chest Tracker · {_app.Store.Total:N0} chests · {Odds.Percent(_app.Chance)} token chance");
        if (!_app.Settings.Reminders || _tray is null) return;
        var now = DateTime.UtcNow;
        if (_app.Treasure.NextHour(now) is not { } h) return;
        var start = h.StartUtc.ToLocalTime();
        var end = h.EndUtc.ToLocalTime();
        if (now < h.StartUtc && h.StartUtc - now <= TimeSpan.FromMinutes(10) && _reminded.Add(h.StartUtc.AddTicks(1)))
            _tray.Notify("Treasure Hour in 10 minutes", $"From {start:h:mm} to {end:h:mm tt}: {h.Pirate:0.##}x Pirate Shop Access odds. Get into a match!");
        else if (now >= h.StartUtc && now - h.StartUtc < TimeSpan.FromMinutes(5) && _reminded.Add(h.StartUtc))
            _tray.Notify("Treasure Hour has started", $"{h.Pirate:0.##}x Pirate Shop Access odds until {end:h:mm tt}.");
    }

    void TraySwitch_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.TrayMode = TraySwitch.IsChecked == true;
        Save();
        UpdateTray();
    }

    void RemindersSwitch_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.Reminders = RemindersSwitch.IsChecked == true;
        Save();
        UpdateTray();
        if (_app.Settings.Reminders) _tray?.Notify("Treasure Hour reminders are on", "You'll get a notification like this 10 minutes before each Treasure Hour.");
    }

    void StartupSwitch_Click(object sender, RoutedEventArgs e)
    {
        bool on = StartupSwitch.IsChecked == true;
        if (!WindowsStartup.Set(on))
        {
            StartupSwitch.IsChecked = !on;
            MessageBox.Show(this, "Windows didn't allow that change.", "Chest Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _app.Settings.StartWithWindows = on;
        if (on && !_app.Settings.TrayMode)
        {
            _app.Settings.TrayMode = true;   // starting with Windows only makes sense in the tray
            TraySwitch.IsChecked = true;
            UpdateTray();
        }
        Save();
    }

    // ================================================================== overlay look

    void LoadLookIntoControls()
    {
        var s = _app.Settings;
        TraySwitch.IsChecked = s.TrayMode;
        StartupSwitch.IsChecked = WindowsStartup.IsOn();
        RemindersSwitch.IsChecked = s.Reminders;
        SquadTabSwitch.IsChecked = s.SquadTab;
        NavSquad.Visibility = s.SquadTab ? Visibility.Visible : Visibility.Collapsed;

        OvCompact.IsChecked = s.OverlayCompact;
        OvCount.IsChecked = s.OverlayShowCount;
        OvChance.IsChecked = s.OverlayShowChance;
        OvOneIn.IsChecked = s.OverlayShowOneIn;
        OvSession.IsChecked = s.OverlayShowSession;
        OvToday.IsChecked = s.OverlayShowToday;
        OvNextHour.IsChecked = s.OverlayShowNextHour;
        OvStatus.IsChecked = s.OverlayShowStatus;
        OvTreasure.IsChecked = s.OverlayShowTreasure;
        OvHint.IsChecked = s.OverlayShowHint;
        var size = SizeSegments.Children.OfType<RadioButton>()
            .OrderBy(r => Math.Abs(double.Parse((string)r.Tag, CultureInfo.InvariantCulture) - s.OverlayScale)).First();
        size.IsChecked = true;
        BuildColourControls();
    }

    void OverlaySize_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton rb) return;
        _app.Settings.OverlayScale = double.Parse((string)rb.Tag, CultureInfo.InvariantCulture);
        Save();
        _overlay?.ApplyLook();
    }

    void OverlayLook_Click(object sender, RoutedEventArgs e)
    {
        var s = _app.Settings;
        s.OverlayCompact = OvCompact.IsChecked == true;
        s.OverlayShowCount = OvCount.IsChecked == true;
        s.OverlayShowChance = OvChance.IsChecked == true;
        s.OverlayShowOneIn = OvOneIn.IsChecked == true;
        s.OverlayShowSession = OvSession.IsChecked == true;
        s.OverlayShowToday = OvToday.IsChecked == true;
        s.OverlayShowNextHour = OvNextHour.IsChecked == true;
        s.OverlayShowStatus = OvStatus.IsChecked == true;
        s.OverlayShowTreasure = OvTreasure.IsChecked == true;
        s.OverlayShowHint = OvHint.IsChecked == true;
        Save();
        _overlay?.ApplyLook();
        _overlay?.UpdateLines();
    }

    // ================================================================== colours

    readonly List<(Func<string> Get, TextBox Box, Border Swatch)> _colourBoxes = new();

    void BuildColourControls()
    {
        AccentSwatches.Children.Clear();
        foreach (var (name, hex) in ThemeManager.AccentPresets)
            AccentSwatches.Children.Add(Swatch(name, hex, null, () => { _app.Settings.AccentColor = hex; ApplyColours(); }));
        BackgroundSwatches.Children.Clear();
        foreach (var (name, bg, card, text) in ThemeManager.BackgroundPresets)
            BackgroundSwatches.Children.Add(Swatch(name, bg, card, () =>
            {
                _app.Settings.BackgroundColor = bg;
                _app.Settings.CardColor = card;
                _app.Settings.TextColor = text;
                ApplyColours();
            }));

        ColourInputs.Children.Clear();
        _colourBoxes.Clear();
        var s = _app.Settings;
        AddColourInput("Accent", () => s.AccentColor, v => s.AccentColor = v);
        AddColourInput("Background", () => s.BackgroundColor, v => s.BackgroundColor = v);
        AddColourInput("Cards", () => s.CardColor, v => s.CardColor = v);
        AddColourInput("Text", () => s.TextColor, v => s.TextColor = v);
        AddColourInput("Overlay background", () => s.OverlayColor, v => s.OverlayColor = v);
        AddColourInput("Overlay text", () => s.OverlayTextColor, v => s.OverlayTextColor = v);
    }

    Button Swatch(string name, string hex, string? second, Action pick)
    {
        var face = new Grid { Width = 26, Height = 26 };
        face.Children.Add(new Ellipse { Fill = new SolidColorBrush(ThemeManager.Parse(hex, "#000000")), Stroke = (Brush)FindResource("Line"), StrokeThickness = 1.5 });
        if (second is not null)
            face.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(ThemeManager.Parse(second, "#000000")) });
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(face);
        content.Children.Add(new TextBlock { Text = name, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Style = (Style)FindResource("SecondaryButton"), Content = content, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(8, 5, 12, 5), ToolTip = hex };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.Click += (_, _) => pick();
        return b;
    }

    void AddColourInput(string label, Func<string> get, Action<string> set)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 10) };
        panel.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 0, 0, 5) });
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var swatch = new Border { Width = 34, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 0), BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(1) };
        var box = new TextBox { Style = (Style)FindResource("Input"), Text = get(), MaxLength = 9, FontSize = 13 };
        Grid.SetColumn(box, 1);
        row.Children.Add(swatch);
        row.Children.Add(box);
        panel.Children.Add(row);
        void Commit()
        {
            var v = box.Text.Trim();
            if (!v.StartsWith('#')) v = "#" + v;
            if (!ThemeManager.IsValid(v))
            {
                ColourResult.Text = $"\"{box.Text}\" isn't a colour code. Use #RRGGBB, for example #FFCD50.";
                box.Text = get();
                return;
            }
            if (string.Equals(v, get(), StringComparison.OrdinalIgnoreCase)) return;
            set(ThemeManager.Hex(ThemeManager.Parse(v, "#000000")));
            ApplyColours();
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        box.LostKeyboardFocus += (_, _) => Commit();
        _colourBoxes.Add((get, box, swatch));
        ColourInputs.Children.Add(panel);
        swatch.Background = new SolidColorBrush(ThemeManager.Parse(get(), "#000000"));
    }

    void ApplyColours()
    {
        Save();
        ThemeManager.Apply(_app.Settings);
        _overlay?.ApplyLook();
        foreach (var (get, box, swatch) in _colourBoxes)
        {
            box.Text = get();
            swatch.Background = new SolidColorBrush(ThemeManager.Parse(get(), "#000000"));
        }
        ColourResult.Text = "Saved. Most colours change straight away; anything that didn't changes the next time you open Chest Tracker.";
        RefreshCounts();
        RefreshStatus();
    }

    void ColoursReset_Click(object sender, RoutedEventArgs e)
    {
        var s = _app.Settings;
        s.AccentColor = Palette.DefaultAccent;
        s.BackgroundColor = Palette.DefaultBackground;
        s.CardColor = Palette.DefaultCard;
        s.TextColor = Palette.DefaultText;
        s.OverlayColor = Palette.DefaultOverlay;
        s.OverlayTextColor = Palette.DefaultText;
        ApplyColours();
    }
}
