# Turret preview sheets: whole turrets as the game stacks them (TurretBase, the body on the head pivot 0.85 m up, the branch
# head, the extra barrels Catalog.Barrels hangs beside it, the head grown by level), one column per look
# (Lv1, A, B at Lv3, A1 / A2 / B1 / B2 at Lv9), a row per OBJ folder and view. Mirrors BuildSystem.SetTurretLook.
#   Blender -b --factory-startup --python tools/turret_preview.py -- <out.png> <label>=<obj dir> [...] -- Gun Cannon ...
# A folder with <T>Body.obj is drawn modular (Body + <T>Default/A/...), one without it classic (<T>Head + BranchA...).
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
cut = args.index("--")
OUT, DIRS, NAMES = args[0], [a.split("=", 1) for a in args[1:cut]], args[cut + 1:]
RES = 300
HEAD_Z = 0.85
LOOKS = (("Lv1", 1, 0), ("A Lv3", 3, 1), ("B Lv3", 3, 2), ("A1 Lv9", 9, 3), ("A2 Lv9", 9, 4), ("B1 Lv9", 9, 5), ("B2 Lv9", 9, 6))
VIEWS = (("3/4", (-0.75, -0.9, 0.5)), ("front", (-0.03, -1, 0.18)))
# Catalog.Barrels at Lv1 / Lv3 (A, B) / Lv9 (A1, A2, B1, B2): shots or targets, with the Lv5 milestone at Lv9.
BARRELS = {
    "Gun": (1, 1, 1, 3, 2, 2, 2), "Cannon": (1, 3, 1, 6, 4, 2, 2), "Harpoon": (1, 3, 1, 6, 4, 2, 2),
    "Mortar": (1, 1, 3, 2, 2, 6, 5), "Tesla": (1, 1, 1, 2, 2, 2, 2), "Flame": (1, 1, 1, 2, 2, 2, 2),
    "Sniper": (1, 1, 1, 2, 2, 2, 2), "Bubble": (1,) * 7, "Mender": (1,) * 7,
}
# Catalog.BarrelMount (Unity head space: x, height, forward) for the modular set, and the classic set's old mounts.
MOUNT = {"Gun": (0.54, 0.4), "Cannon": (0.56, 0.3), "Harpoon": (0.83, 0.3), "Mortar": (0.55, 0.1), "Tesla": (1.12, 0.3),
         "Flame": (0.36, 0.2), "Sniper": (0.83, 0.4), "Bubble": (0.6, 0.3), "Mender": (0.6, 0.3)}
OLD_MOUNT = {"Gun": 0.46, "Cannon": 0.56, "Mortar": 0.58, "Tesla": 0.62, "Flame": 0.44, "Harpoon": 0.42, "Sniper": 0.38, "Bubble": 0.5, "Mender": 0.5}
BRANCH_MOUNT = {"Gun": ((0, 0.06, 0.58), 0.95), "Cannon": ((0, 0.1, 0.7), 1.0), "Harpoon": ((0, 0.08, 0.38), 0.85), "Mortar": ((0, 0.1, 0.9), 0.85),
                "Tesla": ((0, 0, 1.18), 0.72), "Flame": ((0, 0.08, 0.34), 0.9), "Sniper": ((0, 0.04, 0.33), 0.8), "Bubble": ((0, 0, 1.0), 0.85), "Mender": ((0, 0.04, 0.78), 0.85)}


def head_scale(level):  # BuildSystem.SetTurretLook
    return 1 + 0.08 * min(level - 1, 2) + 0.04 * min(max(0, level - 3), 12)


def module_name(t, branch):  # Catalog.ModuleModel / BranchOrnament
    if branch == 0:
        return "Default"
    return ("A" if branch in (1, 3, 4) else "B") + ("" if branch <= 2 else "1" if branch in (3, 5) else "2")


sc = bpy.context.scene
for o in list(sc.objects):
    bpy.data.objects.remove(o, do_unlink=True)
sc.render.engine = "BLENDER_WORKBENCH"
sh = sc.display.shading
sh.light, sh.studio_light, sh.color_type = "STUDIO", "paint.sl", "MATERIAL"
sh.show_backface_culling = False
sh.show_object_outline, sh.object_outline_color = True, (0.06, 0.07, 0.09)
sh.show_cavity, sh.cavity_type = True, "WORLD"
sc.render.film_transparent = True
sc.render.image_settings.file_format = "PNG"
sc.render.image_settings.color_mode = "RGBA"
sc.view_settings.view_transform = "Standard"
sc.render.resolution_x = sc.render.resolution_y = RES
cd = bpy.data.cameras.new("Cam")
cd.type = "ORTHO"
cd.clip_start, cd.clip_end = 0.01, 200
cam = bpy.data.objects.new("Cam", cd)
sc.collection.objects.link(cam)
sc.camera = cam


def mtl_colours(path):
    cols, name, kd = {}, None, None
    for line in open(path):
        q = line.split()
        if not q:
            continue
        if q[0] == "newmtl":
            name = q[1]
        elif q[0] == "Kd":
            kd = Vector([float(x) for x in q[1:4]])
            cols[name] = kd
        elif q[0] == "Ke" and any(float(x) for x in q[1:4]):
            cols[name] = kd.lerp(Vector([float(x) for x in q[1:4]]), 0.5)
    return cols


LOADED = {}


