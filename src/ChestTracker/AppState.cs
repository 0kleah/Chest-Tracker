using System.IO;
using ChestTracker.Core;

namespace ChestTracker;

/// <summary>A prize's odds and your chance of it from the chests that count towards it.</summary>
public sealed record PrizeChance(TreasurePrize Prize, long Chests, double Chance);

/// <summary>Shared app state: settings, the saved count, Treasure Hours, online info, and the tracker.</summary>
public sealed class AppState
{
    public static string DataDir { get; } = AppContext.BaseDirectory;
    public static string CountsPath { get; } = Path.Combine(DataDir, "chest_counts.json");
    public static string SettingsPath { get; } = Path.Combine(DataDir, "chest_tracker_settings.json");
    public static string LedgerPath { get; } = Path.Combine(DataDir, "replays_counted.json");
    public static string TreasureInfoPath { get; } = Path.Combine(DataDir, "treasure_info.json");
    public static string MapImagePath { get; } = Path.Combine(DataDir, "map.png");
    public static string MapPoisPath { get; } = Path.Combine(DataDir, "map_places.json");
    public static string FindsPath { get; } = Path.Combine(DataDir, "finds.json");
    public static string SquadPath { get; } = Path.Combine(DataDir, "squad.json");

    public AppSettings Settings { get; }
    public CountStore Store { get; }
    public ReplayLedger Ledger { get; }
    public Tracker Tracker { get; }
    public TreasureInfo Treasure { get; private set; }
    public FindStore Finds { get; }
    public SquadStore Squad { get; }

    // Treasure Hours switched on/off by hand (F9), and auto Treasure Hours switched off by hand, in local time.
    readonly object _treasureGate = new();
    readonly List<(DateTime On, DateTime? Off)> _manualSpans = new();
    readonly List<(DateTime From, DateTime To)> _autoOff = new();
    bool _manualOn;

    // worked out once per change, not on every overlay tick
    volatile Totals _totals = new(0, 0, 0, 0, 0, 0, 0);
    sealed record Totals(double Chance, double Expected, long EventChests, long Boosted, long Total, long SessionChests, int SessionMatches);
    DateTime? _sessionStart;

    /// <summary>Raised (on any thread) when the count, Treasure Hours, or overlay lock request changes.</summary>
    public event Action? CountsChanged;
    public event Action? TreasureModeChanged;
    public event Action? OverlayLockToggleRequested;
    /// <summary>Update check, Treasure info, status or map places changed.</summary>
    public event Action? OnlineChanged;

    public ReleaseInfo? NewerRelease { get; private set; }
    public FortniteStatus? Status { get; private set; }
    public List<Poi> Places { get; private set; } = new();

    public AppState()
    {
        Settings = AppSettings.Load(SettingsPath);
        Store = new CountStore(CountsPath);
        Ledger = ReplayLedger.Load(LedgerPath);
        Finds = new FindStore(FindsPath);
        Squad = new SquadStore(SquadPath);
        if (Squad.NeedsRebuild)
        {
            // squad lists from 3.2 have no party info: read the saved replays again (friend choices are kept)
            Squad.Rebuild();
            foreach (var e in Ledger.All()) e.SquadTried = false;
            Ledger.Save();
        }
        Treasure = TreasureInfo.Load(TreasureInfoPath);
        Places = LoadPlaces();
        ApplyTreasureSchedule();
        Recalc();
        Tracker = new Tracker(this);
    }

    // ------------------------------------------------------------------ odds

    /// <summary>Chance of at least one Pirate Shop Access token from the chests that count (this Treasure Hunt).</summary>
    public double Chance => _totals.Chance;
    /// <summary>Tokens you'd get on average from these chests. Above 1 doesn't mean you'll have one.</summary>
    public double ExpectedTokens => _totals.Expected;
    /// <summary>Chests that count towards the token (opened since the Treasure Hunt started, plus your previous count).</summary>
    public long EventChests => _totals.EventChests;
    /// <summary>Of those, chests opened in Treasure Hours.</summary>
    public long BoostedChests => _totals.Boosted;

