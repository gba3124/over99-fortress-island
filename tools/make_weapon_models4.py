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

iron, brass = (M(n) for n in ("Iron", "Brass"))
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
        b.cyl(0.0065, 0.004, (x, MUZZLE_Y - 0.001, zz), gunmetal_dark, segs=6, axis="Y")
    for y, d in ((-0.27, 0.012), (MUZZLE_Y + 0.03, 0.014)):
        b.cyl(CLUSTER_R, d, (0, y, z), gunmetal, segs=12, axis="Y")


# ---------- Gatling: a heavy receiver on a pistol grip, an electric motor at the back (cooling fins, a glowing ring), six
# barrels out front, a carry handle over it, a front handle and a guard ring round the barrels (both kept clear of the
# turning cluster: nothing but barrels inside BarrelRadius past BarrelFrom), and a big olive ammo box at its side feeding
# a brass belt up a chute ----------
g = Builder("Gatling", [gunmetal, gunmetal_dark, iron, brass, olive, glow])
grip(g, iron, gunmetal_dark)
trigger(g, brass, iron)
g.box((0.11, 0.2, 0.11), (0, RECEIVER_Y + 0.1, AXIS_Z), gunmetal)               # the receiver, its front at RECEIVER_Y
g.box((0.114, 0.06, 0.03), (0, 0.03, AXIS_Z + 0.06), gunmetal_dark)             # its top cover
g.box((0.116, 0.02, 0.116), (0, RECEIVER_Y + 0.012, AXIS_Z), gunmetal_dark)     # the front plate
g.cyl(0.056, 0.1, (0, 0.155, AXIS_Z), gunmetal_dark, segs=16, axis="Y")         # the motor
for y in (0.12, 0.14, 0.16, 0.18):                                              # its cooling fins
    g.cyl(0.064, 0.006, (0, y, AXIS_Z), gunmetal, segs=16, axis="Y")
torus(g, (0, 0.2, AXIS_Z), (0, 1, 0), 0.052, 0.008, glow, segs=18, tsegs=4)    # its glowing ring (the rarity's colour in the game)
g.cyl(0.034, 0.025, (0, 0.215, AXIS_Z), brass, segs=12, axis="Y")               # the cap at the back
barrels(g, AXIS_Z)
torus(g, (0, -0.3, AXIS_Z), (0, 1, 0), 0.07, 0.008, gunmetal_dark, segs=16, tsegs=4)  # guard ring round the barrels
for x in (-1, 1):                                                               # and its arms back to the receiver
    g.beam((x * 0.066, -0.3, AXIS_Z - 0.02), (x * 0.05, RECEIVER_Y + 0.02, AXIS_Z - 0.03), 0.006, gunmetal_dark, segs=4)
g.beam((0, -0.3, AXIS_Z - 0.07), (0, RECEIVER_Y + 0.02, AXIS_Z - 0.062), 0.007, gunmetal_dark, segs=4)
g.box((0.03, 0.035, 0.12), (0, -0.2, AXIS_Z - 0.125), iron, rot=(-0.15, 0, 0))   # front handle under the barrels
g.box((0.024, 0.06, 0.016), (0, -0.2, AXIS_Z - 0.068), gunmetal_dark)
tube(g, [(0, 0.08, AXIS_Z + 0.055), (0, 0.065, AXIS_Z + 0.12), (0, -0.06, AXIS_Z + 0.12), (0, -0.075, AXIS_Z + 0.055)], 0.012, iron, segs=6)  # carry handle
g.box((0.012, 0.025, 0.02), (0, -0.04, AXIS_Z + 0.138), brass)                  # a sight on it
g.box((0.09, 0.15, 0.11), (-0.115, 0.02, AXIS_Z - 0.04), olive)                 # the ammo box
g.box((0.094, 0.154, 0.014), (-0.115, 0.02, AXIS_Z + 0.017), gunmetal_dark)     # its lid
g.box((0.012, 0.05, 0.016), (-0.163, 0.02, AXIS_Z - 0.03), gunmetal_dark)       # its handle
g.box((0.004, 0.06, 0.03), (-0.161, 0.02, AXIS_Z - 0.065), brass)               # a stencilled plate
tube(g, [(-0.1, 0.0, AXIS_Z + 0.02), (-0.085, -0.01, AXIS_Z + 0.06), (-0.06, -0.02, AXIS_Z + 0.07), (-0.05, -0.03, AXIS_Z + 0.04)], 0.016, gunmetal_dark, segs=6)  # feed chute
for k in range(8):                                                              # the belt: rounds up out of the box into the chute
    t = k / 7
    x, y, z = -0.13 + 0.045 * t, 0.06 - 0.06 * t, AXIS_Z + 0.03 + 0.035 * math.sin(t * math.pi)
    g.box((0.012, 0.01, 0.024), (x, y, z), brass)
out.append(g.finish())

# ---------- GatlingBarrels: the same barrels, their axis at the origin ----------
g = Builder("GatlingBarrels", [gunmetal, gunmetal_dark, iron])
barrels(g, 0.0)
out.append(g.finish())

export(out)
