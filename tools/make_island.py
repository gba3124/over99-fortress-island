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
# The plaza's warmer flagstones and its compass rose's dark points; the old gate's ivy (dark, on the walls) and rusted iron.
flagC = mat("IslFlagC", (0.63, 0.57, 0.48), force=True)
roseD = mat("IslRoseDark", (0.37, 0.38, 0.41), force=True)
ivy = mat("IslIvy", (0.22, 0.36, 0.17), force=True)
rust = mat("IslRust", (0.44, 0.28, 0.19), force=True)
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


def village_d(x, z):  # distance to the nearest footpath's centre line
    best = 99.0
    for x0, z0, x1, z1 in VILLAGE_PATHS:
        dx, dz = x1 - x0, z1 - z0
        t = max(0.0, min(1.0, ((x - x0) * dx + (z - z0) * dz) / (dx * dx + dz * dz)))
        best = min(best, math.hypot(x - (x0 + dx * t), z - (z0 + dz * t)))
    return best


def near_village_path(x, z, width=0.85):
    return village_d(x, z) < width


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


# The ground's colours: each a field that is negative where it applies, tried in order (the first that applies wins).
def jit(x, z):  # the edges wander, no straight cut
    return noise.noise(Vector((x * 0.45, z * 0.45, 9.0))) * 0.6


def lane(x, z):
    return lane_d(x, z, math.hypot(x, z))


def beach_w(x, z):
    return 1.4 + (math.hypot(x, z) - 19.5) * 0.05


def f_gate(x, z, y):  # the gate's threshold, paved
    along, across = gate_frame(x, z)
    return max(abs(along) - 1.5, abs(across) - 3.2)


def f_rut(x, z, y):  # a road's trodden middle
    return lane(x, z) - 0.55


def f_road(x, z, y):
    return min(lane(x, z) - 1.5 - jit(x, z) * 0.4, village_d(x, z) - 0.85)


def f_dust(x, z, y):  # dust where a road meets the grass
    return min(lane(x, z) - 2.2 - jit(x, z), village_d(x, z) - 1.35)


def f_grass2(x, z, y):
    return 0.25 - noise.noise(Vector((x * 0.15, z * 0.15, 5.0)))


def f_beach_rut(x, z, y):
    return lane(x, z) - 0.5


def f_beach_road(x, z, y):
    return lane(x, z) - beach_w(x, z) - jit(x, z) * 0.4


def f_beach_damp(x, z, y):  # damp sand either side of a road on the beach
    return lane(x, z) - beach_w(x, z) - 0.8 - jit(x, z)


def f_tide(x, z, y):  # wet sand by the waterline
    return max(-1.6 - y, y + 0.6)


def ground_layers(r_out):  # for the ring of faces out to r_out: [(field, material)] and the material left over
    if r_out <= 6.0:
        return [], grout  # under the flagstones (the plaza paving below)
    if r_out <= 19.5:
        return [(f_gate, gatestone), (f_rut, pathwet), (f_road, path), (f_dust, pathedge), (f_grass2, grass2)], grass
    if r_out <= 26.5:
        return [(f_gate, gatestone), (f_beach_rut, pathwet), (f_beach_road, path), (f_beach_damp, wet), (f_tide, wet)], sand
    return [(f_gate, gatestone), (f_tide, wet)], sand


island = Builder("FortressIsland", MATS)
bm = island.bm
KEEP = bm.faces.layers.int.new("keep")  # the ground sheet: its faces are wound to face up, and stay that way
RINGS = [0.0, 0.8, 1.6, 2.5, 3.5, 4.5, 5.5, 6.0] + [6.0 + 0.9 * i for i in range(1, 16)] + [19.5 + 1.0 * i for i in range(1, 12)] + [31.0 + 2.0 * i for i in range(1, 8)]
SEG = 144
# Each grid face is cut where those fields cross zero (found on its edges by bisection), so a road's edge runs smooth instead
# of stepping cell by cell (Owen: 「土路顆粒感還是有點重」). A cut point sits on the edge it cuts, at that edge's own height, so
# the ground keeps its shape; a point on an edge two faces share is made once and used by both.
P = []  # points (x, z, y), game coordinates
ON = {}  # point -> (grid edge (a, b) with a < b, how far along it) for the points cut into the grid's edges
EDGE_PTS = {}  # grid edge -> the points cut into it
CUT, FV = {}, {}


def pt(x, z, y):
    P.append((x, z, y))
    return len(P) - 1


def on_edge(p, q):  # (grid edge, p's and q's places along it) when the segment p-q runs along one, else None
    ep, eq = ON.get(p), ON.get(q)
    if ep is None and eq is None:  # two grid points next to each other on a face
        return (min(p, q), max(p, q)), float(p > q), float(q > p)
    if ep is None:
        return (eq[0], float(p == eq[0][1]), eq[1]) if p in eq[0] else None
    if eq is None:
        return (ep[0], ep[1], float(q == ep[0][1])) if q in ep[0] else None
    return (ep[0], ep[1], eq[1]) if ep[0] == eq[0] else None


def val(p, f):
    if (p, f) not in FV:
        FV[p, f] = f(*P[p])
    return FV[p, f]


def cut(p, q, f):  # the point between p and q where field f changes sign
    a, b = min(p, q), max(p, q)
    if (a, b, f) not in CUT:
        (xa, za, ya), (xb, zb, yb) = P[a], P[b]
        at = lambda s: (xa + (xb - xa) * s, za + (zb - za) * s, ya + (yb - ya) * s)
        lo, hi, ina = 0.0, 1.0, val(a, f) < 0
        for _ in range(14):
            mid = (lo + hi) / 2
            lo, hi = (mid, hi) if (f(*at(mid)) < 0) == ina else (lo, mid)
        s = (lo + hi) / 2
        c = pt(*at(s))
        e = on_edge(a, b)
        if e:
            ON[c] = (e[0], e[1] + (e[2] - e[1]) * s)
            EDGE_PTS.setdefault(e[0], []).append(c)
        CUT[a, b, f] = c
    return CUT[a, b, f]


def split(poly, f):  # a convex polygon's parts inside and outside field f (either may be None)
    ins = [val(p, f) < 0 for p in poly]
    if all(ins):
        return poly, None
    if not any(ins):
        return None, poly
    if sum(ins[i] != ins[i - 1] for i in range(len(poly))) > 2:  # crossed twice (a saddle): whole, by its middle
        x, z, y = (sum(P[p][n] for p in poly) / len(poly) for n in range(3))
        return (poly, None) if f(x, z, y) < 0 else (None, poly)
    inside, outside = [], []
    for i, p in enumerate(poly):
        (inside if ins[i] else outside).append(p)
        if ins[i] != ins[(i + 1) % len(poly)]:
            c = cut(p, poly[(i + 1) % len(poly)], f)
            inside.append(c)
            outside.append(c)
    return inside, outside


grid = []
for r in RINGS:
    row = []
    for i in range(SEG):
        a = i * TAU / SEG
        x, z = r * math.cos(a), r * math.sin(a)
        row.append(pt(x, z, height(x, z)))
        if r == 0.0:
            break
    grid.append(row)
pieces = []
for j in range(1, len(grid)):
    A, B = grid[j - 1], grid[j]
    layers, rest = ground_layers(RINGS[j])
    for i in range(SEG):
        k = (i + 1) % SEG
        todo = [[A[0], B[i], B[k]] if len(A) == 1 else [A[i], B[i], B[k], A[k]]]  # inner, outer, outer next, inner next: counter-clockwise from above, so it faces up
        for f, m in layers:
            left = []
            for poly in todo:
                inside, outside = split(poly, f)
                if inside:
                    pieces.append((inside, m))
                if outside:
                    left.append(outside)
            todo = left
        pieces += [(poly, rest) for poly in todo]