def load(folder, name):
    # The OBJ back in Blender's axes, coloured like the game; copies share the mesh.
    key = (folder, name)
    if key not in LOADED:
        path = os.path.join(folder, name + ".obj")
        if not os.path.exists(path):
            return None
        before = set(bpy.data.objects)
        bpy.ops.wm.obj_import(filepath=path, forward_axis="NEGATIVE_Z", up_axis="Y")
        ob = next(o for o in bpy.data.objects if o not in before)
        ob.data.transform(ob.matrix_basis)  # the importer leaves the axis change on the object; copies share only the mesh
        ob.matrix_basis.identity()
        cols = mtl_colours(path[:-4] + ".mtl")
        for m in ob.data.materials:
            c = cols.get(m.name.split(".")[0], Vector((0.8, 0.8, 0.8)))
            m.diffuse_color = (*(min(1.0, x) for x in c), 1.0)
        ob.hide_render = True
        LOADED[key] = ob
    src = LOADED[key]
    ob = bpy.data.objects.new(name, src.data)
    sc.collection.objects.link(ob)
    return ob


def place(ob, parent, unity_pos=(0, 0, 0), scale=1.0):
    # A Unity head-space position (x, up, forward) in Blender's axes under the head's empty.
    ob.parent = parent
    ob.location = (-unity_pos[0], -unity_pos[2], unity_pos[1])
    ob.scale = (scale, scale, scale)


def build(folder, t, level, branch):
    objs = []
    base = load(folder, "TurretBase")
    if base:
        objs.append(base)
    head = bpy.data.objects.new("Head", None)
    sc.collection.objects.link(head)
    head.location = (0, 0, HEAD_Z)
    head.scale = (head_scale(level),) * 3
    modular = os.path.exists(os.path.join(folder, t + "Body.obj"))
    n = BARRELS[t][[l for l, _, b in LOOKS].index(next(l for l, lv, b in LOOKS if lv == level and b == branch))]
    if modular:
        for name in (t + "Body", t + module_name(t, branch)):
            ob = load(folder, name)
            if ob:
                place(ob, head)
                objs.append(ob)
        y, z = MOUNT[t]
    else:
        ob = load(folder, t + "Head")
        if ob:
            place(ob, head)
            objs.append(ob)
        if branch > 0 and level >= 3:
            orn = load(folder, "Branch" + module_name(t, branch))
            if orn:
                at, sc_ = BRANCH_MOUNT[t]
                place(orn, head, at, sc_)
                objs.append(orn)
        y, z = -0.08, OLD_MOUNT[t]
    extra = n - 1
    for i in range(1, extra + 1):
        side = (i - (extra + 1) / 2) * (0.62 + (0.34 - 0.62) * min(1, max(0, (extra - 1) / 5)))
        bar = load(folder, t + "Barrel")
        if bar:
            place(bar, head, (side, y, z), 0.82 - 0.04 * i)
            objs.append(bar)
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
    cam.location = ctr + d * 50
    cd.ortho_scale = scale


tmp = os.path.join(os.path.dirname(OUT) or ".", "_tmp")
os.makedirs(tmp, exist_ok=True)
stats = []
rows = []
for label, folder in DIRS:
    for t in NAMES:
        if not (os.path.exists(os.path.join(folder, t + "Body.obj")) or os.path.exists(os.path.join(folder, t + "Head.obj"))):
            continue
        for v, d in VIEWS:
            rows.append((label, t, v))
        for look, level, branch in LOOKS:
            objs, head = build(folder, t, level, branch)
            pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
            top = max(p.z for p in pts)
            tris = sum(len(p.vertices) - 2 for o in objs for p in o.data.polygons)
            stats.append((label, t, look, round(top, 2), tris))
            for v, d in VIEWS:
                aim(pts, d, 4.0)  # one scale for every look, so sizes compare across the row (a turret's cell is 3 m)
                sc.render.filepath = os.path.join(tmp, f"{label}_{t}_{look.split()[0]}_{v.replace('/', '')}.png")
                bpy.ops.render.render(write_still=True)
            for o in objs:
                bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.objects.remove(head, do_unlink=True)

cell, pad = RES, 24
sheet = np.zeros((len(rows) * (cell + pad), len(LOOKS) * cell, 4), dtype=np.float32)
sheet[:, :] = (0.07, 0.09, 0.12, 1.0)
for r, (l, t, v) in enumerate(rows):
    for c, (look, _, _) in enumerate(LOOKS):
        img = bpy.data.images.load(os.path.join(tmp, f"{l}_{t}_{look.split()[0]}_{v.replace('/', '')}.png"))
        px = np.array(img.pixels[:], dtype=np.float32).reshape(RES, RES, 4)
        y0 = (len(rows) - 1 - r) * (cell + pad)
        a = px[:, :, 3:4]
        block = sheet[y0:y0 + cell, c * cell:(c + 1) * cell]
        block[:, :, :3] = px[:, :, :3] * a + block[:, :, :3] * (1 - a)
        bpy.data.images.remove(img)
out = bpy.data.images.new("Sheet", sheet.shape[1], sheet.shape[0], alpha=True)
out.pixels = sheet.ravel()
out.filepath_raw = OUT
out.file_format = "PNG"
out.save()
with open(OUT[:-4] + ".txt", "w") as f:
    f.write("rows top to bottom: " + ", ".join(f"{l} {t} {v}" for l, t, v in rows) + "; columns: " + ", ".join(l for l, _, _ in LOOKS) + "\n")
    for s in stats:
        f.write("%s %s %s top %.2f m, %d tris in view\n" % s)
print("SHEET", OUT, len(rows), "rows")
