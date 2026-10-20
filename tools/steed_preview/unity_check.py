# The Silver Steed put together the way the game does it, to check BossSteed.cs against the preview (look.py): the exported
# OBJs read like Models.Parse reads them (x flipped), joined like BossSteed.Dress joins them (the joints read out of
# BossSteed.cs), turned like BossSteed.LateUpdate turns them (Unity's axes and quaternions), shown in Blender (Unity
# (x, y, z) -> Blender (-x, -z, y)). The same poses should come out the same as look.py's.
#   OUT=<folder> Blender -b --factory-startup --python tools/steed_preview/unity_check.py   (env as look.py's)
import bpy, os, sys, re, math, json
from mathutils import Vector, Quaternion, Matrix
HERE = os.path.dirname(os.path.abspath(__file__))
REPO, OUT = os.environ.get("REPO") or os.path.dirname(os.path.dirname(HERE)), os.environ["OUT"]
MODELS = os.path.join(REPO, "HtfFortress/Models")
src = open(os.path.join(REPO, "HtfFortress/BossSteed.cs")).read()
def cs_vec(name):
    m = re.search(name + r" = new Vector3\(([-0-9.]+)f, ([-0-9.]+)f, ([-0-9.]+)f\)", src)
    return Vector(tuple(float(x) for x in m.groups()))
J = {n: cs_vec(n) for n in ("Neck", "Dock", "Wing", "Shoulder", "Knee", "Hip", "Hock")}
print("JOINTS", {k: tuple(v) for k, v in J.items()})

def parse(name):
    verts, faces, mats, cur, src_v = [], [], [], -1, []
    names = []
    for raw in open(os.path.join(MODELS, name + ".obj")):
        p = raw.split()
        if not p: continue
        if p[0] == "v": src_v.append(Vector((-float(p[1]), float(p[2]), float(p[3]))))
        elif p[0] == "usemtl":
            if p[1] not in names: names.append(p[1])
            cur = names.index(p[1])
        elif p[0] == "f":
            idx = [int(t.split("/")[0]) - 1 for t in p[1:]]
            for k in range(1, len(idx) - 1):
                b = len(verts)
                verts += [src_v[idx[0]], src_v[idx[k + 1]], src_v[idx[k]]]
                faces.append(((b, b + 1, b + 2), cur))
    kd = {}
    m = None
    for raw in open(os.path.join(MODELS, name + ".mtl")):
        p = raw.split()
        if not p: continue
        if p[0] == "newmtl": m = p[1]
        elif p[0] == "Kd": kd[m] = tuple(float(x) for x in p[1:4])
    return verts, faces, [kd.get(n, (0.5, 0.5, 0.5)) for n in names]

MESH = {}
def mesh(name, mirror=False):
    key = (name, mirror)
    if key not in MESH:
        v, f, c = parse(name)
        if mirror: v = [Vector((-p.x, p.y, p.z)) for p in v]
        MESH[key] = (v, f, c)
    return MESH[key]

def AA(deg, axis): return Quaternion(axis, math.radians(deg))
R, U, F = (1, 0, 0), (0, 1, 0), (0, 0, 1)
class Node:
    def __init__(self, model, parent=None, at=Vector(), mirror=False):
        self.model, self.parent, self.at, self.rot, self.pos, self.mirror = model, parent, Vector(at), Quaternion(), Vector(), mirror
    def world(self):
        local = Matrix.Translation(self.at + self.pos) @ self.rot.to_matrix().to_4x4()
        return (self.parent.world() if self.parent else Matrix.Identity(4)) @ local

def build():
    body = Node("BossSteedBody")
    n = dict(body=body, head=Node("BossSteedHead", body, J["Neck"]), tail=Node("BossSteedTail", body, J["Dock"]))
    for i, leg in enumerate(("FL", "FR", "HL", "HR")):
        side = Vector((1 if i % 2 == 0 else -1, 1, 1))
        up = Node("BossSteedForeArm" if i < 2 else "BossSteedThigh", body, (J["Shoulder"] if i < 2 else J["Hip"]) * side)
        n[leg] = up
        n[leg + "s"] = Node("BossSteedForeShin" if i < 2 else "BossSteedHindShin", up, J["Knee"] if i < 2 else J["Hock"])
    n["WL"] = Node("BossSteedWing", body, J["Wing"])
    n["WR"] = Node("BossSteedWing", body, J["Wing"] * Vector((-1, 1, 1)), mirror=True)
    return n

