# The second set of hand-held weapons (net launcher, jellyfish gun, water cannon, pufferfish bomb, starfish thrower, lobster
# claw) and what they throw (a balled net and the net spread on the ground, a jellyfish, a starfish), built into
# fortress_models.blend and exported as OBJ.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_weapon_models2.py
# Hand-held items: origin = centre of the grip, the business end toward -Y (the game's +Z), Z up (as make_weapon_models.py).
# Projectiles: origin in the middle, the front toward -Y. NetSpread lies flat (z = 0 is the ground), 1 m across.
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Vector
from bkit import TAU, Builder, M, export, fin, loft, mat, torus, tube

iron, metal, steel, brass, gold, dark, rope, black, white, pearl = (
    M(n) for n in ("Iron", "Metal", "Steel", "Brass", "Gold", "DarkWood", "Rope", "BombBlack", "White", "Pearl"))
wood, orange, yellow, coral, red, bubble = (M(n) for n in ("Wood", "FishOrange", "DuckYellow", "Coral", "Bandana", "BubbleGlow"))
navy = M("WpnDeepBlue")
float_red = mat("WpnFloatRed", (0.92, 0.22, 0.18), force=True)
jelly = mat("WpnJelly", (0.95, 0.55, 0.9), (0.85, 0.35, 0.95), force=True)
jelly_deep = mat("WpnJellyDeep", (0.62, 0.38, 0.9), force=True)
water = mat("WpnWaterGlow", (0.45, 0.8, 1.0), (0.2, 0.6, 1.0), force=True)
tank = mat("WpnTankBlue", (0.2, 0.5, 0.9), force=True)
puffer = mat("WpnPuffer", (0.95, 0.82, 0.42), force=True)
puffer_belly = mat("WpnPufferBelly", (1.0, 0.95, 0.82), force=True)
spine = mat("WpnSpine", (0.55, 0.42, 0.2), force=True)
star = mat("WpnStarfish", (1.0, 0.5, 0.25), force=True)
star_dots = mat("WpnStarDots", (1.0, 0.86, 0.6), force=True)
lobster = mat("WpnLobster", (0.85, 0.16, 0.1), force=True)
lobster_dark = mat("WpnLobsterDark", (0.55, 0.08, 0.06), force=True)
out = []


def yring(y, rx, rz, n, cx=0.0, cz=0.0, a0=0.0):
    return [(cx + rx * math.cos(a0 + i * TAU / n), y, cz + rz * math.sin(a0 + i * TAU / n)) for i in range(n)]


def grip(b, m, cap):
    b.box((0.034, 0.05, 0.13), (0, 0, 0), m, rot=(0.28, 0, 0))
    b.box((0.04, 0.058, 0.014), (0, 0.018, -0.0625), cap, rot=(0.28, 0, 0))


def trigger(b, guard, m):
    tube(b, [(0, -0.1, 0.075), (0, -0.098, 0.035), (0, -0.08, 0.014), (0, -0.055, 0.01), (0, -0.032, 0.02)], 0.0055, guard, segs=5)
    b.beam((0, -0.058, 0.075), (0, -0.064, 0.035), 0.007, m, segs=4)


def starfish(b, c, r, thick, up, m, dots):
    # Five fat arms in the plane at right angles to `up`, a bump in the middle, a few dots along the arms.
    up = Vector(up).normalized()
    side = up.orthogonal().normalized()
    for k in range(5):
        a = k * TAU / 5
        d = side * math.cos(a) + up.cross(side) * math.sin(a)
        tube(b, [Vector(c) + d * (r * t) for t in (0.0, 0.35, 0.7, 1.0)], [thick, thick * 0.85, thick * 0.55, 0.0], m, segs=5)
        for t in (0.35, 0.65):
            b.ico(thick * 0.28, Vector(c) + d * (r * t) + up * thick * 0.7, dots, subdiv=1)
    b.ico(thick * 1.1, c, m, subdiv=1)


