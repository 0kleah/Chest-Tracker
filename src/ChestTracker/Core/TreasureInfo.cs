using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChestTracker.Core;

/// <summary>One Treasure Hour and how much it boosts each prize (from fortnite.bot, which lists Epic's numbers).</summary>
public sealed class TreasureHour
{
    [JsonPropertyName("startUtc")] public DateTime StartUtc { get; set; }
    [JsonPropertyName("endUtc")] public DateTime EndUtc { get; set; }
    /// <summary>Pirate Shop Access boost (1.5 = 1.5x).</summary>
    [JsonPropertyName("pirate")] public double Pirate { get; set; } = 1.5;
    [JsonPropertyName("gold")] public double Gold { get; set; } = 7.5;
    [JsonPropertyName("vehicle")] public double Vehicle { get; set; } = 10;
}

/// <summary>A prize you can find in a chest, and the chance per chest.</summary>
public sealed class TreasurePrize
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>1 in this many chests.</summary>
    [JsonPropertyName("oneIn")] public double OneIn { get; set; }
    /// <summary>Changes every week (only chests opened that week count towards it).</summary>
    [JsonPropertyName("weekly")] public bool Weekly { get; set; }
    /// <summary>Which Treasure Hours boost applies: "pirate", "gold", "vehicle" or null (no boost).</summary>
    [JsonPropertyName("boost")] public string? Boost { get; set; }
}

public sealed class TreasureWeek
{
    [JsonPropertyName("number")] public int Number { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("startUtc")] public DateTime StartUtc { get; set; }
    [JsonPropertyName("endUtc")] public DateTime EndUtc { get; set; }
    /// <summary>Prize names; null or "?" while Epic hasn't revealed them.</summary>
    [JsonPropertyName("prizes")] public List<string> Prizes { get; set; } = new();
}

/// <summary>Where the island is on the map picture: pixel = center + world * scale (world X across, world Y down).</summary>
public sealed class MapCalibration
{
    [JsonPropertyName("imageUrl")] public string ImageUrl { get; set; } = "https://fortnite-api.com/images/map.png";
    [JsonPropertyName("poisUrl")] public string PoisUrl { get; set; } = "https://fortnite-api.com/v1/map";
    [JsonPropertyName("size")] public double Size { get; set; } = 2048;
    [JsonPropertyName("centerX")] public double CenterX { get; set; } = 978;
    [JsonPropertyName("centerY")] public double CenterY { get; set; } = 1017;
    [JsonPropertyName("scale")] public double Scale { get; set; } = 0.0066;
    /// <summary>Only chests from this time on are on this map (the island changes between seasons).</summary>
    [JsonPropertyName("fromUtc")] public DateTime FromUtc { get; set; }
}

