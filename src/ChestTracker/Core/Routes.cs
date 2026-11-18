namespace ChestTracker.Core;

/// <summary>One match's route: its chests in the order you opened them, with where they were.</summary>
public sealed record RouteSummary(string Match, DateTime Start, int Chests, List<ChestLogEntry> Points)
{
    /// <summary>Where the route starts (your first chest), in world units.</summary>
    public (double X, double Y) Landing => (Points[0].X!.Value, Points[0].Y!.Value);
}

/// <summary>Routes of every match on this season's map (only matches whose chests have positions).</summary>
public static class RouteBook
{
    /// <summary>Every match with at least two chests on the map, since <paramref name="fromLocal"/> (this season's island).</summary>
    public static List<RouteSummary> All(IEnumerable<ChestLogEntry> log, DateTime fromLocal) =>
        log.Where(e => e.Source == "replay" && e.Match is not null && e.When is DateTime t && t >= fromLocal)
           .GroupBy(e => e.Match!)
           .Select(g =>
           {
               var all = g.OrderBy(e => e.When).ToList();
               var placed = all.Where(e => e.X is not null && e.Y is not null).ToList();
               return new RouteSummary(g.Key, all[0].When!.Value, all.Count, placed);
           })
           .Where(r => r.Points.Count >= 2)
           .ToList();

    /// <summary>Most chests (ties: the newer match).</summary>
    public static RouteSummary? Best(IEnumerable<RouteSummary> routes) =>
        routes.OrderByDescending(r => r.Chests).ThenByDescending(r => r.Start).FirstOrDefault();

    /// <summary>Fewest chests (ties: the newer match).</summary>
    public static RouteSummary? Worst(IEnumerable<RouteSummary> routes) =>
        routes.OrderBy(r => r.Chests).ThenByDescending(r => r.Start).FirstOrDefault();

    /// <summary>Where a world position is on the map picture (in its pixels).</summary>
    public static (double X, double Y) ToMapPixels(MapCalibration cal, double x, double y, int imageWidth, int imageHeight) =>
        ((cal.CenterX + x * cal.Scale) * imageWidth / cal.Size, (cal.CenterY + y * cal.Scale) * imageHeight / cal.Size);
}
