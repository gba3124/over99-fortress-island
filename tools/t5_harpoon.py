# Harpoon turret, art pass 5 (tools/t5kit.py). Plinth (TURRET-UPGRADE-DESIGN.md §0): a square stone base in alternating
# quoin courses (the gatling sheet's 1) carrying a wooden A-frame, a long harpoon rail and a rope winch at the front.
# Classes (§3): A Multi-shot (two harpoons side by side) -> A1 Volley (three harpoons, three slide rails out in front, three
# small reels on the side) / A2 Chain harpoon (the main harpoon trails a chain over a side sprocket to a second small
# harpoon); B Freeze (an ice point, a coolant tank on the rail) -> B1 Glacier (a huge ice crystal round the head, blue-white
# condenser coils, frost at the foot) / B2 Shatter (the head split into three ice blades, a crushed-ice hopper behind).
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/t5_harpoon.py   [ONLY=HarpoonBase,HarpoonA ...]
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from t5kit import *  # noqa: F401,F403

H = 0.74          # head pivot: the top of the plinth
AX = 0.52         # the harpoon's axis above the pivot (head parts are drawn at this size, exported SC bigger)
TOP = 0.08        # the deck's top (the A-frame and the pods stand on it)
DECK = 0.39       # deck half width (an octagon)
SC = 1.2          # the head parts' scale (the launcher reads big against its plinth)
PX, PW, PD, PH = 0.30, 0.12, 0.24, 0.13   # extra launcher pods: |x| of their centre, width, depth, height (= the stacking step)
POD = (PX * SC, TOP * SC, -PD / 2 * SC)   # (Unity head space) beside the A-frame, on the deck, centred on the pivot
STEP = (0.0, PH * SC, 0.0)                # each further pair stands on the one before (four high on the Volley at Lv15)

FOOT, SHAFT, CAP = 0.50, 0.36, 0.45   # half widths
RT, RB = AX - 0.045, AX - 0.16        # the rail's top and bottom
AXLE = AX - 0.2                       # the A-frame's axle
WY, WZ = -0.32, TOP + 0.16            # the winch drum's place on the deck
Y1 = -0.56                            # the rail's nose


# ---------------------------------------------------------------- local shapes
def harpoon(b, x=0.0, z=AX, tail=0.30, neck=-0.95, tip=-1.25, r=0.03, head=0.09, barb=1.0, shaft=WOOD2, hm=IRON,
            fins=True, eye=None, M=I4):
    # A harpoon lying along -Y: the shaft, a brass collar, a four-sided point with swept-back barbs, brass flights.
    rod(b, (x, tail, z), (x, neck, z), r, shaft, segs=6, M=M)
    cyl(b, r * 1.8, r * 1.6, (x, neck + r * 0.8, z), BRASS, segs=6, axis="Y", ch=0.0, M=M)
    cyl(b, head, neck - tip, (x, (neck + tip) / 2, z), hm, segs=4, axis="Y", r2=0.004, ch=0.0, phase=0, M=M)
    if barb:
        for s in (-1, 1):
            bar(b, (x + s * head * 0.5, neck - head * 0.3, z), (x + s * head * (0.5 + 0.9 * barb), neck + head * 1.1, z),
                head * 0.4, head * 0.28, hm, ch=0.0, M=M)
    if fins:
        f = r * 4.5
        box(b, (r * 0.35, f * 1.25, f), (x, tail - f * 0.6, z), BRASS, ch=0.0, M=M)
        box(b, (f, f * 1.25, r * 0.35), (x, tail - f * 0.6, z), BRASS, ch=0.0, M=M)
    if eye is not None:
        ring(b, r * 1.35, r * 0.8, r * 0.5, (x, eye, z - r * 2.0), IRON, segs=6, axis="X", M=M)


def standing(x, y, lean):
    # The frame of a harpoon standing tip up at (x, y) on the ground, its top leaning `lean` towards -y.
    return T(x, y, 0) @ Rx(-math.pi / 2 + lean)


