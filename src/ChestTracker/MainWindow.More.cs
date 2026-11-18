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
                              string Detail, string Kinds, string Finds, Visibility FindsVisibility, string Chests,
                              string LastMatch, Visibility LastMatchVisibility);
/// <summary>One player in a match's squad list (Matches page and the Squad page's last match).</summary>
public sealed record MateRow(string Name, string Badge, Brush BadgeBack, Brush BadgeFore, Visibility BadgeVisibility,
                             string Chests, string ChestsWord, string Kills, string KillsLong);
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
        string? selected = _jumpToMatch ?? (MatchHistoryList.SelectedItem as MatchHistoryRow)?.Id;
        bool jumped = _jumpToMatch is not null;
        _jumpToMatch = null;
        MatchMatesSwitch.IsChecked = _app.Settings.MatchTeammates;
        ShowMatchLayerSwitches();
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
            if (_app.Settings.MatchTeammates && e.Mates is { Count: > 0 } mates)
                bits.Add("with " + string.Join(", ", mates.Select(m => m.Name)));
            return new MatchHistoryRow(e.SessionId!, t.Date == DateTime.Today ? $"Today {t:t}" : $"{t:ddd d MMM}, {t:t}",
                                       bits.Count == 0 ? "Battle Royale" : string.Join(" · ", bits), e.ChestsCounted.ToString("N0"));
        }).ToList();
        MatchHistoryList.ItemsSource = rows;
        MatchHistoryEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var again = rows.FirstOrDefault(r => r.Id == selected) ?? rows.FirstOrDefault();
        MatchHistoryList.SelectedItem = again;
        if (jumped && again is not null) MatchHistoryList.ScrollIntoView(again);
        // Always draw: the list keeps its selection when the same match is still picked (no "selection changed"),
        // and the map picture is let go of whenever you leave the page.
        _ = DrawRouteAsync(again);
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
        SetMatchView(null, new List<Point>());
        var entry = row is null ? null : _matchEntries.FirstOrDefault(m => m.SessionId == row.Id);
        MatchPlaced.Text = entry?.Placement is int pl && pl > 0 ? $"#{pl}" : "–";
        MatchElims.Text = entry?.Eliminations?.ToString() ?? "–";
        MatchChests.Text = entry?.ChestsCounted.ToString("N0") ?? "–";
        MatchLength.Text = entry?.DurationSeconds is int d ? $"{d / 60}:{d % 60:00}" : "–";
        ShowMates(entry);
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
        PrizesHint.Visibility = entry?.Prizes is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;   // the small print only when there's something to explain
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
        var map = string.IsNullOrEmpty(entry.SessionId) ? null : _app.Maps.Load(entry.SessionId);
        MatchMapNote.Text = map is not null ? ""
            : entry.DetailsVersion < Tracker.DetailsVersion && File.Exists(System.IO.Path.Combine(ReplayChests.DemosFolder, entry.File))
                ? "Still reading this match's replay for your path, the storm and the rest…"
                : "Only your chests for this match: its replay was gone before Chest Tracker could save the rest.";
        MatchMapNote.Visibility = MatchMapNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (points.Count == 0 && map is null)
        {
            RouteMessage.Text = "No chest positions for this match (its replay is gone).";
            RouteMessage.Visibility = Visibility.Visible;
            return;
        }
        if (RouteImage.Source is not null) RouteMessage.Visibility = Visibility.Collapsed;
        SetMatchView(map, points.Select(p => p.At).ToList());
        points = points.Select(p => (p.Chest, _matchView(p.At))).ToList();
        // With your real path showing, straight lines from chest to chest would only get in the way.
        DrawRouteOn(RouteCanvas, points, line: !(map is { Path.Count: >= 2 } && _app.Settings.LayerPath));
        if (map is not null) DrawMatchLayers(RouteCanvas, map);
    }

    void ShowMatchLayerSwitches()
    {
        var s = _app.Settings;
        LayerPath.IsChecked = s.LayerPath; LayerBus.IsChecked = s.LayerBus; LayerStorm.IsChecked = s.LayerStorm;
        LayerMarks.IsChecked = s.LayerMarks; LayerMates.IsChecked = s.LayerMates; LayerOthers.IsChecked = s.LayerOthers;
        LayerCloseUp.IsChecked = s.MatchCloseUp;
    }

    void MatchLayer_Click(object sender, RoutedEventArgs e)
    {
        var s = _app.Settings;
        s.LayerPath = LayerPath.IsChecked == true; s.LayerBus = LayerBus.IsChecked == true; s.LayerStorm = LayerStorm.IsChecked == true;
        s.LayerMarks = LayerMarks.IsChecked == true; s.LayerMates = LayerMates.IsChecked == true; s.LayerOthers = LayerOthers.IsChecked == true;
        s.MatchCloseUp = LayerCloseUp.IsChecked == true;
        Save();
        _ = DrawRouteAsync(MatchHistoryList.SelectedItem as MatchHistoryRow);
    }

    // The match map's view: the whole island, or a close-up of where you went (markers keep their size).
    Func<Point, Point> _matchView = p => p;
    double _matchZoom = 1;

    void SetMatchView(MatchMap? map, List<Point> chests)
    {
        _matchView = p => p;
        _matchZoom = 1;
        RouteImage.RenderTransform = Transform.Identity;
        if (!_app.Settings.MatchCloseUp) return;
        var pts = new List<Point>(chests);
        if (map is not null)
        {
            if (_app.Settings.LayerPath) pts.AddRange(map.Path.Select(p => MapPoint(p[1], p[2])));
            if (_app.Settings.LayerMarks) pts.AddRange(map.Marks.Select(m => MapPoint(m.X, m.Y)));
        }
        if (pts.Count == 0) return;
        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        double side = Math.Clamp(Math.Max(maxX - minX, maxY - minY) * 1.2 + 90, 230, 1000);   // at most about 4x closer
        if (side >= 900) return;   // already nearly the whole island
        double x0 = Math.Clamp((minX + maxX) / 2 - side / 2, 0, 1000 - side), y0 = Math.Clamp((minY + maxY) / 2 - side / 2, 0, 1000 - side);
        double k = 1000 / side;
        _matchZoom = k;
        _matchView = p => new Point((p.X - x0) * k, (p.Y - y0) * k);
        RouteImage.RenderTransform = new MatrixTransform(k, 0, 0, k, -x0 * k, -y0 * k);
    }

    Point MV(float x, float y) => _matchView(MapPoint(x, y));

    static readonly Color[] MateColours = { Color.FromRgb(0x3D, 0xDC, 0x84), Color.FromRgb(0xFF, 0x6F, 0xB5), Color.FromRgb(0x2E, 0xE6, 0xC8) };

    /// <summary>
    /// The rest of a match's map around your chests: storm, bus, your path, teammates' and others' chests underneath,
    /// your knocks and eliminations on top. Each layer can be switched off.
    /// </summary>
    void DrawMatchLayers(Canvas canvas, MatchMap map)
    {
        var s = _app.Settings;
        int under = 0;   // layers go under your route (which is already drawn), in this order
        void Under(UIElement e) => canvas.Children.Insert(under++, e);
        var shadow = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0));
        var cal = _app.Treasure.Map;
        double perUnit = 1000 / cal.Size * cal.Scale * _matchZoom;   // canvas pixels per world unit

        if (s.LayerStorm)
        {
            var storm = new SolidColorBrush(Color.FromRgb(0xB0, 0x7C, 0xFF));
            int n = 0;
            foreach (var z in map.Storm)
            {
                n++;
                var c = MV(z[0], z[1]);
                double r = z[2] * perUnit;
                if (r < 2) continue;
                var ring = new Ellipse { Width = r * 2, Height = r * 2, Stroke = storm, StrokeThickness = 2.5, Opacity = 0.85, ToolTip = $"Storm circle {n}" };
                Canvas.SetLeft(ring, c.X - r); Canvas.SetTop(ring, c.Y - r);
                Under(ring);
                var label = new Border
                {
                    Background = storm, CornerRadius = new CornerRadius(7), Padding = new Thickness(5, 0, 5, 1),
                    Child = new TextBlock { Text = n.ToString(CultureInfo.InvariantCulture), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White },
                };
                Canvas.SetLeft(label, c.X - 7); Canvas.SetTop(label, c.Y - r - 9);
                Under(label);
            }
        }

        if (s.LayerBus && map.Bus is { Length: 4 } b)
        {
            Point a = MV(b[0], b[1]), z = MV(b[2], b[3]);
            Under(new Line { X1 = a.X, Y1 = a.Y, X2 = z.X, Y2 = z.Y, Stroke = shadow, StrokeThickness = 5 });
            Under(new Line { X1 = a.X, Y1 = a.Y, X2 = z.X, Y2 = z.Y, Stroke = Brushes.White, StrokeThickness = 2.5, StrokeDashArray = new DoubleCollection { 4, 3 }, ToolTip = "Bus route" });
            // arrow heads along the line, pointing the way it flew (only the ones on the map)
            double len = Math.Sqrt((z.X - a.X) * (z.X - a.X) + (z.Y - a.Y) * (z.Y - a.Y));
            if (len > 1)
            {
                double ux = (z.X - a.X) / len, uy = (z.Y - a.Y) / len;
                for (double d = 0; d < len; d += 160)
                {
                    double x = a.X + ux * d, y = a.Y + uy * d;
                    if (x < 20 || y < 20 || x > 980 || y > 980) continue;
                    var head = new Polygon { Fill = Brushes.White, Stroke = shadow, StrokeThickness = 1 };
                    head.Points.Add(new Point(x + ux * 11, y + uy * 11));
                    head.Points.Add(new Point(x - ux * 7 - uy * 8, y - uy * 7 + ux * 8));
                    head.Points.Add(new Point(x - ux * 7 + uy * 8, y - uy * 7 - ux * 8));
                    Under(head);
                }
            }
        }

        if (s.LayerPath && map.Path.Count >= 2)
        {
            var blue = new SolidColorBrush(Color.FromRgb(0x5C, 0xE1, 0xFF));
            var back = new Polyline { Stroke = shadow, StrokeThickness = 7.5, StrokeLineJoin = PenLineJoin.Round, StrokeEndLineCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round, Opacity = 0.75 };
            var line = new Polyline { Stroke = blue, StrokeThickness = 3.5, StrokeLineJoin = PenLineJoin.Round, StrokeEndLineCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round, ToolTip = "Your path" };
            foreach (var p in map.Path) { var at = MV(p[1], p[2]); back.Points.Add(at); line.Points.Add(at); }
            Under(back); Under(line);
            var start = MV(map.Path[0][1], map.Path[0][2]);
            var jump = new Ellipse { Width = 14, Height = 14, Fill = blue, Stroke = Brushes.White, StrokeThickness = 2, ToolTip = "Where you jumped from the bus" };
            Canvas.SetLeft(jump, start.X - 7); Canvas.SetTop(jump, start.Y - 7);
            Under(jump);
        }

        if (s.LayerOthers)
        {
            var grey = new SolidColorBrush(Color.FromRgb(0xAA, 0xB2, 0xC0));
            foreach (var c in map.Others)
            {
                var at = MV(c[1], c[2]);
                var dot = new Rectangle
                {
                    Width = 11, Height = 11, Fill = grey, Stroke = shadow, StrokeThickness = 1.5, RadiusX = 2, RadiusY = 2, Opacity = 0.9,
                    ToolTip = $"Opened by another player at {(int)c[0] / 60}:{(int)c[0] % 60:00}{(c.Length > 3 && c[3] > 0 ? " (broken open)" : "")}",
                };
                Canvas.SetLeft(dot, at.X - 5.5); Canvas.SetTop(dot, at.Y - 5.5);
                Under(dot);
            }
        }

        if (s.LayerMates)
        {
            int i = 0;
            foreach (var mate in map.Mates)
            {
                var brush = new SolidColorBrush(MateColours[i++ % MateColours.Length]);
                foreach (var c in mate.Chests)
                {
                    var at = MV(c[1], c[2]);
                    var dot = new Ellipse
                    {
                        Width = 15, Height = 15, Fill = brush, Stroke = shadow, StrokeThickness = 1.5,
                        ToolTip = $"{mate.Name}{(mate.Bot ? " (bot)" : "")} at {(int)c[0] / 60}:{(int)c[0] % 60:00}{(c.Length > 3 && c[3] > 0 ? " (broken open)" : "")}",
                    };
                    Canvas.SetLeft(dot, at.X - 7.5); Canvas.SetTop(dot, at.Y - 7.5);
                    Under(dot);
                }
            }
        }

        if (s.LayerMarks)
        {
            foreach (var m in map.Marks)
            {
                var at = MV(m.X, m.Y);
                string who = m.Other is null ? "" : $" {m.Other}{(m.Bot ? " (bot)" : "")}";
                (Brush fill, Brush stroke, string mark, string tip) = m.Kind switch
                {
                    "elim" => ((Brush)FindResource("Red"), (Brush)Brushes.White, "✕", $"You eliminated{(who.Length > 0 ? who : " a player")}"),
                    "knock" => ((Brush)Brushes.Transparent, (Brush)FindResource("Orange"), "", $"You knocked{(who.Length > 0 ? who : " a player")}"),
                    "knocked" => ((Brush)Brushes.Transparent, (Brush)Brushes.White, "", $"You were knocked{(who.Length > 0 ? " by" + who : "")}"),
                    _ => ((Brush)Brushes.White, (Brush)FindResource("Red"), "✕", $"You were eliminated{(who.Length > 0 ? " by" + who : "")}"),
                };
                var g = new Grid { Width = 28, Height = 28, ToolTip = tip };
                g.Children.Add(new Ellipse { Fill = shadow });   // a dark edge so it shows on gold chests and bright ground
                g.Children.Add(new Ellipse { Margin = new Thickness(2), Fill = fill, Stroke = stroke, StrokeThickness = 3 });
                if (mark.Length > 0)
                    g.Children.Add(new TextBlock
                    {
                        Text = mark, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = m.Kind == "elim" ? Brushes.White : (Brush)FindResource("Red"),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    });
                Canvas.SetLeft(g, at.X - 14); Canvas.SetTop(g, at.Y - 14);
                canvas.Children.Add(g);
            }
        }
    }

    /// <summary>A route on a 1000 x 1000 map: a dotted line and numbered chests (sprite chests purple, broken open orange).</summary>
    void DrawRouteOn(Canvas canvas, List<(ChestLogEntry Chest, Point At)> points, bool line = true)
    {
        canvas.Children.Clear();
        var gold = (Brush)FindResource("Gold");
        if (line)
        {
            var dashed = new Polyline { Stroke = gold, StrokeThickness = 3.5, Opacity = 0.85, StrokeLineJoin = PenLineJoin.Round, StrokeDashArray = new DoubleCollection { 3, 2 } };
            foreach (var p in points) dashed.Points.Add(p.At);
            canvas.Children.Add(dashed);
        }
        var orange = (Brush)FindResource("Orange");
        var sprite = new SolidColorBrush(Color.FromRgb(0xB5, 0x7B, 0xFF));
        int n = 0;
        foreach (var (c, at) in points)
        {
            n++;
            var dot = new Grid { Width = 30, Height = 30, ToolTip = $"#{n} · {c.Kind ?? "Chest"} · {(c.Broken ? "broken open" : "opened")} · {c.When:T}" };
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
            canvas.Children.Add(dot);
        }
    }

    /// <summary>The route map is as big as fits under the top row (never squashed: the details scroll instead).</summary>
    void MatchDetailScroll_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RouteBox.MaxHeight = Math.Max(380, MatchDetailScroll.ActualHeight - 100);

    void MatchMatesSwitch_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.MatchTeammates = MatchMatesSwitch.IsChecked == true;
        Save();
        RefreshMatchHistory();
        ShowMates(_matchEntries.FirstOrDefault(m => m.SessionId == (MatchHistoryList.SelectedItem as MatchHistoryRow)?.Id));
    }

    /// <summary>The picked match's squad: you and each teammate, with chests and eliminations.</summary>
    void ShowMates(ReplayLedger.Entry? entry)
    {
        MatesBox.Visibility = _app.Settings.MatchTeammates && entry is not null ? Visibility.Visible : Visibility.Collapsed;
        if (MatesBox.Visibility != Visibility.Visible) { MatesList.ItemsSource = null; return; }
        if (entry!.Mates is null)
        {
            MatesList.ItemsSource = null;
            MatesMessage.Text = entry.DetailsVersion < Tracker.DetailsVersion
                ? "Still reading this match's replay… (it's read between matches)"
                : "Not saved for this match (its replay is gone).";
            MatesMessage.Visibility = Visibility.Visible;
            return;
        }
        MatesMessage.Text = entry.Mates.Count == 0 ? "No teammates in this match (solo)." : "";
        MatesMessage.Visibility = entry.Mates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MatesList.ItemsSource = MateRows(entry);
    }

    /// <summary>You first, then your teammates (most chests first).</summary>
    List<MateRow> MateRows(ReplayLedger.Entry entry)
    {
        var gold = (Brush)FindResource("Gold");
        var goldSoft = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xCD, 0x50));
        var dim = (Brush)FindResource("TextDim");
        var dimSoft = (Brush)FindResource("Surface2");
        var squad = _app.Squad.All();
        static string Word(int n) => n == 1 ? "chest" : "chests";
        static string Long(int? k) => k is int n ? $"{n} elim{(n == 1 ? "" : "s")}" : "elims ?";
        var rows = new List<MateRow>
        {
            new("You", "", dimSoft, dim, Visibility.Collapsed, entry.ChestsCounted.ToString("N0"), Word(entry.ChestsCounted),
                entry.Eliminations?.ToString() ?? "–", Long(entry.Eliminations)),
        };
        foreach (var m in (entry.Mates ?? new()).OrderByDescending(m => m.Chests).ThenByDescending(m => m.Kills ?? 0))
        {
            var known = squad.FirstOrDefault(p => string.Equals(p.Name, m.Name, StringComparison.OrdinalIgnoreCase));
            bool friend = !m.Bot && (known?.FriendOverride ?? m.Party);
            rows.Add(new MateRow(m.Name, m.Bot ? "BOT" : friend ? "FRIEND" : "RANDOM", friend ? goldSoft : dimSoft, friend ? gold : dim,
                                 Visibility.Visible, m.Chests.ToString("N0"), Word(m.Chests), m.Kills?.ToString() ?? "–", Long(m.Kills)));
        }
        return rows;
    }

    static string MatchTime(DateTime utc)
    {
        var t = utc.ToLocalTime();
        return t.Date == DateTime.Today ? $"today {t:t}" : t.Date == DateTime.Today.AddDays(-1) ? $"yesterday {t:t}" : $"{t:ddd d MMM}, {t:t}";
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
        if (f.WhenUtc is DateTime w) bits.Add($"{w.ToLocalTime():ddd d MMM}, {w.ToLocalTime():t}");
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
        if (_teammate is not null && TeammateView.Visibility == Visibility.Visible) ShowTeammate();
        SquadTrackSwitch.IsChecked = _app.Settings.SquadTracking;
        SquadTotalSwitch.IsChecked = _app.Settings.SquadInTotal;
        SquadTotalSwitch.IsEnabled = _app.Settings.SquadTracking;
        var everyone = _app.Squad.All();
        string search = SquadSearch.Text.Trim();
        var people = search.Length == 0 ? everyone
                   : everyone.Where(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        SquadSearchPlaceholder.Visibility = SquadSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var gold = (Brush)FindResource("Gold");
        var goldSoft = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xCD, 0x50));
        var dim = (Brush)FindResource("TextDim");
        var dimSoft = (Brush)FindResource("Surface2");

        // Your most recent match with teammates, and each person's last match with you.
        var withMates = _app.Ledger.All()
            .Where(e => e.Mates is { Count: > 0 } && e.MatchStartUtc is not null && e.Note is null && !string.IsNullOrEmpty(e.SessionId))
            .OrderByDescending(e => e.MatchStartUtc).ToList();
        var lastWith = new Dictionary<string, (ReplayLedger.Entry Entry, ReplayLedger.MateSeen Mate)>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in withMates)
            foreach (var m in e.Mates!)
                lastWith.TryAdd(m.Name, (e, m));
        if (withMates.FirstOrDefault() is { } last)
        {
            LastSquadBox.Visibility = Visibility.Visible;
            var bits = new List<string> { MatchTime(last.MatchStartUtc!.Value) };
            if (last.Placement is int pl && pl > 0) bits.Add($"placed #{pl}");
            if (Mode(last.Playlist) is string mode) bits.Add(mode);
            LastSquadWhen.Text = string.Join(" · ", bits);
            LastSquadList.ItemsSource = MateRows(last);
        }
        else LastSquadBox.Visibility = Visibility.Collapsed;

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
            p.Chests.ToString("N0"),
            lastWith.TryGetValue(p.Name, out var lw)
                ? $"Last match together ({MatchTime(lw.Entry.MatchStartUtc!.Value)}): {lw.Mate.Chests} chest{(lw.Mate.Chests == 1 ? "" : "s")}"
                  + (lw.Mate.Kills is int k ? $" · {k} elim{(k == 1 ? "" : "s")}" : "")
                : "",
            lastWith.ContainsKey(p.Name) ? Visibility.Visible : Visibility.Collapsed)).ToList();
        SquadEmpty.Visibility = people.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SquadEmpty.Text = everyone.Count > 0 && people.Count == 0 ? $"Nobody called \"{search}\" in your list."
                        : _app.Settings.SquadTracking ? "No teammates yet. Play a match with friends (or wait while saved replays are read)." : "Turn on squad tracking and play with friends. They'll show up here.";
    }

    // ------------------------------------------------------------------ one teammate

    public sealed record TeammateMatchRow(string Id, string When, string Detail, string You, string Them);

    string? _teammate;
    string? _jumpToMatch;

    void SquadOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        _teammate = name;
        ShowTeammate();
        SquadPage.ScrollToTop();
    }

    void TeammateBack_Click(object sender, RoutedEventArgs e)
    {
        _teammate = null;
        TeammateView.Visibility = Visibility.Collapsed;
        SquadMain.Visibility = Visibility.Visible;
        RefreshSquad();
    }

    void TeammateMatchOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        _jumpToMatch = id;
        if (NavMatches.IsChecked == true) RefreshMatchHistory();
        else NavMatches.IsChecked = true;
    }

    /// <summary>Every match you played with one teammate: both your chests and eliminations in each, and the totals.</summary>
    void ShowTeammate()
    {
        if (_teammate is not string name) return;
        var person = _app.Squad.All().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        var together = _app.Ledger.All()
            .Where(e => e.Mates is { Count: > 0 } && e.MatchStartUtc is not null && e.Note is null && !string.IsNullOrEmpty(e.SessionId)
                        && e.Mates.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.MatchStartUtc)
            .Select(e => (Entry: e, Mate: e.Mates!.First(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))))
            .ToList();
        SquadMain.Visibility = Visibility.Collapsed;
        TeammateView.Visibility = Visibility.Visible;
        bool bot = person?.Bot ?? together.FirstOrDefault().Mate?.Bot ?? false;
        bool friend = person?.IsFriend == true && !bot;
        TeammateName.Text = name;
        TeammateBadge.Text = bot ? "BOT" : friend ? "FRIEND" : "RANDOM";
        TeammateBadgeBox.Background = friend ? new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xCD, 0x50)) : (Brush)FindResource("Surface2");
        TeammateBadge.Foreground = friend ? (Brush)FindResource("Gold") : (Brush)FindResource("TextDim");
        string shortName = name.Length > 16 ? name[..15] + "…" : name;
        TmTheirsLabel.Text = $"{shortName}'s chests";
        TmThemHeader.Text = shortName.ToUpperInvariant();

        int n = together.Count;
        int mine = together.Sum(t => t.Entry.ChestsCounted), theirs = together.Sum(t => t.Mate.Chests);
        int myElims = together.Sum(t => t.Entry.Eliminations ?? 0), theirElims = together.Sum(t => t.Mate.Kills ?? 0);
        int wins = together.Count(t => t.Entry.Placement == 1);
        TmMatches.Text = n.ToString("N0");
        TmYours.Text = mine.ToString("N0");
        TmTheirs.Text = theirs.ToString("N0");
        TmElims.Text = $"{myElims} / {theirElims}";
        var sub = new List<string>();
        if (person is not null && person.PartyMatches > 0) sub.Add($"{person.PartyMatches} match{(person.PartyMatches == 1 ? "" : "es")} in your party");
        if (wins > 0) sub.Add($"{wins} win{(wins == 1 ? "" : "s")} together");
        if (together.FirstOrDefault().Entry is { } latest) sub.Add($"last played {MatchTime(latest.MatchStartUtc!.Value)}");
        TeammateSub.Text = string.Join(" · ", sub);
        TmAverages.Text = n == 0 ? ""
            : $"Per match together: you {(double)mine / n:0.#} chests, {shortName} {(double)theirs / n:0.#}."
              + (theirs > mine ? $" {shortName} opened more chests than you overall." : mine > theirs ? " You opened more chests overall." : "");
        TeammateMatches.ItemsSource = together.Select(t =>
        {
            var bits = new List<string>();
            if (t.Entry.Placement is int pl && pl > 0) bits.Add(t.Entry.Players is int p && p > 0 ? $"#{pl} of {p}" : $"#{pl}");
            if (Mode(t.Entry.Playlist) is string mode) bits.Add(mode);
            var others = t.Entry.Mates!.Where(m => !string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)).Select(m => m.Name).ToList();
            if (others.Count > 0) bits.Add("also " + string.Join(", ", others));
            string Line(int chests, int? elims) => $"{chests} chest{(chests == 1 ? "" : "s")}{(elims is int k ? $" · {k} elim{(k == 1 ? "" : "s")}" : "")}";
            return new TeammateMatchRow(t.Entry.SessionId!, MatchTime(t.Entry.MatchStartUtc!.Value), string.Join(" · ", bits),
                                        Line(t.Entry.ChestsCounted, t.Entry.Eliminations), Line(t.Mate.Chests, t.Mate.Kills));
        }).ToList();
        int older = (person?.Matches ?? 0) - n;
        TmNote.Text = (n == 0 ? "No matches with details yet. " : "")
            + (older > 0 ? $"{older} older match{(older == 1 ? "" : "es")} together had their replays read before Chest Tracker kept match details, so they're in the totals on the Squad list but not here. " : "")
            + "Teammates' chests are only the ones opened near you: your replay doesn't see the rest of the island.";
    }

    void SquadSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready) RefreshSquad();
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

    /// <summary>
    /// Started by Windows or quietly (--tray): get everything going without showing the window. It's shown for the
    /// first time when you open it from the tray. (Showing it invisibly and hiding it straight away left it drawing
    /// black when it was opened later.)
    /// </summary>
    public void StartHidden()
    {
        _startHidden = true;
        new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        OnLoaded(this, new RoutedEventArgs());
        Memory.Trim();
    }

    void SetUpBackground()
    {
        UpdateTray();
        _fortniteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _fortniteTimer.Tick += (_, _) => FollowFortnite();
        _fortniteTimer.Start();
        FollowFortnite();
        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _reminderTimer.Tick += (_, _) => CheckReminders();
        _reminderTimer.Start();
    }

    // ------------------------------------------------------------------ start and stop with Fortnite

    DispatcherTimer? _fortniteTimer;
    bool? _fortniteWasRunning;
    DateTime? _fortniteClosedAt;

    /// <summary>Waiting in the tray for Fortnite (the overlay is away until it starts).</summary>
    bool Asleep => _app.Settings.FollowFortnite && _fortniteWasRunning == false && _fortniteClosedAt is null;

    /// <summary>
    /// Every few seconds: when Fortnite starts, the overlay comes up; a minute after Fortnite closes (time for your last
    /// match to be counted and its summary shown), it goes away and Chest Tracker waits quietly in the tray. Only acts
    /// when Fortnite starts or stops, so switching the overlay on or off by hand in between is left alone.
    /// </summary>
    void FollowFortnite()
    {
        if (!_app.Settings.FollowFortnite) { _fortniteWasRunning = null; _fortniteClosedAt = null; return; }
        bool running = Win32.FortniteRunning();
        if (running && _fortniteWasRunning != true)
        {
            _fortniteClosedAt = null;
            if (_app.Settings.OverlayEnabled) ShowOverlay();
        }
        else if (!running && _fortniteWasRunning == true)
        {
            _fortniteClosedAt = DateTime.UtcNow;
        }
        if (!running && _fortniteClosedAt is DateTime closed && DateTime.UtcNow - closed > TimeSpan.FromSeconds(60))
        {
            _fortniteClosedAt = null;
            GoToSleep();
        }
        _fortniteWasRunning = running;
        UpdateTrayTip();
    }

    /// <summary>Fortnite closed: put the overlay away (your overlay setting stays on) and free what isn't needed.</summary>
    void GoToSleep()
    {
        _overlay?.Close();
        _overlay = null;
        if (!IsVisible) ReleaseMap();
        Memory.Trim();
    }

    void UpdateTrayTip() =>
        _tray?.SetTooltip($"Chest Tracker · {_app.Store.Total:N0} chests · {Odds.Percent(_app.Chance)} token chance"
                          + (Asleep ? " · waiting for Fortnite" : ""));

    void FollowSwitch_Click(object sender, RoutedEventArgs e)
    {
        bool on = FollowSwitch.IsChecked == true;
        if (on && !WindowsStartup.IsOn() && !WindowsStartup.Set(true))
        {
            FollowSwitch.IsChecked = false;
            MessageBox.Show(this, "Windows didn't allow Chest Tracker to start with Windows, which this needs.", "Chest Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var s = _app.Settings;
        s.FollowFortnite = on;
        if (on)
        {
            // It has to be running to notice Fortnite starting: start with Windows, and stay in the tray when closed.
            s.StartWithWindows = true;
            s.TrayMode = true;
            StartupSwitch.IsChecked = true;
            TraySwitch.IsChecked = true;
            UpdateTray();
            _fortniteWasRunning = null;
            _fortniteClosedAt = null;
            FollowFortnite();
            if (_fortniteWasRunning == false) GoToSleep();
            FollowStatus.Text = _fortniteWasRunning == true
                ? "On. Fortnite is running, so the overlay stays up until a minute after you close it."
                : "On. The overlay is put away until Fortnite starts.";
        }
        else
        {
            FollowStatus.Text = "Off. The overlay stays up whenever Chest Tracker is running.";
            if (s.OverlayEnabled) ShowOverlay();
            UpdateTrayTip();
        }
        FollowStatus.Visibility = Visibility.Visible;
        Save();
    }

    /// <summary>The tray icon is there while tray mode or reminders are on (reminders show from it).</summary>
    void UpdateTray()
    {
        bool want = _app.Settings.TrayMode || _app.Settings.Reminders || _startHidden || _app.Settings.FollowFortnite;
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
        UpdateTrayTip();
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
        UpdateTrayTip();
        if (!_app.Settings.Reminders || _tray is null) return;
        var now = DateTime.UtcNow;
        if (_app.Treasure.NextHour(now) is not { } h) return;
        var start = h.StartUtc.ToLocalTime();
        var end = h.EndUtc.ToLocalTime();
        if (now < h.StartUtc && h.StartUtc - now <= TimeSpan.FromMinutes(10) && _reminded.Add(h.StartUtc.AddTicks(1)))
            _tray.Notify("Treasure Hour in 10 minutes", $"From {start:t} to {end:t}: {h.Pirate:0.##}x Pirate Shop Access odds. Get into a match!");
        else if (now >= h.StartUtc && now - h.StartUtc < TimeSpan.FromMinutes(5) && _reminded.Add(h.StartUtc))
            _tray.Notify("Treasure Hour has started", $"{h.Pirate:0.##}x Pirate Shop Access odds until {end:t}.");
    }

    void TraySwitch_Click(object sender, RoutedEventArgs e)
    {
        if (TraySwitch.IsChecked != true && _app.Settings.FollowFortnite)
        {
            TraySwitch.IsChecked = true;   // it waits for Fortnite in the tray
            FollowStatus.Text = "\"Start and stop with Fortnite\" waits in the tray, so this stays on while that's on.";
            FollowStatus.Visibility = Visibility.Visible;
            return;
        }
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
        if (!on && _app.Settings.FollowFortnite)
        {
            // without starting with Windows it can't be there when Fortnite starts
            _app.Settings.FollowFortnite = false;
            FollowSwitch.IsChecked = false;
            if (_app.Settings.OverlayEnabled) ShowOverlay();
        }
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
        // "Start with Windows" points at this .exe. If the .exe was moved since it was switched on, point it at the new place.
        if (s.StartWithWindows && !WindowsStartup.IsOn() && !App.Quiet) WindowsStartup.Set(true);
        StartupSwitch.IsChecked = WindowsStartup.IsOn();
        RemindersSwitch.IsChecked = s.Reminders;
        FollowSwitch.IsChecked = s.FollowFortnite;
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
        NextHourBoosts.Children.Clear();   // rebuilt in the new colours
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
