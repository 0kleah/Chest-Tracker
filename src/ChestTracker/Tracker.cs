using System.IO;
using ChestTracker.Core;
using ChestTracker.Native;

namespace ChestTracker;

public enum TrackerState
{
    Off,
    /// <summary>Fortnite's replay folder doesn't exist (replays switched off, or Fortnite not installed).</summary>
    NoReplays,
    /// <summary>Watching; no match being played.</summary>
    Waiting,
    /// <summary>A match is being recorded; its chests are added when it ends.</summary>
    InMatch,
    /// <summary>Reading a finished match's replay.</summary>
    Reading,
}

/// <summary>
/// Background work:
///  - polls the hotkeys (F6-F9) 100 times a second;
///  - watches Fortnite's replay folder. When a match ends, Fortnite finishes its replay file, and the
///    chests in it are read and added to the count (see <see cref="ReplayChests"/>).
/// It never looks at the screen and never touches the game itself, only the files Fortnite saves.
/// </summary>
public sealed class Tracker
{
    const int PollMs = 2500;
    readonly AppState _app;
    readonly DateTime _startedUtc = DateTime.UtcNow;
    readonly Dictionary<string, int> _failures = new(StringComparer.OrdinalIgnoreCase);
    readonly FortniteLog _log = new();
    CancellationTokenSource? _cts;
    volatile bool _enrichPending = true;

    /// <summary>Reads older replays again for what's missing (finds, match details, squad). Runs a few at a time.</summary>
    public void RequestEnrich() => _enrichPending = true;

    public bool Running { get; private set; }
    public TrackerState State { get; private set; } = TrackerState.Off;
    public string? Error { get; private set; }
    /// <summary>The most recent match read (counted or not).</summary>
    public ReplayResult? LastMatch { get; private set; }
    public event Action? StatusChanged;
    /// <summary>A match was read and its chests added (count added).</summary>
    public event Action<ReplayResult, int>? MatchCounted;

    /// <summary>
    /// Set when you joined a Battle Royale match and Fortnite didn't start recording a replay (Fortnite's log says
    /// so), which means replays are switched off and the match can't be counted.
    /// </summary>
    public string? ReplayWarning { get; private set; }

    public Tracker(AppState app) => _app = app;

    public void StartInputPolling()
    {
        new Thread(InputLoop) { IsBackground = true, Name = "Hotkeys" }.Start();
    }

    void InputLoop()
    {
        var hotkeys = new (int Vk, Action Run)[]
        {
            (0x75, _app.RequestOverlayLockToggle),   // F6
            (0x76, () => _app.AddChest("manual")),   // F7
            (0x77, _app.Undo),                       // F8
            (0x78, _app.ToggleTreasure),             // F9
        };
        var wasDown = new bool[hotkeys.Length];
        while (true)
        {
            for (int i = 0; i < hotkeys.Length; i++)
            {
                bool d = Win32.KeyDown(hotkeys[i].Vk);
                if (d && !wasDown[i])
                {
                    try { hotkeys[i].Run(); } catch (Exception) { }
                }
                wasDown[i] = d;
            }
            Thread.Sleep(10);
        }
    }

    public void Start()
    {
        if (Running) return;
        _cts = new CancellationTokenSource();
        Running = true;
        Error = null;
        var token = _cts.Token;
        Task.Run(() => WatchLoop(token));
        StatusChanged?.Invoke();
    }

    public void Stop()
    {
        if (!Running) return;
        _cts?.Cancel();
        Running = false;
        SetState(TrackerState.Off);
    }

    void SetState(TrackerState s)
    {
        if (s == State) return;
        State = s;
        StatusChanged?.Invoke();
    }

    async Task WatchLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try { Scan(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* try again next time */ }
                await Task.Delay(PollMs, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Error = e.Message;
            Running = false;
            SetState(TrackerState.Off);
        }
    }

