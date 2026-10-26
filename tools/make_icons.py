# UI icons: every gear, fortress weapon and building piece rendered from its model, a transparent 256 px PNG each, into
# HtfFortress/Icons (shipped next to the models; the vault, loadout bar, pickup cards, build cards and menus show them).
# Gear without a model of its own gets a small icon model built here (not saved: the .blend stays as it was).
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_icons.py [-- Name ...]
#           or:  ... --python tools/make_icons.py -- export-gear   (the gear's models as Models/Gear<Kind>.obj: what a drop on
#                the ground looks like; still nothing saved into the .blend)
# Also writes Icons/_sheet.png: all of them on the HUD's dark panel colour, to check they read at a glance.
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from mathutils import Vector
from bkit import TAU, Builder, M, fin, loft, mat, torus, tube

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "HtfFortress", "Icons")
ONLY = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
EXPORT_GEAR = "export-gear" in ONLY
if EXPORT_GEAR:
    ONLY = []
RES = 256
os.makedirs(OUT, exist_ok=True)

iron, metal, steel, brass, gold, dark, rope, black, white, pearl = (
    M(n) for n in ("Iron", "Metal", "Steel", "Brass", "Gold", "DarkWood", "Rope", "BombBlack", "White", "Pearl"))
copper, spark, fuse, cloth, leaf, red, roof, stone, coin, wood = (
    M(n) for n in ("Copper", "Spark", "Fuse", "Cloth", "Leaf", "FuelRed", "RoofRed", "Stone", "Coin", "Wood"))
ice = mat("IconIce", (0.62, 0.9, 1.0), (0.35, 0.7, 1.0), force=True)
glass = mat("IconGlass", (0.42, 0.72, 0.42), force=True)
flame = mat("IconFlame", (1.0, 0.55, 0.12), (1.0, 0.4, 0.05), force=True)
rubber = mat("IconRubber", (1.0, 0.62, 0.12), force=True)
shark = mat("IconShark", (0.46, 0.55, 0.66), force=True)
belly = mat("IconSharkBelly", (0.88, 0.9, 0.93), force=True)
leather = mat("IconLeather", (0.45, 0.27, 0.15), force=True)
heal = mat("IconHeal", (0.35, 0.95, 0.45), (0.2, 0.8, 0.3), force=True)
wave = mat("IconWave", (0.55, 0.85, 1.0), (0.3, 0.65, 1.0), force=True)
lens = mat("IconLens", (0.35, 0.7, 1.0), (0.2, 0.5, 0.9), force=True)


def around_z(z, r, n=12, sx=1.0, sy=1.0, cx=0.0, cy=0.0):
    return [(cx + r * sx * math.cos(k * TAU / n), cy + r * sy * math.sin(k * TAU / n), z) for k in range(n)]


def around_y(y, r, n=12, cx=0.0, cz=0.0):
    return [(cx + r * math.cos(k * TAU / n), y, cz + r * math.sin(k * TAU / n)) for k in range(n)]


# ---------- icon models for the gear that has none ----------

def gunpowder():  # a powder keg with iron hoops and a lit fuse
    b = Builder("IconGunpowder", [wood, iron, black, fuse, spark])
    loft(b, [around_z(z, r, 14) for z, r in ((0, 0.15), (0.05, 0.175), (0.15, 0.19), (0.25, 0.175), (0.3, 0.15))], wood)
    for z in (0.035, 0.265):
        torus(b, (0, 0, z), (0, 0, 1), 0.168, 0.013, iron, segs=18, tsegs=4)
    b.cone(0.13, 0.03, 0.05, (0, 0, 0.325), black, segs=12)
    tube(b, [(0.0, 0, 0.34), (0.05, -0.01, 0.4), (0.1, -0.03, 0.43), (0.13, -0.05, 0.47)], 0.011, fuse, segs=5)
    b.ico(0.04, (0.135, -0.055, 0.48), spark, subdiv=1)
    return b.finish()


def scope():  # a rifle scope on its mount
    b = Builder("IconScope", [black, iron, brass, lens])
    b.cyl(0.032, 0.3, (0, 0, 0), black, segs=14, axis="Y")
    loft(b, [around_y(-0.15, 0.032, 14), around_y(-0.2, 0.058, 14), around_y(-0.25, 0.058, 14)], black)   # objective bell
    b.cyl(0.052, 0.01, (0, -0.255, 0), lens, segs=14, axis="Y")
    loft(b, [around_y(0.15, 0.032, 14), around_y(0.19, 0.045, 14), around_y(0.23, 0.045, 14)], black)     # eyepiece
    b.cyl(0.04, 0.012, (0, 0.235, 0), lens, segs=14, axis="Y")
    b.cyl(0.02, 0.05, (0, 0.0, 0.045), brass, segs=10)                                                     # adjustment turrets
    b.cyl(0.02, 0.05, (0.045, 0.0, 0), brass, segs=10, axis="X")
    for y in (-0.09, 0.09):
        torus(b, (0, y, 0), (0, 1, 0), 0.036, 0.008, iron, segs=14, tsegs=4)
        b.box((0.03, 0.02, 0.05), (0, y, -0.055), iron)
    b.box((0.04, 0.26, 0.015), (0, 0, -0.085), iron)
    return b.finish()