V = [bm.verts.new(G(x, y, z)) for x, z, y in P]
for poly, m in pieces:
    full = []  # with the points the face across an edge cut into it (where the two use different fields): no T-junctions
    for i, p in enumerate(poly):
        full.append(p)
        e = on_edge(p, poly[(i + 1) % len(poly)])
        if e:
            E, t0, t1 = e
            full += sorted((c for c in EDGE_PTS.get(E, ()) if min(t0, t1) < ON[c][1] < max(t0, t1)), key=lambda c: ON[c][1], reverse=t1 < t0)
    if len(full) == len(poly):
        faces = [[V[p] for p in full]]
    else:  # points in a row along an edge: fanned from the middle, so triangulating can't leave flat slivers facing anywhere
        x, z, y = (sum(P[p][n] for p in full) / len(full) for n in range(3))
        mid = bm.verts.new(G(x, y, z))
        faces = [[mid, V[full[i]], V[full[(i + 1) % len(full)]]] for i in range(len(full))]
    for vs in faces:
        f = bm.faces.new(vs)
        f[KEEP] = 1
        f.material_index = MATS.index(m)

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

MATS += [flagC, roseD, ivy, rust]  # the island's own (the wreck, built above, keeps its list and its .mtl)


# ---------- stonework, for the old gate and the plaza paving below: flat stones cut to an outline, dressed blocks, wedges, leaves ----------
def area(poly):  # of an outline (game x, z): > 0 when it runs counter-clockwise seen from above
    return sum(poly[i - 1][0] * poly[i][1] - poly[i][0] * poly[i - 1][1] for i in range(len(poly))) / 2.0


def inset(poly, d):  # the outline moved in by d (each corner along its bisector, so both its edges move d)
    s = 1.0 if area(poly) > 0 else -1.0
    out = []
    for i, (x1, z1) in enumerate(poly):
        (x0, z0), (x2, z2) = poly[i - 1], poly[(i + 1) % len(poly)]
        a, b = Vector((x1 - x0, z1 - z0)).normalized(), Vector((x2 - x1, z2 - z1)).normalized()
        na, nb = Vector((-a.y, a.x)) * s, Vector((-b.y, b.x)) * s
        k = d / max(0.35, 1.0 + na.dot(nb))
        out.append((x1 + (na.x + nb.x) * k, z1 + (na.y + nb.y) * k))
    return out


def slab(poly, top, m, bev=0.02, base=None):
    # A flat stone cut to an outline (game x, z): up from `base` (under the ground) to `top` (a height, or a function of x, z),
    # its top edges chamfered: in by bev, down by 0.6 bev (half that on a sliver too small for it, none on a smaller one).
    tf = top if callable(top) else (lambda x, z: top)
    a, inner, b = area(poly), poly, 0.0
    for bb in (bev, bev * 0.5):
        q = inset(poly, bb)
        if area(q) * a > 0 and abs(area(q)) > 0.3 * abs(a):
            inner, b = q, bb
            break
    lo = base if base is not None else min(tf(x, z) for x, z in poly) - 0.08
    rings = [[G(x, lo, z) for x, z in poly]] + ([[G(x, tf(x, z) - b * 0.6, z) for x, z in poly]] if b else [])
    loft(island, rings + [[G(x, tf(x, z), z) for x, z in inner]], m)


def dressed(mtx, size, m, bev=0.035, chip=None):
    # A dressed block: a box (size along its own x, y, z; mtx puts its middle) with every edge chamfered; chip = (sx, sy, depth)
    # knocks off its top corner on that side (x, y signs), that deep.
    r = bmesh.ops.create_cube(bm, size=1.0, matrix=mtx @ Matrix.Diagonal((*size, 1.0)))
    new = r["verts"]
    if bev:
        new = bmesh.ops.bevel(bm, geom=new + list({e for v in new for e in v.link_edges}), offset=bev, segments=1, affect="EDGES", clamp_overlap=True)["verts"]
    for f in {f for v in new for f in v.link_faces}:
        f.material_index = MATS.index(m)
    if chip:
        sx, sy, depth = chip
        corner = mtx @ Vector((sx * size[0] / 2, sy * size[1] / 2, size[2] / 2))
        d = mtx.to_3x3() @ Vector((-sx, -sy, -1.0)).normalized()
        for v in new:
            k = (v.co - corner).dot(d)
            if k < depth:
                v.co += d * (depth - k)


def wedge(poly, depth, m, mtx, bev=0.03):  # a stone with outline poly (its own y, z) and depth along its x, every edge chamfered
    inner = inset(poly, bev)
    loft(island, [[mtx @ Vector((x, p, q)) for p, q in pts] for x, pts in ((-depth / 2, inner), (-depth / 2 + bev, poly), (depth / 2 - bev, poly), (depth / 2, inner))], m)


def card(pts, m, facing=None, two=False):
    # A thin flat thing (a leaf, a grass blade): one face turned to `facing` (a direction), or two back to back when it
    # hangs in the air. Kept as wound (finish() would turn single faces any way it likes).
    for side in ((1, -1) if two else (1,)):
        f = bm.faces.new([bm.verts.new(p) for p in (pts if side > 0 else pts[::-1])])
        f.normal_update()
        if facing is not None and f.normal.dot(facing) * side < 0:
            f.normal_flip()
        f[KEEP] = 1
        f.material_index = MATS.index(m)


def leaf(at, nrm, up, s, m, two=False):  # a diamond leaf s long on a surface facing nrm (or hanging), its tip toward up
    n = Vector(nrm).normalized()
    u = (Vector(up) - n * Vector(up).dot(n)).normalized()
    w, at = n.cross(u), Vector(at)
    card([at, at + u * s * 0.4 + w * s * 0.3 + n * 0.01, at + u * s, at + u * s * 0.4 - w * s * 0.3 + n * 0.01], m, n, two)


def moss_on(cx, cz, y, r, m=moss, k=7):  # a cushion of moss on a flat top: a lumpy outline r across, 1.5 cm thick
    a0 = rng.uniform(0, TAU)
    pts = [(cx + math.cos(a0 + i * TAU / k) * r * rng.uniform(0.6, 1.0), cz + math.sin(a0 + i * TAU / k) * r * rng.uniform(0.6, 1.0)) for i in range(k)]
    slab(pts, y + 0.015, m, bev=0.01, base=y - 0.01)


# ---------- the south-east beach: the old gate the lane runs through. Two piers of dressed stone (a footing down into the slope,
# a weathered base, seven courses laid one block, two along or two across, a capital: band, bell with dentils, abacus) under a
# ring of chamfered voussoirs with a stepped back and a carved keystone, broken off short of the right pier, whose abacus has
# split (half of it down in the sand with two fallen voussoirs and rubble). Ruined walls of coursed blocks stepping down toward
# the cliffs; iron pintles where the doors hung, a strap rusting on one and what's left of a door against the left wall; ivy up
# the left pier, vines hanging from the arch, moss on the tops; a threshold of a long sill and worn flagstones, flagstones lost
# down the lane. The lane through it stays clear and flush. Its own random numbers: the island's go on as the old gate left
# them (below), so the harbour and the plants stay put. ----------
gx, gz = GX, GZ
gyL, gyR = height(gx - tx * 3.4, gz - tz * 3.4), height(gx + tx * 3.4, gz + tz * 3.4)
gy = max(gyL, gyR)
ALONG = (math.cos(ge), math.sin(ge))  # seaward along the lane
rng, _rng = random.Random(1909), rng


