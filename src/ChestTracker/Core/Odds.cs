namespace ChestTracker.Core;

/// <summary>Pirate Shop Access token odds. Epic's published rate: 1 in 137,500 per chest.</summary>
public static class Odds
{
    public const double BaseOdds = 137_500;
    public const double DefaultBoost = 1.5;   // Saturday Treasure Hours

    public static double PerChest => 1.0 / BaseOdds;

    public static double PerChestBoosted(double boost) => Math.Min(1.0, boost / BaseOdds);

    /// <summary>Chance of at least one token after opening these chests.</summary>
    public static double ChanceAtLeastOne(long normal, long treasure, double boost)
    {
        double pt = PerChestBoosted(boost);
        if (pt >= 1.0 && treasure > 0) return 1.0;
        double logMiss = normal * Math.Log(1 - PerChest) + treasure * Math.Log(1 - pt);
        return -double.ExpM1(logMiss);   // 1 - e^x, precise for tiny chances
    }

    /// <summary>Chests needed to reach a given chance of at least one token.</summary>
    public static long ChestsNeeded(double target, double perChest)
    {
        if (perChest >= 1.0) return 1;
        return (long)Math.Ceiling(Math.Log(1 - target) / Math.Log(1 - perChest));
    }

    public static string Percent(double chance)
    {
        if (chance <= 0) return "0%";
        double pct = chance * 100;
        if (pct < 0.01) return pct.ToString("0.0000") + "%";
        if (pct < 1) return pct.ToString("0.000") + "%";
        if (pct < 10) return pct.ToString("0.00") + "%";
        return pct.ToString("0.0") + "%";
    }

    public static string OneIn(double chance) =>
        chance <= 0 ? "No chests yet" : chance >= 0.5 ? "Better than a coin flip" : $"about 1 in {1 / chance:N0}";
}
