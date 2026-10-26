# Mortar turret, art pass 5 (tools/t5kit.py; Owen's mortar sheet /tmp/t5/full/12.png). Plinth: sheet cell 1, a foot slab,
# a tapered brick body, a projecting ledge and a narrower capital (iron corner caps from Lv2, iron straps from Lv5); the fat
# iron tube on an iron yoke with a brass elevating gear, drawn at this size and exported SC bigger so it reads big against
# its plinth. Classes (TURRET-UPGRADE-DESIGN.md §4): A Firestorm (red fuel canister, a burning bore) -> A1 Napalm (big side
# fuel tank, burning ring at the muzzle, fuel hoses) / A2 Firestorm (two tubes, one high one low, powder keg behind, black
# smoke hoods); B Barrage (banded tube, ready tray of shells) -> B1 Grand barrage (three-tier shell rack, long loading arm)
# / B2 Cluster shells (wide-mouth tube, a cluster warhead in it, split-shell crate).
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/t5_mortar.py   [ONLY=MortarBase,MortarA ...]
import math
import os
import sys

import bmesh

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from t5kit import *  # noqa: F401,F403
from t5kit import _loft, _put  # noqa: E402

H = 0.80          # head pivot: the top of the plinth
SC = 1.2          # the head parts' scale (the mortar sheet: the tube about three quarters of the capital's width)
# the head parts, in their drawn size:
TR = 0.36         # the trunnions above the pivot
TILT = math.radians(50)
TOP = 0.05        # the platform's top, where the pods stand
BED = 0.10        # the yoke's bed on it
PW = 0.38         # the platform's half width (flush with the capital once scaled)
INNER, OUTER = 0.2, 0.25        # the cheeks
GX = OUTER + 0.024              # the elevating gear's plane, outside a cheek
POD = (0.33 * SC, TOP * SC, 0.12 * SC)     # extra tubes (Unity head space): outside the gear, rows going back
STEP = (0.0, 0.0, -0.16 * SC)

# the plinth (static, real size): sheet cell 1
FOOT, BW0, BW1, LEDGE, CAP = 0.56, 0.52, 0.47, 0.54, 0.46   # half widths: foot slab, the body's foot and top, ledge, capital
ZF, ZB, ZL = 0.10, 0.46, 0.58                               # the tops of the foot slab, the body and the ledge
BODY = ((ZF, 0.22), (0.22, 0.34), (0.34, ZB))
CUT = 0.07


def hw_at(z):
    # The plinth's half width at height z.
    if z <= ZF:
        return FOOT
    if z <= ZB:
        return BW0 - (BW0 - BW1) * (z - ZF) / (ZB - ZF)
    return LEDGE if z <= ZL else CAP


def face_n():
    # The tapered body's front face normal (it leans in).
    return Vector((0, -(ZB - ZF), BW0 - BW1)).normalized()


# ---------------------------------------------------------------- local helpers
def lathe(b, prof, M, m=None, segs=12, bore=None, bore_m=None):
    # A solid of revolution round local Z, prof = [(z, r), ...] from the back to the muzzle; bore = (r, depth) opens the
    # muzzle face onto a dark (or burning, bore_m) bore.
    m = m or IRON
    tmp = bmesh.new()
    mi = b.mi(m)
    rings = [circle(r, segs, z) if r > 1e-4 else [(0, 0, z)] for z, r in prof]
    mats, caps = [mi] * (len(rings) - 1), (mi, mi)
    if bore:
        ri, d = bore
        zf = prof[-1][0]
        dm = b.mi(bore_m or DARK)
        rings += [circle(ri, segs, zf), circle(ri, segs, zf - d)]
        mats += [mi, dm]
        caps = (mi, dm)
    _loft(tmp, rings, mats, cap_mats=caps)
    _put(b, tmp, M)


def slab(b, pts_yz, x0, x1, m, M=I4):
    # A flat plate: an outline in y/z pushed out along x from x0 to x1 (the yoke's cheeks).
    tmp = bmesh.new()
    _loft(tmp, [[(x0, y, z) for y, z in pts_yz], [(x1, y, z) for y, z in pts_yz]], [0])
    _put(b, tmp, M, m)


