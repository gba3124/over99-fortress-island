# The fortress island: terrain, cliffs, the three beaches (a shipwreck cove, tide pools, an old gate), the harbour and the
# lighthouse plaza, built into fortress_models.blend as "FortressIsland" and exported as OBJ; plus where the game's own
# plants go (grass, beach plants, ferns, small pines: drawn by the mod with the game's meshes) and the tide pools, in
# Models/FortressIsland.decor.txt (game coordinates, island-local).
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_island.py [-- preview]
# Layout constants shared with FortressIsland.cs (keep in step): the plaza at the middle (y 1.2), the ready circle south of
# the lighthouse, the dock running south (-z) from z -24 to -41, the village either side of the dock road (the
# quartermaster's shed west, the fish stall and the smithy east; the respawn spot on the road), beaches (enemy landings)
# at 90, 210 and 330 degrees, the build area out to 27 m, the waterline about r 28.5.
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmesh
import bpy
from mathutils import Matrix, Vector, noise
from bkit import TAU, Builder, M, export, loft, mat, torus, tube
import vkit
from vkit import Frame

PREVIEW = "preview" in sys.argv
OUT_DECOR = os.path.join(os.path.dirname(bpy.data.filepath), "FortressIsland.decor.txt")
rng = random.Random(7)

# ---------- materials ----------
sand, grass, stone, rock, wood, dark = (M(n) for n in ("Sand", "Grass", "Stone", "Rock", "Wood", "DarkWood"))
# Muted, closer to the game's own islands (its grass and dirt paths are darker and browner than these were).
sand = mat("Sand", (0.76, 0.66, 0.47), force=True)
grass = mat("Grass", (0.34, 0.5, 0.24), force=True)
iron, rope, cloth, roofred, moss = (M(n) for n in ("Iron", "Rope", "Cloth", "RoofRed", "Moss"))
path = mat("IslPath", (0.64, 0.49, 0.32), force=True)
wet = mat("IslWetSand", (0.6, 0.52, 0.38), force=True)
grass2 = mat("IslGrassDark", (0.28, 0.43, 0.2), force=True)
tile2 = mat("IslTileDark", (0.55, 0.55, 0.58), force=True)
pool = mat("IslPool", (0.25, 0.62, 0.78), (0.05, 0.2, 0.3), force=True)
star = mat("IslStarfish", (1.0, 0.5, 0.3), force=True)
shell = mat("IslShell", (1.0, 0.86, 0.78), force=True)
soil = mat("IslSoil", (0.42, 0.3, 0.2), force=True)
flowerA = mat("IslFlowerA", (1.0, 0.82, 0.25), force=True)
flowerB = mat("IslFlowerB", (0.95, 0.42, 0.55), force=True)
glow = mat("IslLampGlow", (1.0, 0.85, 0.5), (1.0, 0.75, 0.3), force=True)
sail = mat("IslSail", (0.92, 0.88, 0.78), force=True)
buoyR = mat("IslBuoyRed", (0.9, 0.2, 0.15), force=True)
grout = mat("IslGrout", (0.3, 0.27, 0.23), force=True)  # the plaza: mortar, warm flagstones, a darker kerb
flagA = mat("IslFlagA", (0.66, 0.61, 0.53), force=True)
flagB = mat("IslFlagB", (0.58, 0.55, 0.5), force=True)
kerb = mat("IslKerb", (0.42, 0.39, 0.35), force=True)
white = M("White")
MATS = [sand, grass, stone, rock, wood, dark, iron, rope, cloth, roofred, moss, path, wet, grass2, tile2, pool, star, shell, soil,
        flowerA, flowerB, glow, sail, buoyR, white, grout, flagA, flagB, kerb] + vkit.KIT


# ---------- coordinates: everything below is in game coordinates (x east, y up, z north); Blender is (-x, -z, y) ----------
def G(x, y, z):
    return Vector((-x, -z, y))


GAPS = [90.0, 210.0, 330.0]
DOCK = 270.0
PLAZA_Y = 1.2


def ang_diff(a, b):
    return abs((a - b + 180.0) % 360.0 - 180.0)


def lane_angle(gap, r):  # the paths wind a little on their way in
    return gap + 7.0 * math.sin(r * 0.23 + gap)


def lane_dist(x, z, r, gap):  # sideways distance from a lane's centre line
    a = math.radians(lane_angle(gap, r))
    return abs(-x * math.sin(a) + z * math.cos(a)) if math.cos(math.atan2(z, x) - a) > 0 else 99.0


def near_lane(x, z, r, width=1.7):
    return any(lane_dist(x, z, r, g) < width for g in GAPS + [DOCK])


def gap_open(theta, r):  # inside a beach gap (no cliffs), widening a little toward the sea
    return min(ang_diff(theta, g) for g in GAPS) < 20.0 + max(0.0, r - 20.0) * 0.4


# ---------- the terrain ----------
TERRACES = [(150.0, 13.0), (30.0, 13.0)]  # raised platforms between the lanes (high ground for turrets); the south is the village
TERRACE_R, TERRACE_H = 3.4, 1.0


def on_terrace(x, z, pad=0.0):
    return any(math.hypot(x - r * math.cos(math.radians(a)), z - r * math.sin(math.radians(a))) < TERRACE_R + pad for a, r in TERRACES)


