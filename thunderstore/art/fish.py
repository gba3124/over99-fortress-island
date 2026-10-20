import os
from PIL import Image, ImageDraw
from ui import *

FISH = (("Runner", 1, "Runner", "Runs straight at the lighthouse"),
        ("Charger", 3, "Charger", "Fast: charges straight at you"),
        ("Thief", 4, "Thief", "Steals fish and money. Chase it!"),
        ("Spitter", 5, "Spitter", "Spits at you from the sea"),
        ("Splitter", 5, "Splitter", "Splits into 3 when it dies"),
        ("Bomber", 6, "Bomber", "Blows up on whatever it touches"),
        ("Healer", 6, "Healer", "Heals the others. Kill it first!"),
        ("Shield", 7, "Shield Bearer", "Blocks shots in front. Flank it!"),
        ("Tank", 8, "Tank Crab", "Tough, slow, wrecks walls"),
        ("Digger", 9, "Digger", "Tunnels under your walls"))

W, COLS, CW, CH, GAP, TOP = 1200, 5, 212, 316, 20, 150
X0 = (W - COLS * CW - (COLS - 1) * GAP) // 2
H = TOP + 2 * CH + GAP + 120
im = vgrad(W, H, (70, 160, 222), (22, 80, 140))
bubbles(im, (0, 0, W, H), n=40, seed=7, alpha=55)
text(im, (W // 2, 58), "Meet the fish", title(64), fill=WHITE, anchor="mm", stroke=7, stroke_fill=NAVY, drop=7)
text(im, (W // 2, 114), "Ten kinds come for your lighthouse. Learn their tricks!", body(25, "Demi Bold"), fill=SAND, anchor="mm")

name_f, tip_f, wave_f = title(27), body(19, "Demi Bold"), body(16, "Bold")
for i, (kind, wave, name, tip) in enumerate(FISH):
    x = X0 + (i % COLS) * (CW + GAP)
    y = TOP + (i // COLS) * (CH + GAP)
    card(im, (x, y, x + CW, y + CH), fill=SAND, outline=SAND3, radius=24, width=3)
    ImageDraw.Draw(im).rounded_rectangle((x + 12, y + 12, x + CW - 12, y + 186), 18, fill=(196, 232, 250), outline=(150, 205, 238), width=2)
    pic = Image.open(os.path.join(R, "runner.png")) if kind == "Runner" else Image.open(os.path.join(ICONS, f"HelpFoe{kind}.png"))
    pic = fit(pic.convert("RGBA"), CW - 36, 160)
    im.alpha_composite(pic, (x + (CW - pic.width) // 2, y + 18 + (162 - pic.height) // 2))
    s = f"WAVE {wave}+"
    ww = wave_f.getlength(s)
    ImageDraw.Draw(im).rounded_rectangle((x + 18, y + 18, x + 18 + ww + 20, y + 44), 13, fill=SEA, outline=NAVY, width=2)
    text(im, (x + 28, y + 31), s, wave_f, fill=WHITE, anchor="lm")
    text(im, (x + CW // 2, y + 214), name, name_f, fill=INK, anchor="mm")
    for j, line in enumerate(wrap(tip, tip_f, CW - 28)):
        text(im, (x + CW // 2, y + 252 + j * 25), line, tip_f, fill=(70, 80, 96), anchor="mm")

y = TOP + 2 * CH + GAP + 26
card(im, (X0, y, W - X0, y + 70), fill=(250, 238, 210), outline=SAND3, radius=35, width=3)
text(im, (W // 2, y + 24), "In game, every one of them runs on a pair of sneakers.", body(22, "Bold"), fill=INK, anchor="mm")
text(im, (W // 2, y + 50), "Every 5 waves the elites come with a gold crown (x3 loot). Every 10 waves: a boss!", body(18, "Demi Bold"),
     fill=(90, 96, 108), anchor="mm")
im.convert("RGB").save(os.path.join(IMG, "fish.png"), optimize=True)
print("fish", im.size)
