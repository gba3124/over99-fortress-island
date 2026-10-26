# Mender (repair tower), art pass 5 (tools/t5kit.py; Owen's mender sheet /tmp/t5/full/11.png). Plinth: the cream tower of
# cells 1 / 8 (a stone foot, a block, a dark band, a leaning upper course, an iron collar) with a green cross on every face.
# On it a stone torso turns to face whom it mends: an iron cap with the green button (a glowing cross, where the mending
# sparks leave: the layout's muzzle), jointed iron arms with brass knuckles and green guards holding a wrench and a
# green-glow injector. Classes (TURRET-UPGRADE-DESIGN.md §9): A Wide (a crossbar over the cap, an arm hanging from each
# end) -> A1 Grand mender (a portal frame on a tool tray round the waist, two welding arms) / A2 Core mender (a big green
# core on the chest, a healing lens held out in front); B Surge (one big crane arm with a heavy injector, a green tank) ->
# B1 Overheal (the body a pressure tank: three injector tubes, an overflow valve) / B2 Cleanse (white filter cans, a
# cleansing halo, two spray arms).
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/t5_mender.py   [ONLY=MenderBase,MenderA ...]
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from t5kit import *  # noqa: F401,F403

H = 0.76          # head pivot: the top of the iron collar
SC = 1.28         # the head parts' scale (drawn at 1, exported bigger: the head reads big on its tower)
TOP = 0.14        # the turntable column's top: the torso stands on it
CAPZ = 0.47       # the torso's cap; the green button on it is the muzzle, 0.5 * SC up (Turrets: the mending sparks)
SZ = 0.31         # shoulder height
FOOT, LOW, BAND, UP, COL = 0.46, 0.40, 0.39, 0.355, 0.31   # half widths: foot, block, iron band, upper course, collar
ZF, ZB, ZD = 0.11, 0.28, 0.325                              # tops of the foot, the block and the band
UZ, UT = (ZD, 0.70), 0.05                                   # the upper course's span and how far it leans in


CZ = (UZ[0] + UZ[1]) / 2 + 0.005   # the crosses' height


def face(k, z, hw=UP, z0=UZ[0], z1=UZ[1], lean=UT):
    # A point on face k (0 front, 1 right, 2 back, 3 left) of the leaning upper tower at height z, and the face's normal.
    t = math.atan2(lean, z1 - z0)
    a = -math.pi / 2 + k * math.pi / 2
    d = hw - (z - z0) * lean / (z1 - z0)
    return (math.cos(a) * d, math.sin(a) * d, z), (math.cos(a) * math.cos(t), math.sin(a) * math.cos(t), math.sin(t))


def wall(z):
    # The plinth's half width at height z (where a strap or a cable meets it).
    if z < ZF:
        return FOOT
    if z < ZB:
        return LOW - 0.01 * (z - ZF) / (ZB - ZF)
    if z < ZD:
        return BAND
    return UP - UT * (z - UZ[0]) / (UZ[1] - UZ[0]) if z < UZ[1] else COL


def frame(p, d, xref=(1, 0, 0)):
    # The frame at p whose +Z runs along d and +X as near xref as it can (a tool in a hand, a ring on a face).
    z = Vector(d).normalized()
    x = Vector(xref) - z * Vector(xref).dot(z)
    if x.length < 1e-4:
        x = Vector((0, 0, 1)).cross(z)
    x.normalize()
    F = Matrix((x, z.cross(x), z)).transposed().to_4x4()
    F.translation = Vector(p)
    return F


def cross(b, p, n, arm=0.26, w=0.085, t=0.025, m=None):
    # The green repair cross lying on a surface (p on it, facing n).
    plate(b, arm, w, t, p, n, m or GREEN, ch=0.007)
    plate(b, w, arm, t, p, n, m or GREEN, ch=0.007)


# ---------------------------------------------------------------- plinth (static)
def base(b):
    tier(b, FOOT, FOOT, 0.14, 0.0, ZF, STONE2, ch=0.03, chb=0.015)              # foot
    tier(b, LOW, LOW, 0.12, ZF, ZB, STONE, ch=0.02, taper=0.01)                  # block
    tier(b, BAND, BAND, 0.115, ZB, ZD, IRON, ch=0.012, chb=0.008)                # the dark band (cell 8)
    tier(b, UP, UP, 0.1, UZ[0], UZ[1], STONE, ch=0.022, taper=UT)                # upper course
    tier(b, COL, COL, 0.09, UZ[1], H, IRON, ch=0.015, chb=0.01)                  # iron collar
    for k in range(4):
        p, n = face(k, CZ)
        cross(b, p, n, arm=0.25, w=0.085)                                        # a green cross on every face
        a = -math.pi / 2 + k * math.pi / 2
        c, s_ = math.cos(a), math.sin(a)
        for s in (-1, 1):
            stud(b, (c * (BAND + 0.001) - s_ * s * 0.2, s_ * (BAND + 0.001) + c * s * 0.2, (ZB + ZD) / 2), (c, s_, 0), r=0.017)


