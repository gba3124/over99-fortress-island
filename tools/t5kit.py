# Turret art pass 5 (Owen's reference sheets, 2026-10-26): the kit every v5 turret is built from.
# Cream stone in chamfered courses, dark iron with brass rivets, plank crates with iron frames, hollow muzzles.
# A turret is: <T>Base (the plinth, static: it never turns), on the head pivot <T>Body (the turntable) and one class head
# (<T>Default / A / B / A1 / A2 / B1 / B2), <T>Barrel (the extra gun a Lv5/10/15 milestone sets on the turntable), one
# piece for each of Lv2..Lv8 (<T>Kit2..Kit8, built round the plinth) and one for each final class (<T>DressA1..B2).
# Blender -Y is the game's front, Z up, metres; the plinth sits on z = 0, the head parts on the pivot (tools/layout5/<T>.json).
# Every helper takes M, the matrix of the frame it draws in, so sub-assemblies are drawn locally and placed.
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bkit import Builder, mat, ONLY, OUT  # noqa: E402

TAU = 2 * math.pi
I4 = Matrix.Identity(4)
HERE = os.path.dirname(os.path.abspath(__file__))

# ---------- palette (names new to the .blend, so these colours win) ----------
STONE = mat("T5Stone", (0.77, 0.72, 0.62))
STONE2 = mat("T5Stone2", (0.65, 0.60, 0.51))
IRON = mat("T5Iron", (0.20, 0.21, 0.23))
STEEL = mat("T5Steel", (0.37, 0.38, 0.41))
BRASS = mat("T5Brass", (0.84, 0.63, 0.26), (0.10, 0.06, 0.01))
WOOD = mat("T5Wood", (0.60, 0.35, 0.18))
WOOD2 = mat("T5Wood2", (0.41, 0.24, 0.12))
DARK = mat("T5Dark", (0.05, 0.05, 0.06))
SAND = mat("T5Sand", (0.78, 0.70, 0.53))
COPPER = mat("T5Copper", (0.82, 0.44, 0.22), (0.12, 0.04, 0.01))
CRYSTAL = mat("T5Crystal", (0.64, 0.90, 0.90), (0.25, 0.55, 0.55))
GLASS = mat("T5Glass", (0.56, 0.76, 0.98), (0.18, 0.32, 0.5))
GREEN = mat("T5Green", (0.36, 0.64, 0.30))
GLOWG = mat("T5GlowGreen", (0.5, 1.0, 0.55), (0.3, 0.9, 0.35))
LENS = mat("T5Lens", (0.20, 0.40, 0.95), (0.08, 0.2, 0.6))
OLIVE = mat("T5Olive", (0.38, 0.40, 0.22))
RED = mat("T5Red", (0.74, 0.17, 0.10), (0.12, 0.02, 0.01))
FIRE = mat("T5Fire", (1.0, 0.56, 0.12), (1.0, 0.42, 0.05))
ICE = mat("T5Ice", (0.76, 0.94, 1.0), (0.3, 0.7, 1.0))
CYAN = mat("T5Cyan", (0.35, 0.86, 1.0), (0.2, 0.7, 1.0))
AMBER = mat("T5Amber", (1.0, 0.66, 0.18), (0.9, 0.45, 0.05))
WHITE = mat("T5White", (0.93, 0.93, 0.91))
# the pennant each turret flies from Lv7 (its own colour)
CLOTH = {t: mat("T5Cloth" + t, c) for t, c in {
    "Gun": (0.70, 0.16, 0.13), "Cannon": (0.18, 0.26, 0.52), "Harpoon": (0.12, 0.50, 0.52), "Mortar": (0.86, 0.46, 0.12),
    "Tesla": (0.30, 0.72, 0.80), "Flame": (0.88, 0.30, 0.08), "Sniper": (0.34, 0.46, 0.20), "Bubble": (0.40, 0.62, 0.92),
    "Mender": (0.30, 0.66, 0.30)}.items()}


# ---------- frames ----------

def T(x=0.0, y=0.0, z=0.0):
    return Matrix.Translation((x, y, z))


