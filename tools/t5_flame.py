# Flame turret, art pass 5 (tools/t5kit.py; no sheet of its own, built to the mortar's and the gun's finish). Plinth: a low
# octagonal stone bunker with iron-framed firing slits (TURRET-UPGRADE-DESIGN.md §0); on its iron turntable two upright red
# fuel tanks, a valve block and a thick short nozzle with a pilot flame, drawn at this size and exported SC bigger. Classes
# (§6): A Inferno (wide burning mouth, riveted heat shield) -> A1 Hellfire (three tanks, thick black nozzle, exhaust stacks)
# / A2 Melt (long thin lance, white-hot chamber, slag trough); B Wide (flat slot mouth fed at both ends) -> B1 Wall of fire
# (very wide fan mouth, side spray pipes, fuel manifold) / B2 Blue flame (blue tanks, blue-white mouth, low wide base).
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/t5_flame.py   [ONLY=FlameBase,FlameA ...]
import math
import os
import sys

import bmesh

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from t5kit import *  # noqa: F401,F403
from t5kit import _loft, _put  # noqa: E402

H = 0.56          # head pivot: the top of the bunker
SC = 1.2          # the head parts' scale (as the gun's and the mortar's)
# the head parts, in their drawn size:
AX = 0.42         # the nozzle's axis above the pivot
TOP = 0.08        # the turntable's top
DW, DCUT = 0.36, 0.12           # the turntable's half width and corner cut
POD = (0.27 * SC, TOP * SC, 0.05 * SC)     # extra nozzles (Unity head space): beside the valve block, on the turntable
STEP = (0.0, 0.12 * SC, 0.0)               # each further one stands on the one below

BLUE = mat("T5FlameBlue", (0.20, 0.36, 0.70))   # the Blue flame's painted tanks (a colour t5kit has no non-glowing one of)

# the bunker (static, real size)
FOOT, CUTF = 0.56, 0.2          # the foot's half width and corner cut
ZF, ZM, ZW = 0.08, 0.29, 0.46   # the tops of the foot and the two battered courses; the parapet up to H
W0, W1, WCUT = 0.52, 0.45, 0.18     # the battered wall's half width at its foot and its top, its corner cut
PAR, CUTP = 0.475, 0.18


def hw_at(z):
    if z <= ZF:
        return FOOT
    if z <= ZW:
        return W0 - (W0 - W1) * (z - ZF) / (ZW - ZF)
    return PAR


def cut_at(z):
    return CUTF if z <= ZF else WCUT if z <= ZW else CUTP


LEAN = math.atan2(W0 - W1, ZW - ZF)


def wall(face, z, diag=False):
    # A frame on the bunker's face at height z (face 0 = the front, counter-clockwise; diag: the cut corner after it):
    # origin on the surface, x along it, -y out of it, z up it (leaning with the batter).
    hw, c = hw_at(z), cut_at(z)
    if diag:
        return Rz(face * math.pi / 2 + math.pi / 4) @ T(0, -(hw - c / 2) * math.sqrt(2), z) @ Rx(-LEAN if ZF < z <= ZW else 0)
    return Rz(face * math.pi / 2) @ T(0, -hw, z) @ Rx(-LEAN if ZF < z <= ZW else 0)


# ---------------------------------------------------------------- local helpers
def lathe(b, prof, M, m=None, segs=12, bore=None, bore_m=None):
    # A solid of revolution round local Z, prof = [(z, r), ...]; bore = (r, depth) opens the last face onto a dark bore.
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


def fwd(x, y, z):
    # A frame at (x, y, z) whose local Z points forward (-Y), for nozzles.
    return T(x, y, z) @ Rx(math.pi / 2)


def hose(b, pts, r=0.02, m=None):
    for p, q in zip(pts, pts[1:]):
        rod(b, p, q, r, m or IRON, segs=6)


def flame(b, loc, d=(0, -1, 0.5), r=0.03, h=0.09, m=None):
    # A tongue of flame rooted at loc, along d: a round bulb drawn out to a point that leans a little, its facets turned
    # at each step.
    F, _ = toward(loc, Vector(loc) + Vector(d))
    tmp = bmesh.new()
    _loft(tmp, [[(0, 0, 0)], circle(r * 0.75, 5, h * 0.12), circle(r, 5, h * 0.32, phase=0.0), circle(r * 0.55, 5, h * 0.62),
                [(r * 0.2, 0, h)]], [0])
    _put(b, tmp, F, m or FIRE)


