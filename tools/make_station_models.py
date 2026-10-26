# The village stations by the dock, each with a keeper standing behind it: the fish market stall and the weapon smithy,
# in the game's cabin style (tools/vkit.py: plank walls and floors, board roofs, rustic props). Built into
# fortress_models.blend and exported as OBJ.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_station_models.py
# Both: origin on the ground in the middle, the customer's side toward -Y (the game's +Z), Z up. The stall's keeper stands at
# (0, 0.4) behind the counter, the smith at (0.4, 0.55) behind the anvil; FortressIsland.cs places them, the stall's and the
# forge's lights and the smithy's anvil drop, Village.cs the rack's weapons on its pegs and the price board over them
# (keep in step). Look at them with preview/stall-smithy/render.py (every side and close-ups, from the exported OBJs); check
# what the C# relies on with preview/stall-smithy/check.py.
import math
import os
import random
import sys

import bmesh
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bkit import TAU, Builder, M, export, fin, loft, mat, torus, tube
import vkit
from vkit import Frame, MFrame, ball, beam, box, cbox, chain, cyl, lathe, turned

# The walls, floors and roofs draw on vkit.rng, the props on R: the structure stays as it was whatever the props do (the gull
# nest on the smithy's roof, FortressIsland.NestSpots, sits on one of its roof boards). Keep the structure's calls in order.
R = random.Random(5)
F = Frame((0, 0, 0))
out = []
plank, plank2, trim, iron, brass, rope, cream, canvas, stone, stone2 = (vkit.plank, vkit.plank2, vkit.trim, vkit.iron, vkit.brass,
                                                                       vkit.rope, vkit.cream, vkit.canvas, vkit.stone, vkit.stone2)

# ---------- the palette: the kit's, and the props' own, as muted ----------
fishB, fishO = M("FishBlue"), M("FishOrange")
ice = mat("StIce", (0.6, 0.72, 0.8), force=True)
ice2 = mat("StIce2", (0.78, 0.86, 0.9), force=True)                # the chunks catching the light
steel = mat("StSteel", (0.55, 0.57, 0.6), force=True)
slate = mat("StSlate", (0.16, 0.18, 0.18), force=True)
fishR = mat("StFishRed", (0.86, 0.38, 0.32), force=True)
redD = mat("StFishRedDark", (0.62, 0.25, 0.21), force=True)
mack = mat("StMackerel", (0.24, 0.43, 0.48), force=True)
band = mat("StFishBand", (0.13, 0.23, 0.28), force=True)           # a mackerel's bars
belly = mat("StBelly", (0.85, 0.86, 0.84), force=True)
bellyP = mat("StBellyPink", (0.95, 0.8, 0.7), force=True)
flat = mat("StFlounder", (0.55, 0.45, 0.32), force=True)
flatS = mat("StFlounderSpot", (0.38, 0.29, 0.2), force=True)
eyeM = mat("StEye", (0.03, 0.03, 0.035), force=True)
gill = mat("StGill", (0.42, 0.17, 0.16), force=True)               # gills and mouths
dried = mat("StDried", (0.66, 0.47, 0.27), force=True)
driedD = mat("StDriedDark", (0.46, 0.31, 0.18), force=True)
jarG = mat("StJarGreen", (0.46, 0.56, 0.42), force=True)           # pickles seen through the glass
jarA = mat("StJarAmber", (0.72, 0.5, 0.24), force=True)
floatG = mat("StFloatGlass", (0.4, 0.62, 0.58), force=True)        # an old glass net float
coal = mat("StCoal", (1.0, 0.45, 0.1), (1.0, 0.35, 0.05), force=True)
coalHot = mat("StCoalHot", (1.0, 0.78, 0.36), (1.0, 0.62, 0.2), force=True)
ember = mat("StEmber", (0.2, 0.17, 0.16), force=True)              # coal, cold
ash = mat("StAsh", (0.42, 0.39, 0.37), force=True)
soot = mat("StSoot", (0.09, 0.08, 0.08), force=True)
water = mat("StWater", (0.25, 0.42, 0.5), force=True)
waterHi = mat("StWaterHi", (0.5, 0.68, 0.76), force=True)
leather = mat("StLeather", (0.42, 0.26, 0.15), force=True)
anvilM = mat("StAnvil", (0.27, 0.28, 0.31), force=True)
hoodM = mat("StHood", (0.25, 0.25, 0.27), force=True)
bark = mat("StBark", (0.25, 0.18, 0.13), force=True)
grit = mat("StGrindstone", (0.57, 0.53, 0.45), force=True)


# ---------- small makers ----------

def post(b, x, y, h, r=0.08):
    cbox(b, F, (r * 2, r * 2, h), (x, y, h / 2), trim, c=0.016)


def lump(b, G, at, s, m, flat_=0.75):
    # A chunk (coal, ash): an icosahedron squashed and turned at random.
    mtx = G.m(at, R.uniform(0, TAU), R.uniform(0, TAU), R.uniform(0, TAU)) @ Matrix.Diagonal((s * R.uniform(0.8, 1.2), s * R.uniform(0.8, 1.2), s * flat_, 1))
    res = bmesh.ops.create_icosphere(b.bm, subdivisions=1, radius=1.0, matrix=mtx)
    b._tag({fc for v in res["verts"] for fc in v.link_faces}, b.mats.index(m))


def plate(b, G, pts_xz, y, thick, m):
    # A flat shape drawn in G's x/z, from depth y out `thick` toward -y (a sign's relief, a blade).
    fin(b, [G.p((x, y - thick / 2, z)) for x, z in pts_xz], G.m((0, 0, 0)).to_3x3() @ Vector((0, 1, 0)), thick, m)


# ---------- fish, in the game's way: a chunky eight-sided hull, a darker back over a pale belly, thin fins, a dark eye ----------
# prof: (t, half thickness, half depth) in fish lengths from the nose (t 0) to the tail's root (t 1); cols: back, belly, fins,
# the darker shade (bars, scale rows, spots), the side's middle; tail: length, half spread, notch (below 0: rounded);
# fins: (1 the back's, -1 the belly's, t0, t1, height, points); pec: (t, size); eye: (t, radius); gill: t.
FISH = {
    "mackerel": dict(prof=((0, .015, .02), (.12, .05, .075), (.38, .062, .1), (.75, .04, .055), (1, .014, .02)),
                     cols=(mack, belly, mack, band, belly), pattern="bars", tail=(.2, .15, .08),
                     fins=((1, .3, .5, .07, 3), (1, .62, .74, .035, 3), (-1, .62, .76, .035, 3)), pec=(.24, .1), eye=(.1, .036), gill=.21),
    "blue": dict(prof=((0, .016, .022), (.12, .052, .08), (.38, .064, .105), (.75, .042, .06), (1, .015, .022)),
                 cols=(fishB, belly, fishB, band, belly), pattern="bars", tail=(.2, .15, .07),
                 fins=((1, .3, .52, .07, 3), (-1, .6, .76, .04, 3)), pec=(.24, .1), eye=(.1, .036), gill=.21),
    "snapper": dict(prof=((0, .016, .03), (.12, .055, .11), (.4, .066, .15), (.78, .04, .08), (1, .015, .03)),
                    cols=(fishR, bellyP, redD, redD, fishR), pattern="scales", tail=(.2, .14, .045),
                    fins=((1, .15, .72, .09, 5), (-1, .55, .8, .065, 3)), pec=(.26, .11), eye=(.11, .04), gill=.23),
    "gold": dict(prof=((0, .018, .03), (.12, .056, .1), (.4, .066, .13), (.78, .042, .07), (1, .015, .028)),
                 cols=(fishO, cream, brass, brass, fishO), pattern="scales", tail=(.19, .15, .06),
                 fins=((1, .25, .6, .08, 4), (-1, .6, .8, .05, 3)), pec=(.25, .1), eye=(.1, .038), gill=.22),
    "flounder": dict(prof=((0, .01, .05), (.12, .03, .17), (.4, .036, .22), (.78, .026, .14), (1, .01, .04)),
                     cols=(flat, flat, rope, flatS, flat), pattern="spots", tail=(.14, .1, -.03),
                     fins=((1, .06, .95, .06, 8), (-1, .28, .95, .055, 6)), pec=None, eye=(.1, .03), eyes=((.1, 58), (.17, 30)), gill=.2),
    "dried": dict(prof=((0, .01, .03), (.12, .022, .11), (.4, .026, .14), (.78, .018, .08), (1, .01, .025)),
                  cols=(driedD, dried, driedD, driedD, dried), pattern="bars", tail=(.16, .1, .04),
                  fins=((1, .3, .6, .05, 3),), pec=None, eye=(.1, .03), gill=.21),
}
TS = (0.0, 0.06, 0.16, 0.3, 0.46, 0.62, 0.8, 1.0)


