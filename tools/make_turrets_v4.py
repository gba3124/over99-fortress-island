# Turret art pass 4 (TURRET-UPGRADE-DESIGN.md, Owen's picks): each turret is its own base (<T>Body: plinth + chassis, the
# "Head" the game aims) plus seven swappable heads (<T>Default for Lv1-2, <T>A / <T>B at the Lv3 split, <T>A1 / <T>A2 /
# <T>B1 / <T>B2 at the Lv9 split: each second split keeps its first split's mechanism and adds the parts the design names)
# and the extra barrel (<T>Barrel) the game hangs beside the head for every extra shot/target (Catalog.Barrels at
# BuildSystem's Catalog.BarrelMount: the barrel's rear is at its origin, its axis on z = 0, pointing -Y).
# Conventions: Blender -Y is the front (Unity +Z), Z up, metres, origin on the top of TurretBase (the head pivot, 0.85 m up).
# Low-poly: flat faces, <= 5 materials and <= 2500 triangles a model (tests/Headless checks).
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_turrets_v4.py   [ONLY=GunBody,GunA ...]
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bkit import Builder, export, mat, torus, tube, fin, loft

# one palette for the nine (names new to the .blend, so the colours here win): stone / iron / steel / brass / wood + accents
stone = mat("T4Stone", (0.58, 0.56, 0.50))
iron = mat("T4Iron", (0.16, 0.19, 0.24))
steel = mat("T4Steel", (0.55, 0.60, 0.66))
brass = mat("T4Brass", (0.82, 0.58, 0.20), (0.18, 0.09, 0.02))
wood = mat("T4Wood", (0.42, 0.27, 0.14))
dark = mat("T4Dark", (0.05, 0.055, 0.065))
copper = mat("T4Copper", (0.66, 0.32, 0.13), (0.16, 0.05, 0.01))
amber = mat("T4Amber", (1.0, 0.62, 0.16), (0.9, 0.42, 0.04))     # A side glow
cyan = mat("T4Cyan", (0.35, 0.85, 1.0), (0.2, 0.7, 1.0))         # B side glow
ice = mat("T4Ice", (0.72, 0.93, 1.0), (0.3, 0.75, 1.0))          # frost / blue flame
red = mat("T4Red", (0.68, 0.14, 0.08), (0.2, 0.02, 0.01))        # fuel / danger
green = mat("T4Green", (0.3, 0.95, 0.45), (0.15, 0.8, 0.25))     # repair
glass = mat("T4Glass", (0.45, 0.72, 0.8), (0.15, 0.35, 0.45))
white = mat("T4White", (0.92, 0.93, 0.95), (0.4, 0.4, 0.42))
blue = mat("T4Blue", (0.16, 0.30, 0.55), (0.03, 0.08, 0.2))
TAU = 2 * math.pi
B = {}


def ring(r, n, z, phase=0.0):
    return [(r * math.cos(phase + i * TAU / n), r * math.sin(phase + i * TAU / n), z) for i in range(n)]


def model(name, mats, *parts):
    # One model from a list of part functions (each draws onto the builder): A1 = A's parts + its own.
    b = Builder(name, mats)
    for p in parts:
        p(b)
    B[name] = b.finish()


def studs(b, r, n, z, m=brass, size=0.05, phase=0.0):
    for p in ring(r, n, z, phase):
        b.box((size, size, size), p, m)


def barrel(b, x, z, y0, y1, r, m=steel, muzzle=brass, bore=iron, segs=8, bands=()):
    # A gun tube from y0 (rear) to y1 (front, more negative) with a muzzle ring and a dark bore.
    b.beam((x, y0, z), (x, y1, z), r, m, segs=segs)
    b.cyl(r * 1.25, 0.06, (x, y1 + 0.03, z), muzzle, segs=segs, axis="Y")
    b.cyl(r * 0.6, 0.015, (x, y1 - 0.005, z), bore, segs=segs, axis="Y")
    for y in bands:
        b.cyl(r * 1.2, 0.05, (x, y, z), muzzle, segs=segs, axis="Y")


def tank(b, loc, r, length, m, axis="Y", straps=brass, segs=10):
    # A drum with two straps, along Y or X.
    b.cyl(r, length, loc, m, segs=segs, axis=axis)
    for k in (-0.3, 0.3):
        p = list(loc)
        p[1 if axis == "Y" else 0] += k * length
        b.cyl(r * 1.08, 0.04, p, straps, segs=segs, axis=axis)


def crate(b, loc, size, m=wood, strap=iron):
    b.box(size, loc, m)
    for k in (-0.3, 0.3):
        b.box((size[0] * 1.04, size[1] * 0.12, size[2] * 1.04), (loc[0], loc[1] + k * size[1], loc[2]), strap)


def plinth_square(b, w, h, m=stone, band=iron, top=0.9):
    # A stepped square stone plinth from z = 0 to h with an iron band near its top.
    b.box((w, w, h * 0.55), (0, 0, h * 0.275), m)
    b.box((w * top, w * top, h * 0.45), (0, 0, h * 0.775), m)
    b.box((w * top + 0.04, w * top + 0.04, 0.05), (0, 0, h * 0.55), band)


def plinth_round(b, r, h, m=stone, band=iron, segs=10):
    b.cyl(r, h * 0.6, (0, 0, h * 0.3), m, segs=segs)
    b.cyl(r * 0.9, h * 0.4, (0, 0, h * 0.8), m, segs=segs)
    b.cyl(r * 0.92, 0.05, (0, 0, h * 0.6), band, segs=segs)


def cheeks(b, x, y, z, size, m=iron, cap=brass, pin=0.1):
    # Trunnion cheeks either side of a barrel at height z, with a round cap each.
    for s in (-1, 1):
        b.box(size, (s * x, y, z - size[2] * 0.3), m)
        b.cyl(pin, 0.06, (s * (x + size[0] / 2), y, z), cap, segs=8, axis="X")


def wheel(b, x, y, z, r, m=wood, hub=brass, spokes=4):
    b.cyl(r, 0.1, (x, y, z), m, segs=10, axis="X")
    b.cyl(r * 0.3, 0.14, (x, y, z), hub, segs=8, axis="X")
    for k in range(spokes):
        a = k * TAU / spokes
        b.box((0.04, 0.05, r * 1.5), (x, y, z), iron, rot=(a, 0, 0))


def coil(b, c, axis, R, r, m, n=12):
    torus(b, c, axis, R, r, m, segs=n, tsegs=4)


def shard(b, base, tip, r, m):
    b.beam(base, tip, r, m, segs=4, r2=0.0)


# ====================================================================== GUN (base 7)
# tall square stone plinth, the receiver on an iron yoke, an ammo box at the side; the gun's axis at z = 0.78
GZ = 0.72


def gun_body(b):
    plinth_square(b, 0.84, 0.42)
    b.box((0.7, 0.6, 0.1), (0, 0.02, 0.47), iron)                        # yoke
    studs(b, 0.36, 4, 0.53, phase=math.pi / 4, size=0.07)
    cheeks(b, 0.33, 0.05, GZ, (0.08, 0.34, 0.34))
    crate(b, (0.54, 0.1, 0.62), (0.24, 0.46, 0.32))                      # side ammo box
    b.box((0.25, 0.48, 0.04), (0.54, 0.1, 0.8), brass)
    b.box((0.06, 0.14, 0.06), (0.54, -0.14, 0.82), iron)                 # its latch


def gun_receiver(b, w=0.5, l=0.84, h=0.36, z=GZ, m=iron):
    b.box((w, l, h), (0, -0.02, z), m)
    b.box((w * 0.7, l * 0.5, 0.06), (0, 0.0, z + h / 2 + 0.03), steel)   # top cover
    b.box((0.08, 0.12, 0.12), (0, -0.28, z + h / 2 + 0.09), iron)        # sight
    b.box((0.24, 0.22, 0.22), (0, 0.5, z - 0.04), iron)                  # rear stock
    b.box((0.26, 0.05, 0.24), (0, 0.4, z - 0.04), brass)


def gun_belt(b, z=GZ):
    tube(b, [(0.4, 0.0, z + 0.1), (0.3, 0.02, z + 0.2), (0.12, 0.04, z + 0.16)], 0.04, brass, segs=6)


def gun_default(b):
    gun_receiver(b)
    for s in (-1, 1):
        barrel(b, s * 0.11, GZ, -0.4, -1.2, 0.055, bands=(-0.8,))
    b.box((0.32, 0.08, 0.16), (0, -0.65, GZ), iron)                       # barrel clamp
    gun_belt(b)


def gun_A(b):  # Storm: longer twin barrels in a vented shroud, round ammo drum, orange heat fins
    gun_receiver(b, w=0.54)
    for s in (-1, 1):
        barrel(b, s * 0.11, GZ, -0.4, -1.45, 0.055, bands=(-1.1,))
    b.cyl(0.2, 0.55, (0, -0.68, GZ), iron, segs=8, axis="Y")             # shroud
    for y in (-0.5, -0.68, -0.86):
        b.box((0.48, 0.06, 0.07), (0, y, GZ), amber)                     # heat fins
    b.cyl(0.23, 0.14, (0.34, 0.05, GZ + 0.12), iron, segs=10, axis="X")  # ammo drum
    b.cyl(0.09, 0.16, (0.34, 0.05, GZ + 0.12), brass, segs=8, axis="X")
    gun_belt(b)