def ap_rounds():  # three armour-piercing bullets, a red band on each
    b = Builder("IconApRounds", [brass, steel, red])
    for x, y in ((-0.06, 0.02), (0.0, -0.03), (0.06, 0.02)):
        b.cyl(0.026, 0.12, (x, y, 0.06), brass, segs=12)
        b.cyl(0.029, 0.012, (x, y, 0.006), brass, segs=12)
        b.cyl(0.0265, 0.014, (x, y, 0.127), red, segs=12)
        b.cone(0.024, 0.0, 0.085, (x, y, 0.1765), steel, segs=12)
    return b.finish()


def incendiary():  # a fire bottle: green glass, red fuel, a rag, a flame
    b = Builder("IconIncendiary", [glass, red, cloth, flame, spark])
    loft(b, [around_z(z, r, 12) for z, r in ((0, 0.06), (0.1, 0.065), (0.14, 0.05), (0.17, 0.022), (0.24, 0.022))], glass)
    b.cyl(0.0655, 0.05, (0, 0, 0.035), red, segs=12)
    tube(b, [(0, 0, 0.23), (0.01, 0, 0.27), (0.03, -0.01, 0.3)], [0.02, 0.018, 0.012], cloth, segs=6)
    b.cone(0.045, 0.0, 0.12, (0.035, -0.01, 0.36), flame, segs=8)
    b.cone(0.025, 0.0, 0.07, (0.035, -0.02, 0.35), spark, segs=6)
    return b.finish()


def frost_core():  # a cluster of ice crystals on a stone
    b = Builder("IconFrostCore", [ice, stone])
    b.cyl(0.11, 0.04, (0, 0, 0.02), stone, segs=8)
    for (dx, dy, h, r) in ((0, 0, 0.3, 0.05), (0.07, 0.02, 0.2, 0.035), (-0.06, 0.03, 0.22, 0.035), (0.02, -0.07, 0.17, 0.03), (-0.03, -0.05, 0.14, 0.028)):
        top = Vector((dx * 1.8, dy * 1.8, h))
        base = Vector((dx, dy, 0.04))
        b.beam(base, base + (top - base) * 0.8, r, ice, segs=6)
        b.beam(base + (top - base) * 0.8, top, r, ice, segs=6, r2=0.0)
    return b.finish()


def tesla_coil():  # a copper coil on a base, a ball on top, sparks
    b = Builder("IconTeslaCoil", [iron, copper, steel, spark])
    b.cyl(0.1, 0.04, (0, 0, 0.02), iron, segs=12)
    b.cyl(0.03, 0.26, (0, 0, 0.17), iron, segs=10)
    for k in range(8):
        torus(b, (0, 0, 0.07 + k * 0.026), (0, 0, 1), 0.045, 0.011, copper, segs=14, tsegs=4)
    b.ico(0.065, (0, 0, 0.33), steel, subdiv=2)
    for a in (0.3, 2.4, 4.4):
        c, s = math.cos(a), math.sin(a)
        tube(b, [(0.06 * c, 0.06 * s, 0.34), (0.11 * c + 0.02 * s, 0.11 * s - 0.02 * c, 0.37), (0.14 * c, 0.14 * s, 0.33), (0.19 * c, 0.19 * s, 0.36)], 0.009, spark, segs=4)
    return b.finish()


def rebar():  # three ribbed steel bars tied with wire
    b = Builder("IconRebar", [steel, iron, rope])
    for x, z in ((-0.03, 0.0), (0.03, 0.0), (0.0, 0.05)):
        b.cyl(0.018, 0.46, (x, 0, z), steel, segs=8, axis="Y")
        for k in range(9):
            torus(b, (x, -0.2 + k * 0.05, z), (0, 1, 0), 0.019, 0.005, iron, segs=8, tsegs=3)
    for y in (-0.12, 0.12):
        torus(b, (0, y, 0.017), (0, 1, 0), 0.058, 0.009, rope, segs=12, tsegs=4)
    return b.finish()


def core_shield():  # a round shield with the lighthouse on it
    b = Builder("IconCoreShield", [steel, white, roof, spark, iron])
    b.cyl(0.2, 0.03, (0, 0, 0), white, segs=20, axis="Y")
    torus(b, (0, 0, 0), (0, 1, 0), 0.2, 0.022, steel, segs=24, tsegs=5)
    b.cyl(0.035, 0.03, (0, 0.03, 0), iron, segs=10, axis="Y")                                            # the boss behind
    for k, (z0, z1, m) in enumerate(((-0.14, -0.07, roof), (-0.07, 0.0, white), (0.0, 0.07, roof))):     # striped tower
        w0, w1 = 0.06 - (z0 + 0.14) * 0.15, 0.06 - (z1 + 0.14) * 0.15
        loft(b, [[(-w0, -0.022, z0), (w0, -0.022, z0), (w0, -0.035, z0), (-w0, -0.035, z0)],
                 [(-w1, -0.022, z1), (w1, -0.022, z1), (w1, -0.035, z1), (-w1, -0.035, z1)]], m)
    b.box((0.06, 0.016, 0.035), (0, -0.03, 0.09), spark)
    b.cone(0.045, 0.0, 0.04, (0, -0.03, 0.125), roof, segs=4)
    return b.finish()


