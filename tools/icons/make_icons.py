#!/usr/bin/env python3
"""Draws the app and tray icons.

Writes src/LaciaIsland/Assets/app.ico, src/LaciaIsland/Assets/tray.ico and docs/icon.png.
Needs Pillow and numpy:  pip install pillow numpy
"""
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "src" / "LaciaIsland" / "Assets"
DOCS = ROOT / "docs"

APP_SIZES = [256, 128, 64, 48, 40, 32, 24, 20, 16]
TRAY_SIZES = [64, 48, 32, 24, 20, 16]
MASTER = 1024          # app icon is drawn once at this size and scaled down
U = MASTER / 256       # the layout below is written in 256-px units


def squircle_alpha(size: int, n: float = 5.0, ss: int = 2) -> Image.Image:
    """Superellipse |x|^n + |y|^n <= 1, anti-aliased by supersampling."""
    big = size * ss
    c = (big - 1) / 2
    yy, xx = np.mgrid[0:big, 0:big].astype(np.float32)
    v = np.abs((xx - c) / (big / 2)) ** n + np.abs((yy - c) / (big / 2)) ** n
    mask = Image.fromarray(((v <= 1.0) * 255).astype(np.uint8), "L")
    return mask.resize((size, size), Image.LANCZOS)


def background(size: int) -> Image.Image:
    """Deep purple with a magenta glow rising from the bottom edge."""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32) * (256 / size)
    base = np.array([84, 28, 142], np.float32)
    glow = np.array([134, 41, 97], np.float32)
    d = ((xx - 128) / 100) ** 2 + ((yy - 268) / 135) ** 2
    rgb = base + np.exp(-d)[..., None] * glow
    return Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB").convert("RGBA")


def cover_art(px: int) -> Image.Image:
    """Tiny album cover: night-blue square with a pink-orange glow (like the demo track)."""
    yy, xx = np.mgrid[0:px, 0:px].astype(np.float32) / px
    d = np.sqrt((xx - 0.64) ** 2 + (yy - 0.36) ** 2)
    base = np.array([34, 20, 82], np.float32)
    pink = np.array([255, 84, 150], np.float32)
    core = np.array([255, 176, 96], np.float32)
    t1 = np.clip(1 - d / 0.62, 0, 1) ** 1.4
    t2 = np.clip(1 - d / 0.26, 0, 1) ** 1.2
    rgb = base * (1 - t1[..., None]) + pink * t1[..., None]
    rgb = rgb * (1 - t2[..., None]) + core * t2[..., None]
    art = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB").convert("RGBA")
    mask = Image.new("L", (px * 4, px * 4), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, px * 4 - 1, px * 4 - 1], radius=round(px * 4 * 0.27), fill=255)
    art.putalpha(mask.resize((px, px), Image.LANCZOS))
    return art


def app_master() -> Image.Image:
    img = background(MASTER)
    img.putalpha(squircle_alpha(MASTER))

    ss = 2
    layer = Image.new("RGBA", (MASTER * ss, MASTER * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    s = U * ss

    # the island: a black pill near the top, like on a screen
    cx, cy, w, h = 128, 72, 144, 42
    d.rounded_rectangle([(cx - w / 2) * s, (cy - h / 2) * s, (cx + w / 2) * s, (cy + h / 2) * s],
                        radius=h / 2 * s, fill=(0, 0, 0, 255))

    # equaliser bars on the right
    heights = [9, 15, 11, 17, 8]
    bw, gap = 3.6, 2.6
    right = cx + w / 2 - 11
    x = right - len(heights) * bw - (len(heights) - 1) * gap
    for bh in heights:
        d.rounded_rectangle([x * s, (cy - bh / 2) * s, (x + bw) * s, (cy + bh / 2) * s],
                            radius=bw / 2 * s, fill=(255, 159, 10, 255))
        x += bw + gap

    layer = layer.resize((MASTER, MASTER), Image.LANCZOS)
    img.alpha_composite(layer)

    # album cover on the left
    art_px = round(26 * U)
    art = cover_art(art_px)
    img.alpha_composite(art, (round((cx - w / 2 + 8) * U), round((cy - 13) * U)))
    return img


def tray_icon(size: int) -> Image.Image:
    """White island pill with a thin dark rim so it reads on light and dark taskbars."""
    ss = 8
    pad = round(size * 2 / 64)
    h = 2 * round(size * 14 / 64)
    y0 = size / 2 - h / 2
    rim = max(1.0, size / 32)
    big = Image.new("RGBA", (size * ss, size * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    d.rounded_rectangle([pad * ss, y0 * ss, (size - pad) * ss - 1, (y0 + h) * ss - 1],
                        radius=h / 2 * ss, fill=(58, 58, 64, 255))
    r = rim * ss
    d.rounded_rectangle([pad * ss + r, y0 * ss + r, (size - pad) * ss - 1 - r, (y0 + h) * ss - 1 - r],
                        radius=(h / 2 - rim) * ss, fill=(245, 245, 247, 255))
    return big.resize((size, size), Image.BOX)


def save_ico(path: Path, images: dict[int, Image.Image]) -> None:
    largest = max(images)
    images[largest].save(path, format="ICO", sizes=[(s, s) for s in images],
                         append_images=[im for s, im in images.items() if s != largest])


def main() -> None:
    master = app_master()
    app = {s: master.resize((s, s), Image.LANCZOS) for s in APP_SIZES}
    save_ico(ASSETS / "app.ico", app)
    DOCS.mkdir(exist_ok=True)
    master.resize((256, 256), Image.LANCZOS).save(DOCS / "icon.png", optimize=True)

    save_ico(ASSETS / "tray.ico", {s: tray_icon(s) for s in TRAY_SIZES})
    print("icons written")


if __name__ == "__main__":
    main()