def gate_block(x, y, z, w, d, h, m, yaw, rx=0.0, ry=0.0):
    island.box((w, d, h), G(x, y, z), m, rot=(rx, ry, yaw))


def gate_pt(along, up, across):  # game coordinates of a point in the gate's frame
    return (gx + ALONG[0] * along + tx * across, up, gz + ALONG[1] * along + tz * across)


GF = Matrix(((-ALONG[0], -tx, 0, 0), (-ALONG[1], -tz, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1)))  # the frame's axes (along, across, up) in Blender
ALONG_B, ACROSS_B, UP_B = G(ALONG[0], 0, ALONG[1]), G(tx, 0, tz), Vector((0, 0, 1))


def gm(along, up, across, yaw=0.0, tip=0.0, roll=0.0):  # a place in the gate's frame: turned about up, tipped about across, rolled about along
    return Matrix.Translation(G(*gate_pt(along, up, across))) @ GF @ Matrix.Rotation(yaw, 4, "Z") @ Matrix.Rotation(tip, 4, "Y") @ Matrix.Rotation(roll, 4, "X")


def gground(along, across):
    x, _, z = gate_pt(along, 0.0, across)
    return height(x, z)


def crack(pts, nrm, m=iron):  # a dark crack zigzagging over a face: pts (along, up, across), pressed into the face along nrm
    for a, b in zip(pts, pts[1:]):
        island.beam(G(*gate_pt(*a)) + nrm * 0.004, G(*gate_pt(*b)) + nrm * 0.004, 0.011, m, segs=4)


STONE = (flagA, flagA, flagB, flagB, flagC, rock)  # the gate's dressed stone (the plaza's flagstones came from the same quarry)
DAMP = (kerb, rock, flagB)                                  # its foot, darker where the sand keeps it wet
COURSES = (0.44, 0.38, 0.42, 0.36, 0.40, 0.38, 0.38)        # the shaft from gy + 0.44 to gy + 3.2
SPRING = gy + 3.62                                          # the capital's top, where the arch springs


def pier_face(up):  # how far seaward a pier's seaward face is at height up (footing, base, the courses narrowing)
    if up < gy + 0.14:
        return 0.93
    if up < gy + 0.44:
        return 0.81
    y = gy + 0.44
    for k, h in enumerate(COURSES):
        if up < y + h:
            return 0.657 - 0.01 * k
        y += h
    return 0.68

for side, own in ((-1, gyL), (1, gyR)):
    c = side * 3.4
    foot0 = own - 0.55
    dressed(gm(0.0, (foot0 + gy + 0.14) / 2, c), (1.86, 1.68, gy + 0.14 - foot0), kerb, bev=0.05)
    dressed(gm(0.0, gy + 0.29, c), (1.62, 1.46, 0.3), rock, bev=0.085, chip=(1, -side, 0.09) if side > 0 else None)
    y = gy + 0.44
    for k, h in enumerate(COURSES):
        w, d = 1.32 - 0.02 * k, 1.22 - 0.02 * k
        kind = (k + (side > 0)) % 3
        s = rng.uniform(-0.14, 0.14)
        parts = [(0.0, 0.0, w, d)] if kind == 0 else (
            [((-w / 2 + s) / 2, 0.0, w / 2 + s, d), ((w / 2 + s) / 2, 0.0, w / 2 - s, d)] if kind == 1 else
            [(0.0, (-d / 2 + s) / 2, w, d / 2 + s), (0.0, (d / 2 + s) / 2, w, d / 2 - s)])
        for pa, pc, pw, pd in parts:
            m = rng.choice(DAMP if k < 2 else STONE)
            chip = (rng.choice((-1, 1)), rng.choice((-1, 1)), rng.uniform(0.06, 0.11)) if rng.random() < 0.3 else None
            if side > 0 and k == 6 and pa > 0.0:  # the right pier's top course: its seaward block has lost a corner
                chip = (1, 1, 0.16)
            dressed(gm(pa, y + h / 2, c + pc + rng.uniform(-0.012, 0.012)), (pw - 0.006, pd - 0.006, h - 0.004), m, bev=0.035, chip=chip)
        y += h
    # The capital: a band, a bell flaring out under the abacus with dentils round it, the abacus (the right one split, its
    # seaward half fallen: below).
    dressed(gm(0.0, gy + 3.24, c), (1.36, 1.26, 0.08), flagB, bev=0.02)
    loft(island, [[G(*gate_pt(w2 / 2 if sa > 0 and side < 0 else 0.12 if sa > 0 else -w2 / 2, yy, c + sc * d2 / 2)) for sa, sc in ((1, 1), (-1, 1), (-1, -1), (1, -1))]
                  for w2, d2, yy in ((1.27, 1.17, gy + 3.28), (1.42, 1.32, gy + 3.39), (1.5, 1.4, gy + 3.46))], flagA)  # (the right one's seaward half went with the abacus)
    for sa in ((-1, 1) if side < 0 else (-1,)):  # dentils on the faces seen down the lane
        for i in range(6):
            gate_block(*gate_pt(sa * 0.73, gy + 3.405, c + (i - 2.5) * 0.21), 0.07, 0.11, 0.07, flagB, ge)
    if side < 0:
        dressed(gm(0.0, gy + 3.54, c), (1.66, 1.56, 0.16), flagA, bev=0.035)
        moss_on(*[gate_pt(-0.2, 0, c - 0.3)[i] for i in (0, 2)], gy + 3.62, 0.5)
    else:
        dressed(gm(-0.43, gy + 3.53, c, tip=0.09), (0.8, 1.56, 0.16), flagA, bev=0.035, chip=(1, 1, 0.12))
crack([(pier_face(u), u, a) for u, a in ((gy + 3.12, 3.28), (gy + 2.75, 3.52), (gy + 2.45, 3.34), (gy + 2.05, 3.62), (gy + 1.8, 3.5))], ALONG_B)

# The arch: fifteen voussoirs over the half circle (seven the keystone), every other one longer at the back; from the left
# pier over the crown to the break: voussoirs 12-14 have fallen, 11 has slipped on its joint. Each a chamfered wedge with a
# joint either side; outlines in the arch's plane (across, up).
RI, AD, NV = 2.9, 0.84, 15
AM = Matrix.Translation(G(gx, 0.0, gz)) @ GF  # (along, across, up) -> Blender


def vous(k, g=0.0125):  # voussoir k's outline: its joints at the ends of its share of the half circle, each g into the stone
    t0, t1 = math.pi * (1 - k / NV), math.pi * (1 - (k + 1) / NV)
    ro = 4.12 if k == 7 else 3.8 + (0.14 if k % 2 else 0.0)
    return [(r * math.cos(t) + sd * g * math.sin(t), SPRING + r * math.sin(t) - sd * g * math.cos(t))
            for r, t, sd in ((RI, t0, 1), (ro, t0, 1), (ro, t1, -1), (RI, t1, -1))]


