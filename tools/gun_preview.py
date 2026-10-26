# Weapon preview sheets: OBJs imported back into Blender's axes and rendered in the icon look from five views, then put on
# one sheet per weapon (a row per OBJ folder: before / after), with each model's size, triangles and materials under it.
#   Blender -b --factory-startup --python tools/gun_preview.py -- <out.png> <label>=<obj dir> [<label>=<obj dir> ...] -- Name ...
# Views (Blender axes, -Y the front): side (-X, what the icon shows), front 3/4, front, top, and "fp": over the shoulder from
# behind and inside the hand (Unity -X, so Blender +X: Models.Parse flips x), how the holder sees it.
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
VIEWS = (("side", (-1, -0.02, 0.12)), ("3/4", (-0.8, -0.9, 0.45)), ("front", (-0.05, -1, 0.12)), ("top", (0.0, -0.05, 1)), ("fp", (0.45, 1, 0.45)))

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
    # The game's colour of each material: Kd, halfway to Ke when it glows (as make_icons.py / Models.Parse show it).
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


def load(path):
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=path, forward_axis="NEGATIVE_Z", up_axis="Y")
    ob = next(o for o in bpy.data.objects if o not in before)
    cols = mtl_colours(path[:-4] + ".mtl")
    for m in ob.data.materials:
        c = cols.get(m.name.split(".")[0], Vector((0.8, 0.8, 0.8)))
        m.diffuse_color = (*(min(1.0, x) for x in c), 1.0)
    return ob


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
for label, folder in DIRS:
    for name in NAMES:
        path = os.path.join(folder, name + ".obj")
        if not os.path.exists(path):
            continue
        ob = load(path)
        pts = [ob.matrix_world @ v.co for v in ob.data.vertices]
        size = max(ob.dimensions) * 1.12  # one scale for every view of a model, so the views compare
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        dims = tuple(round(max(p[i] for p in pts) - min(p[i] for p in pts), 3) for i in range(3))  # Blender axes: x across, y length, z height
        stats.append((label, name, dims, tris, len(ob.data.materials)))
        for v, d in VIEWS:
            aim(pts, d, size)
            sc.render.filepath = os.path.join(tmp, f"{label}_{name}_{v.replace('/', '')}.png")
            bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(ob, do_unlink=True)

# ---------- the sheet: a row per (label, model), the HUD's dark panel behind ----------
rows = [(l, n) for l, _ in DIRS for n in NAMES if os.path.exists(os.path.join(tmp, f"{l}_{n}_side.png"))]
cell, pad = RES, 26
sheet = np.zeros((len(rows) * (cell + pad), len(VIEWS) * cell, 4), dtype=np.float32)
sheet[:, :] = (0.07, 0.09, 0.12, 1.0)
for r, (l, n) in enumerate(rows):
    for c, (v, _) in enumerate(VIEWS):
        img = bpy.data.images.load(os.path.join(tmp, f"{l}_{n}_{v.replace('/', '')}.png"))
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
    f.write("rows top to bottom; views: " + ", ".join(v for v, _ in VIEWS) + "\n")
    for s in stats:
        f.write("%s %s dims %s tris %d mats %d\n" % s)
print("SHEET", OUT, rows)