def fish(b, base, kind, L=0.32, both=False, lite=False):
    """A fish FISH[kind] L long: nose to -y, back up (+z), its +x side the one shown (both: the other's eye and gill too),
    placed by the matrix `base` (fish -> station). lite: no side fins, gill line or eye rims (fish seen from afar)."""
    k = FISH[kind]
    back, bel, finm, dark, side = k["cols"]
    T3 = base.to_3x3()
    X = T3 @ Vector((1, 0, 0))

    def at(t):
        prof = k["prof"]
        for (t0, *a), (t1, *c) in zip(prof, prof[1:]):
            if t0 <= t <= t1:
                s = (t - t0) / (t1 - t0)
                s = s * s * (3 - 2 * s)
                return [(x + (y - x) * s) * L for x, y in zip(a, c)]
        return [x * L for x in prof[-1][1:]]

    def Y(t):
        return (-0.5 + 0.8 * t) * L

    rings = []
    for t in TS:
        w, h = at(t)
        rings.append([base @ Vector((0, Y(t), 0))] if t == 0 else
                     [base @ Vector((w * math.cos((q + 0.5) * TAU / 8), Y(t), h * math.sin((q + 0.5) * TAU / 8))) for q in range(8)])
    pat = k["pattern"]

    def mf(i, j):
        a = (j + 1) % 8  # the face's middle at a x 45 degrees round from the shown side: 2 the back, 6 the belly
        if a in (5, 6, 7):
            return gill if i == 0 else bel
        if i == 0:
            return back
        if pat == "spots" and a in (0, 1, 3, 4) and R.random() < 0.3:
            return dark
        if a == 2:
            return back
        if a in (1, 3):
            return dark if (pat == "bars" and i % 2 == 0) or (pat == "scales" and i % 2 == 1 and i < 6) else back
        return dark if pat == "scales" and i % 2 == 0 and 1 < i < 6 else side

    loft(b, rings, back, mf=mf, caps=(back, back))
    # the tail, forked (or rounded), standing up from the tail's root
    y0, (w0, h0) = Y(1.0), at(1.0)
    tl, sp, notch = (v * L for v in k["tail"])
    tail = [(0, y0 - 0.01 * L, h0 * 0.9), (0, y0 + tl, sp), (0, y0 + tl - notch, 0), (0, y0 + tl, -sp), (0, y0 - 0.01 * L, -h0 * 0.9)]
    fin(b, [base @ Vector(p) for p in tail], X, [0.03 * L, 0.01 * L, 0.016 * L, 0.01 * L, 0.03 * L], finm)
    # the fins along the back and the belly: tallest at the front, swept back
    for sgn, t0, t1, hgt, n in k["fins"]:
        inner, outer = [], []
        for q in range(n):
            t = t0 + (t1 - t0) * q / (n - 1)
            z = sgn * at(t)[1] * 0.92
            inner.append((0, Y(t), z))
            s = q / (n - 1)
            lift = hgt * L * ((0.55 + 0.45 * math.sin(math.pi * s)) if n > 5 else (1.0 - 0.6 * s))
            outer.append((0, Y(t) + 0.3 * hgt * L, z + sgn * lift))
        fin(b, [base @ Vector(p) for p in inner + outer[::-1]], X, [0.016 * L] * n + [0.005 * L] * n, finm)
    sides = (1, -1) if both else (1,)
    if k.get("pec") and not lite:  # a paddle behind the gill, lying back along the side
        t, size = k["pec"]
        w, h = at(t)
        y, size = Y(t), size * L
        for sx in sides:
            x = sx * w * 0.98
            pts = [(x, y, -h * 0.08), (x, y + size * 0.3, -h * 0.02), (x, y + size, -h * 0.3), (x, y + size * 0.82, -h * 0.55)]
            fin(b, [base @ Vector(p) for p in pts], T3 @ Vector((sx, 0, 0)), 0.008 * L, finm)
    t = k["gill"]  # the gill's edge: a dark arc from the throat over the side
    w, h = at(t)
    for sx in () if lite else sides:
        pts = [(sx * w * math.cos(a) * 0.985, Y(t) + 0.016 * L * math.cos(a * 1.3), h * math.sin(a) * 0.985) for a in map(math.radians, (-62, -25, 15, 55))]
        tube(b, [base @ Vector(p) for p in pts], 0.0075 * L, gill, segs=3)
    for sx in sides:  # the eyes: a pale rim, a dark eye standing out of it
        for t, deg in k.get("eyes", ((k["eye"][0], 28),)):
            w, h = at(t)
            a = math.radians(deg)
            c = Vector((sx * w * math.cos(a) * 0.95, Y(t), h * math.sin(a) * 0.95))
            n = Vector((sx * math.cos(a) * h, 0, math.sin(a) * w)).normalized()
            E = MFrame(base @ Matrix.Translation(c) @ n.to_track_quat("Z", "Y").to_matrix().to_4x4())
            r = k["eye"][1] * L
            if not lite:
                cyl(b, E, r * 1.45, r * 1.45, 0.006 * L, (0, 0, 0.002 * L), bel, segs=8)
            cyl(b, E, r, r * 0.8, 0.012 * L, (0, 0, 0.006 * L), eyeM, segs=6)


def lying(G, at, yaw, L, w=0.065):
    # The matrix for a fish lying on its side on G's floor at `at` (x, y, floor z), its nose turned by yaw from -y.
    x, y, z = at
    return G.m((x, y, z + w * L)) @ Matrix.Rotation(yaw, 4, "Z") @ Matrix.Rotation(-math.pi / 2, 4, "Y")


# ---------- MarketStall: a little plank booth (back and side walls, a board roof with a painted sign on it) and a striped canvas
# awning sagging out over the counter, scalloped all round; a tilted tray of fish on ice with price tags, a balance scale and a
# cutting board with a knife and a cleaver on the counter; shelves of jars and tins behind, buoys, a lantern, a net, dried fish,
# crates and a barrel; oars on the side, a life ring round the back, a chalkboard out front ----------
s = Builder("MarketStall", vkit.KIT + vkit.KIT2 + [ice, ice2, fishB, fishO, fishR, redD, mack, band, belly, bellyP, flat, flatS, eyeM, gill,
                                                   dried, driedD, jarG, jarA, floatG, slate, steel, leather])
W, BACK, FRONT = 3.0, 1.2, -1.45                                             # the booth's width, its back wall, the awning's edge
for k in range(12):                                                          # a plank deck to stand on
    y = -1.05 + (k + 0.5) * 2.5 / 12
    box(s, F, (W + 0.3, 2.5 / 12 - 0.02, 0.08), (0, y, 0.06), plank if k % 2 == 0 else plank2)
for x in (-W / 2 - 0.05, 0, W / 2 + 0.05):
    box(s, F, (0.12, 2.5, 0.06), (x, 0.2, 0.0), trim)
# The booth: a back wall, side walls full height behind the keeper and counter-high in front of him.
vkit.plank_wall(s, F, -W / 2, W / 2, 0.1, 2.42, y=BACK)
for sx in (-1, 1):
    G = F.sub((0, 0, 0), sx * 90)
    def span(ya, yb):                                                        # the stall's y as this side frame's x
        return (ya, yb) if sx > 0 else (-yb, -ya)
    vkit.plank_wall(s, G, *span(0.15, BACK), 0.1, 2.42, y=-W / 2)
    vkit.plank_wall(s, G, *span(-0.88, 0.15), 0.1, 0.92, y=-W / 2)
    cbox(s, F, (0.09, 1.1, 0.09), (sx * W / 2, -0.37, 0.96), trim, c=0.014)  # its top rail
# Posts: at the back corners, where the roof meets the awning, and at the awning's front corners (with knee braces).
for x in (-W / 2, W / 2):
    post(s, x, BACK, 2.9)
    post(s, x, -0.1, 2.5)
    post(s, x, FRONT + 0.05, 2.08, 0.07)
    cbox(s, F, (0.2, 0.2, 0.06), (x, FRONT + 0.05, 2.1), trim, c=0.014)       # a cap on it
    beam(s, F, (x, FRONT + 0.05, 1.72), (x, FRONT + 0.4, 2.03), 0.03, trim, segs=4)
    beam(s, F, (x, -0.1, 2.05), (x, -0.45, 2.33), 0.03, trim, segs=4)
    for z in (2.0, 2.05):                                                    # the awning's tie, wound round the post's top
        torus(s, (x, FRONT + 0.05, z), (0, 0, 1), 0.085, 0.011, rope, segs=10, tsegs=3)
# A board roof over the booth, sloping down to the front; the awning carries on from under its edge.
vkit.shed_roof(s, F, W + 0.1, -0.1, BACK + 0.05, 2.47, 2.9, over=0.18)
cbox(s, F, (W + 0.4, 0.09, 0.09), (0, -0.1, 2.44), trim, c=0.014)          # the beam the awning hangs from
cbox(s, F, (W + 0.4, 0.08, 0.08), (0, FRONT, 2.03), trim, c=0.014)         # the front bar
# The awning: three panels, each a little flatter than the one before (the canvas sags), red and cream stripes; a valance
# hangs from the front bar, two scallops to a stripe, and a row of scallops down each side.
NS = 10
SW = (W + 0.3) / NS
panel = [(-0.1, 2.4), (-0.62, 2.22), (-1.08, 2.11), (FRONT, 2.06)]
for a, b in zip(panel, panel[1:]):
    pitch = math.atan2(a[1] - b[1], a[0] - b[0])
    length = math.hypot(a[0] - b[0], a[1] - b[1]) + 0.02
    for k in range(NS):
        x0 = -W / 2 - 0.15 + k * SW
        box(s, F, (SW + 0.002, length, 0.025), (x0 + SW / 2, (a[0] + b[0]) / 2, (a[1] + b[1]) / 2 + 0.02), canvas if k % 2 == 0 else cream, rx=pitch)


def scallop(b, G, x, z, r, m):
    # A round flap hanging from (x, z) in G's x/z, its face toward -y.
    plate(b, G, [(x - r, z)] + [(x - r * math.cos(a), z - r * math.sin(a)) for a in (math.pi * q / 6 for q in range(1, 6))] + [(x + r, z)], 0.0, 0.016, m)


for k in range(NS):
    x0 = -W / 2 - 0.15 + k * SW
    m = canvas if k % 2 == 0 else cream
    box(s, F, (SW + 0.002, 0.018, 0.09), (x0 + SW / 2, FRONT - 0.05, 2.04), m)
    for q in range(2):
        scallop(s, Frame((0, FRONT - 0.05, 0)), x0 + SW * (q + 0.5) / 2, 1.996, SW / 4, m)
for sx in (-1, 1):
    G = F.sub((sx * (W / 2 + 0.16), 0, 0), -sx * 90)                      # the side, seen from outside
    for k, (a, b) in enumerate(zip(panel, panel[1:])):
        for q in range(2):
            t = (q + 0.5) / 2
            y, z = a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
            scallop(s, G, -sx * y, z + 0.005, 0.1, cream if (k * 2 + q) % 2 == 0 else canvas)
# The sign on the roof: three boards in a chamfered frame on two posts, iron corners, and a fish painted in relief over waves.
for x in (-0.55, 0.55):
    cbox(s, F, (0.09, 0.09, 0.52), (x, -0.02, 2.75), trim, c=0.014)
    beam(s, F, (x, -0.02, 2.62), (x + math.copysign(0.22, x), -0.02, 2.88), 0.022, trim, segs=4)
SG = Frame((0, -0.06, 3.15))
for k, z in enumerate((-0.2, 0.0, 0.2)):
    cbox(s, SG, (1.66, 0.05, 0.198), (0, 0, z), plank if k % 2 == 0 else plank2, c=0.01)
for z in (-0.32, 0.32):
    cbox(s, SG, (1.8, 0.075, 0.08), (0, -0.012, z), trim, c=0.016)
for x in (-0.86, 0.86):
    cbox(s, SG, (0.08, 0.075, 0.72), (x, -0.012, 0), trim, c=0.016)
for sx in (-1, 1):
    for sz in (-1, 1):
        box(s, SG, (0.15, 0.01, 0.035), (sx * 0.8, -0.052, sz * 0.335), iron)
        box(s, SG, (0.035, 0.01, 0.15), (sx * 0.862, -0.052, sz * 0.272), iron)
        box(s, SG, (0.02, 0.014, 0.02), (sx * 0.84, -0.055, sz * 0.31), brass)


def side_outline(x0, x1, zt, zb, zc=0.0):
    # A fish's side: along the back from the tail's root (x0) to the nose (x1), then back along the belly.
    u = (0.0, 0.12, 0.3, 0.5, 0.7, 0.85, 0.95)
    top = (0.32, 0.56, 0.86, 1.0, 0.9, 0.62, 0.32)
    bot = (0.32, 0.5, 0.76, 0.86, 0.8, 0.58, 0.28)
    xs = [x0 + (x1 - x0) * q for q in u]
    return [(x, zc + zt * f) for x, f in zip(xs, top)] + [(x1, zc - 0.02)] + [(x, zc - zb * f) for x, f in zip(xs[::-1], bot[::-1])]


