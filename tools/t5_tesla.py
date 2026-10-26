# Tesla turret, art pass 5 (tools/t5kit.py; Owen's Tesla sheet, /tmp/t5/full/10.png). Plinth: the stepped stone base
# with an iron foot band and corner blocks (sheet 1). Head: an iron column with copper rings and an iron claw holding a pale
# crystal (sheet 1). Classes (TURRET-UPGRADE-DESIGN.md §5): A Spread (conical coil, three-prong fork, sheet 9) -> A1 Storm
# chain (four discharge forks, big top electrode, glass insulators, sheet 6) / A2 Conduction (blue water-ring coils, conduit
# pipes, grounding chain); B Overload (tall copper solenoid under a flat crystal disc, sheets 3 / 5) -> B1 Overcharge
# (transformer, two capacitor jars, red overload gauge, sheet 2) / B2 Arc blast (spherical arc cage round a bright core,
# six radial electrodes).
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/t5_tesla.py   [ONLY=TeslaBase,TeslaA ...]
import math
import os
import sys

import bmesh
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import t5kit as K  # noqa: E402
from t5kit import *  # noqa: F401,F403,E402

H = 0.52          # head pivot: the top of the plinth
TOP = 0.05        # the turntable's top (where the column socket and the pods stand); head parts are drawn at this size,
SC = 1.2          # exported SC bigger (the coil reads big against its plinth)
POD = (0.2, TOP, 0.05)      # extra coil pylons (drawn, Unity head space): |x| beside the column, on the turntable, forward
STEP = (0.0, 0.0, -0.2)    # the next pair stands behind the first
CRYSTAL_Z = 1.03  # the default crystal's centre above the pivot, drawn (the arc leaves from it)

FOOT, BLOCK, UPPER, CAP = 0.52, 0.42, 0.36, 0.33   # half widths: the iron foot band, stone block, upper step, top
FC = 0.34         # the corner blocks' centres on the diagonals
PEN = math.atan2(0.644, 0.765)   # the pennant's cloth runs along the 3/4 view's right, so it faces that camera


# ---------------------------------------------------------------- local helpers
def cring(b, r_out, r_in, h, z, m=None, segs=12, ch=None, M=I4):
    # A fat ring with chamfered edges (the sheet's copper rings): a closed loft of a chamfered rectangle round Z.
    ch = min(h * 0.32, (r_out - r_in) * 0.3) if ch is None else ch
    prof = [(r_in, -h / 2 + ch), (r_in + ch, -h / 2), (r_out - ch, -h / 2), (r_out, -h / 2 + ch),
            (r_out, h / 2 - ch), (r_out - ch, h / 2), (r_in + ch, h / 2), (r_in, h / 2 - ch)]
    tmp = bmesh.new()
    K._loft(tmp, [circle(r, segs, z + dz) for r, dz in prof], [0], closed=True)
    K._put(b, tmp, M, m or COPPER)


def swag(b, a, c, n, r, sag=0.08, m=None):
    # A chain from a to c (sagging by `sag`): n oval links, long along the chain and overlapping, each turned 90 degrees.
    a, c = Vector(a), Vector(c)
    for k in range(n):
        t = (k + 0.5) / n
        p = a.lerp(c, t) - Vector((0, 0, sag * 4 * t * (1 - t)))
        d = (c - a) - Vector((0, 0, sag * 4 * (1 - 2 * t)))
        F, _ = toward(p - d.normalized() * 0.01, p + d.normalized() * 0.01)
        ring(b, r, r * 0.55, r * 0.35, (0, 0, 0), m or IRON, segs=6, M=F @ Rz(k * math.pi / 2) @ Rx(math.pi / 2) @ S(1, 1.6, 1))


def column(b, z0, z1, r=0.085):
    cyl(b, r, z1 - z0, (0, 0, (z0 + z1) / 2), IRON, segs=8, ch=0.0)


def collar(b, z, r=0.12, h=0.05, m=None):
    cyl(b, r, h, (0, 0, z), m or IRON, segs=8, ch=0.012)


def crystal(b, r, loc, m=None):
    ball(b, r, loc, m or CRYSTAL, subdiv=1)


