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
DENSITIES = [8, 16, 32]
TILE_METRES = {8: 8.0, 16: 8.0, 32: 8.0}   # 64, 128, 256 px tiles

# Muted, natural base colours (Firstlight is neutral, not cozy). One 5-step hue-shifted ramp each.
BASES = {
    "grass": "#6c7a47", "dirt": "#7a6449", "bank": "#958866", "bed": "#5b5946", "litter": "#5b6340",
    "rockg": "#7d786b", "rock": "#88837a", "lichen": "#7b8263", "water": "#4f6a74", "shallow": "#6a8483",
    "barkc": "#5a4535", "barkb": "#6c6355", "needle": "#4a5d3f", "leaf": "#5f7341", "fur": "#86684b",
    "berry": "#6e4652", "bloom": "#b9a676", "bloom2": "#8f7f9a", "snow": "#d3d8d8",
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


def snow(d, t, seed=15):
    p = Painter(d, (t, t), seed)
    p.bands("snow", [2, 3, 4], [0.3, 0.75], 1.0)
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
]
SPRITES = [("bush", sprite_bush), ("tuft", sprite_tuft), ("flowers", sprite_flowers)]


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
