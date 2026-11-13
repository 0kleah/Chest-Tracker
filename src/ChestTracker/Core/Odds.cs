namespace ChestTracker.Core;

/// <summary>
/// Pirate Shop Access token odds. Epic's published rate: 1 in 137,500 per chest, boosted in Treasure Hours.
/// Every chest is its own roll, so the chance of at least one token after n chests is 1 - (1 - p)^n.
/// That number only ever gets close to 100%; no number of chests guarantees a token.
/// </summary>
public static class Odds
{
    public const double BaseOdds = 137_500;
    public const double DefaultBoost = 1.5;   // Saturday Treasure Hours

    public static double PerChest => 1.0 / BaseOdds;

    public static double PerChestBoosted(double boost) => PerChestBoosted(boost, BaseOdds);

    public static double PerChestBoosted(double boost, double oneIn) => Math.Min(1.0, Math.Max(0, boost) / oneIn);

    /// <summary>Chance of at least one token after opening these chests.</summary>
    public static double ChanceAtLeastOne(long normal, long treasure, double boost) =>
        ChanceAtLeastOne(new[] { (normal, 1.0), (treasure, boost) }, BaseOdds);

    /// <summary>
    /// Chance of at least one prize from groups of chests, each group with its own boost (1 = normal,
    /// 1.5 = Treasure Hours...). Worked out in logs so tiny chances stay exact.
    /// </summary>
    public static double ChanceAtLeastOne(IEnumerable<(long Count, double Boost)> groups, double oneIn)
    {
        double logMiss = 0;
        foreach (var (count, boost) in groups)
        {
            if (count <= 0) continue;
            double p = PerChestBoosted(boost, oneIn);
            if (p >= 1.0) return 1.0;
            logMiss += count * double.LogP1(-p);
        }
        return -double.ExpM1(logMiss);   // 1 - e^x, precise for tiny chances
    }

    /// <summary>Chests needed to reach a given chance of at least one token.</summary>
    public static long ChestsNeeded(double target, double perChest)
    {
        if (perChest >= 1.0) return 1;
        return (long)Math.Ceiling(double.LogP1(-target) / double.LogP1(-perChest));
    }

    /// <summary>On average, how many tokens these chests would give (it can be well above 1 and still be 0 for you).</summary>
    public static double Expected(IEnumerable<(long Count, double Boost)> groups, double oneIn) =>
        groups.Sum(g => g.Count * PerChestBoosted(g.Boost, oneIn));

    public static string Percent(double chance)
    {
        if (chance <= 0) return "0%";
        double pct = chance * 100;
        if (pct >= 99.99) return pct >= 100 ? "100%" : "99.99%";   // never round up to a "guaranteed" 100%
        if (pct < 0.01) return pct.ToString("0.0000") + "%";
        if (pct < 1) return pct.ToString("0.000") + "%";
        if (pct < 10) return pct.ToString("0.00") + "%";
        return pct.ToString("0.0") + "%";
    }

    public static string OneIn(double chance) =>
        chance <= 0 ? "No chests yet" : chance >= 0.5 ? "Better than a coin flip (still not guaranteed)" : $"about 1 in {1 / chance:N0}";
}