def prong(b, pts, w=0.05, m=None):
    # A bent square arm through the points (each joint a small cube so the bends read solid).
    for a, c in zip(pts, pts[1:]):
        bar(b, a, c, w, w, m or IRON, ch=0.008)
    for p in pts[1:-1]:
        box(b, (w * 1.15, w * 1.15, w * 1.15), p, m or IRON, ch=0.01)


def claw(b, hub_z, cz, cr, spread=0.17, rot=0.0):
    # The sheet's iron claw: a hub, a cup under the crystal, two prongs curling up round it, their tips on its sides.
    collar(b, hub_z, r=0.1, h=0.1)
    cyl(b, 0.065, cz - cr * 0.7 - (hub_z + 0.05), (0, 0, (hub_z + 0.05 + cz - cr * 0.7) / 2), IRON, segs=8, ch=0.0)
    for s in (-1, 1):
        c, sn = math.cos(rot), math.sin(rot)

        def P(x, z):
            return (x * c * s, x * sn * s, z)
        prong(b, [P(0.07, hub_z + 0.03), P(spread, hub_z + 0.1), P(spread + 0.02, cz - 0.02), P(cr * 0.62, cz + cr * 0.72)])
    crystal(b, cr, (0, 0, cz))


def tip_crystal(b, base, direction, length=0.12, r=0.045):
    # A long cut crystal on the end of an arm, pointing along `direction`.
    F, _ = toward(base, (base[0] + direction[0], base[1] + direction[1], base[2] + direction[2]))
    box(b, (0.07, 0.07, 0.05), (0, 0, 0.0), IRON, ch=0.01, M=F)
    gem(b, r, length, r * 0.6, (0, 0, 0.025 + r * 0.6), CRYSTAL, segs=6, M=F, mid=(0.85, length * 0.25))


def fork(b, hub_z, n=2, reach=0.27, rise=0.18, phase=0.0, centre=True, centre_len=0.2):
    # The discharge fork (sheets 6 / 9): a hub, n arms out and up with a crystal each, a tall crystal in the middle.
    collar(b, hub_z, r=0.11, h=0.1)
    cyl(b, 0.075, 0.08, (0, 0, hub_z + 0.09), IRON, segs=8, ch=0.012)
    for k in range(n):
        a = phase + k * TAU / n
        c, s = math.cos(a), math.sin(a)
        p0 = (c * 0.08, s * 0.08, hub_z)
        p1 = (c * reach * 0.62, s * reach * 0.62, hub_z + 0.02)
        p2 = (c * reach, s * reach, hub_z + rise)
        prong(b, [p0, p1, p2], w=0.055)
        tip_crystal(b, (c * reach, s * reach, hub_z + rise + 0.02), (c * 0.25, s * 0.25, 1.0))
    if centre:
        box(b, (0.09, 0.09, 0.05), (0, 0, hub_z + 0.155), IRON, ch=0.01)
        gem(b, 0.06, centre_len, 0.035, (0, 0, hub_z + 0.215), CRYSTAL, segs=6, mid=(0.85, centre_len * 0.25))


def cone_coil(b, z0, z1, r0=0.25, r1=0.15, n=4):
    # Sheet 9's tapering coil: iron discs and copper bands stacked, wide at the bottom.
    step = (z1 - z0) / n
    for k in range(n):
        za, zb = z0 + k * step, z0 + (k + 1) * step
        ra = r0 + (r1 - r0) * k / n
        rb = r0 + (r1 - r0) * (k + 1) / n
        rtier(b, ra, za, za + step * 0.28, IRON, segs=10, ch=0.012, r_top=ra - 0.005)
        rtier(b, ra - 0.015, za + step * 0.28, zb, COPPER, segs=10, ch=0.018, r_top=rb - 0.01)


def solenoid(b, z0, z1, r=0.17, n=9):
    # Sheet 5's tall coil: an iron core wound with tight copper turns between two iron flanges.
    column(b, z0, z1, r=r * 0.62)
    coil_rings(b, r, z0 + 0.04, z1 - 0.04, n, COPPER, segs=10, thick=(z1 - z0 - 0.08) / n * 0.82)
    for z in (z0 + 0.02, z1 - 0.02):
        cyl(b, r + 0.03, 0.05, (0, 0, z), IRON, segs=10, ch=0.012)


