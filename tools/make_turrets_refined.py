# Refined turret heads, second pass (Owen: "把這些砲塔的設計都做的更精緻"). Same conventions as make_fortress_kit.py:
# origins at the turret pivot, sitting on z = 0, facing -Y, and the game scales the model to its head slot.
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_turrets_refined.py
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bkit import Builder, M, export, mat, tube, torus, fin

stone, rock, iron, metal, brass, dark, wood, rope, steel, copper, coil, crystal, black, gold, moss = (
    M(n) for n in ("Stone", "Rock", "Iron", "Metal", "Brass", "DarkWood", "Wood", "Rope", "Steel", "Copper", "Coil", "Crystal", "BombBlack", "Gold", "Moss"))
rubber = mat("Rubber", (0.06, 0.06, 0.07))
glass = mat("Glass", (0.25, 0.55, 0.7), (0.12, 0.35, 0.5))
ember = mat("Ember", (0.9, 0.35, 0.08), (0.8, 0.25, 0.04))
white = mat("White", (0.82, 0.79, 0.74), force=True)
out, TAU = [], 2 * math.pi


def ring(r, n, z, phase=0.0):
    return [(r * math.cos(phase + i * TAU / n), r * math.sin(phase + i * TAU / n), z) for i in range(n)]


def bolts(b, r, n, z, size=0.045, m=brass, phase=0.0):
    for p in ring(r, n, z, phase):
        b.box((size, size, size), p, m)


def rivet_line(b, a, c, n, m=brass, size=0.035):
    a, c = __import__("mathutils").Vector(a), __import__("mathutils").Vector(c)
    for i in range(n):
        t = (i + 0.5) / n
        b.box((size, size, size), tuple(a.lerp(c, t)), m)


def pipe(b, pts, r, m=copper, segs=8):
    tube(b, pts, r, m, segs=segs)


def vents(b, loc, count, m=iron, size=0.09):
    x, y, z = loc
    for i in range(count):
        b.box((size, 0.03, 0.035), (x, y, z - i * 0.07), m)


def plates(b, y, z, n, m=metal):
    for i in range(n):
        b.box((0.5 - i * 0.04, 0.06, 0.06), (0, y, z + i * 0.06), m)


# ---------------- gatling gun ----------------
g = Builder("GunHead", [iron, metal, brass, dark, steel, rubber])
g.box((0.54, 0.46, 0.12), (0, 0.06, 0.07), iron)                       # yoke
for x in (-0.25, 0.25):
    g.box((0.08, 0.38, 0.42), (x, 0.05, 0.3), iron)
    bolts(g, 0.055, 4, 0.36, 0.03) if False else None
g.box((0.4, 0.56, 0.32), (0, 0.06, 0.38), metal)                        # receiver
g.box((0.34, 0.18, 0.06), (0, 0.34, 0.54), dark)                        # feed cover
g.box((0.3, 0.12, 0.14), (0, 0.4, 0.42), dark)                          # grip block
for x in (-0.16, 0.16):
    g.box((0.05, 0.02, 0.16), (x, 0.462, 0.36), rubber)                  # back grips
for p in ring(0.09, 6, 0.0):
    g.beam((p[0], -0.18, 0.38 + p[1]), (p[0], -1.2, 0.38 + p[1]), 0.030, steel, segs=8)
    g.cyl(0.052, 0.05, (p[0], -1.2, 0.38 + p[1]), brass, segs=8, axis="Y")   # muzzle
g.beam((0, -0.16, 0.38), (0, -1.16, 0.38), 0.042, iron, segs=8)
for y in (-0.5, -0.78, -1.06):
    g.cyl(0.15, 0.06, (0, y, 0.38), brass, segs=12, axis="Y")            # barrel clamps
g.cyl(0.18, 0.22, (0.36, 0.06, 0.34), metal, segs=14, axis="X")          # ammo drum
g.cyl(0.19, 0.03, (0.47, 0.06, 0.34), brass, segs=14, axis="X")
g.cyl(0.1, 0.04, (0.24, 0.06, 0.34), dark, segs=12, axis="X")
for i in range(7):                                                       # belt links
    a = -0.2 + i * 0.07
    g.box((0.03, 0.05, 0.03), (-0.3, a, 0.53), brass, rot=(0, 0.5, 0))
