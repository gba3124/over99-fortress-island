# Cannon turret, art pass 5 (tools/t5kit.py). Plinth: Owen's pick 1, a round stone drum in staggered block courses (the
# mortar sheet's 8) carrying a wooden deck with one big gun on a stepped carriage. Classes (TURRET-UPGRADE-DESIGN.md §2):
# A Cluster (three short barrels in a row on a cradle, a shell tray) -> A1 Carpet (a battery of upward tubes over it, an
# ammo belt up to them) / A2 Napalm cluster (a red fuel tank on top, copper feeds, glowing nozzles); B Heavy (a thick long
# barrel, recoil cylinders, a heavy breech with a handwheel) -> B1 Siege (longer still, an armoured shield, big spoked
# wheels, a trail spade) / B2 Shockwave (short bell muzzle, a brass blast ring, the barrel on shock springs, low and wide).
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/t5_cannon.py   [ONLY=CannonBase,CannonA ...]
import math
import os
import sys

import bmesh

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from t5kit import *  # noqa: F401,F403
from t5kit import _loft, _put

H = 0.78          # head pivot: the top of the plinth
AX = 0.46         # the gun's axis above the pivot (head parts are drawn at this size, exported SC bigger)
TOP = 0.08        # the deck's top (the carriage and the pods stand on it)
DECK = 0.45       # deck radius (x SC x 1.3 at the highest levels it overhangs the cap a little)
SC = 1.2          # the head parts' scale (the gun reads big against its plinth)
PX, PW, PD, PH = 0.36, 0.12, 0.22, 0.15   # extra barrel pods: |x| of their centre, width, depth, height (= the stacking step)
POD = (PX * SC, TOP * SC, -PD / 2 * SC)   # (Unity head space) beside the carriage, on the deck, centred on the pivot
STEP = (0.0, PH * SC, 0.0)                # each further pair stands on the one before (four high on the Carpet at Lv15)

FOOT, DRUM, CAP = 0.58, 0.45, 0.56   # radii: the foot, the block faces, the cap (a cornice the deck sits on)


# ---------------------------------------------------------------- local shapes
def profile(b, pts, x=0.0, z=0.0, bore=None, segs=10, M=I4, bm=None):
    # A turned shape along the front (-Y): pts = [(d forward, r, m), ...] from the closed back to the front, m painting the
    # band to the next ring; bore = (r, depth) leaves the front open with a bore (bm, dark by default).
    tmp = bmesh.new()
    rings = [[(0, 0, d)] if r <= 0 else circle(r, segs, d) for d, r, _ in pts]
    mats = [b.mi(m) for _, _, m in pts[:-1]]
    caps = (b.mi(pts[0][2]), b.mi(pts[-1][2]))
    if bore:
        ri, depth = bore
        d = pts[-1][0]
        depth = min(depth, (d - pts[0][0]) * 0.9)
        rings += [circle(ri, segs, d), circle(ri, segs, d - depth)]
        mats += [b.mi(pts[-1][2]), b.mi(bm or DARK)]
        caps = (caps[0], b.mi(bm or DARK))
    _loft(tmp, rings, mats, cap_mats=caps)
    _put(b, tmp, M @ T(x, 0, z) @ Rx(math.pi / 2))


def balls(b, pts, r, m=None):
    for p in pts:
        ball(b, r, p, m or IRON)


def keg(b, M, r=0.1, h=0.24, m=None):
    # A powder keg: bellied staves, an iron hoop near each end.
    tmp = bmesh.new()
    rings = [circle(r * 0.86, 8, 0.0), circle(r, 8, h * 0.3), circle(r, 8, h * 0.7), circle(r * 0.86, 8, h)]
    _loft(tmp, rings, [0])
    _put(b, tmp, M, m or WOOD)
    for z in (0.05, h - 0.05):
        ring(b, r * 0.92 + 0.012, r * 0.92 - 0.02, 0.024, (0, 0, z), IRON, segs=8, M=M)


def spoked_wheel(b, r, w, M, spokes=6, rim=None):
    # A cart wheel facing +X centred on the frame: a wooden felloe, an iron tyre, spokes, a brass hub.
    ring(b, r - 0.015, r - 0.06, w, (0, 0, 0), rim or WOOD, segs=12, M=M, axis="X")
    ring(b, r, r - 0.02, w + 0.006, (0, 0, 0), IRON, segs=12, M=M, axis="X")
    for k in range(spokes):
        a = k * TAU / spokes
        box(b, (0.032, 0.032, r - 0.05), (0, math.cos(a) * (r - 0.03) / 2, math.sin(a) * (r - 0.03) / 2), WOOD2, ch=0.0,
            rot=(a - math.pi / 2, 0, 0), M=M)
    cyl(b, 0.055, w + 0.03, (0, 0, 0), BRASS, segs=8, axis="X", ch=0.0, M=M)


