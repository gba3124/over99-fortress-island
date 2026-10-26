# Sniper turret, art pass 5 (tools/t5kit.py; Owen's sniper sheet /tmp/t5/full/14.png). Plinth: the tall stone tower with an
# iron cap (sheet cell 3), iron strips and feet from cell 7. The rifle: a long thin barrel with a slotted brake, a scope with
# brass rings and a blue lens, a wood stock, on an iron yoke. Classes (TURRET-UPGRADE-DESIGN.md §7): A Armour-breaker
# (heavier barrel, bigger brake, AP rounds on the stock) -> A1 Boss slayer (extra-long thick barrel, huge brake, long scope,
# counterweight) / A2 Piercing shot (rail barrel, piercing spike, cyan coils); B Rapid (short shrouded barrel, box magazine,
# carry handle) -> B1 Machine sniper (side drum, folded bipod, cooling jacket) / B2 Mark (big observation scope (cell 6),
# marker lamp, radar dish).
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/t5_sniper.py   [ONLY=SniperBase,SniperA ...]
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from t5kit import *  # noqa: F401,F403

H = 1.12          # head pivot: the top of the iron cap
AX = 0.30         # the rifle's axis above the pivot (head parts are drawn at this size, exported SC bigger)
TOP = 0.07        # the turntable's top (the pods stand on it)
SC = 1.2          # the head parts' scale (like the Gun's: the rifle reads big against its tower)
POD = (0.235 * SC, TOP * SC, 0.02 * SC)   # extra rifles (Unity head space): |x| beside the yoke, on the turntable, forward
STEP = (0.0, 0.1 * SC, 0.0)               # the next pair stands on the one before
CHEEK = 0.13              # the yoke's cheeks (the receivers fit between them)
FOOT, STEPW, LOW, UP, CAP = 0.50, 0.42, 0.34, 0.31, 0.325   # half widths: foot, step, lower / upper shaft, iron cap
TILT = math.atan2(0.03, 0.37)   # the shaft courses lean in 0.03 over their height


def face(k, hw, z0, z, t=TILT):
    # A point on face k (0 front, 1 right, 2 back, 3 left) of a leaning course that is hw wide at z0, and that face's normal.
    a = -math.pi / 2 + k * math.pi / 2
    d = hw - (z - z0) * math.tan(t)
    n = (math.cos(a) * math.cos(t), math.sin(a) * math.cos(t), math.sin(t))
    return (math.cos(a) * d, math.sin(a) * d, z), n


# ---------------------------------------------------------------- plinth (static)
def base(b):
    tier(b, FOOT, FOOT, 0.15, 0.0, 0.14, STONE2, ch=0.035, chb=0.015)        # foot
    tier(b, STEPW, STEPW, 0.12, 0.14, 0.26, STONE, ch=0.025)                 # step
    tier(b, LOW, LOW, 0.1, 0.26, 0.63, STONE, ch=0.02, taper=0.03)           # lower shaft
    tier(b, UP, UP, 0.09, 0.63, 1.0, STONE, ch=0.02, taper=0.03)             # upper shaft
    tier(b, CAP, CAP, 0.09, 1.0, H, IRON, ch=0.022, chb=0.012)              # the iron cap (cell 3)
    for k in range(4):
        a = k * math.pi / 2
        for s in (-1, 1):
            p = (math.cos(a) * (CAP + 0.001) - math.sin(a) * s * 0.16, math.sin(a) * (CAP + 0.001) + math.cos(a) * s * 0.16, 1.06)
            stud(b, p, (math.cos(a), math.sin(a), 0), r=0.02)
    for u, v in CORNERS4:
        stud(b, (u * 0.385, v * 0.385, 0.14), r=0.03)