def cog(b, r, w, M, teeth=8, spokes=6, m=None):
    # A cheap gear facing +X (t5kit.gear's look in a third of the triangles): a broad rim, short teeth, spokes, a hub.
    m = m or BRASS
    ring(b, r * 0.86, r * 0.6, w, (0, 0, 0), m, segs=teeth * 2, M=M, axis="X")
    for k in range(teeth):
        a = (k + 0.5) * TAU / teeth
        box(b, (w, r * 0.22, r * 0.26), (0, math.cos(a) * r * 0.9, math.sin(a) * r * 0.9), m, ch=0.0, rot=(a - math.pi / 2, 0, 0), M=M)
    for k in range(spokes):
        a = k * TAU / spokes
        box(b, (w * 0.8, r * 0.17, r * 0.42), (0, math.cos(a) * r * 0.42, math.sin(a) * r * 0.42), m, ch=0.0,
            rot=(a - math.pi / 2, 0, 0), M=M)
    cyl(b, r * 0.27, w * 1.4, (0, 0, 0), m, segs=8, axis="X", ch=0.005, M=M)
    cyl(b, r * 0.12, w * 1.8, (0, 0, 0), IRON, segs=6, axis="X", ch=0.0, M=M)


def tube_frame(x=0.0, y=0.0, z=TR, tilt=TILT):
    # The frame of a tube on trunnions at (x, y, z), local +Z up the bore towards the front.
    F, _ = toward((x, y, z), (x, y - math.cos(tilt), z + math.sin(tilt)))
    return F


# the tube's outline (along its axis from the trunnions, radius): a knob at the breech, the chase, the swell and the lip
TUBE = ((-0.34, 0.0), (-0.33, 0.055), (-0.30, 0.065), (-0.28, 0.11), (-0.25, 0.17), (-0.2, 0.2), (0.2, 0.2), (0.23, 0.215),
        (0.42, 0.215), (0.44, 0.245), (0.52, 0.245))


def tube(b, F, s=1.0, bands=(-0.13, 0.2), bore_m=None, prof=TUBE, bore=(0.165, 0.34), segs=12):
    lathe(b, [(z * s, r * s) for z, r in prof], F, IRON, segs=segs, bore=(bore[0] * s, bore[1] * s), bore_m=bore_m)
    for z in bands:
        ring(b, 0.218 * s, 0.19 * s, 0.04 * s, (0, 0, z * s), BRASS, segs=segs, M=F)


def cheek(b, x0, x1, top=TR + 0.09, foot=0.25, edges=True):
    # One iron cheek of the yoke on the bed, brass strips along its slanted edges, a brass cap on top.
    prof = [(foot, BED), (-foot, BED), (-0.1, top), (0.1, top)]
    slab(b, prof, x0, x1, IRON)
    s = 1 if x0 + x1 > 0 else -1
    xo = x1 if s > 0 else x0
    if edges:
        for (ya, za), (yb, zb) in ((prof[1], prof[2]), (prof[0], prof[3])):
            d = Vector((0, yb - ya, zb - za)).normalized()
            n = Vector((0, -d.z, d.y)) if ya < 0 else Vector((0, d.z, -d.y))   # into the cheek
            a = Vector((0, ya, za)) + d * 0.05 + n * 0.026
            c = Vector((0, yb, zb)) - d * 0.03 + n * 0.026
            bar(b, (xo + s * 0.006, a.y, a.z), (xo + s * 0.006, c.y, c.z), 0.04, 0.012, BRASS, ch=0.0, up=(1, 0, 0))
    box(b, (x1 - x0 + 0.02, 0.22, 0.03), ((x0 + x1) / 2, 0, top + 0.005), BRASS, ch=0.006)


def yoke(b, gear=1):
    # Two iron cheeks on the bed with iron feet, the trunnion pin, the brass elevating gear outside one cheek (sheet cell 1;
    # gear 1 the right, -1 the left) and a big brass cap on the other (sheet cell 6).
    for s in (-1, 1):
        x0, x1 = sorted((s * INNER, s * OUTER))
        cheek(b, x0, x1)
        for y in (-0.205, 0.205):   # its feet
            box(b, (OUTER - INNER + 0.03, 0.08, 0.07), (s * (INNER + OUTER) / 2, y, BED + 0.035), IRON, ch=0.01)
            sqstud(b, (s * (INNER + OUTER) / 2, y + (0.04 if y > 0 else -0.04), BED + 0.035), (0, 1 if y > 0 else -1, 0),
                   s=0.035, m=BRASS)
    cyl(b, 0.05, 2 * OUTER + 0.02, (0, 0, TR), IRON, segs=8, axis="X", ch=0.006)
    cyl(b, 0.085, 0.03, (-gear * (OUTER + 0.015), 0, TR), BRASS, segs=10, axis="X", ch=0.008)
    cyl(b, 0.035, 0.02, (-gear * (OUTER + 0.038), 0, TR), IRON, segs=6, axis="X", ch=0.0)
    cog(b, 0.14, 0.02, T(gear * GX, 0, TR))