/// <summary>
/// Treasure Hunters info: Treasure Hours, prize odds, weekly prizes and the map. A copy is built in; a newer one
/// is downloaded from this project's GitHub (treasure.json), so it can be kept up to date without a new .exe.
/// </summary>
public sealed class TreasureInfo
{
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("updated")] public string Updated { get; set; } = "";
    [JsonPropertyName("eventStartUtc")] public DateTime EventStartUtc { get; set; }
    [JsonPropertyName("eventEndUtc")] public DateTime EventEndUtc { get; set; }
    [JsonPropertyName("hours")] public List<TreasureHour> Hours { get; set; } = new();
    [JsonPropertyName("prizes")] public List<TreasurePrize> Prizes { get; set; } = new();
    [JsonPropertyName("weeks")] public List<TreasureWeek> Weeks { get; set; } = new();
    [JsonPropertyName("map")] public MapCalibration Map { get; set; } = new();
    [JsonPropertyName("note")] public string? Note { get; set; }

    public const string RemoteUrl = "https://raw.githubusercontent.com/0kleah/Chest-Tracker/main/treasure.json";

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>The Treasure Hour going on at this UTC time, if any.</summary>
    public TreasureHour? HourAt(DateTime utc) => Hours.FirstOrDefault(h => utc >= h.StartUtc && utc < h.EndUtc);

    /// <summary>The next Treasure Hour that hasn't ended yet.</summary>
    public TreasureHour? NextHour(DateTime utc) => Hours.Where(h => h.EndUtc > utc).OrderBy(h => h.StartUtc).FirstOrDefault();

    public TreasureWeek? WeekAt(DateTime utc) => Weeks.FirstOrDefault(w => utc >= w.StartUtc && utc < w.EndUtc);

    public double PirateOneIn => Prizes.FirstOrDefault(p => p.Boost == "pirate")?.OneIn ?? Odds.BaseOdds;

    public static double BoostFor(TreasureHour? h, string? kind) => h is null ? 1 : kind switch
    {
        "pirate" => h.Pirate,
        "gold" => h.Gold,
        "vehicle" => h.Vehicle,
        _ => 1,
    };

    // ------------------------------------------------------------------ loading

    public static TreasureInfo? Parse(string json)
    {
        try
        {
            var info = JsonSerializer.Deserialize<TreasureInfo>(json);
            return info is { Version: > 0 } && info.Prizes.Count > 0 ? info : null;
        }
        catch (Exception) { return null; }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>The newest of the built-in copy and the saved download.</summary>
    public static TreasureInfo Load(string cachePath)
    {
        var builtIn = BuiltIn();
        try
        {
            if (File.Exists(cachePath) && Parse(File.ReadAllText(cachePath)) is { } saved && saved.Version >= builtIn.Version)
                return saved;
        }
        catch (Exception) { }
        return builtIn;
    }

    static DateTime U(int month, int day, int hour) => new(2026, month, day, hour, 0, 0, DateTimeKind.Utc);

    /// <summary>Copied from fortnite.bot/treasure on 13 November 2026.</summary>
    public static TreasureInfo BuiltIn() => new()
    {
        Version = 1,
        Updated = "13 Nov 2026",
        EventStartUtc = U(11, 1, 14),
        EventEndUtc = U(12, 5, 14),
        Hours =
        {
            new() { StartUtc = U(11, 7, 19), EndUtc = U(11, 7, 21), Pirate = 1.5, Gold = 7.5, Vehicle = 10 },
            new() { StartUtc = U(11, 8, 2), EndUtc = U(11, 8, 4), Pirate = 2, Gold = 10, Vehicle = 10 },
            new() { StartUtc = U(11, 14, 19), EndUtc = U(11, 14, 21) },
            new() { StartUtc = U(11, 15, 2), EndUtc = U(11, 15, 4) },
            new() { StartUtc = U(11, 21, 19), EndUtc = U(11, 21, 21) },
            new() { StartUtc = U(11, 22, 2), EndUtc = U(11, 22, 4) },
            new() { StartUtc = U(11, 28, 19), EndUtc = U(11, 28, 21) },
            new() { StartUtc = U(11, 29, 2), EndUtc = U(11, 29, 4) },
        },
        Prizes =
        {
            new() { Name = "Weekly Gold Outfit", OneIn = 1_562_500, Weekly = true, Boost = "gold" },
            new() { Name = "Pirate Shop Access", OneIn = 137_500, Boost = "pirate" },
            new() { Name = "Weekly Vehicle & Decal", OneIn = 7_813, Weekly = true, Boost = "vehicle" },
            new() { Name = "Weekly Pickaxe or Back Bling", OneIn = 568, Weekly = true },
            new() { Name = "Weekly Jam Track", OneIn = 391, Weekly = true },
            new() { Name = "Weekly Loading Screen", OneIn = 272, Weekly = true },
            new() { Name = "Gizmo (any)", OneIn = 127 },
        },
        Weeks =
        {
            new() { Number = 1, Title = "A$AP Arrives", StartUtc = U(11, 1, 14), EndUtc = U(11, 8, 14),
                    Prizes = { "Solid Gold A$AP Rocky (outfit)", "Solid Gold A$AP Rocky's Gauntlets (pickaxe)", "Gold Mercedes-AMG G 63 Decal", "A$AP Rocky Jam Track", "Plunder in Paradise (loading screen)" } },
            new() { Number = 2, Title = "Lil Uzi Arrives", StartUtc = U(11, 8, 14), EndUtc = U(11, 15, 14),
                    Prizes = { "Solid Gold Lil Uzi Vert (outfit)", "Diamond and Gold (back bling)", "Gold Cadillac CT5-V Decal", "Jam Track", "So Kawaii (loading screen)" } },
            new() { Number = 3, Title = "Miku & Teto Arrive", StartUtc = U(11, 15, 14), EndUtc = U(11, 22, 14) },
            new() { Number = 4, Title = "Oscar Arrives", StartUtc = U(11, 22, 14), EndUtc = U(11, 29, 14) },
            new() { Number = 5, Title = "The Final Hunt", StartUtc = U(11, 29, 14), EndUtc = U(12, 5, 14) },
        },
        Map = new MapCalibration { FromUtc = U(11, 1, 14) },
        Note = "Treasure drop rates from chests are constant since season launch and don't vary through the day, except during Treasure Hours. Other than Gizmos, you can't get the same treasure twice. (Epic Games)",
    };
}