g.box((0.52, 0.06, 0.3), (0, -0.22, 0.47), iron, rot=(-0.12, 0, 0))      # shield
bolts(g, 0.2, 6, 0.58, 0.035)
g.beam((0, -0.1, 0.58), (0, -0.48, 0.58), 0.022, dark, segs=6)           # sight rail
g.box((0.06, 0.08, 0.07), (0, -0.32, 0.62), brass)
pipe(g, [(0.36, 0.16, 0.34), (0.42, 0.3, 0.3), (0.3, 0.42, 0.28)], 0.018, rubber)  # cable
vents(g, (0.18, 0.28, 0.5), 3)
out.append(g.finish())

# ---------------- cannon ----------------
c = Builder("CannonHead", [iron, brass, dark, wood, black, steel, rope])
c.box((0.42, 0.92, 0.08), (0, 0.06, 0.07), dark)                        # carriage bed
for x in (-0.26, 0.26):
    c.box((0.09, 0.9, 0.3), (x, 0.05, 0.22), dark)                      # cheeks
    bolts(c, 0.055, 3, 0.3, 0.032)
c.box((0.3, 0.18, 0.2), (0, 0.42, 0.26), dark)                          # trail block
for x in (-0.36, 0.36):
    c.cyl(0.24, 0.075, (x, -0.16, 0.22), wood, segs=16, axis="X")       # wheels
    c.cyl(0.06, 0.1, (x, -0.16, 0.22), iron, segs=8, axis="X")
    for k in range(8):                                                  # spokes
        a = k * TAU / 8
        c.box((0.03, 0.2, 0.03), (x, -0.16 + 0.2 * math.cos(a), 0.22 + 0.2 * math.sin(a)), dark, rot=(a, 0, 0))
    c.cyl(0.18, 0.04, (x * 0.82, 0.42, 0.16), wood, segs=12, axis="X")
c.cone(0.23, 0.17, 1.3, (0, -0.42, 0.47), iron, segs=16, axis="-Y")     # barrel
c.cyl(0.24, 0.11, (0, -1.02, 0.47), iron, segs=16, axis="Y")            # muzzle swell
c.cyl(0.13, 0.02, (0, -1.075, 0.47), black, segs=12, axis="Y")
for y in (-0.12, -0.62, -0.9):
    c.cyl(0.26, 0.06, (0, y, 0.47), brass, segs=16, axis="Y")
c.ico(0.09, (0, 0.28, 0.47), iron)                                      # cascabel
c.tube = None
pipe(c, [(0, 0.02, 0.6), (0.02, 0.18, 0.66), (0.08, 0.2, 0.78)], 0.02, brass)  # lanyard
for x in (-0.1, 0.1):
    c.beam((x, 0.36, 0.3), (x, 0.58, 0.12), 0.035, wood, segs=6)        # trail legs
c.box((0.3, 0.05, 0.05), (0, -0.4, 0.66), dark)                         # rammer
c.cyl(0.05, 0.9, (0.16, -0.4, 0.66), wood, segs=8, axis="Y")
out.append(c.finish())

# ---------------- harpoon ballista ----------------
h = Builder("HarpoonHead", [dark, wood, steel, rope, iron, brass])
h.box((0.24, 1.6, 0.12), (0, -0.2, 0.28), dark)                        # stock
h.box((0.5, 0.24, 0.16), (0, -0.86, 0.3), iron)                        # bow housing
for s in (-1, 1):
    a, tip = (s * 0.16, -0.86, 0.34), (s * 0.86, -0.52, 0.3)
    h.beam(a, tip, 0.055, wood, segs=6, r2=0.03)
    h.box((0.08, 0.08, 0.08), tip, iron)
    h.cyl(0.06, 0.05, tip, brass, segs=10, axis="X")
    h.beam(tip, (s * 0.8, -0.82, 0.42), 0.012, rope, segs=4)
nock = (0, 0.12, 0.34)
for s in (-1, 1):
    h.beam((s * 0.86, -0.52, 0.3), nock, 0.012, rope, segs=4)
h.beam((0, 0.22, 0.4), (0, -1.18, 0.4), 0.032, steel, segs=8)          # bolt
h.beam((0, -1.18, 0.4), (0, -1.5, 0.4), 0.075, steel, segs=6, r2=0.0)
for s in (-1, 1):
    h.beam((0, -1.22, 0.4), (s * 0.13, -1.12, 0.4), 0.017, steel, segs=4)  # barbs