FX0, FX1, FZ = -0.3, 0.5, 0.0                                              # the relief: its body sunk into the boards, the rest
plate(s, SG, side_outline(FX0, FX1, 0.22, 0.17, FZ), -0.02, 0.02, fishO)   # in layers on top (the body's face at -0.04)
plate(s, SG, [(FX0 + 0.04, FZ - 0.03), (FX0 + 0.24, FZ - 0.04), (FX0 + 0.48, FZ - 0.04), (FX1 - 0.08, FZ - 0.05), (FX1 - 0.02, FZ - 0.06),
              (FX1 - 0.07, FZ - 0.11), (FX0 + 0.5, FZ - 0.14), (FX0 + 0.24, FZ - 0.125), (FX0 + 0.06, FZ - 0.07)], -0.035, 0.012, cream)  # its belly
for xb in (-0.14, 0.0, 0.14):                                                                       # three bars
    plate(s, SG, [(xb - 0.03, FZ + 0.0), (xb + 0.025, FZ + 0.0), (xb + 0.045, FZ + 0.17), (xb - 0.005, FZ + 0.19)], -0.035, 0.012, canvas)
plate(s, SG, [(FX0 + 0.02, FZ + 0.06), (-0.58, FZ + 0.23), (-0.5, FZ + 0.02), (-0.58, FZ - 0.18), (FX0 + 0.02, FZ - 0.04)], -0.02, 0.018, fishR)  # tail
plate(s, SG, [(-0.12, FZ + 0.19), (-0.07, FZ + 0.28), (0.1, FZ + 0.27), (0.2, FZ + 0.2)], -0.02, 0.018, fishR)                                 # back fin
plate(s, SG, [(0.12, FZ - 0.07), (0.02, FZ - 0.17), (0.07, FZ - 0.17), (0.2, FZ - 0.09)], -0.035, 0.012, fishR)                             # side fin
plate(s, SG, [(0.26, FZ + 0.14), (0.3, FZ + 0.06), (0.29, FZ - 0.04), (0.25, FZ - 0.12), (0.27, FZ - 0.12), (0.32, FZ - 0.04), (0.33, FZ + 0.06),
              (0.29, FZ + 0.15)], -0.035, 0.012, redD)                                                                                     # gill
lathe(s, SG, [(0.055, 0.0), (0.055, 0.012), (0, 0.012)], (0.385, -0.038, FZ + 0.06), cream, segs=10, rx=math.pi / 2)                       # eye
lathe(s, SG, [(0.03, 0.0), (0.03, 0.012), (0, 0.016)], (0.39, -0.046, FZ + 0.06), eyeM, segs=8, rx=math.pi / 2)
plate(s, SG, [(FX1 - 0.03, FZ - 0.02), (FX1 - 0.09, FZ - 0.045), (FX1 - 0.09, FZ - 0.03)], -0.035, 0.012, redD)                              # mouth
for x, z, r in ((0.6, FZ + 0.1, 0.03), (0.68, FZ + 0.18, 0.022), (0.64, FZ + 0.25, 0.016)):                                                  # bubbles
    lathe(s, SG, [(r, 0.0), (r, 0.012), (0, 0.016)], (x, -0.022, z), cream, segs=8, rx=math.pi / 2)
wave = [(-0.72, -0.27)] + [(-0.72 + 0.09 * q, -0.235 + 0.035 * math.cos(math.pi * q / 2) ** 2 * (1 if q % 4 in (0, 1) else 0.4)) for q in range(17)] + [(0.72, -0.27)]
plate(s, SG, wave, -0.02, 0.016, fishB)                                                                                                     # waves
# The counter: its front, a chamfered top with a lip, a kick board along its foot.
vkit.plank_wall(s, F, -W / 2, W / 2, 0.1, 0.92, y=-0.88)
for k in range(12):                                                          # the back wall's outside: boards lapped over its seams
    box(s, F, (W - 0.16, 0.022, 0.21), (0, BACK + 0.058, 0.205 + k * 0.193), plank if k % 2 else plank2, rx=0.07)
cbox(s, F, (W + 0.14, 0.82, 0.07), (0, -0.55, 0.96), trim, c=0.016)
cbox(s, F, (W + 0.16, 0.05, 0.11), (0, -0.95, 0.97), trim, c=0.014)
cbox(s, F, (W + 0.06, 0.06, 0.09), (0, -0.935, 0.15), trim, c=0.014)
# The tray on the counter: a box of boards tilted toward the customers (its back on two blocks), iron corners, filled with ice
# in chunks; four kinds of fish lying in rows (mackerel, flounder, snapper, gold) with a price card stuck in each, lemons, greens.
TILT, TW, TD = 0.2, 1.8, 0.66
T = turned(F, (0, -0.92, 0.995), rx=TILT)
box(s, T, (TW, TD, 0.02), (0, TD / 2, 0.01), plank2)
for sy, hh in ((0, 0.075), (1, 0.11)):
    cbox(s, T, (TW + 0.07, 0.035, hh), (0, 0.0175 + sy * (TD - 0.035), hh / 2), plank, c=0.009)
for sx in (-1, 1):
    cbox(s, T, (0.035, TD, 0.095), (sx * (TW / 2 + 0.0175), TD / 2, 0.0475), plank2, c=0.009)
    for sy, hh in ((0, 0.075), (1, 0.11)):
        y = 0.018 + sy * (TD - 0.036)
        box(s, T, (0.05, 0.04, 0.045), (sx * (TW / 2 + 0.006), y, hh - 0.02), iron)
for x in (-0.7, 0.7):
    hb = (TD - 0.04) * math.sin(TILT)
    cbox(s, F, (0.09, 0.07, hb), (x, -0.92 + (TD - 0.04) * math.cos(TILT), 0.995 + hb / 2), plank2, c=0.008)
box(s, T, (TW - 0.01, TD - 0.05, 0.035), (0, TD / 2, 0.035), ice)
ICE = 0.0525                                                               # the ice's top, in the tray
TRAY = [  # (x, y, yaw, kind, length): the front row's noses to the customers' right, the back row's to their left
    (-0.75, 0.2, 0.85, "mackerel", 0.31), (-0.5, 0.2, 0.85, "mackerel", 0.32), (-0.16, 0.21, 0.95, "flounder", 0.34),
    (0.17, 0.2, 0.85, "snapper", 0.33), (0.43, 0.2, 0.85, "snapper", 0.32), (0.73, 0.2, 0.85, "gold", 0.3),
    (-0.74, 0.47, -0.85, "blue", 0.3), (-0.48, 0.47, -0.85, "mackerel", 0.31), (-0.15, 0.46, -0.9, "flounder", 0.33),
    (0.18, 0.47, -0.85, "snapper", 0.32), (0.45, 0.47, -0.85, "gold", 0.31), (0.72, 0.47, -0.85, "gold", 0.29),
]
for x, y, yaw, kind, L in TRAY:
    fish(s, lying(T, (x, y, ICE), yaw, L, 0.04 if kind == "flounder" else 0.065), kind, L)


def on_fish(x, y, pad=0.0):  # is (x, y) in the tray under a fish?
    for fx, fy, yaw, kind, L in TRAY:
        dx, dy = x - fx, y - fy
        along = -dx * math.sin(yaw) + dy * math.cos(yaw)     # along the fish, toward its tail
        across = dx * math.cos(yaw) + dy * math.sin(yaw)
        half = 0.22 if kind == "flounder" else 0.11
        if (along / (L * 0.52 + pad)) ** 2 + (across / (L * half + pad)) ** 2 < 1:
            return True
    return False


for i in range(24):                                                        # the ice: chunks between and round the fish, sunk
    for j in range(8):                                                     # to about the fish's middle
        x, y = -TW / 2 + 0.04 + i * 0.075 + R.uniform(-0.015, 0.015), 0.05 + j * 0.077 + R.uniform(-0.012, 0.012)
        if y > TD - 0.045 or on_fish(x, y, 0.012):
            continue
        e = R.uniform(0.038, 0.055)
        box(s, T, (e, e * R.uniform(0.85, 1.1), e * R.uniform(0.75, 1.0)), (x, y, ICE + e * R.uniform(-0.1, 0.15)), ice2 if R.random() < 0.45 else ice,
            rx=R.uniform(-0.3, 0.3), ry=R.uniform(-0.3, 0.3), rz=R.uniform(0, TAU))
for x, y in ((-0.33, 0.33), (0.31, 0.33), (0.6, 0.6)):                    # lemon halves, cut side up, and sprigs of green
    lathe(s, T, [(0, -0.002), (0.022, 0.0), (0.034, 0.012), (0.037, 0.026), (0.031, 0.03), (0, 0.032)], (x, y, ICE), vkit.petalA, segs=8,
          mf=lambda i, j: vkit.petalC if i >= 4 else vkit.petalA)
for x, y in ((-0.62, 0.34), (0.03, 0.34), (0.58, 0.34), (-0.88, 0.6)):
    for q in range(3):
        a = q * TAU / 3 + R.uniform(0, 1)
        cyl(s, T, 0.022, 0.0, 0.06, (x + 0.02 * math.cos(a), y + 0.02 * math.sin(a), ICE + 0.03), vkit.leaf, segs=3, rx=1.2, rz=a - math.pi / 2)
for x in (-0.62, -0.15, 0.3, 0.72):                                        # price cards on picks, at the back
    G = turned(T, (x, TD - 0.07, ICE), rx=-TILT - 0.25)
    beam(s, G, (0, 0, -0.02), (0, 0, 0.12), 0.004, trim, segs=3)
    box(s, G, (0.085, 0.008, 0.058), (0, -0.006, 0.13), cream)
    box(s, G, (0.05, 0.004, 0.011), (0, -0.011, 0.14), slate)
    box(s, G, (0.03, 0.004, 0.009), (-0.01, -0.011, 0.12), slate)
# A balance scale on the counter's right end: a brass pillar on a wooden plinth, a beam with a pointer, a pan on three chains
# at each end, a mackerel weighed against a stack of weights.
SC = Frame((1.2, -0.48, 0.995))
cbox(s, SC, (0.28, 0.17, 0.04), (0, 0, 0.02), plank2, c=0.008)
lathe(s, SC, [(0.05, 0.04), (0.05, 0.05), (0.03, 0.065), (0.015, 0.085), (0.013, 0.3), (0.021, 0.31), (0.021, 0.325), (0, 0.332)], (0, 0, 0), brass, segs=8)
BT = 0.06                                                                  # the beam down a little to the fish's side
cbox(s, SC, (0.33, 0.014, 0.022), (0, 0, 0.335), brass, c=0.004, ry=-BT)
box(s, SC, (0.024, 0.026, 0.03), (0, 0, 0.335), steel)
beam(s, SC, (0, 0, 0.335), (-0.1 * math.sin(BT), 0, 0.335 + 0.1 * math.cos(BT)), 0.003, steel, segs=3)   # the pointer, in a fork
for sy in (-1, 1):
    box(s, SC, (0.012, 0.004, 0.12), (0, sy * 0.017, 0.392), brass)
