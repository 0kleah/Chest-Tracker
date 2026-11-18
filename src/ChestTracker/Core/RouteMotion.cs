namespace ChestTracker.Core;

/// <summary>
/// Moves the route on screen smoothly with the map. Map positions arrive 30-60 times a second, each from a screen
/// picture taken a moment earlier. Jumping to each new position looks like a staircase, and guessing ahead overshoots
/// when a zoom stops. So, like games do to move other players smoothly, the route is shown a fixed moment behind (about
/// how long a position takes to arrive, plus a little), sliding evenly between the two positions either side of that
/// moment: every screen frame moves it a little, it never pauses or overshoots, and it stops dead with the map. What's
/// followed is what you see: the route's size and middle on screen. Position: screen = S x map + (X, Y).
/// </summary>
public sealed class RouteMotion
{
    public static readonly double TicksPerSecond = System.Diagnostics.Stopwatch.Frequency;
    const double Wobble = 1.0;   // pixels: a change smaller than this between two positions is just wobble

    readonly record struct Fix(long At, double W, double X, double Y);

    readonly Fix[] _fixes = new Fix[16];   // the newest positions, oldest first
    int _count;
    double _latency = 0.020, _interval = 0.022;   // how long positions take to arrive, and how far apart (seconds)
    // the route's middle and size on the map (what's followed on screen)
    double _cx = 1024, _cy = 1024, _size = 1000;
    double _dw, _dx, _dy;

    /// <summary>What's on screen now.</summary>
    public double S { get; private set; }
    public double X { get; private set; }
    public double Y { get; private set; }
    public bool Shown { get; private set; }
    public bool HasTarget => _count > 0;

    /// <summary>How far behind the newest picture the route is shown (seconds).</summary>
    public double Delay => _latency + 0.6 * _interval;

    /// <summary>The route's middle and size on the map, in map pixels.</summary>
    public void SetRoute(double centerX, double centerY, double size)
    {
        _cx = centerX; _cy = centerY; _size = Math.Max(50, size);
        Reset();
    }

    public void Reset()
    {
        _count = 0;
        Shown = false;
    }

    /// <summary>
    /// A new map position, found in a screen picture taken at <paramref name="takenAt"/>, arriving at
    /// <paramref name="arrivedAt"/> (Stopwatch ticks; now if not given).
    /// </summary>
    public void Target(double s, double x, double y, long takenAt, long? arrivedAt = null)
    {
        long arrived = arrivedAt ?? System.Diagnostics.Stopwatch.GetTimestamp();
        var fix = new Fix(takenAt, s * _size, s * _cx + x, s * _cy + y);
        if (_count > 0)
        {
            var last = _fixes[_count - 1];
            double dt = (takenAt - last.At) / TicksPerSecond;
            if (dt <= 0.0005) return;   // the same picture again
            bool jump = Math.Abs(fix.W - last.W) > 0.4 * Math.Max(last.W, 50) || Math.Abs(fix.X - last.X) + Math.Abs(fix.Y - last.Y) > 600;
            if (dt > 0.3 || jump)
            {
                _count = 0;      // a long gap, or the map moved a long way: start again from here, straight there
                Shown = false;
            }
            else
            {
                _interval = 0.8 * _interval + 0.2 * Math.Clamp(dt, 0.005, 0.1);
                // barely different from the last one: that's wobble, not movement, so only take a little of it
                if (Math.Abs(fix.W - last.W) < Wobble && Math.Abs(fix.X - last.X) < Wobble && Math.Abs(fix.Y - last.Y) < Wobble)
                    fix = new Fix(takenAt, last.W + 0.2 * (fix.W - last.W), last.X + 0.2 * (fix.X - last.X), last.Y + 0.2 * (fix.Y - last.Y));
            }
        }
        _latency = 0.8 * _latency + 0.2 * Math.Clamp((arrived - takenAt) / TicksPerSecond, 0, 0.1);
        if (_count == _fixes.Length) { Array.Copy(_fixes, 1, _fixes, 0, _count - 1); _count--; }
        _fixes[_count++] = fix;
    }

    /// <summary>Moves what's on screen on to time <paramref name="now"/>. Returns false if it didn't visibly change.</summary>
    public bool Step(long now)
    {
        if (_count == 0) return false;
        double behind = now / TicksPerSecond - Delay;   // the moment shown
        double w, x, y;
        if (_count == 1 || behind <= _fixes[0].At / TicksPerSecond)
        {
            var f = _fixes[0];
            w = f.W; x = f.X; y = f.Y;
        }
        else if (behind >= _fixes[_count - 1].At / TicksPerSecond)
        {
            // past the newest position: carry on its way for a moment (half a gap at most), then wait for the next
            var a = _fixes[_count - 2];
            var b = _fixes[_count - 1];
            double gap = (b.At - a.At) / TicksPerSecond;
            double over = Math.Min(behind - b.At / TicksPerSecond, 0.5 * _interval);
            double k = gap > 0 ? over / gap : 0;
            w = b.W + (b.W - a.W) * k; x = b.X + (b.X - a.X) * k; y = b.Y + (b.Y - a.Y) * k;
        }
        else
        {
            int i = 1;
            while (i < _count - 1 && _fixes[i].At / TicksPerSecond < behind) i++;
            var a = _fixes[i - 1];
            var b = _fixes[i];
            double k = (behind - a.At / TicksPerSecond) / ((b.At - a.At) / TicksPerSecond);
            w = a.W + (b.W - a.W) * k; x = a.X + (b.X - a.X) * k; y = a.Y + (b.Y - a.Y) * k;
        }
        if (Shown && Math.Abs(w - _dw) < 0.15 && Math.Abs(x - _dx) < 0.15 && Math.Abs(y - _dy) < 0.15) return false;   // no visible change
        _dw = Math.Max(1, w); _dx = x; _dy = y;
        S = _dw / _size;
        X = _dx - S * _cx;
        Y = _dy - S * _cy;
        Shown = true;
        return true;
    }
}
