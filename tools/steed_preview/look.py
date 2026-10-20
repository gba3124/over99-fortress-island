# The Silver Steed's preview, straight from its builder: tools/make_boss_steed.py built in memory (nothing saved), the parts
# put on their joints, posed like BossSteed.cs poses them (pose.py) and rendered from each view.
#   OUT=<folder> Blender -b HtfFortress/Models/fortress_models.blend --python tools/steed_preview/look.py
# env: POSES '[[tag, pose, t], ...]' (rest, gallop, rear, leap; t: how far through it), VIEWS '{name: [x, y, z]}' (where the
# camera is, Blender axes: +X the horse's left, -Y its front), CENTRE, ORTHO, RES. Images: OUT/<tag>_<view>.png, put on one
# sheet with tools/boss_eye_preview/sheet.py.
import bpy, os, sys, math, runpy, json
from mathutils import Vector as V, Matrix, Quaternion
HERE = os.path.dirname(os.path.abspath(__file__))
REPO, OUT = os.environ.get("REPO") or os.path.dirname(os.path.dirname(HERE)), os.environ["OUT"]
sys.argv = [sys.argv[0], "--", "nosave"]
g = runpy.run_path(os.path.join(REPO, "tools/make_boss_steed.py"))
sys.path.insert(0, HERE)
import pose as PZ

objs = {o.name: o for o in bpy.data.objects}
def put(name, at, parent=None, mirror=False, newname=None):
    o = objs[name] if newname is None else objs[name].copy()
    if newname: o.name = newname; bpy.context.scene.collection.objects.link(o)
    o.parent, o.location, o.rotation_mode = parent, at, "QUATERNION"
    if mirror: o.scale.x = -1
    return o
body = put("BossSteedBody", V())
head = put("BossSteedHead", g["NECK"], body)
tail = put("BossSteedTail", g["DOCK"], body)
legs = {}
for sx, tag in ((1, "L"), (-1, "R")):
    legs["F" + tag] = fa = put("BossSteedForeArm", g["SHOULDER"] + V((sx * g["FORE_X"], 0, 0)), body, newname="FA" + tag)
    legs["F" + tag + "s"] = put("BossSteedForeShin", g["KNEE"] - g["SHOULDER"], fa, newname="FS" + tag)
    legs["H" + tag] = th = put("BossSteedThigh", g["HIP"] + V((sx * g["HIND_X"], 0, 0)), body, newname="TH" + tag)
    legs["H" + tag + "s"] = put("BossSteedHindShin", g["HOCK"] - g["HIP"], th, newname="HS" + tag)
wl = put("BossSteedWing", g["WING"], body, newname="WL")
wr = put("BossSteedWing", g["WING"] * V((-1, 1, 1)), body, mirror=True, newname="WR")  # (its turn mirrored too, below)
for n in ("BossSteedForeArm", "BossSteedForeShin", "BossSteedThigh", "BossSteedHindShin", "BossSteedWing"):
    objs[n].hide_render = True
rig = bpy.data.objects.new("Rig", None)
bpy.context.scene.collection.objects.link(rig)
body.parent = rig

# the game's colours: the material's colour, halfway to its glow if it has one
for m in bpy.data.materials:
    b = m.node_tree and next((x for x in m.node_tree.nodes if x.type == "BSDF_PRINCIPLED"), None)
    if not b: continue
    kd = V(b.inputs["Base Color"].default_value[:3]); ke = V(b.inputs["Emission Color"].default_value[:3]) * b.inputs["Emission Strength"].default_value
    m.diffuse_color = (*(kd.lerp(ke, 0.5) if ke.length > 0 else kd), 1.0)
sc = bpy.context.scene
for o in list(sc.objects):
    if o.type == "MESH" and not o.name.startswith(("BossSteed", "FA", "FS", "TH", "HS", "WL", "WR")): o.hide_render = True
sc.render.engine = "BLENDER_WORKBENCH"
sh = sc.display.shading
sh.light, sh.studio_light, sh.color_type = "STUDIO", "paint.sl", "MATERIAL"
sh.show_cavity, sh.cavity_type = True, "WORLD"
sc.render.film_transparent = True
sc.view_settings.view_transform = "Standard"
sc.render.resolution_x = sc.render.resolution_y = int(os.environ.get("RES", 420))
cd = bpy.data.cameras.new("Cam"); cd.type = "ORTHO"; cd.ortho_scale = float(os.environ.get("ORTHO", 19))
cam = bpy.data.objects.new("Cam", cd); sc.collection.objects.link(cam); sc.camera = cam
VIEWS = json.loads(os.environ.get("VIEWS", '{"side": [-1, 0, 0.05], "q": [-0.8, -0.9, 0.3], "low": [-0.6, -1, -0.12], "bq": [-0.8, 0.9, 0.3]}'))

def q(axis, deg): return Quaternion(axis, math.radians(deg))
def apply(P):
    rig.matrix_world = Matrix.Identity(4)
    if P.get("rear"):  # the brain's rear: the whole horse turned nose-up about its hind hooves
        piv = V((0, 2.9, g["GROUND"]))
        rig.matrix_world = Matrix.Translation(piv) @ Matrix.Rotation(math.radians(-P["rear"]), 4, "X") @ Matrix.Translation(-piv)
    body.location = V((0, 0, P["bob"])); body.rotation_quaternion = q((1, 0, 0), P["pitch"])
    head.rotation_quaternion = q((1, 0, 0), P["head"])
    tail.rotation_quaternion = q((0, 0, 1), P["tail"][1]) @ q((1, 0, 0), P["tail"][0])
    for k, (a, b) in P["legs"].items():
        legs[k].rotation_quaternion = q((1, 0, 0), a); legs[k + "s"].rotation_quaternion = q((1, 0, 0), b)
    r, s, t = P["wing"]
    w = q((1, 0, 0), t) @ q((0, 0, 1), s) @ q((0, 1, 0), r)
    wl.rotation_quaternion = w
    wr.rotation_quaternion = Quaternion((w.w, w.x, -w.y, -w.z))  # the left wing's turn in a mirror

centre = V(json.loads(os.environ.get("CENTRE", "[0, 0, 0]")))
for tag, name, t in json.loads(os.environ.get("POSES", '[["rest", "rest", 0]]')):
    apply(PZ.pose(name, t))
    bpy.context.view_layer.update()
    for vn, d in VIEWS.items():
        dv = V(d).normalized()
        cam.rotation_euler = dv.to_track_quat("Z", "Y").to_euler()
        cam.location = centre + dv * 60
        sc.render.filepath = os.path.join(OUT, f"{tag}_{vn}.png")
        bpy.ops.render.render(write_still=True)
print("RENDERED")
