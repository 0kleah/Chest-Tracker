using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ChestTracker.Native;

/// <summary>
/// Windows calls used by the app: hotkey state, which window is active, whether a game has
/// captured the mouse (so the overlay stays click-through), and overlay window styles.
/// Nothing here reads or touches Fortnite itself.
/// </summary>
internal static class Win32
{
    // ---------------------------------------------------------------- keyboard / mouse buttons
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);

    public static bool KeyDown(int vk) => vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;

    // ---------------------------------------------------------------- foreground window
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);

    static uint _cachedPid;
    static bool _cachedIsFortnite;

    public static bool FortniteFocused()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        if (pid == 0) return false;
        if (pid == _cachedPid) return _cachedIsFortnite;
        bool isFortnite = false;
        IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageNameW(h, 0, sb, ref size))
                    isFortnite = Path.GetFileName(sb.ToString()).Contains("fortniteclient", StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseHandle(h); }
        }
        _cachedPid = pid;
        _cachedIsFortnite = isFortnite;
        return isFortnite;
    }

    // ---------------------------------------------------------------- cursor
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct CURSORINFO
    {
        public int Size;
        public int Flags;
        public IntPtr Cursor;
        public POINT Pos;
    }

    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO info);
    [DllImport("user32.dll")] static extern bool GetClipCursor(out RECT rect);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);

    /// <summary>True when a game has grabbed the mouse: cursor hidden, or confined to part of the screen.</summary>
    public static bool CursorCaptured()
    {
        var ci = new CURSORINFO { Size = Marshal.SizeOf<CURSORINFO>() };
        if (GetCursorInfo(ref ci) && (ci.Flags & 1 /* CURSOR_SHOWING */) == 0) return true;
        if (GetClipCursor(out var clip))
        {
            int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
            bool full = clip.Left <= vx && clip.Top <= vy && clip.Right >= vx + vw && clip.Bottom >= vy + vh;
            return !full;
        }
        return false;
    }

    // ---------------------------------------------------------------- window styles
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    public static void SetExStyle(IntPtr hwnd, long add, long remove)
    {
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex = (ex | add) & ~remove;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    public static bool HasExStyle(IntPtr hwnd, long style) => (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & style) != 0;

    public static void KeepTopmost(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void UseRoundedDarkFrame(IntPtr hwnd)
    {
        int round = 2, dark = 1;
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
    }
}