# ---------------------------------------------------------------- turntable + yoke (turns)
def body(b):
    tier(b, 0.27, 0.27, 0.085, 0.0, TOP, IRON, ch=0.018)
    for k in range(8):
        a = (k + 0.5) * TAU / 8
        stud(b, (math.cos(a) * 0.235, math.sin(a) * 0.235, TOP), r=0.018, segs=4)
    cyl(b, 0.1, 0.1, (0, 0.02, TOP + 0.05), IRON, segs=8, ch=0.01)                # swivel post
    ring(b, 0.112, 0.07, 0.025, (0, 0.02, TOP + 0.04), BRASS, segs=8)
    box(b, (0.32, 0.19, 0.045), (0, 0.02, TOP + 0.12), IRON, ch=0.01)            # yoke foot
    for s in (-1, 1):
        box(b, (0.04, 0.17, AX - 0.14 + 0.06), (s * CHEEK, 0.02, (TOP + 0.14 + AX + 0.06) / 2), IRON, ch=0.012)
        cyl(b, 0.048, 0.035, (s * (CHEEK + 0.035), 0.0, AX - 0.03), BRASS, segs=8, axis="X", ch=0.006)
        stud(b, (s * (CHEEK + 0.02), 0.08, TOP + 0.2), (s, 0, 0), r=0.016, segs=4)


# ---------------------------------------------------------------- rifle parts (turn)
def receiver(b, l=0.52, w=0.2, h=0.18, y=0.02):
    box(b, (w, l, h), (0, y, AX), IRON, ch=0.025)
    box(b, (w * 0.7, l * 0.62, 0.035), (0, y + 0.02, AX + h / 2 + 0.017), IRON, ch=0.008)    # top rail
    cyl(b, 0.034, CHEEK * 2 - 0.035, (0, 0.0, AX - 0.03), BRASS, segs=8, axis="X", ch=0.006)  # trunnion
    box(b, (0.05, 0.08, 0.07), (0, y + 0.17, AX - h / 2 - 0.03), IRON, ch=0.01)             # trigger group
    box(b, (w * 0.8, 0.05, h * 0.8), (0, y - l / 2 - 0.01, AX), IRON, ch=0.01)               # front collar
    for s in (-1, 1):
        for k in (-1, 1):
            stud(b, (s * w / 2, y + k * 0.12, AX + 0.035), (s, 0, 0), r=0.019, segs=6)


def stock(b, y0=0.28, l=0.32, m=None):
    box(b, (0.13, l, 0.19), (0, y0 + l / 2, AX - 0.02), m or WOOD, ch=0.026)
    box(b, (0.14, 0.04, 0.21), (0, y0 + l + 0.015, AX - 0.02), IRON, ch=0.01)                # butt plate
    box(b, (0.09, l * 0.55, 0.04), (0, y0 + l * 0.55, AX + 0.09), m or WOOD, ch=0.01)        # cheek rest


def barrel(b, y0=-0.25, y1=-1.2, r=0.03, sr=0.062, sl=0.28, bands=1):
    cyl(b, sr, sl, (0, y0 - sl / 2, AX), IRON, segs=8, axis="Y", ch=0.01)                   # shroud
    for k in range(bands):
        cyl(b, sr * 1.12, 0.03, (0, y0 - 0.035 - k * 0.09, AX), BRASS, segs=8, axis="Y", ch=0.005)
    rod(b, (0, y0 - sl, AX), (0, y1 + 0.05, AX), r, IRON, segs=8)


def brake(b, y1, w=0.1, l=0.17, h=0.088, slots=2):
    # The sheet's slotted muzzle brake: a block with ports through its sides and a bore at the front.
    box(b, (w, l, h), (0, y1 + l / 2, AX), IRON, ch=0.012)
    for k in range(slots):
        y = y1 + l * (k + 0.75) / (slots + 0.5)
        for s in (-1, 1):
            box(b, (0.012, l * 0.2, h * 0.55), (s * (w / 2 - 0.004), y, AX), DARK, ch=0.0)
    pipe(b, (0, y1 + 0.02, AX), (0, y1 - 0.004, AX), min(w, h) * 0.4, min(w, h) * 0.22, IRON, segs=8)