def handwheel(b, r, M):
    # An elevation handwheel facing +X: a brass rim on two iron spokes, a wooden grip.
    ring(b, r, r * 0.8, 0.03, (0, 0, 0), BRASS, segs=10, M=M, axis="X")
    for k in range(2):
        box(b, (0.02, 2 * r * 0.85, 0.022), (0, 0, 0), IRON, ch=0.0, rot=(k * math.pi / 2 + math.pi / 4, 0, 0), M=M)
    cyl(b, 0.03, 0.06, (0, 0, 0), IRON, segs=6, axis="X", ch=0.0, M=M)
    rod(b, (0.0, 0, r * 0.9), (0.07, 0, r * 0.9), 0.014, WOOD, segs=6, M=M)


# ---------------------------------------------------------------- plinth (static)
def base(b):
    rtier(b, FOOT, 0.0, 0.10, STONE2, segs=16, ch=0.03, chb=0.015)          # foot
    rtier(b, DRUM + 0.05, 0.10, 0.16, STONE, segs=16, ch=0.02)             # moulding
    rtier(b, DRUM - 0.04, 0.16, 0.66, STONE2, segs=10, ch=0.0)             # the core the blocks clad (dark joints)
    for course, (z0, z1) in enumerate(((0.16, 0.41), (0.41, 0.66))):      # two staggered courses of ten blocks
        for k in range(10):
            c, s = course_dir(course, k)
            box(b, (0.27, 0.09, z1 - z0 - 0.012), (0, 0, 0), STONE, ch=0.022, M=Rz(math.atan2(s, c) - math.pi / 2) @ T(0, DRUM - 0.045, (z0 + z1) / 2))
    rtier(b, CAP, 0.66, H, STONE, segs=16, ch=0.03, chb=0.02)              # cap
    for k in range(8):
        a = (k + 0.5) * TAU / 8
        stud(b, (math.cos(a) * (FOOT - 0.035), math.sin(a) * (FOOT - 0.035), 0.10), r=0.03, segs=4)


def course_dir(course, k):
    # The outward direction of block k of a course (the upper course has one straight to the front, for the crest).
    a = (k + 0.5 * course) * TAU / 10
    return math.cos(a), math.sin(a)


# ---------------------------------------------------------------- deck (turns)
def body(b):
    rtier(b, DECK, 0.0, TOP, WOOD, segs=16, ch=0.018)
    ring(b, DECK + 0.012, DECK - 0.03, 0.04, (0, 0, 0.035), IRON, segs=16)
    for x in (-0.225, -0.075, 0.075, 0.225):  # plank seams
        h = math.sqrt((DECK - 0.04) ** 2 - x * x)
        box(b, (0.014, 2 * h, 0.008), (x, 0, TOP + 0.001), WOOD2, ch=0.0)
    for k in range(8):
        a = (k + 0.5) * TAU / 8
        stud(b, (math.cos(a) * (DECK - 0.045), math.sin(a) * (DECK - 0.045), TOP), r=0.022)


# ---------------------------------------------------------------- the carriage (part of every head)
def cheeks(b, x=0.21, t=0.08, wm=WOOD2):
    # Two stepped wooden cheeks either side of the gun, iron-strapped, brass-bolted, on four small trucks.
    for s in (-1, 1):
        cx = s * x
        box(b, (t, 0.72, 0.15), (cx, 0, TOP + 0.075), WOOD, ch=0.018)
        box(b, (t, 0.52, 0.11), (cx, -0.1, TOP + 0.205), WOOD, ch=0.018)
        box(b, (t, 0.34, 0.09), (cx, -0.19, TOP + 0.305), WOOD, ch=0.018)
        for y in (-0.16, 0.12):  # straps round the lower step
            box(b, (t + 0.012, 0.035, 0.155), (cx, y, TOP + 0.075), IRON, ch=0.0)
        for y, z in ((-0.1, TOP + 0.2), (-0.26, TOP + 0.3)):
            stud(b, (cx + s * t / 2, y, z), (s, 0, 0), r=0.02, segs=4)
        for y in (-0.27, 0.27):  # trucks
            cyl(b, 0.075, 0.035, (s * (x + t / 2 + 0.018), y, TOP + 0.075), wm, segs=10, axis="X", ch=0.0)
            cyl(b, 0.025, 0.05, (s * (x + t / 2 + 0.02), y, TOP + 0.075), BRASS, segs=6, axis="X", ch=0.0)


