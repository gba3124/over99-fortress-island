# Bubble turret, art pass 5 (tools/t5kit.py; Owen's Bubble sheet, /tmp/t5/full/9.png). Plinth: the round cream stone drum
# on an iron skirt with stone feet (sheet 1). Head (it barely aims, so it stays round): a blue glass dome in a brass cage
# on a brass foot ring, iron pylons with blue windows round it (sheet 1). Classes (TURRET-UPGRADE-DESIGN.md): A Expanse
# (bigger dome, brass band, emitter fins) -> A1 Dome (the biggest dome, four iron arms to a top pressure ring, sheet 6) /
# A2 Ward (three nested rings round a light core on a rune base); B Hardened (iron-ribbed cage) -> B1 Fortress (riveted iron
# shell, a small front window, heavy base) / B2 Aegis (a big faceted lens in an iron frame, top crystal, side stabilisers,
# sheet 9).
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/t5_bubble.py   [ONLY=BubbleBase,BubbleA ...]
import math
import os
import sys

import bmesh
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import t5kit as K  # noqa: E402
from t5kit import *  # noqa: F401,F403,E402

H = 0.46          # head pivot: the top of the drum
TOP = 0.05        # the turntable ring's top; head parts are drawn at this size, exported SC bigger
SC = 1.2
CROWN_Z = 0.468   # the default dome's outlet (its bore's mouth) above the pivot, drawn: the bubbles leave from it
SKIRT = 0.54      # the iron skirt's radius: the kits stand against it
FEET = [math.pi / 4 + k * math.pi / 2 for k in range(4)]
PEN = math.atan2(0.644, 0.765)   # the pennant's cloth runs along the 3/4 view's right, so it faces that camera


def at(r, a, z=0.0):
    return (r * math.cos(a), r * math.sin(a), z)


# ---------------------------------------------------------------- local helpers
def dome(b, r, z0, h, m=None, segs=14, n=4, M=I4):
    # A faceted dome from a circle of r at z0 up to its tip at z0 + h (sheet 1's blue glass dome: LENS reads as its blue).
    rings = [circle(r * math.cos(a), segs, z0 + h * math.sin(a)) for a in (math.pi / 2 * k / n for k in range(n))]
    tmp = bmesh.new()
    K._loft(tmp, rings + [[(0, 0, z0 + h)]], [0])
    K._put(b, tmp, M, m or LENS)


def dome_pt(r, z0, h, a, phi, off=0.0):
    # The point at latitude a, longitude phi on that dome, `off` out from its surface.
    return Vector(((r + off) * math.cos(a) * math.cos(phi), (r + off) * math.cos(a) * math.sin(phi), z0 + (h + off) * math.sin(a)))


def ribs(b, r, z0, h, n, phase=0.0, a1=1.2, w=0.035, t=0.025, m=None, off=0.012, joints=False):
    # n ribs up the dome's meridians from its foot to latitude a1 (where the crown takes over).
    for k in range(n):
        phi = phase + k * TAU / n
        up = (math.cos(phi), math.sin(phi), 0)
        pts = [dome_pt(r, z0, h, a1 * j / 4, phi, off) for j in range(5)]
        for p, q in zip(pts, pts[1:]):
            bar(b, tuple(p), tuple(q), w, t, m or BRASS, ch=0.005, up=up)
        if joints:
            for p in pts[1:-1]:
                stud(b, tuple(p + Vector(up) * 0.0), tuple((p - Vector((0, 0, z0))).normalized()), r=w * 0.42, segs=4)


def curved_plate(b, r, a0, a1, z0, z1, t, m, segs=4, M=I4):
    # A plate bent round a drum of radius r, from angle a0 to a1 and z0 to z1, standing t out from it.
    rings = []
    for k in range(segs + 1):
        a = a0 + (a1 - a0) * k / segs
        c, s = math.cos(a), math.sin(a)
        rings.append([(rr * c, rr * s, z) for rr, z in ((r, z0), (r, z1), (r + t, z1), (r + t, z0))])
    tmp = bmesh.new()
    K._loft(tmp, rings, [0])
    K._put(b, tmp, M, m)


def foot_ring(b, r, h=0.06, m=None):
    rtier(b, r, TOP, TOP + h, m or BRASS, segs=16, ch=0.012, r_top=r - 0.01)


