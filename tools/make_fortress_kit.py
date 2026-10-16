# The fortress kit, second pass: turret base and heads, the lighthouse, stone / iron walls, enemy gear.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_fortress_kit.py
# Conventions (match the game code): turret heads pivot at the origin, sit on z = 0 and point toward -Y;
# walls are centred on the origin inside x ±1.5, z ±1.5; everything else stands on z = 0, front toward -Y.
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bkit import Builder, M, export, mat

stone, rock, iron, metal, brass, dark, wood, rope, steel, copper, coil, crystal, black, gold, moss = (
    M(n) for n in ("Stone", "Rock", "Iron", "Metal", "Brass", "DarkWood", "Wood", "Rope", "Steel", "Copper", "Coil", "Crystal", "BombBlack", "Gold", "Moss"))
white = mat("White", (0.82, 0.79, 0.74), force=True)  # cream: pure white blooms in the game's bright light
red = mat("RoofRed", (0.62, 0.24, 0.19))
helmet = mat("HelmetRed", (0.78, 0.2, 0.16))
bone = mat("Bone", (0.93, 0.89, 0.78))
out = []
TAU = 2 * math.pi


def ring_points(r, n, z, phase=0.0):
    return [(r * math.cos(phase + i * TAU / n), r * math.sin(phase + i * TAU / n), z) for i in range(n)]


# ---------- turret base: stone plinth, iron band with brass bolts, turntable (top at z = 0.85) ----------
b = Builder("TurretBase", [stone, iron, brass, dark])
b.cyl(0.74, 0.46, (0, 0, 0.23), stone, segs=8)
b.cyl(0.77, 0.09, (0, 0, 0.44), iron, segs=8)
for p in ring_points(0.78, 8, 0.44, math.pi / 8):
    b.box((0.07, 0.07, 0.07), p, brass)
b.cyl(0.62, 0.22, (0, 0, 0.6), stone, segs=8)
b.cyl(0.57, 0.1, (0, 0, 0.76), iron, segs=12)
b.cyl(0.6, 0.03, (0, 0, 0.7), brass, segs=12)            # brass rim just under the turntable
for p in ring_points(0.74, 4, 0.12, math.pi / 4):             # wooden feet
    b.box((0.34, 0.12, 0.14), p, dark, rot=(0, 0, math.atan2(p[1], p[0])))
out.append(b.finish())

# ---------- gun: a six-barrel gatling with an ammo drum and a shield plate ----------
g = Builder("GunHead", [iron, metal, brass, dark, steel])
g.box((0.5, 0.42, 0.1), (0, 0.05, 0.05), iron)                 # yoke
for x in (-0.24, 0.24):
    g.box((0.06, 0.34, 0.4), (x, 0.05, 0.28), iron)
g.box((0.36, 0.52, 0.3), (0, 0.08, 0.36), metal)              # receiver
g.box((0.3, 0.12, 0.12), (0, 0.38, 0.38), dark)               # grip block at the back
for p in ring_points(0.085, 6, 0.0):                           # barrels around the spindle
    g.beam((p[0], -0.18, 0.36 + p[1]), (p[0], -1.14, 0.36 + p[1]), 0.032, steel, segs=6)
g.beam((0, -0.15, 0.36), (0, -1.12, 0.36), 0.04, iron, segs=6)
for y in (-0.5, -1.08):
    g.cyl(0.14, 0.05, (0, y, 0.36), brass, segs=10, axis="Y")
g.cyl(0.17, 0.2, (0.33, 0.06, 0.32), metal, segs=10, axis="X")  # ammo drum
g.cyl(0.18, 0.03, (0.43, 0.06, 0.32), brass, segs=10, axis="X")
g.box((0.48, 0.05, 0.26), (0, -0.2, 0.43), iron)              # shield plate
out.append(g.finish())

