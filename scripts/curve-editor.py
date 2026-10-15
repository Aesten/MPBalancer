#!/usr/bin/env python3
"""Editor for MPBalancer's speed curves (mounted: thrust_curve_spear, thrust_curve_lance for weapons that can couch;
thrust_curve_foot for attackers on foot): a melee
thrust does this % of vanilla's damage for the same hit at its closing speed along the thrust, 0 to 25 m/s in steps of
1 m/s. 100% = vanilla (the mod never goes above it).

  python scripts/curve-editor.py

The defaults live in src/MPBalancer/defaults.txt (built into the mod). Drag the points on the chart (dragged points snap to 0.5%; typed and loaded values stay exact),
or type in the boxes under it, then:
  Save as default    writes the curve to defaults.txt (the mod uses it after a rebuild and deploy)
  Apply to server    sends it to the running server (ssh, mpm cmd); takes effect on the next hit, logged under a new
                     settings number, until the next restart
  Copy command       copies "!b thrust_curve_spear 100 100 ..." (26 values, one per m/s) for the game chat or mpm cmd
  Load default       reads defaults.txt; Load from server reads the server's current curve
The dashed line is the curve as last loaded, for comparison; the grey line is 100% (vanilla).
MPM_HOST overrides the ssh target (default bannerlord@7900x). Uses only Python's standard library (Tkinter).
"""
import os
import re
import subprocess
import tkinter as tk
from tkinter import messagebox, ttk

MAX_SPEED = 25
SPEEDS = list(range(MAX_SPEED + 1))
HOST = os.environ.get("MPM_HOST", "bannerlord@7900x")
DEFAULTS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "MPBalancer", "defaults.txt")
CURVES = {"thrust_curve_spear": "Mounted thrusts, spears and other non-lances", "thrust_curve_lance": "Mounted thrusts, lances",
          "thrust_curve_foot": "Thrusts on foot (infantry, archers, dismounted cavalry)"}
STEP = 0.5
Y_LIMIT = 500  # the mod's maximum per point
W, H, PAD_L, PAD_R, PAD_T, PAD_B = 900, 380, 50, 20, 20, 40  # initial chart size; it follows the window


def parse_curve(text):
    """'0:100 4:100 ...' or '100 100 ...' -> percentages at 0..25 m/s (interpolated like the mod, flat beyond the ends)."""
    tokens = text.split()
    if tokens and all(":" not in t for t in tokens):
        points = [(float(i), float(t)) for i, t in enumerate(tokens)]
    else:
        points = sorted((float(a), float(b)) for a, b in (t.split(":") for t in tokens))
    values = []
    for s in SPEEDS:
        if s <= points[0][0]:
            values.append(points[0][1])
        elif s >= points[-1][0]:
            values.append(points[-1][1])
        else:
            for (s0, p0), (s1, p1) in zip(points, points[1:]):
                if s0 <= s <= s1:
                    values.append(p0 + (p1 - p0) * (s - s0) / (s1 - s0))
                    break
    return [round(v, 2) for v in values]


def fmt(value):
    return ("%.2f" % value).rstrip("0").rstrip(".")


def read_default(name):
    with open(DEFAULTS, encoding="utf-8") as f:
        for line in f:
            if line.split("=")[0].strip() == name and "=" in line:
                return line.split("=", 1)[1].strip()
    raise KeyError(name + " isn't in " + DEFAULTS)


def write_default(name, text):
    with open(DEFAULTS, encoding="utf-8") as f:
        lines = f.read().split("\n")
    for i, line in enumerate(lines):
        if "=" in line and line.split("=")[0].strip() == name and not line.lstrip().startswith("#"):
            lines[i] = "%s = %s" % (name, text)
            break
    else:
        raise KeyError(name + " isn't in " + DEFAULTS)
    with open(DEFAULTS, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))


def mpm(command):
    result = subprocess.run(["ssh", HOST, "~/bannerlord/mpm cmd " + command], capture_output=True, text=True, timeout=40)
    return (result.stdout + result.stderr).strip()