def shell(b, loc, r=0.06, m=IRON, band=BRASS, up=(0, 0, 1), segs=6, rnd=False):
    # A mortar shell (sheet cell 6): a faceted egg (rnd: with shoulders) with a brass band round its waist.
    F, _ = toward(loc, Vector(loc) + Vector(up))
    gem(b, r, r * 1.35, r * 0.9, (0, 0, 0), m, segs=segs, M=F, mid=(0.8, r * 0.55) if rnd else None)
    cyl(b, r * 1.03, r * 0.32, (0, 0, -r * 0.08), band, segs=segs, ch=0.0, M=F)


def keg(b, loc, r=0.1, h=0.22, m=None, hoops=IRON, axis="Z"):
    # A wooden keg: a bellied barrel with two iron hoops.
    F = T(*loc) @ {"Z": I4, "X": Ry(math.pi / 2), "Y": Rx(math.pi / 2)}[axis]
    lathe(b, [(-h / 2, r * 0.85), (-h / 4, r), (h / 4, r), (h / 2, r * 0.85)], F, m or WOOD, segs=10)
    for z in (-h * 0.3, h * 0.3):
        ring(b, r * 1.02, r * 0.9, 0.025, (0, 0, z), hoops, segs=10, M=F)


def drum(b, loc, r=0.12, h=0.3, m=None):
    # An upright fuel drum: red, two brass rings, a brass cap.
    x, y, z = loc
    cyl(b, r, h, (x, y, z + h / 2), m or RED, segs=10, ch=0.015)
    for k in (0.3, 0.7):
        ring(b, r * 1.03, r * 0.95, 0.025, (x, y, z + h * k), BRASS, segs=10)
    cyl(b, r * 0.25, 0.03, (x + r * 0.45, y, z + h + 0.012), BRASS, segs=6, ch=0.005)


def seam(b, u, z0, z1, face, lo=0.014, hi=0.02):
    # A mortar joint up a course of the plinth on face 0..3 (0 = the front), u across it.
    za, zc = z0 + lo, z1 - hi
    bar(b, (u, -(hw_at(za) + 0.002), za), (u, -(hw_at(zc) + 0.002), zc), 0.016, 0.008, STONE2, ch=0.0,
        M=Rz(face * math.pi / 2), up=(0, -1, 0))


# ---------------------------------------------------------------- plinth (static): sheet cell 1
def base(b):
    tier(b, FOOT, FOOT, CUT, 0.0, ZF, STONE, ch=0.025, chb=0.012)                               # foot slab
    for z0, z1 in BODY:                                                                         # the tapered body
        tier(b, hw_at(z0), hw_at(z0), CUT - 0.01, z0, z1, STONE, ch=0.018, taper=hw_at(z0) - hw_at(z1))
    tier(b, LEDGE, LEDGE, CUT, ZB, ZL, STONE, ch=0.025, chb=0.025)                             # ledge
    tier(b, CAP, CAP, 0.06, ZL, 0.69, STONE, ch=0.016)                                         # capital
    tier(b, CAP, CAP, 0.06, 0.69, H, STONE, ch=0.025)
    for face in range(4):
        for u in (-0.26, 0.26):
            seam(b, u, 0.0, ZF, face, 0.016, 0.03)
        for k, (z0, z1) in enumerate(BODY):
            for u in ((-0.17, 0.17) if k % 2 == 0 else (-0.32, 0.0, 0.32)):
                seam(b, u, z0, z1, face)
        for u in (-0.21, 0.21):
            seam(b, u, ZB, ZL, face, 0.032, 0.032)
        for u in (-0.2, 0.2):
            seam(b, u, ZL, 0.69, face, 0.008, 0.018)
        seam(b, 0.0, 0.69, H, face, 0.008, 0.028)


# ---------------------------------------------------------------- platform (turns)
def body(b):
    # the iron platform on the capital (sheet cell 1), square brass studs round its rim; the pods stand on it
    tier(b, PW, PW, 0.045, 0.0, TOP, IRON, ch=0.012)
    for face in range(4):
        for u in (-0.21, 0.0, 0.21):
            sqstud(b, (u, -PW, TOP / 2), (0, -1, 0), s=0.03, m=BRASS, M=Rz(face * math.pi / 2))
    box(b, (0.5, 0.52, BED - TOP), (0, 0, (TOP + BED) / 2), IRON, ch=0.012)                   # the yoke's bed


# ---------------------------------------------------------------- heads (turn)
def default(b):
    yoke(b)
    tube(b, tube_frame())


