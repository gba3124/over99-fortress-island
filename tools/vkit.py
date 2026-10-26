# The village kit: the game's own look for buildings and props (its cabin: walls of vertical planks with dark seams,
# framed windows with a cross in them, plank doors with a Z brace, roofs of overlapping boards, rustic props), as pieces
# the station, vault and island scripts put together. Everything goes into a bkit Builder; a Frame places a piece: an
# origin and a turn about Z (Blender: Z up, -Y the front).
import math
import random

import bmesh
from mathutils import Matrix, Vector
from bkit import TAU, mat

rng = random.Random(11)

# ---------- the palette: muted, like the game's ----------
plank = mat("VkPlank", (0.52, 0.36, 0.23), force=True)
plank2 = mat("VkPlank2", (0.46, 0.31, 0.2), force=True)        # every other board a shade darker
seam = mat("VkSeam", (0.2, 0.13, 0.09), force=True)            # what shows between boards
trim = mat("VkTrim", (0.3, 0.21, 0.15), force=True)            # frames, posts, beams
roofw = mat("VkRoof", (0.33, 0.24, 0.19), force=True)          # roof boards
roofw2 = mat("VkRoof2", (0.27, 0.19, 0.15), force=True)
roofr = mat("VkRoofRed", (0.48, 0.2, 0.16), force=True)        # a weathered red roof
roofr2 = mat("VkRoofRed2", (0.4, 0.16, 0.13), force=True)
stone = mat("VkStone", (0.56, 0.54, 0.5), force=True)
stone2 = mat("VkStone2", (0.44, 0.43, 0.41), force=True)
glass = mat("VkGlass", (0.2, 0.27, 0.33), force=True)
lit = mat("VkLit", (1.0, 0.8, 0.45), (1.0, 0.65, 0.25), force=True)  # a lantern's glow
iron = mat("VkIron", (0.18, 0.18, 0.2), force=True)
brass = mat("VkBrass", (0.72, 0.56, 0.26), force=True)
soil = mat("VkSoil", (0.3, 0.22, 0.15), force=True)
leaf = mat("VkLeaf", (0.3, 0.46, 0.22), force=True)
petalA = mat("VkPetalA", (0.92, 0.74, 0.25), force=True)
petalB = mat("VkPetalB", (0.82, 0.36, 0.42), force=True)
petalC = mat("VkPetalC", (0.86, 0.86, 0.8), force=True)
rope = mat("VkRope", (0.66, 0.56, 0.4), force=True)
cream = mat("VkCream", (0.86, 0.82, 0.72), force=True)
canvas = mat("VkCanvas", (0.62, 0.2, 0.17), force=True)
KIT = [plank, plank2, seam, trim, roofw, roofw2, roofr, roofr2, stone, stone2, glass, lit, iron, brass, soil, leaf, petalA, petalB, petalC, rope, cream, canvas]


class Frame:
    # A place to build: everything given in its own axes (x along the front, y back, z up) lands turned by yaw (degrees).
    def __init__(self, origin, yaw=0.0):
        self.o, self.yaw = Vector(origin), math.radians(yaw)

    def m(self, at, rx=0.0, ry=0.0, rz=0.0):
        return (Matrix.Translation(self.o) @ Matrix.Rotation(self.yaw, 4, "Z") @ Matrix.Translation(Vector(at))
                @ Matrix.Rotation(rz, 4, "Z") @ Matrix.Rotation(ry, 4, "Y") @ Matrix.Rotation(rx, 4, "X"))

    def p(self, at):
        return self.m(at) @ Vector((0, 0, 0))

    def sub(self, at, yaw=0.0):  # a frame inside this one
        f = Frame(self.p(at), 0.0)
        f.yaw = self.yaw + math.radians(yaw)
        return f


def _tag(b, verts, m):
    b._tag({f for v in verts for f in v.link_faces}, b.mats.index(m))


def box(b, F, size, at, m, rx=0.0, ry=0.0, rz=0.0):
    r = bmesh.ops.create_cube(b.bm, size=1.0, matrix=F.m(at, rx, ry, rz) @ Matrix.Diagonal((*size, 1.0)))
    _tag(b, r["verts"], m)


def cyl(b, F, r1, r2, depth, at, m, segs=8, rx=0.0, ry=0.0, rz=0.0):
    res = bmesh.ops.create_cone(b.bm, cap_ends=True, segments=segs, radius1=r1, radius2=r2, depth=depth, matrix=F.m(at, rx, ry, rz))
    _tag(b, res["verts"], m)


