# Renders for the Thunderstore pictures, from the mod's own models only (fortress_models.blend opened, never saved).
# SHOTS env: comma list of what to render (banner, hero, runner, lighthouse); OUT env: where.
import bpy, math, os, random
from mathutils import Vector

OUT = os.environ["OUT"]
SHOTS = os.environ.get("SHOTS", "banner,hero,runner,lighthouse").split(",")
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
sc = bpy.context.scene
os.makedirs(OUT, exist_ok=True)


def G(x, y, z):  # island-local game coordinates -> Blender (tools/make_island.py)
    return Vector((-x, -z, y))


for m in bpy.data.materials:  # the icons' colours (tools/make_icons.py)
    bsdf = m.node_tree and next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if not bsdf:
        continue
    kd = Vector(bsdf.inputs["Base Color"].default_value[:3])
    ke = Vector(bsdf.inputs["Emission Color"].default_value[:3]) * bsdf.inputs["Emission Strength"].default_value
    c = kd.lerp(ke, 0.5) if ke.length > 0 else kd
    m.diffuse_color = (*(min(1.0, x) for x in c), 1.0)

for o in sc.objects:
    o.hide_render = True

sc.render.engine = "BLENDER_WORKBENCH"
sh = sc.display.shading
sh.light, sh.studio_light, sh.color_type = "STUDIO", "paint.sl", "MATERIAL"
sh.show_backface_culling = False
sh.show_object_outline = True
sh.object_outline_color = (0.06, 0.07, 0.09)
sh.show_cavity = True
sh.cavity_type = "WORLD"
sc.render.film_transparent = True
sc.render.image_settings.file_format = "PNG"
sc.render.image_settings.color_mode = "RGBA"
sc.view_settings.view_transform = "Standard"
sc.view_settings.look = "None"

made = []


def inst(name, loc=(0, 0, 0), rot=(0, 0, 0), scale=1.0, parent=None, mats=None):
    src = bpy.data.objects[name]
    o = bpy.data.objects.new(name + "_x", src.data)
    sc.collection.objects.link(o)
    o.location, o.rotation_euler = Vector(loc), rot
    o.scale = (scale, scale, scale) if isinstance(scale, (int, float)) else scale
    if parent:
        o.parent = parent
    if mats:
        o.data = src.data.copy()
        for i, m in enumerate(o.data.materials):
            o.data.materials[i] = mats(m)
    made.append(o)
    return o


def empty(loc, yaw=0.0, scale=1.0, tilt=0.0):
    e = bpy.data.objects.new("E", None)
    sc.collection.objects.link(e)
    e.location, e.rotation_euler, e.scale = Vector(loc), (tilt, 0, yaw), (scale, scale, scale)
    made.append(e)
    return e


ANCHOR = {"Helmet": (0.27, 0.04, 1), "MinerHat": (0.27, 0.04, 1), "ShieldBoard": (0.33, 0.0, 1), "ThiefSack": (-0.12, 0.02, 1),
          "Shell": (0.0, 0.12, 1), "HealerStaff": (0.3, 0.02, 1.5)}
GEAR = {"Charger": "Helmet", "Tank": "Shell", "Spitter": "SpitterCannon", "Bomber": "Bomb", "Thief": "ThiefSack",
        "Shield": "ShieldBoard", "Digger": "MinerHat", "Healer": "HealerStaff", "Splitter": "SplitterBubbles"}


def top(ob, along):  # make_icons.foe_top
    vs = [v.co for v in ob.data.vertices]
    y0, y1 = min(v.y for v in vs), max(v.y for v in vs)
    y = (y0 + y1) / 2 - along * (y1 - y0)
    zs = [v.z for v in vs if abs(v.y - y) < 0.06 * (y1 - y0) and abs(v.x) > 0.02]
    return Vector((0, y, max(zs) if zs else max(v.z for v in vs)))


def fish(kind, loc, yaw, scale, tilt=0.0, crown=False):
    """A fish of ours with its gear, nose to -Y turned by yaw, its belly near loc."""
    body = bpy.data.objects["Foe" + kind]
    zmin = min(v.co.z for v in body.data.vertices)
    e = empty(loc, yaw, scale, tilt)
    inst("Foe" + kind, (0, 0, -zmin), parent=e)
    gear = GEAR.get(kind)
    dims = body.dimensions
    if gear:
        along, down, k = ANCHOR.get(gear, (0.0, 0.03, 1))
        size = max(dims.x, 0.3 * dims.y) * k
        at = top(body, along) - Vector((0, 0, down * dims.z)) + Vector((0, 0, -zmin))
        inst(gear, at, scale=size, parent=e)
    if crown:
        at = top(body, 0.27) + Vector((0, 0, -zmin + (0.4 * max(dims.x, 0.3 * dims.y) if gear == "Helmet" else 0)))
        inst("Crown", at, scale=max(dims.x, 0.3 * dims.y) * 0.7, parent=e)
    return e