def Rx(a):
    return Matrix.Rotation(a, 4, "X")


def Ry(a):
    return Matrix.Rotation(a, 4, "Y")


def Rz(a):
    return Matrix.Rotation(a, 4, "Z")


def R(rot):
    return Euler(rot, "XYZ").to_matrix().to_4x4()


def S(x, y=None, z=None):
    return Matrix.Diagonal((x, x if y is None else y, x if z is None else z, 1.0))


def toward(a, c):
    # The frame at a whose +Z points at c (for things drawn along their local Z: cylinders, pipes, tubes).
    a, c = Vector(a), Vector(c)
    q = (c - a).to_track_quat("Z", "Y")
    return T(*a) @ q.to_matrix().to_4x4(), (c - a).length


MIRROR_X = S(-1, 1, 1)


# ---------- builder ----------

class B5(Builder):
    """A Builder whose materials are added as they are first used (at most 6 a model: tests/Headless)."""

    def mi(self, m):
        if m not in self.mats:
            self.mats.append(m)
        return self.mats.index(m)


_ME = None


def _put(b, tmp, mtx, m=None):
    # Merge a part built in its own bmesh into the model, placed by mtx; m paints every face (None: keep the indices).
    global _ME
    if m is not None:
        mi = b.mi(m)
        for f in tmp.faces:
            f.material_index = mi
    if mtx is not None and mtx != I4:
        bmesh.ops.transform(tmp, matrix=mtx, verts=tmp.verts)
        if mtx.to_3x3().determinant() < 0:
            bmesh.ops.reverse_faces(tmp, faces=tmp.faces)
    if _ME is None:
        _ME = bpy.data.meshes.new("_t5tmp")
    tmp.to_mesh(_ME)
    tmp.free()
    b.bm.from_mesh(_ME)


def _bevel(tmp, edges, ch):
    if ch > 0 and edges:
        bmesh.ops.bevel(tmp, geom=list(edges), offset=ch, offset_type="OFFSET", segments=1, profile=0.5, affect="EDGES",
                        clamp_overlap=True)


def _loft(tmp, rings, mats, closed=False, caps=(True, True), cap_mats=None):
    # Skins rings of equal size (a one-point ring is a tip); mats[i] paints the band from ring i to i + 1.
    vs = [[tmp.verts.new(p) for p in r] for r in rings]
    n = max(len(r) for r in vs)

    def face(vv, mi):
        try:
            tmp.faces.new(vv).material_index = mi
        except ValueError:
            pass

    for i in range(len(vs) if closed else len(vs) - 1):
        A, Bq = vs[i], vs[(i + 1) % len(vs)]
        mi = mats[i % len(mats)]
        for j in range(n):
            k = (j + 1) % n
            if len(A) == 1:
                face((A[0], Bq[k], Bq[j]), mi)
            elif len(Bq) == 1:
                face((A[j], A[k], Bq[0]), mi)
            else:
                face((A[j], A[k], Bq[k], Bq[j]), mi)
    if not closed:
        c0, c1 = cap_mats or (mats[0], mats[-1])
        if caps[0] and len(vs[0]) > 2:
            face(vs[0][::-1], c0)
        if caps[1] and len(vs[-1]) > 2:
            face(vs[-1], c1)


def circle(r, n, z=0.0, phase=None, sx=1.0, sy=1.0):
    ph = math.pi / n if phase is None else phase
    return [(r * sx * math.cos(ph + k * TAU / n), r * sy * math.sin(ph + k * TAU / n), z) for k in range(n)]


def octo(hw, hd, cut, z=0.0):
    # A rectangle (half sizes hw x hd) with its corners cut by `cut`, counter-clockwise from the front right.
    if cut <= 1e-4:
        return [(hw, -hd, z), (hw, hd, z), (-hw, hd, z), (-hw, -hd, z)]
    return [(hw - cut, -hd, z), (hw, -hd + cut, z), (hw, hd - cut, z), (hw - cut, hd, z),
            (-hw + cut, hd, z), (-hw, hd - cut, z), (-hw, -hd + cut, z), (-hw + cut, -hd, z)]