def ball(b, F, r, at, m, subdiv=1):
    res = bmesh.ops.create_icosphere(b.bm, subdivisions=subdiv, radius=r, matrix=F.m(at))
    _tag(b, res["verts"], m)


def beam(b, F, a, c, r, m, segs=6):
    a, c = F.p(a), F.p(c)
    d = c - a
    q = d.to_track_quat("Z", "Y")
    res = bmesh.ops.create_cone(b.bm, cap_ends=True, segments=segs, radius1=r, radius2=r, depth=d.length, matrix=Matrix.Translation((a + c) / 2) @ q.to_matrix().to_4x4())
    _tag(b, res["verts"], m)


# ---------- walls, windows, doors, roofs ----------

def plank_wall(b, F, x0, x1, z0, z1, y=0.0, t=0.05, board=0.17, skip=()):
    # Vertical boards from x0 to x1, z0 to z1, their face at y (the outside toward -y), with dark seams between, and a
    # batten over every other seam. skip: (xa, xb, za, zb) holes (a door, a window).
    n = max(1, round((x1 - x0) / board))
    w = (x1 - x0) / n
    for k in range(n):
        xa = x0 + k * w
        xc = xa + w / 2
        spans = [(z0, z1)]
        for (ha, hb, hz0, hz1) in skip:
            if xc > ha and xc < hb:
                spans = [(lo, hi) for lo, hi in ((z0, hz0), (hz1, z1)) if hi - lo > 0.02]
        for lo, hi in spans:
            top = hi + (rng.uniform(-0.015, 0.0) if hi >= z1 - 1e-6 else 0.0)
            box(b, F, (w - 0.014, t, top - lo), (xc, y, (lo + top) / 2), plank if k % 2 == 0 else plank2)
            box(b, F, (w + 0.002, t * 0.6, hi - lo), (xc, y + t * 0.5, (lo + hi) / 2), seam)  # behind it: the dark seams (none across a hole)
        if k % 2 == 1 and k < n - 1:
            for lo, hi in spans:
                box(b, F, (0.035, 0.02, hi - lo), (xa + w, y - t * 0.6, (lo + hi) / 2), trim)


def window(b, F, x, z, w=0.6, h=0.6, y=0.0, shutters=True):
    # A framed window with a cross in it, a sill, and plank shutters folded open.
    box(b, F, (w, 0.03, h), (x, y + 0.01, z), glass)
    for dz in (-h / 2, h / 2):
        box(b, F, (w + 0.1, 0.07, 0.06), (x, y - 0.02, z + dz), trim)
    for dx in (-w / 2, w / 2):
        box(b, F, (0.06, 0.07, h + 0.1), (x + dx, y - 0.02, z), trim)
    box(b, F, (w, 0.04, 0.035), (x, y - 0.015, z), trim)
    box(b, F, (0.035, 0.04, h), (x, y - 0.015, z), trim)
    box(b, F, (w + 0.18, 0.12, 0.05), (x, y - 0.05, z - h / 2 - 0.05), trim)  # the sill
    if shutters:
        for s in (-1, 1):
            sx = x + s * (w / 2 + w * 0.27)
            for k in range(2):
                box(b, F, (w * 0.25 - 0.01, 0.035, h + 0.04), (sx - s * w * 0.06 + (k - 0.5) * w * 0.25 * s, y - 0.04, z), plank if k == 0 else plank2)
            box(b, F, (w * 0.5, 0.04, 0.05), (sx, y - 0.065, z + h * 0.3), trim)
            box(b, F, (w * 0.5, 0.04, 0.05), (sx, y - 0.065, z - h * 0.3), trim)