def sprocket(b, r, w, M, m=None, teeth=10):
    # A chain sprocket facing +X: a rim with square teeth, three spokes, a brass hub (cheaper than t5kit.gear).
    m = m or IRON
    ring(b, r * 0.86, r * 0.62, w, (0, 0, 0), m, segs=teeth, M=M, axis="X")
    for k in range(teeth):
        a = (k + 0.5) * TAU / teeth
        box(b, (w, r * 0.2, r * 0.2), (0, math.cos(a) * r * 0.92, math.sin(a) * r * 0.92), m, ch=0.0, rot=(a, 0, 0), M=M)
    for k in range(3):
        a = k * TAU / 3
        box(b, (w * 0.7, r * 0.14, r * 0.66), (0, math.cos(a) * r * 0.36, math.sin(a) * r * 0.36), m, ch=0.0,
            rot=(a - math.pi / 2, 0, 0), M=M)
    cyl(b, r * 0.2, w * 1.6, (0, 0, 0), BRASS, segs=6, axis="X", ch=0.0, M=M)


def chain(b, a, c, n, r=0.03):
    chain_links(b, a, c, n, r, STEEL)


# ---------------------------------------------------------------- plinth (static)
def base(b):
    tier(b, FOOT, FOOT, 0.14, 0.0, 0.12, STONE2, ch=0.035, chb=0.015)              # foot
    tier(b, FOOT - 0.06, FOOT - 0.06, 0.13, 0.12, 0.18, STONE, ch=0.025)            # moulding
    tier(b, SHAFT - 0.05, SHAFT - 0.05, 0.08, 0.18, 0.62, STONE2, ch=0.0)           # the core the blocks clad (dark joints)
    for course, (z0, z1) in enumerate(((0.18, 0.40), (0.40, 0.62))):              # two courses, the quoins alternating
        h, zc = z1 - z0 - 0.012, (z0 + z1) / 2
        lx, ly = (0.26, 0.18) if course == 0 else (0.18, 0.26)
        for u, v in CORNERS4:
            box(b, (lx, ly, h), (u * (SHAFT - lx / 2), v * (SHAFT - ly / 2), zc), STONE, ch=0.02)
        for v in (-1, 1):
            box(b, (2 * (SHAFT - lx) - 0.012, 0.1, h), (0, v * (SHAFT - 0.05), zc), STONE, ch=0.02)
        for u in (-1, 1):
            box(b, (0.1, 2 * (SHAFT - ly) - 0.012, h), (u * (SHAFT - 0.05), 0, zc), STONE, ch=0.02)
    tier(b, CAP, CAP, 0.13, 0.62, H, STONE, ch=0.03, chb=0.02)                      # cap slab
    for u, v in CORNERS4:
        stud(b, (u * (FOOT - 0.11), v * (FOOT - 0.11), 0.12), r=0.03, segs=4)


# ---------------------------------------------------------------- deck (turns)
def body(b):
    tier(b, DECK, DECK, 0.12, 0.0, 0.045, IRON, ch=0.012)                          # the iron turntable plate
    tier(b, DECK - 0.02, DECK - 0.02, 0.11, 0.045, TOP, WOOD, ch=0.012)            # its plank top
    for x in (-0.18, -0.06, 0.06, 0.18):
        box(b, (0.012, 2 * (DECK - 0.07), 0.006), (x, 0, TOP + 0.001), WOOD2, ch=0.0)
    for u, v in CORNERS4:
        stud(b, (u * (DECK - 0.09), v * (DECK - 0.09), TOP), r=0.022)
    for u in (-1, 1):
        stud(b, (u * (DECK - 0.04), 0, 0.03), (u, 0, 0), r=0.02, segs=4)