box(s, SC, (0.03, 0.038, 0.008), (0, 0, 0.455), brass)
for sx in (-1, 1):
    ex, ez = sx * 0.155, 0.335 + sx * 0.155 * math.sin(BT)
    ball(s, SC, 0.012, (ex, 0, ez), brass)
    torus(s, SC.p((ex, 0, ez - 0.022)), (0, 1, 0), 0.01, 0.003, iron, segs=6, tsegs=3)
    pz = 0.09 + sx * 0.012
    for q in range(3):
        a = math.pi / 2 + q * TAU / 3
        chain(s, SC, (ex, 0, ez - 0.032), (ex + 0.074 * math.cos(a), 0.074 * math.sin(a), pz + 0.024), iron, link=0.05, wire=0.003, pts=4)
    lathe(s, SC, [(0, 0), (0.05, 0), (0.08, 0.022), (0.086, 0.027), (0.078, 0.027), (0.046, 0.008), (0, 0.008)], (ex, 0, pz), brass, segs=10)
SL = Frame(SC.p((-0.155, 0, 0.09 - 0.012 + 0.008)))
fish(s, lying(SL, (0.0, 0.0, 0.0), 1.9, 0.19), "mackerel", 0.19)
for q, (r, h) in enumerate(((0.032, 0.022), (0.026, 0.02), (0.02, 0.018))):
    z = 0.09 + 0.012 + 0.008 + sum(hh for _, hh in ((0.032, 0.022), (0.026, 0.02), (0.02, 0.018))[:q])
    lathe(s, SC, [(0, 0), (r, 0), (r, h), (r * 0.35, h), (r * 0.35, h + 0.008), (0, h + 0.01)], (0.155, 0, z), brass if q % 2 else iron, segs=8)
# A butcher's block on the left end: strips glued edge to edge, a snapper on it, a knife with a bolster, a cleaver stuck in.
CB = Frame((-1.2, -0.55, 0.995))
for q in range(4):
    cbox(s, CB, (0.4, 0.076, 0.045), (0, -0.114 + q * 0.076, 0.0225), plank if q % 2 == 0 else plank2, c=0.006)
fish(s, lying(CB, (0.02, 0.03, 0.045), -1.75, 0.27), "snapper", 0.27)
KN = turned(CB, (-0.02, -0.125, 0.049), rz=0.12)                            # the knife, along the block's front
plate(s, turned(KN, (0, 0, 0), rx=-math.pi / 2), [(0.0, 0.0), (0.0, 0.032), (0.12, 0.03), (0.17, 0.016), (0.19, 0.0)], 0.0, 0.004, steel)
cbox(s, KN, (0.022, 0.038, 0.018), (-0.011, 0.017, 0.004), steel, c=0.004)
cbox(s, KN, (0.11, 0.03, 0.022), (-0.077, 0.017, 0.006), trim, c=0.006)
for x in (-0.05, -0.1):
    cyl(s, KN, 0.005, 0.005, 0.024, (x, 0.017, 0.006), brass, segs=6)
CL = turned(CB, (-0.11, 0.09, 0.04), rx=0.3, rz=0.5)                       # the cleaver, its edge in the wood
cbox(s, CL, (0.15, 0.006, 0.095), (0, 0, 0.035), steel, c=0.002)
box(s, CL, (0.15, 0.008, 0.012), (0, 0, 0.078), iron)
cbox(s, CL, (0.11, 0.024, 0.028), (-0.12, 0, 0.07), trim, c=0.006)
cyl(s, CL, 0.008, 0.008, 0.012, (0.05, 0, 0.06), slate, segs=6, rx=math.pi / 2)
# Behind the keeper: two shelves on knee brackets, jars with lids and labels, tins, bottles, a crock, a coil of rope.
for z in (1.45, 1.9):
    cbox(s, F, (W - 0.5, 0.24, 0.04), (0, BACK - 0.14, z), plank, c=0.008)
    cbox(s, F, (W - 0.48, 0.03, 0.06), (0, BACK - 0.265, z - 0.004), trim, c=0.008)
    for x in (-1.1, 0.0, 1.1):
        fin(s, [(x, BACK - 0.03, z - 0.02), (x, BACK - 0.22, z - 0.02), (x, BACK - 0.03, z - 0.21)], (1, 0, 0), 0.035, trim)


def jar(b, at, r, h, body, lid, label=cream, segs=8):
    lathe(b, F, [(0, 0), (r * 0.9, 0), (r, 0.014), (r, h * 0.24), (r, h * 0.6), (r, h * 0.72), (r * 0.72, h * 0.85), (r * 0.78, h * 0.85),
                 (r * 0.78, h), (0, h)], at, body, segs=segs, mf=lambda i, j: label if i == 3 else lid if i >= 6 else body)


def tin(b, at, r, h, label, segs=6):
    lathe(b, F, [(0, 0), (r, 0), (r, 0.012), (r, h - 0.012), (r, h), (0, h)], at, steel, segs=segs, mf=lambda i, j: label if i == 2 else steel)


def bottle(b, at, r, h, body):
    lathe(b, F, [(0, 0), (r * 0.9, 0), (r, 0.015), (r, h * 0.2), (r, h * 0.48), (r, h * 0.56), (r * 0.42, h * 0.72), (r * 0.34, h * 0.8),
                 (r * 0.34, h * 0.9), (r * 0.38, h * 0.9), (r * 0.38, h), (0, h)], at, body, segs=7,
          mf=lambda i, j: plank2 if i >= 8 else cream if i == 3 else body)


def crock(b, at, r, h):
    lathe(b, F, [(0, 0), (r * 0.85, 0), (r, h * 0.12), (r * 1.06, h * 0.5), (r, h * 0.82), (r * 0.82, h * 0.96), (r * 0.86, h), (0, h)], at, cream,
          segs=9, mf=lambda i, j: fishB if i == 2 else cream)


YS = BACK - 0.14
for x, r, h, body, lid in ((-1.09, 0.06, 0.2, jarG, brass), (-0.92, 0.056, 0.17, jarA, cream), (-0.75, 0.06, 0.21, ice2, brass)):
    jar(s, (x, YS, 1.47), r, h, body, lid)
torus(s, (-0.92, YS, 1.47 + 0.17 * 0.9), (0, 0, 1), 0.046, 0.005, rope, segs=8, tsegs=3)   # the string round a cloth lid
for q, (x, z) in enumerate(((-0.6, 0), (-0.52, 0), (-0.44, 0), (-0.56, 1), (-0.48, 1), (-0.52, 2))):
    tin(s, (x, YS - 0.03, 1.47 + z * 0.045), 0.036, 0.045, (canvas, fishB, cream)[q % 3])
crock(s, (0.0, YS, 1.47), 0.075, 0.17)
for q, x in enumerate((0.36, 0.45)):
    for z in range(3 - q):
        tin(s, (x, YS - 0.02, 1.47 + z * 0.06), 0.034, 0.06, (fishB, canvas)[(q + z) % 2])
bottle(s, (0.6, YS, 1.47), 0.04, 0.26, jarG)
for x, r, h, body, lid in ((0.78, 0.058, 0.19, jarA, brass), (0.95, 0.06, 0.21, jarG, cream), (1.12, 0.05, 0.16, ice2, brass)):
    jar(s, (x, YS, 1.47), r, h, body, lid)
for q in range(2):                                                           # the top shelf
    torus(s, (-1.0, YS, 1.94 + q * 0.03), (0, 0, 1), 0.075 - q * 0.006, 0.017, rope, segs=12, tsegs=4)
for q, x in enumerate((-0.78, -0.7, -0.62, -0.74, -0.66)):
    tin(s, (x, YS - 0.02, 1.92 + (0.045 if q >= 3 else 0)), 0.033, 0.045, (cream, canvas, fishB)[q % 3])
jar(s, (-0.42, YS, 1.92), 0.045, 0.14, jarA, brass, segs=7)
jar(s, (-0.3, YS, 1.92), 0.045, 0.12, jarG, cream, segs=7)
bottle(s, (0.32, YS, 1.92), 0.036, 0.24, jarA)
crock(s, (0.5, YS, 1.92), 0.06, 0.13)
for x, body in ((0.72, ice2), (0.86, jarG), (1.0, jarA)):
    jar(s, (x, YS, 1.92), 0.048, 0.15, body, brass, segs=7)
cyl(s, F, 0.07, 0.07, 0.24, (1.13, YS + 0.02, 1.99), rope, segs=8, ry=math.pi / 2)                   # a rolled net
for x in (1.04, 1.22):
    torus(s, (x, YS + 0.02, 1.99), (1, 0, 0), 0.072, 0.007, trim, segs=8, tsegs=3)
# Buoys hanging from the awning's beam: a red spindle with a cream band on the left, a green glass float in a rope net on the right.
for x in (-1.25, 1.25):
    beam(s, F, (x, -0.12, 2.395), (x, -0.12, 2.2), 0.007, rope, segs=3)
lathe(s, F, [(0, -0.13), (0.045, -0.105), (0.085, -0.05), (0.095, 0), (0.085, 0.05), (0.045, 0.105), (0.014, 0.125), (0.014, 0.15), (0, 0.155)],
      (-1.25, -0.12, 2.04), canvas, segs=10, mf=lambda i, j: cream if i in (2, 3) else iron if i >= 6 else canvas)
torus(s, (-1.25, -0.12, 2.205), (1, 0, 0), 0.018, 0.005, iron, segs=8, tsegs=3)
GF = (1.25, -0.12, 2.06)
lathe(s, F, [(0, -0.09), (0.045, -0.078), (0.078, -0.045), (0.09, 0), (0.078, 0.045), (0.045, 0.078), (0, 0.09)], GF, floatG, segs=10)
for q in range(4):
    a = q * TAU / 4 + 0.4
    tube(s, [Vector(GF) + Vector((0.094 * math.cos(a) * math.cos(e), 0.094 * math.sin(a) * math.cos(e), 0.094 * math.sin(e)))
             for e in map(math.radians, (-80, -45, 0, 45, 80))], 0.005, rope, segs=3)
for z, r in ((-0.045, 0.082), (0.045, 0.082)):
    torus(s, (GF[0], GF[1], GF[2] + z), (0, 0, 1), r, 0.005, rope, segs=10, tsegs=3)
ball(s, F, 0.016, (GF[0], GF[1], GF[2] + 0.1), rope)
beam(s, F, (GF[0], GF[1], GF[2] + 0.1), (GF[0], GF[1], 2.2), 0.007, rope, segs=3)
torus(s, (0, -0.1, 2.39), (1, 0, 0), 0.018, 0.005, iron, segs=8, tsegs=3)  # the lantern in the middle, on a hook
vkit.lantern2(s, F, (0, -0.1, 2.115), 0.8)
# Dried fish hung by their tails from a line across the back, tied to a cleat on each side wall.
def sag(x):
    return 2.62 - 0.05 * math.sin(math.pi * (x + 1.46) / 2.92)