def pedestal(b):
    # The iron foot the B coils stand on: narrow enough to clear the pods, their rings below its top.
    rtier(b, 0.145, 0.15, 0.3, IRON, segs=8, ch=0.015, r_top=0.12)


# ---------------------------------------------------------------- plinth (static)
def base(b):
    tier(b, FOOT, FOOT, 0.12, 0.0, 0.08, IRON, ch=0.02, chb=0.01)                  # the iron foot band
    tier(b, BLOCK, BLOCK, 0.2, 0.08, 0.34, STONE2, ch=0.03, taper=0.02)           # the stone block (deep corner cuts)
    for u, v in CORNERS4:                                                          # corner blocks in the cuts (sheet 1)
        tier(b, 0.12, 0.12, 0.03, 0.08, 0.29, STONE, ch=0.02, taper=0.05, M=T(u * FC, v * FC, 0) @ Rz(math.pi / 4))
        stud(b, (u * FC, v * FC, 0.29), r=0.032, m=IRON, segs=6, h=0.03)
    tier(b, UPPER, UPPER, 0.1, 0.34, 0.46, STONE, ch=0.025, taper=0.015)          # upper step
    tier(b, CAP, CAP, 0.09, 0.46, H, STONE2, ch=0.015)                             # the top


# ---------------------------------------------------------------- turntable (turns)
def body(b):
    tier(b, 0.26, 0.26, 0.075, 0.0, TOP, IRON, ch=0.014)
    tier(b, 0.266, 0.266, 0.077, 0.012, 0.028, BRASS, ch=0.004)                      # a brass band round its rim
    for k in range(8):
        a = (k + 0.5) * TAU / 8
        stud(b, (math.cos(a) * 0.225, math.sin(a) * 0.225, TOP), r=0.02)
    cyl(b, 0.14, 0.12, (0, 0, TOP + 0.06), IRON, segs=8, ch=0.02)                  # the column's socket
    cyl(b, 0.148, 0.03, (0, 0, TOP + 0.085), BRASS, segs=8, ch=0.006)


# ---------------------------------------------------------------- heads (turn)
def default(b):
    column(b, 0.15, 0.85)
    collar(b, 0.23)
    for z in (0.355, 0.52, 0.685):
        cring(b, 0.235, 0.09, 0.12, z)
    collar(b, 0.78)
    claw(b, 0.85, CRYSTAL_Z, 0.165, spread=0.2)


def spread(b):
    column(b, 0.15, 0.8)
    collar(b, 0.23)
    cone_coil(b, 0.33, 0.71, 0.25, 0.15, n=4)
    collar(b, 0.74, r=0.14, h=0.06)
    fork(b, 0.81, n=2, reach=0.27, rise=0.17, phase=0.0, centre_len=0.24)


def insulators(b, z0, n=3, r=0.12):
    # Glass insulator discs stacked on the column (the design's "three glass insulators").
    for k in range(n):
        z = z0 + k * 0.075
        rtier(b, r, z, z + 0.055, CRYSTAL, segs=10, ch=0.015, r_top=r * 0.72)


def storm_chain(b):
    column(b, 0.15, 0.95)
    collar(b, 0.23)
    cone_coil(b, 0.33, 0.67, 0.25, 0.16, n=4)
    collar(b, 0.7, r=0.14, h=0.06)
    insulators(b, 0.73)
    fork(b, 0.96, n=4, reach=0.32, rise=0.2, phase=0.0, centre_len=0.28)   # four forks and the big electrode


def water_coils(b, z0, z1, n=4):
    step = (z1 - z0) / n
    for k in range(n):
        cring(b, 0.21, 0.09, step * 0.62, z0 + step * (k + 0.5), GLASS, segs=10)
        cyl(b, 0.115, step * 0.3, (0, 0, z0 + step * (k + 1)), IRON, segs=8, ch=0.008)


