import math, os
from PIL import Image, ImageDraw, ImageFilter
from ui import *

W, H = 1200, 670
CW, CH, GAP, TOP = 200, 326, 38, 150
X0 = (W - 5 * CW - 4 * GAP) // 2
im = vgrad(W, H, (70, 160, 222), (22, 80, 140))
bubbles(im, (0, 0, W, H), n=30, seed=9, alpha=55)
text(im, (W // 2, 58), "How a run goes", title(64), fill=WHITE, anchor="mm", stroke=7, stroke_fill=NAVY, drop=7)
text(im, (W // 2, 114), "Build, fight, loot, repeat. Can you reach wave 100?", body(25, "Demi Bold"), fill=SAND, anchor="mm")


def group(*parts, w=CW - 30, h=140):  # a few icons overlapped into one little scene: (picture, x, y, size) in a 100x70 box
    out = Image.new("RGBA", (400, 280), (0, 0, 0, 0))
    for pic, x, y, size in parts:
        p = fit(pic, size * 4, size * 4)
        out.alpha_composite(p, (int(x * 4 - p.width / 2), int(y * 4 - p.height / 2)))
    return fit(out, w, h)


lh_gold = Image.open(os.path.join(R, "lighthouse_gold.png")).convert("RGBA")
STEPS = (("1", "Build (90 s)", "Walls, turrets and traps",
          group((icon("PieceWallStone", 256), 34, 34, 56), (icon("PieceCannon", 256), 66, 40, 58))),
         ("2", "Fish wave", "They storm the beaches. Hold the lighthouse!",
          group((icon("HelpFoeTank", 256), 66, 30, 50), (icon("HelpFoeBomber", 256), 30, 30, 46), (icon("HelpFoeCharger", 256), 50, 48, 50))),
         ("3", "Every 10 waves: a boss", "Dodge the red warnings!",
          group((icon("HelpBoss", 256), 50, 36, 72))),
         ("4", "Loot, forge, upgrade", "Grab the drops, sell fish, forge 3 into 1",
          group((icon("HelpSmithy", 256), 38, 32, 56), (icon("HelpLoot", 256), 68, 44, 46))),
         ("100", "Wave 100: OVER 99!", "The lighthouse turns gold!",
          group((lh_gold, 50, 35, 70))))

name_f, tip_f = title(25), body(18, "Demi Bold")
for i, (num, name, tip, pic) in enumerate(STEPS):
    x, y = X0 + i * (CW + GAP), TOP
    last = i == 4
    if last:  # the goal shines
        glow = Image.new("RGBA", im.size, (0, 0, 0, 0))
        ImageDraw.Draw(glow).rounded_rectangle((x - 6, y - 6, x + CW + 6, y + CH + 6), 30, fill=(255, 214, 90, 255))
        im.alpha_composite(glow.filter(ImageFilter.GaussianBlur(14)))
    card(im, (x, y, x + CW, y + CH), fill=(255, 236, 170) if last else SAND, outline=(214, 150, 30) if last else SAND3, radius=24, width=3)
    ImageDraw.Draw(im).rounded_rectangle((x + 12, y + 12, x + CW - 12, y + 170), 18, fill=(255, 248, 220) if last else (196, 232, 250),
                                         outline=(240, 196, 90) if last else (150, 205, 238), width=2)
    im.alpha_composite(pic, (x + (CW - pic.width) // 2, y + 20 + (150 - pic.height) // 2))
    bf = body(18, "Heavy")
    bw = max(30, bf.getlength(num) + 18)
    ImageDraw.Draw(im).rounded_rectangle((x + 16, y + 16, x + 16 + bw, y + 46), 15, fill=GOLD if last else SEA, outline=NAVY, width=2)
    text(im, (x + 16 + bw / 2, y + 31), num, bf, fill=INK if last else WHITE, anchor="mm")
    lines = wrap(name, name_f, CW - 20)
    for j, line in enumerate(lines):
        text(im, (x + CW // 2, y + 198 + j * 28), line, name_f, fill=INK, anchor="mm")
    for j, line in enumerate(wrap(tip, tip_f, CW - 24)):
        text(im, (x + CW // 2, y + 198 + len(lines) * 28 + 2 + j * 23), line, tip_f, fill=(70, 80, 96), anchor="mm")
    if i < 4:  # arrows between the steps
        ax, ay = x + CW + GAP // 2, y + CH // 2
        d = ImageDraw.Draw(im)
        d.ellipse((ax - 17, ay - 17, ax + 17, ay + 17), fill=GOLD, outline=NAVY, width=3)
        d.polygon([(ax - 6, ay - 9), (ax + 9, ay), (ax - 6, ay + 9)], fill=NAVY)

# the loop back from 4 to 1, under the cards
x1, x4 = X0 + CW // 2, X0 + 3 * (CW + GAP) + CW // 2
yb, depth = TOP + CH + 8, 64
d = ImageDraw.Draw(im)
pts = [(x4 + (x1 - x4) * t, yb + depth * math.sin(math.pi * t) ** 0.6) for t in [k / 60 for k in range(61)]]
for a, b in zip(pts, pts[1:]):
    d.line((a, b), fill=SAND, width=8)
d.polygon([(x1 - 13, yb + 16), (x1 + 13, yb + 16), (x1, yb - 4)], fill=SAND)
lbl = "Again! Wave after wave"
lw = body(20, "Heavy").getlength(lbl)
cx, cy = (x1 + x4) / 2, yb + depth
card(im, (cx - lw / 2 - 22, cy - 20, cx + lw / 2 + 22, cy + 20), fill=CORAL, outline=NAVY, radius=20, width=3, drop=False)
text(im, (cx, cy), lbl, body(20, "Heavy"), fill=WHITE, anchor="mm")
text(im, (W // 2, H - 34), "Play with friends: every extra player brings 40% more fish!", body(22, "Bold"), fill=SAND, anchor="mm")
im.convert("RGB").save(os.path.join(IMG, "loop.png"), optimize=True)
print("loop", im.size)