def scope(b, y0=0.14, y1=-0.24, z=None, r=0.045, obj=0.07, lens=None, rings=True):
    z = AX + 0.19 if z is None else z
    for y in (y0 - 0.07, y1 + 0.09):  # the mounts down to the top rail
        box(b, (0.055, 0.05, z - AX - 0.11), (0, y, (z + AX + 0.11) / 2), IRON, ch=0.006)
    cyl(b, r, y0 - y1, (0, (y0 + y1) / 2, z), IRON, segs=8, axis="Y", ch=0.006)
    cyl(b, r * 1.08, 0.08, (0, y1 - 0.04, z), IRON, segs=8, axis="Y", r2=obj, ch=0.0)       # objective bell
    ring(b, obj * 1.05, obj * 0.82, 0.03, (0, y1 - 0.085, z), BRASS, segs=8, axis="Y")
    cyl(b, obj * 0.84, 0.012, (0, y1 - 0.082, z), lens or LENS, segs=8, axis="Y", ch=0.0)
    cyl(b, r * 1.15, 0.06, (0, y0 + 0.03, z), IRON, segs=8, axis="Y", ch=0.008)             # eyepiece
    if rings:
        for y in (y0 - 0.02, y1 + 0.03):
            cyl(b, r * 1.18, 0.025, (0, y, z), BRASS, segs=8, axis="Y", ch=0.004)
    cyl(b, 0.018, 0.04, (0, (y0 + y1) / 2, z + r + 0.015), BRASS, segs=6, ch=0.004)           # turrets
    cyl(b, 0.018, 0.04, (r + 0.015, (y0 + y1) / 2, z), BRASS, segs=6, axis="X", ch=0.004)


# ---------------------------------------------------------------- heads (turn)
def default(b):
    receiver(b)
    stock(b)
    barrel(b)
    brake(b, -1.2)
    scope(b)


def ap_rounds(b, x=0.068, y=0.4):
    # Three armour-piercing rounds clipped along the stock's right side.
    box(b, (0.012, 0.16, 0.05), (x, y, AX - 0.02), IRON, ch=0.0)
    for k in range(3):
        yy = y - 0.05 + k * 0.05
        cyl(b, 0.016, 0.07, (x + 0.02, yy, AX - 0.02), BRASS, segs=6, ch=0.0)
        cyl(b, 0.016, 0.03, (x + 0.02, yy, AX + 0.03), STEEL, segs=6, r2=0.004, ch=0.0)


def armour_breaker(b):
    receiver(b, l=0.56)
    stock(b, y0=0.3)
    barrel(b, y0=-0.27, y1=-1.34, r=0.036, sr=0.072, sl=0.34, bands=2)
    brake(b, -1.34, w=0.13, l=0.21, h=0.105)
    scope(b, y1=-0.28)
    ap_rounds(b)


def boss_slayer(b):
    receiver(b, l=0.58, h=0.2)
    stock(b, y0=0.31, l=0.34)
    box(b, (0.17, 0.12, 0.17), (0, 0.73, AX - 0.02), IRON, ch=0.022)                     # counterweight
    for s in (-1, 1):
        stud(b, (s * 0.085, 0.73, AX + 0.01), (s, 0, 0), r=0.022)
    barrel(b, y0=-0.3, y1=-1.55, r=0.046, sr=0.085, sl=0.42, bands=3)
    for y in (-0.8, -1.18):
        cyl(b, 0.058, 0.04, (0, y, AX), IRON, segs=8, axis="Y", ch=0.006)
    brake(b, -1.55, w=0.17, l=0.29, h=0.14, slots=3)
    scope(b, y0=0.18, y1=-0.42, z=AX + 0.21, r=0.05, obj=0.088)
    ap_rounds(b, y=0.44)