def conduction(b):
    column(b, 0.15, 0.88)
    collar(b, 0.23)
    water_coils(b, 0.31, 0.77)
    collar(b, 0.8, r=0.13)
    # two conduit pipes down the column's front corners, brass joints, into the socket (clear of the pods at the sides)
    for s in (-1, 1):
        x, y = s * 0.17, -0.17
        rod(b, (x * 0.5, y * 0.5, 0.8), (x, y, 0.78), 0.028, IRON, segs=6)
        rod(b, (x, y, 0.78), (x, y, 0.2), 0.028, IRON, segs=6)
        rod(b, (x, y, 0.2), (x * 0.5, y * 0.5, 0.15), 0.028, IRON, segs=6)
        for z in (0.78, 0.49, 0.2):
            cyl(b, 0.04, 0.045, (x, y, z), BRASS, segs=6, ch=0.008)
    # the grounding chain: off an arm at the back, hanging down to a plate on the turntable
    prong(b, [(0.0, 0.1, 0.8), (0.0, 0.255, 0.81)], w=0.045)
    swag(b, (0.0, 0.255, 0.8), (0.0, 0.255, TOP + 0.04), 7, 0.04, sag=0.0)
    box(b, (0.07, 0.07, 0.03), (0.0, 0.225, TOP + 0.015), BRASS, ch=0.006)
    fork(b, 0.88, n=2, reach=0.24, rise=0.15, phase=math.pi / 2, centre_len=0.22)


def overload(b):
    pedestal(b)
    solenoid(b, 0.3, 0.84, r=0.17, n=8)
    column(b, 0.84, 0.96, r=0.07)
    collar(b, 0.94, r=0.1, h=0.05)
    # the flat crystal disc on top (sheets 3 / 5)
    tmp = bmesh.new()
    K._loft(tmp, [circle(0.16, 12, 0.97), circle(0.3, 12, 1.02), circle(0.3, 12, 1.07), circle(0.2, 12, 1.12)], [0])
    K._put(b, tmp, I4, CRYSTAL)
    cyl(b, 0.07, 0.04, (0, 0, 1.13), IRON, segs=8, ch=0.01)


def overcharge(b):
    overload(b)
    # the transformer at the back: an iron box, copper windings, the red overload gauge on its back face
    y = 0.19
    box(b, (0.26, 0.17, 0.26), (0, y, TOP + 0.13), IRON, ch=0.025)
    for x in (-0.07, 0.07):
        cyl(b, 0.055, 0.18, (x, y, TOP + 0.13), COPPER, segs=8, axis="Y", ch=0.01)
    box(b, (0.2, 0.04, 0.05), (0, y + 0.06, TOP + 0.26), IRON, ch=0.008)
    cyl(b, 0.075, 0.02, (0, y + 0.09, TOP + 0.12), WHITE, segs=10, axis="Y", ch=0.004)    # gauge face
    ring(b, 0.085, 0.07, 0.03, (0, y + 0.09, TOP + 0.12), BRASS, segs=10, axis="Y")
    bar(b, (0, y + 0.105, TOP + 0.12), (0.045, y + 0.105, TOP + 0.165), 0.014, 0.008, RED)   # the needle in the red
    # two capacitor jars on brackets off the coil, above the pods (sheet 2)
    for s in (-1, 1):
        x = s * 0.31
        bar(b, (s * 0.15, 0, 0.7), (x, 0, 0.7), 0.05, 0.05, IRON)
        bar(b, (s * 0.15, 0, 0.5), (x, 0, 0.5), 0.05, 0.05, IRON)
        cyl(b, 0.1, 0.28, (x, 0, 0.6), CRYSTAL, segs=8, ch=0.025)
        for z in (0.445, 0.755):
            cyl(b, 0.085, 0.05, (x, 0, z), IRON, segs=8, ch=0.012)
        cyl(b, 0.04, 0.05, (x, 0, 0.8), COPPER, segs=6, ch=0.008)
        rod(b, (x, 0, 0.82), (s * 0.065, 0, 0.9), 0.016, COPPER, segs=5)                    # lead to the coil's top


