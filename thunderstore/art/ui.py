# The one look shared by every Thunderstore picture: sea-blue and sand, rounded cards, chunky type.
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

WORK = os.path.dirname(os.path.abspath(__file__))
IMG = os.path.join(os.path.dirname(WORK), "img")
R = os.path.join(WORK, "r")
ICONS = os.path.join(os.path.dirname(WORK), "..", "HtfFortress", "Icons")

NAVY = (18, 50, 82)
SEA = (38, 120, 186)
SEA2 = (64, 158, 214)
SKY = (186, 226, 250)
SAND = (250, 238, 210)
SAND2 = (232, 206, 156)
SAND3 = (196, 160, 104)
INK = (30, 42, 58)
GOLD = (255, 200, 58)
CORAL = (236, 96, 74)
WHITE = (255, 255, 255)
RARITY = {"Common": (232, 232, 232), "Rare": (74, 163, 255), "Epic": (181, 92, 255), "Legendary": (255, 181, 46)}

_TITLE = "/System/Library/Fonts/Supplemental/Arial Rounded Bold.ttf"
_BODY = "/System/Library/Fonts/Avenir Next.ttc"


def title(size):
    return ImageFont.truetype(_TITLE, size)


def body(size, weight="Demi Bold"):
    return ImageFont.truetype(_BODY, size, index={"Bold": 0, "Demi Bold": 2, "Medium": 5, "Regular": 7, "Heavy": 8}[weight])


def vgrad(w, h, top, bottom):
    im = Image.new("RGB", (1, h))
    for y in range(h):
        t = y / max(1, h - 1)
        im.putpixel((0, y), tuple(int(a + (b - a) * t) for a, b in zip(top, bottom)))
    return im.resize((w, h)).convert("RGBA")


def shadow(im, box, radius, blur=10, offset=(0, 6), alpha=70):
    """A soft drop shadow under a rounded box."""
    sh = Image.new("RGBA", im.size, (0, 0, 0, 0))
    x0, y0, x1, y1 = box
    ImageDraw.Draw(sh).rounded_rectangle((x0 + offset[0], y0 + offset[1], x1 + offset[0], y1 + offset[1]), radius, fill=(10, 30, 50, alpha))
    im.alpha_composite(sh.filter(ImageFilter.GaussianBlur(blur)))


def card(im, box, fill=SAND, outline=SAND3, radius=22, width=3, drop=True):
    if drop:
        shadow(im, box, radius)
    ImageDraw.Draw(im).rounded_rectangle(box, radius, fill=fill, outline=outline, width=width)


def text(im, xy, s, font, fill=INK, anchor="la", stroke=0, stroke_fill=NAVY, drop=0):
    d = ImageDraw.Draw(im)
    if drop:
        sh = Image.new("RGBA", im.size, (0, 0, 0, 0))
        ImageDraw.Draw(sh).text((xy[0], xy[1] + drop), s, font=font, fill=(10, 30, 50, 150), anchor=anchor,
                                stroke_width=stroke, stroke_fill=(10, 30, 50, 150))
        im.alpha_composite(sh.filter(ImageFilter.GaussianBlur(max(1, drop // 2))))
    d.text(xy, s, font=font, fill=fill, anchor=anchor, stroke_width=stroke, stroke_fill=stroke_fill)


def icon(name, size):
    return Image.open(os.path.join(ICONS, name + ".png")).convert("RGBA").resize((size, size), Image.LANCZOS)


def fit(im, w, h):  # scale an RGBA picture to fit in w x h, keeping its shape (cropped to what's drawn first)
    im = im.crop(im.getbbox())
    k = min(w / im.width, h / im.height)
    return im.resize((max(1, round(im.width * k)), max(1, round(im.height * k))), Image.LANCZOS)


def wrap(s, font, width):
    words, lines, cur = s.split(), [], ""
    for w in words:
        t = (cur + " " + w).strip()
        if font.getlength(t) <= width or not cur:
            cur = t
        else:
            lines.append(cur)
            cur = w
    return lines + [cur] if cur else lines


def bubbles(im, box, n=14, seed=3, alpha=60):  # a few sea bubbles for the backgrounds
    import random
    rnd = random.Random(seed)
    layer = Image.new("RGBA", im.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    x0, y0, x1, y1 = box
    for _ in range(n):
        r = rnd.randint(4, 14)
        x, y = rnd.randint(x0, x1), rnd.randint(y0, y1)
        d.ellipse((x - r, y - r, x + r, y + r), outline=(255, 255, 255, alpha), width=2)
        d.ellipse((x - r * 0.45, y - r * 0.55, x - r * 0.1, y - r * 0.2), fill=(255, 255, 255, alpha))
    im.alpha_composite(layer)