def trunnions(b, y=-0.16, z=AX, x=0.21, r=0.05):
    cyl(b, r, 2 * x + 0.08, (0, y, z), IRON, segs=8, axis="X", ch=0.01)
    for s in (-1, 1):  # brass cap squares over them
        box(b, (0.09, 0.14, 0.09), (s * x, y, z + 0.015), BRASS, ch=0.012)


def quoin(b, y=0.25, z_top=0.3):
    box(b, (0.36, 0.1, 0.08), (0, y, TOP + 0.11), WOOD, ch=0.012)      # transom between the cheeks
    box(b, (0.14, 0.2, z_top - TOP - 0.15), (0, y, (z_top + TOP + 0.15) / 2), WOOD2, ch=0.01)  # the wedge
    box(b, (0.04, 0.06, 0.04), (0, y + 0.12, TOP + 0.17), IRON, ch=0.006)   # its handle


# ---------------------------------------------------------------- heads (turn)
def big_barrel(b, z=AX, back=0.37, front=0.95, rb=0.175, rf=0.136, bore=0.095):
    k = rb / 0.175
    profile(b, [(-back, rb * 0.63, IRON), (-back + 0.03, rb, IRON), (-0.20, rb * 1.03, BRASS), (-0.17, rb * 1.09, BRASS),
                (-0.12, rb * 1.09, IRON), (-0.10, rb * 0.97, IRON), (0.30, rf * 1.12, IRON), (0.31, rf * 1.24, BRASS),
                (0.36, rf * 1.24, IRON), (0.37, rf * 1.1, IRON), (front - 0.17, rf, IRON), (front - 0.15, rf * 1.12, IRON),
                (front - 0.09, rf * 1.24, IRON), (front, rf * 1.24, IRON)], z=z, bore=(bore, 0.3 * k), segs=10)
    rod(b, (0, back - 0.01, z), (0, back + 0.06, z), 0.04 * k, IRON, segs=8)     # cascabel
    ball(b, 0.06 * k, (0, back + 0.09, z), IRON)
    box(b, (0.04, 0.05, 0.03), (0, -0.05, z + rb * 1.06), BRASS, ch=0.006)       # vent


def default(b):
    cheeks(b)
    trunnions(b)
    quoin(b)
    big_barrel(b)


CZ = 0.53   # the Cluster's axis, its three barrels lying on a cradle over the cheeks


def short_barrel(b, x, z=CZ, front=0.6, r=0.075, bm=None):
    profile(b, [(-0.2, r * 0.8, IRON), (-0.18, r * 1.12, IRON), (0.05, r * 1.06, IRON), (0.06, r * 1.2, BRASS), (0.1, r * 1.2, IRON),
                (0.11, r * 1.03, IRON), (front - 0.1, r * 0.93, IRON), (front - 0.08, r * 1.13, IRON), (front, r * 1.13, IRON)],
            x=x, z=z, bore=(r * 0.6, 0.18), segs=8, bm=bm)


def cluster_base(b, wm=WOOD2, bm=None):
    cheeks(b, wm=wm)
    box(b, (0.5, 0.36, 0.035), (0, -0.19, TOP + 0.35 + 0.0175), IRON, ch=0.01)        # cradle on the cheeks
    box(b, (0.5, 0.3, 0.26), (0, 0.19, TOP + 0.26 + 0.13), IRON, ch=0.03)            # breech block
    for s in (-1, 1):
        stud(b, (s * 0.25, 0.19, TOP + 0.45), (s, 0, 0), r=0.024, segs=4)
        stud(b, (s * 0.12, 0.34, TOP + 0.4), (0, 1, 0), r=0.022)
    for x in (-0.16, 0.0, 0.16):
        short_barrel(b, x, bm=bm)
    for y in (-0.12, -0.42):  # bands over the three
        box(b, (0.5, 0.05, 0.17), (0, y, CZ), IRON, ch=0.012)


