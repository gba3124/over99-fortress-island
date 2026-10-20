import math, os
from PIL import Image, ImageDraw, ImageFilter, ImageOps
from ui import *

S = 512  # drawn at twice the size, then shrunk to 256


def sea_bg(top=(110, 190, 245), mid=(176, 224, 250), horizon=0.62):
    im = vgrad(S, int(S * horizon), top, mid)
    sea = vgrad(S, S - im.height, (52, 140, 205), (24, 86, 150))
    out = Image.new("RGBA", (S, S))
    out.paste(im, (0, 0))
    out.paste(sea, (0, im.height))
    d = ImageDraw.Draw(out)
    for i, y in enumerate(range(im.height + 26, S, 46)):  # little wave marks
        for x in range(-40 + (i % 2) * 46, S, 92):
            d.arc((x, y, x + 46, y + 22), 200, 340, fill=(140, 205, 245), width=5)
    return out


def rays(im, centre, col, n=16, alpha=110):
    layer = Image.new("RGBA", im.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    cx, cy = centre
    for i in range(n):
        a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 0.5) / n
        d.polygon([(cx, cy), (cx + 900 * math.cos(a0), cy + 900 * math.sin(a0)), (cx + 900 * math.cos(a1), cy + 900 * math.sin(a1))],
                  fill=(*col, alpha))
    im.alpha_composite(layer)


def put(im, pic, x, y, anchor="mb", glow=None):
    if anchor == "mb":
        x, y = x - pic.width // 2, y - pic.height
    elif anchor == "mm":
        x, y = x - pic.width // 2, y - pic.height // 2
    if glow:  # a soft light halo around it
        a = pic.split()[3].filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(14))
        halo = Image.new("RGBA", pic.size, (*glow, 0))
        halo.putalpha(a.point(lambda v: int(v * 0.9)))
        im.alpha_composite(halo, (x, y))
    # a thin dark rim keeps it readable when small
    rim = pic.split()[3].filter(ImageFilter.MaxFilter(5))
    edge = Image.new("RGBA", pic.size, (*NAVY, 0))
    edge.putalpha(rim)
    im.alpha_composite(edge, (x, y))
    im.alpha_composite(pic, (x, y))


def label(im, s, y, size, fill=GOLD, stroke=12):
    f = title(size)
    text(im, (S // 2, y), s, f, fill=fill, anchor="mm", stroke=stroke, stroke_fill=NAVY, drop=10)


def frame(im):  # a rounded sand rim, so it reads as a badge on any page colour
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), 64, fill=255)
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(im, (0, 0), mask)
    ImageDraw.Draw(out).rounded_rectangle((6, 6, S - 7, S - 7), 60, outline=SAND, width=12)
    ImageDraw.Draw(out).rounded_rectangle((0, 0, S - 1, S - 1), 64, outline=NAVY, width=6)
    return out


def lh(tag, h):
    return fit(Image.open(os.path.join(R, f"lighthouse_{tag}.png")).convert("RGBA"), S, h)


def hero(kind, tag, w, h, flip=False):
    pic = fit(Image.open(os.path.join(R, f"hero_{kind}_{tag}.png")).convert("RGBA"), w, h)
    return ImageOps.mirror(pic) if flip else pic


out = {}
# a: the Charger charging the lighthouse
im = sea_bg((58, 138, 218), (150, 206, 246))
rays(im, (372, 250), (255, 255, 255), alpha=40)
put(im, lh("plain", 352), 376, 418)
streaks = Image.new("RGBA", im.size, (0, 0, 0, 0))
d = ImageDraw.Draw(streaks)
for y, x0, x1 in ((330, 8, 70), (372, 2, 58), (414, 14, 74)):  # it's charging
    d.rounded_rectangle((x0, y - 5, x1, y + 5), 5, fill=(255, 255, 255, 220))
im.alpha_composite(streaks)
put(im, hero("Charger", "q", 350, 260), 214, 470)
label(im, "OVER 99", 78, 104)
out["a"] = frame(im)

# b: the golden lighthouse of wave 100, a fish on each side
im = sea_bg((255, 214, 120), (255, 238, 190))
rays(im, (S // 2, 210), (255, 255, 255), alpha=90)
put(im, lh("gold", 360), S // 2, 430, glow=(255, 240, 160))
put(im, hero("Bomber", "f", 170, 170), 96, 482)
put(im, hero("Charger", "f", 170, 170, flip=True), 420, 482)
label(im, "OVER 99", 448, 96)
out["b"] = frame(im)

# c: the Bomber, fuse lit, straight at you
im = sea_bg()
rays(im, (S // 2, 300), (255, 255, 255), alpha=60)
put(im, hero("Bomber", "f", 380, 360), S // 2, 486)
label(im, "OVER 99", 76, 104)
out["c"] = frame(im)

for k, im in out.items():
    small = im.resize((256, 256), Image.LANCZOS)
    small.save(os.path.join(IMG, f"icon_{k}.png"), optimize=True)
# side by side, at 256 and at 64, on Thunderstore's dark page colour
sheet = Image.new("RGBA", (3 * 276 + 20, 276 + 84 + 20), (32, 36, 44, 255))
for i, k in enumerate(out):
    small = Image.open(os.path.join(IMG, f"icon_{k}.png"))
    sheet.alpha_composite(small, (20 + i * 276, 20))
    sheet.alpha_composite(small.resize((64, 64), Image.LANCZOS), (20 + i * 276 + 96, 296 + 10))
sheet.convert("RGB").save(os.path.join(R, "_icons.png"))
print("icons")