VILLAGE = [(-8.0, -17.0, 3.7), (5.5, -16.8, 2.8), (6.2, -10.8, 3.2), (0.5, -18.5, 2.0)]  # vault (a walk-in storehouse), fish stall, smithy, respawn
# Footpaths from the dock road to each station's front: (x0, z0, x1, z1).
VILLAGE_PATHS = [(-1.0, -17.0, -6.0, -17.0), (1.0, -16.8, 3.8, -16.8), (1.0, -10.8, 4.0, -10.8)]


def near_village_path(x, z, width=0.85):
    for x0, z0, x1, z1 in VILLAGE_PATHS:
        dx, dz = x1 - x0, z1 - z0
        t = max(0.0, min(1.0, ((x - x0) * dx + (z - z0) * dz) / (dx * dx + dz * dz)))
        if math.hypot(x - (x0 + dx * t), z - (z0 + dz * t)) < width:
            return True
    return False


def village_flat(x, z):
    k = 0.0
    for vx, vz, vr in VILLAGE:
        d = math.hypot(x - vx, z - vz)
        k = max(k, max(0.0, min(1.0, (vr + 1.5 - d) / 1.5)))
    return k


def height(x, z):
    r = math.hypot(x, z)
    if r < 6.0:
        return PLAZA_Y
    n = noise.noise(Vector((x * 0.09, z * 0.09, 0.3))) * 0.18 + noise.noise(Vector((x * 0.3, z * 0.3, 2.1))) * 0.04
    if r < 19.5:
        base = PLAZA_Y - 0.17 * ((r - 6.0) / 13.5) ** 1.3
        lane = -0.06 if near_lane(x, z, r) else 0.0
        flat = village_flat(x, z)
        return base + n * min(1.0, (r - 6.0) / 3.0) * (1.0 - flat) + lane
    # The beach: gentle at the top, the waterline (-1.1) at r 28.5, then down to the sea bed.
    if r < 23.0:
        y = 1.03 - 0.45 * ((r - 19.5) / 3.5) ** 1.2
    elif r < 28.5:
        y = 0.58 - 1.68 * ((r - 23.0) / 5.5)
    else:
        y = -1.1 - 2.9 * min(1.0, (r - 28.5) / 9.0) ** 0.8
    return max(-4.0, y + n * 0.4 * max(0.0, 1.0 - (r - 19.5) / 6.0))


def ground_mat(x, z, y):
    r = math.hypot(x, z)
    if r < 6.0:
        return grout  # under the flagstones (the plaza paving below)
    if r < 19.5:
        if near_lane(x, z, r, 1.5) or near_village_path(x, z):
            return path
        return grass2 if noise.noise(Vector((x * 0.15, z * 0.15, 5.0))) > 0.25 else grass
    if near_lane(x, z, r, 1.4 + (r - 19.5) * 0.05) and r < 26:
        return path
    return wet if -1.6 < y < -0.6 else sand


island = Builder("FortressIsland", MATS)
bm = island.bm
KEEP = bm.faces.layers.int.new("keep")  # the ground sheet: its faces are wound to face up, and stay that way
RINGS = [0.0, 0.8, 1.6, 2.5, 3.5, 4.5, 5.5, 6.0] + [6.0 + 0.9 * i for i in range(1, 16)] + [19.5 + 1.0 * i for i in range(1, 12)] + [31.0 + 2.0 * i for i in range(1, 8)]
SEG = 144
grid = []
for r in RINGS:
    row = []
    for i in range(SEG):
        a = i * TAU / SEG
        x, z = r * math.cos(a), r * math.sin(a)
        y = height(x, z)
        row.append(bm.verts.new(G(x, y, z)))
        if r == 0.0:
            break
    grid.append(row)
for j in range(1, len(grid)):
    A, B = grid[j - 1], grid[j]
    for i in range(SEG):
        k = (i + 1) % SEG
        if len(A) == 1:
            vs = (A[0], B[i], B[k])
        else:
            vs = (A[i], B[i], B[k], A[k])
        f = bm.faces.new(vs)  # inner, outer, outer next, inner next: counter-clockwise from above, so it faces up
        f[KEEP] = 1
        c = sum((v.co for v in vs), Vector()) / len(vs)
        gx, gz, gy = -c.x, -c.y, c.z
        f.material_index = MATS.index(ground_mat(gx, gz, gy))

# ---------- rocks: cliffs between the beaches, layered, grassy tops; sea stacks offshore ----------
def rock_blob(cx, cz, w, h, d, yaw, base=None):
    y0 = height(cx, cz) - 0.4 if base is None else base
    res = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0, matrix=Matrix())
    verts = res["verts"]
    seed = rng.random() * 100.0
    for v in verts:
        p = v.co.copy()
        p *= 1.0 + 0.22 * noise.noise(p * 1.4 + Vector((seed, seed, seed)))  # lumpy
        p.z = max(p.z, -0.6)                                                 # flat-ish underside
        if p.z > 0.55:
            p.z = 0.55 + (p.z - 0.55) * 0.45                                 # flattened top (grass grows on it)
        loc = Matrix.Rotation(yaw, 4, "Z") @ Vector((p.x * w, p.y * d, p.z * h))
        v.co = G(cx, y0, cz) + loc + Vector((0, 0, h * 0.6))
    faces = {f for v in verts for f in v.link_faces}
    for f in faces:
        f.material_index = MATS.index(moss if f.normal.z > 0.82 and h > 1.2 else (rock if rng.random() < 0.75 else stone))
    return verts


