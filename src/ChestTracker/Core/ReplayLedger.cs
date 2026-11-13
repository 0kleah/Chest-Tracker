using System.IO;
using System.Text.Json;

namespace ChestTracker.Core;

/// <summary>
/// Which replay files have already been dealt with, so a match is never counted twice (not even if
/// Fortnite renames the replay when you save it, since matches are also remembered by session id).
/// </summary>
public sealed class ReplayLedger
{
    public sealed class Entry
    {
        public string File { get; set; } = "";
        public string? SessionId { get; set; }
        public DateTime? MatchStartUtc { get; set; }
        /// <summary>Chests added to the count from this replay.</summary>
        public int ChestsCounted { get; set; }
        public int Opened { get; set; }
        public int Broken { get; set; }
        public int AmmoBoxes { get; set; }
        /// <summary>Was there when the tracker first started: not counted unless imported.</summary>
        public bool FromBefore { get; set; }
        /// <summary>Why nothing was counted ("not Battle Royale", "couldn't read"...), if so.</summary>
        public string? Note { get; set; }
        public DateTime When { get; set; }
        /// <summary>Chest positions (for the map) were saved, or looked for, from this replay.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public bool PositionsTried { get; set; }
        // Match details for the Matches page (from the replay)
        public int? Placement { get; set; }
        public int? Players { get; set; }
        public int? Eliminations { get; set; }
        public int? DurationSeconds { get; set; }
        public string? Playlist { get; set; }
        /// <summary>Details, finds and positions were read (or looked for) from this replay.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public bool DetailsTried { get; set; }
        /// <summary>Teammates from this replay were added to squad totals (or looked for).</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public bool SquadTried { get; set; }
        /// <summary>Which version of the details pass read this replay (re-read once when it goes up).</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public int DetailsVersion { get; set; }
        /// <summary>Golden chest prizes the replay saw anyone get in this match.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<PrizeSeen>? Prizes { get; set; }
    }

    /// <summary>A golden chest prize seen in a match: who got it ("me", "teammate", "other") and what.</summary>
    public sealed class PrizeSeen
    {
        public string Who { get; set; } = "other";
        public string Player { get; set; } = "";
        public string PrizeId { get; set; } = "";
        public float TimeSeconds { get; set; }
    }

    public bool Initialized { get; set; }
    public Dictionary<string, Entry> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    readonly object _gate = new();
    string _path = "";

    public static ReplayLedger Load(string path)
    {
        ReplayLedger ledger;
        try
        {
            ledger = File.Exists(path) ? JsonSerializer.Deserialize<ReplayLedger>(File.ReadAllText(path)) ?? new() : new();
            ledger.Files = new Dictionary<string, Entry>(ledger.Files, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception) { ledger = new(); }
        ledger._path = path;
        return ledger;
    }

    public bool Has(string file) { lock (_gate) return Files.ContainsKey(file); }

    public bool SessionCounted(string sessionId)
    {
        lock (_gate) return Files.Values.Any(e => e.SessionId == sessionId && !e.FromBefore && e.Note is null);
    }

    public void Set(Entry e) { lock (_gate) Files[e.File] = e; }

    public Entry? Get(string file) { lock (_gate) return Files.TryGetValue(file, out var e) ? e : null; }

    public List<Entry> All() { lock (_gate) return Files.Values.ToList(); }

    public Entry? LastCounted()
    {
        lock (_gate) return Files.Values.Where(e => !e.FromBefore && e.Note is null).OrderByDescending(e => e.When).FirstOrDefault();
    }

    public void Save()
    {
        if (_path.Length == 0) return;
        try
        {
            string json;
            lock (_gate) json = JsonSerializer.Serialize(this, JsonOpts);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (IOException) { }
    }
}
