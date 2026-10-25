# The turret's foot, with a proper lamp shelf for the 17 lamps that light up level by level (Owen: the base design that
# gains more lit lamps should be better): octagonal stone plinth, iron band, brass lamp rail, wooden feet and brackets.
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_turret_base.py
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bkit import Builder, M, export, mat, torus

stone, iron, brass, dark, metal, steel = (M(n) for n in ("Stone", "Iron", "Brass", "DarkWood", "Metal", "Steel"))
out, TAU = [], 2 * math.pi


def ring(r, n, z, phase=0.0):
    return [(r * math.cos(phase + i * TAU / n), r * math.sin(phase + i * TAU / n), z) for i in range(n)]


b = Builder("TurretBase", [stone, iron, brass, dark, metal, steel])
# wide lamp shelf, then the stepped plinth
b.cyl(1.14, 0.06, (0, 0, 0.03), iron, segs=20)
b.cyl(1.08, 0.035, (0, 0, 0.075), brass, segs=20)
torus(b, (0, 0, 0.1), (0, 0, 1), 1.06, 0.026, brass, segs=28, tsegs=5)
for p in ring(1.02, 17, 0.04):                       # a brass socket pad for each lamp (the bulbs sit on them)
    b.cyl(0.105, 0.035, (p[0], p[1], 0.065), brass, segs=22)
    b.box((0.025, 0.025, 0.05), (p[0] * 1.12, p[1] * 1.12, 0.055), iron)
b.cyl(0.72, 0.2, (0, 0, 0.15), stone, segs=8)
b.cyl(0.78, 0.07, (0, 0, 0.27), iron, segs=8)
for p in ring(0.78, 8, 0.27):
    b.box((0.075, 0.075, 0.075), p, brass)
b.cyl(0.64, 0.18, (0, 0, 0.38), stone, segs=8)
b.cyl(0.58, 0.6, (0, 0, 0.68), stone, segs=8)
b.cyl(0.62, 0.05, (0, 0, 0.55), brass, segs=22)       # trim band
b.cyl(0.6, 0.08, (0, 0, 0.75), iron, segs=22)
b.cyl(0.56, 0.08, (0, 0, 0.8), metal, segs=22)
for p in ring(0.58, 8, 0.55):
    b.box((0.06, 0.06, 0.06), p, brass)
for s in (-1, 1):                                      # four iron brackets down the sides
    for t in (-1, 1):
        b.box((0.06, 0.24, 0.34), (s * 0.4, t * 0.4, 0.26), steel)
        b.box((0.1, 0.1, 0.06), (s * 0.4, t * 0.5, 0.09), brass)
for p in ring(0.9, 4, 0.09, math.pi / 4):              # wooden feet out at the corners
    b.box((0.36, 0.14, 0.14), p, dark, rot=(0, 0, math.atan2(p[1], p[0])))
out.append(b.finish())
export(out)