def piercing_shot(b):
    receiver(b, l=0.54)
    stock(b)
    box(b, (0.16, 0.13, 0.16), (0, -0.31, AX), IRON, ch=0.02)
    for s in (-1, 1):  # the rail barrel: two rails, a cyan glow between
        bar(b, (s * 0.045, -0.36, AX), (s * 0.045, -1.4, AX), 0.028, 0.085, IRON)
        bar(b, (s * 0.027, -0.42, AX), (s * 0.027, -1.34, AX), 0.009, 0.036, CYAN)
    for y in (-0.56, -0.86, -1.16):  # aiming coils
        ring(b, 0.1, 0.07, 0.045, (0, y, AX), CYAN, segs=8, axis="Y")
        box(b, (0.13, 0.035, 0.024), (0, y, AX - 0.055), IRON, ch=0.0)
    box(b, (0.12, 0.07, 0.12), (0, -1.43, AX), IRON, ch=0.012)
    rod(b, (0, -1.46, AX), (0, -1.66, AX), 0.042, STEEL, segs=6, r2=0.003)             # the piercing spike
    scope(b)
    ring(b, 0.068, 0.05, 0.035, (0, -0.06, AX + 0.19), CYAN, segs=8, axis="Y")


def box_mag(b, y=-0.06):
    box(b, (0.075, 0.12, 0.2), (0, y, AX - 0.17), IRON, ch=0.012, rot=(0.25, 0, 0))
    box(b, (0.08, 0.125, 0.03), (0, y + 0.025, AX - 0.27), BRASS, ch=0.004, rot=(0.25, 0, 0))


def rapid(b):
    receiver(b, l=0.5)
    stock(b, y0=0.27, l=0.28)
    barrel(b, y0=-0.24, y1=-1.06, r=0.03, sr=0.068, sl=0.46, bands=0)
    for y in (-0.34, -0.47, -0.6):  # cooling holes as rings
        cyl(b, 0.072, 0.022, (0, y, AX), DARK, segs=8, axis="Y", ch=0.0)
    cyl(b, 0.04, 0.12, (0, -1.07, AX), IRON, segs=8, axis="Y", r2=0.054, ch=0.0)      # flash hider
    box_mag(b)
    for y in (-0.1, 0.14):  # carry handle
        box(b, (0.03, 0.03, 0.09), (0, y, AX + 0.165), IRON, ch=0.0)
    box(b, (0.03, 0.28, 0.03), (0, 0.02, AX + 0.215), IRON, ch=0.0)
    scope(b, y0=0.12, y1=-0.14, z=AX + 0.29, r=0.036, obj=0.054, rings=False)


def machine_sniper(b):
    receiver(b, l=0.5)
    stock(b, y0=0.27, l=0.28)
    cyl(b, 0.074, 0.46, (0, -0.48, AX), IRON, segs=8, axis="Y", ch=0.01)               # cooling jacket
    for y in (-0.32, -0.42, -0.52, -0.62):
        cyl(b, 0.078, 0.022, (0, y, AX), BRASS, segs=8, axis="Y", ch=0.0)
    rod(b, (0, -0.71, AX), (0, -0.92, AX), 0.03, IRON, segs=8)
    cyl(b, 0.04, 0.12, (0, -0.96, AX), IRON, segs=8, axis="Y", r2=0.055, ch=0.0)
    # the side drum (behind the yoke, clear of the pods)
    cyl(b, 0.115, 0.08, (0.165, 0.33, AX), OLIVE, segs=10, axis="X", ch=0.014)
    cyl(b, 0.035, 0.1, (0.165, 0.33, AX), BRASS, segs=6, axis="X", ch=0.005)
    box(b, (0.06, 0.09, 0.06), (0.11, 0.27, AX + 0.02), IRON, ch=0.006)
    # folded bipod along the barrel's underside, on its clamp
    cyl(b, 0.05, 0.06, (0, -0.76, AX - 0.02), IRON, segs=8, axis="Y", ch=0.006)
    for s in (-1, 1):
        rod(b, (s * 0.028, -0.78, AX - 0.065), (s * 0.045, -0.93, AX - 0.095), 0.014, IRON, segs=4)
        box(b, (0.03, 0.045, 0.024), (s * 0.045, -0.95, AX - 0.1), IRON, ch=0.0)
    scope(b, y0=0.1, y1=-0.14, z=AX + 0.17, r=0.038, obj=0.056)


