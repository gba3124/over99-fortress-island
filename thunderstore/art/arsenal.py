import os
from PIL import Image, ImageDraw, ImageFilter
from ui import *

GUNS = (("Rocket", "Rocket Launcher", "You start with it"), ("Laser", "Laser Gun", None), ("Gatling", "Gatling Gun", "After wave 80"),
        ("BubbleGun", "Bubble Gun", None), ("JellyGun", "Jellyfish Gun", None), ("WaterCannon", "Water Cannon", None),
        ("NetLauncher", "Net Launcher", None), ("Hook", "Grapple Gun", None), ("Whirlpool", "Whirlpool Cannon", None),
        ("PoisonGas", "Gas Gun", None), ("Conch", "Sonic Conch", None), ("SharkWhistle", "Shark Whistle", None),
        ("Boomerang", "Tuna Boomerang", None), ("StarThrower", "Starfish Thrower", None), ("PufferBomb", "Pufferfish Bomb", None),
        ("LobsterClaw", "Lobster Claw", None), ("Absorber", "Absorber", "Smithy only"))

W, COLS, GAP, TOP = 1200, 6, 16, 150
CW = (W - 60 - (COLS - 1) * GAP) // COLS
CH = 222
X0 = (W - COLS * CW - (COLS - 1) * GAP) // 2
rows = (len(GUNS) + COLS - 1) // COLS
STRIP = TOP + rows * (CH + GAP) + 14
H = STRIP + 262
im = vgrad(W, H, (70, 160, 222), (22, 80, 140))
bubbles(im, (0, 0, W, H), n=40, seed=11, alpha=55)
text(im, (W // 2, 58), "The arsenal", title(64), fill=WHITE, anchor="mm", stroke=7, stroke_fill=NAVY, drop=7)
text(im, (W // 2, 114), "17 fishy weapons. Fish and chests drop them, the smithy forges them up.", body(25, "Demi Bold"), fill=SAND, anchor="mm")

name_f, tag_f = title(21), body(15, "Bold")
for i, (ic, name, tag) in enumerate(GUNS):
    r, c = divmod(i, COLS)
    n_in_row = min(COLS, len(GUNS) - r * COLS)
    x = X0 + c * (CW + GAP) + (COLS - n_in_row) * (CW + GAP) // 2
    y = TOP + r * (CH + GAP)
    card(im, (x, y, x + CW, y + CH), fill=SAND, outline=SAND3, radius=22, width=3)
    ImageDraw.Draw(im).rounded_rectangle((x + 10, y + 10, x + CW - 10, y + 150), 16, fill=(196, 232, 250), outline=(150, 205, 238), width=2)
    pic = fit(icon("Gear" + ic, 256), CW - 30, 128)
    im.alpha_composite(pic, (x + (CW - pic.width) // 2, y + 16 + (128 - pic.height) // 2))
    lines = wrap(name, name_f, CW - 20)
    for j, line in enumerate(lines):
        text(im, (x + CW // 2, y + 186 + (j - (len(lines) - 1) / 2) * 25), line, name_f, fill=INK, anchor="mm")
    if tag:
        tw = tag_f.getlength(tag)
        ImageDraw.Draw(im).rounded_rectangle((x + CW // 2 - tw / 2 - 10, y + 134, x + CW // 2 + tw / 2 + 10, y + 158), 12, fill=CORAL,
                                             outline=NAVY, width=2)
        text(im, (x + CW // 2, y + 146), tag, tag_f, fill=WHITE, anchor="mm")

# the rarity strip: three of a kind at the smithy make one of the next
card(im, (30, STRIP, W - 30, STRIP + 232), fill=SAND, outline=SAND3, radius=28, width=3)
text(im, (W // 2, STRIP + 34), "Every weapon comes in four rarities", title(30), fill=INK, anchor="mm")
names = list(RARITY)
cw, gap = 200, 86
x = (W - 4 * cw - 3 * gap) // 2
for k, rar in enumerate(names):
    col = RARITY[rar]
    bx = x + k * (cw + gap)
    by = STRIP + 62
    glow = Image.new("RGBA", im.size, (0, 0, 0, 0))
    ImageDraw.Draw(glow).rounded_rectangle((bx, by, bx + cw, by + 112), 20, fill=(*col, 255))
    im.alpha_composite(glow.filter(ImageFilter.GaussianBlur(8)))
    ImageDraw.Draw(im).rounded_rectangle((bx, by, bx + cw, by + 112), 20, fill=tuple(int(v * 0.35 + 255 * 0.65) for v in col),
                                         outline=col, width=5)
    pic = fit(icon("GearRocket", 256), 120, 70)
    im.alpha_composite(pic, (bx + (cw - pic.width) // 2, by + 8 + (70 - pic.height) // 2))
    text(im, (bx + cw // 2, by + 94), rar.upper(), body(19, "Heavy"), fill=INK, anchor="mm")
    if k < 3:
        ax = bx + cw + gap // 2
        text(im, (ax, by + 40), "3x", title(24), fill=INK, anchor="mm")
        d = ImageDraw.Draw(im)
        d.line((ax - 26, by + 66, ax + 18, by + 66), fill=INK, width=6)
        d.polygon([(ax + 28, by + 66), (ax + 14, by + 56), (ax + 14, by + 76)], fill=INK)
text(im, (W // 2, STRIP + 203), "The smithy forges 3 of the same into 1 of the next rarity. Legendaries get a special power!",
     body(19, "Demi Bold"), fill=(80, 88, 100), anchor="mm")
im.convert("RGB").save(os.path.join(IMG, "arsenal.png"), optimize=True)
print("arsenal", im.size)