# ---------------------------------------------------------------- turntable (turns)
def body(b):
    tier(b, 0.24, 0.24, 0.07, 0.0, 0.05, IRON, ch=0.015)
    for k in range(8):
        a = (k + 0.5) * TAU / 8
        stud(b, (math.cos(a) * 0.2, math.sin(a) * 0.2, 0.05), r=0.015, segs=4)
    cyl(b, 0.15, 0.1, (0, 0, 0.09), IRON, segs=8, ch=0.01)                      # the column the torso stands on
    ring(b, 0.165, 0.13, 0.025, (0, 0, 0.0675), BRASS, segs=8)


# ---------------------------------------------------------------- torso, arms and tools (turn)
def emitter(b, z=CAPZ):
    # The green button (cells 1 / 6 / 8) with a glowing cross on it.
    cyl(b, 0.12, 0.04, (0, 0, z + 0.02), GREEN, segs=8, ch=0.012)
    for rz in (0.0, math.pi / 2):
        box(b, (0.17, 0.055, 0.03), (0, 0, z + 0.05), GLOWG, ch=0.006, rot=(0, 0, rz))


def torso(b, hw=0.27, front=True, button=True):
    # The stone torso (cell 1): a dark band low down, an iron cap with brass studs, the green cross on its chest.
    tier(b, hw, hw, 0.08, TOP, 0.4, STONE, ch=0.02, chb=0.012)
    tier(b, hw + 0.012, hw + 0.012, 0.085, 0.17, 0.22, IRON, ch=0.008, chb=0.006)
    tier(b, hw + 0.02, hw + 0.02, 0.09, 0.4, CAPZ, IRON, ch=0.018, chb=0.01)
    for k in range(4):
        a = -math.pi / 2 + k * math.pi / 2
        c, s_ = math.cos(a), math.sin(a)
        for s in (-1, 1):
            stud(b, (c * (hw + 0.021) - s_ * s * 0.15, s_ * (hw + 0.021) + c * s * 0.15, 0.435), (c, s_, 0), r=0.016)
    if front:
        cross(b, (0, -hw, 0.305), (0, -1, 0), arm=0.17, w=0.06, t=0.02)
    if button:
        emitter(b)


def shoulder(b, s, hw=0.27, z=SZ):
    # An iron hub on side s of the torso with a brass cap: the arm starts at its outer face.
    cyl(b, 0.088, 0.08, (s * (hw + 0.04), 0, z), IRON, segs=8, axis="X", ch=0.014)
    cyl(b, 0.052, 0.03, (s * (hw + 0.085), 0, z), BRASS, segs=8, axis="X", ch=0.006)
    return (s * (hw + 0.1), 0, z)


def knuckle(b, p, ax, r=0.07, w=0.14):
    F = frame(p, ax)
    cyl(b, r, w, (0, 0, 0), IRON, segs=8, ch=0.01, M=F)
    cyl(b, r * 0.55, w + 0.03, (0, 0, 0), BRASS, segs=8, ch=0.0, M=F)


def arm(b, pts, d, w=0.1, guards=(), r=0.07, xref=(1, 0, 0)):
    # A jointed iron arm through pts (cell 1): square segments, a brass-pinned knuckle at each joint (the pin square to the
    # bend), green guards on the segments in `guards`. Returns the frame at the wrist for its tool, +Z along d.
    P = [Vector(p) for p in pts]
    D = [P[i + 1] - P[i] for i in range(len(P) - 1)] + [Vector(d)]
    for i in range(len(P) - 1):
        bar(b, P[i], P[i + 1], w, w, IRON, ch=0.012)
        if i in guards:
            bar(b, P[i].lerp(P[i + 1], 0.3), P[i].lerp(P[i + 1], 0.78), w + 0.035, w + 0.035, GREEN, ch=0.014)
    for i in range(1, len(P)):
        ax = D[i - 1].cross(D[i])
        knuckle(b, P[i], ax if ax.length > 1e-3 else Vector((0, 1, 0)), r=r, w=w + 0.04)
    return frame(P[-1], d, xref)


