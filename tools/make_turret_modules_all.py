# Modular turret kit for every tower: one shared body + seven top modules (Default, A, A1, A2, B, B1, B2).
# Each module follows its mechanic: more barrels = more simultaneous shots, long/armoured = range/piercing, tanks/fins = fire,
# crystals/coils = ice/electric. Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_turret_modules_all.py
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bkit import Builder, M, export, mat, tube, torus

dark = mat("V3Dark", (0.045, 0.055, 0.065)); iron = mat("V3Iron", (0.12, 0.14, 0.17))
steel = mat("V3Steel", (0.52, 0.57, 0.62)); brass = mat("V3Brass", (0.80, 0.55, 0.17), (0.22, 0.10, 0.02))
rub = mat("V3Rubber", (0.025, 0.025, 0.03)); glass = mat("V3Glass", (0.30, 0.62, 0.72), (0.15, 0.45, 0.6))
teal = mat("V3Teal", (0.06, 0.28, 0.32), (0.02, 0.10, 0.12)); red = mat("V3Red", (0.56, 0.12, 0.07), (0.18, 0.02, 0.01))
blue = mat("V3Blue", (0.10, 0.23, 0.42), (0.03, 0.08, 0.18)); violet = mat("V3Violet", (0.27, 0.16, 0.38), (0.08, 0.04, 0.12))
copper = mat("V3Copper", (0.64, 0.30, 0.12), (0.2, 0.06, 0.02)); orange = mat("V3Orange", (0.66, 0.23, 0.06), (0.2, 0.05, 0.01))
green = mat("V3Green", (0.12, 0.28, 0.17), (0.02, 0.1, 0.05)); cyan = mat("V3Cyan", (0.10, 0.36, 0.43), (0.05, 0.3, 0.35))
mint = mat("V3Mint", (0.14, 0.32, 0.21), (0.05, 0.2, 0.1)); amber = mat("V3Amber", (1.0, 0.62, 0.16), (0.85, 0.4, 0.03))
ice = mat("V3Ice", (0.55, 0.9, 1.0), (0.2, 0.7, 0.95))
out, TAU = [], 2 * math.pi


def ring(r, n, z, phase=0.0):
    return [(r * math.cos(phase + i * TAU / n), r * math.sin(phase + i * TAU / n), z) for i in range(n)]


def studs(b, r, n, z, m=brass, size=0.05):
    for p in ring(r, n, z): b.box((size, size, size), p, m)


def tubes(b, count, r, y0, y1, thick, z0, m=steel, muzzle=brass):
    for i in range(count):
        a = i * TAU / max(1, count)
        x, z = r * math.cos(a), z0 + r * math.sin(a)
        b.beam((x, y0, z), (x, y1, z), thick, m, segs=10)


def collar(b, z, m=brass, r=0.22):
    b.cyl(r, 0.16, (0, -0.36, z), m, segs=16, axis="Y")


B = {}


def body(name, mats, fn):
    b = Builder(name, mats); fn(b); B[name] = b.finish()


def module(name, mats, fn):
    b = Builder(name, mats); fn(b); B[name] = b.finish()


# ============================== CANNON ==============================
def cannon_body(b):
    b.box((0.5, 1.0, 0.14), (0, 0.1, 0.14), dark)
    for s in (-1, 1):
        b.box((0.12, 0.9, 0.3), (s * 0.3, 0.06, 0.32), red)
        b.box((0.13, 0.05, 0.32), (s * 0.38, 0.06, 0.32), brass)
        b.cyl(0.27, 0.12, (s * 0.42, -0.2, 0.28), red, segs=26, axis="X")
        b.cyl(0.1, 0.14, (s * 0.42, -0.2, 0.28), brass, segs=14, axis="X")
        for k in range(6):
            a = k * TAU / 6; b.box((0.06, 0.36, 0.06), (s * 0.42, -0.2 + 0.19 * math.cos(a), 0.28 + 0.19 * math.sin(a)), dark, rot=(a, 0, 0))
    b.box((0.34, 0.2, 0.2), (0, 0.48, 0.34), dark); b.beam((0, 0.48, 0.32), (0, 0.82, 0.12), 0.07, dark, segs=8)
    b.cyl(0.2, 0.2, (0, 0.06, 0.4), steel, segs=16, axis="X")
    b.box((0.32, 0.08, 0.08), (0, 0.03, 0.72), steel)
    for s in (-1, 1): b.box((0.05, 0.06, 0.16), (s * 0.1, 0.03, 0.8), brass)