def gun_A1(b):  # Typhoon: rotary feed carousel on top, an extra row of short barrels, a belt up into the carousel
    b.cyl(0.28, 0.12, (0, 0.05, GZ + 0.3), iron, segs=10)                # carousel
    b.cyl(0.3, 0.04, (0, 0.05, GZ + 0.38), brass, segs=10)
    for p in ring(0.2, 6, GZ + 0.45):
        b.cyl(0.045, 0.12, (p[0], p[1] + 0.05, p[2]), brass, segs=6)     # rounds in it
    b.cyl(0.06, 0.16, (0, 0.05, GZ + 0.48), amber, segs=6)
    for x in (-0.24, 0, 0.24):
        barrel(b, x, GZ - 0.24, -0.4, -1.0, 0.04)                         # the short row
    b.box((0.62, 0.08, 0.1), (0, -0.6, GZ - 0.24), iron)
    tube(b, [(0.46, 0.1, GZ + 0.05), (0.44, 0.1, GZ + 0.34), (0.3, 0.08, GZ + 0.4)], 0.045, brass, segs=6)


def gun_A2(b):  # Hailstorm: blue coolant jackets on the barrels, a coolant tank each side, a frost ring at the muzzle
    for s in (-1, 1):
        b.cyl(0.09, 0.55, (s * 0.11, -0.75, GZ), ice, segs=8, axis="Y")  # jackets
        tank(b, (s * 0.38, 0.1, GZ + 0.02), 0.1, 0.4, ice, straps=steel, segs=8)
        tube(b, [(s * 0.38, -0.1, GZ + 0.06), (s * 0.34, -0.4, GZ + 0.14), (s * 0.11, -0.5, GZ + 0.1)], 0.03, steel, segs=6)
    torus(b, (0, -1.38, GZ), (0, 1, 0), 0.19, 0.04, ice, segs=10, tsegs=4)  # frost ring at the muzzles
    b.cyl(0.08, 0.34, (0, 0.3, GZ + 0.34), ice, segs=8)                   # the tall coolant stack
    b.cyl(0.1, 0.05, (0, 0.3, GZ + 0.53), steel, segs=8)


def gun_B(b):  # Armour-piercing: one long barrel on a brass rail, a heavier receiver
    gun_receiver(b, w=0.56, l=0.9, h=0.4)
    barrel(b, 0, GZ, -0.4, -1.65, 0.07, bands=(-0.9, -1.3))
    b.box((0.09, 1.1, 0.05), (0, -0.9, GZ - 0.11), brass)                 # rail
    b.box((0.22, 0.08, 0.14), (0, -1.35, GZ - 0.09), iron)                # rail foot
    b.box((0.4, 0.28, 0.34), (0, 0.6, GZ), iron)                          # heavy breech
    b.box((0.1, 0.1, 0.18), (0.3, 0.45, GZ + 0.06), brass)                # charging handle


def gun_B1(b):  # Railgun: two parallel rails, three copper coils round the barrel, a blue arc at the muzzle
    for s in (-1, 1):
        b.box((0.05, 1.6, 0.07), (s * 0.14, -1.0, GZ), steel)             # rails
        b.box((0.03, 1.5, 0.03), (s * 0.14, -1.0, GZ + 0.05), cyan)
    for y in (-0.6, -0.95, -1.3):
        coil(b, (0, y, GZ), (0, 1, 0), 0.16, 0.045, copper, n=10)
    b.ico(0.1, (0, -1.9, GZ), cyan, subdiv=1)                             # arc
    b.cyl(0.07, 0.25, (0, -1.75, GZ), steel, segs=8, axis="Y")
    b.box((0.3, 0.3, 0.12), (0, 0.4, GZ + 0.26), iron)                    # capacitor bank
    for x in (-0.09, 0.09):
        b.cyl(0.05, 0.3, (x, 0.4, GZ + 0.26), cyan, segs=6, axis="Y")


def gun_B2(b):  # Executioner: a thick short barrel, a huge muzzle brake, a counterweight behind; lower and thicker
    b.box((0.64, 0.56, 0.22), (0, 0.1, GZ - 0.26), iron)                  # fattened lower receiver
    barrel(b, 0, GZ, -0.4, -1.2, 0.12, bands=(-0.8,))
    b.box((0.36, 0.3, 0.36), (0, -1.25, GZ), iron)                        # brake
    for k in range(3):
        b.box((0.42, 0.05, 0.12), (0, -1.14 - k * 0.1, GZ + 0.15), steel)
        b.box((0.42, 0.05, 0.12), (0, -1.14 - k * 0.1, GZ - 0.15), steel)
    b.box((0.5, 0.34, 0.38), (0, 0.72, GZ - 0.04), iron)                  # counterweight
    b.box((0.52, 0.08, 0.4), (0, 0.72, GZ - 0.04), red)


def gun_barrel(b):  # the extra gun: a slim twin cluster on a spindle (origin at its rear, axis z = 0, pointing -Y)
    b.cyl(0.09, 0.1, (0, -0.05, 0), iron, segs=8, axis="Y")
    for s in (-1, 1):
        barrel(b, s * 0.05, 0, -0.1, -0.8, 0.03, segs=6)
    b.cyl(0.08, 0.05, (0, -0.5, 0), brass, segs=8, axis="Y")


GUN = [iron, steel, brass]
model("GunBody", [stone, iron, brass, wood], gun_body)
model("GunDefault", GUN, gun_default)
model("GunA", [iron, steel, brass, amber], gun_A)
model("GunA1", [iron, steel, brass, amber], gun_A, gun_A1)
model("GunA2", [iron, steel, brass, amber, ice], gun_A, gun_A2)
model("GunB", GUN, gun_B)
model("GunB1", [iron, steel, brass, cyan, copper], gun_B, gun_B1)
model("GunB2", [iron, steel, brass, red], gun_B, gun_B2)
model("GunBarrel", [iron, steel, brass], gun_barrel)

# ====================================================================== CANNON (base 1)
# round stone and wood carriage, two wooden cheeks, one huge muzzle; the barrel's axis at z = 0.72
CZ = 0.72


def cannon_body(b):
    plinth_round(b, 0.58, 0.32)
    b.cyl(0.54, 0.1, (0, 0, 0.37), wood, segs=10)                        # plank ring
    studs(b, 0.47, 10, 0.42, m=iron, size=0.05)
    for s in (-1, 1):
        b.box((0.14, 0.9, 0.36), (s * 0.27, 0.08, 0.58), wood)           # cheeks
        b.box((0.15, 0.06, 0.38), (s * 0.27, -0.3, 0.58), iron)
        b.cyl(0.1, 0.06, (s * 0.35, 0.0, CZ), brass, segs=8, axis="X")   # trunnion caps
        wheel(b, s * 0.5, 0.25, 0.5, 0.2, wood)
    b.box((0.3, 0.3, 0.12), (0, 0.55, 0.46), wood)                        # trail
    b.beam((0, 0.55, 0.46), (0, 0.8, 0.3), 0.06, iron, segs=4)


def cannon_tube(b, length, r, rear=0.3, breech=1.2):
    b.beam((0, rear, CZ), (0, -0.2, CZ), r * breech, steel, segs=10, r2=r)  # breech taper
    barrel(b, 0, CZ, -0.2, -length, r, segs=10, bands=(-length * 0.45, -length * 0.8))
    b.cyl(r * 1.1, 0.06, (0, rear - 0.03, CZ), brass, segs=10, axis="Y")
    b.ico(r * 0.7, (0, rear + 0.08, CZ), brass, subdiv=1)                  # cascabel
    b.cyl(0.08, 0.5, (0, 0.05, CZ), iron, segs=8, axis="X")                # trunnion
    b.box((0.26, 0.08, 0.06), (0, 0.1, CZ + r + 0.04), steel)              # sight


def cannon_default(b):
    cannon_tube(b, 1.15, 0.17)


def cannon_A(b):  # Cluster: a shorter tube (the game hangs two more beside it) and a shell rack at the side
    cannon_tube(b, 0.9, 0.14)
    b.box((0.2, 0.5, 0.06), (0.52, 0.0, 0.72), iron)                      # rack
    b.box((0.2, 0.06, 0.2), (0.52, 0.26, 0.8), iron)
    for y in (-0.15, 0.0, 0.15):
        b.cyl(0.05, 0.18, (0.52, y, 0.8), iron, segs=6)
        b.cyl(0.035, 0.06, (0.52, y, 0.92), brass, segs=6)
    b.box((0.26, 0.06, 0.06), (0, -0.5, CZ + 0.18), amber)                 # cluster sight band


def cannon_A1(b):  # Carpet: a stacked mortar rack above the tube, muzzles up, an ammo belt feeding it
    b.box((0.5, 0.3, 0.08), (0, 0.15, CZ + 0.4), iron)                     # rack bed
    for s in (-1, 1):
        b.box((0.06, 0.3, 0.4), (s * 0.25, 0.15, CZ + 0.22), iron)
    for x in (-0.16, 0, 0.16):
        b.beam((x, 0.2, CZ + 0.42), (x, -0.08, CZ + 0.88), 0.055, steel, segs=8)
        b.cyl(0.07, 0.05, (x, -0.07, CZ + 0.87), brass, segs=8, axis="Y")
        b.cyl(0.04, 0.015, (x, -0.09, CZ + 0.9), iron, segs=8, axis="Y")
    b.box((1.8, 0.1, 0.1), (0, -0.28, CZ - 0.2), iron)                      # the axle bar the extra tubes rack on
    for x in (-0.85, 0.85):
        b.box((0.08, 0.14, 0.2), (x, -0.28, CZ - 0.24), brass)
    b.box((0.1, 0.9, 0.05), (0.52, 0.1, 0.78), iron)                       # belt from the rack
    for y in (-0.3, -0.1, 0.1, 0.3):
        b.cyl(0.035, 0.12, (0.52, y, 0.78), brass, segs=6, axis="X")
    tube(b, [(0.5, 0.45, 0.8), (0.4, 0.5, CZ + 0.5), (0.22, 0.42, CZ + 0.56)], 0.035, brass, segs=6)


