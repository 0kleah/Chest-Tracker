using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ChestTracker.Core;

namespace ChestTracker;

/// <summary>The Stats, Map and Treasure pages.</summary>
public partial class MainWindow
{
    // ================================================================== stats

    void RefreshStats()
    {
        var st = ChestStats.From(_app.Store.Snapshot(), DateTime.Now);
        StatMatches.Text = st.Matches.Count.ToString("N0");
        StatMatchesHint.Text = st.Matches.Count == 0 ? "none yet" : $"since {st.Matches[0].Start:d MMM}";
        StatPerMatch.Text = st.PerMatch.ToString("0.0", CultureInfo.CurrentCulture);
        StatBest.Text = st.Best?.Chests.ToString("N0") ?? "0";
        StatBestHint.Text = st.Best is { } b ? $"{b.Start:ddd d MMM, h:mm tt}" : "";
        StatWeek.Text = st.ThisWeek.ToString("N0");
        StatToday.Text = $"{st.Today:N0} today";

        long max = Math.Max(1, st.Days.Max(d => d.Count));
        var gold = (Brush)FindResource("Gold");
        var dim = (Brush)FindResource("Surface3");
        DayBars.ItemsSource = st.Days.Select(d => new DayBar(
            d.Count > 0 ? d.Count.ToString("N0") : "",
            d.Count == 0 ? 3 : 4 + 100.0 * d.Count / max,
            d.Day == DateTime.Today ? gold : d.Count > 0 ? (Brush)FindResource("GoldDeep") : dim,
            d.Day == DateTime.Today ? "Today" : d.Day.ToString("ddd", CultureInfo.CurrentCulture)[..2],
            $"{d.Day:dddd d MMM}: {d.Count:N0} chest{(d.Count == 1 ? "" : "s")}")).ToList();

        long kindTotal = Math.Max(1, st.ByKind.Sum(k => k.Count));
        KindList.ItemsSource = st.ByKind.Select(k => new KindRow(k.Kind, k.Count / (double)kindTotal, k.Count.ToString("N0"))).ToList();
        KindsEmpty.Visibility = st.ByKind.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var sources = new List<SourceRow>
        {
            new("Opened (from replays)", st.Opened.ToString("N0")),
            new("Broken open (from replays)", st.Broken.ToString("N0")),
        };
        if (st.ByHand > 0) sources.Add(new("Added by hand (F7)", st.ByHand.ToString("N0")));
        if (st.Bulk > 0) sources.Add(new("Previous count you added", st.Bulk.ToString("N0")));
        long other = st.Total - st.Opened - st.Broken - st.ByHand - st.Bulk;
        if (other > 0) sources.Add(new("Old screen method", other.ToString("N0")));
        sources.Add(new("In Treasure Hours", st.TreasureHours.ToString("N0")));
        sources.Add(new("Total", st.Total.ToString("N0")));
        SourceList.ItemsSource = sources;
    }

    // ================================================================== map

    bool _mapLoading;

