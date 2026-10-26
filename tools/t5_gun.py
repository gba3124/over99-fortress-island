# Gun turret, art pass 5 (tools/t5kit.py; Owen's twin-gun and gatling sheets). Plinth: the tall octagonal stone pedestal
# (twin-gun sheet 1 / 7). Classes (TURRET-UPGRADE-DESIGN.md §1): A Storm (longer twin barrels, drum magazine, amber fins)
# -> A1 Typhoon (twin gatlings, top feed carousel) / A2 Hailstorm (frost-jacketed barrels, coolant tower); B Armour-piercing
# (one long AP barrel on brass rails) -> B1 Railgun (twin rails, copper coils, cyan arc) / B2 Executioner (short heavy
# barrel, big brake, armoured shield).
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/t5_gun.py   [ONLY=GunBase,GunA ...]
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from t5kit import *  # noqa: F401,F403

H = 1.10          # head pivot: the top of the plinth
AX = 0.27         # the gun's axis above the pivot (head parts are drawn at this size, exported SC bigger)
TOP = 0.16        # the turntable's top (where the receiver and the pods stand)
SC = 1.2          # the head parts' scale (twin-gun sheet: the gun reads big against its pedestal)
POD = (0.31 * SC, TOP * SC, 0.12 * SC)   # extra barrel pods (Unity head space): |x| beside the receiver, on the turntable, forward
STEP = (0.0, 0.13 * SC, 0.0)             # each further pair stands on the one before

FOOT, SHAFT, CAP = 0.50, 0.37, 0.43   # half widths


# ---------------------------------------------------------------- plinth (static)
def base(b):
    tier(b, FOOT, FOOT, 0.16, 0.0, 0.12, STONE2, ch=0.035, chb=0.015)          # foot
    tier(b, FOOT - 0.07, FOOT - 0.07, 0.14, 0.12, 0.20, STONE, ch=0.025)       # base moulding
    tier(b, SHAFT, SHAFT, 0.12, 0.20, 0.60, STONE, ch=0.02, taper=0.01)        # shaft, lower course
    tier(b, SHAFT - 0.01, SHAFT - 0.01, 0.12, 0.60, 0.98, STONE, ch=0.02)      # upper course
    tier(b, CAP, CAP, 0.13, 0.98, H, STONE, ch=0.03, chb=0.02)                 # capital
    for u, v in CORNERS4:
        stud(b, (u * (FOOT - 0.12), v * (FOOT - 0.12), 0.12), r=0.03)


# ---------------------------------------------------------------- turntable (turns)
def body(b):
    tier(b, 0.33, 0.33, 0.1, 0.0, 0.07, IRON, ch=0.018)
    tier(b, 0.26, 0.26, 0.08, 0.07, 0.11, IRON, ch=0.014)
    for k in range(8):
        a = (k + 0.5) * TAU / 8
        stud(b, (math.cos(a) * 0.3, math.sin(a) * 0.3, 0.07), r=0.022)
    box(b, (0.2, 0.26, 0.06), (0, 0.04, 0.13), IRON, ch=0.015)


# ---------------------------------------------------------------- heads (turn)
def receiver(b, l=0.62, w=0.44, h=0.26, y=0.04):
    box(b, (w, l, h), (0, y, AX), IRON, ch=0.035)
    box(b, (w * 0.72, l * 0.6, 0.08), (0, y + 0.04, AX + h / 2 + 0.035), IRON, ch=0.022)          # top cover
    box(b, (0.05, 0.1, 0.07), (0, y - l * 0.28, AX + h / 2 + 0.1), IRON, ch=0.012)                # sight
    box(b, (w * 0.78, 0.06, h * 0.7), (0, y + l / 2 + 0.02, AX), IRON, ch=0.015)                  # back plate
    for s in (-1, 1):
        stud(b, (s * w * 0.26, y + l / 2 + 0.05, AX), (0, 1, 0), r=0.024)
        box(b, (0.05, l * 0.56, h * 0.9), (s * (w / 2 + 0.02), y - 0.03, AX), IRON, ch=0.012)     # cheeks
        for k in (-1, 1):
            stud(b, (s * (w / 2 + 0.045), y - 0.03 + k * l * 0.17, AX + 0.04), (s, 0, 0), r=0.022)


