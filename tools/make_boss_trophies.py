# The ten bosses' trophies: the part each leaves behind when it dies (picked up with E, sold at the fish crate, kept in
# the vault). Lying on the ground about 0.5-0.9 m long, origin at the middle of the bottom, front = -Y.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_boss_trophies.py
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Vector
from bkit import Builder, export, mat, tube, torus, fin

TAU = 2 * math.pi
rust = mat("TrophyRust", (0.55, 0.16, 0.08))
rust_dark = mat("TrophyRustDark", (0.3, 0.08, 0.05))
brass = mat("TrophyBrass", (0.85, 0.62, 0.25))
iron = mat("TrophyIron", (0.28, 0.28, 0.3))
ivory = mat("TrophyIvory", (0.92, 0.88, 0.76))
violet = mat("TrophyViolet", (0.16, 0.04, 0.2))
pink_glow = mat("TrophyPinkGlow", (1.0, 0.35, 0.85), (1.0, 0.3, 0.85))
navy = mat("TrophyNavy", (0.05, 0.07, 0.16))
cyan_glow = mat("TrophyCyanGlow", (0.45, 1.0, 1.0), (0.4, 1.0, 1.0))
gold = mat("TrophyGold", (1.0, 0.78, 0.3))
gold_glow = mat("TrophyGoldGlow", (1.0, 0.85, 0.4), (1.0, 0.8, 0.3))
silver = mat("TrophySilver", (0.82, 0.84, 0.9))
basalt = mat("TrophyBasalt", (0.13, 0.13, 0.12))
venom = mat("TrophyVenom", (0.55, 1.0, 0.3), (0.45, 1.0, 0.25))
steel = mat("TrophySteelBlue", (0.32, 0.46, 0.72))
white = mat("TrophyWhite", (0.95, 0.96, 1.0))
ribbon = mat("TrophyRibbon", (0.8, 0.1, 0.12))
crimson = mat("TrophyCrimson", (0.6, 0.04, 0.1))
sucker = mat("TrophySucker", (1.0, 0.62, 0.62))
pixel_green = mat("TrophyPixelGreen", (0.4, 1.0, 0.6), (0.35, 1.0, 0.55))
pixel_pink = mat("TrophyPixelPink", (1.0, 0.4, 0.8), (1.0, 0.35, 0.75))
pixel_dark = mat("TrophyPixelDark", (0.08, 0.1, 0.12))
dragon = mat("TrophyDragonRed", (0.75, 0.12, 0.06))
obsidian = mat("TrophyObsidian", (0.03, 0.02, 0.04))
red_glow = mat("TrophyRedGlow", (1.0, 0.08, 0.12), (1.0, 0.05, 0.1))
out = []


def arc(c, r, a0, a1, n, z=0.0, plane="XY"):
    pts = []
    for i in range(n + 1):
        a = a0 + (a1 - a0) * i / n
        x, y = math.cos(a) * r, math.sin(a) * r
        pts.append((c[0] + x, c[1] + y, c[2] + z) if plane == "XY" else (c[0] + x, c[1], c[2] + y))
    return pts


# 1. The Iron-Claw Warden: its claw, riveted, a broken shackle round the wrist.
g = Builder("TrophyWarden", [rust, rust_dark, brass, iron, ivory])
tube(g, [(0, 0.3, 0.1), (0, 0.12, 0.13), (0, -0.08, 0.15), (0, -0.2, 0.15)], [0.06, 0.11, 0.12, 0.1], rust, segs=8)
tube(g, [(0, -0.2, 0.2), (0.01, -0.36, 0.25), (0, -0.5, 0.23), (-0.01, -0.6, 0.17)], [0.075, 0.06, 0.04, 0.0], rust, segs=7)
tube(g, [(0, -0.2, 0.09), (0.01, -0.36, 0.05), (0, -0.48, 0.07), (-0.01, -0.56, 0.13)], [0.06, 0.05, 0.03, 0.0], rust_dark, segs=7)
for i in range(4):
    y = -0.3 - i * 0.065
    g.cone(0.014, 0.0, 0.04, (0, y, 0.205 - i * 0.008), ivory, segs=4, axis="Z")
    g.cone(0.012, 0.0, 0.035, (0, y + 0.02, 0.105 + i * 0.006), ivory, segs=4, axis="Z")
