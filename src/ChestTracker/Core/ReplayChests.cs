using System.IO;
using ChestCounter;

namespace ChestTracker.Core;

/// <summary>What one finished match's replay says you did with chests.</summary>
/// <param name="Record">Everything the replay reader found.</param>
/// <param name="Counts">Whether its chests go in your count (a Battle Royale match you played).</param>
/// <param name="WhyNot">Why not, if they don't.</param>
public sealed record ReplayResult(MatchRecord Record, bool Counts, string? WhyNot)
{
    public int Opened => Record.Chests.Count(c => !c.Destroyed);
    public int Broken => Record.Chests.Count(c => c.Destroyed);
}

/// <summary>
/// Reads chests from Fortnite's own replay files. The replay records every container the game
/// sent to you, with its exact type and the server's record of who searched it ("SearchingPawn"),
/// so the count is exact:
///   - a chest counts when YOU opened it, or broke it while it was still unopened (pickaxe, gun or
///     explosive, from any distance the replay saw);
///   - breaking a chest that was already opened doesn't count;
///   - ammo boxes, food boxes and loot cannons are never chests;
///   - a chest refilled by Fortnite and opened again counts again (it's a new search).
/// The reading itself is done by the open-source code in /lib (MIT; see lib/README.md).
/// </summary>
public static class ReplayChests
{
    public static string DemosFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Demos");

    public static ReplayResult Analyze(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var rec = new ReplayAnalyzer().Analyze(fs, path, null);
        string? whyNot = !rec.IsBattleRoyale ? "not a Battle Royale match"
                       : !rec.IsReplayOwner ? "someone else's replay"
                       : null;
        return new ReplayResult(rec, whyNot is null, whyNot);
    }

    /// <summary>Plain-English name for a chest blueprint.</summary>
    public static string FriendlyKind(string blueprint) => ChestKinds.Classify(blueprint) switch
    {
        ChestKinds.Normal => "Normal chest",
        ChestKinds.Rare => "Rare chest",
        ChestKinds.Buried => "Buried chest",
        ChestKinds.Extraction => "Sprite chest",
        ChestKinds.PosterChest => "Sprite poster chest",
        _ => "Chest",
    };

    /// <summary>When a chest in the replay was opened, in local time.</summary>
    public static DateTime LocalTime(MatchRecord rec, ChestRecord chest, DateTime fallbackStartUtc) =>
        (rec.MatchStartUtc ?? fallbackStartUtc).AddSeconds(chest.TimeSeconds).ToLocalTime();

    /// <summary>Count-log entries for a match, oldest first.</summary>
    /// <param name="treasureAt">Was Treasure Hours on at this local time?</param>
    public static List<ChestLogEntry> Entries(ReplayResult r, Func<DateTime, bool> treasureAt, DateTime fallbackStartUtc) =>
        Entries(r, t => treasureAt(t) ? null : 1.0, fallbackStartUtc);

    /// <summary>Count-log entries for a match, oldest first.</summary>
    /// <param name="boostAt">Treasure Hours boost at this local time: 1 = none, null = on with the usual boost.</param>
    public static List<ChestLogEntry> Entries(ReplayResult r, Func<DateTime, double?> boostAt, DateTime fallbackStartUtc)
    {
        if (!r.Counts) return new();
        return r.Record.Chests.OrderBy(c => c.TimeSeconds).Select(c =>
        {
            var when = LocalTime(r.Record, c, fallbackStartUtc);
            double? boost = boostAt(when);
            return new ChestLogEntry
            {
                Time = CountStore.Stamp(when),
                Treasure = boost is null || boost > 1,
                Boost = boost > 1 ? boost : null,
                X = c.X,
                Y = c.Y,
                Source = "replay",
                Kind = FriendlyKind(c.Blueprint),
                Broken = c.Destroyed,
                Match = r.Record.SessionId,
            };
        }).ToList();
    }
}
