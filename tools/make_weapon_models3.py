# The rocket launcher everyone starts with, the rocket it fires, the repair tower's head, the stall helpers' basket and the
# absorber (a special weapon), built into fortress_models.blend and exported as OBJ.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_weapon_models3.py
# Hand-held: origin = centre of the grip, the business end toward -Y (the game's +Z), Z up (as make_weapon_models2.py).
# The rocket: origin in the middle, its nose toward -Y. The tower head: origin at its foot, on top of TurretBase (~1 m tall).
# The basket: origin at its bottom, 0.4 m across.
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bkit import Builder, M, export, loft, mat, torus, tube

iron, steel, brass, black, white = (M(n) for n in ("Iron", "Steel", "Brass", "BombBlack", "White"))
olive = mat("WpnOlive", (0.36, 0.42, 0.22), force=True)
olive_dark = mat("WpnOliveDark", (0.22, 0.26, 0.14), force=True)
warhead = mat("WpnWarhead", (0.9, 0.22, 0.12), force=True)
flame = mat("WpnRocketFlame", (1.0, 0.6, 0.15), (1.0, 0.45, 0.05), force=True)
mend = mat("MendGlow", (0.4, 1.0, 0.5), (0.25, 0.9, 0.35), force=True)
wicker = mat("WpnWicker", (0.72, 0.52, 0.26), force=True)
fishm = mat("WpnBasketFish", (0.95, 0.55, 0.25), force=True)
out = []


def grip(b, m, cap):
    b.box((0.034, 0.05, 0.13), (0, 0, 0), m, rot=(0.28, 0, 0))
    b.box((0.04, 0.058, 0.014), (0, 0.018, -0.0625), cap, rot=(0.28, 0, 0))


def trigger(b, guard, m):
    tube(b, [(0, -0.1, 0.075), (0, -0.098, 0.035), (0, -0.08, 0.014), (0, -0.055, 0.01), (0, -0.032, 0.02)], 0.0055, guard, segs=5)
    b.beam((0, -0.058, 0.075), (0, -0.064, 0.035), 0.007, m, segs=4)


def rocket(b, y0, length, r, body, nose, fins):
    # Nose toward -Y from y0: a cone of warhead, a body, three fins at the back.
    b.cone(r, 0.0, length * 0.32, (0, y0 - length * 0.16, 0), nose, segs=10, axis="Y")
    b.cyl(r, length * 0.55, (0, y0 + length * 0.275, 0), body, segs=10, axis="Y")
    for k in range(3):
        a = k * 2 * math.pi / 3
        d = (math.cos(a), 0, math.sin(a))
        b.beam((d[0] * r, y0 + length * 0.5, d[2] * r), (d[0] * r * 2.1, y0 + length * 0.62, d[2] * r * 2.1), r * 0.25, fins, segs=4)


# ---------- Rocket: a long olive launch tube over the shoulder (rear exhaust bell, a shoulder pad), a pistol grip and a front
# grip under it, flip-up iron sights on top, a red warhead seated in the muzzle. Tube axis at z = Z; the muzzle ring's centre is
# WeaponItems.Spec(Rocket).Muzzle ----------
g = Builder("Rocket", [olive, olive_dark, warhead, iron, black, white])
grip(g, iron, olive_dark)
trigger(g, iron, iron)
Z, R, FRONT, BACK = 0.13, 0.064, -0.41, 0.26
g.cyl(R, BACK - FRONT - 0.06, (0, (FRONT + BACK - 0.06) / 2, Z), olive, segs=16, axis="Y")      # the tube
g.cyl(R + 0.008, 0.035, (0, FRONT + 0.0175, Z), olive_dark, segs=16, axis="Y")                 # muzzle ring
g.cyl(R - 0.006, 0.004, (0, FRONT - 0.001, Z), black, segs=16, axis="Y")                       # the bore round the warhead
loft(g, [[(r * math.cos(k * math.tau / 16), y, Z + r * math.sin(k * math.tau / 16)) for k in range(16)] for y, r in
         ((BACK - 0.08, R), (BACK - 0.04, R + 0.008), (BACK, R + 0.036), (BACK + 0.014, R + 0.038))], olive_dark, caps=(olive_dark, black))  # exhaust bell