tube(s, [(x, BACK - 0.3, sag(x)) for x in (-1.46, -1.1, -0.66, -0.22, 0.22, 0.66, 1.1, 1.46)], 0.007, rope, segs=4)
for sx in (-1, 1):
    cbox(s, F, (0.05, 0.1, 0.05), (sx * 1.455, BACK - 0.3, 2.62), trim, c=0.008)
for k in range(6):
    x = -1.1 + k * 0.44
    z = sag(x)
    beam(s, F, (x, BACK - 0.3, z), (x, BACK - 0.3, z - 0.06), 0.005, rope, segs=3)
    L = 0.3 + 0.03 * (k % 2)
    base = (Matrix.Translation((x, BACK - 0.3, z - 0.06 - 0.5 * L)) @ Matrix.Rotation(-math.pi / 2 + R.uniform(-0.35, 0.35), 4, "Z")
            @ Matrix.Rotation(math.pi / 2, 4, "X"))
    fish(s, base, "dried", L, lite=True)
# A net hung on the left wall inside, from three pegs: diamond mesh gathered toward its foot, corks along its top, two glass floats.
NX = -W / 2 + 0.05


def net_at(u, v):
    y = (0.3 + 0.8 * u) * (1 - v ** 1.4) + (0.55 + 0.3 * u) * v ** 1.4
    z = 2.17 - 0.05 * abs(math.sin(math.pi * 2 * u)) * (1 - v) - 0.92 * v
    return Vector((NX + 0.05 * math.sin(math.pi * v) * (0.6 + 0.4 * math.sin(math.pi * u)), y, z))


for y in (0.3, 0.7, 1.1):
    cbox(s, F, (0.08, 0.04, 0.04), (NX + 0.02, y, 2.2), trim, c=0.008)
tube(s, [net_at(u / 8, 0) for u in range(9)], 0.008, rope, segs=4)
for fam in (1, -1):
    for c in range(-3, 10):
        pts = []
        for q in range(9):
            v = q / 8
            u = c / 6 + fam * v * 0.5
            if 0 <= u <= 1:
                pts.append(net_at(u, v))
        if len(pts) > 1:
            tube(s, pts, 0.0045, rope, segs=3)
for u in (0.12, 0.38, 0.62, 0.88):
    cyl(s, F, 0.024, 0.024, 0.05, net_at(u, 0) + Vector((0.02, 0, -0.015)), plank, segs=6, rx=math.pi / 2)
for u, v in ((0.35, 0.55), (0.62, 0.72)):
    lathe(s, F, [(0, -0.055), (0.04, -0.04), (0.055, 0), (0.04, 0.04), (0, 0.055)], net_at(u, v) + Vector((0.045, 0, 0)), floatG, segs=8)
# Behind the keeper: an open crate of fish on ice, a barrel with its lid on, a closed crate with a coil of rope on it.
vkit.crate2(s, F, (0.56, 0.44, 0.4), (0.95, 0.7, 0.1), 5, lid=False)
CI = F.sub((0.95, 0.7, 0.1), 5)
box(s, CI, (0.5, 0.38, 0.03), (0, 0, 0.37), ice)
for i in range(6):
    for j in range(4):
        x, y = -0.21 + i * 0.085, -0.14 + j * 0.09
        if abs(y) < 0.08 and -0.15 < x < 0.18:
            continue
        e = R.uniform(0.04, 0.06)
        box(s, CI, (e, e, e * 0.8), (x, y, 0.39), ice2 if R.random() < 0.4 else ice, rx=R.uniform(-0.4, 0.4), ry=R.uniform(-0.4, 0.4), rz=R.uniform(0, 3))
for q, (x, y, yaw, kind) in enumerate(((-0.12, -0.04, 1.9, "mackerel"), (0.0, 0.03, 1.4, "snapper"), (0.12, -0.02, 1.7, "blue"))):
    fish(s, lying(CI, (x, y, 0.385 + 0.012 * q), yaw, 0.27), kind, 0.27)
vkit.barrel2(s, F, 0.26, 0.72, (-1.0, 0.75, 0.1))
vkit.crate2(s, F, (0.46, 0.46, 0.44), (-0.45, 0.9, 0.1), -10)
for q in range(2):
    torus(s, (-0.45, 0.9, 0.59 + q * 0.03), (0, 0, 1), 0.11 - q * 0.01, 0.018, rope, segs=12, tsegs=4)
# Oars crossed on the right wall outside, red tips, lashed where they cross, on two pegs.
for k, ang in enumerate((0.5, -0.5)):
    O = turned(F, (W / 2 + 0.12 + 0.03 * k, 0.55, 1.35), rx=ang)
    beam(s, O, (0, -0.8, 0), (0, 0.42, 0), 0.022, plank, segs=6)
    cyl(s, O, 0.027, 0.025, 0.13, (0, -0.86, 0), plank2, segs=6, rx=math.pi / 2)
    cyl(s, O, 0.029, 0.029, 0.12, (0, -0.32, 0), leather, segs=6, rx=math.pi / 2)
    blade = [(0.36, -0.024), (0.55, -0.068), (0.82, -0.068), (0.9, -0.05), (0.93, 0), (0.9, 0.05), (0.82, 0.068), (0.55, 0.068), (0.36, 0.024)]
    fin(s, [O.p((0, y, z)) for y, z in blade], O.m((0, 0, 0)).to_3x3() @ Vector((1, 0, 0)), 0.022, plank2)
    fin(s, [O.p((0.012, y, z)) for y, z in ((0.8, -0.069), (0.9, -0.05), (0.93, 0), (0.9, 0.05), (0.8, 0.069))], O.m((0, 0, 0)).to_3x3() @ Vector((1, 0, 0)), 0.006, canvas)
    fin(s, [O.p((0.012, y, z)) for y, z in ((0.74, -0.069), (0.78, -0.069), (0.78, 0.069), (0.74, 0.069))], O.m((0, 0, 0)).to_3x3() @ Vector((1, 0, 0)), 0.006, cream)
torus(s, (W / 2 + 0.135, 0.55, 1.35), (0, 1, 0), 0.04, 0.009, rope, segs=8, tsegs=3)
for y in (0.55 - 0.44, 0.55 + 0.44):                                          # a peg under each loom
    cbox(s, F, (0.17, 0.035, 0.035), (W / 2 + 0.1, y, 1.35 - 0.44 * math.tan(0.5) - 0.04), trim, c=0.008)
# Round the back: a life ring on a peg, a grab line looped round it, and two crates stacked by it.
LR = Vector((0.75, BACK + 0.13, 1.55))
for k in range(8):
    a0, a1 = k * TAU / 8, (k + 1) * TAU / 8
    tube(s, [LR + Vector((0.22 * math.cos(a0 + (a1 - a0) * t / 3), 0, 0.22 * math.sin(a0 + (a1 - a0) * t / 3))) for t in range(4)], 0.056,
         canvas if k % 2 == 0 else cream, segs=6)
for k in range(4):
    a = (k * 2 + 1.5) * TAU / 8
    torus(s, LR + Vector((0.22 * math.cos(a), 0, 0.22 * math.sin(a))), Vector((-math.sin(a), 0, math.cos(a))), 0.062, 0.008, rope, segs=8, tsegs=3)
    b0, b1 = a, a + TAU / 4
    tube(s, [LR + Vector((0.285 * math.cos(b0 + (b1 - b0) * t / 4) * (1 + 0.06 * math.sin(math.pi * t / 4)), -0.01,
                          0.285 * math.sin(b0 + (b1 - b0) * t / 4) * (1 + 0.06 * math.sin(math.pi * t / 4)))) for t in range(5)], 0.008, rope, segs=3)
cbox(s, F, (0.05, 0.1, 0.05), (0.75, BACK + 0.1, 1.69), trim, c=0.008)
vkit.crate2(s, F, (0.56, 0.56, 0.52), (-0.9, BACK + 0.57, 0.0), 4)          # on the ground, off the deck's end
vkit.crate2(s, F, (0.46, 0.46, 0.4), (-0.86, BACK + 0.57, 0.566), -7)       # on the lid's batten
# A lantern on an iron arm off the front left post, a chalkboard of prices out front.
LX = -W / 2 - 0.07
box(s, F, (0.26, 0.03, 0.03), (LX - 0.12, FRONT + 0.05, 1.86), iron)
beam(s, F, (LX, FRONT + 0.05, 1.68), (LX - 0.18, FRONT + 0.05, 1.86), 0.01, iron, segs=4)
torus(s, (LX - 0.2, FRONT + 0.05, 1.835), (0, 1, 0), 0.016, 0.005, iron, segs=8, tsegs=3)
vkit.lantern2(s, F, (LX - 0.2, FRONT + 0.05, 1.56), 0.8)
CK = Frame((-W / 2 - 0.55, -1.25, 0))
for sy in (-1, 1):                                                           # the two leaves, meeting at the hinge
    Lf = turned(CK, (0, sy * 0.105, 0.42), rx=sy * 0.2)
    for x in (-0.26, 0.26):
        cbox(s, Lf, (0.045, 0.035, 0.86), (x, 0, 0), trim, c=0.008)
    cbox(s, Lf, (0.56, 0.035, 0.05), (0, 0, 0.405), trim, c=0.008)
    cbox(s, Lf, (0.56, 0.035, 0.045), (0, 0, -0.13), trim, c=0.008)
    box(s, Lf, (0.48, 0.012, 0.5), (0, 0, 0.14), slate)
    if sy < 0:                                                               # chalk on the front: a fish, three prices, the ledge
        S = turned(Lf, (0, -0.008, 0.14), 0)
        tube(s, [S.p((x, 0, z)) for x, z in ((0.11, 0.1), (0.04, 0.15), (-0.06, 0.13), (-0.12, 0.08), (-0.17, 0.13), (-0.17, 0.03),
                                              (-0.12, 0.08), (-0.06, 0.03), (0.04, 0.01), (0.11, 0.06), (0.11, 0.1))], 0.0045, cream, segs=3)
        ball(s, S, 0.008, (0.06, 0, 0.1), cream)
        for q in range(3):
            tube(s, [S.p((-0.16, 0, -0.04 - q * 0.065)), S.p((0.02 - 0.03 * (q % 2), 0, -0.04 - q * 0.065))], 0.0045, cream, segs=3)
            ball(s, S, 0.009, (0.1, 0, -0.04 - q * 0.065), cream)
            tube(s, [S.p((0.07, 0, -0.04 - q * 0.065)), S.p((0.17, 0, -0.04 - q * 0.065))], 0.0045, cream, segs=3)
        cbox(s, Lf, (0.5, 0.05, 0.02), (0, -0.03, -0.11), trim, c=0.006)
        cyl(s, Lf, 0.007, 0.007, 0.06, (0.12, -0.035, -0.095), cream, segs=5, ry=math.pi / 2)
