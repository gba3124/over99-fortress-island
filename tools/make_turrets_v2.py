# Turret heads, from the silhouette up (Owen: "一次重做 9 顆"). A coherent chassis, a real weapon, and only then details;
# all heads keep the game's convention: pivot at the origin, sit on z = 0, point toward -Y, one mesh + materials.
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_turrets_v2.py
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bkit import Builder, M, export, mat, tube, torus

stone, iron, metal, brass, dark, wood, rope, steel, copper, coil, crystal, black, gold = (
    M(n) for n in ("Stone", "Iron", "Metal", "Brass", "DarkWood", "Wood", "Rope", "Steel", "Copper", "Coil", "Crystal", "BombBlack", "Gold"))
red = mat("TurretRed", (0.62, 0.22, 0.16), (0.3, 0.04, 0.02))
glass = mat("TurretGlass", (0.28, 0.58, 0.72), (0.15, 0.4, 0.55))
ember = mat("TurretEmber", (0.95, 0.45, 0.08), (0.9, 0.35, 0.03))
out, TAU = [], 2 * math.pi


def ring(r, n, z, phase=0.0):
    return [(r * math.cos(phase + i * TAU / n), r * math.sin(phase + i * TAU / n), z) for i in range(n)]


def bolts(b, r, n, z, size=0.045, m=brass, phase=0.0):
    for p in ring(r, n, z, phase):
        b.box((size, size, size), p, m)


def spokes(b, x, y, z, radius, n=8, m=dark):
    for k in range(n):
        a = k * TAU / n
        b.box((0.035, radius * 1.9, 0.035), (x, y + radius * math.cos(a), z + radius * math.sin(a)), m, rot=(a, 0, 0))


# =====================================================================================
# GUN — a rotating six-barrel receiver with a side ammo drum, feed chute and shield
# =====================================================================================
g = Builder("GunHead", [iron, metal, brass, dark, steel, red, black, rubber := M("Rubber")])
g.box((0.54, 0.42, 0.12), (0, 0.04, 0.08), iron)                         # turret yoke
for x in (-0.25, 0.25):
    g.box((0.07, 0.34, 0.34), (x, 0.02, 0.27), iron)
    g.cyl(0.055, 0.05, (x, 0.02, 0.27), brass, segs=22, axis="X")
g.cyl(0.27, 0.52, (0, 0.03, 0.4), metal, segs=20, axis="Y")               # main receiver drum
g.cyl(0.285, 0.05, (0, -0.23, 0.4), iron, segs=22, axis="Y")              # front bearing
g.cyl(0.285, 0.05, (0, 0.27, 0.4), iron, segs=22, axis="Y")               # rear plate
for x in (-0.26, 0.26):
    g.box((0.05, 0.3, 0.22), (x, 0.07, 0.47), red, rot=(0, 0, 0))         # side armour
    bolts(g, 0.04, 3, 0.36, 0.025) if False else None
for p in ring(0.095, 6, 0.0):                                             # six barrels
    g.beam((p[0], -0.26, 0.4 + p[1]), (p[0], -1.18, 0.4 + p[1]), 0.029, steel, segs=22)
    g.cyl(0.045, 0.05, (p[0], -1.19, 0.4 + p[1]), brass, segs=22, axis="Y")
g.beam((0, -0.24, 0.4), (0, -1.1, 0.4), 0.045, iron, segs=8)             # spindle
for y in (-0.48, -0.78, -1.05):
    g.cyl(0.155, 0.055, (0, y, 0.4), brass, segs=22, axis="Y")            # barrel clamps
g.cyl(0.1, 0.04, (0, -1.2, 0.4), black, segs=22, axis="Y")                # muzzle bore
g.cyl(0.19, 0.26, (0.39, 0.08, 0.38), metal, segs=22, axis="X")           # ammo drum
g.cyl(0.2, 0.035, (0.53, 0.08, 0.38), brass, segs=22, axis="X")
for i in range(9):                                                        # ammo belt links into the feed
    a = -0.16 + i * 0.045
    g.box((0.035, 0.05, 0.03), (-0.3, a, 0.53), brass, rot=(0, 0.6, 0))
