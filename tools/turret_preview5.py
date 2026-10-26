# Turret preview sheets for art pass 5: whole turrets stacked as BuildSystem.SetTurretLook does (the static <T>Base, the
# <T>Kit2..8 a level adds, the <T>Dress of a final class, the head on its pivot with <T>Body + the class head + extra
# barrel pods, the ring of lamps), rendered in EEVEE on the reference sheets' warm grey.
#   Blender -b --factory-startup --python tools/turret_preview5.py -- <out.png> levels|classes|angles <T> [models dir]
# levels: Lv1..Lv9 on the A -> A1 path, then Lv10 and Lv15 (one 3/4 view); classes: Lv1, A, B, A1, A2, B1, B2 from the
# 3/4 front and the 3/4 back; angles: one look (env LOOK=level,branch) from eight directions round it.
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
OUT, MODE, TURRET = args[0], args[1], args[2]
HERE = os.path.dirname(os.path.abspath(__file__))
DIR = args[3] if len(args) > 3 else os.path.join(HERE, "..", "HtfFortress", "Models")
LAY = json.load(open(os.path.join(HERE, "layout5", TURRET + ".json")))
RES = int(os.environ.get("RES", "360"))
BG = (0.60, 0.57, 0.52)

# Catalog.Barrels at Lv1 / Lv3 A, B / Lv9 A1, A2, B1, B2 (the Lv9 ones include the Lv5 milestone); other levels add
# their milestones (Lv5 / 10 / 15) on top of the nearest of these.
BARRELS = {
    "Gun": (1, 1, 1, 3, 2, 2, 2), "Cannon": (1, 3, 1, 6, 4, 2, 2), "Harpoon": (1, 3, 1, 6, 4, 2, 2),
    "Mortar": (1, 1, 3, 2, 2, 6, 5), "Tesla": (1, 1, 1, 2, 2, 2, 2), "Flame": (1, 1, 1, 2, 2, 2, 2),
    "Sniper": (1, 1, 1, 2, 2, 2, 2), "Bubble": (1,) * 7, "Mender": (1,) * 7,
}
CLASS = {0: "Default", 1: "A", 2: "B", 3: "A1", 4: "A2", 5: "B1", 6: "B2"}


