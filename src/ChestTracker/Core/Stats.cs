namespace ChestTracker.Core;

/// <summary>One counted match, from its replay chests.</summary>
public sealed record MatchStat(string Match, DateTime Start, int Chests, int Broken, int Sprite, float? LandX, float? LandY);

/// <summary>A named place on the island (from the map service).</summary>
public sealed record Poi(string Name, double X, double Y);

/// <summary>Where you landed (your first chest), how often, and how many chests those matches gave.</summary>
public sealed record LandingSpot(string Name, int Matches, double AvgChests, int Best);

/// <summary>Numbers for the Stats page, worked out from the saved chests.</summary>
public sealed class ChestStats
{
    public long Total { get; init; }
    public long Tracked { get; init; }
    public long FromReplays { get; init; }
    public long Bulk { get; init; }
    public long ByHand { get; init; }
    public long Opened { get; init; }
    public long Broken { get; init; }
    public long TreasureHours { get; init; }
    public long Today { get; init; }
    public long ThisWeek { get; init; }
    public List<MatchStat> Matches { get; init; } = new();
    public double PerMatch => Matches.Count == 0 ? 0 : Matches.Average(m => m.Chests);
    public MatchStat? Best => Matches.OrderByDescending(m => m.Chests).ThenByDescending(m => m.Start).FirstOrDefault();
    public List<(string Kind, long Count)> ByKind { get; init; } = new();
    /// <summary>Chests per day, oldest first, ending today.</summary>
    public List<(DateTime Day, long Count)> Days { get; init; } = new();

    public static ChestStats From(IReadOnlyList<ChestLogEntry> log, DateTime now, int days = 14)
    {
        var today = now.Date;
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));   // Monday
        var timed = log.Where(e => e.Source != "bulk").Select(e => (Entry: e, When: e.When)).ToList();
        var replay = log.Where(e => e.Source == "replay").ToList();

        var matches = replay.Where(e => e.Match is not null && e.When is not null)
            .GroupBy(e => e.Match!)
            .Select(g =>
            {
                var ordered = g.OrderBy(e => e.When).ToList();
                var land = ordered.FirstOrDefault(e => e.X is not null);
                return new MatchStat(g.Key, ordered[0].When!.Value, ordered.Count, ordered.Count(e => e.Broken),
                                     ordered.Count(e => e.Kind?.Contains("Sprite", StringComparison.OrdinalIgnoreCase) == true),
                                     land?.X, land?.Y);
            })
            .OrderBy(m => m.Start).ToList();

        var perDay = new List<(DateTime, long)>();
        for (int i = days - 1; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            perDay.Add((d, timed.Where(x => x.When?.Date == d).Sum(x => x.Entry.Chests)));
        }

        return new ChestStats
        {
            Total = log.Sum(e => e.Chests),
            Tracked = timed.Sum(x => x.Entry.Chests),
            FromReplays = replay.Count,
            Bulk = log.Where(e => e.Source == "bulk").Sum(e => e.Chests),
            ByHand = log.Where(e => e.Source == "manual").Sum(e => e.Chests),
            Opened = replay.Count(e => !e.Broken),
            Broken = replay.Count(e => e.Broken),
            TreasureHours = log.Where(e => e.Treasure).Sum(e => e.Chests),
            Today = timed.Where(x => x.When >= today).Sum(x => x.Entry.Chests),
            ThisWeek = timed.Where(x => x.When >= weekStart).Sum(x => x.Entry.Chests),
            Matches = matches,
            ByKind = replay.GroupBy(e => e.Kind ?? "Chest").Select(g => (g.Key, (long)g.Count()))
                           .OrderByDescending(k => k.Item2).ToList(),
            Days = perDay,
        };
    }

    /// <summary>The closest named place to a point (world units).</summary>
    public static Poi? Nearest(IReadOnlyList<Poi> pois, double x, double y) =>
        pois.Count == 0 ? null : pois.MinBy(p => (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));

    /// <summary>Your landing spots (nearest place to the first chest of each match), best average first.</summary>
    public static List<LandingSpot> LandingSpots(IEnumerable<MatchStat> matches, IReadOnlyList<Poi> pois) =>
        matches.Where(m => m.LandX is not null)
            .Select(m => (Match: m, Place: Nearest(pois, m.LandX!.Value, m.LandY!.Value)?.Name))
            .Where(x => x.Place is not null)
            .GroupBy(x => x.Place!)
            .Select(g => new LandingSpot(g.Key, g.Count(), g.Average(x => x.Match.Chests), g.Max(x => x.Match.Chests)))
            .OrderByDescending(s => s.AvgChests).ThenByDescending(s => s.Matches).ToList();
}
