# The helpers with nothing to do (Helpers.Rest), from the exported OBJs, placed as FortressIsland.cs / Helpers.cs / Person.cs
# place them: a coffee ring at each rest spot (the mod's own helper models standing in for the game's villager bodies, a mug
# in each right hand the way PoseModelled holds it), and a seagull sitting in each nest (the lighthouse's gallery, the pier's
# pile caps, the smithy's roof). Each subject from the island and from its front, side and back.
#   M=HtfFortress/Models OUT=<folder> Blender -b --python tools/helper_idle_preview.py
#   python3 tools/boss_eye_preview/sheet.py <OUT> preview/helper-idle/sheet.png '<rows>' '<views>'   (README in that folder)
# Numbers copied from the C# (keep in step): FortressIsland.RestSpots / NestSpots / SmithyRoofTilt, Helpers.CircleSlot /
# NestSeat / GullBelly / MugColors, Person.CupGrip / CupScale.
import bpy, os, math
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

M, OUT = os.environ["M"], os.environ["OUT"]
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene

# The game's frame (x right, y up, z ahead; the OBJs as Models.Parse loads them, x flipped) into Blender's (-x, -z, y).
C = Matrix(((-1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
CI = C.inverted()
def rx(d): a = math.radians(d); c, s = math.cos(a), math.sin(a); return Matrix(((1, 0, 0, 0), (0, c, -s, 0), (0, s, c, 0), (0, 0, 0, 1)))
def ry(d): a = math.radians(d); c, s = math.cos(a), math.sin(a); return Matrix(((c, 0, s, 0), (0, 1, 0, 0), (-s, 0, c, 0), (0, 0, 0, 1)))
def rz(d): a = math.radians(d); c, s = math.cos(a), math.sin(a); return Matrix(((c, -s, 0, 0), (s, c, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1)))
def euler(x, y, z): return ry(y) @ rx(x) @ rz(z)  # Quaternion.Euler: z, then x, then y
def T(p): return Matrix.Translation(Vector(p))
def S(s): return Matrix.Diagonal((s, s, s, 1))

meshes = {}
def place(name, g, color=None):
    """An instance of Models/<name>.obj at the game-frame transform g; `color` repaints its first material (Models.Recolor)."""
    if name not in meshes:
        before = set(bpy.data.objects)
        bpy.ops.wm.obj_import(filepath=os.path.join(M, name + ".obj"), forward_axis="NEGATIVE_Z", up_axis="Y")
        o = next(o for o in bpy.data.objects if o not in before)
        o.data.transform(o.matrix_world)  # Blender axes, the model's own origin
        for m in o.data.materials:  # the viewport colour as the game makes it: lit colour, half way to the glow for glowing parts
            b = m and m.node_tree and next((x for x in m.node_tree.nodes if x.type == "BSDF_PRINCIPLED"), None)
            if not b: continue
            kd = Vector(b.inputs["Base Color"].default_value[:3])
            ke = Vector(b.inputs["Emission Color"].default_value[:3]) * b.inputs["Emission Strength"].default_value
            m.diffuse_color = (*(kd.lerp(ke, 0.5) if ke.length > 0 else kd), 1)
        meshes[name] = o.data
        bpy.data.objects.remove(o)
    data = meshes[name]
    if color:
        data = data.copy()
        m = data.materials[0].copy(); m.diffuse_color = (*color, 1); data.materials[0] = m
    o = bpy.data.objects.new(name, data)
    sc.collection.objects.link(o)
    o.matrix_world = C @ g @ CI
    return o

island = place("FortressIsland", Matrix.Identity(4))
bvh = BVHTree.FromObject(island, bpy.context.evaluated_depsgraph_get())
def ground(x, z):
    hit = bvh.ray_cast(Vector((-x, -z, 60)), Vector((0, 0, -1)))
    return hit[0].z if hit[0] else 0.0

stations = {}
for name, x, z, yaw in (("Vault", -8, -17, 90), ("MarketStall", 5.5, -16.8, -90), ("Smithy", 6.2, -10.8, -90)):
    stations[name] = T((x, ground(x, z), z)) @ ry(yaw)
    place(name, stations[name])
lighthouse = Vector((0, ground(0, 0), 0))
place("Core", T(lighthouse) @ ry(180))  # Siege: its door (+z in the model) to the plaza

# ---------- the nests, a gull sitting in each ----------
NESTS = [("Lighthouse", (0.5, 4.36, 0.866), 30, 1.0), ("Lighthouse", (-0.5, 4.36, 0.866), -30, 1.0),
         ("Island", (1.4, 1.23, -28.5), 90, 0.85), ("Island", (1.4, 1.23, -36.5), 90, 0.85), ("Smithy", (-1.1, 2.881, -0.5), 0, 1.0)]
NEST_SEAT, GULL_BELLY, NECK = 0.075, 0.19, (0, 0.37, 0.15)
nest_at = {}
for i, (on, at, yaw, s) in enumerate(NESTS):
    if on == "Lighthouse": g = T(lighthouse) @ ry(180) @ T(at) @ ry(yaw)
    elif on == "Smithy": g = stations["Smithy"] @ T(at) @ rx(10) @ ry(yaw)
    else: g = T(at) @ ry(yaw)
    g = g @ S(s)
    place("GullNest", g)
    seat = g @ Vector((0, NEST_SEAT, 0))
    ahead = g.to_3x3() @ Vector((0, 0, 1))
    gull = T(seat - Vector((0, GULL_BELLY, 0))) @ ry(math.degrees(math.atan2(ahead.x, ahead.z))) @ S(0.95)
    place("SeagullSit", gull)
    place("SeagullHead", gull @ T(NECK) @ ry((25, -30, 0, 40, -15)[i]))  # each looking about
    nest_at.setdefault(on, seat)

# ---------- the coffee rings: one at each rest spot, three to five round it ----------
RESTS = [((-8.2, -12.9), 90, 5), ((9.6, -13.9), -90, 3), ((-7.0, -21.0), 100, 4)]
MUGS = [(0.84, 0.3, 0.24), (0.27, 0.5, 0.74), (0.92, 0.76, 0.3), (0.38, 0.64, 0.42), (0.9, 0.86, 0.76)]
CUP_GRIP, CUP_SCALE, SIZE = Vector((0.055, 0.047, 0)), 1.3, 0.82
rings, hands = [], []
for (sx, sz), syaw, n in RESTS:
    objs = []
    r = max(0.5, 0.48 / math.sin(math.pi / n))
    base = syaw + 180 + {2: 90, 4: 22.5}.get(n, 0)
    spots = [(sx + math.sin(math.radians(base + 360 * k / n)) * r, sz + math.cos(math.radians(base + 360 * k / n)) * r) for k in range(n)]
    talker = 1  # one talks (the others look at them), one has a drink
    for k, (x, z) in enumerate(spots):
        yaw = math.degrees(math.atan2(sx - x, sz - z))
        root = T((x, ground(x, z), z)) @ ry(yaw) @ S(SIZE)
        look = 0.0
        if k != talker:
            to = math.degrees(math.atan2(spots[talker][0] - x, spots[talker][1] - z))
            look = max(-75, min(75, (to - yaw + 180) % 360 - 180))
        lift = 22 if k == 0 else 0
        who = f"PersonHelper{k % 3 + 1}"
        arm = root @ T((0.19, 1.38, 0)) @ euler(-55 - lift, 0, 6)
        for part, g in (("Body", root), ("Head", root @ T((0, 1.45, 0)) @ euler(0, look, 0)), ("ArmL", root @ T((-0.19, 1.38, 0)) @ euler(0, 0, -6)),
                        ("ArmR", arm), ("LegL", root @ T((-0.085, 0.92, 0))), ("LegR", root @ T((0.085, 0.92, 0)))):
            objs.append(place(who + part, g))
        grip, turn, s = arm @ Vector((0, -0.6, 0)), ry(yaw), SIZE * CUP_SCALE  # Person.PlaceCup
        objs.append(place("CoffeeCup", T(grip - turn.to_3x3() @ (CUP_GRIP * s)) @ turn @ S(s), MUGS[k]))
        hands.append((grip, yaw))
    rings.append(objs)

sc.render.engine = "BLENDER_WORKBENCH"
sh = sc.display.shading
sh.light, sh.studio_light, sh.color_type = "STUDIO", "paint.sl", "MATERIAL"
sh.show_cavity, sh.cavity_type = True, "WORLD"
sc.render.film_transparent = True
sc.view_settings.view_transform = "Standard"
sc.render.resolution_x = sc.render.resolution_y = int(os.environ.get("RES", 600))
cd = bpy.data.cameras.new("Cam"); cd.type = "ORTHO"
cam = bpy.data.objects.new("Cam", cd); sc.collection.objects.link(cam); sc.camera = cam

# Each subject: what to look at (game frame), how wide close up and from the island, where its front is seen from (yaw), the
# island view's yaw and pitch (where a player would see it from; the ring behind the stall through the gap from the road),
# who shows (a ring, the first of it on its own for the mug, nobody with the nests), and how far in front of it the front /
# side / back views cut away (so a wall doesn't hide a ring, nor the lantern the nests from behind).
def mid(i): (x, z), _, _ = RESTS[i]; return Vector((x, ground(x, z) + 0.75, z))
hand, hand_yaw = hands[0]
ROWS = {
    "ring1": (mid(0), 3.4, 10, RESTS[0][1], (RESTS[0][1] - 30, 32), rings[0], (1.4, 1.4, 1.4)),
    "ring2": (mid(1), 3.0, 10, RESTS[1][1], (RESTS[1][1], 12), rings[1], (1.4, 1.4, 1.4)),
    "ring3": (mid(2), 3.2, 10, RESTS[2][1], (RESTS[2][1] - 30, 32), rings[2], (1.4, 1.4, 1.4)),
    "mug": (hand + Vector((0, 0.12, 0)), 0.75, 1.6, hand_yaw, (hand_yaw + 40, 30), rings[0][:7], (0.9, 0.9, 0.9)),
    "lighthouse": (nest_at["Lighthouse"] + Vector((0.5, 0.25, 0)), 2.6, 7, 180, (200, -18), [], (0.3, 0.3, 0.3)),
    "pier": (nest_at["Island"] + Vector((0, 0.2, 0)), 1.4, 6, 90, (-120, 22), [], (1.0, 1.0, 1.0)),
    "smithy": (nest_at["Smithy"] + Vector((0, 0.2, 0)), 1.4, 7, -90, (-60, -8), [], (1.0, 1.0, 1.0)),
}
for key, (at, close, wide, front, (iyaw, ipitch), shown, (cf, cs, cb)) in ROWS.items():
    for objs in rings:
        for o in objs: o.hide_render = o not in shown
    for view, yaw, pitch, size, cut in (("island", iyaw, ipitch, wide, wide * 0.9), ("front", front, 6, close, cf), ("side", front + 90, 6, close, cs), ("back", front + 180, 6, close, cb)):
        y, p = math.radians(yaw), math.radians(pitch)
        d = (C.to_3x3() @ Vector((math.sin(y) * math.cos(p), math.sin(p), math.cos(y) * math.cos(p)))).normalized()
        cd.ortho_scale = size
        cd.clip_start, cd.clip_end = 60 - cut, 200 if view == "island" else 60 + size  # close up, nothing far behind either
        cam.location = C.to_3x3() @ at + d * 60
        cam.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        sc.render.filepath = os.path.join(OUT, f"{key}_{view}.png")
        bpy.ops.render.render(write_still=True)
print("RENDERED")
