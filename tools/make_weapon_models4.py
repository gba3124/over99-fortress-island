# The gatling (a special weapon, sold once the wave 80 boss is beaten): built into fortress_models.blend and exported as OBJ.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_weapon_models4.py
# Hand-held: origin = centre of the grip, the business end toward -Y (the game's +Z), Z up (as make_weapon_models3.py).
# Gatling: the whole gun (the hotbar, a drop, the icon). GatlingBarrels: its barrel cluster alone, on its own axis at the origin:
# the game cuts the barrels out of the whole gun's mesh and turns this in their place (WeaponItems.Gatling: GatlingAxis =
# (0, AXIS_Z, 0), BarrelFrom between RECEIVER_Y and HUB_Y, BarrelRadius over CLUSTER_R).
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bkit import Builder, M, export, mat, torus, tube

iron, brass, black = (M(n) for n in ("Iron", "Brass", "BombBlack"))
gunmetal = mat("WpnGatlingMetal", (0.3, 0.32, 0.36), force=True)
gunmetal_dark = mat("WpnGatlingDark", (0.14, 0.15, 0.17), force=True)
olive = mat("WpnOlive", (0.36, 0.42, 0.22), force=True)
glow = mat("WpnGatlingGlow", (1.0, 0.55, 0.18), (1.0, 0.45, 0.1), force=True)
AXIS_Z, RECEIVER_Y, HUB_Y, MUZZLE_Y, CLUSTER_R = 0.11, -0.095, -0.105, -0.44, 0.047
out = []


def grip(b, m, cap):
    b.box((0.034, 0.05, 0.13), (0, 0, 0), m, rot=(0.28, 0, 0))
    b.box((0.04, 0.058, 0.014), (0, 0.018, -0.0625), cap, rot=(0.28, 0, 0))


def trigger(b, guard, m):
    # make_weapon_models3.py's, 8 mm back: nothing of the gun but its barrels may reach past RECEIVER_Y.
    tube(b, [(0, -0.092, 0.075), (0, -0.09, 0.035), (0, -0.072, 0.014), (0, -0.047, 0.01), (0, -0.024, 0.02)], 0.0055, guard, segs=5)
    b.beam((0, -0.05, 0.075), (0, -0.056, 0.035), 0.007, m, segs=4)


def barrels(b, z):
    # Six barrels round a rod from the hub to the muzzle, held by two clamps; each barrel's bore dark at the front.
    length = HUB_Y - 0.03 - MUZZLE_Y
    mid = MUZZLE_Y + length / 2
    b.cyl(0.045, 0.03, (0, HUB_Y - 0.015, z), gunmetal_dark, segs=12, axis="Y")     # the hub
    b.cyl(0.01, length - 0.02, (0, mid + 0.01, z), iron, segs=6, axis="Y")         # the rod
    for k in range(6):
        a = k * math.pi / 3
        x, zz = 0.03 * math.cos(a), z + 0.03 * math.sin(a)
        b.cyl(0.011, length, (x, mid, zz), iron, segs=8, axis="Y")
        b.cyl(0.0065, 0.004, (x, MUZZLE_Y - 0.001, zz), black, segs=6, axis="Y")
    for y, d in ((-0.27, 0.012), (MUZZLE_Y + 0.03, 0.014)):
        b.cyl(CLUSTER_R, d, (0, y, z), gunmetal, segs=12, axis="Y")


# ---------- Gatling: a squat receiver on a pistol grip, a motor at the back with a glowing ring, six barrels out front, a
# carry handle over it and an olive ammo box at its side feeding a brass belt in ----------
g = Builder("Gatling", [gunmetal, gunmetal_dark, iron, brass, black, olive, glow])
grip(g, iron, gunmetal_dark)
trigger(g, brass, iron)
g.box((0.1, 0.19, 0.1), (0, RECEIVER_Y + 0.095, AXIS_Z), gunmetal)              # the receiver, its front at RECEIVER_Y
g.box((0.104, 0.05, 0.03), (0, 0.02, AXIS_Z + 0.055), gunmetal_dark)            # its top cover
g.cyl(0.048, 0.07, (0, 0.13, AXIS_Z), gunmetal_dark, segs=14, axis="Y")         # the motor
torus(g, (0, 0.13, AXIS_Z), (0, 1, 0), 0.05, 0.008, glow, segs=18, tsegs=4)    # its glowing ring (the rarity's colour in the game)
g.cyl(0.03, 0.02, (0, 0.175, AXIS_Z), brass, segs=12, axis="Y")                 # the cap at the back
barrels(g, AXIS_Z)
tube(g, [(0, 0.06, AXIS_Z + 0.05), (0, 0.045, AXIS_Z + 0.105), (0, -0.07, AXIS_Z + 0.105), (0, -0.085, AXIS_Z + 0.05)], 0.011, iron, segs=6)  # carry handle
g.box((0.012, 0.025, 0.02), (0, -0.05, AXIS_Z + 0.122), brass)                  # a sight on it
g.box((0.07, 0.1, 0.085), (-0.095, 0.02, AXIS_Z - 0.035), olive)                # the ammo box
g.box((0.074, 0.104, 0.012), (-0.095, 0.02, AXIS_Z + 0.008), gunmetal_dark)     # its lid
for k in range(7):                                                              # the belt: rounds up out of the box into the side
    t = k / 6
    x, y, z = -0.095 + 0.05 * t, 0.02 - 0.03 * t, AXIS_Z + 0.02 + 0.025 * math.sin(t * math.pi)
    g.box((0.012, 0.01, 0.022), (x, y, z), brass)
out.append(g.finish())

# ---------- GatlingBarrels: the same barrels, their axis at the origin ----------
g = Builder("GatlingBarrels", [gunmetal, gunmetal_dark, iron, black])
barrels(g, 0.0)
out.append(g.finish())

export(out)
