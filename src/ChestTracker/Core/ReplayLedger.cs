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
        /// <summary>Your teammates in this match with the chests and eliminations the replay saw (empty = solo; null = not read).</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<MateSeen>? Mates { get; set; }
    }

    /// <summary>A teammate in a match: name, chests the replay saw them open, and their eliminations.</summary>
    public sealed class MateSeen
    {
        public string Name { get; set; } = "";
        public int Chests { get; set; }
        public int Broken { get; set; }
        public int? Kills { get; set; }
        public bool Bot { get; set; }
        /// <summary>Queued with you in your party (a friend), rather than a random fill.</summary>
        public bool Party { get; set; }
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

    /// <summary>How opening the saved file went (see <see cref="DataFile"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public LoadState LoadState { get; private set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? SetAside { get; private set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? BackupUsed { get; private set; }

    public static ReplayLedger Load(string path)
    {
        var loaded = DataFile.Load(path, json =>
        {
            var l = JsonSerializer.Deserialize<ReplayLedger>(json);
            if (l is null) return null;
            l.Files = new Dictionary<string, Entry>((l.Files ?? new()).Where(kv => kv.Value is not null), StringComparer.OrdinalIgnoreCase);
            return l;
        }, () => new ReplayLedger());
        var ledger = loaded.Value;
        ledger._path = path;
        ledger.LoadState = loaded.State;
        ledger.SetAside = loaded.SetAside;
        ledger.BackupUsed = loaded.BackupUsed;
        return ledger;
    }

    /// <summary>Forgets entries (so their replays are read again). Returns how many.</summary>
    public int Forget(Func<Entry, bool> which)
    {
        lock (_gate)
        {
            var keys = Files.Where(kv => which(kv.Value)).Select(kv => kv.Key).ToList();
            foreach (var k in keys) Files.Remove(k);
            return keys.Count;
        }
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
        // newest match played (an older match added from Settings is written later, but isn't your last match)
        lock (_gate) return Files.Values.Where(e => !e.FromBefore && e.Note is null).OrderByDescending(e => e.MatchStartUtc ?? e.When).FirstOrDefault();
    }

    public void Save()
    {
        if (_path.Length == 0) return;
        string json;
        lock (_gate) json = JsonSerializer.Serialize(this, JsonOpts);
        DataFile.Save(_path, json);
    }
}
