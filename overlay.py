"""
Always-on-top overlay that sits over Fortnite and shows your chest count.

  F6           switch between "move" mode (drag it anywhere) and "locked"
               mode (clicks go straight through it into the game)
  Right-click  menu: Treasure Hours, add / undo a chest, lock, quit

Whenever Fortnite is the active window and has the mouse captured (cursor
hidden or confined), the overlay is forced click-through no matter which
mode it's in, so clicking can never pull focus away from the game.
Move it while the cursor is visible (Fortnite menus, or alt-tabbed).

Works over Fortnite in Windowed Fullscreen or Windowed mode (no overlay
can draw over exclusive fullscreen). It never takes focus from the game.
"""

import ctypes
import json
import os
import tkinter as tk

user32 = ctypes.windll.user32

GWL_EXSTYLE = -20
WS_EX_LAYERED = 0x00080000
WS_EX_TRANSPARENT = 0x00000020    # clicks pass through
WS_EX_TOOLWINDOW = 0x00000080     # no taskbar / alt-tab entry
WS_EX_NOACTIVATE = 0x08000000     # clicking it doesn't steal focus from the game
HWND_TOPMOST = -1
SWP_FLAGS = 0x0001 | 0x0002 | 0x0010  # NOSIZE | NOMOVE | NOACTIVATE

KEY = "#010203"          # transparent colour (nearly the panel colour, so edges don't fringe)
BG = "#14161c"
EDGE = "#2b2f3a"
ACCENT = "#4c9cff"
GOLD = "#ffc94a"
TEXT = "#f2f3f5"
DIM = "#8a909c"
GREEN = "#3ddc84"
ORANGE = "#ff9a3c"

W, H = 250, 96


class _POINT(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]


class _CURSORINFO(ctypes.Structure):
    _fields_ = [("cbSize", ctypes.c_uint), ("flags", ctypes.c_uint),
                ("hCursor", ctypes.c_void_p), ("ptScreenPos", _POINT)]


class _RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long), ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


def cursor_captured():
    """True when a game has grabbed the mouse: cursor hidden, or confined to part of the screen."""
    info = _CURSORINFO()
    info.cbSize = ctypes.sizeof(_CURSORINFO)
    if user32.GetCursorInfo(ctypes.byref(info)) and not (info.flags & 1):   # CURSOR_SHOWING
        return True
    clip = _RECT()
    if user32.GetClipCursor(ctypes.byref(clip)):
        vx, vy = user32.GetSystemMetrics(76), user32.GetSystemMetrics(77)    # virtual screen origin
        vw, vh = user32.GetSystemMetrics(78), user32.GetSystemMetrics(79)    # virtual screen size
        full = clip.left <= vx and clip.top <= vy and clip.right >= vx + vw and clip.bottom >= vy + vh
        return not full
    return False


def _default_game_has_mouse():
    from chest_counter import fortnite_focused
    return fortnite_focused() and cursor_captured()


def _get_long(hwnd, idx):
    fn = getattr(user32, "GetWindowLongPtrW", None) or user32.GetWindowLongW
    fn.restype = ctypes.c_ssize_t
    return fn(hwnd, idx)


def _set_long(hwnd, idx, val):
    fn = getattr(user32, "SetWindowLongPtrW", None) or user32.SetWindowLongW
    fn.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_ssize_t]
    fn(hwnd, idx, val)


