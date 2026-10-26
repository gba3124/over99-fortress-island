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


def starfish(b, c, r, thick, up, m, dots, at=(0.35, 0.65)):
    # Five fat arms in the plane at right angles to `up`, a bump in the middle, a few dots along the arms.
    up = Vector(up).normalized()
    side = up.orthogonal().normalized()
    for k in range(5):
        a = k * TAU / 5
        d = side * math.cos(a) + up.cross(side) * math.sin(a)
        tube(b, [Vector(c) + d * (r * t) for t in (0.0, 0.35, 0.7, 1.0)], [thick, thick * 0.85, thick * 0.55, 0.0], m, segs=5)
        for t in at:
            b.ico(thick * 0.28, Vector(c) + d * (r * t) + up * thick * 0.7, dots, subdiv=1)
    b.ico(thick * 1.1, c, m, subdiv=1)


# ---------- NetLauncher: a wide brass net canister on a wooden stock, the balled net and red floats at its mouth, a compressed-air
# bottle along its side (the holder's side: +X here, the game flips x) piped into the breech ----------
g = Builder("NetLauncher", [wood, brass, rope, float_red, white, iron])
grip(g, wood, iron)
trigger(g, iron, iron)
Z = 0.1
loft(g, [yring(y, r, r, 16, cz=Z) for y, r in ((0.09, 0.042), (0.07, 0.047), (-0.12, 0.05), (-0.16, 0.08), (-0.18, 0.088), (-0.3, 0.092), (-0.31, 0.088))],
     brass, caps=(iron, iron))                                                  # breech, barrel and the wide canister (its mouth behind the net)
for y in (-0.19, -0.25):
    g.cyl(0.095, 0.012, (0, y, Z), iron, segs=16, axis="Y")
g.cyl(0.098, 0.014, (0, -0.305, Z), iron, segs=16, axis="Y")                   # muzzle rim
for k in range(8):                                                              # floats round the rim
    a = k * TAU / 8
    g.ico(0.017, (0.095 * math.cos(a), -0.308, Z + 0.095 * math.sin(a)), float_red if k % 2 else white, subdiv=1)
g.ico(0.068, (0, -0.29, Z), rope, subdiv=2)                                    # the balled net in the mouth
for k in range(5):
    a = k * TAU / 5 + 0.4
    tube(g, [(0.05 * math.cos(a), -0.3, Z + 0.05 * math.sin(a)), (0.075 * math.cos(a), -0.315, Z + 0.075 * math.sin(a)),
             (0.07 * math.cos(a + 0.4), -0.312, Z + 0.07 * math.sin(a + 0.4))], 0.005, rope, segs=4)
g.box((0.05, 0.14, 0.05), (0, 0.13, Z - 0.02), wood, rot=(-0.15, 0, 0))       # stock
g.box((0.054, 0.02, 0.064), (0, 0.2, Z - 0.03), iron, rot=(-0.15, 0, 0))
for y in (-0.05, 0.03):
    g.cyl(0.052, 0.014, (0, y, Z), iron, segs=14, axis="Y")
g.cyl(0.026, 0.15, (0.07, -0.06, Z - 0.02), white, segs=12, axis="Y")          # the air bottle, a red band, domed ends
g.cyl(0.027, 0.03, (0.07, -0.1, Z - 0.02), float_red, segs=12, axis="Y")
g.ico(0.026, (0.07, -0.135, Z - 0.02), white, subdiv=2)
g.ico(0.026, (0.07, 0.015, Z - 0.02), white, subdiv=2)
for x in (0.045, 0.0):
    g.box((0.035 if x else 0.02, 0.012, 0.02), (0.057 if x else 0.04, x and -0.09 or -0.02, Z - 0.02), iron)  # straps
g.cyl(0.01, 0.02, (0.07, 0.045, Z - 0.02), iron, segs=8, axis="Y")            # its valve and the pipe into the breech
tube(g, [(0.07, 0.055, Z - 0.02), (0.06, 0.075, Z - 0.01), (0.04, 0.08, Z)], 0.006, iron, segs=5)
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

