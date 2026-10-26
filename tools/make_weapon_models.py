# The hand-held weapons of the gun slot and the summoned shark, built into fortress_models.blend and exported as OBJ.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_weapon_models.py
# Hand-held items: origin = centre of the grip (where the hand holds it), the business end toward -Y (the game's +Z), Z up.
# Boomerang: a tuna bent into a 110° V lying flat in x/y (its back on the outside, elbow toward -Y), origin at its centre.
# Shark (~2 m): origin in the middle of the body, waterline at z = 0 (back and fins above, belly below), facing -Y.
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from mathutils import Vector
from bkit import Builder, M, export, fin, loft, mat, torus, tube

TAU = 2 * math.pi
UP = Vector((0, 0, 1))

iron, metal, steel, brass, gold, dark, rope, black, white, pearl = (
    M(n) for n in ("Iron", "Metal", "Steel", "Brass", "Gold", "DarkWood", "Rope", "BombBlack", "White", "Pearl"))
crystal, bubble, fishblue, orange, yellow, coral, conch, red, wood = (
    M(n) for n in ("Crystal", "BubbleGlow", "FishBlue", "FishOrange", "DuckYellow", "Coral", "Conch", "Bandana", "Wood"))
teal = mat("WpnTeal", (0.55, 0.86, 0.82), force=True)
toxic = mat("WpnToxicGlow", (0.6, 1.0, 0.25), (0.5, 1.0, 0.1), force=True)
purple = mat("WpnPurple", (0.52, 0.34, 0.7), force=True)
pink = mat("WpnPink", (1.0, 0.55, 0.75), force=True)
soap = mat("WpnSoap", (0.74, 0.62, 1.0), force=True)
navy = mat("WpnDeepBlue", (0.12, 0.22, 0.6), force=True)
grey = mat("WpnShark", (0.46, 0.55, 0.66), force=True)
mouth = mat("WpnMouth", (0.55, 0.16, 0.22), force=True)
band = mat("WpnShellBand", (0.85, 0.52, 0.36), force=True)
out = []


# ---------- shape helpers (on top of bkit's boxes and cylinders) ----------

def lerp_table(rows, x):
    # Piecewise-linear lookup in rows (x, values...).
    rows = sorted(rows)
    x = min(max(x, rows[0][0]), rows[-1][0])
    for r0, r1 in zip(rows, rows[1:]):
        if x <= r1[0]:
            t = (x - r0[0]) / (r1[0] - r0[0])
            return tuple(a + (b - a) * t for a, b in zip(r0[1:], r1[1:]))


def yring(y, rx, rz, n, cx=0.0, cz=0.0, a0=0.0):
    # An ellipse around the y axis: vertex 0 on the +x side, then over the top.
    return [(cx + rx * math.cos(a0 + i * TAU / n), y, cz + rz * math.sin(a0 + i * TAU / n)) for i in range(n)]


def grip(b, m, cap):
    # Pistol grip centred on the origin, raked back, with a cap on the butt.
    b.box((0.034, 0.05, 0.13), (0, 0, 0), m, rot=(0.28, 0, 0))
    b.box((0.04, 0.058, 0.014), (0, 0.018, -0.0625), cap, rot=(0.28, 0, 0))


def trigger(b, guard, m):
    tube(b, [(0, -0.1, 0.075), (0, -0.098, 0.035), (0, -0.08, 0.014), (0, -0.055, 0.01), (0, -0.032, 0.02)], 0.0055, guard, segs=5)
    b.beam((0, -0.058, 0.075), (0, -0.064, 0.035), 0.007, m, segs=4)


# ---------- LaserGun: sleek white ray gun, teal trim, a glowing cyan lens, a battery canister with glowing stripes, fins ----------
g = Builder("LaserGun", [white, teal, metal, crystal])
grip(g, metal, teal)
trigger(g, teal, metal)
Z = 0.09
loft(g, [yring(y, rx, rz, 12, cz=Z) for y, rx, rz in (
    (0.125, 0.012, 0.012), (0.115, 0.03, 0.033), (0.09, 0.042, 0.047), (0.03, 0.047, 0.053), (-0.05, 0.045, 0.05), (-0.11, 0.036, 0.04), (-0.13, 0.026, 0.028))], white)