def arc_blast(b):
    pedestal(b)
    solenoid(b, 0.3, 0.72, r=0.17, n=6)
    column(b, 0.72, 0.9, r=0.07)
    # six radial electrodes off a ring
    cyl(b, 0.13, 0.07, (0, 0, 0.8), IRON, segs=12, ch=0.015)
    for k in range(6):
        a = k * TAU / 6
        c, s = math.cos(a), math.sin(a)
        rod(b, (c * 0.1, s * 0.1, 0.8), (c * 0.3, s * 0.3, 0.8), 0.022, IRON, segs=6)
        cyl(b, 0.035, 0.04, (c * 0.22, s * 0.22, 0.8), BRASS, segs=6, rot=(0, math.pi / 2, a), ch=0.006)
        tip_crystal(b, (c * 0.3, s * 0.3, 0.8), (c, s, 0.18), length=0.1, r=0.035)
    # the spherical arc cage round a bright core
    cz = 1.04
    cyl(b, 0.08, 0.1, (0, 0, 0.87), IRON, segs=8, ch=0.012)
    ball(b, 0.11, (0, 0, cz), CYAN, subdiv=1)
    for rot in ((0, 0, 0), (math.pi / 2, 0, 0), (math.pi / 2, 0, math.pi / 2)):
        ring(b, 0.21, 0.18, 0.035, (0, 0, 0), BRASS, segs=14, M=T(0, 0, cz) @ R(rot))
    for k in range(4):  # posts from the cage to the core
        a = k * TAU / 4 + math.pi / 4
        rod(b, (math.cos(a) * 0.19, math.sin(a) * 0.19, cz), (math.cos(a) * 0.1, math.sin(a) * 0.1, cz), 0.014, IRON, segs=4)
    rod(b, (0, 0, 0.92), (0, 0, cz - 0.1), 0.03, IRON, segs=6)
    gem(b, 0.045, 0.12, 0.02, (0, 0, cz + 0.21), CRYSTAL, segs=6)


# ---------------------------------------------------------------- extra coil pylon (turns, on the turntable)
def pod(b):
    # bottom centre at the origin: an iron foot, a short rod with two copper rings, a crystal tip. Below the column's rings.
    box(b, (0.11, 0.11, 0.045), (0, 0, 0.0225), IRON, ch=0.012)
    stud(b, (0, -0.055, 0.025), (0, -1, 0), r=0.014, segs=4)
    column(b, 0.04, 0.2, r=0.024)
    for z in (0.09, 0.15):
        ring(b, 0.058, 0.024, 0.04, (0, 0, z), COPPER, segs=8)
    cyl(b, 0.035, 0.03, (0, 0, 0.2), IRON, segs=6, ch=0.006)
    gem(b, 0.032, 0.06, 0.015, (0, 0, 0.23), CRYSTAL, segs=6)


# ---------------------------------------------------------------- one piece a level (static, round the plinth)
def kit2(b):   # Lv2: iron caps on the corner blocks (bolted on their outer faces), an iron band round the block's top
    for u, v in CORNERS4:
        F = T(u * FC, v * FC, 0) @ Rz(math.pi / 4)
        tier(b, 0.1, 0.1, 0.026, 0.23, 0.275, IRON, ch=0.01, M=F)
        tier(b, 0.128, 0.128, 0.035, 0.08, 0.115, IRON, ch=0.01, M=F)
        d = Vector((u, v, 0)).normalized()
        t = Vector((-v, u, 0)).normalized()
        for k in (-1, 1):
            p = Vector((u * FC, v * FC, 0.0975)) + d * 0.128 + t * k * 0.055
            stud(b, tuple(p), tuple(d), r=0.016, m=BRASS, segs=4)
    tier(b, BLOCK - 0.008, BLOCK - 0.008, 0.2, 0.3, 0.335, IRON, ch=0.01)


