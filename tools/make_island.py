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
from bkit import TAU, Builder, M, export, fin, loft, mat, torus, tube
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
# The lanes in layers: a darker trodden middle, the path, a dusty edge that wanders into the grass (on the sand the edge is
# the wet-sand tone). The old gate's threshold and the flagstones lost along its lane. Cords (nets) that nothing collides with.
pathwet = mat("IslPathWet", (0.52, 0.39, 0.26), force=True)
pathedge = mat("IslPathEdge", (0.5, 0.49, 0.29), force=True)
gatestone = mat("IslGateStone", (0.5, 0.49, 0.46), force=True)  # ground faces only (tests/Headless checks ground materials face up)
cord = mat("IslCord", (0.66, 0.56, 0.4), force=True)
sanddrift = mat("IslSandDrift", (0.76, 0.66, 0.47), force=True)  # the sand and wet-sand tones again, for mounds (closed shapes:
wetdrift = mat("IslWetDrift", (0.6, 0.52, 0.38), force=True)     # tests/Headless wants the ground materials on the open ground sheet only)
# The wreck's own materials (its own OBJ and collider, see shipwreck()).
wwood = mat("WreckWood", (0.47, 0.33, 0.21), force=True)
wdark = mat("WreckDark", (0.27, 0.17, 0.11), force=True)
wiron = mat("WreckIron", (0.26, 0.23, 0.21), force=True)
wsail = mat("WreckSail", (0.88, 0.84, 0.74), force=True)
white = M("White")
MATS = [sand, grass, stone, rock, wood, dark, iron, rope, cloth, roofred, moss, path, wet, grass2, tile2, pool, star, shell, soil,
        flowerA, flowerB, glow, sail, buoyR, white, grout, flagA, flagB, kerb, pathwet, pathedge, gatestone, cord, sanddrift, wetdrift,
        wwood, wdark, wiron, wsail] + vkit.KIT


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


def lane_d(x, z, r):  # sideways distance to the nearest lane
    return min(lane_dist(x, z, r, g) for g in GAPS + [DOCK])


# The old gate on the south-east beach: where it stands, and its frame (along the lane out to sea, across it).
GATE_R = 21.5
ge = math.radians(GAPS[2])
GX, GZ = GATE_R * math.cos(ge), GATE_R * math.sin(ge)  # (upper case: the ground loop below reuses gx/gz for face centres)
tx, tz = -math.sin(ge), math.cos(ge)  # across the lane


def gate_frame(x, z):  # (along the lane from the gate, seaward +; across it)
    return (x - GX) * math.cos(ge) + (z - GZ) * math.sin(ge), (x - GX) * tx + (z - GZ) * tz


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
    along, across = gate_frame(x, z)
    if abs(along) < 1.5 and abs(across) < 3.2:
        return gatestone  # the gate's threshold, paved
    d = lane_d(x, z, r)
    j = noise.noise(Vector((x * 0.45, z * 0.45, 9.0))) * 0.6  # the edges wander, no straight cut
    if r < 19.5:
        if d < 0.55:
            return pathwet
        if d < 1.5 + j * 0.4 or near_village_path(x, z):
            return path
        if d < 2.2 + j or near_village_path(x, z, 1.35):
            return pathedge
        return grass2 if noise.noise(Vector((x * 0.15, z * 0.15, 5.0))) > 0.25 else grass
    w = 1.4 + (r - 19.5) * 0.05
    if r < 26.5:
        if d < 0.5:
            return pathwet
        if d < w + j * 0.4:
            return path
        if d < w + 0.8 + j:
            return wet
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

# ---------- the north beach: a shipwreck heeled over on the sand east of the lane (the lane winds west here, x -2..-3 at
# r 22..28; the old wreck sat right on it). Its own OBJ (Wreck) and a smooth closed hull for its collider (WreckCollider):
# fish slide round it instead of wedging between the ribs of an open shell, as they did when it was part of the island mesh. ----------
WRECK_AT = (3.6, 25.6)
WRECK_YAW, WRECK_ROLL, WRECK_SCALE = -15.0, -30.0, 1.4  # heeled away from the lane: its planked side and the hole in it face the island
HL, HW, HH = 7.5, 2.7, 1.9  # the hull before scaling: length (y, bow +y), beam, depth
HR, HK = 11, 11             # rings along the hull, points round each (port gunwale down round the keel to starboard)