g.cyl(0.05, 0.016, (0, -0.02, Z), teal, segs=12, axis="Y")               # band
g.cyl(0.019, 0.2, (0, -0.22, Z), teal, segs=10, axis="Y")                # barrel
for y, r in ((-0.165, 0.042), (-0.21, 0.037), (-0.255, 0.032)):
    g.cyl(r, 0.012, (0, y, Z), white, segs=12, axis="Y")                 # emitter rings
g.cone(0.022, 0.04, 0.04, (0, -0.32, Z), teal, segs=12, axis="Y")         # muzzle bell
g.cyl(0.034, 0.01, (0, -0.342, Z), crystal, segs=12, axis="Y")           # lens
g.ico(0.024, (0, -0.34, Z), crystal, subdiv=2)
g.cyl(0.022, 0.11, (0, 0.01, 0.16), white, segs=10, axis="Y")            # battery canister
for y in (-0.025, 0.01, 0.045):
    g.cyl(0.0245, 0.011, (0, y, 0.16), crystal, segs=10, axis="Y")       # glowing stripes
for y in (-0.048, 0.068):
    g.cyl(0.017, 0.012, (0, y, 0.16), metal, segs=10, axis="Y")
for y in (-0.02, 0.04):
    g.box((0.012, 0.014, 0.03), (0, y, 0.14), metal)                     # clamps
for s in (-1, 1):                                                         # swept side fins
    fin(g, [(s * 0.035, 0.03, Z), (s * 0.085, 0.085, Z), (s * 0.085, 0.118, Z), (s * 0.03, 0.11, Z)], (0, 0, 1), 0.01, teal)
fin(g, [(0, 0.075, Z + 0.045), (0, 0.11, Z + 0.1), (0, 0.13, Z + 0.1), (0, 0.125, Z + 0.015)], (1, 0, 0), 0.01, teal)
out.append(g.finish())

# ---------- GasGun: cartoon bug-sprayer, purple pump barrel, a glowing toxic-green glass tank, brass nozzle, T pump handle ----------
g = Builder("GasGun", [purple, brass, toxic, iron, white, black])
grip(g, iron, brass)
trigger(g, brass, iron)
Z = 0.09
g.cyl(0.044, 0.2, (0, 0, Z), purple, segs=12, axis="Y")                  # pump barrel
for y in (-0.1, 0.1):
    g.cyl(0.048, 0.018, (0, y, Z), brass, segs=12, axis="Y")
g.beam((0, 0.1, Z), (0, 0.175, Z), 0.008, iron, segs=6)                 # plunger rod
g.cyl(0.014, 0.09, (0, 0.18, Z), iron, segs=8, axis="X")                 # T handle
for x in (-0.048, 0.048):
    g.ico(0.017, (x, 0.18, Z), brass)
g.cyl(0.03, 0.035, (0, -0.02, 0.135), brass, segs=10)                    # tank collar
g.ico(0.065, (0, -0.02, 0.195), toxic, subdiv=2)                          # glass tank
torus(g, (0, -0.02, 0.195), (0, 0, 1), 0.066, 0.005, brass, segs=16, tsegs=4)
g.cyl(0.024, 0.018, (0, -0.02, 0.262), brass, segs=10)
g.ico(0.012, (0, -0.02, 0.276), brass)
g.beam((0, -0.06, 0.18), (0, -0.16, 0.093), 0.007, brass, segs=6)        # feed pipe
g.beam((0, -0.1, Z), (0, -0.3, Z), 0.013, brass, segs=8)                 # nozzle pipe
g.cone(0.016, 0.034, 0.05, (0, -0.32, Z), brass, segs=10, axis="Y")
g.cyl(0.028, 0.006, (0, -0.346, Z), toxic, segs=10, axis="Y")
g.cyl(0.02, 0.012, (0.046, 0.04, Z), brass, segs=10, axis="X")           # pressure gauge
g.cyl(0.016, 0.004, (0.053, 0.04, Z), white, segs=10, axis="X")
g.box((0.003, 0.004, 0.014), (0.0555, 0.04, Z + 0.004), black, rot=(0.6, 0, 0))
out.append(g.finish())

