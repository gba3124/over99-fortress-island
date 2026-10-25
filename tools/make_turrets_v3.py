# Turret heads, art pass 3: thick, readable cartoon shapes (Owen chose the chunky Clash-Royale-like direction).
# Conventions unchanged: pivot at origin, sits on z = 0, faces -Y. Big forms first; trim and glow last; no thin loose wires.
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_turrets_v3.py
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bkit import Builder, M, export, mat, torus, tube

dark = mat("V3Dark", (0.045, 0.055, 0.065))
iron = mat("V3Iron", (0.12, 0.14, 0.17))
steel = mat("V3Steel", (0.52, 0.57, 0.62))
brass = mat("V3Brass", (0.80, 0.55, 0.17), (0.22, 0.10, 0.02))
rubber = mat("V3Rubber", (0.025, 0.025, 0.03))
glass = mat("V3Glass", (0.30, 0.62, 0.72), (0.15, 0.45, 0.6))
# saturated accents, one per turret family
teal = mat("V3Teal", (0.06, 0.28, 0.32), (0.02, 0.10, 0.12))
red = mat("V3Red", (0.56, 0.12, 0.07), (0.18, 0.02, 0.01))
blue = mat("V3Blue", (0.10, 0.23, 0.42), (0.03, 0.08, 0.18))
violet = mat("V3Violet", (0.27, 0.16, 0.38), (0.08, 0.04, 0.12))
copper = mat("V3Copper", (0.64, 0.30, 0.12), (0.2, 0.06, 0.02))
orange = mat("V3Orange", (0.66, 0.23, 0.06), (0.2, 0.05, 0.01))
green = mat("V3Green", (0.12, 0.28, 0.17), (0.02, 0.1, 0.05))
cyan = mat("V3Cyan", (0.10, 0.36, 0.43), (0.05, 0.3, 0.35))
mint = mat("V3Mint", (0.14, 0.32, 0.21), (0.05, 0.2, 0.1))
amber = mat("V3Amber", (1.0, 0.62, 0.16), (0.85, 0.4, 0.03))
ice = mat("V3Ice", (0.55, 0.9, 1.0), (0.2, 0.7, 0.95))
out, TAU = [], 2 * math.pi


def ring(r, n, z, phase=0.0):
    return [(r * math.cos(phase + i * TAU / n), r * math.sin(phase + i * TAU / n), z) for i in range(n)]


def studs(b, r, n, z, size=0.06, m=brass):
    for p in ring(r, n, z): b.box((size, size, size), p, m)


def wheels(b, x, y, z, r, m, hub=brass):
    b.cyl(r, 0.12, (x, y, z), m, segs=28, axis="X")
    b.cyl(r * 0.35, 0.14, (x, y, z), hub, segs=14, axis="X")
    for k in range(6):
        a = k * TAU / 6
        b.box((0.06, r * 1.5, 0.06), (x, y + r * 0.7 * math.cos(a), z + r * 0.7 * math.sin(a)), dark, rot=(a, 0, 0))


# ---------------- 1. GUN: big teal receiver, one fat barrel shroud with six bores ----------------
g = Builder("GunHead", [iron, dark, steel, brass, teal, rubber, amber])
g.box((0.72, 0.5, 0.16), (0, 0.08, 0.1), iron)                              # base yoke
for s in (-1, 1):
    g.box((0.1, 0.38, 0.3), (s * 0.34, 0.08, 0.3), teal)                    # side cheeks
    g.box((0.12, 0.06, 0.32), (s * 0.4, 0.08, 0.3), brass)