g.cyl(R + 0.024, 0.004, (0, BACK + 0.01, Z), black, segs=16, axis="Y")                         # its dark throat
for y in (-0.3, -0.06, 0.12):                                                                  # straps
    g.cyl(R + 0.004, 0.022, (0, y, Z), olive_dark, segs=16, axis="Y")
g.cyl(R + 0.0045, 0.012, (0, -0.2, Z), white, segs=16, axis="Y")                               # a stencil stripe
g.cyl(R - 0.008, 0.05, (0, FRONT - 0.02, Z), warhead, segs=12, axis="Y")                       # the warhead seated in the muzzle
g.cone(R - 0.008, 0.012, 0.085, (0, FRONT - 0.087, Z), warhead, segs=12, axis="Y")
g.cyl(R - 0.0075, 0.01, (0, FRONT - 0.04, Z), white, segs=12, axis="Y")
g.box((0.05, 0.13, 0.03), (0, 0.17, Z - R - 0.008), black)                                     # shoulder pad
g.box((0.04, 0.06, 0.02), (0, 0.0, Z - R - 0.005), iron)                                       # the grip's mount
g.box((0.03, 0.04, 0.11), (0, -0.2, Z - R - 0.05), olive_dark, rot=(-0.18, 0, 0))              # front grip
g.box((0.036, 0.046, 0.012), (0, -0.21, Z - R - 0.106), iron, rot=(-0.18, 0, 0))
g.box((0.012, 0.012, 0.04), (0, -0.36, Z + R + 0.016), iron)                                   # front post on its block
g.box((0.024, 0.03, 0.012), (0, -0.36, Z + R + 0.002), iron)
g.box((0.024, 0.03, 0.012), (0, 0.04, Z + R + 0.002), iron)                                     # rear aperture, flipped up
torus(g, (0, 0.04, Z + R + 0.036), (0, 1, 0), 0.014, 0.0045, iron, segs=10, tsegs=4)
g.box((0.006, 0.006, 0.02), (0, 0.04, Z + R + 0.014), iron)
g.box((0.03, 0.05, 0.03), (-R - 0.012, 0.08, Z), olive_dark)                                    # the firing box at the side
g.box((0.008, 0.02, 0.008), (-R - 0.03, 0.08, Z + 0.005), white)
out.append(g.finish())

# ---------- RocketShell (flying): a red-nosed rocket with fins and a glowing flame at the back ----------
g = Builder("RocketShell", [olive, warhead, iron, flame, white])
rocket(g, -0.2, 0.5, 0.055, olive, warhead, iron)
g.cyl(0.056, 0.03, (0, -0.15, 0), white, segs=10, axis="Y")
g.cone(0.04, 0.0, 0.14, (0, 0.145, 0), flame, segs=8, axis="-Y")             # the flame, its tip toward the back
out.append(g.finish())

# ---------- MenderHead: a column with a big glowing green cross on a white plate, a wrench across it, lamps round the foot ----------
g = Builder("MenderHead", [iron, brass, white, mend, steel])
g.cyl(0.32, 0.12, (0, 0, 0.06), iron, segs=10)
g.beam((0, 0, 0.12), (0, 0, 0.5), 0.07, brass, segs=8)
g.box((0.62, 0.06, 0.62), (0, 0, 0.82), white)                                # the plate
g.box((0.5, 0.14, 0.15), (0, 0, 0.82), mend)                                  # the cross, through both faces
g.box((0.15, 0.14, 0.5), (0, 0, 0.82), mend)
g.beam((-0.34, -0.1, 0.36), (0.3, -0.1, 0.58), 0.03, steel, segs=6)           # a wrench across the column
torus(g, (0.33, -0.1, 0.59), (0, 1, 0), 0.06, 0.02, steel, segs=10, tsegs=3)
g.box((0.06, 0.05, 0.1), (-0.36, -0.1, 0.35), steel)
for k in range(4):                                                            # lamps round the foot
    a = math.pi / 4 + k * math.pi / 2
    g.ico(0.05, (0.26 * math.cos(a), 0.26 * math.sin(a), 0.15), mend, subdiv=1)
out.append(g.finish())