def milestones(level):
    return min(3, level // 5)


def barrels(t, level, branch):
    tab = BARRELS[t]
    if level < 3:
        return tab[0]
    if level < 9 or branch <= 2:
        return tab[1 if branch in (1, 3, 4) else 2] + milestones(level)
    return tab[branch] + milestones(level) - milestones(9)


def head_scale(level):  # BuildSystem.HeadScale
    return 1 + 0.025 * min(max(0, level - 1), 12)


def lamps_lit(level):
    return min(17, max(0, level - 3))


sc = bpy.context.scene
for o in list(sc.objects):
    bpy.data.objects.remove(o, do_unlink=True)
for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
    try:
        sc.render.engine = eng
        break
    except TypeError:
        pass
sc.render.resolution_x = sc.render.resolution_y = RES
sc.render.film_transparent = False
sc.view_settings.view_transform = "Standard"
sc.render.image_settings.file_format = "PNG"
try:
    sc.eevee.taa_render_samples = 32
    sc.eevee.use_shadows = True
except AttributeError:
    pass
world = bpy.data.worlds.new("W")
sc.world = world
world.use_nodes = True
bgn = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
bgn.inputs[0].default_value = (*BG, 1)
bgn.inputs[1].default_value = 0.6
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
sun.data.energy = 1.7
sun.data.angle = math.radians(12)
sun.rotation_euler = Vector((0.45, 1.0, -1.25)).to_track_quat("-Z", "Y").to_euler()  # from the front left, high: the sheets' key light
sc.collection.objects.link(sun)
fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "SUN"))
fill.data.energy = 0.35
fill.data.use_shadow = False
fill.rotation_euler = (math.radians(60), 0, math.radians(150))
sc.collection.objects.link(fill)
ground_mat = bpy.data.materials.new("Ground")
ground_mat.use_nodes = True
gb = next(n for n in ground_mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
gb.inputs["Base Color"].default_value = (*[c * 0.62 for c in BG], 1)
gb.inputs["Roughness"].default_value = 1.0
bpy.ops.mesh.primitive_plane_add(size=40)
ground = bpy.context.active_object
ground.data.materials.append(ground_mat)
cd = bpy.data.cameras.new("Cam")
cd.type = "ORTHO"
cd.clip_start, cd.clip_end = 0.01, 200
cam = bpy.data.objects.new("Cam", cd)
sc.collection.objects.link(cam)
sc.camera = cam


def mtl(path):
    out, name = {}, None
    for line in open(path):
        q = line.split()
        if not q:
            continue
        if q[0] == "newmtl":
            name = q[1]
            out[name] = [Vector((0.8, 0.8, 0.8)), Vector((0, 0, 0))]
        elif q[0] == "Kd":
            out[name][0] = Vector([float(x) for x in q[1:4]])
        elif q[0] == "Ke":
            out[name][1] = Vector([float(x) for x in q[1:4]])
    return out


MATS = {}


def material(name, kd, ke):
    if name not in MATS:
        m = bpy.data.materials.new("P_" + name)
        m.use_nodes = True
        p = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        p.inputs["Base Color"].default_value = (*kd, 1)
        p.inputs["Roughness"].default_value = 0.62
        if ke.length > 0:
            p.inputs["Emission Color"].default_value = (*ke, 1)
            p.inputs["Emission Strength"].default_value = 1.2
        MATS[name] = m
    return MATS[name]


LOADED = {}


def load(name):
    if name not in LOADED:
        path = os.path.join(DIR, name + ".obj")
        if not os.path.exists(path):
            print("MISSING", name)
            LOADED[name] = None
            return None
        before = set(bpy.data.objects)
        bpy.ops.wm.obj_import(filepath=path, forward_axis="NEGATIVE_Z", up_axis="Y")
        ob = next(o for o in bpy.data.objects if o not in before)
        ob.data.transform(ob.matrix_basis)
        ob.matrix_basis.identity()
        cols = mtl(path[:-4] + ".mtl")
        for i, m in enumerate(ob.data.materials):
            key = m.name.split(".")[0]
            kd, ke = cols.get(key, (Vector((0.8, 0.8, 0.8)), Vector((0, 0, 0))))
            ob.data.materials[i] = material(key, kd, ke)
        for p in ob.data.polygons:
            p.use_smooth = False
        ob.hide_render = True
        ob.hide_set(True)
        LOADED[name] = ob
    src = LOADED[name]
    if not src:
        return None
    ob = bpy.data.objects.new(name, src.data)
    sc.collection.objects.link(ob)
    return ob


LAMP_DIM = material("LampDim", Vector((0.09, 0.1, 0.12)), Vector((0, 0, 0)))
LAMP_LIT = material("LampLit", Vector((1.0, 0.72, 0.2)), Vector((1.0, 0.6, 0.15)))
LAMP_RAIL = material("LampRail", Vector((0.24, 0.26, 0.3)), Vector((0, 0, 0)))


def lamps(level):
    objs = []
    lit = lamps_lit(level)
    for i in range(17):
        s = (i + 1) // 2
        th = math.pi + (-1 if i % 2 == 1 else 1) * s * 2 * math.pi / 17
        x, fwd = math.sin(th) * 1.0, math.cos(th) * 1.0  # Unity x, z
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.1, depth=0.06, location=(-x, -fwd, 0.03))
        so = bpy.context.active_object
        so.data.materials.append(LAMP_RAIL)
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.085, location=(-x, -fwd, 0.14))
        bo = bpy.context.active_object
        bo.data.materials.append(LAMP_LIT if i < lit else LAMP_DIM)
        objs += [so, bo]
    return objs


def build(t, level, branch):
    objs = [load(t + "Base")]
    dressed = level >= 9 and branch >= 3
    for n in range(2, min(level, 8) + 1):
        if dressed and n in (4, 6):
            continue
        objs.append(load("%sKit%d" % (t, n)))
    if dressed:
        objs.append(load(t + "Dress" + CLASS[branch]))
    head = bpy.data.objects.new("Head", None)
    sc.collection.objects.link(head)
    head.location = (0, 0, LAY["head"])
    head.scale = (head_scale(level),) * 3
    parts = [load(t + "Body"), load(t + CLASS[branch if level >= 3 else 0])]
    pod, step = LAY.get("pod"), LAY.get("step", (0, 0.13, 0))
    if pod:  # BuildSystem.SetTurretLook: odd ones on the right, even on the left, each pair a step further (Catalog.BarrelStep)
        x, y, z = pod
        for i in range(1, barrels(t, level, branch)):
            side, k = (1 if i % 2 == 1 else -1), (i - 1) // 2
            ob = load(t + "Barrel")
            if ob:
                ob.location = (-side * (x + k * step[0]), -(z + k * step[2]), y + k * step[1])
                if LAY.get("mirror") and side < 0:
                    ob.scale = (-1, 1, 1)
                parts.append(ob)
    for ob in parts:
        if ob:
            ob.parent = head
    objs += [o for o in parts if o]
    objs = [o for o in objs if o]
    objs += lamps(level)
    bpy.context.view_layer.update()
    return objs, head


