using System.IO;
using System.Text.Json;

namespace ChestTracker.Core;

public sealed class AppSettings
{
    // Tracking (chests are read from Fortnite's replays after each match)
    public bool AutoStart { get; set; } = true;

    // Odds / feedback
    public double Boost { get; set; } = Odds.DefaultBoost;
    public bool Beep { get; set; } = true;
    /// <summary>Switch Treasure Hours on by itself at the times Epic announced (F9 still works).</summary>
    public bool AutoTreasure { get; set; } = true;
    /// <summary>Look for a newer Chest Tracker on GitHub (only reads the release name).</summary>
    public bool CheckUpdates { get; set; } = true;
    /// <summary>Show a short summary on the overlay when a match is counted.</summary>
    public bool MatchSummary { get; set; } = true;
    /// <summary>The match summary also lists each teammate's chests.</summary>
    public bool SummaryTeammates { get; set; } = true;

    // Background / Windows
    /// <summary>Closing the window keeps Chest Tracker counting in the system tray.</summary>
    public bool TrayMode { get; set; }
    public bool StartWithWindows { get; set; }
    /// <summary>The overlay comes up when Fortnite starts and goes away after it closes (waits in the tray in between).</summary>
    public bool FollowFortnite { get; set; }
    /// <summary>A Windows notification 10 minutes before a Treasure Hour, and when it starts.</summary>
    public bool Reminders { get; set; }

    // Squad
    public bool SquadTab { get; set; }
    public bool SquadTracking { get; set; }
    /// <summary>Show "with your squad" (your chests + your teammates') under your count, on Home and the overlay.</summary>
    public bool SquadInTotal { get; set; }
    /// <summary>Matches page: show each teammate's chests and eliminations for the picked match.</summary>
    public bool MatchTeammates { get; set; } = true;
    /// <summary>Layers on a match's map (Matches page).</summary>
    public bool LayerPath { get; set; } = true;
    public bool LayerBus { get; set; } = true;
    public bool LayerStorm { get; set; } = true;
    public bool LayerMarks { get; set; } = true;
    public bool LayerMates { get; set; } = true;
    public bool LayerOthers { get; set; } = true;
    /// <summary>The match map zooms in on where you went (off: the whole island).</summary>
    public bool MatchCloseUp { get; set; } = true;
    /// <summary>Draw a route over the Fortnite map when you press your map key. Off until you switch it on: it's the
    /// one feature that looks at the screen (only while the in-game map is open).</summary>
    public bool MapOverlay { get; set; }
    /// <summary>Your Fortnite map key (Windows key code; 0x4D = M).</summary>
    public int MapKey { get; set; } = 0x4D;
    /// <summary>Controller button that opens the map (XInput bit; 0 = none). View (Back) is Fortnite's default.</summary>
    public int MapPadButton { get; set; } = 0x0020;
    /// <summary>Chest Tracker's own keys (virtual-key codes; 0 = no key). F6 to F9 by default.</summary>
    public int HotkeyLock { get; set; } = 0x75;
    public int HotkeyAdd { get; set; } = 0x76;
    public int HotkeyUndo { get; set; } = 0x77;
    public int HotkeyTreasure { get; set; } = 0x78;
    /// <summary>The match whose route is drawn on the Fortnite map (null = your best route).</summary>
    public string? MapOverlayMatch { get; set; }

    // Overlay content and size
    public double OverlayScale { get; set; } = 1.0;
    public bool OverlayCompact { get; set; }
    public bool OverlayShowCount { get; set; } = true;
    public bool OverlayShowChance { get; set; } = true;
    public bool OverlayShowOneIn { get; set; } = true;
    public bool OverlayShowSession { get; set; } = true;
    public bool OverlayShowToday { get; set; }
    public bool OverlayShowStatus { get; set; } = true;
    public bool OverlayShowTreasure { get; set; } = true;
    public bool OverlayShowNextHour { get; set; }
    public bool OverlayShowHint { get; set; } = true;

    // Colours ("#RRGGBB")
    public string AccentColor { get; set; } = Palette.DefaultAccent;
    public string BackgroundColor { get; set; } = Palette.DefaultBackground;
    public string CardColor { get; set; } = Palette.DefaultCard;
    public string TextColor { get; set; } = Palette.DefaultText;
    public string OverlayColor { get; set; } = Palette.DefaultOverlay;
    public string OverlayTextColor { get; set; } = Palette.DefaultText;

    // Overlay
    public bool OverlayEnabled { get; set; } = true;
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    public bool OverlayLocked { get; set; }
    public double OverlayOpacity { get; set; } = 0.92;

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Loaded<AppSettings> Load(string path) =>
        DataFile.Load(path, json => JsonSerializer.Deserialize<AppSettings>(json)?.Checked(), () => new AppSettings());

    /// <summary>Puts values a hand-edited or damaged file could hold back into range.</summary>
    AppSettings Checked()
    {
        if (!(Boost > 0) || double.IsInfinity(Boost)) Boost = Odds.DefaultBoost;
        if (!(OverlayScale > 0) || double.IsInfinity(OverlayScale)) OverlayScale = 1.0;
        if (!(OverlayOpacity > 0) || double.IsNaN(OverlayOpacity)) OverlayOpacity = 0.92;
        OverlayOpacity = Math.Clamp(OverlayOpacity, 0.3, 1.0);
        if (MapKey is <= 0 or >= 256) MapKey = 0x4D;
        if (MapPadButton is < 0 or > 0xFFFF || (MapPadButton & (MapPadButton - 1)) != 0) MapPadButton = 0x0020;
        if (HotkeyLock is < 0 or >= 256) HotkeyLock = 0x75;
        if (HotkeyAdd is < 0 or >= 256) HotkeyAdd = 0x76;
        if (HotkeyUndo is < 0 or >= 256) HotkeyUndo = 0x77;
        if (HotkeyTreasure is < 0 or >= 256) HotkeyTreasure = 0x78;
        AccentColor ??= Palette.DefaultAccent;
        BackgroundColor ??= Palette.DefaultBackground;
        CardColor ??= Palette.DefaultCard;
        TextColor ??= Palette.DefaultText;
        OverlayColor ??= Palette.DefaultOverlay;
        OverlayTextColor ??= Palette.DefaultText;
        return this;
    }

    readonly object _saveGate = new();

    public void Save(string path)
    {
        lock (_saveGate) DataFile.Save(path, JsonSerializer.Serialize(this, JsonOpts));
    }
}

/// <summary>Default colours (the original gold-on-midnight look).</summary>
public static class Palette
{
    public const string DefaultAccent = "#FFCD50";
    public const string DefaultBackground = "#0B0E14";
    public const string DefaultCard = "#131822";
    public const string DefaultText = "#EEF1F6";
    public const string DefaultOverlay = "#151B28";
}