g.box((0.1, 0.22, 0.16), (-0.3, 0.18, 0.5), dark)                        # feed housing
g.box((0.16, 0.12, 0.06), (0, 0.34, 0.66), dark)                          # rear sight base
g.beam((0, -0.05, 0.68), (0, -0.55, 0.68), 0.022, dark, segs=6)           # sight rail
g.box((0.06, 0.09, 0.08), (0, -0.36, 0.72), brass)
g.box((0.06, 0.2, 0.12), (0.3, 0.38, 0.52), dark)                         # charging handle
g.beam((0.3, 0.3, 0.52), (0.4, 0.5, 0.52), 0.025, steel, segs=6)
g.cyl(0.03, 0.18, (0.48, 0.44, 0.4), brass, segs=8, axis="Y")             # cable spool
tube(g, [(0.4, 0.22, 0.36), (0.48, 0.14, 0.3), (0.33, 0.12, 0.26), (-0.2, 0.2, 0.26)], 0.018, rubber)
g.box((0.52, 0.08, 0.3), (0, -0.2, 0.55), iron)                           # gun shield
for p in ring(0.2, 4, 0.68): g.box((0.04, 0.04, 0.05), p, brass)
out.append(g.finish())

# =====================================================================================
# CANNON — a carriage frame, an elevation gear and one heavy reinforced barrel
# =====================================================================================
c = Builder("CannonHead", [iron, brass, dark, wood, black, steel, copper, red])
c.box((0.44, 0.9, 0.08), (0, 0.06, 0.06), dark)                           # carriage bed
for s in (-1, 1):
    c.box((0.09, 0.84, 0.28), (s * 0.27, 0.04, 0.22), dark)               # cheeks
    c.box((0.035, 0.7, 0.06), (s * 0.315, 0.04, 0.3), brass)              # iron straps
    bolts(c, 0.04, 4, 0.26, 0.03) if False else None
    c.cyl(0.24, 0.08, (s * 0.37, -0.15, 0.22), wood, segs=22, axis="X")   # wheels
    c.cyl(0.065, 0.1, (s * 0.37, -0.15, 0.22), iron, segs=22, axis="X")
    spokes(c, s * 0.37, -0.15, 0.22, 0.19, 8, dark)
    c.cyl(0.19, 0.035, (s * 0.305, 0.4, 0.17), wood, segs=24, axis="X")
c.box((0.3, 0.18, 0.18), (0, 0.4, 0.26), dark)                            # trail block
c.beam((0, 0.4, 0.26), (0, 0.72, 0.1), 0.045, wood, segs=6)               # trail
c.cyl(0.17, 0.12, (0, 0.02, 0.29), iron, segs=24, axis="X")               # trunnion
c.beam((-0.22, 0.02, 0.3), (0.22, 0.02, 0.3), 0.05, steel, segs=8)
c.beam((0, 0.24, 0.3), (0, -0.32, 0.5), 0.24, iron, segs=24, r2=0.185)    # breech to muzzle
c.beam((0, -0.32, 0.5), (0, -1.12, 0.5), 0.185, iron, segs=24, r2=0.145)  # chase
c.cyl(0.2, 0.12, (0, -1.06, 0.5), iron, segs=24, axis="Y")                # muzzle swell
c.cyl(0.11, 0.025, (0, -1.13, 0.5), black, segs=20, axis="Y")
for y in (-0.16, -0.6, -0.9):
    c.cyl(0.205, 0.05, (0, y, 0.5), brass, segs=24, axis="Y")             # bands
c.box((0.28, 0.2, 0.16), (0, 0.3, 0.52), copper)                          # breech block
c.ico(0.105, (0, 0.46, 0.5), iron)                                         # cascabel
c.beam((0.19, 0.18, 0.34), (0.19, 0.18, 0.52), 0.035, brass, segs=6)      # elevation screw
c.cyl(0.09, 0.04, (0.19, 0.18, 0.54), steel, segs=24)                     # elevation wheel
for k in range(6): c.box((0.02, 0.02, 0.12), (0.19 + 0.07 * math.cos(k * TAU / 6), 0.18, 0.54 + 0.07 * math.sin(k * TAU / 6)), brass)
c.box((0.3, 0.06, 0.06), (0, 0.02, 0.64), steel)                          # sight bar
for s in (-1, 1): c.box((0.05, 0.05, 0.16), (s * 0.1, 0.02, 0.7), brass)
c.beam((0.06, 0.0, 0.62), (0.06, 0.3, 0.6), 0.02, copper, segs=4)         # lanyard
out.append(c.finish())