g.cyl(0.34, 0.6, (0, 0.02, 0.46), teal, segs=16, axis="Y")                  # big drum receiver
for y in (-0.24, 0.26): g.cyl(0.36, 0.06, (0, y, 0.46), brass, segs=18, axis="Y")
g.box((0.44, 0.4, 0.08), (0, 0.02, 0.78), dark)                             # top armour
g.box((0.3, 0.22, 0.06), (0, 0.02, 0.84), brass)
g.cyl(0.2, 0.4, (0, -0.46, 0.46), steel, segs=18, axis="Y")                 # fat shroud
g.cyl(0.22, 0.07, (0, -0.68, 0.46), brass, segs=20, axis="Y")
for i in range(6):
    a = i * TAU / 6; x = 0.105 * math.cos(a); z = 0.46 + 0.105 * math.sin(a)
    g.beam((x, -0.5, z), (x, -1.18, z), 0.03, steel, segs=10)
    g.cyl(0.04, 0.04, (x, -1.2, z), brass, segs=10, axis="Y")
g.cyl(0.09, 0.06, (0, -1.2, 0.46), dark, segs=12, axis="Y")
g.box((0.2, 0.38, 0.28), (0.44, 0.06, 0.46), dark)                          # side ammo box
g.box((0.21, 0.05, 0.29), (0.55, 0.06, 0.46), brass)
tube(g, [(0.34, 0.06, 0.56), (0.2, 0.08, 0.66), (0.02, 0.1, 0.66)], 0.055, brass, segs=10)
g.box((0.12, 0.16, 0.1), (0, 0.02, 0.86), dark)                             # sight
g.box((0.06, 0.08, 0.08), (0, -0.06, 0.9), amber)
for s in (-1, 1): g.box((0.06, 0.14, 0.18), (s * 0.24, 0.4, 0.56), rubber, rot=(0, 0, s * 0.12))
g.cyl(0.22, 0.18, (0, 0.34, 0.46), teal, segs=14, axis="Y")                 # rear counterweight
g.cyl(0.23, 0.05, (0, 0.44, 0.46), brass, segs=14, axis="Y")
out.append(g.finish())

# ---------------- 2. CANNON: red carriage, two big wheels, one chunky barrel ----------------
c = Builder("CannonHead", [iron, dark, steel, brass, red, rubber, dark])
c.box((0.5, 1.0, 0.14), (0, 0.1, 0.14), dark)                               # bed
for s in (-1, 1):
    c.box((0.12, 0.9, 0.3), (s * 0.3, 0.06, 0.32), red)                     # cheeks
    c.box((0.13, 0.05, 0.32), (s * 0.38, 0.06, 0.32), brass)
    wheels(c, s * 0.42, -0.2, 0.28, 0.27, red)
c.box((0.34, 0.2, 0.2), (0, 0.48, 0.34), dark)                              # trail
c.beam((0, 0.48, 0.32), (0, 0.82, 0.12), 0.07, dark, segs=8)
c.cyl(0.2, 0.2, (0, 0.06, 0.4), steel, segs=16, axis="X")                   # trunnion
c.beam((0, 0.3, 0.42), (0, -0.36, 0.56), 0.27, steel, segs=20, r2=0.21)     # breech
c.beam((0, -0.36, 0.56), (0, -1.25, 0.56), 0.21, steel, segs=20, r2=0.16)   # chase
c.cyl(0.22, 0.13, (0, -1.18, 0.56), iron, segs=18, axis="Y")                # muzzle
c.cyl(0.12, 0.03, (0, -1.26, 0.56), dark, segs=14, axis="Y")
for y in (-0.2, -0.7, -1.0): c.cyl(0.23, 0.06, (0, y, 0.56), brass, segs=18, axis="Y")
c.box((0.3, 0.22, 0.18), (0, 0.34, 0.58), red)                              # breech block
c.ico(0.12, (0, 0.48, 0.56), brass)                                         # cascabel
c.box((0.32, 0.08, 0.08), (0, 0.03, 0.72), steel)                           # sight
for s in (-1, 1): c.box((0.05, 0.06, 0.18), (s * 0.1, 0.03, 0.8), brass)
c.cyl(0.1, 0.05, (0.24, 0.18, 0.66), brass, segs=12, axis="Y")             # elevation wheel
out.append(c.finish())