# ---------------------------------------------------------------- the launcher's frame (part of every head)
def aframe(b, x=0.18, w2=WOOD2):
    # Two wooden A-frames either side, iron shoes and bearing blocks, the axle across them, a saddle clamping the rail on.
    for s in (-1, 1):
        cx = s * x
        for v in (-1, 1):
            bar(b, (cx, v * 0.25, TOP), (cx, v * 0.04, AXLE - 0.05), 0.075, 0.075, WOOD, ch=0.012, up=(1, 0, 0))
            box(b, (0.09, 0.085, 0.045), (cx, v * 0.245, TOP + 0.0225), IRON, ch=0.0)
            stud(b, (cx + s * 0.045, v * 0.245, TOP + 0.0225), (s, 0, 0), r=0.015, segs=4)
        bar(b, (cx, -0.18, TOP + 0.11), (cx, 0.18, TOP + 0.11), 0.06, 0.06, w2, ch=0.008)
        box(b, (0.1, 0.15, 0.13), (cx, 0, AXLE), IRON, ch=0.018)
        for v in (-1, 1):  # on the bearing's front and back, clear of the pods beside it
            stud(b, (cx, v * 0.075, AXLE + 0.03), (0, v, 0), r=0.02, segs=4)
    cyl(b, 0.04, 2 * x + 0.06, (0, 0, AXLE), IRON, segs=8, axis="X", ch=0.0)
    box(b, (0.16, 0.16, RB - AXLE + 0.06), (0, 0, (RB + AXLE - 0.06) / 2), IRON, ch=0.014)


def rail(b, w=0.16, y0=0.45, lanes=(0.0,), bands=(0.3, -0.05, -0.36)):
    # The long wooden rail, iron-banded, iron lips for each harpoon, an iron guide on its nose they run through.
    box(b, (w, y0 - Y1, RT - RB), (0, (y0 + Y1) / 2, (RT + RB) / 2), WOOD, ch=0.018)
    for y in bands:
        box(b, (w + 0.016, 0.035, RT - RB + 0.014), (0, y, (RT + RB) / 2), IRON, ch=0.0)
    for x in lanes:
        for s in (-1, 1):
            box(b, (0.022, y0 - Y1 - 0.04, 0.022), (x + s * 0.045, (y0 + Y1) / 2, RT + 0.011), IRON, ch=0.0)
    box(b, (w + 0.04, 0.08, AX + 0.065 - RB), (0, Y1 + 0.04, (AX + 0.065 + RB) / 2), IRON, ch=0.02)
    for u in (-1, 1):
        stud(b, (u * (w / 2 + 0.02), Y1 + 0.04, AX), (u, 0, 0), r=0.022, segs=4)


def breech(b, y=0.45, w=0.2):
    # The trigger box on the rail's back end, two aiming handles behind it.
    box(b, (w, 0.12, 0.16), (0, y - 0.04, RB + 0.07), IRON, ch=0.022)
    stud(b, (0, y + 0.02, RB + 0.1), (0, 1, 0), r=0.024)
    for s in (-1, 1):
        rod(b, (s * 0.07, y + 0.02, RB + 0.05), (s * 0.07, y + 0.16, RB + 0.02), 0.018, IRON, segs=6)
        ball(b, 0.034, (s * 0.07, y + 0.17, RB + 0.02), BRASS)


def winch(b, y=WY, z=WZ, hw=0.15):
    # The rope winch on the front of the deck: a drum of rope between wooden posts, a crank right, a ratchet and pawl left.
    for s in (-1, 1):
        box(b, (0.06, 0.1, z - TOP + 0.07), (s * hw, y, (TOP + z + 0.07) / 2), WOOD, ch=0.012)
    cyl(b, 0.08, 2 * hw - 0.06, (0, y, z), SAND, segs=8, axis="X", ch=0.025)
    for s in (-1, 1):
        cyl(b, 0.1, 0.022, (s * (hw - 0.041), y, z), IRON, segs=8, axis="X", ch=0.0)
    cyl(b, 0.024, 2 * hw + 0.08, (0, y, z), IRON, segs=6, axis="X", ch=0.0)
    box(b, (0.022, 0.03, 0.13), (hw + 0.042, y, z + 0.055), IRON, ch=0.0)
    rod(b, (hw + 0.042, y, z + 0.11), (hw + 0.085, y, z + 0.11), 0.016, WOOD, segs=6)
    cyl(b, 0.06, 0.022, (-(hw + 0.042), y, z), BRASS, segs=8, axis="X", ch=0.0)
    box(b, (0.02, 0.07, 0.02), (-(hw + 0.042), y + 0.05, z + 0.05), IRON, ch=0.0, rot=(0.6, 0, 0))