def cannon_A2(b):  # Napalm cluster: a red fuel tank, orange hoses to the muzzle, three burner nozzles, a sooty mouth
    tank(b, (-0.5, 0.15, 0.78), 0.14, 0.5, red, segs=8)
    tube(b, [(-0.5, -0.1, 0.9), (-0.45, -0.55, 0.95), (-0.15, -0.8, CZ + 0.1)], 0.03, amber, segs=6)
    tube(b, [(-0.5, 0.0, 0.64), (-0.3, -0.4, 0.6), (-0.1, -0.7, CZ - 0.1)], 0.03, amber, segs=6)
    for p in ring(0.11, 3, 0, math.pi / 2):
        b.beam((p[0], -0.92, CZ + p[1]), (p[0], -1.1, CZ + p[1]), 0.04, iron, segs=6, r2=0.055)
        b.ico(0.03, (p[0], -1.1, CZ + p[1]), amber, subdiv=0)
    b.cyl(0.2, 0.04, (0, -0.9, CZ), iron, segs=10, axis="Y")               # burn ring


def cannon_B(b):  # Heavy: a thicker longer tube, two recoil cylinders above it, a heavy breech block
    cannon_tube(b, 1.45, 0.2, breech=1.25)
    for s in (-1, 1):
        b.cyl(0.05, 0.6, (s * 0.12, -0.1, CZ + 0.26), copper, segs=8, axis="Y")
        b.cyl(0.065, 0.1, (s * 0.12, 0.17, CZ + 0.26), brass, segs=8, axis="Y")
    b.box((0.36, 0.26, 0.32), (0, 0.4, CZ), iron)                          # breech block
    b.box((0.12, 0.14, 0.14), (0.26, 0.4, CZ + 0.08), brass)               # breech handle


def cannon_B1(b):  # Siege: the longest barrel, a front armour shield, big wheels and a spade; the tallest
    b.beam((0, -1.45, CZ), (0, -1.75, CZ), 0.16, steel, segs=10)
    b.cyl(0.2, 0.06, (0, -1.72, CZ), brass, segs=10, axis="Y")
    b.cyl(0.1, 0.015, (0, -1.76, CZ), iron, segs=10, axis="Y")
    for s in (-1, 1):                                                       # shield in two halves round the barrel
        b.box((0.3, 0.06, 0.7), (s * 0.4, -0.45, CZ + 0.05), steel, rot=(0, 0, s * 0.25))
        b.box((0.26, 0.03, 0.14), (s * 0.4, -0.49, CZ + 0.3), cyan, rot=(0, 0, s * 0.25))
        wheel(b, s * 0.58, 0.3, 0.55, 0.32, iron, spokes=5)
    b.box((0.5, 0.06, 0.26), (0, -0.45, CZ + 0.38), steel)                 # shield top plate
    b.beam((0, 0.7, 0.4), (0, 1.0, 0.1), 0.07, iron, segs=4)               # spade
    b.box((0.3, 0.1, 0.1), (0, 1.02, 0.1), iron)


def cannon_B2(b):  # Shockwave: a wide muzzle brake and a blast ring, the carriage weighted low with springs
    b.cone(0.2, 0.34, 0.3, (0, -1.55, CZ), iron, segs=10, axis="-Y")
    b.cyl(0.26, 0.02, (0, -1.71, CZ), iron, segs=10, axis="Y")
    torus(b, (0, -1.3, CZ), (0, 1, 0), 0.42, 0.045, cyan, segs=12, tsegs=4)  # blast ring
    for s in (-1, 1):
        b.beam((s * 0.3, -1.3, CZ), (s * 0.2, -1.1, CZ), 0.03, steel, segs=4)
        b.box((0.16, 0.5, 0.2), (s * 0.52, 0.15, 0.42), iron)              # side weights
        for y in (0.0, 0.3):
            for k in range(3):                                              # springs: three rings each
                coil(b, (s * 0.52, y, 0.55 + k * 0.06), (0, 0, 1), 0.07, 0.02, steel, n=8)


def cannon_barrel(b):  # the extra cannon tube
    b.beam((0, 0.0, 0), (0, -0.25, 0), 0.16, steel, segs=10, r2=0.13)
    barrel(b, 0, 0, -0.25, -0.95, 0.13, segs=10, bands=(-0.6,))
    b.ico(0.09, (0, 0.05, 0), brass, subdiv=1)


CAN = [steel, brass, iron]
model("CannonBody", [stone, wood, iron, brass], cannon_body)
model("CannonDefault", CAN, cannon_default)
model("CannonA", [steel, brass, iron, amber], cannon_A)
model("CannonA1", [steel, brass, iron, amber], cannon_A, cannon_A1)
model("CannonA2", [steel, brass, iron, amber, red], cannon_A, cannon_A2)
model("CannonB", [steel, brass, iron, copper], cannon_B)
model("CannonB1", [steel, brass, iron, copper, cyan], cannon_B, cannon_B1)
model("CannonB2", [steel, brass, iron, copper, cyan], cannon_B, cannon_B2)
model("CannonBarrel", [steel, brass, iron], cannon_barrel)

# ====================================================================== HARPOON
# square stone base, a wooden A-frame, the winch in front, the spear on a long rail; the spear's axis at z = 0.95
HZ = 0.95


def harpoon_body(b):
    plinth_square(b, 0.86, 0.4)
    b.box((0.76, 0.96, 0.06), (0, 0.0, 0.45), wood)                       # deck
    for s in (-1, 1):                                                       # A-frame legs
        b.beam((s * 0.38, 0.22, 0.46), (s * 0.06, 0.12, HZ - 0.02), 0.06, wood, segs=4)
        b.beam((s * 0.38, 0.22, 0.46), (s * 0.3, -0.1, 0.46), 0.05, wood, segs=4)
        b.box((0.05, 0.05, 0.12), (s * 0.24, 0.17, 0.5), iron)
        b.box((0.06, 0.14, 0.26), (s * 0.17, -0.36, 0.58), iron)          # winch uprights
    b.box((0.44, 0.06, 0.06), (0, 0.17, 0.74), wood)                        # brace
    b.cyl(0.11, 0.3, (0, -0.36, 0.62), wood, segs=8, axis="X")              # winch drum
    b.cyl(0.12, 0.04, (0.17, -0.36, 0.62), brass, segs=8, axis="X")
    b.beam((0.2, -0.36, 0.62), (0.3, -0.36, 0.62), 0.03, iron, segs=4)      # crank
    b.box((0.04, 0.04, 0.18), (0.3, -0.36, 0.7), brass)


def harpoon_rail(b, x=0.0, y0=0.5, y1=-1.0, m=wood):
    b.box((0.12, y0 - y1, 0.08), (x, (y0 + y1) / 2, HZ - 0.08), m)
    b.box((0.16, 0.08, 0.1), (x, y1 + 0.04, HZ - 0.08), iron)
    b.box((0.16, 0.08, 0.1), (x, y0 - 0.04, HZ - 0.08), iron)


def spear(b, x=0.0, tail=0.45, tip=-1.4, r=0.035, m=steel, head=steel):
    b.beam((x, tail, HZ), (x, tip, HZ), r, m, segs=6)
    b.beam((x, tip, HZ), (x, tip - 0.32, HZ), r * 2.6, head, segs=4, r2=0.0)
    for s in (-1, 1):
        b.beam((x, tip - 0.04, HZ), (x + s * 0.1, tip + 0.1, HZ), 0.018, head, segs=4)
    b.cyl(r * 1.8, 0.08, (x, tail - 0.03, HZ), brass, segs=6, axis="Y")


def harpoon_default(b):
    harpoon_rail(b)
    spear(b)
    tube(b, [(0, -0.36, 0.7), (0, 0.1, HZ - 0.12), (0, 0.45, HZ - 0.02)], 0.02, wood, segs=4)   # the winch rope


def harpoon_A(b):  # Multi: a wide rail head with three small reels in front (the game hangs two more spears beside)
    harpoon_default(b)
    b.box((0.5, 0.3, 0.1), (0, -0.75, HZ - 0.14), iron)
    for x in (-0.16, 0, 0.16):
        b.cyl(0.06, 0.08, (x, -0.95, HZ - 0.14), brass, segs=8, axis="X")
    b.box((0.5, 0.04, 0.04), (0, -0.6, HZ - 0.06), amber)


def harpoon_A1(b):  # Volley: a rail each side and a triple winch behind
    for s in (-1, 1):
        harpoon_rail(b, s * 0.3, 0.3, -1.05)
    b.box((1.7, 0.14, 0.08), (0, -0.3, HZ - 0.16), wood)                     # the wide rail bed
    b.box((1.72, 0.06, 0.1), (0, -0.3, HZ - 0.16), iron)
    b.box((0.76, 0.1, 0.1), (0, 0.5, HZ - 0.1), iron)
    for x in (-0.26, 0, 0.26):
        b.cyl(0.08, 0.2, (x, 0.5, HZ + 0.02), brass, segs=8, axis="X")


def harpoon_A2(b):  # Chain harpoon: a chain spool behind, links under the spear, a second hook, a sprocket on the side
    b.cyl(0.15, 0.26, (0, 0.6, HZ + 0.2), iron, segs=8, axis="X")
    b.cyl(0.16, 0.04, (0.15, 0.6, HZ + 0.2), brass, segs=8, axis="X")
    for i in range(7):
        y = 0.35 - i * 0.24
        torus(b, (0, y, HZ - 0.12), (1 if i % 2 else 0, 0, 0 if i % 2 else 1), 0.06, 0.016, steel, segs=8, tsegs=4)
    b.beam((0, -1.15, HZ - 0.16), (0, -1.4, HZ - 0.16), 0.07, steel, segs=4, r2=0.0)   # the trailing hook
    b.cyl(0.1, 0.05, (0.3, 0.2, HZ + 0.1), brass, segs=8, axis="X")                   # sprocket
    for p in ring(0.1, 6, 0):
        b.box((0.05, 0.04, 0.04), (0.3, 0.2 + p[0], HZ + 0.1 + p[1]), brass)


