# Chest-Tracker

> **Disclaimer:** This project was coded with **Claude Opus 5.5**, Anthropic's AI model.

Counts the chests you open in Fortnite and shows your odds of finding the **Pirate Shop Access token**, with an overlay that sits on top of the game.

## ℹ️ Good to know

- **Windows 10/11, PC Fortnite only.** It reads the replay files Fortnite saves on your PC.
- **Replays must be on:** in Fortnite, Settings → Game → Replays → **Record Battle Royale Replays: On** (on by default on PC).
- **Chests are added a few seconds after each match ends**, not the moment you open them. Fortnite only finishes a match's replay when the match is over.
- **Battle Royale matches only.** Creative and other modes don't count. Use F7 to add a chest by hand if you want.
- **Leaving early:** if you close Fortnite mid-match, that match can't be read. Leaving after you're eliminated is fine.
- **Safe:** no screen capture, no game memory, no network snooping, no injection. Nothing is uploaded anywhere.
- **Been counting already?** Type your previous count into the box on the Home page (see [below](#been-counting-already)).
- **Your data** stays in the same folder as the .exe: `chest_counts.json` (your count) and `replays_counted.json` (which matches were counted). Keep them with the .exe when you update.
- **After a Fortnite update**, the replay format can change. If a match can't be read, the app tells you. Please open an issue so it can be fixed.

## How it counts (exactly)

Fortnite saves a **replay** of every match on your PC. The replay is the game's own record of the match, and it includes every container: its exact type, and the server's note of **who searched it** or **who broke it**. A few seconds after each match ends, Chest Tracker reads that replay and adds your chests:

- ✅ Chests **you opened**: normal, rare and sprite chests.
- ✅ Chests **you broke open** while they were still unopened, with a pickaxe, gun or explosive, from close or far away.
- ❌ Breaking a chest that was **already opened** doesn't count.
- ❌ **Ammo boxes**, food boxes and loot cannons never count. They're a different type of object in the replay.
- 🔁 A chest Fortnite **refills** and you open again counts again (it's a new search).

It never looks at your screen, never reads or touches the game while it's running, and never sends anything anywhere. It only reads the replay files Fortnite already writes, so there's nothing for anti-cheat to see.

**Checked against real matches:** in a match watched live, the replay listed every chest that showed a "+600 XP CHEST" or "+900 XP SPRITE CHEST" pop-up. It also listed a chest that was broken open (which shows no pop-up at all), and kept the ammo box separate. 100 out of 100 saved replays were read without errors.

**The one trade-off:** Fortnite only finishes a replay when the match is over, so your count goes up a few seconds **after** each match, not the moment you open a chest. The overlay shows "In a match" while you play.

## Download

Download **ChestTracker.exe** from the **[latest release](https://github.com/0kleah/Chest-Tracker/releases/latest)**, put it in its own folder and double-click it. You don't need to install anything.

> Windows may say "Windows protected your PC" because the .exe isn't signed. Click **More info → Run anyway**. The full source is here if you'd rather build it yourself.

## How to use it

1. In Fortnite, make sure replays are on: **Settings → Game → Replays → Record Battle Royale Replays: On** (it's on by default on PC).
2. Open **ChestTracker.exe**. Tracking starts automatically. The dot is **green** while it's waiting, and **gold** during a match.
3. Play. When a match ends, its chests are added (with a beep), and your odds update in the app and on the overlay.

If you close Fortnite in the middle of a match, that match can't be read, because Fortnite never finishes its replay.

**Home** shows your total, today's chests, your token chance, progress toward a 50% chance, and your recent chests (type, opened or broken open). It's also where you add a previous count.
**Odds** is a calculator: chests needed for 10% to 99%, and how many matches that is.
**Settings** shows your recent matches and what was counted in each, lets you add chests from **older replays** (Fortnite keeps about 100), and covers the overlay and your data.

### Been counting already?

If you were counting chests before you got Chest Tracker (by hand, in a notepad, or with another tracker), add that number so your odds are right:

1. Open the **Home** page and find **Been counting already?**
2. Type your number (for example `250`) and press **Add** or Enter.
3. If some of those chests were opened in Treasure Hours, add them separately with **These were opened in Treasure Hours** switched on, so they get the 1.5x odds.

Your previous count is saved as its own entry. It's added on top of everything the tracker counts, and replays never change it. If you typed the wrong number, **Undo** (or F8) takes the whole entry back off.

You can also add chests from matches Fortnite still has replays of (it keeps about 100): **Settings → Check older replays**. Only do this for matches your previous count doesn't already include.

### Hotkeys (work in game)

| Key | Does |
|-----|------|
| F6  | Overlay: move mode / locked |
| F7  | Add a chest by hand (for example from a mode without replays) |
| F8  | Undo the last entry |
| F9  | Treasure Hours on/off. Chests opened while it's on use the 1.5x boosted odds, matched by each chest's own time in the replay. |

A replay is the exact record of its match, so if you press F7 during a Battle Royale match, that hand-added chest is replaced by the replay's count when the match ends. Nothing is counted twice.

### The overlay

- **F6** switches between **move mode** (dashed border: drag it anywhere, right-click for a menu) and **locked** (clicks pass through).
- While you're playing (Fortnite active with your cursor captured) it's **always click-through** and **never takes focus**, so it can't pull you out of the game.
- It remembers its position and see-through level.

## The odds

Epic's official rate is **1 in 137,500 per chest**, boosted **1.5x** during Saturday Treasure Hours. Every chest is an independent roll:

| Chance of getting it | Chests needed |
|---|---|
| 10% | ~14,500 |
| 50% | ~95,300 |
| 90% | ~316,600 |
| 99% | ~633,200 |

## Is it safe for my account?

Yes. It reads the replay files Fortnite saves in `%LOCALAPPDATA%\FortniteGame\Saved\Demos`, after each match is over, and checks the F6 to F9 hotkeys. It never reads or changes the game's memory or network traffic, never injects anything, never captures the screen, and never sends data anywhere.

### Why replays?

Other ways of knowing a chest was opened were tried or checked first:

- **Reading the screen** (the SEARCH prompt and XP pop-ups): worked most of the time, but it was fooled when an ammo box sat next to a chest, and it can't see chests that are broken open.
- **Game memory / network traffic:** Easy Anti-Cheat bans for it. Not an option.
- **Fortnite's log file:** doesn't record chest opens reliably.
- **Epic's stats and Overwolf's game events:** no chest counts.
- **Replays:** the game's own exact record. ✔

## Build from source

Needs Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/ChestTracker
```

Run the tests (odds, saved counts, no double counting, plus exact chest counts on real replays if you pass a folder of them):

```bash
dotnet run --project tests/ChestTracker.Tests -- path/to/folder-with-replays
```

Build the single .exe:

```bash
dotnet publish src/ChestTracker -c Release -o publish
```

### Project layout

- `src/ChestTracker/Core/`: odds, saved counts, replay results → chests, the record of which replays were counted, settings
- `src/ChestTracker/Tracker.cs`: watches the replay folder and counts each finished match; hotkeys
- `src/ChestTracker/MainWindow.xaml`, `OverlayWindow.xaml`, `Theme.xaml`: the UI (WPF)
- `lib/`: the replay reader (third-party, MIT, see below)
- `tests/ChestTracker.Tests/`: test runner

### Credits

Replay reading uses open-source code, included in `lib/` under the MIT licence:

- [FortniteReplayDecompressor](https://github.com/Shiqan/FortniteReplayDecompressor) by Shiqan
- the patches and chest analysis from [Fortnite-ChestCounter](https://github.com/LED-N/Fortnite-ChestCounter) by LED-N

See `lib/README.md` for details.

Not affiliated with or endorsed by Epic Games. Fortnite is a trademark of Epic Games, Inc.

## License

[MIT](LICENSE)
