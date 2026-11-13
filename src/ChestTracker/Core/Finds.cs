using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChestCounter;

namespace ChestTracker.Core;

/// <summary>A Treasure Hunters prize you found (read from a replay, or added by hand).</summary>
public sealed class FoundItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>Gizmo, Outfit, Pickaxe, Back Bling, Car Decal, Jam Track, Loading Screen, Pirate Shop Access, Other.</summary>
    [JsonPropertyName("category")] public string Category { get; set; } = "Other";
    [JsonPropertyName("whenUtc")] public DateTime? WhenUtc { get; set; }
    /// <summary>"replay" (found by the tracker) or "manual" (added by you).</summary>
    [JsonPropertyName("source")] public string Source { get; set; } = "manual";
    [JsonPropertyName("match")] public string? Match { get; set; }
    /// <summary>The prize's own id in the replay (golden chest prizes), or a key made from its time.</summary>
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("chest")] public string? Chest { get; set; }
    [JsonPropertyName("broken")] public bool Broken { get; set; }
    /// <summary>The name isn't certain (the replay only hints at it). You can change it.</summary>
    [JsonPropertyName("unsure")] public bool Unsure { get; set; }
    /// <summary>For "before I used the tracker" finds: which week (optional).</summary>
    [JsonPropertyName("week")] public int? Week { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
}

/// <summary>Your finds, saved in finds.json next to the app.</summary>
public sealed class FindStore
{
    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    readonly object _gate = new();
    readonly string _path;
    List<FoundItem> _items;

    public FindStore(string path)
    {
        _path = path;
        try { _items = File.Exists(path) ? JsonSerializer.Deserialize<List<FoundItem>>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception) { _items = new(); }
    }

    public List<FoundItem> All() { lock (_gate) return _items.OrderByDescending(i => i.WhenUtc ?? DateTime.MinValue).ToList(); }

    public bool HasKey(string match, string key) { lock (_gate) return _items.Any(i => i.Match == match && i.Key == key); }

    public void Add(FoundItem item) { lock (_gate) { _items.Add(item); Save(); } }

    /// <summary>Adds what a replay says you found (once per prize). Returns how many were new.</summary>
    public int AddFromReplay(MatchRecord rec)
    {
        int added = 0;
        lock (_gate)
        {
            foreach (var f in rec.TreasureFinds)
            {
                string key = f.PrizeId ?? $"{f.RawItem}@{f.ObtainedAtSeconds:0.0}";
                if (_items.Any(i => i.Match == rec.SessionId && i.Key == key)) continue;
                var (name, category, unsure) = FindNames.Describe(f);
                _items.Add(new FoundItem
                {
                    Name = name, Category = category, Unsure = unsure, Source = "replay", Match = rec.SessionId, Key = key,
                    WhenUtc = rec.MatchStartUtc?.AddSeconds(f.ObtainedAtSeconds ?? 0),
                    Chest = ReplayChests.FriendlyKind(f.SourceBlueprint), Broken = f.SourceDestroyed,
                });
                added++;
            }
            if (added > 0) Save();
        }
        return added;
    }

    public void Update(string id, string name, string category)
    {
        lock (_gate)
        {
            if (_items.FirstOrDefault(i => i.Id == id) is not { } item) return;
            item.Name = name;
            item.Category = category;
            item.Unsure = false;
            Save();
        }
    }

    public void Remove(string id) { lock (_gate) { _items.RemoveAll(i => i.Id == id); Save(); } }

    void Save()
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_items, JsonOpts));
        File.Move(tmp, _path, overwrite: true);
    }
}

/// <summary>Plain-English names for what the replay says you found, and the list you can pick from by hand.</summary>
public static class FindNames
{
    public static readonly string[] Gizmos = { "Extraction Accelerator", "Portable Extractor", "Sprite Locator", "Lucky Locator", "Golden Firework" };
    public static readonly string[] Categories = { "Pirate Shop Access", "Outfit", "Pickaxe", "Back Bling", "Car Decal", "Jam Track", "Loading Screen", "Gizmo", "Other" };

    public static (string Name, string Category, bool Unsure) Describe(TreasureFind f)
    {
        string raw = (f.RawItem ?? "").ToLowerInvariant();
        if (raw.Contains("flameceleb")) return ("Golden Firework", "Gizmo", false);
        if (raw.Contains("relicextractor")) return ("Portable Extractor", "Gizmo", false);
        if (raw.Contains("relicfinder")) return ("Sprite Locator", "Gizmo", false);
        if (raw.Contains("luckylocator")) return ("Lucky Locator", "Gizmo", false);
        if (raw.Contains("smugglerextractionkey") || raw.Contains("extractionaccelerat")) return ("Extraction Accelerator", "Gizmo", false);

        string id = (f.PrizeId ?? "").ToLowerInvariant();
        if (id.StartsWith("delilahprize:", StringComparison.Ordinal))
        {
            // Golden chest prizes carry an id. The ones seen so far (week 1):
            return id switch
            {
                "delilahprize:dpid_item1" => ("Solid Gold A$AP Rocky's Gauntlets (pickaxe)", "Pickaxe", true),
                "delilahprize:dpid_item2" => ("Plunder in Paradise (loading screen)", "Loading Screen", false),
                "delilahprize:dpid_item3" => ("Gold Mercedes-AMG G 63 Decal", "Car Decal", true),
                "delilahprize:dpid_item4" => ("No Trespassing (Jam Track)", "Jam Track", false),
                _ => ("Golden chest prize", "Other", true),
            };
        }
        // A gizmo whose exact type isn't in the replay.
        return ("Treasure gizmo", "Gizmo", true);
    }

    /// <summary>What you can pick when adding a find by hand: this event's prizes, the gizmos and the token.</summary>
    public static List<(string Name, string Category, int? Week)> Catalog(TreasureInfo info)
    {
        var list = new List<(string, string, int?)> { ("Pirate Shop Access", "Pirate Shop Access", null) };
        foreach (var w in info.Weeks)
            foreach (var p in w.Prizes.Where(p => !string.IsNullOrWhiteSpace(p) && p != "?"))
                list.Add((p, CategoryOf(p), w.Number));
        foreach (var g in Gizmos) list.Add((g, "Gizmo", null));
        return list;
    }

    public static string CategoryOf(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("pirate shop") ? "Pirate Shop Access"
             : n.Contains("outfit") ? "Outfit"
             : n.Contains("pickaxe") || n.Contains("gauntlet") ? "Pickaxe"
             : n.Contains("back bling") ? "Back Bling"
             : n.Contains("decal") || n.Contains("vehicle") ? "Car Decal"
             : n.Contains("jam track") ? "Jam Track"
             : n.Contains("loading screen") ? "Loading Screen"
             : Gizmos.Any(g => g.Equals(name, StringComparison.OrdinalIgnoreCase)) || n.Contains("gizmo") ? "Gizmo"
             : "Other";
    }
}