def door(b, F, x, z0, w=0.85, h=1.45, y=0.0, double=False, straps=False):
    # Plank door(s) in a frame: a Z brace on each leaf, a ring handle; iron straps on a storehouse's.
    leaves = [(x - w / 4, w / 2), (x + w / 4, w / 2)] if double else [(x, w)]
    for cx, lw in leaves:
        n = max(2, round(lw / 0.16))
        for k in range(n):
            bx = cx - lw / 2 + (k + 0.5) * lw / n
            box(b, F, (lw / n - 0.012, 0.05, h), (bx, y, z0 + h / 2), plank if k % 2 == 0 else plank2)
        for dz in (0.22, h - 0.22):
            box(b, F, (lw - 0.06, 0.03, 0.09), (cx, y - 0.035, z0 + dz), iron if straps else trim)
        ang = math.atan2(h - 0.44, lw - 0.1)
        box(b, F, (math.hypot(h - 0.44, lw - 0.1), 0.03, 0.08), (cx, y - 0.035, z0 + h / 2), trim, ry=-ang)
    for dx in (-w / 2 - 0.04, w / 2 + 0.04):
        box(b, F, (0.08, 0.1, h + 0.08), (x + dx, y - 0.01, z0 + h / 2), trim)
    box(b, F, (w + 0.16, 0.1, 0.08), (x, y - 0.01, z0 + h + 0.04), trim)
    hx = x + (0.06 if double else w * 0.32)
    ball(b, F, 0.035, (hx, y - 0.06, z0 + h * 0.48), brass)
    if double:
        ball(b, F, 0.035, (x - 0.06, y - 0.06, z0 + h * 0.48), brass)
        box(b, F, (0.14, 0.03, 0.1), (x, y - 0.06, z0 + h * 0.56), iron)  # a padlock across the leaves


def gable_roof(b, F, w, d, z0, rise, over=0.28, m=None, m2=None, ridge_x=True):
    # Two slopes of overlapping boards (rows a shade apart) meeting at a capped ridge: the ridge along x when ridge_x
    # (slopes to the front and back), the eaves at z0 over the walls, the ridge `rise` above, `over` past the walls.
    m, m2 = m or roofw, m2 or roofw2
    run = (d if ridge_x else w) / 2
    half = run + over
    length = (w if ridge_x else d) + over * 2
    pitch = math.atan2(rise, run)
    rows = max(3, round(half / math.cos(pitch) / 0.2))
    step = half / rows
    for s in (-1, 1):
        for k in range(rows):
            dist = (k + 0.5) * step
            z = z0 + rise - dist * math.tan(pitch) + 0.03 + rng.uniform(-0.008, 0.008)
            mm = m if k % 2 == 0 else m2
            if ridge_x:
                box(b, F, (length, step / math.cos(pitch) + 0.05, 0.045), (0, s * dist, z), mm, rx=-s * pitch)
            else:
                box(b, F, (step / math.cos(pitch) + 0.05, length, 0.045), (s * dist, 0, z), mm, ry=s * pitch)
    if ridge_x:
        box(b, F, (length + 0.04, 0.16, 0.1), (0, 0, z0 + rise + 0.06), trim)
    else:
        box(b, F, (0.16, length + 0.04, 0.1), (0, 0, z0 + rise + 0.06), trim)


def shed_roof(b, F, w, y0, y1, z0, z1, over=0.25, m=None, m2=None):
    # A single slope of overlapping boards from the front (y0, low at z0) to the back (y1, high at z1), rafters under it.
    m, m2 = m or roofw, m2 or roofw2
    pitch = math.atan2(z1 - z0, y1 - y0)
    run = (y1 - y0) + over * 2
    rows = max(4, round(run / math.cos(pitch) / 0.22))
    step = run / rows
    for k in range(rows):
        yy = y0 - over + (k + 0.5) * step
        zz = z0 + (yy - y0) * math.tan(pitch) + 0.06 + rng.uniform(-0.008, 0.008)
        box(b, F, (w + over * 2, step / math.cos(pitch) + 0.05, 0.045), (0, yy, zz), m if k % 2 == 0 else m2, rx=pitch)
    for k in range(5):
        x = -w / 2 + k * w / 4
        beam(b, F, (x, y0 - over * 0.8, z0 - over * 0.8 * math.tan(pitch)), (x, y1 + over * 0.5, z1 + over * 0.5 * math.tan(pitch)), 0.05, trim, segs=4)


def gable_end(b, F, w, z0, rise, y, m=plank):
    # The triangle under a roof's end: boards stepping up to the ridge.
    n = max(3, round(w / 0.17))
    for k in range(n):
        x = -w / 2 + (k + 0.5) * w / n
        top = z0 + rise * (1 - abs(x) / (w / 2))
        if top - z0 > 0.03:
            box(b, F, (w / n - 0.014, 0.05, top - z0), (x, y, (z0 + top) / 2), plank if k % 2 == 0 else plank2)