def gun_tube(b, x, z, y0, y1, r=0.052, jacket=0.24, m=IRON, bands=()):
    # One barrel from y0 (the receiver's face) forward to y1: a jacket, the tube, a hollow muzzle.
    cyl(b, r * 1.35, jacket, (x, y0 - jacket / 2, z), m, axis="Y", ch=0.01)
    rod(b, (x, y0 - jacket, z), (x, y1 + 0.09, z), r, m, segs=8)
    pipe(b, (x, y1 + 0.1, z), (x, y1, z), r * 1.3, r * 0.62, m)
    for y in bands:
        cyl(b, r * 1.25, 0.03, (x, y, z), BRASS, axis="Y", ch=0.006)


def ammo_box(b, x=0.39, y=0.12):
    s = 1 if x > 0 else -1
    box(b, (0.28, 0.32, 0.03), (x, y, AX - 0.13), IRON, ch=0.008)       # shelf off the cheek
    crate(b, (0.24, 0.32, 0.26), M=T(x, y, AX - 0.115))
    box(b, (0.08, 0.12, 0.05), (x - s * 0.13, y - 0.04, AX + 0.11), BRASS, ch=0.01)  # feed into the receiver


def default(b):
    receiver(b)
    box(b, (0.36, 0.13, 0.2), (0, -0.31, AX), IRON, ch=0.025)          # barrel block
    for s in (-1, 1):
        gun_tube(b, s * 0.095, AX, -0.37, -1.15)
    ammo_box(b)


def drum(b, x, y, z, r=0.18, w=0.14):
    # The Storm's round magazine on the side (gatling sheet 4).
    cyl(b, r, w, (x, y, z), IRON, segs=12, axis="X", ch=0.02)
    for s in (-1, 1):
        ring(b, r * 1.02, r * 0.86, 0.02, (x + s * w * 0.42, y, z), BRASS, segs=12, axis="X")
    cyl(b, r * 0.3, w + 0.03, (x, y, z), BRASS, segs=6, axis="X", ch=0.01)
    for k in range(6):
        a = k * TAU / 6
        stud(b, (x + (w / 2 if x > 0 else -w / 2), y + math.cos(a) * r * 0.62, z + math.sin(a) * r * 0.62), (1 if x > 0 else -1, 0, 0), r=0.018, segs=4)
    s = 1 if x > 0 else -1
    box(b, (0.06, 0.12, 0.08), (x - s * (w / 2 + 0.03), y, z + 0.02), IRON, ch=0.01)   # neck into the receiver


def fins(b, n=4, y=0.08, l=0.34, m=None):
    for k in range(n):
        box(b, (0.022, l, 0.07), ((k - (n - 1) / 2) * 0.065, y, AX + 0.13 + 0.08 + 0.03), m or AMBER, ch=0.006)


def storm(b):
    receiver(b, l=0.68)
    box(b, (0.38, 0.15, 0.21), (0, -0.34, AX), IRON, ch=0.025)
    for s in (-1, 1):
        gun_tube(b, s * 0.1, AX, -0.41, -1.3, jacket=0.32, bands=(-0.82, -1.02))
    drum(b, 0.34, 0.12, AX)
    fins(b)


def gatling(b, x, z, y0, y1, n=6, rr=0.075, r=0.026):
    # A rotary cluster: n barrels round a spindle, a front and rear brass band, a hollow-ended spindle cap.
    cyl(b, rr + r + 0.03, 0.15, (x, y0 - 0.075, z), IRON, segs=10, axis="Y", ch=0.015)     # rear housing
    for k in range(n):
        a = k * TAU / n + math.pi / n
        px, pz = x + math.cos(a) * rr, z + math.sin(a) * rr
        pipe(b, (px, y0 - 0.15, pz), (px, y1, pz), r, r * 0.55, IRON, segs=6, depth=0.05)
    rod(b, (x, y0 - 0.15, z), (x, y1 + 0.12, z), rr * 0.45, IRON, segs=6)
    for y in (y0 - 0.22, y1 + 0.17):
        ring(b, rr + r + 0.02, rr * 0.4, 0.045, (x, y, z), BRASS, segs=10, axis="Y")


