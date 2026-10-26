# The helpers' idle props (TODO-3D-MODELS.md §1): the mug each villager holds in the coffee circle, and the seagulls' nest
# of twigs with eggs in it. Built into fortress_models.blend and exported as OBJ.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_helper_props.py
# (tools/helper_idle_preview.py shows them in place).
# CoffeeCup: origin at the middle of its bottom, 8 cm across, 9 cm tall (a full-size person's mug; Person scales it with the
# helper), the handle toward the game's +x: held in the right hand by the handle (Person.Pose places it).
# GullNest: origin at the middle of its flat bottom, which sits on a pile cap, the lighthouse's gallery or the smithy's roof
# (FortressIsland.GullNestSpots); 46 cm across, the bowl's bottom 6 cm up, where a gull sits (Helpers.NestSeat).
# Blender axes: Z up, -Y the game's +Z, +X the game's -X.
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmesh
import bpy
from mathutils import Matrix, Vector
from bkit import TAU, Builder, export, loft, mat, tube

rng = random.Random(23)
body = mat("MugBody", (0.88, 0.84, 0.74), force=True)    # enamel; each helper's mug is repainted (Person.HoldCup)
rim = mat("MugRim", (0.16, 0.2, 0.3), force=True)        # the dark enamel rim
coffee = mat("MugCoffee", (0.3, 0.17, 0.09), force=True)
twig = mat("NestTwig", (0.42, 0.31, 0.19), force=True)
twig2 = mat("NestTwigDark", (0.3, 0.21, 0.13), force=True)
straw = mat("NestStraw", (0.74, 0.64, 0.4), force=True)
egg = mat("GullEgg", (0.68, 0.64, 0.48), force=True)     # a gull's egg: olive buff with dark speckles
speck = mat("GullEggSpot", (0.24, 0.19, 0.14), force=True)


def ring(r, z, n, jitter=0.0, zj=0.0, phase=0.0):
    return [Vector((r * (1 + rng.uniform(-jitter, jitter)) * math.cos(phase + k * TAU / n),
                    r * (1 + rng.uniform(-jitter, jitter)) * math.sin(phase + k * TAU / n), z + rng.uniform(-zj, zj))) for k in range(n)]


def blob(b, at, size, yaw, m, subdiv=1):
    # An icosphere stretched to `size` (x, y, z) and turned about Z: an egg lying in the bowl, a speck on it.
    mtx = Matrix.Translation(Vector(at)) @ Matrix.Rotation(yaw, 4, "Z") @ Matrix.Diagonal((*size, 1.0))
    res = bmesh.ops.create_icosphere(b.bm, subdivisions=subdiv, radius=1.0, matrix=mtx)
    b._tag({f for v in res["verts"] for f in v.link_faces}, b.mats.index(m))


# ---------- the mug: one closed profile turned round (bottom, wall, the rim's band and lip, the inside down to the coffee),
# and a handle ----------
cup = Builder("CoffeeCup", [body, rim, coffee])
N = 14
prof = [(0.037, 0.0, body), (0.039, 0.006, body), (0.04, 0.081, rim), (0.0405, 0.09, rim), (0.0345, 0.09, rim), (0.0335, 0.073, coffee)]
rings = [[Vector((0, 0, 0.0))]] + [ring(r, z, N) for r, z, _ in prof] + [[Vector((0, 0, 0.073))]]
mats = [body] + [m for _, _, m in prof]
loft(cup, rings, body, mf=lambda i, j: mats[i])
# The handle: a loop out from the wall on the game's +x (Blender -x), a little below the rim to near the bottom.
tube(cup, [(-0.037, 0, 0.074), (-0.056, 0, 0.072), (-0.066, 0, 0.057), (-0.066, 0, 0.037), (-0.056, 0, 0.022), (-0.036, 0, 0.02)],
     0.0075, body, segs=6)
cup_ob = cup.finish()

# ---------- the nest: a dish of packed twigs (flat under, a thick rim), straw lining the bowl, loose twigs laid round the rim
# and poking out, three speckled eggs in the bowl ----------
nest = Builder("GullNest", [twig, twig2, straw, egg, speck])
N = 16
prof = [(0.16, 0.0, 0.0, 0.0, twig2), (0.205, 0.045, 0.05, 0.006, twig), (0.226, 0.088, 0.05, 0.01, twig), (0.205, 0.124, 0.05, 0.01, twig),
        (0.165, 0.118, 0.04, 0.008, straw), (0.11, 0.08, 0.03, 0.004, straw), (0.055, 0.064, 0.0, 0.0, straw)]
rings = [[Vector((0, 0, 0.0))]] + [ring(r, z, N, j, zj) for r, z, j, zj, _ in prof] + [[Vector((0, 0, 0.06))]]
mats = [twig2] + [m for *_, m in prof]
loft(nest, rings, twig, mf=lambda i, j: mats[i])
for k in range(20):  # twigs laid round the rim, tangent-ish, some poking out past it
    a = k * TAU / 20 + rng.uniform(-0.12, 0.12)
    r = rng.uniform(0.17, 0.2)
    z = rng.uniform(0.07, 0.12)
    c = Vector((r * math.cos(a), r * math.sin(a), z))
    t = Vector((-math.sin(a), math.cos(a), 0)) * rng.uniform(0.6, 1.0) + Vector((math.cos(a), math.sin(a), 0)) * rng.uniform(-0.4, 0.4)
    t = t.normalized() * rng.uniform(0.08, 0.13)
    t.z = rng.uniform(-0.025, 0.025)
    nest.beam(c - t, c + t, rng.uniform(0.007, 0.011), twig if k % 3 else twig2, segs=4)
for k in range(3):  # the eggs, on their sides, pointing in
    a = math.radians(30 + k * 120)
    at = (0.048 * math.cos(a), 0.048 * math.sin(a), 0.064 + 0.028)
    blob(nest, at, (0.042, 0.03, 0.029), a, egg)
    for s in range(2):
        b = a + rng.uniform(-0.6, 0.6)
        blob(nest, (at[0] + 0.03 * math.cos(b), at[1] + 0.03 * math.sin(b), at[2] + 0.012 + 0.008 * s), (0.006, 0.006, 0.005), b, speck, subdiv=0)
nest_ob = nest.finish()

export([cup_ob, nest_ob])