def ground(x, z):  # the island's height under game (x, z) (the island itself: every source object sits at the origin too)
    hit, p, *_ = bpy.data.objects["FortressIsland"].ray_cast(G(x, 30, z), Vector((0, 0, -1)))
    return p.z if hit else 0.0


def zmin_of(name):
    return min(v.co.z for v in bpy.data.objects[name].data.vertices)


cd = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cd)
sc.collection.objects.link(cam)
sc.camera = cam
cd.clip_start, cd.clip_end = 0.05, 3000


def look(eye, at, lens=None, ortho=None):
    cam.location = eye
    cam.rotation_euler = (at - eye).to_track_quat("-Z", "Y").to_euler()
    cd.type = "ORTHO" if ortho else "PERSP"
    if ortho:
        cd.ortho_scale = ortho
    if lens:
        cd.lens = lens


def shot(name, w, h):
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("SHOT", name)


def clear():
    for o in made:
        bpy.data.objects.remove(o, do_unlink=True)
    made.clear()


def aim(objs, d, margin=1.12):  # make_icons.aim: an ortho view along d framing these objects
    pts = [o.matrix_world @ v.co for o in objs if o.type == "MESH" for v in o.data.vertices]
    d = Vector(d).normalized()
    q = d.to_track_quat("Z", "Y")
    right, up = q @ Vector((1, 0, 0)), q @ Vector((0, 1, 0))
    ctr = sum(pts, Vector()) / len(pts)
    xs = [(p - ctr).dot(right) for p in pts]
    ys = [(p - ctr).dot(up) for p in pts]
    ctr = ctr + right * (max(xs) + min(xs)) / 2 + up * (max(ys) + min(ys)) / 2
    cam.rotation_euler = q.to_euler()
    cam.location = ctr + d * 60
    cd.type = "ORTHO"
    cd.ortho_scale = max(max(xs) - min(xs), max(ys) - min(ys)) * margin


def gold(m):  # the OVER 99 lighthouse: every colour gilded
    g = m.copy()
    c = Vector(m.diffuse_color[:3])
    lum = 0.3 * c.x + 0.59 * c.y + 0.11 * c.z
    gc = Vector((1.0, 0.78, 0.25)) * (0.55 + 0.75 * lum)
    g.diffuse_color = (*(min(1.0, x) for x in gc), 1.0)
    return g


if "runner" in SHOTS:  # the Runner as the help panel would show it (it wears nothing)
    f = fish("Runner", (0, 0, 0), 0.0, 1.0)
    bpy.context.view_layer.update()
    aim([o for o in made if o.type == "MESH"], (-0.75, -1.0, 0.6), 1.14)
    shot("runner", 256, 256)
    clear()

if "hero" in SHOTS:  # single fish with their gear, big, for the icon and the posters
    for kind in ("Charger", "Bomber", "Tank", "Shield", "Thief", "Splitter", "Healer", "Digger", "Spitter", "Runner"):
        for tag, d in (("q", (-0.9, -1.0, 0.45)), ("f", (-0.35, -1.0, 0.25))):
            fish(kind, (0, 0, 0), 0.0, 1.0)
            bpy.context.view_layer.update()
            aim([o for o in made if o.type == "MESH"], d, 1.04)
            shot(f"hero_{kind}_{tag}", 768, 768)
            clear()

if "lighthouse" in SHOTS:
    for tag, mats in (("plain", None), ("gold", gold)):
        inst("Core", mats=mats)
        bpy.context.view_layer.update()
        aim(made, (-0.55, -1.0, 0.3), 1.04)
        shot(f"lighthouse_{tag}", 768, 768)
        clear()

def face(frm, to):  # the yaw that points a model's -Y (a fish's nose, a turret's barrel) from frm at to
    d = to - frm
    return math.atan2(d.x, -d.y)