def typhoon(b):
    receiver(b, l=0.68, w=0.5)
    for s in (-1, 1):
        box(b, (0.17, 0.17, 0.22), (s * 0.15, -0.36, AX), IRON, ch=0.02)
        gatling(b, s * 0.16, AX, -0.44, -1.14)
    drum(b, 0.37, 0.14, AX, r=0.2)
    # the feed carousel on top
    cyl(b, 0.21, 0.08, (0, 0.1, AX + 0.29), IRON, segs=10, ch=0.015)
    ring(b, 0.22, 0.13, 0.03, (0, 0.1, AX + 0.33), BRASS, segs=10)
    for k in range(5):
        a = k * TAU / 5
        box(b, (0.05, 0.05, 0.06), (math.cos(a) * 0.17, 0.1 + math.sin(a) * 0.17, AX + 0.35), BRASS, ch=0.01)
    cyl(b, 0.05, 0.08, (0, 0.1, AX + 0.38), IRON, segs=6, ch=0.01)
    bar(b, (0.3, 0.14, AX + 0.18), (0.15, 0.12, AX + 0.31), 0.06, 0.03, BRASS)    # belt up to it


def hailstorm(b):
    receiver(b, l=0.66)
    box(b, (0.36, 0.15, 0.21), (0, -0.33, AX), IRON, ch=0.025)
    for s in (-1, 1):
        x = s * 0.1
        cyl(b, 0.08, 0.46, (x, -0.64, AX), ICE, segs=8, axis="Y", ch=0.012)          # cooling jacket
        for y in (-0.44, -0.64, -0.84):
            cyl(b, 0.088, 0.026, (x, y, AX), IRON, segs=8, axis="Y", ch=0.005)
        rod(b, (x, -0.87, AX), (x, -1.2, AX), 0.05, IRON)
        pipe(b, (x, -1.18, AX), (x, -1.29, AX), 0.068, 0.032, IRON)
        ring(b, 0.09, 0.055, 0.032, (x, -1.22, AX), ICE, segs=8, axis="Y")                # frost ring
        # coolant canister on each side, behind the pods
        cyl(b, 0.08, 0.28, (s * 0.31, 0.12, AX - 0.02), GLASS, segs=8, axis="Y", ch=0.015)
        for y in (0.0, 0.24):
            cyl(b, 0.088, 0.03, (s * 0.31, y, AX - 0.02), IRON, segs=8, axis="Y", ch=0.006)
        box(b, (0.08, 0.08, 0.05), (s * 0.255, 0.12, AX - 0.02), IRON, ch=0.01)
    # the tall coolant tower on top: narrow and high
    cyl(b, 0.09, 0.32, (0, 0.1, AX + 0.36), ICE, segs=8, ch=0.015)
    for z in (0.22, 0.51):
        cyl(b, 0.1, 0.04, (0, 0.1, AX + z), IRON, segs=8, ch=0.008)
    gem(b, 0.06, 0.1, 0.02, (0, 0.1, AX + 0.54), ICE, segs=6)


def ap_barrel(b, x=0.0, y1=-1.5, r=0.062):
    box(b, (0.26, 0.17, 0.22), (x, -0.35, AX), IRON, ch=0.025)
    cyl(b, r * 1.4, 0.32, (x, -0.6, AX), IRON, axis="Y", ch=0.012)
    rod(b, (x, -0.76, AX), (x, y1 + 0.11, AX), r, IRON, segs=8)
    pipe(b, (x, y1 + 0.13, AX), (x, y1, AX), r * 1.35, r * 0.55, IRON)


def ap_rails(b, y0=-0.44, y1=-1.32):
    for s in (-1, 1):
        bar(b, (s * 0.11, y0, AX - 0.065), (s * 0.11, y1, AX - 0.065), 0.032, 0.032, BRASS)
    for y in (y0 - 0.12, (y0 + y1) / 2, y1 + 0.06):
        box(b, (0.26, 0.035, 0.05), (0, y, AX - 0.065), IRON, ch=0.008)