def mark(b):
    receiver(b)
    stock(b)
    barrel(b)
    brake(b, -1.2)
    # the big observation scope (cell 6) on a raised mount
    for y in (0.08, -0.16):
        box(b, (0.06, 0.06, 0.16), (0, y, AX + 0.2), IRON, ch=0.006)
    box(b, (0.15, 0.36, 0.04), (0, -0.04, AX + 0.29), IRON, ch=0.008)
    cyl(b, 0.085, 0.48, (0, -0.02, AX + 0.39), IRON, segs=10, axis="Y", ch=0.012)
    cyl(b, 0.09, 0.12, (0, -0.32, AX + 0.39), IRON, segs=10, axis="Y", r2=0.122, ch=0.0)
    ring(b, 0.128, 0.104, 0.04, (0, -0.385, AX + 0.39), BRASS, segs=10, axis="Y")
    cyl(b, 0.108, 0.014, (0, -0.38, AX + 0.39), LENS, segs=10, axis="Y", ch=0.0)
    for y in (0.17, -0.2):
        cyl(b, 0.094, 0.035, (0, y, AX + 0.39), BRASS, segs=10, axis="Y", ch=0.004)
    cyl(b, 0.07, 0.07, (0, 0.25, AX + 0.39), IRON, segs=8, axis="Y", ch=0.008)
    # radar dish on a mast on top of it
    rod(b, (0, 0.06, AX + 0.47), (0, 0.06, AX + 0.62), 0.016, IRON, segs=6)
    F = T(0, 0.06, AX + 0.63) @ Rx(-0.7)
    cyl(b, 0.024, 0.04, (0, 0, 0.0), IRON, segs=8, r2=0.12, ch=0.0, M=F)
    cyl(b, 0.014, 0.07, (0, 0, 0.045), BRASS, segs=6, ch=0.0, M=F)
    # the marker lamp on the left, behind the yoke
    box(b, (0.1, 0.045, 0.035), (-0.14, 0.22, AX + 0.02), IRON, ch=0.005)
    cyl(b, 0.048, 0.08, (-0.21, 0.22, AX + 0.02), IRON, segs=8, axis="X", ch=0.006)
    cyl(b, 0.04, 0.02, (-0.255, 0.22, AX + 0.02), AMBER, segs=8, axis="X", ch=0.0)
    ring(b, 0.054, 0.04, 0.016, (-0.26, 0.22, AX + 0.02), BRASS, segs=8, axis="X")


# ---------------------------------------------------------------- extra rifle pod (turns, on the turntable)
def pod(b):
    # rear-bottom-centre at the origin, pointing -Y: a block on the turntable, a short rifle barrel, a little brake.
    box(b, (0.08, 0.22, 0.1), (0, -0.11, 0.05), IRON, ch=0.015)
    box(b, (0.07, 0.1, 0.07), (0, 0.04, 0.045), WOOD, ch=0.012)
    stud(b, (0.04, -0.12, 0.05), (1, 0, 0), r=0.014, segs=4)
    stud(b, (-0.04, -0.12, 0.05), (-1, 0, 0), r=0.014, segs=4)
    rod(b, (0, -0.22, 0.05), (0, -0.58, 0.05), 0.018, IRON, segs=6)
    box(b, (0.05, 0.08, 0.045), (0, -0.6, 0.05), IRON, ch=0.006)
    for s in (-1, 1):
        box(b, (0.006, 0.02, 0.025), (s * 0.024, -0.6, 0.05), DARK, ch=0.0)