if "banner" in SHOTS:
    CAMS = [tuple(map(float, c.split(","))) for c in os.environ.get("CAMS", "-14,9,30,0.5,2.5,5,30").split(";")]
    NOPLANT = [(c[0], c[2]) for c in CAMS]
    isl = inst("FortressIsland")
    from bpy_extras.object_utils import world_to_camera_view
    look(G(*CAMS[0][:3]), G(*CAMS[0][3:6]), lens=CAMS[0][6])
    cd.shift_y = CAMS[0][7] if len(CAMS[0]) > 7 else 0.0
    sc.render.resolution_x, sc.render.resolution_y = 1800, 720
    bpy.context.view_layer.update()

    def in_title(pt):  # the top left of the banner is the title's
        u = world_to_camera_view(sc, cam, pt)
        return u.x < 0.5 and u.y > 0.55
    # trees and bushes standing in for the game's own plants (as the island preview draws them); no grass
    import bmesh
    pm = {k: bpy.data.materials.new(k) for k in ("PvBush", "PvPine", "PvTrunk", "PvSea")}
    for k, c in (("PvBush", (0.3, 0.56, 0.26)), ("PvPine", (0.2, 0.46, 0.27)), ("PvTrunk", (0.42, 0.29, 0.17)), ("PvSea", (0.22, 0.5, 0.78))):
        pm[k].diffuse_color = (*c, 1.0)
    bm = bmesh.new()
    me = bpy.data.meshes.new("Plants")
    for k in ("PvBush", "PvPine", "PvTrunk"):
        me.materials.append(pm[k])

    def paint(geom, mi, loc, sq=1.0):
        for v in geom["verts"]:
            v.co.z *= sq
            v.co += loc
        for f in {f for v in geom["verts"] for f in v.link_faces}:
            f.material_index = mi

    for line in open(os.path.join(ROOT, "HtfFortress/Models/FortressIsland.decor.txt")):
        p = line.split()
        if not p or p[0].startswith("#") or p[0] in ("Pool", "GrassIsland2", "GrassIsland3"):
            continue
        k, x, y, z, s = p[0], float(p[1]), float(p[2]), float(p[3]), float(p[5])
        if any((x - cx) ** 2 + (z - cz) ** 2 < (256 if k == "PineTreeSmall" else 49) for cx, cz in NOPLANT):
            continue
        if k == "PineTreeSmall" and in_title(G(x, y + 3.6 * s, z)):
            continue
        if k == "PineTreeSmall":
            paint(bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.18 * s, radius2=0.0, depth=1.0 * s), 2, G(x, y + 0.5 * s, z))
            paint(bmesh.ops.create_cone(bm, cap_ends=True, segments=7, radius1=0.95 * s, radius2=0.0, depth=2.8 * s), 1, G(x, y + 2.3 * s, z))
        else:
            paint(bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.42 * s), 0, G(x, y + 0.12 * s, z), 0.65)
    bm.to_mesh(me)
    po = bpy.data.objects.new("Plants", me)
    sc.collection.objects.link(po)
    made.append(po)
    sea = bpy.data.meshes.new("Sea")
    bm2 = bmesh.new()
    bmesh.ops.create_grid(bm2, x_segments=1, y_segments=1, size=1500.0)
    bm2.to_mesh(sea)
    sea.materials.append(pm["PvSea"])
    so = bpy.data.objects.new("Sea", sea)
    so.location = (0, 0, -1.0)
    sc.collection.objects.link(so)
    made.append(so)

    def on_ground(name, x, z, rot=0.0, up=0.0):
        return inst(name, G(x, ground(x, z) + up, z) - Vector((0, 0, zmin_of(name))), rot=(0, 0, rot))

    # the lighthouse on the plaza, a wall with a metal gate across the north lane
    on_ground("Core", 0, 0)
    for c in (-4.5, -1.5, 1.5, 4.5):
        on_ground("WallMetal" if abs(c) < 2 else "WallStone", c, 9.0)
    fish_at = G(0, 1.5, 17)
    for tx, tz, head in ((-11.3, 6.5, "CannonHead"), (11.3, 6.5, "GunHead"), (-7.5, 7.5, "TeslaHead"), (7.5, 7.5, "HarpoonHead"),
                         (-4.0, 3.5, "MortarHead"), (4.0, 3.5, "SniperHead")):
        b = on_ground("TurretBase", tx, tz)
        hp = b.location + Vector((0, 0, 0.85))
        inst(head, hp, rot=(0, 0, face(hp, fish_at)))
    lh = G(0, 1.5, 0)
    # placed from the first camera: how far along its view and how far to its right (game metres on the ground)
    ex, _, ez, ax, _, az = CAMS[0][:6]
    f = Vector((ax - ex, az - ez)).normalized()
    r = Vector((f.y, -f.x))
    squad = (("Bomber", 11.6, -2.6, 2.6, False, 1), ("Charger", 12.6, 1.4, 2.6, False, 1),
             ("Shield", 15.0, -1.0, 2.4, False, 0), ("Runner", 14.6, -4.2, 2.4, True, 0), ("Tank", 16.0, -7.2, 2.8, False, 0),
             ("Digger", 13.4, -6.4, 2.4, False, 1), ("Thief", 17.6, -3.2, 2.3, False, 0), ("Healer", 19.0, -6.4, 2.3, False, 0),
             ("Splitter", 20.0, -4.6, 2.4, False, 0), ("Spitter", 22.0, -8.6, 2.4, False, 0))
    for kind, along, right, s, crown, hero in squad:
        g = Vector((ex, ez)) + f * along + r * right
        at = G(g.x, ground(g.x, g.y) + 0.04, g.y)
        # the two in front run to the right and a little toward us; the rest make for the lighthouse
        h = r * 0.85 - f * 0.5
        to = G(g.x + h.x, 0, g.y + h.y) if hero else lh
        fish(kind, at, face(at, Vector((to.x, to.y, at.z))), s, tilt=-0.06, crown=crown)
    bpy.context.view_layer.update()
    for i, (ex, ey, ez, ax, ay, az, lens, *shift) in enumerate(CAMS):
        look(G(ex, ey, ez), G(ax, ay, az), lens=lens)
        cd.shift_y = shift[0] if shift else 0.0
        shot(f"banner_{i}", int(os.environ.get("BW", 900)), int(os.environ.get("BH", 360)))
    clear()
print("DONE")