h.box((0.18, 0.14, 0.2), (0, 0.34, 0.3), iron)                          # winch housing
h.cyl(0.13, 0.5, (0, 0.52, 0.26), rope, segs=14, axis="X")              # reel
for x in (-0.25, 0.25):
    h.cyl(0.14, 0.03, (x, 0.52, 0.26), brass, segs=12, axis="X")
    h.beam((x, 0.52, 0.26), (x, 0.52, 0.5), 0.03, iron, segs=4)
h.cyl(0.04, 0.16, (0.3, 0.52, 0.34), iron, segs=8, axis="Y")
torque = torus(h, (0, 0.52, 0.26), (0, 1, 0), 0.16, 0.025, rope, segs=20, tsegs=5) if False else None
h.box((0.34, 0.08, 0.06), (0, -0.06, 0.62), brass)                      # rear sight
out.append(h.finish())

# ---------------- mortar ----------------
m = Builder("MortarHead", [iron, brass, black, dark, steel, copper])
m.cyl(0.5, 0.09, (0, 0, 0.045), iron, segs=16)
bolts(m, 0.43, 10, 0.1, 0.045)
m.cyl(0.4, 0.08, (0, 0, 0.15), iron, segs=16)
m.cyl(0.34, 0.05, (0, 0, 0.225), brass, segs=16)
for x in (-0.26, 0.26):
    m.box((0.09, 0.36, 0.38), (x, 0.02, 0.34), iron)
    m.cyl(0.1, 0.06, (x, 0.02, 0.34), brass, segs=10, axis="X")
m.beam((-0.32, 0.02, 0.42), (0.32, 0.02, 0.42), 0.065, steel, segs=10)  # trunnion
for s in (-1, 1):
    m.beam((s * 0.3, 0.02, 0.42), (s * 0.34, 0.24, 0.12), 0.04, iron, segs=6)
    m.beam((s * 0.3, 0.02, 0.42), (s * 0.3, -0.18, 0.42), 0.03, copper, segs=6)  # recoil cylinder
base, muzzle = (0, 0.24, 0.3), (0, -0.34, 1.06)
m.beam(base, muzzle, 0.235, iron, segs=16, r2=0.2)
for k in (0.25, 0.55, 0.82):
    p = (0, 0.24 - 0.58 * k, 0.3 + 0.76 * k)
    m.beam((p[0], p[1] - 0.02, p[2]), (p[0], p[1] + 0.02, p[2]), 0.245, brass, segs=16)
m.beam((0, -0.29, 1.0), (0, -0.38, 1.11), 0.25, brass, segs=16)         # muzzle ring
m.beam((0, -0.36, 1.09), (0, -0.39, 1.12), 0.17, black, segs=12)        # bore
m.box((0.2, 0.2, 0.12), (0, 0.28, 0.2), copper)                         # breech block
vent = None
for i in range(3):
    m.box((0.26, 0.03, 0.035), (0, 0.1 + i * 0.07, 0.34), dark)         # vent slats
out.append(m.finish())

# ---------------- tesla ----------------
t = Builder("TeslaHead", [iron, copper, coil, brass, dark, crystal])
t.cyl(0.34, 0.16, (0, 0, 0.08), iron, segs=12)
bolts(t, 0.29, 8, 0.13, 0.04)
t.cyl(0.28, 0.07, (0, 0, 0.19), copper, segs=12)
for i, z in enumerate((0.25, 0.33, 0.41)):
    t.cyl(0.25 - i * 0.025, 0.055, (0, 0, z), copper, segs=14)
t.cyl(0.1, 1.05, (0, 0, 0.9), copper, segs=10)
for z in (0.5, 0.64, 0.78, 0.92, 1.06, 1.18):
    torus(t, (0, 0, z), (0, 0, 1), 0.21, 0.022, coil, segs=18, tsegs=5)
for z in (0.56, 0.72, 0.88, 1.04):
    t.cyl(0.14, 0.035, (0, 0, z), brass, segs=12)