TOPS = []  # rock tops where a pine can grow: (x, y, z)


def pillar(cx, cz, h):
    # Stacked slabs, each a little off and turned: sedimentary rock worn by the sea.
    y = height(cx, cz) - 0.3
    n = max(3, int(h / 0.7))
    for i in range(n):
        s = 2.1 - 0.9 * (i / n) + rng.uniform(-0.15, 0.15)
        t = h / n
        island.box((s, s * rng.uniform(0.75, 0.95), t * 0.98), G(cx + rng.uniform(-0.2, 0.2), y + t * (i + 0.5), cz + rng.uniform(-0.2, 0.2)),
                   rock if i % 2 else stone, rot=(rng.uniform(-0.04, 0.04), rng.uniform(-0.04, 0.04), rng.uniform(0, TAU)))
    island.box((s * 0.9, s * 0.8, 0.12), G(cx, y + h + 0.05, cz), moss)
for gi in range(len(GAPS)):
    a0 = GAPS[gi] + 24.0
    a1 = GAPS[(gi + 1) % len(GAPS)] - 24.0 + (360.0 if GAPS[(gi + 1) % len(GAPS)] < GAPS[gi] else 0.0)
    span = a1 - a0
    n = int(span / 5.5)
    for k in range(n + 1):
        a = a0 + span * k / n
        if ang_diff(a % 360.0, DOCK) < 30.0:
            continue  # the harbour side stays open: the dock, the village
        edge = min(k, n - k) / max(1, n / 2)  # 0 at the ends of a cliff, 1 in its middle
        for row, (rr, hh) in enumerate(((21.0, 1.4 + 2.0 * edge), (24.2, 2.4 + 3.8 * edge))):
            rad = rr + rng.uniform(-0.6, 0.6)
            x, z = rad * math.cos(math.radians(a + rng.uniform(-1.5, 1.5))), rad * math.sin(math.radians(a + rng.uniform(-1.5, 1.5)))
            h = max(0.9, hh * rng.uniform(0.75, 1.2))
            if row == 1 and k % 4 == 2:
                pillar(x, z, h * 1.25)  # a layered sea-worn pillar
                TOPS.append((x, height(x, z) + h * 1.25, z))
                continue
            w = rng.uniform(1.4, 2.3) * (1.0 if row == 0 else 1.2)
            if any(math.hypot(x - vx, z - vz) < vr + w * 0.5 for vx, vz, vr in VILLAGE):
                continue  # not into a village building (the vault you walk into reaches the cliff line)
            rock_blob(x, z, w, h, w * rng.uniform(0.7, 1.1), rng.uniform(0, TAU))
            if row == 1 and h > 3.0 and rng.random() < 0.5:
                TOPS.append((x, height(x, z) - 0.4 + h * 0.6 + 0.55 * h, z))

STACKS = [(45.0, 120.0), (52.0, 160.0), (48.0, 250.0), (55.0, 295.0), (47.0, 20.0), (58.0, 60.0)]
for r, a in STACKS:
    x, z = r * math.cos(math.radians(a)), r * math.sin(math.radians(a))
    h = rng.uniform(4.0, 7.5)
    rock_blob(x, z, rng.uniform(2.2, 3.4), h, rng.uniform(2.0, 3.0), rng.uniform(0, TAU), base=-4.5)
    TOPS.append((x, -4.5 + h * 0.6 + 0.55 * h, z))

# A boulder in the cleft between the two cliff rows at 30 degrees (game 19.3, 11.2): you could drop in from the rocks and
# not climb back out (Owen's screenshot). Its own random numbers, so nothing else on the island moves.
rng, _rng = random.Random(1907), rng
rock_blob(19.3, 10.4, 1.6, 1.8, 1.9, math.radians(30))
rng = _rng

# ---------- the north beach: a shipwreck lying on its side, splitting the lane ----------
def shipwreck(cx, cz, yaw_deg):
    b = Builder("Wreck", MATS)
    L, W, H = 7.5, 2.7, 1.9
    rings = []
    for i in range(9):
        u = i / 8.0
        y = -L / 2 + L * u
        width = W * (math.sin(math.pi * (0.15 + 0.85 * u)) ** 0.6) * (0.25 if i == 8 else 1.0)
        ring = []
        for k in range(9):  # an open U: from one gunwale down round the keel to the other
            t = math.pi * k / 8.0
            ring.append((width / 2 * math.cos(t), y, -H * math.sin(t) * (0.6 + 0.4 * math.sin(math.pi * u))))
        rings.append(ring)
    hull = [[Vector(p) for p in ring] for ring in rings]
    vs = [[b.bm.verts.new(p) for p in ring] for ring in hull]
    kp = b.bm.faces.layers.int.new("keep")
    for i in range(len(vs) - 1):
        for k in range(len(vs[i]) - 1):
            quad = [vs[i][k], vs[i][k + 1], vs[i + 1][k + 1], vs[i + 1][k]]
            for side in (quad, quad[::-1]):  # an open shell: planks inside and out
                f = b.bm.faces.new([b.bm.verts.new(v.co) for v in side])
                f.material_index = MATS.index(dark if k % 2 else wood)
                f[kp] = 1
    for i in (1, 3, 5, 7):  # ribs over the open side
        b.beam(hull[i][0] + Vector((0, 0, 0.05)), hull[i][-1] + Vector((0, 0, 0.05)), 0.07, dark)
    b.box((0.25, 1.6, 0.18), (0, -0.4, -0.2), wood)  # a thwart
    b.beam((0, 0.6, -0.4), (0.15, 0.7, 2.3), 0.12, dark, segs=6)  # the broken mast
    b.beam((-1.2, 0.72, 1.9), (1.3, 0.68, 2.0), 0.06, dark, segs=4)
    b.poly([(-1.1, 0.75, 1.85), (1.2, 0.71, 1.95), (0.9, 0.95, 0.4), (-0.5, 0.9, 0.7)], sail)  # what's left of the sail
    ob = b.finish()
    ob.matrix_world = Matrix.Translation(G(cx, height(cx, cz) + 0.9, cz)) @ Matrix.Rotation(math.radians(yaw_deg), 4, "Z") @ Matrix.Rotation(math.radians(28), 4, "Y") @ Matrix.Scale(1.45, 4)
    return ob