def shark_armor():  # a grey vest with a shark fin on the back and a row of teeth round the collar
    b = Builder("IconSharkArmor", [shark, belly, pearl, leather])
    loft(b, [around_z(z, r, 14, sx=1.0, sy=0.62) for z, r in ((0, 0.13), (0.12, 0.15), (0.24, 0.155), (0.3, 0.12))], shark,
         mf=lambda i, j: belly if 9 <= j <= 12 else shark)
    for s in (-1, 1):
        b.box((0.06, 0.1, 0.025), (s * 0.08, 0, 0.31), leather)
    for k in range(9):                                                                                     # teeth round the collar
        a = math.pi + 0.35 + k * (math.pi - 0.7) / 8
        p = Vector((0.12 * math.cos(a), 0.12 * 0.62 * math.sin(a), 0.3))
        b.beam(p, p + Vector((0, 0, -0.045)), 0.012, pearl, segs=4, r2=0.0)
    fin(b, [(0, 0.09, 0.08), (0, 0.17, 0.3), (0, 0.13, 0.33), (0, 0.095, 0.27)], (1, 0, 0), 0.02, shark)
    return b.finish()


def fish_gloves():  # a rubber work glove
    b = Builder("IconFishGloves", [rubber, iron])
    b.box((0.14, 0.05, 0.13), (0, 0, 0.12), rubber)
    for k, (x, h) in enumerate(((-0.05, 0.1), (-0.017, 0.12), (0.017, 0.115), (0.05, 0.09))):
        b.beam((x, 0, 0.18), (x, 0, 0.18 + h), 0.018, rubber, segs=8)
        b.ico(0.018, (x, 0, 0.18 + h), rubber, subdiv=1)
    b.beam((0.07, 0, 0.1), (0.13, 0, 0.18), 0.02, rubber, segs=8)
    b.ico(0.02, (0.13, 0, 0.18), rubber, subdiv=1)
    loft(b, [around_z(z, r, 12, sx=1.0, sy=0.45) for z, r in ((-0.06, 0.1), (0.06, 0.08))], rubber)       # cuff
    torus(b, (0, 0, 0.055), (0, 0, 1), 0.08, 0.008, iron, segs=14, tsegs=3)
    return b.finish()


def gauntlet():  # a steel fist with spiked knuckles
    b = Builder("IconGauntlet", [steel, iron, gold])
    b.box((0.15, 0.1, 0.11), (0, 0, 0.1), steel)
    for x in (-0.05, -0.017, 0.017, 0.05):
        b.cyl(0.022, 0.05, (x, -0.06, 0.14), iron, segs=8, axis="Y")
        b.cone(0.013, 0.0, 0.04, (x, -0.1, 0.14), steel, segs=6, axis="-Y")
    b.beam((0.075, -0.02, 0.07), (0.09, -0.06, 0.12), 0.024, steel, segs=8)                              # thumb
    loft(b, [around_z(z, r, 12, sy=0.8) for z, r in ((-0.09, 0.085), (0.05, 0.08))], iron)                # cuff
    torus(b, (0, 0, -0.02), (0, 0, 1), 0.085, 0.01, gold, segs=14, tsegs=3)
    return b.finish()


def tool_belt():  # a hammer and a wrench crossed over a leather belt
    b = Builder("IconToolBelt", [leather, brass, dark, iron, steel])
    tube(b, [(0.2 * math.cos(a), -0.05, 0.2 * math.sin(a) - 0.12) for a in (math.radians(d) for d in range(200, 341, 20))], 0.028, leather, segs=6)
    b.box((0.05, 0.02, 0.05), (0.0, -0.075, -0.31), brass)
    b.beam((-0.13, 0, -0.16), (0.1, 0, 0.13), 0.016, dark, segs=6)                                        # hammer
    b.box((0.12, 0.05, 0.045), (0.1, 0, 0.14), iron, rot=(0, math.radians(38), 0))
    b.beam((0.13, -0.01, -0.16), (-0.08, -0.01, 0.1), 0.014, steel, segs=4)                              # wrench
    torus(b, (-0.1, -0.01, 0.135), (0, 1, 0), 0.04, 0.014, steel, segs=10, tsegs=4)
    return b.finish()