def shell_tray(b, z=TOP + 0.52):
    box(b, (0.44, 0.22, 0.03), (0, 0.19, z + 0.015), IRON, ch=0.008)
    for s in (-1, 1):
        box(b, (0.44, 0.02, 0.05), (0, 0.19 + s * 0.1, z + 0.04), BRASS, ch=0.004)
    balls(b, [(x, 0.19, z + 0.03 + 0.055) for x in (-0.13, 0.0, 0.13)], 0.055)


def cluster(b):
    cluster_base(b)
    shell_tray(b)


def carpet(b):
    cluster_base(b)
    zb = TOP + 0.52
    for s in (-1, 1):  # the battery's frame on the breech block
        box(b, (0.05, 0.05, 0.34), (s * 0.22, 0.28, zb + 0.17), IRON, ch=0.0)
        box(b, (0.05, 0.05, 0.22), (s * 0.22, 0.06, zb + 0.11), IRON, ch=0.0)
        stud(b, (s * 0.245, 0.28, zb + 0.3), (s, 0, 0), r=0.018, segs=4)
    for y, z in ((0.06, zb + 0.22), (0.28, zb + 0.34)):
        F = T(0, y, z) @ Rx(math.radians(35))             # plate tilted so its normal points up and forward
        box(b, (0.5, 0.2, 0.04), (0, 0, 0), IRON, ch=0.01, M=F)
        for x in (-0.15, 0.0, 0.15):
            pipe(b, (x, 0, 0.0), (x, 0, 0.2), 0.058, 0.036, IRON, segs=8, M=F)
            ring(b, 0.066, 0.05, 0.025, (x, 0, 0.17), BRASS, segs=6, M=F)
    # the ammo belt up the left side of the breech block to the battery, brass rounds on it
    a, c = (-0.28, 0.4, TOP + 0.16), (-0.28, 0.1, TOP + 0.66)
    bar(b, a, c, 0.06, 0.025, WOOD2, up=(0, 1, 0))
    for k in range(6):
        t = (k + 0.5) / 6
        p = [a[i] + (c[i] - a[i]) * t for i in range(3)]
        box(b, (0.07, 0.035, 0.03), (p[0], p[1] + 0.85 * 0.025, p[2] + 0.53 * 0.025), BRASS, ch=0.0)


def napalm(b):
    cluster_base(b, wm=WOOD, bm=FIRE)                 # six materials: the bores glow, the trucks are plain wood
    for x in (-0.16, 0.0, 0.16):  # glowing rings and flared nozzles
        ring(b, 0.078, 0.066, 0.03, (x, -0.47, CZ), FIRE, segs=8, axis="Y")
        profile(b, [(0.6, 0.085, COPPER), (0.68, 0.1, COPPER)], x=x, z=CZ, bore=(0.06, 0.2), segs=8, bm=FIRE)
    zt = TOP + 0.52 + 0.14
    cyl(b, 0.14, 0.52, (0, 0.12, zt), RED, segs=12, axis="Y", ch=0.03)              # the fuel tank
    for y in (-0.04, 0.28):
        ring(b, 0.152, 0.13, 0.04, (0, y, zt), IRON, segs=12, axis="Y")
    cyl(b, 0.05, 0.06, (0, 0.12, zt + 0.16), BRASS, segs=6, ch=0.01)               # filler
    cyl(b, 0.07, 0.03, (0, 0.12, zt + 0.2), IRON, segs=6, ch=0.0)
    for x in (-0.16, 0.0, 0.16):  # copper feeds down to each barrel
        rod(b, (x * 0.6, -0.08, zt - 0.08), (x, -0.25, CZ + 0.06), 0.022, COPPER, segs=6)
        cyl(b, 0.032, 0.03, (x, -0.25, CZ + 0.075), BRASS, segs=6, ch=0.0)
    cyl(b, 0.07, 0.1, (0, 0.4, zt), IRON, segs=8, axis="Y", ch=0.01)               # pump on its back


BZ = 0.5     # the Heavy's axis


def heavy_cheeks(b, t=0.09, x=0.24):
    for s in (-1, 1):
        cx = s * x
        box(b, (t, 0.74, 0.16), (cx, 0, TOP + 0.08), WOOD, ch=0.02)
        box(b, (t, 0.5, 0.14), (cx, -0.08, TOP + 0.23), WOOD, ch=0.02)
        box(b, (t, 0.3, 0.1), (cx, -0.12, TOP + 0.35), WOOD, ch=0.02)
        for y in (-0.3, -0.1, 0.12, 0.3):
            box(b, (t + 0.01, 0.04, 0.165), (cx, y, TOP + 0.08), IRON, ch=0.0)
        box(b, (t + 0.01, 0.74, 0.03), (cx, 0, TOP + 0.015), IRON, ch=0.0)          # iron skid
        for y in (-0.24, 0.16):  # clear of the pods beside the cheeks
            stud(b, (cx + s * t / 2, y, TOP + 0.24), (s, 0, 0), r=0.02, segs=4)


