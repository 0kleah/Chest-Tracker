using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChestTracker.Core;

public sealed class ChestLogEntry
{
    [JsonPropertyName("time")] public string Time { get; set; } = "";
    [JsonPropertyName("treasure")] public bool Treasure { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = "auto";
    /// <summary>For chests added in one go ("I've already opened 250"); 0 means a single chest.</summary>
    [JsonPropertyName("count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Count { get; set; }

    /// <summary>For chests read from a replay: "Normal chest", "Sprite chest"...</summary>
    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    /// <summary>For chests read from a replay: true if it was broken open rather than searched.</summary>
    [JsonPropertyName("broken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Broken { get; set; }

    /// <summary>For chests read from a replay: the match it was in.</summary>
    [JsonPropertyName("match")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Match { get; set; }

    /// <summary>Treasure Hours boost for this chest (1.5, 2...). Null means the usual boost when <see cref="Treasure"/> is on.</summary>
    [JsonPropertyName("boost")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Boost { get; set; }

    /// <summary>For chests read from a replay: where the chest was on the island (world units).</summary>
    [JsonPropertyName("x")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? X { get; set; }

    [JsonPropertyName("y")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Y { get; set; }

    [JsonIgnore] public long Chests => Count > 0 ? Count : 1;

    [JsonIgnore]
    public DateTime? When =>
        DateTime.TryParse(Time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t) ? t : null;
}

/// <summary>Same file format as the original Python version, so old counts carry over.</summary>
public sealed class ChestCounts
{
    [JsonPropertyName("normal")] public long Normal { get; set; }
    [JsonPropertyName("treasure")] public long Treasure { get; set; }
    [JsonPropertyName("log")] public List<ChestLogEntry> Log { get; set; } = new();
}

/// <summary>Thread-safe chest count, saved to chest_counts.json after every change.</summary>
public sealed class CountStore
{
    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    readonly object _gate = new();
    readonly string _path;
    ChestCounts _data;

    public CountStore(string path)
    {
        _path = path;
        _data = Load(path);
    }

    public string FilePath => _path;
    public long Normal { get { lock (_gate) return _data.Normal; } }
    public long Treasure { get { lock (_gate) return _data.Treasure; } }
    public long Total { get { lock (_gate) return _data.Normal + _data.Treasure; } }

    public void Add(bool treasure, string source) => Add(treasure, source, 1);

    /// <summary>Adds <paramref name="chests"/> chests as one entry (one Undo takes them all back off).</summary>
    public void Add(bool treasure, string source, long chests)
    {
        if (chests <= 0) return;
        lock (_gate)
        {
            if (treasure) _data.Treasure += chests; else _data.Normal += chests;
            _data.Log.Add(new ChestLogEntry
            {
                Time = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                Treasure = treasure,
                Source = source,
                Count = chests == 1 ? 0 : chests,
            });
            SaveLocked();
        }
    }

    /// <summary>Adds several single chests in one save (a match read from its replay), oldest first.</summary>
    public void AddEntries(IEnumerable<ChestLogEntry> entries)
    {
        lock (_gate)
        {
            foreach (var e in entries)
            {
                e.Count = 0;
                if (e.Treasure) _data.Treasure++; else _data.Normal++;
                _data.Log.Add(e);
            }
            SaveLocked();
        }
    }

    /// <summary>Entries that a match's replay supersedes: screen-detected and hand-added chests (not bulk, not replay).</summary>
    public static bool IsGuess(ChestLogEntry e) => e.Source is not ("replay" or "bulk");

    /// <summary>Is there a guessed (screen or hand-added) chest between two local times?</summary>
    public bool HasGuessesBetween(DateTime fromLocal, DateTime toLocal)
    {
        lock (_gate) return _data.Log.Any(e => IsGuess(e) && e.When is DateTime t && t >= fromLocal && t <= toLocal);
    }

    /// <summary>
    /// A match's replay is the exact record of every chest you opened in it, so it replaces the
    /// guesses made during that match: screen-detected chests and ones added by hand (F7) between two
    /// local times are removed, then the replay's chests are added. Bulk "already opened" entries and
    /// other replays' chests are kept. Returns the entries that were removed.
    /// </summary>
    public List<ChestLogEntry> ReplaceAuto(DateTime fromLocal, DateTime toLocal, IReadOnlyList<ChestLogEntry> replayEntries)
    {
        lock (_gate)
        {
            var removed = _data.Log.Where(e => IsGuess(e) && e.When is DateTime t && t >= fromLocal && t <= toLocal).ToList();
            foreach (var e in removed)
            {
                _data.Log.Remove(e);
                if (e.Treasure) _data.Treasure = Math.Max(0, _data.Treasure - e.Chests);
                else _data.Normal = Math.Max(0, _data.Normal - e.Chests);
            }
            var insertAt = _data.Log.FindIndex(e => e.When is DateTime t && t > toLocal);
            foreach (var e in replayEntries)
            {
                e.Count = 0;
                if (e.Treasure) _data.Treasure++; else _data.Normal++;
            }
            if (insertAt < 0) _data.Log.AddRange(replayEntries); else _data.Log.InsertRange(insertAt, replayEntries);
            SaveLocked();
            return removed;
        }
    }

    /// <summary>A copy of every entry, oldest first (for stats and the map).</summary>
    public List<ChestLogEntry> Snapshot()
    {
        lock (_gate) return _data.Log.ToList();
    }

    /// <summary>
    /// Marks chests opened during a known Treasure Hour (that weren't marked yet) as Treasure Hours chests, with
    /// that hour's boost. Bulk entries have no real time and are left alone. Returns how many changed.
    /// </summary>
    public int ApplyTreasureHours(Func<DateTime, double?> boostAt)
    {
        lock (_gate)
        {
            int changed = 0;
            foreach (var e in _data.Log)
            {
                if (e.Source == "bulk" || e.When is not DateTime t || boostAt(t) is not double boost) continue;
                if (e.Treasure && e.Boost is double b && Math.Abs(b - boost) < 1e-9) continue;
                if (!e.Treasure)
                {
                    e.Treasure = true;
                    _data.Normal = Math.Max(0, _data.Normal - e.Chests);
                    _data.Treasure += e.Chests;
                }
                e.Boost = boost;
                changed++;
            }
            if (changed > 0) SaveLocked();
            return changed;
        }
    }

    /// <summary>Fills in where a match's chests were (from its replay), matched in time order. Returns how many were set.</summary>
    public int SetPositions(string match, IReadOnlyList<(float? X, float? Y)> positionsInOrder)
    {
        lock (_gate)
        {
            var entries = _data.Log.Where(e => e.Match == match && e.Source == "replay").OrderBy(e => e.When).ToList();
            if (entries.Count != positionsInOrder.Count) return 0;
            int set = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (positionsInOrder[i].X is null) continue;
                entries[i].X = positionsInOrder[i].X;
                entries[i].Y = positionsInOrder[i].Y;
                set++;
            }
            if (set > 0) SaveLocked();
            return set;
        }
    }

    /// <summary>Matches whose chests have no position yet.</summary>
    public HashSet<string> MatchesWithoutPositions()
    {
        lock (_gate)
            return _data.Log.Where(e => e.Source == "replay" && e.Match is not null && e.X is null)
                            .Select(e => e.Match!).ToHashSet();
    }

    public static string Stamp(DateTime local) => local.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    public bool Undo()
    {
        lock (_gate)
        {
            if (_data.Log.Count == 0) return false;
            var last = _data.Log[^1];
            _data.Log.RemoveAt(_data.Log.Count - 1);
            if (last.Treasure) _data.Treasure = Math.Max(0, _data.Treasure - last.Chests);
            else _data.Normal = Math.Max(0, _data.Normal - last.Chests);
            SaveLocked();
            return true;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _data = new ChestCounts();
            SaveLocked();
        }
    }

    /// <summary>Most recent chests, newest first, with their running chest number.</summary>
    public List<(long Number, ChestLogEntry Entry)> Recent(int count)
    {
        lock (_gate)
        {
            var result = new List<(long, ChestLogEntry)>();
            long number = _data.Normal + _data.Treasure;
            for (int i = _data.Log.Count - 1; i >= 0 && result.Count < count; i--)
            {
                result.Add((number, _data.Log[i]));
                number -= _data.Log[i].Chests;
            }
            return result;
        }
    }

    /// <summary>Chests opened since a time (chests added in one go as "already opened" don't count).</summary>
    public long CountSince(DateTime since)
    {
        lock (_gate)
            return _data.Log.Where(e => e.Source != "bulk" && e.When is DateTime t && t >= since).Sum(e => e.Chests);
    }

    static ChestCounts Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<ChestCounts>(File.ReadAllText(path)) ?? new ChestCounts();
        }
        catch (Exception) { /* unreadable file: start fresh rather than crash */ }
        return new ChestCounts();
    }

    void SaveLocked()
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_data, JsonOpts));
        File.Move(tmp, _path, overwrite: true);
    }
}