def hose(b, pts, r=0.022, m=None):
    for p, q in zip(pts, pts[1:]):
        rod(b, p, q, r, m or IRON, segs=6)


def saddles(b, x, z, r, ys, m=IRON, post=0.16):
    # Saddle straps off the right cheek's cap holding a tank (along Y at x, z, radius r) up over the pods, an iron post
    # under its back end down to the platform (behind the one pod the A path puts on this side).
    for y in ys:
        bar(b, (INNER, y, TR + 0.1), (x, y, TR + 0.1), 0.035, 0.025, m, ch=0.0)
        ring(b, r + 0.012, r - 0.004, 0.03, (x, y, z), m, segs=10, axis="Y")
    box(b, (0.05, 0.05, z - r - TOP + 0.01), (x, post, (TOP + z - r) / 2), IRON, ch=0.008)
    box(b, (0.09, 0.09, 0.02), (x, post, TOP + 0.01), IRON, ch=0.005)


def firestorm(b):
    # The gear goes left: the red fuel canister rides the right cheek (above the pods), a hose into the breech.
    yoke(b, gear=-1)
    tube(b, tube_frame(), bore_m=FIRE)
    x, z = 0.335, TR + 0.17
    cyl(b, 0.075, 0.3, (x, 0.03, z), RED, segs=8, axis="Y", ch=0.012)
    for y in (-0.08, 0.14):
        ring(b, 0.08, 0.07, 0.025, (x, y, z), BRASS, segs=8, axis="Y")
    cyl(b, 0.03, 0.03, (x, -0.135, z), BRASS, segs=6, axis="Y", ch=0.0)
    cyl(b, 0.035, 0.012, (x, -0.02, z + 0.08), BRASS, segs=8, ch=0.0)            # its gauge
    saddles(b, x, z, 0.075, (-0.04, 0.1), IRON)
    hose(b, [(x, 0.18, z), (x * 0.85, 0.3, TR), (0.1, 0.3, 0.2), (0.02, 0.24, 0.14)])


def napalm(b):
    yoke(b, gear=-1)
    F = tube_frame()
    tube(b, F, bore_m=FIRE)
    # the burning ring round the muzzle and its tongues of flame
    ring(b, 0.28, 0.245, 0.05, (0, 0, 0.49), RED, segs=12, M=F)
    for k in range(6):
        a = k * TAU / 6
        gem(b, 0.032, 0.1, 0.015, (math.cos(a) * 0.262, math.sin(a) * 0.262, 0.52), FIRE, segs=5, M=F)
    # the big napalm tank on the right cheek, above the pods, its burner line up to the ring
    x, z = 0.36, TR + 0.19
    cyl(b, 0.11, 0.46, (x, 0.04, z), RED, segs=10, axis="Y", ch=0.015)
    cyl(b, 0.04, 0.05, (x, 0.1, z + 0.12), BRASS, segs=6, ch=0.008)
    cyl(b, 0.035, 0.03, (x, -0.2, z), BRASS, segs=6, axis="Y", ch=0.0)
    saddles(b, x, z, 0.11, (-0.1, 0.06, 0.2), BRASS)
    hose(b, [(x, 0.27, z), (x * 0.85, 0.33, TR - 0.02), (0.1, 0.3, 0.2), (0.02, 0.24, 0.14)], r=0.026)
    hose(b, [(x - 0.03, -0.2, z + 0.05), (0.31, -0.25, 0.66), (0.27, -0.29, 0.73)], r=0.016, m=BRASS)


def smoke_tube(b, F, s):
    tube(b, F, s=s, bore_m=FIRE)
    # the smoke hood: a flared sooty bell over the muzzle, vents round it
    lathe(b, [(0.44 * s, 0.255 * s), (0.54 * s, 0.3 * s), (0.6 * s, 0.3 * s)], F, IRON, segs=12, bore=(0.25 * s, 0.12 * s))
    ring(b, 0.305 * s, 0.27 * s, 0.03 * s, (0, 0, 0.585 * s), DARK, segs=12, M=F)
    for k in range(6):
        a = k * TAU / 6
        box(b, (0.03 * s, 0.05 * s, 0.07 * s), (math.cos(a) * 0.24 * s, math.sin(a) * 0.24 * s, 0.36 * s), DARK, ch=0.0,
            rot=(0, 0, a), M=F)