# =====================================================================================
# HARPOON — a ballista rail: bow arms, taut string, bolt and winch
# =====================================================================================
h = Builder("HarpoonHead", [dark, wood, steel, rope, iron, brass])
h.box((0.26, 1.45, 0.12), (0, -0.28, 0.26), dark)                        # stock rail
h.box((0.4, 0.3, 0.18), (0, 0.42, 0.28), wood)                           # rear stock
h.box((0.46, 0.22, 0.2), (0, -0.74, 0.3), iron)                          # bow housing
for s in (-1, 1):
    a, mid, tip = (s * 0.18, -0.78, 0.32), (s * 0.6, -0.7, 0.3), (s * 0.84, -0.5, 0.26)
    tube(h, [a, mid, tip], 0.055, wood, segs=8)
    h.box((0.08, 0.08, 0.08), tip, iron)
    h.cyl(0.045, 0.05, tip, brass, segs=22, axis="X")
    tube(h, [tip, (s * 0.8, -0.72, 0.36)], 0.012, rope, segs=4, closed=False)
nock = (0, 0.16, 0.34)
for s in (-1, 1): tube(h, [(s * 0.84, -0.5, 0.26), nock], 0.012, rope, segs=4)
h.beam((0, 0.24, 0.4), (0, -1.12, 0.4), 0.032, steel, segs=8)             # bolt shaft
h.beam((0, -1.12, 0.4), (0, -1.5, 0.4), 0.08, steel, segs=6, r2=0.0)      # head
for s in (-1, 1): h.beam((0, -1.16, 0.4), (s * 0.14, -1.06, 0.4), 0.017, steel, segs=4)
h.box((0.2, 0.16, 0.22), (0, 0.3, 0.3), iron)                             # winch housing
h.cyl(0.13, 0.52, (0, 0.52, 0.26), rope, segs=22, axis="X")               # reel
for s in (-1, 1):
    h.cyl(0.145, 0.035, (s * 0.27, 0.52, 0.26), brass, segs=20, axis="X")
    h.beam((s * 0.27, 0.52, 0.26), (s * 0.27, 0.52, 0.5), 0.03, iron, segs=4)
h.cyl(0.04, 0.18, (0.31, 0.52, 0.34), iron, segs=8, axis="Y")             # crank
h.box((0.26, 0.06, 0.06), (0, -0.08, 0.6), brass)                          # rear sight
h.box((0.06, 0.06, 0.1), (0, -0.1, 0.64), iron)
h.box((0.14, 0.14, 0.16), (0, -0.45, 0.36), brass)                         # bolt guide
out.append(h.finish())

# =====================================================================================
# MORTAR — a heavy A-frame, a short wide tube, recoil cylinders and shells
# =====================================================================================
m = Builder("MortarHead", [iron, brass, black, dark, steel, copper, red])
m.cyl(0.5, 0.1, (0, 0, 0.05), iron, segs=24)                              # base plate
m.cyl(0.4, 0.08, (0, 0, 0.15), iron, segs=24)
bolts(m, 0.44, 12, 0.11, 0.045)
m.cyl(0.3, 0.06, (0, 0, 0.22), brass, segs=24)
for s in (-1, 1):
    m.box((0.09, 0.4, 0.34), (s * 0.25, 0.02, 0.34), iron)                # cradle cheeks
    m.box((0.04, 0.3, 0.05), (s * 0.3, 0.02, 0.44), brass)
    m.beam((s * 0.3, 0.02, 0.44), (s * 0.36, 0.3, 0.12), 0.045, steel, segs=6)  # outrigger
    m.cyl(0.05, 0.4, (s * 0.16, -0.08, 0.4), copper, segs=8, axis="Y")   # recoil cylinder
    m.cyl(0.06, 0.05, (s * 0.16, -0.28, 0.4), brass, segs=8, axis="Y")
