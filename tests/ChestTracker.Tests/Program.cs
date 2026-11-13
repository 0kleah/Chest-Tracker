using System.Diagnostics;
using System.Text.Json;
using ChestTracker.Core;

// `--treasure-json <file>`: write the built-in Treasure Hunters info (the repo's treasure.json starts from this).
if (args.Length >= 2 && args[0] == "--treasure-json")
{
    File.WriteAllText(args[1], TreasureInfo.BuiltIn().ToJson() + Environment.NewLine);
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
var doc = JsonDocument.Parse(File.ReadAllText(tmp)).RootElement;
Check("file keeps the same JSON keys", doc.TryGetProperty("normal", out _) && doc.TryGetProperty("treasure", out _) && doc.TryGetProperty("log", out _));
File.Delete(tmp);

// ------------------------------------------------------------------ the ledger never counts a match twice
string lp = Path.Combine(Path.GetTempPath(), $"ct_ledger_{Guid.NewGuid():N}.json");
var ledger = ReplayLedger.Load(lp);
ledger.Set(new ReplayLedger.Entry { File = "a.replay", SessionId = "S1", ChestsCounted = 5 });
ledger.Set(new ReplayLedger.Entry { File = "old.replay", SessionId = "S2", FromBefore = true });
ledger.Save();
var ledger2 = ReplayLedger.Load(lp);
Check("ledger: counted match remembered by session (even if the file is renamed)", ledger2.SessionCounted("S1") && ledger2.Has("A.REPLAY"));
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

Console.WriteLine(failed == 0 ? $"ALL {passed} PASSED" : $"{failed} FAILED, {passed} passed");
return failed == 0 ? 0 : 1;

// where the 32-byte key sits: right after the "encrypted" flag and the key length (32)
static int FindKey(byte[] b)
{
    for (int i = 0; i + 8 < b.Length; i++)
        if (BitConverter.ToUInt32(b, i) == 1 && BitConverter.ToInt32(b, i + 4) == 32 && i > 40) return i + 8;
    return -1;
}
