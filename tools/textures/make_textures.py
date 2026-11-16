"""Firstlight material textures, drawn with PixelForge.

Every feature is sized in metres and drawn at a given density (pixels per metre), so one script makes the
8, 16 and 32 px/m sets used for the density comparison. Tiles are seamless (wrap=True canvases).

Run with PixelForge's venv:
  C:/Users/niczm/Downloads/PixelForge/.venv/Scripts/python.exe tools/textures/make_textures.py
Writes Assets/Resources/Textures/d{8,16,32}/<name>.png, Assets/StreamingAssets/Data/materials.json and
runs/textures_preview.png (a contact sheet for review).
"""
import json
import math
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, "C:/Users/niczm/Downloads/PixelForge")
import pixelforge as pf  # noqa: E402
from pixelforge.palette import Palette, make_ramp  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets" / "Resources" / "Textures"
DENSITIES = [64]   # chosen by Nico 2026-11-13 (comparison of 8-128 in docs/art)
TILE_METRES = {8: 8.0, 16: 8.0, 32: 8.0, 64: 8.0, 128: 8.0}   # 64 ... 1024 px tiles

# Muted, natural base colours (Firstlight is neutral, not cozy). One 5-step hue-shifted ramp each.
BASES = {
    "grass": "#6c7a47", "dirt": "#7a6449", "bank": "#958866", "bed": "#5b5946", "litter": "#5b6340",
    "rockg": "#7d786b", "rock": "#88837a", "lichen": "#7b8263", "water": "#4f6a74", "shallow": "#6a8483",
    "barkc": "#5a4535", "barkb": "#6c6355", "needle": "#4a5d3f", "leaf": "#5f7341", "fur": "#86684b",
    "berry": "#6e4652", "deer": "#7e6149", "deerpale": "#bfae90", "skin": "#a57c63", "hide": "#8b7354", "hair": "#3a302a", "wood": "#9b8566", "bloom": "#b9a676", "bloom2": "#8f7f9a", "snow": "#d3d8d8",
    "brush": "#5f5040", "dryleaf": "#7a6a48", "straw": "#a08c5c", "wicker": "#94795a", "ash": "#6f6a64",
    "char": "#3b3531", "ember": "#b4602e", "flame": "#d9893a", "clay": "#9a6f55", "meat": "#8c4c40",
    "soil": "#5f4b38", "crop": "#a6915a",
    # Agricultural Age (phase 3): young and green crops, stubble, flax, worn paths, dressed stone, fired clay, linen,
    # ore stains and worked metal. Same muted, natural range.
    "shoot": "#6f8047", "cropg": "#7a8a4c", "stubble": "#a08a5c", "flax": "#7f8f6a", "flaxbloom": "#7d8aa6",
    "path": "#806a50", "dressed": "#8d877c", "potter": "#a5694a", "linen": "#b3a98e", "verd": "#5f8a74",
    "tin": "#7c7c84", "metal": "#b07a48", "daub": "#a8946f",
}
PAL = Palette.from_ramps({k: make_ramp(v, 5, step_L=0.075, hue_shift=10) for k, v in BASES.items()}, name="firstlight")
pf.use_palette(PAL)