cyl(s, CK, 0.014, 0.014, 0.56, (0, 0, 0.84), iron, segs=6, ry=math.pi / 2)   # the hinge
for x in (-0.25, 0.25):                                                       # a cord holding the legs apart
    beam(s, CK, (x, -0.13, 0.3), (x, 0.13, 0.3), 0.005, rope, segs=3)
out.append(s.finish())

# ---------- Smithy: a lean-to of boards over a stone forge (bellows at its side, an iron hood, a chimney through the roof), an
# anvil on a stump, a quench trough, a treadle grindstone, a coal barrel, tongs and hammers, a weapon rack on the back wall
# and a sign hanging out front ----------
R.seed(7)                                                                    # the smithy's props drawn the same whatever the stall's do
f = Builder("Smithy", vkit.KIT + vkit.KIT2 + [coal, coalHot, ember, ash, soot, water, waterHi, leather, steel, anvilM, hoodM, bark, grit])
W, D = 4.0, 3.0
for k in range(9):                                                           # a floor of flagstones
    for j in range(7):
        x, y = -W / 2 + (k + 0.5) * W / 9, -1.4 + (j + 0.5) * D / 7
        box(f, F, (W / 9 - 0.04, D / 7 - 0.04, 0.1 + vkit.rng.uniform(0, 0.02)), (x, y, 0.06), stone if (k + j) % 3 else stone2)
vkit.stones(f, F, -W / 2, W / 2, 1.3, 1.6, 0.0, 1.0, rows=3)                  # the back wall: stone below,
vkit.plank_wall(f, F, -W / 2, W / 2, 1.0, 2.9, y=1.42)                         # boards above
vkit.plank_wall(f, F.sub((0, 0, 0), 90), 0.1, 1.45, 0.1, 1.05, y=-W / 2 - 0.02)  # a low board wall on the right
for x in (-W / 2, W / 2):                                                     # posts, braced
    post(f, x, -1.35, 2.45, 0.09)
    post(f, x, 1.45, 2.95, 0.09)
    beam(f, F, (x, -1.35, 2.0), (x, -0.85, 2.42), 0.04, trim, segs=4)
cbox(f, F, (W + 0.3, 0.14, 0.16), (0, -1.35, 2.45), trim, c=0.018)          # the front beam
vkit.shed_roof(f, F, W, -1.45, 1.55, 2.45, 2.98, over=0.3)
for k in range(10):                                                          # the back wall's outside: boards lapped over its seams
    box(f, F, (W - 0.18, 0.022, 0.21), (0, 1.478, 1.105 + k * 0.193), plank if k % 2 else plank2, rx=0.07)
# The forge, back left: a hearth of rough stones under a chamfered capstone, a fire well of glowing coals (hottest in the middle),
# the ash pit's dark mouth under a lintel, embers in it; the bellows' pipe into its side.
fx, fy = -1.25, 0.95
HX0, HX1, HY0, HY1, HZ = -1.9, -0.6, 0.45, 1.4, 0.82
box(f, F, (HX1 - HX0 - 0.03, HY1 - HY0, HZ - 0.08), ((HX0 + HX1) / 2, (HY0 + HY1) / 2 + 0.01, (HZ + 0.08) / 2), stone2)


def facing(b, G, x0, x1, z0, z1, rows=3, holes=()):
    # Rough stones in courses from x0 to x1 (their faces toward G's -y), a gap where holes (xa, xb, za, zb) are.
    h = (z1 - z0) / rows
    for r in range(rows):
        x = x0
        while x < x1 - 0.04:
            w = min(R.uniform(0.19, 0.33), x1 - x)
            if x1 - (x + w) < 0.1:
                w = x1 - x
            za, zb = z0 + h * r, z0 + h * (r + 1)
            if not any(x < hb and x + w > ha and za < vb and zb > va for ha, hb, va, vb in holes):
                cbox(b, G, (w - 0.022, 0.07, h - 0.022), (x + w / 2, R.uniform(-0.01, 0.006), (za + zb) / 2), stone if R.random() < 0.55 else stone2,
                     c=0.016)
            x += w


PIT = (fx - 0.25, fx + 0.25, 0.1, 0.57)
facing(f, F.sub((0, HY0, 0)), HX0, HX1, 0.1, HZ, holes=(PIT,))
facing(f, F.sub((HX0, 0, 0), -90), -HY1 + 0.02, -HY0, 0.1, HZ)
facing(f, F.sub((HX1, 0, 0), 90), HY0, HY1 - 0.02, 0.1, HZ)
box(f, F, (0.36, 0.03, 0.4), (fx, HY0 + 0.02, 0.3), soot)                   # the ash pit's mouth
for sx in (-1, 1):
    cbox(f, F, (0.08, 0.07, 0.38), (fx + sx * 0.21, HY0 - 0.004, 0.29), stone, c=0.014)
cbox(f, F, (0.56, 0.08, 0.1), (fx, HY0 - 0.008, 0.53), stone, c=0.018)       # its lintel
for q in range(4):
    lump(f, F, (fx - 0.11 + q * 0.07, HY0 - 0.02, 0.135), 0.035, coal if q % 2 else ember)
WX, WY = 0.33, 0.27                                                            # the fire well's half size
for x0, x1, y0, y1 in ((HX0 - 0.03, HX1 + 0.03, HY0 - 0.045, fy - WY), (HX0 - 0.03, HX1 + 0.03, fy + WY, HY1 + 0.02),
                       (HX0 - 0.03, fx - WX, fy - WY, fy + WY), (fx + WX, HX1 + 0.03, fy - WY, fy + WY)):
    cbox(f, F, (x1 - x0, y1 - y0, 0.09), ((x0 + x1) / 2, (y0 + y1) / 2, HZ + 0.045), stone, c=0.02)
for sx in (-1, 1):
    box(f, F, (0.03, WY * 2, 0.03), (fx + sx * (WX - 0.015), fy, HZ + 0.075), iron)
    box(f, F, (WX * 2, 0.03, 0.03), (fx, fy + sx * (WY - 0.015), HZ + 0.075), iron)
box(f, F, (WX * 2 - 0.03, WY * 2 - 0.03, 0.05), (fx, fy, HZ + 0.065), ember)    # a bed of coal, a glowing heap on it
for q in range(44):
    rr, a = R.random() ** 0.7, R.uniform(0, TAU)
    x, y = fx + rr * (WX - 0.07) * math.cos(a), fy + rr * (WY - 0.06) * math.sin(a)
    z = HZ + 0.095 + 0.08 * (1 - rr * rr) + R.uniform(0, 0.015)
    lump(f, F, (x, y, z), R.uniform(0.04, 0.06), coalHot if rr < 0.45 and R.random() < 0.85 else coal if rr < 0.85 else ember)
# The hood: iron plates (a skirt, then sloping in to the flue) on a riveted rim, the chimney's stones on it, out through the roof
# with a collar of iron, a corbelled course and a cap.
HB, HT = 1.45, 2.14
ob = [(HX0 + 0.05, HY0 - 0.035), (HX1 - 0.05, HY0 - 0.035), (HX1 - 0.05, HY1), (HX0 + 0.05, HY1)]
ot = [(fx - 0.25, fy - 0.13), (fx + 0.25, fy - 0.13), (fx + 0.25, fy + 0.35), (fx - 0.25, fy + 0.35)]


def inset(pts, d):
    cx, cy = sum(p[0] for p in pts) / 4, sum(p[1] for p in pts) / 4
    return [(x - math.copysign(d, x - cx), y - math.copysign(d, y - cy)) for x, y in pts]


rings = [[Vector((x, y, HB)) for x, y in ob], [Vector((x, y, HB + 0.09)) for x, y in ob], [Vector((x, y, HT)) for x, y in ot],
         [Vector((x, y, HT)) for x, y in inset(ot, 0.025)], [Vector((x, y, HB + 0.09)) for x, y in inset(ob, 0.025)],
         [Vector((x, y, HB)) for x, y in inset(ob, 0.025)]]
loft(f, rings, hoodM, mf=lambda i, j: iron if i in (0, 5) else hoodM if i == 1 else soot, closed=True)
for (xa, ya), (xb, yb) in ((ob[3], ob[0]), (ob[0], ob[1]), (ob[1], ob[2])):  # rivets round the rim (not on the wall's side)
    n = max(2, round(math.hypot(xb - xa, yb - ya) / 0.13))
    for q in range(1, n):
        t = q / n
        x, y = xa + (xb - xa) * t, ya + (yb - ya) * t
        out_ = Vector((0, -1, 0)) if abs(ya - yb) < 1e-6 else Vector((math.copysign(1, x - fx), 0, 0))
        box(f, F, (0.018, 0.018, 0.018), Vector((x, y, HB + 0.045)) + out_ * 0.004, iron)
for (bx, by), (tx, ty) in zip(ob[:2], ot[:2]):                               # the seams up its front corners and its middle
    beam(f, F, (bx, by, HB + 0.09), (tx, ty, HT), 0.011, iron, segs=4)
beam(f, F, (fx, HY0 - 0.035, HB + 0.09), (fx, fy - 0.13, HT), 0.008, iron, segs=4)
vkit.stones(f, F, fx - 0.25, fx + 0.25, fy - 0.15, fy + 0.35, HT - 0.02, 4.0, rows=8)  # the chimney
CC = F.sub((fx, fy + 0.1, 0))                                                # the chimney's middle


def square_ring(G, r0, r1, z, h, m, c):
    # A square frame round G's z axis, from r0 out to r1, h tall at z.
    for x0, x1, y0, y1 in ((-r1, r1, -r1, -r0), (-r1, r1, r0, r1), (-r1, -r0, -r0, r0), (r0, r1, -r0, r0)):
        cbox(f, G, (x1 - x0, y1 - y0, h), ((x0 + x1) / 2, (y0 + y1) / 2, z), m, c=c)


square_ring(CC, 0.25, 0.31, HT + 0.02, 0.07, iron, 0.01)                     # the flue's collar on the hood
square_ring(CC, 0.25, 0.36, 3.975, 0.09, stone, 0.016)                        # a corbelled course
box(f, CC, (0.46, 0.46, 0.12), (0, 0, 4.08), soot)                            # the flue, open between four piers
for sx in (-1, 1):
    for sy in (-1, 1):
        cbox(f, CC, (0.13, 0.13, 0.11), (sx * 0.22, sy * 0.22, 4.075), stone2, c=0.014)
cbox(f, CC, (0.66, 0.66, 0.07), (0, 0, 4.165), stone, c=0.02)                 # its cap
PITCH = math.atan2(2.98 - 2.45, 1.55 + 1.45)                                  # iron flashing where it meets the roof
square_ring(turned(F, (fx, fy + 0.1, 2.45 + (fy + 0.1 + 1.45) * math.tan(PITCH) + 0.1), rx=PITCH), 0.29, 0.37, 0.0, 0.05, hoodM, 0.01)
# The bellows at the forge's right: two pear-shaped boards, pleated leather between them, brass studs, a brass nozzle into the
# hearth's side, a lever to pump it, on two trestles.
BW = turned(F, (HX1 + 0.1, fy, 0.56), rz=0.0)                              # its hinge end; +x toward the handle