def crown(b, z, ro=0.1, ri=0.064, h=0.07):
    # The bubble outlet on the dome's top: a brass collar round a dark bore.
    pipe(b, (0, 0, z - h * 0.6), (0, 0, z + h * 0.4), ro, ri, BRASS, segs=12)


def pylons(b, angles, r=0.31, h=0.2, win=True):
    # Sheet 1's iron pylons round the dome: a blue window on the outside, a brass cap.
    for a in angles:
        F = Rz(a) @ T(r, 0, 0)
        tier(b, 0.034, 0.042, 0.012, TOP, TOP + h, IRON, ch=0.01, M=F)
        tier(b, 0.04, 0.048, 0.012, TOP + h, TOP + h + 0.03, BRASS, ch=0.008, M=F)
        if win:
            box(b, (0.012, 0.03, h * 0.45), (0.034, 0, TOP + h * 0.52), LENS, ch=0.0, M=F)


# ---------------------------------------------------------------- plinth (static)
def base(b):
    rtier(b, SKIRT, 0.0, 0.06, IRON, segs=16, ch=0.02, chb=0.01)                   # the iron skirt
    rtier(b, 0.49, 0.06, 0.38, STONE, segs=16, ch=0.025, r_top=0.475)              # the drum
    rtier(b, 0.482, 0.38, H, STONE2, segs=16, ch=0.018, chb=0.012, r_top=0.47)     # the cornice
    for a in FEET:                                                                  # stone feet (sheet 1)
        tier(b, 0.095, 0.072, 0.02, 0.0, 0.25, STONE2, ch=0.02, taper=0.03, M=Rz(a) @ T(0.48, 0, 0))


# ---------------------------------------------------------------- turntable ring (turns)
def body(b):
    rtier(b, 0.35, 0.0, TOP, IRON, segs=16, ch=0.012)
    ring(b, 0.356, 0.33, 0.018, (0, 0, 0.022), BRASS, segs=16)                      # brass band round its rim
    for k in range(8):
        a = (k + 0.5) * TAU / 8
        stud(b, at(0.33, a, TOP), r=0.018)


# ---------------------------------------------------------------- heads (turn)
def default(b):
    foot_ring(b, 0.33)
    dome(b, 0.31, TOP + 0.05, 0.34)
    ribs(b, 0.31, TOP + 0.05, 0.34, 4, a1=1.3)
    crown(b, CROWN_Z - 0.028, ro=0.105, ri=0.068)
    pylons(b, FEET, r=0.35, h=0.22)


def expanse(b):
    z0, r, h = TOP + 0.05, 0.33, 0.37
    foot_ring(b, 0.35)
    dome(b, r, z0, h, segs=16)
    ribs(b, r, z0, h, 4, phase=math.pi / 4, a1=1.3)
    # a brass band round the dome, four emitter fins off it (front, back, sides)
    a = 0.5
    zb, rb = z0 + h * math.sin(a), r * math.cos(a)
    ring(b, rb + 0.025, rb - 0.012, 0.045, (0, 0, zb), BRASS, segs=16)
    for k in range(4):
        F = Rz(k * math.pi / 2) @ T(rb + 0.02, 0, zb)
        box(b, (0.07, 0.024, 0.11), (0.03, 0, 0), IRON, ch=0.008, M=F)
        box(b, (0.03, 0.04, 0.13), (0.0, 0, 0), BRASS, ch=0.006, M=F)
        ball(b, 0.032, (0.07, 0, 0.0), LENS, M=F)
    crown(b, z0 + h, ro=0.115, ri=0.075, h=0.08)
    pylons(b, FEET, r=0.36, h=0.18)