EXTRA = []
gn = math.radians(GAPS[0])
lx, lz = 25.0 * math.cos(gn), 25.0 * math.sin(gn)
side = (-math.sin(gn), math.cos(gn))
EXTRA.append(shipwreck(lx + side[0] * 3.6, lz + side[1] * 3.6, -15.0))


def barrel(x, z, tip=0.0):
    vkit.barrel(island, Frame(G(x, height(x, z), z)), 0.3, 0.8)


def crate(x, z, s=0.8, yaw=0.0):
    vkit.crate(island, Frame(G(x, height(x, z), z)), s, (0, 0, 0), math.degrees(yaw))


for dx, dz in ((1.8, 2.0), (-2.4, 1.2), (3.0, -1.5)):  # planks and cargo washed up round the wreck
    px, pz = lx + side[0] * 3.6 + dx, lz + side[1] * 3.6 + dz
    island.box((1.8, 0.25, 0.08), G(px, height(px, pz) + 0.05, pz), wood, rot=(0, 0, rng.uniform(0, 3.1)))
barrel(lx + side[0] * 6.0, lz + side[1] * 6.0 - 1.5)
crate(lx + side[0] * 1.2 - 1.5, lz + side[1] * 1.2 + 2.0, 0.7, 0.4)

# ---------- the south-west beach: tide pools on rock ledges ----------
POOLS = []
gs = math.radians(GAPS[1])
for r, off, size in ((22.4, 0.6, 1.9), (25.4, -1.0, 2.2), (23.4, 4.6, 1.3), (26.6, 4.2, 1.2)):
    x, z = r * math.cos(gs) - math.sin(gs) * off, r * math.sin(gs) + math.cos(gs) * off
    y = height(x, z)
    # The beach falls away seaward: the pool sits in a rock ledge built up from the lowest sand round it (a flat disc on the
    # slope floated on its low side, and so did what stood round it).
    low = min(height(x + math.cos(k * TAU / 12) * (size + 0.35), z + math.sin(k * TAU / 12) * (size + 0.35)) for k in range(12))
    island.cyl(size + 0.35, y - low + 0.22, G(x, (y + low) / 2 - 0.09, z), rock, segs=16)
    island.cyl(size, 0.05, G(x, y + 0.03, z), pool, segs=16)   # the water, just above the ledge
    for k in range(9):  # a rim of low stones round it, on the ledge or on the sand where that's higher
        a = k * TAU / 9 + rng.uniform(-0.15, 0.15)
        rx, rz = x + math.cos(a) * (size + 0.15), z + math.sin(a) * (size + 0.15)
        island.ico(rng.uniform(0.18, 0.32), G(rx, max(y, height(rx, rz)) + 0.05, rz), rock if k % 3 else stone, subdiv=1)
    POOLS.append((x, y + 0.03, z, size))
    for s in range(2):  # starfish and shells round the pool, lying on the sand
        a = rng.uniform(0, TAU)
        sx, sz = x + math.cos(a) * (size + 0.5), z + math.sin(a) * (size + 0.5)
        sy = height(sx, sz) + 0.04
        if s == 0:
            for arm in range(5):
                b2 = a + arm * TAU / 5
                island.beam(G(sx, sy, sz), G(sx + math.cos(b2) * 0.22, sy, sz + math.sin(b2) * 0.22), 0.05, star, segs=4, r2=0.015)
        else:
            island.cone(0.09, 0.0, 0.12, G(sx, sy + 0.05, sz), shell, segs=6)

# ---------- the south-east beach: an old gate the lane runs through ----------
ge = math.radians(GAPS[2])
gx, gz = 21.5 * math.cos(ge), 21.5 * math.sin(ge)
tx, tz = -math.sin(ge), math.cos(ge)  # across the lane
gy = height(gx, gz)


def gate_block(x, y, z, w, d, h, m, yaw, rx=0.0, ry=0.0):
    island.box((w, d, h), G(x, y, z), m, rot=(rx, ry, yaw))


