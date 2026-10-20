# What the weapons throw: the poison gas canister and the grappling claw, built into fortress_models.blend and exported as OBJ.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_fx_models.py
# Both: origin in the middle, the front toward -Y (the game's +Z), Z up.
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Vector
from bkit import TAU, Builder, M, export, loft, mat, torus, tube

iron, steel, brass, black, red = (M(n) for n in ("Iron", "Steel", "Brass", "BombBlack", "FuelRed"))
can = mat("FxCanGreen", (0.32, 0.52, 0.26), force=True)
hazard = mat("FxHazard", (1.0, 0.8, 0.12), force=True)
toxic = mat("WpnToxicGlow", (0.6, 1.0, 0.25), (0.5, 1.0, 0.1))
out = []


def ring_z(z, r, n=14):
    return [(r * math.cos(k * TAU / n), r * math.sin(k * TAU / n), z) for k in range(n)]


# ---------- GasCanister: a stubby green cylinder, hazard bands, a domed top with a red valve and a glowing window ----------
g = Builder("GasCanister", [can, hazard, black, steel, red, toxic])
loft(g, [ring_z(z, r) for z, r in ((-0.15, 0.07), (-0.14, 0.085), (0.1, 0.085), (0.13, 0.07), (0.15, 0.03))], can,
     caps=(steel, can))
for z0 in (-0.1, 0.055):                                   # hazard bands: yellow with black slashes
    loft(g, [ring_z(z, 0.088, 20) for z in (z0, z0 + 0.035)], hazard, mf=lambda i, j: black if j % 4 < 2 else hazard)
g.cyl(0.03, 0.05, (0, 0, 0.17), steel, segs=10)             # neck
torus(g, (0, 0, 0.205), (0, 0, 1), 0.04, 0.009, red, segs=14, tsegs=4)   # valve wheel
for k in range(3):
    a = k * TAU / 3
    g.beam((0, 0, 0.205), (0.04 * math.cos(a), 0.04 * math.sin(a), 0.205), 0.006, red, segs=4)
g.cyl(0.012, 0.04, (0, -0.045, 0.19), steel, segs=8, axis="Y")   # nozzle
g.box((0.05, 0.012, 0.09), (0, -0.088, -0.02), toxic)       # a window onto the green gas
out.append(g.finish())

# ---------- GrappleClaw: a steel shank, a brass collar, three hooked prongs forward, an eye for the rope behind ----------
c = Builder("GrappleClaw", [steel, iron, brass])
c.cyl(0.022, 0.24, (0, 0.02, 0), steel, segs=10, axis="Y")
c.cyl(0.032, 0.04, (0, -0.06, 0), brass, segs=12, axis="Y")
torus(c, (0, 0.17, 0), (1, 0, 0), 0.035, 0.009, iron, segs=12, tsegs=4)   # the eye
for k in range(3):
    a = k * TAU / 3 + math.pi / 2
    side = Vector((math.cos(a), 0, math.sin(a)))
    pts = [Vector((0, -0.08, 0)) + side * 0.02, Vector((0, -0.14, 0)) + side * 0.06, Vector((0, -0.19, 0)) + side * 0.075,
           Vector((0, -0.215, 0)) + side * 0.06, Vector((0, -0.2, 0)) + side * 0.035, Vector((0, -0.17, 0)) + side * 0.03]
    tube(c, pts, [0.016, 0.014, 0.012, 0.01, 0.008, 0.0], steel, segs=6)
out.append(c.finish())

export(out)