def hull_ring(i):
    u = i / (HR - 1.0)
    width = HW * math.sin(math.pi * (0.1 + 0.9 * u)) ** 0.55 * (0.22 if i == HR - 1 else 1.0)
    depth = HH * (0.62 + 0.38 * math.sin(math.pi * u))
    top = 0.22 * (1.0 - math.sin(math.pi * u))  # the sheer: the gunwale rises toward bow and stern
    return [Vector((width / 2 * math.cos(t), -HL / 2 + HL * u, top - depth * math.sin(t) ** 0.75)) for t in (math.pi * k / (HK - 1) for k in range(HK))]


def torn(i, k):  # the lane side (local +x, game west) is stove in amidships: no planks there, the ribs show
    return k <= 3 and 3 <= i <= 6


def shipwreck():
    b = Builder("Wreck", MATS)
    hull = [hull_ring(i) for i in range(HR)]
    kp = b.bm.faces.layers.int.new("keep")
    for i in range(HR - 1):
        for k in range(HK - 1):
            if torn(i, k) or torn(i, k + 1):
                continue
            quad = [hull[i][k], hull[i][k + 1], hull[i + 1][k + 1], hull[i + 1][k]]
            for side in (quad, quad[::-1]):  # planks inside and out
                f = b.bm.faces.new([b.bm.verts.new(v) for v in side])
                f.material_index = MATS.index(wdark if k % 3 == 1 else wwood)
                f[kp] = 1
    for i in range(1, HR - 1, 2):  # ribs inside the hull; amidships they stick up broken past the torn planking
        pts = [p + Vector((0, 0, 0.04)) for p in hull[i]]
        tube(b, pts if not (3 <= i <= 6) else pts[3:], 0.05, wdark, segs=4)
        if 3 <= i <= 6:
            tip = hull[i][3] + (hull[i][3] - hull[i][4]).normalized() * rng.uniform(0.35, 0.7)
            b.beam(hull[i][3], tip, 0.05, wdark, segs=4, r2=0.02)
    tube(b, [r[HK // 2] - Vector((0, 0, 0.06)) for r in hull] + [Vector((0, HL / 2 + 0.9, 1.0))], 0.09, wdark, segs=5)  # the keel, up into the stem
    tube(b, [r[-1] for r in hull], 0.06, wwood, segs=4)                                                                     # the far gunwale rail
    tube(b, [r[0] for r in hull[:4]], 0.06, wwood, segs=4)                                                                  # the near one: torn away amidships
    tube(b, [r[0] for r in hull[6:]], 0.06, wwood, segs=4)
    b.box((HW * 0.4, 0.1, HH * 0.42), (0, -HL / 2 + 0.02, -HH * 0.36), wwood)                                             # the transom
    for k in (2, 6):  # iron strakes round the hull
        tube(b, [Vector((r[k].x * 1.03, r[k].y, r[k].z)) for r in hull[1:-1]], 0.035, wiron, segs=4)
    b.box((0.22, 1.4, 0.16), (0, -0.6, -0.5), wwood)                                                                     # a thwart
    b.beam((0, 0.4, -1.0), (-0.45, 0.55, 2.7), 0.12, wdark, segs=6, r2=0.07)                                              # the broken mast
    b.beam((-0.45, 0.55, 2.7), (-0.6, 0.6, 3.0), 0.06, wdark, segs=4, r2=0.0)
    b.beam((-1.35, 0.7, 2.0), (1.5, 0.62, 2.1), 0.055, wdark, segs=5)                                                     # the yard
    fin(b, [(-1.25, 0.72, 1.96), (1.3, 0.64, 2.05), (1.05, 0.66, 1.1), (0.45, 0.68, 0.45), (-0.2, 0.7, 0.9), (-0.9, 0.72, 0.4)], (0, 1, 0), 0.03, wsail)  # what's left of the sail
    b.beam((-0.55, 0.58, 2.9), (0, HL / 2 + 0.9, 1.0), 0.02, rope, segs=3)                                                 # a stay to the stem
    b.beam((-1.3, 0.7, 2.0), (-1.0, -0.2, 0.1), 0.015, rope, segs=3)                                                      # a loose brace
    ob = b.finish()
    # Its collider: the hull closed over, a little fuller, nothing thin (fish push along it and round the ends).
    c = Builder("WreckCollider", [wwood])
    rings = []
    for i in range(HR):
        u = i / (HR - 1.0)
        width = HW * math.sin(math.pi * (0.1 + 0.9 * u)) ** 0.55 * (0.4 if i == HR - 1 else 1.0) + 0.3
        depth = HH * (0.62 + 0.38 * math.sin(math.pi * u)) + 0.15
        top = 0.22 * (1.0 - math.sin(math.pi * u)) + 0.15
        mid, half = (top - depth) / 2, (top + depth) / 2
        rings.append([Vector((width / 2 * math.cos(t), -HL / 2 + HL * u, mid + half * math.sin(t))) for t in (TAU * k / 10 for k in range(10))])
    loft(c, rings, wwood)
    col = c.finish()
    for o in (ob, col):
        o.matrix_world = (Matrix.Translation(G(WRECK_AT[0], height(*WRECK_AT) + 1.3, WRECK_AT[1])) @ Matrix.Rotation(math.radians(WRECK_YAW), 4, "Z")
                          @ Matrix.Rotation(math.radians(WRECK_ROLL), 4, "Y") @ Matrix.Scale(WRECK_SCALE, 4))
    return ob, col


def drift(cx, cz, w, d, h, yaw, m=sanddrift):
    # A low mound of sand blown up against something: a squashed lumpy sphere, mostly below the beach.
    y0 = height(cx, cz)
    res = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0, matrix=Matrix())
    verts = res["verts"]
    seed = rng.random() * 100.0
    for v in verts:
        p = v.co.copy()
        p *= 1.0 + 0.15 * noise.noise(p * 1.2 + Vector((seed, seed, seed)))
        p.z = max(p.z, -0.35)
        v.co = G(cx, y0, cz) + Matrix.Rotation(yaw, 4, "Z") @ Vector((p.x * w, p.y * d, p.z * h)) - Vector((0, 0, h * 0.2))
    for f in {f for v in verts for f in v.link_faces}:
        f.material_index = MATS.index(m)


def barrel(x, z):
    vkit.barrel(island, Frame(G(x, height(x, z), z)), 0.3, 0.8)


def crate(x, z, s=0.8, yaw=0.0):
    vkit.crate(island, Frame(G(x, height(x, z), z)), s, (0, 0, 0), math.degrees(yaw))


WRECK, WRECK_COL = shipwreck()
gn = math.radians(GAPS[0])
lx, lz = 25.0 * math.cos(gn), 25.0 * math.sin(gn)
wx, wz = WRECK_AT
yaw_w = math.radians(-WRECK_YAW)  # the hull's long axis in Blender's frame
drift(wx + 1.1, wz, 2.2, 5.8, 0.42, yaw_w)      # sand banked against the low side of the hull
drift(wx - 1.6, wz + 1.5, 1.2, 3.0, 0.2, yaw_w, wetdrift)  # a wet scour on the lane side
# The anchor dragged up the sand on the lane side, its chain back to the bow.
ancx, ancz = wx - 2.6, wz - 3.4
ancy = height(ancx, ancz) + 0.06
island.beam(G(ancx, ancy, ancz), G(ancx + 0.9, ancy, ancz - 0.5), 0.045, wiron, segs=5)                  # the shank
island.beam(G(ancx + 0.55, ancy, ancz - 1.0), G(ancx + 1.25, ancy, ancz + 0.1), 0.05, wiron, segs=5)      # the flukes
torus(island, G(ancx - 0.1, ancy + 0.02, ancz + 0.06), (0, 0, 1), 0.13, 0.03, wiron, segs=10, tsegs=4)   # the ring
for k in range(7):
    cx_, cz_ = ancx - 0.25 + k * 0.42, ancz + 0.3 + k * 0.33
    torus(island, G(cx_, height(cx_, cz_) + 0.04, cz_), (0, 0, 1) if k % 2 else (1, 0, 1), 0.09, 0.02, wiron, segs=8, tsegs=4)
for px, pz, yaw in ((wx + 2.6, wz - 2.8, 0.4), (wx + 3.3, wz + 1.6, 1.3), (wx + 1.0, wz + 5.2, 2.0), (wx - 0.6, wz + 6.0, 0.9)):  # planks washed up round it, bedded in the sand
    island.box((1.7, 0.24, 0.08), G(px, height(px, pz) - 0.01, pz), wood, rot=(0, 0, yaw))
    island.box((0.25, 0.06, 0.03), G(px + 0.4 * math.cos(yaw), height(px, pz) + 0.04, pz + 0.4 * math.sin(yaw)), iron, rot=(0, 0, yaw))  # an iron fitting still on it
barrel(wx + 3.0, wz - 1.2)
crate(wx + 2.4, wz + 3.4, 0.7, 0.4)

# ---------- the south-west beach: tide pools on rock ledges ----------
POOLS = []
gs = math.radians(GAPS[1])
for r, off, size in ((22.4, 0.6, 1.9), (25.4, -1.0, 2.2), (23.4, 4.6, 1.3), (26.6, 4.2, 1.2)):
    x, z = r * math.cos(gs) - math.sin(gs) * off, r * math.sin(gs) + math.cos(gs) * off
    y = height(x, z)
    # The beach falls away seaward: the pool sits in a rock ledge built up from the lowest sand round it (a flat disc on the
    # slope floated on its low side, and so did what stood round it).
    low = min(height(x + math.cos(k * TAU / 12) * (size + 0.35), z + math.sin(k * TAU / 12) * (size + 0.35)) for k in range(12))
    island.cone(size + 1.0, size + 0.35, y - low + 0.22, G(x, (y + low) / 2 - 0.09, z), rock, segs=16)  # a ramp, not a step: fish walk up it
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

# ---------- the south-east beach: the old gate the lane runs through: two pillars footed in the slope, a ring arch of
# voussoirs broken short of the right pillar, a ruined wall running from each pillar toward the cliffs, a paved threshold,
# the fallen stones beside the pillars (off the lane), flagstones lost down the lane ----------
gx, gz = GX, GZ
gyL, gyR = height(gx - tx * 3.4, gz - tz * 3.4), height(gx + tx * 3.4, gz + tz * 3.4)
gy = max(gyL, gyR)
ALONG = (math.cos(ge), math.sin(ge))  # seaward along the lane


def gate_block(x, y, z, w, d, h, m, yaw, rx=0.0, ry=0.0):
    island.box((w, d, h), G(x, y, z), m, rot=(rx, ry, yaw))


def gate_pt(along, up, across):  # game coordinates of a point in the gate's frame
    return (gx + ALONG[0] * along + tx * across, up, gz + ALONG[1] * along + tz * across)


# Two weathered pillars: a footing down into the slope (each side's own ground), seven slightly different courses, a capital.
for side, own in ((-1, gyL), (1, gyR)):
    px, pz = gx + tx * side * 3.4, gz + tz * side * 3.4
    foot0, foot1 = own - 0.6, gy + 0.24
    gate_block(px, (foot0 + foot1) / 2, pz, 1.8, 1.6, foot1 - foot0, stone, ge)
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
        gate_block(px + tx * side * 0.70, gy + 0.85 + k * 0.9, pz + tz * side * 0.70, 0.06, 0.12, 0.18, dark, ge)

# The arch: a ring of voussoirs lofted round the curve (one stone per segment, shade alternating), from the left pillar over
# the top and down to a broken end short of the right pillar, which has lost its share. Rings run across the lane (tx) and up.
RI, RO, AD = 2.9, 3.85, 0.84
spring = gy + 3.52


def arch_ring(t):
    cs, sn = math.cos(t), math.sin(t)
    pts = []
    for rad, s in ((RI, -AD / 2), (RO, -AD / 2), (RO, AD / 2), (RI, AD / 2)):
        pts.append(G(*gate_pt(s, spring + rad * sn, rad * cs)))
    return pts


def arch(t0, t1, n):
    ts = [t0 + (t1 - t0) * k / n for k in range(n + 1)]
    loft(island, [arch_ring(t) for t in ts], stone, mf=lambda i, j: rock if i % 2 else stone)


arch(math.pi, 0.62, 13)   # from the left pillar over the crown to the break
x, y, z = gate_pt(0.0, spring + (RI + RO) / 2 + 0.04, 0.0)
gate_block(x, y, z, 0.9, AD + 0.2, RO - RI + 0.22, stone, ge)  # the keystone, proud of the ring

# A ruined wall running from each pillar toward the cliffs: courses stepping down to rubble, footed below the sand.
for side in (-1, 1):
    heights = (2.1, 1.65, 1.15, 0.65)
    for k, h in enumerate(heights):
        across = side * (3.4 + 0.67 + 0.5 + k * 1.0)
        bx, _, bz = gate_pt(0.0, 0.0, across)
        base = min(height(bx + tx * d, bz + tz * d) for d in (-0.5, 0.0, 0.5)) - 0.35
        courses = max(1, round(h / 0.42))
        for c in range(courses):
            y = base + 0.42 * (c + 0.5)
            x, _, z = gate_pt(rng.uniform(-0.03, 0.03), 0.0, across + rng.uniform(-0.04, 0.04))
            gate_block(x, y, z, 1.0 - 0.02 * c, 0.95 - 0.04 * c, 0.4, stone if (c + k) % 2 else rock, ge + rng.uniform(-0.03, 0.03))
        if k == 0:  # a mossy cap where the wall meets the pillar
            x, _, z = gate_pt(0.0, 0.0, across)
            gate_block(x, base + 0.42 * courses + 0.04, z, 0.9, 0.7, 0.08, moss, ge)
    for k in range(3):  # rubble where the wall gives out, sunk into the sand
        across = side * (3.4 + 0.67 + 0.5 + len(heights) * 1.0 + rng.uniform(-0.2, 1.2))
        x, _, z = gate_pt(rng.uniform(-0.9, 0.9), 0.0, across)
        w, d, h = rng.uniform(0.5, 0.8), rng.uniform(0.45, 0.7), rng.uniform(0.3, 0.42)
        gate_block(x, height(x, z) + h * 0.3, z, w, d, h, rock if k % 2 else stone, rng.uniform(0, TAU), rng.uniform(-0.08, 0.08), rng.uniform(-0.08, 0.08))

# Fallen voussoirs beside the right pillar's seaward foot (off the lane, half in the sand), one by the left.
for across, along, m in ((4.9, 1.3, stone), (5.6, 2.2, rock), (4.6, 2.9, stone), (6.3, 1.6, rock), (-5.0, 1.8, rock)):
    x, _, z = gate_pt(along, 0.0, across)
    gate_block(x, height(x, z) + 0.2, z, 0.7, 0.82, 0.5, m, rng.uniform(0, TAU), rng.uniform(-0.1, 0.1), rng.uniform(-0.1, 0.1))

# The threshold: flagstones through the gate, a kerb either side; and stones of the old causeway lost down the lane,
# flush with the sand (this beach's own look: old masonry, as the north has the wreck's timbers and the west its shells).
for i in range(-2, 3):
    for j in range(-3, 4):
        x, _, z = gate_pt(i * 0.6, 0.0, j * 0.8)
        gate_block(x, height(x, z) + 0.01, z, 0.54, 0.74, 0.09, (flagA, stone, flagB)[(i + j) % 3], ge + rng.uniform(-0.03, 0.03))
for i, r in enumerate((23.6, 24.4, 25.3, 26.0, 26.9, 27.6, 23.9, 25.7)):
    a = math.radians(lane_angle(GAPS[2], r))
    off = rng.choice((-1, 1)) * rng.uniform(1.0, 2.0)
    x, z = r * math.cos(a) - math.sin(a) * off, r * math.sin(a) + math.cos(a) * off
    gate_block(x, height(x, z) - 0.04, z, rng.uniform(0.5, 0.7), rng.uniform(0.45, 0.6), 0.14, flagB if i % 2 else stone, rng.uniform(0, TAU))

# Moss at the feet on the north side and a little vine over one shoulder.
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
# The signpost: an arm for each place, pointing where it really is (Blender +x is game west, so an arm's yaw is the game
# direction's atan2(-dz, -dx)), an emblem on each board: a crate for the quartermaster's store, a fish for the stall, an
# anvil for the smithy, the lighthouse itself.
def arm_yaw(x, z):
    return math.degrees(math.atan2(-(z - spz), -(x - spx)))


vkit.signpost(island, Frame(G(spx, height(spx, spz), spz)), [
    (arm_yaw(*VILLAGE[0][:2]), 2.1, vkit.roofr, "crate"), (arm_yaw(*VILLAGE[1][:2]), 1.82, vkit.canvas, "fish"),
    (arm_yaw(*VILLAGE[2][:2]), 1.54, vkit.iron, "anvil"), (arm_yaw(0.0, 0.0), 1.26, vkit.cream, "tower")])
for lx3, lz3 in ((-1.9, -12.0), (1.9, -20.6), (-1.9, -21.4)):  # lamp posts, their lanterns out over the road
    vkit.lamp_post(island, Frame(G(lx3, height(lx3, lz3), lz3), 180.0 if lx3 < 0 else 0.0))
for x, z in ((-4.6, -15.0), (-4.2, -19.6), (3.1, -14.4), (3.4, -12.8)):  # flower boxes by the paths
    vkit.planter(island, Frame(G(x, height(x, z), z)), w=0.9, d=0.38, h=0.32)

# ---------- the harbour: the pier on its piles (bearers, stringers, braces, a stone head on the sand), planks over a dark
# deck sheet (nothing to fall through), rope rails both sides on posts, lanterns, a net rack, cargo, buoys ----------
Z0, Z1 = -24.3, -24.3 - 17 * 0.95
island.box((2.9, 2.0, 1.0), G(0, 0.06, -24.1), kerb)                                   # the head: stone the first planks rest on
island.box((2.56, Z0 - Z1 + 0.9, 0.06), G(0, 0.555, (Z0 + Z1) / 2), vkit.seam)         # the deck sheet: what shows between planks
for s in (-1, 1):
    island.box((0.16, Z0 - Z1 + 0.9, 0.16), G(s * 1.05, 0.44, (Z0 + Z1) / 2), dark)      # stringers under the planks
for k in range(18):  # planks across the pier, z -24 .. -41, with gaps
    z = Z0 - k * 0.95
    island.box((2.6, 0.82, 0.12), G(rng.uniform(-0.04, 0.04), 0.6, z), wood if k % 3 else dark, rot=(0, 0, rng.uniform(-0.02, 0.02)))
PILES = (-24.5, -28.5, -32.5, -36.5, -40.5)
for i, z in enumerate(PILES):
    island.box((3.0, 0.18, 0.16), G(0, 0.44, z), dark)                                   # a bearer across the piles
    for s in (-1, 1):
        island.cyl(0.13, 5.6, G(s * 1.4, -1.6, z), dark, segs=8)                         # piles down to the sea bed, up past the deck
        island.cyl(0.15, 0.08, G(s * 1.4, 0.75, z), iron, segs=8)                        # an iron band
        island.cyl(0.15, 0.06, G(s * 1.4, 1.2, z), iron, segs=8)                         # and a cap
        if 0 < i < len(PILES) - 1:
            island.beam(G(s * 1.4, 0.3, z), G(s * 1.4, -1.3, PILES[i + 1]), 0.05, dark, segs=4)  # a brace down to the next pile
        if i < len(PILES) - 1:
            island.beam(G(s * 1.4, 0.95, z), G(s * 1.4, 0.95, PILES[i + 1]), 0.035, rope, segs=4)  # the rope rail, both sides
            zm = (z + PILES[i + 1]) / 2
            island.box((0.1, 0.1, 0.42), G(s * 1.4, 0.87, zm), dark)                     # a short post halfway, the rope on it
            island.cone(0.07, 0.0, 0.08, G(s * 1.4, 1.12, zm), dark, segs=4)
for s in (-1, 1):  # bollards at the head, a rope coiled on one
    island.box((0.2, 0.2, 1.1), G(s * 1.55, 0.6, -23.6), dark)
    island.cyl(0.14, 0.05, G(s * 1.55, 1.17, -23.6), iron, segs=8)
torus(island, G(1.55, 0.72, -23.6), (0, 0, 1), 0.17, 0.035, rope, segs=10, tsegs=4)
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
    island.beam(G(xk, ny + 2.0, nz), G(xk, ny + 0.5 + 0.2 * math.sin(u * math.pi), nz - 0.2), 0.012, cord, segs=3)
for k in range(4):
    yk = ny + 1.9 - k * 0.42
    island.beam(G(nx - 1.5, yk, nz - 0.05 * k), G(nx + 1.5, yk, nz - 0.05 * k), 0.012, cord, segs=3)
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


# ---------- where the game's plants go ----------
def on_ground(x, z):
    return height(x, z)


decor = []
def add(kind, x, y, z, s):
    decor.append(f"{kind} {x:.2f} {y:.2f} {z:.2f} {rng.uniform(0, 360):.0f} {s:.2f}")


def clear_of_things(x, z):
    r = math.hypot(x, z)
    spots = [(vx, vz, vr + 0.8) for vx, vz, vr in VILLAGE] + [(wx, wz, 6.0), (GX, GZ, 4.2), (4.5, -19.0, 1.6), (0.0, -4.5, 2.4), (-4.6, -25.5, 2.4), (-2.6, -3.4, 1.2), (1.4, -14.2, 0.8), (3.6, -6.2, 1.4)] + [(px, pz, pr + 0.7) for px, _, pz, pr in POOLS]
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
    shown = ["FortressIsland", "Wreck", "PreviewPlants"]
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
    shot("island_gate", G(gx + ALONG[0] * 15.0 + tx * 3.0, 4.5, gz + ALONG[1] * 15.0 + tz * 3.0), G(gx, 2.6, gz), lens=28)
    shot("island_gate_inside", G(gx - ALONG[0] * 13.0 + tx * 5.0, 3.6, gz - ALONG[1] * 13.0 + tz * 5.0), G(gx, 2.4, gz), lens=28)
    shot("island_wreck", G(wx - 9.0, 4.0, wz - 8.0), G(wx, 1.0, wz), lens=30)
    shot("island_wreck_sea", G(wx + 2.0, 5.0, wz + 13.0), G(wx, 0.8, wz), lens=30)
    shot("island_wreck_lane", G(-2.5, 2.2, 32.0), G(0.0, 1.0, 16.0), lens=26)
    shot("island_dock", G(6.0, 3.2, -45.0), G(0.0, 0.6, -30.0), lens=26)
    shot("island_dock_head", G(-4.0, 2.6, -27.5), G(0.0, 0.7, -22.5), lens=28)
    shot("island_signpost", G(-1.4, 2.7, -17.6), G(spx, 1.7, spz), lens=34)
    shot("island_lanes", G(0.0, 26.0, -14.0), G(0.0, 0.0, 10.0), lens=30)
    shot("island_vault_front", G(-1.2, 3.2, -15.6), G(-8.0, 2.2, -17.0), lens=30)
    shot("island_smithy_front", G(1.6, 2.5, -10.2), G(6.2, 1.9, -10.8), lens=35)
    for i, gap in enumerate(GAPS + [DOCK]):
        a = math.radians(gap)
        eye = G(math.cos(a) * 34, 7.0, math.sin(a) * 34)
        look = G(math.cos(a) * 14, 1.5, math.sin(a) * 14)
        shot(f"island_beach{i}", eye, look, lens=20)
    print("PREVIEW", out)
else:
    export([ob, WRECK, WRECK_COL])