# Two weathered pillars: a plinth, seven slightly different courses, a capital, mortar lines and cracks.
for side in (-1, 1):
    px, pz = gx + tx * side * 3.4, gz + tz * side * 3.4
    gate_block(px, gy + 0.12, pz, 1.75, 1.55, 0.24, stone, ge)
    gate_block(px, gy + 0.30, pz, 1.55, 1.38, 0.16, dark, ge)
    for k in range(7):
        y = gy + 0.40 + k * 0.43
        w = 1.34 - k * 0.055 + rng.uniform(-0.04, 0.04)
        d = 1.26 - k * 0.05 + rng.uniform(-0.03, 0.03)
        gate_block(px + rng.uniform(-0.035, 0.035), y, pz + rng.uniform(-0.035, 0.035),
                   w, d, 0.40, stone if k % 3 else rock, ge + rng.uniform(-0.035, 0.035),
                   rng.uniform(-0.012, 0.012), rng.uniform(-0.012, 0.012))
        gate_block(px, y + 0.205, pz, w + 0.035, d + 0.035, 0.012, dark, ge)
    gate_block(px, gy + 3.46, pz, 1.55, 1.42, 0.18, stone, ge)
    # A few chips and a dark crack down the seaward face.
    gate_block(px + tx * side * 0.72, gy + 1.55, pz + tz * side * 0.72, 0.035, 0.10, 1.8, dark, ge)
    for k in range(3):
        gate_block(px + tx * side * 0.70, gy + 0.85 + k * 0.9, pz + tz * side * 0.70,
                   0.06, 0.12, 0.18, dark, ge)

# A proper arch: many small voussoirs following the curve, a bigger keystone, one broken gap and fallen rubble.
R = 3.4
spring = gy + 3.52
# Many small overlapping stones make a continuous curved arch. No tilt on each stone: tilting them opened visible
# gaps and made the top look like a pile of floating crates (Owen's screenshot).
n = 31
for k in range(n):
    t = math.pi * k / (n - 1)
    if k == 20:  # the broken quarter
        continue
    ax, az = gx + tx * R * math.cos(t), gz + tz * R * math.cos(t)
    ay = spring + R * math.sin(t)
    gate_block(ax, ay, az, 0.62, 0.72, 0.52, stone if k % 2 else rock, ge)
    if k % 6 == 0:
        gate_block(ax + tx * 0.02, ay + 0.02, az + tz * 0.02, 0.48, 0.58, 0.38, dark, ge)
# The keystone, slightly proud of the ring.
gate_block(gx, spring + R + 0.20, gz, 0.86, 0.82, 0.55, stone, ge)

# Cap stones / fallen blocks around the feet.
for k in range(5):
    fx, fz = gx + tx * rng.uniform(-5.5, 5.5) + math.cos(ge) * rng.uniform(1.8, 3.8), gz + tz * rng.uniform(-5.5, 5.5) + math.sin(ge) * rng.uniform(1.8, 3.8)
    gate_block(fx, height(fx, fz) + 0.22, fz, rng.uniform(0.55, 1.0), rng.uniform(0.5, 0.85), rng.uniform(0.28, 0.5),
               rock if k % 2 else stone, rng.uniform(0, TAU), rng.uniform(-0.25, 0.25), rng.uniform(-0.25, 0.25))

# Moss on the north face and a little vine over one shoulder.
for x, z, s in ((gx - tx * 3.0, gz - tz * 3.0, 0.8), (gx + tx * 2.8, gz + tz * 2.8, 0.55)):
    gate_block(x, height(x, z) + 0.08, z, s, s * 0.7, 0.10, moss, ge)
for k in range(4):
    gate_block(gx - tx * 3.4, spring + 0.35 + k * 0.45, gz - tz * 3.4, 0.10, 0.12, 0.32, moss, ge + k * 0.35,
               rng.uniform(-0.12, 0.12), rng.uniform(-0.12, 0.12))

# ---------- the terraces: stone-walled platforms between the lanes, steps toward the lighthouse ----------
for a, r in TERRACES:
    cx, cz = r * math.cos(math.radians(a)), r * math.sin(math.radians(a))
    y0 = height(cx, cz) - 0.25
    island.cyl(TERRACE_R + 0.12, 0.6, G(cx, y0 + 0.3, cz), stone, segs=10)                    # the lower course
    island.cyl(TERRACE_R, TERRACE_H + 0.25 - 0.6, G(cx, y0 + 0.6 + (TERRACE_H + 0.25 - 0.6) / 2, cz), rock, segs=10)  # the upper course
    island.cyl(TERRACE_R - 0.05, 0.06, G(cx, y0 + TERRACE_H + 0.25, cz), grass2, segs=10)      # grass on top
    for k in range(10):  # cap stones round the rim
        b = (k + 0.5) * TAU / 10
        island.box((0.5, 1.95, 0.14), G(cx + math.cos(b) * (TERRACE_R - 0.15), y0 + TERRACE_H + 0.3, cz + math.sin(b) * (TERRACE_R - 0.15)), stone, rot=(0, 0, b))  # along the rim
    inward = math.atan2(-cz, -cx)  # steps down toward the plaza
    for k in range(3):
        d = TERRACE_R + 0.25 + 0.45 * k
        sx, sz = cx + math.cos(inward) * d, cz + math.sin(inward) * d
        h = TERRACE_H * (3 - k) / 3.0
        island.box((0.45, 1.5, h + 0.25), G(sx, y0 + (h + 0.25) / 2, sz), stone if k % 2 else rock, rot=(0, 0, inward))
    TOPS.append((cx + math.cos(inward + math.pi) * 2.0, y0 + TERRACE_H + 0.28, cz + math.sin(inward + math.pi) * 2.0))  # a pine at the back