class Overlay:
    def __init__(self, state, settings_path, game_has_mouse=_default_game_has_mouse):
        self.state = state
        self.settings_path = settings_path
        self.settings = self._load_settings()
        self.locked = self.settings.get("locked", False)
        self.game_has_mouse = game_has_mouse
        self.in_game = False          # Fortnite has the cursor captured -> always click-through
        self.applied = None           # click-through state last applied to the window
        self.flash_until = 0.0
        self.seen_total = None
        prev_foreground = user32.GetForegroundWindow()

        self.root = tk.Tk()
        self.root.title("Chest-Tracker overlay")
        self.root.overrideredirect(True)
        self.root.attributes("-topmost", True)
        self.root.attributes("-alpha", self.settings.get("opacity", 0.9))
        self.root.configure(bg=KEY)
        self.root.attributes("-transparentcolor", KEY)
        sw, sh = self.root.winfo_screenwidth(), self.root.winfo_screenheight()
        x = min(max(0, self.settings.get("x", 24)), sw - 60)
        y = min(max(0, self.settings.get("y", int(sh * 0.32))), sh - 40)
        self.root.geometry(f"{W}x{H}+{x}+{y}")

        self.c = tk.Canvas(self.root, width=W, height=H, bg=KEY, highlightthickness=0, bd=0)
        self.c.pack()
        self.panel = self._round_rect(2, 2, W - 2, H - 2, 14, fill=BG, outline=EDGE, width=2)
        self.c.create_text(16, 15, text="CHESTS OPENED", anchor="w", fill=DIM, font=("Segoe UI Semibold", 8))
        self.dot = self.c.create_oval(W - 22, 9, W - 12, 19, fill=DIM, outline="")
        self.th = self.c.create_text(W - 30, 15, text="", anchor="e", fill=ORANGE, font=("Segoe UI Semibold", 8))
        self.count = self.c.create_text(14, 44, text="0", anchor="w", fill=TEXT, font=("Segoe UI", 26, "bold"))
        self.pct = self.c.create_text(W - 14, 38, text="0%", anchor="e", fill=GOLD, font=("Segoe UI Semibold", 12))
        self.one_in = self.c.create_text(W - 14, 56, text="pirate token odds", anchor="e", fill=DIM, font=("Segoe UI", 8))
        self.hint = self.c.create_text(W // 2, H - 13, text="", fill=DIM, font=("Segoe UI", 8))

        self.menu = tk.Menu(self.root, tearoff=0)
        self.menu.add_command(label="Treasure Hours on/off  (F9)", command=state.toggle_treasure)
        self.menu.add_command(label="Add a chest  (F7)", command=lambda: state.add_chest("manual"))
        self.menu.add_command(label="Undo last chest  (F8)", command=state.undo)
        self.menu.add_separator()
        self.menu.add_command(label="Lock in place  (F6)", command=self.toggle_lock)
        self.menu.add_command(label="More see-through", command=lambda: self._opacity(-0.1))
        self.menu.add_command(label="Less see-through", command=lambda: self._opacity(+0.1))
        self.menu.add_separator()
        self.menu.add_command(label="Quit Chest-Tracker", command=self.quit)

        self.c.bind("<ButtonPress-1>", self._drag_start)
        self.c.bind("<B1-Motion>", self._drag_move)
        self.c.bind("<ButtonRelease-1>", lambda e: self._save_settings())
        self.c.bind("<Button-3>", self._show_menu)

        self.root.update_idletasks()
        self.hwnd = user32.GetParent(self.root.winfo_id()) or self.root.winfo_id()
        self._apply_styles()
        self.root.update()
        # Opening a window can briefly take focus - hand it straight back (e.g. to Fortnite).
        if prev_foreground and user32.GetForegroundWindow() != prev_foreground:
            user32.SetForegroundWindow(prev_foreground)
        self._tick()
        self._keep_on_top()

    # ---------------------------------------------------------------- drawing
    def _round_rect(self, x1, y1, x2, y2, r, **kw):
        pts = [x1 + r, y1, x2 - r, y1, x2, y1, x2, y1 + r, x2, y2 - r, x2, y2,
               x2 - r, y2, x1 + r, y2, x1, y2, x1, y2 - r, x1, y1 + r, x1, y1]
        return self.c.create_polygon(pts, smooth=True, **kw)

    def _tick(self):
        import time
        s = self.state.snapshot()
        if not s["running"]:
            self.quit()
            return
        if s["toggle_lock"]:
            self.toggle_lock()
        try:
            in_game = bool(self.game_has_mouse())
        except Exception:
            in_game = False
        if in_game != self.in_game:
            self.in_game = in_game
            if in_game:
                self.menu.unpost()
            self._apply_styles()

        total = s["normal"] + s["treasure"]
        now = time.monotonic()
        if self.seen_total is not None and total > self.seen_total:
            self.flash_until = now + 0.9
        self.seen_total = total

        self.c.itemconfigure(self.count, text=f"{total:,}", fill=GOLD if now < self.flash_until else TEXT)
        chance = s["chance"]
        self.c.itemconfigure(self.pct, text=f"{chance * 100:.4f}%" if chance < 0.01 else f"{chance * 100:.2f}%")
        self.c.itemconfigure(self.one_in, text=f"1 in {1 / chance:,.0f} for the token" if chance else "pirate token odds")
        self.c.itemconfigure(self.th, text=f"TREASURE HOURS {s['boost']:g}x" if s["treasure_mode"] else "")
        if s["prompt"]:
            dot = GOLD
        elif s["focused"]:
            dot = GREEN
        else:
            dot = DIM
        self.c.itemconfigure(self.dot, fill=dot)
        self.root.after(150, self._tick)

    def _keep_on_top(self):
        # Fortnite can grab the top spot when it gets focus; take it back every couple of seconds.
        user32.SetWindowPos(self.hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_FLAGS)
        self.root.after(2000, self._keep_on_top)

    # ---------------------------------------------------------------- lock / move
    @property
    def click_through(self):
        return self.locked or self.in_game

    def _apply_styles(self):
        through = self.click_through
        if through != self.applied:
            ex = _get_long(self.hwnd, GWL_EXSTYLE) | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            if through:
                ex |= WS_EX_TRANSPARENT
            else:
                ex &= ~WS_EX_TRANSPARENT
            _set_long(self.hwnd, GWL_EXSTYLE, ex)
            user32.SetWindowPos(self.hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_FLAGS | 0x0020)  # FRAMECHANGED
            self.applied = through
        self.c.itemconfigure(self.panel, outline=EDGE if through else ACCENT, dash=() if through else (4, 3))
        if self.in_game:
            hint = "click-through while you play"
        elif self.locked:
            hint = "F6 to move"
        else:
            hint = "drag to move  ·  right-click menu  ·  F6 lock"
        self.c.itemconfigure(self.hint, text=hint)
        self.menu.entryconfigure(4, label="Unlock  (F6)" if self.locked else "Lock in place  (F6)")

    def _show_menu(self, e):
        if not self.click_through:      # never pop a menu (which takes focus) while the game has the mouse
            self.menu.tk_popup(e.x_root, e.y_root)

    def toggle_lock(self):
        self.locked = not self.locked
        self._apply_styles()
        self._save_settings()

    def _drag_start(self, e):
        self._dx, self._dy = e.x, e.y

    def _drag_move(self, e):
        if self.click_through:
            return
        x = self.root.winfo_x() + e.x - self._dx
        y = self.root.winfo_y() + e.y - self._dy
        self.root.geometry(f"+{x}+{y}")

    def _opacity(self, delta):
        val = min(1.0, max(0.3, round(self.root.attributes("-alpha") + delta, 2)))
        self.root.attributes("-alpha", val)
        self._save_settings()

    # ---------------------------------------------------------------- settings
    def _load_settings(self):
        try:
            with open(self.settings_path, encoding="utf-8") as f:
                return json.load(f)
        except (OSError, ValueError):
            return {}

    def _save_settings(self):
        data = {"x": self.root.winfo_x(), "y": self.root.winfo_y(), "locked": self.locked,
                "opacity": self.root.attributes("-alpha")}
        try:
            with open(self.settings_path, "w", encoding="utf-8") as f:
                json.dump(data, f)
        except OSError:
            pass

    # ---------------------------------------------------------------- run
    def quit(self):
        self.state.stop()
        try:
            self.root.destroy()
        except tk.TclError:
            pass

    def run(self):
        def wake():           # lets Ctrl+C in the console reach Python while Tk is running
            self.root.after(250, wake)
        wake()
        try:
            self.root.mainloop()
        except KeyboardInterrupt:
            self.quit()