# ---------------------------------------------------------------- one piece a level (static, round the tower)
def kit2(b):   # Lv2: iron caps on the foot's corners, iron strips down the front and back (cells 3 / 7)
    c = FOOT - 0.15 / 2
    for u, v in CORNERS4:
        plate(b, 0.22, 0.12, 0.035, (u * c, v * c, 0.07), (u, v, 0), IRON, rivets=((-1, 0), (1, 0)), rv=0.018, rivet_m=IRON)
    for k in (0, 2):
        for z0, hw, z in ((0.26, LOW, 0.445), (0.63, UP, 0.815)):
            p, n = face(k, hw, z0, z)
            plate(b, 0.1, 0.37, 0.028, p, n, IRON, rivets=((0, -1), (0, 1)), rv=0.016)
        sgn = -1 if k == 0 else 1
        box(b, (0.11, 0.03, 0.12), (0, sgn * (STEPW + 0.015), 0.2), IRON, ch=0.006)


def kit3(b):   # Lv3: a brass band where the shaft meets the cap, rivets on the cap's corners
    tier(b, 0.29, 0.29, 0.085, 0.955, 0.985, BRASS, ch=0.006)
    for k in range(4):
        a = k * TAU / 4
        stud(b, (math.cos(a) * (CAP + 0.001), math.sin(a) * (CAP + 0.001), 1.06), (math.cos(a), math.sin(a), 0), r=0.022)


def ammo_case(b, M, w=0.14, l=0.3, h=0.22, m=None):
    # The olive ammo case hung on the tower's side (cell 3): body, lid seam, brass latches, carrying strap.
    m = m or OLIVE
    box(b, (w, l, h), (0, 0, 0), m, ch=0.015, M=M)
    box(b, (w + 0.008, l + 0.008, 0.02), (0, 0, h * 0.28), m, ch=0.004, M=M)
    for s in (-1, 1):
        box(b, (0.012, 0.045, 0.06), (w / 2 + 0.004, s * l * 0.28, h * 0.22), BRASS, ch=0.003, M=M)
    box(b, (w * 0.6, 0.03, 0.02), (0, 0, h / 2 + 0.01), IRON, ch=0.0, M=M)


def side_mount(b, z, l=0.34):
    # An iron shelf and strap on the right face of the upper shaft for what hangs there; its outer edge at x.
    p, n = face(1, UP, 0.63, z)
    box(b, (0.2, l, 0.03), (p[0] + 0.09, 0, z - 0.125), IRON, ch=0.006)
    for s in (-1, 1):
        bar(b, (p[0] - 0.005, s * (l / 2 - 0.03), z - 0.11), (p[0] + 0.17, s * (l / 2 - 0.03), z - 0.13), 0.025, 0.02, IRON)
    return p[0]


def kit4(b):   # Lv4: the olive ammo case hung on the right of the tower (cell 3)
    x = side_mount(b, 0.82)
    ammo_case(b, T(x + 0.075, 0, 0.82))
    for s in (-1, 1):
        box(b, (0.15, 0.025, 0.24), (x + 0.075, s * 0.1, 0.81), IRON, ch=0.0)       # straps round it


def kit5(b):   # Lv5: riveted iron plates on the lower shaft's sides and back
    for k in (1, 2, 3):
        p, n = face(k, LOW, 0.26, 0.445)
        plate(b, 0.32 if k != 2 else 0.24, 0.26, 0.028, p, n, IRON, rivets=CORNERS4, rv=0.018)


def kit6(b):   # Lv6: a sandbag nest along the left side
    for y, z, rz in ((-0.2, 0, 0.0), (0.2, 0, 0.05), (0.0, 0.13, -0.04)):
        sandbag(b, (0.19, 0.38, 0.14), M=T(-(FOOT + 0.09), y, z) @ Rz(rz))


def kit7(b):   # Lv7: the pennant on the right of the foot, its cloth square to the 3/4 camera (below the rifle's sweep)
    x, y = STEPW + 0.025, 0.235
    pennant(b, 0.92, CLOTH["Sniper"], M=T(x, y, 0.14) @ Rz(math.pi / 4))
    for z, hw, z0 in ((0.4, LOW, 0.26), (0.76, UP, 0.63)):
        bar(b, (hw - (z - z0) * math.tan(TILT) - 0.04, y, z), (x, y, z), 0.035, 0.035, IRON)