# ---------- Boomerang: a carved wooden V (110°, its elbow toward -Y), lying flat in x/y, a tuna-blue stripe painted along its
# top, brass caps on the tips and a fat rope-bound hub at the elbow (12 cm through, so it reads edge-on in the hand) ----------
g = Builder("Boomerang", [wood, dark, fishblue, brass, rope])
L, HALF, BEND = 0.3, math.radians(55), 0.12


def bpt(u):  # the spine: u -1 (left tip) .. 1 (right tip), the elbow (u 0) rounded off
    a = abs(u)
    return Vector((math.sin(HALF) * L * u, math.cos(HALF) * L * (math.sqrt(a * a + BEND * BEND) - BEND) - 0.025 * (1 - u * u), 0))


US = [-1.0, -0.97, -0.9, -0.75, -0.55, -0.35, -0.18, -0.07, 0.0, 0.07, 0.18, 0.35, 0.55, 0.75, 0.9, 0.97, 1.0]
rings = []
for u in US:
    p = bpt(u)
    t = (bpt(min(1.0, u + 0.01)) - bpt(max(-1.0, u - 0.01))).normalized()
    side = Vector((-t.y, t.x, 0))
    w = 0.046 - 0.012 * abs(u) if abs(u) < 0.96 else 0.014     # half width in the plane, rounded off at the tips
    th = 0.03 - 0.008 * abs(u) if abs(u) < 0.96 else 0.01     # half thickness
    rings.append([p + side * (w * math.cos(j * TAU / 10)) + UP * (th * math.sin(j * TAU / 10)) for j in range(10)])
loft(g, rings, wood, mf=lambda i, j: fishblue if j in (2, 3) and (2 <= i <= 6 or 10 <= i <= 14) else dark if j > 5 else wood)
for u in (-0.93, 0.93):                                                   # brass tip caps
    p = bpt(u)
    t = (bpt(u + 0.01) - bpt(u - 0.01)).normalized() * (1 if u > 0 else -1)
    g.beam(p - t * 0.03, p + t * 0.02, 0.036, brass, segs=8, r2=0.014)
for x in (-0.026, 0.0, 0.026):                                           # the hub: rope turns round the elbow
    loft(g, [[Vector((x + dx, 0, 0)) + Vector((0, 0.05 * math.cos(j * TAU / 10), 0.064 * math.sin(j * TAU / 10))) for j in range(10)] for dx in (-0.01, 0.01)],
         rope)
lo = Vector((min(v.co.x for v in g.bm.verts), min(v.co.y for v in g.bm.verts), 0))
hi = Vector((max(v.co.x for v in g.bm.verts), max(v.co.y for v in g.bm.verts), 0))
for v in g.bm.verts:
    v.co -= (lo + hi) / 2
out.append(g.finish())

# ---------- SharkWhistle: a little shark head with open toothy jaws (the whistle's hole), a flat whistle mouthpiece behind with
# its sound window on top, a steel lug and a red lanyard hanging from it (no grip: held to the mouth) ----------
g = Builder("SharkWhistle", [grey, white, black, mouth, red, steel])
HEAD = ((0.038, 0.026, 0.026, 0.0), (0.025, 0.031, 0.03, 0.0), (0.0, 0.033, 0.032, 0.0), (-0.03, 0.03, 0.029, 0.0), (-0.05, 0.025, 0.024, -0.002),
        (-0.058, 0.021, 0.019, -0.003), (-0.054, 0.015, 0.013, -0.003), (-0.04, 0.01, 0.008, -0.003))  # ends by turning into the mouth hole
loft(g, [yring(y, rx, rz, 12, cz=cz) for y, rx, rz, cz in HEAD], grey, caps=(grey, black),
     mf=lambda i, j: black if i >= 6 else mouth if i == 5 else white if math.sin((j + 0.5) * TAU / 12) < -0.35 else grey)
loft(g, [yring(y, rx, rz, 10, cz=cz) for y, rx, rz, cz in ((-0.03, 0.022, 0.014, 0.014), (-0.055, 0.019, 0.012, 0.012), (-0.07, 0.012, 0.008, 0.01))]
     + [[(0, -0.078, 0.008)]], grey)                                     # snout over the mouth