def dome_a1(b):
    z0, r, h = TOP + 0.07, 0.36, 0.35
    rtier(b, 0.39, TOP, TOP + 0.08, BRASS, segs=16, ch=0.015, r_top=0.38)         # heavy brass foot ring (sheet 6)
    dome(b, r, z0, h, segs=16)
    # four iron arms over the dome to the top pressure ring
    lat = (0.0, 0.32, 0.64, 0.96, 1.22)
    for phi in FEET:
        up = (math.cos(phi), math.sin(phi), 0)
        pts = [dome_pt(r, z0, h, a, phi, 0.026) for a in lat]
        for p, q in zip(pts, pts[1:]):
            bar(b, tuple(p), tuple(q), 0.055, 0.04, IRON, ch=0.008, up=up)
        for p in pts[1:-1]:
            stud(b, tuple(p + Vector(up) * 0.0), tuple((p - Vector((0, 0, z0 - 0.1))).normalized()), r=0.022, segs=4)
        box(b, (0.07, 0.08, 0.09), tuple(Vector(at(0.375, phi, z0 + 0.03))), IRON, ch=0.012, rot=(0, 0, phi))   # the arm's shoe
    zt = z0 + (h + 0.026) * math.sin(lat[-1])
    rt = (r + 0.026) * math.cos(lat[-1])
    rtier(b, rt + 0.03, zt - 0.035, zt + 0.035, BRASS, segs=12, ch=0.012)          # the pressure ring
    rtier(b, rt - 0.005, zt + 0.035, zt + 0.07, IRON, segs=12, ch=0.012, r_top=rt - 0.03)
    crown(b, zt + 0.08, ro=0.065, ri=0.04, h=0.06)
    pylons(b, [k * math.pi / 2 for k in range(4)], r=0.375, h=0.16)


def ward(b):
    rtier(b, 0.34, TOP, TOP + 0.06, IRON, segs=16, ch=0.012, r_top=0.33)           # the rune base
    for k in range(8):                                                              # glowing runes round its rim
        a = (k + 0.5) * TAU / 8
        box(b, (0.012, 0.06, 0.022), at(0.337, a, TOP + 0.03), CYAN, ch=0.0, rot=(0, 0, a))
        box(b, (0.012, 0.022, 0.034), at(0.337, a + 0.09, TOP + 0.03), CYAN, ch=0.0, rot=(0, 0, a))
    rtier(b, 0.11, TOP + 0.06, 0.31, BRASS, segs=8, ch=0.015, r_top=0.06)           # the pedestal
    rtier(b, 0.06, 0.31, 0.37, BRASS, segs=8, ch=0.01, r_top=0.1)                  # its cup
    zc = 0.46
    ball(b, 0.11, (0, 0, zc), CYAN, subdiv=2)                                       # the light core
    rod(b, (-0.32, 0, zc), (0.32, 0, zc), 0.016, IRON, segs=6)                      # the rings' axle
    for ro, ri, tilt, m in ((0.33, 0.292, 0.45, GLASS), (0.26, 0.228, -0.4, BRASS), (0.19, 0.16, 1.15, GLASS)):
        ring(b, ro, ri, 0.038, (0, 0, 0), m, segs=16, M=T(0, 0, zc) @ Rx(tilt))
    for s in (-1, 1):                                                               # two posts hold the axle
        x = s * 0.33
        rod(b, (x, 0, TOP + 0.05), (x, 0, zc + 0.02), 0.026, IRON, segs=6)
        cyl(b, 0.034, 0.05, (x, 0, zc), BRASS, segs=8, axis="X", ch=0.008)
        cyl(b, 0.04, 0.04, (x, 0, TOP + 0.075), BRASS, segs=8, ch=0.008)


def hardened(b):
    z0, r, h = TOP + 0.06, 0.31, 0.34
    rtier(b, 0.35, TOP, TOP + 0.07, IRON, segs=16, ch=0.012, r_top=0.34)           # iron foot ring
    for k in range(8):
        stud(b, at(0.346, (k + 0.5) * TAU / 8, TOP + 0.035), at(1, (k + 0.5) * TAU / 8), r=0.016)
    dome(b, r, z0, h)
    ribs(b, r, z0, h, 6, a1=1.15, w=0.05, t=0.035, m=IRON, off=0.015)              # heavy iron ribs
    a = 0.6
    ring(b, r * math.cos(a) + 0.03, r * math.cos(a) - 0.01, 0.04, (0, 0, z0 + h * math.sin(a)), IRON, segs=16)   # the hoop
    for k in range(6):
        phi = k * TAU / 6
        stud(b, tuple(dome_pt(r, z0, h, a, phi, 0.05)), at(1, phi), r=0.018)
    rtier(b, 0.12, z0 + h * 0.86, z0 + h + 0.03, IRON, segs=12, ch=0.015, r_top=0.085)   # the iron top cap
    crown(b, z0 + h + 0.06, ro=0.065, ri=0.04, h=0.06)


