using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ChestTracker.Core;

namespace ChestTracker;

public sealed record RouteRow(string Id, string When, string Detail, string Chests);

/// <summary>The Routes page, and drawing a route over the Fortnite map (see <see cref="MapRouteOverlay"/>).</summary>
public partial class MainWindow
{
    MapRouteOverlay? _mapRoutes;
    List<RouteSummary> _routes = new();
    bool _waitingForMapKey;

    void SetUpMapRoutes()
    {
        RoutesList.ItemContainerStyle = MatchHistoryList.ItemContainerStyle;
        // In a short window the list keeps at least 230 px; the card under it scrolls inside itself instead.
        RoutesPage.SizeChanged += (_, _) => MapOverlayCard.MaxHeight = Math.Max(150, RoutesPage.ActualHeight - 80 - 230 - 12);
        _mapRoutes = new MapRouteOverlay(_app, Dispatcher);
        _app.MapKeyPressed += () => _mapRoutes.Toggle();
        _app.EscapePressed += () => { if (_mapRoutes.Active) _mapRoutes.Stop(); };
        // get the map's landmarks ready in the background (only slow the very first time; then they're saved)
        if (_app.Settings.MapOverlay)
        {
            var later = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            later.Tick += (_, _) => { later.Stop(); _ = _mapRoutes.PrepareAsync(); };
            later.Start();
        }
    }

    void RefreshRoutes()
    {
        var all = RouteBook.All(_app.Store.Snapshot(), _app.Treasure.Map.FromUtc.ToLocalTime());
        _routes = RoutesWorst.IsChecked == true ? all.OrderBy(r => r.Chests).ThenByDescending(r => r.Start).ToList()
                : RoutesLatest.IsChecked == true ? all.OrderByDescending(r => r.Start).ToList()
                : all.OrderByDescending(r => r.Chests).ThenByDescending(r => r.Start).ToList();
        var ledger = _app.Ledger.All().Where(e => e.SessionId is not null).GroupBy(e => e.SessionId!).ToDictionary(g => g.Key, g => g.First());
        string? selected = (RoutesList.SelectedItem as RouteRow)?.Id;
        var rows = _routes.Select((r, i) =>
        {
            var bits = new List<string>();
            if (i == 0 && RoutesLatest.IsChecked != true) bits.Add(RoutesWorst.IsChecked == true ? "Worst" : "Best");
            if (Place(r) is string place) bits.Add(place);
            if (ledger.TryGetValue(r.Match, out var e))
            {
                if (e.Placement is int pl && pl > 0) bits.Add($"#{pl}");
                if (Mode(e.Playlist) is string mode) bits.Add(mode);
            }
            var t = r.Start;
            return new RouteRow(r.Match, t.Date == DateTime.Today ? $"Today {t:t}" : $"{t:ddd d MMM}, {t:t}",
                                string.Join(" · ", bits), r.Chests.ToString("N0"));
        }).ToList();
        RoutesList.ItemsSource = rows;
        RoutesEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RoutesList.SelectedItem = rows.FirstOrDefault(r => r.Id == selected) ?? rows.FirstOrDefault();
        // Always draw (see RefreshMatchHistory): coming back to the page must show the map again.
        _ = DrawRoutesMapAsync(_routes.FirstOrDefault(r => r.Match == (RoutesList.SelectedItem as RouteRow)?.Id));
        RefreshMapOverlayCard();
    }

    string? Place(RouteSummary r) => ChestStats.Nearest(_app.Places, r.Landing.X, r.Landing.Y)?.Name;

