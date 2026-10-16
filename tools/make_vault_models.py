# Builds the vault shed, the treasure chest and the loot gem into fortress_models.blend and exports each as OBJ.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_vault_models.py
# Blender axes: -Y is the front (it ends up facing the game's +Z, toward the dock path), Z is up, the base sits on z = 0.
import math
import os
import bmesh
import bpy
from mathutils import Matrix, Vector

OUT = os.path.dirname(bpy.data.filepath)


def mat(name, kd, ke=(0.0, 0.0, 0.0)):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    if name not in EXISTING:
        bsdf.inputs["Base Color"].default_value = (*kd, 1.0)
        if any(ke):
            bsdf.inputs["Emission Color"].default_value = (*ke, 1.0)
            bsdf.inputs["Emission Strength"].default_value = 1.0
    return m


EXISTING = {m.name for m in bpy.data.materials}


class Builder:
    def __init__(self, name, mats):
        self.name, self.mats, self.bm = name, mats, bmesh.new()

    def _tag(self, geom, mi):
        for f in geom:
            if isinstance(f, bmesh.types.BMFace):
                f.material_index = mi

    def box(self, size, loc, m, rot=(0.0, 0.0, 0.0)):
        mtx = Matrix.Translation(Vector(loc)) @ (Matrix.Rotation(rot[0], 4, "X") @ Matrix.Rotation(rot[1], 4, "Y") @ Matrix.Rotation(rot[2], 4, "Z")) @ Matrix.Diagonal((*size, 1.0))
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=mtx)
        faces = {f for v in r["verts"] for f in v.link_faces}
        self._tag(faces, self.mats.index(m))

    def cyl(self, radius, depth, loc, m, segs=8, axis="Z"):
        rot = {"Z": Matrix.Identity(4), "X": Matrix.Rotation(math.pi / 2, 4, "Y"), "Y": Matrix.Rotation(math.pi / 2, 4, "X")}[axis]
        r = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius, depth=depth, matrix=Matrix.Translation(Vector(loc)) @ rot)
        self._tag({f for v in r["verts"] for f in v.link_faces}, self.mats.index(m))

    def poly(self, pts, m):
        vs = [self.bm.verts.new(p) for p in pts]
        f = self.bm.faces.new(vs)
        f.material_index = self.mats.index(m)

    def prism(self, tri_xz, y, thick, m):
        # A triangle (in x/z) extruded along y: closed, so it has a proper outside.
        a = [self.bm.verts.new((x, y - thick / 2, z)) for x, z in tri_xz]
        b = [self.bm.verts.new((x, y + thick / 2, z)) for x, z in tri_xz]
        faces = [self.bm.faces.new(a), self.bm.faces.new(b[::-1])]
        for i in range(3):
            j = (i + 1) % 3
            faces.append(self.bm.faces.new((a[i], b[i], b[j], a[j])))
        self._tag(faces, self.mats.index(m))

    def finish(self):
        old = bpy.data.objects.get(self.name)
        if old:
            bpy.data.objects.remove(old, do_unlink=True)
        me = bpy.data.meshes.new(self.name)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        self.bm.to_mesh(me)
        self.bm.free()
        for m in self.mats:
            me.materials.append(m)
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


wood, dark, iron, gold, stone, spark = (bpy.data.materials[n] for n in ("Wood", "DarkWood", "Iron", "Gold", "Stone", "Spark"))
roof = mat("RoofRed", (0.62, 0.24, 0.19))
glow = mat("LootGlow", (1.0, 1.0, 1.0), (1.0, 1.0, 1.0))

# ---------- treasure chest (1.0 x 0.64 x ~0.85) ----------
c = Builder("Chest", [wood, dark, iron, gold])
c.box((1.0, 0.64, 0.5), (0, 0, 0.25), wood)                    # body
c.box((1.04, 0.68, 0.06), (0, 0, 0.03), dark)                  # foot rim
c.box((1.04, 0.68, 0.16), (0, 0, 0.58), dark)                  # lid, stepped into a low dome
c.box((0.98, 0.52, 0.1), (0, 0, 0.71), wood)
c.box((0.96, 0.3, 0.07), (0, 0, 0.79), wood)
for x in (-0.34, 0.34):                                        # iron bands over body and lid
    c.box((0.08, 0.68, 0.52), (x, 0, 0.27), iron)
    c.box((0.08, 0.72, 0.18), (x, 0, 0.58), iron)
    c.box((0.08, 0.56, 0.12), (x, 0, 0.71), iron)
    c.box((0.08, 0.34, 0.09), (x, 0, 0.79), iron)