def wrench(b, F, k=1.0, m=None):
    # The pale open-end wrench (cells 1 / 2 / 4): a flat handle along +Z, a round head, the jaws curling in.
    m = m or WHITE
    box(b, (0.07 * k, 0.04 * k, 0.22 * k), (0, 0, 0.11 * k), m, ch=0.012 * k, M=F)
    cyl(b, 0.095 * k, 0.05 * k, (0, 0, 0.27 * k), m, segs=8, axis="Y", ch=0.012 * k, M=F)
    for s in (-1, 1):
        box(b, (0.068 * k, 0.05 * k, 0.13 * k), (s * 0.068 * k, 0, 0.395 * k), m, ch=0.018 * k, rot=(0, -s * 0.18, 0), M=F)


def injector(b, F, k=1.0):
    # The green-glow injector (cell 1): an iron body with glowing windows, a brass collar and nozzle.
    cyl(b, 0.058 * k, 0.21 * k, (0, 0, 0.105 * k), IRON, segs=8, ch=0.012 * k, M=F)
    for rz in (0.0, math.pi / 2):
        box(b, (0.124 * k, 0.03 * k, 0.1 * k), (0, 0, 0.11 * k), GLOWG, ch=0.0, rot=(0, 0, rz), M=F)
    cyl(b, 0.066 * k, 0.03 * k, (0, 0, 0.215 * k), BRASS, segs=8, ch=0.006 * k, M=F)
    cyl(b, 0.034 * k, 0.13 * k, (0, 0, 0.295 * k), BRASS, segs=8, r2=0.006 * k, ch=0.0, M=F)


def welder(b, F):
    # A welding torch: an iron body in brass rings, a bent brass nozzle with an amber flame.
    cyl(b, 0.05, 0.17, (0, 0, 0.085), IRON, segs=8, ch=0.01, M=F)
    for z in (0.04, 0.15):
        cyl(b, 0.056, 0.025, (0, 0, z), BRASS, segs=8, ch=0.0, M=F)
    rod(b, (0, 0, 0.17), (0, 0, 0.25), 0.024, BRASS, segs=6, M=F)
    rod(b, (0, 0, 0.25), (0, -0.05, 0.31), 0.017, BRASS, segs=6, M=F)
    gem(b, 0.03, 0.09, 0.012, (0, 0, 0), AMBER, segs=5, M=F @ T(0, -0.056, 0.317) @ Rx(0.7))


def sprayer(b, F):
    # The Cleanse's spray head: an iron body, a brass bell, the cleansing glow in its mouth.
    cyl(b, 0.048, 0.13, (0, 0, 0.065), IRON, segs=8, ch=0.01, M=F)
    cyl(b, 0.054, 0.025, (0, 0, 0.13), BRASS, segs=8, ch=0.0, M=F)
    cyl(b, 0.032, 0.12, (0, 0, 0.2), BRASS, segs=8, r2=0.08, ch=0.0, M=F)
    cyl(b, 0.072, 0.012, (0, 0, 0.254), GLOWG, segs=8, ch=0.0, M=F)


def tank(b, p, r=0.1, h=0.3, m=None, bands=2, segs=10):
    # A banded cylinder standing at p (its centre) with an iron neck.
    cyl(b, r, h, p, m or GREEN, segs=segs, ch=0.02)
    for k in range(bands):
        cyl(b, r + 0.01, 0.03, (p[0], p[1], p[2] - h / 2 + h * (k + 1) / (bands + 1)), IRON, segs=segs, ch=0.0)
    cyl(b, r * 0.5, 0.05, (p[0], p[1], p[2] + h / 2 + 0.02), IRON, segs=8, ch=0.008)


# ---------------------------------------------------------------- class heads (turn)
def default(b):
    torso(b)
    F = arm(b, [shoulder(b, -1), (-0.58, 0.03, 0.47), (-0.52, -0.02, 0.68)], (0.2, -0.08, 1), guards=(1,))
    wrench(b, F, 1.2)
    F = arm(b, [shoulder(b, 1), (0.6, -0.02, 0.44), (0.58, -0.25, 0.58)], (-0.2, -1, 0.3), guards=(1,))
    injector(b, F, 1.25)


def gantry(b, z=0.74, half=0.54, post=0.2):
    # The Wide class's crossbar (cells 3 / 7): two posts off the cap, a beam with green ends and a little green cross.
    for s in (-1, 1):
        box(b, (0.08, 0.1, z - CAPZ + 0.02), (s * post, 0, (z + CAPZ - 0.02) / 2), IRON, ch=0.012)
    box(b, (2 * half, 0.12, 0.1), (0, 0, z + 0.05), IRON, ch=0.02)
    for s in (-1, 1):
        box(b, (0.13, 0.15, 0.13), (s * half, 0, z + 0.05), GREEN, ch=0.018)
        for x in (0.3, 0.4):
            stud(b, (s * x, -0.06, z + 0.05), (0, -1, 0), r=0.016)
    cross(b, (0, -0.06, z + 0.05), (0, -1, 0), arm=0.09, w=0.032, t=0.016)
    return z