def tank(b, x, y, r=0.13, h=0.52, m=None, z0=TOP, bands=(0.22, 0.6)):
    # An upright fuel tank (a domed cylinder) with brass bands and a brass valve on its crown.
    lathe(b, [(0.0, r * 0.92), (0.025, r), (h - r * 0.55, r), (h - r * 0.2, r * 0.72), (h, r * 0.28)], T(x, y, z0), m or RED, segs=10)
    for k in bands:
        ring(b, r * 1.04, r * 0.95, 0.03, (x, y, z0 + h * k), BRASS, segs=10)
    cyl(b, 0.03, 0.05, (x, y, z0 + h + 0.02), BRASS, segs=6, ch=0.0)
    ring(b, 0.045, 0.034, 0.012, (x, y, z0 + h + 0.05), BRASS, segs=6)


def lying_tank(b, x, y, z, r=0.1, l=0.42, m=None):
    # A fuel tank on its side along Y, domed ends, brass bands.
    F = T(x, y - l / 2, z) @ Rx(-math.pi / 2)
    lathe(b, [(0.0, r * 0.3), (r * 0.25, r * 0.75), (r * 0.55, r), (l - r * 0.55, r), (l - r * 0.25, r * 0.75), (l, r * 0.3)], F,
          m or RED, segs=10)
    for k in (0.28, 0.72):
        ring(b, r * 1.04, r * 0.95, 0.03, (x, y - l / 2 + l * k, z), BRASS, segs=10, axis="Y")


def cradle(b, xs, y, h, r=0.13):
    # The iron base plate the upright tanks stand on and a strap bar behind them.
    box(b, (abs(xs[-1] - xs[0]) + 2 * r + 0.04, 2 * r + 0.04, 0.03), ((xs[0] + xs[-1]) / 2, y, TOP + 0.015), IRON, ch=0.008)
    bar(b, (xs[0], y + r + 0.01, TOP + h * 0.42), (xs[-1], y + r + 0.01, TOP + h * 0.42), 0.04, 0.03, IRON, ch=0.0)


def valve_block(b, y=-0.06, w=0.28, d=0.26, h=0.25):
    # The valve block on its pedestal, a brass handwheel on top, rivets on its cheeks.
    box(b, (0.2, 0.22, AX - h / 2 - TOP + 0.01), (0, y + 0.02, (TOP + AX - h / 2) / 2), IRON, ch=0.012)
    box(b, (w, d, h), (0, y, AX), IRON, ch=0.03)
    for s in (-1, 1):
        for dy in (-0.06, 0.06):
            sqstud(b, (s * w / 2, y + dy, AX), (s, 0, 0), s=0.035, m=BRASS)
    cyl(b, 0.014, 0.06, (0, y, AX + h / 2 + 0.03), BRASS, segs=6, ch=0.0)
    ring(b, 0.07, 0.056, 0.02, (0, y, AX + h / 2 + 0.06), BRASS, segs=10)
    for a in (0.0, math.pi / 2):
        box(b, (0.13, 0.012, 0.012), (0, y, AX + h / 2 + 0.06), BRASS, ch=0.0, rot=(0, 0, a))


def prof_r(p, z):
    # A lathe outline's radius at z (the larger one where it steps).
    for (z0, r0), (z1, r1) in zip(p, p[1:]):
        if z0 <= z <= z1:
            return max(r0, r1) if z1 - z0 < 1e-6 else r0 + (r1 - r0) * (z - z0) / (z1 - z0)
    return p[-1][1]


NOZZLE = ((0.0, 0.125), (0.065, 0.125), (0.075, 0.095), (0.3, 0.095), (0.44, 0.165), (0.49, 0.165))


def nozzle(b, y0=-0.18, prof=NOZZLE, m=None, bore_m=None, rings=(0.035, 0.3), pilot=True, s=1.0):
    # The thick short nozzle out of the valve block's face: a collar, the barrel, the flared mouth, a pilot flame under it.
    p = [(z * s, r * s) for z, r in prof]
    lathe(b, p, fwd(0, y0, AX), m or STEEL, segs=12, bore=(p[-1][1] * 0.72, 0.12 * s), bore_m=bore_m)
    for z in rings:
        r = prof_r(p, z * s) * 1.08
        ring(b, r, r * 0.8, 0.03 * s, (0, 0, z * s), BRASS, segs=12, M=fwd(0, y0, AX))
    if pilot:
        ye, zp = y0 - p[-1][0] - 0.02, AX - p[-1][1] - 0.03
        rod(b, (0, y0 - 0.06, zp), (0, ye, zp), 0.016, BRASS, segs=6)
        for yy in (y0 - 0.12, y0 - 0.27 * s):
            bar(b, (0, yy, AX - 0.07), (0, yy, zp), 0.018, 0.018, IRON, ch=0.0)
        flame(b, (0, ye - 0.01, zp + 0.01), d=(0, -1, 0.35), r=0.026, h=0.08)