m.cyl(0.1, 0.46, (0, 0.02, 0.34), steel, segs=24, axis="X")              # trunnion
base, muzzle = (0, 0.22, 0.28), (0, -0.34, 1.02)
m.beam(base, muzzle, 0.23, iron, segs=24, r2=0.19)                        # tube
for k in (0.22, 0.5, 0.78):
    p = (0, 0.22 - 0.56 * k, 0.28 + 0.74 * k)
    m.beam((p[0], p[1] - 0.025, p[2]), (p[0], p[1] + 0.025, p[2]), 0.245, brass, segs=24)
m.beam((0, -0.3, 0.96), (0, -0.38, 1.06), 0.25, brass, segs=24)           # muzzle ring
m.beam((0, -0.36, 1.03), (0, -0.4, 1.08), 0.17, black, segs=20)           # bore
m.box((0.2, 0.2, 0.12), (0, 0.26, 0.2), copper)                           # breech block
m.box((0.3, 0.12, 0.05), (0, 0.1, 0.5), steel)                            # sight rail
m.box((0.08, 0.08, 0.1), (0, 0.1, 0.58), brass)
for i in range(3):                                                        # ready shells on the base
    x = -0.28 + i * 0.28
    m.cyl(0.065, 0.2, (x, 0.3, 0.14), red, segs=22, axis="Z")
    m.cone(0.065, 0.0, 0.08, (x, 0.3, 0.28), copper, segs=22)
out.append(m.finish())

# =====================================================================================
# TESLA — ceramic insulator base, copper column, coil rings, charged crown
# =====================================================================================
t = Builder("TeslaHead", [iron, copper, coil, brass, dark, crystal, glass])
t.cyl(0.36, 0.14, (0, 0, 0.07), iron, segs=22)
bolts(t, 0.3, 10, 0.12, 0.04)
for i, z in enumerate((0.18, 0.24, 0.3)):
    t.cyl(0.3 - i * 0.02, 0.05, (0, 0, z), dark, segs=22)                 # ceramic insulators
t.cyl(0.22, 0.08, (0, 0, 0.36), copper, segs=22)
t.cyl(0.11, 1.0, (0, 0, 0.9), copper, segs=24)                            # column
for z in (0.46, 0.58, 0.7, 0.82, 0.94, 1.06):
    torus(t, (0, 0, z), (0, 0, 1), 0.2, 0.022, coil, segs=20, tsegs=5)
for z in (0.52, 0.76, 1.0):
    t.cyl(0.145, 0.035, (0, 0, z), brass, segs=20)
t.cyl(0.16, 0.06, (0, 0, 1.2), brass, segs=20)                            # crown base
for k in range(4):
    a = k * TAU / 4
    t.beam((0.1 * math.cos(a), 0.1 * math.sin(a), 1.22), (0.22 * math.cos(a), 0.22 * math.sin(a), 1.5), 0.018, iron, segs=5)
t.ico(0.16, (0, 0, 1.4), coil, subdiv=2)
t.ico(0.07, (0, 0.14, 1.4), crystal, subdiv=1)
for s in (-1, 1):
    tube(t, [(s * 0.22, 0.14, 0.2), (s * 0.4, 0.24, 0.34), (s * 0.3, 0.3, 0.7), (s * 0.08, 0.15, 1.1)], 0.016, dark)
out.append(t.finish())

# =====================================================================================
# FLAME — twin domed fuel tanks, pump, copper hose and a long nozzle
# =====================================================================================
f = Builder("FlameHead", [iron, brass, black, dark, steel, ember, copper, rubber, red])
f.box((0.5, 0.5, 0.12), (0, 0.05, 0.07), iron)
bolts(f, 0.2, 6, 0.13, 0.03)
for x in (-0.18, 0.18):
    f.cyl(0.13, 0.44, (x, 0.18, 0.34), red, segs=22, axis="Y")            # tanks
    f.ico(0.13, (x, -0.04, 0.34), red, subdiv=2)
    f.ico(0.13, (x, 0.4, 0.34), red, subdiv=2)
    for y in (-0.06, 0.3):                                                # straps
        torus(f, (x, y, 0.34), (0, 1, 0), 0.135, 0.014, brass, segs=20, tsegs=4)
    f.cyl(0.045, 0.08, (x, 0.5, 0.34), brass, segs=8, axis="Y")           # valves
    f.cyl(0.065, 0.04, (x, 0.55, 0.34), steel, segs=8, axis="Y")