# ---------- HelperBasket: a wicker basket heaped with fish, a rope handle over it ----------
g = Builder("HelperBasket", [wicker, fishm, black, iron])
g.cone(0.15, 0.2, 0.2, (0, 0, 0.1), wicker, segs=14)
torus(g, (0, 0, 0.2), (0, 0, 1), 0.2, 0.016, wicker, segs=18, tsegs=4)
for k, (x, y, z, yaw) in enumerate(((-0.06, -0.04, 0.22, 0.3), (0.07, 0.03, 0.23, 2.0), (0.0, 0.07, 0.27, 4.0), (0.02, -0.06, 0.28, 5.4))):
    d = (math.cos(yaw), math.sin(yaw), 0.15)
    head, tail = (x + d[0] * 0.09, y + d[1] * 0.09, z + 0.01), (x - d[0] * 0.09, y - d[1] * 0.09, z - 0.01)
    g.beam(head, (x, y, z), 0.045, fishm, segs=6, r2=0.012)
    g.beam(tail, (x, y, z), 0.045, fishm, segs=6, r2=0.008)
    g.beam(tail, (x - d[0] * 0.15, y - d[1] * 0.15, z + 0.03), 0.03, fishm, segs=4, r2=0.0)
    g.ico(0.012, (head[0] - d[1] * 0.025, head[1] + d[0] * 0.025, head[2] + 0.015), black, subdiv=1)
tube(g, [(-0.19, 0, 0.2), (-0.14, 0, 0.42), (0.14, 0, 0.42), (0.19, 0, 0.2)], 0.012, wicker, segs=5)
out.append(g.finish())

# ---------- Absorber: a fat slate body, a wide steel-rimmed intake round a glowing void at the front, a chamber on top (two steel hoops on a collar:
# the glass and what's in it are added in the game, at Absorber.cs DomeAt = (0, DOME_Z, -DOME_Y), DomeR = DOME_R), a hose
# from the mouth up into it ----------
shell = mat("WpnAbsorbShell", (0.22, 0.24, 0.3), force=True)
shell_dark = mat("WpnAbsorbShellDark", (0.12, 0.12, 0.17), force=True)
void = mat("WpnAbsorbVoid", (0.1, 0.02, 0.16), (0.32, 0.08, 0.55), force=True)
ring = mat("WpnAbsorbRing", (0.7, 0.74, 0.8), force=True)
DOME_Y, DOME_Z, DOME_R = -0.02, 0.21, 0.055
g = Builder("Absorber", [shell, shell_dark, ring, iron, void])
grip(g, iron, shell_dark)
trigger(g, ring, iron)
Z = 0.11
g.cyl(0.052, 0.27, (0, -0.005, Z), shell, segs=14, axis="Y")                   # the body
g.ico(0.052, (0, 0.13, Z), shell, subdiv=2)                                    # its round back
g.cone(0.052, 0.1, 0.11, (0, -0.195, Z), shell_dark, segs=16, axis="Y")       # the intake, widening to the front
g.cyl(0.088, 0.006, (0, -0.247, Z), void, segs=16, axis="Y")                # the void inside it
g.ico(0.05, (0, -0.225, Z), void, subdiv=2)                                   # its core, bulging out of the dark
torus(g, (0, -0.25, Z), (0, 1, 0), 0.1, 0.012, ring, segs=18, tsegs=4)       # its steel rim
for y in (-0.09, 0.07):                                                       # steel bands
    g.cyl(0.055, 0.016, (0, y, Z), ring, segs=14, axis="Y")
g.cyl(0.05, 0.03, (0, DOME_Y, 0.165), ring, segs=14)                         # the chamber's collar
torus(g, (0, DOME_Y, 0.18), (0, 0, 1), DOME_R, 0.008, ring, segs=16, tsegs=4)
for ax in ((1, 0), (0, 1)):                                                   # two hoops over the glass
    arc = [(ax[0] * DOME_R * math.cos(t), DOME_Y + ax[1] * DOME_R * math.cos(t), DOME_Z + DOME_R * math.sin(t)) for t in (math.pi * k / 8 for k in range(9))]
    tube(g, arc, 0.004, ring, segs=4)
g.ico(0.012, (0, DOME_Y, DOME_Z + DOME_R + 0.008), ring, subdiv=1)           # a knob on top
tube(g, [(0.07, -0.2, Z), (0.08, -0.15, Z + 0.035), (0.06, -0.08, 0.175), (0.035, DOME_Y - 0.03, 0.19)], 0.01, shell_dark, segs=5)  # the hose
g.box((0.03, 0.08, 0.035), (0, -0.12, Z - 0.068), shell_dark)                  # front handle under the body
out.append(g.finish())

export(out)