def pear(z, k=1.0):
    pts = [(x * 1.2, y * 1.2) for x, y in ((0.0, -0.06), (0.12, -0.11), (0.25, -0.158), (0.36, -0.165), (0.43, -0.13), (0.46, -0.06), (0.465, 0.0))]
    pts = pts + [(x, -y) for x, y in pts[-2::-1]]
    return [(x, y * k, z(x)) for x, y in pts]


OPEN = 0.17
lo_z = lambda x: 0.012
hi_z = lambda x: 0.028 + x * math.tan(OPEN) - 0.012
fin(f, [BW.p(p) for p in pear(lambda x: 0.0)], BW.m((0, 0, 0)).to_3x3() @ Vector((0, 0, 1)), 0.024, plank2)
TB = turned(BW, (0, 0, 0.028), ry=-OPEN)
fin(f, [TB.p(p) for p in pear(lambda x: 0.0)], TB.m((0, 0, 0)).to_3x3() @ Vector((0, 0, 1)), 0.024, plank)
loft(f, [[BW.p((x * (0.97 if q % 2 else 1.0), y * (0.86 if q % 2 else 1.0), lo_z(x) + (hi_z(x) - lo_z(x)) * q / 5)) for x, y, _ in pear(lambda x: 0)]
         for q in range(6)], leather)                                          # the leather, pleated
for x, y, _ in pear(lambda x: 0)[1:-1:2]:
    box(f, TB, (0.016, 0.016, 0.012), (x * 0.97, y * 0.97, 0.014), brass)
tube(f, [BW.p((0.02, 0, 0.02)), BW.p((-0.06, 0, 0.02)), BW.p((-0.13, 0, 0.022))], [0.036, 0.024, 0.017], brass, segs=8)
beam(f, TB, (0.53, 0, 0.012), (0.7, 0, 0.12), 0.017, plank2, segs=6)
cyl(f, TB, 0.024, 0.024, 0.09, (0.7, 0, 0.12), trim, segs=6, rx=math.pi / 2)
for x in (0.12, 0.42):
    for sy in (-1, 1):
        beam(f, BW, (x, sy * 0.07, -0.01), (x, sy * 0.16, -0.44), 0.016, trim, segs=4)
    cbox(f, BW, (0.04, 0.32, 0.035), (x, 0, -0.26), trim, c=0.006)
    cbox(f, BW, (0.05, 0.2, 0.03), (x, 0, -0.025), trim, c=0.006)
# Tongs hung over a bar on the hearth's front, a poker left in the fire.


def tongs(b, G, L=0.48, jaw=0.08):
    # Hanging jaws down in G: two reins up to a hook over the bar at G's origin, the rivet, two jaws curling together.
    for sx in (-1, 1):
        tube(b, [G.p((sx * 0.006, 0, 0.0)), G.p((sx * 0.014, 0, -L * 0.5)), G.p((sx * 0.01, 0, -L + jaw))], 0.007, iron, segs=4)
        tube(b, [G.p((sx * 0.01, 0, -L + jaw)), G.p((sx * 0.028, 0, -L + jaw * 0.5)), G.p((sx * 0.006, 0, -L))], 0.0085, iron, segs=4)
    cyl(b, G, 0.013, 0.013, 0.03, (0, 0, -L + jaw), iron, segs=6, rx=math.pi / 2)
    tube(b, [G.p((0, 0, 0.0)), G.p((0, 0.03, 0.02)), G.p((0, 0.05, -0.015))], 0.007, iron, segs=4)


beam(f, F, (-1.02, HY0 - 0.065, HZ - 0.08), (-0.66, HY0 - 0.065, HZ - 0.08), 0.01, iron, segs=6)
for x in (-1.02, -0.66):
    box(f, F, (0.02, 0.05, 0.02), (x, HY0 - 0.04, HZ - 0.08), iron)
for x, L in ((-0.93, 0.5), (-0.78, 0.44)):
    tongs(f, Frame((x, HY0 - 0.08, HZ - 0.06)), L)
PK = [Vector((fx - 0.05, fy - 0.05, HZ + 0.15)), Vector((fx + 0.12, HY0 - 0.02, HZ + 0.1)), Vector((fx + 0.18, HY0 - 0.24, HZ + 0.08))]
tube(f, PK, 0.01, iron, segs=4)                                              # in the coals, over the front edge
d = (PK[2] - PK[1]).normalized()
torus(f, PK[2] + d * 0.035, d.cross(Vector((0, 0, 1))), 0.035, 0.008, iron, segs=8, tsegs=3)
# Hammers and long tongs on hooks by the forge, on a board under the rack's end.


def hammer(b, G, L=0.3, head=0.1, w=0.034, ball_=False):
    # In G: the head across x (its face at +x, the peen at -x), the handle down -z from it.
    cbox(b, G, (head * 0.6, w, w), (head * 0.1, 0, 0), iron, c=w * 0.15)
    cyl(b, G, w * 0.56, w * 0.5, head * 0.14, (head * 0.46, 0, 0), iron, segs=8, ry=math.pi / 2)
    if ball_:
        lump(b, G, (-head * 0.32, 0, 0), w * 0.62, iron, 1.0)
    else:
        fin(b, [G.p((-head * 0.2, 0, -w / 2)), G.p((-head * 0.52, 0, -w * 0.1)), G.p((-head * 0.52, 0, w * 0.1)), G.p((-head * 0.2, 0, w / 2))],
            G.m((0, 0, 0)).to_3x3() @ Vector((0, 1, 0)), w * 0.95, iron)
    beam(b, G, (0, 0, w * 0.55), (0, 0, -L), 0.012, plank2, segs=6)
    cyl(b, G, 0.016, 0.014, L * 0.4, (0, 0, -L * 0.78), plank2, segs=6)
    box(b, G, (0.012, w * 0.7, 0.006), (0, 0, w * 0.52), soot)


cbox(f, F, (0.34, 0.03, 0.06), (-0.6, 1.378, 2.3), trim, c=0.008)
for x in (-0.73, -0.6, -0.48):
    beam(f, F, (x, 1.37, 2.3), (x, 1.31, 2.32), 0.006, iron, segs=4)
tongs(f, Frame((-0.73, 1.33, 2.32)), 0.52)
hammer(f, Frame((-0.6, 1.325, 2.338)), 0.3)                                 # each by its head, its handle hanging
hammer(f, Frame((-0.48, 1.325, 2.338)), 0.26, 0.09, 0.03, ball_=True)
# The anvil, front middle, on a stump bound with iron: feet, a waist, a body flaring up to a steel face, a horn, a step, the heel
# over a hardy hole and a pritchel hole; a hammer lying on it, a sledge leant on the stump.
ax, ay = 0.2, -0.45
A = Frame((ax, ay, 0))
BARK = [R.random() < 0.6 for j in range(11)]
lathe(f, A, [(0.33, 0.06), (0.31, 0.15), (0.29, 0.3), (0.29, 0.58), (0.272, 0.6), (0, 0.6)], (0, 0, 0), bark, segs=11,
      mf=lambda i, j: (bark if BARK[j] else trim) if i < 3 else plank2 if i == 3 else plank)
for k in range(5):                                                             # its roots
    a = k * TAU / 5 + R.uniform(-0.25, 0.25)
    cbox(f, A, (0.2, 0.1, 0.12), (0.3 * math.cos(a), 0.3 * math.sin(a), 0.12), bark, c=0.02, ry=0.45, rz=a)
for r in (0.2, 0.11):                                                         # its growth rings
    torus(f, A.p((0, 0, 0.6)), (0, 0, 1), r, 0.005, plank2, segs=11, tsegs=3)


def oct_ring(cx, hx, hy, z, c):
    return [Vector((cx + x, y, z)) for x, y in ((hx, -(hy - c)), (hx, hy - c), (hx - c, hy), (-(hx - c), hy), (-hx, hy - c), (-hx, -(hy - c)),
                                                (-(hx - c), -hy), (hx - c, -hy))]


AB = [(0.0, 0.2, 0.15, 0.6, 0.04), (0.0, 0.19, 0.14, 0.64, 0.04), (0.01, 0.11, 0.085, 0.7, 0.025), (0.01, 0.11, 0.08, 0.78, 0.025),
      (0.02, 0.2, 0.1, 0.9, 0.025), (0.02, 0.21, 0.1, 0.945, 0.02)]
loft(f, [[A.p(v) for v in oct_ring(*r)] for r in AB], anvilM)
cbox(f, A, (0.43, 0.205, 0.06), (0.075, 0, 0.975), steel, c=0.01)          # the face
fin(f, [A.p(p) for p in ((0.2, 0, 0.86), (0.29, 0, 0.945), (0.2, 0, 0.945))], Vector((0, 1, 0)), 0.18, anvilM)  # under the heel
cbox(f, A, (0.1, 0.17, 0.05), (-0.185, 0, 0.955), anvilM, c=0.01)          # the step
tube(f, [A.p(p) for p in ((-0.15, 0, 0.925), (-0.24, 0, 0.93), (-0.32, 0, 0.935), (-0.4, 0, 0.941), (-0.46, 0, 0.947), (-0.52, 0, 0.952))],
     [0.055, 0.05, 0.04, 0.028, 0.015, 0.0], anvilM, segs=8)               # the horn, its top level with the step
box(f, A, (0.034, 0.034, 0.004), (0.24, 0, 1.006), soot)
cyl(f, A, 0.012, 0.012, 0.004, (0.165, 0.045, 1.006), soot, segs=6)
for sy in (-1, 1):                                                           # staples over its feet
    box(f, A, (0.05, 0.016, 0.016), (0.0, sy * 0.155, 0.612), iron)
    for sx in (-1, 1):
        box(f, A, (0.014, 0.016, 0.04), (sx * 0.025, sy * 0.155, 0.6), iron)
hammer(f, turned(A, (0.12, -0.02, 1.005 + 0.017), rx=-math.pi / 2, rz=-0.5), 0.32)
hammer(f, turned(A, (0.05, -0.5, 0.145), rx=2.69, rz=0.3), 0.5, 0.15, 0.05)  # the sledge, on its head, leant on the stump
# The quench trough, front right: thick boards with iron bands, dark water with a ripple or two, tongs dipped in it.
TX, TY = 1.4, -0.75
for sy in (-1, 1):
    for k in range(2):
        cbox(f, F, (0.88, 0.045, 0.2), (TX, TY + sy * 0.225, 0.22 + k * 0.2), plank if (k + (sy > 0)) % 2 == 0 else plank2, c=0.01)
for sx in (-1, 1):
    cbox(f, F, (0.05, 0.495, 0.45), (TX + sx * 0.465, TY, 0.34), plank2, c=0.014)