# ---------------------------------------------------------------- plinth (static): the low bunker
def seam(b, face, u, z0, z1, diag=False):
    za, zc = z0 + 0.018, z1 - 0.024
    for (a, c) in ((za, zc),):
        pa = wall(face, a, diag) @ Vector((u, -0.002, 0))
        pc = wall(face, c, diag) @ Vector((u, -0.002, 0))
        n = (wall(face, (a + c) / 2, diag).to_3x3() @ Vector((0, -1, 0)))
        bar(b, tuple(pa), tuple(pc), 0.016, 0.008, STONE2, ch=0.0, up=tuple(n))


def slit(b, face):
    # An iron-framed firing slit in the battered wall.
    M = wall(face, 0.19)
    box(b, (0.32, 0.04, 0.07), (0, 0.012, 0), DARK, ch=0.0, M=M)
    box(b, (0.4, 0.045, 0.035), (0, -0.002, 0.052), IRON, ch=0.008, M=M)
    box(b, (0.4, 0.05, 0.03), (0, -0.004, -0.05), IRON, ch=0.008, M=M)
    for s in (-1, 1):
        box(b, (0.03, 0.04, 0.08), (s * 0.18, -0.002, 0), IRON, ch=0.0, M=M)


def base(b):
    tier(b, FOOT, FOOT, CUTF, 0.0, ZF, STONE2, ch=0.03, chb=0.012)                              # foot
    tier(b, W0, W0, WCUT, ZF, ZM, STONE, ch=0.02, taper=hw_at(ZF) - hw_at(ZM))                 # the battered wall
    tier(b, hw_at(ZM), hw_at(ZM), WCUT, ZM, ZW, STONE, ch=0.02, taper=hw_at(ZM) - hw_at(ZW))
    tier(b, PAR, PAR, CUTP, ZW, H, STONE, ch=0.03, chb=0.025)                                   # parapet
    for f in range(4):
        slit(b, f)
        for u in (-0.2, 0.2):
            seam(b, f, u, 0.0, ZF)
            seam(b, f, u * 1.4, ZF, ZM)
        for u in (-0.08, 0.26):
            seam(b, f, u if f % 2 else -u, ZM, ZW)
        for u in (-0.16, 0.16):
            seam(b, f, u, ZW + 0.01, H - 0.01)
        seam(b, f, 0.0, ZF, ZM, diag=True)
        seam(b, f, 0.0, ZW + 0.01, H - 0.01, diag=True)


# ---------------------------------------------------------------- turntable (turns)
def body(b):
    tier(b, DW, DW, DCUT, 0.0, TOP, IRON, ch=0.016)
    ring(b, 0.2, 0.15, 0.02, (0, 0.04, TOP + 0.01), BRASS, segs=12)       # the race the head turns on
    for f in range(4):
        for u in (-0.2, 0.2):
            sqstud(b, (u, -DW, TOP / 2), (0, -1, 0), s=0.035, m=BRASS, M=Rz(f * math.pi / 2))


# ---------------------------------------------------------------- heads (turn)
def twin_tanks(b, m=None, h=0.52, y=0.19):
    cradle(b, (-0.16, 0.16), y, h)
    for s in (-1, 1):
        tank(b, s * 0.16, y, h=h, m=m)
        hose(b, [(s * 0.16, y - 0.05, TOP + h - 0.02), (s * 0.12, y - 0.1, TOP + h - 0.04), (s * 0.07, 0.07, AX + 0.07)])


def default(b):
    valve_block(b)
    nozzle(b)
    twin_tanks(b)


def heat_shield(b):
    # A riveted iron shield across the valve block's back, between the burner and the tanks, a brass edge along its foot.
    F = T(0, 0.035, AX + 0.12)
    box(b, (0.6, 0.035, 0.32), (0, 0, 0), IRON, ch=0.012, M=F)
    for u, v in CORNERS4:
        stud(b, (u * 0.26, -0.018, v * 0.12), (0, -1, 0), r=0.022, M=F)
    box(b, (0.62, 0.045, 0.03), (0, 0.035, AX - 0.035), BRASS, ch=0.006)


INFERNO = ((0.0, 0.11), (0.06, 0.11), (0.07, 0.09), (0.26, 0.09), (0.42, 0.19), (0.47, 0.19))


def inferno(b):
    valve_block(b)
    nozzle(b, prof=INFERNO, bore_m=FIRE)
    heat_shield(b)
    twin_tanks(b, h=0.5)


HELL = ((0.0, 0.14), (0.07, 0.14), (0.08, 0.12), (0.3, 0.12), (0.42, 0.21), (0.5, 0.21))