# ---------- NetLauncher: a short wide-mouthed brass tube on a wooden stock, a balled net and red floats at the mouth ----------
g = Builder("NetLauncher", [wood, brass, rope, float_red, white, iron, dark])
grip(g, dark, iron)
trigger(g, iron, iron)
Z = 0.1
loft(g, [yring(y, r, r, 14, cz=Z) for y, r in ((0.09, 0.045), (0.07, 0.05), (-0.16, 0.06), (-0.24, 0.078), (-0.27, 0.085))], brass, caps=(iron, dark))  # the mouth (hidden behind the net)
g.cyl(0.07, 0.012, (0, -0.268, Z), iron, segs=14, axis="Y")                  # muzzle rim
for k in range(6):                                                              # floats round the rim
    a = k * TAU / 6
    g.ico(0.016, (0.082 * math.cos(a), -0.27, Z + 0.082 * math.sin(a)), float_red if k % 2 else white, subdiv=1)
g.ico(0.052, (0, -0.25, Z), rope, subdiv=2)                                    # the balled net in the mouth
for k in range(4):
    a = k * TAU / 4 + 0.4
    tube(g, [(0.04 * math.cos(a), -0.24, Z + 0.04 * math.sin(a)), (0.06 * math.cos(a), -0.29, Z + 0.06 * math.sin(a)),
             (0.05 * math.cos(a + 0.5), -0.31, Z + 0.05 * math.sin(a + 0.5))], 0.005, rope, segs=4)
g.box((0.05, 0.14, 0.05), (0, 0.13, Z - 0.02), wood, rot=(-0.15, 0, 0))       # stock
for y in (-0.05, 0.03):
    g.cyl(0.054, 0.014, (0, y, Z), iron, segs=14, axis="Y")
out.append(g.finish())

# ---------- NetBall (flying) and NetSpread (on the ground, 1 m across): rope web with red floats ----------
g = Builder("NetBall", [rope, float_red, white])
g.ico(0.18, (0, 0, 0), rope, subdiv=1)
for k in range(8):
    a = k * TAU / 8
    tube(g, [(0.17 * math.cos(a), 0.17 * math.sin(a), 0), (0.26 * math.cos(a + 0.2), 0.26 * math.sin(a + 0.2), 0.04 * (k % 3 - 1))], 0.012, rope, segs=4)
    g.ico(0.035, (0.27 * math.cos(a + 0.2), 0.27 * math.sin(a + 0.2), 0.04 * (k % 3 - 1)), float_red if k % 2 else white, subdiv=1)
out.append(g.finish())
g = Builder("NetSpread", [rope, float_red, white])
for k in range(12):                                                             # spokes
    a = k * TAU / 12
    g.beam((0, 0, 0.03), (0.5 * math.cos(a), 0.5 * math.sin(a), 0.0), 0.008, rope, segs=4)
for rr in (0.15, 0.3, 0.45):                                                    # rings (a 12-gon each)
    for k in range(12):
        a0, a1 = k * TAU / 12, (k + 1) * TAU / 12
        g.beam((rr * math.cos(a0), rr * math.sin(a0), 0.02 - rr * 0.04), (rr * math.cos(a1), rr * math.sin(a1), 0.02 - rr * 0.04), 0.007, rope, segs=4)
for k in range(12):
    a = (k + 0.5) * TAU / 12
    g.ico(0.03, (0.5 * math.cos(a), 0.5 * math.sin(a), 0.0), float_red if k % 2 else white, subdiv=1)
out.append(g.finish())

# ---------- JellyGun: a coral-pink body under a glass dome with a glowing jellyfish inside, a trumpet nozzle ----------
g = Builder("JellyGun", [coral, white, jelly, jelly_deep, brass, iron])
grip(g, iron, brass)
trigger(g, brass, iron)
Z = 0.095
loft(g, [yring(y, r, r * 1.1, 12, cz=Z) for y, r in ((0.11, 0.02), (0.1, 0.04), (0.0, 0.05), (-0.12, 0.045), (-0.17, 0.03))], coral)
g.cone(0.022, 0.05, 0.12, (0, -0.24, Z), brass, segs=12, axis="Y")            # trumpet nozzle
g.cyl(0.052, 0.006, (0, -0.302, Z), jelly, segs=12, axis="Y")
g.cyl(0.05, 0.02, (0, -0.02, Z + 0.05), brass, segs=12)                        # dome collar
loft(g, [[(r * math.cos(k * TAU / 12), -0.02 + r * math.sin(k * TAU / 12), z) for k in range(12)] for z, r in
         ((Z + 0.058, 0.048), (Z + 0.1, 0.05), (Z + 0.13, 0.04), (Z + 0.15, 0.022), (Z + 0.155, 0.004))], jelly)  # the glowing jelly dome