def harpoon_B(b):  # Freeze: a cold blue shaft, an ice block round the head, condensation pipes along the rail
    harpoon_rail(b)
    spear(b, m=ice, head=ice)
    b.box((0.2, 0.3, 0.2), (0, -1.5, HZ), ice, rot=(0, math.pi / 4, 0))
    for s in (-1, 1):
        tube(b, [(s * 0.1, 0.4, HZ - 0.14), (s * 0.1, -0.9, HZ - 0.14), (s * 0.06, -1.1, HZ - 0.06)], 0.02, steel, segs=4)
    tube(b, [(0, -0.36, 0.7), (0, 0.1, HZ - 0.12), (0, 0.45, HZ - 0.02)], 0.02, wood, segs=4)


def harpoon_B1(b):  # Glacier: a great ice crystal round the head, condenser loops, frost on the rail
    shard(b, (0, -1.4, HZ), (0, -2.0, HZ + 0.05), 0.12, ice)
    shard(b, (0, -1.45, HZ), (0.2, -1.75, HZ + 0.3), 0.08, ice)
    shard(b, (0, -1.45, HZ), (-0.2, -1.7, HZ - 0.2), 0.08, ice)
    for s in (-1, 1):
        tube(b, [(s * 0.2, 0.3, HZ - 0.2), (s * 0.32, 0.0, HZ + 0.1), (s * 0.32, -0.6, HZ + 0.1), (s * 0.14, -0.9, HZ - 0.1)], 0.03, steel, segs=5)
        b.cyl(0.06, 0.2, (s * 0.32, -0.3, HZ + 0.1), ice, segs=6, axis="Y")
    torus(b, (0, -0.3, HZ - 0.08), (0, 1, 0), 0.12, 0.03, ice, segs=8, tsegs=4)


def harpoon_B2(b):  # Shatter: the head split into three ice blades, a hopper of broken ice behind
    for a in (0, 2.1, -2.1):
        n = (math.cos(a), 0, math.sin(a))
        c = (0.07 * math.cos(a + 1.57), 0, 0.07 * math.sin(a + 1.57))
        fin(b, [(c[0], -1.35, HZ + c[2]), (c[0] + n[0] * 0.2, -1.5, HZ + n[2] * 0.2), (c[0] + n[0] * 0.08, -1.95, HZ + n[2] * 0.08)], (n[2], 0, -n[0]), 0.03, ice)
    b.box((0.36, 0.3, 0.26), (0, 0.5, HZ + 0.12), iron)
    for p in ring(0.09, 5, 0):
        b.box((0.09, 0.09, 0.09), (p[0], 0.5 + p[1], HZ + 0.28), ice, rot=(0.4, 0.6, p[0]))


def harpoon_barrel(b):
    b.box((0.08, 0.7, 0.06), (0, -0.3, -0.06), wood)
    b.box((0.07, 0.07, 0.4), (0, -0.3, -0.28), iron)                        # post down to the rail bed
    b.beam((0, 0.0, 0), (0, -0.95, 0), 0.025, steel, segs=6)
    b.beam((0, -0.95, 0), (0, -1.2, 0), 0.06, steel, segs=4, r2=0.0)
    b.box((0.12, 0.06, 0.08), (0, -0.62, -0.06), iron)


HAR = [wood, steel, iron, brass]
model("HarpoonBody", [stone, wood, iron, brass], harpoon_body)
model("HarpoonDefault", HAR, harpoon_default)
model("HarpoonA", HAR + [amber], harpoon_A)
model("HarpoonA1", HAR + [amber], harpoon_A, harpoon_A1)
model("HarpoonA2", HAR + [amber], harpoon_A, harpoon_A2)
model("HarpoonB", HAR + [ice], harpoon_B)
model("HarpoonB1", HAR + [ice], harpoon_B, harpoon_B1)
model("HarpoonB2", HAR + [ice], harpoon_B, harpoon_B2)
model("HarpoonBarrel", [wood, steel, iron], harpoon_barrel)

# ====================================================================== MORTAR
# a squat stone bunker, the tube pitched up from an iron cradle, a shell crate at the side; the pivot at z = 0.45
MP = (0, 0.15, 0.45)
MD = (0, -0.55, 0.75)  # the tube's direction (up and forward)


def along(k, x=0.0):  # a point k along the tube
    return (x + MP[0], MP[1] + MD[1] * k, MP[2] + MD[2] * k)


def mortar_body(b):
    plinth_round(b, 0.6, 0.4, segs=8)
    for p in ring(0.52, 8, 0.46, math.pi / 8):
        b.box((0.16, 0.16, 0.12), p, stone, rot=(0, 0, math.atan2(p[1], p[0])))   # merlons
    b.cyl(0.34, 0.08, (0, 0.05, 0.42), iron, segs=8)
    cheeks(b, 0.24, 0.15, 0.5, (0.09, 0.44, 0.3))
    crate(b, (0.58, 0.05, 0.5), (0.26, 0.46, 0.3))
    for y in (-0.12, 0.05, 0.22):
        b.cyl(0.045, 0.1, (0.58, y, 0.68), iron, segs=6)
        b.beam((0.58, y, 0.72), (0.58, y, 0.8), 0.045, brass, segs=6, r2=0.0)


def mortar_tube(b, r=0.17, k=1.0, x=0.0):
    b.beam(along(0, x), along(k, x), r, steel, segs=10)
    b.beam(along(k, x), along(k + 0.06, x), r * 1.2, brass, segs=10)
    b.beam(along(k + 0.06, x), along(k + 0.075, x), r * 0.72, iron, segs=10)
    b.beam(along(k * 0.5, x), along(k * 0.5 + 0.05, x), r * 1.15, brass, segs=10)


def mortar_default(b):
    mortar_tube(b)
    b.cyl(0.07, 0.56, MP, brass, segs=8, axis="X")                           # trunnion
    b.box((0.2, 0.26, 0.16), (0, 0.3, 0.42), iron)                           # breech
    b.beam((0.2, 0.15, 0.45), (0.2, -0.1, 0.8), 0.03, iron, segs=4)          # elevation screw
    b.cyl(0.05, 0.04, (0.2, -0.1, 0.8), brass, segs=6, axis="Y")


def mortar_A(b):  # Fire: a fuel tank at the side, a hose up to the tube, a burning ring at the muzzle
    mortar_default(b)
    tank(b, (-0.46, 0.1, 0.6), 0.13, 0.44, red, segs=8)
    tube(b, [(-0.46, -0.1, 0.72), (-0.3, -0.25, 0.75), (-0.12, -0.2, 0.75)], 0.025, brass, segs=5)
    torus(b, along(0.98), MD, 0.22, 0.035, amber, segs=10, tsegs=4)


def mortar_A1(b):  # Napalm: a second fuel barrel, oil pipes round the base, a bigger ring of fire
    tank(b, (0.0, 0.5, 0.56), 0.13, 0.4, red, axis="X", segs=8)
    tube(b, [(-0.46, 0.32, 0.5), (-0.4, 0.55, 0.42), (0.0, 0.6, 0.42)], 0.025, brass, segs=5)
    torus(b, along(0.9), MD, 0.26, 0.04, amber, segs=10, tsegs=4)
    b.cyl(0.08, 0.08, (-0.46, 0.1, 0.78), brass, segs=6)                     # filler cap


def mortar_A2(b):  # Firestorm: a second lower tube, powder kegs behind, a soot hood over the muzzles
    mortar_tube(b, r=0.13, k=0.8, x=0.3)
    for x in (-0.14, 0.14):
        b.cyl(0.1, 0.22, (x, 0.5, 0.52), iron, segs=8)
        b.cyl(0.11, 0.03, (x, 0.5, 0.58), brass, segs=8)
    p = along(1.1)
    b.cone(0.3, 0.22, 0.22, (0.15, p[1], p[2] + 0.14), iron, segs=8)


def mortar_B(b):  # Barrage: a shell rack and a loading arm (the game hangs two more tubes beside)
    mortar_default(b)
    b.box((0.26, 0.5, 0.05), (-0.5, 0.05, 0.5), iron)
    for y in (-0.15, 0.0, 0.15):
        b.cyl(0.05, 0.18, (-0.5, y, 0.6), steel, segs=6)
        b.beam((-0.5, y, 0.68), (-0.5, y, 0.76), 0.05, brass, segs=6, r2=0.0)
    b.beam((-0.4, 0.3, 0.55), (-0.2, 0.2, 1.0), 0.03, steel, segs=4)         # loading arm
    b.beam((-0.2, 0.2, 1.0), (0.0, -0.1, 1.05), 0.03, steel, segs=4)
    b.box((0.1, 0.1, 0.06), (0.0, -0.12, 1.05), brass)
    b.box((0.26, 0.04, 0.04), (-0.5, 0.33, 0.52), cyan)