for k in range(5):                                                         # teeth: down from the snout, up from the lower rim
    x = -0.012 + k * 0.006
    g.beam((x, -0.0585, 0.002), (x, -0.06, -0.006), 0.0024, white, segs=3, r2=0.0)
    a = math.radians(215 + k * 27.5)
    rim = Vector((0.021 * math.cos(a), -0.0585, -0.003 + 0.019 * math.sin(a)))
    g.beam(rim, rim + (Vector((0, -0.06, -0.005)) - rim) * 0.35, 0.0024, white, segs=3, r2=0.0)
for s in (-1, 1):
    g.ico(0.0075, (s * 0.025, -0.032, 0.012), white, subdiv=2)
    g.ico(0.004, (s * 0.03, -0.035, 0.0125), black, subdiv=1)
fin(g, [(0, -0.014, 0.026), (0, -0.004, 0.045), (0, 0.01, 0.06), (0, 0.022, 0.07), (0, 0.016, 0.052), (0, 0.016, 0.038), (0, 0.024, 0.026)],
    (1, 0, 0), [0.009, 0.007, 0.005, 0.003, 0.005, 0.007, 0.009], grey)
loft(g, [yring(y, rx, rz, 10, cz=0.004) for y, rx, rz in ((0.03, 0.02, 0.014), (0.05, 0.02, 0.012), (0.07, 0.019, 0.008), (0.084, 0.018, 0.0065),
                                                         (0.086, 0.014, 0.003))], white, caps=(white, black))  # flat mouthpiece, a dark slit at the end
g.cyl(0.0205, 0.004, (0, 0.074, 0.004), steel, segs=10, axis="Y")        # its lip bead
g.box((0.02, 0.014, 0.006), (0, 0.045, 0.0165), black)                    # the sound window on top
g.box((0.02, 0.003, 0.008), (0, 0.0375, 0.0175), white, rot=(-0.6, 0, 0))  # and its sharp edge
torus(g, (0, 0.03, -0.017), (1, 0, 0), 0.007, 0.0022, steel, segs=10, tsegs=4)  # lug under the mouthpiece
tube(g, [(0, 0.03, -0.024), (0.004, 0.026, -0.045), (0.012, 0.012, -0.068), (0.016, -0.012, -0.082), (0.008, -0.036, -0.085),
         (-0.006, -0.042, -0.072), (-0.008, -0.026, -0.052), (-0.004, 0.006, -0.036), (0, 0.03, -0.024)], 0.003, red, segs=5)  # the lanyard loop
g.cyl(0.006, 0.012, (0.016, -0.004, -0.078), red, segs=6)               # a bead on it
out.append(g.finish())

# ---------- BubbleGun: pink toy blaster, a big orange wand ring holding a glowing soap film, a soap bottle on top ----------
g = Builder("BubbleGun", [pink, orange, bubble, soap, white, yellow])
grip(g, orange, pink)
trigger(g, orange, yellow)
Z = 0.095
loft(g, [yring(y, rx, rz, 12, cz=Z) for y, rx, rz in (
    (0.105, 0.022, 0.026), (0.095, 0.04, 0.05), (0.06, 0.05, 0.062), (0.0, 0.052, 0.064), (-0.08, 0.048, 0.058), (-0.13, 0.04, 0.046), (-0.145, 0.03, 0.034))], pink)
g.cyl(0.03, 0.12, (0, -0.2, Z), orange, segs=10, axis="Y")                # neck
g.cone(0.03, 0.045, 0.03, (0, -0.27, Z), orange, segs=12, axis="Y")
R = 0.085
torus(g, (0, -0.32, Z), (0, 1, 0), R, 0.011, orange, segs=20, tsegs=6)    # wand ring
g.cyl(R - 0.002, 0.004, (0, -0.32, Z), bubble, segs=20, axis="Y")        # soap film
g.ico(0.046, (0.015, -0.33, Z + 0.012), bubble, subdiv=3)                 # a bubble swelling out of it, with a shine
g.ico(0.01, (0.031, -0.365, Z + 0.0375), white, subdiv=1)
for a in (math.pi / 2, math.pi * 7 / 6, math.pi * 11 / 6):                # struts from the neck to the ring
    g.beam((0.04 * math.cos(a), -0.28, Z + 0.04 * math.sin(a)), (R * math.cos(a), -0.32, Z + R * math.sin(a)), 0.007, orange, segs=5)