    /// <summary>The boost a chest gets for one kind of prize ("pirate", "gold", "vehicle" or null).</summary>
    public double BoostOf(ChestLogEntry e, string? kind)
    {
        if (!e.Treasure || kind is null) return 1;
        var hour = e.When is DateTime t ? Treasure.HourAt(t.ToUniversalTime()) : null;
        if (kind == "pirate") return e.Boost ?? hour?.Pirate ?? Settings.Boost;
        return hour is not null ? TreasureInfo.BoostFor(hour, kind)
             : TreasureInfo.BoostFor(Treasure.Hours.LastOrDefault() ?? new TreasureHour(), kind);
    }

    bool CountsForEvent(ChestLogEntry e) =>
        e.Source == "bulk" || e.When is not DateTime t || t.ToUniversalTime() >= Treasure.EventStartUtc;

    // ------------------------------------------------------------------ session (since Fortnite was opened)

    /// <summary>When Fortnite was last opened: it starts a fresh log file each time (no need to touch the game).</summary>
    static DateTime? FortniteOpenedAt() => FortniteLog.OpenedAt();

    /// <summary>Start of this session: when you last opened Fortnite (or today, if it hasn't run on this PC).</summary>
    public DateTime SessionStart => _sessionStart ?? DateTime.Today;
    public long SessionChests => _totals.SessionChests;

    /// <summary>The squad line is on: squad tab, squad tracking and "add to my total" all switched on.</summary>
    public bool ShowSquadTotal => Settings.SquadTab && Settings.SquadTracking && Settings.SquadInTotal;
    /// <summary>Your chests plus your teammates' (only what your replays saw of theirs).</summary>
    public long TotalWithSquad => Store.Total + Squad.TotalChests;
    public int SessionMatches => _totals.SessionMatches;

    void Recalc()
    {
        _sessionStart = FortniteOpenedAt();
        var log = Store.Snapshot();
        var groups = log.Where(CountsForEvent).GroupBy(e => BoostOf(e, "pirate"))
                        .Select(g => (Count: g.Sum(e => e.Chests), Boost: g.Key)).ToList();
        double oneIn = Treasure.PirateOneIn;
        var start = SessionStart;
        var session = log.Where(e => e.Source != "bulk" && e.When >= start).ToList();
        _totals = new Totals(Odds.ChanceAtLeastOne(groups, oneIn), Odds.Expected(groups, oneIn),
                             groups.Sum(g => g.Count), groups.Where(g => g.Boost > 1).Sum(g => g.Count), log.Sum(e => e.Chests),
                             session.Sum(e => e.Chests), session.Where(e => e.Match is not null).Select(e => e.Match).Distinct().Count());
    }

    /// <summary>Your chance of each prize: weekly prizes from this week's chests, the rest from this Treasure Hunt's.</summary>
    public List<PrizeChance> PrizeChances(DateTime nowUtc)
    {
        var log = Store.Snapshot().Where(CountsForEvent).ToList();
        var week = Treasure.WeekAt(nowUtc);
        var result = new List<PrizeChance>();
        foreach (var prize in Treasure.Prizes)
        {
            var chests = prize.Weekly
                ? log.Where(e => week is not null && e.Source != "bulk" && e.When is DateTime t && t.ToUniversalTime() >= week.StartUtc && t.ToUniversalTime() < week.EndUtc)
                : log;
            var groups = chests.GroupBy(e => BoostOf(e, prize.Boost)).Select(g => (g.Sum(e => e.Chests), g.Key)).ToList();
            result.Add(new PrizeChance(prize, groups.Sum(g => g.Item1), Odds.ChanceAtLeastOne(groups, prize.OneIn)));
        }
        return result;
    }

    public void SaveSettings() => Settings.Save(SettingsPath);

    public void NotifyCountsChanged() => Changed();

    void Changed()
    {
        Recalc();
        CountsChanged?.Invoke();
    }

    public void AddChest(string source)
    {
        Store.Add(TreasureMode, source);
        if (TreasureMode) ApplyTreasureSchedule();
        Beep();
        Changed();
    }

    /// <summary>Adds chests you opened before using the tracker (a previous count), in one entry.</summary>
    public void AddAlreadyOpened(long chests, bool treasure = false)
    {
        if (chests <= 0) return;
        Store.Add(treasure, "bulk", chests);
        Changed();
    }