body("CannonBody", [iron, dark, steel, brass, red], cannon_body)

def cannon_mod(name, count, length, thick, z, col, wide=0.2, extra=None):
    def fn(b):
        collar(b, z); b.box((0.34, 0.2, 0.22), (0, -0.48, z), col)
        if count == 1:
            b.cyl(thick, length, (0, -0.48 - length / 2, z), steel, segs=20, axis="Y")
            b.cyl(thick * 1.1, 0.14, (0, -0.48 - length, z), iron, segs=20, axis="Y")
            b.cyl(thick * 0.55, 0.03, (0, -0.48 - length - 0.07, z), dark, segs=14, axis="Y")
        else:
            for i in range(count):
                off = (i - (count - 1) / 2) * (wide * 2 / max(1, count - 1))
                b.cyl(thick * 0.7, length, (off, -0.48 - length / 2, z), steel, segs=12, axis="Y")
                b.cyl(thick * 0.78, 0.1, (off, -0.48 - length, z), brass, segs=12, axis="Y")
        for k in range(3): b.cyl(0.24, 0.04, (0, -0.8 - k * length / 4, z), brass, segs=20, axis="Y")
        if extra: extra(b, z)
    module(name, [iron, dark, steel, brass, col, amber], fn)
cannon_mod("CannonDefault", 1, 1.1, 0.19, 0.56, red)
cannon_mod("CannonA", 3, 0.9, 0.15, 0.56, red)
cannon_mod("CannonA1", 5, 0.7, 0.12, 0.56, red)
cannon_mod("CannonA2", 3, 0.9, 0.14, 0.56, orange, extra=lambda b, z: (b.cyl(0.2, 0.3, (0.36, -0.35, z), orange, segs=12, axis="Y"), b.ico(0.08, (0, -1.1, z), amber, subdiv=1)))
cannon_mod("CannonB", 1, 1.5, 0.27, 0.58, red)
cannon_mod("CannonB1", 1, 1.8, 0.31, 0.58, red)
def b2x(b, z):
    b.cyl(0.36, 0.22, (0, -1.45, z), iron, segs=20, axis="Y")
    for k in range(4): b.box((0.5, 0.04, 0.04), (0, -1.55 - k * 0.04, z - 0.1 + k * 0.08), dark)
    b.box((0.26, 0.16, 0.18), (0, -0.75, z + 0.2), red)
cannon_mod("CannonB2", 1, 1.1, 0.34, 0.58, red, extra=b2x)

# ============================== HARPOON ==============================
def harpoon_body(b):
    b.box((0.34, 1.5, 0.18), (0, -0.25, 0.26), dark)
    b.box((0.5, 0.25, 0.24), (0, 0.46, 0.3), blue)
    b.box((0.58, 0.26, 0.26), (0, -0.8, 0.32), iron)
    for s in (-1, 1): tube(b, [(s * 0.2, -0.82, 0.34), (s * 0.65, -0.72, 0.3), (s * 1.0, -0.45, 0.26)], 0.075, blue, segs=10)
    b.box((0.26, 0.2, 0.24), (0, 0.36, 0.3), iron); b.cyl(0.15, 0.2, (0.2, 0.36, 0.3), brass, segs=16, axis="X")
    b.box((0.3, 0.08, 0.08), (0, -0.12, 0.62), brass)
body("HarpoonBody", [iron, dark, steel, brass, blue], harpoon_body)

