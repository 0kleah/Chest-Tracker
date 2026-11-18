using System.IO;
using System.Diagnostics;
using System.Text.Json;
using ChestTracker.Core;

// `--treasure-json <file>`: write the built-in Treasure Hunters info (the repo's treasure.json starts from this).
if (args.Length >= 2 && args[0] == "--treasure-json")
{
    File.WriteAllText(args[1], TreasureInfo.BuiltIn().ToJson() + Environment.NewLine);
    return 0;
}

// `--glide-debug`: frame by frame, a still map with wobbly positions (to see where any shake comes from)
if (args.Length >= 1 && args[0] == "--glide-debug")
{
    long Tk(double seconds) => (long)(seconds * RouteMotion.TicksPerSecond);
    var rng = new Random(5);
    double Nz(double sd) => sd * Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
    var mo = new RouteMotion();
    mo.SetRoute(1300, 1000, 700);
    int nf = 0;
    double? prevShown = null;
    double Zm(double t) => t < 0.3 ? 1 : t > 0.7 ? 2.5 : Math.Exp(Math.Log(2.5) * (3 * Math.Pow((t - 0.3) / 0.4, 2) - 2 * Math.Pow((t - 0.3) / 0.4, 3)));
    for (double t = 0; t < 1.1; t += 1.0 / 40)
    {
        string fixes = "";
        while (nf / 45.0 + 0.018 <= t)
        {
            double tc = nf / 45.0, k = Zm(tc);
            double s = 0.6 * k * Math.Exp(Nz(0.0002)), x = 900 - 700 * k + Nz(0.35), y = 500 - 600 * k + Nz(0.35);
            mo.Target(s, x, y, Tk(tc), Tk(tc + 0.018));
            fixes += $" fix({s * 1400 + x:0.0})";
            nf++;
        }
        mo.Step(Tk(t));
        double shown = mo.S * 1400 + mo.X, truth = 900 + 140 * Zm(t);
        if (t > 0.6 && t < 0.86) Console.WriteLine($"t={t:0.000} shown {shown:0.0} truth {truth:0.0} off {shown - truth:+0.0;-0.0}{fixes}");
        prevShown = shown;
    }
    return 0;
}

// `--render-bench`: what a see-through window costs to redraw, big and small (off screen; nothing shows).
if (args.Length >= 1 && args[0] == "--render-bench")
{
    RenderBench.Run(3440, 1440, false);
    RenderBench.Run(1600, 1200, false);
    RenderBench.Run(700, 900, false);
    RenderBench.Run(700, 900, true);
    return 0;
}

// `--capture-bench`: how long a screen picture takes (the whole main screen, shrunk to 720 tall; kept in memory only).
if (args.Length >= 1 && args[0] == "--capture-bench")
{
    Monitors.UseRealPixels();
    // every monitor: the fast copy must give the same picture as the old way, from the right place
    var monitors = Monitors.All();
    Console.WriteLine($"{monitors.Count} monitor(s)");
    foreach (var mon in monitors)
    {
        var gdi = ChestTracker.Native.ScreenCapture.GrayGdi(mon, 720);
        var swm = Stopwatch.StartNew();
        var fast = ChestTracker.Native.ScreenCapture.Gray(mon, 720);
        double first = swm.Elapsed.TotalMilliseconds;
        swm.Restart();
        for (int i = 0; i < 10; i++) fast = ChestTracker.Native.ScreenCapture.Gray(mon, 720);
        double each = swm.Elapsed.TotalMilliseconds / 10;
        gdi = ChestTracker.Native.ScreenCapture.GrayGdi(mon, 720);
        string same = "different sizes";
        if (gdi is not null && fast is not null && gdi.Width == fast.Width && gdi.Height == fast.Height)
        {
            double d = 0;
            for (int i = 0; i < gdi.Pixels.Length; i++) d += Math.Abs(gdi.Pixels[i] - fast.Pixels[i]);
            same = $"average difference {d / gdi.Pixels.Length:0.00} of 255";
        }
        Console.WriteLine($"  monitor at {mon.Left},{mon.Top} size {mon.Right - mon.Left}x{mon.Bottom - mon.Top}: picture {fast?.Width}x{fast?.Height}, " +
                          $"{each:0.0} ms each (first {first:0} ms), vs old way: {same}");
    }
    ChestTracker.Native.ScreenCapture.Release();
    if (monitors.Count > 0) return 0;
    var area = new ChestTracker.Native.Win32.RECT { Left = 0, Top = 0, Right = ChestTracker.Native.Win32.GetSystemMetrics(0), Bottom = ChestTracker.Native.Win32.GetSystemMetrics(1) };
    Console.WriteLine($"screen {area.Right}x{area.Bottom}");
    var g0 = ChestTracker.Native.ScreenCapture.GrayGdi(area, 720);
    var swc = Stopwatch.StartNew();
    for (int i = 0; i < 20; i++) ChestTracker.Native.ScreenCapture.GrayGdi(area, 720);
    Console.WriteLine($"GDI copy + own shrink: {swc.Elapsed.TotalMilliseconds / 20:0.0} ms per picture ({g0?.Width}x{g0?.Height})");
    var g1 = ChestTracker.Native.ScreenCapture.Gray(area, 720);
    swc.Restart();
    for (int i = 0; i < 20; i++) g1 = ChestTracker.Native.ScreenCapture.Gray(area, 720);
    Console.WriteLine($"Desktop Duplication: {swc.Elapsed.TotalMilliseconds / 20:0.0} ms per picture ({g1?.Width}x{g1?.Height})");
    if (g0 is not null && g1 is not null && g0.Width == g1.Width && g0.Height == g1.Height)
    {
        g0 = ChestTracker.Native.ScreenCapture.GrayGdi(area, 720)!;
        g1 = ChestTracker.Native.ScreenCapture.Gray(area, 720)!;
        double diff = 0;
        for (int i = 0; i < g0.Pixels.Length; i++) diff += Math.Abs(g0.Pixels[i] - g1.Pixels[i]);
        Console.WriteLine($"same picture? average difference {diff / g0.Pixels.Length:0.00} of 255");
    }
    ChestTracker.Native.ScreenCapture.Release();
    return 0;
}

// `--maps <out folder> <replay>...`: write each replay's match map the way the app does (read-only on the replays).
if (args.Length >= 3 && args[0] == "--maps")
{
    var maps = new MatchMaps(args[1]);
    foreach (var f in args.Skip(2))
    {
        var r = ReplayChests.Analyze(f);
        var mm = MatchMap.From(r.Record);
        maps.Save(r.Record.SessionId, mm);
        Console.WriteLine($"{Path.GetFileName(f)} -> {r.Record.SessionId}: path {mm.Path.Count}, bus {(mm.Bus is null ? "no" : "yes")}, storm {mm.Storm.Count}, marks {mm.Marks.Count}, mates {mm.Mates.Sum(m => m.Chests.Count)}, others {mm.Others.Count}");
    }
    return 0;
}