# ---------- primitives ----------

def box(b, size, loc=(0, 0, 0), m=None, ch=0.025, rot=(0, 0, 0), edges=None, M=I4):
    # A box with chamfered edges: edges None = all, "z" = the vertical ones, "top" = the top rim, "ztop" = both.
    tmp = bmesh.new()
    bmesh.ops.create_cube(tmp, size=1.0, matrix=S(*size))
    ch = min(ch, 0.45 * min(size))
    es = list(tmp.edges)
    vert = [e for e in es if abs(e.verts[0].co.z - e.verts[1].co.z) > 1e-6]
    top = [e for e in es if e.verts[0].co.z > 1e-6 and e.verts[1].co.z > 1e-6]
    _bevel(tmp, {None: es, "z": vert, "top": top, "ztop": vert + top}[edges], ch)
    _put(b, tmp, M @ T(*loc) @ R(rot), m)


def cyl(b, r, h, loc=(0, 0, 0), m=None, segs=8, ch=0.015, axis="Z", r2=None, rot=None, M=I4, phase=None):
    # A cylinder (or frustum, r2) along axis X / Y / Z, its rims chamfered; flat faces up for even counts.
    tmp = bmesh.new()
    ph = (math.pi / segs) if phase is None else phase
    bmesh.ops.create_cone(tmp, cap_ends=True, segments=segs, radius1=r, radius2=r if r2 is None else r2, depth=h, matrix=Rz(ph))
    rims = [e for e in tmp.edges if abs(e.verts[0].co.z - e.verts[1].co.z) < 1e-6 and abs(abs(e.verts[0].co.z) - h / 2) < 1e-5]
    _bevel(tmp, rims, min(ch, 0.4 * min(r, r if r2 is None else max(r2, 1e-3)), 0.45 * h))
    o = {"Z": I4, "X": Ry(math.pi / 2), "Y": Rx(math.pi / 2), "-Y": Rx(-math.pi / 2)}[axis] if rot is None else R(rot)
    _put(b, tmp, M @ T(*loc) @ o, m)


def rod(b, a, c, r, m, segs=8, ch=0.0, r2=None, M=I4):
    # A cylinder from point a to point c.
    F, L = toward(a, c)
    cyl(b, r, L, (0, 0, L / 2), m, segs=segs, ch=ch, r2=r2, M=M @ F)


def bar(b, a, c, w, h, m, ch=0.008, M=I4, up=(0, 0, 1)):
    # A square bar w x h from a to c (its h along `up` as far as it can).
    a, c = Vector(a), Vector(c)
    d = (c - a)
    z = d.normalized()
    x = Vector(up).cross(z)
    if x.length < 1e-6:
        x = Vector((1, 0, 0)).cross(z)
    x.normalize()
    y = z.cross(x)
    F = Matrix((x, y, z)).transposed().to_4x4()
    F.translation = (a + c) / 2
    box(b, (w, h, d.length), (0, 0, 0), m, ch=ch, M=M @ F)


def ball(b, r, loc, m, subdiv=1, M=I4, sz=1.0):
    tmp = bmesh.new()
    bmesh.ops.create_icosphere(tmp, subdivisions=subdiv, radius=r, matrix=S(1, 1, sz))
    _put(b, tmp, M @ T(*loc), m)


def gem(b, r, h_up, h_down, loc, m, segs=6, M=I4, mid=None):
    # A cut crystal: a ring of `segs` at mid height, a point above and below.
    tmp = bmesh.new()
    rings = [[(0, 0, -h_down)], circle(r, segs, 0.0), [(0, 0, h_up)]]
    if mid:
        rings = [[(0, 0, -h_down)], circle(r * mid[0], segs, -mid[1]), circle(r, segs, 0.0), circle(r * mid[0], segs, mid[1]), [(0, 0, h_up)]]
    _loft(tmp, rings, [0])
    _put(b, tmp, M @ T(*loc), m)


