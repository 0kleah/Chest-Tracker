using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChestCounter;

namespace ChestTracker.Core;

/// <summary>Someone you've played with, and what the replays saw them open.</summary>
public sealed class Squadmate
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("epicId")] public string? EpicId { get; set; }
    [JsonPropertyName("bot")] public bool Bot { get; set; }
    [JsonPropertyName("matches")] public int Matches { get; set; }
    [JsonPropertyName("chests")] public int Chests { get; set; }
    [JsonPropertyName("broken")] public int Broken { get; set; }
    /// <summary>Chests by type ("Normal chest": 12...).</summary>
    [JsonPropertyName("kinds")] public Dictionary<string, int> Kinds { get; set; } = new();
    /// <summary>Prizes they found while playing with you ("Treasure gizmo · 13 Nov").</summary>
    [JsonPropertyName("finds")] public List<string> Finds { get; set; } = new();
    [JsonPropertyName("lastSeenUtc")] public DateTime? LastSeenUtc { get; set; }
}

/// <summary>
/// Squad totals, saved in squad.json. Only chests your own replay saw are counted: a teammate who is far away from you
/// is out of the replay's range, so their real total is usually higher.
/// </summary>
public sealed class SquadStore
{
    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    readonly object _gate = new();
    readonly string _path;
    Data _data;

    sealed class Data
    {
        [JsonPropertyName("people")] public List<Squadmate> People { get; set; } = new();
        [JsonPropertyName("matches")] public HashSet<string> Matches { get; set; } = new();
    }

    public SquadStore(string path)
    {
        _path = path;
        try { _data = File.Exists(path) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception) { _data = new(); }
    }

    public List<Squadmate> All()
    {
        lock (_gate) return _data.People.OrderByDescending(p => p.Chests).ThenByDescending(p => p.LastSeenUtc).ToList();
    }

    public bool HasMatch(string match) { lock (_gate) return _data.Matches.Contains(match); }

    /// <summary>Adds a match's teammates (once per match). Returns true if it was new.</summary>
    public bool AddMatch(MatchRecord rec)
    {
        if (string.IsNullOrEmpty(rec.SessionId)) return false;
        lock (_gate)
        {
            if (!_data.Matches.Add(rec.SessionId)) return false;
            foreach (var mate in rec.Teammates)
            {
                if (string.IsNullOrWhiteSpace(mate.Name) && mate.EpicId is null) continue;
                var p = _data.People.FirstOrDefault(x => (mate.EpicId is not null && x.EpicId == mate.EpicId)
                                                         || (!string.IsNullOrEmpty(mate.Name) && string.Equals(x.Name, mate.Name, StringComparison.OrdinalIgnoreCase)));
                if (p is null) { p = new Squadmate { EpicId = mate.EpicId, Bot = mate.IsBot }; _data.People.Add(p); }
                if (!string.IsNullOrWhiteSpace(mate.Name)) p.Name = mate.Name;   // keep the latest name
                p.EpicId ??= mate.EpicId;
                p.Matches++;
                p.Chests += mate.Chests.Count;
                p.Broken += mate.Chests.Count(c => c.Destroyed);
                foreach (var c in mate.Chests)
                {
                    var kind = ReplayChests.FriendlyKind(c.Blueprint);
                    p.Kinds[kind] = p.Kinds.GetValueOrDefault(kind) + 1;
                }
                var when = rec.MatchStartUtc;
                if (when > p.LastSeenUtc || p.LastSeenUtc is null) p.LastSeenUtc = when;
                foreach (var f in rec.TeamTreasureFinds.Where(f => !string.IsNullOrEmpty(f.Teammate)
                                                                   && string.Equals(f.Teammate, mate.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    var (name, _, _) = FindNames.Describe(f);
                    p.Finds.Add($"{name} · {(when?.AddSeconds(f.ObtainedAtSeconds ?? 0).ToLocalTime().ToString("d MMM") ?? "")}");
                }
            }
            Save();
            return true;
        }
    }

    public void Forget(string name)
    {
        lock (_gate) { _data.People.RemoveAll(p => p.Name == name); Save(); }
    }

    public void Clear()
    {
        lock (_gate) { _data = new Data(); Save(); }
    }

    void Save()
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_data, JsonOpts));
        File.Move(tmp, _path, overwrite: true);
    }
}