def wide(b):
    torso(b)
    z = gantry(b)
    for s, tool in ((-1, wrench), (1, injector)):  # an arm hanging from each end of the beam
        F = arm(b, [(s * 0.54, 0, z - 0.01), (s * 0.58, -0.1, z - 0.24), (s * 0.5, -0.3, z - 0.33)], (-s * 0.15, -1, -0.3), guards=(1,))
        tool(b, F, 1.15)


def tray(b, z=0.2, r=0.42):
    # The tool tray round the waist: an iron trough on brass brackets with tools lying in it.
    ring(b, r, 0.3, 0.025, (0, 0, z), IRON, segs=12)
    ring(b, r + 0.005, r - 0.03, 0.07, (0, 0, z + 0.035), IRON, segs=12)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        c, s = math.cos(a), math.sin(a)
        bar(b, (c * 0.27, s * 0.27, z - 0.06), (c * (r - 0.02), s * (r - 0.02), z - 0.012), 0.035, 0.03, BRASS, ch=0.0)
    a = -2.0  # a spanner lying in the front left
    wrench(b, frame((math.cos(a) * 0.35, math.sin(a) * 0.35, z + 0.03), (-math.sin(a), math.cos(a), 0), (math.cos(a), math.sin(a), 0)), k=0.5, m=IRON)
    a = -0.9  # a hammer in the front right
    F = frame((math.cos(a) * 0.35, math.sin(a) * 0.35, z + 0.03), (-math.sin(a), math.cos(a), 0), (math.cos(a), math.sin(a), 0))
    box(b, (0.022, 0.022, 0.17), (0, 0, 0.0), BRASS, ch=0.0, M=F)
    box(b, (0.05, 0.045, 0.09), (0, 0, 0.1), IRON, ch=0.0, rot=(0, math.pi / 2, 0), M=F)
    cyl(b, 0.045, 0.08, (math.cos(2.3) * 0.36, math.sin(2.3) * 0.36, z + 0.05), BRASS, segs=6, ch=0.0)  # an oil can


def portal(b, half=0.4, z=0.86):
    # The Grand mender's frame (cell 3): two legs standing on the tray's rim, a heavy beam with green ends and the cross.
    for s in (-1, 1):
        box(b, (0.085, 0.11, z - 0.25), (s * half, 0, (z + 0.25) / 2), IRON, ch=0.014)
        box(b, (0.12, 0.15, 0.06), (s * half, 0, 0.27), IRON, ch=0.012)
    box(b, (2 * half + 0.16, 0.13, 0.11), (0, 0, z + 0.055), IRON, ch=0.02)
    for s in (-1, 1):
        box(b, (0.15, 0.16, 0.14), (s * (half + 0.03), 0, z + 0.055), GREEN, ch=0.02)
        stud(b, (s * 0.2, -0.065, z + 0.055), (0, -1, 0), r=0.018)
    cross(b, (0, -0.065, z + 0.055), (0, -1, 0), arm=0.1, w=0.035, t=0.016)
    return z


def grand_mender(b):
    torso(b)
    tray(b)
    z = portal(b)
    for s in (-1, 1):  # two welding arms off the beam, reaching down in front
        F = arm(b, [(s * 0.24, 0, z - 0.01), (s * 0.3, -0.16, z - 0.18), (s * 0.24, -0.36, z - 0.3)], (-s * 0.35, -0.7, -0.6), guards=(0,))
        welder(b, F)


def core_mender(b):
    torso(b, front=False)
    cz = 0.3
    # the big green core on the chest (cell 4), in an iron housing with four clamps
    ring(b, 0.2, 0.15, 0.07, (0, -0.29, cz), IRON, segs=10, axis="Y")
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        c, s = math.cos(a), math.sin(a)
        bar(b, (c * 0.18, -0.3, cz + s * 0.18), (c * 0.11, -0.44, cz + s * 0.11), 0.045, 0.045, IRON)
        stud(b, (c * 0.2, -0.325, cz + s * 0.2), (c, 0, s), r=0.016)
    ball(b, 0.175, (0, -0.37, cz), GREEN, subdiv=1)
    # the healing lens held out in front by both arms
    lz, ly = 0.36, -0.8
    ring(b, 0.17, 0.135, 0.05, (0, ly, lz), BRASS, segs=12, axis="Y")
    cyl(b, 0.138, 0.014, (0, ly, lz), GLOWG, segs=12, axis="Y", ch=0.0)
    for s in (-1, 1):
        box(b, (0.05, 0.07, 0.08), (s * 0.18, ly, lz), IRON, ch=0.01)
        arm(b, [shoulder(b, s), (s * 0.5, -0.3, 0.38), (s * 0.22, ly, lz)], (-s, 0, 0), guards=(1,))