def tier(b, hw, hd, cut, z0, z1, m, ch=0.03, chb=0.0, taper=0.0, M=I4, cut_top=None):
    # One course of a plinth: a cut-cornered block from z0 to z1, its sides leaning in by `taper`, top rim (and bottom
    # rim, chb) chamfered. The chamfers between courses are the joints the eye reads as stonework.
    tmp = bmesh.new()
    ct = cut if cut_top is None else cut_top
    tw, td = hw - taper, hd - taper
    rings = []
    if chb > 0:
        rings += [octo(hw - chb, hd - chb, max(0.0, cut - chb * 0.41), z0), octo(hw, hd, cut, z0 + chb)]
    else:
        rings += [octo(hw, hd, cut, z0)]
    if ch > 0:
        rings += [octo(tw, td, ct, z1 - ch), octo(tw - ch, td - ch, max(0.0, ct - ch * 0.41), z1)]
    else:
        rings += [octo(tw, td, ct, z1)]
    _loft(tmp, rings, [0])
    _put(b, tmp, M, m)


def rtier(b, r, z0, z1, m, segs=12, ch=0.03, chb=0.0, r_top=None, M=I4):
    # A round course (segs-gon) from z0 to z1, r_top for a cone, its rims chamfered.
    tmp = bmesh.new()
    rt = r if r_top is None else r_top
    rings = []
    if chb > 0:
        rings += [circle(r - chb, segs, z0), circle(r, segs, z0 + chb)]
    else:
        rings += [circle(r, segs, z0)]
    if ch > 0:
        rings += [circle(rt, segs, z1 - ch), circle(rt - ch, segs, z1)]
    else:
        rings += [circle(rt, segs, z1)]
    _loft(tmp, rings, [0])
    _put(b, tmp, M, m)


def ring(b, r_out, r_in, h, loc=(0, 0, 0), m=None, segs=12, M=I4, axis="Z"):
    # A flat ring (washer / band / rim) of rectangular section, around axis.
    tmp = bmesh.new()
    rings = [circle(r_out, segs, -h / 2), circle(r_out, segs, h / 2), circle(r_in, segs, h / 2), circle(r_in, segs, -h / 2)]
    _loft(tmp, rings, [0], closed=True)
    o = {"Z": I4, "X": Ry(math.pi / 2), "Y": Rx(math.pi / 2)}[axis]
    _put(b, tmp, M @ T(*loc) @ o, m)


def pipe(b, a, c, ro, ri, m, bore=None, segs=8, depth=None, M=I4, lip=None):
    # A hollow tube from a (closed end) to c (open end): the outer wall, the lip and a dark bore `depth` deep.
    F, L = toward(a, c)
    d = min(L * 0.9, depth if depth is not None else ri * 2.5)
    tmp = bmesh.new()
    lm = b.mi(m)
    bm_ = b.mi(bore or DARK)
    rings = [circle(ro, segs, 0.0), circle(ro, segs, L), circle(ri, segs, L), circle(ri, segs, L - d)]
    _loft(tmp, rings, [lm, lm, bm_], caps=(True, True), cap_mats=(lm, bm_))
    _put(b, tmp, M @ F)


def stud(b, loc, nrm=(0, 0, 1), r=0.028, m=None, segs=6, h=None, M=I4):
    # A rivet: a squat dome (frustum) on a surface facing nrm, loc on the surface.
    m = m or BRASS
    h = h or r * 0.75
    tmp = bmesh.new()
    bmesh.ops.create_cone(tmp, cap_ends=True, segments=segs, radius1=r, radius2=r * 0.5, depth=h, matrix=T(0, 0, h / 2) @ Rz(math.pi / segs))
    q = Vector(nrm).to_track_quat("Z", "Y").to_matrix().to_4x4()
    _put(b, tmp, M @ T(*loc) @ q, m)


def sqstud(b, loc, nrm=(0, 0, 1), s=0.05, m=None, M=I4):
    # A square pyramid stud (the mortar sheet's iron caps).
    stud(b, loc, nrm, r=s * 0.7, m=m or BRASS, segs=4, h=s * 0.45, M=M)