// `--probe <replay>`: what a replay holds besides chests (paths, storm, bus, eliminations). Read-only.
if (args.Length >= 2 && args[0] == "--probe")
{
    foreach (var f in args.Skip(1))
    {
        using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var an = new ChestCounter.ReplayAnalyzer();
        var rec = an.Analyze(fs, f, null);
        var rp = an.LastReplay!;
        var me = rp.PlayerData?.FirstOrDefault(p => p.IsReplayOwner) ?? rp.PlayerData?.FirstOrDefault(p => p.PlayerName == rec.PlayerName);
        Console.WriteLine($"== {Path.GetFileName(f)} me={rec.PlayerName} chests={rec.Chests.Count} len={rec.DurationSeconds}s players={rp.PlayerData?.Count()}");
        Console.WriteLine($"  my locations: {me?.Locations.Count} first={me?.Locations.FirstOrDefault()?.ReplicatedMovement?.Location} t={me?.Locations.FirstOrDefault()?.ReplicatedWorldTimeSeconds}");
        foreach (var l in (me?.Locations ?? new List<FortniteReplayReader.Models.PlayerMovement>()).Where((x, i) => i % 40 == 0).Take(12))
            Console.WriteLine($"    t={l.ReplicatedWorldTimeSeconds} last={l.LastUpdateTime} at={l.ReplicatedMovement?.Location} sky={l.bIsSkydiving}");
        Console.WriteLine($"  players with locations: {rp.PlayerData?.Count(p => p.Locations.Count > 0)}");
        Console.WriteLine($"  my death: {me?.DeathLocation} t={me?.DeathTime} placement={me?.Placement} kills={me?.Kills}");
        Console.WriteLine($"  bus paths: {rp.MapData?.BattleBusFlightPaths?.Count()}");
        foreach (var b in rp.MapData?.BattleBusFlightPaths ?? Enumerable.Empty<FortniteReplayReader.Models.BattleBus>())
            Console.WriteLine($"    start={b.FlightStartLocation} rot={b.FlightStartRotation} speed={b.FlightSpeed} end={b.TimeTillFlightEnd} drop={b.TimeTillDropStart}-{b.TimeTillDropEnd} ts={b.ReplicatedFlightTimestamp}");
        Console.WriteLine($"  safe zones: {rp.MapData?.SafeZones.Count}");
        foreach (var z in rp.MapData?.SafeZones ?? new List<FortniteReplayReader.Models.SafeZone>())
            Console.WriteLine($"    r={z.Radius:0} last={z.LastCenter}/{z.LastRadius:0} next={z.NextCenter}/{z.NextRadius:0} shrink={z.StartShrinkTime:0}-{z.FinishShrinkTime:0}");
        Console.WriteLine($"  elims: {rp.Eliminations?.Count} with location: {rp.Eliminations?.Count(e => e.IsValidLocation)}");
        foreach (var e in (rp.Eliminations ?? new List<FortniteReplayReader.Models.Events.PlayerElimination>()).Where(e => e.Eliminator == me?.EpicId || e.Eliminated == me?.EpicId || e.Eliminator == me?.PlayerId).Take(10))
            Console.WriteLine($"    {e.Time} {e.Eliminator}->{e.Eliminated} knocked={e.Knocked} at={e.EliminatedInfo.Location} from={e.EliminatorInfo.Location}");
        Console.WriteLine($"  killfeed: {rp.KillFeed?.Count} with location: {rp.KillFeed?.Count(k => k.DeathLocation != null)}");
        foreach (var k in (rp.KillFeed ?? new List<FortniteReplayReader.Models.KillFeedEntry>()).Where(k => k.FinisherOrDowner == me?.Id || k.PlayerId == me?.Id).Take(10))
            Console.WriteLine($"    t={k.ReplicatedWorldTimeSeconds} {k.FinisherOrDownerName}->{k.PlayerName} downed={k.IsDowned} revived={k.IsRevived} at={k.DeathLocation}");
        Console.WriteLine($"  my id={me?.Id} epic={me?.EpicId} team={me?.TeamIndex} sz start={rp.GameData?.SafeZonesStartTime}");
        Console.WriteLine($"  PATH {rec.Path.Count} points, air {rec.Path.Count(p => p.Air)}, first {rec.Path.FirstOrDefault()?.T}s {rec.Path.FirstOrDefault()?.X},{rec.Path.FirstOrDefault()?.Y} last {rec.Path.LastOrDefault()?.T}s");
        Console.WriteLine($"  BUS {rec.BusLine?.Length} {(rec.BusLine is null ? "none" : string.Join(",", rec.BusLine.Select(v => v.ToString("0"))))}");
        Console.WriteLine($"  STORM {rec.Storm.Count}: {string.Join(" | ", rec.Storm.Select(z => $"{z.X:0},{z.Y:0} r{z.Radius:0}"))}");
        foreach (var m in rec.Marks) Console.WriteLine($"  MARK {m.Kind} at {m.X},{m.Y} other={m.Other} bot={m.OtherIsBot}");
        Console.WriteLine($"  OTHERS {rec.OthersChests.Count}; mates' chests with position {rec.Teammates.Sum(t => t.Chests.Count(c => c.X != null))}/{rec.Teammates.Sum(t => t.Chests.Count)}; my chests with position {rec.Chests.Count(c => c.X != null)}/{rec.Chests.Count}");
    }
    return 0;
}

// `--replays <folder>`: read every replay in a folder and print what each counts (read-only).
if (args.Length >= 2 && args[0] == "--replays")
{
    foreach (var f in Directory.GetFiles(args[1], "*.replay").OrderBy(File.GetLastWriteTime))
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var info = ReplayFileInfo.Read(f);
            if (info is not { Complete: true }) { Console.WriteLine($"{Path.GetFileName(f)}: not finished"); continue; }
            var r = ReplayChests.Analyze(f);
            Console.WriteLine($"{Path.GetFileName(f)} {sw.Elapsed.TotalSeconds:0.0}s: {(r.Counts ? $"{r.Record.Chests.Count} chests ({r.Opened} opened, {r.Broken} broken), {r.Record.AmmoBoxesOpened} ammo" : "not counted: " + r.WhyNot)}");
            if (args.Length >= 3 && args[2] == "--finds")
            {
                var rec = r.Record;
                Console.WriteLine($"    placement {rec.Placement}/{rec.TotalPlayers} elims {rec.Eliminations} playlist {rec.Playlist} length {rec.DurationSeconds}s");
                foreach (var fd in rec.TreasureFinds) Console.WriteLine($"    FIND {fd.ObtainedAtSeconds}s {fd.Item} raw={fd.RawItem} id={fd.PrizeId} golden={fd.FromGoldenChest} src={fd.SourceBlueprint}");
                foreach (var fd in rec.TeamTreasureFinds) Console.WriteLine($"    TEAMFIND {fd.ObtainedAtSeconds}s {fd.Item} raw={fd.RawItem} id={fd.PrizeId} by={fd.Teammate}");
                foreach (var tm in rec.Teammates) Console.WriteLine($"    MATE {(tm.IsBot ? "(bot) " : "")}{(tm.InMyParty ? "(party) " : "")}{tm.Name}: {tm.Chests.Count} chests ({tm.Chests.Count(c => c.Destroyed)} broken), {tm.AmmoBoxes} ammo, {(tm.Kills?.ToString() ?? "?")} kills");
                foreach (var sp2 in rec.SeenPrizes) Console.WriteLine($"    PRIZE {sp2.TimeSeconds}s {sp2.PrizeId} {sp2.Who} {sp2.PlayerName}{(sp2.IsBot ? " (bot)" : "")}");
            }
            if (args.Length >= 3 && args[2] == "--where")
                foreach (var ch in r.Record.Chests)
                    Console.WriteLine($"    {ch.TimeSeconds,7:0.0}s {(ch.Destroyed ? "broken" : "opened")} {ch.Blueprint} at {(ch.X is null ? "?" : $"{ch.X:0},{ch.Y:0}")}");
        }
        catch (Exception e) { Console.WriteLine($"{Path.GetFileName(f)}: FAILED {e.Message}"); }
    }
    return 0;
}

int failed = 0, passed = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) passed++; else failed++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  (" + detail + ")" : "")}");
}

// ------------------------------------------------------------------ odds
Check("50% needs 95,308 chests", Odds.ChestsNeeded(0.5, Odds.PerChest) == 95_308);
Check("90% needs 316,605 chests", Odds.ChestsNeeded(0.9, Odds.PerChest) == 316_605);
Check("99% in Treasure Hours needs 422,139", Odds.ChestsNeeded(0.99, Odds.PerChestBoosted(1.5)) == 422_139);
double c = Odds.ChanceAtLeastOne(320, 50, 1.5);
Check("320 + 50 TH chests = 0.2869%", Math.Abs(c * 100 - 0.2869) < 0.0005, (c * 100).ToString("0.0000"));

// ------------------------------------------------------------------ saved counts (same file as the old Python version)
string tmp = Path.Combine(Path.GetTempPath(), $"ct_test_{Guid.NewGuid():N}.json");
File.WriteAllText(tmp, "{\"normal\": 412, \"treasure\": 60, \"log\": [{\"time\": \"2026-11-10T21:00:00\", \"treasure\": false, \"source\": \"auto\"}]}");
var store = new CountStore(tmp);
Check("reads old chest_counts.json", store.Normal == 412 && store.Treasure == 60);
store.Add(true, "manual");
Check("add goes to Treasure bucket", new CountStore(tmp).Treasure == 61);
store.Undo();
store.Undo();
Check("undo twice", store.Normal == 411 && store.Treasure == 60);
store.Add(false, "bulk", 250);
Check("add 250 already-opened chests in one go", new CountStore(tmp).Normal == 661);
Check("already-opened chests don't count as 'today'", store.CountSince(DateTime.Today) == 0);
store.Undo();
Check("one undo takes all 250 back off", new CountStore(tmp).Normal == 411);