def crane(b, tool, k=1.0):
    # The Surge's one big crane arm (cell 5): from a pivot on the torso's back, up, out over the front, the tool hanging.
    box(b, (0.2, 0.12, 0.22), (0, 0.31, 0.36), IRON, ch=0.02)
    cyl(b, 0.065, 0.26, (0, 0.33, 0.42), IRON, segs=8, axis="X", ch=0.012)
    for s in (-1, 1):
        cyl(b, 0.04, 0.02, (s * 0.135, 0.33, 0.42), BRASS, segs=8, axis="X", ch=0.0)
    F = arm(b, [(0, 0.33, 0.42), (0, 0.16, 0.92), (0, -0.42, 0.94), (0, -0.64, 0.72)], (0, -0.25, -1), w=0.095, guards=(1,), r=0.064)
    tool(b, F, k)


def surge(b):
    torso(b)
    crane(b, injector, 1.3)
    # a green tank strapped to the left of the torso, its hose up to the crane
    tank(b, (-0.37, 0.02, 0.31), r=0.095, h=0.3)
    for z in (0.24, 0.38):
        box(b, (0.1, 0.05, 0.03), (-0.3, 0.02, z), IRON, ch=0.0)
    rod(b, (-0.37, 0.02, 0.48), (-0.12, 0.3, 0.5), 0.022, IRON, segs=6)


def tube(b, F):
    # One overheal injector tube: an iron collar, a glowing glass tube, a brass needle.
    cyl(b, 0.048, 0.05, (0, 0, 0.025), IRON, segs=8, ch=0.008, M=F)
    cyl(b, 0.036, 0.2, (0, 0, 0.15), GLOWG, segs=8, ch=0.0, M=F)
    cyl(b, 0.045, 0.03, (0, 0, 0.25), BRASS, segs=8, ch=0.0, M=F)
    cyl(b, 0.022, 0.12, (0, 0, 0.32), BRASS, segs=6, r2=0.004, ch=0.0, M=F)


def overheal(b):
    # the body is a big green pressure tank in iron bands with a domed lid
    rtier(b, 0.27, TOP, 0.6, GREEN, segs=12, ch=0.03, chb=0.015)
    for z in (0.22, 0.4, 0.55):
        rtier(b, 0.282, z - 0.02, z + 0.02, IRON, segs=12, ch=0.006)
    rtier(b, 0.255, 0.6, 0.67, IRON, segs=12, ch=0.03, r_top=0.2)
    # its label: a white disc with the green cross
    cyl(b, 0.115, 0.012, (0, -0.267, 0.47), WHITE, segs=12, axis="Y", ch=0.0)
    cross(b, (0, -0.273, 0.47), (0, -1, 0), arm=0.15, w=0.05, t=0.012)
    # three injector tubes fanned out of a manifold low on the front
    box(b, (0.38, 0.12, 0.12), (0, -0.29, 0.28), IRON, ch=0.02)
    for a in (-0.38, 0.0, 0.38):
        tube(b, frame((math.sin(a) * 0.13, -0.35, 0.28), (math.sin(a), -math.cos(a), -0.12)))
    # the overflow valve on the lid: a brass pipe, the valve wheel, a glowing vent
    rod(b, (0, 0, 0.66), (0, 0, 0.8), 0.032, BRASS, segs=8)
    F = T(0, 0, 0.8)
    ring(b, 0.1, 0.078, 0.024, (0, 0, 0), BRASS, segs=10, M=F)
    for k in range(3):
        box(b, (0.19, 0.016, 0.014), (0, 0, 0), BRASS, ch=0.0, rot=(0, 0, k * math.pi / 3), M=F)
    rod(b, (0.1, 0.1, 0.64), (0.14, 0.14, 0.74), 0.03, IRON, segs=6)
    gem(b, 0.04, 0.07, 0.01, (0.14, 0.14, 0.74), GLOWG, segs=6)
    # a pressure gauge on the right
    cyl(b, 0.075, 0.05, (0.285, -0.05, 0.4), IRON, segs=10, axis="X", ch=0.01)
    cyl(b, 0.062, 0.012, (0.312, -0.05, 0.4), WHITE, segs=10, axis="X", ch=0.0)
    ring(b, 0.078, 0.06, 0.02, (0.314, -0.05, 0.4), BRASS, segs=10, axis="X")
    box(b, (0.008, 0.01, 0.05), (0.32, -0.05, 0.415), IRON, ch=0.0, rot=(0.7, 0, 0))


