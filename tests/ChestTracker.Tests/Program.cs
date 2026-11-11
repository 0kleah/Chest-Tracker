using System.Diagnostics;
using System.Text.Json;
using ChestTracker.Core;

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