def rope_line(b, x=0.0, eye=-0.85):
    # The winch's rope through a fairlead under the rail's nose to the harpoon's eye.
    fl = (x, Y1 - 0.005, RB - 0.045)
    box(b, (0.03, 0.03, 0.045), (x, Y1 + 0.01, RB - 0.0225), IRON, ch=0.0)
    ring(b, 0.032, 0.019, 0.016, fl, IRON, segs=6, axis="X")
    rod(b, (x, WY - 0.04, WZ + 0.075), fl, 0.013, SAND, segs=4)
    rod(b, fl, (x, eye, AX - 0.1), 0.013, SAND, segs=4)


# ---------------------------------------------------------------- heads (turn)
def default(b):
    aframe(b)
    rail(b)
    harpoon(b, eye=-0.85)
    breech(b)
    winch(b)
    rope_line(b)


def multishot(b):
    aframe(b)
    rail(b, w=0.34, lanes=(-0.09, 0.09))
    for s in (-1, 1):
        harpoon(b, x=s * 0.09, head=0.07, barb=0.6, eye=-0.85, tail=0.28 - 0.04 * (s + 1))
        rope_line(b, x=s * 0.09)
    breech(b, w=0.34)
    winch(b)


def volley(b):
    aframe(b)
    lanes = (-0.14, 0.0, 0.14)
    rail(b, w=0.44, y0=0.5, lanes=lanes)
    for i, x in enumerate(lanes):
        harpoon(b, x=x, head=0.065, barb=0.5, tail=0.34 - 0.05 * i, neck=-1.0, tip=-1.26, eye=-0.9 if x == 0 else None)
        for s in (-1, 1):  # its slide rails out in front of the nose
            box(b, (0.022, 0.3, 0.022), (x + s * 0.045, Y1 - 0.15, RT + 0.011), IRON, ch=0.0)
    box(b, (0.44, 0.045, 0.04), (0, Y1 - 0.29, RT - 0.009), IRON, ch=0.008)            # their front bar
    for s in (-1, 1):
        bar(b, (s * 0.2, Y1, RB + 0.02), (s * 0.2, Y1 - 0.29, RT - 0.03), 0.035, 0.035, IRON, ch=0.0)
    breech(b, y=0.5, w=0.44)
    # three small reels on the right side, behind the pods
    box(b, (0.022, 0.36, 0.13), (0.231, 0.34, RB + 0.055), IRON, ch=0.004)
    for y in (0.22, 0.34, 0.46):
        z = RB + 0.055
        cyl(b, 0.048, 0.07, (0.28, y, z), SAND, segs=8, axis="X", ch=0.012)
        for x in (0.248, 0.312):
            cyl(b, 0.06, 0.014, (x, y, z), IRON, segs=8, axis="X", ch=0.0)
        cyl(b, 0.014, 0.13, (0.28, y, z), IRON, segs=6, axis="X", ch=0.0)
        box(b, (0.014, 0.022, 0.08), (0.345, y, z + 0.035), IRON, ch=0.0)
        rod(b, (0.345, y, z + 0.07), (0.38, y, z + 0.07), 0.011, WOOD, segs=6)
    rope_line(b, x=0.0, eye=-0.9)
    winch(b)


def chained(b):
    aframe(b)
    rail(b, y0=0.52)
    harpoon(b, tail=-0.02, head=0.1, fins=False, eye=-0.85)
    ring(b, 0.042, 0.025, 0.018, (0, -0.035, AX), STEEL, segs=6, axis="X")           # an eye on its tail
    harpoon(b, tail=0.44, neck=0.26, tip=0.15, r=0.022, head=0.055, barb=0.6)       # the second, smaller one behind it
    ring(b, 0.035, 0.02, 0.015, (0, 0.29, AX), STEEL, segs=6, axis="X")
    hub = (0.15, 0.06, AX + 0.12)
    sprocket(b, 0.14, 0.04, T(*hub), m=BRASS)                                       # the chain runs up over it
    bar(b, (0.08, 0.06, RT), hub, 0.035, 0.045, IRON, ch=0.0)
    chain(b, (0.01, -0.04, AX + 0.02), (0.145, -0.04, AX + 0.22), 4, r=0.036)
    chain(b, (0.145, 0.16, AX + 0.22), (0.01, 0.29, AX + 0.03), 4, r=0.036)
    breech(b, y=0.52)
    winch(b)
    rope_line(b)