for k in range(12):
    poly = vous(k)
    if k == 11:  # broken off across, slipped on its joint with voussoir 10 and sagging
        t0, D = math.pi * (1 - 11 / NV), math.pi / NV
        poly = poly[:2] + [(r * math.cos(t0 - f * D), SPRING + r * math.sin(t0 - f * D)) for r, f in ((3.55, 0.45), (3.2, 0.8), (RI, 0.62))]
        (px, py), q = poly[0], -0.08
        poly = [(px + (x - px) * math.cos(q) - (y - py) * math.sin(q), py + (x - px) * math.sin(q) + (y - py) * math.cos(q) - 0.035) for x, y in poly]
    if k == 7:  # the keystone: deeper than the ring, standing proud of both faces, sunk a little with the years
        poly = [(x, y - 0.03) for x, y in poly]
    wedge(poly, AD + (0.16 if k == 7 else 0.0), flagA if k == 7 else rng.choice(STONE), AM)
# A star carved on the keystone, both faces (the compass rose's, at the lighthouse).
kc = SPRING + (RI + 4.12) / 2 - 0.03
star = [((0.19 if i % 2 == 0 else 0.065) * math.sin(i * math.pi / 4), kc + (0.19 if i % 2 == 0 else 0.065) * math.cos(i * math.pi / 4)) for i in range(8)]
for sa in (-1, 1):
    loft(island, [[AM @ Vector((sa * a, p, q)) for p, q in star] for a in (0.49, 0.525)], flagB)
# Cracks: down from the break through voussoir 10, and over the left pier's seaward face.
crack([(AD / 2, SPRING + r * math.sin(t), r * math.cos(t)) for r, t in ((3.77, 0.95), (3.48, 0.91), (3.22, 0.96), (2.93, 0.93))], ALONG_B)
crack([(pier_face(u), u, a) for u, a in ((gy + 2.95, -3.15), (gy + 2.55, -3.32), (gy + 2.3, -3.1), (gy + 1.85, -3.26), (gy + 1.5, -3.05))], ALONG_B)
# Moss over the crown, where the rain sits.
for k in (6, 7, 8):
    t = math.pi * (1 - (k + 0.5) / NV)
    ro = 4.12 - 0.03 if k == 7 else 3.8 + (0.14 if k % 2 else 0.0)
    pts = [(rng.uniform(-0.32, -0.18), t + 0.07), (rng.uniform(0.18, 0.32), t + 0.07), (rng.uniform(0.15, 0.3), t - 0.07), (rng.uniform(-0.3, -0.15), t - 0.07)]
    loft(island, [[AM @ Vector((a, (ro + dr) * math.cos(tt), SPRING + (ro + dr) * math.sin(tt))) for a, tt in pts] for dr in (-0.012, 0.008)], moss)

# Ruined walls from each pier toward the cliffs: courses of blocks of all lengths stepping down to rubble, footed under the sand;
# moss on some of the tops, a block slid off here and there.
WALL = []  # (side, across, length, bottom, height, slid seaward)
for side in (-1, 1):
    x0, x1 = 4.07, 8.1
    base = min(gground(a, side * u * 0.25) for u in range(int(x0 * 4), int(x1 * 4) + 1) for a in (-0.43, 0.43)) - 0.3
    tops, y, ci = [], base, 0
    while y < base + 2.5:
        h = rng.uniform(0.33, 0.42)
        u = x0
        first = rng.uniform(0.3, 0.55) if ci % 2 else rng.uniform(0.55, 0.9)
        row = []
        while u < x1 - 0.12:
            L = min(first if u == x0 else rng.uniform(0.45, 0.9), x1 - u)
            uc = u + L / 2
            limit = base + 2.4 - 1.65 * (uc - x0) / (x1 - x0) + rng.uniform(-0.18, 0.12)
            if y + h > gground(0.0, side * uc) - 0.05 and y + h <= limit and rng.random() > 0.06:
                row.append((uc, L))
            u += L
        for uc, L in row:
            slid = rng.uniform(0.05, 0.1) if y + h > base + 1.2 and rng.random() < 0.12 else 0.0
            dressed(gm(slid, y + h / 2, side * uc, rng.uniform(-0.012, 0.012) + (rng.uniform(-0.09, 0.09) if slid else 0.0)),
                    (0.86, L - 0.008, h - 0.004), rng.choice(DAMP if y < base + 0.6 else STONE), bev=0.035,
                    chip=(rng.choice((-1, 1)), rng.choice((-1, 1)), rng.uniform(0.06, 0.12)) if rng.random() < 0.25 else None)
            WALL.append((side, uc, L, y, h, slid))
        tops.append((y + h, row))
        y += h
        ci += 1
    for i, (yt, row) in enumerate(tops):  # moss on the blocks nothing sits on
        above = tops[i + 1][1] if i + 1 < len(tops) else []
        for uc, L in row:
            if not any(abs(uc - u2) < (L + L2) / 2 - 0.1 for u2, L2 in above) and rng.random() < 0.55:
                moss_on(*[gate_pt(rng.uniform(-0.15, 0.15), 0, side * uc)[i] for i in (0, 2)], yt, min(0.4, L * 0.45))
    for k in range(4):  # where the wall gives out: blocks sunk in the sand
        u = x1 + rng.uniform(0.0, 1.5)
        a = rng.uniform(-0.9, 0.9)
        w, d, h = rng.uniform(0.45, 0.75), rng.uniform(0.4, 0.65), rng.uniform(0.3, 0.42)
        dressed(gm(a, gground(a, side * u) + h * 0.05, side * u, rng.uniform(0, TAU), rng.uniform(-0.12, 0.12), rng.uniform(-0.12, 0.12)),
                (w, d, h), rng.choice(STONE), bev=0.03, chip=(1, 1, 0.08) if k % 2 else None)

# What fell: the right abacus's seaward half and voussoirs 12 and 13 beside the right pier's seaward foot (off the lane), half in
# the sand with sand blown against them, chips of 14 round them; one block by the left pier.
x, _, z = gate_pt(1.6, 0, 4.85)
dressed(gm(1.6, height(x, z) + 0.02, 4.85, 0.5, 0.18, 0.1), (0.8, 1.56, 0.16), flagA, bev=0.035, chip=(-1, 1, 0.1))
moss_on(*[gate_pt(1.75, 0, 4.6)[i] for i in (0, 2)], height(x, z) + 0.12, 0.3)
for k, al, ac, yaw, tip, roll in ((12, 2.5, 5.75, 1.2, 1.45, 0.1), (13, 1.2, 6.4, -0.5, 0.06, -math.pi * (1 - 14 / NV))):  # face down; on its joint
    poly = vous(k)
    cx, cy = sum(p for p, _ in poly) / 4, sum(q for _, q in poly) / 4
    poly = [(p - cx, q - cy) for p, q in poly]
    low = AD / 2 if abs(tip) > 1.0 else -min(p * math.sin(roll) + q * math.cos(roll) for p, q in poly)
    x, _, z = gate_pt(al, 0, ac)
    wedge(poly, AD, rng.choice(STONE), gm(al, height(x, z) + low - 0.3, ac, yaw, tip, roll), bev=0.03)
for k in range(6):
    al, ac = rng.uniform(0.9, 3.4), rng.uniform(4.4, 6.9)
    x, _, z = gate_pt(al, 0, ac)
    s = rng.uniform(0.2, 0.36)
    dressed(gm(al, height(x, z) + s * 0.2, ac, rng.uniform(0, TAU), rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3)),
            (s * rng.uniform(1.0, 1.6), s, s * rng.uniform(0.6, 0.9)), rng.choice(STONE), bev=0.025, chip=(1, -1, s * 0.4))
