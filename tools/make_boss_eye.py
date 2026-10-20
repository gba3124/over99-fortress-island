# The Eye at the End (wave 100): a giant eye in a socket of black glass, made in the parts the game moves (BossEye.cs):
#   BossEyeShell   the socket: plates of black glass over a dark core; a few long cracks glow red between them
#   BossEyeBall    the eyeball: it turns to stare at whoever is looking (a red iris, a slit pupil, veins)
#   BossEyeLidTop / BossEyeLidBottom   glass plates like the socket's; shut, they meet in a glowing red crack
#   BossEyeHalo    a black sun's corona behind it, turning
#   BossEyeShards  a ring of black glass circling it
# All centred on the middle of the eye, at the size the game shows them (1 unit = 1 m), front = -Y, up = Z.
# The lids are bands round the X axis: they open by turning about it (BossEye.cs), the shell covers the rest of that turn.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_boss_eye.py
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmesh
from mathutils import Vector
from bkit import TAU, Builder, export, mat, torus, tube

random.seed(100)
R = 2.2  # the eyeball
LID_IN, LID_MID, LID_OUT = R + 0.26, R + 0.38, R + 0.5  # the lids ride over the eye and what is laid on it
SHELL = R + 0.72  # the socket's plates start here, over the lids
LID_TOP, LID_BOTTOM, SHELL_FROM = 112.0, 100.0, 100.0  # degrees round the X axis from the front: top lid, bottom lid, shell
SIDE = 0.82  # the socket also covers the eye's corners (|x| past this much of its radius)
D = math.radians

obsidian = mat("EyeObsidian", (0.08, 0.06, 0.1), force=True)
obsidian_hi = mat("EyeObsidianHi", (0.2, 0.15, 0.25), force=True)
crack = mat("EyeCrack", (1.0, 0.12, 0.12), (1.0, 0.06, 0.08), force=True)
deep = mat("EyeDeep", (0.03, 0.02, 0.04), force=True)  # between the plates, where no crack glows
sclera = mat("EyeSclera", (0.84, 0.79, 0.7), force=True)  # bone, not white: white blooms in the game's light
vein = mat("EyeVein", (0.66, 0.08, 0.1), force=True)
iris_rim = mat("EyeIrisRim", (0.3, 0.01, 0.05), force=True)
iris = mat("EyeIris", (1.0, 0.16, 0.1), (1.0, 0.1, 0.06), force=True)
iris_core = mat("EyeIrisCore", (1.0, 0.62, 0.2), (1.0, 0.55, 0.15), force=True)
pupil = mat("EyePupil", (0.02, 0.01, 0.02), force=True)
glint = mat("EyeGlint", (1.0, 0.95, 0.9), (1.0, 0.9, 0.85), force=True)


def around_x(theta, phi, r=1.0):
    # theta: degrees round the X axis from the front (-Y) up over the top (+Z); phi: degrees toward the sides (+-X).
    t, p = D(theta), D(phi)
    return Vector((math.sin(p), -math.cos(p) * math.cos(t), math.cos(p) * math.sin(t))) * r


def theta_of(d):
    return math.degrees(math.atan2(d.z, -d.y))


# ---------- the lids ----------

def plates(b, cells, r_base, r_top, shrink=(0.84, 0.92), hi=0.3):
    # Plates of black glass over a glowing core: each cell (corner directions) a slab a little smaller than it (the gaps
    # glow), its top narrower and raised by r_top (a number, or a pick per plate).
    for ds in cells:
        c = sum(ds, Vector()).normalized()
        k = random.uniform(*shrink)
        top = r_top() if callable(r_top) else r_top
        lo = [b.bm.verts.new((c + (d - c) * k).normalized() * r_base) for d in ds]
        up = [b.bm.verts.new((c + (d - c) * k * 0.76).normalized() * top) for d in ds]
        m = obsidian_hi if random.random() < hi else obsidian
        b.bm.faces.new(up).material_index = b.mats.index(m)
        b.bm.faces.new(lo[::-1]).material_index = b.mats.index(obsidian)
        for i in range(len(ds)):
            j = (i + 1) % len(ds)
            b.bm.faces.new((lo[i], lo[j], up[j], up[i])).material_index = b.mats.index(obsidian)


