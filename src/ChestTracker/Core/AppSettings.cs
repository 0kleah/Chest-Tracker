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

    // Background / Windows
    /// <summary>Closing the window keeps Chest Tracker counting in the system tray.</summary>
    public bool TrayMode { get; set; }
    public bool StartWithWindows { get; set; }
    /// <summary>A Windows notification 10 minutes before a Treasure Hour, and when it starts.</summary>
    public bool Reminders { get; set; }

    // Squad
    public bool SquadTab { get; set; }
    public bool SquadTracking { get; set; }
    /// <summary>Show "with your squad" (your chests + your teammates') under your count, on Home and the overlay.</summary>
    public bool SquadInTotal { get; set; }

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

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch (Exception) { }
        return new AppSettings();
    }

    public void Save(string path)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts)); }
        catch (IOException) { }
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
