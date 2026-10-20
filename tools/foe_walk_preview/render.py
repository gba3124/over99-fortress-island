# Our bodies on the game's walker exactly as in game: the prefab's transforms (Dripper > DripFish node > DripFish mesh,
# ShoeL, ShoeR), the shoes posed by the game's own DripIdle / DripWalk keys, ours placed by FoeBodies.Fit, the root
# stretched by Enemies.ShapeOf. Unity root space -> Blender by swapping y/z (left- to right-handed).
import bpy, os, json
import numpy as np
from mathutils import Matrix, Vector

S, REF, MODELS, OUT = (os.environ[k] for k in ("SHOES", "REF", "MODELS", "OUT"))
clips = json.load(open(os.path.join(S, "clips.json")))
walk = clips["DripWalk"]


def q2m(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


RA, PA, REST = q2m((0, 0.7071068, 0, 0.7071068)), np.array([0, -0.04, -0.4]), (-0.7071068, 0, 0, 0.7071068)
F = np.diag([-1.0, 1.0, 1.0])  # the importer keeps the file's (X, Y, Z) in the mesh (its axis turn is on the object) -> Unity (-X, Y, Z)
SWAP = np.array([[1, 0, 0], [0, 0, 1], [0, 1, 0]], float)
SHAPE = {"Charger": (0.85, 0.8, 1.35), "Tank": (1.3, 1.05, 1.1), "Thief": (0.75, 1.1, 0.95), "Shield": (1.25, 1, 0.95),
         "Digger": (1.15, 0.75, 1.15), "Healer": (0.85, 1.3, 0.85), "Bomber": (1.15, 1.1, 1), "Splitter": (1.15, 0.9, 1.15)}


def L(m3, t=(0, 0, 0)):
    m = np.eye(4); m[:3, :3] = m3; m[:3, 3] = t
    return m


def unity_verts(path):
    return np.array([[-float(a), float(b), float(c)] for a, b, c in (l.split()[1:4] for l in open(path) if l.startswith("v "))])


bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
img = bpy.data.images.load(os.path.join(REF, "tex__Colors_Colors.png"))
pal = bpy.data.materials.new("Pal"); pal.use_nodes = True
nt = pal.node_tree; bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img; tex.interpolation = "Closest"
nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])


def load(path, palette=False):
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=path, forward_axis="NEGATIVE_Z", up_axis="Y")
    ob = next(o for o in bpy.data.objects if o not in before)
    u = unity_verts(path)
    got = np.array([F @ np.array(v.co) for v in ob.data.vertices])
    assert np.allclose(np.sort(got, 0), np.sort(u, 0), atol=1e-4), path  # the importer's axes are the ones assumed
    if palette:
        ob.data.materials.clear(); ob.data.materials.append(pal)
    ob.hide_render = True
    return ob, u


game, game_u = load(os.path.join(REF, "DripFish.obj"), True)
shoes = [load(os.path.join(REF, n + ".obj"), True)[0] for n in ("ShoeL", "ShoeR")]
for m in bpy.data.materials:
    b2 = m.node_tree and next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if b2 and m != pal:
        m.diffuse_color = (*b2.inputs["Base Color"].default_value[:3], 1.0)


def snap(v):
    a = np.abs(v); i = int(np.argmax(a)); out = np.zeros(3); out[i] = np.sign(v[i]); return out


def fit(ours_u, shape):  # FoeBodies.Fit, node space
    n2r = RA @ q2m(REST)
    fwd, up = snap(n2r.T @ [0, 0, 1]), snap(n2r.T @ [0, 1, 0])
    right = np.cross(up, fwd)
    gmin, gmax, omin, omax = game_u.min(0), game_u.max(0), ours_u.min(0), ours_u.max(0)
    length = abs((gmax - gmin) @ fwd)
    k = length / max(0.01, (omax - omin)[2])
    scale = np.array([k * shape[2] / shape[0], k * shape[2] / shape[1], k])
    c = (omin + omax) / 2 * scale
    pos = (gmin + gmax) / 2 - (right * c[0] + up * c[1] + fwd * c[2]) + up * (0.03 * length)
    return L(np.column_stack([right, up, fwd]) @ np.diag(scale), pos)


def placed(src, m4):
    ob = src.copy(); ob.data = src.data.copy(); sc.collection.objects.link(ob)
    ob.data.transform(Matrix(m4.tolist())); ob.matrix_world = Matrix.Identity(4); ob.hide_render = False
    return ob


def pose(t):  # rotations of DripFish / ShoeL / ShoeR at key t of the walk, or the idle (= rest) pose
    if t is None:
        return REST, REST, REST
    i = min(range(len(walk["DripFish"])), key=lambda j: abs(walk["DripFish"][j][0] - t))
    return walk["DripFish"][i][1], walk["ShoeL"][i][1], walk["ShoeR"][i][1]


sc.render.engine = "BLENDER_WORKBENCH"
sh = sc.display.shading
sh.light, sh.studio_light, sh.color_type = "STUDIO", "paint.sl", "TEXTURE"
sh.show_object_outline, sh.show_cavity, sh.cavity_type = False, True, "WORLD"
sc.render.film_transparent = True
sc.view_settings.view_transform = "Standard"
sc.render.resolution_x = sc.render.resolution_y = 360
cd = bpy.data.cameras.new("Cam"); cd.type = "ORTHO"; cd.ortho_scale = 1.75
cam = bpy.data.objects.new("Cam", cd); sc.collection.objects.link(cam); sc.camera = cam
VIEWS = {"side": (1, 0, 0.1), "q": (0.75, 0.9, 0.55)}


def shot(name, kind, t, view):
    shape = SHAPE.get(kind, (1, 1, 1))
    root = L(SWAP) @ L(np.diag(shape)) @ L(RA, PA)
    qf, ql, qr = pose(t)
    made = [placed(s, root @ L(q2m(q)) @ L(F)) for s, q in zip(shoes, (ql, qr))]
    if kind == "Game":
        made.append(placed(game, root @ L(q2m(qf)) @ L(F)))
    else:
        path = os.path.join(MODELS, f"Foe{kind}.obj")
        src, u = load(path)
        made.append(placed(src, root @ L(q2m(qf)) @ fit(u, shape) @ L(F)))
        bpy.data.objects.remove(src)
    for m in bpy.data.materials:
        b2 = m.node_tree and next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if b2 and m != pal:
            m.diffuse_color = (*b2.inputs["Base Color"].default_value[:3], 1.0)
    dv = Vector(VIEWS[view]).normalized()
    centre = Vector((0, -0.45, -0.2))
    cam.rotation_euler = dv.to_track_quat("Z", "Y").to_euler()
    cam.location = centre + dv * 10
    sc.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    for o in made:
        bpy.data.objects.remove(o)


KINDS = ["Runner", "Charger", "Spitter", "Bomber", "Tank", "Thief", "Shield", "Digger", "Healer", "Splitter"]
for kind in ("Game", "Runner"):
    for tag, t in (("air", None), ("w0", 0.0), ("w1", 0.083), ("w2", 0.167)):
        shot(f"pose_{kind}_{tag}", kind, t, "side")
for kind in ["Game"] + KINDS:
    shot(f"walk_{kind}_q", kind, 0.083, "q")
    shot(f"walk_{kind}_side", kind, 0.083, "side")
print("RENDERED")