def cleanse(b):
    torso(b)
    # white filter cans on the back
    box(b, (0.42, 0.06, 0.09), (0, 0.3, 0.32), IRON, ch=0.012)
    for x in (-0.12, 0.12):
        tank(b, (x, 0.355, 0.3), r=0.09, h=0.34, m=WHITE, bands=1, segs=8)
    # the cleansing halo on three posts above the cap
    for k in range(3):
        a = k * TAU / 3 + math.pi / 2
        c, s = math.cos(a), math.sin(a)
        rod(b, (c * 0.17, s * 0.17, CAPZ - 0.01), (c * 0.29, s * 0.29, 0.79), 0.018, IRON, segs=6)
    ring(b, 0.31, 0.27, 0.035, (0, 0, 0.8), GLOWG, segs=16)
    ring(b, 0.328, 0.305, 0.05, (0, 0, 0.8), BRASS, segs=16)
    # two spray arms
    for s in (-1, 1):
        F = arm(b, [shoulder(b, s), (s * 0.5, -0.08, 0.46), (s * 0.42, -0.3, 0.52)], (-s * 0.3, -1, 0.1), guards=(1,))
        sprayer(b, F)


def stub(b):
    # Never hung (the mender gets no extra barrels): a short injector arm stub, rear-bottom-centre at the origin, facing -Y.
    box(b, (0.1, 0.12, 0.08), (0, -0.06, 0.04), IRON, ch=0.012)
    injector(b, frame((0, -0.11, 0.04), (0, -1, 0)), k=0.8)


# ---------------------------------------------------------------- one piece a level (static, round the tower)
def kit2(b):   # Lv2: chunky iron feet on the foot's corners with brass studs (cells 1 / 8)
    d = (2 * FOOT - 0.14) / math.sqrt(2)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        F = T(math.cos(a) * (d + 0.045), math.sin(a) * (d + 0.045), 0) @ Rz(a)
        box(b, (0.13, 0.25, 0.19), (0, 0, 0.095), IRON, ch=0.022, M=F)
        sqstud(b, (0.065, 0, 0.1), (1, 0, 0), s=0.05, m=BRASS, M=F)
        sqstud(b, (0.0, 0, 0.19), (0, 0, 1), s=0.06, m=BRASS, M=F)


def kit3(b):   # Lv3: a row of brass rivets along the dark band, a brass band under the collar
    for k in range(4):
        a = -math.pi / 2 + k * math.pi / 2
        c, s_ = math.cos(a), math.sin(a)
        for s in (-0.1, 0.0, 0.1):
            stud(b, (c * (BAND + 0.001) - s_ * s, s_ * (BAND + 0.001) + c * s, (ZB + ZD) / 2), (c, s_, 0), r=0.014)
    tier(b, COL + 0.012, COL + 0.012, 0.095, UZ[1] - 0.025, UZ[1] + 0.02, BRASS, ch=0.006)


def toolbox(b, M):
    # The sheet's wooden toolbox (cells 2 / 5): a green band, an iron handle, spanners standing in it.
    box(b, (0.2, 0.36, 0.15), (0, 0, 0.075), WOOD, ch=0.012, M=M)
    box(b, (0.21, 0.37, 0.028), (0, 0, 0.11), GREEN, ch=0.004, M=M)
    for s in (-1, 1):
        box(b, (0.03, 0.025, 0.12), (0, s * 0.15, 0.2), IRON, ch=0.004, M=M)
    box(b, (0.03, 0.33, 0.025), (0, 0, 0.255), IRON, ch=0.004, M=M)
    wrench(b, M @ T(0.03, -0.08, 0.09) @ Rx(0.12), k=0.55)
    wrench(b, M @ T(-0.04, 0.07, 0.09) @ Ry(-0.18), k=0.48)


def kit4(b):   # Lv4: the toolbox against the right of the foot
    toolbox(b, T(FOOT + 0.095, -0.05, 0))


def kit5(b):   # Lv5: riveted iron plates on the block's four faces
    for k in range(4):
        a = -math.pi / 2 + k * math.pi / 2
        n = (math.cos(a), math.sin(a), 0)
        plate(b, 0.36, 0.12, 0.025, (n[0] * (LOW - 0.008), n[1] * (LOW - 0.008), (ZF + ZB) / 2), n, IRON, rivets=CORNERS4, rv=0.015)


