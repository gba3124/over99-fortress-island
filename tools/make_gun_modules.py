# Gun turret, modular art: one shared body + seven barrel/branch modules (default, A, A1, A2, B, B1, B2).
# Modules are authored in the body's head space (the body is the turret's "Head" model); BuildSystem swaps the module as the
# branch changes. Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_gun_modules.py
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bkit import Builder, M, export, mat, tube, torus

dark = mat("V3Dark", (0.045, 0.055, 0.065))
iron = mat("V3Iron", (0.12, 0.14, 0.17))
steel = mat("V3Steel", (0.52, 0.57, 0.62))
brass = mat("V3Brass", (0.80, 0.55, 0.17), (0.22, 0.10, 0.02))
teal = mat("V3Teal", (0.06, 0.28, 0.32), (0.02, 0.10, 0.12))
rubber = mat("V3Rubber", (0.025, 0.025, 0.03))
amber = mat("V3Amber", (1.0, 0.62, 0.16), (0.85, 0.4, 0.03))
ice = mat("V3Ice", (0.55, 0.9, 1.0), (0.2, 0.7, 0.95))
red = mat("V3Red", (0.56, 0.12, 0.07), (0.18, 0.02, 0.01))
out, TAU = [], 2 * math.pi


def ring(r, n, z, phase=0.0):
    return [(r * math.cos(phase + i * TAU / n), r * math.sin(phase + i * TAU / n), z) for i in range(n)]


def studs(b, r, n, z, m=brass, size=0.05):
    for p in ring(r, n, z): b.box((size, size, size), p, m)


def barrels(b, count, r, y0, y1, thick, mat_=steel, muzzle=brass):
    for i in range(count):
        a = i * TAU / count
        x, z = r * math.cos(a), 0.46 + r * math.sin(a)
        b.beam((x, y0, z), (x, y1, z), thick, mat_, segs=10)
        b.cyl(thick * 1.25, 0.05, (x, y1 + 0.02, z), muzzle, segs=10, axis="Y")


# shared body: yoke, drum receiver, top plate, ammo box, sight, rear counterweight; front is open for a module
body = Builder("GunBody", [iron, dark, steel, brass, teal, rubber, amber])
body.box((0.72, 0.5, 0.16), (0, 0.08, 0.1), iron)
for s in (-1, 1):
    body.box((0.1, 0.38, 0.3), (s * 0.34, 0.08, 0.3), teal)
    body.box((0.12, 0.06, 0.32), (s * 0.4, 0.08, 0.3), brass)
body.cyl(0.34, 0.58, (0, 0.0, 0.46), teal, segs=16, axis="Y")
for y in (-0.24, 0.26): body.cyl(0.36, 0.06, (0, y, 0.46), brass, segs=18, axis="Y")
body.box((0.44, 0.4, 0.08), (0, 0.02, 0.78), dark)
body.box((0.3, 0.22, 0.06), (0, 0.02, 0.84), brass)
body.cyl(0.27, 0.07, (0, -0.3, 0.46), steel, segs=16, axis="Y")      # socket collar
body.box((0.2, 0.38, 0.28), (0.44, 0.06, 0.46), dark)                # ammo box
body.box((0.21, 0.05, 0.29), (0.55, 0.06, 0.46), brass)
tube(body, [(0.34, 0.06, 0.56), (0.2, 0.08, 0.66), (0.02, 0.1, 0.66)], 0.05, brass, segs=10)
body.box((0.12, 0.16, 0.1), (0, 0.02, 0.86), dark)
body.box((0.06, 0.08, 0.08), (0, -0.06, 0.9), amber)
for s in (-1, 1): body.box((0.06, 0.14, 0.18), (s * 0.24, 0.4, 0.56), rubber, rot=(0, 0, s * 0.12))
body.cyl(0.22, 0.18, (0, 0.34, 0.46), teal, segs=14, axis="Y")
body.cyl(0.23, 0.05, (0, 0.44, 0.46), brass, segs=14, axis="Y")
out.append(body.finish())