g.cyl(0.018, 0.03, (0, 0.02, 0.165), white, segs=10)                      # soap bottle, upside down in its socket
g.cone(0.018, 0.034, 0.022, (0, 0.02, 0.19), soap, segs=12)
g.cyl(0.034, 0.065, (0, 0.02, 0.233), soap, segs=12)
g.cyl(0.0355, 0.022, (0, 0.02, 0.232), pink, segs=12)
for s in (-1, 1):
    g.ico(0.013, (s * 0.048, 0.02, Z + 0.012), yellow, subdiv=1)          # buttons
out.append(g.finish())

# ---------- HookGun: a grapple gun: a wooden stock and grip, an iron receiver, a steel barrel ending in a flared hook seat (the
# muzzle, WeaponItems.Spec(Hook): the grapple flies from there), a three-pronged gold grapple sitting in it, a big rope reel
# with a crank on the side and the line running from it to the grapple ----------
g = Builder("HookGun", [dark, iron, steel, gold, rope, black])
grip(g, dark, iron)
trigger(g, iron, steel)
Z = 0.09
loft(g, [yring(y, rx, rz, 8, cz=Z, a0=math.pi / 8) for y, rx, rz in ((0.105, 0.024, 0.028), (0.095, 0.032, 0.04), (-0.085, 0.032, 0.04),
                                                                     (-0.095, 0.026, 0.032))], iron)  # octagonal receiver
g.box((0.03, 0.1, 0.05), (0, 0.15, Z - 0.012), dark, rot=(0.12, 0, 0))   # short stock
g.box((0.034, 0.016, 0.058), (0, 0.205, Z - 0.02), iron, rot=(0.12, 0, 0))
g.cyl(0.026, 0.17, (0, -0.17, Z), steel, segs=10, axis="Y")              # barrel
for y in (-0.11, -0.2):
    g.cyl(0.031, 0.016, (0, y, Z), iron, segs=10, axis="Y")
g.cone(0.028, 0.046, 0.04, (0, -0.27, Z), iron, segs=12, axis="Y")       # the hook seat, flaring to the muzzle (y -0.29)
g.cyl(0.038, 0.004, (0, -0.2905, Z), black, segs=12, axis="Y")
g.cyl(0.009, 0.1, (0, -0.31, Z), gold, segs=8, axis="Y")                 # the grapple: shank, hub, three prongs swept back
g.ico(0.017, (0, -0.36, Z), gold, subdiv=1)
for k in range(3):
    a = math.pi / 2 + k * TAU / 3
    d = Vector((math.cos(a), 0, math.sin(a)))
    c = Vector((0, -0.36, Z))
    tube(g, [c, c + d * 0.025 + Vector((0, -0.025, 0)), c + d * 0.055 + Vector((0, -0.03, 0)), c + d * 0.075 + Vector((0, -0.01, 0)),
             c + d * 0.072 + Vector((0, 0.02, 0))], [0.009, 0.008, 0.007, 0.005, 0.0], gold, segs=6)
g.cyl(0.008, 0.07, (0.065, 0, Z - 0.005), steel, segs=8, axis="X")        # rope reel: axle, drum, flanges, coil, crank
g.cyl(0.034, 0.05, (0.065, 0, Z - 0.005), dark, segs=12, axis="X")
for x in (0.037, 0.093):
    g.cyl(0.054, 0.006, (x, 0, Z - 0.005), iron, segs=14, axis="X")
