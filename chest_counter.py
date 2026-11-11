"""
Fortnite chest counter - watches your screen (like Medal does for kills)
and counts every chest you open, then shows your Pirate Token odds.

How it detects a chest:
  1. The interaction prompt for a chest ("Chest" / "Search") is on screen
     near your crosshair  (read with Windows' built-in text recognition)
  2. You hold the interact key (E on keyboard, X on an Xbox-style controller)
  3. The prompt disappears right after -> the chest was opened

It only reads your screen and key presses. It never touches the game's
memory, so it's the same kind of thing OBS / Medal do (anti-cheat safe).
Play in "Windowed Fullscreen" - exclusive fullscreen can capture as black.

Hotkeys (work while in game):
  F7  add a chest by hand (if it missed one)
  F8  undo the last chest
  F9  toggle Treasure Hours on/off (chests count toward the boosted odds)
  Ctrl+C in this window to quit

Usage:
  python chest_counter.py
  python chest_counter.py --debug            # print what it reads on screen
  python chest_counter.py --test-image shot.png   # check a screenshot
  python chest_counter.py --key F --pad-button A  # if you rebound interact
  python chest_counter.py --reset            # start counting from zero
"""

import argparse
import asyncio
import ctypes
import ctypes.wintypes as wt
import json
import os
import sys
import threading
import time
from datetime import datetime

import mss
import numpy as np
from winrt.windows.graphics.imaging import BitmapAlphaMode, BitmapPixelFormat, SoftwareBitmap
from winrt.windows.media.ocr import OcrEngine
from winrt.windows.storage.streams import DataWriter

from pirate_token_odds import DEFAULT_BOOST, app_dir, chance_at_least_one

HERE = app_dir()
COUNTS_FILE = os.path.join(HERE, "chest_counts.json")

# Part of the screen to read, as fractions of the monitor (left, top, right, bottom).
# Fortnite's interact prompt sits just right of / below the crosshair.
DEFAULT_REGION = (0.35, 0.40, 0.80, 0.80)

user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32


# --------------------------------------------------------------------------- input

def vk_from_name(name):
    name = name.strip().upper()
    if len(name) == 1 and name.isalnum():
        return ord(name)
    if name.startswith("F") and name[1:].isdigit():
        return 0x70 + int(name[1:]) - 1
    named = {"SPACE": 0x20, "SHIFT": 0x10, "CTRL": 0x11, "ALT": 0x12, "MOUSE4": 0x05, "MOUSE5": 0x06}
    if name in named:
        return named[name]
    raise ValueError(f"Unknown key: {name}")


def key_down(vk):
    return bool(user32.GetAsyncKeyState(vk) & 0x8000)


PAD_BUTTONS = {"A": 0x1000, "B": 0x2000, "X": 0x4000, "Y": 0x8000}


class _XInputGamepad(ctypes.Structure):
    _fields_ = [("wButtons", wt.WORD), ("bLeftTrigger", ctypes.c_ubyte), ("bRightTrigger", ctypes.c_ubyte),
                ("sThumbLX", ctypes.c_short), ("sThumbLY", ctypes.c_short),
                ("sThumbRX", ctypes.c_short), ("sThumbRY", ctypes.c_short)]


class _XInputState(ctypes.Structure):
    _fields_ = [("dwPacketNumber", wt.DWORD), ("Gamepad", _XInputGamepad)]


def _load_xinput():
    for dll in ("xinput1_4", "xinput1_3", "xinput9_1_0"):
        try:
            return getattr(ctypes.windll, dll)
        except OSError:
            continue
    return None


_xinput = _load_xinput()


def pad_button_down(mask):
    if not _xinput or not mask:
        return False
    state = _XInputState()
    for i in range(4):
        if _xinput.XInputGetState(i, ctypes.byref(state)) == 0 and state.Gamepad.wButtons & mask:
            return True
    return False