def heavy_barrel(b, front=1.24, z=BZ):
    profile(b, [(-0.37, 0.13, IRON), (-0.34, 0.2, IRON), (-0.22, 0.205, BRASS), (-0.19, 0.215, BRASS), (-0.13, 0.215, IRON),
                (-0.11, 0.19, IRON), (0.4, 0.165, IRON), (0.42, 0.18, BRASS), (0.47, 0.18, IRON), (0.49, 0.162, IRON),
                (front - 0.13, 0.15, IRON), (front - 0.11, 0.17, BRASS), (front - 0.07, 0.17, IRON), (front, 0.175, IRON)], z=z,
            bore=(0.105, 0.35), segs=10)


def recoil(b, z=BZ):
    for s in (-1, 1):
        cyl(b, 0.05, 0.56, (s * 0.1, -0.2, z - 0.21), IRON, segs=8, axis="Y", ch=0.01)
        rod(b, (s * 0.1, -0.48, z - 0.21), (s * 0.1, -0.68, z - 0.21), 0.022, BRASS, segs=6)
        cyl(b, 0.058, 0.03, (s * 0.1, 0.06, z - 0.21), BRASS, segs=8, axis="Y", ch=0.0)
    ring(b, 0.2, 0.16, 0.06, (0, -0.68, z), IRON, segs=10, axis="Y")               # clamp on the barrel
    box(b, (0.3, 0.06, 0.1), (0, -0.68, z - 0.19), IRON, ch=0.01)                  # its lug down to the rods


def heavy_breech(b, z=BZ):
    box(b, (0.36, 0.2, 0.36), (0, 0.36, z), IRON, ch=0.035)
    for u, v in CORNERS4:
        stud(b, (u * 0.12, 0.46, z + v * 0.12), (0, 1, 0), r=0.022)
    handwheel(b, 0.12, T(0.2, 0.36, z + 0.02))


def heavy(b):
    heavy_cheeks(b)
    trunnions(b, y=-0.12, z=BZ, x=0.24, r=0.055)
    heavy_barrel(b)
    recoil(b)
    heavy_breech(b)


def siege(b):
    heavy_cheeks(b)
    trunnions(b, y=-0.12, z=BZ, x=0.24, r=0.055)
    heavy_barrel(b, front=1.44)
    heavy_breech(b)
    # the armoured shield (gatling sheet 5), its port round the barrel
    zs = BZ + 0.02
    for s in (-1, 1):
        box(b, (0.19, 0.06, 0.5), (s * 0.195, -0.42, zs), IRON, ch=0.025)
    box(b, (0.2, 0.06, 0.1), (0, -0.42, zs + 0.2), IRON, ch=0.02)
    for u, v in CORNERS4:
        stud(b, (u * 0.22, -0.45, zs + v * 0.19), (0, -1, 0), r=0.026, segs=4)
    for s in (-1, 1):  # its braces back to the cheeks
        bar(b, (s * 0.25, -0.39, zs - 0.12), (s * 0.25, -0.28, TOP + 0.3), 0.05, 0.04, IRON, ch=0.0)
    # big spoked wheels outside the pods, on one axle over them
    cyl(b, 0.03, 1.0, (0, -0.06, TOP + 0.34), IRON, segs=8, axis="X", ch=0.0)
    for s in (-1, 1):
        spoked_wheel(b, 0.335, 0.05, T(s * 0.475, -0.06, TOP + 0.34))
    # the trail and its spade at the back
    bar(b, (0, 0.3, TOP + 0.12), (0, 0.44, TOP + 0.03), 0.16, 0.08, WOOD)
    box(b, (0.24, 0.05, 0.12), (0, 0.44, TOP + 0.05), IRON, ch=0.01, rot=(0.5, 0, 0))


SZ = 0.36    # the Shockwave's axis: low