# ---------- WaterCannon: a big blue pressure tank on top, a hose from it down to a wide flat high-pressure nozzle, a pump grip
# under the barrel, a glowing water window and a gauge ----------
g = Builder("WaterCannon", [tank, brass, water, yellow, iron, white])
grip(g, iron, yellow)
trigger(g, yellow, iron)
Z = 0.09
g.cyl(0.034, 0.25, (0, -0.045, Z), yellow, segs=12, axis="Y")                  # body
g.cyl(0.03, 0.02, (0, 0.09, Z), brass, segs=12, axis="Y")
g.cyl(0.018, 0.14, (0, -0.24, Z), brass, segs=10, axis="Y")                    # the nozzle's pipe
for y in (-0.19, -0.29):
    g.cyl(0.023, 0.014, (0, y, Z), brass, segs=10, axis="Y")
loft(g, [yring(y, rx, rz, 14, cz=Z) for y, rx, rz in ((-0.305, 0.022, 0.022), (-0.33, 0.034, 0.024), (-0.37, 0.056, 0.02), (-0.39, 0.064, 0.022),
                                                       (-0.398, 0.06, 0.018))], brass, caps=(brass, water))  # wide flat fan nozzle, glowing slot
g.cyl(0.022, 0.06, (0, -0.2, Z - 0.045), white, segs=10, axis="Y")             # pump grip under the barrel
for y in (-0.22, -0.2, -0.18):
    g.cyl(0.0235, 0.006, (0, y, Z - 0.045), iron, segs=10, axis="Y")
g.box((0.012, 0.03, 0.03), (0, -0.2, Z - 0.022), iron)
g.cyl(0.062, 0.22, (0, -0.01, Z + 0.1), tank, segs=16, axis="Y")               # pressure tank, domed at both ends
for y in (-0.12, 0.1):
    g.cyl(0.065, 0.016, (0, y, Z + 0.1), brass, segs=16, axis="Y")
g.cone(0.062, 0.03, 0.03, (0, -0.135, Z + 0.1), tank, segs=16, axis="Y")
g.cone(0.03, 0.062, 0.03, (0, 0.115, Z + 0.1), tank, segs=16, axis="Y")
g.box((0.012, 0.12, 0.034), (0.06, -0.01, Z + 0.1), water)                       # window onto the water
for y in (-0.04, 0.03):
    g.box((0.03, 0.014, 0.05), (0, y, Z + 0.045), iron)                         # the tank's clamps
tube(g, [(0, -0.15, Z + 0.1), (-0.02, -0.18, Z + 0.09), (-0.045, -0.2, Z + 0.06), (-0.04, -0.24, Z + 0.03), (-0.015, -0.27, Z + 0.016)], 0.011, iron, segs=6)  # hose
g.cyl(0.024, 0.012, (0, 0.135, Z + 0.1), white, segs=12, axis="Y")             # gauge on the back of the tank
g.box((0.004, 0.003, 0.016), (0.004, 0.142, Z + 0.105), iron, rot=(0, 0.6, 0))
g.beam((0, 0.1, Z + 0.16), (0, 0.1, Z + 0.19), 0.006, iron, segs=4)            # the pump handle on top
g.cyl(0.008, 0.06, (0, 0.1, Z + 0.19), yellow, segs=6, axis="X")
out.append(g.finish())

# ---------- PufferBomb: a round pufferfish with spines, big eyes, a little mouth, a grenade pin and ring on its head ----------
g = Builder("PufferBomb", [puffer, puffer_belly, spine, black, steel, red])
g.ico(0.09, (0, 0, 0.09), puffer, subdiv=2)
g.ico(0.07, (0, -0.02, 0.06), puffer_belly, subdiv=2)
for k in range(18):                                                             # spines
    a, e = k * 2.39996, math.asin(-1 + 2 * (k + 0.5) / 18)
    d = Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)))
    if d.z < -0.6:
        continue
    tube(g, [Vector((0, 0, 0.09)) + d * 0.085, Vector((0, 0, 0.09)) + d * 0.135], [0.01, 0.0], spine, segs=4)
for s in (-1, 1):
    g.ico(0.022, (s * 0.045, -0.07, 0.12), puffer_belly, subdiv=1)                     # eyes
    g.ico(0.011, (s * 0.048, -0.088, 0.122), black, subdiv=1)