def mortar_B1(b):  # Grand barrage: a three-tier rack, a longer loader, a belt of spent shells
    for z in (0.78, 0.96):
        b.box((0.26, 0.5, 0.04), (-0.5, 0.05, z), iron)
        for y in (-0.15, 0.0, 0.15):
            b.cyl(0.05, 0.16, (-0.5, y, z + 0.1), steel, segs=6)
    for s in (-1, 1):
        b.box((0.04, 0.04, 0.5), (-0.5 + s * 0.11, 0.3, 0.75), iron)
    b.beam((0.0, -0.1, 1.05), (0.1, -0.45, 1.25), 0.03, steel, segs=4)
    b.box((0.12, 0.12, 0.08), (0.1, -0.5, 1.25), brass)
    b.box((1.8, 0.12, 0.1), (0, -0.1, 0.5), iron)                            # the cradle bar the extra tubes sit on
    for x in (-0.3, -0.15, 0.0, 0.15, 0.3):
        b.cyl(0.035, 0.1, (x, -0.5, 0.46), brass, segs=6, axis="Y")


def mortar_B2(b):  # Cluster shells: a flared mouth, a cluster warhead sitting in it, a split ammo crate
    b.beam(along(0.95), along(1.2), 0.17, steel, segs=10, r2=0.27)
    p = along(1.22)
    for q in ring(0.1, 4, 0):
        b.ico(0.07, (p[0] + q[0], p[1] + q[1] * 0.8, p[2] + q[1] * 0.6), iron, subdiv=1)
    b.box((0.26, 0.2, 0.2), (0.0, 0.55, 0.5), iron)
    b.box((0.26, 0.2, 0.2), (0.0, 0.55, 0.78), iron, rot=(0, 0.3, 0))
    b.box((0.27, 0.04, 0.22), (0.0, 0.55, 0.5), cyan)


def mortar_barrel(b):
    b.beam((0, 0, 0), (0, -0.4, 0.55), 0.12, steel, segs=8)
    b.beam((0, -0.4, 0.55), (0, -0.44, 0.6), 0.14, brass, segs=8)
    b.beam((0, -0.44, 0.6), (0, -0.45, 0.62), 0.09, iron, segs=8)
    b.cyl(0.1, 0.1, (0, 0, 0.0), iron, segs=8)
    b.box((0.2, 0.2, 0.06), (0, 0, -0.03), iron)


MOR = [steel, brass, iron]
model("MortarBody", [stone, iron, brass, wood], mortar_body)
model("MortarDefault", MOR, mortar_default)
model("MortarA", MOR + [red, amber], mortar_A)
model("MortarA1", MOR + [red, amber], mortar_A, mortar_A1)
model("MortarA2", MOR + [red, amber], mortar_A, mortar_A2)
model("MortarB", MOR + [cyan], mortar_B)
model("MortarB1", MOR + [cyan], mortar_B, mortar_B1)
model("MortarB2", MOR + [cyan], mortar_B, mortar_B2)
model("MortarBarrel", MOR, mortar_barrel)

# ====================================================================== TESLA
# a tall tapering stone and iron tower, the coil column on top; the column's foot at z = 1.15
TZ = 1.15


def tesla_body(b):
    plinth_square(b, 0.76, 0.3)
    for w, z in ((0.56, 0.45), (0.46, 0.73), (0.36, 0.98)):
        b.box((w, w, 0.28), (0, 0, z), iron)
        b.box((w + 0.04, w + 0.04, 0.04), (0, 0, z + 0.14), brass)
    for p in ring(0.44, 4, 0.3, math.pi / 4):
        b.beam(p, (p[0] * 0.45, p[1] * 0.45, 1.0), 0.03, steel, segs=4)
    b.cyl(0.26, 0.06, (0, 0, TZ - 0.03), brass, segs=8)
    for p in ring(0.3, 4, TZ):
        b.cyl(0.04, 0.12, p, glass, segs=6)
        b.cyl(0.05, 0.03, (p[0], p[1], TZ + 0.07), brass, segs=6)


def tesla_default(b):
    b.cyl(0.09, 0.6, (0, 0, TZ + 0.3), copper, segs=8)
    for z in (0.15, 0.3, 0.45):
        coil(b, (0, 0, TZ + z), (0, 0, 1), 0.2, 0.045, copper, n=10)
    b.cyl(0.13, 0.06, (0, 0, TZ + 0.63), brass, segs=8)
    b.cyl(0.03, 0.14, (0, 0, TZ + 0.72), steel, segs=6)
    b.ico(0.14, (0, 0, TZ + 0.85), ice, subdiv=1)


def tesla_A(b):  # Spread: four discharge prongs off the top, insulators on the column
    tesla_default(b)
    for p in ring(0.36, 4, TZ + 1.05, math.pi / 4):
        b.beam((0, 0, TZ + 0.85), p, 0.02, steel, segs=4)
        b.ico(0.04, p, ice, subdiv=0)
    for p in ring(0.22, 3, TZ + 0.3, 0.5):
        b.cyl(0.04, 0.14, p, glass, segs=6)
        b.beam((0, 0, TZ + 0.3), p, 0.02, brass, segs=4)


def tesla_A1(b):  # Storm chain: a huge top electrode, more insulators, longer prongs
    b.ico(0.24, (0, 0, TZ + 0.95), ice, subdiv=1)
    for p in ring(0.5, 4, TZ + 1.2, math.pi / 4):
        b.beam((0, 0, TZ + 0.95), p, 0.02, steel, segs=4)
        b.ico(0.05, p, ice, subdiv=0)
    for p in ring(0.26, 3, TZ + 0.55, 0.5):
        b.cyl(0.05, 0.16, p, glass, segs=6)
        b.beam((0, 0, TZ + 0.55), p, 0.02, brass, segs=4)


def tesla_A2(b):  # Conduction: a ground chain down the tower, a wet conduit ring, a blue coil
    for i in range(7):
        z = TZ - 0.05 - i * 0.14
        torus(b, (0.33, 0.33, z), (1 if i % 2 else 0, 1 if i % 2 else 0, 0 if i % 2 else 1), 0.05, 0.014, steel, segs=8, tsegs=4)
    torus(b, (0, 0, TZ + 0.05), (0, 0, 1), 0.34, 0.04, ice, segs=12, tsegs=5)
    for p in ring(0.34, 4, TZ + 0.05):
        b.beam(p, (p[0] * 0.3, p[1] * 0.3, TZ + 0.05), 0.025, steel, segs=4)
    coil(b, (0, 0, TZ + 0.52), (0, 0, 1), 0.22, 0.05, ice, n=10)


def tesla_B(b):  # Overload: a transformer at the side, two capacitor drums, a red gauge
    tesla_default(b)
    b.box((0.3, 0.36, 0.4), (-0.42, 0.05, TZ + 0.2), copper)
    for y in (-0.08, 0.08):
        b.cyl(0.03, 0.1, (-0.42, y, TZ + 0.45), brass, segs=6)
    b.cyl(0.07, 0.03, (-0.42, -0.14, TZ + 0.3), red, segs=8, axis="Y")
    for y in (-0.12, 0.12):
        b.cyl(0.08, 0.3, (0.38, y, TZ + 0.15), steel, segs=8)
        b.cyl(0.085, 0.03, (0.38, y, TZ + 0.3), brass, segs=8)
    tube(b, [(-0.27, 0.05, TZ + 0.4), (-0.1, 0.05, TZ + 0.5), (0, 0, TZ + 0.5)], 0.02, brass, segs=4)


def tesla_B1(b):  # Overcharge: the transformer stacked higher, bigger drums with red caps, vents, a big gauge
    b.box((0.3, 0.36, 0.26), (-0.42, 0.05, TZ + 0.53), copper)
    for k in range(3):
        b.box((0.34, 0.02, 0.04), (-0.42, -0.06 + k * 0.06, TZ + 0.66), steel)
    for y in (-0.14, 0.14):
        b.cyl(0.1, 0.44, (0.4, y, TZ + 0.22), steel, segs=8)
        b.cyl(0.1, 0.04, (0.4, y, TZ + 0.46), red, segs=8)
    b.cyl(0.1, 0.03, (-0.42, -0.19, TZ + 0.5), red, segs=8, axis="Y")
    b.box((0.02, 0.06, 0.12), (-0.42, -0.21, TZ + 0.54), brass)


def tesla_B2(b):  # Arc blast: a spherical cage round the electrode, six radial electrodes, a white core
    c = (0, 0, TZ + 0.85)
    for ax in ((1, 0, 0), (0, 1, 0), (0, 0, 1)):
        torus(b, c, ax, 0.3, 0.018, copper, segs=12, tsegs=4)
    for ax in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0.7, 0.7, 0)):
        b.beam(c, (c[0] + ax[0] * 0.36, c[1] + ax[1] * 0.36, c[2] + ax[2] * 0.36), 0.025, steel, segs=4, r2=0.0)
    b.ico(0.17, c, ice, subdiv=1)


def tesla_barrel(b):
    b.cyl(0.08, 0.06, (0, 0, 0.03), brass, segs=8)
    b.cyl(0.05, 0.4, (0, 0, 0.26), copper, segs=6)
    for z in (0.15, 0.3):
        coil(b, (0, 0, z), (0, 0, 1), 0.1, 0.025, copper, n=8)
    b.ico(0.07, (0, 0, 0.52), ice, subdiv=0)


TES = [copper, brass, steel, ice]
model("TeslaBody", [stone, iron, brass, steel, glass], tesla_body)
model("TeslaDefault", TES, tesla_default)
model("TeslaA", TES + [glass], tesla_A)
model("TeslaA1", TES + [glass], tesla_A, tesla_A1)
model("TeslaA2", TES + [glass], tesla_A, tesla_A2)
model("TeslaB", TES + [red], tesla_B)
model("TeslaB1", TES + [red], tesla_B, tesla_B1)
model("TeslaB2", TES + [red], tesla_B, tesla_B2)
model("TeslaBarrel", TES, tesla_barrel)