def kit8(b):   # Lv8: a brass crosshair crest on the front, brass knobs on the cap's corners
    p, n = face(0, UP, 0.63, 0.815)
    p = tuple(p[i] + n[i] * 0.028 for i in range(3))   # on Kit2's iron strip
    plate(b, 0.24, 0.24, 0.022, p, n, BRASS, rivets=CORNERS4, rv=0.014, rivet_m=IRON)
    F = T(p[0], p[1] - 0.025, p[2]) @ Rx(math.pi / 2 - TILT)
    ring(b, 0.085, 0.06, 0.012, (0, 0, 0), IRON, segs=12, M=F)
    box(b, (0.2, 0.018, 0.012), (0, 0, 0), IRON, ch=0.0, M=F)
    box(b, (0.018, 0.2, 0.012), (0, 0, 0), IRON, ch=0.0, M=F)
    cyl(b, 0.018, 0.016, (0, 0, 0.004), RED, segs=6, ch=0.0, M=F)
    for k in range(4):  # brass bosses on the cap's corners (below the turntable's sweep)
        a = k * TAU / 4 + math.pi / 4
        d = (2 * CAP - 0.09) / math.sqrt(2)
        stud(b, (math.cos(a) * d, math.sin(a) * d, 1.06), (math.cos(a), math.sin(a), 0), r=0.034, segs=8)


# ---------------------------------------------------------------- final classes (static, from Lv9; Kit4 + Kit6 make way)
def dress_a1(b):   # Boss slayer: huge AP shells racked on the right, a heavy shell crate and sandbags on the left
    x = side_mount(b, 0.82, l=0.4)
    box(b, (0.16, 0.38, 0.06), (x + 0.08, 0, 0.73), WOOD, ch=0.01)
    for k in range(4):
        y = -0.13 + k * 0.087
        cyl(b, 0.034, 0.2, (x + 0.08, y, 0.86), BRASS, segs=8, ch=0.006)
        cyl(b, 0.034, 0.09, (x + 0.08, y, 1.005), STEEL, segs=8, r2=0.008, ch=0.0)
    box(b, (0.17, 0.4, 0.03), (x + 0.08, 0, 0.9), WOOD2, ch=0.004)
    x = -(FOOT + 0.105)
    crate(b, (0.22, 0.4, 0.24), M=T(x, -0.05, 0), planks=1)
    for k in range(3):
        cyl(b, 0.038, 0.14, (x, -0.17 + k * 0.12, 0.31), BRASS, segs=8, ch=0.006)
    sandbag(b, (0.19, 0.3, 0.13), M=T(-(FOOT + 0.09), 0.33, 0) @ Rz(0.1))


def dress_a2(b):   # Piercing shot: a cyan battery on the right, a coil pylon on the left with its cable
    x = side_mount(b, 0.82)
    box(b, (0.15, 0.3, 0.24), (x + 0.075, 0, 0.83), IRON, ch=0.015)
    for k in range(3):
        box(b, (0.012, 0.24, 0.025), (x + 0.153, 0, 0.75 + k * 0.07), CYAN, ch=0.0)
    for s in (-1, 1):
        cyl(b, 0.035, 0.05, (x + 0.075, s * 0.08, 0.975), COPPER, segs=8, ch=0.006)
    x = -(FOOT + 0.105)
    tier(b, 0.11, 0.11, 0.03, 0.0, 0.1, IRON, ch=0.02, M=T(x, 0.05, 0))
    cyl(b, 0.055, 0.62, (x, 0.05, 0.41), IRON, segs=8, ch=0.0)
    coil_rings(b, 0.1, 0.16, 0.62, 5, COPPER, segs=10, M=T(x, 0.05, 0))
    cyl(b, 0.085, 0.05, (x, 0.05, 0.745), IRON, segs=8, ch=0.01)
    gem(b, 0.05, 0.12, 0.03, (x, 0.05, 0.8), CYAN, segs=6)
    rod(b, (x + 0.07, 0.05, 0.1), (-0.33, 0.04, 0.32), 0.02, IRON, segs=6)