class InteractWatcher(threading.Thread):
    """Polls the interact key/button 100x a second and remembers recent holds."""

    def __init__(self, vk, pad_mask):
        super().__init__(daemon=True)
        self.vk, self.pad_mask = vk, pad_mask
        self.lock = threading.Lock()
        self.hold_start = None          # time the current hold began, or None
        self.holds = []                 # finished holds: (start, end)

    def run(self):
        while True:
            down = key_down(self.vk) or pad_button_down(self.pad_mask)
            now = time.monotonic()
            with self.lock:
                if down and self.hold_start is None:
                    self.hold_start = now
                elif not down and self.hold_start is not None:
                    self.holds.append((self.hold_start, now))
                    self.holds = self.holds[-20:]
                    self.hold_start = None
            time.sleep(0.01)

    def snapshot(self, now):
        with self.lock:
            holds = list(self.holds)
            if self.hold_start is not None:
                holds.append((self.hold_start, now))
            return holds


# --------------------------------------------------------------------------- detection

class ChestDetector:
    """Decides when a chest was opened, from prompt visibility + interact holds.

    Pure logic (no screen/keyboard access) so it can be tested with fake timelines.
    """

    def __init__(self, min_hold=0.15, grace=1.2, miss_frames=2, cooldown=0.8):
        self.min_hold = min_hold        # shortest hold that counts as "searching"
        self.grace = grace              # prompt must vanish within this long after the hold
        self.miss_frames = miss_frames  # frames without the prompt before we call it gone
        self.cooldown = cooldown
        self.prompt_on = False
        self.first_seen = self.last_seen = 0.0
        self.misses = 0
        self.last_count = -1e9
        self.consumed_until = -1e9      # holds starting before this were already used

    def reset_prompt(self):
        self.prompt_on = False
        self.misses = 0

    def update(self, now, prompt_visible, holds):
        """Feed one frame. Returns True when a chest open is detected."""
        if prompt_visible:
            if not self.prompt_on:
                self.prompt_on = True
                self.first_seen = now
            self.last_seen = now
            self.misses = 0
            return False

        if not self.prompt_on:
            return False
        self.misses += 1
        if self.misses < self.miss_frames:
            return False

        # Prompt has gone away - was it because the player searched it?
        self.prompt_on = False
        if now - self.last_count < self.cooldown:
            return False
        for start, end in holds:
            if start <= self.consumed_until:
                continue
            long_enough = end - start >= self.min_hold
            during_prompt = start <= self.last_seen + 0.15 and end >= self.first_seen
            recent = end >= self.last_seen - self.grace
            if long_enough and during_prompt and recent:
                self.last_count = now
                self.consumed_until = start
                return True
        return False


def looks_like_chest_prompt(lines, loose=False):
    """lines: OCR text lines. A chest prompt has a name line with 'chest' and an
    action line with 'search'/'open' (separate lines, so a quest popup like
    'Search Chests 2/5' doesn't count)."""
    low = [l.lower() for l in lines]
    if loose:
        text = " ".join(low)
        return "chest" in text
    name_line = any("chest" in l and "search" not in l and len(l.split()) <= 4 for l in low)
    action_line = any(("search" in l or "open" in l) and "chest" not in l for l in low)
    return name_line and action_line


# --------------------------------------------------------------------------- screen + OCR

class ScreenReader:
    def __init__(self, region=DEFAULT_REGION, monitor=1):
        self.sct = mss.MSS() if hasattr(mss, "MSS") else mss.mss()
        mon = self.sct.monitors[monitor]
        l, t, r, b = region
        self.box = {
            "left": mon["left"] + int(mon["width"] * l),
            "top": mon["top"] + int(mon["height"] * t),
            "width": int(mon["width"] * (r - l)),
            "height": int(mon["height"] * (b - t)),
        }
        self.engine = OcrEngine.try_create_from_user_profile_languages()
        if self.engine is None:
            sys.exit("Windows text recognition isn't available. Add an English language pack in Windows Settings.")
        self.loop = asyncio.new_event_loop()

    def grab(self):
        shot = self.sct.grab(self.box)
        return np.frombuffer(shot.bgra, dtype=np.uint8).reshape(shot.height, shot.width, 4)

    def read_lines(self, bgra):
        img = bgra
        if img.shape[1] > 1400:              # 4K etc: shrink to keep OCR fast
            img = img[::2, ::2]
        img = np.ascontiguousarray(img).copy()
        img[:, :, 3] = 255
        writer = DataWriter()
        writer.write_bytes(img.tobytes())
        bmp = SoftwareBitmap.create_copy_from_buffer(
            writer.detach_buffer(), BitmapPixelFormat.BGRA8, img.shape[1], img.shape[0], BitmapAlphaMode.PREMULTIPLIED)
        result = self.loop.run_until_complete(self.engine.recognize_async(bmp))
        return [line.text for line in result.lines]