f.box((0.24, 0.18, 0.2), (0, 0.46, 0.28), iron)                            # pump
f.cyl(0.05, 0.18, (0.2, 0.46, 0.34), brass, segs=8, axis="X")
tube(f, [(-0.18, 0.5, 0.34), (-0.12, 0.5, 0.5), (0, 0.4, 0.52), (0.16, 0.48, 0.42)], 0.022, rubber)
tube(f, [(0.18, -0.04, 0.34), (0.3, -0.2, 0.36), (0.24, -0.45, 0.34), (0, -0.5, 0.34)], 0.024, copper)
f.beam((0, -0.28, 0.34), (0, -0.86, 0.38), 0.075, iron, segs=22)           # barrel
f.beam((0, -0.86, 0.38), (0, -1.08, 0.4), 0.08, iron, segs=22, r2=0.14)    # nozzle flare
for y in (-0.8, -0.98): f.cyl(0.1, 0.045, (0, y, 0.39), brass, segs=24, axis="Y")
f.cyl(0.1, 0.02, (0, -1.09, 0.4), black, segs=22, axis="Y")
f.ico(0.05, (0, -1.02, 0.52), ember, subdiv=1)                             # pilot light
f.beam((0, -1.02, 0.4), (0, -1.02, 0.5), 0.014, iron, segs=4)
f.box((0.22, 0.18, 0.3), (0.3, 0.24, 0.3), dark)                           # heat shield
for i in range(3): f.box((0.2, 0.02, 0.03), (0.3, 0.2, 0.24 + i * 0.06), brass)
out.append(f.finish())

# =====================================================================================
# SNIPER — a long precision rifle: chassis, fluted barrel, big scope, bipod
# =====================================================================================
s = Builder("SniperHead", [iron, steel, dark, crystal, brass, rubber, black, glass])
s.box((0.34, 0.52, 0.16), (0, 0.24, 0.18), dark)                           # stock
s.box((0.14, 0.18, 0.12), (0, 0.5, 0.16), rubber)                          # butt pad
s.box((0.22, 0.5, 0.18), (0, -0.18, 0.3), iron)                            # receiver
s.box((0.18, 0.2, 0.16), (0, 0.02, 0.3), dark)                             # magazine
s.beam((0, -0.4, 0.32), (0, -1.92, 0.32), 0.05, steel, segs=20)            # barrel
for y in (-0.7, -1.05, -1.4): s.cyl(0.062, 0.03, (0, y, 0.32), brass, segs=20, axis="Y")
s.cyl(0.07, 0.16, (0, -1.92, 0.32), iron, segs=20, axis="Y")               # muzzle brake
for i in range(3): s.box((0.14, 0.04, 0.03), (0, -1.98, 0.25 + i * 0.06), black)
s.beam((0, -0.02, 0.45), (0, -0.6, 0.45), 0.065, iron, segs=24)            # scope
s.cyl(0.07, 0.02, (0, -0.62, 0.45), glass, segs=20, axis="Y")
s.cyl(0.075, 0.03, (0, 0.02, 0.45), brass, segs=20, axis="Y")
for y in (-0.45, 0.04): s.beam((0, y, 0.37), (0, y, 0.44), 0.024, iron, segs=6)
s.box((0.03, 0.16, 0.03), (0.13, 0.0, 0.3), steel, rot=(0, 0.7, 0))        # bolt
s.cyl(0.04, 0.03, (0.17, 0.0, 0.34), brass, segs=8)
for side in (-1, 1):
    s.beam((0, -1.0, 0.28), (side * 0.26, -1.22, 0.0), 0.021, iron, segs=5)  # bipod
    s.box((0.05, 0.08, 0.03), (side * 0.26, -1.22, 0.01), rubber)