def kit3(b):   # Lv3: a brass band round the upper step, a rivet on each of its faces
    hw = UPPER + 0.004
    tier(b, hw, hw, 0.1, 0.385, 0.41, BRASS, ch=0.006)
    for k in range(8):
        a = k * TAU / 8
        d = hw if k % 2 == 0 else (2 * hw - 0.1) / math.sqrt(2)
        stud(b, (math.cos(a) * d, math.sin(a) * d, 0.3975), (math.cos(a), math.sin(a), 0), r=0.016)


def reel(b, M):
    # A wooden cable reel wound with copper wire, its axle along X, standing on the ground.
    r = 0.12
    for x in (-0.09, 0.09):
        cyl(b, r, 0.03, (x, 0, r), WOOD, segs=10, axis="X", ch=0.006, M=M)
    cyl(b, r * 0.72, 0.15, (0, 0, r), COPPER, segs=10, axis="X", ch=0.01, M=M)
    cyl(b, 0.025, 0.24, (0, 0, r), IRON, segs=6, axis="X", ch=0.0, M=M)
    rod(b, (0.0, -r * 0.7, r * 0.5), (0.0, -r * 1.05, 0.012), 0.012, COPPER, segs=4, M=M)   # the loose end


def kit4(b):   # Lv4: a reel of copper wire and a battery box against the back of the foot
    reel(b, T(-0.16, FOOT + 0.11, 0) @ Rz(math.pi))
    F = T(0.17, FOOT + 0.098, 0) @ Rz(-0.08)
    crate(b, (0.2, 0.2, 0.16), M=F, planks=1, lid=False)
    for x in (-0.05, 0.05):  # its terminals
        cyl(b, 0.022, 0.05, (x, 0, 0.18), COPPER, segs=6, ch=0.006, M=F)


def kit5(b):   # Lv5: vent plates on the stone block's faces (sheet 6: iron frames, copper louvres)
    lean = 0.02 / 0.26
    for k in range(4):
        a = k * TAU / 4
        o = Vector((math.cos(a), math.sin(a), 0))
        n = Vector((o.x, o.y, lean)).normalized()
        c = o * (BLOCK - 0.02 * 0.11 / 0.26 - 0.002) + Vector((0, 0, 0.19))
        plate(b, 0.3, 0.15, 0.025, tuple(c), tuple(n), IRON, rivets=CORNERS4, rv=0.015)
        for j in (-1, 0, 1):
            p = c + o * 0.03 + Vector((0, 0, j * 0.035))
            box(b, (0.2, 0.012, 0.018), tuple(p), COPPER, ch=0.0, rot=(0, 0, a + math.pi / 2))


def kit6(b):   # Lv6: a crate of spare crystals against the left of the foot
    x = -(FOOT + 0.1)
    crate(b, (0.2, 0.34, 0.17), M=T(x, 0.02, 0), planks=1)
    for y, h, tilt in ((-0.08, 0.14, 0.2), (0.04, 0.18, -0.1), (0.12, 0.12, 0.3)):
        gem(b, 0.04, h, 0.03, (0, 0, 0), CRYSTAL, segs=6, M=T(x, y, 0.17) @ Ry(tilt))


def kit7(b):   # Lv7: the pennant at the front right, its cloth facing the 3/4 view, clear of the coil's sweep
    px, py = 0.566, -0.269
    tier(b, 0.06, 0.06, 0.02, 0.0, 0.05, IRON, ch=0.01, M=T(px, py, 0))
    pennant(b, 1.0, CLOTH["Tesla"], M=T(px, py, 0.0) @ Rz(PEN))
    bar(b, (0.39, py, 0.2), (px, py, 0.2), 0.04, 0.04, IRON)          # brackets into the corner block and the upper step
    bar(b, (0.33, py, 0.42), (px, py, 0.42), 0.04, 0.04, IRON)


def kit8(b):   # Lv8: the brass crest with a lightning bolt on the upper step's front, brass knobs on the corner blocks
    y = -(UPPER - 0.0075)
    plate(b, 0.22, 0.1, 0.02, (0, y, 0.4), (0, -1, 0), BRASS, rivets=CORNERS4, rv=0.011, rivet_m=IRON)
    bolt = [(-0.026, 0.042), (0.017, 0.01), (-0.007, 0.007), (0.026, -0.042), (-0.017, -0.01), (0.007, -0.007)]
    tmp = bmesh.new()
    K._loft(tmp, [[(x, y - 0.015, 0.4 + z) for x, z in bolt], [(x, y - 0.036, 0.4 + z) for x, z in bolt]], [0])
    K._put(b, tmp, I4, IRON)
    for u, v in CORNERS4:
        ball(b, 0.03, (u * FC, v * FC, 0.34), BRASS)