def spear(b, count, length, z, col=steel, head_col=steel):
    for i in range(count):
        off = 0 if count == 1 else (i - (count - 1) / 2) * 0.12
        b.box((0.08, 0.5, 0.08), (off, -0.5, z), dark)
        b.beam((0, -0.6, z), (off, -0.6 - length, z), 0.035, col, segs=8)
        b.beam((off, -0.6 - length, z), (off, -0.6 - length - 0.3, z), 0.09, head_col, segs=6, r2=0.0)
        for s in (-1, 1): b.beam((off, -0.6 - length - 0.05, z), (off + s * 0.12, -0.55 - length, z), 0.018, head_col, segs=4)

def harpoon_mod(name, count, length, col, extra=None):
    def fn(b):
        collar(b, 0.42); b.box((0.36, 0.2, 0.2), (0, -0.5, 0.42), col)
        spear(b, count, length, 0.42, steel, col)
        if extra: extra(b)
    module(name, [iron, dark, steel, brass, col, red, amber, ice], fn)
harpoon_mod("HarpoonDefault", 1, 1.0, steel)
harpoon_mod("HarpoonA", 3, 0.9, blue)
harpoon_mod("HarpoonA1", 5, 0.8, blue)
harpoon_mod("HarpoonA2", 3, 0.9, steel, extra=lambda b: [b.cyl(0.09, 0.16, (0.0, -1.3, 0.42), brass, segs=10) for _ in range(1)])
harpoon_mod("HarpoonB", 1, 1.2, ice)
def hb1(b):
    for z in (0.3, 0.4, 0.5, 0.6): b.cyl(0.18, 0.05, (0, -0.95, z), ice, segs=14, axis="Y")
    b.ico(0.12, (0, -1.1, 0.3), ice, subdiv=1)
harpoon_mod("HarpoonB1", 1, 1.4, ice, extra=hb1)
def hb2(b):
    b.cyl(0.22, 0.26, (0, -1.95, 0.42), red, segs=16, axis="Y")
    b.ico(0.09, (0, -1.9, 0.42), amber, subdiv=1)
harpoon_mod("HarpoonB2", 1, 1.0, red, extra=hb2)

# ============================== MORTAR ==============================
def mortar_body(b):
    b.cyl(0.55, 0.12, (0, 0, 0.06), iron, segs=22); b.cyl(0.46, 0.1, (0, 0, 0.17), violet, segs=22)
    studs(b, 0.5, 10, 0.13, brass, 0.055)
    for s in (-1, 1):
        b.box((0.12, 0.4, 0.36), (s * 0.28, 0.02, 0.34), violet)
        b.cyl(0.07, 0.42, (s * 0.16, -0.1, 0.42), copper, segs=10, axis="Y")
    b.cyl(0.12, 0.5, (0, 0.02, 0.36), steel, segs=14, axis="X")
    b.box((0.34, 0.12, 0.08), (0, 0.1, 0.55), steel)
body("MortarBody", [iron, dark, steel, brass, violet, copper], mortar_body)

def mortar_mod(name, count, r, tilt, col, extra=None):
    def fn(b):
        collar(b, 0.3)
        for i in range(count):
            off = 0 if count == 1 else (i - (count - 1) / 2) * 0.28
            b.beam((off, 0, 0.3), (off, -0.34, 1.04 + (abs(off) * 0.1)), r / max(1, count) * (1.6 if count > 1 else 1), col, segs=18, r2=(r * 0.8) / max(1, count))
            b.cyl(r * 0.75 / max(1, count), 0.1, (off, -0.32, 1.02), brass, segs=16, axis="Y")
        if extra: extra(b)
    module(name, [iron, dark, steel, brass, violet, col, copper, amber], fn)