def plate(b, w, h, t, loc, nrm, m, rivets=None, rv=0.022, ch=0.01, M=I4, rivet_m=None):
    # A riveted plate w x h, t thick, lying on a surface facing nrm (its centre at loc on the surface).
    n = Vector(nrm).normalized()
    up = Vector((0, 0, 1)) if abs(n.z) < 0.9 else Vector((0, 1, 0))
    x = up.cross(n).normalized()
    y = n.cross(x)
    F = Matrix((x, y, n)).transposed().to_4x4()
    F.translation = Vector(loc) + n * (t / 2)
    box(b, (w, h, t), (0, 0, 0), m, ch=ch, M=M @ F)
    if rivets:
        for u, v in rivets:
            stud(b, (u * (w / 2 - rv * 1.6), v * (h / 2 - rv * 1.6), t / 2), (0, 0, 1), r=rv, m=rivet_m or BRASS, M=M @ F)


CORNERS4 = ((-1, -1), (1, -1), (1, 1), (-1, 1))


def crate(b, size, M=I4, wood=None, frame=None, rivet=None, planks=2, lid=True):
    # The sheets' ammo box: planks in an iron frame, brass rivets at the frame's corners. Origin at the bottom centre.
    wood, frame, rivet = wood or WOOD, frame or IRON, rivet or BRASS
    sx, sy, sz = size
    box(b, (sx - 0.02, sy - 0.02, sz - 0.01), (0, 0, sz / 2), wood, ch=0.01, M=M)
    t = min(0.04, 0.16 * min(sx, sy, sz))
    for u, v in CORNERS4:  # the frame's posts
        box(b, (t, t, sz), (u * (sx / 2 - t / 2), v * (sy / 2 - t / 2), sz / 2), frame, ch=0.0, M=M)
    for z in (t / 2, sz - t / 2):  # its rims
        for v in (-1, 1):
            box(b, (sx, t, t), (0, v * (sy / 2 - t / 2), z), frame, ch=0.0, M=M)
        for u in (-1, 1):
            box(b, (t, sy - 2 * t, t), (u * (sx / 2 - t / 2), 0, z), frame, ch=0.0, M=M)
    for k in range(1, planks + 1):  # plank seams on the long faces
        z = sz * k / (planks + 1)
        for v in (-1, 1):
            box(b, (sx - 2 * t, 0.012, 0.012), (0, v * (sy / 2 - 0.006), z), WOOD2, ch=0.0, M=M)
    for u in (-1, 1):
        for z in (t * 0.5, sz - t * 0.5):
            stud(b, (u * (sx / 2 - t / 2), -sy / 2, z), (0, -1, 0), r=t * 0.38, m=rivet, segs=4, M=M)
    if lid:
        box(b, (sx * 0.4, 0.03, 0.05), (0, -sy / 2 - 0.012, sz * 0.8), frame, ch=0.0, M=M)  # latch


def gear(b, r, w, M=I4, m=None, teeth=12, spokes=6, hub=None):
    # A gear wheel facing +X (axle along X), centred on the frame: rim, teeth, spokes, hub.
    m, hub = m or BRASS, hub or IRON
    ring(b, r * 0.86, r * 0.66, w, (0, 0, 0), m, segs=teeth, M=M, axis="X")
    for k in range(teeth):
        a = (k + 0.5) * TAU / teeth
        box(b, (w, r * 0.22, r * 0.2), (0, math.cos(a) * r * 0.93, math.sin(a) * r * 0.93), m, ch=0.006, rot=(a, 0, 0), M=M)
    for k in range(spokes):
        a = k * TAU / spokes
        box(b, (w * 0.7, r * 0.12, r * 0.62), (0, math.cos(a) * r * 0.42, math.sin(a) * r * 0.42), m, ch=0.004, rot=(a - math.pi / 2, 0, 0), M=M)
    cyl(b, r * 0.22, w * 1.5, (0, 0, 0), hub, segs=6, axis="X", M=M)
    cyl(b, r * 0.1, w * 1.9, (0, 0, 0), m, segs=6, axis="X", M=M)