for z in (0.2, 0.46):                                                         # two iron bands round it, nailed
    for sy in (-1, 1):
        box(f, F, (0.996, 0.008, 0.035), (TX, TY + sy * 0.2515, z), iron)
        for x in (-0.3, 0.0, 0.3):
            box(f, F, (0.014, 0.006, 0.014), (TX + x, TY + sy * 0.2565, z), iron)
    for sx in (-1, 1):
        box(f, F, (0.008, 0.511, 0.035), (TX + sx * 0.494, TY, z), iron)
box(f, F, (0.88, 0.42, 0.08), (TX, TY, 0.13), plank2)
box(f, F, (0.87, 0.41, 0.03), (TX, TY, 0.475), water)
for x, y, r in ((TX - 0.18, TY + 0.03, 0.07), (TX - 0.18, TY + 0.03, 0.12), (TX + 0.2, TY - 0.06, 0.05)):   # ripples
    torus(f, (x, y, 0.49), (0, 0, 1), r, 0.005, waterHi, segs=12, tsegs=3)
tongs(f, turned(F, (TX + 0.53, TY + 0.1, 0.67), ry=0.75), 0.5)              # over the end board, its jaws in the water
# The grindstone, front left, its face to the road: a sandstone wheel on an iron axle between two A-frames, a little trough of
# water under it, a crank on the front and a treadle on the floor working it.
GX, GY, GZ, GR = -1.55, -0.85, 0.6, 0.3
WH = turned(F, (GX, GY, GZ), rx=math.pi / 2)
lathe(f, WH, [(0, -0.045), (GR - 0.03, -0.045), (GR, -0.022), (GR, 0.022), (GR - 0.03, 0.045), (0, 0.045)], (0, 0, 0), grit, segs=16,
      mf=lambda i, j: stone2 if i in (1, 2, 3) else grit)
for z in (-0.046, 0.046):
    torus(f, WH.p((0, 0, z)), WH.m((0, 0, 0)).to_3x3() @ Vector((0, 0, 1)), 0.19, 0.006, stone2, segs=14, tsegs=3)
cyl(f, WH, 0.06, 0.06, 0.112, (0, 0, 0), iron, segs=8)
cbox(f, WH, (0.05, 0.05, 0.02), (0, 0, 0.065), iron, c=0.006, rz=math.pi / 4)
beam(f, F, (GX, GY - 0.25, GZ), (GX, GY + 0.2, GZ), 0.017, iron, segs=6)
for y in (GY - 0.15, GY + 0.15):
    for sx in (-1, 1):
        beam(f, F, (GX + sx * 0.3, y, 0.12), (GX + sx * 0.04, y, GZ - 0.03), 0.024, trim, segs=4)
    cbox(f, F, (0.52, 0.04, 0.04), (GX, y, 0.3), trim, c=0.008)
    cbox(f, F, (0.11, 0.07, 0.08), (GX, y, GZ), plank2, c=0.01)
for sy in (-1, 1):
    cbox(f, F, (0.5, 0.02, 0.2), (GX, GY + sy * 0.075, 0.22), plank, c=0.006)
for sx in (-1, 1):
    cbox(f, F, (0.02, 0.17, 0.2), (GX + sx * 0.25, GY, 0.22), plank2, c=0.006)
box(f, F, (0.48, 0.13, 0.02), (GX, GY, 0.31), water)
cbox(f, F, (0.026, 0.02, 0.14), (GX, GY - 0.255, GZ - 0.06), iron, c=0.005)  # the crank, its grip
beam(f, F, (GX, GY - 0.255, GZ - 0.12), (GX, GY - 0.36, GZ - 0.12), 0.008, iron, segs=4)
cyl(f, F, 0.02, 0.02, 0.07, (GX, GY - 0.33, GZ - 0.12), plank2, segs=6, rx=math.pi / 2)
cbox(f, F, (0.07, 0.07, 0.05), (GX - 0.36, GY - 0.32, 0.14), trim, c=0.008)  # the treadle on its block, a rod up to the crank
cbox(f, F, (0.66, 0.1, 0.025), (GX - 0.02, GY - 0.32, 0.19), plank, c=0.006, ry=-math.atan2(0.08, 0.66))
beam(f, F, (GX, GY - 0.3, 0.2), (GX, GY - 0.27, GZ - 0.12), 0.01, trim, segs=4)
# A barrel of coal by the forge, a shovel in it.
CX, CY = -1.72, 0.12
vkit.barrel2(f, F, 0.21, 0.56, (CX, CY, 0.115), staves=12, head=False, inset=0.07)
for q in range(13):
    rr, a = 0.13 * math.sqrt(R.random()), R.uniform(0, TAU)
    lump(f, F, (CX + rr * math.cos(a), CY + rr * math.sin(a), 0.62 + 0.05 * (1 - rr / 0.13)), R.uniform(0.035, 0.05), ember)
SH = turned(F, (CX + 0.04, CY - 0.03, 0.66), rx=0.35, ry=0.25)
cbox(f, SH, (0.13, 0.012, 0.15), (0, 0, -0.02), iron, c=0.004)
beam(f, SH, (0, 0, 0.05), (0, 0, 0.5), 0.015, plank2, segs=6)
torus(f, SH.p((0, 0, 0.56)), SH.m((0, 0, 0)).to_3x3() @ Vector((0, 1, 0)), 0.045, 0.01, plank2, segs=8, tsegs=3)
# Firewood stacked against the back, between two stakes, its cut ends out.
for x in (0.56, 1.54):
    cbox(f, F, (0.05, 0.05, 0.62), (x, 1.76, 0.31), trim, c=0.01)
for k in range(4):
    for i in range(8 - k % 2):
        r = R.uniform(0.052, 0.062)
        lathe(f, F, [(0, 0), (r, 0), (r, 0.2), (0, 0.2)], (0.63 + i * 0.124 + (k % 2) * 0.062, 1.66 + R.uniform(0, 0.012), 0.062 + k * 0.116), bark,
              segs=6, rx=-math.pi / 2, ry=R.uniform(0, 1), mf=lambda i, j, m=(plank, plank2)[R.random() < 0.5]: bark if i == 1 else m)
# The weapon rack on the back wall: three rails of seven slots (two pegs a slot), one for every weapon the shop sells; the mod
# hangs a common of each on them (Village.cs WeaponRack; keep in step), its price board over them. A brass plate under each slot.
RACK_X, RACK_Z = tuple(-0.15 + k * 0.33 for k in range(7)), (2.4, 1.85, 1.3)
for z in RACK_Z:
    cbox(f, F, (2.4, 0.1, 0.08), (0.84, 1.36, z), trim, c=0.014)
    for x in RACK_X:
        box(f, F, (0.07, 0.006, 0.026), (x, 1.307, z - 0.006), brass)
        for dx in (-0.1, 0.1):
            beam(f, F, (x + dx, 1.33, z), (x + dx, 1.17, z + 0.04), 0.014, iron, segs=6)
cbox(f, F, (0.07, 0.08, 1.42), (-0.4, 1.37, 1.86), trim, c=0.012)
# The sign hanging from the front beam on two short chains: an iron-bound board, an anvil with a hammer and a gun crossed over it.
SN = Frame((0, -1.35, 2.13))
for x in (-0.44, 0.44):
    torus(f, SN.p((x, 0, 0.225)), (1, 0, 0), 0.014, 0.004, iron, segs=6, tsegs=3)
    chain(f, SN, (x, 0, 0.215), (x, 0, 0.178), iron, link=0.03, wire=0.004)
for k, z in enumerate((-0.085, 0.085)):
    cbox(f, SN, (1.1, 0.045, 0.168), (0, 0, z), plank if k else plank2, c=0.01)
for z in (-0.177, 0.177):
    cbox(f, SN, (1.15, 0.055, 0.024), (0, 0, z), iron, c=0.006)
for x in (-0.564, 0.564):
    cbox(f, SN, (0.024, 0.055, 0.378), (x, 0, 0), iron, c=0.006)
    for z in (-0.12, 0.12):
        box(f, SN, (0.018, 0.06, 0.018), (x * 0.93, 0, z), iron)
# The relief, in layers from the boards out: a hammer and a gun crossed, an anvil in front of them, sparks flying.
SH2 = turned(SN, (0, -0.03, 0.0), ry=-0.35)                                # the hammer, its head up on the right
cbox(f, SH2, (0.5, 0.02, 0.024), (-0.02, 0, 0), plank2, c=0.005)
cbox(f, SH2, (0.05, 0.026, 0.12), (0.235, -0.003, 0.0), steel, c=0.007)
cbox(f, SH2, (0.03, 0.022, 0.05), (0.235, -0.003, -0.075), steel, c=0.005)
SG2 = turned(SN, (0, -0.036, 0.0), ry=0.35)                                # the gun, its muzzle up on the left
cbox(f, SG2, (0.29, 0.032, 0.028), (-0.135, 0, 0.012), iron, c=0.006)
cbox(f, SG2, (0.02, 0.036, 0.036), (-0.27, 0, 0.012), iron, c=0.005)
box(f, SG2, (0.018, 0.036, 0.032), (-0.11, 0, 0.012), brass)
plate(f, SG2, [(-0.02, 0.026), (0.06, 0.02), (0.24, 0.012), (0.25, -0.05), (0.2, -0.052), (0.08, -0.016), (0.02, -0.02), (-0.02, -0.004)], 0.016,
      0.032, plank)                                                         # its stock
plate(f, SN, [(-0.2, -0.03), (-0.13, -0.045), (-0.07, -0.06), (-0.05, -0.09), (-0.08, -0.12), (-0.1, -0.135), (0.12, -0.135), (0.1, -0.12),
              (0.07, -0.09), (0.08, -0.06), (0.16, -0.05), (0.17, -0.02), (-0.1, -0.02), (-0.13, -0.025)], -0.02, 0.042, anvilM)  # the anvil
box(f, SN, (0.27, 0.046, 0.008), (0.035, -0.041, -0.016), steel)          # its face
for x, z, r in ((-0.33, 0.07, 0.035), (-0.42, -0.02, 0.025), (0.34, 0.1, 0.03), (0.41, 0.0, 0.022), (0.3, -0.07, 0.018)):   # sparks
    plate(f, SN, [(x + r * (1 if q % 2 == 0 else 0.35) * math.cos(q * math.pi / 4), z + r * (1 if q % 2 == 0 else 0.35) * math.sin(q * math.pi / 4))
                  for q in range(8)], -0.02, 0.014, brass)
# A lantern on an iron arm off the front right post.
box(f, F, (0.03, 0.3, 0.03), (W / 2, -1.58, 2.2), iron)
beam(f, F, (W / 2, -1.44, 2.02), (W / 2, -1.7, 2.2), 0.01, iron, segs=4)
torus(f, (W / 2, -1.7, 2.17), (1, 0, 0), 0.018, 0.005, iron, segs=8, tsegs=3)
vkit.lantern2(f, F, (W / 2, -1.7, 1.87), 0.9)
out.append(f.finish())

export(out)