g.cyl(0.016, 0.012, (0, -0.09, 0.075), red, segs=8, axis="Y")                  # mouth
g.cyl(0.012, 0.03, (0, 0.0, 0.19), steel, segs=8)                              # pin, ring
torus(g, (0.025, 0, 0.21), (1, 0, 0), 0.022, 0.004, steel, segs=12, tsegs=3)
g.box((0.03, 0.03, 0.01), (0, 0.06, 0.03), puffer_belly)                               # where the hand holds it (under the back)
out.append(g.finish())

# ---------- StarThrower: a wrist launcher, a magazine of starfish standing on edge, a spring arm ----------
g = Builder("StarThrower", [navy, brass, star, star_dots, iron, white])
grip(g, iron, brass)
trigger(g, brass, iron)
Z = 0.09
g.box((0.07, 0.24, 0.04), (0, -0.07, Z), navy)                                 # rail
g.box((0.075, 0.03, 0.05), (0, -0.19, Z), brass)                               # spring head
for y in (-0.12, -0.08, -0.04, 0.0):                                            # the magazine: starfish on edge
    starfish(g, (0, y, Z + 0.06), 0.045, 0.012, (0, 1, 0), star, star_dots, at=(0.5,))  # one dot an arm: the tris budget
g.beam((0.035, 0.02, Z + 0.02), (0.035, -0.17, Z + 0.02), 0.005, iron, segs=4)
g.beam((-0.035, 0.02, Z + 0.02), (-0.035, -0.17, Z + 0.02), 0.005, iron, segs=4)
out.append(g.finish())

# ---------- Starfish (flying): flat, spinning, its top toward +Z ----------
g = Builder("Starfish", [star, star_dots])
starfish(g, (0, 0, 0), 0.16, 0.035, (0, 0, 1), star, star_dots)
out.append(g.finish())

# ---------- LobsterClaw: a lobster's pincer on a handle: a segmented shell cuff, a fat palm, the fixed finger below and the hinged
# finger above it opened (the muzzle is in the gap between their tips), knobbly inner edges, a dark hinge knuckle ----------
g = Builder("LobsterClaw", [lobster, lobster_dark, white, iron])
grip(g, iron, lobster_dark)
trigger(g, lobster_dark, iron)
Z = 0.09
loft(g, [yring(y, r, r * 0.85, 12, cz=Z) for y, r in ((0.09, 0.04), (0.06, 0.05), (0.0, 0.054), (-0.05, 0.056))], lobster,
     mf=lambda i, j: lobster_dark if i == 1 else lobster)                       # the cuff, a darker joint ring
loft(g, [yring(y, rx, rz, 12, cz=Z + dz) for y, rx, rz, dz in ((-0.05, 0.05, 0.05, 0.0), (-0.08, 0.06, 0.072, 0.0), (-0.14, 0.062, 0.078, 0.002),
                                                             (-0.19, 0.05, 0.068, 0.004), (-0.215, 0.034, 0.05, 0.0))], lobster)  # the palm
for y, dz in ((-0.1, 0.07), (-0.15, 0.074), (-0.125, -0.07)):
    g.ico(0.01, (0, y, Z + dz), lobster_dark, subdiv=1)                         # bumps on the shell


def finger(pts, rs, m, inward):
    tube(g, [Vector(p) for p in pts], rs, m, segs=8)
    for k in (1, 2, 3):                                                         # knobs along the inner edge
        a, b = Vector(pts[k]), Vector(pts[k + 1])
        g.ico(0.008, a.lerp(b, 0.5) + Vector((0, 0, inward * rs[k] * 0.8)), white, subdiv=1)


finger([(0, -0.18, Z - 0.03), (0, -0.23, Z - 0.035), (0, -0.29, Z - 0.03), (0, -0.34, Z - 0.015), (0, -0.37, Z + 0.008)],
       [0.032, 0.028, 0.022, 0.014, 0.0], lobster, 1)                           # the fixed finger, its tip hooking up
finger([(0, -0.18, Z + 0.035), (0, -0.23, Z + 0.06), (0, -0.29, Z + 0.07), (0, -0.335, Z + 0.06), (0, -0.365, Z + 0.035)],
       [0.03, 0.026, 0.02, 0.012, 0.0], lobster_dark, -1)                       # the hinged finger, opened, its tip hooking down
g.cyl(0.02, 0.07, (0, -0.18, Z + 0.035), lobster_dark, segs=10, axis="X")      # the hinge knuckle
out.append(g.finish())

export(out)