    /// <summary>The span of local time a match covers, with a little slack either side.</summary>
    public static (DateTime From, DateTime To) MatchWindow(DateTime startUtc, double durationSeconds)
    {
        var start = startUtc.ToLocalTime();
        return (start.AddSeconds(-60), start.AddSeconds(durationSeconds + 90));
    }

    /// <summary>
    /// Adds a finished match's chests from its replay, replacing any guesses (screen-detected or
    /// added by hand) from the same match. Returns how many chests were added.
    /// </summary>
    /// <param name="treasureKnown">False for old replays from before the tracker: Treasure Hours is then
    /// taken from the entries being replaced (and from the announced Treasure Hours).</param>
    public int AddReplayMatch(ReplayResult r, DateTime fallbackStartUtc, bool treasureKnown = true)
    {
        var (from, end) = MatchWindow(r.Record.MatchStartUtc ?? fallbackStartUtc, r.Record.DurationSeconds ?? 1800);

        bool oldTreasure = false;
        if (!treasureKnown)
            oldTreasure = Store.Recent(int.MaxValue).Any(x => x.Entry.Treasure && CountStore.IsGuess(x.Entry)
                                                             && x.Entry.When is DateTime t && t >= from && t <= end);
        var entries = ReplayChests.Entries(r, treasureKnown ? BoostAt : t => oldTreasure ? null : ScheduledBoostAt(t) ?? 1.0, fallbackStartUtc);
        Store.ReplaceAuto(from, end, entries);
        if (entries.Count > 0) Beep();
        Changed();
        return entries.Count;
    }

    void Beep()
    {
        if (Settings.Beep)
            Task.Run(() => { try { Console.Beep(1400, 70); } catch (Exception) { } });
    }

    public void Undo()
    {
        if (Store.Undo()) Changed();
    }

    public void ResetCount()
    {
        Store.Reset();
        Changed();
    }

    // ------------------------------------------------------------------ Treasure Hours

    /// <summary>The announced Treasure Hour going on now (when automatic Treasure Hours is on and not switched off).</summary>
    public TreasureHour? AutoHourNow
    {
        get
        {
            if (!Settings.AutoTreasure) return null;
            var hour = Treasure.HourAt(DateTime.UtcNow);
            return hour is not null && !AutoSwitchedOff(DateTime.Now) ? hour : null;
        }
    }

    /// <summary>Treasure Hours is on right now (switched on by hand, or automatically).</summary>
    public bool TreasureMode => _manualOn || AutoHourNow is not null;

    /// <summary>The boost chests get right now.</summary>
    public double CurrentBoost => AutoHourNow?.Pirate ?? Settings.Boost;

    bool AutoSwitchedOff(DateTime local)
    {
        lock (_treasureGate) return _autoOff.Any(s => local >= s.From && local < s.To);
    }

    double? ScheduledBoostAt(DateTime local)
    {
        if (!Settings.AutoTreasure || AutoSwitchedOff(local)) return null;
        return Treasure.HourAt(local.ToUniversalTime())?.Pirate;
    }

    /// <summary>Treasure Hours boost for a chest opened at this local time: 1 = off, null = on with the usual boost.</summary>
    public double? BoostAt(DateTime local)
    {
        if (ScheduledBoostAt(local) is double b) return b;
        lock (_treasureGate)
            return _manualSpans.Any(s => local >= s.On && (s.Off is null || local <= s.Off)) ? null : 1.0;
    }

    /// <summary>Was Treasure Hours on at this local time?</summary>
    public bool TreasureAt(DateTime local) => BoostAt(local) is not 1.0;

    /// <summary>F9: switches Treasure Hours off if it's on (even an automatic one), on if it's off.</summary>
    public void ToggleTreasure() => SetTreasure(!TreasureMode);