def hellfire(b):
    valve_block(b, w=0.3)
    nozzle(b, prof=HELL, m=DARK, bore_m=FIRE, rings=(0.04, 0.4))
    heat_shield(b)
    # three tanks in a row behind the shield, the middle one taller
    y = 0.19
    cradle(b, (-0.23, 0.23), y, 0.5, r=0.11)
    for x, h in ((-0.23, 0.48), (0.0, 0.6), (0.23, 0.48)):
        tank(b, x, y, r=0.11, h=h)
        hose(b, [(x, y - 0.05, TOP + h - 0.02), (x * 0.6, y - 0.1, TOP + h - 0.03), (x * 0.3, 0.07, AX + 0.07)])
    # the exhaust stacks on top: iron flues up the valve block's cheeks, burning at their mouths
    for s in (-1, 1):
        x = s * 0.2
        rod(b, (x, -0.025, AX - 0.06), (x, -0.025, AX + 0.46), 0.04, IRON, segs=8)
        lathe(b, [(0.0, 0.045), (0.05, 0.065), (0.08, 0.065)], T(x, -0.025, AX + 0.44), IRON, segs=8, bore=(0.045, 0.05),
              bore_m=FIRE)
        ring(b, 0.05, 0.04, 0.03, (x, -0.025, AX + 0.2), BRASS, segs=8)
        box(b, (0.08, 0.06, 0.05), (s * 0.165, -0.025, AX - 0.03), IRON, ch=0.0)
        flame(b, (x, -0.025, AX + 0.49), d=(0, 0.25, 1), r=0.055, h=0.16)


LANCE = ((0.0, 0.11), (0.06, 0.11), (0.07, 0.05), (0.2, 0.05), (0.24, 0.04), (0.78, 0.04), (0.82, 0.055), (0.86, 0.055))


def melt(b):
    # The white-hot chamber in an iron cage where the valve block was, a long thin lance out of it, a slag trough under it.
    c = (0, -0.06, AX)
    box(b, (0.2, 0.22, AX - 0.15 - TOP), (0, -0.04, (TOP + AX - 0.15) / 2), IRON, ch=0.012)
    ball(b, 0.15, c, WHITE, subdiv=2)
    ring(b, 0.158, 0.14, 0.03, c, FIRE, segs=12)
    for a in (math.pi / 4, -math.pi / 4):
        ring(b, 0.172, 0.148, 0.035, (0, 0, 0), IRON, segs=12, M=T(*c) @ Rz(a), axis="X")
    lathe(b, [(0.0, 0.05), (0.06, 0.042), (0.09, 0.055)], T(0, -0.06, AX + 0.14), IRON, segs=8, bore=(0.04, 0.04), bore_m=FIRE)
    flame(b, (0, -0.06, AX + 0.2), d=(0, 0.2, 1), r=0.045, h=0.13)
    nozzle(b, y0=-0.18, prof=LANCE, rings=(0.035, 0.45, 0.7), pilot=False, bore_m=FIRE)
    # the slag trough on the turntable under the lance, glowing slag in it
    y = -0.3
    box(b, (0.28, 0.14, 0.03), (0, y, TOP + 0.015), IRON, ch=0.006)
    for s in (-1, 1):
        box(b, (0.28, 0.02, 0.08), (0, y + s * 0.06, TOP + 0.055), IRON, ch=0.004)
        box(b, (0.02, 0.14, 0.08), (s * 0.13, y, TOP + 0.055), IRON, ch=0.004)
    box(b, (0.24, 0.1, 0.02), (0, y, TOP + 0.07), FIRE, ch=0.0)
    for x in (-0.06, 0.05):
        gem(b, 0.03, 0.035, 0.0, (x, y, TOP + 0.08), FIRE, segs=5)
    # the tanks behind, a heat baffle over the chamber
    twin_tanks(b)


WIDE = ((0.0, 0.11), (0.06, 0.11), (0.07, 0.085), (0.26, 0.085))


def slot_mouth(b, w=0.5, y=-0.5, n=4, glow=None):
    # The flat wide mouth: a tapered iron box ending in a dark slot with glowing vanes.
    tmp = bmesh.new()
    rings = [[(x, y + 0.06, z) for x, z in ((-0.1, -0.1), (0.1, -0.1), (0.1, 0.1), (-0.1, 0.1))],
             [(x, y - 0.12, z) for x, z in ((-w / 2, -0.06), (w / 2, -0.06), (w / 2, 0.06), (-w / 2, 0.06))],
             [(x, y - 0.18, z) for x, z in ((-w / 2, -0.06), (w / 2, -0.06), (w / 2, 0.06), (-w / 2, 0.06))]]
    _loft(tmp, rings, [0], closed=False)
    _put(b, tmp, T(0, 0, AX), IRON)
    box(b, (w - 0.04, 0.02, 0.07), (0, y - 0.18, AX), DARK, ch=0.0)
    for k in range(n):
        x = (k - (n - 1) / 2) * (w - 0.1) / max(1, n - 1)
        box(b, (0.035, 0.025, 0.06), (x, y - 0.185, AX), glow or FIRE, ch=0.0)
    box(b, (w + 0.02, 0.03, 0.135), (0, y - 0.16, AX), BRASS, ch=0.008)


