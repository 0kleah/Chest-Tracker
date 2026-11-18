using System.Runtime.InteropServices;
using ChestTracker.Native;

/// <summary>The monitors on this PC (for checking the screen capture on each one).</summary>
static class Monitors
{
    delegate bool EnumProc(IntPtr monitor, IntPtr dc, ref Win32.RECT rect, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    /// <summary>Same as the app: real pixels on every monitor, whatever its Windows scaling.</summary>
    public static void UseRealPixels() => SetProcessDpiAwarenessContext(new IntPtr(-4));

    public static List<Win32.RECT> All()
    {
        var list = new List<Win32.RECT>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref Win32.RECT r, IntPtr _) => { list.Add(r); return true; }, IntPtr.Zero);
        return list;
    }
}