x, _, z = gate_pt(1.8, 0, -5.0)
dressed(gm(1.8, height(x, z) + 0.1, -5.0, 0.7, 0.08, -0.1), (0.7, 0.82, 0.5), rock, bev=0.035, chip=(1, 1, 0.1))
for al, ac, w, d, h, yaw in ((1.9, 5.25, 0.55, 0.8, 0.2, 0.4), (1.5, 6.6, 0.45, 0.65, 0.16, 1.1), (2.0, -5.25, 0.45, 0.6, 0.15, 0.2)):
    x, _, z = gate_pt(al, 0, ac)
    drift(x, z, w, d, h, ge + yaw)

# Where the doors hung: two iron pintles on each pier's face to the lane; on the left one's lower pintle a broken strap rusts.
for side in (-1, 1):
    fc = side * (3.4 - 0.6)
    for yy in (gy + 0.8, gy + 2.5):
        gate_block(*gate_pt(-0.45, yy, fc - side * 0.012), 0.16, 0.03, 0.13, iron, ge)
        gate_block(*gate_pt(-0.45, yy, fc - side * 0.07), 0.045, 0.12, 0.045, iron, ge)
        island.cyl(0.022, 0.16, G(*gate_pt(-0.45, yy + 0.05, fc - side * 0.125)), iron, segs=6)
eye = Vector(G(*gate_pt(-0.45, gy + 0.86, -2.8 + 0.125)))
torus(island, eye, (0, 0, 1), 0.04, 0.012, rust, segs=8, tsegs=4)
island.beam(eye + G(0.0, -0.02, 0.0) + ACROSS_B * 0.04, eye + G(0.0, -0.62, 0.0) + ACROSS_B * 0.16 - ALONG_B * 0.12, 0.016, rust, segs=4)
# What's left of a door: four planks on two rusted straps, one board broken short, propped against the left wall's seaward face.
lean = 0.3
foot = min(gground(0.86, -5.05 - i * 0.19) for i in range(4)) - 0.08
for i, hgt in enumerate((1.45, 1.52, 1.1, 1.48)):
    dressed(gm(0.86 - math.sin(lean) * hgt / 2, foot + math.cos(lean) * hgt / 2, -5.05 - i * 0.19, tip=-lean), (0.05, 0.17, hgt), wwood if i % 2 else wdark, bev=0.008)
for yy in (0.3, 0.95):
    dressed(gm(0.86 - math.sin(lean) * yy + math.cos(lean) * 0.037, foot + math.cos(lean) * yy + math.sin(lean) * 0.037, -5.33, tip=-lean), (0.025, 0.8, 0.07), rust, bev=0.0)

# Ivy: stems wandering up the left pier's seaward face (the wall's foot to the capital, one over onto the arch's face), a smaller
# one up the right pier's inland face, leaves clustered either side of each; more spilling down the right wall's seaward face;
# vines hanging from the arch, tapering, a few white flowers on them.
def ivy_run(pts, at, nrm, spread=0.09, step=0.06):
    # leaves along a stem through pts (up, across), at(up, across) the point on the face (Blender), nrm the face's normal
    for (u0, a0), (u1, a1) in zip(pts, pts[1:]):
        n = max(1, int(math.hypot(u1 - u0, a1 - a0) / step))
        for i in range(n):
            u, a = u0 + (u1 - u0) * i / n, a0 + (a1 - a0) * i / n
            for _ in range(rng.choice((1, 2, 2))):
                du, da = rng.uniform(-spread, spread) * 0.4, rng.uniform(-spread, spread)
                leaf(at(u + du, a + da), nrm, UP_B + ACROSS_B * rng.uniform(-0.9, 0.9) + nrm * 0.0, rng.uniform(0.12, 0.17), ivy if rng.random() < 0.55 else vkit.leaf)


def wander(u0, a0, u1, a1, n, amp):  # a stem from (u0, a0) to (u1, a1) in n kinks, wandering amp either side
    return [(u0 + (u1 - u0) * i / n, a0 + (a1 - a0) * i / n + (rng.uniform(-amp, amp) if 0 < i < n else 0.0)) for i in range(n + 1)]


seaL = lambda u, a: G(*gate_pt(pier_face(u) + 0.006, u, max(-3.99, min(-2.82, a))))
low = max(gyL + 0.04, gy - 0.05)
ivy_run(wander(low, -3.95, gy + 3.15, -3.45, 7, 0.12), seaL, ALONG_B)
ivy_run(wander(low, -3.62, gy + 2.1, -3.05, 5, 0.1), seaL, ALONG_B)
ivy_run(wander(low, -3.2, gy + 0.95, -2.95, 3, 0.06), seaL, ALONG_B, spread=0.07)
archL = lambda u, a: G(*gate_pt(AD / 2 + 0.012, u, a))
ivy_run([(SPRING + r * math.sin(t), r * math.cos(t)) for r, t in ((3.35, math.pi), (3.4, math.pi * 0.95), (3.3, math.pi * 0.9), (3.45, math.pi * 0.84), (3.35, math.pi * 0.79))], archL, ALONG_B, spread=0.12)
landR = lambda u, a: G(*gate_pt(-pier_face(u) - 0.006, u, max(2.82, min(3.99, a))))
ivy_run(wander(max(gyR + 0.04, gy - 0.05), 3.9, gy + 1.85, 3.55, 4, 0.1), landR, -ALONG_B, spread=0.08)
for side, uc, L, y, h, slid in WALL:  # spilling down the right wall's seaward face from its top, thinning out lower down
    if side > 0 and uc < 6.6:
        for i in range(int(L * h * 30)):
            up = y + h * (1.0 - rng.random() ** 1.8)
            if up > gground(0.43 + slid, uc) + 0.08 and rng.random() < 0.85:
                leaf(G(*gate_pt(0.43 + slid + 0.004, up, uc + rng.uniform(-L / 2 + 0.06, L / 2 - 0.06))), ALONG_B, -UP_B + ACROSS_B * rng.uniform(-0.6, 0.6),
                     rng.uniform(0.12, 0.17), ivy if rng.random() < 0.5 else vkit.leaf)
for k, al, length in ((1, 0.1, 0.6), (2, -0.2, 1.1), (3, 0.15, 1.5), (4, -0.25, 0.7), (5, 0.05, 1.85), (6, -0.3, 0.5), (8, 0.25, 1.2),
                      (9, -0.1, 1.55), (10, 0.2, 0.8), (3, -0.32, 0.45), (8, -0.28, 0.6)):
    t = math.pi * (1 - (k + rng.uniform(0.3, 0.7)) / NV)
    top = Vector(G(*gate_pt(al, SPRING + RI * math.sin(t) - 0.015, RI * math.cos(t))))
    for i in range(5):  # a clump where it hangs from, flat on the stone's underside
        leaf(top + ALONG_B * rng.uniform(-0.12, 0.12) + ACROSS_B * rng.uniform(-0.1, 0.1), -UP_B, ALONG_B * rng.uniform(-1, 1) + ACROSS_B * rng.uniform(-1, 1),
             rng.uniform(0.12, 0.16), vkit.leaf)
    n = int(length / 0.065)
    sway = rng.uniform(0, TAU)
    for i in range(n):
        p = top - UP_B * (i * 0.065) + (ALONG_B * math.sin(sway + i * 0.45) + ACROSS_B * math.cos(sway + i * 0.3)) * 0.03 * i ** 0.5
        for j in range(1 if i % 3 else 2):
            a = sway + i * 2.4 + j * math.pi
            out = ALONG_B * math.cos(a) + ACROSS_B * math.sin(a)
            leaf(p, out, -UP_B + out * 0.9, 0.16 - 0.06 * i / n, vkit.leaf, two=True)
        if i % 6 == 4 and k in (3, 5, 9):
            vkit.ball(island, Frame(p + ALONG_B * 0.04), 0.03, (0, 0, 0), vkit.petalC)