    void Scan()
    {
        CheckFortniteLog();
        var folder = ReplayChests.DemosFolder;
        if (!Directory.Exists(folder)) { SetState(TrackerState.NoReplays); return; }
        var ledger = _app.Ledger;
        var files = new DirectoryInfo(folder).GetFiles("*.replay").OrderBy(f => f.LastWriteTimeUtc).ToList();

        // First run: replays already there are matches from before replay tracking.
        //  - If your count already has (screen-detected or hand-added) chests during one, that match is
        //    part of your count, so its guesses are swapped for the exact chests in its replay.
        //  - Otherwise it isn't counted automatically; Settings can import it.
        if (!ledger.Initialized)
        {
            bool backedUp = false;
            foreach (var f in files)
            {
                if (f.LastWriteTimeUtc >= _startedUtc || ReplayFileInfo.Read(f.FullName) is not { Complete: true } info) continue;
                var started = info.StartedUtc ?? f.CreationTimeUtc;
                var (from, to) = AppState.MatchWindow(started, info.LengthMs / 1000.0);
                if (_app.Store.HasGuessesBetween(from, to))
                {
                    if (!backedUp)
                    {
                        try { File.Copy(AppState.CountsPath, Path.Combine(AppState.DataDir, "chest_counts.before-replays.json"), overwrite: false); }
                        catch (IOException) { }
                        backedUp = true;
                    }
                    SetState(TrackerState.Reading);
                    ReadAndCount(f, info, treasureKnown: false);
                    if (ledger.Has(f.Name)) continue;
                }
                ledger.Set(new ReplayLedger.Entry { File = f.Name, FromBefore = true, MatchStartUtc = started, When = DateTime.Now });
            }
            ledger.Initialized = true;
            ledger.Save();
        }

        bool inMatch = false;
        foreach (var f in files)
        {
            if (ledger.Has(f.Name)) continue;
            var info = ReplayFileInfo.Read(f.FullName);
            if (info is null) continue;
            bool recentlyWritten = DateTime.UtcNow - f.LastWriteTimeUtc < TimeSpan.FromSeconds(4);
            if (!info.Complete)
            {
                // still being recorded (or Fortnite closed mid-match and never finished it)
                if (DateTime.UtcNow - f.LastWriteTimeUtc < TimeSpan.FromMinutes(2)) { inMatch = true; ReplaySeen(); }
                else if (DateTime.UtcNow - f.LastWriteTimeUtc > TimeSpan.FromHours(1))
                {
                    ledger.Set(new ReplayLedger.Entry { File = f.Name, Note = "match never finished (Fortnite closed?)", When = DateTime.Now });
                    ledger.Save();
                }
                continue;
            }
            if (recentlyWritten) { inMatch = true; continue; }   // let Fortnite finish writing

            SetState(TrackerState.Reading);
            ReadAndCount(f, info);
        }
        SetState(inMatch ? TrackerState.InMatch : TrackerState.Waiting);

        if (_enrichPending && !inMatch) _enrichPending = EnrichOlder(maxReplays: 8);
    }

    // ------------------------------------------------------------------ replays switched off?

    void CheckFortniteLog()
    {
        _log.Poll(DateTime.UtcNow);
        string? warning = _log.LastMatchNotRecorded
            ? "Fortnite isn't saving a replay of your match, so its chests can't be counted. In Fortnite: Settings → Game → Replays → Record Battle Royale Replays: On."
            : null;
        if (warning != ReplayWarning)
        {
            ReplayWarning = warning;
            StatusChanged?.Invoke();
        }
    }

    void ReplaySeen()
    {
        _log.ReplaySeen();
        if (ReplayWarning is not null) { ReplayWarning = null; StatusChanged?.Invoke(); }
    }

    // ------------------------------------------------------------------ details from replays (map, finds, matches, squad)

    /// <summary>
    /// Reads older replays Fortnite still has, for anything not saved yet: chest positions (map), what you found
    /// (Finds), placement and eliminations (Matches) and, if squad tracking is on, your teammates. A few replays per
    /// pass so new matches are never held up. Returns true while there is more to do.
    /// </summary>
    bool EnrichOlder(int maxReplays)
    {
        bool squad = _app.Settings.SquadTracking;
        var todo = _app.Ledger.All()
            .Where(e => !e.DetailsTried || (squad && !e.SquadTried))
            .OrderByDescending(e => e.MatchStartUtc ?? DateTime.MinValue)
            .ToList();
        if (todo.Count == 0) return false;
        bool changed = false;
        int read = 0;
        foreach (var e in todo)
        {
            bool needSquad = squad && !e.SquadTried;
            var path = Path.Combine(ReplayChests.DemosFolder, e.File);
            bool readable = File.Exists(path) && (e.Note is null || !e.Note.StartsWith("couldn't read", StringComparison.Ordinal))
                            && e.Note is not ("not a Battle Royale match" or "someone else's replay");
            if (readable)
            {
                if (read >= maxReplays) break;
                read++;
                try
                {
                    var r = ReplayChests.Analyze(path);
                    ApplyDetails(e, r, details: !e.DetailsTried, squad: needSquad);
                    changed = true;
                }
                catch (Exception) { }
            }
            e.DetailsTried = true;
            if (needSquad) e.SquadTried = true;
        }
        _app.Ledger.Save();
        Memory.Trim();
        if (changed) _app.NotifyCountsChanged();
        return todo.Count > read && read >= maxReplays;
    }