# ---------------------------------------------------------------- final classes (static, from Lv9; Kit4 + Kit6 make way)
def mast(b, x, y, h=0.86, discs=3):
    # A storm collector: an iron foot, an iron mast with glass insulator discs, a crystal on top.
    tier(b, 0.09, 0.09, 0.025, 0.0, 0.08, IRON, ch=0.015, M=T(x, y, 0))
    cyl(b, 0.035, h - 0.08, (x, y, (h + 0.08) / 2), IRON, segs=6, ch=0.0)
    for k in range(discs):
        z = 0.25 + k * 0.12
        rtier(b, 0.08, z, z + 0.045, CRYSTAL, segs=8, ch=0.012, r_top=0.055, M=T(x, y, 0))
    cyl(b, 0.05, 0.04, (x, y, h), IRON, segs=6, ch=0.008)
    gem(b, 0.045, 0.13, 0.02, (x, y, h + 0.02), CRYSTAL, segs=6, mid=(0.85, 0.03))


def dress_a1(b):   # Storm chain: two collector masts against the foot (back, left), each with a cross-arm of small collectors
    d = FOOT + 0.1
    for x, y, ax in ((-d, 0.0, (0, 1, 0)), (0.0, d, (1, 0, 0))):
        mast(b, x, y, h=0.92)
        a = Vector(ax)
        bar(b, tuple(Vector((x, y, 0.74)) - a * 0.17), tuple(Vector((x, y, 0.74)) + a * 0.17), 0.04, 0.04, IRON)
        for k in (-1, 1):
            p = Vector((x, y, 0.76)) + a * 0.15 * k
            gem(b, 0.03, 0.09, 0.015, tuple(p), CRYSTAL, segs=6)


def dress_a2(b):   # Conduction: a water tub at the back with the grounding rod in it, conduits out of the ground on the left
    ty = FOOT + 0.13
    rtier(b, 0.12, 0.0, 0.18, WOOD, segs=10, ch=0.012, r_top=0.13, M=T(0.0, ty, 0))
    for z in (0.045, 0.14):
        rtier(b, 0.13, z - 0.014, z + 0.014, IRON, segs=10, ch=0.0, M=T(0.0, ty, 0))
    cyl(b, 0.115, 0.02, (0.0, ty, 0.17), GLASS, segs=10, ch=0.0)                      # the water
    top = (0.12, FOOT + 0.06, 0.6)
    rod(b, (0.03, ty - 0.01, 0.08), top, 0.022, COPPER, segs=6)                         # grounding rod
    ball(b, 0.03, (top[0], top[1], top[2] + 0.02), BRASS)
    swag(b, (top[0], top[1], top[2] - 0.02), (FC, FC, 0.32), 6, 0.026, sag=0.04)      # to the corner block's bolt
    for y in (-0.18, 0.12):  # conduits: out of the ground, bend, into the stone block
        x = -(FOOT + 0.08)
        rod(b, (x, y, 0.0), (x, y, 0.22), 0.04, IRON, segs=6)
        rod(b, (x, y, 0.22), (-(BLOCK - 0.03), y, 0.22), 0.04, IRON, segs=6)
        cyl(b, 0.055, 0.05, (x, y, 0.22), BRASS, segs=6, ch=0.008)
        cyl(b, 0.05, 0.04, (x, y, 0.02), IRON, segs=6, ch=0.006)
        rod(b, (x, y, 0.24), (x, y, 0.3), 0.012, IRON, segs=4)                           # valve stem
        box(b, (0.03, 0.1, 0.03), (x, y, 0.3), RED, ch=0.004)                          # valve handle