def fortress(b):
    z0, r, h = TOP + 0.09, 0.34, 0.36
    rtier(b, 0.37, TOP, TOP + 0.1, IRON, segs=16, ch=0.015, r_top=0.36)            # the heavy base
    for k in range(12):
        a = (k + 0.5) * TAU / 12
        stud(b, at(0.367, a, TOP + 0.05), at(1, a), r=0.018)
    dome(b, r, z0, h, IRON, segs=16)                                                # the riveted iron shell
    for a in (0.3, 0.85):                                                           # two riveted bands
        zb, rb = z0 + h * math.sin(a), r * math.cos(a)
        ring(b, rb + 0.018, rb - 0.02, 0.04, (0, 0, zb), BRASS, segs=16)
        for k in range(8):
            phi = (k + 0.5) * TAU / 8
            stud(b, at(rb + 0.018, phi, zb), at(1, phi), r=0.014, m=IRON)
    # the small front window: glass in a brass rim, set into the shell (sheet: Fortress)
    a, phi = 0.42, -math.pi / 2
    p = dome_pt(r, z0, h, a, phi)
    nrm = Vector((0, -math.cos(a) / r, math.sin(a) / h)).normalized()
    F, _ = toward(p - nrm * 0.03, p + nrm)
    cyl(b, 0.075, 0.05, (0, 0, 0.02), GLASS, segs=10, ch=0.0, M=F)
    ring(b, 0.095, 0.07, 0.04, (0, 0, 0.03), BRASS, segs=10, M=F)
    for k in range(4):
        stud(b, (math.cos(k * TAU / 4) * 0.083, math.sin(k * TAU / 4) * 0.083, 0.05), (0, 0, 1), r=0.011, m=IRON, M=F)
    # the top hatch
    zt = z0 + h
    cyl(b, 0.11, 0.05, (0, 0, zt - 0.005), IRON, segs=12, ch=0.012)
    bar(b, (-0.05, 0, zt + 0.045), (0.05, 0, zt + 0.045), 0.02, 0.02, BRASS)
    for x in (-0.05, 0.05):
        bar(b, (x, 0, zt + 0.015), (x, 0, zt + 0.055), 0.02, 0.02, BRASS)


def aegis(b):
    rtier(b, 0.32, TOP, TOP + 0.05, IRON, segs=16, ch=0.012, r_top=0.31)           # the base, a brass step on it
    ring(b, 0.325, 0.3, 0.016, (0, 0, TOP + 0.025), BRASS, segs=16)
    rtier(b, 0.2, TOP + 0.05, TOP + 0.09, BRASS, segs=12, ch=0.012, r_top=0.19)
    for k in range(6):
        stud(b, at(0.26, (k + 0.5) * TAU / 6, TOP + 0.05), r=0.02)
    zc, ax, az, ay = 0.47, 0.27, 0.33, 0.08
    rtier(b, 0.075, TOP + 0.09, zc - az + 0.02, BRASS, segs=10, ch=0.012, r_top=0.11)   # the cradle under the lens
    ball(b, 1.0, (0, 0, 0), LENS, subdiv=2, M=T(0, 0, zc) @ S(ax, ay, az))          # the big faceted lens (sheet 9)
    ring(b, 1.0, 0.9, 0.06, (0, 0, 0), BRASS, segs=20, M=T(0, 0, zc) @ S(ax + 0.015, 1, az + 0.015) @ Rx(math.pi / 2))
    # the iron frame: an upright each side, bolted to the rim
    for s in (-1, 1):
        pts = [(s * 0.31, 0, TOP + 0.05), (s * 0.35, 0, zc), (s * 0.31, 0, zc + 0.21), (s * 0.23, 0, zc + 0.3)]
        for p, q in zip(pts, pts[1:]):
            bar(b, p, q, 0.075, 0.06, IRON, ch=0.01)
        for p in pts[1:-1]:
            box(b, (0.08, 0.075, 0.08), p, IRON, ch=0.012)
        rod(b, (s * 0.27, 0, zc), (s * 0.38, 0, zc), 0.03, BRASS, segs=8)            # the pivot bolt
        cyl(b, 0.045, 0.03, (s * 0.4, 0, zc), BRASS, segs=8, axis="X", ch=0.008)
        # the side stabiliser: an iron vane off the upright, high above the pennant's reach
        box(b, (0.1, 0.03, 0.16), (s * 0.42, 0, zc + 0.13), IRON, ch=0.008)
        box(b, (0.11, 0.04, 0.03), (s * 0.425, 0, zc + 0.215), BRASS, ch=0.006)
    # the top crystal in a brass clasp
    box(b, (0.08, 0.1, 0.05), (0, 0, zc + az + 0.01), BRASS, ch=0.01)
    gem(b, 0.06, 0.17, 0.03, (0, 0, zc + az + 0.06), CRYSTAL, segs=6, mid=(0.85, 0.04))


