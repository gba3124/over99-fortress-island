# The village stations by the dock, each with a keeper standing behind it: the fish market stall and the weapon smithy,
# in the game's cabin style (tools/vkit.py: plank walls and floors, board roofs, rustic props). Built into
# fortress_models.blend and exported as OBJ.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_station_models.py
# Both: origin on the ground in the middle, the customer's side toward -Y (the game's +Z), Z up. The keeper stands at
# (0, 0.35) behind the counter; FortressIsland.cs places the keeper, the forge's light and the weapon rack pegs (keep in step).
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bkit import TAU, Builder, M, export, mat, tube
import vkit
from vkit import Frame, box, cyl, ball, beam

fishB, fishO = M("FishBlue"), M("FishOrange")
ice = mat("StIce", (0.6, 0.72, 0.8), force=True)
coal = mat("StCoal", (1.0, 0.45, 0.1), (1.0, 0.35, 0.05), force=True)
ember = mat("StEmber", (0.2, 0.17, 0.16), force=True)
water = mat("StWater", (0.3, 0.55, 0.72), force=True)
leather = mat("StLeather", (0.42, 0.26, 0.15), force=True)
steel = mat("StSteel", (0.55, 0.57, 0.6), force=True)
slate = mat("StSlate", (0.16, 0.18, 0.18), force=True)
out = []
F = Frame((0, 0, 0))


def fish(b, x, y, z, yaw, m, length=0.36):
    # A fish lying on its side: a body thick in the middle, a forked tail, an eye.
    d = (math.cos(yaw), math.sin(yaw))
    head, tail = (x + d[0] * length / 2, y + d[1] * length / 2, z), (x - d[0] * length / 2, y - d[1] * length / 2, z)
    beam(b, F, head, (x, y, z), length * 0.17, m, segs=6)
    beam(b, F, (x, y, z), tail, length * 0.17, m, segs=6)
    for s in (-1, 1):
        beam(b, F, tail, (tail[0] - d[0] * 0.09 - d[1] * s * 0.05, tail[1] - d[1] * 0.09 + d[0] * s * 0.05, z), 0.02, m, segs=3)
    ball(b, F, 0.016, (head[0] - d[0] * 0.04, head[1] - d[1] * 0.04, z + 0.045), vkit.iron)


def post(b, x, y, h, r=0.08):
    box(b, F, (r * 2, r * 2, h), (x, y, h / 2), vkit.trim)


# ---------- MarketStall: a little plank booth (back and side walls, a board roof with a big fish sign on it) and a striped
# canvas awning sagging out over the counter, scalloped all round; a tilted tray of fish on ice with price flags, a scale
# and a cutting board on the counter, shelves of jars and tins behind, buoys, a net, dried fish, crates and a barrel ----------
fishR = mat("StFishRed", (0.86, 0.38, 0.32), force=True)
s = Builder("MarketStall", vkit.KIT + [ice, fishB, fishO, fishR, slate, steel])
W, BACK, FRONT = 3.0, 1.2, -1.45                                             # the booth's width, its back wall, the awning's edge
for k in range(12):                                                          # a plank deck to stand on
    y = -1.05 + (k + 0.5) * 2.5 / 12
    box(s, F, (W + 0.3, 2.5 / 12 - 0.02, 0.08), (0, y, 0.06), vkit.plank if k % 2 == 0 else vkit.plank2)
for x in (-W / 2 - 0.05, 0, W / 2 + 0.05):
    box(s, F, (0.12, 2.5, 0.06), (x, 0.2, 0.0), vkit.trim)
# The booth: a back wall, side walls full height behind the keeper and counter-high in front of him.
vkit.plank_wall(s, F, -W / 2, W / 2, 0.1, 2.42, y=BACK)
for sx in (-1, 1):
    G = F.sub((0, 0, 0), sx * 90)
    def span(ya, yb):                                                        # the stall's y as this side frame's x
        return (ya, yb) if sx > 0 else (-yb, -ya)
    vkit.plank_wall(s, G, *span(0.15, BACK), 0.1, 2.42, y=-W / 2)
    vkit.plank_wall(s, G, *span(-0.88, 0.15), 0.1, 0.92, y=-W / 2)
    box(s, F, (0.09, 1.1, 0.09), (sx * W / 2, -0.37, 0.96), vkit.trim)       # its top rail
