using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

/// <summary>
/// How much a see-through window costs to redraw (the route on the map is drawn in one). The window is put far off
/// the screen, so nothing shows; it's moved 60 times a second for a few seconds, and the CPU time used is measured.
/// </summary>
static class RenderBench
{
    public static void Run(int width, int height, bool moveWindow)
    {
        string result = "";
        var t = new Thread(() =>
        {
            var canvas = new Canvas();
            var line = new Polyline { Stroke = Brushes.Gold, StrokeThickness = 4 };
            canvas.Children.Add(line);
            var dots = new List<Ellipse>();
            for (int i = 0; i < 30; i++) { var e = new Ellipse { Width = 24, Height = 24, Fill = Brushes.Gold }; dots.Add(e); canvas.Children.Add(e); }
            var w = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
                ShowActivated = false, Left = -20000, Top = -20000, Width = width, Height = height, Content = canvas,
            };
            int frames = 0;
            var sw = Stopwatch.StartNew();
            var proc = Process.GetCurrentProcess();
            TimeSpan cpu0 = TimeSpan.Zero;
            long last = 0;
            EventHandler? onFrame = null;
            onFrame = (_, _) =>
            {
                long now = sw.ElapsedTicks;
                if (now - last < Stopwatch.Frequency / 62) return;
                last = now;
                if (frames == 30) { proc.Refresh(); cpu0 = proc.TotalProcessorTime; sw.Restart(); last = 0; }
                frames++;
                double k = 1 + 0.5 * Math.Sin(frames * 0.05);
                var pts = new PointCollection();
                for (int i = 0; i < 30; i++)
                {
                    var p = new Point(width / 2.0 + Math.Cos(i * 0.4) * 200 * k, height / 2.0 + Math.Sin(i * 0.3) * 150 * k);
                    pts.Add(p);
                    Canvas.SetLeft(dots[i], p.X - 12);
                    Canvas.SetTop(dots[i], p.Y - 12);
                }
                pts.Freeze();
                line.Points = pts;
                if (moveWindow && frames % 4 == 0) { w.Width = width + (frames % 8 == 0 ? 40 : 0); }
                if (frames == 30 + 180)
                {
                    proc.Refresh();
                    double secs = sw.Elapsed.TotalSeconds;
                    result = $"{width}x{height}{(moveWindow ? " (resizing)" : "")}: {180 / secs:0} redraws a second, CPU {(proc.TotalProcessorTime - cpu0).TotalMilliseconds / secs / 10:0}% of one core";
                    CompositionTarget.Rendering -= onFrame;
                    w.Close();
                }
            };
            w.Closed += (_, _) => System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            w.Show();
            CompositionTarget.Rendering += onFrame;
            System.Windows.Threading.Dispatcher.Run();
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        Console.WriteLine(result);
    }
}