# ---------------------------------------------------------------- the barrel model (Bubble never sets pods; kept small)
def pod(b):
    box(b, (0.1, 0.1, 0.04), (0, 0, 0.02), IRON, ch=0.01)
    cyl(b, 0.03, 0.1, (0, 0, 0.09), BRASS, segs=8, ch=0.006)
    pipe(b, (0, 0.02, 0.15), (0, -0.1, 0.15), 0.035, 0.022, BRASS)
    ball(b, 0.03, (0, -0.13, 0.15), GLASS)


# ---------------------------------------------------------------- one piece a level (static, round the drum)
def kit2(b):   # Lv2: iron caps and shoes on the stone feet, an iron band at the upper drum's foot
    for a in FEET:
        F = Rz(a) @ T(0.48, 0, 0)
        tier(b, 0.08, 0.057, 0.015, 0.21, 0.26, IRON, ch=0.01, M=F)
        tier(b, 0.105, 0.082, 0.02, 0.0, 0.05, IRON, ch=0.01, M=F)
        for y in (-0.04, 0.04):
            stud(b, (0.105, y, 0.025), (1, 0, 0), r=0.014, m=BRASS, segs=4, M=F)
    rtier(b, 0.484, 0.3, 0.33, IRON, segs=16, ch=0.008)


def kit3(b):   # Lv3: a brass band round the upper drum, rivets on it
    rtier(b, 0.487, 0.405, 0.43, BRASS, segs=16, ch=0.006)
    for k in range(8):
        a = (k + 0.5) * TAU / 8
        stud(b, at(0.487, a, 0.4175), at(1, a), r=0.014, m=IRON)


def kit4(b):   # Lv4: a soap barrel foaming with bubbles and a crate, against the back of the skirt
    a = math.radians(78)
    r = 0.12
    x, y, _ = at(SKIRT + r, a)
    rtier(b, r, 0.0, 0.16, WOOD, segs=10, ch=0.0, r_top=r + 0.015, M=T(x, y, 0))
    rtier(b, r + 0.015, 0.16, 0.32, WOOD, segs=10, ch=0.015, r_top=r, M=T(x, y, 0))
    for z in (0.05, 0.27):
        rtier(b, r + 0.012, z - 0.012, z + 0.012, IRON, segs=10, ch=0.0, M=T(x, y, 0))
    for (dx, dy, dz, br) in ((0.0, 0.0, 0.33, 0.07), (0.07, -0.03, 0.37, 0.05), (-0.04, 0.04, 0.4, 0.04), (0.0, 0.02, 0.42, 0.03)):
        ball(b, br, (x + dx, y + dy, dz), GLASS)                                    # the foam
    c = math.radians(112)
    cx, cy, _ = at(SKIRT + 0.09, c)
    crate(b, (0.2, 0.18, 0.15), M=T(cx, cy, 0) @ Rz(c + math.pi / 2), planks=1)


def kit5(b):   # Lv5: riveted iron plates bent round the lower drum between the feet
    for k in range(4):
        a = k * math.pi / 2
        curved_plate(b, 0.481, a - 0.32, a + 0.32, 0.09, 0.24, 0.022, IRON)
        for da in (-0.26, 0.26):
            for z in (0.115, 0.215):
                stud(b, at(0.503, a + da, z), at(1, a + da), r=0.014)