def stones(b, F, x0, x1, y0, y1, z0, z1, rows=2):
    # A foundation of rough blocks.
    box(b, F, (x1 - x0, y1 - y0, z1 - z0), ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), stone2)
    h = (z1 - z0) / rows
    for r in range(rows):
        for side in (y0, y1):
            x = x0
            while x < x1 - 0.05:
                bw = min(rng.uniform(0.25, 0.45), x1 - x)
                box(b, F, (bw - 0.03, 0.06, h - 0.03), (x + bw / 2, side + (-0.02 if side == y0 else 0.02), z0 + h * (r + 0.5)), stone if rng.random() < 0.6 else stone2)
                x += bw
        for side in (x0, x1):
            y = y0
            while y < y1 - 0.05:
                bd = min(rng.uniform(0.25, 0.45), y1 - y)
                box(b, F, (0.06, bd - 0.03, h - 0.03), (side + (-0.02 if side == x0 else 0.02), y + bd / 2, z0 + h * (r + 0.5)), stone if rng.random() < 0.6 else stone2)
                y += bd


# ---------- props ----------

def lantern(b, F, at, s=1.0):
    # An iron lantern: a cap, four corner bars round a glowing glass, a ring on top.
    x, y, z = at
    cyl(b, F, 0.13 * s, 0.03 * s, 0.1 * s, (x, y, z + 0.17 * s), iron, segs=4, rz=math.pi / 4)
    box(b, F, (0.17 * s, 0.17 * s, 0.03 * s), (x, y, z - 0.13 * s), iron)
    box(b, F, (0.12 * s, 0.12 * s, 0.22 * s), (x, y, z), lit)
    for dx in (-1, 1):
        for dy in (-1, 1):
            box(b, F, (0.025 * s, 0.025 * s, 0.26 * s), (x + dx * 0.07 * s, y + dy * 0.07 * s, z), iron)
    beam(b, F, (x, y, z + 0.22 * s), (x, y, z + 0.3 * s), 0.012 * s, iron, segs=4)


def lamp_post(b, F, h=2.6):
    # A square wooden post on a stone, an arm with a brace, a lantern hanging from it.
    box(b, F, (0.32, 0.32, 0.18), (0, 0, 0.09), stone)
    box(b, F, (0.14, 0.14, h), (0, 0, h / 2), trim)
    box(b, F, (0.18, 0.18, 0.06), (0, 0, h), trim)
    box(b, F, (0.62, 0.08, 0.08), (0.27, 0, h - 0.12), trim)
    beam(b, F, (0.05, 0, h - 0.5), (0.36, 0, h - 0.14), 0.025, trim, segs=4)
    beam(b, F, (0.5, 0, h - 0.16), (0.5, 0, h - 0.34), 0.008, iron, segs=4)
    lantern(b, F, (0.5, 0, h - 0.55))


def bench(b, F, w=1.5):
    # Three seat boards and two for the back on stout legs, arms at the ends.
    for k in range(3):
        box(b, F, (w, 0.11, 0.045), (0, -0.12 + k * 0.12, 0.45), plank if k % 2 == 0 else plank2)
    for k in range(2):
        box(b, F, (w, 0.04, 0.11), (0, 0.2, 0.62 + k * 0.15), plank2 if k else plank, rx=-0.12)
    for s in (-1, 1):
        box(b, F, (0.08, 0.08, 0.45), (s * (w / 2 - 0.12), -0.15, 0.22), trim)
        box(b, F, (0.08, 0.08, 0.85), (s * (w / 2 - 0.12), 0.22, 0.42), trim)
        box(b, F, (0.08, 0.46, 0.06), (s * (w / 2 - 0.12), 0.03, 0.66), trim)


def flowers(b, F, x0, x1, y0, y1, z, n=6):
    # Stems with leaves and a bloom each, in three colours.
    for k in range(n):
        x, y = rng.uniform(x0, x1), rng.uniform(y0, y1)
        hh = rng.uniform(0.12, 0.24)
        beam(b, F, (x, y, z), (x, y, z + hh), 0.008, leaf, segs=3)
        cyl(b, F, 0.05, 0.0, 0.08, (x + 0.03, y, z + hh * 0.45), leaf, segs=3, ry=0.8)
        ball(b, F, rng.uniform(0.035, 0.05), (x, y, z + hh), (petalA, petalB, petalC)[k % 3], subdiv=1)


