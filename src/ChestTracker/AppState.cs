using System.IO;
using ChestTracker.Core;

namespace ChestTracker;

/// <summary>Shared app state: settings, the saved count, Treasure Hours, and the tracker.</summary>
public sealed class AppState
{
    public static string DataDir { get; } = AppContext.BaseDirectory;
    public static string CountsPath { get; } = Path.Combine(DataDir, "chest_counts.json");
    public static string SettingsPath { get; } = Path.Combine(DataDir, "chest_tracker_settings.json");
    public static string LedgerPath { get; } = Path.Combine(DataDir, "replays_counted.json");

    public AppSettings Settings { get; }
    public CountStore Store { get; }
    public ReplayLedger Ledger { get; }
    public Tracker Tracker { get; }
    public bool TreasureMode { get; private set; }

    // When Treasure Hours was switched on/off (local time), so chests read from a replay after the
    // match get the boost if they were opened while it was on.
    readonly object _treasureGate = new();
    readonly List<(DateTime On, DateTime? Off)> _treasureSpans = new();

    /// <summary>Raised (on any thread) when the count, Treasure Hours, or overlay lock request changes.</summary>
    public event Action? CountsChanged;
    public event Action? TreasureModeChanged;
    public event Action? OverlayLockToggleRequested;

    public AppState()
    {
        Settings = AppSettings.Load(SettingsPath);
        Store = new CountStore(CountsPath);
        Ledger = ReplayLedger.Load(LedgerPath);
        Tracker = new Tracker(this);
    }

    public double Chance => Odds.ChanceAtLeastOne(Store.Normal, Store.Treasure, Settings.Boost);

    public void SaveSettings() => Settings.Save(SettingsPath);

    public void AddChest(string source)
    {
        Store.Add(TreasureMode, source);
        Beep();
        CountsChanged?.Invoke();
    }

    /// <summary>Adds chests you opened before using the tracker (a previous count), in one entry.</summary>
    public void AddAlreadyOpened(long chests, bool treasure = false)
    {
        if (chests <= 0) return;
        Store.Add(treasure, "bulk", chests);
        CountsChanged?.Invoke();
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
    /// taken from the screen-detected entries being replaced.</param>
    public int AddReplayMatch(ReplayResult r, DateTime fallbackStartUtc, bool treasureKnown = true)
    {
        var (from, end) = MatchWindow(r.Record.MatchStartUtc ?? fallbackStartUtc, r.Record.DurationSeconds ?? 1800);

        bool oldTreasure = false;
        if (!treasureKnown)
            oldTreasure = Store.Recent(int.MaxValue).Any(x => x.Entry.Treasure && CountStore.IsGuess(x.Entry)
                                                             && x.Entry.When is DateTime t && t >= from && t <= end);
        var entries = ReplayChests.Entries(r, treasureKnown ? TreasureAt : _ => oldTreasure, fallbackStartUtc);
        Store.ReplaceAuto(from, end, entries);
        if (entries.Count > 0) Beep();
        CountsChanged?.Invoke();
        return entries.Count;
    }

    void Beep()
    {
        if (Settings.Beep)
            Task.Run(() => { try { Console.Beep(1400, 70); } catch (Exception) { } });
    }

    public void Undo()
    {
        if (Store.Undo()) CountsChanged?.Invoke();
    }

    public void ResetCount()
    {
        Store.Reset();
        CountsChanged?.Invoke();
    }

    public void ToggleTreasure() => SetTreasure(!TreasureMode);

    public void SetTreasure(bool on)
    {
        if (TreasureMode == on) return;
        TreasureMode = on;
        lock (_treasureGate)
        {
            if (on) _treasureSpans.Add((DateTime.Now, null));
            else if (_treasureSpans.Count > 0 && _treasureSpans[^1].Off is null)
                _treasureSpans[^1] = (_treasureSpans[^1].On, DateTime.Now);
        }
        TreasureModeChanged?.Invoke();
    }

    /// <summary>Was Treasure Hours switched on at this local time?</summary>
    public bool TreasureAt(DateTime local)
    {
        lock (_treasureGate)
            return _treasureSpans.Any(s => local >= s.On && (s.Off is null || local <= s.Off));
    }

    public void RequestOverlayLockToggle() => OverlayLockToggleRequested?.Invoke();
}
