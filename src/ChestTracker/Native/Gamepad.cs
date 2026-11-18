using System.Runtime.InteropServices;

namespace ChestTracker.Native;

/// <summary>
/// Which buttons are held on Xbox-style controllers, through Windows' own XInput (the same thing games use to read a
/// controller). Nothing is read from Fortnite. PlayStation controllers only show up here when something like Steam
/// presents them as an Xbox controller.
/// </summary>
public static class Gamepad
{
    public const int Back = 0x0020;   // "View" on Xbox controllers: Fortnite's map button
    public const int Start = 0x0010;
    public const int B = 0x2000;      // closes the map in Fortnite

    [StructLayout(LayoutKind.Sequential)]
    struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    static extern int GetState14(int index, out XInputState state);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    static extern int GetState910(int index, out XInputState state);

    static bool _use910, _none;
    static readonly bool[] Connected = new bool[4];
    static long _nextScan;

    static int GetState(int index, out XInputState state)
    {
        state = default;
        if (_none) return -1;
        try { return _use910 ? GetState910(index, out state) : GetState14(index, out state); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            if (!_use910) { _use910 = true; return GetState(index, out state); }
            _none = true;
            return -1;
        }
    }

    /// <summary>
    /// Buttons held on any connected controller (bits as in XInput). Asking about a controller that isn't plugged in is
    /// slow, so empty slots are only checked again every two seconds.
    /// </summary>
    public static int Held()
    {
        long now = Environment.TickCount64;
        bool scan = now >= _nextScan;
        if (scan) _nextScan = now + 2000;
        int held = 0;
        for (int i = 0; i < 4; i++)
        {
            if (!Connected[i] && !scan) continue;
            Connected[i] = GetState(i, out var st) == 0;
            if (Connected[i]) held |= st.Gamepad.Buttons;
        }
        return held;
    }

    public static bool AnyConnected => Connected.Any(c => c);

    /// <summary>The buttons held at the last check (read by Settings while you pick a button).</summary>
    public static volatile int LastHeld;

    /// <summary>Settings is waiting for you to press a button: check controllers even if the map button is off.</summary>
    public static volatile bool Listening;

    /// <summary>"View", "A", "D-pad up"... for the screen.</summary>
    public static string Name(int button) => button switch
    {
        0 => "no button",
        0x0001 => "D-pad up", 0x0002 => "D-pad down", 0x0004 => "D-pad left", 0x0008 => "D-pad right",
        0x0010 => "Menu (Start)", 0x0020 => "View (Back)", 0x0040 => "left stick click", 0x0080 => "right stick click",
        0x0100 => "LB", 0x0200 => "RB", 0x1000 => "A", 0x2000 => "B", 0x4000 => "X", 0x8000 => "Y",
        _ => "a button",
    };
}