def heavy_breech(b):
    box(b, (0.38, 0.3, 0.32), (0, 0.36, AX + 0.02), IRON, ch=0.035)
    for s in (-1, 1):
        for z in (0.06, -0.04):
            stud(b, (s * 0.11, 0.51, AX + z), (0, 1, 0), r=0.026)


def armour_piercing(b):
    receiver(b, l=0.56, w=0.4)
    heavy_breech(b)
    ap_barrel(b)
    ap_rails(b)
    ammo_box(b, x=0.36, y=0.14)


def railgun(b):
    receiver(b, l=0.56, w=0.4)
    heavy_breech(b)
    box(b, (0.32, 0.17, 0.26), (0, -0.35, AX), IRON, ch=0.025)
    for s in (-1, 1):  # the rails, a cyan glow along their inner faces
        bar(b, (s * 0.08, -0.43, AX), (s * 0.08, -1.56, AX), 0.055, 0.13, IRON)
        bar(b, (s * 0.049, -0.54, AX), (s * 0.049, -1.52, AX), 0.012, 0.065, CYAN)
    for y in (-0.68, -0.94, -1.2):  # copper coils round them
        ring(b, 0.18, 0.13, 0.075, (0, y, AX), COPPER, segs=10, axis="Y")
    box(b, (0.24, 0.05, 0.22), (0, -1.57, AX), IRON, ch=0.012)
    for s in (-1, 1):  # capacitor cans either side of the breech
        cyl(b, 0.075, 0.22, (s * 0.27, 0.34, AX + 0.02), COPPER, segs=8, ch=0.012)
        cyl(b, 0.055, 0.03, (s * 0.27, 0.34, AX + 0.145), CYAN, segs=8, ch=0.005)


def executioner(b):
    receiver(b, l=0.56, w=0.46, h=0.28)
    box(b, (0.5, 0.32, 0.22), (0, 0.36, AX - 0.02), IRON, ch=0.035)             # counterweight
    for s in (-1, 1):
        for z in (-0.06, 0.04):
            stud(b, (s * 0.16, 0.52, AX + z), (0, 1, 0), r=0.026)
    cyl(b, 0.13, 0.3, (0, -0.47, AX), IRON, segs=8, axis="Y", ch=0.02)
    rod(b, (0, -0.62, AX), (0, -0.84, AX), 0.1, IRON, segs=8)
    box(b, (0.28, 0.22, 0.24), (0, -0.95, AX), IRON, ch=0.03)                  # the brake
    for s in (-1, 1):
        box(b, (0.03, 0.07, 0.13), (s * 0.14, -0.95, AX), DARK, ch=0.0)
    pipe(b, (0, -1.04, AX), (0, -1.09, AX), 0.11, 0.065, IRON)
    # the armoured shield in front (gatling sheet 5), its port round the barrel
    for s in (-1, 1):
        box(b, (0.25, 0.06, 0.54), (s * 0.235, -0.36, AX + 0.04), IRON, ch=0.03)
    box(b, (0.22, 0.06, 0.15), (0, -0.36, AX + 0.235), IRON, ch=0.02)
    box(b, (0.22, 0.06, 0.13), (0, -0.36, AX - 0.145), IRON, ch=0.02)
    for u, v in CORNERS4:
        stud(b, (u * 0.29, -0.395, AX + 0.04 + v * 0.2), (0, -1, 0), r=0.03, segs=6)


# ---------------------------------------------------------------- extra barrel pod (turns, on the turntable)
def pod(b):
    # rear-bottom-centre at the origin, pointing -Y: a block that stands on the turntable and a short barrel.
    box(b, (0.1, 0.24, 0.12), (0, -0.12, 0.06), IRON, ch=0.02)
    stud(b, (0.05, -0.12, 0.08), (1, 0, 0), r=0.018, segs=4)
    stud(b, (-0.05, -0.12, 0.08), (-1, 0, 0), r=0.018, segs=4)
    rod(b, (0, -0.24, 0.065), (0, -0.56, 0.065), 0.032, IRON)
    pipe(b, (0, -0.54, 0.065), (0, -0.62, 0.065), 0.042, 0.02, IRON)