def firestorm2(b):
    # Two tubes in a three-cheek yoke, a big one on high trunnions and a small one on low; a powder keg behind.
    hi, lo = TR + 0.07, TR - 0.06
    cheek(b, -OUTER, -INNER, hi + 0.08, 0.24)
    cheek(b, INNER, OUTER, lo + 0.08, 0.24)
    cheek(b, -0.015, 0.015, hi + 0.08, 0.24, edges=False)
    cyl(b, 0.045, 0.25, (-0.12, 0, hi), IRON, segs=8, axis="X", ch=0.006)
    cyl(b, 0.04, 0.25, (0.12, 0, lo), IRON, segs=8, axis="X", ch=0.006)
    cyl(b, 0.07, 0.025, (-(OUTER + 0.012), 0, hi), BRASS, segs=10, axis="X", ch=0.006)
    cyl(b, 0.06, 0.02, (OUTER + 0.01, 0, lo), BRASS, segs=10, axis="X", ch=0.005)
    smoke_tube(b, tube_frame(x=-0.11, z=hi, tilt=math.radians(62)), 0.5)
    smoke_tube(b, tube_frame(x=0.11, z=lo, tilt=math.radians(40)), 0.45)
    keg(b, (0.0, 0.31, TOP + 0.11), r=0.075, h=0.22)
    cyl(b, 0.018, 0.04, (0.0, 0.31, TOP + 0.235), BRASS, segs=6, ch=0.0)
    rod(b, (0.0, 0.31, TOP + 0.25), (0.03, 0.27, TOP + 0.32), 0.007, IRON, segs=4)     # its fuse
    gem(b, 0.014, 0.04, 0.005, (0.03, 0.27, TOP + 0.325), FIRE, segs=4)


def ready_tray(b, y=-0.32, n=4):
    # A low wooden tray of shells standing ready in front, under the muzzle.
    box(b, (0.42, 0.1, 0.03), (0, y, TOP + 0.015), WOOD, ch=0.006)
    box(b, (0.42, 0.02, 0.07), (0, y - 0.05, TOP + 0.05), WOOD2, ch=0.004)
    for k in range(n):
        shell(b, (-0.15 + k * 0.1, y, TOP + 0.075), r=0.042, rnd=True)


def barrage(b):
    yoke(b)
    tube(b, tube_frame(), bands=(-0.13, 0.04, 0.2, 0.33))
    ready_tray(b)


def grand_barrage(b):
    yoke(b)
    tube(b, tube_frame(), bands=(-0.13, 0.04, 0.2, 0.33))
    # the three-tier shell rack behind
    y = 0.33
    for s in (-1, 1):
        box(b, (0.035, 0.11, 0.74), (s * 0.215, y, TOP + 0.37), WOOD2, ch=0.006)
    for k in range(3):
        z = TOP + 0.08 + k * 0.23
        box(b, (0.46, 0.11, 0.03), (0, y, z), WOOD, ch=0.006)
        for j in range(3):
            shell(b, (-0.13 + j * 0.13, y, z + 0.015 + 0.042 * 0.9), r=0.042)
    # the loading crane on top: a post, a braced boom over the tube, a shell hanging on its cable in front of the muzzle
    top = TOP + 0.74
    box(b, (0.47, 0.12, 0.04), (0, y, top), IRON, ch=0.008)
    box(b, (0.05, 0.05, 0.3), (0, y, top + 0.17), IRON, ch=0.008)
    bar(b, (0, y + 0.02, top + 0.3), (0, -0.44, top + 0.3), 0.045, 0.045, IRON, ch=0.006)
    bar(b, (0, y, top + 0.04), (0, y - 0.24, top + 0.28), 0.03, 0.03, IRON, ch=0.0)
    for p in ((0, y, top + 0.3), (0, -0.44, top + 0.3)):
        cyl(b, 0.035, 0.07, p, BRASS, segs=8, axis="X", ch=0.006)
    rod(b, (0, -0.44, top + 0.27), (0, -0.44, top + 0.17), 0.007, IRON, segs=4)
    shell(b, (0, -0.44, top + 0.08), r=0.045, rnd=True)


WIDE = ((-0.34, 0.0), (-0.33, 0.055), (-0.3, 0.065), (-0.28, 0.11), (-0.25, 0.17), (-0.2, 0.2), (0.1, 0.2), (0.2, 0.23),
        (0.36, 0.29), (0.43, 0.32), (0.52, 0.32))