mortar_mod("MortarDefault", 1, 0.42, 0, violet)
mortar_mod("MortarA", 1, 0.44, 0, orange, extra=lambda b: b.cyl(0.16, 0.3, (0.28, -0.3, 0.5), orange, segs=12, axis="Y"))
mortar_mod("MortarA1", 1, 0.5, 0, orange, extra=lambda b: (b.cyl(0.22, 0.42, (0.3, -0.2, 0.5), orange, segs=12, axis="Y"), b.ico(0.07, (0, -0.36, 1.1), amber, subdiv=1)))
mortar_mod("MortarA2", 2, 0.34, 0, orange)
mortar_mod("MortarB", 3, 0.34, 0, violet)
mortar_mod("MortarB1", 4, 0.3, 0, violet)
mortar_mod("MortarB2", 2, 0.52, 0, violet)

# ============================== TESLA ==============================
def tesla_body(b):
    b.cyl(0.42, 0.16, (0, 0, 0.09), iron, segs=20); b.cyl(0.36, 0.1, (0, 0, 0.21), steel, segs=20)
    studs(b, 0.34, 8, 0.16, brass, 0.06); b.cyl(0.15, 0.5, (0, 0, 0.45), copper, segs=16)
    b.cyl(0.2, 0.08, (0, 0, 0.7), brass, segs=16)
body("TeslaBody", [iron, dark, steel, brass, copper], tesla_body)

def tesla_mod(name, coils, prongs, col, extra=None):
    def fn(b):
        collar(b, 0.7, brass, 0.24)
        b.cyl(0.16, 0.5, (0, 0, 0.85), copper, segs=16)
        for k in range(coils): torus(b, (0, 0, 0.6 + k * 0.11), (0, 0, 1), 0.26, 0.045, col, segs=20, tsegs=6)
        b.cyl(0.2, 0.08, (0, 0, 1.18), brass, segs=16)
        for k in range(prongs):
            a = k * TAU / max(1, prongs); b.box((0.06, 0.06, 0.24), (0.2 * math.cos(a), 0.2 * math.sin(a), 1.32), steel, rot=(0, 0, -a))
        b.ico(0.2, (0, 0, 1.42), col, subdiv=2)
        if extra: extra(b)
    module(name, [iron, dark, steel, brass, copper, col, amber, ice], fn)
tesla_mod("TeslaDefault", 3, 3, ice)
tesla_mod("TeslaA", 3, 6, ice)
tesla_mod("TeslaA1", 4, 8, ice)
tesla_mod("TeslaA2", 3, 5, cyan, extra=lambda b: b.cyl(0.12, 0.3, (0.3, -0.2, 0.8), cyan, segs=12, axis="Y"))
tesla_mod("TeslaB", 5, 3, amber)
tesla_mod("TeslaB1", 6, 4, amber)
tesla_mod("TeslaB2", 4, 6, amber, extra=lambda b: b.cyl(0.3, 0.06, (0, 0, 0.75), amber, segs=20, axis="Y"))

# ============================== FLAME ==============================
def flame_body(b):
    b.box((0.6, 0.5, 0.16), (0, 0.06, 0.1), iron); studs(b, 0.22, 6, 0.16, brass, 0.05)
    for x in (-0.22, 0.22):
        b.cyl(0.18, 0.46, (x, 0.2, 0.4), red, segs=18, axis="Y"); b.ico(0.18, (x, -0.02, 0.4), red, subdiv=2); b.ico(0.18, (x, 0.42, 0.4), red, subdiv=2)
        for y in (-0.05, 0.3): b.cyl(0.19, 0.05, (x, y, 0.4), brass, segs=18, axis="Y")
    b.box((0.28, 0.2, 0.22), (0, 0.46, 0.36), iron)
body("FlameBody", [iron, dark, steel, brass, orange, red], flame_body)