# Posts: at the back corners, where the roof meets the awning, and at the awning's front corners (with knee braces).
for x in (-W / 2, W / 2):
    post(s, x, BACK, 2.9)
    post(s, x, -0.1, 2.5)
    post(s, x, FRONT + 0.05, 2.08, 0.07)
    box(s, F, (0.2, 0.2, 0.06), (x, FRONT + 0.05, 2.1), vkit.trim)           # a cap on it
    beam(s, F, (x, FRONT + 0.05, 1.72), (x, FRONT + 0.4, 2.03), 0.03, vkit.trim, segs=4)
    beam(s, F, (x, -0.1, 2.05), (x, -0.45, 2.33), 0.03, vkit.trim, segs=4)
# A board roof over the booth, sloping down to the front; the awning carries on from under its edge.
vkit.shed_roof(s, F, W + 0.1, -0.1, BACK + 0.05, 2.47, 2.9, over=0.18)
box(s, F, (W + 0.4, 0.09, 0.09), (0, -0.1, 2.44), vkit.trim)               # the beam the awning hangs from
box(s, F, (W + 0.4, 0.08, 0.08), (0, FRONT, 2.03), vkit.trim)              # the front bar
# The awning: three panels, each a little flatter than the one before (the canvas sags), red and cream stripes.
NS = 10
panel = [(-0.1, 2.4), (-0.62, 2.22), (-1.08, 2.11), (FRONT, 2.06)]
for a, b in zip(panel, panel[1:]):
    pitch = math.atan2(a[1] - b[1], a[0] - b[0])
    length = math.hypot(a[0] - b[0], a[1] - b[1]) + 0.02
    for k in range(NS):
        w = (W + 0.3) / NS
        x0 = -W / 2 - 0.15 + k * w
        box(s, F, (w + 0.002, length, 0.025), (x0 + w / 2, (a[0] + b[0]) / 2, (a[1] + b[1]) / 2 + 0.02), vkit.canvas if k % 2 == 0 else vkit.cream, rx=pitch)
# Scallops all round its edge: round flaps along the front, and down both sides.
for k in range(NS):
    w = (W + 0.3) / NS
    cyl(s, F, w * 0.5, w * 0.5, 0.025, (-W / 2 - 0.15 + (k + 0.5) * w, FRONT - 0.01, 2.06), vkit.canvas if k % 2 == 0 else vkit.cream, segs=12, rx=math.pi / 2)
for sx in (-1, 1):
    n = 5
    for k in range(n):
        y = -0.15 + (FRONT + 0.15) * (k + 0.5) / n
        z = 2.4 + (2.06 - 2.4) * (k + 0.5) / n
        cyl(s, F, 0.13, 0.13, 0.025, (sx * (W / 2 + 0.16), y, z - 0.02), vkit.cream if k % 2 == 0 else vkit.canvas, segs=12, ry=math.pi / 2)
# The sign on the roof: a board on two posts with a big fish on it, waves under it.
for x in (-0.55, 0.55):
    box(s, F, (0.08, 0.08, 0.5), (x, -0.02, 2.75), vkit.trim)
box(s, F, (1.7, 0.06, 0.62), (0, -0.06, 3.15), vkit.plank)
box(s, F, (1.8, 0.05, 0.72), (0, -0.03, 3.15), vkit.trim)
box(s, F, (0.62, 0.05, 0.3), (0.0, -0.1, 3.2), fishO)                      # the fish: its body,
s.prism([(0.31, 3.35), (0.31, 3.05), (0.55, 3.2)], -0.1, 0.05, fishO)      # its head,
s.prism([(-0.31, 3.2), (-0.56, 3.38), (-0.56, 3.02)], -0.1, 0.05, fishO)   # its tail,
s.prism([(-0.1, 3.34), (0.12, 3.34), (0.0, 3.44)], -0.1, 0.05, fishO)      # its fin
box(s, F, (0.06, 0.06, 0.06), (0.36, -0.13, 3.25), vkit.iron)               # and its eye
for k in range(3):
    box(s, F, (0.18, 0.04, 0.035), (-0.42 + k * 0.42, -0.1, 2.93), fishB)    # waves under it