def lucky_charm():  # a golden horseshoe
    b = Builder("IconLuckyCharm", [gold, dark])
    pts = [(0.13 * math.cos(a), 0, 0.13 * math.sin(a) + 0.13) for a in (math.radians(d) for d in range(-215, 36, 12))]
    tube(b, pts, 0.034, gold, segs=8)
    for a in (-200, -150, -30, 20):
        r = math.radians(a)
        b.box((0.014, 0.08, 0.014), (0.13 * math.cos(r), 0, 0.13 * math.sin(r) + 0.13), dark)
    return b.finish()


def overclock():  # a cog with a lightning bolt in front
    b = Builder("IconOverclock", [iron, spark, black])
    b.cyl(0.13, 0.04, (0, 0, 0), iron, segs=20, axis="Y")
    for k in range(10):
        a = k * TAU / 10
        b.box((0.05, 0.04, 0.05), (0.15 * math.cos(a), 0, 0.15 * math.sin(a)), iron, rot=(0, -a, 0))
    b.cyl(0.045, 0.05, (0, 0, 0), black, segs=12, axis="Y")
    bolt = [(-0.02, 0.17), (0.07, 0.17), (0.02, 0.03), (0.08, 0.03), (-0.06, -0.19), (-0.02, -0.04), (-0.08, -0.04)]
    loft(b, [[(x, -0.04, z) for x, z in bolt], [(x, -0.08, z) for x, z in bolt]], spark)
    return b.finish()


def field_repair():  # a big wrench and a green cross
    b = Builder("IconFieldRepair", [steel, heal])
    b.beam((-0.12, 0, -0.12), (0.09, 0, 0.09), 0.025, steel, segs=4)
    torus(b, (0.12, 0, 0.12), (0, 1, 0), 0.055, 0.022, steel, segs=10, tsegs=4)
    b.box((0.06, 0.06, 0.02), (0.15, 0, 0.15), steel, rot=(0, math.radians(45), 0))
    torus(b, (-0.14, 0, -0.14), (0, 1, 0), 0.035, 0.018, steel, segs=10, tsegs=4)
    b.box((0.17, 0.05, 0.055), (0.07, -0.06, -0.08), heal)
    b.box((0.055, 0.05, 0.17), (0.07, -0.06, -0.08), heal)
    return b.finish()


def shockwave():  # rings spreading out from a burst
    b = Builder("IconShockwave", [wave, white])
    for r, t in ((0.11, 0.02), (0.2, 0.017), (0.29, 0.013)):
        torus(b, (0, 0, 0), (0, 0, 1), r, t, wave, segs=28, tsegs=4)
    b.ico(0.05, (0, 0, 0.0), white, subdiv=2)
    for k in range(8):
        a = k * TAU / 8
        b.beam((0.06 * math.cos(a), 0.06 * math.sin(a), 0), (0.1 * math.cos(a), 0.1 * math.sin(a), 0), 0.012, white, segs=4, r2=0.0)
    return b.finish()


def net_cannon():  # the cannon turret's head with a balled-up net at its mouth
    b = Builder("IconNetBall", [rope])
    for k in range(6):
        torus(b, (0, -0.62, 0.1), (math.cos(k * math.pi / 6), 0, math.sin(k * math.pi / 6)), 0.13, 0.012, rope, segs=14, tsegs=3)
    return b.finish()


# ---------- what each icon shows ----------
# name: [(object, location)], view (where the camera sits, seen from the middle), optional rotation of the first object
def fish(b, c, length, m, fin, yaw=0.0, up=0.0):  # a plump fish lying along X (turned by yaw about Z), tail to +X
    ca, sa = math.cos(yaw), math.sin(yaw)
    def P(x, z):
        return (c[0] + x * ca, c[1] + x * sa, c[2] + z + up * x)
    rings = []
    for x, r in ((-0.5, 0.0), (-0.42, 0.28), (-0.25, 0.42), (0.0, 0.45), (0.22, 0.33), (0.38, 0.14)):
        rings.append([(P(x * length, 0)[0] - math.sin(yaw) * r * length * 0.55 * math.cos(a), P(x * length, 0)[1] + math.cos(yaw) * r * length * 0.55 * math.cos(a),
                       P(x * length, 0)[2] + r * length * 0.55 * math.sin(a)) for a in [k * TAU / 8 for k in range(8)]] if r > 0 else [P(x * length, 0)])
    loft(b, rings, m)
    tail = P(0.38 * length, 0)
    b.beam(tail, P(0.6 * length, 0.12 * length), length * 0.07, fin, segs=4, r2=0.0)
    b.beam(tail, P(0.6 * length, -0.12 * length), length * 0.07, fin, segs=4, r2=0.0)
    b.ico(length * 0.05, (P(-0.33 * length, 0.08 * length)[0] - math.sin(yaw) * 0.12 * length, P(-0.33 * length, 0)[1] + math.cos(yaw) * -0.12 * length, P(0, 0.08 * length)[2]), black, subdiv=1)


fish_body = mat("IconFishBody", (0.95, 0.55, 0.25), force=True)
fish_grilled = mat("IconFishGrilled", (0.62, 0.36, 0.16), force=True)
wicker = mat("IconWicker", (0.72, 0.52, 0.26), force=True)
coal = mat("IconCoal", (1.0, 0.35, 0.08), (1.0, 0.3, 0.05), force=True)
boot = mat("IconBoot", (0.25, 0.45, 0.85), force=True)