# ---------------- 3. HARPOON: blue ballista, thick bow arms, one big spear ----------------
h = Builder("HarpoonHead", [iron, dark, steel, brass, blue, rubber, amber])
h.box((0.34, 1.5, 0.18), (0, -0.25, 0.26), dark)                           # stock
h.box((0.5, 0.25, 0.24), (0, 0.46, 0.3), blue)                             # rear stock
h.box((0.58, 0.26, 0.26), (0, -0.8, 0.32), iron)                           # bow housing
for s in (-1, 1):
    tube(h, [(s * 0.2, -0.82, 0.34), (s * 0.65, -0.72, 0.3), (s * 1.0, -0.45, 0.26)], 0.075, blue, segs=10)
    h.ico(0.1, (s * 1.0, -0.45, 0.26), brass, subdiv=1)
    tube(h, [(s * 1.0, -0.45, 0.26), (0, 0.2, 0.34)], 0.022, rubber, segs=6) # string
h.beam((0, 0.26, 0.42), (0, -1.2, 0.42), 0.05, steel, segs=10)             # spear shaft
h.beam((0, -1.2, 0.42), (0, -1.62, 0.42), 0.11, steel, segs=8, r2=0.0)     # spear head
for s in (-1, 1): h.beam((0, -1.24, 0.42), (s * 0.18, -1.1, 0.42), 0.03, steel, segs=6)
h.box((0.26, 0.2, 0.24), (0, 0.36, 0.3), iron)                             # winch
h.cyl(0.15, 0.2, (0.2, 0.36, 0.3), brass, segs=16, axis="X")
h.cyl(0.045, 0.18, (0.32, 0.36, 0.3), dark, segs=8, axis="X")
h.box((0.3, 0.08, 0.08), (0, -0.12, 0.62), brass)                           # sight
h.ico(0.06, (0, -0.2, 0.66), amber, subdiv=1)
out.append(h.finish())

# ---------------- 4. MORTAR: fat violet tube on a broad base ----------------
m = Builder("MortarHead", [iron, dark, steel, brass, violet, copper, amber])
m.cyl(0.55, 0.12, (0, 0, 0.06), iron, segs=22)                             # base
m.cyl(0.46, 0.1, (0, 0, 0.17), violet, segs=22)
studs(m, 0.5, 10, 0.13, 0.055)
for s in (-1, 1):
    m.box((0.12, 0.4, 0.36), (s * 0.28, 0.02, 0.34), violet)               # cradle
    m.box((0.1, 0.12, 0.3), (s * 0.34, 0.02, 0.34), brass)
    m.cyl(0.07, 0.42, (s * 0.16, -0.1, 0.42), copper, segs=10, axis="Y")   # recoil
m.cyl(0.12, 0.5, (0, 0.02, 0.36), steel, segs=14, axis="X")                # trunnion
m.beam((0, 0.24, 0.3), (0, -0.3, 1.05), 0.27, violet, segs=20, r2=0.22)    # fat tube
for k in (0.3, 0.62):
    p = (0, 0.24 - 0.54 * k, 0.3 + 0.75 * k)
    m.beam((p[0], p[1] - 0.03, p[2]), (p[0], p[1] + 0.03, p[2]), 0.285, brass, segs=20)
m.beam((0, -0.27, 1.0), (0, -0.35, 1.1), 0.29, brass, segs=20)             # muzzle ring
m.beam((0, -0.33, 1.07), (0, -0.37, 1.12), 0.2, dark, segs=16)
m.box((0.26, 0.24, 0.16), (0, 0.28, 0.24), copper)                          # breech
m.box((0.34, 0.12, 0.08), (0, 0.1, 0.55), steel)                            # sight
m.box((0.1, 0.1, 0.12), (0, 0.1, 0.63), amber)
out.append(m.finish())

