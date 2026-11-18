using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ChestTracker;

// Settings → Hotkeys: pick your own keys in place of F6 to F9.
public partial class MainWindow
{
    public sealed record HotkeyRow(Hotkeys.Action Action, string Label, string Key, string Button);

    Hotkeys.Action? _waitingForHotkey;

    void ShowHotkeys()
    {
        var s = _app.Settings;
        HotkeyRow Row(Hotkeys.Action a, string label) =>
            new(a, label, Hotkeys.Name(s, a), _waitingForHotkey == a ? "Press a key…" : "Change");
        HotkeyRows.ItemsSource = new[]
        {
            Row(Hotkeys.Action.Add, "Add a chest it missed"),
            Row(Hotkeys.Action.Undo, "Undo the last chest"),
            Row(Hotkeys.Action.Treasure, "Treasure Hours on or off"),
            Row(Hotkeys.Action.Lock, "Move or lock the overlay"),
        };
        ApplyHotkeyNames();
    }

    /// <summary>Every place in the app that names a hotkey.</summary>
    void ApplyHotkeyNames()
    {
        var s = _app.Settings;
        string N(Hotkeys.Action a) => Hotkeys.Get(s, a) is > 0 and < 256 ? Hotkeys.Name(s, a) : "";
        UndoButton.ToolTip = "Undo the last chest" + Hotkeys.Suffix(s, Hotkeys.Action.Undo);
        AddButton.ToolTip = "Add a chest it missed" + Hotkeys.Suffix(s, Hotkeys.Action.Add);
        OverlayKeyHint.Text = N(Hotkeys.Action.Lock) is { Length: > 0 } lockKey
            ? $"Clicks pass through it · {lockKey} · always on while you play" : "Clicks pass through it · always on while you play";
        var keys = new List<string>();
        if (N(Hotkeys.Action.Lock) is { Length: > 0 } k1) keys.Add($"{k1} move/lock overlay");
        if (N(Hotkeys.Action.Add) is { Length: > 0 } k2) keys.Add($"{k2} add a missed chest");
        if (N(Hotkeys.Action.Undo) is { Length: > 0 } k3) keys.Add($"{k3} undo");
        if (N(Hotkeys.Action.Treasure) is { Length: > 0 } k4) keys.Add($"{k4} Treasure Hours");
        HomeKeysLine.Text = "Chests are added a few seconds after each match ends." + (keys.Count > 0 ? "   " + string.Join("   ·   ", keys) : "");
        AutoTreasureHint.Text = "Turns Treasure Hours on by itself at the times Epic announced, with that hour's boost."
            + (N(Hotkeys.Action.Treasure) is { Length: > 0 } k5 ? $" {k5} still switches it on or off by hand." : "");
        TreasureHint.Text = TreasureHintText();
    }

    string TreasureKey() =>
        Hotkeys.Get(_app.Settings, Hotkeys.Action.Treasure) is > 0 and < 256 ? Hotkeys.Name(_app.Settings, Hotkeys.Action.Treasure) : "";

    void HotkeyChange_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HotkeyRow row } button) return;
        HotkeyProblem.Visibility = Visibility.Collapsed;
        _waitingForHotkey = _waitingForHotkey == row.Action ? null : row.Action;
        ShowHotkeys();
        if (_waitingForHotkey is not null)
            Dispatcher.BeginInvoke(() => FocusRowButton(row.Action), System.Windows.Threading.DispatcherPriority.Input);
    }

    void FocusRowButton(Hotkeys.Action action)
    {
        var item = HotkeyRows.Items.Cast<HotkeyRow>().FirstOrDefault(r => r.Action == action);
        if (item is null || HotkeyRows.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement host) return;
        var button = FindChild<Button>(host);
        button?.Focus();
    }

    static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (c is T t) return t;
            if (FindChild<T>(c) is { } found) return found;
        }
        return null;
    }

    void HotkeyChange_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_waitingForHotkey is not Hotkeys.Action action) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { _waitingForHotkey = null; ShowHotkeys(); return; }
        var s = _app.Settings;
        int vk = key == Key.Back ? 0 : KeyInterop.VirtualKeyFromKey(key);
        if (vk is < 0 or >= 256) return;
        if (vk != 0 && vk == s.MapKey && s.MapOverlay)
        {
            HotkeyProblem.Text = $"{Hotkeys.Name(vk)} is your map key (Routes page). Pick another key.";
            HotkeyProblem.Visibility = Visibility.Visible;
            return;
        }
        if (vk is 0x0D or 0x20 or 0x09 or 0x5B or 0x5C)   // Enter, Space, Tab, Windows keys
        {
            HotkeyProblem.Text = $"{Hotkeys.Name(vk)} can't be a hotkey. Pick another key.";
            HotkeyProblem.Visibility = Visibility.Visible;
            return;
        }
        // A key can only do one thing: if another action had it, that one is left with no key.
        foreach (var other in Enum.GetValues<Hotkeys.Action>())
            if (other != action && vk != 0 && Hotkeys.Get(s, other) == vk)
            {
                Hotkeys.Set(s, other, 0);
                HotkeyProblem.Text = $"{Hotkeys.Name(vk)} was already used, so that one has no key now.";
                HotkeyProblem.Visibility = Visibility.Visible;
            }
        Hotkeys.Set(s, action, vk);
        _waitingForHotkey = null;
        Save();
        ShowHotkeys();
        _app.NotifyHotkeysChanged();
    }

    void HotkeysReset_Click(object sender, RoutedEventArgs e)
    {
        var s = _app.Settings;
        s.HotkeyLock = 0x75; s.HotkeyAdd = 0x76; s.HotkeyUndo = 0x77; s.HotkeyTreasure = 0x78;
        if (Hotkeys.IsOwn(s, s.MapKey)) s.MapKey = 0x4D;
        _waitingForHotkey = null;
        HotkeyProblem.Visibility = Visibility.Collapsed;
        Save();
        ShowHotkeys();
        _app.NotifyHotkeysChanged();
    }
}