def aim(pts, d, scale):
    d = Vector(d).normalized()
    q = d.to_track_quat("Z", "Y")
    ctr = sum(pts, Vector()) / len(pts)
    right, up = q @ Vector((1, 0, 0)), q @ Vector((0, 1, 0))
    xs, ys = [(p - ctr).dot(right) for p in pts], [(p - ctr).dot(up) for p in pts]
    ctr += right * (max(xs) + min(xs)) / 2 + up * (max(ys) + min(ys)) / 2
    cam.rotation_euler = q.to_euler()
    cam.location = ctr + d * 40
    cd.ortho_scale = scale


V34 = (0.8, -0.95, 0.66)       # the sheets' view: from the front right, so the barrels point to the lower left
VBACK = (-0.85, 0.9, 0.62)
if MODE == "levels":
    LOOKS = [("Lv%d" % lv, lv, 0 if lv < 3 else 1 if lv < 9 else 3) for lv in range(1, 10)] + [("Lv10", 10, 3), ("Lv15", 15, 3)]
    VIEWS = [("3/4", V34)]
elif MODE == "levelsB":
    LOOKS = [("Lv%d" % lv, lv, 0 if lv < 3 else 2 if lv < 9 else 6) for lv in range(1, 10)] + [("Lv10", 10, 6), ("Lv15", 15, 6)]
    VIEWS = [("3/4", V34)]
elif MODE == "classes":
    LOOKS = [("Lv1", 1, 0), ("A", 3, 1), ("B", 3, 2), ("A1", 9, 3), ("A2", 9, 4), ("B1", 9, 5), ("B2", 9, 6)]
    VIEWS = [("3/4", V34), ("back", VBACK)]
else:
    lv, br = (int(x) for x in os.environ.get("LOOK", "9,3").split(","))
    LOOKS = [("Lv%d %s" % (lv, CLASS[br]), lv, br)]
    VIEWS = [("a%d" % k, (math.sin(k * math.pi / 4) * 1.0, -math.cos(k * math.pi / 4) * 1.0, 0.55)) for k in range(8)]

tmp = os.path.join(os.path.dirname(os.path.abspath(OUT)), "_tmp5")
os.makedirs(tmp, exist_ok=True)
SCALE = float(os.environ.get("SCALE", "3.2"))
stats = []
files = []
for label, level, branch in LOOKS:
    objs, head = build(TURRET, level, branch)
    pts = [o.matrix_world @ v.co for o in objs if o.type == "MESH" for v in o.data.vertices]
    tris = sum(len(p.vertices) - 2 for o in objs if o.name.split(".")[0].startswith(TURRET) for p in o.data.polygons)
    stats.append((label, round(max(p.z for p in pts), 2), tris))
    row = []
    for vname, d in VIEWS:
        aim(pts, d, SCALE)
        f = os.path.join(tmp, "%s_%s_%s_%s.png" % (TURRET, MODE, label.replace(" ", ""), vname.replace("/", "")))
        sc.render.filepath = f
        bpy.ops.render.render(write_still=True)
        row.append(f)
    files.append(row)
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.objects.remove(head, do_unlink=True)

# sheet: one column per look, one row per view (angles: one row of eight)
if MODE not in ("levels", "levelsB", "classes"):
    files = [[f] for f in files[0]]
cols, rows = len(files), len(files[0])
sheet = np.zeros((rows * RES, cols * RES, 4), dtype=np.float32)
for c, col in enumerate(files):
    for r, f in enumerate(col):
        img = bpy.data.images.load(f)
        px = np.array(img.pixels[:], dtype=np.float32).reshape(RES, RES, 4)
        y0 = (rows - 1 - r) * RES
        sheet[y0:y0 + RES, c * RES:(c + 1) * RES] = px
        bpy.data.images.remove(img)
out = bpy.data.images.new("Sheet", sheet.shape[1], sheet.shape[0], alpha=True)
out.pixels = sheet.ravel()
out.filepath_raw = OUT
out.file_format = "PNG"
out.save()
with open(OUT[:-4] + ".txt", "w") as fh:
    fh.write("columns: " + ", ".join(l for l, _, _ in LOOKS) + "; rows: " + ", ".join(v for v, _ in VIEWS) + "\n")
    for s in stats:
        fh.write("%s: top %.2f m, %d turret tris\n" % s)
print("SHEET", OUT)