# ---------------- 5. TESLA: chunky copper column, three big coils, glowing orb ----------------
t = Builder("TeslaHead", [iron, dark, steel, brass, copper, vio:=violet, glass, amber, ice])
t.cyl(0.42, 0.16, (0, 0, 0.09), iron, segs=20)
t.cyl(0.36, 0.1, (0, 0, 0.21), steel, segs=20)
studs(t, 0.34, 8, 0.16, 0.06)
t.cyl(0.15, 1.0, (0, 0, 0.85), copper, segs=16)                             # thick column
for z in (0.42, 0.62, 0.82, 1.02):
    torus(t, (0, 0, z), (0, 0, 1), 0.28, 0.05, brass, segs=22, tsegs=8)     # big coil rings
t.cyl(0.2, 0.08, (0, 0, 1.3), brass, segs=16)
t.ico(0.24, (0, 0, 1.45), ice, subdiv=2)                                    # orb
t.ico(0.12, (0, 0, 1.45), amber, subdiv=1)
for k in range(4):
    a = k * TAU / 4
    t.box((0.06, 0.06, 0.2), (0.2 * math.cos(a), 0.2 * math.sin(a), 1.38), steel, rot=(0, 0, -a))
out.append(t.finish())

# ---------------- 6. FLAME: two round fuel tanks and a big orange nozzle ----------------
f = Builder("FlameHead", [iron, dark, steel, brass, orange, red, rubber, amber])
f.box((0.6, 0.5, 0.16), (0, 0.06, 0.1), iron)
studs(f, 0.22, 6, 0.16, 0.05)
for x in (-0.22, 0.22):
    f.cyl(0.18, 0.46, (x, 0.2, 0.4), red, segs=18, axis="Y")               # fat tanks
    f.ico(0.18, (x, -0.02, 0.4), red, subdiv=2)
    f.ico(0.18, (x, 0.42, 0.4), red, subdiv=2)
    for y in (-0.05, 0.3): f.cyl(0.19, 0.05, (x, y, 0.4), brass, segs=18, axis="Y")  # straps
    f.cyl(0.06, 0.08, (x, 0.56, 0.4), brass, segs=10, axis="Y")
f.box((0.28, 0.2, 0.22), (0, 0.46, 0.36), iron)                             # pump
f.cyl(0.06, 0.16, (0.2, 0.46, 0.42), brass, segs=10, axis="X")
f.beam((0, -0.28, 0.4), (0, -0.95, 0.44), 0.09, steel, segs=10)
f.beam((0, -0.95, 0.44), (0, -1.2, 0.46), 0.1, steel, segs=10, r2=0.18)     # nozzle flare
for y in (-0.88, -1.1): f.cyl(0.12, 0.06, (0, y, 0.45), brass, segs=14, axis="Y")
f.ico(0.06, (0, -1.12, 0.6), amber, subdiv=1)                               # pilot
f.box((0.26, 0.2, 0.34), (0.38, 0.22, 0.36), dark)                          # heat shield
out.append(f.finish())

# ---------------- 7. SNIPER: long green rifle with a big scope ----------------
s = Builder("SniperHead", [iron, dark, steel, brass, green, rubber, glass, amber])
s.box((0.4, 0.72, 0.22), (0, 0.22, 0.22), green)                            # body
s.box((0.2, 0.24, 0.16), (0, 0.56, 0.2), rubber)                            # butt
s.box((0.28, 0.56, 0.2), (0, -0.2, 0.34), dark)                             # receiver
s.box((0.22, 0.24, 0.18), (0, -0.02, 0.34), green)
s.beam((0, -0.45, 0.38), (0, -2.05, 0.38), 0.07, steel, segs=12)            # thick barrel
for y in (-0.8, -1.2, -1.6): s.cyl(0.085, 0.045, (0, y, 0.38), brass, segs=12, axis="Y")
s.cyl(0.1, 0.2, (0, -2.0, 0.38), iron, segs=12, axis="Y")                   # brake
s.cyl(0.14, 0.04, (0, -2.1, 0.38), dark, segs=10, axis="Y")
s.beam((0, -0.05, 0.55), (0, -0.7, 0.55), 0.1, iron, segs=12)               # big scope
s.cyl(0.1, 0.04, (0, -0.72, 0.55), glass, segs=12, axis="Y")
for y in (-0.4, 0.05): s.box((0.12, 0.1, 0.12), (0, y, 0.46), brass)
s.box((0.08, 0.2, 0.08), (0.18, 0.0, 0.4), steel, rot=(0, 0.6, 0))           # bolt
for side in (-1, 1): s.beam((0, -1.1, 0.32), (side * 0.3, -1.35, 0.02), 0.03, dark, segs=6)
out.append(s.finish())