tube(g, [(0.043 + 0.044 * k / 36, 0.039 * math.sin(k * TAU / 12), Z - 0.005 + 0.039 * math.cos(k * TAU / 12)) for k in range(37)], 0.007, rope, segs=6)
g.beam((0.1, 0, Z - 0.005), (0.1, 0.035, Z - 0.035), 0.006, iron, segs=4)
g.cyl(0.008, 0.026, (0.11, 0.035, Z - 0.035), dark, segs=6, axis="X")
tube(g, [(0.065, -0.03, Z + 0.03), (0.05, -0.09, Z + 0.036), (0.04, -0.18, Z + 0.034), (0.034, -0.26, Z + 0.034), (0.014, -0.3, Z + 0.016),
         (0, -0.32, Z + 0.006)], 0.005, rope, segs=5)                     # line from the reel to the grapple
out.append(g.finish())

# ---------- WhirlCannon: a machine, not a shell (the conch is the shell): a deep-blue body narrowing to the front, a motor with
# cooling rings and a glowing core at the back, and at the front a ducted turbine: a white duct, a ring of twisted blades and a
# brass spinner in front of a glowing eye ----------
g = Builder("WhirlCannon", [navy, white, crystal, brass, dark, black])
grip(g, dark, brass)
trigger(g, brass, brass)
Z, N = 0.1, 16
loft(g, [yring(y, r, r, N, cz=Z) for y, r in ((0.08, 0.05), (0.05, 0.058), (-0.1, 0.05), (-0.2, 0.04), (-0.23, 0.04))], navy)  # the body
for y in (0.03, -0.06, -0.15):
    g.cyl(lerp_table(((0.05, 0.058), (-0.1, 0.05), (-0.2, 0.04)), y)[0] + 0.004, 0.012, (0, y, Z), brass, segs=N, axis="Y")
for y in (0.09, 0.105, 0.12):                                             # the motor's cooling rings
    g.cyl(0.054, 0.007, (0, y, Z), dark, segs=N, axis="Y")
g.cyl(0.046, 0.05, (0, 0.105, Z), navy, segs=N, axis="Y")
g.ico(0.032, (0, 0.135, Z), crystal, subdiv=2)                            # the glowing core
torus(g, (0, 0.135, Z), (0, 1, 0), 0.036, 0.005, brass, segs=12, tsegs=4)
duct = [(-0.22, 0.068), (-0.215, 0.082), (-0.25, 0.09), (-0.31, 0.092), (-0.315, 0.084), (-0.3, 0.078), (-0.23, 0.074)]  # the duct's section
loft(g, [[((r * math.cos(k * TAU / 20)), y, Z + r * math.sin(k * TAU / 20)) for k in range(20)] for y, r in duct], white, closed=True,
     mf=lambda i, j: navy if i in (1, 2) and j % 5 == 0 else white)
g.cyl(0.074, 0.004, (0, -0.226, Z), crystal, segs=20, axis="Y")           # the glowing eye behind the blades
for k in range(3):                                                         # struts from the body to the duct
    a = math.pi / 2 + k * TAU / 3
    g.beam((0.035 * math.cos(a), -0.215, Z + 0.035 * math.sin(a)), (0.075 * math.cos(a), -0.235, Z + 0.075 * math.sin(a)), 0.005, dark, segs=4)
for k in range(8):                                                         # the twisted blades
    a0 = k * TAU / 8
    pts = []
    for r, y, da in ((0.022, -0.245, 0.0), (0.074, -0.245, 0.18), (0.074, -0.275, 0.62), (0.022, -0.275, 0.36)):
        pts.append(Vector((r * math.cos(a0 + da), y, Z + r * math.sin(a0 + da))))
    nrm = (pts[1] - pts[0]).cross(pts[3] - pts[0])
    fin(g, pts, nrm, 0.004, white if k % 2 else dark)
g.cyl(0.024, 0.04, (0, -0.255, Z), brass, segs=12, axis="Y")              # the hub and its spinner
g.cone(0.024, 0.0, 0.05, (0, -0.3, Z), brass, segs=12, axis="Y")
out.append(g.finish())

# ---------- Conch: a big spiral shell horn, the flared pink opening forward, a brass mouthpiece on the spire ----------
g = Builder("Conch", [conch, band, coral, brass, black, mouth])
N, APEX = 12, 0.17
ENV = ((APEX, 0.006), (0.15, 0.022), (0.12, 0.042), (0.09, 0.06), (0.06, 0.078), (0.035, 0.094), (0.015, 0.102), (-0.02, 0.097),
       (-0.07, 0.085), (-0.12, 0.073), (-0.155, 0.074), (-0.17, 0.08))
