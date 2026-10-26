# The lighthouse plaza and the old gate from every side, from the exported OBJs (the island, the lighthouse where Siege stands
# it, the wreck), in EEVEE under an afternoon sun so bevels, chips and joints throw their shadows as they would in the game.
#   Blender -b --factory-startup --python preview/plaza-gate/render.py -- <out dir> <tag> [plaza|gate|all]
#   python3 preview/plaza-gate/sheet.py <out dir>      (before/after pages from <out dir>/before_* and after_*)
import math
import os
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

args = sys.argv[sys.argv.index("--") + 1:]
OUT, TAG = args[0], args[1]
ONLY = args[2] if len(args) > 2 else "all"
M = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "HtfFortress", "Models")
RES = (int(os.environ.get("W", 1280)), int(os.environ.get("H", 800)))
os.makedirs(OUT, exist_ok=True)


def G(x, y, z):  # game (x east, y up, z north) -> Blender
    return Vector((-x, -z, y))


sc = bpy.context.scene
for o in list(sc.objects):
    bpy.data.objects.remove(o, do_unlink=True)
for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
    try:
        sc.render.engine = eng
        break
    except TypeError:
        pass
sc.render.resolution_x, sc.render.resolution_y = RES
sc.view_settings.view_transform = "Standard"
try:
    sc.eevee.taa_render_samples = 48
except AttributeError:
    pass
world = bpy.data.worlds.new("W")
sc.world = world
world.use_nodes = True
bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
bg.inputs[0].default_value = (0.58, 0.74, 0.92, 1)
bg.inputs[1].default_value = 1.1
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
sun.data.energy = 3.6
sun.data.angle = math.radians(3)
sun.rotation_euler = G(0.55, 1.0, 0.75).to_track_quat("Z", "Y").to_euler()  # light from the south-west, high afternoon
sc.collection.objects.link(sun)

MATS = {}


def mtl(path):
    out, name = {}, None
    for line in open(path):
        q = line.split()
        if not q:
            continue
        if q[0] == "newmtl":
            name = q[1]
            out[name] = [(0.8, 0.8, 0.8), (0, 0, 0)]
        elif q[0] == "Kd":
            out[name][0] = tuple(float(x) for x in q[1:4])
        elif q[0] == "Ke":
            out[name][1] = tuple(float(x) for x in q[1:4])
    return out


def material(name, kd, ke):
    if name not in MATS:
        m = bpy.data.materials.new("P_" + name)
        m.use_nodes = True
        p = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        g = 0.299 * kd[0] + 0.587 * kd[1] + 0.114 * kd[2]  # as Models.SetMatColor paints it: 15% toward grey, 0.9 bright, sRGB -> linear
        p.inputs["Base Color"].default_value = (*[(((c * 0.85 + g * 0.15) * 0.9 + 0.055) / 1.055) ** 2.4 for c in kd], 1)
        m.use_backface_culling = True  # the game's shader draws front faces only
        p.inputs["Roughness"].default_value = 0.9
        for k in ("Specular IOR Level", "Specular"):
            if k in p.inputs:
                p.inputs[k].default_value = 0.15
        if any(ke):
            p.inputs["Emission Color"].default_value = (*ke, 1)
            p.inputs["Emission Strength"].default_value = 1.0
        MATS[name] = m
    return MATS[name]


def load(name, at=(0, 0, 0), turn=0.0):
    path = os.path.join(M, name + ".obj")
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=path, forward_axis="NEGATIVE_Z", up_axis="Y")
    ob = next(o for o in bpy.data.objects if o not in before)
    ob.data.transform(ob.matrix_basis)
    ob.matrix_basis.identity()
    cols = mtl(path[:-4] + ".mtl")
    for i, m in enumerate(ob.data.materials):
        key = m.name.split(".")[0]
        ob.data.materials[i] = material(key, *cols.get(key, ((0.8, 0.8, 0.8), (0, 0, 0))))
    for p in ob.data.polygons:
        p.use_smooth = False
    ob.location = G(*at)
    ob.rotation_euler = (0, 0, turn)
    return ob


island = load("FortressIsland")
bvh = BVHTree.FromObject(island, bpy.context.evaluated_depsgraph_get())


def ground(x, z):
    hit = bvh.ray_cast(G(x, 40, z), Vector((0, 0, -1)))
    return hit[0].z if hit[0] else 0.0