g.ico(0.03, (0, -0.02, Z + 0.1), jelly, subdiv=2)                              # the jellyfish inside
for k in range(4):
    a = k * TAU / 4
    tube(g, [(0.015 * math.cos(a), -0.02 + 0.015 * math.sin(a), Z + 0.085), (0.02 * math.cos(a + 0.4), -0.02 + 0.02 * math.sin(a + 0.4), Z + 0.07)],
         0.004, jelly_deep, segs=4)
out.append(g.finish())

# ---------- Jellyfish (flying, then hanging over the fish): a glowing bell with frilled rim and trailing tentacles ----------
g = Builder("Jellyfish", [jelly, jelly_deep, white])
loft(g, [[(r * math.cos(k * TAU / 14), r * math.sin(k * TAU / 14), z) for k in range(14)] for z, r in
         ((0.0, 0.26), (0.08, 0.25), (0.16, 0.2), (0.22, 0.12), (0.25, 0.03))], jelly, caps=(jelly_deep, jelly))
for k in range(14):                                                             # frills
    a = k * TAU / 14
    g.ico(0.03, (0.26 * math.cos(a), 0.26 * math.sin(a), -0.01), jelly_deep, subdiv=1)
for k in range(7):                                                              # tentacles
    a = k * TAU / 7 + 0.2
    rr = 0.12 + 0.06 * (k % 2)
    pts = [Vector((rr * math.cos(a), rr * math.sin(a), -0.02 - 0.12 * t)) + Vector((0.04 * math.sin(t * 5 + k), 0.04 * math.cos(t * 4 + k), 0)) for t in range(6)]
    tube(g, pts, [0.018, 0.015, 0.012, 0.009, 0.006, 0.0], jelly_deep if k % 2 else jelly, segs=4)
out.append(g.finish())

# ---------- WaterCannon: a blue pressure tank on top, a long brass nozzle, a pump grip under the barrel, glowing water window ----------
g = Builder("WaterCannon", [tank, brass, water, yellow, iron, white, black])
grip(g, iron, yellow)
trigger(g, yellow, iron)
Z = 0.09
g.cyl(0.03, 0.22, (0, -0.06, Z), yellow, segs=12, axis="Y")                    # body
g.beam((0, -0.17, Z), (0, -0.36, Z), 0.014, brass, segs=10)                    # nozzle
g.cone(0.014, 0.022, 0.04, (0, -0.375, Z), brass, segs=10, axis="Y")
g.cyl(0.018, 0.006, (0, -0.395, Z), water, segs=10, axis="Y")
g.cyl(0.055, 0.2, (0, -0.02, Z + 0.09), tank, segs=14, axis="Y")               # pressure tank
for y in (-0.11, 0.07):
    g.cyl(0.058, 0.016, (0, y, Z + 0.09), brass, segs=14, axis="Y")
g.box((0.02, 0.1, 0.03), (0.055, -0.02, Z + 0.09), water)                       # window onto the water
g.cyl(0.026, 0.05, (0, -0.2, Z - 0.03), iron, segs=10, axis="Y")               # pump grip under the barrel
g.cyl(0.03, 0.04, (0, -0.2, Z - 0.03), white, segs=10, axis="Y")
g.cyl(0.02, 0.012, (0.05, 0.05, Z + 0.15), white, segs=10, axis="X")           # pressure gauge
g.box((0.003, 0.004, 0.012), (0.057, 0.05, Z + 0.153), black, rot=(0.5, 0, 0))
out.append(g.finish())

