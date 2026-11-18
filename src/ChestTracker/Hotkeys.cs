using System.Windows.Input;
using ChestTracker.Core;

namespace ChestTracker;

/// <summary>Chest Tracker's own keys (F6 to F9 unless you changed them) and their names for the screen.</summary>
public static class Hotkeys
{
    public enum Action { Lock, Add, Undo, Treasure }

    public static int Get(AppSettings s, Action a) => a switch
    {
        Action.Lock => s.HotkeyLock,
        Action.Add => s.HotkeyAdd,
        Action.Undo => s.HotkeyUndo,
        _ => s.HotkeyTreasure,
    };

    public static void Set(AppSettings s, Action a, int vk)
    {
        switch (a)
        {
            case Action.Lock: s.HotkeyLock = vk; break;
            case Action.Add: s.HotkeyAdd = vk; break;
            case Action.Undo: s.HotkeyUndo = vk; break;
            default: s.HotkeyTreasure = vk; break;
        }
    }

    /// <summary>Is this key one of Chest Tracker's own (so it can't also be the map key)?</summary>
    public static bool IsOwn(AppSettings s, int vk) =>
        vk != 0 && (vk == s.HotkeyLock || vk == s.HotkeyAdd || vk == s.HotkeyUndo || vk == s.HotkeyTreasure);

    /// <summary>"F7", "Num 5", "[" ... or "no key".</summary>
    public static string Name(int vk)
    {
        if (vk is <= 0 or >= 256) return "no key";
        var key = KeyInterop.KeyFromVirtualKey(vk);
        return key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (key - Key.NumPad0),
            Key.Tab => "Tab", Key.CapsLock => "Caps Lock", Key.OemComma => ",", Key.OemPeriod => ".",
            Key.OemQuestion => "/", Key.OemSemicolon => ";", Key.OemQuotes => "'", Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]",
            Key.OemMinus => "-", Key.OemPlus => "=", Key.OemTilde => "`", Key.OemPipe => "\\", Key.OemBackslash => "\\",
            Key.Multiply => "Num *", Key.Add => "Num +", Key.Subtract => "Num -", Key.Divide => "Num /", Key.Decimal => "Num .",
            Key.PageUp => "Page Up", Key.PageDown => "Page Down", Key.Insert => "Insert", Key.Delete => "Delete", Key.Home => "Home", Key.End => "End",
            Key.Scroll => "Scroll Lock", Key.Pause => "Pause",
            Key.LeftShift or Key.RightShift => "Shift", Key.LeftCtrl or Key.RightCtrl => "Ctrl", Key.LeftAlt or Key.RightAlt => "Alt",
            _ => key.ToString(),
        };
    }

    public static string Name(AppSettings s, Action a) => Name(Get(s, a));

    /// <summary>" (F7)" after a menu item or tooltip, or nothing when the key is switched off.</summary>
    public static string Suffix(AppSettings s, Action a) => Get(s, a) is > 0 and < 256 ? $" ({Name(s, a)})" : "";
}