def sandbag(b, size, M=I4, m=None):
    # A plump bag: a box with deep chamfers.
    box(b, size, (0, 0, size[2] / 2), m or SAND, ch=min(size) * 0.33, M=M)


def pennant(b, h, cloth, M=I4, flip=False):
    # A flag on an iron pole from the frame's origin up h: brass knob on top, the cloth with a swallow tail.
    rod(b, (0, 0, 0), (0, 0, h), 0.022, IRON, segs=6, M=M)
    ball(b, 0.04, (0, 0, h + 0.03), BRASS, M=M)
    s = -1 if flip else 1
    top, low = h - 0.04, h - 0.34
    pts = [(0.02 * s, top), (0.40 * s, top - 0.03), (0.30 * s, (top + low) / 2), (0.40 * s, low + 0.03), (0.02 * s, low)]
    tmp = bmesh.new()
    th = 0.018
    rings = [[(x, -th / 2, z) for x, z in pts], [(x, th / 2, z) for x, z in pts]]
    _loft(tmp, rings, [0])
    _put(b, tmp, M, cloth)
    box(b, (0.05, 0.05, 0.03), (0, 0, top + 0.01), BRASS, ch=0.005, M=M)
    box(b, (0.05, 0.05, 0.03), (0, 0, low - 0.01), BRASS, ch=0.005, M=M)


def chain_links(b, a, c, n, r, m=None, M=I4):
    # n flat links from a to c, alternating 90 degrees.
    a, c = Vector(a), Vector(c)
    for k in range(n):
        p = a.lerp(c, (k + 0.5) / n)
        F, _ = toward(p - (c - a).normalized() * 0.01, p + (c - a).normalized() * 0.01)
        ring(b, r, r * 0.55, r * 0.35, (0, 0, 0), m or IRON, segs=6, M=M @ F @ Rz(k * math.pi / 2) @ Ry(math.pi / 2) @ S(1, 1.6, 1))


def coil_rings(b, r, z0, z1, n, m=None, segs=12, thick=None, M=I4):
    # A stack of n copper rings (a coil) round the Z axis from z0 to z1.
    m = m or COPPER
    step = (z1 - z0) / n
    th = thick or step * 0.78
    for k in range(n):
        ring(b, r, r * 0.62, th, (0, 0, z0 + step * (k + 0.5)), m, segs=segs, M=M)


# ---------- models and export ----------

MODELS = {}


def model(name, *parts, scale=1.0):
    # One model from part functions drawing onto the builder (A1 = A's parts + its own), scaled about the origin.
    b = B5(name, [])
    for p in parts:
        p(b)
    if scale != 1.0:
        bmesh.ops.transform(b.bm, matrix=S(scale), verts=b.bm.verts)
    MODELS[name] = b.finish()
    return MODELS[name]


def export_all():
    objs = [o for n, o in MODELS.items() if o and (not ONLY or n in ONLY)]
    if os.environ.get("SAVE"):
        bpy.ops.wm.save_mainfile()
    for ob in objs:
        for o in bpy.context.scene.objects:
            o.select_set(False)
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.wm.obj_export(filepath=os.path.join(OUT, ob.name + ".obj"), export_selected_objects=True, forward_axis="NEGATIVE_Z",
                              up_axis="Y", export_materials=True, export_triangulated_mesh=True, export_uv=False, export_normals=True,
                              export_smooth_groups=False)
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        print("EXPORTED %-16s %5d tris %d mats  %s" % (ob.name, tris, len(ob.data.materials), tuple(round(d, 2) for d in ob.dimensions)))


def layout(t, **kw):
    # The numbers the game needs to stack this turret (Catalog.HeadHeight / BarrelMount / BarrelStep; tests/Headless
    # checks the C# against this file, tools/turret_preview5.py stacks the previews with it).
    os.makedirs(os.path.join(HERE, "layout5"), exist_ok=True)
    with open(os.path.join(HERE, "layout5", t + ".json"), "w") as f:
        json.dump(kw, f, indent=1)