def shockwave(b):
    for s in (-1, 1):  # low heavy cheeks, the barrel's beams on shock springs over them
        cx = s * 0.235
        box(b, (0.1, 0.7, 0.12), (cx, 0, TOP + 0.06), WOOD, ch=0.02)
        for y in (-0.22, 0.0, 0.22):
            box(b, (0.11, 0.04, 0.125), (cx, y, TOP + 0.06), IRON, ch=0.0)
        for y in (-0.22, 0.2):
            coil_rings(b, 0.045, TOP + 0.12, TOP + 0.19, 3, IRON, segs=6, M=T(cx, y, 0))
            rod(b, (cx, y, TOP + 0.12), (cx, y, TOP + 0.19), 0.02, BRASS, segs=6)
        box(b, (0.1, 0.62, 0.05), (cx, -0.01, TOP + 0.215), IRON, ch=0.012)
        box(b, (0.1, 0.12, 0.1), (cx, -0.05, TOP + 0.29), BRASS, ch=0.012)        # bearing block
    cyl(b, 0.045, 0.56, (0, -0.05, SZ), IRON, segs=8, axis="X", ch=0.008)        # trunnion pin
    profile(b, [(-0.34, 0.13, IRON), (-0.31, 0.2, IRON), (-0.18, 0.205, BRASS), (-0.15, 0.215, BRASS), (-0.1, 0.215, IRON),
                (-0.08, 0.19, IRON), (0.4, 0.17, IRON), (0.5, 0.2, IRON), (0.62, 0.28, IRON), (0.7, 0.31, BRASS),
                (0.74, 0.31, IRON)], z=SZ, bore=(0.22, 0.3), segs=12)
    rod(b, (0, 0.33, SZ), (0, 0.4, SZ), 0.045, IRON, segs=8)
    ball(b, 0.07, (0, 0.43, SZ), IRON)
    # the blast ring in front of the bell, on four struts
    ring(b, 0.37, 0.31, 0.05, (0, -0.86, SZ), BRASS, segs=12, axis="Y")
    for k in range(4):
        a = k * TAU / 4 + math.pi / 4
        c, s_ = math.cos(a), math.sin(a)
        bar(b, (c * 0.28, -0.72, SZ + s_ * 0.28), (c * 0.33, -0.86, SZ + s_ * 0.33), 0.03, 0.03, IRON)


# ---------------------------------------------------------------- extra barrel pod (turns, on the deck)
def pod(b):
    # rear-bottom-centre at the origin, pointing -Y: a flat-topped gun box (the next one stands on it) and a short barrel.
    box(b, (PW, PD, PH), (0, -PD / 2, PH / 2), WOOD, ch=0.015)
    for y in (-0.045, -0.175):
        box(b, (PW + 0.008, 0.03, PH + 0.004), (0, y, PH / 2), IRON, ch=0.0)
    profile(b, [(0.16, 0.04, IRON), (0.2, 0.05, BRASS), (0.24, 0.05, IRON), (0.25, 0.042, IRON), (0.36, 0.038, IRON),
                (0.37, 0.05, IRON), (0.42, 0.05, IRON)], z=0.08, bore=(0.026, 0.1), segs=8)
    for s in (-1, 1):  # studs on its face (none on the sides: the pods stand hard against the carriage)
        stud(b, (s * 0.042, -PD, PH - 0.025), (0, -1, 0), r=0.013, segs=4)


# ---------------------------------------------------------------- one piece a level (static, round the plinth)
def kit2(b):   # Lv2: iron straps up the drum over the upper course's joints, square studs, an iron band on the moulding
    for k in range(0, 10, 2):
        cx, cy = course_dir(0, k)
        F = Rz(math.atan2(cy, cx) - math.pi / 2)
        box(b, (0.06, 0.04, 0.5), (0, DRUM + 0.015, 0.41), IRON, ch=0.006, M=F)
        for z in (0.25, 0.41, 0.57):
            sqstud(b, (0, DRUM + 0.035, z), (0, 1, 0), s=0.045, m=IRON, M=F)
    ring(b, DRUM + 0.065, DRUM + 0.03, 0.04, (0, 0, 0.13), IRON, segs=16)


def kit3(b):   # Lv3: a brass band round the cap, rivets on it
    ring(b, CAP + 0.012, CAP - 0.02, 0.035, (0, 0, 0.715), BRASS, segs=16)
    for k in range(12):
        a = (k + 0.5) * TAU / 12
        stud(b, (math.cos(a) * (CAP + 0.012), math.sin(a) * (CAP + 0.012), 0.715), (math.cos(a), math.sin(a), 0), r=0.018, segs=4)