def planter(b, F, w=1.0, d=0.4, h=0.35):
    # A box of horizontal boards with corner posts, soil, flowers.
    for k in range(3):
        z = 0.06 + k * (h - 0.06) / 3 + (h - 0.06) / 6
        for s in (-1, 1):
            box(b, F, (w, 0.04, (h - 0.06) / 3 - 0.012), (0, s * d / 2, z), plank if k % 2 == 0 else plank2)
            box(b, F, (0.04, d, (h - 0.06) / 3 - 0.012), (s * w / 2, 0, z), plank2 if k % 2 == 0 else plank)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(b, F, (0.07, 0.07, h + 0.04), (sx * w / 2, sy * d / 2, (h + 0.04) / 2), trim)
    box(b, F, (w - 0.04, d - 0.04, 0.05), (0, 0, h - 0.04), soil)
    flowers(b, F, -w / 2 + 0.08, w / 2 - 0.08, -d / 2 + 0.06, d / 2 - 0.06, h - 0.02, n=int(w * 7))


def barrel(b, F, r=0.3, h=0.8, at=(0, 0, 0)):
    # Staves (alternating shades, a column each) bulging at the middle, two iron hoops, a lid.
    x, y, z = at
    n = 14
    prof = [(0.0, r * 0.84), (h * 0.25, r * 0.96), (h * 0.5, r), (h * 0.75, r * 0.96), (h, r * 0.84)]
    rings = [[F.p((x + rr * math.cos(k * TAU / n), y + rr * math.sin(k * TAU / n), z + zz)) for k in range(n)] for zz, rr in prof]
    from bkit import loft
    loft(b, rings, plank, mf=lambda i, j: plank if j % 2 == 0 else plank2, caps=(plank2, plank2))
    for zz in (h * 0.2, h * 0.8):
        cyl(b, F, r * 0.99, r * 0.99, 0.045, (x, y, z + zz), iron, segs=14)
    cyl(b, F, r * 0.78, r * 0.78, 0.02, (x, y, z + h), trim, segs=12)


def crate(b, F, s=0.7, at=(0, 0, 0), yaw=0.0):
    # Boards on every face inside a frame of darker edges, a cross brace on the front.
    G = F.sub(at, yaw)
    h = s
    for k in range(3):
        z = (k + 0.5) * h / 3
        for sy in (-1, 1):
            box(b, G, (s - 0.04, 0.03, h / 3 - 0.015), (0, sy * s / 2, z), plank if k % 2 == 0 else plank2)
        for sx in (-1, 1):
            box(b, G, (0.03, s - 0.04, h / 3 - 0.015), (sx * s / 2, 0, z), plank2 if k % 2 == 0 else plank)
    box(b, G, (s - 0.04, s - 0.04, 0.03), (0, 0, h), plank)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(b, G, (0.06, 0.06, h + 0.02), (sx * s / 2, sy * s / 2, h / 2), trim)
    for sy in (-1, 1):
        for z in (0.03, h):
            box(b, G, (s + 0.04, 0.05, 0.05), (0, sy * s / 2, z), trim)
    ang = math.atan2(h - 0.1, s - 0.1)
    box(b, G, (math.hypot(h - 0.1, s - 0.1), 0.03, 0.05), (0, -s / 2 - 0.02, h / 2), trim, ry=-ang)


def sign_icon(b, G, kind, x, z, paint):
    # A small emblem on both faces of an arm board: a fish, an anvil, a crate, a lighthouse.
    for sy in (-1, 1):
        y = sy * 0.04
        if kind == "fish":
            box(b, G, (0.18, 0.03, 0.08), (x, y, z), paint)
            for rz in (0.55, -0.55):
                box(b, G, (0.1, 0.03, 0.035), (x - 0.13, y, z), paint, ry=rz)
            box(b, G, (0.025, 0.034, 0.025), (x + 0.05, y, z + 0.02), cream)
        elif kind == "anvil":
            box(b, G, (0.14, 0.03, 0.035), (x, y, z - 0.065), paint)
            box(b, G, (0.07, 0.03, 0.05), (x, y, z - 0.025), paint)
            box(b, G, (0.2, 0.03, 0.05), (x, y, z + 0.02), paint)
            box(b, G, (0.07, 0.03, 0.035), (x + 0.13, y, z + 0.03), paint, ry=0.35)
        elif kind == "crate":
            box(b, G, (0.15, 0.03, 0.15), (x, y, z), paint)
            for rz in (0.785, -0.785):
                box(b, G, (0.19, 0.034, 0.02), (x, y, z), brass, ry=rz)
        elif kind == "tower":
            cyl(b, G, 0.05, 0.035, 0.16, (x, y, z - 0.01), paint, segs=6)
            box(b, G, (0.11, 0.034, 0.02), (x, y, z + 0.075), iron)
            box(b, G, (0.05, 0.034, 0.04), (x, y, z + 0.105), lit)
            cyl(b, G, 0.05, 0.0, 0.05, (x, y, z + 0.15), canvas, segs=6)