# ====================================================================== FLAME
# a low stone bunker with the two fuel tanks on it, the nozzle out front; the nozzle's axis at z = 0.5
FZ = 0.5


def flame_body(b):
    b.box((1.0, 0.8, 0.26), (0, 0.08, 0.13), stone)
    b.box((1.0, 0.3, 0.2), (0, -0.4, 0.1), stone, rot=(0.5, 0, 0))            # the sloped front
    b.box((0.7, 0.5, 0.06), (0, 0.15, 0.29), iron)
    for s in (-1, 1):
        tank(b, (s * 0.3, 0.25, 0.5), 0.16, 0.44, red, segs=8)
        b.ico(0.16, (s * 0.3, 0.47, 0.5), red, subdiv=1)
        b.ico(0.16, (s * 0.3, 0.03, 0.5), red, subdiv=1)
        b.box((0.1, 0.1, 0.22), (s * 0.3, 0.25, 0.3), iron)
    b.box((0.26, 0.2, 0.22), (0, 0.5, 0.42), iron)                           # pump
    b.cyl(0.06, 0.03, (0, 0.39, 0.46), brass, segs=8, axis="Y")               # gauge


def nozzle(b, r=0.09, flare=0.15, y0=-0.1, y1=-0.75, y2=-0.95, m=steel, mouth=iron):
    b.beam((0, y0, FZ), (0, y1, FZ), r, m, segs=8)
    b.beam((0, y1, FZ), (0, y2, FZ), r, m, segs=8, r2=flare)
    b.cyl(flare * 0.8, 0.02, (0, y2 - 0.005, FZ), mouth, segs=8, axis="Y")
    b.cyl(r * 1.3, 0.05, (0, y1 + 0.02, FZ), brass, segs=8, axis="Y")


def flame_default(b):
    nozzle(b)
    b.box((0.3, 0.3, 0.26), (0, 0.1, FZ), iron)                                # valve block
    torus(b, (0, -0.25, FZ + 0.2), (0, 0, 1), 0.08, 0.02, brass, segs=8, tsegs=4)  # valve wheel
    b.cyl(0.02, 0.1, (0, -0.25, FZ + 0.15), brass, segs=4)
    for s in (-1, 1):
        tube(b, [(s * 0.3, 0.0, FZ + 0.05), (s * 0.2, -0.15, FZ + 0.1), (s * 0.1, -0.2, FZ + 0.05)], 0.025, copper, segs=5)
    b.ico(0.05, (0, -0.9, FZ + 0.18), amber, subdiv=0)                         # pilot light


def flame_A(b):  # Inferno: a tank stacked on each side and a wider burner behind a heat shield
    nozzle(b, flare=0.2)
    b.box((0.3, 0.3, 0.26), (0, 0.1, FZ), iron)
    torus(b, (0, -0.25, FZ + 0.2), (0, 0, 1), 0.08, 0.02, brass, segs=8, tsegs=4)
    b.cyl(0.02, 0.1, (0, -0.25, FZ + 0.15), brass, segs=4)
    for s in (-1, 1):
        tank(b, (s * 0.3, 0.25, 0.8), 0.14, 0.4, red, segs=8)
        tube(b, [(s * 0.3, 0.05, 0.8), (s * 0.2, -0.1, FZ + 0.15), (s * 0.1, -0.2, FZ + 0.05)], 0.025, brass, segs=5)
    b.box((0.7, 0.04, 0.3), (0, -0.05, 0.62), iron)                            # heat shield
    b.ico(0.05, (0, -0.9, FZ + 0.22), amber, subdiv=0)


def flame_A1(b):  # Hellfire: a third drum, a thick black nozzle, a vent stack
    tank(b, (0, 0.3, 1.02), 0.15, 0.44, red, segs=8)
    b.beam((0, -0.2, FZ), (0, -0.8, FZ), 0.13, iron, segs=8)
    b.beam((0, -0.8, FZ), (0, -1.05, FZ), 0.13, iron, segs=8, r2=0.22)
    b.cyl(0.18, 0.02, (0, -1.06, FZ), amber, segs=8, axis="Y")
    b.cyl(0.06, 0.44, (0.25, 0.45, 0.95), iron, segs=6)
    b.cyl(0.09, 0.05, (0.25, 0.45, 1.18), steel, segs=6)
    b.cyl(0.05, 0.04, (0.25, 0.45, 0.7), brass, segs=6)


def flame_A2(b):  # Melt: a long fine high-heat lance, a white-hot chamber, a slag trough under it
    b.beam((0, -0.9, FZ), (0, -1.55, FZ), 0.045, steel, segs=6)
    b.cyl(0.06, 0.04, (0, -1.3, FZ), brass, segs=6, axis="Y")
    b.ico(0.13, (0, -0.4, FZ), amber, subdiv=1)
    b.box((0.3, 0.5, 0.08), (0, -0.95, FZ - 0.24), iron)
    b.box((0.24, 0.42, 0.04), (0, -0.95, FZ - 0.19), amber)


def flame_B(b, glow=amber):  # Wide: a flat wide mouth and side pipes feeding its ends
    nozzle(b, y1=-0.6, y2=-0.7, flare=0.11)
    b.box((0.3, 0.3, 0.26), (0, 0.1, FZ), iron)
    b.box((0.56, 0.22, 0.14), (0, -0.8, FZ), iron)
    for x in (-0.2, -0.07, 0.07, 0.2):
        b.box((0.06, 0.03, 0.08), (x, -0.915, FZ), glow)
    for s in (-1, 1):
        tube(b, [(s * 0.3, 0.0, FZ + 0.05), (s * 0.34, -0.5, FZ + 0.08), (s * 0.24, -0.72, FZ)], 0.025, copper, segs=5)


def flame_B1(b):  # Wall of fire: a very wide mouth, burner wings at its ends, a fuel distributor on top
    b.box((0.9, 0.2, 0.14), (0, -0.84, FZ), iron)
    for x in (-0.38, -0.26, -0.14, 0.14, 0.26, 0.38):
        b.box((0.06, 0.03, 0.08), (x, -0.95, FZ), amber)
    for s in (-1, 1):
        b.beam((s * 0.45, -0.84, FZ), (s * 0.62, -0.98, FZ), 0.045, steel, segs=6, r2=0.07)
        b.cyl(0.05, 0.02, (s * 0.63, -0.99, FZ), amber, segs=6, axis="Y")
    b.box((0.4, 0.14, 0.1), (0, -0.4, FZ + 0.2), brass)
    for x in (-0.3, 0, 0.3):
        tube(b, [(x * 0.4, -0.45, FZ + 0.2), (x, -0.7, FZ + 0.12), (x, -0.78, FZ + 0.05)], 0.02, copper, segs=4)


def flame_B2(b):  # Blue flame: cryo bottles behind, a white-blue mouth, a lit plate under it; low and wide
    for s in (-1, 1):
        tank(b, (s * 0.2, 0.4, 0.82), 0.1, 0.34, ice, straps=steel, segs=8)
        tube(b, [(s * 0.2, 0.2, 0.82), (s * 0.12, 0.0, FZ + 0.16), (s * 0.08, -0.1, FZ + 0.1)], 0.02, copper, segs=4)
    b.box((0.6, 0.06, 0.16), (0, -0.92, FZ), ice)
    b.box((0.7, 0.4, 0.04), (0, -0.7, FZ - 0.2), iron)
    b.box((0.66, 0.36, 0.02), (0, -0.7, FZ - 0.17), ice)


def flame_barrel(b):
    b.beam((0, 0, 0), (0, -0.45, 0), 0.06, steel, segs=6)
    b.beam((0, -0.45, 0), (0, -0.6, 0), 0.06, steel, segs=6, r2=0.1)
    b.cyl(0.08, 0.02, (0, -0.605, 0), iron, segs=6, axis="Y")
    b.cyl(0.08, 0.04, (0, -0.4, 0), brass, segs=6, axis="Y")


FLA = [steel, iron, brass]
model("FlameBody", [stone, iron, brass, red], flame_body)
model("FlameDefault", FLA + [copper, amber], flame_default)
model("FlameA", FLA + [red, amber], flame_A)
model("FlameA1", FLA + [red, amber], flame_A, flame_A1)
model("FlameA2", FLA + [red, amber], flame_A, flame_A2)
model("FlameB", FLA + [copper, amber], flame_B)
model("FlameB1", FLA + [copper, amber], flame_B, flame_B1)
model("FlameB2", FLA + [copper, ice], lambda b: flame_B(b, ice), flame_B2)
model("FlameBarrel", FLA, flame_barrel)

# ====================================================================== SNIPER
# a tall stone pier on an iron tripod, the long rifle on a bearing; the barrel's axis at z = 0.95
SZ = 0.95


def sniper_body(b):
    plinth_square(b, 0.8, 0.2)
    b.box((0.46, 0.46, 0.5), (0, 0, 0.45), stone)
    b.box((0.5, 0.5, 0.05), (0, 0, 0.72), iron)
    for p in ring(0.46, 3, 0.2, math.pi / 2):
        b.beam(p, (p[0] * 0.45, p[1] * 0.45, 0.72), 0.035, iron, segs=4)
        b.box((0.1, 0.1, 0.06), p, iron)
    b.cyl(0.22, 0.06, (0, 0, 0.77), brass, segs=8)
    b.box((0.3, 0.3, 0.06), (0, 0, 0.83), iron)
    b.beam((0, 0.2, 0.74), (0, 0.42, 0.22), 0.035, steel, segs=6)             # hydraulic spade
    b.box((0.2, 0.08, 0.06), (0, 0.44, 0.2), iron)