# The counter: its front, a top board with a lip, and a tray of crushed ice tilted toward the customers.
vkit.plank_wall(s, F, -W / 2, W / 2, 0.1, 0.92, y=-0.88)
box(s, F, (W + 0.14, 0.82, 0.07), (0, -0.55, 0.96), vkit.trim)
box(s, F, (W + 0.16, 0.05, 0.11), (0, -0.95, 0.97), vkit.trim)
TILT = 0.2
box(s, F, (W - 0.5, 0.62, 0.08), (0, -0.58, 1.06), ice, rx=-TILT)
for sx in (-1, 1):
    box(s, F, (0.06, 0.66, 0.16), (sx * (W - 0.44) / 2, -0.58, 1.06), vkit.trim, rx=-TILT)
for row, ry in enumerate((-0.74, -0.44)):
    for k in range(4):
        x = -0.9 + k * 0.6 + (0.3 if row else 0.0) * 0.5
        z = 1.11 + (ry + 0.58) * math.tan(TILT)
        fish(s, x, ry, z, 0.25 if (k + row) % 2 else -0.2, (fishB, fishO, fishR)[(k + row) % 3], 0.34)
        beam(s, F, (x + 0.12, ry + 0.06, z), (x + 0.12, ry + 0.06, z + 0.16), 0.005, vkit.trim, segs=3)   # a price flag
        box(s, F, (0.08, 0.006, 0.05), (x + 0.16, ry + 0.06, z + 0.14), vkit.cream)
# A scale on the counter's right end; a cutting board with a fish and a knife on the left.
box(s, F, (0.22, 0.2, 0.05), (1.32, -0.42, 1.02), steel)
box(s, F, (0.04, 0.04, 0.22), (1.32, -0.36, 1.15), steel)
cyl(s, F, 0.09, 0.09, 0.02, (1.32, -0.36, 1.27), vkit.cream, segs=12, rx=math.pi / 2)  # its dial
cyl(s, F, 0.14, 0.1, 0.05, (1.32, -0.48, 1.3), vkit.brass, segs=12)                     # its dish
box(s, F, (0.42, 0.3, 0.03), (-1.22, -0.42, 1.01), vkit.plank2)
fish(s, -1.24, -0.42, 1.06, 0.1, fishR, 0.3)
box(s, F, (0.17, 0.035, 0.008), (-1.12, -0.31, 1.035), steel)
box(s, F, (0.08, 0.03, 0.025), (-1.25, -0.31, 1.04), vkit.trim)
# Behind the keeper: two shelves on the back wall with jars and tins, a rolled net on top.
for z in (1.45, 1.9):
    box(s, F, (W - 0.5, 0.24, 0.04), (0, BACK - 0.14, z), vkit.plank)
    for x in (-1.1, 1.1):
        box(s, F, (0.04, 0.2, 0.12), (x, BACK - 0.12, z - 0.08), vkit.trim)
for k, x in enumerate((-1.0, -0.72, -0.44, 0.35, 0.62, 0.9)):
    cyl(s, F, 0.065, 0.065, 0.17, (x, BACK - 0.14, 1.555), vkit.glass, segs=8)
    cyl(s, F, 0.07, 0.07, 0.035, (x, BACK - 0.14, 1.66), (vkit.canvas, vkit.brass, fishB)[k % 3], segs=8)
for k, x in enumerate((-0.95, -0.82, -0.69, -0.2, 0.0, 0.2, 0.75, 0.9)):
    cyl(s, F, 0.045, 0.045, 0.1, (x, BACK - 0.14, 1.97), (fishB, vkit.canvas, vkit.cream, vkit.brass)[k % 4], segs=8)