# ---------- the village: a signpost where the paths leave the dock road, lamp posts along it ----------
spx, spz = 1.4, -14.2
# The signpost: arrows to the vault (west, its red roof), the fish stall and the smithy (east). In Blender game-west is +x.
vkit.signpost(island, Frame(G(spx, height(spx, spz), spz)), [(0.0, 1.95, vkit.roofr), (180.0, 1.65, vkit.canvas), (180.0, 1.35, vkit.iron)])
for lx3, lz3 in ((-1.9, -12.0), (1.9, -20.6), (-1.9, -21.4)):  # lamp posts, their lanterns out over the road
    vkit.lamp_post(island, Frame(G(lx3, height(lx3, lz3), lz3), 180.0 if lx3 < 0 else 0.0))
for x, z in ((-4.6, -15.0), (-4.2, -19.6), (3.1, -14.4), (3.4, -12.8)):  # flower boxes by the paths
    vkit.planter(island, Frame(G(x, height(x, z), z)), w=0.9, d=0.38, h=0.32)

# ---------- the harbour: the pier, posts, lanterns, a rope rail, a net rack, cargo, buoys ----------
for k in range(18):  # planks across the pier, z -24 .. -41, with gaps
    z = -24.3 - k * 0.95
    island.box((2.6, 0.82, 0.12), G(rng.uniform(-0.04, 0.04), 0.6, z), wood if k % 3 else dark, rot=(0, 0, rng.uniform(-0.02, 0.02)))
for z in (-24.5, -28.5, -32.5, -36.5, -40.5):
    for s in (-1, 1):
        island.cyl(0.13, 5.0, G(s * 1.4, -1.9, z), dark, segs=8)  # posts down to the sea bed
        island.cyl(0.15, 0.08, G(s * 1.4, 0.75, z), iron, segs=8)
    island.beam(G(1.4, 0.95, z), G(1.4, 0.95, z - 4.0) if z > -40 else G(1.4, 0.95, z), 0.035, rope, segs=4)  # the rope rail, east side
for z in (-26.5, -38.5):  # lanterns on tall posts, hanging over the pier
    vkit.lamp_post(island, Frame(G(-1.4, 0.66, z), 180.0), h=2.7)
for k in range(5):  # buoys bobbing off the pier
    bx, bz = rng.choice((-1, 1)) * rng.uniform(3.2, 7.0), -27.0 - k * 3.2
    island.ico(0.32, G(bx, -1.05, bz), buoyR if k % 2 else white, subdiv=1)
# The net rack on the sand west of the pier: two A-frames, a pole, a sagging net with floats.
nx, nz = -4.6, -25.5
for s in (-1, 1):
    fx = nx + s * 1.6
    fy = height(fx, nz)
    island.beam(G(fx, fy, nz - 0.6), G(fx, fy + 2.0, nz), 0.06, dark)
    island.beam(G(fx, fy, nz + 0.6), G(fx, fy + 2.0, nz), 0.06, dark)
ny = height(nx, nz)
island.beam(G(nx - 1.8, ny + 2.0, nz), G(nx + 1.8, ny + 2.0, nz), 0.05, dark)
for k in range(8):  # the net: a grid of cords sagging off the pole
    u = k / 7.0
    xk = nx - 1.5 + 3.0 * u
    island.beam(G(xk, ny + 2.0, nz), G(xk, ny + 0.5 + 0.2 * math.sin(u * math.pi), nz - 0.2), 0.012, rope, segs=3)
for k in range(4):
    yk = ny + 1.9 - k * 0.42
    island.beam(G(nx - 1.5, yk, nz - 0.05 * k), G(nx + 1.5, yk, nz - 0.05 * k), 0.012, rope, segs=3)
for k in range(3):
    island.ico(0.09, G(nx - 1.0 + k, ny + 0.55, nz - 0.2), buoyR, subdiv=1)
for x, z, s, yaw in ((6.6, -20.6, 0.8, 0.3), (7.4, -20.0, 0.7, 0.9), (7.0, -20.3, 0.6, 0.1)):  # cargo behind the fish stall
    crate(x, z, s, yaw)
barrel(7.9, -21.6)
barrel(-2.6, -23.0)

# ---------- the lighthouse plaza: planters with flowers, lamps, benches ----------
for k in range(6):
    a = math.radians(30 + k * 60)
    if ang_diff(math.degrees(a), 270.0) < 25:  # not in front of the door / the ready circle
        continue
    px, pz = 5.6 * math.cos(a), 5.6 * math.sin(a)
    PF = Frame(G(px, PLAZA_Y, pz), -math.degrees(a))
    vkit.stones(island, PF, -0.75, 0.75, -0.45, 0.45, 0.0, 0.42, rows=2)  # a stone planter
    vkit.box(island, PF, (1.3, 0.7, 0.06), (0, 0, 0.42), soil)
    vkit.flowers(island, PF, -0.55, 0.55, -0.25, 0.25, 0.44, n=9)
for a in (60.0, 180.0, 300.0):  # lamp posts round the plaza, lanterns toward the lighthouse
    lx2, lz2 = 4.6 * math.cos(math.radians(a)), 4.6 * math.sin(math.radians(a))
    vkit.lamp_post(island, Frame(G(lx2, PLAZA_Y, lz2), a))