# The threshold: a long sill across the gate (in two, cracked) and worn flagstones either side, flush with the ground (the lane
# runs over it), joints showing the paving under them; stones of the old causeway lost down the lane, half in the sand.
def gpoly(a0, a1, c0, c1):
    return [(p[0], p[2]) for p in (gate_pt(a0, 0, c0), gate_pt(a1, 0, c0), gate_pt(a1, 0, c1), gate_pt(a0, 0, c1))]


def flush(dy=0.022):
    return lambda x, z: height(x, z) + dy


J2 = 0.022
slab(gpoly(-0.34 + J2, 0.34 - J2, -2.56, 0.28 - J2), flush(), flagB, bev=0.025)
slab(gpoly(-0.34 + J2, 0.34 - J2, 0.28 + 0.008, 2.56), flush(0.016), flagB, bev=0.02)
for a0, a1 in ((0.34, 0.86), (0.86, 1.42), (-0.86, -0.34), (-1.42, -0.86)):
    c = -2.56
    while c < 2.5:
        L = min(rng.uniform(0.45, 0.85), 2.56 - c)
        if 2.56 - c - L < 0.3:
            L = 2.56 - c
        sink = 0.012 if rng.random() < 0.15 else 0.0
        slab(gpoly(a0 + J2, a1 - J2, c + J2, c + L - J2), flush(0.022 - sink + rng.uniform(-0.004, 0.004)), rng.choice((flagA, flagB, flagC, stone)), bev=0.025)
        c += L