t.cyl(0.15, 0.05, (0, 0, 1.25), brass, segs=12)
for p in ring(0.15, 3, 1.25):
    t.beam(p, (p[0] * 2.2, p[1] * 2.2, 1.58), 0.022, iron, segs=5)
t.ico(0.17, (0, 0, 1.46), coil, subdiv=2)
t.ico(0.07, (0, 0, 1.46), crystal, subdiv=1)
for s in (-1, 1):
    pipe(t, [(s * 0.22, 0.12, 0.15), (s * 0.42, 0.22, 0.28), (s * 0.34, 0.3, 0.62), (0, 0.1, 1.1)], 0.016, dark)
out.append(t.finish())

# ---------------- flamethrower ----------------
f = Builder("FlameHead", [iron, brass, black, dark, steel, ember, copper, rubber])
f.box((0.5, 0.56, 0.12), (0, 0.06, 0.07), iron)
bolts(f, 0.2, 6, 0.14, 0.03)
for x in (-0.17, 0.17):
    f.cyl(0.14, 0.56, (x, 0.2, 0.36), ember, segs=14, axis="Y")          # fuel tanks
    f.cyl(0.145, 0.05, (x, 0.5, 0.36), brass, segs=14, axis="Y")
    f.cyl(0.145, 0.05, (x, -0.08, 0.36), brass, segs=14, axis="Y")
    for z in (0.28, 0.44):
        f.box((0.02, 0.6, 0.02), (x, 0.22, z), brass)                    # straps
f.box((0.2, 0.18, 0.2), (0, 0.46, 0.3), iron)                           # pump housing
f.cyl(0.05, 0.18, (0.2, 0.46, 0.36), brass, segs=8, axis="X")
f.beam((0, 0.0, 0.32), (0, -0.78, 0.36), 0.08, iron, segs=10)
f.beam((0, -0.78, 0.36), (0, -1.04, 0.38), 0.09, iron, segs=10, r2=0.15)
for y in (-0.74, -0.92):
    f.cyl(0.12, 0.05, (0, y, 0.37), brass, segs=12, axis="Y")
f.cyl(0.11, 0.02, (0, -1.05, 0.38), black, segs=10, axis="Y")
f.ico(0.055, (0, -0.98, 0.52), ember, subdiv=1)
f.beam((0, -0.98, 0.38), (0, -0.98, 0.48), 0.016, iron, segs=4)
pipe(f, [(-0.17, -0.08, 0.36), (-0.1, -0.28, 0.46), (0, -0.3, 0.5), (0.15, -0.2, 0.46)], 0.022, copper)
f.box((0.24, 0.22, 0.4), (0.28, 0.05, 0.36), dark)                      # heat shield
vents(f, (0.28, 0.18, 0.4), 4, brass)
out.append(f.finish())

# ---------------- sniper ----------------
sn = Builder("SniperHead", [iron, steel, dark, crystal, brass, rubber, black])
sn.box((0.38, 0.64, 0.2), (0, 0.16, 0.2), dark)                         # stock
sn.box((0.22, 0.54, 0.18), (0, -0.24, 0.32), iron)                      # receiver
sn.box((0.16, 0.2, 0.12), (0, 0.42, 0.28), rubber)                      # butt pad
sn.beam((0, -0.42, 0.34), (0, -1.95, 0.34), 0.05, steel, segs=12)       # barrel
for y in (-0.72, -1.1, -1.48):
    sn.cyl(0.065, 0.035, (0, y, 0.34), brass, segs=12, axis="Y")        # fluting rings
sn.cyl(0.075, 0.16, (0, -1.92, 0.34), iron, segs=12, axis="Y")          # muzzle brake
for k in range(3):
    sn.box((0.16, 0.04, 0.03), (0, -1.98, 0.28 + k * 0.05), black)
sn.beam((0, 0.0, 0.47), (0, -0.56, 0.47), 0.065, iron, segs=10)         # scope
sn.cyl(0.075, 0.03, (0, -0.58, 0.47), crystal, segs=12, axis="Y")
sn.cyl(0.075, 0.03, (0, 0.04, 0.47), brass, segs=12, axis="Y")
for y in (-0.44, 0.06):
    sn.beam((0, y, 0.4), (0, y, 0.46), 0.025, iron, segs=6)             # scope mounts