c.box((0.18, 0.05, 0.22), (0, -0.345, 0.5), gold)              # lock plate (front)
c.box((0.06, 0.06, 0.08), (0, -0.37, 0.45), iron)              # keyhole
for x, z in ((-0.22, 0.66), (0.12, 0.67), (0.28, 0.65)):       # coins peeking out under the lid
    c.cyl(0.07, 0.025, (x, -0.33, z), gold, segs=8, axis="Y")
chest = c.finish()

# ---------- vault shed (2.4 x 1.8 footprint, ~2.6 tall; door on the front) ----------
v = Builder("Vault", [stone, wood, dark, roof, iron, gold, spark])
v.box((2.5, 1.9, 0.25), (0, 0, 0.125), stone)                  # foundation
v.box((2.3, 1.7, 1.6), (0, 0, 1.05), wood)                     # walls
for x in (-1.1, 1.1):
    for y in (-0.8, 0.8):
        v.box((0.16, 0.16, 1.7), (x, y, 1.1), dark)            # corner posts
for z in (0.62, 1.22):
    v.box((2.32, 1.72, 0.05), (0, 0, z), dark)                 # plank lines
for y in (-0.82, 0.82):                                        # gables
    v.prism([(-1.15, 1.85), (1.15, 1.85), (0, 2.42)], y, 0.06, wood)
pitch = math.atan2(0.62, 1.0)
for s in (-1, 1):                                              # roof slabs with overhang
    v.box((1.32, 2.7, 0.1), (s * 0.58, 0, 2.17), roof, rot=(0, s * pitch, 0))
v.box((0.12, 2.74, 0.12), (0, 0, 2.5), dark)                   # ridge beam
# door on the front (-Y)
v.box((0.9, 0.06, 1.3), (0, -0.87, 0.9), dark)
for z in (0.55, 1.25):
    v.box((0.94, 0.08, 0.08), (0, -0.88, z), iron)
v.box((0.08, 0.08, 0.14), (0.3, -0.92, 0.9), gold)             # handle
v.box((1.06, 0.06, 0.34), (0, -0.88, 1.72), wood)              # sign board
v.box((0.36, 0.05, 0.18), (0, -0.92, 1.72), gold)              # chest emblem on the sign
v.box((0.36, 0.05, 0.05), (0, -0.92, 1.84), iron)
# lantern beside the door
v.box((0.05, 0.22, 0.05), (0.72, -0.96, 1.62), iron)
v.box((0.16, 0.16, 0.2), (0.72, -1.05, 1.45), iron)
v.box((0.11, 0.11, 0.15), (0.72, -1.05, 1.45), spark)
vault = v.finish()

# ---------- loot gem (tinted per rarity in game) ----------
g = Builder("LootGem", [glow])
top, bot = (0, 0, 0.5), (0, 0, -0.5)
ring = [(0.3 * math.cos(a), 0.3 * math.sin(a), 0.08) for a in (i * math.pi / 3 for i in range(6))]
for i in range(6):
    a, b = ring[i], ring[(i + 1) % 6]
    g.poly([a, b, top], glow)
    g.poly([b, a, bot], glow)
gem = g.finish()

bpy.ops.wm.save_mainfile()
for ob in (chest, vault, gem):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.wm.obj_export(filepath=os.path.join(OUT, ob.name + ".obj"), export_selected_objects=True, forward_axis="NEGATIVE_Z", up_axis="Y",
                          export_materials=True, export_triangulated_mesh=True, export_uv=False, export_normals=True, export_smooth_groups=False)
    print("EXPORTED", ob.name, tuple(round(d, 2) for d in ob.dimensions), len(ob.data.polygons), "faces")
