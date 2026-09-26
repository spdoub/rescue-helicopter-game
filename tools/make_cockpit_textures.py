#!/usr/bin/env python3
"""Draws the cockpit instrument faces, horizon ball, sticky notes and radio displays (PNG)."""
import math, os
from PIL import Image, ImageDraw, ImageFont

OUT = "/Users/samwudel/Game/RescueGame/Assets/Art/Helicopters/Medevac/Interior/Textures"
os.makedirs(OUT, exist_ok=True)
S = 512
C = S // 2
BOLD = "/System/Library/Fonts/Supplemental/DIN Alternate Bold.ttf"
HAND = "/System/Library/Fonts/Noteworthy.ttc"
FACE = (30, 31, 34)
INK = (238, 234, 222)

def font(path, size):
    try: return ImageFont.truetype(path, size)
    except Exception: return ImageFont.load_default()

def polar(r, deg):  # 0 deg = up, clockwise
    a = math.radians(deg)
    return C + r * math.sin(a), C - r * math.cos(a)

def base(label=None):
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.ellipse((4, 4, S - 4, S - 4), fill=FACE)
    d.ellipse((14, 14, S - 14, S - 14), outline=(55, 56, 60), width=4)
    if label:
        f = font(BOLD, 34)
        w = d.textlength(label, font=f)
        d.text((C - w / 2, C + 70), label, font=f, fill=(170, 168, 160))
    return im, d

def arc_band(d, r0, r1, a0, a1, colour):
    for a in range(int(a0 * 4), int(a1 * 4)):
        p0 = polar(r0, a / 4); p1 = polar(r1, a / 4)
        d.line((p0, p1), fill=colour, width=4)

def ticks(d, a0, a1, n_major, n_minor, labels=None, r=225, size=36):
    f = font(BOLD, size)
    total = n_major * n_minor
    for i in range(total + 1):
        a = a0 + (a1 - a0) * i / total
        major = i % n_minor == 0
        d.line((polar(r, a), polar(r - (34 if major else 18), a)), fill=INK, width=7 if major else 3)
        if major and labels is not None:
            t = str(labels[i // n_minor])
            x, y = polar(r - 72, a)
            w = d.textlength(t, font=f)
            d.text((x - w / 2, y - size * 0.62), t, font=f, fill=INK)

def save(im, name): im.save(os.path.join(OUT, name + ".png"))

# airspeed 0-160 kt over 300 deg
im, d = base("KNOTS")
arc_band(d, 200, 222, 40, 200, (70, 170, 70)); arc_band(d, 200, 222, 200, 250, (230, 190, 40)); arc_band(d, 200, 222, 250, 262, (210, 50, 40))
ticks(d, 0, 300, 8, 4, [0, 20, 40, 60, 80, 100, 120, 140, 160]); save(im, "Gauge_Airspeed")
# altimeter 0-9 full circle
im, d = base("ALT x100")
ticks(d, 0, 324, 9, 5, list(range(10))); save(im, "Gauge_Altimeter")
# vertical speed: -20..+20 over left half (+ up)
im, d = base("CLIMB")
ticks(d, -150, -30, 4, 5, [-20, -10, 0, 10, 20]); save(im, "Gauge_VSI")
# rotor rpm 0-120 %
im, d = base("RPM %")
arc_band(d, 200, 222, 237, 262, (70, 170, 70)); arc_band(d, 200, 222, 262, 275, (210, 50, 40))
ticks(d, 0, 275, 6, 4, [0, 20, 40, 60, 80, 100, 120]); save(im, "Gauge_RPM")
# fuel E..F quarter arc
im, d = base("FUEL")
arc_band(d, 200, 222, -60, -40, (210, 50, 40))
ticks(d, -60, 60, 4, 2, ["E", "", "1/2", "", "F"], size=44); save(im, "Gauge_Fuel")
# engine temp C..H
im, d = base("TEMP")
arc_band(d, 200, 222, 40, 60, (210, 50, 40))
ticks(d, -60, 60, 2, 4, ["C", "", "H"], size=44); save(im, "Gauge_Temp")
# clock
im, d = base(None)
ticks(d, 0, 330, 11, 5, [12] + list(range(1, 12)), size=40); save(im, "Gauge_Clock")
# heading card (the whole card rotates)
im = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
d.ellipse((4, 4, S - 4, S - 4), fill=FACE)
f = font(BOLD, 52)
for i in range(72):
    a = i * 5
    major = i % 6 == 0
    d.line((polar(225, a), polar(225 - (30 if major else 14), a)), fill=INK, width=6 if major else 3)
for a, t in ((0, "N"), (90, "E"), (180, "S"), (270, "W")):
    x, y = polar(160, a); w = d.textlength(t, font=f)
    d.text((x - w / 2, y - 34), t, font=f, fill=(250, 160, 60) if t == "N" else INK)
save(im, "Gauge_Heading")

# attitude ball: sky over ground with pitch ladder, 1 texture unit = 90 deg of pitch
H = 1024
im = Image.new("RGB", (512, H)); d = ImageDraw.Draw(im)
d.rectangle((0, 0, 512, H // 2), fill=(70, 150, 215)); d.rectangle((0, H // 2, 512, H), fill=(150, 95, 55))
d.line((0, H // 2, 512, H // 2), fill=(250, 250, 250), width=8)
for p in (-30, -20, -10, 10, 20, 30):
    y = H // 2 - p * (H / 180)
    w = 120 if p % 20 == 0 else 70
    d.line((256 - w, y, 256 + w, y), fill=(250, 250, 250), width=5)
save(im, "Attitude_Ball")

# sticky notes
notes = [("DO NOT\npress the\nRED one", (250, 225, 90)), ("fuel =\nIMPORTANT", (250, 170, 190)),
         ("up is\nUP", (170, 225, 140)), ("Grandma\npickup 3pm\n(bring snacks)", (250, 225, 90))]
for k, (text, col) in enumerate(notes):
    im = Image.new("RGB", (256, 256), col); d = ImageDraw.Draw(im)
    d.rectangle((0, 0, 255, 26), fill=tuple(int(c * 0.9) for c in col))
    d.multiline_text((18, 40), text, font=font(HAND, 40), fill=(40, 40, 70), spacing=2)
    save(im, f"Note_{k}")

# radio displays
for k, text in enumerate(("121.50", "MEDEVAC 1", "--:--")):
    im = Image.new("RGB", (256, 96), (20, 40, 22)); d = ImageDraw.Draw(im)
    f = font(BOLD, 52 if len(text) < 8 else 40)
    w = d.textlength(text, font=f)
    d.text((128 - w / 2, 18), text, font=f, fill=(120, 255, 140))
    save(im, f"Radio_{k}")
print("ok", len(os.listdir(OUT)))
