# The fishing hand net (pole + hoop, with the net bag as its own object) and the little fish shown in it, built into
# fortress_models.blend and exported as OBJ.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_net_models.py
# HandNet and HandNetBag share one origin, the grip at the end of the pole, so they line up when attached at the same point.
# The pole runs toward -Y (the game's +Z), Z up; the hoop at its end is tipped 35° down so its opening faces forward and up,
# and the bag hangs from the hoop. The game scales the bag as the net fills: grow it about the hoop centre, C below.
# NetFish (~0.22 m): origin at its centre, head toward -Y.
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from mathutils import Vector
from bkit import Builder, M, export, fin, loft, mat, tube

TAU = 2 * math.pi
UP = Vector((0, 0, 1))
X = Vector((1, 0, 0))

wood, dark, brass, steel, rope, pearl, black, fishblue = (
    M(n) for n in ("Wood", "DarkWood", "Brass", "Steel", "Rope", "Pearl", "BombBlack", "FishBlue"))
silver = mat("WpnSilverFish", (0.62, 0.72, 0.84), force=True)
out = []


# ---------- shape helpers, copied from make_weapon_models.py (importing it would rebuild the weapons) ----------

def yring(y, rx, rz, n, cx=0.0, cz=0.0, a0=0.0):
    # An ellipse around the y axis: vertex 0 on the +x side, then over the top.
    return [(cx + rx * math.cos(a0 + i * TAU / n), y, cz + rz * math.sin(a0 + i * TAU / n)) for i in range(n)]


# ---------- the hoop, shared by HandNet and HandNetBag so the bag's top ring sits exactly in the hoop ----------

TIP, R, DROOP = 0.79, 0.21, math.radians(35)          # pole-to-hoop joint (in front of the grip), hoop radius, hoop tipped down
F = Vector((0, -math.cos(DROOP), -math.sin(DROOP)))    # in the hoop's plane, from the joint across to the far rim
N = Vector((0, -math.sin(DROOP), math.cos(DROOP)))     # the way the opening faces: forward and up
C = Vector((0, -TIP, 0)) + F * R                       # hoop centre


def on_hoop(a, r=R, c=C):
    # A point on a circle parallel to the hoop: a = 0 on the +x side, -90° at the joint.
    return c + (X * math.cos(a) + F * math.sin(a)) * r


HOOP = [on_hoop(k * TAU / 24) for k in range(24)]

# ---------- HandNet: wooden pole, dark grip with brass end cap and collar, brass ferrule, a thick steel hoop ----------
g = Builder("HandNet", [wood, dark, brass, steel])
g.cyl(0.015, 0.84, (0, -0.33, 0), wood, segs=8, axis="Y")               # pole, from the end cap to inside the ferrule
g.cyl(0.021, 0.17, (0, 0, 0), dark, segs=10, axis="Y")                   # grip
for y in (0.093, -0.09):
    g.cyl(0.023, 0.014, (0, y, 0), brass, segs=10, axis="Y")             # end cap, collar
g.cyl(0.019, 0.09, (0, -TIP + 0.04, 0), brass, segs=10, axis="Y")        # ferrule, the hoop runs through its tip
tube(g, HOOP, 0.014, steel, segs=6, closed=True)                          # hoop
out.append(g.finish())

# ---------- HandNetBag: a diamond-mesh bag, two families of cords spiralling down from the hoop and crossing at the knots ----------
g = Builder("HandNetBag", [rope])
DEPTH, MESH = 0.4, 12
D = (-N - UP).normalized()                       # hangs back from the opening and sags under its weight
BOTTOM = C + D * DEPTH
RINGS = [(t, R * math.sin(math.pi / 2 * (1 - t)) ** 0.7) for t in (0.0, 0.15, 0.3, 0.45, 0.6, 0.74, 0.87)]  # (depth fraction, radius)
for turn in (1, -1):
    for j in range(MESH):
        pts = [on_hoop((j + turn * i / 2) * TAU / MESH, r, C + D * (DEPTH * t)) for i, (t, r) in enumerate(RINGS)]
        tube(g, pts + [BOTTOM], [0.0045] * len(pts) + [0.0], rope, segs=3)
tube(g, HOOP, 0.006, rope, segs=4, closed=True)  # top cord, hidden inside the hoop
g.ico(0.02, BOTTOM, rope, subdiv=2)              # knot at the bottom
out.append(g.finish())

# ---------- NetFish: little silvery-blue fish, lighter belly, blue fins and a forked tail ----------
g = Builder("NetFish", [silver, pearl, fishblue, black])
BODY = ((-0.104, 0.009, 0.012, -0.002), (-0.09, 0.016, 0.024, 0.0), (-0.068, 0.022, 0.035, 0.002), (-0.035, 0.025, 0.041, 0.003),
        (0.0, 0.024, 0.039, 0.002), (0.03, 0.019, 0.03, 0.001), (0.055, 0.011, 0.017, 0.0), (0.072, 0.006, 0.009, 0.0))  # (y, half width, half height, centre z)
loft(g, [[(0, -0.112, -0.003)]] + [yring(y, rx, rz, 10, cz=cz) for y, rx, rz, cz in BODY], silver,
     mf=lambda i, j: pearl if math.sin((j + 0.5) * TAU / 10) < -0.3 else silver)
fin(g, [(0, y, z) for y, z in ((0.064, 0.006), (0.088, 0.032), (0.112, 0.047), (0.1, 0.012), (0.095, 0.0), (0.1, -0.012), (0.112, -0.047),
                               (0.088, -0.032), (0.064, -0.006))], X, [0.008, 0.005, 0.003, 0.004, 0.005, 0.004, 0.003, 0.005, 0.008], fishblue)  # tail
fin(g, [(0, y, z) for y, z in ((-0.032, 0.036), (-0.01, 0.064), (0.02, 0.05), (0.028, 0.028))], X, [0.006, 0.003, 0.003, 0.006], fishblue)  # dorsal
fin(g, [(0, y, z) for y, z in ((0.018, -0.03), (0.038, -0.046), (0.047, -0.038), (0.047, -0.017))], X, [0.005, 0.003, 0.003, 0.005], fishblue)  # anal
for s in (-1, 1):
    a, b, c = Vector((s * 0.018, -0.062, -0.014)), Vector((s * 0.019, -0.048, -0.019)), Vector((s * 0.034, -0.034, -0.026))
    fin(g, [a, b, c], (b - a).cross(c - a), [0.005, 0.005, 0.002], fishblue)  # pectorals, swept back and down
    g.ico(0.0085, (s * 0.0165, -0.083, 0.008), pearl, subdiv=1)              # eyes
    g.ico(0.005, (s * 0.022, -0.086, 0.009), black, subdiv=1)
lo = Vector([min(v.co[k] for v in g.bm.verts) for k in range(3)])
hi = Vector([max(v.co[k] for v in g.bm.verts) for k in range(3)])
for v in g.bm.verts:
    v.co -= (lo + hi) / 2
out.append(g.finish())

export(out)
