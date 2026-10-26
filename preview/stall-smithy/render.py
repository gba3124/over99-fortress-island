# Preview pictures of the village's fish stall and smithy (tools/make_station_models.py), from the exported OBJs (what the
# game loads), with each keeper standing where FortressIsland.cs puts them (the mod's own fallback person as a stand-in),
# the smithy's rack hung with the weapons Village.cs WeaponRack puts on it (ours; the game's six guns can't be drawn here)
# and its price board over them. Colours as the game paints them (Models.SetMatColor: 15% toward grey, x0.9, sRGB).
#   M=HtfFortress/Models OUT=preview/stall-smithy/after \
#     /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python preview/stall-smithy/render.py
#   python3 tools/boss_eye_preview/sheet.py <OUT> <sheet.png> '[["stall","..."]]' '[["front","..."],...]'
# Every view in the station's own Blender frame: -Y is the customers' side, Z up, the origin on the ground in the middle.
import math
import os

import bpy
from mathutils import Matrix, Vector

M, OUT = os.environ["M"], os.environ["OUT"]
RES = int(os.environ.get("RES", "900"))
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene

# The game's frame (x right, y up, z ahead; the OBJs as Models.Parse loads them, x flipped) and Blender's: (-x, -z, y).
C = Matrix(((-1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
CI = C.inverted()


def game_colour(kd, ke):
    c = kd.lerp(ke, 0.5) if ke.length > 0 else kd
    g = 0.299 * c.x + 0.587 * c.y + 0.114 * c.z
    c = c.lerp(Vector((g, g, g)), 0.15) * 0.9
    return [((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c]  # authored as sRGB


def mtl(path):
    out, name = {}, None
    for line in open(path):
        q = line.split()
        if q and q[0] == "newmtl":
            name = q[1]
            out[name] = [Vector((0.8, 0.8, 0.8)), Vector((0, 0, 0))]
        elif q and q[0] in ("Kd", "Ke") and name:
            out[name][0 if q[0] == "Kd" else 1] = Vector([float(x) for x in q[1:4]])
    return out


meshes = {}


def place(name, mw=Matrix.Identity(4)):
    if name not in meshes:
        before = set(bpy.data.objects)
        bpy.ops.wm.obj_import(filepath=os.path.join(M, name + ".obj"), forward_axis="NEGATIVE_Z", up_axis="Y")
        o = next(o for o in bpy.data.objects if o not in before)
        o.data.transform(o.matrix_world)
        cols = mtl(os.path.join(M, name + ".mtl"))
        for m in o.data.materials:
            kd, ke = cols.get(m.name.split(".")[0], (Vector((0.8, 0.8, 0.8)), Vector((0, 0, 0))))
            m.diffuse_color = (*game_colour(kd, ke), 1)
        meshes[name] = o.data
        bpy.data.objects.remove(o)
    o = bpy.data.objects.new(name, meshes[name])
    sc.collection.objects.link(o)
    o.matrix_world = mw
    return o


def keeper(who, at):  # Person.Modelled's joints, facing the customers
    parts = {"Body": (0, 0, 0), "Head": (0, 1.45, 0), "ArmL": (-0.19, 1.38, 0), "ArmR": (0.19, 1.38, 0), "LegL": (-0.085, 0.92, 0), "LegR": (0.085, 0.92, 0)}
    root = C @ Matrix.Translation(Vector(at)) @ CI
    return [place("Person" + who + p, root @ C @ Matrix.Translation(Vector(off)) @ CI) for p, off in parts.items()]


def board(at, size, rgb):
    bpy.ops.mesh.primitive_cube_add(size=1, location=at)
    o = bpy.context.active_object
    o.scale = size
    m = bpy.data.materials.new("Board")
    m.diffuse_color = (*game_colour(Vector(rgb), Vector((0, 0, 0))), 1)
    o.data.materials.append(m)
    return o


# ---------- the two stations, side by side far apart (each view hides the other) ----------
STALL_AT, SMITHY_AT = Vector((0, 0, 0)), Vector((40, 0, 0))
stall = [place("MarketStall", Matrix.Translation(STALL_AT))] + keeper("Market", (0, 0.1, -0.4))
smithy = [place("Smithy", Matrix.Translation(SMITHY_AT))]
smithy += [o for o in keeper("Smith", (-0.4, 0.12, -0.55))]
for o in smithy[1:]:
    o.matrix_world = Matrix.Translation(SMITHY_AT) @ o.matrix_world
# The rack (Village.cs WeaponRack): Kinds.Take(21) in Gear order, three rails of seven, each 0.3 m long on its pegs.
RACK = ["LaserGun", "GasGun", "Boomerang", "SharkWhistle", "BubbleGun", "HookGun", "WhirlCannon", "Conch", "NetLauncher", "JellyGun",
        "WaterCannon", "PufferBomb", "StarThrower", "LobsterClaw", "Rocket"]
SLOT_Y, SLOT_Z, LENGTH = (2.44, 1.89, 1.34), -1.22, 0.3
ROT = Matrix(((0, 0, 1, 0), (0, 1, 0, 0), (-1, 0, 0, 0), (0, 0, 0, 1)))  # Quaternion.Euler(0, 90, 0)
for i, name in enumerate(RACK):
    if not os.path.exists(os.path.join(M, name + ".obj")):
        continue
    o = place(name)
    gv = [CI @ v.co for v in o.data.vertices]
    lo = Vector([min(v[a] for v in gv) for a in range(3)])
    hi = Vector([max(v[a] for v in gv) for a in range(3)])
    c, size = (lo + hi) / 2, hi - lo
    k = LENGTH / max(0.01, max(size))
    slot = Matrix.Translation(Vector((0.15 - (i % 7) * 0.33, SLOT_Y[i // 7], SLOT_Z))) @ ROT
    g = slot @ Matrix.Translation(-c * k + Vector((0, size.y / 2 * k, 0))) @ Matrix.Diagonal((k, k, k, 1))
    o.matrix_world = Matrix.Translation(SMITHY_AT) @ C @ g @ CI
    smithy.append(o)
smithy.append(board(SMITHY_AT + Vector((0.85, 1.3, 2.78)), (1.5, 0.04, 0.24), (0.42, 0.29, 0.18)))  # WeaponRack.Sign
bpy.ops.mesh.primitive_plane_add(size=200, location=(20, 0, -0.005))
ground = bpy.context.active_object
gm = bpy.data.materials.new("Ground")
gm.diffuse_color = (*game_colour(Vector((0.56, 0.62, 0.42)), Vector((0, 0, 0))), 1)
ground.data.materials.append(gm)

sc.render.engine = "BLENDER_WORKBENCH"
sh = sc.display.shading
sh.light, sh.studio_light, sh.color_type = "STUDIO", "paint.sl", "MATERIAL"
sh.show_cavity, sh.cavity_type = True, "WORLD"
sh.cavity_ridge_factor, sh.cavity_valley_factor = 0.6, 1.0
sh.show_shadows, sh.shadow_intensity = True, 0.35
sc.display.light_direction = (0.45, -0.55, 0.7)
sc.display.shadow_focus = 0.25
sc.render.film_transparent = False
world = bpy.data.worlds.new("W")
sc.world = world
world.color = (0.55, 0.62, 0.7)
sh.background_type = "WORLD"
sc.view_settings.view_transform = "Standard"
sc.render.resolution_x = sc.render.resolution_y = RES
sc.render.image_settings.color_mode, sc.render.image_settings.compression = "RGB", 100  # a third smaller in git
cd = bpy.data.cameras.new("Cam")
cd.clip_start, cd.clip_end = 0.05, 200
cam = bpy.data.objects.new("Cam", cd)
sc.collection.objects.link(cam)
sc.camera = cam


def persp(eye, at, lens):
    cd.type, cd.lens = "PERSP", lens
    cam.location = eye
    cam.rotation_euler = (Vector(at) - Vector(eye)).to_track_quat("-Z", "Y").to_euler()


def ortho(d, at, scale):
    d = Vector(d).normalized()
    cd.type, cd.ortho_scale = "ORTHO", scale
    cam.location = Vector(at) + d * 30
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()


# name: (kind, args) in the station's frame
VIEWS = {
    "stall": {
        "front": ("p", (0, -4.8, 1.7), (0, 0.1, 1.45), 30),          # a customer's eye, 1.7 m up, from the road
        "counter": ("p", (0, -2.05, 1.7), (0, -0.2, 1.2), 24),       # a customer at the counter
        "left": ("o", (-1, -0.05, 0.12), (0, 0.25, 1.55), 4.4),
        "right": ("o", (1, -0.05, 0.12), (0, 0.25, 1.55), 4.4),
        "back": ("o", (0.05, 1, 0.12), (-0.2, 0.25, 1.55), 4.9),
        "high34": ("p", (3.7, -4.3, 3.9), (-0.1, 0.15, 1.15), 34),
        "tray": ("p", (-0.15, -1.75, 1.72), (0, -0.62, 1.06), 32),
        "scale": ("p", (0.75, -1.55, 1.55), (1.3, -0.45, 1.16), 38),
        "board": ("p", (-0.7, -1.55, 1.55), (-1.22, -0.45, 1.06), 38),
        "shelves": ("p", (0.9, -1.4, 1.65), (-0.1, 1.1, 1.75), 26),
        "sign": ("p", (0.3, -2.7, 2.65), (0, -0.06, 3.12), 34),
    },
    "smithy": {
        "front": ("p", (0, -5.6, 1.7), (0, 0.2, 1.5), 28),
        "counter": ("p", (0.2, -2.2, 1.7), (0.0, 0.3, 1.2), 24),
        "left": ("o", (-1, -0.05, 0.12), (0, 0.05, 1.9), 5.0),
        "right": ("o", (1, -0.05, 0.12), (0, 0.05, 1.9), 5.0),
        "back": ("o", (0.05, 1, 0.12), (0, 0.05, 1.9), 5.4),
        "high34": ("p", (4.3, -4.9, 4.5), (0, 0.15, 1.35), 34),
        "anvil": ("p", (0.95, -1.75, 1.5), (0.2, -0.45, 0.82), 40),
        "forge": ("p", (0.15, -1.35, 1.85), (-1.25, 0.85, 1.15), 30),
        "rack": ("p", (0.85, -1.4, 1.8), (0.85, 1.3, 1.85), 26),
        "grind": ("p", (-0.5, -2.5, 1.35), (-1.55, -0.85, 0.6), 38),
        "trough": ("p", (2.3, -2.4, 1.55), (1.4, -0.7, 0.55), 38),
        "sign": ("p", (0.35, -3.3, 1.75), (0, -1.35, 2.13), 30),
    },
}
ONLY = set(filter(None, os.environ.get("VIEWS", "").split(",")))
for station, at, objs, other in (("stall", STALL_AT, stall, smithy), ("smithy", SMITHY_AT, smithy, stall)):
    for o in other:
        o.hide_render = True
    for o in objs:
        o.hide_render = False
    for view, (kind, a, b, c) in VIEWS[station].items():
        if ONLY and view not in ONLY:
            continue
        if kind == "p":
            persp(at + Vector(a), at + Vector(b), c)
        else:
            ortho(a, at + Vector(b), c)
        sc.render.filepath = os.path.join(OUT, f"{station}_{view}.png")
        bpy.ops.render.render(write_still=True)
print("RENDERED", OUT)