sys.path.insert(0, HERE)
import pose as PZ
def apply(n, P):
    # BossSteed.LateUpdate, in pose.py's numbers
    legs = [P["legs"][k] for k in ("FL", "FR", "HL", "HR")]
    n["body"].pos = Vector((0, P["bob"], 0)); n["body"].rot = AA(P["pitch"], R)
    n["head"].rot = AA(P["head"], R); n["tail"].rot = AA(P["tail"][0], R)
    for i, leg in enumerate(("FL", "FR", "HL", "HR")):
        n[leg].rot = AA(legs[i][0], R); n[leg + "s"].rot = AA(legs[i][1], R)
    r, s, t = P["wing"]
    w = AA(t, R) @ AA(-s, U) @ AA(r, F)
    n["WL"].rot = w
    n["WR"].rot = Quaternion((w.w, w.x, -w.y, -w.z))

def to_b(p): return Vector((-p.x, -p.z, p.y))
def show(n, rear=0.0):
    for o in [o for o in bpy.data.objects if o.name.startswith("U_")]: bpy.data.objects.remove(o, do_unlink=True)
    rootm = Matrix.Identity(4)
    if rear:  # the brain's rear: turned about the hind hooves, 2.9 m behind and 5.7 under the root (Unity: nose up = -angle about x)
        piv = Vector((0, -5.7, -2.9))
        rootm = Matrix.Translation(piv) @ AA(-rear, R).to_matrix().to_4x4() @ Matrix.Translation(-piv)
    for key, node in n.items():
        v, f, cols = mesh(node.model, node.mirror)
        M = rootm @ node.world()
        me = bpy.data.meshes.new("U_" + key)
        me.from_pydata([to_b(M @ p) for p in v], [], [tri for tri, _ in f])
        for i, c in enumerate(cols):
            mt = bpy.data.materials.new(f"U_{key}_{i}"); mt.diffuse_color = (*c, 1); me.materials.append(mt)
        for poly, (_, mi) in zip(me.polygons, f): poly.material_index = mi
        ob = bpy.data.objects.new("U_" + key, me); bpy.context.scene.collection.objects.link(ob)

sc = bpy.context.scene
for o in list(sc.objects):
    if o.type == "MESH": o.hide_render = True
sc.render.engine = "BLENDER_WORKBENCH"
sh = sc.display.shading
sh.light, sh.studio_light, sh.color_type = "STUDIO", "paint.sl", "MATERIAL"
sh.show_cavity, sh.cavity_type = True, "WORLD"
sc.render.film_transparent = True
sc.view_settings.view_transform = "Standard"
sc.render.resolution_x = sc.render.resolution_y = int(os.environ.get("RES", 400))
cd = bpy.data.cameras.new("Cam"); cd.type = "ORTHO"; cd.ortho_scale = float(os.environ.get("ORTHO", 19))
cam = bpy.data.objects.new("Cam", cd); sc.collection.objects.link(cam); sc.camera = cam
VIEWS = json.loads(os.environ.get("VIEWS", '{"side": [-1, 0, 0.05], "q": [-0.8, -0.9, 0.3], "bq": [-0.8, 0.9, 0.3]}'))
nodes = build()
for tag, name, t in json.loads(os.environ.get("POSES", '[["rest", "rest", 0]]')):
    P = PZ.pose(name, t)
    apply(nodes, P)
    show(nodes, P.get("rear", 0))
    for vn, d in VIEWS.items():
        dv = Vector(d).normalized()
        cam.rotation_euler = dv.to_track_quat("Z", "Y").to_euler()
        cam.location = Vector((0, 0, 0)) + dv * 60
        sc.render.filepath = os.path.join(OUT, f"{tag}_{vn}.png")
        bpy.ops.render.render(write_still=True)
print("RENDERED")