// replay entries replace screen guesses from the same match, but never hand-added chests
var t0 = new DateTime(2026, 11, 11, 20, 0, 0);
store.AddEntries(new[]
{
    new ChestLogEntry { Time = CountStore.Stamp(t0.AddMinutes(1)), Source = "auto-label" },
    new ChestLogEntry { Time = CountStore.Stamp(t0.AddMinutes(2)), Source = "auto-screen" },
    new ChestLogEntry { Time = CountStore.Stamp(t0.AddMinutes(3)), Source = "manual" },
    new ChestLogEntry { Time = CountStore.Stamp(t0.AddMinutes(30)), Source = "auto-label" },
});
long before = store.Total;
var replayEntries = new List<ChestLogEntry>
{
    new() { Time = CountStore.Stamp(t0.AddMinutes(1.5)), Source = "replay", Kind = "Normal chest" },
    new() { Time = CountStore.Stamp(t0.AddMinutes(2.5)), Source = "replay", Kind = "Sprite chest", Broken = true },
    new() { Time = CountStore.Stamp(t0.AddMinutes(4)), Source = "replay", Kind = "Normal chest" },
};
store.Add(false, "bulk", 40);
before = store.Total;
Check("guesses found inside the match window", store.HasGuessesBetween(t0, t0.AddMinutes(10)) && !store.HasGuessesBetween(t0.AddMinutes(11), t0.AddMinutes(20)));
var removed = store.ReplaceAuto(t0, t0.AddMinutes(10), replayEntries);
Check("replay replaces the screen guesses and F7 adds in its match", removed.Count == 3 && store.Total == before - 3 + 3, $"removed {removed.Count}, total {store.Total} (was {before})");
var reread = new CountStore(tmp);
Check("bulk 'already opened' chests are kept", reread.Recent(20).Any(r => r.Entry.Source == "bulk" && r.Entry.Chests == 40));
Check("no hand-added chest left inside the replayed match", !reread.Recent(20).Any(r => r.Entry.Source == "manual"));
Check("screen guess outside the match is kept", reread.Recent(10).Count(r => r.Entry.Source == "auto-label") == 1);
Check("replay chests saved with type and 'broken'", reread.Recent(10).Any(r => r.Entry.Kind == "Sprite chest" && r.Entry.Broken));
long afterFirst = reread.Total;
var sameMatch = Enumerable.Range(0, 3).Select(i => new ChestLogEntry { Time = CountStore.Stamp(t0.AddMinutes(1 + i)), Source = "replay", Match = "M1" }).ToList();
reread.ReplaceAuto(t0, t0.AddMinutes(10), sameMatch);
var sameAgain = Enumerable.Range(0, 3).Select(i => new ChestLogEntry { Time = CountStore.Stamp(t0.AddMinutes(1 + i)), Source = "replay", Match = "M1" }).ToList();
reread.ReplaceAuto(t0, t0.AddMinutes(10), sameAgain);
Check("refine: the same match read twice is only counted once", reread.Total == afterFirst + 3, $"total {reread.Total}, expected {afterFirst + 3}");
var doc = JsonDocument.Parse(File.ReadAllText(tmp)).RootElement;
Check("file keeps the same JSON keys", doc.TryGetProperty("normal", out _) && doc.TryGetProperty("treasure", out _) && doc.TryGetProperty("log", out _));
File.Delete(tmp);

