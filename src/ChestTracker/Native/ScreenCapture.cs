using System.Runtime.InteropServices;
using ChestTracker.Core;

namespace ChestTracker.Native;

/// <summary>
/// Takes a small greyscale picture of the part of the screen Fortnite is shown in (like a screenshot, kept in memory
/// only, never saved). Used while your map is open to see where the map is and how far it's zoomed. Nothing in the
/// game is read or touched.
/// </summary>
internal static class ScreenCapture
{
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out Win32.RECT rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
        public int Colors;
    }

    const uint SRCCOPY = 0x00CC0020;

    /// <summary>The game's picture area on screen (physical pixels), if Fortnite is the window in front.</summary>
    public static Win32.RECT? FortniteArea()
    {
        if (!Win32.FortniteFocused()) return null;
        var hwnd = Win32.GetForegroundWindow();
        if (!GetClientRect(hwnd, out var rc) || rc.Right - rc.Left < 320 || rc.Bottom - rc.Top < 240) return null;
        var p = new POINT();
        if (!ClientToScreen(hwnd, ref p)) return null;
        return new Win32.RECT { Left = p.X, Top = p.Y, Right = p.X + rc.Right - rc.Left, Bottom = p.Y + rc.Bottom - rc.Top };
    }

    // The screen copy is reused from one picture to the next (up to 30 a second while the map is open) and let go of
    // when the map closes (Release), instead of making a new 20 MB picture every time.
    static readonly object Lock = new();
    static IntPtr _mem, _bmp, _old, _bits;
    static int _bw, _bh;
    static byte[]? _bgra;

    static DesktopDuplication? _dd;
    static DateTime _ddRetryAt;

    /// <summary>
    /// How many times to halve a screen <paramref name="screenHeight"/> pixels tall to get close to
    /// <paramref name="height"/> (1440 -> 720, 1080 -> 540, 2160 -> 540, 1600 -> 800, 768 -> 768). The same for
    /// both ways of copying the screen, so the map is found the same way on any monitor.
    /// </summary>
    public static int Halvings(int screenHeight, int height)
    {
        int n = Math.Max(0, (int)Math.Round(Math.Log2(screenHeight / (double)height)));
        while (n > 0 && (screenHeight >> n) < 300) n--;
        return n;
    }

    /// <summary>
    /// A greyscale copy of part of the screen, shrunk to about <paramref name="height"/> pixels tall. Uses Desktop
    /// Duplication (fast, done by the graphics card) when Windows allows it, otherwise the older GDI way.
    /// </summary>
    public static GrayImage? Gray(Win32.RECT area, int height)
    {
        lock (Lock)
        {
            if (_dd is not null && !_dd.Covers(area)) { _dd.Dispose(); _dd = null; }   // Fortnite moved to another monitor
            if (_dd is null && DateTime.UtcNow >= _ddRetryAt)
            {
                try { _dd = DesktopDuplication.For(area); } catch (Exception) { _dd = null; }
                if (_dd is null) _ddRetryAt = DateTime.UtcNow.AddSeconds(3);
            }
            if (_dd is not null)
            {
                GrayImage? fast = null;
                try { fast = _dd.Gray(area, height); } catch (Exception) { }
                if (fast is not null) return fast;
                _dd.Dispose();   // lost (screen mode changed...): set it up again shortly
                _dd = null;
                _ddRetryAt = DateTime.UtcNow.AddSeconds(1);
            }
        }
        return GrayGdi(area, height);
    }

    /// <summary>
    /// The GDI way: copy the screen as it is and average each block of k x k pixels into one. Slower on big screens.
    /// Layered windows (like Chest Tracker's own overlays) aren't in it.
    /// </summary>
    public static GrayImage? GrayGdi(Win32.RECT area, int height)
    {
        int sw = area.Right - area.Left, sh = area.Bottom - area.Top;
        if (sw <= 0 || sh <= 0) return null;
        int k = 1 << Halvings(sh, height);
        int ow = sw / k, oh = sh / k;
        if (ow < 64 || oh < 64) return null;
        byte[] bgra;
        lock (Lock)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            try
            {
                if (_bmp == IntPtr.Zero || _bw != sw || _bh != sh)
                {
                    FreeLocked();
                    _mem = CreateCompatibleDC(screen);
                    var bmi = new BITMAPINFO { Size = 40, Width = sw, Height = -sh, Planes = 1, BitCount = 32 };
                    _bmp = CreateDIBSection(_mem, ref bmi, 0, out _bits, IntPtr.Zero, 0);
                    if (_bmp == IntPtr.Zero || _bits == IntPtr.Zero) { FreeLocked(); return null; }
                    _old = SelectObject(_mem, _bmp);
                    _bw = sw; _bh = sh;
                    _bgra = new byte[sw * sh * 4];
                }
                if (!BitBlt(_mem, 0, 0, sw, sh, screen, area.Left, area.Top, SRCCOPY)) return null;
                bgra = _bgra!;
                Marshal.Copy(_bits, bgra, 0, sw * sh * 4);
            }
            finally { ReleaseDC(IntPtr.Zero, screen); }
        }
        // average each k x k block into one grey pixel (rows side by side on the CPU's cores)
        var gray = new byte[ow * oh];
        int stride = sw * 4, div = k * k;
        Parallel.For(0, oh, y =>
        {
            int orow = y * ow;
            for (int x = 0; x < ow; x++)
            {
                int sum = 0;
                for (int dy = 0; dy < k; dy++)
                {
                    int j = (y * k + dy) * stride + x * k * 4;
                    for (int dx = 0; dx < k; dx++, j += 4)
                        sum += bgra[j] * 29 + bgra[j + 1] * 150 + bgra[j + 2] * 77;
                }
                gray[orow + x] = (byte)(sum / div >> 8);
            }
        });
        return new GrayImage(ow, oh, gray);
    }

    /// <summary>Lets go of the reused screen copies (called when the map closes).</summary>
    public static void Release()
    {
        lock (Lock)
        {
            FreeLocked();
            _dd?.Dispose();
            _dd = null;
        }
    }

    static void FreeLocked()
    {
        if (_old != IntPtr.Zero && _mem != IntPtr.Zero) SelectObject(_mem, _old);
        if (_bmp != IntPtr.Zero) DeleteObject(_bmp);
        if (_mem != IntPtr.Zero) DeleteDC(_mem);
        _mem = _bmp = _old = _bits = IntPtr.Zero;
        _bw = _bh = 0;
        _bgra = null;
    }
}