for a in range(6):
    t = a * TAU / 6
    g.ico(0.014, (math.cos(t) * 0.115, -0.02 + math.sin(t) * 0.04, 0.14 + math.sin(t) * 0.1), brass)
torus(g, (0, 0.24, 0.11), (0, 1, 0), 0.085, 0.018, iron, segs=14, tsegs=5)
torus(g, (0.06, 0.33, 0.04), (1, 0, 0.2), 0.035, 0.01, iron, segs=10, tsegs=4)
torus(g, (0.06, 0.39, 0.02), (0, 1, 0), 0.035, 0.01, iron, segs=10, tsegs=4)
out.append(g.finish())

# 2. The Thousand-Needle Urchin: one of its great spikes, glowing at the tip, a piece of its shell at the root.
g = Builder("TrophyUrchin", [violet, pink_glow])
g.ico(0.1, (0, 0.33, 0.09), violet, subdiv=1)
g.cone(0.055, 0.012, 0.66, (0, -0.02, 0.1), violet, segs=6, axis="Y")
g.cone(0.014, 0.0, 0.16, (0, -0.42, 0.1), pink_glow, segs=6, axis="-Y")
for k in range(5):
    t = k * TAU / 5
    d = Vector((math.cos(t), 0.4, math.sin(t))).normalized()
    a = Vector((0, 0.33, 0.09)) + d * 0.08
    g.beam(tuple(a), tuple(a + d * 0.16), 0.016, violet, segs=4, r2=0.0)
out.append(g.finish())

# 3. The Abyssal Lantern: its lure, a bright ball on a bent stalk.
g = Builder("TrophyAngler", [navy, cyan_glow])
tube(g, [(0, 0.32, 0.03), (0, 0.18, 0.16), (0, 0.0, 0.27), (0, -0.16, 0.27), (0, -0.24, 0.19)], [0.045, 0.035, 0.028, 0.022, 0.018], navy, segs=6)
g.ico(0.1, (0, -0.26, 0.11), cyan_glow, subdiv=2)
g.cone(0.05, 0.03, 0.05, (0, -0.25, 0.2), navy, segs=8)
g.ico(0.07, (0, 0.32, 0.03), navy, subdiv=1)
out.append(g.finish())

# 4. The Silver Steed: a golden horseshoe, silver nails, a gem at its crown.
g = Builder("TrophySteed", [gold, silver, gold_glow])
shoe = [(0.13, 0.17, 0.035)] + arc((0, 0, 0), 0.13, 0.0, math.pi, 14, 0.035, "XY")[::-1][::-1] + [(-0.13, 0.17, 0.035)]
shoe = [(0.13, 0.17, 0.035)] + [(math.cos(a) * 0.13, -math.sin(a) * 0.13, 0.035) for a in [i * math.pi / 14 for i in range(15)]] + [(-0.13, 0.17, 0.035)]
tube(g, shoe, 0.032, gold, segs=6)
for a in (0.35, 0.8, 1.25, 1.9, 2.35, 2.8):
    g.box((0.018, 0.018, 0.03), (math.cos(a) * 0.13, -math.sin(a) * 0.13, 0.07), silver)
g.ico(0.04, (0, -0.135, 0.085), gold_glow, subdiv=1)
out.append(g.finish())

# 5. The Venom Rock Emperor: a lump of its rock with three poison spines.
g = Builder("TrophyStonefish", [basalt, venom])
g.ico(0.15, (0, 0.05, 0.12), basalt, subdiv=1)
g.ico(0.09, (0.08, 0.16, 0.08), basalt, subdiv=1)
for d, l in (((0, -0.5, 0.75), 0.42), ((0.45, -0.2, 0.7), 0.32), ((-0.45, -0.15, 0.75), 0.34)):
    v = Vector(d).normalized()
    a = Vector((0, 0.05, 0.12)) + v * 0.1
    g.beam(tuple(a), tuple(a + v * l), 0.03, venom, segs=5, r2=0.0)
for k in range(5):
    t = k * TAU / 5 + 0.4
    g.ico(0.025, (math.cos(t) * 0.14, 0.05 + math.sin(t) * 0.14, 0.05), venom)
out.append(g.finish())