sn.box((0.14, 0.2, 0.14), (0, 0.02, 0.2), brass)                        # magazine
sn.box((0.03, 0.16, 0.03), (0.14, 0.02, 0.28), iron, rot=(0, 0.7, 0))   # bolt handle
sn.cyl(0.045, 0.03, (0.18, 0.02, 0.34), brass, segs=8)
for s in (-1, 1):
    sn.beam((0, -1.2, 0.3), (s * 0.3, -1.36, 0.0), 0.022, iron, segs=5) # bipod
    sn.box((0.05, 0.08, 0.03), (s * 0.3, -1.36, 0.02), rubber)
sn.box((0.08, 0.1, 0.16), (0, -0.06, 0.13), brass)                      # trigger guard
out.append(sn.finish())

# ---------------- bubble shield emitter ----------------
bubble = mat("BubbleGlow", (0.6, 0.85, 1.0), (0.45, 0.8, 1.2))
bb = Builder("BubbleHead", [iron, brass, bubble, copper, dark, glass])
bb.cyl(0.36, 0.12, (0, 0, 0.06), iron, segs=14)
bolts(bb, 0.3, 8, 0.13, 0.035)
bb.cyl(0.3, 0.08, (0, 0, 0.17), brass, segs=14)
for s in (-1, 1):
    bb.beam((s * 0.2, 0, 0.2), (s * 0.2, 0, 0.42), 0.035, brass, segs=6)
    bb.beam((s * 0.2, 0, 0.42), (0, 0, 0.62), 0.025, copper, segs=6)
bb.cone(0.12, 0.42, 0.28, (0, 0, 0.36), brass, segs=16)                 # dish
bb.cyl(0.44, 0.03, (0, 0, 0.52), bubble, segs=20)
bb.beam((0, 0, 0.5), (0, 0, 0.86), 0.045, copper, segs=8)
bb.ico(0.22, (0, 0, 0.9), glass, subdiv=2)                              # emitter orb
bb.ico(0.13, (0, 0, 0.9), bubble, subdiv=2)
for z in (0.62, 1.02):
    torus(bb, (0, 0, z), (0, 0, 1), 0.3, 0.025, bubble, segs=20, tsegs=5)
for k in range(4):
    p = ring(0.26, 4, 0.22)[k]
    bb.beam(p, (p[0] * 1.3, p[1] * 1.3, 0.5), 0.02, dark, segs=5)        # supports
pipe(bb, [(0.3, 0.12, 0.18), (0.4, 0.2, 0.3), (0.28, 0.26, 0.46)], 0.015, dark)
out.append(bb.finish())

# ---------------- repair mender ----------------
md = Builder("MenderHead", [iron, brass, coil, steel, crystal, copper, dark])
md.cyl(0.36, 0.12, (0, 0, 0.06), iron, segs=14)
bolts(md, 0.3, 8, 0.12, 0.035)
md.cyl(0.28, 0.7, (0, 0, 0.48), iron, segs=14)                          # core column
for z in (0.26, 0.42, 0.58, 0.74):
    md.cyl(0.3, 0.045, (0, 0, z), brass, segs=16)
    torus(md, (0, 0, z), (0, 0, 1), 0.31, 0.018, coil, segs=18, tsegs=4)
md.cyl(0.2, 0.06, (0, 0, 0.86), brass, segs=12)
md.ico(0.14, (0, 0, 0.94), crystal, subdiv=2)
for s in (-1, 1):
    md.beam((s * 0.28, 0, 0.4), (s * 0.52, -0.12, 0.62), 0.045, steel, segs=6)  # arms
    md.box((0.14, 0.12, 0.08), (s * 0.55, -0.12, 0.64), iron)
    md.cyl(0.035, 0.18, (s * 0.6, -0.18, 0.58), brass, segs=8, axis="X")
    md.box((0.03, 0.16, 0.16), (s * 0.66, -0.2, 0.52), steel)           # spanner
md.box((0.22, 0.2, 0.2), (0, 0.24, 0.36), dark)                         # toolbox
for i in range(3):
    md.box((0.18, 0.03, 0.035), (0, 0.25, 0.3 + i * 0.05), copper)
pipe(md, [(0.18, 0.2, 0.2), (0.32, 0.26, 0.3), (0.24, 0.22, 0.5)], 0.015, dark)
out.append(md.finish())

export(out)