def frost_ring(b, y=Y1 - 0.015):
    ring(b, 0.06, 0.036, 0.035, (0, y, AX), IRON, segs=8, axis="Y")
    ring(b, 0.04, 0.03, 0.03, (0, y - 0.004, AX), CYAN, segs=8, axis="Y")


def coolant_tank(b, x=0.0, y0=0.2, y1=0.44, r=0.065, z=None):
    z = RT + r + 0.012 if z is None else z
    for y in (y0 + 0.04, y1 - 0.04):  # iron cradles
        box(b, (2 * r - 0.01, 0.03, z - RT), (x, y, (z + RT) / 2), IRON, ch=0.0)
    cyl(b, r, y1 - y0, (x, (y0 + y1) / 2, z), CYAN, segs=8, axis="Y", ch=0.015)
    for y in (y0, y1):
        cyl(b, r + 0.012, 0.025, (x, y, z), IRON, segs=8, axis="Y", ch=0.0)
    cyl(b, 0.022, 0.05, (x, y0 - 0.035, z), BRASS, segs=6, axis="Y", ch=0.0)
    return z


def freeze(b):
    aframe(b, w2=WOOD)
    rail(b)
    harpoon(b, tail=0.16, head=0.095, shaft=WOOD, hm=ICE, fins=False, eye=-0.85)
    gem(b, 0.06, 0.07, 0.09, (0, 0, 0), ICE, segs=5, M=T(0, -0.93, AX) @ Rx(math.pi / 2))   # frost crust round the neck
    z = coolant_tank(b)
    rod(b, (0.0, 0.165, z), (0.09, 0.12, RT - 0.02), 0.013, CYAN, segs=4)
    rod(b, (0.09, 0.12, RT - 0.02), (0.09, Y1 + 0.08, RT - 0.02), 0.013, CYAN, segs=4)
    frost_ring(b)
    breech(b)
    winch(b)
    rope_line(b)


def glacier(b):
    aframe(b, w2=WOOD)
    rail(b)
    harpoon(b, tail=0.16, neck=-0.82, tip=-0.95, head=0.07, barb=0, shaft=WOOD, hm=ICE, fins=False, eye=-0.7)
    F = T(0, -1.04, AX) @ Rx(math.pi / 2)                                             # the huge crystal round the head
    gem(b, 0.23, 0.46, 0.26, (0, 0, 0), ICE, segs=6, M=F, mid=(0.8, 0.12))
    for a, c, s in ((0.7, 0.3, 0.6), (-0.8, 1.9, 0.55), (0.5, 3.6, 0.5), (-0.6, 4.9, 0.45)):
        gem(b, 0.14 * s, 0.4 * s, 0.04, (0, 0, 0), ICE, segs=5, M=F @ Rz(c) @ Ry(a) @ T(0, 0, 0.05))
    zc = (RT + RB) / 2 + 0.04
    coil_rings(b, 0.14, 0.12, 0.44, 5, CYAN, segs=8, thick=0.04, M=T(0, 0, zc) @ Rx(math.pi / 2))     # condenser coils round rail and shaft
    box(b, (0.38, 0.32, 0.022), (0, 0.27, RT + 0.011), IRON, ch=0.005)                  # a plate across the rail for the tanks
    for s in (-1, 1):  # twin coolant tanks on it, pipes into the coils
        z = coolant_tank(b, x=s * 0.125, y0=0.12, y1=0.42, r=0.06, z=RT + 0.022 + 0.06 + 0.01)
        rod(b, (s * 0.125, 0.1, z), (s * 0.1, -0.12, zc + 0.05), 0.015, CYAN, segs=4)
    frost_ring(b)
    breech(b)
    winch(b)
    rope_line(b, eye=-0.7)