# 6. The Sky Torpedo Admiral: its wing, ribbed, with an admiral's star pinned on.
g = Builder("TrophySkyfish", [steel, white, gold, ribbon])
outline = [(0.0, 0.28, 0.04), (0.08, 0.1, 0.04), (0.2, -0.1, 0.04), (0.3, -0.36, 0.04), (0.18, -0.4, 0.04), (0.06, -0.22, 0.04), (-0.04, 0.0, 0.04), (-0.06, 0.24, 0.04)]
fin(g, outline, (0, 0, 1), 0.02, steel)
for x, y in ((0.05, -0.05), (0.13, -0.18), (0.22, -0.32)):
    g.beam((0.0, 0.25, 0.055), (x, y, 0.055), 0.008, white, segs=4)
g.prism([(0.0, 0.11), (0.035, 0.0), (-0.035, 0.0)], 0.2, 0.02, gold)
g.prism([(0.0, -0.035), (0.035, 0.075), (-0.035, 0.075)], 0.2, 0.02, gold)
g.box((0.05, 0.02, 0.09), (0.0, 0.29, 0.06), ribbon)
out.append(g.finish())

# 7. The Crimson Kraken: a tentacle, curled, suckers on the inside.
g = Builder("TrophyKraken", [crimson, sucker])
path, radii = [], []
for i in range(22):
    k = i / 21
    a = k * 2.6 * math.pi * 0.6
    r = 0.24 * (1 - k * 0.75)
    path.append((math.cos(a) * r, math.sin(a) * r - 0.05, 0.07 + 0.02 * math.sin(k * 6)))
    radii.append(0.07 * (1 - k) + 0.008)
tube(g, path, radii, crimson, segs=7)
for i in range(2, 19, 2):
    p, q = Vector(path[i]), Vector(path[i + 1])
    inward = -Vector((p.x, p.y + 0.05, 0)).normalized()
    g.ico(radii[i] * 0.45, tuple(p + inward * radii[i] * 0.8), sucker)
out.append(g.finish())

# 8. The Shattered Pixel King: its core, a block of glowing pixels with bits missing, one floating free.
g = Builder("TrophyVoxel", [pixel_dark, pixel_green, pixel_pink])
s = 0.075
for x in range(-1, 2):
    for y in range(-1, 2):
        for z in range(0, 3):
            if (x, y, z) in ((1, -1, 2), (-1, 1, 2), (1, 1, 0)):
                continue
            m = pixel_pink if (x + y + z) % 3 == 0 else pixel_green if (x * y + z) % 2 == 0 else pixel_dark
            g.box((s * 0.94, s * 0.94, s * 0.94), (x * s, y * s, s * 0.5 + z * s), m)
g.box((s * 0.8, s * 0.8, s * 0.8), (s * 1.7, -s * 1.6, s * 3.6), pixel_pink, rot=(0.4, 0.3, 0.2))
out.append(g.finish())

# 9. The Koi of the Dragon Gate: the dragon's pearl, held in three claws.
g = Builder("TrophyKoi", [gold_glow, dragon, gold])
g.cyl(0.09, 0.03, (0, 0, 0.015), gold, segs=12)
g.ico(0.105, (0, 0, 0.16), gold_glow, subdiv=2)
for k in range(3):
    t = k * TAU / 3
    c, s2 = math.cos(t), math.sin(t)
    tube(g, [(c * 0.06, s2 * 0.06, 0.03), (c * 0.13, s2 * 0.13, 0.11), (c * 0.11, s2 * 0.11, 0.22), (c * 0.05, s2 * 0.05, 0.27)], [0.025, 0.022, 0.016, 0.0], dragon, segs=5)
out.append(g.finish())

# 10. The Eye at the End: a shard of black glass with its red eye still open in it.
g = Builder("TrophyEye", [obsidian, red_glow])
g.cone(0.13, 0.0, 0.26, (0, 0, 0.27), obsidian, segs=6)
g.cone(0.0, 0.13, 0.18, (0, 0, 0.05), obsidian, segs=6)
g.box((0.2, 0.03, 0.035), (0, -0.115, 0.15), red_glow)
g.box((0.1, 0.031, 0.06), (0, -0.116, 0.15), red_glow)
for k in range(3):
    t = k * TAU / 3 + 0.5
    g.cone(0.03, 0.0, 0.09, (math.cos(t) * 0.22, math.sin(t) * 0.22, 0.1), obsidian, segs=4)
out.append(g.finish())

export(out)