def feed_pipes(b, xs, y1, z=AX + 0.06, m=None):
    # Pipes from the valve block's cheeks forward to the mouth's ends.
    for x in xs:
        s = 1 if x > 0 else -1
        hose(b, [(s * 0.14, -0.1, AX - 0.03), (x * 0.95, -0.24, AX - 0.08), (x, y1, AX - 0.03)], r=0.022, m=m or BRASS)


def wide(b):
    valve_block(b)
    nozzle(b, prof=WIDE, rings=(0.035,), pilot=False)
    slot_mouth(b, w=0.5, y=-0.46)
    feed_pipes(b, (-0.21, 0.21), -0.56)
    twin_tanks(b)


def wall_of_fire(b):
    valve_block(b)
    nozzle(b, prof=WIDE, rings=(0.035,), pilot=False)
    slot_mouth(b, w=0.84, y=-0.46, n=7)
    # spray pipes off the mouth's ends, angled out
    for s in (-1, 1):
        x = s * 0.42
        rod(b, (x, -0.62, AX), (s * 0.56, -0.74, AX), 0.035, STEEL, segs=8, r2=0.055)
        ring(b, 0.06, 0.045, 0.02, (0, 0, 0), BRASS, segs=8, M=toward((s * 0.56, -0.74, AX), (s * 0.66, -0.83, AX))[0])
        flame(b, (s * 0.555, -0.735, AX), d=(s * 0.7, -1, 0.3), r=0.055, h=0.15)
    feed_pipes(b, (-0.36, 0.36), -0.6)
    # the fuel manifold on the turntable: a brass header between the tanks and the valve block, three valves on it
    box(b, (0.4, 0.1, 0.08), (0, 0.06, TOP + 0.04), BRASS, ch=0.01)
    for x in (-0.13, 0.0, 0.13):
        cyl(b, 0.025, 0.06, (x, 0.06, TOP + 0.1), BRASS, segs=6, ch=0.0)
        ring(b, 0.035, 0.025, 0.012, (x, 0.06, TOP + 0.13), IRON, segs=8)
    twin_tanks(b)


BLUEJET = ((0.0, 0.11), (0.06, 0.11), (0.07, 0.08), (0.34, 0.08), (0.38, 0.1), (0.46, 0.1))


def blue_flame(b):
    # Low and wide: the valve block straight on a broad sled, the blue tanks lying behind, a blue-white mouth.
    box(b, (0.4, 0.5, 0.08), (0, 0.04, TOP + 0.04), IRON, ch=0.015)
    for u, v in CORNERS4:
        sqstud(b, (u * 0.16, 0.04 + v * 0.21, TOP + 0.08), s=0.035, m=BRASS)
    box(b, (0.3, 0.24, 0.2), (0, -0.06, AX - 0.04), IRON, ch=0.03)
    box(b, (0.18, 0.18, AX - 0.14 - TOP - 0.08), (0, -0.04, (TOP + 0.08 + AX - 0.14) / 2), IRON, ch=0.01)
    lathe(b, [(z, r) for z, r in BLUEJET], fwd(0, -0.18, AX - 0.04), STEEL, segs=12, bore=(0.075, 0.12), bore_m=ICE)
    ring(b, 0.112, 0.09, 0.03, (0, 0, 0.035), BRASS, segs=12, M=fwd(0, -0.18, AX - 0.04))
    ring(b, 0.11, 0.098, 0.04, (0, 0, 0.42), ICE, segs=12, M=fwd(0, -0.18, AX - 0.04))
    flame(b, (0, -0.66, AX - 0.17), d=(0, -1, 0.35), r=0.026, h=0.08, m=CYAN)
    rod(b, (0, -0.24, AX - 0.17), (0, -0.65, AX - 0.17), 0.016, BRASS, segs=6)
    bar(b, (0, -0.3, AX - 0.12), (0, -0.3, AX - 0.17), 0.018, 0.018, IRON, ch=0.0)
    for s in (-1, 1):
        lying_tank(b, s * 0.15, 0.2, TOP + 0.08 + 0.105, r=0.1, l=0.4, m=BLUE)
        hose(b, [(s * 0.15, 0.0, TOP + 0.26), (s * 0.1, -0.02, AX + 0.02)], r=0.02, m=BRASS)
        for y in (0.07, 0.33):
            box(b, (0.24, 0.04, 0.05), (s * 0.15, y, TOP + 0.105), IRON, ch=0.006)


