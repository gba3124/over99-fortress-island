import os
from PIL import Image, ImageDraw
from ui import *

PIECES = (("Wall", "Wall", "Blocks the fish", "LV 1"), ("WallStone", "Stone wall", "Holds out longer", "LV 2"),
          ("WallMetal", "Metal wall", "Holds out longest", "LV 3"), ("Floor", "Floor", "Build platforms up high", None),
          ("Ramp", "Ramp", "A slope up onto a platform", None),
          ("Gun", "Machine gun", "Fast fire at the nearest fish", None), ("Cannon", "Cannon", "Shells that burst where they land", None),
          ("Harpoon", "Harpoon", "Spears a whole line and slows it", None), ("Mortar", "Mortar", "Longest range, not within 8 m", None),
          ("Tesla", "Tesla coil", "Shock jumps fish to fish", None), ("Flame", "Flamethrower", "A short cone of fire", None),
          ("Sniper", "Sniper Tower", "Hits the toughest fish hard", None), ("Spikes", "Spike trap", "Hurts fish standing on it", None),
          ("Bubble", "Bubble Shield", "30-50% less damage inside", None), ("Mender", "Repair Tower", "Heals buildings in its ring", None))
VILLAGE = (("HelpLighthouse", "Lighthouse", "[E] at its door opens the gate"), ("HelpPearl", "The pearl", "What the fish want. Guard it!"),
           ("HelpStall", "Fish stall", "Buys your fish, sells nets and helpers"), ("HelpSmithy", "Smithy", "3 of a kind forge into a better one"),
           ("HelpVault", "Vault", "Your loot lands here. Gear up!"))

W, COLS, CW, CH, GAP, TOP = 1200, 5, 212, 266, 20, 150
X0 = (W - COLS * CW - (COLS - 1) * GAP) // 2
VT = TOP + 3 * (CH + GAP) + 10
H = VT + 90 + CH + 40
im = vgrad(W, H, (70, 160, 222), (22, 80, 140))
bubbles(im, (0, 0, W, H), n=44, seed=5, alpha=55)
text(im, (W // 2, 58), "Build your fortress", title(64), fill=WHITE, anchor="mm", stroke=7, stroke_fill=NAVY, drop=7)
text(im, (W // 2, 114), "[G] to build. 90 seconds between waves. Upgrade and repair with [RMB].", body(25, "Demi Bold"), fill=SAND, anchor="mm")
name_f, tip_f, lv_f = title(25), body(18, "Demi Bold"), body(16, "Bold")


def tile(x, y, pic, name, tip, badge=None, panel=(196, 232, 250), rim=(150, 205, 238)):
    card(im, (x, y, x + CW, y + CH), fill=SAND, outline=SAND3, radius=24, width=3)
    ImageDraw.Draw(im).rounded_rectangle((x + 12, y + 12, x + CW - 12, y + 158), 18, fill=panel, outline=rim, width=2)
    pic = fit(pic, CW - 44, 132)
    im.alpha_composite(pic, (x + (CW - pic.width) // 2, y + 18 + (136 - pic.height) // 2))
    if badge:
        bw = lv_f.getlength(badge)
        ImageDraw.Draw(im).rounded_rectangle((x + 18, y + 18, x + 38 + bw, y + 44), 13, fill=SEA, outline=NAVY, width=2)
        text(im, (x + 28, y + 31), badge, lv_f, fill=WHITE, anchor="lm")
    text(im, (x + CW // 2, y + 184), name, name_f, fill=INK, anchor="mm")
    for j, line in enumerate(wrap(tip, tip_f, CW - 26)):
        text(im, (x + CW // 2, y + 214 + j * 23), line, tip_f, fill=(70, 80, 96), anchor="mm")


for i, (ic, name, tip, lv) in enumerate(PIECES):
    x = X0 + (i % COLS) * (CW + GAP)
    y = TOP + (i // COLS) * (CH + GAP)
    tile(x, y, icon("Piece" + ic, 256), name, tip, lv)
    if i < 2:  # a wall upgrades: wood, stone, metal
        ax, ay = x + CW + GAP // 2, y + 86
        d = ImageDraw.Draw(im)
        d.ellipse((ax - 17, ay - 17, ax + 17, ay + 17), fill=GOLD, outline=NAVY, width=3)
        d.polygon([(ax - 6, ay - 9), (ax + 9, ay), (ax - 6, ay + 9)], fill=NAVY)

# the village: a sand band under the pieces
card(im, (20, VT, W - 20, H - 20), fill=(236, 214, 168), outline=SAND3, radius=30, width=3)
text(im, (W // 2, VT + 42), "The village", title(40), fill=INK, anchor="mm")
for i, (ic, name, tip) in enumerate(VILLAGE):
    tile(X0 + i * (CW + GAP), VT + 74, icon(ic, 256), name, tip, panel=(250, 240, 214), rim=SAND2)
im.convert("RGB").save(os.path.join(IMG, "build.png"), optimize=True)
print("build", im.size)