def kit4(b):   # Lv4: a pyramid of cannonballs on a wooden tray against the back of the foot
    y = FOOT + 0.11
    box(b, (0.44, 0.22, 0.05), (0, y, 0.025), WOOD, ch=0.01)
    for s in (-1, 1):
        box(b, (0.46, 0.025, 0.07), (0, y + s * 0.11, 0.035), WOOD2, ch=0.006)
    r = 0.064
    pts = [(x, y + v, 0.05 + r) for x in (-0.14, 0.0, 0.14) for v in (-0.065, 0.065)]
    pts += [(x, y, 0.05 + r + r * 1.4) for x in (-0.07, 0.07)]
    balls(b, pts, r)


def kit5(b):   # Lv5: riveted iron plates on the lower course between the straps
    for k in range(1, 10, 2):
        n = course_dir(0, k) + (0,)
        plate(b, 0.2, 0.18, 0.025, (n[0] * DRUM, n[1] * DRUM, 0.285), n, IRON, rivets=CORNERS4, rv=0.018)


def kit6(b):   # Lv6: powder kegs and a rammer against the left of the foot
    x = -(FOOT + 0.09)
    keg(b, T(x, -0.12, 0))
    keg(b, T(x, 0.12, 0))
    keg(b, T(x, 0.0, 0.24 + 0.085) @ Rx(math.pi / 2) @ T(0, 0, -0.12), r=0.085, h=0.24)
    rod(b, (x - 0.05, 0.34, 0.0), (-0.44, 0.2, 0.62), 0.018, WOOD, segs=6)
    cyl(b, 0.04, 0.1, (0, 0, 0.05), SAND, segs=8, ch=0.01, M=T(x - 0.05, 0.34, 0.0) @ Ry(0.36))


PEN = -math.radians(20)   # the pennant's pole: front right of the foot, its cloth running back along the 3/4 view


def kit7(b):   # Lv7: the turret's pennant, the cloth square to the 3/4 camera, under the deck's sweep
    r = FOOT + 0.035
    px, py = r * math.cos(PEN), r * math.sin(PEN)
    pennant(b, 0.7, CLOTH["Cannon"], M=T(px, py, 0) @ Rz(math.atan2(0.8, 0.95)))
    box(b, (0.1, 0.1, 0.03), (px, py, 0.015), IRON, ch=0.008)                    # its foot plate on the ground
    for z in (0.3, 0.55):  # brackets into the drum
        bar(b, (px, py, z), ((DRUM - 0.01) * math.cos(PEN), (DRUM - 0.01) * math.sin(PEN), z), 0.035, 0.035, IRON)


def kit8(b):   # Lv8: the brass crest on the front block of the upper course (a cannonball in a ring), knobs either side
    F = T(0, -DRUM, 0.535)
    cyl(b, 0.1, 0.03, (0, -0.015, 0), BRASS, segs=12, axis="Y", ch=0.008, M=F)
    ring(b, 0.12, 0.095, 0.035, (0, -0.02, 0), BRASS, segs=12, M=F, axis="Y")
    ball(b, 0.055, (0, -0.035, 0), IRON, M=F)
    for s in (-1, 1):
        box(b, (0.05, 0.06, 0.05), (s * 0.19, 0.01, 0), IRON, ch=0.008, M=F)
        ball(b, 0.035, (s * 0.19, -0.04, 0), BRASS, M=F)


# ---------------------------------------------------------------- final classes (static, from Lv9; Kit4 + Kit6 make way)
def dress_a1(b):   # Carpet: two open shell crates at the back, stacked crates on the left
    for x, rz in ((-0.14, 0.05), (0.16, -0.06)):
        F = T(x, FOOT + 0.11, 0) @ Rz(rz)
        crate(b, (0.26, 0.22, 0.2), M=F, planks=1, lid=False)
        balls(b, [(u * 0.06, v * 0.05, 0.2 + 0.03) for u in (-1, 1) for v in (-1, 1)], 0.045)
    x = -(FOOT + 0.1)
    for s, y in ((0, -0.12), (1, 0.16)):
        crate(b, (0.2, 0.26, 0.18), M=T(x, y, 0) @ Rz(0.04 * (1 - 2 * s)), planks=1)
    crate(b, (0.18, 0.22, 0.14), M=T(x + 0.01, 0.02, 0.18), planks=1)