class Painter:
    """Helpers that size everything in metres at density d."""

    def __init__(self, d, size_px, seed, wrap=True):
        self.d = d
        self.rng = np.random.default_rng(seed)
        self.cv = pf.Canvas(size_px[0], size_px[1], PAL, fill=0, wrap=wrap)

    def px(self, metres, minimum=1):
        return max(minimum, int(round(metres * self.d)))

    def r(self, a, b=None):
        return self.rng.uniform(a, b) if b is not None else self.rng.uniform(0, a)

    def ri(self, a, b):
        return int(self.rng.integers(a, b))

    def fill(self, c):
        self.cv.rect(0, 0, self.cv.w, self.cv.h, c)

    def noise(self, sx_m, sy_m=None, octaves=3):
        """Seamless value noise in 0..1 with features about sx_m x sy_m metres (wraps at the tile edge)."""
        sy_m = sy_m or sx_m
        w, h = self.cv.w, self.cv.h
        out = np.zeros((h, w))
        amp, total = 1.0, 0.0
        for o in range(octaves):
            nx = max(1, int(round(w / (sx_m * self.d) * 2 ** o)))
            ny = max(1, int(round(h / (sy_m * self.d) * 2 ** o)))
            g = self.rng.random((ny, nx))
            xs = np.arange(w) / w * nx
            ys = np.arange(h) / h * ny
            x0 = np.floor(xs).astype(int); y0 = np.floor(ys).astype(int)
            fx = xs - x0; fy = ys - y0
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy)
            x1 = (x0 + 1) % nx; y1 = (y0 + 1) % ny
            a = g[np.ix_(y0, x0)]; b = g[np.ix_(y0, x1)]; c = g[np.ix_(y1, x0)]; d = g[np.ix_(y1, x1)]
            top = a + (b - a) * fx[None, :]
            bot = c + (d - c) * fx[None, :]
            out += (top + (bot - top) * fy[:, None]) * amp
            total += amp
            amp *= 0.5
        out /= total
        lo, hi = np.percentile(out, 1), np.percentile(out, 99)
        return np.clip((out - lo) / max(1e-6, hi - lo), 0, 1)

    def bands(self, ramp, tones, cuts, sx_m, sy_m=None, octaves=3):
        """Fill with organic patches: noise split at `cuts` into the given ramp tones."""
        n = self.noise(sx_m, sy_m, octaves)
        idx = np.digitize(n, cuts)
        for i, tone in enumerate(tones):
            self.cv.fill_mask(idx == i, f"{ramp}{tone}")

    def blobs(self, color, count_per_m2, size_m, jitter=0.5, clip=None):
        """Irregular clusters (2+ px) - texture in clumps, never single-pixel noise above 8 px/m."""
        area = self.cv.w * self.cv.h / (self.d * self.d)
        for _ in range(int(area * count_per_m2)):
            w = self.px(size_m * self.r(1 - jitter, 1 + jitter))
            h = self.px(size_m * self.r(1 - jitter, 1 + jitter) * 0.8)
            self.cv.ellipse(self.ri(0, self.cv.w), self.ri(0, self.cv.h), w, h, color, clip=clip)

    def strokes(self, color, count_per_m2, length_m, angle_deg, spread_deg=15, clip=None):
        area = self.cv.w * self.cv.h / (self.d * self.d)
        for _ in range(int(area * count_per_m2)):
            L = self.px(length_m * self.r(0.6, 1.3))
            a = math.radians(angle_deg + self.r(-spread_deg, spread_deg))
            x, y = self.ri(0, self.cv.w), self.ri(0, self.cv.h)
            self.cv.line(x, y, int(round(x + math.cos(a) * L)), int(round(y - math.sin(a) * L)), color, clip=clip)

    def pebbles(self, ramp, count_per_m2, size_m):
        """Small stones lit from the top-left: a dark rim bottom-right, a light pixel top-left."""
        area = self.cv.w * self.cv.h / (self.d * self.d)
        for _ in range(int(area * count_per_m2)):
            w = self.px(size_m * self.r(0.7, 1.3), 1)
            h = max(1, int(w * self.r(0.6, 0.9)))
            x, y = self.ri(0, self.cv.w), self.ri(0, self.cv.h)
            tone = self.ri(2, 4)
            if w >= 3:
                self.cv.ellipse(x + 1, y + 1, w, h, f"{ramp}0")
                self.cv.ellipse(x, y, w, h, f"{ramp}{tone}")
                self.cv.px(x + w // 3, y + h // 3, f"{ramp}4")
            else:
                self.cv.px(x + 1, y + 1, f"{ramp}0")
                self.cv.rect(x, y, w, h, f"{ramp}{tone}")


# ---------------------------------------------------------------------------------------------- materials
# Each returns a Canvas. t = tile size in px.

def grass(d, t, seed=1):
    p = Painter(d, (t, t), seed)
    p.bands("grass", [1, 2, 3], [0.36, 0.68], 1.1)
    p.strokes("grass1", 9, 0.12, 90, 20)       # blades
    p.strokes("grass3", 6, 0.10, 90, 25)
    p.strokes("grass4", 1.2, 0.08, 90, 25)
    p.pebbles("dirt", 0.08, 0.06)
    return p.cv


def litter(d, t, seed=2):
    p = Painter(d, (t, t), seed)
    p.bands("litter", [1, 2], [0.5], 0.9)
    p.cv.fill_mask(p.noise(0.6) > 0.78, "dirt1")
    p.strokes("needle1", 6, 0.1, 30, 60)      # fallen needles
    p.strokes("litter3", 3, 0.08, 120, 60)
    p.blobs("dirt2", 1.0, 0.09)               # leaves
    p.blobs("leaf1", 0.6, 0.08)
    return p.cv


def dirt(d, t, seed=3):
    p = Painter(d, (t, t), seed)
    p.bands("dirt", [1, 2, 3], [0.33, 0.7], 0.9)
    p.pebbles("rockg", 0.6, 0.08)
    p.strokes("dirt0", 0.6, 0.25, 10, 40)     # cracks
    return p.cv


def bank(d, t, seed=4):
    p = Painter(d, (t, t), seed)
    p.bands("bank", [1, 2, 3], [0.3, 0.68], 0.8)
    p.pebbles("rockg", 2.5, 0.07)
    p.pebbles("bank", 2.0, 0.06)
    return p.cv


def bed(d, t, seed=5):
    p = Painter(d, (t, t), seed)
    p.bands("bed", [0, 1, 2], [0.3, 0.65], 0.9)
    p.pebbles("bed", 3.0, 0.08)
    return p.cv


def rock_ground(d, t, seed=6):
    p = Painter(d, (t, t), seed)
    p.fill("rockg1")
    # Irregular flat stones set in the soil.
    area = t * t / (d * d)
    for _ in range(int(area * 2.2)):
        w = p.px(p.r(0.3, 0.6), 2); h = max(2, int(w * p.r(0.55, 0.85)))
        x, y = p.ri(0, t), p.ri(0, t)
        p.cv.rrect(x + 1, y + 1, w, h, max(1, w // 4), "rockg0")
        p.cv.rrect(x, y, w, h, max(1, w // 4), f"rockg{p.ri(2, 4)}")
        p.cv.line(x + 1, y, x + w - 2, y, "rockg4")
    p.blobs("lichen2", 0.4, 0.1)
    p.strokes("grass1", 2, 0.1, 90, 20)
    return p.cv


def rock(d, t, seed=7):
    p = Painter(d, (t, t), seed)
    p.bands("rock", [1, 2, 3], [0.33, 0.66], 0.7, 0.45)
    # Cracks with a lit lip on their upper-left side.
    area = t * t / (d * d)
    for _ in range(int(area * 2.0)):
        x, y = p.ri(0, t), p.ri(0, t)
        pts = [(x, y)]
        for _ in range(p.ri(2, 4)):
            x += p.ri(-p.px(0.25), p.px(0.25) + 1); y += p.ri(1, p.px(0.25) + 2)
            pts.append((x, y))
        p.cv.polyline([(a - 1, b) for a, b in pts], "rock4")
        p.cv.polyline(pts, "rock0")
    p.blobs("lichen1", 0.25, 0.15)
    p.blobs("lichen3", 0.25, 0.1)
    return p.cv


def water(d, t, seed=8):
    p = Painter(d, (t, t), seed)
    p.bands("water", [1, 2], [0.45], 2.0, 0.6)
    p.strokes("water3", 2.0, 0.45, 0, 4)       # ripples, mostly horizontal
    p.strokes("water1", 1.5, 0.5, 0, 4)
    p.strokes("water4", 0.4, 0.2, 0, 4)
    return p.cv


def shallow(d, t, seed=9):
    p = Painter(d, (t, t), seed)
    p.fill("shallow2")
    p.pebbles("bed", 1.5, 0.08)
    p.strokes("shallow3", 2.0, 0.4, 0, 4)
    p.strokes("shallow4", 0.5, 0.2, 0, 4)
    return p.cv


def bark(d, t, ramp, seed):
    p = Painter(d, (t, t), seed)
    p.fill(f"{ramp}2")
    p.strokes(f"{ramp}1", 6, 0.45, 90, 4)      # vertical furrows
    p.strokes(f"{ramp}0", 3, 0.35, 90, 3)
    p.strokes(f"{ramp}3", 3, 0.3, 90, 4)
    p.blobs(f"{ramp}3", 0.5, 0.1)
    if ramp == "barkb":
        p.blobs("lichen2", 0.4, 0.1)
    return p.cv


def needles(d, t, seed=12):
    p = Painter(d, (t, t), seed)
    p.fill("needle1")
    p.strokes("needle2", 14, 0.14, 60, 25)
    p.strokes("needle2", 14, 0.14, 120, 25)
    p.strokes("needle3", 5, 0.1, 60, 30)
    p.strokes("needle0", 4, 0.12, 120, 30)
    return p.cv


def leaves(d, t, ramp="leaf", seed=13):
    p = Painter(d, (t, t), seed)
    p.fill(f"{ramp}0")
    p.blobs(f"{ramp}1", 8, 0.16)
    p.blobs(f"{ramp}2", 7, 0.13)
    p.blobs(f"{ramp}3", 3.5, 0.1)
    p.blobs(f"{ramp}4", 0.8, 0.07)
    return p.cv


def fur(d, t, seed=14):
    p = Painter(d, (t, t), seed)
    p.fill("fur2")
    p.strokes("fur1", 10, 0.1, 0, 15)
    p.strokes("fur3", 6, 0.08, 0, 15)
    return p.cv


def deer(d, t, seed=16):
    """Deer coat: an even warm brown with short hairs running along the body."""
    p = Painter(d, (t, t), seed)
    p.fill("deer2")
    p.strokes("deer1", 40, 0.05, 0, 10)
    p.strokes("deer3", 25, 0.04, 0, 10)
    return p.cv


def deer_pale(d, t, seed=17):
    """Pale belly, rump and tail patches; also antlers (tinted darker on the mesh)."""
    p = Painter(d, (t, t), seed)
    p.fill("deerpale3")
    p.strokes("deerpale2", 30, 0.04, 0, 10)
    p.strokes("deerpale4", 20, 0.04, 0, 10)
    return p.cv


def skin(d, t, seed=18):
    """Skin: an even tone with faint soft patches (faces and limbs are small, so detail stays quiet)."""
    p = Painter(d, (t, t), seed)
    p.fill("skin2")
    p.blobs("skin3", 2, 0.04)
    return p.cv


def hide(d, t, seed=19):
    """Tanned hide clothing: worn, uneven leather with short stitched seams."""
    p = Painter(d, (t, t), seed)
    p.bands("hide", [2, 3], [0.62], 0.5)
    p.strokes("hide1", 20, 0.05, 0, 20)
    p.strokes("hide0", 3, 0.06, 90, 5)
    return p.cv


def hair(d, t, seed=20):
    p = Painter(d, (t, t), seed)
    p.fill("hair2")
    p.strokes("hair1", 50, 0.06, 90, 15)
    p.strokes("hair3", 25, 0.05, 90, 15)
    return p.cv


def wood(d, t, seed=21):
    """Spear shafts and tent poles: pale stripped wood with grain."""
    p = Painter(d, (t, t), seed)
    p.fill("wood2")
    p.strokes("wood1", 30, 0.12, 90, 4)
    p.strokes("wood3", 20, 0.1, 90, 4)
    return p.cv


def snow(d, t, seed=15):
    """Lying snow with relief: the wind has combed it into long ripples and low swells, lit from the upper left, so
    each ripple has a bright crest, a soft face and a blue-grey lee. Fine crust grain on the faces, a few packed
    hollows, and bright crystals on the crests. Four tones of the ramp, mostly the upper two."""
    p = Painter(d, (t, t), seed)
    # A height field: broad swells, the ripples across them (long across the wind, short along it), and a little chop.
    swell = p.noise(3.2, 2.2, octaves=2)
    ripple = p.noise(1.3, 0.42, octaves=2)
    chop = p.noise(0.22, 0.12, octaves=2)
    h = swell * 1.0 + ripple * 0.6 + chop * 0.12
    # Light from the upper left: how much higher this pixel stands than the one down and to the right of it.
    k = max(1, p.px(0.03))
    slope = h - np.roll(np.roll(h, -k, axis=0), -k, axis=1)
    slope += (p.rng.random(h.shape) - 0.5) * (slope.std() * 0.22)          # crust grain breaks the tone edges up
    q = np.quantile(slope, [0.04, 0.21, 0.72])
    idx = np.digitize(slope, q)
    for i, tone in enumerate([1, 2, 3, 4]):
        p.cv.fill_mask(idx == i, f"snow{tone}")
    # Packed hollows in the lee of the biggest swells, and crystals catching the light on the crests.
    hollow = (swell < np.quantile(swell, 0.12)) & (idx <= 1)
    p.cv.fill_mask(hollow & (p.rng.random(h.shape) < 0.35), "snow1")
    crest = (idx == 3) & (p.rng.random(h.shape) < 0.02)
    p.cv.fill_mask(crest, "snow4")
    return p.cv


def branches(d, t, seed=30):
    """Brushwood roofs (lean-to): a tangle of thin branches with dried leaves caught in it."""
    p = Painter(d, (t, t), seed)
    p.bands("brush", [1, 2], [0.5], 0.6)
    for ang, tone in ((25, "brush0"), (155, "brush3"), (70, "brush1"), (110, "brush4")):
        p.strokes(tone, 7, 0.35, ang, 20)
    p.blobs("dryleaf1", 3, 0.07)
    p.blobs("dryleaf3", 2, 0.06)
    return p.cv


def thatch(d, t, seed=31):
    """Straw thatch: long strands running down the roof, darker rows where the bundles overlap."""
    p = Painter(d, (t, t), seed)
    p.fill("straw2")
    p.strokes("straw1", 30, 0.18, 90, 6)
    p.strokes("straw3", 22, 0.16, 90, 6)
    p.strokes("straw4", 5, 0.12, 90, 8)
    row = p.px(0.35)
    for y in range(0, t, row):
        p.cv.rect(0, y, t, max(1, p.px(0.02)), "straw0")
    return p.cv


def basket(d, t, seed=32):
    """Woven wicker (baskets, screens): an over-under weave of light and dark rods."""
    p = Painter(d, (t, t), seed)
    p.fill("wicker1")
    cell = p.px(0.05, 2)
    for y in range(0, t, cell):
        for x in range(0, t, cell * 2):
            off = cell if (y // cell) % 2 else 0
            p.cv.rect((x + off) % t, y, cell, max(1, cell - 1), "wicker3")
            p.cv.px((x + off) % t, y, "wicker4")
    return p.cv


def ash(d, t, seed=33):
    """Hearth ash: grey ash, black charcoal chunks and a few glowing embers."""
    p = Painter(d, (t, t), seed)
    p.bands("ash", [1, 2, 3], [0.35, 0.7], 0.3)
    p.blobs("char1", 6, 0.06)
    p.blobs("char0", 3, 0.05)
    p.blobs("ember2", 1.2, 0.03)
    p.blobs("ember4", 0.5, 0.02)
    return p.cv


def flame(d, t, seed=34):
    """Flames (drawn unlit, so they glow): orange with yellow tongues licking upwards."""
    p = Painter(d, (t, t), seed)
    p.bands("flame", [2, 3], [0.5], 0.2, 0.4)
    p.strokes("flame4", 30, 0.12, 90, 12)
    p.strokes("ember3", 12, 0.1, 90, 12)
    return p.cv


def clay(d, t, seed=35):
    """Wet clay and clay walls: smooth with finger smears and grit."""
    p = Painter(d, (t, t), seed)
    p.bands("clay", [1, 2, 3], [0.3, 0.7], 0.5)
    p.strokes("clay3", 3, 0.12, 0, 60)
    p.pebbles("clay", 0.6, 0.04)
    return p.cv


def meat(d, t, seed=36):
    """Meat strips drying: dark red with pale fat streaks."""
    p = Painter(d, (t, t), seed)
    p.bands("meat", [1, 2], [0.5], 0.15)
    p.strokes("meat4", 14, 0.08, 90, 15)
    p.strokes("meat0", 8, 0.06, 90, 15)
    return p.cv


def soil(d, t, seed=37):
    """Tilled soil: dark earth in furrows with lighter crumbled ridges."""
    p = Painter(d, (t, t), seed)
    p.bands("soil", [1, 2], [0.5], 0.5)
    row = p.px(0.25)
    for y in range(0, t, row):
        p.cv.rect(0, y, t, max(1, row // 3), "soil0")
        p.cv.rect(0, y + row // 3, t, max(1, row // 6), "soil3")
    p.pebbles("rockg", 0.4, 0.05)
    return p.cv


def crop(d, t, seed=38):
    """Ripe grain: dense upright stalks with heads at the top of each row."""
    p = Painter(d, (t, t), seed)
    p.fill("crop1")
    p.strokes("crop2", 40, 0.18, 90, 8)
    p.strokes("crop3", 25, 0.14, 90, 8)
    p.blobs("crop4", 6, 0.04)
    p.strokes("grass1", 6, 0.12, 90, 10)
    return p.cv


def shoots(d, t, seed=40):
    """Young crop in spring: rows of short green shoots on dark tilled soil."""
    p = Painter(d, (t, t), seed)
    p.bands("soil", [1, 2], [0.5], 0.5)
    row = p.px(0.25)
    for y in range(0, t, row):
        for x in range(p.ri(0, 3), t, p.px(0.06, 2)):
            h = p.ri(1, max(2, p.px(0.07)))
            p.cv.line(x, y + row // 2, x + p.ri(-1, 2), y + row // 2 - h, ["shoot2", "shoot3", "shoot1"][p.ri(0, 3)])
    return p.cv


def crop_green(d, t, seed=41):
    """Summer crop: dense green stalks, still unripe, a few paler heads forming."""
    p = Painter(d, (t, t), seed)
    p.fill("cropg1")
    p.strokes("cropg2", 40, 0.18, 90, 8)
    p.strokes("cropg3", 22, 0.14, 90, 8)
    p.strokes("cropg0", 10, 0.16, 90, 8)
    p.blobs("cropg4", 3, 0.035)
    return p.cv


def stubble(d, t, seed=42):
    """Cut field: short pale straw stubble in rows over the soil, loose straw lying about."""
    p = Painter(d, (t, t), seed)
    p.bands("soil", [1, 2], [0.5], 0.5)
    row = p.px(0.25)
    for y in range(0, t, row):
        p.cv.rect(0, y + row // 3, t, max(1, row // 3), "stubble1")
        for x in range(p.ri(0, 3), t, p.px(0.05, 2)):
            p.cv.line(x, y + row // 3, x, y + row // 3 - p.ri(0, 2), f"stubble{p.ri(2, 5)}")
    p.strokes("stubble3", 4, 0.15, 20, 50)
    return p.cv


def flax(d, t, seed=43):
    """Flax: fine grey-green stems, blue flowers in summer (also the fibre in weavers' baskets)."""
    p = Painter(d, (t, t), seed)
    p.fill("flax1")
    p.strokes("flax2", 45, 0.16, 90, 6)
    p.strokes("flax3", 25, 0.12, 90, 6)
    p.blobs("flaxbloom3", 4, 0.035)
    p.blobs("flaxbloom4", 1.2, 0.025)
    return p.cv


def path(d, t, seed=44):
    """Trodden path: packed earth worn smooth, two faint ruts, grass creeping in at the edges, small stones."""
    p = Painter(d, (t, t), seed)
    p.bands("path", [1, 2, 3], [0.3, 0.72], 0.7)
    p.cv.fill_mask(p.noise(0.25) > 0.8, "path3")
    p.strokes("path0", 1.5, 0.3, 90, 6)
    p.pebbles("rockg", 0.5, 0.06)
    p.strokes("grass1", 0.6, 0.08, 90, 30)
    return p.cv


def stone_wall(d, t, seed=45):
    """Dressed stone: squared blocks in courses with dark mortar joints and a lit top edge."""
    p = Painter(d, (t, t), seed)
    p.fill("dressed0")
    course = p.px(0.22, 3)
    for y in range(0, t, course):
        x = -p.ri(0, p.px(0.3))
        while x < t:
            w = p.px(p.r(0.28, 0.5), 3)
            p.cv.rect(x + 1, y + 1, w - 1, course - 1, f"dressed{p.ri(1, 4)}")
            p.cv.line(x + 1, y + 1, x + w - 1, y + 1, "dressed4")
            x += w
    p.blobs("lichen2", 0.15, 0.08)
    return p.cv


def pottery(d, t, seed=46):
    """Fired clay pots: warm terracotta with throwing rings and a darker fire-blush."""
    p = Painter(d, (t, t), seed)
    p.bands("potter", [2, 3], [0.6], 0.4)
    ring = p.px(0.04, 2)
    for y in range(0, t, ring * 2):
        p.cv.rect(0, y, t, 1, "potter1")
    p.blobs("potter0", 0.5, 0.12)
    p.blobs("potter4", 1.0, 0.03)
    return p.cv


def cloth(d, t, seed=47):
    """Woven linen: a fine plain weave of pale threads with a few slubs."""
    p = Painter(d, (t, t), seed)
    p.fill("linen2")
    for y in range(0, t, 2):
        p.cv.rect(0, y, t, 1, "linen3" if (y // 2) % 2 else "linen1")
    for x in range(0, t, 4):
        p.cv.rect(x, 0, 1, t, "linen1")
    p.strokes("linen4", 3, 0.05, 0, 3)
    return p.cv


def ore_copper(d, t, seed=48):
    """Copper ore: grey rock streaked and blotched with green and blue-green stains."""
    p = rock_painter(d, t, seed)
    p.cv.fill_mask(p.noise(0.3) > 0.62, "verd2")
    p.blobs("verd3", 1.5, 0.08)
    p.blobs("verd1", 1.0, 0.06)
    p.strokes("verd4", 1.0, 0.15, 60, 30)
    return p.cv


def ore_tin(d, t, seed=49):
    """Tin ore: dark grey stone with heavy black-brown grains and a dull sheen."""
    p = rock_painter(d, t, seed)
    p.cv.fill_mask(p.noise(0.25) > 0.6, "tin1")
    p.blobs("tin0", 3, 0.05)
    p.blobs("tin4", 0.8, 0.03)
    return p.cv


def rock_painter(d, t, seed):
    p = Painter(d, (t, t), seed)
    p.bands("rock", [1, 2, 3], [0.33, 0.66], 0.7, 0.45)
    p.strokes("rock0", 1.5, 0.2, 80, 40)
    return p


def daub(d, t, seed=51):
    """Wattle and daub: pale earth plaster smoothed by hand, straw flecks, and the weave showing through in cracks."""
    p = Painter(d, (t, t), seed)
    p.bands("daub", [2, 3], [0.55], 0.6)
    p.cv.fill_mask(p.noise(0.4) > 0.82, "daub1")
    p.strokes("straw3", 6, 0.06, 20, 70)
    p.strokes("daub0", 0.8, 0.18, 80, 30)
    p.blobs("daub4", 1.0, 0.05)
    return p.cv


def metal(d, t, seed=50):
    """Worked copper and bronze: a warm metal with hammer facets and a bright edge."""
    p = Painter(d, (t, t), seed)
    p.bands("metal", [1, 2, 3], [0.3, 0.7], 0.12)
    p.blobs("metal4", 3, 0.04)
    p.blobs("metal0", 1, 0.05)
    return p.cv


# ---------------------------------------------------------------------------------------------- sprites
# Drawn at native size (metres x density), not wrapped; placed top-left in their layer.

def sprite_bush(d, seed=20):
    w, h = max(8, round(1.4 * d)), max(7, round(1.1 * d))
    p = Painter(d, (w, h), seed, wrap=False)
    cv = p.cv
    cv.ellipse(0, h // 4, w, h - h // 4, "leaf0")
    cv.ellipse(w // 6, 0, w * 2 // 3, h * 3 // 4, "leaf0")
    mask = cv.mask("leaf0")
    for tone, n, s in (("leaf1", 30, 0.16), ("leaf2", 26, 0.13), ("leaf3", 12, 0.1), ("leaf4", 3, 0.07)):
        for _ in range(int(n * w * h / (d * d) / 1.5)):
            x, y = p.ri(0, w), p.ri(0, h)
            if mask[y, x] and (tone in ("leaf1", "leaf2") or y < h * 0.65):
                cv.ellipse(x, y, p.px(s), p.px(s * 0.8), tone, clip="leaf0" if False else None)
    cv.data[~mask] = 0                              # keep the silhouette
    for _ in range(max(2, int(w * h / (d * d) * 4))):  # a few berries
        x, y = p.ri(1, w - 1), p.ri(h // 4, h - 1)
        if mask[y, x]:
            cv.px(x, y, "berry2")
    return cv


def sprite_tuft(d, seed=21):
    w, h = max(5, round(0.6 * d)), max(4, round(0.45 * d))
    p = Painter(d, (w, h), seed, wrap=False)
    for _ in range(max(4, int(w * 1.2))):
        x0 = p.ri(w // 4, w - w // 4)
        x1 = int(x0 + p.r(-w / 2.5, w / 2.5))
        y1 = p.ri(0, max(1, h // 3))
        p.cv.line(x0, h - 1, x1, y1, ["grass1", "grass2", "grass3"][p.ri(0, 3)])
    return p.cv


def sprite_flowers(d, seed=22):
    w, h = max(5, round(0.6 * d)), max(4, round(0.4 * d))
    p = Painter(d, (w, h), seed, wrap=False)
    for _ in range(max(3, w // 2)):
        x = p.ri(1, w - 1)
        top = p.ri(0, h // 2)
        p.cv.line(x, h - 1, x, top + 1, "grass1")
        ramp = "bloom" if p.r(1) < 0.6 else "bloom2"
        s = max(1, p.px(0.05))
        p.cv.rect(x - s // 2, top, s, s, f"{ramp}3")
    return p.cv


MATERIALS = [
    ("grass", grass), ("litter", litter), ("dirt", dirt), ("bank", bank), ("bed", bed), ("rock_ground", rock_ground),
    ("rock", rock), ("water", water), ("shallow", shallow),
    ("bark_conifer", lambda d, t: bark(d, t, "barkc", 10)), ("bark_broad", lambda d, t: bark(d, t, "barkb", 11)),
    ("needles", needles), ("leaves", leaves), ("fur", fur), ("snow", snow),
    ("deer", deer), ("deer_pale", deer_pale),
    ("skin", skin), ("hide", hide), ("hair", hair), ("wood", wood),
    ("branches", branches), ("thatch", thatch), ("basket", basket), ("ash", ash), ("flame", flame),
    ("clay", clay), ("meat", meat), ("soil", soil), ("crop", crop),
    ("shoots", shoots), ("crop_green", crop_green), ("stubble", stubble), ("flax", flax), ("path", path),
    ("stone_wall", stone_wall), ("pottery", pottery), ("cloth", cloth), ("ore_copper", ore_copper), ("ore_tin", ore_tin),
    ("metal", metal), ("daub", daub),
]
def sprite_reeds(d, seed=23):
    """A clump of reeds for the river bank: tall thin stems leaning a little, some with a dark seed head."""
    w, h = max(6, round(0.7 * d)), max(8, round(1.05 * d))
    p = Painter(d, (w, h), seed, wrap=False)
    cv = p.cv
    for _ in range(max(5, int(w * 0.55))):
        x0 = p.ri(w // 5, w - w // 5)
        lean = p.r(-w / 5, w / 5)
        top = p.ri(0, h // 3)
        x1 = int(min(w - 1, max(0, x0 + lean)))
        tone = ["grass1", "grass2", "grass3", "flax2", "stubble2"][p.ri(0, 5)]
        cv.line(x0, h - 1, x1, top, tone)
        if p.r(1) < 0.45:                                   # a seed head: a short dark club near the top
            s = max(2, p.px(0.11))
            for k in range(s):
                cv.px(x1, min(h - 1, top + k), "brush1")
                if d >= 32:
                    cv.px(min(w - 1, x1 + 1), min(h - 1, top + k), "brush2")
    return cv


def sprite_fern(d, seed=24):
    """A fern for the forest floor: fronds arching out from the middle, each a spine with short leaflets."""
    w, h = max(8, round(0.85 * d)), max(5, round(0.5 * d))
    p = Painter(d, (w, h), seed, wrap=False)
    cv = p.cv
    n = max(5, int(w / 7))
    order = list(range(n))
    p.rng.shuffle(order)
    for i in order:
        a = math.pi * (0.06 + 0.88 * (i + p.r(-0.25, 0.25)) / (n - 1))
        length = p.r(0.7, 1.0) * w * 0.5
        spine, leaf = [("needle0", "leaf1"), ("needle1", "leaf2"), ("leaf0", "leaf2"), ("needle1", "needle3")][p.ri(0, 4)]
        steps = max(4, int(length * 1.5))
        px_, py_ = w / 2, h - 1
        for s in range(steps):
            t = s / steps
            x = w / 2 + math.cos(a) * length * t
            y = (h - 1) - math.sin(a) * length * t * (h / (w * 0.5)) * (1 - 0.3 * t)   # the frond arches over
            # Leaflets across the frond, longest a third of the way out, tapering to the tip.
            dx, dy = x - px_, y - py_
            ln = math.hypot(dx, dy) or 1.0
            nx, ny = -dy / ln, dx / ln
            half = max(0.0, (0.07 * d) * math.sin(math.pi * min(1.0, t * 0.85 + 0.12)))
            if s % 2 == 0:
                for k in range(-int(round(half)), int(round(half)) + 1):
                    xi, yi = int(round(x + nx * k)), int(round(y + ny * k))
                    if 0 <= xi < w and 0 <= yi < h:
                        cv.px(xi, yi, leaf)
            xi, yi = int(round(x)), int(round(y))
            if 0 <= xi < w and 0 <= yi < h:
                cv.px(xi, yi, spine)
            px_, py_ = x, y
    return cv


def sprite_footprint(d, seed=25):
    """One footprint pressed into snow, toe at the top: the sole and the heel as two shadowed hollows with a darker
    floor, drawn in the snow ramp's lower tones so it reads as a dent, not a stain."""
    w, h = max(3, round(0.17 * d)), max(5, round(0.36 * d))
    p = Painter(d, (w, h), seed, wrap=False)
    cv = p.cv
    sole_h = int(h * 0.6)
    cv.ellipse(0, 0, w, sole_h, "snow1")
    cv.ellipse(max(0, w // 6), sole_h + max(1, h // 14), w - 2 * max(0, w // 6), h - sole_h - max(1, h // 14), "snow1")
    if w >= 6:
        cv.ellipse(w // 4, sole_h // 5, w - 2 * (w // 4), sole_h - 2 * (sole_h // 5), "snow0")
        hw = w - 2 * (w // 6)
        cv.ellipse(w // 6 + hw // 4, sole_h + h // 14 + (h - sole_h) // 4, max(1, hw // 2), max(1, (h - sole_h) // 3), "snow0")
    return cv


SPRITES = [("bush", sprite_bush), ("tuft", sprite_tuft), ("flowers", sprite_flowers), ("reeds", sprite_reeds), ("fern", sprite_fern),
           ("footprint", sprite_footprint)]


def main():
    manifest = {"layers": [], "sprites": {}}
    for name, _ in MATERIALS:
        manifest["layers"].append(name)
    for name, _ in SPRITES:
        manifest["layers"].append(name)
    previews = []
    for d in DENSITIES:
        t = int(TILE_METRES[d] * d)
        out = OUT / f"d{d}"
        out.mkdir(parents=True, exist_ok=True)
        row = []
        for name, fn in MATERIALS:
            cv = fn(d, t)
            cv.save(out / f"{name}.png")
            row.append((name, cv))
        for name, fn in SPRITES:
            sp = fn(d)
            layer = pf.Canvas(t, t, PAL, fill=0)
            layer.paste(sp, 0, 0)
            layer.save(out / f"{name}.png")
            manifest["sprites"].setdefault(name, {})[f"d{d}"] = [sp.w, sp.h]
            row.append((name, sp))
        previews.append((d, t, row))
    (ROOT / "Assets" / "StreamingAssets" / "Data" / "materials.json").write_text(
        json.dumps({"layers": manifest["layers"], "densities": DENSITIES,
                    "tileTexels": [int(TILE_METRES[d] * d) for d in DENSITIES],
                    "sprites": [{"name": k, **v} for k, v in manifest["sprites"].items()]}, indent=2))
    contact_sheet(previews)


def contact_sheet(previews):
    """Each material shown as 2 m x 2 m of surface at each density, scaled to the same on-screen size."""
    from PIL import Image, ImageDraw
    cell = 192
    names = [n for n, _ in previews[0][2]]
    sheet = Image.new("RGB", (110 + cell * len(names) // 2 + 10, 30 + (cell + 24) * len(previews) * 2), (40, 40, 40))
    draw = ImageDraw.Draw(sheet)
    half = len(names) // 2 + len(names) % 2
    for ri, (d, t, row) in enumerate(previews):
        for i, (name, cv) in enumerate(row):
            img = cv.to_image()
            if cv.w == t and name not in [s for s, _ in SPRITES]:
                crop = int(2 * d)  # 2 m of surface
                img = img.crop((0, 0, crop, crop))
            else:
                bg = Image.new("RGBA", img.size, (90, 110, 70, 255))
                bg.alpha_composite(img)
                img = bg
            k = cell / max(img.size)
            img = img.resize((max(1, int(img.size[0] * k)), max(1, int(img.size[1] * k))), Image.NEAREST)
            col, sub = i % half, i // half
            x = 110 + col * cell
            y = 30 + (ri * 2 + sub) * (cell + 24)
            sheet.paste(img.convert("RGB"), (x, y))
            draw.text((x + 4, y + cell + 4), name, fill=(220, 220, 210))
        draw.text((10, 30 + ri * 2 * (cell + 24) + 80), f"{d} px/m", fill=(255, 255, 255))
    sheet.save(ROOT / "runs" / "textures_preview.png")


if __name__ == "__main__":
    main()