def signpost(b, F, arms):
    # A post with pointed boards: arms = [(yaw degrees, height, paint material[, emblem])]; a colour band along each
    # board's top edge and the emblem on both faces (sign_icon).
    box(b, F, (0.36, 0.36, 0.16), (0, 0, 0.08), stone)
    top = max(a[1] for a in arms) + 0.3
    box(b, F, (0.12, 0.12, top), (0, 0, top / 2), trim)
    cyl(b, F, 0.1, 0.0, 0.14, (0, 0, top + 0.07), trim, segs=4, rz=math.pi / 4)
    for yaw, z, paint, *icon in arms:
        G = F.sub((0, 0, 0), yaw)
        box(b, G, (0.66, 0.045, 0.24), (0.39, 0, z), plank)                      # the board
        box(b, G, (0.68, 0.03, 0.28), (0.39, 0, z), trim)                        # its dark edge, top and bottom
        cyl(b, G, 0.15, 0.0, 0.12, (0.78, 0, z), plank, segs=3, ry=math.pi / 2)  # the point
        box(b, G, (0.56, 0.05, 0.04), (0.36, 0, z + 0.085), paint)               # the colour band
        box(b, G, (0.08, 0.08, 0.05), (0.1, 0, z - 0.145), iron)                 # the bracket it hangs on
        if icon:
            sign_icon(b, G, icon[0], 0.42, z - 0.02, paint if icon[0] in ("fish", "tower") else iron)


def fence(b, F, length, h=0.9, posts=4):
    for k in range(posts):
        x = -length / 2 + k * length / (posts - 1)
        box(b, F, (0.1, 0.1, h), (x, 0, h / 2), trim)
        cyl(b, F, 0.07, 0.0, 0.08, (x, 0, h + 0.04), trim, segs=4, rz=math.pi / 4)
    for z in (h * 0.4, h * 0.8):
        box(b, F, (length, 0.04, 0.09), (0, -0.06, z), plank)


# ---------- finer pieces (the stations' props): any turn, chamfered boxes, turned shapes, chains, hooped barrels, slatted
# crates, a glazed lantern. None of them draws on rng (the shared one above shapes the walls and roofs). ----------

glow2 = mat("VkGlow2", (0.95, 0.74, 0.42), (0.85, 0.5, 0.2), force=True)  # a lantern's glass, lit from inside
KIT2 = [glow2]


class MFrame(Frame):
    # A frame turned any way (a fish on its side, a tilted tray): a whole matrix where Frame has an origin and a yaw.
    def __init__(self, mtx):
        self.M = Matrix(mtx)

    def m(self, at, rx=0.0, ry=0.0, rz=0.0):
        return self.M @ Matrix.Translation(Vector(at)) @ Matrix.Rotation(rz, 4, "Z") @ Matrix.Rotation(ry, 4, "Y") @ Matrix.Rotation(rx, 4, "X")

    def p(self, at):
        return self.M @ Vector(at)

    def sub(self, at, yaw=0.0):
        return MFrame(self.m(at, rz=math.radians(yaw)))


def turned(F, at, rx=0.0, ry=0.0, rz=0.0):  # a frame at `at` in F, turned about x, then y, then z (radians)
    return MFrame(F.m(at, rx, ry, rz))