def dress_a2(b):   # Napalm cluster: red fuel drums on the left with a copper line into the drum, fire shells racked at the back
    x = -(FOOT + 0.09)
    for y in (-0.15, 0.15):
        cyl(b, 0.11, 0.36, (x, y, 0.18), RED, segs=10, ch=0.02)
        for z in (0.06, 0.3):
            ring(b, 0.118, 0.1, 0.03, (x, y, z), IRON, segs=10)
        cyl(b, 0.035, 0.04, (x, y, 0.38), BRASS, segs=6, ch=0.006)
        rod(b, (x, y, 0.38), (x, 0.0, 0.46), 0.02, COPPER, segs=6)
    rod(b, (x, 0.0, 0.46), (-(DRUM - 0.01), 0.0, 0.46), 0.024, COPPER, segs=6)
    cyl(b, 0.04, 0.05, (-(DRUM + 0.01), 0.0, 0.46), BRASS, segs=6, axis="X", ch=0.006)
    y = FOOT + 0.09
    box(b, (0.46, 0.18, 0.06), (0, y, 0.03), WOOD, ch=0.012)
    box(b, (0.48, 0.03, 0.05), (0, y + 0.06, 0.2), WOOD, ch=0.008)
    for s in (-1, 1):
        box(b, (0.04, 0.04, 0.2), (s * 0.22, y + 0.06, 0.12), IRON, ch=0.0)
    for k in range(4):
        cx = -0.165 + k * 0.11
        cyl(b, 0.042, 0.2, (cx, y, 0.16), BRASS, segs=8, ch=0.006)
        cyl(b, 0.042, 0.08, (cx, y, 0.3), FIRE, segs=8, r2=0.012, ch=0.0)


def dress_b1(b):   # Siege: big siege shot in an iron tray at the back, a spare wheel leaning on the left
    y = FOOT + 0.11
    box(b, (0.46, 0.22, 0.06), (0, y, 0.03), WOOD, ch=0.012)
    for s in (-1, 1):
        box(b, (0.48, 0.03, 0.08), (0, y + s * 0.11, 0.04), IRON, ch=0.0)
    balls(b, [(x, y, 0.06 + 0.1) for x in (-0.12, 0.12)] + [(0.0, y, 0.06 + 0.1 + 0.15)], 0.1)
    spoked_wheel(b, 0.315, 0.05, T(-(FOOT + 0.06), 0.0, 0.0) @ Ry(0.22) @ T(0, 0, 0.32))


def dress_b2(b):   # Shockwave: two sprung buffer posts on the left, stacked counterweights at the back
    x = -(FOOT + 0.08)
    for y in (-0.16, 0.16):
        rtier(b, 0.1, 0.0, 0.06, IRON, segs=8, ch=0.015, M=T(x, y, 0))
        coil_rings(b, 0.07, 0.06, 0.34, 5, IRON, segs=8, M=T(x, y, 0))
        rod(b, (x, y, 0.06), (x, y, 0.4), 0.03, BRASS, segs=6)
        cyl(b, 0.09, 0.05, (x, y, 0.4), IRON, segs=8, ch=0.01)
        bar(b, (x, y, 0.4), (-(DRUM - 0.01), y * 0.6, 0.4), 0.04, 0.04, IRON)
    y = FOOT + 0.1
    for w, z in ((0.44, 0.0), (0.38, 0.09), (0.3, 0.18)):
        box(b, (w, 0.2, 0.09), (0, y, z + 0.045), IRON, ch=0.015)
        stud(b, (0, y - 0.1, z + 0.045), (0, -1, 0), r=0.02)


model("CannonBase", base)
model("CannonBody", body, scale=SC)
model("CannonDefault", default, scale=SC)
model("CannonA", cluster, scale=SC)
model("CannonA1", carpet, scale=SC)
model("CannonA2", napalm, scale=SC)
model("CannonB", heavy, scale=SC)
model("CannonB1", siege, scale=SC)
model("CannonB2", shockwave, scale=SC)
model("CannonBarrel", pod, scale=SC)
for n, f in ((2, kit2), (3, kit3), (4, kit4), (5, kit5), (6, kit6), (7, kit7), (8, kit8)):
    model("CannonKit%d" % n, f)
for n, f in (("A1", dress_a1), ("A2", dress_a2), ("B1", dress_b1), ("B2", dress_b2)):
    model("CannonDress" + n, f)
export_all()
layout("Cannon", head=H, muzzle=round(H + AX * SC, 3), pod=[round(v, 3) for v in POD], step=[round(v, 3) for v in STEP])