for a in (120.0, 0.0):  # benches facing the lighthouse
    bx2, bz2 = 6.6 * math.cos(math.radians(a)), 6.6 * math.sin(math.radians(a))
    vkit.bench(island, Frame(G(bx2, height(bx2, bz2), bz2), a + 90.0))

# ---------- the plaza paving: flagstones in rings round the lighthouse, mortar between, a kerb at the edge and round the
# lighthouse's foot (Owen: the big grey wedges looked odd). Its own random numbers, so the rest of the island stays put. ----------
pr = random.Random(1908)
edges = [1.15 + 0.6 * i for i in range(9)]  # 1.15 .. 5.95
gap = 0.03  # half the mortar line
for i in range(len(edges) - 1):
    r0, r1 = edges[i] + gap, edges[i + 1] - gap
    n = max(8, round(TAU * (r0 + r1) / 2 / 0.8))
    off = pr.uniform(0, TAU / n)  # joints staggered ring to ring
    for k in range(n):
        a0, a1 = off + TAU * k / n, off + TAU * (k + 1) / n
        corners = [(r0, a0 + gap / r0), (r1, a0 + gap / r1), (r1, a1 - gap / r1), (r0, a1 - gap / r0)]
        top = PLAZA_Y + pr.uniform(0.025, 0.035)
        m = kerb if i in (0, len(edges) - 2) else (rock if pr.random() < 0.06 else flagA if pr.random() < 0.65 else flagB)
        loft(island, [[G(r * math.cos(a), y, r * math.sin(a)) for r, a in corners] for y in (PLAZA_Y - 0.02, top)], m)

ob = island.finish()
for e in EXTRA:  # the wreck becomes part of the island mesh (one OBJ, one collider)
    e.select_set(True)
ob.select_set(True)
bpy.context.view_layer.objects.active = ob
bpy.ops.object.join()
ob = bpy.context.view_layer.objects.active
ob.name = ob.data.name = "FortressIsland"


# ---------- where the game's plants go ----------
def on_ground(x, z):
    return height(x, z)


decor = []
def add(kind, x, y, z, s):
    decor.append(f"{kind} {x:.2f} {y:.2f} {z:.2f} {rng.uniform(0, 360):.0f} {s:.2f}")


def clear_of_things(x, z):
    r = math.hypot(x, z)
    spots = [(vx, vz, vr + 0.8) for vx, vz, vr in VILLAGE] + [(4.5, -19.0, 1.6), (0.0, -4.5, 2.4), (-4.6, -25.5, 2.4), (-2.6, -3.4, 1.2), (1.4, -14.2, 0.8), (3.6, -6.2, 1.4)] + [(px, pz, pr + 0.7) for px, _, pz, pr in POOLS]
    return (r > 6.8 and not near_lane(x, z, r, 2.0) and not near_village_path(x, z, 1.3) and not on_terrace(x, z, 0.6)
            and all(math.hypot(x - a, z - b) > c for a, b, c in spots) and (abs(x) > 2.5 or z > -22))


for _ in range(900):  # grass tufts on the grass
    r = math.sqrt(rng.uniform(6.8 ** 2, 19.0 ** 2))
    a = rng.uniform(0, TAU)
    x, z = r * math.cos(a), r * math.sin(a)
    if clear_of_things(x, z) and len(decor) < 420:
        add("GrassIsland2", x, height(x, z), z, rng.uniform(0.8, 1.3))
for _ in range(400):  # beach grass and beach plants along the top of the sand
    r = rng.uniform(19.0, 24.5)
    a = rng.uniform(0, TAU)
    x, z = r * math.cos(a), r * math.sin(a)
    y = height(x, z)
    if y > -0.3 and not near_lane(x, z, r, 2.2) and clear_of_things(x, z):
        kind = rng.choice(("GrassIsland3", "GrassIsland3", "BeachPlant1", "BeachPlant2"))
        add(kind, x, y, z, rng.uniform(0.8, 1.2))
for _ in range(60):  # ferns at the foot of the cliffs
    gi = rng.randrange(3)
    a = rng.uniform(GAPS[gi] + 26, GAPS[gi] + 94)
    r = rng.uniform(18.8, 19.6)
    x, z = r * math.cos(math.radians(a)), r * math.sin(math.radians(a))
    if not near_lane(x, z, r, 2.2) and clear_of_things(x, z):
        add("Fern", x, height(x, z), z, rng.uniform(0.9, 1.4))
for a, r in TERRACES:  # tufts on the terraces
    cx, cz = r * math.cos(math.radians(a)), r * math.sin(math.radians(a))
    top = height(cx, cz) - 0.25 + TERRACE_H + 0.28
    for k in range(9):
        b, d = rng.uniform(0, TAU), rng.uniform(0.6, TERRACE_R - 0.5)
        add("GrassIsland2", cx + math.cos(b) * d, top, cz + math.sin(b) * d, rng.uniform(0.7, 1.0))
for x, y, z in TOPS:  # small pines on the cliff tops and sea stacks
    add("PineTreeSmall", x, y, z, rng.uniform(1.0, 1.5))
for x, y, z, r in POOLS:
    decor.append(f"Pool {x:.2f} {y:.2f} {z:.2f} 0 {r:.2f}")