    public void SetTreasure(bool on)
    {
        if (TreasureMode == on) return;
        var now = DateTime.Now;
        lock (_treasureGate)
        {
            if (on)
            {
                _manualOn = true;
                _manualSpans.Add((now, null));
                _autoOff.RemoveAll(s => s.To > now);
            }
            else
            {
                _manualOn = false;
                if (_manualSpans.Count > 0 && _manualSpans[^1].Off is null)
                    _manualSpans[^1] = (_manualSpans[^1].On, now);
                if (Settings.AutoTreasure && Treasure.HourAt(DateTime.UtcNow) is { } hour)
                    _autoOff.Add((now, hour.EndUtc.ToLocalTime()));
            }
        }
        TreasureModeChanged?.Invoke();
    }

    /// <summary>Called every second: lets the window and overlay know when an automatic Treasure Hour starts or ends.</summary>
    bool _lastMode;
    int _ticks;
    public void TickTreasure()
    {
        // a new Fortnite session (game reopened) starts a new session count
        if (++_ticks % 15 == 0 && FortniteOpenedAt() != _sessionStart) Changed();
        bool mode = TreasureMode;
        if (mode == _lastMode) return;
        _lastMode = mode;
        TreasureModeChanged?.Invoke();
    }

    /// <summary>Chests opened during an announced Treasure Hour get its boost, even ones counted before it was known.</summary>
    void ApplyTreasureSchedule()
    {
        if (!Settings.AutoTreasure) return;
        Store.ApplyTreasureHours(t => AutoSwitchedOff(t) ? null : Treasure.HourAt(t.ToUniversalTime())?.Pirate);
    }

    public void SetAutoTreasure(bool on)
    {
        Settings.AutoTreasure = on;
        SaveSettings();
        ApplyTreasureSchedule();
        Changed();
        TreasureModeChanged?.Invoke();
    }

    public void RequestOverlayLockToggle() => OverlayLockToggleRequested?.Invoke();

    // ------------------------------------------------------------------ online (update check, Treasure info, status, map)

    /// <summary>Checks for a newer Chest Tracker and newer Treasure info. Safe to call any time; failures are ignored.</summary>
    public async Task RefreshOnlineAsync()
    {
        if (Settings.CheckUpdates)
        {
            var latest = await Online.LatestReleaseAsync().ConfigureAwait(false);
            var current = typeof(AppState).Assembly.GetName().Version;
            NewerRelease = latest is not null && Online.IsNewer(latest.Version, current) ? latest : null;
        }
        else NewerRelease = null;

        var json = await Online.GetStringAsync(TreasureInfo.RemoteUrl).ConfigureAwait(false);
        if (json is not null && TreasureInfo.Parse(json) is { } info && info.Version > Treasure.Version)
        {
            Treasure = info;
            try { File.WriteAllText(TreasureInfoPath, json); } catch (IOException) { }
            ApplyTreasureSchedule();
            Changed();
        }

        if (Places.Count == 0 || File.GetLastWriteTimeUtc(MapPoisPath) < DateTime.UtcNow.AddDays(-1))
        {
            var places = await Online.GetStringAsync(Treasure.Map.PoisUrl).ConfigureAwait(false);
            if (places is not null && Online.ParsePois(places) is { Count: > 0 } list)
            {
                Places = list;
                try { File.WriteAllText(MapPoisPath, places); } catch (IOException) { }
            }
        }
        OnlineChanged?.Invoke();
    }

    public async Task RefreshStatusAsync()
    {
        var status = await Online.StatusAsync().ConfigureAwait(false);
        if (status is not null) Status = status;
        OnlineChanged?.Invoke();
    }

    /// <summary>The map picture, downloaded once and kept next to the app.</summary>
    public async Task<string?> MapImageAsync()
    {
        if (File.Exists(MapImagePath) && File.GetLastWriteTimeUtc(MapImagePath) >= Treasure.Map.FromUtc && new FileInfo(MapImagePath).Length > 10_000)
            return MapImagePath;
        return await Online.DownloadAsync(Treasure.Map.ImageUrl, MapImagePath).ConfigureAwait(false) ? MapImagePath
             : File.Exists(MapImagePath) ? MapImagePath : null;
    }

    static List<Poi> LoadPlaces()
    {
        try { return File.Exists(MapPoisPath) ? Online.ParsePois(File.ReadAllText(MapPoisPath)) : new(); }
        catch (IOException) { return new(); }
    }
}