def coil_drum(b, x, y, r=0.13, h=0.42):
    # Sheet 8's copper coil drum with a crystal cap.
    tier(b, r + 0.03, r + 0.03, 0.04, 0.0, 0.06, IRON, ch=0.015, M=T(x, y, 0))
    cyl(b, r * 0.7, h, (x, y, 0.06 + h / 2), IRON, segs=10, ch=0.0)
    coil_rings(b, r, 0.09, h + 0.02, 6, COPPER, segs=10, M=T(x, y, 0))
    cyl(b, r + 0.02, 0.05, (x, y, h + 0.06), IRON, segs=10, ch=0.012)
    rtier(b, r * 0.8, h + 0.085, h + 0.17, CRYSTAL, segs=10, ch=0.03, r_top=r * 0.5, M=T(x, y, 0))


def dress_b1(b):   # Overcharge: two coil drums against the foot (back, left), copper cables into the stone block
    d = FOOT + 0.16
    coil_drum(b, 0.0, d)
    coil_drum(b, -d, 0.0)
    rod(b, (0.0, d - 0.12, 0.3), (0.0, BLOCK - 0.03, 0.22), 0.022, COPPER, segs=5)
    rod(b, (-(d - 0.12), 0.0, 0.3), (-(BLOCK - 0.03), 0.0, 0.22), 0.022, COPPER, segs=5)


def dress_b2(b):   # Arc blast: crystals burst out of rubble on the left, an arc ring standing at the back
    box(b, (0.16, 0.42, 0.06), (-(FOOT + 0.07), 0.0, 0.03), STONE2, ch=0.02)               # the rubble they burst from
    for x, y, s, tilt in ((-(FOOT + 0.08), 0.0, 1.0, -0.15), (-(FOOT + 0.06), 0.14, 0.7, 0.4), (-(FOOT + 0.07), -0.15, 0.75, -0.35),
                          (0.31, FOOT + 0.05, 0.6, 0.3), (-0.31, FOOT + 0.05, 0.55, -0.3)):
        gem(b, 0.07 * s, 0.32 * s, 0.03, (0, 0, 0), CRYSTAL, segs=6, M=T(x, y, 0.0) @ R((0, tilt, 0) if x < -0.5 else (tilt, 0, 0)))
    x, y, z = 0.0, FOOT + 0.06, 0.27
    box(b, (0.5, 0.12, 0.05), (x, y, 0.025), IRON, ch=0.012)
    for k in (-1, 1):  # two posts hold the ring upright
        box(b, (0.05, 0.06, 0.16), (x + k * 0.205, y, 0.1), IRON, ch=0.008)
    ring(b, 0.23, 0.18, 0.05, (0, 0, 0), IRON, segs=12, M=T(x, y, z) @ Rx(math.pi / 2))
    ball(b, 0.055, (x, y, z), CYAN)
    for k in (-1, 1):
        rod(b, (x + k * 0.185, y, z), (x + k * 0.045, y, z), 0.014, IRON, segs=4)
        stud(b, (x + k * 0.205, y - 0.025, z), (0, -1, 0), r=0.018, segs=4)


model("TeslaBase", base)
model("TeslaBody", body, scale=SC)
model("TeslaDefault", default, scale=SC)
model("TeslaA", spread, scale=SC)
model("TeslaA1", storm_chain, scale=SC)
model("TeslaA2", conduction, scale=SC)
model("TeslaB", overload, scale=SC)
model("TeslaB1", overcharge, scale=SC)
model("TeslaB2", arc_blast, scale=SC)
model("TeslaBarrel", pod, scale=SC)
for n, f in ((2, kit2), (3, kit3), (4, kit4), (5, kit5), (6, kit6), (7, kit7), (8, kit8)):
    model("TeslaKit%d" % n, f)
for n, f in (("A1", dress_a1), ("A2", dress_a2), ("B1", dress_b1), ("B2", dress_b2)):
    model("TeslaDress" + n, f)
export_all()
layout("Tesla", head=H, muzzle=round(H + CRYSTAL_Z * SC, 3), pod=[round(v * SC, 3) for v in POD],
       step=[round(v * SC, 3) for v in STEP])