# default: one thick barrel in a hex shroud
d = Builder("GunDefault", [iron, dark, steel, brass, teal])
d.cyl(0.2, 0.16, (0, -0.36, 0.46), steel, segs=16, axis="Y")
d.cyl(0.18, 0.9, (0, -0.84, 0.46), steel, segs=16, axis="Y")
d.cyl(0.2, 0.07, (0, -1.28, 0.46), brass, segs=16, axis="Y")
d.cyl(0.1, 0.03, (0, -1.33, 0.46), dark, segs=12, axis="Y")
out.append(d.finish())

# A storm: an unmistakable heat-sink radiator: tall orange fins and glowing exhaust stacks
a = Builder("GunA", [iron, dark, steel, brass, teal, amber, red, rubber])
a.cyl(0.24, 0.18, (0, -0.36, 0.46), brass, segs=16, axis="Y")
# barrel bundle
barrels(a, 6, 0.09, -0.42, -1.12, 0.026, steel, brass)
for y in (-0.55, -0.85): a.cyl(0.16, 0.05, (0, y, 0.46), brass, segs=16, axis="Y")
# a big radiator block on top: dark housing with six tall, glowing orange fins
a.box((0.34, 0.7, 0.12), (0, -0.6, 0.82), dark)
for k in range(6):
    y = -0.9 + k * 0.115
    a.box((0.5, 0.07, 0.3), (0, y, 0.92), amber)
    a.box((0.54, 0.02, 0.28), (0, y - 0.02, 0.92), red)  # hot edge
# side exhaust stacks with glowing throats
for s in (-1, 1):
    a.cyl(0.09, 0.42, (s * 0.28, -0.62, 0.62), iron, segs=12, axis="Y")
    a.cyl(0.065, 0.05, (s * 0.28, -0.84, 0.62), amber, segs=12, axis="Y")
    for z in (0.48, 0.56, 0.64):
        a.box((0.16, 0.03, 0.03), (s * 0.28, -0.62, z), amber)
# glowing heat band round the shroud
a.cyl(0.18, 0.06, (0, -0.45, 0.46), amber, segs=16, axis="Y")
out.append(a.finish())

# A1 typhoon: a full ring of barrels and a second drum
a1 = Builder("GunA1", [iron, dark, steel, brass, teal, amber])
a1.cyl(0.25, 0.18, (0, -0.36, 0.46), brass, segs=18, axis="Y")
barrels(a1, 8, 0.12, -0.44, -1.18, 0.028, steel, brass)
a1.cyl(0.2, 0.08, (0, -0.45, 0.46), brass, segs=18, axis="Y")
a1.cyl(0.2, 0.2, (-0.3, -0.1, 0.5), teal, segs=16, axis="X")
for k in range(5): a1.box((0.26, 0.04, 0.03), (0, -0.75, 0.66 + k * 0.04), amber)
out.append(a1.finish())

# A2 hailstorm: a big crystal cluster and a frosted canister, instantly readable as ice
a2 = Builder("GunA2", [iron, dark, steel, brass, teal, ice, M("V3Glass")])
a2.cyl(0.22, 0.18, (0, -0.38, 0.46), brass, segs=16, axis="Y")
# twin barrels with icicles hanging off the shroud
for s in (-1, 1):
    a2.beam((s * 0.11, -0.4, 0.46), (s * 0.11, -1.12, 0.46), 0.045, steel, segs=10)
    a2.cyl(0.06, 0.05, (s * 0.11, -1.14, 0.46), brass, segs=10, axis="Y")
for k in range(4):
    a2.cone(0.055, 0.0, 0.26, (-0.22 + k * 0.15, -0.95, 0.3), ice, segs=6)  # icicles under the barrels