def cbox(b, F, size, at, m, c=0.012, rx=0.0, ry=0.0, rz=0.0):
    # A box with every edge chamfered by c: the game's flat light catches each edge in a bright line. 26 faces.
    h = [s / 2 for s in size]
    c = min(c, *(x * 0.45 for x in h))
    mtx = F.m(at, rx, ry, rz)
    P = {}
    for s in ((x, y, z) for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)):
        for a in range(3):
            q = [s[k] * (h[k] - c) for k in range(3)]
            q[a] = s[a] * h[a]
            P[s, a] = b.bm.verts.new(mtx @ Vector(q))
    mi = b.mats.index(m)

    def face(vs):
        b.bm.faces.new(vs).material_index = mi

    def S(a, sa, u, su, v, sv):
        s = [0, 0, 0]
        s[a], s[u], s[v] = sa, su, sv
        return tuple(s)

    for a in range(3):
        u, v = (a + 1) % 3, (a + 2) % 3
        for sa in (-1, 1):
            face([P[S(a, sa, u, su, v, sv), a] for su, sv in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
        for su in (-1, 1):
            for sv in (-1, 1):
                lo, hi = S(a, -1, u, su, v, sv), S(a, 1, u, su, v, sv)
                face([P[lo, u], P[hi, u], P[hi, v], P[lo, v]])
    for s in ((x, y, z) for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)):
        face([P[s, 0], P[s, 1], P[s, 2]])


def lathe(b, F, prof, at, m, segs=8, mf=None, rx=0.0, ry=0.0, rz=0.0, caps=True, sx=1.0):
    # A turned shape: the profile [(radius, height)] from the bottom up, revolved round the frame's z at `at` (a radius of 0
    # closes that end in a point; sx squashes it across x, for an oval). mf(i, j): the material of band i (point i to i + 1)
    # at column j: a jar's label, its lid, a barrel's staves. caps=False leaves the ends open (a hoop).
    from bkit import loft
    mtx = F.m(at, rx, ry, rz)
    a0 = math.pi / segs
    rings = [[mtx @ Vector((0, 0, z))] if r <= 0 else
             [mtx @ Vector((r * sx * math.cos(a0 + k * TAU / segs), r * math.sin(a0 + k * TAU / segs), z)) for k in range(segs)] for r, z in prof]
    if caps:
        loft(b, rings, m, mf=mf, caps=(mf(0, 0), mf(len(prof) - 2, 0)) if mf else None)
        return
    vs = [[b.bm.verts.new(p) for p in r] for r in rings]
    for i in range(len(vs) - 1):
        for j in range(segs):
            k = (j + 1) % segs
            b.bm.faces.new((vs[i][j], vs[i][k], vs[i + 1][k], vs[i + 1][j])).material_index = b.mats.index(mf(i, j) if mf else m)


def chain(b, F, a, c, m, link=0.04, wire=0.006, pts=6):
    # Links from a to c, each a flat oval turned a quarter from the last (`pts` points round, a three-sided wire).
    from bkit import tube
    A, Cc = F.p(a), F.p(c)
    d = Cc - A
    n = max(1, round(d.length / (link * 0.72)))
    t = d.normalized()
    e1 = t.orthogonal().normalized()
    e2 = t.cross(e1)
    for i in range(n):
        mid = A + d * ((i + 0.5) / n)
        e = e1 if i % 2 == 0 else e2
        tube(b, [mid + t * (link / 2 - wire) * math.cos(k * TAU / pts) + e * (link * 0.32 - wire) * math.sin(k * TAU / pts) for k in range(pts)], wire, m,
             segs=3, closed=True)


def barrel2(b, F, r=0.3, h=0.8, at=(0, 0, 0), staves=12, hoops=(0.09, 0.27, 0.73, 0.91), head=True, inset=0.035):
    # A coopered barrel: staves bulging at the middle (alternate shades), the staves' ends standing past the head (the chime),
    # a head of three boards, iron hoops standing proud. head=False leaves the inside showing down to `inset` (for coal).
    x, y, z = at
    G = F.sub((x, y, z))
    prof = [(r * 0.86, 0.0), (r * 0.97, h * 0.25), (r, h * 0.5), (r * 0.97, h * 0.75), (r * 0.86, h)]
    rim = r * 0.86 - 0.022
    prof += [(rim, h), (rim, h - inset), (0, h - inset)]
    lathe(b, G, prof, (0, 0, 0), plank, segs=staves, mf=lambda i, j: (plank if j % 2 == 0 else plank2) if i < 6 else plank)
    if head:
        for k in (-1, 1):
            box(b, G, (rim * 1.85, 0.012, 0.006), (0, k * rim * 0.34, h - inset + 0.002), seam)
    for t in hoops:
        zz = h * t
        k = min(3, int(t * 4))
        rr = prof[k][0] + (prof[k + 1][0] - prof[k][0]) * (t * 4 - k)  # the staves' radius there
        lathe(b, G, [(rr - 0.004, zz - 0.022), (rr + 0.011, zz - 0.018), (rr + 0.011, zz + 0.018), (rr - 0.004, zz + 0.022)], (0, 0, 0), iron,
              segs=staves, caps=False)


def crate2(b, F, size=(0.6, 0.45, 0.4), at=(0, 0, 0), yaw=0.0, slats=3, lid=True, nails=True):
    # A slatted crate: chamfered corner posts, horizontal slats with gaps on every side (alternate shades), a rim round the
    # top, a floor, nail heads at the slat ends on the long sides; a lid of boards, or open (lid=False) to show what's in it.
    G = F.sub(at, yaw)
    w, d, h = size
    gap = 0.018
    sh = (h - 0.02 - gap * (slats - 1)) / slats
    for k in range(slats):
        zc = 0.02 + k * (sh + gap) + sh / 2
        for sy in (-1, 1):
            box(b, G, (w - 0.05, 0.02, sh), (0, sy * (d / 2 - 0.01), zc), plank if (k + (sy > 0)) % 2 == 0 else plank2)
            if nails:
                for sx in (-1, 1):
                    box(b, G, (0.014, 0.008, 0.014), (sx * (w / 2 - 0.055), sy * (d / 2 + 0.002), zc), iron)
        for sx in (-1, 1):
            box(b, G, (0.02, d - 0.05, sh), (sx * (w / 2 - 0.01), 0, zc), plank2 if k % 2 == 0 else plank)
    for sx in (-1, 1):
        for sy in (-1, 1):
            cbox(b, G, (0.05, 0.05, h + 0.01), (sx * (w / 2 - 0.02), sy * (d / 2 - 0.02), (h + 0.01) / 2), trim, c=0.008)
    box(b, G, (w - 0.04, d - 0.04, 0.02), (0, 0, 0.015), plank2)
    for sy in (-1, 1):
        box(b, G, (w + 0.01, 0.035, 0.03), (0, sy * (d / 2 - 0.005), h - 0.005), trim)
    for sx in (-1, 1):
        box(b, G, (0.035, d - 0.03, 0.03), (sx * (w / 2 - 0.005), 0, h - 0.005), trim)
    if lid:
        n = max(2, round(w / 0.16))
        for k in range(n):
            box(b, G, (w / n - 0.012, d - 0.03, 0.022), (-w / 2 + (k + 0.5) * w / n, 0, h + 0.016), plank if k % 2 == 0 else plank2)
        box(b, G, (w - 0.04, 0.05, 0.02), (0, 0, h + 0.036), trim)


def lantern2(b, F, at, s=1.0):
    # A glazed lantern: a stepped base, four corner posts round the glass (a flame-bright core behind it), a wire guard across
    # each pane, a pyramid cap with a vented chimney and a ring on top.
    from bkit import torus
    x, y, z = at
    G = F.sub((x, y, z))
    q = s
    cbox(b, G, (0.17 * q, 0.17 * q, 0.03 * q), (0, 0, -0.135 * q), iron, c=0.006 * q)
    cbox(b, G, (0.13 * q, 0.13 * q, 0.02 * q), (0, 0, -0.16 * q), iron, c=0.005 * q)
    box(b, G, (0.125 * q, 0.125 * q, 0.22 * q), (0, 0, 0), glow2)
    box(b, G, (0.07 * q, 0.07 * q, 0.12 * q), (0, 0, -0.02 * q), lit)
    for dx in (-1, 1):
        for dy in (-1, 1):
            box(b, G, (0.024 * q, 0.024 * q, 0.25 * q), (dx * 0.067 * q, dy * 0.067 * q, 0), iron)
    for k in range(4):
        H = G.sub((0, 0, 0), 90 * k)
        box(b, H, (0.11 * q, 0.012 * q, 0.012 * q), (0, -0.066 * q, 0.03 * q), iron)
        box(b, H, (0.012 * q, 0.012 * q, 0.2 * q), (0, -0.066 * q, 0), iron)
    cbox(b, G, (0.175 * q, 0.175 * q, 0.025 * q), (0, 0, 0.122 * q), iron, c=0.006 * q)
    cyl(b, G, 0.12 * q, 0.035 * q, 0.09 * q, (0, 0, 0.18 * q), iron, segs=4, rz=math.pi / 4)
    cyl(b, G, 0.032 * q, 0.032 * q, 0.04 * q, (0, 0, 0.245 * q), iron, segs=6)
    cyl(b, G, 0.05 * q, 0.0, 0.03 * q, (0, 0, 0.28 * q), iron, segs=6)
    torus(b, G.p((0, 0, 0.32 * q)), G.m((0, 0, 0)).to_3x3() @ Vector((1, 0, 0)), 0.03 * q, 0.007 * q, iron, segs=8, tsegs=3)