def stall_helper():  # a net bag full of fish over a rope handle
    b = Builder("IconStallHelper", [rope, fish_body, black, white])
    for k, (x, z) in enumerate(((-0.06, 0.02), (0.07, 0.04), (0.0, 0.12))):
        fish(b, (x, 0, z), 0.2, fish_body, fish_body, yaw=0.4 * k)
    for k in range(6):
        a = k * TAU / 6
        tube(b, [(0.15 * math.cos(a), 0.15 * math.sin(a), -0.06), (0.12 * math.cos(a), 0.12 * math.sin(a), 0.12), (0.0, 0.0, 0.27)], 0.006, rope, segs=4)
    torus(b, (0, 0, 0.3), (1, 0, 0), 0.04, 0.008, rope, segs=10, tsegs=3)
    return b.finish()


def stall_speed():  # a blue boot with little wings
    b = Builder("IconStallSpeed", [boot, white, black])
    b.box((0.09, 0.22, 0.06), (0, -0.04, -0.07), boot)
    b.cyl(0.06, 0.18, (0, 0.04, 0.04), boot, segs=10)
    b.box((0.1, 0.24, 0.02), (0, -0.04, -0.105), black)
    for s in (-1, 1):
        for k in range(3):
            b.box((0.012, 0.14 - 0.03 * k, 0.03), (s * 0.075, 0.1 + 0.03 * k, 0.07 + 0.035 * k), white, rot=(math.radians(-25), 0, 0))
    return b.finish()


def stall_basket():  # a wicker basket heaped with fish
    b = Builder("IconStallBasket", [wicker, fish_body, black, rope])
    b.cone(0.13, 0.17, 0.16, (0, 0, -0.05), wicker, segs=14)
    torus(b, (0, 0, 0.03), (0, 0, 1), 0.17, 0.014, rope, segs=18, tsegs=4)
    for k, (x, y, z) in enumerate(((-0.06, -0.03, 0.05), (0.06, 0.02, 0.06), (0.0, -0.05, 0.1), (0.02, 0.06, 0.08))):
        fish(b, (x, y, z), 0.17, fish_body, fish_body, yaw=1.1 * k, up=0.3)
    tube(b, [(-0.15, 0, 0.02), (-0.1, 0, 0.2), (0.1, 0, 0.2), (0.15, 0, 0.02)], 0.01, wicker, segs=5)
    return b.finish()


def stall_oven():  # a grill on legs, glowing coals, a fish on it
    b = Builder("IconStallOven", [iron, coal, fish_grilled, black])
    b.cone(0.16, 0.12, 0.1, (0, 0, -0.02), iron, segs=14)
    b.cyl(0.14, 0.015, (0, 0, 0.035), coal, segs=14)
    for k in range(5):
        b.box((0.3, 0.008, 0.008), (0, -0.1 + 0.05 * k, 0.05), iron)
    for k in range(3):
        a = k * TAU / 3
        b.beam((0.1 * math.cos(a), 0.1 * math.sin(a), -0.06), (0.16 * math.cos(a), 0.16 * math.sin(a), -0.2), 0.012, iron, segs=4)
    fish(b, (0, 0, 0.085), 0.24, fish_grilled, fish_grilled)
    return b.finish()


def stall_food():  # a grilled fish on a stick
    b = Builder("IconStallFood", [fish_grilled, wood, black])
    fish(b, (0.02, 0, 0.05), 0.28, fish_grilled, fish_grilled, up=0.25)
    b.beam((-0.2, 0, -0.03), (0.22, 0, 0.1), 0.008, wood, segs=4)
    return b.finish()


FRONT, SIDE, HIGH = (-0.75, -1.0, 0.6), (-1.0, -0.45, 0.35), (-0.6, -0.8, 1.1)
built = {}


def obj(name):
    return built.get(name) or bpy.data.objects[name]


ICONS = {}
for g, fn in (("Gunpowder", gunpowder), ("Scope", scope), ("ApRounds", ap_rounds), ("Incendiary", incendiary), ("FrostCore", frost_core),
              ("TeslaCoil", tesla_coil), ("Rebar", rebar), ("CoreShield", core_shield), ("SharkArmor", shark_armor),
              ("FishGloves", fish_gloves), ("Gauntlet", gauntlet), ("ToolBelt", tool_belt), ("LuckyCharm", lucky_charm),
              ("Overclock", overclock), ("FieldRepair", field_repair), ("Shockwave", shockwave)):
    if not ONLY or "Gear" + g in ONLY:
        built["Icon" + g] = fn()
    ICONS["Gear" + g] = ([("Icon" + g, (0, 0, 0))], FRONT if g not in ("Scope", "Rebar") else SIDE)