s.box((0.12, 0.18, 0.04), (0, 0.06, 0.38), brass)                          # rail
out.append(s.finish())

# =====================================================================================
# BUBBLE — a brass emitter dish with a glass orb, rings and four arms
# =====================================================================================
bubble = M("BubbleGlow")
bb = Builder("BubbleHead", [iron, brass, bubble, copper, dark, glass, crystal])
bb.cyl(0.38, 0.12, (0, 0, 0.06), iron, segs=22)
bolts(bb, 0.31, 10, 0.13, 0.04)
bb.cyl(0.3, 0.08, (0, 0, 0.18), brass, segs=22)
for k in range(4):
    a = k * TAU / 4
    bb.beam((0.2 * math.cos(a), 0.2 * math.sin(a), 0.22), (0.16 * math.cos(a), 0.16 * math.sin(a), 0.5), 0.035, brass, segs=6)
bb.cone(0.5, 0.13, 0.24, (0, 0, 0.42), brass, segs=20)                     # dish
bb.cyl(0.5, 0.025, (0, 0, 0.55), bubble, segs=22)
bb.beam((0, 0, 0.5), (0, 0, 0.84), 0.045, copper, segs=8)
bb.ico(0.2, (0, 0, 0.92), glass, subdiv=2)                                 # emitter orb
bb.ico(0.1, (0, 0, 0.92), bubble, subdiv=2)
for z in (0.64, 1.0): torus(bb, (0, 0, z), (0, 0, 1), 0.28, 0.02, bubble, segs=22, tsegs=5)
for k in range(4):
    a = k * TAU / 4
    bb.beam((0.27 * math.cos(a), 0.27 * math.sin(a), 0.24), (0.34 * math.cos(a), 0.34 * math.sin(a), 0.48), 0.02, dark, segs=5)
bb.box((0.16, 0.03, 0.1), (0, -0.3, 0.38), crystal)                        # focus crystal
out.append(bb.finish())

# =====================================================================================
# MENDER — a repair core with banded column, crystal and two working arms
# =====================================================================================
md = Builder("MenderHead", [iron, brass, coil, steel, crystal, copper, dark, glass])
md.cyl(0.38, 0.12, (0, 0, 0.06), iron, segs=22)
bolts(md, 0.31, 10, 0.13, 0.04)
md.cyl(0.29, 0.68, (0, 0, 0.44), iron, segs=22)                            # core column
for z in (0.22, 0.38, 0.54, 0.7):
    md.cyl(0.31, 0.045, (0, 0, z), brass, segs=24)
    torus(md, (0, 0, z), (0, 0, 1), 0.32, 0.017, coil, segs=20, tsegs=4)
md.cyl(0.2, 0.06, (0, 0, 0.84), brass, segs=20)
md.ico(0.13, (0, 0, 0.92), crystal, subdiv=2)
md.ico(0.06, (0, 0, 0.92), glass, subdiv=1)
for side in (-1, 1):
    md.beam((side * 0.28, 0, 0.38), (side * 0.52, -0.1, 0.6), 0.05, steel, segs=8)   # arm
    md.cyl(0.06, 0.1, (side * 0.52, -0.1, 0.6), brass, segs=22, axis="X")
    md.beam((side * 0.52, -0.1, 0.6), (side * 0.6, -0.3, 0.45), 0.03, steel, segs=6) # forearm
    md.box((0.05, 0.16, 0.14), (side * 0.6, -0.36, 0.4), steel)                       # tool head
    md.box((0.03, 0.06, 0.12), (side * 0.6, -0.45, 0.4), brass)
md.box((0.24, 0.2, 0.18), (0, 0.26, 0.3), dark)                            # toolbox
for i in range(3): md.box((0.18, 0.02, 0.03), (0, 0.26, 0.24 + i * 0.05), copper)
tube(md, [(0.18, 0.2, 0.2), (0.3, 0.25, 0.3), (0.22, 0.22, 0.48)], 0.015, dark)
md.box((0.14, 0.05, 0.05), (0, -0.3, 0.6), brass)                           # light bar
md.cyl(0.04, 0.03, (0, -0.32, 0.6), glass, segs=22, axis="Y")
out.append(md.finish())

export(out)