def flame_mod(name, width, col, extra=None):
    def fn(b):
        collar(b, 0.44)
        b.beam((0, -0.42, 0.44), (0, -0.95, 0.46), 0.1, steel, segs=10)
        b.beam((0, -0.95, 0.46), (0, -1.2, 0.48), 0.12, steel, segs=10, r2=width)
        b.cyl(width * 0.5, 0.04, (0, -1.22, 0.48), dark, segs=12, axis="Y")
        for y in (-0.85, -1.08): b.cyl(0.13, 0.05, (0, y, 0.47), brass, segs=14, axis="Y")
        b.ico(0.05, (0, -1.15, 0.62), amber, subdiv=1)
        if extra: extra(b)
    module(name, [iron, dark, steel, brass, orange, red, amber, ice], fn)
flame_mod("FlameDefault", 0.16, orange)
flame_mod("FlameA", 0.2, orange, extra=lambda b: b.cyl(0.18, 0.28, (0.3, -0.4, 0.55), orange, segs=12, axis="Y"))
flame_mod("FlameA1", 0.22, orange, extra=lambda b: (b.cyl(0.2, 0.34, (0.32, -0.4, 0.55), orange, segs=12, axis="Y"), b.ico(0.06, (0, -1.3, 0.6), amber, subdiv=1)))
flame_mod("FlameA2", 0.16, orange, extra=lambda b: b.beam((0, -1.2, 0.48), (0, -1.6, 0.48), 0.05, steel, segs=8))
flame_mod("FlameB", 0.34, orange)
flame_mod("FlameB1", 0.42, orange)
flame_mod("FlameB2", 0.28, cyan, extra=lambda b: b.cyl(0.16, 0.1, (0, -1.3, 0.48), ice, segs=12, axis="Y"))

# ============================== SNIPER ==============================
def sniper_body(b):
    b.box((0.4, 0.72, 0.22), (0, 0.22, 0.22), green); b.box((0.2, 0.24, 0.16), (0, 0.56, 0.2), rub)
    b.box((0.28, 0.56, 0.2), (0, -0.2, 0.34), dark); b.beam((0, -0.05, 0.55), (0, -0.7, 0.55), 0.1, iron, segs=12)
    b.cyl(0.1, 0.04, (0, -0.72, 0.55), glass, segs=12, axis="Y")
    b.box((0.08, 0.2, 0.08), (0.18, 0, 0.4), steel, rot=(0, 0.6, 0))
body("SniperBody", [iron, dark, steel, brass, green, rub, glass], sniper_body)

def sniper_mod(name, length, thick, col, count=1, scope=False, extra=None):
    def fn(b):
        collar(b, 0.38)
        for i in range(count):
            off = 0 if count == 1 else (i - (count - 1) / 2) * 0.1
            b.beam((off, -0.42, 0.38), (off, -0.42 - length, 0.38), thick, steel, segs=12)
            b.cyl(thick * 1.4, 0.1, (off, -0.42 - length, 0.38), iron, segs=12, axis="Y")
        if scope: b.beam((0, -0.45, 0.58), (0, -0.95, 0.58), 0.09, col, segs=12)
        if extra: extra(b)
    module(name, [iron, dark, steel, brass, green, col, glass, amber], fn)
sniper_mod("SniperDefault", 1.3, 0.06, green)
sniper_mod("SniperA", 1.7, 0.075, green)
sniper_mod("SniperA1", 2.0, 0.09, red)
sniper_mod("SniperA2", 1.8, 0.07, red, scope=True)
sniper_mod("SniperB", 0.9, 0.07, green, count=1, extra=lambda b: b.box((0.18, 0.2, 0.14), (0, -0.7, 0.32), brass))
sniper_mod("SniperB1", 0.8, 0.08, green, extra=lambda b: b.cyl(0.13, 0.22, (0, -0.7, 0.32), brass, segs=14, axis="Y"))
sniper_mod("SniperB2", 1.2, 0.07, green, scope=True, extra=lambda b: b.ico(0.06, (0, -1.0, 0.58), amber, subdiv=1))

# ============================== BUBBLE ==============================
def bubble_body(b):
    b.cyl(0.46, 0.14, (0, 0, 0.07), iron, segs=20); b.cyl(0.38, 0.1, (0, 0, 0.19), cyan, segs=20)
    studs(b, 0.4, 10, 0.14, brass, 0.05)
    for k in range(4):
        a = k * TAU / 4; b.box((0.1, 0.1, 0.36), (0.26 * math.cos(a), 0.26 * math.sin(a), 0.36), brass, rot=(0, 0, -a))