def fortnite_focused():
    hwnd = user32.GetForegroundWindow()
    pid = wt.DWORD()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    h = kernel32.OpenProcess(0x1000, False, pid.value)  # PROCESS_QUERY_LIMITED_INFORMATION
    if not h:
        return False
    try:
        buf = ctypes.create_unicode_buffer(512)
        size = wt.DWORD(512)
        kernel32.QueryFullProcessImageNameW(h, 0, buf, ctypes.byref(size))
        return "fortniteclient" in os.path.basename(buf.value).lower()
    finally:
        kernel32.CloseHandle(h)


# --------------------------------------------------------------------------- saving

def load_counts():
    try:
        with open(COUNTS_FILE, encoding="utf-8") as f:
            data = json.load(f)
        data.setdefault("normal", 0)
        data.setdefault("treasure", 0)
        data.setdefault("log", [])
        return data
    except (OSError, ValueError):
        return {"normal": 0, "treasure": 0, "log": []}


def save_counts(data):
    tmp = COUNTS_FILE + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
    os.replace(tmp, COUNTS_FILE)


def odds_line(data, boost):
    total = data["normal"] + data["treasure"]
    chance = chance_at_least_one(data["normal"], data["treasure"], boost)
    extra = f" (1 in {1 / chance:,.0f})" if chance else ""
    return f"{total:,} chests ({data['treasure']:,} in Treasure Hours) -> {chance * 100:.4f}% chance of the Pirate Token{extra}"


def beep():
    import winsound
    threading.Thread(target=winsound.Beep, args=(1400, 70), daemon=True).start()


# --------------------------------------------------------------------------- main

def test_image(path, region, loose):
    from PIL import Image
    img = Image.open(path).convert("RGB")
    w, h = img.size
    l, t, r, b = region
    crop = img.crop((int(w * l), int(h * t), int(w * r), int(h * b)))
    bgra = np.array(crop.convert("RGBA"))[:, :, [2, 1, 0, 3]]
    reader = ScreenReader.__new__(ScreenReader)
    reader.engine = OcrEngine.try_create_from_user_profile_languages()
    reader.loop = asyncio.new_event_loop()
    lines = reader.read_lines(bgra)
    print("Text found in the prompt area:")
    for line in lines:
        print("   ", line)
    print("Chest prompt detected:", "YES" if looks_like_chest_prompt(lines, loose) else "no")
    crop.save(os.path.join(HERE, "test_image_crop.png"))
    print("Saved the area it looked at to test_image_crop.png")