def cluster(b):
    yoke(b)
    F = tube_frame()
    lathe(b, WIDE, F, IRON, segs=12, bore=(0.265, 0.24))
    for z, r in ((-0.13, 0.2), (0.12, 0.215)):
        ring(b, r + 0.018, r - 0.01, 0.04, (0, 0, z), BRASS, segs=12, M=F)
    ring(b, 0.335, 0.3, 0.045, (0, 0, 0.47), BRASS, segs=12, M=F)
    # the cluster warhead seated in the bore: bomblets round a brass nose
    gem(b, 0.08, 0.13, 0.02, (0, 0, 0.42), BRASS, segs=8, M=F)
    for k in range(6):
        a = k * TAU / 6
        ball(b, 0.06, (math.cos(a) * 0.16, math.sin(a) * 0.16, 0.38), IRON, M=F)
    # the split-shell crate behind, open on halved shells
    crate(b, (0.42, 0.11, 0.13), M=T(0, 0.315, TOP), planks=1, lid=False)
    for k in range(3):
        x = -0.12 + k * 0.12
        gem(b, 0.045, 0.06, 0.03, (x, 0.315, TOP + 0.15), IRON, segs=6)
        box(b, (0.1, 0.012, 0.05), (x, 0.315, TOP + 0.15), BRASS, ch=0.0)


# ---------------------------------------------------------------- extra tube (turns, on the platform)
def pod(b):
    # rear-bottom-centre at the origin, pointing -Y: a small mortar in its own little yoke on an iron block.
    box(b, (0.1, 0.15, 0.045), (0, -0.075, 0.0225), IRON, ch=0.008)
    for s in (-1, 1):
        slab(b, [(-0.14, 0.045), (-0.01, 0.045), (-0.05, 0.14), (-0.1, 0.14)], min(s * 0.04, s * 0.05), max(s * 0.04, s * 0.05),
             IRON)
        cyl(b, 0.02, 0.012, (s * 0.056, -0.075, 0.115), BRASS, segs=6, axis="X", ch=0.0)
    F, _ = toward((0, -0.075, 0.115), (0, -0.075 - math.cos(TILT), 0.115 + math.sin(TILT)))
    prof = [(-0.09, 0.0), (-0.08, 0.026), (-0.06, 0.04), (0.06, 0.04), (0.075, 0.05), (0.12, 0.05)]
    lathe(b, prof, F, IRON, segs=8, bore=(0.034, 0.09))
    ring(b, 0.045, 0.036, 0.016, (0, 0, 0.0), BRASS, segs=8, M=F)


# ---------------------------------------------------------------- one piece a level (static, round the plinth)
def corner_cap(b, u, v, z0, z1, hw, cut=CUT, studs=True):
    # An iron cap round one plinth corner (sheet cell 1): a plate on the cut corner, one on each face beside it.
    h, zc = z1 - z0, (z0 + z1) / 2
    plate(b, 0.12, h, 0.035, (u * (hw - cut / 2), v * (hw - cut / 2), zc), (u, v, 0), IRON, ch=0.0)
    for nx, ny in ((u, 0), (0, v)):
        px = u * hw if nx else u * (hw - cut - 0.075)
        py = v * hw if ny else v * (hw - cut - 0.075)
        plate(b, 0.15, h, 0.03, (px, py, zc), (nx, ny, 0), IRON, ch=0.0)
        if studs:
            sqstud(b, (px + nx * 0.03, py + ny * 0.03, zc), (nx, ny, 0), s=0.05, m=BRASS)


def kit2(b):   # Lv2: iron caps round the foot slab's and the ledge's corners (sheet cell 1)
    for u, v in CORNERS4:
        corner_cap(b, u, v, 0.0, ZF + 0.02, FOOT)
        corner_cap(b, u, v, ZB + 0.004, ZL - 0.004, LEDGE)


def kit3(b):   # Lv3: a brass band round the ledge, two rivets on each face
    tier(b, LEDGE + 0.006, LEDGE + 0.006, CUT + 0.003, ZL - 0.065, ZL - 0.035, BRASS, ch=0.0)
    for face in range(4):
        for u in (-0.2, 0.2):
            stud(b, (u, -(LEDGE + 0.006), ZL - 0.05), (0, -1, 0), r=0.018, M=Rz(face * math.pi / 2))


# Where the levels' pieces stand (the 3/4 view sees the front and the right): Lv4 against the right face's front half,
# Lv6 before the front face's left half, the pennant at the right face's back half; a final class's dressing takes over
# the first two.
KIT4 = (FOOT + 0.1, -0.14)
KIT6 = (-0.18, -(FOOT + 0.09))


def kit4(b):   # Lv4: a crate of shells against the right of the foot (sheet cells 2 / 7)
    x, y = KIT4
    crate(b, (0.2, 0.32, 0.22), M=T(x, y, 0), planks=2, lid=False)
    for dy, z in ((-0.07, 0.0), (0.07, 0.0), (0.0, 0.075)):
        ball(b, 0.062, (x, y + dy, 0.235 + z), IRON)