def lid(name, t0, t1):
    # A band from t0 (the edge that closes) to t1, corner to corner: plates of black glass on a dark core whose closing
    # edge is bare and glows, so shut lids meet in one red crack.
    b = Builder(name, [obsidian, obsidian_hi, crack, deep])
    sgn = 1 if t1 > t0 else -1
    ts = [t0] + [t0 + sgn * 4.0 + (t1 - t0 - sgn * 4.0) * k / 8 for k in range(9)]
    ps = [-84 + 168 * k / 12 for k in range(13)]
    out = [[b.bm.verts.new(around_x(t, p, LID_MID)) for p in ps] for t in ts]
    inn = [[b.bm.verts.new(around_x(t, p, LID_IN)) for p in ps] for t in ts]
    face = lambda vs, m: setattr(b.bm.faces.new(vs), "material_index", b.mats.index(m))
    for i in range(len(ts) - 1):
        for j in range(len(ps) - 1):
            face((out[i][j], out[i][j + 1], out[i + 1][j + 1], out[i + 1][j]), crack if i == 0 else deep)
            face((inn[i][j], inn[i + 1][j], inn[i + 1][j + 1], inn[i][j + 1]), obsidian)
    for j in range(len(ps) - 1):
        face((out[0][j], inn[0][j], inn[0][j + 1], out[0][j + 1]), crack)
        face((out[-1][j], out[-1][j + 1], inn[-1][j + 1], inn[-1][j]), obsidian)
    for i in range(len(ts) - 1):
        for j in (0, len(ps) - 1):
            face((out[i][j], out[i + 1][j], inn[i + 1][j], inn[i][j]), crack if i == 0 else obsidian)
    # the plates: a few big ones on a grid whose inner corners are pushed about, so no two are alike
    rows = [t0 + sgn * 4.0 + (t1 - t0 - sgn * 4.0) * k / 4 for k in range(5)]
    cols = [-84 + 168 * k / 7 for k in range(8)]
    dt, dp = (rows[1] - rows[0]) * 0.3, (cols[1] - cols[0]) * 0.3
    node = [[around_x(t + (random.uniform(-dt, dt) if 0 < i < 4 else 0), p + (random.uniform(-dp, dp) if 0 < j < 7 else 0))
             for j, p in enumerate(cols)] for i, t in enumerate(rows)]
    cells = [[node[i][j], node[i][j + 1], node[i + 1][j + 1], node[i + 1][j]] for i in range(4) for j in range(7)]
    plates(b, cells, LID_MID - 0.01, lambda: LID_OUT + random.uniform(-0.04, 0.04))
    return b.finish()


# ---------- the socket ----------

def shell():
    b = Builder("BossEyeShell", [obsidian, obsidian_hi, crack, deep])
    keep = b.bm.faces.layers.int.new("keep")
    # a few long cracks: arcs of great circles round the socket, the plates along them parted over the glow
    arcs = [(Vector((random.gauss(0, 1), random.gauss(0, 1), random.gauss(0, 1))).normalized(),
             Vector((random.gauss(0, 1), random.gauss(0, 1), random.gauss(0, 1))).normalized()) for _ in range(4)]
    on_crack = lambda c: any(abs(c.dot(n)) < 0.07 and c.dot(a) > -0.3 for n, a in arcs)
    ico = bmesh.new()
    bmesh.ops.create_icosphere(ico, subdivisions=3, radius=1.0)
    cells = []
    for f in ico.faces:
        c = f.calc_center_median().normalized()
        if abs(theta_of(c)) < SHELL_FROM and abs(c.x) < SIDE:
            continue
        vs = [v.co.normalized() for v in f.verts]
        # what shows between the plates (a crack's glow, or dark), wound to face out and kept as it is (a lone face has no
        # inside to turn it by)
        glow = [b.bm.verts.new(v * (SHELL - 0.05)) for v in vs]
        if (glow[1].co - glow[0].co).cross(glow[2].co - glow[0].co).dot(c) < 0:
            glow.reverse()
        g = b.bm.faces.new(glow)
        g.material_index = b.mats.index(crack if on_crack(c) else deep)
        g[keep] = 1
        cells.append(vs)
    ico.free()
    plates(b, cells, SHELL, lambda: SHELL + random.uniform(0.18, 0.42), shrink=(0.76, 0.86), hi=0.35)
    return b.finish()


# ---------- the eyeball ----------

def on_eye(x, z, r):
    # the point of a sphere of radius r in front of (x, z): plates on the eye follow its curve
    return Vector((x, -math.sqrt(max(0.0, r * r - x * x - z * z)), z))


def plate(b, outline, r_top, m, rings=4, depth=0.06):
    # A thin piece laid on the eye: outline (x, z) points, filled from the middle in `rings` steps so it hugs the curve.
    n = len(outline)
    top = [[b.bm.verts.new(on_eye(0, 0, r_top))]] + [[b.bm.verts.new(on_eye(x * k / rings, z * k / rings, r_top)) for x, z in outline] for k in range(1, rings + 1)]
    bot = [b.bm.verts.new(on_eye(x, z, r_top - depth)) for x, z in outline]
    mi = b.mats.index(m)
    for k in range(rings):
        for i in range(n):
            j = (i + 1) % n
            vs = (top[0][0], top[1][i], top[1][j]) if k == 0 else (top[k][i], top[k + 1][i], top[k + 1][j], top[k][j])
            b.bm.faces.new(vs).material_index = mi
    c = b.bm.verts.new(on_eye(0, 0, r_top - depth))
    for i in range(n):
        j = (i + 1) % n
        b.bm.faces.new((c, bot[j], bot[i])).material_index = mi
        b.bm.faces.new((top[-1][i], bot[i], bot[j], top[-1][j])).material_index = mi