cyl(s, F, 0.09, 0.09, 0.42, (0.45, BACK - 0.14, 2.03), vkit.rope, segs=8, ry=math.pi / 2)
# Buoys hanging from the awning's beam, red with a white band; a lantern in the middle.
for x in (-1.25, 1.25):
    beam(s, F, (x, -0.12, 2.4), (x, -0.12, 2.12), 0.007, vkit.rope, segs=3)
    ball(s, F, 0.1, (x, -0.12, 2.03), vkit.canvas, subdiv=2)
    cyl(s, F, 0.102, 0.102, 0.05, (x, -0.12, 2.03), vkit.cream, segs=12)
beam(s, F, (0, -0.12, 2.4), (0, -0.12, 2.3), 0.008, vkit.iron, segs=3)
vkit.lantern(s, F, (0, -0.12, 2.12), 0.8)
# Dried fish on a string along the back of the roof.
beam(s, F, (-1.3, BACK - 0.3, 2.62), (1.3, BACK - 0.3, 2.62), 0.008, vkit.rope, segs=3)
for k in range(6):
    x = -1.1 + k * 0.44
    beam(s, F, (x, BACK - 0.3, 2.62), (x, BACK - 0.3, 2.5), 0.006, vkit.rope, segs=3)
    fish(s, x, BACK - 0.3, 2.32, math.pi / 2, (fishB, fishO, fishR)[k % 3], 0.28)
# A net hung on the left wall inside, with two floats in it.
G = F.sub((0, 0, 0), -90)
for k in range(7):
    t = k / 6
    beam(s, G, (-0.25 - 0.8 * t, -W / 2 + 0.06, 2.2), (-0.35 - 0.8 * t, -W / 2 + 0.06, 1.15), 0.006, vkit.rope, segs=3)
for k in range(5):
    z = 2.15 - k * 0.24
    beam(s, G, (-0.25, -W / 2 + 0.06, z), (-1.15, -W / 2 + 0.06, z - 0.04), 0.006, vkit.rope, segs=3)
for (y, z) in ((-0.6, 1.7), (-0.9, 1.35)):
    ball(s, G, 0.07, (y, -W / 2 + 0.1, z), vkit.cream, subdiv=1)
# Behind the keeper: a crate of fish on ice, a barrel, a crate; oars crossed on the right wall outside.
vkit.crate(s, F, 0.56, (0.95, 0.7, 0.1), 5)
box(s, F, (0.5, 0.5, 0.05), (0.95, 0.7, 0.67), ice)
for k in range(3):
    fish(s, 0.83 + k * 0.12, 0.65 + 0.05 * k, 0.73, 0.4 * k, (fishB, fishO, fishR)[k], 0.26)
vkit.barrel(s, F, 0.26, 0.72, (-1.0, 0.75, 0.1))
vkit.crate(s, F, 0.46, (-0.45, 0.9, 0.1), -10)
for k, ang in enumerate((0.5, -0.5)):
    box(s, F, (0.06, 1.8, 0.05), (W / 2 + 0.08, 0.55, 1.35), vkit.plank2, rx=ang)
    box(s, F, (0.05, 0.36, 0.16), (W / 2 + 0.08, 0.55 + 0.82 * math.cos(ang), 1.35 + 0.82 * math.sin(ang)), vkit.plank, rx=ang)
# Round the back: a life ring on the wall and two crates stacked by it.
for k in range(8):
    a0, a1 = k * TAU / 8, (k + 1) * TAU / 8
    tube_pts = [(0.75 + 0.24 * math.cos(a0 + (a1 - a0) * t / 3), BACK + 0.09, 1.55 + 0.24 * math.sin(a0 + (a1 - a0) * t / 3)) for t in range(4)]
    tube(s, tube_pts, 0.06, vkit.canvas if k % 2 == 0 else vkit.cream, segs=6)
beam(s, F, (0.75, BACK + 0.05, 1.82), (0.75, BACK + 0.05, 1.9), 0.012, vkit.iron, segs=4)
vkit.crate(s, F, 0.6, (-0.9, BACK + 0.45, 0.06), 8)
vkit.crate(s, F, 0.48, (-0.85, BACK + 0.45, 0.66), -6)
# A lantern on the front left post, a chalkboard of prices out front.
vkit.lantern(s, F, (-W / 2 - 0.12, FRONT + 0.05, 1.55), 0.8)
box(s, F, (0.2, 0.05, 0.05), (-W / 2 - 0.05, FRONT + 0.05, 1.82), vkit.trim)
for sy in (-1, 1):
    box(s, F, (0.55, 0.04, 0.75), (-W / 2 - 0.55, -1.25 + sy * 0.14, 0.42), vkit.trim, rx=sy * 0.2)