ys = [APEX - k * 0.15 / 33 for k in range(34)] + [0.02 - k * 0.019 for k in range(1, 11)]  # spire (~5.5 cm a whorl), then the body whorl
rings = []
for k, y in enumerate(ys):  # one segment of twist per ring: column 11 is a groove spiralling down the spire, fading out on the body
    amt = min(1.0, max(0.0, y / 0.03))
    rs = [lerp_table(ENV, y)[0] * (1 - 0.22 * amt * (1 - math.sin(math.pi * (j + 0.5) / N))) for j in range(N)]
    rings.append([(r * math.cos((j + k) * TAU / N), y, r * math.sin((j + k) * TAU / N)) for j, r in enumerate(rs)])


def lip(y, r, wing, back):  # the +x side flares out and sweeps back, like a conch's lip
    pts = []
    for j in range(N):
        a = (j + len(ys) - 1) * TAU / N
        c = max(0.0, math.cos(a)) ** 2
        pts.append(((r + wing * c) * math.cos(a), y + back * c, r * 0.92 * math.sin(a)))
    return pts


rings += [lip(*row) for row in ((-0.185, 0.09, 0.012, 0.0), (-0.205, 0.112, 0.05, 0.022), (-0.213, 0.105, 0.046, 0.022), (-0.196, 0.088, 0.026, 0.012),
                                (-0.16, 0.064, 0.0, 0.0), (-0.11, 0.042, 0.0, 0.0))]  # flare, lip rim, then the pink inside
loft(g, rings, conch, caps=(conch, mouth), tri=True,
     mf=lambda i, j: (band if j == N - 1 else conch) if i < len(ys) - 1 else conch if i < len(ys) + 1 else coral)
for k in range(8):                                                         # blunt knobs on the shoulder
    a = k * TAU / 8 + 0.2
    g.beam((0.08 * math.cos(a), 0.02, 0.08 * math.sin(a)), (0.132 * math.cos(a), 0.034, 0.132 * math.sin(a)), 0.02, conch, segs=6, r2=0.005)
g.cyl(0.014, 0.025, (0, 0.18, 0), brass, segs=10, axis="Y")              # mouthpiece
g.cyl(0.0075, 0.004, (0, 0.1925, 0), black, segs=8, axis="Y")
out.append(g.finish())

# ---------- Shark: cartoon shark for the summon, open toothy mouth, big dorsal fin, waterline at z = 0 ----------
g = Builder("Shark", [grey, white, pearl, black, mouth])
N = 16
BODY = ((-0.62, 0.18, 0.18, -0.06), (-0.5, 0.245, 0.25, -0.04), (-0.35, 0.27, 0.27, -0.04), (-0.1, 0.265, 0.26, -0.04), (0.15, 0.22, 0.22, -0.035),
        (0.38, 0.15, 0.155, -0.025), (0.56, 0.085, 0.1, -0.015), (0.7, 0.045, 0.065, -0.01))  # (y, half width, half height, centre z)
loft(g, [yring(y, rx, rz, N, cz=cz) for y, rx, rz, cz in BODY],
     grey, mf=lambda i, j: white if math.sin((j + 0.5) * TAU / N) < -0.3 else grey, caps=(black, grey))  # front cap = the throat


def half_ring(y, rx, rz, cz, lower=False):
    # Half an ellipse (the dome over the top, or under the bottom for the jaw) closed by a flat chord: the palate / mouth floor.
    s = -1 if lower else 1
    return [(rx * math.cos(a), y, cz + s * rz * math.sin(a)) for a in (k * math.pi / 8 for k in range(9))] + [(x, y, cz) for x in (-rx / 2, 0.0, rx / 2)]


HEAD = ((-0.5, 0.245, 0.25, -0.04), (-0.6, 0.215, 0.215, -0.05), (-0.72, 0.19, 0.19, -0.05), (-0.82, 0.16, 0.16, -0.035), (-0.9, 0.12, 0.13, -0.02),
        (-0.96, 0.075, 0.09, -0.005), (-1.0, 0.035, 0.05, 0.01))  # starts on the body's own section, so the top runs on without a seam