def kit6(b):   # Lv6: a crate of soap bottles against the left of the skirt
    x = -(SKIRT + 0.1)
    crate(b, (0.2, 0.32, 0.15), M=T(x, 0.02, 0) @ Rz(math.pi / 2), planks=1)
    for dx, dy, h in ((-0.04, -0.08, 0.1), (0.04, 0.0, 0.12), (-0.03, 0.09, 0.09)):
        cyl(b, 0.034, h, (x + dx, 0.02 + dy, 0.14 + h / 2), GLASS, segs=8, ch=0.01)
        cyl(b, 0.014, 0.05, (x + dx, 0.02 + dy, 0.14 + h + 0.02), GLASS, segs=6, ch=0.0)
        cyl(b, 0.017, 0.025, (x + dx, 0.02 + dy, 0.14 + h + 0.05), WOOD2, segs=6, ch=0.004)   # cork


def kit7(b):   # Lv7: the pennant at the front right, its cloth along the turn of the drum and toward the 3/4 view
    a = math.radians(-25)
    d = Vector(at(1, a))
    px, py, _ = d * 0.69
    bar(b, tuple(d * (SKIRT - 0.03) + Vector((0, 0, 0.025))), tuple(d * 0.75 + Vector((0, 0, 0.025))), 0.12, 0.05, IRON)  # its sill
    pennant(b, 1.0, CLOTH["Bubble"], M=T(px, py, 0.0) @ Rz(a + math.pi / 2))
    for z in (0.2, 0.36):                                           # brackets into the drum
        bar(b, tuple(d * 0.46 + Vector((0, 0, z))), (px, py, z), 0.04, 0.04, IRON)


def kit8(b):   # Lv8: the brass crest with a bubble on the upper drum's front, brass knobs on the feet
    a = -math.pi / 2
    curved_plate(b, 0.476, a - 0.22, a + 0.22, 0.255, 0.355, 0.02, BRASS)
    for da in (-0.18, 0.18):
        for z in (0.275, 0.335):
            stud(b, at(0.496, a + da, z), at(1, a + da), r=0.011, m=IRON)
    ball(b, 0.04, at(0.504, a, 0.305), GLASS, subdiv=1)                             # the bubble emblem
    ring(b, 0.052, 0.038, 0.02, at(0.506, a, 0.305), IRON, segs=10, axis="Y")
    for f in FEET:
        ball(b, 0.03, at(0.48, f, 0.28), BRASS)


# ---------------------------------------------------------------- final classes (static, from Lv9; Kit4 + Kit6 make way)
def tank(b, a, r=0.1, h=0.4):
    # A pressure tank on the ground against the skirt: iron body, brass bands, a domed top and a valve, a pipe into the drum.
    x, y, _ = at(SKIRT + r + 0.01, a)
    M = T(x, y, 0)
    rtier(b, r + 0.02, 0.0, 0.05, IRON, segs=10, ch=0.01, M=M)
    cyl(b, r, h - 0.13, (x, y, 0.05 + (h - 0.13) / 2), IRON, segs=10, ch=0.0)
    dome(b, r, h - 0.08, 0.07, IRON, segs=10, n=2, M=M)
    for z in (0.12, h - 0.13):
        rtier(b, r + 0.012, z - 0.016, z + 0.016, BRASS, segs=10, ch=0.0, M=M)
    cyl(b, 0.025, 0.04, (x, y, h - 0.005), BRASS, segs=6, ch=0.0)
    cyl(b, 0.05, 0.016, (x, y, h + 0.02), RED, segs=8, ch=0.004)                   # the valve wheel
    d = Vector(at(1, a))
    rod(b, tuple(d * (SKIRT + 0.02) + Vector((0, 0, 0.26))), tuple(d * 0.46 + Vector((0, 0, 0.26))), 0.026, IRON, segs=6)
    cyl(b, 0.04, 0.03, tuple(d * 0.5 + Vector((0, 0, 0.26))), BRASS, segs=8, rot=(0, math.pi / 2, a), ch=0.006)


def dress_a1(b):   # Dome: pressure tanks against the back and the left of the skirt, piped into the drum
    tank(b, math.radians(72))
    tank(b, math.radians(108), r=0.085, h=0.34)
    tank(b, math.radians(180))