for i, r in enumerate((23.6, 24.4, 25.3, 26.0, 26.9, 27.6, 23.9, 25.7)):
    a = math.radians(lane_angle(GAPS[2], r))
    off = rng.choice((-1, 1)) * rng.uniform(1.0, 2.0)
    x, z = r * math.cos(a) - math.sin(a) * off, r * math.sin(a) + math.cos(a) * off
    w, d, yaw = rng.uniform(0.25, 0.35), rng.uniform(0.22, 0.3), rng.uniform(0, TAU)
    pts = [(x + math.cos(yaw) * u * w - math.sin(yaw) * v * d, z + math.sin(yaw) * u * w + math.cos(yaw) * v * d) for u, v in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    tilt = (rng.uniform(-0.05, 0.05), rng.uniform(-0.05, 0.05))
    slab(pts, lambda x2, z2, x=x, z=z, t=tilt: height(x, z) + 0.01 + t[0] * (x2 - x) + t[1] * (z2 - z), flagB if i % 2 else stone, bev=0.025)

# Moss and grass at the piers' feet.
for al, ac, s in ((-1.25, -3.6, 0.45), (-1.2, 3.3, 0.35), (1.25, -4.1, 0.3)):
    x, _, z = gate_pt(al, 0, ac)
    moss_on(x, z, height(x, z) + 0.005, s)
rng = _rng
for _ in range(280):  # what the old gate drew from the island's random numbers (sizes, places, a side for each of the
    rng.random()      # lost flagstones), drawn and dropped so the harbour's planks and buoys and the plants keep their places
for _ in range(8):
    rng.choice((-1, 1))
    for _ in range(4):
        rng.random()
for _ in range(8):
    rng.random()

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

# ---------- the plaza paving round the lighthouse: a ring of dark plinth stones at the tower's foot; a compass rose (cream points
# with slate halves, the north one red like the tower's stripes) inside a dark band with a brass ring; two courses of flagstones
# of all lengths in four tones, joints skewed, big stones across both (the lamps stand on three), some chipped or cracked, a
# marker with a brass N to the north, three iron drains; a kerb of worn stones, setts outside it in the grass and the paths; moss
# and grass in the joints. Each stone is cut from an exact tiling, pulled in half a joint, so the mortar (the ground, IslGrout)
# shows 3 cm down between them. The tops stay within a centimetre or so of 1.23: people and fish walk on it. Its own random
# numbers, so the rest of the island stays put. ----------
pr = random.Random(1908)
TOP = PLAZA_Y + 0.03
J = 0.022  # half a joint
PLANTERS = [math.radians(30 + k * 60) for k in range(6) if ang_diff(30 + k * 60, 270.0) >= 25]  # as placed above
LAMPS = [math.radians(a) for a in (60.0, 180.0, 300.0)]


def polar(r, a):
    return (r * math.cos(a), r * math.sin(a))


def arc(r, a0, a1, step=0.5):  # round the lighthouse at radius r from angle a0 to a1, points at most `step` apart
    n = max(1, math.ceil(abs(a1 - a0) * r / step))
    return [polar(r, a0 + (a1 - a0) * i / n) for i in range(n + 1)]


def ring_tile(r0, r1, a0, a1, s0=0.0, s1=0.0):  # between radii r0, r1 and the joints at a0, a1 (each turned on by s outside)
    return arc(r0, a0 - s0, a1 - s1) + arc(r1, a1 + s1, a0 + s0)


def under_planter(x, z, pad=0.0):  # on a planter's footprint (1.5 m by 0.9, its long side along (-cos p, sin p): Frame's yaw), pad round it
    for p in PLANTERS:
        dx, dz = x - 5.6 * math.cos(p), z - 5.6 * math.sin(p)
        if abs(-dx * math.cos(p) + dz * math.sin(p)) < 0.75 + pad and abs(dx * math.sin(p) + dz * math.cos(p)) < 0.45 + pad:
            return True
    return False


def corners(poly):  # the outline's real corners (where it turns more than 25 degrees)
    out = []
    for i in range(len(poly)):
        a = Vector(poly[i]) - Vector(poly[i - 1])
        b = Vector(poly[(i + 1) % len(poly)]) - Vector(poly[i])
        if a.length > 1e-6 and b.length > 1e-6 and a.angle(b) > 0.45:
            out.append(i)
    return out


def knock(poly, i, d):  # corner i knocked off, d along each of its edges
    p, a, b = Vector(poly[i]), Vector(poly[i - 1]) - Vector(poly[i]), Vector(poly[(i + 1) % len(poly)]) - Vector(poly[i])
    pa, pb = p + a.normalized() * min(d, a.length * 0.45), p + b.normalized() * min(d, b.length * 0.45)
    return poly[:i] + [tuple(pa), tuple(pb)] + poly[i + 1:]


def cracked(poly):  # the two pieces either side of a crack (with a kink in it) from the longest edge to the far side
    n = len(poly)
    i = max(range(n), key=lambda k: (Vector(poly[(k + 1) % n]) - Vector(poly[k])).length)
    A = Vector(poly[i]).lerp(Vector(poly[(i + 1) % n]), pr.uniform(0.3, 0.7))
    j = max((k for k in range(n) if k != i), key=lambda k: ((Vector(poly[k]) + Vector(poly[(k + 1) % n])) / 2 - A).length)
    B = Vector(poly[j]).lerp(Vector(poly[(j + 1) % n]), pr.uniform(0.3, 0.7))
    K = (A + B) / 2 + Vector((A.y - B.y, B.x - A.x)).normalized() * pr.uniform(-0.07, 0.07)
    one = [tuple(A)] + [poly[k % n] for k in range(i + 1, (j if j > i else j + n) + 1)] + [tuple(B), tuple(K)]
    two = [tuple(B)] + [poly[k % n] for k in range(j + 1, (i if i > j else i + n) + 1)] + [tuple(A), tuple(K)]
    return one, two


LAID = []  # (outline, how weathered 0..1) of every paving stone: where the moss and grass go after


def lay(tile, m, wear=0.5, bev=0.022, top=None, j=J):
    # One paving stone from its tile: pulled in half a joint, its top within a few mm of TOP and tipped a hair; now and then a
    # corner knocked off, or cracked across (the halves settled a little differently).
    poly = inset(tile, j)
    cs = corners(poly)
    if cs and pr.random() < 0.12 * wear:
        poly = knock(poly, pr.choice(cs), pr.uniform(0.07, 0.13))
    parts = [inset(q, 0.007) for q in cracked(poly)] if pr.random() < 0.08 * wear and abs(area(poly)) > 0.18 else [poly]
    for k, q in enumerate(parts):
        cx, cz = sum(x for x, _ in q) / len(q), sum(z for _, z in q) / len(q)
        reach = max(math.hypot(x - cx, z - cz) for x, z in q)  # tipped at most 6 mm at its far corner, whatever its size
        t0, dx, dz = TOP + pr.uniform(-0.004, 0.004) - 0.005 * k, pr.uniform(-0.0042, 0.0042) / reach, pr.uniform(-0.0042, 0.0042) / reach
        tf = top if top is not None else (lambda x, z, t0=t0, cx=cx, cz=cz, dx=dx, dz=dz: t0 + dx * (x - cx) + dz * (z - cz))
        slab(q, tf, m, bev=bev if len(parts) == 1 else 0.012)
        LAID.append((q, wear))


def pick(*weighted):  # a material from (material, weight) pairs
    r = pr.random() * sum(w for _, w in weighted)
    for m, w in weighted:
        r -= w
        if r <= 0:
            return m
    return weighted[-1][0]


FIELD = ((flagA, 0.38), (flagB, 0.3), (flagC, 0.17), (vkit.stone, 0.1), (rock, 0.05))
DARK = ((kerb, 0.6), (vkit.stone2, 0.4))
MID = ((flagB, 0.6), (vkit.stone, 0.4))

# The plinth ring at the lighthouse's foot (the tower's octagon reaches 1.08).
RIN = 1.75
off = pr.uniform(0, TAU)
for k in range(12):
    lay(ring_tile(1.1, RIN, off + k * TAU / 12, off + (k + 1) * TAU / 12), pick(*DARK), wear=0.8, bev=0.03)

# The compass rose: eight points (the four winds out to the band, the four between shorter), each in two halves either side of
# its axis; the stones between them split where two points meet; a brass ring round it in the joint to the band.
RC, RD, RV, BAND0, BAND1 = 3.72, 3.05, 2.25, 3.72, 4.0
for k in range(8):
    t, t2 = k * math.pi / 4, (k + 1) * math.pi / 4
    R, R2, v = (RC if k % 2 == 0 else RD), (RC if k % 2 else RD), t + math.pi / 8
    for sd in (-1, 1):
        w = t + sd * math.pi / 8
        lay([polar(RIN, t), polar(R, t), polar(RV, w)] + arc(RIN, w, t, 0.4)[:-1],
            (vkit.roofr if k == 2 else roseD) if sd < 0 else vkit.cream, wear=0.2, j=0.018)
    tip2 = [polar(R2, t2)] if R2 < BAND0 else []
    lay([polar(R, t)] + ([polar(BAND0, t)] if R < BAND0 else []) + arc(BAND0, t, v, 0.4)[1:] + [polar(RV, v)], pick(*MID), wear=0.4, j=0.018)
    lay([polar(RV, v)] + arc(BAND0, v, t2, 0.4) + tip2, pick(*MID), wear=0.4, j=0.018)
for k in range(16):
    a0, a1 = k * TAU / 16, (k + 1) * TAU / 16
    slab(arc(BAND0 + 0.016, a0, a1, 0.4) + arc(BAND0 - 0.016, a1, a0, 0.4), TOP - 0.003, vkit.brass, bev=0.0, base=PLAZA_Y - 0.01)
off = pr.uniform(0, TAU)
for k in range(24):
    lay(ring_tile(BAND0, BAND1, off + k * TAU / 24, off + (k + 1) * TAU / 24), pick(*DARK), wear=0.3, bev=0.02)

# The field: two courses (R0..RM, RM..R1) of stones of all lengths, joints skewed; big stones across both (one under each lamp);
# the north marker (a cream stone with a brass N, read from the lighthouse) in the inner course, three drains in the outer.
R0, RM, R1 = BAND1, 4.66, 5.35
windows = [(math.pi / 2, 0.31 / 4.33, "north")] + [(math.radians(a), 0.42 / 5.0, "drain") for a in (0.0, 120.0, 240.0)] + \
          [(a, 0.45 / RM, "big") for a in LAMPS]
for _ in range(80):
    if len(windows) == 11:
        break
    c, hw = pr.uniform(0, TAU), pr.uniform(0.38, 0.5) / RM
    if all(ang_diff(math.degrees(c), math.degrees(w[0])) > math.degrees(hw + w[1] + 0.45 / RM) for w in windows):
        windows.append((c, hw, "big"))
windows.sort()


def fill(r0, r1, a0, a1):  # a course's stones between two windows: joints at random lengths, each skewed a little
    rm = (r0 + r1) / 2
    cuts, a = [(a0, 0.0)], a0
    while True:
        a += pr.uniform(0.48, 1.05) / rm
        if a > a1 - 0.4 / rm:
            break
        cuts.append((a, pr.uniform(-0.035, 0.035) / rm))
    cuts.append((a1, 0.0))
    for (c0, s0), (c1, s1) in zip(cuts, cuts[1:]):
        lay(ring_tile(r0, r1, c0, c1, s0, s1), pick(*FIELD), wear=0.6 if r1 < R1 else 1.0)


for course, (r0, r1) in enumerate(((R0, RM), (RM, R1))):
    mine = [w for w in windows if w[2] == "big" or w[2] == ("north" if course == 0 else "drain")]
    for i, (c, hw, kind) in enumerate(mine):
        c1, hw1, _ = mine[(i + 1) % len(mine)]
        fill(r0, r1, c + hw, c1 - hw1 + (TAU if i == len(mine) - 1 else 0.0))
        tn = Vector((-math.sin(c), math.cos(c)))  # along the course (seen from the lighthouse, to the left)
        if kind == "big" and course == 0:
            lay(ring_tile(R0, R1, c - hw, c + hw), pick(*FIELD), wear=1.0)
        elif kind == "north":  # the N: up its left side, down across, up its right (the same either way up)
            lay(ring_tile(r0, r1, c - hw, c + hw), vkit.cream, wear=0.0, top=TOP)
            for (u0, v0), (u1, v1) in (((-0.14, 0.1), (0.14, 0.1)), ((0.14, 0.1), (-0.14, -0.1)), ((-0.14, -0.1), (0.14, -0.1))):
                p0, p1 = Vector(polar(4.33 + u0, c)) + tn * v0, Vector(polar(4.33 + u1, c)) + tn * v1
                n = (p1 - p0).normalized()
                w = Vector((-n.y, n.x)) * 0.022
                slab([tuple(p0 - n * 0.02 + w), tuple(p0 - n * 0.02 - w), tuple(p1 + n * 0.02 - w), tuple(p1 + n * 0.02 + w)], TOP + 0.004, vkit.brass, bev=0.0, base=TOP - 0.02)
        elif kind == "drain":  # a stone either side of an iron grate over a dark slot
            rc = (r0 + r1) / 2
            g = 0.17 / rc
            lay(ring_tile(r0, r1, c - hw, c - g), pick(*FIELD), wear=1.0)
            lay(ring_tile(r0, r1, c + g, c + hw), pick(*FIELD), wear=1.0)
            at = lambda u, v, rc=rc, c=c, tn=tn: tuple(Vector(polar(rc + u, c)) + tn * v)
            hu, hv = (r1 - r0) / 2 - J, 0.17 - J
            slab([at(-hu, -hv), at(hu, -hv), at(hu, hv), at(-hu, hv)], PLAZA_Y + 0.006, iron, bev=0.0, base=PLAZA_Y - 0.02)
            for u0, v0, u1, v1 in ((-hu, -hv, hu, -hv + 0.03), (-hu, hv - 0.03, hu, hv), (-hu, -hv, -hu + 0.03, hv), (hu - 0.03, -hv, hu, hv)) + \
                    tuple((-hu + 0.03, v - 0.012, hu - 0.03, v + 0.012) for v in (-0.075, -0.025, 0.025, 0.075)):
                slab([at(u0, v0), at(u1, v0), at(u1, v1), at(u0, v1)], TOP - 0.004, iron, bev=0.004, base=PLAZA_Y)

# The kerb: stones of all lengths, their outer corners worn round; setts outside it in the grass (some lost) and two rows across
# each path where it comes in.
K0, K1 = R1, 5.95
a = start = pr.uniform(0, TAU)
while a < start + TAU - 0.01:
    q0, a = a, min(a + pr.uniform(0.7, 1.15) / 5.65, start + TAU)
    if start + TAU - a < 0.5 / 5.65:
        a = start + TAU
    tile = ring_tile(K0, K1, q0, a)
    n0 = len(arc(K0, q0, a))
    lay(knock(knock(tile, len(tile) - 1, pr.uniform(0.04, 0.08)), n0, pr.uniform(0.04, 0.08)), pick(*DARK), wear=0.7, bev=0.032)
mouths = [math.radians(lane_angle(g, 6.2)) for g in GAPS + [DOCK]]


def in_mouth(a):
    return any(ang_diff(math.degrees(a), math.degrees(c)) < math.degrees(1.6 / 6.2) for c in mouths)


GAPPED = []  # where a sett is missing (grass grows there)
for r0, r1, mouth_only in ((K1 + 0.02, K1 + 0.3, False), (K1 + 0.33, K1 + 0.61, True)):
    a = start = pr.uniform(0, TAU)
    while a < start + TAU - 0.03:
        q0, a = a, min(a + pr.uniform(0.22, 0.32) / r0, start + TAU)
        mid = (q0 + a) / 2
        x, z = polar((r0 + r1) / 2, mid)
        if (mouth_only and not in_mouth(mid)) or under_planter(x, z, 0.08):
            continue
        if not in_mouth(mid) and pr.random() < 0.22:
            GAPPED.append((x, z))
            continue
        poly = inset(ring_tile(r0, r1, q0, a), 0.016)
        top = max(height(px, pz) for px, pz in poly) + 0.008
        slab(poly, top, pick((rock, 0.45), (kerb, 0.3), (vkit.stone2, 0.25)), bev=0.02, base=top - 0.1)


# Moss along some joints (more toward the grass, and at the tower's shady north foot); tufts of grass where joints meet and along
# the setts (drawn only: VkLeaf isn't solid, nothing trips on them).
def tuft(x, z, y, h=0.1):
    if under_planter(x, z, 0.05) or any(math.hypot(x - 4.6 * math.cos(l), z - 4.6 * math.sin(l)) < 0.32 for l in LAMPS):
        return
    for _ in range(pr.choice((3, 4))):
        a, lean, hh = pr.uniform(0, TAU), pr.uniform(0.25, 0.6), h * pr.uniform(0.6, 1.25)
        w = (math.cos(a + math.pi / 2) * 0.016, math.sin(a + math.pi / 2) * 0.016)
        card([G(x - w[0], y - 0.01, z - w[1]), G(x + w[0], y - 0.01, z + w[1]), G(x + math.cos(a) * hh * lean, y + hh, z + math.sin(a) * hh * lean)], vkit.leaf, two=True)


for poly, wear in LAID:
    cx, cz = sum(x for x, _ in poly) / len(poly), sum(z for _, z in poly) / len(poly)
    r = math.hypot(cx, cz)
    shade = 0.6 if r < 2.0 and cz > 0.3 else 0.0  # the north side of the tower's foot, out of the sun
    if pr.random() < 0.3 * wear + shade:
        i = pr.randrange(len(poly))
        p, q = Vector(poly[i]), Vector(poly[(i + 1) % len(poly)])
        if (q - p).length > 0.18 and abs(((p + q) / 2).length - BAND0) > 0.06 and not under_planter(*((p + q) / 2)):
            n = Vector(((q - p).y, -(q - p).x)).normalized() * (1 if area(poly) > 0 else -1)  # out of the stone
            u0, u1 = sorted((pr.uniform(0.0, 0.6), pr.uniform(0.4, 1.0)))
            a0, a1 = p.lerp(q, u0) + n * J, p.lerp(q, u1) + n * J
            m0, m1 = a0.lerp(a1, 0.3), a0.lerp(a1, 0.7)
            w0, w1 = n * pr.uniform(0.016, 0.028), n * pr.uniform(0.016, 0.028)  # a streak filling the joint, creeping onto the stones' edges
            slab([tuple(a0), tuple(m0 - w0), tuple(m1 - w1), tuple(a1), tuple(m1 + w1), tuple(m0 + w0)], PLAZA_Y + 0.02, moss, bev=0.005, base=PLAZA_Y - 0.01)
    if (r > 4.5 and pr.random() < 0.2 * wear) or (shade and pr.random() < 0.35):
        i = pr.choice(corners(poly) or [0])
        p = Vector(poly[i])
        out = (p - Vector((cx, cz))).normalized()
        tuft(p.x + out.x * J, p.y + out.y * J, PLAZA_Y)
for x, z in GAPPED[::2]:
    tuft(x, z, height(x, z), 0.12)
for k in range(24):  # in the grass just outside the setts
    x, z = polar(K1 + pr.uniform(0.38, 0.6), pr.uniform(0, TAU))
    if not in_mouth(math.atan2(z, x)):
        tuft(x, z, height(x, z), 0.13)

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


def under_gate_stone(x, z):  # where the gate's walls or a fallen voussoir now lie (dropped after, so the rest stay put)
    al, ac = gate_frame(x, z)
    return any(abs(al - slid) < 0.45 and abs(ac - side * uc) < L / 2 for side, uc, L, _, _, slid in WALL) or \
        any(math.hypot(al - a, ac - c) < 0.55 for a, c in ((1.2, 6.4), (2.5, 5.75), (1.6, 4.85), (1.8, -5.0)))


decor = [d for d in decor if not under_gate_stone(float(d.split()[1]), float(d.split()[3]))]
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
    shot("island_lane_eye", G(2.0, 2.9, -3.5), G(0.0, 0.9, -20.0), lens=20)  # a player at the plaza's edge looking down the dock road
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