# ---------------------------------------------------------------- one piece a level (static, round the plinth)
def kit2(b):   # Lv2: iron caps on the foot's corners, an iron band where the shaft starts
    c = FOOT - 0.16 / 2
    for u, v in CORNERS4:
        plate(b, 0.24, 0.12, 0.035, (u * c, v * c, 0.062), (u, v, 0), IRON, rivets=((-1, 0), (1, 0)), rv=0.02, rivet_m=IRON)
    tier(b, SHAFT + 0.015, SHAFT + 0.015, 0.125, 0.2, 0.25, IRON, ch=0.012)


def kit3(b):   # Lv3: a brass band round the capital, a rivet on each of its faces
    tier(b, CAP + 0.008, CAP + 0.008, 0.132, 1.025, 1.055, BRASS, ch=0.008)
    for k in range(8):
        a = k * TAU / 8
        hw = CAP + 0.008
        d = hw if k % 2 == 0 else (2 * hw - 0.132) / math.sqrt(2)
        stud(b, (math.cos(a) * d, math.sin(a) * d, 1.04), (math.cos(a), math.sin(a), 0), r=0.02)


def kit4(b):   # Lv4: ammo crates against the back of the foot
    crate(b, (0.44, 0.24, 0.24), M=T(0.04, FOOT + 0.12, 0))
    crate(b, (0.22, 0.17, 0.15), M=T(-0.06, FOOT + 0.11, 0.24) @ Rz(0.15), planks=1)


def kit5(b):   # Lv5: riveted iron plates on the shaft's four faces
    for k in range(4):
        a = k * TAU / 4
        n = (math.cos(a), math.sin(a), 0)
        plate(b, 0.34, 0.26, 0.03, (n[0] * (SHAFT - 0.002), n[1] * (SHAFT - 0.002), 0.4), n, IRON, rivets=CORNERS4, rv=0.02)


def kit6(b):   # Lv6: sandbags along the left side
    for y, z, rz in ((-0.2, 0, 0.0), (0.2, 0, 0.05), (0.0, 0.13, -0.04)):
        sandbag(b, (0.2, 0.38, 0.14), M=T(-(FOOT + 0.1), y, z) @ Rz(rz))


def kit7(b):   # Lv7: the turret's pennant on the back left of the foot (below the gun's sweep)
    c = SHAFT - 0.06 + 0.1
    pennant(b, 1.0, CLOTH["Gun"], M=T(-c, c, 0.12) @ Rz(math.pi / 4))   # back left, the cloth along the back: inside the lamps
    for z in (0.34, 0.72):
        bar(b, (-(SHAFT - 0.07), SHAFT - 0.07, z), (-c, c, z), 0.04, 0.04, IRON)


def kit8(b):   # Lv8: the brass crest on the front, brass knobs on the capital's corners
    plate(b, 0.24, 0.22, 0.025, (0, -(SHAFT - 0.01), 0.8), (0, -1, 0), BRASS, rivets=CORNERS4, rv=0.016, rivet_m=IRON)
    for s in (-1, 1):  # two crossed rounds on it
        F = T(0, -(SHAFT + 0.03), 0.8) @ Ry(s * 0.6)
        rod(b, (0, 0, -0.08), (0, 0, 0.05), 0.022, IRON, M=F)
        cyl(b, 0.022, 0.04, (0, 0, 0.07), BRASS, r2=0.004, M=F)
    for u, v in CORNERS4:
        ball(b, 0.035, (u * (CAP - 0.08), v * (CAP - 0.08), H + 0.03), BRASS)


# ---------------------------------------------------------------- final classes (static, from Lv9; Kit4 + Kit6 make way)
def dress_a1(b):   # Typhoon: a long ammo bin at the back, belt boxes stacked on the left, a belt up the plinth
    crate(b, (0.62, 0.24, 0.28), M=T(0, FOOT + 0.12, 0))
    crate(b, (0.28, 0.18, 0.2), M=T(-0.12, FOOT + 0.11, 0.28), planks=1)
    for i, y in enumerate((-0.17, 0.15)):
        crate(b, (0.22, 0.28, 0.22), M=T(-(FOOT + 0.11), y, 0) @ Rz(0.04 * (2 * i - 1)), planks=1)
    crate(b, (0.18, 0.22, 0.16), M=T(-(FOOT + 0.11), -0.02, 0.22) @ Rz(0.1), planks=1)
    for k in range(8):
        box(b, (0.08, 0.03, 0.06), (-(SHAFT + 0.02), -0.12 + k * 0.025, 0.24 + k * 0.095), BRASS, ch=0.008)