class Editor:
    def __init__(self, root):
        self.root = root
        root.title("MPBalancer speed curve editor")
        self.curve = tk.StringVar(value="thrust_curve_spear")
        self.values = [tk.DoubleVar() for _ in SPEEDS]
        self.reference = []
        self.drag = None

        top = ttk.Frame(root, padding=8)
        top.pack(fill="x")
        ttk.Label(top, text="Curve:").pack(side="left")
        box = ttk.Combobox(top, textvariable=self.curve, values=list(CURVES), state="readonly", width=20)
        box.pack(side="left", padx=4)
        box.bind("<<ComboboxSelected>>", lambda e: self.load_default())
        self.caption = ttk.Label(top, text="")
        self.caption.pack(side="left", padx=10)
        for text, action in (("Load default", self.load_default), ("Load from server", self.load_server),
                             ("Copy command", self.copy), ("Apply to server", self.apply), ("Save as default", self.save)):
            ttk.Button(top, text=text, command=action).pack(side="right", padx=3)

        self.w, self.h = W, H
        self.canvas = tk.Canvas(root, width=W, height=H, background="white", highlightthickness=0)
        self.canvas.pack(padx=8, fill="both", expand=True)
        self.canvas.bind("<Configure>", self.on_resize)
        self.canvas.bind("<Button-1>", self.on_press)
        self.canvas.bind("<B1-Motion>", self.on_drag)
        self.canvas.bind("<ButtonRelease-1>", lambda e: setattr(self, "drag", None))

        grid = ttk.Frame(root, padding=(8, 4))
        grid.pack()
        for i in SPEEDS:
            ttk.Label(grid, text=str(i), width=4, anchor="center").grid(row=0, column=i)
            spin = ttk.Spinbox(grid, from_=0, to=Y_LIMIT, increment=STEP, textvariable=self.values[i], width=4,
                               command=self.redraw)
            spin.grid(row=1, column=i, padx=1)
            spin.bind("<KeyRelease>", lambda e: self.redraw())
        ttk.Label(grid, text="closing speed (m/s) →  % of vanilla damage").grid(row=2, column=0, columnspan=MAX_SPEED + 1, pady=(4, 0))

        self.command = tk.StringVar()
        ttk.Entry(root, textvariable=self.command, width=140).pack(padx=8, pady=(4, 8), fill="x")
        self.load_default()

    # --- values -------------------------------------------------------------------------------------------------
    def set_values(self, values, reference=True):
        for var, v in zip(self.values, values):
            var.set(v)
        if reference:
            self.reference = list(values)
        self.redraw()

    def current(self):
        out = []
        for var in self.values:
            try:
                out.append(max(0.0, min(float(Y_LIMIT), round(float(var.get()), 2))))
            except (tk.TclError, ValueError):
                out.append(0)
        return out

    def load_default(self):
        name = self.curve.get()
        self.caption.config(text=CURVES[name])
        try:
            self.set_values(parse_curve(read_default(name)))
        except (OSError, KeyError, ValueError) as e:
            messagebox.showerror("Load default", str(e))

    def save(self):
        name = self.curve.get()
        text = self.values_text()
        if not messagebox.askyesno("Save as default", "Write to defaults.txt?\n\n%s = %s\n\nThe mod uses it after a rebuild "
                                   "(dotnet build) and deploy (scripts/deploy.sh)." % (name, text)):
            return
        try:
            write_default(name, text)
        except (OSError, KeyError) as e:
            messagebox.showerror("Save as default", str(e))
            return
        self.reference = self.current()
        self.redraw()

    def load_server(self):
        name = self.curve.get()
        try:
            text = mpm("b " + name)
        except Exception as e:
            messagebox.showerror("Load from server", str(e))
            return
        m = re.search(name + r" = ([0-9.: ]+?)(?: \(|$)", text)
        if not m:
            messagebox.showerror("Load from server", "Unexpected answer:\n" + text)
            return
        self.set_values(parse_curve(m.group(1)))

    def values_text(self):
        return " ".join(fmt(v) for v in self.current())

    def command_text(self):
        return "!b %s %s" % (self.curve.get(), self.values_text())

    def copy(self):
        self.root.clipboard_clear()
        self.root.clipboard_append(self.command_text())

    def apply(self):
        command = "b %s %s" % (self.curve.get(), self.values_text())
        if not messagebox.askyesno("Apply to server", "Send to %s?\n\n%s" % (HOST, command)):
            return
        try:
            answer = mpm(command)
        except Exception as e:
            answer = str(e)
        messagebox.showinfo("Apply to server", answer or "(no answer)")

    # --- chart --------------------------------------------------------------------------------------------------
    def y_max(self):
        # Fixed while dragging, so the point stays under the mouse; otherwise room above the highest value.
        if self.drag is None or not hasattr(self, "_y_max"):
            top = max(self.current() + (self.reference or [0]) + [100])
            self._y_max = max(150, int((top + 49) // 50 * 50 + 50))
        return self._y_max

    def xy(self, speed, percent):
        x = PAD_L + (self.w - PAD_L - PAD_R) * speed / MAX_SPEED
        y = self.h - PAD_B - (self.h - PAD_T - PAD_B) * percent / self.y_max()
        return x, y

    def redraw(self):
        c = self.canvas
        c.delete("all")
        step = 25 if self.y_max() <= 200 else 50
        for p in range(0, self.y_max() + 1, step):
            x0, y = self.xy(0, p)
            x1, _ = self.xy(MAX_SPEED, p)
            c.create_line(x0, y, x1, y, fill="#eef1f4")
            c.create_text(x0 - 6, y, text="%d%%" % p, anchor="e", fill="#5b6570", font=("Segoe UI", 8))
        for s in range(0, MAX_SPEED + 1, 5):
            x, y0 = self.xy(s, 0)
            _, y1 = self.xy(s, self.y_max())
            c.create_line(x, y0, x, y1, fill="#eef1f4")
            c.create_text(x, y0 + 12, text="%d m/s" % s, fill="#5b6570", font=("Segoe UI", 8))
        x0, y = self.xy(0, 100)
        x1, _ = self.xy(MAX_SPEED, 100)
        c.create_line(x0, y, x1, y, fill="#9aa3ad", width=1)
        c.create_text(x1, y - 8, text="vanilla", anchor="e", fill="#9aa3ad", font=("Segoe UI", 8))
        if self.reference:
            c.create_line(*[v for s in SPEEDS for v in self.xy(s, self.reference[s])], fill="#b07aa1", dash=(4, 3))
        values = self.current()
        c.create_line(*[v for s in SPEEDS for v in self.xy(s, values[s])], fill="#2f6db5", width=2)
        for s in SPEEDS:
            x, y = self.xy(s, values[s])
            c.create_oval(x - 4, y - 4, x + 4, y + 4, fill="#2f6db5", outline="white")
        self.command.set(self.command_text())

    def on_resize(self, event):
        self.w, self.h = max(event.width, PAD_L + PAD_R + 100), max(event.height, PAD_T + PAD_B + 60)
        self.redraw()

    def nearest(self, x):
        return min(SPEEDS, key=lambda s: abs(self.xy(s, 0)[0] - x))

    def on_press(self, event):
        self.drag = self.nearest(event.x)
        self.on_drag(event)

    def on_drag(self, event):
        if self.drag is None:
            return
        percent = (self.h - PAD_B - event.y) / (self.h - PAD_T - PAD_B) * self.y_max()
        self.values[self.drag].set(round(max(0, min(Y_LIMIT, percent)) / STEP) * STEP)
        self.redraw()


if __name__ == "__main__":
    root = tk.Tk()
    root.minsize(760, 420)
    Editor(root)
    root.mainloop()
