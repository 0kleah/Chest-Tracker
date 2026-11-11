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