def rune_stone(b, a, h, w=0.09):
    # A standing stone against the skirt, glowing runes cut into its outer face.
    d = 0.06
    x, y, _ = at(SKIRT + d - 0.005, a)
    M = T(x, y, 0) @ Rz(a)
    tier(b, d, w, 0.025, 0.0, h, STONE2, ch=0.02, taper=0.02, M=M)
    for k, (dz, ww, hh) in enumerate(((0.3, 0.07, 0.016), (0.42, 0.016, 0.07), (0.2, 0.05, 0.016))):
        z = h * dz / 0.5
        box(b, (0.012, ww, hh), (d - 0.002 - 0.02 * z / h, 0, z), CYAN, ch=0.0, M=M)


def dress_a2(b):   # Ward: rune stones against the back and the left of the skirt
    for a, h in ((math.radians(65), 0.4), (math.radians(95), 0.45), (math.radians(125), 0.35), (math.radians(180), 0.44)):
        rune_stone(b, a, h)


def dress_b1(b):   # Fortress: iron bulwarks curving round the back and the left, stayed to the skirt
    for a0, a1 in ((math.radians(58), math.radians(122)), (math.radians(150), math.radians(210))):
        curved_plate(b, 0.6, a0, a1, 0.0, 0.34, 0.04, IRON, segs=5)
        curved_plate(b, 0.6, a0 - 0.01, a1 + 0.01, 0.31, 0.35, 0.05, BRASS, segs=5)
        for k in range(4):
            a = a0 + (a1 - a0) * (k + 0.5) / 4
            for z in (0.08, 0.22):
                stud(b, at(0.64, a, z), at(1, a), r=0.016)
        for a in (a0 + 0.12, a1 - 0.12):                                            # stays to the skirt
            d = Vector(at(1, a))
            bar(b, tuple(d * (SKIRT - 0.02) + Vector((0, 0, 0.06))), tuple(d * 0.605 + Vector((0, 0, 0.2))), 0.04, 0.035, IRON)


def hex_shield(b, a):
    # A hexagonal glass shield in an iron frame on a stand, against the skirt, facing out.
    d = Vector(at(1, a))
    x, y, _ = d * (SKIRT + 0.1)
    tier(b, 0.1, 0.08, 0.02, 0.0, 0.05, IRON, ch=0.01, M=T(x, y, 0) @ Rz(a))           # the foot, onto the skirt's rim
    rod(b, (x, y, 0.05), (x, y, 0.15), 0.025, IRON, segs=6)
    F = T(x, y, 0.29) @ Rz(a) @ Ry(math.pi / 2)
    cyl(b, 0.16, 0.03, (0, 0, 0), GLASS, segs=6, ch=0.0, M=F)
    ring(b, 0.18, 0.15, 0.05, (0, 0, 0), IRON, segs=6, M=F)
    for k in range(6):
        stud(b, (math.cos(k * TAU / 6 + math.pi / 6) * 0.165, math.sin(k * TAU / 6 + math.pi / 6) * 0.165, 0.025), (0, 0, 1),
             r=0.014, M=F)


def dress_b2(b):   # Aegis: hex shields standing at the back and the left
    for a in (math.radians(68), math.radians(112), math.radians(180)):
        hex_shield(b, a)


model("BubbleBase", base)
model("BubbleBody", body, scale=SC)
model("BubbleDefault", default, scale=SC)
model("BubbleA", expanse, scale=SC)
model("BubbleA1", dome_a1, scale=SC)
model("BubbleA2", ward, scale=SC)
model("BubbleB", hardened, scale=SC)
model("BubbleB1", fortress, scale=SC)
model("BubbleB2", aegis, scale=SC)
model("BubbleBarrel", pod, scale=SC)
for n, f in ((2, kit2), (3, kit3), (4, kit4), (5, kit5), (6, kit6), (7, kit7), (8, kit8)):
    model("BubbleKit%d" % n, f)
for n, f in (("A1", dress_a1), ("A2", dress_a2), ("B1", dress_b1), ("B2", dress_b2)):
    model("BubbleDress" + n, f)
export_all()
layout("Bubble", head=H, muzzle=round(H + CROWN_Z * SC, 3), pod=None)
