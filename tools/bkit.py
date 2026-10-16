# Shared helpers for the model scripts: materials and a bmesh builder with boxes, cylinders, beams and prisms.
# Blender axes: -Y is the front (the game's +Z after export), Z is up.
import math
import os
import bmesh
import bpy
from mathutils import Matrix, Vector

OUT = os.path.dirname(bpy.data.filepath)
EXISTING = {m.name for m in bpy.data.materials}


def mat(name, kd, ke=(0.0, 0.0, 0.0), force=False):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    if force or name not in EXISTING:
        bsdf.inputs["Base Color"].default_value = (*kd, 1.0)
        if any(ke):
            bsdf.inputs["Emission Color"].default_value = (*ke, 1.0)
            bsdf.inputs["Emission Strength"].default_value = 1.0
    return m


def M(name):
    return bpy.data.materials[name]


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

    def cone(self, r1, r2, depth, loc, m, segs=8, axis="Z"):
        rot = {"Z": Matrix.Identity(4), "X": Matrix.Rotation(math.pi / 2, 4, "Y"), "Y": Matrix.Rotation(math.pi / 2, 4, "X"),
               "-Y": Matrix.Rotation(-math.pi / 2, 4, "X")}[axis]
        r = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=segs, radius1=r1, radius2=r2, depth=depth, matrix=Matrix.Translation(Vector(loc)) @ rot)
        self._tag({f for v in r["verts"] for f in v.link_faces}, self.mats.index(m))

    # A cylinder (segs) or square bar (segs=4) from a to b.
    def beam(self, a, b, radius, m, segs=4, r2=None):
        a, b = Vector(a), Vector(b)
        d = b - a
        q = d.to_track_quat("Z", "Y")
        mtx = Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4()
        r = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius if r2 is None else r2, depth=d.length, matrix=mtx)
        self._tag({f for v in r["verts"] for f in v.link_faces}, self.mats.index(m))

    def ico(self, r, loc, m, subdiv=1):
        res = bmesh.ops.create_icosphere(self.bm, subdivisions=subdiv, radius=r, matrix=Matrix.Translation(Vector(loc)))
        self._tag({f for v in res["verts"] for f in v.link_faces}, self.mats.index(m))

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




def export(objs):
    bpy.ops.wm.save_mainfile()
    for ob in objs:
        for o in bpy.context.scene.objects:
            o.select_set(False)
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.wm.obj_export(filepath=os.path.join(OUT, ob.name + ".obj"), export_selected_objects=True, forward_axis="NEGATIVE_Z", up_axis="Y",
                              export_materials=True, export_triangulated_mesh=True, export_uv=False, export_normals=True, export_smooth_groups=False)
        print("EXPORTED", ob.name, tuple(round(d, 2) for d in ob.dimensions), len(ob.data.polygons), "faces")