# ---------- cannon: fat iron barrel on a wheeled wooden carriage ----------
c = Builder("CannonHead", [iron, brass, dark, wood, black])
for x in (-0.22, 0.22):
    c.box((0.08, 0.86, 0.32), (x, 0.05, 0.22), dark)          # carriage cheeks
c.box((0.36, 0.7, 0.06), (0, 0.08, 0.07), dark)
c.beam((-0.34, -0.18, 0.18), (0.34, -0.18, 0.18), 0.04, iron, segs=6)
for x in (-0.34, 0.34):
    c.cyl(0.22, 0.07, (x, -0.18, 0.2), wood, segs=10, axis="X")
    c.cyl(0.07, 0.1, (x, -0.18, 0.2), iron, segs=6, axis="X")
    c.cyl(0.13, 0.06, (x * 0.9, 0.38, 0.13), wood, segs=8, axis="X")
c.cone(0.21, 0.16, 1.2, (0, -0.4, 0.44), iron, segs=12, axis="Y")  # barrel: wide at the back, tapering forward
c.cyl(0.2, 0.09, (0, -0.98, 0.44), iron, segs=12, axis="Y")       # muzzle swell
c.cyl(0.12, 0.02, (0, -1.03, 0.44), black, segs=10, axis="Y")     # bore
for y in (-0.08, -0.62):
    c.cyl(0.225, 0.05, (0, y, 0.44), brass, segs=12, axis="Y")
c.ico(0.08, (0, 0.27, 0.44), iron)                             # cascabel
c.beam((0, 0.02, 0.44), (0, 0.02, 0.62), 0.025, brass, segs=4)  # touch-hole
out.append(c.finish())

# ---------- harpoon: a ballista with a barbed bolt and a rope reel ----------
h = Builder("HarpoonHead", [dark, wood, steel, rope, iron])
h.box((0.16, 1.5, 0.14), (0, -0.2, 0.3), dark)                # stock
h.box((0.3, 0.2, 0.22), (0, -0.82, 0.3), iron)                 # bow bracket
tips = []
for s in (-1, 1):
    a, tip = (s * 0.12, -0.82, 0.32), (s * 0.78, -0.55, 0.32)
    h.beam(a, tip, 0.05, wood, segs=4, r2=0.035)
    h.box((0.07, 0.07, 0.07), tip, iron)
    tips.append(tip)
nock = (0, 0.12, 0.34)
for t in tips:
    h.beam(t, nock, 0.012, rope, segs=4)
h.beam((0, 0.2, 0.42), (0, -1.15, 0.42), 0.03, steel, segs=6)  # bolt shaft
h.beam((0, -1.15, 0.42), (0, -1.42, 0.42), 0.08, steel, segs=4, r2=0.0)  # head
for s in (-1, 1):
    h.beam((0, -1.18, 0.42), (s * 0.12, -1.08, 0.42), 0.015, steel, segs=4)  # barbs
h.cyl(0.11, 0.26, (0, 0.5, 0.26), rope, segs=10, axis="X")    # rope reel
h.cyl(0.12, 0.03, (-0.14, 0.5, 0.26), iron, segs=10, axis="X")
h.cyl(0.12, 0.03, (0.14, 0.5, 0.26), iron, segs=10, axis="X")
h.box((0.06, 0.06, 0.18), (0.2, 0.5, 0.34), iron)              # crank
out.append(h.finish())

# ---------- mortar: a stubby, wide barrel tilted up on a bolted base plate ----------
m = Builder("MortarHead", [iron, brass, black, dark])
m.cyl(0.46, 0.08, (0, 0, 0.04), iron, segs=10)
for p in ring_points(0.4, 8, 0.09, math.pi / 8):
    m.box((0.06, 0.06, 0.04), p, brass)
for x in (-0.24, 0.24):
    m.box((0.08, 0.34, 0.36), (x, 0.02, 0.26), iron)          # cradle cheeks