def circle(r, n=24, cx=0.0, cz=0.0):
    return [(cx + math.cos(TAU * i / n) * r, cz + math.sin(TAU * i / n) * r) for i in range(n)]


def eyeball():
    b = Builder("BossEyeBall", [sclera, vein, iris_rim, iris, iris_core, pupil, glint])
    b.ico(R, (0, 0, 0), sclera, subdiv=3)
    plate(b, circle(1.08), R + 0.05, iris_rim)
    plate(b, circle(0.94), R + 0.09, iris)
    for k in range(14):  # the iris's streaks
        a = TAU * k / 14
        w = 0.035
        p0, p1 = (0.52, 0.86)
        plate(b, [(math.cos(a - w) * p0, math.sin(a - w) * p0), (math.cos(a) * p1, math.sin(a) * p1), (math.cos(a + w) * p0, math.sin(a + w) * p0)],
              R + 0.12, iris_rim, rings=1)
    plate(b, circle(0.5), R + 0.14, iris_core)
    slit = [(0.19 * math.sin(t) * abs(math.sin(t)) ** 0.35, 0.78 * math.cos(t)) for t in (TAU * i / 24 for i in range(24))]
    plate(b, slit, R + 0.17, pupil, rings=3)
    plate(b, circle(0.13, 10, -0.42, 0.4), R + 0.2, glint, rings=1)
    for k in range(10):  # veins from the back of the eye toward the iris, a fork each
        a = TAU * (k + random.uniform(-0.25, 0.25)) / 10
        pts, rs = [], []
        for i in range(9):
            t = 34 + (100 - 34) * i / 8  # degrees from the front
            wob = math.radians(random.uniform(-6, 6))
            d = Vector((math.cos(a + wob) * math.sin(D(t)), -math.cos(D(t)), math.sin(a + wob) * math.sin(D(t))))
            pts.append(d * (R + 0.01))
            rs.append(0.07 * (1 - i / 10) if i else 0.0)
        tube(b, pts, rs, vein, segs=4)
        f = pts[3]
        side = Vector((math.cos(a + 0.6), 0, math.sin(a + 0.6)))
        tube(b, [f, (f + side * 0.35 + Vector((0, 0.25, 0))).normalized() * (R + 0.01), (f + side * 0.6 + Vector((0, 0.7, 0))).normalized() * (R + 0.01)], [0.045, 0.03, 0.0], vein, segs=4)
    return b.finish()


# ---------- the corona ----------

def halo():
    # A black sun's corona behind the eye: a glowing ring with blades of black glass fanned round it (it turns, BossEye.cs).
    b = Builder("BossEyeHalo", [obsidian, obsidian_hi, crack])
    y = 1.3
    torus(b, (0, y, 0), (0, 1, 0), 3.85, 0.15, crack, segs=40, tsegs=6)
    for k in range(12):
        a = TAU * (k + random.uniform(-0.12, 0.12)) / 12
        d, side = Vector((math.cos(a), 0, math.sin(a))), Vector((-math.sin(a), 0, math.cos(a)))
        length = random.uniform(4.5, 6.1)
        p0 = d * 3.55 + Vector((0, y + 0.1, 0))
        p1 = d * length + side * random.uniform(-0.5, 0.5) + Vector((0, y + 0.7, 0))
        b.beam(tuple(p0), tuple(p1), 0.24 + 0.05 * (length - 4.5), obsidian if k % 3 else obsidian_hi, segs=4, r2=0.0)
    return b.finish()


# ---------- the ring of shards ----------

def shards():
    # Flat round the eye: the game tilts the ring (its front up, so from the island it passes over the eye, not across it)
    # and turns it in its own plane (BossEye.cs).
    b = Builder("BossEyeShards", [obsidian, obsidian_hi, crack])
    n = 8
    for k in range(n):
        a = TAU * (k + random.uniform(-0.15, 0.15)) / n
        p = Vector((math.cos(a) * 7.2, math.sin(a) * 7.2, random.uniform(-0.4, 0.4)))
        h, r = random.uniform(1.1, 1.8), random.uniform(0.3, 0.45)
        up = Vector((random.uniform(-0.35, 0.35), random.uniform(-0.35, 0.35), 1)).normalized()
        m = crack if k % 4 == 1 else obsidian_hi if k % 2 else obsidian
        b.beam(tuple(p), tuple(p + up * h * 0.62), r, m, segs=5, r2=0.0)
        b.beam(tuple(p), tuple(p - up * h * 0.38), r, m, segs=5, r2=0.0)
    return b.finish()


export([shell(), eyeball(), lid("BossEyeLidTop", 0.0, LID_TOP), lid("BossEyeLidBottom", 0.0, -LID_BOTTOM), halo(), shards()])