# ---------------------------------------------------------------- extra nozzle (turns, on the turntable)
def pod(b):
    # rear-bottom-centre at the origin, pointing -Y: an iron block (the next one stands on it), a short nozzle, its flame.
    box(b, (0.11, 0.18, 0.12), (0, -0.09, 0.06), IRON, ch=0.014)
    for s in (-1, 1):
        sqstud(b, (s * 0.055, -0.09, 0.06), (s, 0, 0), s=0.03, m=BRASS)
    lathe(b, [(0.0, 0.045), (0.04, 0.045), (0.05, 0.035), (0.16, 0.035), (0.22, 0.055), (0.24, 0.055)], fwd(0, -0.18, 0.06),
          STEEL, segs=8, bore=(0.038, 0.06))
    ring(b, 0.048, 0.036, 0.02, (0, 0, 0.02), BRASS, segs=8, M=fwd(0, -0.18, 0.06))
    flame(b, (0, -0.43, 0.02), d=(0, -1, 0.4), r=0.02, h=0.06)
    rod(b, (0, -0.22, 0.015), (0, -0.42, 0.015), 0.01, BRASS, segs=4)


# ---------------------------------------------------------------- one piece a level (static, round the bunker)
# Where they stand (the 3/4 view sees the front and the right): Lv4 against the right face's front half, Lv6 before the
# front face's left half, the pennant at the right face's back half; a final class's dressing takes over the first two.
KIT4 = (FOOT + 0.11, -0.16)
KIT6 = (-0.22, -(FOOT + 0.08))


def kit2(b):   # Lv2: riveted iron armour on the foot's and the parapet's cut corners
    for f in range(4):
        M = wall(f, ZF / 2 + 0.01, diag=True)
        plate(b, 0.26, 0.12, 0.03, (0, 0, 0), (0, -1, 0), IRON, rivets=((-1, 0), (1, 0)), rv=0.022, M=M, rivet_m=BRASS)
        M = wall(f, (ZW + H) / 2, diag=True)
        plate(b, 0.22, 0.085, 0.03, (0, 0, 0), (0, -1, 0), IRON, rivets=((-1, 0), (1, 0)), rv=0.018, M=M, rivet_m=BRASS)


def kit3(b):   # Lv3: a brass band round the parapet, rivets on its faces
    tier(b, PAR + 0.006, PAR + 0.006, CUTP + 0.003, ZW + 0.035, ZW + 0.065, BRASS, ch=0.0)
    for f in range(4):
        for u in (-0.17, 0.17):
            stud(b, (u, -(PAR + 0.006), ZW + 0.05), (0, -1, 0), r=0.016, M=Rz(f * math.pi / 2))


def kit4(b):   # Lv4: red fuel drums against the right face, a hose into the wall
    x, y = KIT4
    for dy, h in ((-0.1, 0.32), (0.13, 0.26)):
        cyl(b, 0.1, h, (x, y + dy, h / 2), RED, segs=10, ch=0.012)
        for k in (0.3, 0.7):
            ring(b, 0.104, 0.095, 0.022, (x, y + dy, h * k), BRASS, segs=10)
        cyl(b, 0.025, 0.025, (x + 0.04, y + dy, h + 0.012), BRASS, segs=6, ch=0.0)
    hose(b, [(x - 0.02, y - 0.1, 0.33), (x - 0.08, y - 0.1, 0.36), (hw_at(0.3) + 0.004, y - 0.1, 0.3)], r=0.018)


def kit5(b):   # Lv5: riveted heat-shield plates on the battered wall's cut corners
    for f in range(4):
        M = wall(f, (ZF + ZW) / 2, diag=True)
        plate(b, 0.24, 0.34, 0.03, (0, 0, 0), (0, -1, 0), IRON, rivets=CORNERS4, rv=0.02, M=M, rivet_m=BRASS)


def kit6(b):   # Lv6: a rack of gas bottles before the front face
    x, y = KIT6
    box(b, (0.36, 0.14, 0.03), (x, y, 0.015), IRON, ch=0.006)
    for s in (-1, 1):
        box(b, (0.03, 0.03, 0.34), (x + s * 0.17, y + 0.05, 0.17), IRON, ch=0.0)
    box(b, (0.37, 0.03, 0.03), (x, y + 0.05, 0.26), IRON, ch=0.0)
    for k, m in enumerate((STEEL, RED, STEEL)):
        bx = x + (k - 1) * 0.11
        lathe(b, [(0.0, 0.045), (0.24, 0.045), (0.28, 0.03), (0.3, 0.012)], T(bx, y - 0.005, 0.03), m, segs=8)
        cyl(b, 0.012, 0.04, (bx, y - 0.005, 0.35), BRASS, segs=6, ch=0.0)
        ring(b, 0.025, 0.018, 0.01, (bx, y - 0.005, 0.37), BRASS, segs=6)