# ---------------- 8. BUBBLE: big cyan dish and a glass orb ----------------
bubble = mat("V3Bubble", (0.55, 0.9, 1.0), (0.35, 0.8, 1.0))
bb = Builder("BubbleHead", [iron, dark, steel, brass, cyan, bubble, glass])
bb.cyl(0.46, 0.14, (0, 0, 0.07), iron, segs=20)
bb.cyl(0.38, 0.1, (0, 0, 0.19), cyan, segs=20)
studs(bb, 0.4, 10, 0.14, 0.05)
for k in range(4):
    a = k * TAU / 4
    bb.box((0.1, 0.1, 0.36), (0.26 * math.cos(a), 0.26 * math.sin(a), 0.36), brass, rot=(0, 0, -a))
bb.cone(0.56, 0.16, 0.3, (0, 0, 0.52), brass, segs=24)                      # dish
bb.cyl(0.56, 0.04, (0, 0, 0.68), cyan, segs=24)
bb.beam((0, 0, 0.62), (0, 0, 1.0), 0.06, steel, segs=8)
bb.ico(0.24, (0, 0, 1.08), glass, subdiv=2)
bb.ico(0.12, (0, 0, 1.08), bubble, subdiv=2)
torus(bb, (0, 0, 0.88), (0, 0, 1), 0.32, 0.035, bubble, segs=24, tsegs=6)
out.append(bb.finish())

# ---------------- 9. MENDER: chunky green repair bot with two big arms ----------------
md = Builder("MenderHead", [iron, dark, steel, brass, mint, glass, amber])
md.cyl(0.46, 0.14, (0, 0, 0.07), iron, segs=20)
md.cyl(0.36, 0.74, (0, 0, 0.5), mint, segs=18)                              # fat core
for z in (0.24, 0.42, 0.6, 0.78): md.cyl(0.39, 0.06, (0, 0, z), brass, segs=22)
studs(md, 0.42, 10, 0.14, 0.05)
md.cyl(0.22, 0.08, (0, 0, 0.92), brass, segs=16)
md.ico(0.18, (0, 0, 1.02), glass, subdiv=2)
md.ico(0.09, (0, 0, 1.02), amber, subdiv=1)
for s in (-1, 1):
    md.beam((s * 0.34, 0, 0.5), (s * 0.62, -0.1, 0.72), 0.08, steel, segs=8)  # upper arm
    md.box((0.16, 0.16, 0.16), (s * 0.66, -0.12, 0.72), mint)
    md.beam((s * 0.66, -0.12, 0.68), (s * 0.58, -0.36, 0.46), 0.06, steel, segs=6)
    md.box((0.16, 0.2, 0.16), (s * 0.58, -0.42, 0.4), brass)                 # tool
    md.box((0.24, 0.05, 0.05), (s * 0.58, -0.5, 0.38), steel)
md.box((0.3, 0.24, 0.22), (0, 0.28, 0.32), dark)                            # toolbox
for i in range(3): md.box((0.24, 0.04, 0.04), (0, 0.28, 0.26 + i * 0.06), brass)
out.append(md.finish())

export(out)
