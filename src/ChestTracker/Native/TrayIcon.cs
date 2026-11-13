using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;

namespace ChestTracker.Native;

/// <summary>
/// A small icon in the Windows notification area (system tray), straight through the Windows API so no extra
/// library is needed. Left-click opens Chest Tracker, right-click shows a menu. It can also show a Windows
/// notification (used for Treasure Hour reminders).
/// </summary>
public sealed class TrayIcon : IDisposable
{
    const int WM_APP_TRAY = 0x8000 + 77;
    const int WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_LBUTTONDBLCLK = 0x0203;
    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
    const uint NIIF_USER = 4, NIIF_LARGE_ICON = 0x20;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr ExtractIcon(IntPtr hInst, string file, int index);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

    readonly HwndSource _window;
    readonly IntPtr _icon;
    bool _added;

    public event Action? Clicked;
    public event Action? MenuRequested;

    public TrayIcon(string tooltip)
    {
        _window = new HwndSource(new HwndSourceParameters("ChestTrackerTray") { Width = 0, Height = 0, WindowStyle = 0 });
        _window.AddHook(WndProc);
        _icon = ExtractIcon(IntPtr.Zero, Environment.ProcessPath ?? "", 0);
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        data.szTip = tooltip;
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
        data.uVersion = 4;
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    public IntPtr Handle => _window.Handle;

    NOTIFYICONDATA Data(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_APP_TRAY,
        hIcon = _icon,
        szTip = "", szInfo = "", szInfoTitle = "",
    };

    public void SetTooltip(string text)
    {
        if (!_added) return;
        var data = Data(NIF_TIP);
        data.szTip = text.Length > 127 ? text[..127] : text;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    /// <summary>Shows a Windows notification from the tray icon.</summary>
    public void Notify(string title, string text)
    {
        if (!_added) return;
        var data = Data(NIF_INFO);
        data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        data.szInfo = text.Length > 255 ? text[..255] : text;
        data.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON;
        data.hBalloonIcon = _icon;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    /// <summary>Lets a popup menu close when you click elsewhere (Windows needs the owner in front).</summary>
    public void PrepareMenu() => SetForegroundWindow(_window.Handle);

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_APP_TRAY)
        {
            int ev = (int)(lParam.ToInt64() & 0xFFFF);
            if (ev is WM_LBUTTONUP or WM_LBUTTONDBLCLK) Clicked?.Invoke();
            else if (ev == WM_RBUTTONUP) MenuRequested?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data(0);
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        _window.Dispose();
    }
}

/// <summary>Start with Windows: one entry under your user's "Run" key, pointing at this .exe (removed when switched off).</summary>
public static class WindowsStartup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "ChestTracker";

    public static bool IsOn()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string v && v.Contains(Environment.ProcessPath ?? "\0", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    public static bool Set(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue(Name, $"\"{Environment.ProcessPath}\" --tray");
            else key.DeleteValue(Name, throwOnMissingValue: false);
            return true;
        }
        catch (Exception) { return false; }
    }
}