def bottle(b, x, y, h=0.5, m=None):
    # A green gas bottle standing at (x, y): valve, brass cap, an iron strap band.
    cyl(b, 0.075, h - 0.03, (x, y, (h - 0.03) / 2), m or GREEN, segs=8, ch=0.025)
    cyl(b, 0.045, 0.05, (x, y, h), IRON, segs=8, ch=0.008)
    cyl(b, 0.028, 0.03, (x, y, h + 0.035), BRASS, segs=6, ch=0.005)
    cyl(b, 0.081, 0.03, (x, y, h * 0.62), IRON, segs=8, ch=0.0)


def kit6(b):   # Lv6: two green gas bottles flanking the front left corner, strapped to the tower
    d = FOOT + 0.066
    for u, v in ((-0.21, -d), (-d, -0.21)):
        bottle(b, u, v)
        if abs(v) > abs(u):
            bar(b, (u, v + 0.07, 0.3), (u, -(wall(0.3) - 0.02), 0.3), 0.03, 0.03, IRON)
        else:
            bar(b, (u + 0.07, v, 0.3), (-(wall(0.3) - 0.02), v, 0.3), 0.03, 0.03, IRON)


def kit7(b):   # Lv7: the pennant on the right of the foot, its cloth square to the 3/4 camera, below the arms' sweep
    x, y = LOW + 0.028, 0.2
    pennant(b, H - ZF - 0.02, CLOTH["Mender"], M=T(x, y, ZF) @ Rz(math.pi / 4))   # its knob at H + 0.05
    for z in (0.2, 0.5):
        bar(b, (wall(z) - 0.03, y, z), (x, y, z), 0.035, 0.035, IRON)


def kit8(b):   # Lv8: brass medallion rims round the four green crosses, brass knobs on the collar's corners
    for k in range(4):
        p, n = face(k, CZ)
        ring(b, 0.182, 0.155, 0.02, (0, 0, 0.01), BRASS, segs=8, M=frame(p, n, (-n[1], n[0], 0)))
    for k in range(4):
        a = k * TAU / 4 + math.pi / 4
        d = (2 * COL - 0.09) / math.sqrt(2)
        stud(b, (math.cos(a) * d, math.sin(a) * d, (UZ[1] + H) / 2), (math.cos(a), math.sin(a), 0), r=0.026, segs=8)


# ---------------------------------------------------------------- final classes (static, from Lv9; Kit4 + Kit6 make way)
def dress_a1(b):   # Grand mender: a workbench with a vice on the right, a tool board on the left
    M = T(FOOT + 0.105, -0.08, 0)
    box(b, (0.26, 0.46, 0.045), (0, 0, 0.42), WOOD, ch=0.01, M=M)
    for u, v in CORNERS4:
        box(b, (0.045, 0.045, 0.4), (u * 0.095, v * 0.19, 0.2), WOOD2, ch=0.006, M=M)
    box(b, (0.22, 0.42, 0.03), (0, 0, 0.12), WOOD2, ch=0.005, M=M)
    box(b, (0.1, 0.12, 0.08), (0.02, -0.15, 0.48), IRON, ch=0.012, M=M)                 # the vice
    box(b, (0.03, 0.14, 0.05), (0.02, -0.15, 0.53), IRON, ch=0.006, M=M)
    rod(b, (0.08, -0.15, 0.48), (0.16, -0.15, 0.48), 0.012, BRASS, segs=6, M=M)
    wrench(b, M @ T(0.0, 0.08, 0.455) @ Rx(-math.pi / 2) @ Rz(0.3), k=0.55)
    box(b, (0.12, 0.07, 0.07), (0.03, 0.17, 0.48), BRASS, ch=0.01, M=M)                  # an oil can
    x = -(FOOT + 0.02)
    for y in (-0.2, 0.2):
        box(b, (0.05, 0.05, 0.78), (x, y, 0.39), IRON, ch=0.006)
        bar(b, (-(BAND - 0.02), y, (ZB + ZD) / 2), (x, y, (ZB + ZD) / 2), 0.035, 0.035, IRON)
    F = T(x - 0.04, 0, 0.56) @ Rz(math.pi / 2)
    box(b, (0.48, 0.03, 0.36), (0, 0, 0), WOOD, ch=0.008, M=F)
    for xx, k in ((-0.14, 0.4), (0.0, 0.5), (0.14, 0.36)):
        wrench(b, F @ T(xx, 0.03, -0.12), k=k)


def crystals(b, x, y, s=1.0):
    # A green crystal cluster in an iron socket.
    tier(b, 0.12 * s, 0.12 * s, 0.035, 0.0, 0.08, IRON, ch=0.015, M=T(x, y, 0))
    gem(b, 0.075 * s, 0.38 * s, 0.02, (x, y, 0.08), GLOWG, segs=6)
    for k in range(3):
        a = k * TAU / 3 + 0.5
        gem(b, 0.042 * s, 0.19 * s, 0.01, (0, 0, 0), GREEN, segs=5,
            M=T(x + math.cos(a) * 0.06 * s, y + math.sin(a) * 0.06 * s, 0.075) @ Rz(a) @ Ry(0.45))