load("Core", (0, 1.2, 0), math.pi)  # Siege stands it on the plaza, its door (+z in the model) to the south
load("Wreck")
for name, x, z, yaw in (("Vault", -8, -17, 90), ("MarketStall", 5.5, -16.8, -90), ("Smithy", 6.2, -10.8, -90)):
    load(name, (x, ground(x, z), z), -math.radians(yaw))
bpy.ops.mesh.primitive_plane_add(size=400, location=(0, 0, -1.1))
sea = bpy.context.active_object
sea.data.materials.append(material("Sea", (0.16, 0.42, 0.66), (0, 0, 0)))

cd = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cd)
sc.collection.objects.link(cam)
sc.camera = cam


def shot(name, eye, look, lens=30.0, ortho=0.0):
    cd.type = "ORTHO" if ortho else "PERSP"
    cd.ortho_scale = ortho or 1.0
    cd.lens = lens
    cd.clip_start, cd.clip_end = 0.05, 400
    e, t = G(*eye), G(*look)
    cam.location = e
    cam.rotation_euler = (t - e).to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = os.path.join(OUT, f"{TAG}_{name}.png")
    bpy.ops.render.render(write_still=True)


# The plaza: the lighthouse at the middle, its door and the dock road to the south.
if ONLY in ("all", "plaza"):
    Y = 1.2
    shot("plaza_front", (0.0, Y + 5.0, -12.5), (0.0, Y + 0.6, -0.5), lens=28)      # from the dock road
    shot("plaza_back", (0.0, Y + 5.0, 12.5), (0.0, Y + 0.6, 0.5), lens=28)         # from the north lane
    shot("plaza_side", (12.5, Y + 5.0, 1.0), (0.5, Y + 0.6, 0.0), lens=28)         # from the east
    shot("plaza_high", (9.0, Y + 13.0, -10.5), (0.0, Y, 0.0), lens=26)             # high three-quarter
    shot("plaza_top", (0.0, Y + 40.0, -0.01), (0.0, Y, 0.0), ortho=13.2)           # straight down
    shot("plaza_eye", (1.3, Y + 1.7, -7.6), (0.0, Y + 0.3, -1.0), lens=24)         # a player walking up from the dock road
    shot("plaza_edge", (5.6, Y + 1.7, -7.4), (3.3, Y + 0.1, -3.6), lens=28)        # the kerb and joints at a player's eye
    shot("plaza_north", (-2.3, Y + 1.9, 6.3), (0.1, Y, 3.9), lens=30)             # the north marker, the rose's red point
    shot("plaza_drain", (3.0, Y + 1.5, -1.9), (5.1, Y, 0.1), lens=30)             # a drain, the kerb, setts and grass

# The old gate at 330 degrees, 21.5 m out: along the lane (seaward +) and across it.
if ONLY in ("all", "gate"):
    ge = math.radians(330.0)
    gx, gz = 21.5 * math.cos(ge), 21.5 * math.sin(ge)
    al, ac = (math.cos(ge), math.sin(ge)), (-math.sin(ge), math.cos(ge))
    gy = min(ground(gx + al[0] * d, gz + al[1] * d) for d in (-2.5, 2.5))  # on the lane either side (straight down at the gate is the arch)

    def F(along, up, across):
        return (gx + al[0] * along + ac[0] * across, gy + up, gz + al[1] * along + ac[1] * across)

    shot("gate_front", F(13.0, 3.2, 0.6), F(0.0, 3.0, 0.0), lens=28)              # from the beach, the sea at your back
    shot("gate_back", F(-12.0, 3.4, -0.6), F(0.0, 3.0, 0.0), lens=28)             # from the island side
    shot("gate_side", F(4.0, 3.0, 11.0), F(0.0, 2.6, 0.0), lens=26)               # from the north-north-east, the broken side
    shot("gate_side2", F(5.0, 3.0, -11.5), F(0.0, 2.6, 0.0), lens=26)             # from the south-south-west, the whole pier
    shot("gate_high", F(10.0, 10.0, -9.0), F(0.0, 1.5, 0.0), lens=26)             # high three-quarter
    shot("gate_eye", F(-7.5, 1.7, 0.4), F(0.0, 2.4, 0.0), lens=24)                # a player walking out to the beach
    shot("gate_eye_sea", F(7.0, 1.7, -0.6), F(0.0, 2.2, 0.0), lens=24)            # coming up the beach to it
    shot("gate_detail", F(4.6, 1.5, 7.6), F(1.2, 0.7, 4.4), lens=30)              # the broken side: what fell by the right pier
print("RENDERED", TAG, ONLY)
