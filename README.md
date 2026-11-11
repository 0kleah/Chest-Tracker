# Chest-Tracker

> **Disclaimer:** This project was coded with **Claude Opus 5.5**, Anthropic's AI model.

Counts the chests you open in Fortnite, and tells you your odds of finding the **Pirate Shop Access token**.

It watches your screen the way Medal spots kills: when the chest's **"Chest / Search"** prompt is near your crosshair, you hold interact, and the prompt disappears, that's one chest opened. Your count is saved between sessions and turned into live odds.

## Download

Download **PirateTokenOdds.exe** from the **[latest release](https://github.com/0kleah/Chest-Tracker/releases/latest)**, put it in its own folder and double-click it. You don't need Python.

> Windows may say "Windows protected your PC" because the .exe isn't signed. Click **More info → Run anyway**. Some antivirus tools also flag it because it reads the screen and keypresses. The full source is here if you'd rather run or build it yourself.

## How to use it

Double-click the .exe and pick from the menu:

1. **Start the chest counter + overlay.** Leave it running while you play. Stop it with Ctrl+C in its window, or right-click the overlay and choose Quit.
2. **Show my Pirate Token odds**, based on the chests it has counted.
3. **Odds calculator**, where you type in your own numbers.
4. **Test a screenshot**, for when chests aren't being detected.
5. **Reset my chest count to zero.**

### The overlay

A small panel floats on top of Fortnite with your chest count and Pirate Token odds. It never takes focus away from the game.

- **F6** switches between **move mode** (dashed blue border, drag it anywhere) and **locked** (clicks pass straight through it into the game). It remembers where you put it.
- While you're playing (Fortnite active with your cursor captured) it's **always click-through**, even in move mode, so a click can never pull focus away from the game. Move it from Fortnite's menus (cursor visible) or after alt-tabbing.
- **Right-click** it (in move mode) for Treasure Hours, add/undo a chest, see-through level, and Quit.
- The dot is **green** while it's watching Fortnite, **gold** while a chest prompt is on screen, and grey when Fortnite isn't the active window. The count flashes gold when a chest is counted.
- Don't want it? Run `PirateTokenOdds.exe --no-overlay`.

Hotkeys while you play:

| Key | Does |
|-----|------|
| F6  | Overlay: move mode / locked |
| F7  | Add a chest it missed |
| F8  | Undo the last chest |
| F9  | Turn Treasure Hours on/off (those chests use the boosted odds) |

Tips:
- Play in **Windowed Fullscreen**. Exclusive fullscreen can capture as a black screen, and no overlay can draw over it.
- It only counts while Fortnite is the active window.
- Rebound interact? Run it with an option, e.g. `PirateTokenOdds.exe --key F` or `--pad-button A`.
- Not detecting chests? Run `PirateTokenOdds.exe --debug` to see what text it reads on screen.
- Controllers are read through XInput (Xbox pads, or PlayStation pads through Steam Input or DS4Windows).

## The odds

Epic's official rate is **1 in 137,500 per chest**, boosted **1.5x** during Saturday Treasure Hours. Every chest is an independent roll:

| Chance of getting it | Chests needed |
|---|---|
| 10% | ~14,500 |
| 50% | ~95,300 |
| 90% | ~316,600 |
| 99% | ~633,200 |

## Is it safe for my account?

It only takes screenshots (like OBS or Medal) and checks whether your interact key is held. It never reads or changes the game's memory, never injects anything, and never sends data anywhere. Text recognition is done offline by Windows' built-in OCR.

## Run from source

Windows 10/11 and Python 3.10+:

```bash
pip install -r requirements.txt
python pirate_token_app.py        # menu
python chest_counter.py --help    # counter options
python pirate_token_odds.py --help
```

Build the .exe yourself:

```bash
pip install pyinstaller
pyinstaller --onefile --console --name PirateTokenOdds --collect-all winrt pirate_token_app.py
```

## Files

- `pirate_token_app.py`: the menu (this is what the .exe runs)
- `chest_counter.py`: screen and keypress chest detection
- `overlay.py`: the movable on-top overlay
- `pirate_token_odds.py`: odds calculator
- `chest_counts.json` / `overlay_settings.json`: your saved count and overlay position (created next to the .exe, not uploaded)

Not affiliated with or endorsed by Epic Games. Fortnite is a trademark of Epic Games, Inc.

## License

[MIT](LICENSE)