loft(g, [half_ring(*row) for row in HEAD] + [[(0, -1.03, 0.03)]], grey, mf=lambda i, j: mouth if j >= 8 else grey)
JAW = ((-0.6, 0.18, 0.12), (-0.7, 0.17, 0.11), (-0.8, 0.14, 0.09), (-0.87, 0.1, 0.065), (-0.91, 0.05, 0.035))
HINGE, DROP = (-0.6, -0.08), math.radians(25)


def jaw(p):  # swing a point of the lower jaw open around the hinge
    dy, dz = p[1] - HINGE[0], p[2] - HINGE[1]
    return (p[0], HINGE[0] + dy * math.cos(DROP) - dz * math.sin(DROP), HINGE[1] + dy * math.sin(DROP) + dz * math.cos(DROP))


loft(g, [[jaw(p) for p in half_ring(y, rx, rz, -0.08, lower=True)] for y, rx, rz in JAW] + [[jaw((0, -0.93, -0.08))]], white,
     mf=lambda i, j: mouth if j >= 8 else white)
for k in range(7):                                                         # teeth: top row down from the palate, bottom row up from the jaw
    y = -0.64 - k * 0.052
    rx, _, cz = lerp_table(HEAD, y)
    for s in (-1, 1):
        g.beam((s * rx * 0.9, y, cz + 0.01), (s * rx * 0.86, y - 0.01, cz - 0.06), 0.022, pearl, segs=4, r2=0.0)
for k in range(6):
    y = -0.64 - k * 0.048
    rx = lerp_table(JAW, y)[0]
    for s in (-1, 1):
        g.beam(jaw((s * rx * 0.88, y, -0.09)), jaw((s * rx * 0.84, y - 0.01, -0.03)), 0.02, pearl, segs=4, r2=0.0)
for s in (-1, 1):
    g.ico(0.054, (s * 0.11, -0.79, 0.065), pearl, subdiv=2)               # eyes
    g.ico(0.03, (s * 0.154, -0.81, 0.075), black, subdiv=2)
    for y in (-0.44, -0.39, -0.34):                                       # gill slits, half sunk into the flank
        rx, rz, cz = lerp_table(BODY, y)
        tube(g, [(s * rx * math.cos(a), y, cz + rz * math.sin(a)) for a in (math.radians(d) for d in (-8, 6, 20, 34))], 0.009, black, segs=4)
    o = Vector((s * 0.906, 0, -0.423))                                    # pectoral fin, swept back and down
    root = Vector((s * 0.19, -0.36, -0.13))
    fin(g, [root + o * u + Vector((0, v, 0)) for u, v in ((0, -0.12), (0.2, -0.02), (0.42, 0.14), (0.28, 0.1), (0.1, 0.12), (0, 0.1))],
        o.cross(Vector((0, 1, 0))), [0.05, 0.04, 0.015, 0.025, 0.035, 0.05], grey)
fin(g, [(0, y, z) for y, z in ((-0.28, 0.18), (-0.17, 0.4), (-0.02, 0.6), (0.14, 0.72), (0.1, 0.52), (0.1, 0.36), (0.16, 0.15))],
    (1, 0, 0), [0.09, 0.07, 0.05, 0.02, 0.04, 0.06, 0.09], grey)          # dorsal fin
fin(g, [(0, 0.48, 0.07), (0, 0.56, 0.17), (0, 0.6, 0.165), (0, 0.6, 0.07)], (1, 0, 0), [0.04, 0.02, 0.02, 0.04], grey)
fin(g, [(0, y, z) for y, z in ((0.62, 0.05), (0.8, 0.22), (1.0, 0.48), (0.9, 0.2), (0.86, 0.0), (0.94, -0.2), (0.78, -0.12), (0.62, -0.06))],
    (1, 0, 0), [0.08, 0.05, 0.02, 0.04, 0.05, 0.02, 0.05, 0.08], grey)    # tail
out.append(g.finish())

export(out)