def dress_a2(b):   # Core mender: green crystal clusters set against both sides, cables into the block
    for sx, y, s in ((1, -0.05, 1.0), (-1, 0.08, 0.9)):
        x = sx * (FOOT + 0.115 * s)
        crystals(b, x, y, s)
        rod(b, (x - sx * 0.08, y, 0.05), (sx * (wall(0.2) - 0.02), y, 0.2), 0.022, IRON, segs=6)
        box(b, (0.03, 0.09, 0.09), (sx * (wall(0.2) + 0.005), y, 0.2), BRASS, ch=0.008)


def dress_b1(b):   # Overheal: three green tanks strapped against the left, a pressure gauge post on the right
    x = -(FOOT + 0.066)
    for y in (-0.2, 0.0, 0.2):
        bottle(b, x, y, h=0.6)
    for z in (0.16, 0.44):
        bar(b, (x - 0.085, -0.3, z), (x - 0.085, 0.3, z), 0.03, 0.035, IRON)
        for y in (-0.3, 0.3):
            bar(b, (x - 0.085, y, z), (-(wall(z) - 0.02), y, z), 0.03, 0.035, IRON)
    x = FOOT + 0.09
    tier(b, 0.09, 0.09, 0.03, 0.0, 0.08, IRON, ch=0.015, M=T(x, -0.05, 0))
    cyl(b, 0.03, 0.5, (x, -0.05, 0.33), IRON, segs=8, ch=0.0)
    rod(b, (x, -0.05, 0.2), (wall(0.2) - 0.02, -0.05, 0.2), 0.022, IRON, segs=6)
    F = frame((x, -0.05, 0.62), (0.64, -0.77, 0))   # the dial faces the 3/4 camera
    cyl(b, 0.1, 0.05, (0, 0, 0), IRON, segs=12, ch=0.01, M=F)
    cyl(b, 0.085, 0.012, (0, 0, 0.026), WHITE, segs=12, ch=0.0, M=F)
    ring(b, 0.105, 0.088, 0.02, (0, 0, 0.028), BRASS, segs=12, M=F)
    box(b, (0.01, 0.07, 0.008), (0.015, 0.02, 0.034), RED, ch=0.0, rot=(0, 0, 0.6), M=F)


def dress_b2(b):   # Cleanse: white filter cans racked on the right, a stone basin with a spray on the left
    x = FOOT + 0.078
    for y in (-0.28, -0.08, 0.12):
        tank(b, (x, y, 0.23), r=0.085, h=0.42, m=WHITE)
    for z in (0.12, 0.36):
        box(b, (0.2, 0.62, 0.035), (x, -0.08, z), IRON, ch=0.006)
    x = -(FOOT + 0.15)
    rtier(b, 0.16, 0.0, 0.22, STONE2, segs=10, ch=0.03, r_top=0.19, M=T(x, 0.05, 0))
    cyl(b, 0.165, 0.02, (x, 0.05, 0.2), GLOWG, segs=10, ch=0.0)
    rod(b, (x, 0.05, 0.2), (x, 0.05, 0.52), 0.024, IRON, segs=6)
    cyl(b, 0.03, 0.08, (x, 0.05, 0.56), BRASS, segs=8, r2=0.065, ch=0.0)
    cyl(b, 0.058, 0.012, (x, 0.05, 0.6), GLOWG, segs=8, ch=0.0)
    rod(b, (x + 0.15, 0.05, 0.12), (-(wall(0.2) - 0.02), 0.05, 0.2), 0.02, IRON, segs=6)


model("MenderBase", base)
model("MenderBody", body, scale=SC)
model("MenderDefault", default, scale=SC)
model("MenderA", wide, scale=SC)
model("MenderA1", grand_mender, scale=SC)
model("MenderA2", core_mender, scale=SC)
model("MenderB", surge, scale=SC)
model("MenderB1", overheal, scale=SC)
model("MenderB2", cleanse, scale=SC)
model("MenderBarrel", stub, scale=SC)
for n, f in ((2, kit2), (3, kit3), (4, kit4), (5, kit5), (6, kit6), (7, kit7), (8, kit8)):
    model("MenderKit%d" % n, f)
for n, f in (("A1", dress_a1), ("A2", dress_a2), ("B1", dress_b1), ("B2", dress_b2)):
    model("MenderDress" + n, f)
export_all()
layout("Mender", head=H, muzzle=round(H + 0.5 * SC, 3), pod=None)