body("BubbleBody", [iron, dark, steel, brass, cyan], bubble_body)
def bubble_mod(name, radius, col, extra=None):
    def fn(b):
        collar(b, 0.5, brass, 0.24)
        b.cone(radius, 0.16, 0.3, (0, 0, 0.6), brass, segs=24)
        b.cyl(radius, 0.04, (0, 0, 0.76), col, segs=24)
        b.beam((0, 0, 0.7), (0, 0, 1.0), 0.06, steel, segs=8)
        b.ico(0.22, (0, 0, 1.1), col, subdiv=2)
        torus(b, (0, 0, 0.92), (0, 0, 1), radius * 0.7, 0.035, col, segs=24, tsegs=6)
        if extra: extra(b)
    module(name, [iron, dark, steel, brass, cyan, col, glass], fn)
bubble_mod("BubbleDefault", 0.4, cyan)
bubble_mod("BubbleA", 0.55, cyan)
bubble_mod("BubbleA1", 0.65, cyan)
bubble_mod("BubbleA2", 0.5, ice)
bubble_mod("BubbleB", 0.4, brass, extra=lambda b: [b.box((0.16, 0.16, 0.2), (0.3 * math.cos(k * TAU / 4), 0.3 * math.sin(k * TAU / 4), 0.7), steel) for k in range(4)])
bubble_mod("BubbleB1", 0.45, brass, extra=lambda b: [b.box((0.18, 0.18, 0.24), (0.32 * math.cos(k * TAU / 4), 0.32 * math.sin(k * TAU / 4), 0.7), iron) for k in range(4)])
bubble_mod("BubbleB2", 0.5, red)

# ============================== MENDER ==============================
def mender_body(b):
    b.cyl(0.46, 0.14, (0, 0, 0.07), iron, segs=20); b.cyl(0.36, 0.6, (0, 0, 0.4), mint, segs=18)
    for z in (0.24, 0.4, 0.56): b.cyl(0.39, 0.06, (0, 0, z), brass, segs=22)
    studs(b, 0.42, 10, 0.14, brass, 0.05)
body("MenderBody", [iron, dark, steel, brass, mint], mender_body)
def mender_mod(name, arms, core, col, extra=None):
    def fn(b):
        collar(b, 0.7, brass, 0.24)
        b.cyl(0.2, 0.12, (0, 0, 0.8), brass, segs=16); b.ico(core, (0, 0, 0.95), col, subdiv=2)
        for s in (-1, 1):
            b.beam((s * 0.3, 0, 0.55), (s * 0.52, -0.1, 0.72), 0.07, steel, segs=8)
            b.box((0.15, 0.15, 0.15), (s * 0.55, -0.12, 0.72), mint)
            for k in range(arms - 1):
                b.beam((s * 0.55, -0.12, 0.7 - k * 0.12), (s * 0.5, -0.3, 0.55 - k * 0.12), 0.04, steel, segs=6)
            b.box((0.12, 0.16, 0.12), (s * 0.52, -0.38, 0.45), brass)
        if extra: extra(b)
    module(name, [iron, dark, steel, brass, mint, col, glass, amber, ice], fn)
mender_mod("MenderDefault", 1, 0.13, glass)
mender_mod("MenderA", 2, 0.14, glass)
mender_mod("MenderA1", 2, 0.16, amber)
mender_mod("MenderA2", 1, 0.18, amber, extra=lambda b: b.cyl(0.1, 0.2, (0.28, -0.2, 0.55), glass, segs=10, axis="Y"))
mender_mod("MenderB", 1, 0.18, amber)
mender_mod("MenderB1", 1, 0.22, amber)
mender_mod("MenderB2", 2, 0.15, ice)

export(list(B.values()))