    void RoutesSort_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || RoutesPage.Visibility != Visibility.Visible) return;
        RoutesList.SelectedItem = null;
        RefreshRoutes();
    }

    void RoutesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _ = DrawRoutesMapAsync(_routes.FirstOrDefault(r => r.Match == (RoutesList.SelectedItem as RouteRow)?.Id));

    async Task DrawRoutesMapAsync(RouteSummary? route)
    {
        RoutesCanvas.Children.Clear();
        var entry = route is null ? null : _app.Ledger.All().FirstOrDefault(m => m.SessionId == route.Match && m.Note is null);
        RouteChests.Text = route?.Chests.ToString("N0") ?? "–";
        RouteLanded.Text = route is null ? "–" : Place(route) ?? "?";
        RoutePlaced.Text = entry?.Placement is int pl && pl > 0 ? $"#{pl}" : "–";
        RouteElims.Text = entry?.Eliminations?.ToString() ?? "–";
        RefreshMapOverlayCard();
        if (RoutesImage.Source is null)
        {
            RoutesMessage.Text = "Loading the map…";
            RoutesMessage.Visibility = Visibility.Visible;
            RoutesImage.Source = await MapBitmapAsync();
            if (RoutesPage.Visibility != Visibility.Visible) { RoutesImage.Source = null; return; }
            RoutesMessage.Text = "Couldn't download the map picture (are you online?).";
            RoutesMessage.Visibility = RoutesImage.Source is null ? Visibility.Visible : Visibility.Collapsed;
        }
        if (route is null) return;
        DrawRouteOn(RoutesCanvas, route.Points.Select(c => (c, MapPoint(c.X!.Value, c.Y!.Value))).ToList());
    }

    // ------------------------------------------------------------------ route on the Fortnite map

    void RefreshMapOverlayCard()
    {
        var s = _app.Settings;
        MapOverlaySwitch.IsChecked = s.MapOverlay;
        if (!_waitingForMapKey) MapKeyText.Text = $"Map key: {KeyName(s.MapKey)}";
        if (_padTimer is null) MapPadText.Text = $"Controller map button: {Native.Gamepad.Name(s.MapPadButton)}";
        MapPadOff.Visibility = s.MapPadButton == 0 || _padTimer is not null ? Visibility.Collapsed : Visibility.Visible;
        var shown = _mapRoutes?.ChosenRoute();
        bool picked = shown is not null && s.MapOverlayMatch == shown.Match;
        MapRouteText.Text = shown is null
            ? "Play a match first: your route needs chest positions from a replay."
            : $"Shows {(picked ? "the route you picked" : "your best route")}: {shown.Chests} chests on {shown.Start:ddd d MMM}{(Place(shown) is string p ? $", landed at {p}" : "")}.";
        var selected = (RoutesList.SelectedItem as RouteRow)?.Id;
        MapUseThis.IsEnabled = selected is not null && selected != s.MapOverlayMatch;
        MapUseBest.Visibility = s.MapOverlayMatch is null ? Visibility.Collapsed : Visibility.Visible;
    }

    void MapOverlaySwitch_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.MapOverlay = MapOverlaySwitch.IsChecked == true;
        Save();
        if (_app.Settings.MapOverlay) _ = _mapRoutes?.PrepareAsync();
        else _mapRoutes?.Stop();
    }

    void MapUseThis_Click(object sender, RoutedEventArgs e)
    {
        if (RoutesList.SelectedItem is not RouteRow row) return;
        _app.Settings.MapOverlayMatch = row.Id;
        Save();
        RefreshMapOverlayCard();
    }

    void MapUseBest_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.MapOverlayMatch = null;
        Save();
        RefreshMapOverlayCard();
    }

    System.Windows.Threading.DispatcherTimer? _padTimer;

    /// <summary>Waits up to 10 seconds for a button on an Xbox-style controller and uses it as the map button.</summary>
    void MapPadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_padTimer is not null) { StopWaitingForPad(); return; }
        Native.Gamepad.Listening = true;
        MapPadButton.Content = "Cancel";
        MapPadText.Text = "Press your Fortnite map button on your controller…";
        var started = DateTime.UtcNow;
        bool released = false;
        _padTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _padTimer.Tick += (_, _) =>
        {
            int held = Native.Gamepad.LastHeld;
            if (DateTime.UtcNow - started > TimeSpan.FromSeconds(10))
            {
                StopWaitingForPad();
                if (!Native.Gamepad.AnyConnected) MapPadText.Text = "No controller found. Xbox-style controllers work (PlayStation ones through Steam).";
                return;
            }
            if (held == 0) { released = true; return; }   // wait until nothing is held, then take the first button pressed
            if (!released) return;
            int bit = held & -held;
            _app.Settings.MapPadButton = bit;
            Save();
            StopWaitingForPad();
        };
        _padTimer.Start();
    }

    void StopWaitingForPad()
    {
        _padTimer?.Stop();
        _padTimer = null;
        Native.Gamepad.Listening = false;
        MapPadButton.Content = "Change";
        RefreshMapOverlayCard();
    }

    void MapPadOff_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.MapPadButton = 0;
        Save();
        RefreshMapOverlayCard();
    }

    void MapKeyButton_Click(object sender, RoutedEventArgs e)
    {
        _waitingForMapKey = true;
        MapKeyText.Text = "Press your Fortnite map key…";
        MapKeyButton.Content = "Cancel";
        MapKeyButton.Focus();
        if (MapKeyButton.Tag is "waiting") { StopWaitingForKey(); return; }
        MapKeyButton.Tag = "waiting";
    }

    void MapKeyButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_waitingForMapKey) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        if (key == Key.Escape) { StopWaitingForKey(); return; }
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (Hotkeys.IsOwn(_app.Settings, vk))   // Chest Tracker's own keys (F6 to F9 unless changed)
        {
            MapKeyText.Text = $"{Hotkeys.Name(vk)} is one of Chest Tracker's hotkeys. Pick another key.";
            return;
        }
        if (vk is > 0 and < 256)
        {
            _app.Settings.MapKey = vk;
            Save();
        }
        StopWaitingForKey();
    }

    void StopWaitingForKey()
    {
        _waitingForMapKey = false;
        MapKeyButton.Tag = null;
        MapKeyButton.Content = "Change";
        MapKeyText.Text = $"Map key: {KeyName(_app.Settings.MapKey)}";
    }

    static string KeyName(int vk) => Hotkeys.Name(vk);

    async void MapCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_mapRoutes is null) return;
        MapCheck.IsEnabled = false;
        MapCheckText.Visibility = Visibility.Visible;
        MapCheckText.Text = "Getting ready…";
        try
        {
            var progress = new Progress<int>(left => MapCheckText.Text = $"Switch to Fortnite and open your map now… ({left}s)");
            MapCheckText.Text = await _mapRoutes.CheckAsync(20, progress);
        }
        catch (Exception ex) { MapCheckText.Text = "Couldn't check: " + ex.Message; }
        finally { MapCheck.IsEnabled = true; }
    }
}