def kit7(b):   # Lv7: the turret's pennant on the right, the cloth flying back towards the 3/4 view, below the head's sweep
    px, py = FOOT + 0.035, 0.08
    pennant(b, H - 0.08, CLOTH["Flame"], M=T(px, py, 0.0) @ Rz(math.radians(75)))
    box(b, (0.09, 0.09, 0.03), (px, py, 0.015), IRON, ch=0.006)
    for z in (0.05, 0.36):
        bar(b, (hw_at(z) - 0.01, py, z), (px, py, z), 0.035, 0.035, IRON, ch=0.0)


def kit8(b):   # Lv8: the brass flame crest on the front, brass knobs on the foot's corners
    M = wall(0, 0.37)
    plate(b, 0.2, 0.16, 0.025, (0, 0, 0), (0, -1, 0), BRASS, rivets=CORNERS4, rv=0.014, M=M, rivet_m=IRON)
    for dx, h, r in ((0.0, 0.11, 0.035), (-0.045, 0.07, 0.024), (0.045, 0.07, 0.024)):
        flame(b, tuple(M @ Vector((dx, -0.04, -0.035))), d=tuple(M.to_3x3() @ Vector((0, 0, 1))), r=r, h=h)
    for f in range(4):
        ball(b, 0.03, tuple(wall(f, ZF, diag=True) @ Vector((0, -0.03, 0.02))), BRASS)


# ---------------------------------------------------------------- final classes (static, from Lv9; Kit4 + Kit6 make way)
def drum(b, loc, r=0.1, h=0.3, m=None):
    x, y, z = loc
    cyl(b, r, h, (x, y, z + h / 2), m or RED, segs=10, ch=0.012)
    for k in (0.3, 0.7):
        ring(b, r * 1.04, r * 0.95, 0.022, (x, y, z + h * k), BRASS, segs=10)
    cyl(b, r * 0.25, 0.025, (x + r * 0.45, y, z + h + 0.012), BRASS, segs=6, ch=0.0)


def brazier(b, x, y, glow=None):
    rtier(b, 0.11, 0.0, 0.05, IRON, segs=8, ch=0.01, M=T(x, y, 0.0))
    for k in range(3):
        a = k * TAU / 3 + math.pi / 6
        bar(b, (x + math.cos(a) * 0.07, y + math.sin(a) * 0.07, 0.05), (x + math.cos(a) * 0.11, y + math.sin(a) * 0.11, 0.24),
            0.028, 0.028, IRON, ch=0.0)
    rtier(b, 0.11, 0.22, 0.29, IRON, segs=8, ch=0.01, r_top=0.13, M=T(x, y, 0.0))
    for k in range(5):
        a = k * TAU / 5
        flame(b, (x + math.cos(a) * 0.055, y + math.sin(a) * 0.055, 0.3), d=(0, 0, 1), r=0.034, h=0.1, m=glow)


def dress_a1(b):   # Hellfire: three fuel drums before the front (one laid across the other two), a brazier on the right
    x, y = KIT6
    for dx in (-0.1, 0.11):
        drum(b, (x + dx, y, 0.0), h=0.3)
    cyl(b, 0.085, 0.3, (x + 0.005, y, 0.3 + 0.085), RED, segs=10, ch=0.01, axis="X")
    for dx in (-0.09, 0.1):
        ring(b, 0.089, 0.08, 0.02, (x + 0.005 + dx, y, 0.385), BRASS, segs=10, axis="X")
    hose(b, [(x + 0.13, y + 0.03, 0.3), (x + 0.18, -(hw_at(0.33) + 0.06), 0.36), (x + 0.22, -(hw_at(0.33) + 0.004), 0.33)])
    brazier(b, *KIT4)
    bar(b, (KIT4[0] - 0.11, KIT4[1], 0.1), (FOOT, KIT4[1], 0.1), 0.028, 0.028, IRON, ch=0.0)


def dress_a2(b):   # Melt: a slag trough before the front, a little furnace on the right
    x, y = KIT6
    box(b, (0.38, 0.16, 0.03), (x, y, 0.015), IRON, ch=0.006)
    for s in (-1, 1):
        box(b, (0.38, 0.02, 0.1), (x, y + s * 0.07, 0.065), IRON, ch=0.004)
        box(b, (0.02, 0.16, 0.1), (x + s * 0.18, y, 0.065), IRON, ch=0.004)
    box(b, (0.34, 0.12, 0.02), (x, y, 0.09), FIRE, ch=0.0)
    for dx in (-0.1, 0.03, 0.12):
        gem(b, 0.035, 0.04, 0.0, (x + dx, y, 0.1), FIRE, segs=5)
    for dx, h in ((-0.25, 0.07), (0.23, 0.06)):   # cooled slag heaped at its ends
        gem(b, 0.06, h, 0.0, (x + dx, y + 0.02, 0.0), DARK, segs=5)
    x, y = KIT4
    x -= 0.02
    box(b, (0.2, 0.28, 0.28), (x, y, 0.14), STONE2, ch=0.02)
    box(b, (0.02, 0.14, 0.12), (x, y - 0.14, 0.13), DARK, ch=0.0, rot=(0, 0, math.pi / 2))
    box(b, (0.015, 0.1, 0.08), (x, y - 0.145, 0.12), FIRE, ch=0.0, rot=(0, 0, math.pi / 2))
    box(b, (0.22, 0.3, 0.04), (x, y, 0.3), IRON, ch=0.01)
    cyl(b, 0.05, 0.15, (x + 0.03, y + 0.07, 0.395), IRON, segs=8, ch=0.008)       # its flue, under the head's sweep
    flame(b, (x + 0.03, y + 0.07, 0.47), d=(0, 0, 1), r=0.03, h=0.08)