ICONS["GearShockwave"] = ([("IconShockwave", (0, 0, 0))], HIGH)
ICONS["GearOverclock"] = ([("IconOverclock", (0, 0, 0))], (-0.3, -1.0, 0.25))
ICONS["GearCoreShield"] = ([("IconCoreShield", (0, 0, 0))], (-0.35, -1.0, 0.25))
ICONS["GearFieldRepair"] = ([("IconFieldRepair", (0, 0, 0))], (-0.3, -1.0, 0.25))
ICONS["GearLuckyCharm"] = ([("IconLuckyCharm", (0, 0, 0))], (-0.35, -1.0, 0.3))
ICONS["GearToolBelt"] = ([("IconToolBelt", (0, 0, 0))], (-0.3, -1.0, 0.3))
ICONS["GearWarChest"] = ([("Chest", (0, 0, 0))], FRONT)
ICONS["GearDecoy"] = ([("Decoy", (0, 0, 0))], FRONT)
ICONS["GearBigNet"] = ([("HandNet", (0, 0, 0)), ("HandNetBag", (0, 0, 0))], (-0.45, -1.0, 0.55))
if not ONLY or "GearNetCannon" in ONLY:
    built["IconNetBall"] = net_cannon()
ICONS["GearNetCannon"] = ([("CannonHead", (0, 0, 0)), ("IconNetBall", (0, 0, 0))], FRONT)
for name, fn in (("StallHelper", stall_helper), ("StallSpeed", stall_speed), ("StallBasket", stall_basket), ("StallOven", stall_oven), ("StallFood", stall_food)):
    if not ONLY or name in ONLY:
        built["Icon" + name] = fn()
    ICONS[name] = ([("Icon" + name, (0, 0, 0))], FRONT)
for g, m in (("Laser", "LaserGun"), ("PoisonGas", "GasGun"), ("Boomerang", "Boomerang"), ("SharkWhistle", "SharkWhistle"),
             ("BubbleGun", "BubbleGun"), ("Hook", "HookGun"), ("Whirlpool", "WhirlCannon"), ("Conch", "Conch"),
             ("NetLauncher", "NetLauncher"), ("JellyGun", "JellyGun"), ("WaterCannon", "WaterCannon"), ("PufferBomb", "PufferBomb"),
             ("StarThrower", "StarThrower"), ("LobsterClaw", "LobsterClaw"), ("Rocket", "Rocket"), ("Absorber", "Absorber"), ("Gatling", "Gatling")):
    ICONS["Gear" + g] = ([(m, (0, 0, 0))], SIDE if g != "Boomerang" else (-0.25, -0.45, 1.0))  # the wooden V from above, tilted to show its edge
for p in ("Wall", "WallStone", "WallMetal", "Floor", "Ramp", "Spikes"):
    ICONS["Piece" + p] = ([(p, (0, 0, 0))], FRONT)
FRAME = {}  # icon: (objects the frame is fitted to, how much lower the frame reaches, in parts of its height)
for t in ("Gun", "Cannon", "Harpoon", "Mortar", "Tesla", "Flame", "Sniper", "Bubble", "Mender"):
    ICONS["Piece" + t] = ([("TurretBase", (0, 0, 0)), (t + "Body", (0, 0, 0.85)), (t + "Default", (0, 0, 0.85))], FRONT)
    FRAME["Piece" + t] = ([t + "Body", t + "Default"], 0.35)

# ---------- the help panel's pictures (Tips.cs): the lighthouse and its pearl, the village, each kind of fish ----------
# A part can carry a rotation and a scale too: (object, location, rotation, scale).
CLAM = [("ClamBase", (0, -1.03, 1.06)), ("ClamLid", (0, -1.03, 1.06), (math.radians(-62), 0, 0)), ("Pearl", (0, -1.25, 1.093))]  # Siege.cs: Hinge, PearlAt
ICONS["HelpLighthouse"] = ([("Core", (0, 0, 0))] + CLAM, (0.3, -1.0, 0.3))
ICONS["HelpLadder"] = ([("Core", (0, 0, 0))] + CLAM, (1.0, -0.4, 0.3))
ICONS["HelpPearl"] = ([("ClamBase", (0, 0, 0)), ("ClamLid", (0, 0, 0), (math.radians(-62), 0, 0)), ("Pearl", (0, -0.22, 0.033))], (-0.35, -1.0, 0.7))
ICONS["HelpLoot"] = ([("Rocket", (0, 0, 0)), ("LootGem", (0, -0.05, 0.48), (0, 0, 0), (0.36, 0.36, 0.36))], SIDE)


def revive():  # a red heart with a white cross on it: a teammate picks you up
    b = Builder("IconRevive", [red, white])
    for x in (-0.1, 0.1):
        b.ico(0.135, (x, 0, 0.08), red, subdiv=2)
    b.cone(0.0, 0.215, 0.27, (0, 0, -0.07), red, segs=12)
    b.box((0.06, 0.04, 0.2), (0, -0.235, 0.02), white)
    b.box((0.2, 0.04, 0.06), (0, -0.235, 0.02), white)
    return b.finish()