def kit5(b):   # Lv5: riveted iron straps up the body's faces, right of the middle (sheet cell 1)
    n = face_n()
    zc = (ZF + ZB) / 2
    length = (ZB - ZF - 0.02) / abs(n.y)
    for face in range(4):
        M = Rz(face * math.pi / 2)
        plate(b, 0.11, length, 0.03, (0.26, -(hw_at(zc) + 0.002), zc), n, IRON, ch=0.006, M=M)
        for z in (ZF + 0.06, zc, ZB - 0.06):
            p = Vector((0.26, -(hw_at(z) + 0.002), z)) + n * 0.03
            sqstud(b, tuple(p), tuple(n), s=0.045, m=BRASS, M=M)


def kit6(b):   # Lv6: shells on a pallet before the front face (sheet cell 6)
    x, y = KIT6
    box(b, (0.3, 0.18, 0.03), (x, y, 0.015), WOOD2, ch=0.005)
    for dx in (-0.09, 0.0, 0.09):
        shell(b, (x + dx, y, 0.03 + 0.065 * 0.9), r=0.058, rnd=True)
    shell(b, (x - 0.045, y + 0.01, 0.17), r=0.05, up=(1, -0.3, 0.25), rnd=True)


def kit7(b):   # Lv7: the turret's pennant on the right, the cloth flying back towards the 3/4 view, below the head's sweep
    px, py = FOOT + 0.035, 0.06
    pennant(b, H - 0.08, CLOTH["Mortar"], M=T(px, py, 0.0) @ Rz(math.radians(70)))
    box(b, (0.09, 0.09, 0.03), (px, py, 0.015), IRON, ch=0.006)
    for z, x0 in ((0.06, FOOT), (0.52, LEDGE)):
        bar(b, (x0 - 0.01, py, z), (px, py, z), 0.035, 0.035, IRON, ch=0.0)


def kit8(b):   # Lv8: the brass crest (a shell and its burning fuse) on the front, brass knobs on the ledge's corners
    n = face_n()
    zc = 0.32
    p = Vector((0, -(hw_at(zc) + 0.002), zc))
    plate(b, 0.24, 0.24, 0.025, tuple(p), tuple(n), BRASS, rivets=CORNERS4, rv=0.016, rivet_m=IRON)
    ball(b, 0.065, tuple(p + n * 0.07 + Vector((0, 0, -0.02))), IRON)
    cyl(b, 0.016, 0.04, tuple(p + n * 0.07 + Vector((0.03, 0, 0.05))), BRASS, segs=6, ch=0.0)
    gem(b, 0.025, 0.07, 0.0, tuple(p + n * 0.07 + Vector((0.035, 0, 0.07))), FIRE, segs=5)
    for u, v in CORNERS4:
        ball(b, 0.03, (u * (LEDGE - 0.06), v * (LEDGE - 0.06), ZL + 0.02), BRASS)


# ---------------------------------------------------------------- final classes (static, from Lv9; Kit4 + Kit6 make way)
def dress_a1(b):   # Napalm: red fuel drums before the front, a hose reel on the right, hoses into the plinth
    x, y = KIT6
    drum(b, (x - 0.07, y - 0.02, 0.0), r=0.12, h=0.36)
    drum(b, (x + 0.17, y + 0.0, 0.0), r=0.1, h=0.26)
    hose(b, [(x - 0.03, y + 0.05, 0.38), (x + 0.05, -(hw_at(0.42) + 0.04), 0.46), (x + 0.1, -(hw_at(0.42) + 0.005), 0.42)],
         r=0.02)
    x, y = KIT4
    for dy in (-0.15, 0.15):   # the reel's stand
        slab(b, [(y + dy - 0.02, 0.0), (y + dy + 0.02, 0.0), (y + dy + 0.012, 0.3), (y + dy - 0.012, 0.3)], x - 0.09, x + 0.09,
             WOOD2)
    cyl(b, 0.11, 0.26, (x, y, 0.2), IRON, segs=10, axis="Y", ch=0.01)
    for dy in (-0.07, 0.0, 0.07):
        ring(b, 0.118, 0.095, 0.05, (x, y + dy, 0.2), RED, segs=10, axis="Y")
    cyl(b, 0.025, 0.36, (x, y, 0.2), BRASS, segs=6, axis="Y", ch=0.0)
    hose(b, [(x - 0.09, y, 0.26), (hw_at(0.32) + 0.005, y, 0.32)], r=0.02)