def shatter(b):
    aframe(b, w2=WOOD)
    rail(b)
    harpoon(b, tail=0.14, neck=-0.86, tip=-0.88, head=0.04, barb=0, shaft=WOOD, hm=ICE, fins=False, eye=-0.78)
    hub = (0, -0.9, AX)
    cyl(b, 0.065, 0.08, hub, BRASS, segs=6, axis="Y", ch=0.012)
    for a in (-0.5, 0.0, 0.5):  # three ice blades fanned out of the hub
        F = T(*hub) @ Rz(a) @ Rx(math.pi / 2) @ S(1.8, 0.45, 1)
        gem(b, 0.055, 0.46 if a == 0 else 0.4, 0.06, (0, 0, 0.03), ICE, segs=4, M=F)
    # the crushed-ice hopper behind the harpoon, ice chunks heaped in it
    tier(b, 0.08, 0.08, 0.02, RT, RT + 0.16, IRON, ch=0.012, taper=-0.06, M=T(0, 0.27, 0))
    for x, y, s in ((-0.06, 0.23, 1.0), (0.06, 0.31, 0.9), (0.0, 0.27, 1.3), (0.07, 0.2, 0.8), (-0.05, 0.33, 0.85)):
        gem(b, 0.05 * s, 0.08 * s, 0.03, (x, y, RT + 0.17), ICE, segs=5)
    box(b, (0.06, 0.12, 0.03), (0, 0.15, RT + 0.035), IRON, ch=0.0, rot=(0.35, 0, 0))   # chute to the rail
    frost_ring(b)
    breech(b)
    winch(b)
    rope_line(b, eye=-0.78)


# ---------------------------------------------------------------- extra launcher pod (turns, on the deck)
def pod(b):
    # rear-bottom-centre at the origin, pointing -Y: a flat-topped launcher box (the next one stands on it) and its bolt.
    box(b, (PW, PD, PH), (0, -PD / 2, PH / 2), WOOD, ch=0.015)
    box(b, (PW + 0.01, 0.025, PH + 0.01), (0, -PD + 0.0125, PH / 2), IRON, ch=0.0)
    box(b, (PW + 0.008, 0.03, PH + 0.004), (0, -0.07, PH / 2), IRON, ch=0.0)
    harpoon(b, z=0.07, tail=-PD + 0.02, neck=-PD - 0.1, tip=-PD - 0.17, r=0.014, head=0.03, barb=0.6, fins=False)
    for s in (-1, 1):  # studs on its face (none on the sides: the pods stand hard against the frame)
        stud(b, (s * 0.04, -PD, PH - 0.025), (0, -1, 0), r=0.013, segs=4)


# ---------------------------------------------------------------- one piece a level (static, round the plinth)
def kit2(b):   # Lv2: iron caps on the foot's corners, an iron band where the shaft starts
    c = FOOT - 0.14 / 2
    for u, v in CORNERS4:
        plate(b, 0.22, 0.13, 0.04, (u * c, v * c, 0.065), (u, v, 0), IRON, rivets=((-1, 0), (1, 0)), rv=0.02)
    tier(b, SHAFT + 0.015, SHAFT + 0.015, 0.03, 0.18, 0.23, IRON, ch=0.01)


def kit3(b):   # Lv3: a brass band round the cap slab, a rivet on each face and corner
    tier(b, CAP + 0.008, CAP + 0.008, 0.132, 0.665, 0.695, BRASS, ch=0.008)
    for k in range(8):
        a = k * TAU / 8
        hw = CAP + 0.008
        d = hw if k % 2 == 0 else (2 * hw - 0.132) / math.sqrt(2)
        stud(b, (math.cos(a) * d, math.sin(a) * d, 0.68), (math.cos(a), math.sin(a), 0), r=0.018, segs=4)


def kit4(b):   # Lv4: spare harpoons racked against the back
    y = FOOT + 0.06
    box(b, (0.42, 0.12, 0.06), (0, y, 0.03), WOOD, ch=0.01)
    for x in (-0.13, 0.0, 0.13):
        harpoon(b, z=0.0, tail=-0.06, neck=-0.5, tip=-0.66, r=0.018, head=0.045, barb=0.7, M=standing(x, y, 0.18))
    box(b, (0.36, 0.03, 0.03), (0, y - 0.3 * math.sin(0.18) + 0.025, 0.3), IRON, ch=0.0)


def kit5(b):   # Lv5: riveted iron plates on the shaft's four faces
    for k in range(4):
        a = k * TAU / 4
        n = (math.cos(a), math.sin(a), 0)
        plate(b, 0.3, 0.17, 0.025, (n[0] * SHAFT, n[1] * SHAFT, 0.29), n, IRON, rivets=CORNERS4, rv=0.018)


