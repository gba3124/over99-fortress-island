# The Eye at the End assembled from its exported parts (tools/make_boss_eye.py), the lids and the eyeball posed the way
# BossEye.cs poses them, rendered from the island and round it. Blender axes as the model script: front = -Y, up = Z.
#   M=HtfFortress/Models OUT=<folder> Blender -b --python tools/boss_eye_preview/render.py
# Env (JSON): STATES {name: [top lid, bottom lid, look at the camera 0/1, shard spin]}, VIEWS {name: toward the camera};
# ORTHO (metres across) and RES. Pictures land in OUT as <state>_<view>.png; sheet.py lays them out on one page.
import bpy, os, math, json
from mathutils import Vector, Euler, Matrix

M, OUT = os.environ["M"], os.environ["OUT"]
STATES = json.loads(os.environ.get("STATES", '{"closed": [0, 0, 0, 0], "open": [-58, 42, 0, 0], "stare": [-62, 46, 1, 0]}'))
VIEWS = json.loads(os.environ.get("VIEWS", '{"island": [0, -1, -0.45], "front": [0, -1, 0.04], "q": [-0.75, -0.85, 0.25], "side": [-1, 0, 0.12], "back": [0.45, 1, 0.55]}'))
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
parts = {}
for n in ("BossEyeShell", "BossEyeBall", "BossEyeLidTop", "BossEyeLidBottom", "BossEyeHalo", "BossEyeShards"):
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=os.path.join(M, n + ".obj"), forward_axis="NEGATIVE_Z", up_axis="Y")
    parts[n] = next(o for o in bpy.data.objects if o not in before)
    o = parts[n]; o.data.transform(o.matrix_world); o.matrix_world = Matrix.Identity(4)  # the importer's axis turn into the mesh: Blender axes again
for m in bpy.data.materials:  # the viewport colour as the game makes it: lit colour, half way to the glow for glowing parts
    b = m.node_tree and next((x for x in m.node_tree.nodes if x.type == "BSDF_PRINCIPLED"), None)
    if not b: continue
    kd = Vector(b.inputs["Base Color"].default_value[:3])
    ke = Vector(b.inputs["Emission Color"].default_value[:3]) * b.inputs["Emission Strength"].default_value
    c = kd.lerp(ke, 0.5) if ke.length > 0 else kd
    m.diffuse_color = (*c, 1.0)
sc.render.engine = "BLENDER_WORKBENCH"
sh = sc.display.shading
sh.light, sh.studio_light, sh.color_type = "STUDIO", "paint.sl", "MATERIAL"
sh.show_cavity, sh.cavity_type = True, "WORLD"
sc.render.film_transparent = True
sc.view_settings.view_transform = "Standard"
sc.render.resolution_x = sc.render.resolution_y = int(os.environ.get("RES", 420))
cd = bpy.data.cameras.new("Cam"); cd.type = "ORTHO"; cd.ortho_scale = float(os.environ.get("ORTHO", 15))
cam = bpy.data.objects.new("Cam", cd); sc.collection.objects.link(cam); sc.camera = cam
for st, (top, bottom, look, spin) in STATES.items():
    parts["BossEyeLidTop"].rotation_euler = (math.radians(top), 0, 0)
    parts["BossEyeLidBottom"].rotation_euler = (math.radians(bottom), 0, 0)
    parts["BossEyeShards"].rotation_mode = "ZYX"  # turned in its own plane, then tilted front-up (BossEye.cs)
    parts["BossEyeShards"].rotation_euler = (math.radians(-24), 0, math.radians(spin))
    for vn, d in VIEWS.items():
        dv = Vector(d).normalized()
        ball = parts["BossEyeBall"]
        if look:  # turn the eye toward the camera, at most BossEye.MaxLook off straight ahead
            f = Vector((0, -1, 0)); a = min(f.angle(dv), math.radians(32))
            axis = f.cross(dv)
            ball.rotation_mode = "QUATERNION"
            from mathutils import Quaternion
            ball.rotation_quaternion = Quaternion(axis.normalized(), a) if axis.length > 1e-6 else Quaternion()
        else:
            ball.rotation_mode = "QUATERNION"; ball.rotation_quaternion = (1, 0, 0, 0)
        cam.rotation_euler = dv.to_track_quat("Z", "Y").to_euler()
        cam.location = dv * 60
        sc.render.filepath = os.path.join(OUT, f"{st}_{vn}.png")
        bpy.ops.render.render(write_still=True)
print("RENDERED")