def dress_a2(b):   # Hailstorm: two coolant towers on the left, frost crystals at the foot
    for y in (-0.24, 0.24):
        x = -(FOOT + 0.13)
        cyl(b, 0.12, 0.66, (x, y, 0.35), GLASS, segs=8, ch=0.02)
        for z in (0.04, 0.35, 0.66):
            cyl(b, 0.13, 0.05, (x, y, z), IRON, segs=8, ch=0.01)
        rod(b, (x, y, 0.52), (-(SHAFT - 0.02), y * 0.6, 0.52), 0.025, IRON, segs=6)
        cyl(b, 0.05, 0.06, (x, y, 0.72), ICE, segs=6, ch=0.01)
    for x, y, s in ((0.28, FOOT + 0.08, 1.0), (-0.2, FOOT + 0.1, 0.8), (FOOT + 0.08, -0.1, 0.9), (FOOT + 0.06, 0.28, 0.7)):
        gem(b, 0.06 * s, 0.22 * s, 0.03, (x, y, 0.02), ICE, segs=5)


def dress_b1(b):   # Railgun: two capacitor pylons (copper coils, cyan caps) with cables into the plinth
    for x, y in ((-(FOOT + 0.13), 0.0), (0.0, FOOT + 0.14)):
        tier(b, 0.1, 0.1, 0.03, 0.0, 0.1, IRON, ch=0.02, M=T(x, y, 0))
        cyl(b, 0.06, 0.62, (x, y, 0.41), IRON, segs=8, ch=0.0)
        coil_rings(b, 0.1, 0.16, 0.62, 5, COPPER, segs=10, M=T(x, y, 0))
        cyl(b, 0.09, 0.05, (x, y, 0.76), IRON, segs=8, ch=0.01)
        gem(b, 0.055, 0.1, 0.03, (x, y, 0.81), CYAN, segs=6)
        rod(b, (x * 0.85, y * 0.85, 0.08), (x * 0.6, y * 0.6, 0.26), 0.02, IRON, segs=6)


def dress_b2(b):   # Executioner: heavy shells racked at the back, a sandbag wall on the left
    y = FOOT + 0.1
    box(b, (0.62, 0.18, 0.06), (0, y, 0.03), WOOD, ch=0.012)
    for k in range(5):
        x = -0.24 + k * 0.12
        cyl(b, 0.048, 0.24, (x, y, 0.18), BRASS, segs=8, ch=0.008)
        cyl(b, 0.048, 0.1, (x, y, 0.35), IRON, segs=8, r2=0.015, ch=0.0)
    box(b, (0.64, 0.04, 0.05), (0, y + 0.07, 0.22), WOOD2, ch=0.008)
    for x in (-(FOOT + 0.1), FOOT + 0.1):  # sandbag walls either side
        for y, z, rz in ((-0.19, 0, 0.0), (0.19, 0, 0.04), (0.0, 0.13, -0.03)):
            sandbag(b, (0.19, 0.36, 0.14), M=T(x, y, z) @ Rz(rz))


model("GunBase", base)
model("GunBody", body, scale=SC)
model("GunDefault", default, scale=SC)
model("GunA", storm, scale=SC)
model("GunA1", typhoon, scale=SC)
model("GunA2", hailstorm, scale=SC)
model("GunB", armour_piercing, scale=SC)
model("GunB1", railgun, scale=SC)
model("GunB2", executioner, scale=SC)
model("GunBarrel", pod, scale=SC)
for n, f in ((2, kit2), (3, kit3), (4, kit4), (5, kit5), (6, kit6), (7, kit7), (8, kit8)):
    model("GunKit%d" % n, f)
for n, f in (("A1", dress_a1), ("A2", dress_a2), ("B1", dress_b1), ("B2", dress_b2)):
    model("GunDress" + n, f)
export_all()
layout("Gun", head=H, muzzle=round(H + AX * SC, 3), pod=POD, step=STEP)
