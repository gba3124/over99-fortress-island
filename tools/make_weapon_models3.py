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
from mathutils import Vector
from bkit import Builder, M, export, fin, loft, mat, torus, tube

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


def warhead_rings(y0, r_bore, z):
    # A PG-7-style warhead pointing to -Y from its seat at y0: a boat tail inside the bore, a bulge wider than the tube, an
    # ogive to a point. (y, r) pairs; the bulge's shoulder is index 1 (a white band), the rest the warhead's red.
    prof = ((y0 + 0.02, r_bore), (y0 - 0.01, 0.040), (y0 - 0.04, 0.058), (y0 - 0.07, 0.060), (y0 - 0.10, 0.052), (y0 - 0.13, 0.034), (y0 - 0.145, 0.014))
    return [[(r * math.cos(k * math.tau / 12), y, z + r * math.sin(k * math.tau / 12)) for k in range(12)] for y, r in prof] + [[(0, y0 - 0.152, z)]]


# ---------- Rocket: a shoulder-fired launcher, long and slender (Owen: the round-2 one read as a toy): an olive tube 78 cm
# long and 9 cm through, a flared exhaust bell with a dark throat at the back, a padded shoulder rest and a heat cuff over
# the shoulder, the pistol grip and trigger under the middle, a front grip, flip-up iron sights (a bladed post at the front,
# a leaf with a ghost ring at the back), the firing box and its cable on the side the holder sees, and an oversized red
# warhead seated in the muzzle. Tube axis at z = Z, x = 0; the tube's front face (y = FRONT) is WeaponItems.Spec(Rocket).Muzzle ----------
g = Builder("Rocket", [olive, olive_dark, warhead, iron, black, white])
grip(g, iron, olive_dark)
trigger(g, iron, iron)
Z, R, FRONT, BACK, N = 0.125, 0.046, -0.50, 0.28, 16
g.cyl(R, (BACK - 0.10) - (FRONT + 0.03), (0, (BACK - 0.10 + FRONT + 0.03) / 2, Z), olive, segs=N, axis="Y")   # the tube
g.cyl(R + 0.007, 0.05, (0, FRONT + 0.025, Z), olive_dark, segs=N, axis="Y")                                     # muzzle collar
g.cyl(R - 0.004, 0.004, (0, FRONT - 0.001, Z), black, segs=N, axis="Y")                                         # the bore round the warhead
loft(g, warhead_rings(FRONT, R - 0.012, Z), warhead, caps=(olive_dark, warhead), mf=lambda i, j: olive_dark if i == 0 else white if i == 1 else warhead)
g.cyl(0.006, 0.016, (0, FRONT - 0.158, Z), iron, segs=6, axis="Y")                                               # the fuze cap on its nose
loft(g, [[(r * math.cos(k * math.tau / N), y, Z + r * math.sin(k * math.tau / N)) for k in range(N)] for y, r in
         ((BACK - 0.10, R), (BACK - 0.07, R + 0.002), (BACK - 0.03, R + 0.018), (BACK - 0.005, R + 0.034), (BACK, R + 0.036))],
     olive_dark, caps=(olive_dark, black))                                                                      # exhaust bell, its throat dark
for y in (-0.40, -0.17, 0.06):                                                                                  # cuffs round the tube
    g.cyl(R + 0.004, 0.02, (0, y, Z), olive_dark, segs=N, axis="Y")
g.cyl(R + 0.003, 0.012, (0, -0.29, Z), white, segs=N, axis="Y")                                                 # a stencil stripe
g.cyl(R + 0.003, 0.016, (0, BACK - 0.13, Z), black, segs=N, axis="Y")                                           # the danger band before the bell
g.cyl(R + 0.012, 0.12, (0, 0.16, Z), black, segs=N, axis="Y")                                                   # heat cuff where it rides the shoulder
g.box((0.03, 0.03, 0.045), (0, 0.19, Z - R - 0.02), iron)                                                       # shoulder rest: bracket and pad
g.box((0.055, 0.13, 0.028), (0, 0.19, Z - R - 0.052), black)
g.box((0.04, 0.09, 0.028), (0, -0.02, 0.068), olive_dark)                                                       # trigger housing, grip to tube
g.box((0.03, 0.04, 0.10), (0, -0.26, Z - R - 0.048), olive_dark, rot=(-0.2, 0, 0))                              # front grip
g.box((0.034, 0.046, 0.012), (0, -0.27, Z - R - 0.1), iron, rot=(-0.2, 0, 0))
g.box((0.024, 0.03, 0.012), (0, -0.42, Z + R + 0.005), iron)                                                    # front sight: hinge block, blade, a white dot
g.box((0.006, 0.014, 0.05), (0, -0.42, Z + R + 0.035), iron)
g.box((0.008, 0.004, 0.006), (0, -0.4105, Z + R + 0.054), white)
g.box((0.03, 0.03, 0.012), (0, 0.03, Z + R + 0.005), iron)                                                      # rear sight: hinge block, leaf, ghost ring
g.box((0.034, 0.004, 0.03), (0, 0.03, Z + R + 0.026), iron)
for z in (0.02, 0.03):
    g.box((0.02, 0.005, 0.003), (0, 0.03, Z + R + z), white)
torus(g, (0, 0.03, Z + R + 0.052), (0, 1, 0), 0.011, 0.0035, iron, segs=10, tsegs=4)
g.box((0.028, 0.06, 0.036), (R + 0.01, -0.04, Z - 0.01), olive_dark)                                            # firing box on the holder's side
g.box((0.004, 0.03, 0.012), (R + 0.025, -0.04, Z), white)
tube(g, [(R + 0.02, -0.01, Z + 0.01), (R + 0.014, 0.03, Z + 0.02), (R + 0.01, 0.07, Z + 0.02)], 0.004, black, segs=5)  # its cable back along the tube
out.append(g.finish())

# ---------- RocketShell (flying): the same warhead on an olive motor with four fins and a glowing flame behind; origin in
# the middle, nose toward -Y ----------
g = Builder("RocketShell", [olive, warhead, iron, flame, white])
loft(g, warhead_rings(-0.06, 0.03, 0), warhead, caps=(olive, warhead), mf=lambda i, j: olive if i == 0 else white if i == 1 else warhead)
g.cyl(0.006, 0.016, (0, -0.218, 0), iron, segs=6, axis="Y")
g.cyl(0.032, 0.3, (0, 0.1, 0), olive, segs=10, axis="Y")                    # the motor
g.cyl(0.033, 0.02, (0, 0.0, 0), white, segs=10, axis="Y")
for k in range(4):
    a = k * math.pi / 2
    d = Vector((math.cos(a), 0, math.sin(a)))
    fin(g, [d * 0.03 + Vector((0, 0.15, 0)), d * 0.075 + Vector((0, 0.22, 0)), d * 0.075 + Vector((0, 0.25, 0)), d * 0.03 + Vector((0, 0.25, 0))],
        d.cross(Vector((0, 1, 0))), 0.005, iron)
g.cone(0.028, 0.0, 0.14, (0, 0.32, 0), flame, segs=8, axis="-Y")             # the flame, its tip toward the back
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