box(s, F, (0.46, 0.03, 0.62), (-W / 2 - 0.55, -1.41, 0.44), slate, rx=-0.2)
for k in range(3):
    box(s, F, (0.3 - 0.06 * k, 0.02, 0.025), (-W / 2 - 0.57, -1.43 - 0.03 * k, 0.6 - 0.14 * k), vkit.cream, rx=-0.2)
out.append(s.finish())

# ---------- Smithy: a lean-to of boards over a stone forge with a chimney, an anvil on a stump, a weapon rack ----------
f = Builder("Smithy", vkit.KIT + [coal, ember, water, leather, steel])
W, D = 4.0, 3.0
for k in range(9):                                                           # a floor of flagstones
    for j in range(7):
        x, y = -W / 2 + (k + 0.5) * W / 9, -1.4 + (j + 0.5) * D / 7
        box(f, F, (W / 9 - 0.04, D / 7 - 0.04, 0.1 + vkit.rng.uniform(0, 0.02)), (x, y, 0.06), vkit.stone if (k + j) % 3 else vkit.stone2)
vkit.stones(f, F, -W / 2, W / 2, 1.3, 1.6, 0.0, 1.0, rows=3)                  # the back wall: stone below,
vkit.plank_wall(f, F, -W / 2, W / 2, 1.0, 2.9, y=1.42)                         # boards above
vkit.plank_wall(f, F.sub((0, 0, 0), 90), 0.1, 1.45, 0.1, 1.05, y=-W / 2 - 0.02)  # a low board wall on the right
for x in (-W / 2, W / 2):                                                     # posts, braced
    post(f, x, -1.35, 2.45, 0.09)
    post(f, x, 1.45, 2.95, 0.09)
    beam(f, F, (x, -1.35, 2.0), (x, -0.85, 2.42), 0.04, vkit.trim, segs=4)
box(f, F, (W + 0.3, 0.14, 0.16), (0, -1.35, 2.45), vkit.trim)               # the front beam
vkit.shed_roof(f, F, W, -1.45, 1.55, 2.45, 2.98, over=0.3)
# The forge, back left: a hearth of rough stones with glowing coals, an iron hood, a stone chimney through the roof.
fx, fy = -1.25, 0.95
vkit.stones(f, F, fx - 0.65, fx + 0.65, fy - 0.5, fy + 0.45, 0.1, 0.85, rows=3)
box(f, F, (1.1, 0.8, 0.06), (fx, fy, 0.88), ember)
for i in range(7):
    a = i * TAU / 7
    ball(f, F, 0.11, (fx + 0.32 * math.cos(a), fy + 0.25 * math.sin(a), 0.93), coal)