// ------------------------------------------------------------------ 3.4: saved files are never lost quietly
{
    string dir = Path.Combine(Path.GetTempPath(), $"ct_data_{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    string cp = Path.Combine(dir, "chest_counts.json");
    var s1 = new CountStore(cp);
    Check("data: a first run starts empty", s1.Loaded.State == LoadState.New && s1.Total == 0);
    s1.Add(false, "manual"); s1.Add(false, "manual"); s1.Add(true, "manual");
    var s2 = new CountStore(cp);
    Check("data: a saved count loads back", s2.Loaded.State == LoadState.Ok && s2.Total == 3);
    DataFile.Backup(cp, new DateTime(2026, 11, 15));
    Check("data: a daily backup is kept", DataFile.Backups(cp).Count == 1);
    File.WriteAllText(cp, "{ \"normal\": 2, \"trea");   // cut off half way
    var s3 = new CountStore(cp);
    Check("data: a damaged file is set aside and the backup put back",
          s3.Loaded.State == LoadState.RestoredFromBackup && s3.Total == 3 && s3.Loaded.SetAside is string aside1
          && File.Exists(Path.Combine(dir, aside1)) && new CountStore(cp).Total == 3, $"{s3.Loaded.State}, total {s3.Total}");
    foreach (var b in DataFile.Backups(cp)) File.Delete(b);
    File.WriteAllText(cp, "");   // empty
    var s4 = new CountStore(cp);
    Check("data: a damaged file with no backup is kept aside, never overwritten",
          s4.Loaded.State == LoadState.Unreadable && s4.Total == 0 && Directory.GetFiles(dir, "chest_counts.unreadable-*").Length == 2, $"{s4.Loaded.State}");
    File.WriteAllText(cp, "null");
    Check("data: a file that says nothing useful counts as damaged too", new CountStore(cp).Loaded.State == LoadState.Unreadable);
    s4.Add(false, "manual");
    using (var hold = new FileStream(cp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        Check("data: a file another program is holding is reported, not replaced", new CountStore(cp).Loaded.State == LoadState.Busy);
    Check("data: ...and it's untouched afterwards", new CountStore(cp).Total == 1);
    var s6 = new CountStore(cp);
    using (var hold = new FileStream(cp, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        s6.Add(false, "manual");
        Check("data: a save that can't be written is reported, not thrown", DataFile.SaveProblem is not null && s6.Total == 2);
    }
    s6.Add(false, "manual");
    Check("data: the next save writes everything", DataFile.SaveProblem is null && new CountStore(cp).Total == 3);
    foreach (var b in DataFile.Backups(cp)) File.Delete(b);
    for (int d = 1; d <= 7; d++) DataFile.Backup(cp, new DateTime(2026, 11, d));
    var kept = DataFile.Backups(cp);
    Check("data: only the last few daily backups are kept", kept.Count == 4 && Path.GetFileName(kept[0]).Contains("2026-11-07") && Path.GetFileName(kept[3]).Contains("2026-11-04"),
          string.Join(", ", kept.Select(Path.GetFileName)));
    Check("data: a folder that can't be written to is noticed", DataFile.CanWrite(dir) && !DataFile.CanWrite(Path.Combine(dir, "missing", "folder")));
    // settings: damaged values are put back in range, and the map route is off until switched on
    string setPath = Path.Combine(dir, "settings.json");
    File.WriteAllText(setPath, "{ \"OverlayScale\": 0, \"OverlayOpacity\": 5, \"MapKey\": 9999, \"AccentColor\": null }");
    var loadedSet = AppSettings.Load(setPath).Value;
    Check("settings: out-of-range values are put right", loadedSet.OverlayScale == 1.0 && loadedSet.OverlayOpacity == 1.0 && loadedSet.MapKey == 0x4D && loadedSet.AccentColor == Palette.DefaultAccent);
    Check("settings: the route on the Fortnite map is off for a new user", !new AppSettings().MapOverlay);
    Directory.Delete(dir, true);
}

// ------------------------------------------------------------------ 3.5: match maps, hotkeys and the controller button in settings
{
    string dir = Path.Combine(Path.GetTempPath(), $"ct_maps_{Guid.NewGuid():N}");
    var rec = new ChestCounter.MatchRecord { SessionId = "abc123" };
    rec.Path.Add(new ChestCounter.PathPoint { T = 60, X = 100, Y = 200 });
    rec.Path.Add(new ChestCounter.PathPoint { T = 61, X = 150, Y = 260 });
    rec.Storm.Add(new ChestCounter.StormCircle { X = 1, Y = 2, Radius = 80000 });
    rec.Marks.Add(new ChestCounter.MapMark { Kind = "elim", X = 5, Y = 6, Other = "Someone" });
    rec.Teammates.Add(new ChestCounter.TeammateRecord { Name = "Mate", Chests = { new ChestCounter.ChestRecord { TimeSeconds = 70, X = 9, Y = 9 }, new ChestCounter.ChestRecord { TimeSeconds = 80 } } });
    rec.OthersChests.Add(new ChestCounter.SeenChest { T = 90, X = 3, Y = 4, Destroyed = true });
    var mm = MatchMap.From(rec);
    Check("maps: only chests with a position go on the map", mm.Mates.Single().Chests.Count == 1 && mm.HasAnything);
    var mapStore = new MatchMaps(dir);
    Check("maps: nothing saved yet", mapStore.Load("abc123") is null && !mapStore.Has("abc123"));
    mapStore.Save("abc123", mm);
    var back = new MatchMaps(dir).Load("abc123");
    Check("maps: a saved match map loads back", back is not null && back.Path.Count == 2 && back.Path[1][1] == 150 && back.Storm[0][2] == 80000
          && back.Marks[0].Other == "Someone" && back.Others[0][3] == 1 && back.Mates[0].Name == "Mate");
    File.WriteAllText(Path.Combine(dir, "broken.json"), "{ not json");
    Check("maps: a damaged map file is just missing, never a crash", new MatchMaps(dir).Load("broken") is null);
    Check("maps: odd characters in a match id can't escape the folder", Path.GetDirectoryName(Path.GetFullPath(Path.Combine(dir, "x"))) == Path.GetFullPath(dir)
          && new Func<bool>(() => { mapStore.Save("..\\..\\evil", mm); return !File.Exists(Path.Combine(dir, "..", "..", "evil.json")); })());
    Directory.Delete(dir, true);

    string sdir = Path.Combine(Path.GetTempPath(), $"ct_keys_{Guid.NewGuid():N}");
    Directory.CreateDirectory(sdir);
    string sp2 = Path.Combine(sdir, "settings.json");
    var fresh = new AppSettings();
    Check("settings: hotkeys start as F6 to F9", fresh.HotkeyLock == 0x75 && fresh.HotkeyAdd == 0x76 && fresh.HotkeyUndo == 0x77 && fresh.HotkeyTreasure == 0x78);
    Check("settings: the controller map button starts as View", fresh.MapPadButton == 0x0020);
    Check("settings: start and stop with Fortnite is off until switched on", !fresh.FollowFortnite);
    File.WriteAllText(sp2, "{ \"HotkeyAdd\": 0, \"HotkeyUndo\": 99999, \"MapPadButton\": 3 }");
    var ks = AppSettings.Load(sp2).Value;
    Check("settings: a hotkey can be switched off, and bad ones are put back", ks.HotkeyAdd == 0 && ks.HotkeyUndo == 0x77 && ks.MapPadButton == 0x0020);
    Check("settings: all map layers are on to start", fresh.LayerPath && fresh.LayerBus && fresh.LayerStorm && fresh.LayerMarks && fresh.LayerMates && fresh.LayerOthers && fresh.SummaryTeammates);
    Directory.Delete(sdir, true);
}

// ------------------------------------------------------------------ the ledger never counts a match twice
string lp = Path.Combine(Path.GetTempPath(), $"ct_ledger_{Guid.NewGuid():N}.json");
var ledger = ReplayLedger.Load(lp);
ledger.Set(new ReplayLedger.Entry { File = "a.replay", SessionId = "S1", ChestsCounted = 5 });
ledger.Set(new ReplayLedger.Entry { File = "old.replay", SessionId = "S2", FromBefore = true });
ledger.Set(new ReplayLedger.Entry { File = "duo.replay", SessionId = "S3", Mates = new() { new() { Name = "Pal", Chests = 7, Broken = 1, Kills = 3, Party = true } } });
ledger.Set(new ReplayLedger.Entry { File = "solo.replay", SessionId = "S4", Mates = new() });
ledger.Save();
var ledger2 = ReplayLedger.Load(lp);
Check("ledger: counted match remembered by session (even if the file is renamed)", ledger2.SessionCounted("S1") && ledger2.Has("A.REPLAY"));
var duo = ledger2.All().First(e => e.SessionId == "S3").Mates;
Check("ledger (3.4): a match's teammates keep their chests and kills", duo is { Count: 1 } && duo[0].Name == "Pal" && duo[0].Chests == 7 && duo[0].Kills == 3 && duo[0].Party);
Check("ledger (3.4): solo is an empty list, not-read is null",
      ledger2.All().First(e => e.SessionId == "S4").Mates is { Count: 0 } && ledger2.All().First(e => e.SessionId == "S1").Mates is null);
Check("ledger: an old, not-imported match isn't 'counted'", !ledger2.SessionCounted("S2"));
File.Delete(lp);


// ------------------------------------------------------------------ v3.1: odds are never a guarantee
Check("odds: 100,000 chests is about a 51.7% chance", Math.Abs(Odds.ChanceAtLeastOne(100_000, 0, 1) - 0.5168) < 0.001,
      Odds.Percent(Odds.ChanceAtLeastOne(100_000, 0, 1)));
Check("odds: even 2,000,000 chests never shows 100%", Odds.Percent(Odds.ChanceAtLeastOne(2_000_000, 0, 1)) != "100%",
      Odds.Percent(Odds.ChanceAtLeastOne(2_000_000, 0, 1)));
Check("odds: groups with different boosts add up", Math.Abs(Odds.ChanceAtLeastOne(new[] { (1000L, 1.0), (1000L, 2.0) }, 137_500)
      - Odds.ChanceAtLeastOne(3000, 0, 1)) < 1e-6);
Check("odds: expected tokens", Math.Abs(Odds.Expected(new[] { (137_500L, 1.0) }, 137_500) - 1) < 1e-9);

// ------------------------------------------------------------------ v3.1: Treasure Hours (from fortnite.bot)
var ti = TreasureInfo.BuiltIn();
Check("treasure: Sat 14 Nov 7:30 PM UTC is a 1.5x Treasure Hour", ti.HourAt(new DateTime(2026, 11, 14, 19, 30, 0, DateTimeKind.Utc))?.Pirate == 1.5);
Check("treasure: the 8 Nov 2-4 AM UTC hour was 2x", ti.HourAt(new DateTime(2026, 11, 8, 3, 0, 0, DateTimeKind.Utc))?.Pirate == 2);
Check("treasure: 9 PM UTC (just after) is not a Treasure Hour", ti.HourAt(new DateTime(2026, 11, 14, 21, 0, 0, DateTimeKind.Utc)) is null);
Check("treasure: next hour after 13 Nov is 14 Nov 7 PM UTC", ti.NextHour(new DateTime(2026, 11, 13, 16, 0, 0, DateTimeKind.Utc))?.StartUtc == new DateTime(2026, 11, 14, 19, 0, 0, DateTimeKind.Utc));
Check("treasure: week 2 runs until 15 Nov", ti.WeekAt(new DateTime(2026, 11, 13, 16, 0, 0, DateTimeKind.Utc))?.Number == 2);
Check("treasure: json round trip", TreasureInfo.Parse(ti.ToJson()) is { Version: 1, Hours.Count: 8, Prizes.Count: 7 });
Check("treasure: broken json is ignored", TreasureInfo.Parse("{nope") is null && TreasureInfo.Parse("{}") is null);

string tp = Path.Combine(Path.GetTempPath(), $"ct_test_th_{Guid.NewGuid():N}.json");
var ths = new CountStore(tp);
var inHour = new DateTime(2026, 11, 8, 3, 10, 0, DateTimeKind.Utc).ToLocalTime();
ths.AddEntries(new[]
{
    new ChestLogEntry { Time = CountStore.Stamp(inHour), Source = "replay" },
    new ChestLogEntry { Time = CountStore.Stamp(inHour.AddHours(3)), Source = "replay" },
});
ths.Add(false, "bulk", 50);
int flipped = ths.ApplyTreasureHours(t => ti.HourAt(t.ToUniversalTime())?.Pirate);
Check("treasure: a chest opened in a Treasure Hour gets that hour's boost", flipped == 1 && ths.Treasure == 1 && ths.Normal == 51
      && ths.Snapshot()[0].Boost == 2, $"flipped {flipped}, normal {ths.Normal}, treasure {ths.Treasure}");
Check("treasure: applying again changes nothing", ths.ApplyTreasureHours(t => ti.HourAt(t.ToUniversalTime())?.Pirate) == 0);
File.Delete(tp);

// ------------------------------------------------------------------ v3.1: replays switched off (Fortnite's log)
var lt0 = new DateTime(2026, 11, 13, 20, 0, 0, DateTimeKind.Utc);
var flog = new FortniteLog(Path.Combine(Path.GetTempPath(), "no-such-log.log"));
flog.Feed("[2026.11.13-20.00.00:000][1]LogNet: Welcomed by server (Level: /Delilah/Maps/Delilah_Terrain?VerseURI=/Fortnite.com/GameFeatures/BRRoot, Game: /Game/Athena/Athena_GameMode.Athena_GameMode_C)\n", lt0);
flog.Check(lt0.AddSeconds(60));
Check("replays off: no warning in the first 90 s of a match", !flog.LastMatchNotRecorded);
flog.Check(lt0.AddSeconds(95));
Check("replays off: warning when a BR match doesn't start recording", flog.LastMatchNotRecorded);
flog.Feed("[x]LogDemo: UReplaySubsystem::RecordReplay: Starting recording with demo driver.\n", lt0.AddSeconds(100));
Check("replays off: warning goes once it records", !flog.LastMatchNotRecorded);
var flog2 = new FortniteLog(Path.Combine(Path.GetTempPath(), "no-such-log.log"));
flog2.Feed("[x]LogNet: Welcomed by server (Level: /Game/Creative/Maps/Creative, Game: /Game/Creative.Creative_C)\n", lt0);
flog2.Check(lt0.AddMinutes(5));
Check("replays off: Creative never warns", !flog2.LastMatchNotRecorded);
var flog3 = new FortniteLog(Path.Combine(Path.GetTempPath(), "no-such-log.log"));
flog3.Feed("[x]LogNet: Welcomed by server (Level: /Delilah/Maps/Delilah_Terrain?VerseURI=/Fortnite.com/Game", lt0);
flog3.Feed("Features/BRRoot, Game: x)\n[x]LogDemo: UReplaySubsystem::RecordReplay: Starting recording\n", lt0);
flog3.Check(lt0.AddMinutes(5));
Check("replays off: a line split across two reads still works", !flog3.LastMatchNotRecorded && flog3.Recording);

// ------------------------------------------------------------------ v3.1: stats and landing spots
var now0 = new DateTime(2026, 11, 13, 21, 0, 0);
var slog = new List<ChestLogEntry>();
void AddMatch(string id, DateTime start, int n, float x, float y)
{
    for (int i = 0; i < n; i++)
        slog.Add(new ChestLogEntry { Time = CountStore.Stamp(start.AddSeconds(30 * i)), Source = "replay", Match = id,
                                     Kind = i == 0 ? "Sprite chest" : "Normal chest", Broken = i == 1, X = x + i, Y = y });
}
AddMatch("A", now0.AddHours(-2), 9, -51000, 16000);   // Arlong Park
AddMatch("B", now0.AddHours(-1), 5, 26000, 89000);    // Plunder Port
AddMatch("C", now0.AddDays(-1), 11, -51500, 16500);   // Arlong Park
slog.Add(new ChestLogEntry { Time = CountStore.Stamp(now0), Source = "bulk", Count = 100 });
var st = ChestStats.From(slog, now0);
Check("stats: 3 matches, best 11, average 8.3", st.Matches.Count == 3 && st.Best?.Chests == 11 && Math.Abs(st.PerMatch - 25 / 3.0) < 1e-9);
Check("stats: total includes the previous count, today doesn't", st.Total == 125 && st.Today == 14, $"total {st.Total} today {st.Today}");
Check("stats: opened / broken / sprite", st.Opened == 22 && st.Broken == 3 && st.ByKind.First(k => k.Kind == "Sprite chest").Count == 3);
var pois = Online.ParsePois("{\"data\":{\"pois\":[{\"name\":\"Arlong Park\",\"location\":{\"x\":-51707,\"y\":16134,\"z\":0}},{\"name\":\"Plunder Port\",\"location\":{\"x\":26307,\"y\":89235,\"z\":0}}]}}");
var spots = ChestStats.LandingSpots(st.Matches, pois);
Check("map: place names read", pois.Count == 2);
Check("map: best landing spot is Arlong Park (10 a match)", spots.Count == 2 && spots[0].Name == "Arlong Park" && spots[0].Matches == 2 && spots[0].AvgChests == 10);

// ------------------------------------------------------------------ v3.1: update check and server status
Check("update: version read from the release name", Online.VersionIn("Chest Tracker 3.1") == new Version(3, 1, 0));
Check("update: 3.1 is newer than 3.0.0.0", Online.IsNewer(new Version(3, 1, 0), new Version(3, 0, 0, 0)));
Check("update: 3.1 is not newer than 3.1.0.0", !Online.IsNewer(new Version(3, 1, 0), new Version(3, 1, 0, 0)));
var stat = Online.ParseStatus("""
{"components":[{"id":"g","name":"Fortnite","status":"operational","group":true},
 {"id":"a","name":"Login","status":"operational","group_id":"g","position":1},
 {"id":"b","name":"Matchmaking","status":"partial_outage","group_id":"g","position":2},
 {"id":"z","name":"Rocket League","status":"major_outage","group_id":"x"}],
 "incidents":[{"name":"Fortnite matchmaking issues","status":"investigating","components":[{"group_id":"g"}]}],
 "scheduled_maintenances":[]}
""", DateTime.UtcNow);
Check("status: Fortnite parts only, in plain words", stat is { Overall: "Working", Parts.Count: 2 } && stat.Parts[1].Status == "Partly down" && stat.Notices.Count == 1,
      stat is null ? "null" : $"{stat.Overall} {stat.Parts.Count} {stat.Notices.Count}");


Check("session: Fortnite's open time read from its log", FortniteLog.ParseOpenLine("\uFEFFLog file open, 11/12/26 20:39:28\r\nLogWindows: x") == new DateTime(2026, 11, 12, 20, 39, 28)
      && FortniteLog.ParseOpenLine("something else") is null);

// ------------------------------------------------------------------ v3.2: finds, squad, efficiency
var frec = new ChestCounter.MatchRecord
{
    SessionId = "M1", MatchStartUtc = new DateTime(2026, 11, 1, 16, 10, 0, DateTimeKind.Utc),
    TreasureFinds =
    {
        new ChestCounter.TreasureFind { RawItem = "DelilahPrize:dpid_item3", PrizeId = "DelilahPrize:dpid_item3", ObtainedAtSeconds = 88, SourceBlueprint = "Tiered_Chest_Athena_C", FromGoldenChest = true },
        new ChestCounter.TreasureFind { RawItem = "WID_FlameCeleb_Consumable_S43", ObtainedAtSeconds = 578, SourceBlueprint = "B_TieredChest_Relics_C" },
        new ChestCounter.TreasureFind { RawItem = "GA_TreasureHandler", PrizeId = "Unknown:x", ObtainedAtSeconds = 483, SourceBlueprint = "AlwaysSpawn_RareChest_C", SourceDestroyed = true },
    },
    TeamTreasureFinds = { new ChestCounter.TreasureFind { RawItem = "GA_TreasureHandler", ObtainedAtSeconds = 100, Teammate = "Monsterboy8754" } },
    Teammates =
    {
        new ChestCounter.TeammateRecord { Name = "Monsterboy8754", EpicId = "abc", Chests = { new() { Blueprint = "Tiered_Chest_Athena_C" }, new() { Blueprint = "B_TieredChest_Relics_C", Destroyed = true } } },
        new ChestCounter.TeammateRecord { Name = "Wir3D._", Chests = { } },
    },
};
string fp = Path.Combine(Path.GetTempPath(), $"ct_test_finds_{Guid.NewGuid():N}.json");
var fstore = new FindStore(fp);
Check("finds: three prizes read from a replay", fstore.AddFromReplay(frec) == 3);
Check("finds: the same replay adds nothing twice", fstore.AddFromReplay(frec) == 0);
var fall = fstore.All();
Check("finds: plain-English names", fall.Any(f => f.Name == "Golden Firework" && f.Category == "Gizmo" && !f.Unsure)
      && fall.Any(f => f.Name == "Gold Mercedes-AMG G 63 Decal" && f.Category == "Car Decal")
      && fall.Any(f => f.Name == "Treasure gizmo" && f.Unsure && f.Broken), string.Join(", ", fall.Select(f => f.Name)));
fstore.Add(new FoundItem { Name = "Solid Gold Lil Uzi Vert (outfit)", Category = FindNames.CategoryOf("Solid Gold Lil Uzi Vert (outfit)"), Week = 2 });
var gizmoId = fall.First(f => f.Name == "Treasure gizmo").Id;
fstore.Update(gizmoId, "Lucky Locator", "Gizmo");
var reloaded = new FindStore(fp).All();
Check("finds: added by hand, changed and saved", reloaded.Count == 4 && reloaded.Any(f => f.Category == "Outfit" && f.Source == "manual")
      && reloaded.Any(f => f.Name == "Lucky Locator" && !f.Unsure));
Check("finds: the catalog lists the token, this event's prizes and gizmos", FindNames.Catalog(TreasureInfo.BuiltIn()) is var cat
      && cat[0].Name == "Pirate Shop Access" && cat.Any(c => c.Week == 2 && c.Category == "Outfit") && cat.Count(c => c.Category == "Gizmo") == 5);
File.Delete(fp);

string sp = Path.Combine(Path.GetTempPath(), $"ct_test_squad_{Guid.NewGuid():N}.json");
var squad = new SquadStore(sp);
Check("squad: a match is added once", squad.AddMatch(frec) && !squad.AddMatch(frec));
frec.SessionId = "M2";
squad.AddMatch(frec);
var mates = new SquadStore(sp).All();
Check("squad: chests, types and finds per teammate", mates.Count == 2 && mates[0].Name == "Monsterboy8754" && mates[0].Matches == 2
      && mates[0].Chests == 4 && mates[0].Broken == 2 && mates[0].Kinds["Sprite chest"] == 2 && mates[0].Finds.Count == 2,
      mates.Count == 0 ? "none" : $"{mates[0].Name} {mates[0].Matches} {mates[0].Chests} {string.Join(",", mates[0].Kinds)} {mates[0].Finds.Count}");
Check("squad: total of everyone's chests", new SquadStore(sp).TotalChests == 4);
var sq2 = new SquadStore(sp);
frec.SessionId = "M3";
frec.Teammates[0].InMyParty = true;
sq2.AddMatch(frec);
var mb = sq2.All().First(x => x.Name == "Monsterboy8754");
var wz = sq2.All().First(x => x.Name == "Wir3D._");
Check("squad: queued together = friend, fill = random", mb.PartyMatches == 1 && mb.IsFriend && !wz.IsFriend);
sq2.SetFriend("Wir3D._", true);
sq2.SetFriend("Monsterboy8754", false);
Check("squad: you can change who's a friend", new SquadStore(sp).All() is var sq3 && sq3.First(x => x.Name == "Wir3D._").IsFriend && !sq3.First(x => x.Name == "Monsterboy8754").IsFriend);
Check("prizes: token found by name, others named", FindNames.IsToken("DelilahPrize:PirateShopAccess") && !FindNames.IsToken("DelilahPrize:dpid_item3")
      && FindNames.PrizeName("DelilahPrize:dpid_item4") == "No Trespassing (Jam Track)" && FindNames.PrizeName("DelilahPrize:dpid_item9").Contains("dpid_item9"));
File.Delete(sp);

var est = ChestStats.From(slog, now0);
var starts = new Dictionary<string, DateTime> { ["A"] = now0.AddHours(-2).AddMinutes(-3), ["B"] = now0.AddHours(-1).AddMinutes(-2) };
var eff = ChestStats.EfficiencyOf("All", est.Matches, starts);
Check("efficiency: first chest 2:30 after the start on average", Math.Abs(eff.FirstChestSeconds - 150) < 1, $"{eff.FirstChestSeconds}");
Check("efficiency: first 5 minutes and per minute", est.Matches.First(m => m.Match == "A").FirstFive == 9 && Math.Abs(est.Matches.First(m => m.Match == "A").PerMinute - 9 / 4.0) < 1e-9);
var bySpot = ChestStats.EfficiencyBySpot(est.Matches, pois, starts);
Check("efficiency: per landing spot", bySpot.Count == 2 && bySpot.Any(b => b.Name == "Arlong Park" && b.Matches == 2));

// ------------------------------------------------------------------ real replays
// Pass the fixtures folder. Expected numbers were checked against what happened in those matches:
// the 20:12 match was watched live (8 "+600 XP CHEST" / "SPRITE CHEST" pop-ups, 1 ammo box, and one
// chest broken open that showed no pop-up at all).
if (args.Length > 0 && Directory.Exists(args[0]))
{
    foreach (var (file, chests, opened, broken, ammo, sprite) in new[]
    {
        ("UnsavedReplay-2026.11.11-20.12.36.replay", 9, 8, 1, 1, 1),
        ("UnsavedReplay-2026.11.11-20.17.38.replay", 10, 7, 3, 2, 1),
    })
    {
        string path = Path.Combine(args[0], file);
        if (!File.Exists(path)) { Console.WriteLine($"(missing {file})"); continue; }
        var info = ReplayFileInfo.Read(path);
        Check($"{file}: finished replay detected", info is { Complete: true, Encrypted: true, KeyPresent: true }, $"{info}");

        var sw = Stopwatch.StartNew();
        var r = ReplayChests.Analyze(path);
        double secs = sw.Elapsed.TotalSeconds;
        Check($"{file}: counts (Battle Royale, your replay)", r.Counts, r.WhyNot ?? "");
        Check($"{file}: {chests} chests", r.Record.Chests.Count == chests, $"{r.Record.Chests.Count} in {secs:0.0} s");
        Check($"{file}: {opened} opened, {broken} broken open", r.Opened == opened && r.Broken == broken, $"{r.Opened}/{r.Broken}");
        Check($"{file}: {ammo} ammo box(es), never counted as chests", r.Record.AmmoBoxesOpened == ammo
              && r.Record.Chests.All(ch => !ch.Blueprint.Contains("Ammo", StringComparison.OrdinalIgnoreCase)));
        var entries = ReplayChests.Entries(r, t => false, DateTime.UtcNow);
        Check($"{file}: {sprite} sprite chest named", entries.Count(e => e.Kind == "Sprite chest") == sprite,
              string.Join(", ", entries.Select(e => e.Kind + (e.Broken ? " (broken)" : ""))));
        var start = r.Record.MatchStartUtc!.Value.ToLocalTime();
        var withTh = ReplayChests.Entries(r, t => t >= start.AddSeconds(150), DateTime.UtcNow);
        Check($"{file}: Treasure Hours applied by each chest's own time", withTh.Any(e => e.Treasure) && withTh.Any(e => !e.Treasure));
        Check($"{file}: every chest has its position on the map", entries.All(e => e.X is not null && e.Y is not null));
        var boosted = ReplayChests.Entries(r, t => t >= start.AddSeconds(150) ? 2.0 : 1.0, DateTime.UtcNow);
        Check($"{file}: a Treasure Hour's own boost is saved per chest", boosted.Any(e => e.Treasure && e.Boost == 2) && boosted.Any(e => !e.Treasure && e.Boost is null));
        var fm = MatchMap.From(r.Record);
        Check($"{file}: the match map has your path from the jump (not the pre-game island)", fm.Path.Count > 50 && fm.Path[0][0] > 20, $"{fm.Path.Count} points from {fm.Path.FirstOrDefault()?[0]}s");
        Check($"{file}: the match map has the storm circles", fm.Storm.Count >= 1, $"{fm.Storm.Count}");
        Check($"{file}: your path passes your chests", r.Record.Chests.Where(c => c.X is not null).All(c => fm.Path.Any(p => Math.Abs(p[1] - c.X!.Value) < 2500 && Math.Abs(p[2] - c.Y!.Value) < 2500)),
              string.Join(", ", r.Record.Chests.Where(c => c.X is not null && !fm.Path.Any(p => Math.Abs(p[1] - c.X!.Value) < 2500 && Math.Abs(p[2] - c.Y!.Value) < 2500)).Select(c => $"{c.TimeSeconds}s")));
    }

    // a replay still being recorded (no key yet) must not be read
    string live = Path.Combine(args[0], "UnsavedReplay-2026.11.11-20.12.36.replay");
    if (File.Exists(live))
    {
        var bytes = File.ReadAllBytes(live).Take(8192).ToArray();
        var info = ReplayFileInfo.Parse(bytes, bytes.Length)!;
        // blank the key, and set "live", the way Fortnite writes it during a match
        int keyAt = FindKey(bytes);
        for (int i = 0; i < 32; i++) bytes[keyAt + i] = 0;
        var blank = ReplayFileInfo.Parse(bytes, bytes.Length)!;
        Check("replay without its key (match in progress) is not read yet", info.Complete && !blank.Complete);
    }
}
else Console.WriteLine("(skipping replay tests - pass the fixtures folder)");

// ------------------------------------------------------------------ 3.4: the route glides with the map
// A pretend map: still, then a smooth zoom (x2.5 around one spot, like scrolling), then still again. Positions are
// found 45 times a second, 18 ms after each picture, each a little off (as real ones are, measured below). The screen redraws at 40
// and at 144 times a second. A route point is followed on screen and compared with where it really is.
foreach (int fps in new[] { 40, 144 })
{
    long T(double seconds) => (long)(seconds * RouteMotion.TicksPerSecond);
    const double s0 = 0.6, x0 = 200, y0 = -100, ax = 900, ay = 500, px0 = 1400, py0 = 900;
    double Zoom(double t) => t < 0.3 ? 1 : t > 0.7 ? 2.5 : Math.Exp(Math.Log(2.5) * (3 * Math.Pow((t - 0.3) / 0.4, 2) - 2 * Math.Pow((t - 0.3) / 0.4, 3)));
    (double S, double X, double Y) Truth(double t) { double k = Zoom(t); return (s0 * k, ax - (ax - x0) * k, ay - (ay - y0) * k); }
    var rng = new Random(5);
    double Noise(double sd) => sd * Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
    var mo = new RouteMotion();
    mo.SetRoute(1300, 1000, 700);
    int nextFix = 0;
    double stillJitter = 0, stillN = 0, zoomErr = 0, naiveErr = 0, settleErr = 0, stepRms = 0, stepN = 0, overshoot = 0;
    double? lastShown = null, lastTrue = null, lastStep = null, lastTrueStep = null;
    (double S, double X, double Y)? newest = null;
    for (double t = 0; t < 1.2; t += 1.0 / fps)
    {
        while (nextFix / 45.0 + 0.018 <= t)
        {
            double tc = nextFix / 45.0;
            var tr = Truth(tc);
            var seen = (tr.S * Math.Exp(Noise(0.0002)), tr.X + Noise(0.35), tr.Y + Noise(0.35));   // about the wobble measured below, a bit more
            mo.Target(seen.Item1, seen.Item2, seen.Item3, T(tc), T(tc + 0.018));
            newest = seen;
            nextFix++;
        }
        mo.Step(T(t));
        if (!mo.Shown || newest is not { } nw) continue;
        var truth = Truth(t);
        double shownX = mo.S * px0 + mo.X, trueX = truth.S * px0 + truth.X, err = Math.Abs(shownX - trueX);
        double naive = Math.Abs(nw.S * px0 + nw.X - trueX);
        if (t > 0.15 && t < 0.3 && lastShown is double ls1) { stillJitter += (shownX - ls1) * (shownX - ls1); stillN++; }
        if (t > 0.95 && lastShown is double ls2) { stillJitter += (shownX - ls2) * (shownX - ls2); stillN++; }
        if (t >= 0.32 && t <= 0.7) { zoomErr = Math.Max(zoomErr, err); naiveErr = Math.Max(naiveErr, naive); }
        // stutter = the route's speed changing suddenly from one frame to the next (more than the map's does)
        if (lastShown is double ls3 && lastTrue is double lt)
        {
            double step = shownX - ls3, trueStep = trueX - lt;
            if (t >= 0.36 && t <= 0.7 && lastStep is double pst && lastTrueStep is double ptt)
            {
                double d = Math.Abs(step - pst) - Math.Abs(trueStep - ptt);
                stepRms += d * d; stepN++;
            }
            lastStep = step; lastTrueStep = trueStep;
        }
        if (t > 0.85) settleErr = Math.Max(settleErr, err);
        if (t > 0.7) overshoot = Math.Max(overshoot, shownX - trueX);   // past where the zoom stopped
        lastShown = shownX; lastTrue = trueX;
    }
    stillJitter = Math.Sqrt(stillJitter / Math.Max(1, stillN));
    stepRms = Math.Sqrt(stepRms / Math.Max(1, stepN));
    // (a third of a pixel can't be seen; the pretend positions here wobble a bit more than real ones do)
    Check($"route glide ({fps} fps screen): still map doesn't shake", stillJitter < 0.35, $"moves {stillJitter:0.00} px a frame when still");
    Check($"route glide ({fps} fps screen): keeps up with a zoom", zoomErr <= naiveErr * 1.05, $"worst gap {zoomErr:0.0} px (just jumping to each position: {naiveErr:0.0} px)");
    // (a 40 fps screen and 45 positions a second don't line up, which shows as a little unevenness; that isn't stutter)
    Check($"route glide ({fps} fps screen): moves smoothly while zooming", stepRms < (fps < 60 ? 1.6 : 1.0), $"speed jumps by {stepRms:0.00} px a frame");
    Check($"route glide ({fps} fps screen): settles on the map after the zoom", settleErr < 1.0, $"off by up to {settleErr:0.00} px");
    Check($"route glide ({fps} fps screen): hardly overshoots when the zoom stops", overshoot < 8, $"went {overshoot:0.0} px past");
}
{
    long T(double seconds) => (long)(seconds * RouteMotion.TicksPerSecond);
    var mo = new RouteMotion();
    mo.Target(0.5, 100, 100, T(0), T(0.018)); mo.Step(T(0.05));
    bool moved = false;
    for (int k = 1; k < 20; k++) { mo.Target(0.5, 100, 100, T(k / 45.0), T(k / 45.0 + 0.018)); moved |= mo.Step(T(k / 45.0 + 0.03)); }
    Check("route glide: a still map isn't redrawn", !moved);
    mo.Target(1.4, -900, -300, T(0.5), T(0.518));
    mo.Step(T(0.52));
    Check("route glide: a big jump goes straight there", Math.Abs(mo.S / 1.4 - 1) < 0.02 && Math.Abs(mo.X + 900) < 15);
}

// ------------------------------------------------------------------ 3.4: finding the Fortnite map on screen
// Fake "in-game map" screens made from the real map picture: zoomed, moved, darker, with labels, markers and noise.
string mapPng = Path.GetFullPath(Path.Combine(args.Length > 0 && Directory.Exists(args[0]) ? args[0] : ".", "..", "..", "map.png"));
if (File.Exists(mapPng))
{
    var swMap = Stopwatch.StartNew();
    var mapGray = MapTestKit.LoadGray(mapPng);
    var matcher = MapMatcher.Build(mapGray);
    Console.WriteLine($"    (map landmarks: {matcher.Count:N0} in {swMap.Elapsed.TotalSeconds:0.0}s)");
    var cases = new (string Name, double Scale, double Ox, double Oy)[]
    {
        ("zoomed out, whole island", 0.30, 333, 53),
        ("a bit zoomed in", 0.55, -150, -300),
        ("zoomed in on one corner", 1.05, -900, -400),
        ("zoomed right in", 1.45, -1500, -1200),
    };
    MapFix? last = null;
    foreach (var mc in cases)
    {
        var screen = MapTestKit.FakeScreen(mapGray, 1280, 720, mc.Scale, mc.Ox, mc.Oy, seed: (int)(mc.Scale * 100));
        var sw1 = Stopwatch.StartNew();
        var fix = matcher.Find(screen);
        double ms = sw1.Elapsed.TotalMilliseconds;
        bool ok = fix is { } f && Math.Abs(f.Scale / mc.Scale - 1) < 0.02
                  && Math.Abs(f.OffsetX - mc.Ox) < 4 && Math.Abs(f.OffsetY - mc.Oy) < 4;
        Check($"map overlay: finds the map ({mc.Name})", ok,
              fix is { } g ? $"got scale {g.Scale:0.000} offset {g.OffsetX:0},{g.OffsetY:0} ({g.Inliers} landmarks, {ms:0} ms), wanted {mc.Scale} {mc.Ox},{mc.Oy}" : $"not found ({ms:0} ms)");
        last = fix ?? last;
    }
    // following a zoom: the next picture is a little more zoomed in, found quickly from the last position
    var zoomed = MapTestKit.FakeScreen(mapGray, 1280, 720, 1.15, -1050, -470, seed: 5);
    var swT = Stopwatch.StartNew();
    var follow = matcher.Find(zoomed, new MapFix(1.05, -900, -400, 50));
    Check("map overlay: follows a zoom from the last position", follow is { } fz && Math.Abs(fz.Scale / 1.15 - 1) < 0.02 && Math.Abs(fz.OffsetX + 1050) < 4,
          follow is { } fy ? $"scale {fy.Scale:0.000} offset {fy.OffsetX:0},{fy.OffsetY:0} in {swT.Elapsed.TotalMilliseconds:0} ms" : "not found");
    // big zoom steps (one scroll that zooms 50% in or out around the middle of the screen): followed from the last position, quickly
    foreach (var (name, from, to) in new[]
    {
        ("in", new MapFix(1.05, -900, -400, 50), (S: 1.575, X: 640 - (640 + 900) * 1.5, Y: 360 - (360 + 400) * 1.5)),
        ("out", new MapFix(0.55, -150, -300, 50), (S: 0.55 / 1.5, X: 640 - (640 + 150) / 1.5, Y: 360 - (360 + 300) / 1.5)),
    })
    {
        var step = MapTestKit.FakeScreen(mapGray, 1280, 720, to.S, to.X, to.Y, seed: 21);
        matcher.Find(step, from);   // warm up
        var swZ = Stopwatch.StartNew();
        MapFix? zf = null;
        for (int rep = 0; rep < 5; rep++) zf = matcher.Find(step, from);
        double each = swZ.Elapsed.TotalMilliseconds / 5;
        Check($"map overlay: follows a big zoom {name} step", zf is { } zz && Math.Abs(zz.Scale / to.S - 1) < 0.02 && Math.Abs(zz.OffsetX - to.X) < 4 && Math.Abs(zz.OffsetY - to.Y) < 4,
              zf is { } zy ? $"scale {zy.Scale:0.000} offset {zy.OffsetX:0},{zy.OffsetY:0}, {each:0} ms each" : $"not found ({each:0} ms)");
    }
    // ultrawide screen (3440 x 1440 shrunk to 1720 x 720), zoomed all the way out: the island is small in the middle
    foreach (var (zs, zname) in new[] { (0.30, "zoomed out"), (0.22, "zoomed right out"), (0.15, "zoomed out as far as it goes") })
    {
        double zox = (1720 - 2048 * zs) / 2, zoy = (720 - 2048 * zs) / 2;
        var wide = MapTestKit.FakeScreen(mapGray, 1720, 720, zs, zox, zoy, seed: 31);
        var wf = matcher.Find(wide);
        Check($"map overlay: ultrawide screen, {zname}", wf is { } ww && Math.Abs(ww.Scale / zs - 1) < 0.03 && Math.Abs(ww.OffsetX - zox) < 4 && Math.Abs(ww.OffsetY - zoy) < 4,
              wf is { } wy ? $"scale {wy.Scale:0.000} offset {wy.OffsetX:0},{wy.OffsetY:0} ({wy.Inliers} landmarks), wanted {zs} {zox:0},{zoy:0}" : "not found");
        // and zooming out to it from a closer view
        var wfol = matcher.Find(wide, new MapFix(zs * 1.5, zox - 200, zoy - 150, 50));
        Check($"map overlay: ultrawide screen, follows zooming out to {zname}", wfol is { } wq && Math.Abs(wq.Scale / zs - 1) < 0.03,
              wfol is { } wr ? $"scale {wr.Scale:0.000}" : "not found");
    }
    // wobble: the same map view with different labels and noise each time; how much does a route point move?
    foreach (var (ws, wname) in new[] { (0.30, "zoomed out"), (0.9, "zoomed in"), (1.4, "zoomed right in") })
    {
        double wx = ws < 0.5 ? (1720 - 2048 * ws) / 2 : 860 - ws * 1000, wy = ws < 0.5 ? (720 - 2048 * ws) / 2 : 360 - ws * 1050;
        var pts = new List<(double X, double Y)>();
        for (int seed = 0; seed < 20; seed++)
        {
            var scr = MapTestKit.FakeScreen(mapGray, 1720, 720, ws, wx, wy, seed: 100 + seed);
            if (matcher.Find(scr, new MapFix(ws, wx, wy, 50)) is { } wf)
                foreach (var (mx, my) in new[] { (1000.0, 1050.0), (1000 + 300 / ws, 1050 + 150 / ws) })
                    pts.Add((wf.Scale * mx + wf.OffsetX - (ws * mx + wx), wf.Scale * my + wf.OffsetY - (ws * my + wy)));
        }
        double sd = Math.Sqrt(pts.Sum(p => p.X * p.X + p.Y * p.Y) / Math.Max(1, pts.Count) / 2);
        Console.WriteLine($"    (wobble {wname}: a route point is off by {sd:0.00} px on average in the 1720x720 picture, {sd * 2:0.00} px on a 1440p screen)");
    }
    // timing: where the time goes while following the map on an ultrawide screen (zoomed out, and zoomed in)
    foreach (var (ts, tname) in new[] { (0.30, "zoomed out"), (1.0, "zoomed in") })
    {
        double tx = ts < 0.5 ? (1720 - 2048 * ts) / 2 : 860 - ts * 1000, ty = ts < 0.5 ? (720 - 2048 * ts) / 2 : 360 - ts * 1050;
        var tScreen = MapTestKit.FakeScreen(mapGray, 1720, 720, ts, tx, ty, seed: 41);
        var tPrev = new MapFix(ts * 1.05, tx + 10, ty - 8, 50);
        for (int rep = 0; rep < 3; rep++) matcher.Find(tScreen, tPrev);
        double det = 0, mat = 0;
        var swAll = Stopwatch.StartNew();
        for (int rep = 0; rep < 20; rep++) { matcher.Find(tScreen, tPrev); det += matcher.LastDetectMs; mat += matcher.LastMatchMs; }
        Console.WriteLine($"    (following, {tname}, ultrawide: {swAll.Elapsed.TotalMilliseconds / 20:0.0} ms each = landmarks {det / 20:0.0} ms + matching {mat / 20:0.0} ms, {matcher.LastFeatures} landmarks on screen)");
    }
    // Every common monitor: the screen picture is the size the app would make for it, and the map has to be found
    // zoomed out, zoomed right out and zoomed in, followed through a zoom, and the minimap ignored.
    foreach (var (mw, mh, mname) in new[]
    {
        (1920, 1080, "1080p 16:9"), (2560, 1440, "1440p 16:9"), (3840, 2160, "4K 16:9"), (1366, 768, "768p laptop"),
        (1280, 720, "720p window"), (1920, 1200, "16:10 1200p"), (2560, 1600, "16:10 1600p"), (1600, 1200, "4:3"),
        (3440, 1440, "21:9 ultrawide"), (5120, 1440, "32:9 super ultrawide"),
    })
    {
        int k = 1 << ChestTracker.Native.ScreenCapture.Halvings(mh, 720);
        int W = mw / k, H = mh / k;
        double sOut = 0.85 * H / 2048, sFar = 0.45 * H / 2048, sIn = sOut * 3.5;
        var monitorCases = new (string What, double S, double X, double Y)[]
        {
            ("zoomed out", sOut, (W - 2048 * sOut) / 2, (H - 2048 * sOut) / 2),
            ("zoomed right out", sFar, (W - 2048 * sFar) / 2, (H - 2048 * sFar) / 2),
            ("zoomed in", sIn, W / 2.0 - sIn * 1000, H / 2.0 - sIn * 1050),
        };
        var fails = new List<string>();
        foreach (var mcase in monitorCases)
        {
            var scr = MapTestKit.FakeScreen(mapGray, W, H, mcase.S, mcase.X, mcase.Y, seed: mw + mh);
            var got = matcher.Find(scr);
            if (got is not { } g1 || Math.Abs(g1.Scale / mcase.S - 1) > 0.03 || Math.Abs(g1.OffsetX - mcase.X) > 5 || Math.Abs(g1.OffsetY - mcase.Y) > 5)
                fails.Add($"{mcase.What}: {(got is { } gx ? $"scale {gx.Scale:0.000} vs {mcase.S:0.000}" : "not found")}");
        }
        // follow a 1.5x zoom in around the middle, from zoomed out
        double sz = sOut * 1.5, zx = W / 2.0 - (W / 2.0 - monitorCases[0].X) * 1.5, zy = H / 2.0 - (H / 2.0 - monitorCases[0].Y) * 1.5;
        var zoomScr = MapTestKit.FakeScreen(mapGray, W, H, sz, zx, zy, seed: mw);
        var zfix = matcher.Find(zoomScr, new MapFix(sOut, monitorCases[0].X, monitorCases[0].Y, 50));
        if (zfix is not { } zg || Math.Abs(zg.Scale / sz - 1) > 0.03) fails.Add("following a zoom: " + (zfix is { } zh ? $"scale {zh.Scale:0.000}" : "lost"));
        // the minimap (a fifth of the screen's height, top right corner, zoomed in) must not be taken for the map
        int mm = (int)(0.2 * H);
        var gameScr = MapTestKit.Noise(W, H, mw);
        var monMini = MapTestKit.FakeScreen(mapGray, mm, mm, sIn, mm / 2.0 - sIn * 1000, mm / 2.0 - sIn * 1050, seed: mh);
        for (int y = 0; y < mm; y++) for (int x = 0; x < mm; x++) gameScr.Pixels[(10 + y) * W + (W - mm - 10) + x] = monMini.Pixels[y * mm + x];
        if (matcher.Find(gameScr) is not null) fails.Add("the minimap was taken for the map");
        Check($"map overlay on a {mname} monitor ({mw}x{mh}, looked at as {W}x{H})", fails.Count == 0, string.Join("; ", fails));
    }
    // the minimap on an ultrawide screen
    var wideMini = MapTestKit.Noise(1720, 720, 12);
    var wideMiniMap = MapTestKit.FakeScreen(mapGray, 200, 200, 0.9, -700, -600, seed: 13);
    for (int y = 0; y < 200; y++) for (int x = 0; x < 200; x++) wideMini.Pixels[(20 + y) * 1720 + 1500 + x] = wideMiniMap.Pixels[y * 200 + x];
    Check("map overlay: ultrawide screen, the minimap isn't taken for the map", matcher.Find(wideMini) is null);
    // the minimap: the island zoomed in, but only in a small square in the corner of the screen
    var mini = MapTestKit.Noise(1280, 720, 9);
    var miniMap = MapTestKit.FakeScreen(mapGray, 200, 200, 0.9, -700, -600, seed: 11);
    for (int y = 0; y < 200; y++) for (int x = 0; x < 200; x++) mini.Pixels[(20 + y) * 1280 + 1060 + x] = miniMap.Pixels[y * 200 + x];
    Check("map overlay: the small minimap in the corner isn't taken for the map", matcher.Find(mini) is null);
    // not the map (game picture without the map open): must say no
    var noMap = MapTestKit.Noise(1280, 720, 3);
    Check("map overlay: nothing found when the map isn't open", matcher.Find(noMap) is null);
    string cache = Path.Combine(Path.GetTempPath(), $"ct_mapfeat_{Guid.NewGuid():N}.bin");
    matcher.Save(cache, "k1");
    var mapReloaded = MapMatcher.Load(cache, "k1");
    Check("map overlay: saved landmarks load back (and a different map's don't)", mapReloaded?.Count == matcher.Count && MapMatcher.Load(cache, "k2") is null);
    File.Delete(cache);
}
else Console.WriteLine("(skipping map overlay tests - no map.png next to the app)");

Console.WriteLine(failed == 0 ? $"ALL {passed} PASSED" : $"{failed} FAILED, {passed} passed");
return failed == 0 ? 0 : 1;

// where the 32-byte key sits: right after the "encrypted" flag and the key length (32)
static int FindKey(byte[] b)
{
    for (int i = 0; i + 8 < b.Length; i++)
        if (BitConverter.ToUInt32(b, i) == 1 && BitConverter.ToInt32(b, i + 4) == 32 && i > 40) return i + 8;
    return -1;
}