if not ONLY or "HelpRevive" in ONLY:
    built["IconRevive"] = revive()
ICONS["HelpRevive"] = ([("IconRevive", (0, 0, 0))], (-0.25, -1.0, 0.2))
for st in ("MarketStall", "Smithy", "Vault"):
    ICONS["Help" + st.replace("MarketStall", "Stall")] = ([(st, (0, 0, 0))], FRONT)

# Each kind of fish as you meet it and what it wears (below: its own body, Foe<Kind>); the build and dye here are only the
# boss's: a big red one with a crown.
FOES = {"Charger": ((0.85, 0.8, 1.35), (1.0, 0.5, 0.44), "Helmet"), "Tank": ((1.3, 1.05, 1.1), (0.88, 0.76, 0.52), "Shell"),
        "Spitter": ((1.0, 1.0, 1.0), (0.62, 0.68, 0.76), "SpitterCannon"), "Bomber": ((1.15, 1.1, 1.0), (0.55, 0.55, 0.66), "Bomb"),
        "Thief": ((0.75, 1.1, 0.95), (0.74, 0.52, 1.0), "ThiefSack"), "Shield": ((1.25, 1.0, 0.95), (0.55, 0.74, 1.0), "ShieldBoard"),
        "Digger": ((1.15, 0.75, 1.15), (0.96, 0.68, 0.38), "MinerHat"), "Healer": ((0.85, 1.3, 0.85), (0.5, 1.0, 0.58), "HealerStaff"),
        "Splitter": ((1.15, 0.9, 1.15), (0.78, 1.0, 0.34), "SplitterBubbles"), "Boss": ((1.35, 1.25, 1.4), (0.78, 0.2, 0.18), "Crown")}


def foe_fish(kind, shape, dye):  # a fish 2 m long facing -Y, its middle at the origin, built to the kind's shape
    m = mat("IconFoe" + kind, dye, force=True)
    b = Builder("IconFoe" + kind, [m, black])
    fish(b, (0, 0, 0), 2.0, m, m, yaw=math.pi / 2)
    for v in b.bm.verts:
        v.co.x, v.co.y, v.co.z = v.co.x * shape[0], v.co.y * shape[2], v.co.z * shape[1]
    return b.finish()


# The kinds as the game shows them: our own body (tools/make_foe_models.py, Foe<Kind> in the .blend) and its gear where
# Foes.cs puts it: along the body (nose +0.5), let down into the back by a share of its height, sized on its width.
FOE_ANCHOR = {"Helmet": (0.27, 0.04, 1), "MinerHat": (0.27, 0.04, 1), "ShieldBoard": (0.33, 0.0, 1), "ThiefSack": (-0.12, 0.02, 1),
              "Shell": (0.0, 0.12, 1), "HealerStaff": (0.3, 0.02, 1.5)}


def foe_top(ob, along):  # the hull's top (not a fin's edge: those are plates on the middle line) at `along` of its length
    vs = [v.co for v in ob.data.vertices]
    y0, y1 = min(v.y for v in vs), max(v.y for v in vs)
    y = (y0 + y1) / 2 - along * (y1 - y0)  # the nose is -Y
    zs = [v.z for v in vs if abs(v.y - y) < 0.06 * (y1 - y0) and abs(v.x) > 0.02]
    return Vector((0, y, max(zs) if zs else max(v.z for v in vs)))


for kind, (shape, dye, gear) in FOES.items():
    name = "HelpBoss" if kind == "Boss" else "HelpFoe" + kind
    if kind == "Boss":
        if not ONLY or name in ONLY:
            built["IconFoe" + kind] = foe_fish(kind, shape, dye)
        ICONS[name] = ([("IconFoe" + kind, (0, 0, 0)), (gear, (0, 0, 0.495 * shape[1] - 0.05), (0, 0, 0), (1, 1, 1))], FRONT)
        continue
    body = bpy.data.objects["Foe" + kind]
    along, down, k = FOE_ANCHOR.get(gear, (0.0, 0.03, 1))
    size = max(body.dimensions.x, 0.3 * body.dimensions.y) * k
    at = foe_top(body, along) - Vector((0, 0, down * body.dimensions.z))
    ICONS[name] = ([("Foe" + kind, (0, 0, 0)), (gear, tuple(at), (0, 0, 0), (size, size, size))], FRONT)

# ---------- export: the gear models for drops on the ground ----------
if EXPORT_GEAR:
    models = os.path.join(ROOT, "HtfFortress", "Models")
    for g in ("Gunpowder", "Scope", "ApRounds", "Incendiary", "FrostCore", "TeslaCoil", "Rebar", "CoreShield", "SharkArmor",
              "FishGloves", "Gauntlet", "ToolBelt", "LuckyCharm", "Overclock", "FieldRepair", "Shockwave", "NetCannon"):
        ob = built["IconNetBall" if g == "NetCannon" else "Icon" + g]
        for o in bpy.context.scene.objects:
            o.select_set(False)
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        ob.name = ob.data.name = "Gear" + g
        bpy.ops.wm.obj_export(filepath=os.path.join(models, "Gear" + g + ".obj"), export_selected_objects=True, forward_axis="NEGATIVE_Z",
                              up_axis="Y", export_materials=True, export_triangulated_mesh=True, export_uv=False, export_normals=True,
                              export_smooth_groups=False)
        print("EXPORTED", "Gear" + g, tuple(round(d, 2) for d in ob.dimensions))
    sys.exit(0)