def kit6(b):   # Lv6: coils of harpoon line and a line tub on the left
    x = -(FOOT + 0.15)
    coil_rings(b, 0.15, 0.0, 0.12, 3, SAND, segs=10, thick=0.04, M=T(x, -0.14, 0))
    coil_rings(b, 0.11, 0.12, 0.2, 2, SAND, segs=10, thick=0.04, M=T(x + 0.01, -0.13, 0))
    xt = -(FOOT + 0.12)
    rtier(b, 0.12, 0.0, 0.26, WOOD, segs=10, ch=0.015, r_top=0.13, M=T(xt, 0.18, 0))
    for z in (0.05, 0.21):
        ring(b, 0.132 + z * 0.04, 0.11, 0.025, (xt, 0.18, z), IRON, segs=10)
    coil_rings(b, 0.1, 0.26, 0.3, 1, SAND, segs=10, thick=0.04, M=T(xt, 0.18, 0))


PEN = -math.radians(25)   # the pennant: front right, its cloth running back along the 3/4 view


def kit7(b):   # Lv7: the turret's pennant, the cloth square to the 3/4 camera, under the deck's sweep
    px, py = FOOT + 0.035, (FOOT + 0.035) * math.tan(PEN)
    pennant(b, 0.66, CLOTH["Harpoon"], M=T(px, py, 0) @ Rz(math.atan2(0.8, 0.95)))
    box(b, (0.1, 0.1, 0.03), (px, py, 0.015), IRON, ch=0.008)
    for z in (0.3, 0.55):
        bar(b, (px, py, z), (SHAFT - 0.01, py, z), 0.035, 0.035, IRON)


def kit8(b):   # Lv8: the brass crest on the front (two crossed harpoons), brass knobs on the foot's front corners
    y = -SHAFT
    plate(b, 0.22, 0.18, 0.025, (0, y, 0.51), (0, -1, 0), BRASS, rivets=CORNERS4, rv=0.014, rivet_m=IRON)
    for s in (-1, 1):
        F = T(0, y - 0.03, 0.51) @ Ry(s * 0.7) @ Rx(-math.pi / 2)
        harpoon(b, z=0.0, tail=0.09, neck=-0.06, tip=-0.11, r=0.012, head=0.025, barb=0.8, shaft=IRON, fins=False, M=F)
    for u in (-1, 1):  # knobs on the upper course's front quoins
        box(b, (0.05, 0.05, 0.05), (u * 0.26, y - 0.02, 0.51), IRON, ch=0.008)
        ball(b, 0.035, (u * 0.26, y - 0.065, 0.51), BRASS)


# ---------------------------------------------------------------- final classes (static, from Lv9; Kit4 + Kit6 make way)
def dress_a1(b):   # Volley: a long rack of spare harpoons at the back, crates of bolts on the left
    y = FOOT + 0.06
    box(b, (0.62, 0.12, 0.06), (0, y, 0.03), WOOD, ch=0.01)
    for x in (-0.24, -0.12, 0.0, 0.12, 0.24):
        harpoon(b, z=0.0, tail=-0.06, neck=-0.5, tip=-0.66, r=0.018, head=0.045, barb=0.7, M=standing(x, y, 0.18))
    box(b, (0.56, 0.03, 0.03), (0, y - 0.3 * math.sin(0.18) + 0.025, 0.3), IRON, ch=0.0)
    x = -(FOOT + 0.11)
    for i, yy in enumerate((-0.16, 0.14)):
        crate(b, (0.22, 0.28, 0.2), M=T(x, yy, 0) @ Rz(0.04 * (2 * i - 1)), planks=1)
    crate(b, (0.18, 0.22, 0.15), M=T(x + 0.01, -0.02, 0.2) @ Rz(0.08), planks=1)