ball(f, F, 0.13, (fx, fy, 0.95), coal)
cyl(f, F, 0.75, 0.25, 0.7, (fx, fy + 0.05, 1.75), vkit.iron, segs=4, rz=math.pi / 4)  # the hood
vkit.stones(f, F, fx - 0.25, fx + 0.25, fy - 0.15, fy + 0.35, 2.1, 4.1, rows=8)      # the chimney
box(f, F, (0.6, 0.6, 0.1), (fx, fy + 0.1, 4.15), vkit.stone2)
f.prism([(0.0, 0.0), (0.5, 0.12), (0.5, -0.12)], 0, 0.3, leather)              # bellows at its side
beam(f, F, (0.5, 0.0, 0.0), (0.75, 0.0, 0.05), 0.025, vkit.trim, segs=4)
# The anvil on a stump, front middle, with a hammer on it.
ax, ay = 0.2, -0.45
cyl(f, F, 0.32, 0.3, 0.55, (ax, ay, 0.33), vkit.trim, segs=10)
cyl(f, F, 0.27, 0.27, 0.02, (ax, ay, 0.61), vkit.plank, segs=10)
box(f, F, (0.5, 0.26, 0.16), (ax, ay, 0.68), vkit.iron)
box(f, F, (0.3, 0.18, 0.16), (ax, ay, 0.8), vkit.iron)
box(f, F, (0.62, 0.3, 0.13), (ax, ay, 0.94), steel)
cyl(f, F, 0.12, 0.0, 0.32, (ax - 0.46, ay, 0.94), steel, segs=6, ry=-math.pi / 2)
beam(f, F, (ax + 0.05, ay - 0.05, 1.03), (ax + 0.3, ay - 0.25, 1.03), 0.02, vkit.plank, segs=5)
box(f, F, (0.14, 0.07, 0.07), (ax + 0.03, ay - 0.03, 1.04), vkit.iron)
# The quench trough, front right: boards round dark water.
vkit.plank_wall(f, F, 0.95, 1.85, 0.1, 0.55, y=-1.0, t=0.04)
vkit.plank_wall(f, F.sub((0, 0, 0), 180), -1.85, -0.95, 0.1, 0.55, y=0.5, t=0.04)
box(f, F, (0.84, 0.44, 0.04), (1.4, -0.75, 0.5), water)
# A grindstone on its frame, front left, a coal barrel by the forge.
cyl(f, F, 0.32, 0.32, 0.1, (-1.55, -0.85, 0.62), vkit.stone, segs=14, ry=math.pi / 2)
for dx in (-0.12, 0.12):
    box(f, F, (0.06, 0.5, 0.06), (-1.55 + dx, -0.85, 0.32), vkit.trim, rx=0.4)
    box(f, F, (0.06, 0.5, 0.06), (-1.55 + dx, -0.85, 0.32), vkit.trim, rx=-0.4)
beam(f, F, (-1.72, -0.85, 0.62), (-1.38, -0.85, 0.62), 0.025, vkit.iron, segs=4)
vkit.barrel(f, F, 0.24, 0.62, (-0.35, 1.05, 0.1))
# The weapon rack on the back wall: three rails of seven slots (two pegs a slot), one for every weapon the shop sells; the
# mod hangs a common of each on them (Village.cs WeaponRack; keep in step).
RACK_X, RACK_Z = tuple(-0.15 + k * 0.33 for k in range(7)), (2.4, 1.85, 1.3)
for z in RACK_Z:
    box(f, F, (2.4, 0.1, 0.08), (0.84, 1.36, z), vkit.trim)
    for x in RACK_X:
        for dx in (-0.1, 0.1):
            beam(f, F, (x + dx, 1.33, z), (x + dx, 1.17, z + 0.04), 0.014, vkit.iron, segs=4)
# Tongs and hammers hanging on the boards by the forge.
for k, x in enumerate((-0.52, -0.42)):
    beam(f, F, (x, 1.36, 2.35), (x + 0.02 * k, 1.36, 1.85), 0.015, vkit.iron, segs=4)
box(f, F, (0.14, 0.06, 0.07), (-0.52, 1.34, 1.84), vkit.iron)
# The sign over the front, hanging from the beam: a hammer crossing a gun barrel.
for x in (-0.5, 0.5):
    beam(f, F, (x, -1.5, 2.37), (x, -1.5, 2.2), 0.008, vkit.iron, segs=4)
box(f, F, (1.4, 0.06, 0.45), (0, -1.52, 1.98), vkit.plank)
box(f, F, (1.48, 0.04, 0.53), (0, -1.49, 1.98), vkit.trim)
beam(f, F, (-0.4, -1.57, 1.82), (0.35, -1.57, 2.14), 0.035, vkit.iron, segs=6)
box(f, F, (0.28, 0.06, 0.14), (0.36, -1.58, 2.15), steel, ry=math.radians(-25))
beam(f, F, (-0.35, -1.58, 2.14), (0.42, -1.58, 1.82), 0.03, vkit.trim, segs=5)
box(f, F, (0.12, 0.05, 0.22), (-0.38, -1.59, 2.14), steel)
box(f, F, (0.06, 0.3, 0.06), (W / 2, -1.5, 2.15), vkit.trim)                 # a lantern on the front right post
vkit.lantern(f, F, (W / 2, -1.62, 1.9), 0.9)
out.append(f.finish())

export(out)