# a big snowflake plate behind the barrels
for k in range(6):
    ang = k * math.pi / 3
    a2.box((0.06, 0.5, 0.06), (0, -0.62, 0.5), ice, rot=(ang, 0, 0))
a2.cyl(0.2, 0.04, (0, -0.62, 0.5), ice, segs=16, axis="Y")
# crystal cluster on top (large, faceted, glowing)
for i, (x, y, z, r, h) in enumerate([(-0.16, -0.55, 0.82, 0.09, 0.55), (0.04, -0.62, 0.9, 0.12, 0.72), (0.2, -0.5, 0.8, 0.08, 0.45), (0.0, -0.42, 0.78, 0.07, 0.35)]):
    a2.cone(r, 0.0, h, (x, y, z), ice, segs=6)
    a2.cone(r * 0.55, 0.0, h * 0.5, (x + 0.02, y - 0.02, z + h * 0.32), M("V3Ice"), segs=6)
# frosted coolant canister on the side
a2.cyl(0.17, 0.4, (0.4, -0.18, 0.55), ice, segs=14, axis="Y")
for y in (-0.32, -0.04):
    a2.cyl(0.19, 0.05, (0.4, y, 0.55), brass, segs=14, axis="Y")
a2.ico(0.1, (0.4, -0.4, 0.66), ice, subdiv=1)
out.append(a2.finish())

# B piercing: one long reinforced barrel with a muzzle brake
b = Builder("GunB", [iron, dark, steel, brass, teal, red])
b.cyl(0.22, 0.2, (0, -0.38, 0.46), brass, segs=16, axis="Y")
b.box((0.3, 0.2, 0.24), (0, -0.48, 0.46), iron, rot=(0, 0, 0))
b.cyl(0.11, 1.45, (0, -1.15, 0.46), steel, segs=16, axis="Y")
for y in (-0.7, -1.1, -1.5): b.cyl(0.13, 0.06, (0, y, 0.46), red, segs=14, axis="Y")
b.cyl(0.17, 0.18, (0, -1.95, 0.46), iron, segs=14, axis="Y")
for k in range(4): b.box((0.32, 0.04, 0.04), (0, -1.95, 0.37 + k * 0.06), dark)
out.append(b.finish())

# B1 railgun: a long twin-rail barrel with electromagnetic coils
b1 = Builder("GunB1", [iron, dark, steel, brass, teal, amber, red])
b1.cyl(0.22, 0.2, (0, -0.38, 0.46), brass, segs=16, axis="Y")
for s in (-1, 1): b1.box((0.05, 1.7, 0.08), (s * 0.13, -1.25, 0.46), steel)
for y in (-0.65, -0.95, -1.25, -1.55):
    b1.cyl(0.17, 0.06, (0, y, 0.46), brass, segs=16, axis="Y")
    b1.box((0.36, 0.05, 0.05), (0, y, 0.62), amber)
b1.box((0.24, 0.16, 0.16), (0, -0.6, 0.66), iron)
out.append(b1.finish())

# B2 executioner: a fat short barrel and a huge brake/counterweight
b2 = Builder("GunB2", [iron, dark, steel, brass, teal, red, amber])
b2.cyl(0.26, 0.22, (0, -0.38, 0.46), brass, segs=18, axis="Y")
b2.cyl(0.2, 0.95, (0, -0.9, 0.46), steel, segs=18, axis="Y")
b2.cyl(0.28, 0.26, (0, -1.42, 0.46), iron, segs=18, axis="Y")
for s in (-1, 1): b2.box((0.08, 0.36, 0.16), (s * 0.2, -1.5, 0.46), dark)
b2.cyl(0.12, 0.04, (0, -1.56, 0.46), dark, segs=12, axis="Y")
b2.box((0.22, 0.18, 0.2), (0, 0.02, 0.64), red)
for k in range(3): b2.box((0.26, 0.04, 0.03), (0, -1.52, 0.36 + k * 0.06), amber)
out.append(b2.finish())

export(out)