def dress_a2(b):   # Chain harpoon: a chain locker with its chain heaped at the back, a spare sprocket leaning on the left
    y = FOOT + 0.1
    box(b, (0.44, 0.2, 0.16), (0, y, 0.08), IRON, ch=0.015)
    for u in (-1, 1):
        stud(b, (u * 0.17, y - 0.1, 0.12), (0, -1, 0), r=0.02)
    coil_rings(b, 0.13, 0.16, 0.25, 3, STEEL, segs=8, thick=0.03, M=T(0, y, 0))
    chain(b, (0.12, y - 0.04, 0.24), (0.2, SHAFT + 0.02, 0.5), 5)
    box(b, (0.08, 0.04, 0.06), (0.2, SHAFT + 0.01, 0.5), IRON, ch=0.0)
    sprocket(b, 0.24, 0.05, T(-(FOOT + 0.05), 0.0, 0.0) @ Ry(0.34) @ T(0, 0, 0.245))


def dress_b1(b):   # Glacier: frost at the foot, ice crystals growing round it, a frozen block on the left
    c = FOOT - 0.14 / 2
    for u, v in CORNERS4:  # crystal clusters on the foot's corners
        for k, (dx, dy, s, lean) in enumerate(((0.06, 0.0, 1.0, 0.25), (-0.02, 0.06, 0.75, -0.3), (0.05, 0.07, 0.6, 0.4))):
            gem(b, 0.055 * s, 0.24 * s, 0.03, (0, 0, 0), ICE, segs=5,
                M=T(u * (c + dx), v * (c + dy), 0.02) @ Rz(math.atan2(v, u)) @ Ry(lean))
    for x, s_, lean in ((-0.2, 0.8, 0.3), (0.12, 0.65, -0.35), (0.3, 0.55, 0.2)):  # and along the back
        gem(b, 0.05 * s_, 0.22 * s_, 0.03, (0, 0, 0), ICE, segs=5, M=T(x, FOOT + 0.03, 0.0) @ Rx(-lean))
    x = -(FOOT + 0.13)
    box(b, (0.26, 0.4, 0.22), (x, 0.0, 0.11), ICE, ch=0.04)
    for y, s in ((-0.12, 1.0), (0.1, 0.8), (0.0, 1.2)):
        gem(b, 0.06 * s, 0.2 * s, 0.03, (x, y, 0.2), ICE, segs=5)


def dress_b2(b):   # Shatter: bins of crushed ice at the back and on the left
    y = FOOT + 0.11
    tier(b, 0.2, 0.1, 0.03, 0.0, 0.2, IRON, ch=0.012, taper=-0.03, M=T(0, y, 0))
    for x, s in ((-0.12, 1.0), (0.0, 1.3), (0.12, 0.9), (-0.05, 0.8), (0.07, 1.1)):
        gem(b, 0.05 * s, 0.09 * s, 0.03, (x, y + 0.02 * s, 0.2), ICE, segs=5)
    x = -(FOOT + 0.11)
    for i, yy in enumerate((-0.15, 0.15)):
        crate(b, (0.22, 0.26, 0.18), M=T(x, yy, 0) @ Rz(0.05 * (2 * i - 1)), planks=1, lid=False)
        for dx, dy, s in ((-0.04, -0.04, 1.0), (0.05, 0.03, 0.8), (0.0, 0.06, 1.1)):
            gem(b, 0.045 * s, 0.08 * s, 0.03, (x + dx, yy + dy, 0.18), ICE, segs=5)


model("HarpoonBase", base)
model("HarpoonBody", body, scale=SC)
model("HarpoonDefault", default, scale=SC)
model("HarpoonA", multishot, scale=SC)
model("HarpoonA1", volley, scale=SC)
model("HarpoonA2", chained, scale=SC)
model("HarpoonB", freeze, scale=SC)
model("HarpoonB1", glacier, scale=SC)
model("HarpoonB2", shatter, scale=SC)
model("HarpoonBarrel", pod, scale=SC)
for n, f in ((2, kit2), (3, kit3), (4, kit4), (5, kit5), (6, kit6), (7, kit7), (8, kit8)):
    model("HarpoonKit%d" % n, f)
for n, f in (("A1", dress_a1), ("A2", dress_a2), ("B1", dress_b1), ("B2", dress_b2)):
    model("HarpoonDress" + n, f)
export_all()
layout("Harpoon", head=H, muzzle=round(H + AX * SC, 3), pod=[round(v, 3) for v in POD], step=[round(v, 3) for v in STEP])