def main():
    ap = argparse.ArgumentParser(description="Counts the chests you open in Fortnite.")
    ap.add_argument("--key", default="E", help="interact key (default E)")
    ap.add_argument("--pad-button", default="X", help="controller interact button A/B/X/Y, or 'none' (default X)")
    ap.add_argument("--region", default=None, help="screen area to read: left,top,right,bottom as fractions")
    ap.add_argument("--monitor", type=int, default=1, help="monitor number (default 1 = main)")
    ap.add_argument("--min-hold", type=float, default=0.15, help="seconds interact must be held (default 0.15)")
    ap.add_argument("--boost", type=float, default=DEFAULT_BOOST, help="Treasure Hours multiplier (default 1.5)")
    ap.add_argument("--loose", action="store_true", help="count any prompt containing 'chest' (if it misses chests)")
    ap.add_argument("--any-window", action="store_true", help="don't require Fortnite to be the active window")
    ap.add_argument("--quiet", action="store_true", help="no beep when a chest is counted")
    ap.add_argument("--debug", action="store_true", help="print the text it reads and save debug_crop.png")
    ap.add_argument("--test-image", help="run detection on a screenshot file and exit")
    ap.add_argument("--reset", action="store_true", help="set the count back to zero")
    args = ap.parse_args()

    region = tuple(float(x) for x in args.region.split(",")) if args.region else DEFAULT_REGION

    if args.test_image:
        test_image(args.test_image, region, args.loose)
        return

    data = load_counts()
    if args.reset:
        data = {"normal": 0, "treasure": 0, "log": []}
        save_counts(data)
        print("Count reset to zero.")

    pad_mask = 0 if args.pad_button.lower() == "none" else PAD_BUTTONS[args.pad_button.upper()]
    watcher = InteractWatcher(vk_from_name(args.key), pad_mask)
    watcher.start()
    reader = ScreenReader(region, args.monitor)
    detector = ChestDetector(min_hold=args.min_hold)
    treasure_mode = False
    hotkeys = {"add": vk_from_name("F7"), "undo": vk_from_name("F8"), "treasure": vk_from_name("F9")}
    hotkey_was_down = {k: False for k in hotkeys}

    def add_chest(source):
        bucket = "treasure" if treasure_mode else "normal"
        data[bucket] += 1
        data["log"].append({"time": datetime.now().isoformat(timespec="seconds"), "treasure": treasure_mode, "source": source})
        save_counts(data)
        if not args.quiet:
            beep()
        print(f"\n[+] Chest opened ({source}). {odds_line(data, args.boost)}")

    print("Fortnite chest counter running. F7 = add, F8 = undo, F9 = Treasure Hours, Ctrl+C = quit")
    print(f"Interact: key {args.key.upper()}" + (f" / controller {args.pad_button.upper()}" if pad_mask else ""))
    print("Starting at:", odds_line(data, args.boost))
    if not _xinput and pad_mask:
        print("(No XInput found - controller detection off)")

    last_text, last_debug_save = None, 0.0
    try:
        while True:
            frame_start = time.monotonic()

            # hotkeys
            for name, vk in hotkeys.items():
                down = key_down(vk)
                if down and not hotkey_was_down[name]:
                    if name == "add":
                        add_chest("manual")
                    elif name == "undo" and data["log"]:
                        last = data["log"].pop()
                        data["treasure" if last["treasure"] else "normal"] -= 1
                        save_counts(data)
                        print(f"\n[-] Undid last chest. {odds_line(data, args.boost)}")
                    elif name == "treasure":
                        treasure_mode = not treasure_mode
                        print(f"\nTreasure Hours {'ON - chests count with the ' + str(args.boost) + 'x boost' if treasure_mode else 'OFF'}")
                hotkey_was_down[name] = down

            focused = args.any_window or fortnite_focused()
            if not focused:
                detector.reset_prompt()
                sys.stdout.write("\r  Waiting for Fortnite to be the active window...      ")
                sys.stdout.flush()
                time.sleep(0.5)
                continue

            frame = reader.grab()
            lines = reader.read_lines(frame)
            visible = looks_like_chest_prompt(lines, args.loose)
            now = time.monotonic()
            if detector.update(now, visible, watcher.snapshot(now)):
                add_chest("auto")

            if args.debug:
                text = " | ".join(lines)
                if text != last_text:
                    print(f"\n[debug] chest prompt={visible}  text: {text[:150]}")
                    last_text = text
                if now - last_debug_save > 3:
                    from PIL import Image
                    Image.fromarray(frame[:, :, [2, 1, 0]]).save(os.path.join(HERE, "debug_crop.png"))
                    last_debug_save = now

            total = data["normal"] + data["treasure"]
            status = f"\r  Chests: {total:,}  | prompt: {'CHEST' if visible else '-----'}  | Treasure Hours: {'ON ' if treasure_mode else 'off'}   "
            sys.stdout.write(status)
            sys.stdout.flush()

            time.sleep(max(0.0, 0.15 - (time.monotonic() - frame_start)))
    except KeyboardInterrupt:
        print("\n\nStopped. Final:", odds_line(data, args.boost))
        print(f"Counts saved to {COUNTS_FILE}")


if __name__ == "__main__":
    main()
