# Chest-Tracker

> **Disclaimer:** This project was coded with **Claude Opus 5.5**, Anthropic's AI model.

Counts the chests you open in Fortnite and shows your odds of finding the **Pirate Shop Access token**, with an overlay that sits on top of the game. It also shows your stats, a map of every chest you've opened, your route in each match, your best landing spots, everything you've found, your squad's chests, and the Treasure Hunters info (Treasure Hours, prize odds, weekly prizes, server status) right in the app. You can change what the overlay shows, how big it is, and the colours of everything.

## In simple terms

1. Fortnite already saves a **replay** of every match on your PC. It's like a recording of everything that happened.
2. When your match ends, Chest Tracker opens that replay and looks for the chests **you** opened or broke open.
3. It adds them to your count and works out your chance of getting the Pirate Shop Access token.

That's it. Counting never looks at your screen, never touches the game while you play, and nothing about you is sent anywhere. It's a single .exe: download it, double-click it, done.

(One optional extra, [your route on the Fortnite map](#your-route-on-the-fortnite-map), does look at your screen, only while your in-game map is open. It's off until you switch it on.)

## ℹ️ Good to know

- **Windows 10 (version 2004 or newer) or Windows 11, PC Fortnite only.** It reads the replay files Fortnite saves on your PC, so it can't work for PlayStation, Xbox, Switch or mobile.
- **Replays must be on:** in Fortnite, Settings → Game → Replays → **Record Battle Royale Replays: On** (on by default on PC).
- **Chests are added a few seconds after each match ends**, not the moment you open them. Fortnite only finishes a match's replay when the match is over.
- **Battle Royale matches only.** Creative and other modes don't count. Use F7 to add a chest by hand if you want.
- **Leaving early:** if you close Fortnite mid-match, that match can't be read. Leaving after you're eliminated is fine.
- **Safe:** no game memory, no network snooping, no injection, and nothing is uploaded anywhere. Chests are counted from replay files, not from your screen. The only thing that ever looks at your screen is the optional route on the Fortnite map, while your map is open (off by default).
- **Been counting already?** Type your previous count into the box on the Home page (see [below](#been-counting-already)).
- **Replays switched off?** The app notices when a Battle Royale match starts without a replay being recorded (Fortnite's own log file says so) and warns you on the overlay and in the app, so you don't lose a whole session.
- **The token is never guaranteed.** Epic's rate is per chest: every chest is its own 1 in 137,500 roll. More chests raise your chance but can never make it 100%. Some players have opened over 100,000 chests without getting it (at 100,000 chests, about half of players still wouldn't have it). See [The odds](#the-odds).
- **Treasure Hours switch on by themselves** at the times Epic announced, with that hour's real boost (for example 2x on 8 Nov). You can still switch them on or off by hand with F9.
- **Your data** stays in the same folder as the .exe: `chest_counts.json` (your count), `replays_counted.json` (which matches were counted), `finds.json` (what you found), `squad.json` (your teammates, if you turn that on) and `chest_tracker_settings.json`. Keep them with the .exe when you update. The app tells you when a new version is out.
- **Your data is looked after:** a copy of each of those files is kept in the `backups` folder (one a day, for the last few days). If a file ever gets damaged, the app sets it aside, puts the newest good copy back and tells you. It never quietly starts again from zero.
- **Small and light:** about 25 MB to download, and usually 20 to 70 MB of memory while it runs (most of the time it's in the background).
- **After a Fortnite update**, the replay format can change. If a match can't be read, the app tells you. Please open an issue so it can be fixed.

## How it counts (exactly)

Fortnite saves a **replay** of every match on your PC. The replay is the game's own record of the match, and it includes every container: its exact type, and the server's note of **who searched it** or **who broke it**. A few seconds after each match ends, Chest Tracker reads that replay and adds your chests:

- ✅ Chests **you opened**: normal, rare and sprite chests.
- ✅ Chests **you broke open** while they were still unopened, with a pickaxe, gun or explosive, from close or far away.
- ❌ Breaking a chest that was **already opened** doesn't count.
- ❌ **Ammo boxes**, food boxes and loot cannons never count. They're a different type of object in the replay.
- 🔁 A chest Fortnite **refills** and you open again counts again (it's a new search).

Counting never looks at your screen, never reads or touches the game while it's running, and never sends anything anywhere. It only reads the replay files Fortnite already writes, so there's nothing for anti-cheat to see.

**Checked against real matches:** in a match watched live, the replay listed every chest that showed a "+600 XP CHEST" or "+900 XP SPRITE CHEST" pop-up. It also listed a chest that was broken open (which shows no pop-up at all), and kept the ammo box separate. 100 out of 100 saved replays were read without errors.

**The one trade-off:** Fortnite only finishes a replay when the match is over, so your count goes up a few seconds **after** each match, not the moment you open a chest. The overlay shows "In a match" while you play.

## Download

Download **ChestTracker.exe** from the **[latest release](https://github.com/0kleah/Chest-Tracker/releases/latest)**, put it in its own folder (for example `Documents\Chest Tracker`, not Program Files) and double-click it. You don't need to install anything.

> Windows may say "Windows protected your PC" because the .exe isn't signed. Click **More info → Run anyway**. Some antivirus programs also warn about small unsigned programs they haven't seen before. The full source is here if you'd rather build it yourself.

**Updating:** download the new `ChestTracker.exe`, close Chest Tracker (right-click its tray icon → Quit if it's in the tray), and put the new .exe in the same folder in place of the old one. Your count and settings are the other files in that folder, so they carry over.

**Removing it:** switch off **Settings → Start with Windows** if you turned it on, quit Chest Tracker, and delete its folder. That's everything: it doesn't install anything else or leave anything in Windows.

## How to use it

1. In Fortnite, make sure replays are on: **Settings → Game → Replays → Record Battle Royale Replays: On** (it's on by default on PC).
2. Open **ChestTracker.exe**. Tracking starts automatically. The dot is **green** while it's waiting, and **gold** during a match.
3. Play. When a match ends, its chests are added (with a beep), and your odds update in the app and on the overlay.

If you close Fortnite in the middle of a match, that match can't be read, because Fortnite never finishes its replay.

**Home** shows your total, today's chests, your token chance, progress toward a 50% chance, and your recent chests (type, opened or broken open). It's also where you add a previous count.
**Stats** shows matches counted, chests per match, your best match, this week, a 14-day chart, your chest types, and **how fast you find chests**: time to your first chest, chests in the first 5 minutes after landing, and chests per minute, overall and for each landing spot.
**Map** shows every chest you've opened this season on the island, and your **best landing spots**: where your first chest of each match was, and how many chests those matches gave you on average.
**Matches** lists every counted match with your placement, eliminations, mode and length. Pick one to see your **route**: its chests on the map, numbered in the order you opened them, and any **golden chest prizes** the replay saw anyone get in that match (you, a teammate or another player). If one is ever the Pirate Shop Access token, it's marked. A replay only sees what happens near you, so prizes found on the other side of the island can't show up. With **Show teammates** on (top right), each match also lists **your squad**: you and each teammate, with how many chests they opened and how many eliminations they got.

The match map can also show, each with its own switch above the map: **your path** (where you actually went, from the moment you jumped), the **bus** route and the way it flew, the **storm** circles in order, your **eliminations** and knocks (and where you went down), your **teammates' chests** in their own colours, and **chests other players opened** near you. **Close-up** zooms in on where you went. All of it comes from the replay, read once and kept in the `match_maps` folder; matches whose replays Fortnite has already deleted only have their chests. The replay only sees what happens near you.

In the **Squad** tab, **Matches together** next to a teammate shows every match you played with them, with both your chests and eliminations side by side.

**Routes** shows the route you took in every match this season on the map. **Best** and **Worst** put the matches with the most and fewest chests first (**Latest** lists them by date).
**Finds** lists the Treasure Hunters prizes you've found (gizmos, weekly cosmetics, and the Pirate Shop Access token if it ever drops). The tracker adds them from your replays by itself, including older replays Fortnite still has. Found something before you had Chest Tracker? Add it by hand from the list (or type its name). If the replay doesn't say exactly which prize it was, the app tells you, and you can press **Change** to fix it.
**Squad** (turn on **Squad totals** in Settings) remembers everyone you play with: how many chests they opened and of what type, and any prize they found. Your replay only sees what happens near you, so a teammate who drops far away won't have all their chests counted; their real totals are usually higher. Squad totals never change your own count or odds. Each teammate is marked **FRIEND** if they queued with you in your party, or **RANDOM** if they were a fill (Fortnite doesn't keep your friends list on your PC, so this is worked out from your parties; press **Mark as friend** to change it). At the top, **Your last match together** shows how many chests and eliminations each of you got in your most recent match with a squad, and each teammate's row shows their last match with you. Type in **Search teammates** to find someone in the list.
**Treasure** has the Treasure Hunters info: a countdown to the next Treasure Hour, every Treasure Hour in your time zone with its boosts, the odds of every prize with **your** chance of each, the weekly prizes, and Fortnite's server status.
**Odds** is a calculator: chests needed for 10% to 99%, and how many matches that is.
**Settings** shows your recent matches and what was counted in each, lets you add chests from **older replays** (Fortnite keeps about 100), and covers automatic Treasure Hours, update checks, **tray mode**, **start with Windows**, **Treasure Hour reminders**, **squad totals**, the **overlay look** and **colours**, and your data.

The **Home** page and the overlay also show **this session**: chests and matches since you last opened Fortnite.

After each match the overlay shows a short summary for a few seconds, for example **+9 chests · 1 sprite · 2 broken open**.

### Been counting already?

If you were counting chests before you got Chest Tracker (by hand, in a notepad, or with another tracker), add that number so your odds are right:

1. Open the **Home** page and find **Been counting already?**
2. Type your number (for example `250`) and press **Add** or Enter.
3. If some of those chests were opened in Treasure Hours, add them separately with **These were opened in Treasure Hours** switched on, so they get the 1.5x odds.

Your previous count is saved as its own entry. It's added on top of everything the tracker counts, and replays never change it. If you typed the wrong number, **Undo** (or F8) takes the whole entry back off.

You can also add chests from matches Fortnite still has replays of (it keeps about 100): **Settings → Check older replays**. Only do this for matches your previous count doesn't already include.

### Hotkeys

They work while Fortnite or Chest Tracker is the window in front (so pressing F8 in your browser can't change your count). You can pick your own keys, or no key, in **Settings → Hotkeys**; the app's labels follow what you pick.

| Key | Does |
|-----|------|
| F6  | Overlay: move mode / locked |
| F7  | Add a chest by hand (for example from a mode without replays) |
| F8  | Undo the last entry |
| F9  | Treasure Hours on/off by hand. It also switches on by itself during announced Treasure Hours; F9 can switch that off too. Each chest gets the boost by its own time in the replay. |

After each match the overlay shows a short summary ("+9 chests · 1 sprite · 2 broken open") and, unless you switch it off, a second line with each teammate's chests ("Squad: Ben 7 · Sam 4").

A replay is the exact record of its match, so if you press F7 during a Battle Royale match, that hand-added chest is replaced by the replay's count when the match ends. Nothing is counted twice.

### The overlay

- **F6** switches between **move mode** (dashed border: drag it anywhere, right-click for a menu) and **locked** (clicks pass through).
- While you're playing (Fortnite active with your cursor captured) it's **always click-through** and **never takes focus**, so it can't pull you out of the game.
- It remembers its position and see-through level.
- **Settings → Overlay look:** size (70% to 170%), a compact mode, and what it shows: chest count, token chance, "1 in …", this session, today's chests, next Treasure Hour, the status line, the Treasure Hours badge and the move/lock hint.

### Your route on the Fortnite map

It's off until you turn it on: **Routes → Show a route on the Fortnite map**. Then press your **map key** (M unless you changed it; you can set yours on the Routes page) in a match, or your controller's map button (View unless you changed it; B closes it). Controllers work if they're Xbox-style; PlayStation controllers only work through something like Steam that presents them as Xbox controllers. Your best route (or one you picked with **Use this route**) is drawn over the Fortnite map: a numbered gold line, chest by chest. It **follows the map when you zoom in or move it**, on whichever monitor Fortnite is on, and disappears when you close the map (map key or Esc).

- To find the map, Chest Tracker **looks at your screen only while your map is open** (like a screen recorder would): a small greyscale picture, kept in memory and never saved. It matches landmarks (roads, coastlines, buildings) against the map picture, so it knows where the map is and how far it's zoomed. It stops as soon as the map closes.
- **Check it works** (on the Routes page) gives you 20 seconds to open your map in Fortnite and tells you if it was found. It saves one picture of what it saw, `map_check.png`, next to the app.
- The route is left out of screen recordings and streams (so the map finder never sees its own drawing). Fortnite needs to be in windowed fullscreen or windowed mode, the same as for the overlay.
- The first time, it takes a few seconds in the background to learn the map's landmarks; they're saved in `map_landmarks.bin` and redone only when the season's map changes.

### Colours

**Settings → Colours** has accent presets (gold, ocean, emerald, violet, rose, fire, ice, silver), background presets (midnight, pure black, navy, forest, plum, slate), and exact colour codes for the accent, background, cards, text, overlay background and overlay text. Changes show straight away. **Back to the original colours** undoes it all.

### Tray, start with Windows and reminders

- **Keep running in the tray:** closing the window leaves Chest Tracker counting in the system tray (by the clock). Click the chest icon to open it, right-click it to quit. Opening Chest Tracker.exe again also brings it back up.
- **Start with Windows:** opens quietly in the tray when you turn on your PC. This adds one entry for Chest Tracker under your Windows user's startup programs, and switching it off removes it.
- **Treasure Hour reminders:** a Windows notification 10 minutes before each Treasure Hour, and when it starts.
- **Start and stop with Fortnite:** the overlay comes up when Fortnite starts and goes away a minute after you close it (once your last match is counted). In between, Chest Tracker waits in the tray using very little. Windows can't open a program when another one starts, so this also turns on Start with Windows.

## If something goes wrong

- **No chests are counted:** check replays are on in Fortnite (Settings → Game → Replays → Record Battle Royale Replays). Chests are added a few seconds after a match **ends**; a match you quit by closing Fortnite can't be read. Only Battle Royale counts.
- **"Chest Tracker can't save anything in this folder":** move ChestTracker.exe to a folder of its own that you can write to, such as one in Documents or on the Desktop.
- **"...couldn't open its saved file ... because another program is using it":** a sync, backup or antivirus program was reading the file at that moment. Wait a few seconds and start Chest Tracker again. Nothing is changed when this happens.
- **A red "Couldn't save" line in the app:** the same thing while it was running. Your newest chests are kept in memory and saved as soon as the file can be written again.
- **A file was damaged:** the app tells you, keeps the damaged file (named `...unreadable-...`) and puts back the newest copy from the `backups` folder.
- **The overlay or the route doesn't show over Fortnite:** set Fortnite's display mode to **Windowed Fullscreen** (programs can't draw over exclusive fullscreen).
- **A match couldn't be read after a Fortnite update:** the replay format changed. Please [open an issue](https://github.com/0kleah/Chest-Tracker/issues).
- **Anything else:** if the app says something went wrong, it saves the details in `crash.log` next to the .exe. Attach that file when you [report the problem](https://github.com/0kleah/Chest-Tracker/issues).

## The odds

Epic's official rate is **1 in 137,500 per chest**, boosted during Treasure Hours (usually **1.5x**, once **2x**). Every chest is an independent roll:

| Chance of getting it | Chests needed |
|---|---|
| 10% | ~14,500 |
| 50% | ~95,300 |
| 90% | ~316,600 |
| 99% | ~633,200 |

> ⚠️ **No number of chests guarantees the token.** The rate is per chest, not "one token every 137,500 chests". Each chest has the same tiny chance however many you've opened before, so it's luck, not a countdown. Some players have opened more than 100,000 chests without getting it: at 100,000 chests about 52% of players would have found it and about 48% wouldn't. The numbers in the app are chances, never promises.

How the app works it out: your chance is `1 - (1 - p)^n`, worked out for each group of chests with its own boost (normal, 1.5x, 2x). Only chests opened since the Treasure Hunters event started count for the token, plus any previous count you add. Weekly prizes (gold outfit, vehicle and decal, and so on) change every week, so only that week's chests count for them.

### Treasure Hunters info

The Treasure page uses the Treasure Hours, prize odds and weekly prizes listed on [fortnite.bot](https://fortnite.bot/treasure), which shows Epic's numbers. That info is built into the app and kept up to date through [`treasure.json`](treasure.json) in this repo, so new Treasure Hours and newly revealed prizes show up without a new download. (The app reads that file from GitHub; it doesn't load fortnite.bot itself.)

## Is it safe for my account?

Yes. It reads the replay files Fortnite saves in `%LOCALAPPDATA%\FortniteGame\Saved\Demos` after each match is over, reads new lines of Fortnite's log file (to notice replays being switched off), and checks your hotkeys (and, if you turn on the route on the map, your map key, Esc and your controller's map button). It never reads or changes the game's memory or network traffic, never injects anything, and never sends data about you anywhere. The only time it looks at your screen is while your Fortnite map is open with **Show a route on the Fortnite map** turned on (it's off by default), to see where the map is; that picture stays in memory and is thrown away straight after.

It only goes online to read public pages, anonymously: this repo's latest release (update check) and `treasure.json`, Epic's public status page (server status), and [fortnite-api.com](https://fortnite-api.com)'s map picture and place names (chest map). You can turn the update check off in Settings.

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

Run the tests (odds, saved counts, damaged and locked files, no double counting, finding the map on screen if `map.png` is next to the repo's root, plus exact chest counts on real replays if you pass a folder of them):

```bash
dotnet run --project tests/ChestTracker.Tests -- path/to/folder-with-replays
```

Build the single .exe (about 25 MB: parts of .NET the app never uses are left out, while WPF and the replay reader are kept whole):

```bash
dotnet publish src/ChestTracker -c Release -o publish
```

### Project layout

- `src/ChestTracker/Core/`: odds, saved counts, safe loading and saving with backups (`DataFile.cs`), replay results → chests, the record of which replays were counted, stats, routes, Treasure Hunters info, the log check, online look-ups, settings, and finding the Fortnite map in a screen picture (`MapMatcher.cs`, `RouteMotion.cs`)
- `src/ChestTracker/Tracker.cs`: watches the replay folder and counts each finished match; hotkeys
- `src/ChestTracker/MainWindow.xaml` (+ `MainWindow.Pages.cs`, `MainWindow.More.cs`, `MainWindow.Routes.cs`), `OverlayWindow.xaml`, `Theme.xaml`: the UI (WPF)
- `src/ChestTracker/MapRouteOverlay.cs`, `Native/ScreenCapture.cs`, `Native/DesktopDuplication.cs`: the route drawn over the in-game map (screen pictures only while the map is open)
- `treasure.json`: Treasure Hours, prize odds and weekly prizes, read by the app (edit this when Epic announces new ones)
- `lib/`: the replay reader (third-party, MIT, see below)
- `tests/ChestTracker.Tests/`: test runner

### Credits

Replay reading uses open-source code, included in `lib/` under the MIT licence:

- [FortniteReplayDecompressor](https://github.com/Shiqan/FortniteReplayDecompressor) by Shiqan
- the patches and chest analysis from [Fortnite-ChestCounter](https://github.com/LED-N/Fortnite-ChestCounter) by LED-N

See `lib/README.md` for details.

Treasure Hours, prize odds and weekly prizes are taken from [fortnite.bot](https://fortnite.bot/treasure) (by HYPEX), which lists Epic's numbers. The map picture and place names come from [fortnite-api.com](https://fortnite-api.com). Server status comes from [Epic's public status page](https://status.epicgames.com).

Not affiliated with or endorsed by Epic Games. Fortnite is a trademark of Epic Games, Inc.

## License

[MIT](LICENSE)