m.beam((-0.3, 0.02, 0.36), (0.3, 0.02, 0.36), 0.06, brass, segs=8)  # trunnion
base, muzzle = (0, 0.22, 0.24), (0, -0.32, 0.92)
m.beam(base, muzzle, 0.23, iron, segs=12, r2=0.21)
m.beam((0, -0.27, 0.86), (0, -0.36, 0.97), 0.25, brass, segs=12)  # muzzle ring
m.beam((0, -0.35, 0.96), (0, -0.37, 0.985), 0.16, black, segs=10)  # bore
m.beam((0, 0.1, 0.38), (0, 0.05, 0.44), 0.245, brass, segs=12)
out.append(m.finish())

# ---------- tesla: copper column with glowing rings, a charged sphere and three prongs ----------
t = Builder("TeslaHead", [iron, copper, coil, brass])
t.cyl(0.3, 0.18, (0, 0, 0.09), iron, segs=8)
for i, z in enumerate((0.22, 0.29)):
    t.cyl(0.24 - i * 0.04, 0.06, (0, 0, z), copper, segs=10)
t.cyl(0.09, 1.0, (0, 0, 0.8), copper, segs=8)
for z in (0.48, 0.72, 0.96):
    t.cyl(0.21, 0.04, (0, 0, z), coil, segs=12)
t.cyl(0.14, 0.05, (0, 0, 1.22), brass, segs=10)
for p in ring_points(0.14, 3, 1.22):
    t.beam(p, (p[0] * 2.0, p[1] * 2.0, 1.48), 0.02, iron, segs=4)
t.ico(0.16, (0, 0, 1.42), coil, subdiv=2)
out.append(t.finish())

# ---------- the lighthouse (the core): striped tower, gallery, glowing lantern, red cap (~6.2 m) ----------
L = Builder("Core", [stone, white, red, dark, iron, crystal])
L.cyl(1.08, 0.2, (0, 0, 0.1), stone, segs=8)
L.cyl(0.98, 0.18, (0, 0, 0.29), stone, segs=8)
z0, z1, r0, r1 = 0.38, 4.25, 0.9, 0.6
bands = 6
for i in range(bands):
    za, zb = z0 + (z1 - z0) * i / bands, z0 + (z1 - z0) * (i + 1) / bands
    ra, rb = r0 + (r1 - r0) * i / bands, r0 + (r1 - r0) * (i + 1) / bands
    L.cone(ra, rb, zb - za, (0, 0, (za + zb) / 2), red if i % 2 else white, segs=12)
L.box((0.42, 0.1, 0.78), (0, -0.86, 0.78), dark)               # door
L.box((0.5, 0.06, 0.08), (0, -0.89, 1.2), iron)
for z, ang in ((2.0, 0.0), (3.1, math.pi)):                    # window slits
    rr = r0 + (r1 - r0) * (z - z0) / (z1 - z0)
    L.box((0.12, 0.08, 0.32), (rr * math.sin(ang) * 0.98, -rr * math.cos(ang) * 0.98, z), dark, rot=(0, 0, ang))
L.cyl(0.9, 0.1, (0, 0, 4.3), iron, segs=12)                    # gallery deck
posts = ring_points(0.86, 12, 4.5)
for p in posts:
    L.box((0.04, 0.04, 0.32), p, iron)
for i in range(12):
    a, bpt = posts[i], posts[(i + 1) % 12]
    L.beam((a[0], a[1], 4.64), (bpt[0], bpt[1], 4.64), 0.02, iron, segs=4)
L.cyl(0.5, 0.12, (0, 0, 4.41), white, segs=10)
L.cyl(0.44, 0.78, (0, 0, 4.86), crystal, segs=8)               # lantern (glows)
for p in ring_points(0.45, 8, 4.86, math.pi / 8):
    L.box((0.05, 0.05, 0.8), p, iron)
