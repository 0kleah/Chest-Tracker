#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ChestTracker;

/// <summary>
/// Development checks, in Debug builds only (the public .exe has none of this):
///   --page name     open straight to a page
///   --offscreen     keep the window off the screen, never in front, with no pop-up messages
///   --shots folder  save a picture of every page into a folder, then close
/// Used with a copy of the app in its own folder, so the real data is never touched.
/// </summary>
public partial class MainWindow
{
    public void GoOffscreen()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -30000;
        Top = -30000;
        ShowActivated = false;
        ShowInTaskbar = false;
    }

    RadioButton NavFor(string page) => page.ToLowerInvariant() switch
    {
        "odds" => NavOdds, "settings" => NavSettings, "stats" => NavStats, "map" => NavMap, "treasure" => NavTreasure,
        "matches" => NavMatches, "routes" => NavRoutes, "finds" => NavFinds, "squad" => NavSquad, _ => NavHome,
    };

    void StartDevChecks()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--page");
        if (i >= 0 && i + 1 < args.Length) NavFor(args[i + 1]).IsChecked = true;
        int s = Array.IndexOf(args, "--shots");
        if (s >= 0 && s + 1 < args.Length) _ = ShotsAsync(args[s + 1]);
    }

    async Task ShotsAsync(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            await Task.Delay(2500);
            NavSquad.Visibility = Visibility.Visible;
            var pages = new (string Name, FrameworkElement Page, int Wait)[]
            {
                ("home", HomePage, 1200), ("stats", StatsPage, 1200), ("map", MapPage, 4000), ("matches", MatchesPage, 3000),
                ("routes", RoutesPage, 3000), ("finds", FindsPage, 1200), ("treasure", TreasurePage, 2500), ("odds", OddsPage, 1200),
                ("squad", SquadPage, 1200), ("settings", SettingsPage, 1200),
            };
            foreach (var (name, page, wait) in pages)
            {
                NavFor(name).IsChecked = true;
                await Task.Delay(wait);
                SaveShot(Path.Combine(folder, name + ".png"));
                if (page is ScrollViewer sv && sv.ScrollableHeight > 0)
                {
                    int n = 2;
                    for (double at = sv.ViewportHeight - 60; at < sv.ExtentHeight - 20 && n < 9; at += sv.ViewportHeight - 60, n++)
                    {
                        sv.ScrollToVerticalOffset(at);
                        await Task.Delay(500);
                        SaveShot(Path.Combine(folder, $"{name}-{n}.png"));
                    }
                    sv.ScrollToTop();
                }
            }
            // matches that have a saved match map: the map with every layer
            Width = 1400; Height = 1100;
            NavFor("matches").IsChecked = true;
            await Task.Delay(1500);
            int shown = 0;
            foreach (var row in MatchHistoryList.Items.Cast<MatchHistoryRow>().ToList())
            {
                if (shown >= 3 || !_app.Maps.Has(row.Id)) continue;
                MatchHistoryList.SelectedItem = row;
                await Task.Delay(1500);
                MatchDetailScroll.ScrollToEnd();
                await Task.Delay(500);
                SaveShot(Path.Combine(folder, $"matches-map-{++shown}.png"));
                Save(RouteBox, Path.Combine(folder, $"matches-map-{shown}-big.png"), Colors.Black);
            }
            // one teammate's page (the first in the list), if there is one
            if (_app.Squad.All().FirstOrDefault() is { } mate)
            {
                NavFor("squad").IsChecked = true;
                _teammate = mate.Name;
                ShowTeammate();
                await Task.Delay(1000);
                SaveShot(Path.Combine(folder, "squad-teammate.png"));
                TeammateBack_Click(this, new RoutedEventArgs());
            }
            // the smallest the window can be
            Width = MinWidth > 0 ? MinWidth : 900;
            Height = MinHeight > 0 ? MinHeight : 600;
            foreach (var name in new[] { "home", "matches", "routes", "squad", "settings" })
            {
                NavFor(name).IsChecked = true;
                await Task.Delay(name is "matches" or "routes" ? 2500 : 1000);
                SaveShot(Path.Combine(folder, name + "-small.png"));
            }
            // the overlay (not shown on screen: laid out and drawn into a picture)
            var overlay = new OverlayWindow(_app);
            overlay.ShowToast("+9 chests Â· 1 sprite Â· 2 broken open\nSquad: Teammate One 7 Â· Teammate Two 4");
            if (overlay.Content is FrameworkElement content)
            {
                content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                content.Arrange(new Rect(content.DesiredSize));
                content.UpdateLayout();
                Save(content, Path.Combine(folder, "overlay.png"), Color.FromRgb(40, 60, 90));
            }
            overlay.Close();
            File.WriteAllText(Path.Combine(folder, "done.txt"), $"window {ActualWidth}x{ActualHeight}, min {MinWidth}x{MinHeight}");
        }
        catch (Exception e) { try { File.WriteAllText(Path.Combine(folder, "error.txt"), e.ToString()); } catch (IOException) { } }
        QuitApp();
    }

    void SaveShot(string path) => Save(RootBorder, path, Colors.Black);

    static void Save(FrameworkElement what, string path, Color behind)
    {
        int w = Math.Max(1, (int)Math.Ceiling(what.ActualWidth)), h = Math.Max(1, (int)Math.Ceiling(what.ActualHeight));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(behind), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(what) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        png.Save(fs);
    }
}
#endif