def dress_b1(b):   # Wall of fire: a fuel manifold before the front, burner heads in the ground on the right
    x, y = KIT6
    rod(b, (x - 0.2, y, 0.08), (x + 0.2, y, 0.08), 0.035, BRASS, segs=8)
    for dx in (-0.16, 0.0, 0.16):
        box(b, (0.06, 0.06, 0.08), (x + dx, y, 0.04), IRON, ch=0.008)
        rod(b, (x + dx, y, 0.08), (x + dx, y, 0.2), 0.02, BRASS, segs=6)
        ring(b, 0.045, 0.03, 0.015, (x + dx, y, 0.21), IRON, segs=8)
        hose(b, [(x + dx, y + 0.03, 0.1), (x + dx * 0.6, -(hw_at(0.14) + 0.004), 0.14)], r=0.016)
    x, y = KIT4
    for dy in (-0.14, 0.0, 0.14):
        cyl(b, 0.06, 0.06, (x, y + dy, 0.03), IRON, segs=8, ch=0.008)
        cyl(b, 0.035, 0.03, (x, y + dy, 0.075), STEEL, segs=8, ch=0.0)
        flame(b, (x, y + dy, 0.09), d=(0, 0, 1), r=0.03, h=0.13)
    rod(b, (FOOT, y - 0.14, 0.03), (x, y - 0.14, 0.03), 0.015, BRASS, segs=4)
    rod(b, (x, y - 0.14, 0.03), (x, y + 0.14, 0.03), 0.015, BRASS, segs=4)


def dress_b2(b):   # Blue flame: blue gas bottles racked before the front, frost on the ground on the right
    x, y = KIT6
    box(b, (0.42, 0.16, 0.03), (x, y, 0.015), IRON, ch=0.006)
    for s in (-1, 1):
        box(b, (0.03, 0.03, 0.38), (x + s * 0.2, y + 0.06, 0.19), IRON, ch=0.0)
    box(b, (0.43, 0.03, 0.03), (x, y + 0.06, 0.3), IRON, ch=0.0)
    for k in range(4):
        bx = x + (k - 1.5) * 0.1
        lathe(b, [(0.0, 0.042), (0.26, 0.042), (0.3, 0.028), (0.32, 0.012)], T(bx, y - 0.01, 0.03), BLUE, segs=8)
        cyl(b, 0.012, 0.04, (bx, y - 0.01, 0.37), BRASS, segs=6, ch=0.0)
    x, y = KIT4
    for dx, dy, s in ((-0.02, -0.08, 1.0), (-0.05, 0.1, 0.8), (0.04, 0.02, 0.65), (-0.06, -0.2, 0.55)):
        gem(b, 0.05 * s, 0.2 * s, 0.02, (x + dx, y + dy, 0.0), ICE, segs=5)
    box(b, (0.18, 0.4, 0.015), (x - 0.02, y - 0.04, 0.0075), ICE, ch=0.0)


model("FlameBase", base)
model("FlameBody", body, scale=SC)
model("FlameDefault", default, scale=SC)
model("FlameA", inferno, scale=SC)
model("FlameA1", hellfire, scale=SC)
model("FlameA2", melt, scale=SC)
model("FlameB", wide, scale=SC)
model("FlameB1", wall_of_fire, scale=SC)
model("FlameB2", blue_flame, scale=SC)
model("FlameBarrel", pod, scale=SC)
for n, f in ((2, kit2), (3, kit3), (4, kit4), (5, kit5), (6, kit6), (7, kit7), (8, kit8)):
    model("FlameKit%d" % n, f)
for n, f in (("A1", dress_a1), ("A2", dress_a2), ("B1", dress_b1), ("B2", dress_b2)):
    model("FlameDress" + n, f)
export_all()
layout("Flame", head=H, muzzle=round(H + AX * SC, 3), pod=[round(v, 4) for v in POD], step=[round(v, 4) for v in STEP])