# ---------- rendering ----------
sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"
sh = sc.display.shading
sh.light = "STUDIO"
sh.studio_light = "paint.sl"
sh.color_type = "MATERIAL"
sh.show_backface_culling = False
sh.show_object_outline = True
sh.object_outline_color = (0.06, 0.07, 0.09)
sh.show_cavity = True
sh.cavity_type = "WORLD"
sc.render.film_transparent = True
sc.render.image_settings.file_format = "PNG"
sc.render.image_settings.color_mode = "RGBA"
sc.view_settings.view_transform = "Standard"
sc.view_settings.look = "None"
sc.render.resolution_x = sc.render.resolution_y = RES

for m in bpy.data.materials:  # game-like colours: a glowing material shows between its base and its glow
    bsdf = m.node_tree and next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if not bsdf:
        continue
    kd = Vector(bsdf.inputs["Base Color"].default_value[:3])
    ke = Vector(bsdf.inputs["Emission Color"].default_value[:3]) * bsdf.inputs["Emission Strength"].default_value
    c = kd.lerp(ke, 0.5) if ke.length > 0 else kd
    m.diffuse_color = (*(min(1.0, x) for x in c), 1.0)

for o in sc.objects:
    o.hide_render = True
cd = bpy.data.cameras.new("IconCam")
cd.type = "ORTHO"
cd.clip_start, cd.clip_end = 0.01, 200
cam = bpy.data.objects.new("IconCam", cd)
sc.collection.objects.link(cam)
sc.camera = cam


def aim(pts, d, margin=1.14):
    d = Vector(d).normalized()
    q = d.to_track_quat("Z", "Y")
    right, up = q @ Vector((1, 0, 0)), q @ Vector((0, 1, 0))
    ctr = sum(pts, Vector()) / len(pts)
    xs = [(p - ctr).dot(right) for p in pts]
    ys = [(p - ctr).dot(up) for p in pts]
    ctr = ctr + right * (max(xs) + min(xs)) / 2 + up * (max(ys) + min(ys)) / 2
    cam.rotation_euler = q.to_euler()
    cam.location = ctr + d * 50
    cd.ortho_scale = max(max(xs) - min(xs), max(ys) - min(ys)) * margin


done = []
for name, (parts, view) in ICONS.items():
    if ONLY and name not in ONLY:
        continue
    objs = []
    for part in parts:
        oname, loc = part[0], part[1]
        o = obj(oname)
        o.location = loc
        o.rotation_euler = part[2] if len(part) > 2 else (math.radians(45), 0, 0) if oname == "Ramp" else (0, 0, 0)  # the ramp tilted as the game lays it
        o.scale = part[3] if len(part) > 3 else (1, 1, 1)
        o.hide_render = False
        objs.append(o)
    bpy.context.view_layer.update()
    fit, low = FRAME.get(name, ([o.name for o in objs], 0.0))
    pts = [o.matrix_world @ v.co for o in objs if o.name in fit for v in o.data.vertices]
    if low:  # reach down a little, so the top of what it stands on shows under it
        zmin, zmax = min(p.z for p in pts), max(p.z for p in pts)
        pts.append(Vector((pts[0].x, pts[0].y, zmin - (zmax - zmin) * low)))
    aim(pts, view)
    sc.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    for o in objs:
        o.hide_render = True
    done.append(name)
    print("ICON", name)

# ---------- contact sheet on the HUD's panel colour ----------
if not ONLY:
    import numpy as np
    cols = 8
    rows = (len(done) + cols - 1) // cols
    cell = 128
    sheet = np.zeros((rows * cell, cols * cell, 4), dtype=np.float32)
    sheet[:, :] = (0.07, 0.09, 0.12, 1.0)
    for i, name in enumerate(done):
        img = bpy.data.images.load(os.path.join(OUT, name + ".png"))
        px = np.array(img.pixels[:], dtype=np.float32).reshape(RES, RES, 4)[::2, ::2]
        r, c = divmod(i, cols)
        y0 = (rows - 1 - r) * cell
        a = px[:, :, 3:4]
        block = sheet[y0:y0 + cell, c * cell:(c + 1) * cell]
        block[:, :, :3] = px[:, :, :3] * a + block[:, :, :3] * (1 - a)
        bpy.data.images.remove(img)
    out = bpy.data.images.new("Sheet", cols * cell, rows * cell, alpha=True)
    out.pixels = sheet.ravel()
    out.filepath_raw = os.path.join(OUT, "_sheet.png")
    out.file_format = "PNG"
    out.save()
    print("SHEET", len(done))
