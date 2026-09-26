#!/usr/bin/env python3
"""Painterly tiling textures for the city (concept 3: warm brick rooftops at golden hour).
Soft, low-contrast colour variation (no photo grime): brick walls in three colours, roof tar,
street asphalt. Tiles seamlessly; 1 texture = 2 m of wall (bricks ~24 x 7.5 cm).
"""
import os, random
from PIL import Image, ImageDraw, ImageFilter

OUT = "/Users/samwudel/Game/RescueGame/Assets/Art/City/Textures"
S = 512

def jitter(c, amt, rnd):
    """Brightness-only variation (keeps the hue, like hand-painted bricks)."""
    k = 1.0 + rnd.uniform(-amt, amt) / 100.0
    warm = rnd.uniform(-amt, amt) * 0.15      # tiny warm/cool drift
    r, g, b = c
    return (max(0, min(255, int(r * k + warm))), max(0, min(255, int(g * k))), max(0, min(255, int(b * k - warm))))

def brick(name, base, mortar, seed):
    rnd = random.Random(seed)
    im = Image.new("RGB", (S, S), mortar)
    d = ImageDraw.Draw(im)
    rows, cols = 26, 8                      # 2 m tile: 26 courses, 8 bricks across
    bh, bw = S / rows, S / cols
    for r in range(rows):
        off = (bw / 2) if r % 2 else 0
        for c in range(-1, cols + 1):
            x0 = c * bw + off + 2; y0 = r * bh + 1.5
            col = jitter(base, 9, rnd)
            if rnd.random() < 0.08: col = jitter(tuple(v * 0.82 for v in base), 8, rnd)   # a few dark ones
            if rnd.random() < 0.05: col = jitter(tuple(min(255, v * 1.12) for v in base), 8, rnd)
            for dx in (-S, 0, S):              # wrap horizontally so it tiles
                d.rectangle((x0 + dx, y0, x0 + dx + bw - 3, y0 + bh - 2.5), fill=col)
    im = im.filter(ImageFilter.GaussianBlur(0.8))
    # soft painterly wash: big faint blotches
    wash = Image.new("L", (S, S), 128); wd = ImageDraw.Draw(wash)
    for _ in range(40):
        x, y, r = rnd.uniform(0, S), rnd.uniform(0, S), rnd.uniform(30, 110)
        v = int(rnd.uniform(110, 146))
        for dx in (-S, 0, S):
            for dy in (-S, 0, S):
                wd.ellipse((x - r + dx, y - r + dy, x + r + dx, y + r + dy), fill=v)
    wash = wash.filter(ImageFilter.GaussianBlur(40))
    im = Image.blend(im, Image.merge("RGB", [wash] * 3).point(lambda v: v), 0.0)
    px = im.load(); wp = wash.load()
    for y in range(S):
        for x in range(S):
            k = (wp[x, y] - 128) / 128.0 * 0.12 + 1.0
            r_, g_, b_ = px[x, y]
            px[x, y] = (min(255, int(r_ * k)), min(255, int(g_ * k)), min(255, int(b_ * k)))
    im.save(os.path.join(OUT, name + ".png"))

def flat(name, base, amt, seed, spots=60, spot_col=None):
    rnd = random.Random(seed)
    im = Image.new("RGB", (S, S), base); d = ImageDraw.Draw(im)
    for _ in range(spots):
        x, y, r = rnd.uniform(0, S), rnd.uniform(0, S), rnd.uniform(8, 60)
        col = jitter(spot_col or base, amt, rnd)
        for dx in (-S, 0, S):
            for dy in (-S, 0, S):
                d.ellipse((x - r + dx, y - r + dy, x + r + dx, y + r + dy), fill=col)
    im = im.filter(ImageFilter.GaussianBlur(14))
    im.save(os.path.join(OUT, name + ".png"))

brick("Brick_Red", (168, 72, 52), (196, 170, 140), 1)
brick("Brick_Brown", (128, 78, 58), (186, 162, 134), 2)
brick("Brick_Tan", (196, 150, 104), (214, 196, 166), 3)
flat("RoofTar", (92, 86, 82), 10, 4, spots=80)
flat("Asphalt", (84, 80, 78), 8, 5, spots=120)
flat("Concrete", (172, 164, 150), 10, 6, spots=70)
print("ok", sorted(os.listdir(OUT)))