def rifle(b, length=2.0, r=0.045, scope=0.5):
    b.box((0.28, 0.6, 0.26), (0, 0.1, SZ), iron)                               # receiver
    b.box((0.22, 0.3, 0.2), (0, 0.5, SZ - 0.04), iron)                         # stock
    b.box((0.24, 0.05, 0.22), (0, 0.38, SZ - 0.04), brass)
    barrel(b, 0, SZ, -0.2, -length, r, segs=8, bands=(-length * 0.5, -length * 0.8))
    b.cyl(0.07, scope, (0, -0.1, SZ + 0.2), iron, segs=8, axis="Y")            # scope
    b.cyl(0.075, 0.03, (0, -0.1 - scope / 2, SZ + 0.2), glass, segs=8, axis="Y")
    for y in (-0.25, 0.05):
        b.box((0.08, 0.06, 0.1), (0, y, SZ + 0.13), brass)
    b.box((0.08, 0.14, 0.04), (0.18, 0.1, SZ + 0.04), steel, rot=(0, 0.5, 0))  # bolt


def sniper_default(b):
    rifle(b)


def sniper_A(b, glow=amber):  # Armour-breaker: a huge muzzle brake, a longer scope
    rifle(b, scope=0.7)
    b.box((0.18, 0.28, 0.18), (0, -2.0, SZ), iron)
    for k in range(3):
        b.box((0.24, 0.04, 0.1), (0, -1.9 - k * 0.08, SZ), steel)
    b.box((0.1, 0.06, 0.04), (0, -1.2, SZ + 0.06), glow)


def sniper_A1(b):  # Boss slayer: an extra-long heavy barrel, a giant brake, an oversized scope
    b.beam((0, -0.3, SZ), (0, -2.5, SZ), 0.065, steel, segs=8)
    b.cyl(0.085, 0.05, (0, -1.6, SZ), brass, segs=8, axis="Y")
    b.box((0.26, 0.34, 0.26), (0, -2.55, SZ), iron)
    for k in range(4):
        b.box((0.32, 0.04, 0.14), (0, -2.42 - k * 0.08, SZ), steel)
    b.cyl(0.1, 0.8, (0, -0.2, SZ + 0.24), iron, segs=8, axis="Y")
    b.cyl(0.105, 0.03, (0, -0.6, SZ + 0.24), glass, segs=8, axis="Y")


def sniper_A2(b):  # Piercing shot: rails along the barrel, a piercing tip, a blue sighting coil
    for s in (-1, 1):
        b.box((0.03, 1.6, 0.05), (s * 0.08, -1.3, SZ), steel)
    b.beam((0, -2.0, SZ), (0, -2.4, SZ), 0.07, steel, segs=6, r2=0.0)
    coil(b, (0, -0.55, SZ), (0, 1, 0), 0.12, 0.03, cyan, n=10)
    b.cyl(0.08, 0.03, (0, -0.46, SZ + 0.2), cyan, segs=8, axis="Y")


def sniper_B(b):  # Rapid: a shorter barrel, a side magazine, a bipod
    rifle(b, length=1.5, scope=0.4)
    b.box((0.1, 0.3, 0.2), (0.19, 0.0, SZ), iron)
    b.box((0.11, 0.26, 0.04), (0.19, 0.0, SZ + 0.04), cyan)
    for s in (-1, 1):
        b.beam((0, -1.1, SZ - 0.06), (s * 0.28, -1.1, SZ - 0.5), 0.025, steel, segs=4)
        b.box((0.06, 0.06, 0.04), (s * 0.28, -1.1, SZ - 0.52), iron)


def sniper_B1(b):  # Machine sniper: a drum magazine, a second bipod, a vented high-rate receiver
    b.cyl(0.18, 0.12, (0.3, 0.05, SZ), iron, segs=10, axis="X")
    b.cyl(0.07, 0.14, (0.3, 0.05, SZ), brass, segs=8, axis="X")
    for s in (-1, 1):
        b.beam((0, -0.7, SZ - 0.06), (s * 0.28, -0.7, SZ - 0.5), 0.025, steel, segs=4)
    for k in range(4):
        b.box((0.3, 0.03, 0.06), (0, -0.05 + k * 0.08, SZ + 0.14), steel)
    b.box((0.3, 0.3, 0.02), (0, 0.07, SZ + 0.11), cyan)


def sniper_B2(b):  # Mark: a giant observation scope, a marker lamp, a little radar on top
    b.cyl(0.13, 0.7, (0, -0.05, SZ + 0.32), iron, segs=8, axis="Y")
    b.cyl(0.14, 0.04, (0, -0.4, SZ + 0.32), glass, segs=8, axis="Y")
    b.box((0.1, 0.1, 0.16), (0, 0.1, SZ + 0.18), brass)
    b.box((0.06, 0.06, 0.1), (-0.2, 0.0, SZ + 0.18), iron)
    b.ico(0.06, (-0.2, 0.0, SZ + 0.26), cyan, subdiv=0)
    b.cyl(0.02, 0.2, (0, 0.2, SZ + 0.55), steel, segs=4)
    b.cone(0.17, 0.03, 0.08, (0, 0.2, SZ + 0.66), steel, segs=8, axis="-Y")


def sniper_barrel(b):
    b.cyl(0.06, 0.08, (0, -0.04, 0), iron, segs=6, axis="Y")
    barrel(b, 0, 0, -0.08, -1.25, 0.035, segs=6, bands=(-0.7,))
    b.cyl(0.05, 0.1, (0, -1.2, 0), iron, segs=6, axis="Y")


SNI = [iron, steel, brass, glass]
model("SniperBody", [stone, iron, brass, steel], sniper_body)
model("SniperDefault", SNI, sniper_default)
model("SniperA", SNI + [amber], sniper_A)
model("SniperA1", SNI + [amber], sniper_A, sniper_A1)
model("SniperA2", SNI + [cyan], lambda b: sniper_A(b, cyan), sniper_A2)
model("SniperB", SNI + [cyan], sniper_B)
model("SniperB1", SNI + [cyan], sniper_B, sniper_B1)
model("SniperB2", SNI + [cyan], sniper_B, sniper_B2)
model("SniperBarrel", [iron, steel, brass], sniper_barrel)

# ====================================================================== BUBBLE
# a round stone pedestal, the generator drum and its pressure ring, the glass dome on top; the collar at z = 0.7
BZ = 0.7


def bubble_body(b):
    plinth_round(b, 0.52, 0.3)
    torus(b, (0, 0, 0.36), (0, 0, 1), 0.42, 0.04, brass, segs=12, tsegs=5)
    b.cyl(0.3, 0.3, (0, 0, 0.5), iron, segs=10)
    for p in ring(0.36, 4, 0.5):
        b.beam((p[0] * 0.8, p[1] * 0.8, 0.5), p, 0.04, brass, segs=6)
        b.cyl(0.06, 0.03, (p[0] * 1.08, p[1] * 1.08, 0.5), steel, segs=6, axis="X" if abs(p[0]) > abs(p[1]) else "Y")


def dome(b, r=0.3, z=None, m=glass):
    z = BZ + 0.25 if z is None else z
    b.cyl(0.28, 0.1, (0, 0, BZ), iron, segs=10)
    b.ico(r, (0, 0, z), m, subdiv=2)
    torus(b, (0, 0, z), (0, 0, 1), r, 0.03, brass, segs=12, tsegs=4)
    b.cyl(0.02, 0.4, (0, 0, z + r + 0.1), steel, segs=4)
    b.ico(0.05, (0, 0, z + r + 0.32), cyan, subdiv=0)


def bubble_default(b):
    dome(b)


def bubble_A(b):  # Expanse: a wider dome held by four arms
    dome(b, r=0.38)
    for p in ring(0.36, 4, BZ, math.pi / 4):
        b.beam(p, (p[0] * 0.75, p[1] * 0.75, BZ + 0.55), 0.03, steel, segs=4)
        b.box((0.08, 0.08, 0.06), (p[0] * 0.72, p[1] * 0.72, BZ + 0.57), brass)


def bubble_A1(b):  # Dome: a huge dome, braces, a pressure ring on its crown
    b.ico(0.5, (0, 0, BZ + 0.3), glass, subdiv=2)
    for p in ring(0.44, 4, BZ, math.pi / 4):
        b.beam(p, (p[0] * 0.9, p[1] * 0.9, BZ + 0.7), 0.035, steel, segs=4)
    torus(b, (0, 0, BZ + 0.76), (0, 0, 1), 0.18, 0.035, brass, segs=10, tsegs=4)


def bubble_A2(b):  # Ward: three rings of light, a bright core, runes round the collar
    for k, R in enumerate((0.45, 0.56, 0.67)):
        torus(b, (0, 0, BZ + 0.1 + k * 0.1), (0, 0, 1), R, 0.025, cyan, segs=14, tsegs=4)
    b.ico(0.13, (0, 0, BZ + 0.86), cyan, subdiv=1)
    for p in ring(0.32, 6, BZ + 0.02):
        b.box((0.06, 0.06, 0.08), p, cyan, rot=(0, 0, math.atan2(p[1], p[0])))


def bubble_B(b):  # Hardened: armour plates round the dome and two pressure rings
    dome(b)
    for p in ring(0.33, 4, BZ + 0.25):
        b.box((0.3, 0.08, 0.34), p, iron, rot=(0, 0, math.atan2(p[1], p[0]) + math.pi / 2))
    for z in (BZ + 0.12, BZ + 0.38):
        torus(b, (0, 0, z), (0, 0, 1), 0.36, 0.03, brass, segs=12, tsegs=4)