def dress_b1(b):   # Machine sniper: two olive cases stacked on the right, belt cases and a sandbag on the left
    x = side_mount(b, 0.8)
    ammo_case(b, T(x + 0.075, 0, 0.68), h=0.17)
    ammo_case(b, T(x + 0.075, 0, 0.86), h=0.17)
    for s in (-1, 1):
        box(b, (0.15, 0.025, 0.38), (x + 0.075, s * 0.1, 0.76), IRON, ch=0.0)
    for y, z in ((-0.22, 0.0), (0.06, 0.0), (-0.08, 0.17)):
        ammo_case(b, T(-(FOOT + 0.12), y, z + 0.085) @ Rz(math.pi / 2), w=0.16, l=0.26, h=0.17)
    sandbag(b, (0.19, 0.32, 0.14), M=T(-(FOOT + 0.09), 0.34, 0) @ Rz(-0.06))


def dress_b2(b):   # Mark: a radio box with dials on the right, a marker beacon on the left
    x = side_mount(b, 0.82)
    box(b, (0.15, 0.3, 0.22), (x + 0.075, 0, 0.82), OLIVE, ch=0.015)
    for k in range(3):
        cyl(b, 0.022, 0.02, (x + 0.155, -0.09 + k * 0.09, 0.85), BRASS, segs=8, axis="X", ch=0.0)
    box(b, (0.01, 0.2, 0.05), (x + 0.153, 0, 0.765), IRON, ch=0.0)
    rod(b, (x + 0.1, 0.11, 0.93), (x + 0.1, 0.11, 1.12), 0.008, IRON, segs=4)
    ball(b, 0.016, (x + 0.1, 0.11, 1.13), BRASS)
    # the beacon: an iron post, a lamp in a cage
    x = -(FOOT + 0.105)
    tier(b, 0.11, 0.11, 0.03, 0.0, 0.09, IRON, ch=0.02, M=T(x, 0.0, 0))
    cyl(b, 0.035, 0.66, (x, 0.0, 0.42), IRON, segs=8, ch=0.0)
    cyl(b, 0.07, 0.04, (x, 0.0, 0.77), IRON, segs=8, ch=0.008)
    cyl(b, 0.055, 0.12, (x, 0.0, 0.85), AMBER, segs=8, ch=0.01)
    for k in range(4):
        a = k * TAU / 4 + math.pi / 4
        bar(b, (x + math.cos(a) * 0.065, math.sin(a) * 0.065, 0.79), (x + math.cos(a) * 0.065, math.sin(a) * 0.065, 0.93), 0.014, 0.014, IRON)
    cyl(b, 0.075, 0.035, (x, 0.0, 0.945), IRON, segs=8, ch=0.006)
    sandbag(b, (0.19, 0.3, 0.13), M=T(-(FOOT + 0.09), 0.3, 0) @ Rz(0.08))


model("SniperBase", base)
model("SniperBody", body, scale=SC)
model("SniperDefault", default, scale=SC)
model("SniperA", armour_breaker, scale=SC)
model("SniperA1", boss_slayer, scale=SC)
model("SniperA2", piercing_shot, scale=SC)
model("SniperB", rapid, scale=SC)
model("SniperB1", machine_sniper, scale=SC)
model("SniperB2", mark, scale=SC)
model("SniperBarrel", pod, scale=SC)
for n, f in ((2, kit2), (3, kit3), (4, kit4), (5, kit5), (6, kit6), (7, kit7), (8, kit8)):
    model("SniperKit%d" % n, f)
for n, f in (("A1", dress_a1), ("A2", dress_a2), ("B1", dress_b1), ("B2", dress_b2)):
    model("SniperDress" + n, f)
export_all()
layout("Sniper", head=H, muzzle=round(H + AX * SC, 3), pod=[round(v, 3) for v in POD], step=[round(v, 3) for v in STEP])