    void MapFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (_ready && MapPage.Visibility == Visibility.Visible) _ = RefreshMapAsync();
    }

    /// <summary>Drops the map picture while the page isn't shown (it's the biggest thing in memory).</summary>
    void ReleaseMap()
    {
        if (MapImage.Source is null && MapDots.Children.Count == 0) return;
        MapImage.Source = null;
        MapDots.Children.Clear();
    }

    async Task RefreshMapAsync()
    {
        var cal = _app.Treasure.Map;
        if (MapImage.Source is null && !_mapLoading)
        {
            _mapLoading = true;
            MapMessage.Text = "Loading the map…";
            MapMessage.Visibility = Visibility.Visible;
            var path = await _app.MapImageAsync();
            _mapLoading = false;
            if (MapPage.Visibility != Visibility.Visible) return;
            if (path is null)
            {
                MapMessage.Text = "Couldn't download the map picture (are you online?). Your chests are still drawn where they were.";
            }
            else
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;   // read it all now so the file isn't kept open
                    bmp.DecodePixelWidth = 1000;                   // 1000 px is plenty, and a quarter of the memory
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    bmp.Freeze();
                    MapImage.Source = bmp;
                    MapMessage.Visibility = Visibility.Collapsed;
                }
                catch (Exception)
                {
                    try { File.Delete(path); } catch (IOException) { }
                    MapMessage.Text = "The map picture couldn't be opened. It will be downloaded again next time.";
                }
            }
        }

        DateTime from = MapToday.IsChecked == true ? DateTime.Today
                      : MapWeek.IsChecked == true ? DateTime.Now.AddDays(-7)
                      : cal.FromUtc.ToLocalTime();
        var chests = _app.Store.Snapshot()
            .Where(e => e.X is not null && e.Y is not null && e.When is DateTime t && t >= from)
            .ToList();

        MapDots.Children.Clear();
        var gold = (Brush)FindResource("Gold");
        var orange = (Brush)FindResource("Orange");
        var sprite = new SolidColorBrush(Color.FromRgb(0xB5, 0x7B, 0xFF));
        sprite.Freeze();
        var edge = new SolidColorBrush(Color.FromArgb(0xAA, 0, 0, 0));
        edge.Freeze();
        double k = 1000 / cal.Size;
        foreach (var e in chests)
        {
            double x = (cal.CenterX + e.X!.Value * cal.Scale) * k;
            double y = (cal.CenterY + e.Y!.Value * cal.Scale) * k;
            if (x < 0 || y < 0 || x > 1000 || y > 1000) continue;
            var dot = new Ellipse
            {
                Width = 11, Height = 11, StrokeThickness = 1.2, Stroke = edge, Opacity = 0.9,
                Fill = e.Kind?.StartsWith("Sprite", StringComparison.Ordinal) == true ? sprite : e.Broken ? orange : gold,
                ToolTip = $"{e.Kind ?? "Chest"} · {(e.Broken ? "broken open" : "opened")} · {e.When:ddd d MMM, h:mm tt}",
            };
            Canvas.SetLeft(dot, x - 5.5);
            Canvas.SetTop(dot, y - 5.5);
            MapDots.Children.Add(dot);
        }
        MapCountText.Text = chests.Count == 0
            ? "No chests with a position yet. They're saved from now on, and filled in from replays Fortnite still has."
            : $"{chests.Count:N0} chest{(chests.Count == 1 ? "" : "s")} on the map.";
        MapSubtitle.Text = MapToday.IsChecked == true ? "Chests you opened today, from your match replays."
                         : MapWeek.IsChecked == true ? "Chests you opened in the last 7 days, from your match replays."
                         : "Every chest you opened this season, from your match replays.";

        var stats = ChestStats.From(_app.Store.Snapshot(), DateTime.Now);
        var spots = ChestStats.LandingSpots(stats.Matches.Where(m => m.Start >= from), _app.Places).Take(8).ToList();
        SpotList.ItemsSource = spots.Select(s => new SpotRow(s.Name,
            $"{s.Matches} match{(s.Matches == 1 ? "" : "es")} · best {s.Best}", s.AvgChests.ToString("0.0", CultureInfo.CurrentCulture))).ToList();
        SpotsEmpty.Visibility = spots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SpotsEmpty.Text = _app.Places.Count == 0 ? "Place names are downloaded when you're online." : "Play a few matches to see your best spots.";
    }

    // ================================================================== treasure (info from fortnite.bot)

    void RefreshTreasure()
    {
        var info = _app.Treasure;
        var now = DateTime.UtcNow;
        TreasureSource.Text = $"Treasure Hours, odds and prizes for the Treasure Hunters event. Info from fortnite.bot (updated {info.Updated}).";
        EpicNote.Text = info.Note ?? "";
        RefreshCountdown();

        PrizeList.ItemsSource = _app.PrizeChances(now).Select(p => new PrizeRow(
            p.Prize.Name,
            p.Prize.Weekly ? $"{p.Chests:N0} chests this week" : $"{p.Chests:N0} chests this Treasure Hunt",
            $"1 in {p.Prize.OneIn:N0}",
            Odds.Percent(p.Chance))).ToList();

        var gold = (Brush)FindResource("Gold");
        var orange = (Brush)FindResource("Orange");
        var dim = (Brush)FindResource("TextFaint");
        var goldSoft = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xCD, 0x50));
        var greenSoft = new SolidColorBrush(Color.FromArgb(0x26, 0x3D, 0xDC, 0x84));
        var none = Brushes.Transparent;
        var next = info.NextHour(now);
        HourList.ItemsSource = info.Hours.OrderBy(h => h.EndUtc <= now).ThenBy(h => h.EndUtc <= now ? -h.StartUtc.Ticks : h.StartUtc.Ticks)
            .Select(h =>
            {
                var start = h.StartUtc.ToLocalTime();
                var end = h.EndUtc.ToLocalTime();
                bool live = now >= h.StartUtc && now < h.EndUtc, ended = h.EndUtc <= now;
                string tag = live ? "LIVE NOW" : ended ? "ENDED" : h == next ? "NEXT" : "UPCOMING";
                return new HourRow($"{start:ddd d MMM} · {start:h:mm} – {end:h:mm tt}",
                    $"Pirate Shop Access {h.Pirate:0.##}× · Gold Outfit {h.Gold:0.##}× · Vehicle & Decal {h.Vehicle:0.##}×",
                    tag, live ? greenSoft : h == next ? goldSoft : none,
                    live ? (Brush)FindResource("Green") : h == next ? gold : dim, ended ? 0.5 : 1);
            }).ToList();
        HoursHint.Text = _app.Settings.AutoTreasure
            ? "In your time zone. Chests opened in these hours get the boost automatically."
            : "In your time zone. Automatic Treasure Hours is off in Settings, so press F9 when one starts.";

        WeekList.ItemsSource = info.Weeks.Select(w =>
        {
            bool current = now >= w.StartUtc && now < w.EndUtc, ended = w.EndUtc <= now;
            var prizes = w.Prizes.Where(p => !string.IsNullOrWhiteSpace(p) && p != "?").ToList();
            return new WeekRow($"WEEK {w.Number} · {w.StartUtc.ToLocalTime():d MMM} – {w.EndUtc.ToLocalTime():d MMM}", w.Title,
                current ? $"Ends {When(w.EndUtc.ToLocalTime())}" : ended ? "Ended" : $"Starts {When(w.StartUtc.ToLocalTime())}",
                prizes.Count > 0 ? string.Join("\n", prizes) : "Not revealed yet",
                current ? gold : (Brush)FindResource("Line"), ended ? 0.55 : 1);
        }).ToList();

        var st = _app.Status;
        if (st is null)
        {
            StatusOverall.Text = "Checking…";
            StatusSource.Text = "From Epic's public status page.";
            StatusParts.ItemsSource = null;
            StatusNotices.ItemsSource = null;
        }
        else
        {
            bool ok = st.Overall == "Working";
            StatusOverall.Text = ok ? "Fortnite is up" : st.Overall;
            StatusOverall.Foreground = ok ? (Brush)FindResource("Green") : orange;
            StatusPill.Background = ok ? greenSoft : new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0x9A, 0x3C));
            StatusSource.Text = $"From Epic's public status page · checked {st.FetchedUtc.ToLocalTime():h:mm tt}";
            StatusParts.ItemsSource = st.Parts;
            StatusNotices.ItemsSource = st.Notices;
        }
    }

    void RefreshCountdown()
    {
        var info = _app.Treasure;
        var now = DateTime.UtcNow;
        var next = info.NextHour(now);
        NextHourBoosts.Children.Clear();
        if (next is null)
        {
            NextHourLabel.Text = "TREASURE HOURS";
            NextHourCountdown.Text = "None announced";
            NextHourWhen.Text = now > info.EventEndUtc ? "The Treasure Hunters event has ended." : "New Treasure Hours show up here when Epic announces them.";
            return;
        }
        bool live = now >= next.StartUtc;
        var left = (live ? next.EndUtc : next.StartUtc) - now;
        NextHourLabel.Text = live ? "TREASURE HOUR · LIVE NOW · ENDS IN" : "NEXT TREASURE HOUR STARTS IN";
        NextHourCountdown.Text = left.TotalDays >= 1 ? $"{(int)left.TotalDays}d {left.Hours}h {left.Minutes:00}m {left.Seconds:00}s"
                                                     : $"{left.Hours}h {left.Minutes:00}m {left.Seconds:00}s";
        var start = next.StartUtc.ToLocalTime();
        NextHourWhen.Text = $"{When(start)} until {next.EndUtc.ToLocalTime():h:mm tt} (your time)";
        foreach (var (name, boost) in new[] { ("Pirate Shop Access", next.Pirate), ("Gold Outfit", next.Gold), ("Vehicle & Decal", next.Vehicle) })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new TextBlock { Text = name, Foreground = (Brush)FindResource("TextDim"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            row.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 2), Background = (Brush)FindResource("OrangeSoft"),
                Child = new TextBlock { Text = $"{boost:0.##}×", FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Orange") },
            });
            NextHourBoosts.Children.Add(row);
        }
    }

    void RefreshStatus_Click(object sender, RoutedEventArgs e) => _ = _app.RefreshStatusAsync();
}