# ---------- PufferBomb: a round pufferfish with spines, big eyes, a little mouth, a grenade pin and ring on its head ----------
g = Builder("PufferBomb", [puffer, puffer_belly, spine, black, white, steel, red])
g.ico(0.09, (0, 0, 0.09), puffer, subdiv=2)
g.ico(0.07, (0, -0.02, 0.06), puffer_belly, subdiv=2)
for k in range(18):                                                             # spines
    a, e = k * 2.39996, math.asin(-1 + 2 * (k + 0.5) / 18)
    d = Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)))
    if d.z < -0.6:
        continue
    tube(g, [Vector((0, 0, 0.09)) + d * 0.085, Vector((0, 0, 0.09)) + d * 0.135], [0.01, 0.0], spine, segs=4)
for s in (-1, 1):
    g.ico(0.022, (s * 0.045, -0.07, 0.12), white, subdiv=1)                     # eyes
    g.ico(0.011, (s * 0.048, -0.088, 0.122), black, subdiv=1)
g.cyl(0.016, 0.012, (0, -0.09, 0.075), red, segs=8, axis="Y")                  # mouth
g.cyl(0.012, 0.03, (0, 0.0, 0.19), steel, segs=8)                              # pin, ring
torus(g, (0.025, 0, 0.21), (1, 0, 0), 0.022, 0.004, steel, segs=12, tsegs=3)
g.box((0.03, 0.03, 0.01), (0, 0.06, 0.03), white)                               # where the hand holds it (under the back)
out.append(g.finish())

# ---------- StarThrower: a wrist launcher, a magazine of starfish standing on edge, a spring arm ----------
g = Builder("StarThrower", [navy, brass, star, star_dots, iron, white])
grip(g, iron, brass)
trigger(g, brass, iron)
Z = 0.09
g.box((0.07, 0.24, 0.04), (0, -0.07, Z), navy)                                 # rail
g.box((0.075, 0.03, 0.05), (0, -0.19, Z), brass)                               # spring head
for y in (-0.12, -0.08, -0.04, 0.0):                                            # the magazine: starfish on edge
    starfish(g, (0, y, Z + 0.06), 0.045, 0.012, (0, 1, 0), star, star_dots)
g.beam((0.035, 0.02, Z + 0.02), (0.035, -0.17, Z + 0.02), 0.005, iron, segs=4)
g.beam((-0.035, 0.02, Z + 0.02), (-0.035, -0.17, Z + 0.02), 0.005, iron, segs=4)
out.append(g.finish())

# ---------- Starfish (flying): flat, spinning, its top toward +Z ----------
g = Builder("Starfish", [star, star_dots])
starfish(g, (0, 0, 0), 0.16, 0.035, (0, 0, 1), star, star_dots)
out.append(g.finish())

# ---------- LobsterClaw: a big red claw on a handle: a fixed jaw and a hinged one, teeth, a shell cuff round the wrist ----------
g = Builder("LobsterClaw", [lobster, lobster_dark, white, iron])
grip(g, iron, lobster_dark)
Z = 0.09
loft(g, [yring(y, r, r * 0.8, 10, cz=Z) for y, r in ((0.08, 0.045), (0.02, 0.06), (-0.08, 0.075), (-0.15, 0.06))], lobster)  # cuff and palm
for s, top in ((1, True), (-1, False)):                                         # two jaws: a curved fat one and a thin one
    zc = Z + (0.025 if top else -0.03)
    pts = [Vector((0, -0.14, zc)), Vector((0.0, -0.22, zc + s * 0.025)), Vector((0, -0.3, zc + s * 0.02)), Vector((0, -0.36, zc - s * 0.012))]
    tube(g, pts, [0.05 if top else 0.032, 0.045 if top else 0.028, 0.032 if top else 0.02, 0.0], lobster if top else lobster_dark, segs=7)
    for k in range(4):                                                          # teeth
        y = -0.2 - 0.035 * k
        g.cone(0.008, 0.0, 0.02, (0, y, zc - s * 0.028), white, segs=4, axis="Z")
for y in (-0.03, 0.04):
    g.cyl(0.064, 0.012, (0, y, Z), lobster_dark, segs=10, axis="Y")
out.append(g.finish())

export(out)