def dress_a2(b):   # Firestorm: powder kegs before the front, a brazier of coals on the right
    x, y = KIT6
    keg(b, (x - 0.1, y, 0.12), r=0.1, h=0.24)
    keg(b, (x + 0.11, y, 0.12), r=0.1, h=0.24)
    keg(b, (x + 0.005, y - 0.005, 0.32), r=0.09, h=0.2, axis="X")
    x, y = KIT4
    x += 0.02
    rtier(b, 0.12, 0.0, 0.06, IRON, segs=8, ch=0.01, M=T(x, y, 0.0))
    for k in range(3):
        a = k * TAU / 3 + math.pi / 6
        bar(b, (x + math.cos(a) * 0.08, y + math.sin(a) * 0.08, 0.06), (x + math.cos(a) * 0.12, y + math.sin(a) * 0.12, 0.25),
            0.03, 0.03, IRON, ch=0.0)
    rtier(b, 0.12, 0.23, 0.31, IRON, segs=8, ch=0.01, r_top=0.14, M=T(x, y, 0.0))
    for k in range(5):
        a = k * TAU / 5
        gem(b, 0.04, 0.11, 0.02, (x + math.cos(a) * 0.06, y + math.sin(a) * 0.06, 0.31), FIRE, segs=5)
    bar(b, (x - 0.12, y, 0.1), (FOOT, y, 0.1), 0.03, 0.03, IRON, ch=0.0)    # tied to the foot


def dress_b1(b):   # Grand barrage: a shell rack before the front, stacked shell crates on the right
    x, y = KIT6
    y -= 0.01
    for s in (-1, 1):
        box(b, (0.04, 0.2, 0.42), (x + s * 0.17, y, 0.21), WOOD2, ch=0.008)
    for z in (0.0, 0.2):
        box(b, (0.38, 0.2, 0.03), (x, y, z + 0.015), WOOD, ch=0.006)
        for k in range(3):
            shell(b, (x - 0.1 + k * 0.1, y, z + 0.03 + 0.042 * 0.9), r=0.042, rnd=True)
    box(b, (0.38, 0.03, 0.04), (x, y - 0.085, 0.4), WOOD2, ch=0.006)
    x, y = KIT4
    crate(b, (0.22, 0.36, 0.2), M=T(x + 0.01, y, 0.0), planks=2)
    crate(b, (0.18, 0.26, 0.15), M=T(x + 0.01, y + 0.02, 0.2) @ Rz(0.06), planks=1)


def dress_b2(b):   # Cluster shells: a pyramid of cluster bombs before the front, an open crate of bomblets on the right
    x, y = KIT6
    box(b, (0.42, 0.18, 0.02), (x, y, 0.01), WOOD2, ch=0.004)
    for dx, z in ((-0.14, 0.0), (0.0, 0.0), (0.14, 0.0), (-0.07, 0.115), (0.07, 0.115), (0.0, 0.23)):
        ball(b, 0.07, (x + dx, y, z + 0.09), IRON)
        ring(b, 0.073, 0.05, 0.022, (x + dx, y, z + 0.09), BRASS, segs=8, axis="X")
    x, y = KIT4
    crate(b, (0.22, 0.38, 0.18), M=T(x + 0.01, y, 0.0), planks=1, lid=False)
    for dx in (-0.05, 0.05):
        for dy in (-0.12, 0.0, 0.12):
            ball(b, 0.045, (x + 0.01 + dx, y + dy, 0.2), IRON)


model("MortarBase", base)
model("MortarBody", body, scale=SC)
model("MortarDefault", default, scale=SC)
model("MortarA", firestorm, scale=SC)
model("MortarA1", napalm, scale=SC)
model("MortarA2", firestorm2, scale=SC)
model("MortarB", barrage, scale=SC)
model("MortarB1", grand_barrage, scale=SC)
model("MortarB2", cluster, scale=SC)
model("MortarBarrel", pod, scale=SC)
for n, f in ((2, kit2), (3, kit3), (4, kit4), (5, kit5), (6, kit6), (7, kit7), (8, kit8)):
    model("MortarKit%d" % n, f)
for n, f in (("A1", dress_a1), ("A2", dress_a2), ("B1", dress_b1), ("B2", dress_b2)):
    model("MortarDress" + n, f)
export_all()
layout("Mortar", head=H, muzzle=round(H + (TR + 0.52 * math.sin(TILT)) * SC, 3), pod=[round(v, 4) for v in POD],
       step=[round(v, 4) for v in STEP])