with open(OUT_DECOR, "w") as fh:
    fh.write("# kind x y z yaw scale  (island-local game coordinates; Pool: a tide pool, scale = its radius)\n")
    fh.write("\n".join(decor) + "\n")
print("DECOR", len(decor), "pines", len(TOPS), "pools", len(POOLS))

if PREVIEW:
    # Look at it (nothing saved): stand-ins for the plants, then a view from above and one per beach and the harbour.
    out = sys.argv[sys.argv.index("preview") + 1] if len(sys.argv) > sys.argv.index("preview") + 1 else "/tmp"
    os.makedirs(out, exist_ok=True)
    green = mat("IslPreviewPlant", (0.25, 0.55, 0.2), force=True)
    pine = mat("IslPreviewPine", (0.15, 0.4, 0.2), force=True)
    trunk = mat("IslPreviewTrunk", (0.4, 0.27, 0.15), force=True)
    pv = Builder("PreviewPlants", [green, pine, trunk])
    for line in decor:
        k, x, y, z, yaw, sc = line.split()
        x, y, z, sc = float(x), float(y), float(z), float(sc)
        if k == "Pool":
            continue
        if k == "PineTreeSmall":
            pv.cyl(0.15 * sc, 1.0 * sc, G(x, y + 0.5 * sc, z), trunk, segs=6)
            pv.cone(0.9 * sc, 0.0, 2.6 * sc, G(x, y + 2.2 * sc, z), pine, segs=7)
        else:
            pv.cone(0.25 * sc, 0.0, 0.5 * sc, G(x, y + 0.25 * sc, z), green, segs=5)
    pv.finish()
    sc_ = bpy.context.scene
    # The stations FortressIsland.cs puts in the village (game yaw t is Blender -t about Z).
    shown = ["FortressIsland", "PreviewPlants"]
    for name, x, z, yaw in (("MarketStall", 5.5, -16.8, -90.0), ("Smithy", 6.2, -10.8, -90.0), ("Vault", -8.0, -17.0, 90.0)):
        src = bpy.data.objects.get(name)
        if not src:
            continue
        cp = bpy.data.objects.new("Preview" + name, src.data)
        sc_.collection.objects.link(cp)
        cp.location = G(x, height(x, z), z)
        cp.rotation_euler = (0.0, 0.0, -math.radians(yaw))
        shown.append(cp.name)
    sc_.render.engine = "BLENDER_WORKBENCH"
    sh = sc_.display.shading
    sh.light, sh.studio_light, sh.color_type = "STUDIO", "paint.sl", "MATERIAL"
    sh.show_object_outline = True
    sh.object_outline_color = (0.1, 0.1, 0.12)
    sh.show_cavity = True
    sc_.view_settings.view_transform = "Standard"
    sc_.world = sc_.world or bpy.data.worlds.new("W")
    sc_.world.color = (0.55, 0.75, 0.95)
    for m in bpy.data.materials:
        bsdf = m.node_tree and next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf:
            m.diffuse_color = (*bsdf.inputs["Base Color"].default_value[:3], 1.0)
    for o in sc_.objects:
        o.hide_render = o.name not in shown
    sea = bpy.data.meshes.new("PreviewSea")
    bms = bmesh.new()
    bmesh.ops.create_grid(bms, x_segments=1, y_segments=1, size=90.0)
    bms.to_mesh(sea)
    seao = bpy.data.objects.new("PreviewSea", sea)
    seao.location = (0, 0, -1.1)
    seam = mat("IslPreviewSea", (0.2, 0.45, 0.75), force=True)
    seam.diffuse_color = (0.2, 0.45, 0.75, 1.0)
    sea.materials.append(seam)
    sc_.collection.objects.link(seao)
    camd = bpy.data.cameras.new("PreviewCam")
    cam = bpy.data.objects.new("PreviewCam", camd)
    sc_.collection.objects.link(cam)
    sc_.camera = cam
    def shot(name, eye, look, lens=24.0, ortho=0.0):
        camd.type = "ORTHO" if ortho else "PERSP"
        camd.ortho_scale = ortho or 1.0
        camd.lens = lens
        camd.clip_end = 500
        cam.location = eye
        cam.rotation_euler = (look - eye).to_track_quat("-Z", "Y").to_euler()
        sc_.render.resolution_x, sc_.render.resolution_y = 1400, 900
        sc_.render.filepath = os.path.join(out, name + ".png")
        bpy.ops.render.render(write_still=True)
    shot("island_top", G(0, 120, -0.01), G(0, 0, 0), ortho=95.0)
    shot("island_overview", G(-42, 38, -52), G(0, 0, 0), lens=28)
    shot("island_village", G(0.0, 9.0, -30.0), G(0.0, 0.0, -13.0), lens=26)
    shot("island_plaza", G(0.0, 6.5, -11.0), G(0.0, 1.2, 0.0), lens=24)
    shot("island_cleft", G(13.0, 8.0, 3.0), G(19.3, 1.5, 10.4), lens=24)
    for i, gap in enumerate(GAPS + [DOCK]):
        a = math.radians(gap)
        eye = G(math.cos(a) * 34, 7.0, math.sin(a) * 34)
        look = G(math.cos(a) * 14, 1.5, math.sin(a) * 14)
        shot(f"island_beach{i}", eye, look, lens=20)
    print("PREVIEW", out)
else:
    export([ob])