L.cone(0.6, 0.0, 0.6, (0, 0, 5.55), red, segs=10)              # cap
L.cyl(0.07, 0.12, (0, 0, 5.9), iron, segs=6)
L.beam((0, 0, 5.95), (0, 0, 6.25), 0.025, iron, segs=4)
out.append(L.finish())

# ---------- walls: stone (Lv2) and iron (Lv3); Lv1 stays the wooden palisade ----------
w = Builder("WallStone", [stone, rock, dark])
rows = 5
for r in range(rows):
    z = -1.5 + 0.06 + r * 0.54 + 0.27
    off = 0.0 if r % 2 == 0 else 0.36
    x = -1.5 - off
    i = 0
    while x < 1.5:
        x2 = min(1.5, x + 0.72)
        xa = max(-1.5, x)
        if x2 - xa > 0.05:
            w.box((x2 - xa - 0.04, 0.34 + 0.03 * ((r + i) % 3), 0.5), ((xa + x2) / 2, 0, z), rock if (r * 3 + i) % 4 == 0 else stone)
        x += 0.72
        i += 1
w.box((3.0, 0.38, 0.1), (0, 0, -1.45), dark)                   # footing
for x in (-1.12, 0.0, 1.12):                                    # merlons on top
    w.box((0.56, 0.4, 0.22), (x, 0, 1.39), stone)
out.append(w.finish())

w = Builder("WallMetal", [iron, metal, brass, steel])
for x in (-1.42, 1.42):
    w.box((0.16, 0.36, 3.0), (x, 0, 0), iron)                  # posts
for x in (-0.68, 0.68):
    w.box((1.32, 0.18, 2.5), (x, 0, -0.1), metal)              # plates
    for zz in (-1.25, 0.0, 1.05):
        for xx in (-0.52, 0.52):
            w.box((0.06, 0.24, 0.06), (x + xx, 0, zz), brass)  # rivets
w.box((3.0, 0.3, 0.14), (0, 0, 0.0), iron)                     # middle brace
w.box((3.0, 0.3, 0.14), (0, 0, 1.18), iron)
w.box((3.0, 0.3, 0.14), (0, 0, -1.43), iron)
for i in range(9):                                              # spikes on top
    x = -1.33 + i * 0.333
    w.cone(0.07, 0.0, 0.26, (x, 0, 1.37), steel, segs=4)
out.append(w.finish())

# ---------- enemy gear: a horned helmet (chargers) and the behemoth's spiked shell ----------
hm = Builder("Helmet", [helmet, iron, bone])
hm.cyl(0.36, 0.16, (0, 0, 0.08), helmet, segs=10)
hm.cone(0.33, 0.12, 0.22, (0, 0, 0.27), helmet, segs=10)
hm.cyl(0.38, 0.05, (0, 0, 0.03), iron, segs=10)
hm.box((0.06, 0.5, 0.3), (0, 0, 0.22), iron)                   # crest
for s in (-1, 1):
    hm.beam((s * 0.3, 0, 0.16), (s * 0.5, -0.05, 0.42), 0.07, bone, segs=6, r2=0.0)
out.append(hm.finish())

bs = Builder("BossShell", [rock, moss, stone, crystal])
bs.cyl(0.5, 0.3, (0, 0, 0.12), rock, segs=9)
bs.cone(0.5, 0.26, 0.3, (0, 0, 0.42), rock, segs=9)
bs.cyl(0.52, 0.08, (0, 0, 0.0), moss, segs=9)
for i, p in enumerate(ring_points(0.32, 6, 0.42, 0.3)):
    bs.beam(p, (p[0] * 1.35, p[1] * 1.35, 0.9 + 0.08 * (i % 2)), 0.09, stone, segs=4, r2=0.0)
bs.beam((0, 0, 0.55), (0, 0, 1.15), 0.12, stone, segs=4, r2=0.0)
for p in ring_points(0.36, 3, 0.3, 1.0):
    bs.ico(0.07, p, crystal)
out.append(bs.finish())

export(out)
