using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChestCounter;

namespace ChestTracker.Core;

/// <summary>
/// Everything a match's map can show besides your chests: where you went, the bus, the storm, your knocks and
/// eliminations, your teammates' chests and chests other players opened near you. Read from the replay once and kept
/// in a small file per match (the replay itself is deleted by Fortnite after a while). Positions are world units.
/// </summary>
public sealed class MatchMap
{
    public const int CurrentVersion = 1;
    public int V { get; set; } = CurrentVersion;
    /// <summary>Your path: [seconds, x, y] about once a second, starting where you jumped from the bus.</summary>
    public List<float[]> Path { get; set; } = new();
    /// <summary>The bus route as [x1, y1, x2, y2], pointing the way it flew. Null when the replay didn't show it clearly.</summary>
    public float[]? Bus { get; set; }
    /// <summary>Storm circles in order: [x, y, radius].</summary>
    public List<float[]> Storm { get; set; } = new();
    public List<Mark> Marks { get; set; } = new();
    public List<Mate> Mates { get; set; } = new();
    /// <summary>Chests other players (not your team) opened near your path: [seconds, x, y, 1 if broken open].</summary>
    public List<float[]> Others { get; set; } = new();

    public sealed class Mark
    {
        /// <summary>"knock" or "elim" (you did it), "knocked" or "died" (it happened to you).</summary>
        public string Kind { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Other { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Bot { get; set; }
    }

    public sealed class Mate
    {
        public string Name { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Party { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Bot { get; set; }
        /// <summary>[seconds, x, y, 1 if broken open], only chests with a known position.</summary>
        public List<float[]> Chests { get; set; } = new();
    }

    public bool HasAnything => Path.Count > 0 || Storm.Count > 0 || Marks.Count > 0 || Mates.Any(m => m.Chests.Count > 0) || Others.Count > 0;

    public static MatchMap From(MatchRecord rec) => new()
    {
        Path = rec.Path.Select(p => new[] { p.T, p.X, p.Y }).ToList(),
        Bus = rec.BusLine,
        Storm = rec.Storm.Select(z => new[] { MathF.Round(z.X), MathF.Round(z.Y), MathF.Round(z.Radius) }).ToList(),
        Marks = rec.Marks.Select(m => new Mark { Kind = m.Kind, X = m.X, Y = m.Y, Other = string.IsNullOrWhiteSpace(m.Other) ? null : m.Other, Bot = m.OtherIsBot }).ToList(),
        Mates = rec.Teammates.Where(t => t.Chests.Any(c => c.X is not null)).Select(t => new Mate
        {
            Name = string.IsNullOrWhiteSpace(t.Name) ? "Teammate" : t.Name, Party = t.InMyParty, Bot = t.IsBot,
            Chests = t.Chests.Where(c => c.X is not null).Select(c => new[] { c.TimeSeconds, MathF.Round(c.X!.Value), MathF.Round(c.Y!.Value), c.Destroyed ? 1f : 0f }).ToList(),
        }).ToList(),
        Others = rec.OthersChests.Select(c => new[] { c.T, c.X, c.Y, c.Destroyed ? 1f : 0f }).ToList(),
    };
}

/// <summary>The match map files, in the "match_maps" folder next to the .exe (one small file per match).</summary>
public sealed class MatchMaps
{
    static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };
    readonly string _folder;
    readonly object _gate = new();
    // The last few read, so flicking between matches doesn't hit the disk each time.
    readonly Dictionary<string, MatchMap?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public MatchMaps(string folder) => _folder = folder;

    public string Folder => _folder;

    static string Safe(string match) => string.Concat(match.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

    string PathOf(string match) => System.IO.Path.Combine(_folder, Safe(match) + ".json");

    public bool Has(string match)
    {
        try { return File.Exists(PathOf(match)); } catch (Exception) { return false; }
    }

    public void Save(string match, MatchMap map)
    {
        if (string.IsNullOrEmpty(match)) return;
        try
        {
            Directory.CreateDirectory(_folder);
            string path = PathOf(match), tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(map, Json));
            File.Move(tmp, path, overwrite: true);
            lock (_gate) _cache[match] = map;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // only a nice-to-have: the replay is still there for next time
    }

    /// <summary>The map of a match, or null if it was never read (or its file can't be read).</summary>
    public MatchMap? Load(string match)
    {
        if (string.IsNullOrEmpty(match)) return null;
        lock (_gate) if (_cache.TryGetValue(match, out var cached)) return cached;
        MatchMap? map = null;
        try
        {
            var path = PathOf(match);
            if (File.Exists(path)) map = JsonSerializer.Deserialize<MatchMap>(File.ReadAllText(path), Json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { map = null; }
        lock (_gate)
        {
            if (_cache.Count > 40) _cache.Clear();
            _cache[match] = map;
        }
        return map;
    }

    /// <summary>Every saved match map (for the Squad page and future totals). Unreadable files are skipped.</summary>
    public IEnumerable<string> Matches()
    {
        try { return Directory.Exists(_folder) ? Directory.GetFiles(_folder, "*.json").Select(f => System.IO.Path.GetFileNameWithoutExtension(f)!).ToList() : new List<string>(); }
        catch (Exception) { return new List<string>(); }
    }

    public void Forget(string match)
    {
        try { File.Delete(PathOf(match)); } catch (Exception) { }
        lock (_gate) _cache.Remove(match);
    }
}