def bubble_B1(b):  # Fortress: an armoured housing with a front window on a heavy base
    b.cyl(0.38, 0.5, (0, 0, BZ + 0.27), iron, segs=8)
    b.cyl(0.4, 0.06, (0, 0, BZ + 0.54), brass, segs=8)
    b.box((0.18, 0.06, 0.14), (0, -0.38, BZ + 0.28), glass)
    b.cyl(0.44, 0.08, (0, 0, BZ + 0.02), iron, segs=8)
    studs(b, 0.4, 8, BZ + 0.45, size=0.05)


def bubble_B2(b):  # Aegis: six shield plates round the dome, a crystal tip, two stabilizers
    for p in ring(0.42, 6, BZ + 0.3):
        b.box((0.22, 0.04, 0.28), p, steel, rot=(0, 0.3, math.atan2(p[1], p[0]) + math.pi / 2))
        b.box((0.24, 0.02, 0.04), (p[0], p[1], BZ + 0.46), cyan, rot=(0, 0, math.atan2(p[1], p[0]) + math.pi / 2))
    b.beam((0, 0, BZ + 0.56), (0, 0, BZ + 0.95), 0.07, cyan, segs=4, r2=0.0)
    for s in (-1, 1):
        fin(b, [(s * 0.3, 0.1, BZ), (s * 0.6, 0.0, BZ + 0.3), (s * 0.3, -0.1, BZ + 0.4)], (0, 1, 0), 0.03, steel)


def bubble_barrel(b):
    b.cyl(0.06, 0.05, (0, 0, 0.02), brass, segs=6)
    b.cyl(0.02, 0.3, (0, 0, 0.18), steel, segs=4)
    b.ico(0.07, (0, 0, 0.38), glass, subdiv=1)


BUB = [glass, iron, brass, steel, cyan]
model("BubbleBody", [stone, iron, brass, steel], bubble_body)
model("BubbleDefault", BUB, bubble_default)
model("BubbleA", BUB, bubble_A)
model("BubbleA1", BUB, bubble_A, bubble_A1)
model("BubbleA2", BUB, bubble_A, bubble_A2)
model("BubbleB", BUB, bubble_B)
model("BubbleB1", BUB, bubble_B, bubble_B1)
model("BubbleB2", BUB, bubble_B, bubble_B2)
model("BubbleBarrel", [brass, steel, glass], bubble_barrel)

# ====================================================================== MENDER
# a stone platform, a turntable with the pillar and its green core, a toolbox at the side; the shoulders at z = 0.9
MZ = 0.9


def mender_body(b):
    plinth_square(b, 0.86, 0.3)
    b.cyl(0.36, 0.1, (0, 0, 0.35), iron, segs=10)
    b.box((0.3, 0.3, 0.5), (0, 0, 0.65), iron)
    b.box((0.34, 0.34, 0.05), (0, 0, 0.62), brass)
    crate(b, (0.46, 0.2, 0.41), (0.34, 0.24, 0.22))
    b.box((0.08, 0.08, 0.06), (0.46, 0.2, 0.55), brass)
    b.ico(0.12, (0, 0, 0.96), green, subdiv=1)
    for p in ring(0.14, 4, 0.96, math.pi / 4):
        b.beam((p[0], p[1], 0.86), (p[0], p[1], 1.08), 0.015, brass, segs=4)


def arm(b, s, tool="wrench"):
    sh = (s * 0.17, -0.05, MZ)
    el = (s * 0.32, -0.42, MZ + 0.28)
    wr = (s * 0.14, -0.72, MZ + 0.02)
    b.ico(0.07, sh, steel, subdiv=0)
    b.beam(sh, el, 0.04, steel, segs=6)
    b.ico(0.06, el, brass, subdiv=0)
    b.beam(el, wr, 0.035, steel, segs=6)
    b.ico(0.05, wr, brass, subdiv=0)
    if tool == "wrench":
        b.box((0.08, 0.16, 0.05), (wr[0], wr[1] - 0.1, wr[2]), iron)
        for k in (-1, 1):
            b.box((0.03, 0.08, 0.05), (wr[0] + k * 0.045, wr[1] - 0.22, wr[2]), iron)
    else:  # welder
        b.beam(wr, (wr[0], wr[1] - 0.18, wr[2]), 0.03, brass, segs=6, r2=0.012)
        b.ico(0.03, (wr[0], wr[1] - 0.2, wr[2]), amber, subdiv=0)


def mender_default(b):
    arm(b, 1)
    b.box((0.16, 0.16, 0.08), (0, 0.1, MZ + 0.12), iron)
    b.ico(0.05, (0, 0.1, MZ + 0.2), green, subdiv=0)


def mender_A(b):  # Wide: two arms and a wide beam dish in front
    arm(b, 1)
    arm(b, -1)
    b.cone(0.22, 0.1, 0.16, (0, -0.42, MZ + 0.1), brass, segs=10, axis="-Y")
    b.cyl(0.12, 0.02, (0, -0.51, MZ + 0.1), green, segs=10, axis="Y")
    b.box((0.16, 0.16, 0.08), (0, 0.1, MZ + 0.12), iron)


def mender_A1(b):  # Grand mender: welders on both arms, a tool carousel at the waist
    for s in (-1, 1):
        wr = (s * 0.14, -0.72, MZ + 0.02)
        b.beam(wr, (wr[0], wr[1] - 0.18, wr[2]), 0.03, brass, segs=6, r2=0.012)
        b.ico(0.035, (wr[0], wr[1] - 0.2, wr[2]), amber, subdiv=0)
    torus(b, (0, 0, MZ - 0.32), (0, 0, 1), 0.3, 0.025, brass, segs=12, tsegs=4)
    for p in ring(0.3, 4, MZ - 0.4, math.pi / 4):
        b.box((0.07, 0.07, 0.12), p, iron)
        b.box((0.03, 0.03, 0.06), (p[0], p[1], MZ - 0.31), steel)


def mender_A2(b):  # Core mender: a big chest core and a healing lens in front of it
    b.box((0.44, 0.3, 0.4), (0, -0.2, MZ - 0.05), steel)
    b.ico(0.2, (0, -0.3, MZ - 0.05), green, subdiv=1)
    b.cyl(0.03, 0.2, (0, -0.52, MZ - 0.05), brass, segs=6, axis="Y")
    b.cyl(0.15, 0.04, (0, -0.64, MZ - 0.05), glass, segs=10, axis="Y")
    torus(b, (0, -0.64, MZ - 0.05), (0, 1, 0), 0.15, 0.02, brass, segs=10, tsegs=4)


def mender_B(b, pipe=copper):  # Surge: a green tank beside the pillar and injector pipes to the core
    arm(b, 1)
    tank(b, (-0.38, 0.1, MZ - 0.05), 0.13, 0.36, green, axis="Z", segs=8)
    for k in range(3):
        y = -0.1 + k * 0.1
        tube(b, [(-0.3, y, MZ + 0.05 + k * 0.04), (-0.18, y, MZ + 0.2), (0.0, y * 0.5, MZ + 0.12)], 0.018, pipe, segs=4)
    b.box((0.16, 0.16, 0.08), (0, 0.1, MZ + 0.12), iron)


def mender_B1(b):  # Overheal: a tall tank, three injector needles forward, an overflow valve on top
    b.cyl(0.13, 0.4, (-0.38, 0.1, MZ + 0.33), green, segs=8)
    b.cyl(0.14, 0.04, (-0.38, 0.1, MZ + 0.15), brass, segs=8)
    b.cyl(0.08, 0.08, (-0.38, 0.1, MZ + 0.57), brass, segs=8)
    torus(b, (-0.38, 0.1, MZ + 0.64), (0, 0, 1), 0.07, 0.015, steel, segs=8, tsegs=4)
    for x in (-0.12, 0, 0.12):
        b.cyl(0.03, 0.2, (x, -0.3, MZ - 0.2), steel, segs=6, axis="Y")
        b.beam((x, -0.4, MZ - 0.2), (x, -0.62, MZ - 0.2), 0.012, brass, segs=4)
    b.box((0.4, 0.12, 0.08), (0, -0.2, MZ - 0.2), iron)


def mender_B2(b):  # Cleanse: white filter drums, a halo above, two spray arms
    for y in (-0.12, 0.14):
        b.cyl(0.09, 0.3, (0.38, y, MZ), white, segs=8)
        b.cyl(0.1, 0.03, (0.38, y, MZ + 0.14), brass, segs=8)
    torus(b, (0, 0, MZ + 0.5), (0, 0, 1), 0.28, 0.03, white, segs=14, tsegs=4)
    for s in (-1, 1):
        b.beam((s * 0.1, -0.1, MZ + 0.2), (s * 0.4, -0.5, MZ + 0.3), 0.025, steel, segs=4)
        b.cone(0.06, 0.02, 0.12, (s * 0.44, -0.56, MZ + 0.3), white, segs=6, axis="-Y")


def mender_barrel(b):
    b.cyl(0.06, 0.05, (0, 0, 0.02), iron, segs=6)
    b.beam((0, 0, 0.05), (0, -0.2, 0.35), 0.03, steel, segs=4)
    b.box((0.06, 0.1, 0.04), (0, -0.25, 0.37), brass)
    b.ico(0.03, (0, -0.3, 0.38), green, subdiv=0)


MEN = [iron, steel, brass, green]
model("MenderBody", [stone, iron, wood, brass, green], mender_body)
model("MenderDefault", MEN, mender_default)
model("MenderA", MEN, mender_A)
model("MenderA1", MEN + [amber], mender_A, mender_A1)
model("MenderA2", MEN + [glass], mender_A, mender_A2)
model("MenderB", MEN + [copper], mender_B)
model("MenderB1", MEN + [copper], mender_B, mender_B1)
model("MenderB2", MEN + [white], lambda b: mender_B(b, brass), mender_B2)
model("MenderBarrel", MEN, mender_barrel)


export(list(B.values()))
