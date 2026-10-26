# Shared helpers for the model scripts: materials and a bmesh builder with boxes, cylinders, beams and prisms.
# Blender axes: -Y is the front (the game's +Z after export), Z is up.
import math
import os
import bmesh
import bpy
from mathutils import Matrix, Vector

OUT = os.path.dirname(bpy.data.filepath)
EXISTING = {m.name for m in bpy.data.materials}
# ONLY=Name,Name...: rebuild and export just these (the scripts also hold models later scripts replaced, e.g. MenderHead).
ONLY = set(filter(None, os.environ.get("ONLY", "").split(",")))


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
        if ONLY and self.name not in ONLY:
            self.bm.free()
            return bpy.data.objects.get(self.name)
        old = bpy.data.objects.get(self.name)
        if old:
            bpy.data.objects.remove(old, do_unlink=True)
        me = bpy.data.meshes.new(self.name)
        # Closed shapes get their normals made outward; faces tagged in a "keep" layer (an open sheet like the island's
        # ground, whose winding was set by hand) are left alone: recalculating could turn a whole sheet upside down.
        keep = self.bm.faces.layers.int.get("keep")
        bmesh.ops.recalc_face_normals(self.bm, faces=[f for f in self.bm.faces if not (keep and f[keep])])
        self.bm.to_mesh(me)
        self.bm.free()
        for m in self.mats:
            me.materials.append(m)
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob




def export(objs):
    objs = [o for o in objs if o and (not ONLY or o.name in ONLY)]
    bpy.ops.wm.save_mainfile()
    for ob in objs:
        for o in bpy.context.scene.objects:
            o.select_set(False)
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.wm.obj_export(filepath=os.path.join(OUT, ob.name + ".obj"), export_selected_objects=True, forward_axis="NEGATIVE_Z", up_axis="Y",
                              export_materials=True, export_triangulated_mesh=True, export_uv=False, export_normals=True, export_smooth_groups=False)
        print("EXPORTED", ob.name, tuple(round(d, 2) for d in ob.dimensions), len(ob.data.polygons), "faces")


# ---------- shapes on top of the builder: lofts, tubes, rings, plates ----------

TAU = 2 * math.pi
UP = Vector((0, 0, 1))


def loft(b, rings, m, mf=None, caps=None, closed=False, tri=False):
    # Skins rings of equal size with quads (a one-point ring is a tip) and caps both ends, or joins the last ring back to the first.
    # mf(i, j) picks the material of the quad from ring i to i + 1 at column j; caps = (first, last) cap materials.
    # tri: rings twisted one segment each (vertex j of ring i + 1 above vertex j + 1 of ring i) split into two triangles along
    # that diagonal, so every triangle lies flat on the untwisted prism and the spiral columns shade cleanly.
    vs = [[b.bm.verts.new(p) for p in r] for r in rings]
    n = max(len(r) for r in vs)

    def face(vv, mm):
        b.bm.faces.new(vv).material_index = b.mats.index(mm)

    for i in range(len(vs) if closed else len(vs) - 1):
        A, B = vs[i], vs[(i + 1) % len(vs)]
        for j in range(n):
            k, mm = (j + 1) % n, mf(i, j) if mf else m
            if len(A) == 1 or len(B) == 1:
                face((A[0], B[k], B[j]) if len(A) == 1 else (A[j], A[k], B[0]), mm)
            elif tri:
                face((A[j], A[k], B[j]), mm)
                face((A[k], B[k], B[j]), mm)
            else:
                face((A[j], A[k], B[k], B[j]), mm)
    if not closed:
        c0, c1 = caps or (m, m)
        if len(vs[0]) > 2:
            face(vs[0][::-1], c0)
        if len(vs[-1]) > 2:
            face(vs[-1], c1)


def tube(b, path, r, m, segs=6, closed=False):
    # A round tube along a polyline (r: one radius or one per point, 0 = a point), framed by parallel transport so it doesn't twist.
    P = [Vector(p) for p in path]
    rs = list(r) if isinstance(r, (list, tuple)) else [r] * len(P)
    n = len(P)
    T = [((P[(i + 1) % n] if closed or i < n - 1 else P[i]) - (P[i - 1] if closed or i > 0 else P[i])).normalized() for i in range(n)]
    ref = UP if abs(T[0].z) < 0.9 else Vector((1, 0, 0))
    nrm = ref - T[0] * ref.dot(T[0])
    rings = []
    for p, t, rad in zip(P, T, rs):
        nrm = (nrm - t * nrm.dot(t)).normalized()
        bi = t.cross(nrm)
        rings.append([p] if rad <= 0 else [p + (nrm * math.cos(k * TAU / segs) + bi * math.sin(k * TAU / segs)) * rad for k in range(segs)])
    loft(b, rings, m, closed=closed)


def torus(b, c, axis, R, r, m, segs=16, tsegs=6):
    c, ax = Vector(c), Vector(axis).normalized()
    u = ax.orthogonal().normalized()
    v = ax.cross(u)
    tube(b, [c + (u * math.cos(k * TAU / segs) + v * math.sin(k * TAU / segs)) * R for k in range(segs)], r, m, segs=tsegs, closed=True)


def fin(b, pts, normal, thick, m):
    # A flat plate: the outline pts (in order) thickened along normal; thick is one number or one per point (to taper).
    nrm = Vector(normal).normalized()
    ts = list(thick) if isinstance(thick, (list, tuple)) else [thick] * len(pts)
    loft(b, [[Vector(p) - nrm * (t / 2) for p, t in zip(pts, ts)], [Vector(p) + nrm * (t / 2) for p, t in zip(pts, ts)]], m)