    /// <summary>Saves what a replay says beyond the chest count: positions, finds, placement, teammates.</summary>
    void ApplyDetails(ReplayLedger.Entry entry, ReplayResult r, bool details, bool squad)
    {
        var rec = r.Record;
        if (details)
        {
            entry.Placement = rec.Placement;
            entry.Players = rec.TotalPlayers;
            entry.Eliminations = rec.Eliminations;
            entry.DurationSeconds = rec.DurationSeconds;
            entry.Playlist = rec.Playlist;
            entry.MatchStartUtc ??= rec.MatchStartUtc;
            if (r.Counts && !string.IsNullOrEmpty(rec.SessionId))
                _app.Store.SetPositions(rec.SessionId, rec.Chests.OrderBy(c => c.TimeSeconds).Select(c => (c.X, c.Y)).ToList(), onlyMissing: true);
            if (rec.IsBattleRoyale && rec.IsReplayOwner) _app.Finds.AddFromReplay(rec);
        }
        if (squad && rec.IsBattleRoyale && rec.IsReplayOwner) _app.Squad.AddMatch(rec);
    }

    void ReadAndCount(FileInfo f, ReplayFileInfo info, bool treasureKnown = true)
    {
        var ledger = _app.Ledger;
        ReplayResult r;
        try { r = ReplayChests.Analyze(f.FullName); }
        catch (Exception e)
        {
            int n = _failures.GetValueOrDefault(f.Name) + 1;
            _failures[f.Name] = n;
            if (n >= 3)   // give up on this one after a few tries, and say why
            {
                ledger.Set(new ReplayLedger.Entry { File = f.Name, Note = "couldn't read: " + e.Message, When = DateTime.Now });
                ledger.Save();
                Error = $"Couldn't read the replay {f.Name}. A Fortnite update may have changed the replay format.";
                StatusChanged?.Invoke();
            }
            return;
        }

        var rec = r.Record;
        var entry = new ReplayLedger.Entry
        {
            File = f.Name, SessionId = rec.SessionId, MatchStartUtc = rec.MatchStartUtc ?? info.StartedUtc,
            Opened = r.Opened, Broken = r.Broken, AmmoBoxes = rec.AmmoBoxesOpened, When = DateTime.Now,
        };
        if (!r.Counts) entry.Note = r.WhyNot;
        else if (!string.IsNullOrEmpty(rec.SessionId) && ledger.SessionCounted(rec.SessionId)) entry.Note = "match already counted";

        int added = 0;
        if (entry.Note is null)
        {
            added = _app.AddReplayMatch(r, info.StartedUtc ?? f.CreationTimeUtc, treasureKnown);
            entry.ChestsCounted = added;
        }
        entry.PositionsTried = true;
        try { ApplyDetails(entry, r, details: true, squad: _app.Settings.SquadTracking); } catch (Exception) { }
        entry.DetailsTried = true;
        entry.SquadTried = _app.Settings.SquadTracking;
        ledger.Set(entry);
        ledger.Save();
        Error = null;
        LastMatch = r;
        ReplaySeen();
        Memory.Trim();   // reading a replay uses a lot of memory for a moment; give it back
        MatchCounted?.Invoke(r, added);
        StatusChanged?.Invoke();
    }

    /// <summary>Older replays (from before the tracker) that haven't been counted, with their chests.</summary>
    public async Task<List<(string File, ReplayResult Result)>> ReadOlderReplaysAsync(IProgress<(int Done, int Total)>? progress = null)
    {
        var older = _app.Ledger.All().Where(e => e.FromBefore).Select(e => e.File).ToList();
        var results = new List<(string, ReplayResult)>();
        int done = 0;
        foreach (var name in older)
        {
            var path = Path.Combine(ReplayChests.DemosFolder, name);
            if (File.Exists(path))
            {
                try { results.Add((name, await Task.Run(() => ReplayChests.Analyze(path)))); }
                catch (Exception) { }
            }
            progress?.Report((++done, older.Count));
        }
        return results;
    }

    /// <summary>Adds the chests of older replays (chosen by the user after <see cref="ReadOlderReplaysAsync"/>).</summary>
    public int ImportOlder(IEnumerable<(string File, ReplayResult Result)> replays)
    {
        int total = 0;
        foreach (var (file, r) in replays.OrderBy(x => x.Result.Record.MatchStartUtc))
        {
            var rec = r.Record;
            var entry = new ReplayLedger.Entry
            {
                File = file, SessionId = rec.SessionId, MatchStartUtc = rec.MatchStartUtc,
                Opened = r.Opened, Broken = r.Broken, AmmoBoxes = rec.AmmoBoxesOpened, When = DateTime.Now,
            };
            if (!r.Counts) entry.Note = r.WhyNot;
            else if (!string.IsNullOrEmpty(rec.SessionId) && _app.Ledger.SessionCounted(rec.SessionId)) entry.Note = "match already counted";
            else
            {
                entry.ChestsCounted = _app.AddReplayMatch(r, rec.MatchStartUtc ?? DateTime.UtcNow, treasureKnown: false);
                total += entry.ChestsCounted;
            }
            _app.Ledger.Set(entry);
        }
        _app.Ledger.Save();
        StatusChanged?.Invoke();
        return total;
    }
}
