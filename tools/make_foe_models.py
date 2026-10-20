# The fortress's land fish, one body per kind (Owen: 「我們的魚要畫的更好一點像是這遊戲的畫風」), drawn the way the game draws
# its own fish (read from its assets: a smooth low-poly hull of ~300-600 flat faces, natural muted colours with a darker
# back and a lighter belly, a small dark eye, thin pointed fins, a forked tail). The eyes are all black, no glint (Owen:
# 「眼睛可以試試看全黑」). In the game each one
# replaces the walking fish's body and walks on that fish's own sneakers (Enemies.TryDress); the gear goes on top.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_foe_models.py
#           or:  FOE_PREVIEW=<dir> ... --python tools/make_foe_models.py -- preview   (renders them into <dir>, saves nothing)
# Writes HtfFortress/Models/Foe<Kind>.obj/.mtl (nose to -Y, Z up, 1 m long like every model here; the game scales it).
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from mathutils import Vector
from bkit import TAU, Builder, export, fin, loft, mat, tube

PREVIEW = "preview" in (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
N = 14  # points round the hull

eye = mat("FoeEye", (0.02, 0.02, 0.025), force=True)
mouth = mat("FoeMouth", (0.28, 0.12, 0.12), force=True)


def lerp_prof(prof, t):
    # prof: [(t, half width, half height, centre height)] by t (0 nose .. 1 tail root); smooth in between
    for (t0, *a), (t1, *b) in zip(prof, prof[1:]):
        if t0 <= t <= t1:
            k = (t - t0) / (t1 - t0) if t1 > t0 else 0.0
            k = k * k * (3 - 2 * k)
            return [x + (y - x) * k for x, y in zip(a, b)]
    return list(prof[-1][1:])


def ring(y, w, h, zc, p=2.0):
    pts = []
    for k in range(N):
        a = (k + 0.5) * TAU / N  # no point straight up or down: the back and belly are a pair of faces, not a ridge
        c, s = math.cos(a), math.sin(a)
        pts.append((w * math.copysign(abs(c) ** (2 / p), c), y, zc + h * math.copysign(abs(s) ** (2 / p), s)))
    return pts


def fish(kind, prof, cols, L=1.0, p=2.0, stations=15, pattern=None, eye_t=0.13, eye_r=0.04, eye_up=0.35, tail=(0.17, 0.15, 0.07),
         dorsal=None, anal=None, pecs=(0.3, 0.1), extra=None):
    """A fish lying nose to -Y. prof: hull profile; cols: back, belly, fin (and pattern) colours; pattern(i, j, side) picks
    a face's colour: 'back', 'belly', 'fin' or 'dark'; tail: (length, half spread, notch depth); dorsal/anal: (t0, t1, height,
    sweep) fins along the top / the bottom."""
    back, belly, finm = (mat(f"Foe{kind}{n}", c, force=True) for n, c in zip(("Back", "Belly", "Fin"), cols[:3]))
    dark = mat(f"Foe{kind}Dark", cols[3], force=True) if len(cols) > 3 else back
    b = Builder("Foe" + kind, [back, belly, finm, dark, eye, mouth])
    ts = [0.0] + [((i / (stations - 1)) ** 1.15) for i in range(1, stations)]
    rings, prs = [], []
    for t in ts:
        w, h, zc = lerp_prof(prof, t)
        y = -L / 2 + t * L * 0.82  # the hull is 82% of the length, the tail fin the rest
        prs.append((y, w, h, zc))
        rings.append(ring(y, w, h, zc, p) if t > 0 else [(0.0, y, zc)])
    by = {"back": back, "belly": belly, "fin": finm, "dark": dark}

    def mf(i, j):
        a = (j + 1.0) * TAU / N  # the face between point j and j+1
        s = math.sin(a)
        side = "back" if s > 0.3 else "belly" if s < -0.45 else "side"
        if i == 0 and s < 0.1:
            return mouth  # the mouth: the nose's lower half
        pick = pattern(i, j, side) if pattern else None
        return by[pick] if pick else back if side != "belly" else belly

    loft(b, rings, back, mf=mf, caps=(back, back))

    # the tail fin, forked, standing up from the tail root
    y0, w0, h0, z0 = prs[-1]
    tl, spread, notch = tail
    fin(b, [(0, y0 - 0.02, z0 + h0 * 0.8), (0, y0 + tl, z0 + spread), (0, y0 + tl - notch, z0), (0, y0 + tl, z0 - spread), (0, y0 - 0.02, z0 - h0 * 0.8)],
        (1, 0, 0), [0.03, 0.008, 0.016, 0.008, 0.03], finm)

    def top_at(t, sgn=1):
        y, w, h, zc = prs[min(range(len(ts)), key=lambda k: abs(ts[k] - t))]
        return y, zc + sgn * h * 0.97

    for f, sgn in ((dorsal, 1), (anal, -1)):
        if not f:
            continue
        t0, t1, hgt, sweep = f
        ya, za = top_at(t0, sgn)
        yb, zb = top_at(t1, sgn)
        fin(b, [(0, ya, za - sgn * 0.01), (0, ya + (yb - ya) * 0.35 + sweep, za + sgn * hgt), (0, yb + sweep * 0.6, zb + sgn * hgt * 0.35), (0, yb, zb - sgn * 0.01)],
            (1, 0, 0), [0.025, 0.006, 0.006, 0.025], finm)

    # pectoral fins: small paddles behind the gills, swept back and down
    if pecs:
        t, size = pecs
        w, h, zc = lerp_prof(prof, t)
        y = -L / 2 + t * L * 0.82
        for sx in (-1, 1):
            x = sx * w * 0.92
            fin(b, [(x, y - 0.01, zc - h * 0.25), (x + sx * size * 0.55, y + size, zc - h * 0.45 - size * 0.3), (x + sx * size * 0.2, y + size * 0.9, zc - h * 0.15),
                    (x, y + size * 0.35, zc - h * 0.05)], (sx * 0.3, 0, 1), 0.012, finm)

    # the eyes: all black, set into the head's side
    w, h, zc = lerp_prof(prof, eye_t)
    y = -L / 2 + eye_t * L * 0.82
    for sx in (-1, 1):
        a = math.asin(eye_up)
        c = Vector((sx * w * math.cos(a) * 0.93, y, zc + h * math.sin(a) * 0.95))
        b.ico(eye_r, c, eye, subdiv=1)
    if extra:
        extra(b, prs, ts, {"back": back, "belly": belly, "fin": finm, "dark": dark, "eye": eye})
    ob = b.finish()
    return ob


def stripes(every, side_too=True):  # dark bars across the back
    return lambda i, j, side: "dark" if i % every == 1 and (side == "back" or (side_too and side == "side")) else None


def spots(seed, rate, where=("back", "side")):
    return lambda i, j, side: "dark" if side in where and ((i * 7 + j * 13 + seed) % 97) / 97.0 < rate and i > 1 else None


# ---------- the kinds (their idea from Enemies.cs; colours near DyeOf, toned down like the game's) ----------

def brow(b, prs, ts, m):  # an angry brow over each eye (the charger)
    y, w, h, zc = prs[2]
    for sx in (-1, 1):
        b.beam((sx * w * 0.75, y - 0.03, zc + h * 0.62), (sx * w * 0.55, y + 0.06, zc + h * 0.8), 0.014, m["dark"], segs=4)


def mask(i, j, side):  # the thief's bandit mask across the eyes
    return "dark" if i in (1, 2) and side != "belly" else None


def whiskers(b, prs, ts, m):  # the digger's barbels
    y, w, h, zc = prs[1]
    for sx in (-1, 1):
        tube(b, [(sx * w * 0.6, y, zc - h * 0.4), (sx * (w + 0.06), y - 0.03, zc - h * 0.7), (sx * (w + 0.14), y + 0.04, zc - h * 1.1)], 0.007, m["dark"], segs=4)


def puffer_spines(b, prs, ts, m):  # the bomber's little spines
    for i in range(2, len(prs) - 2):
        y, w, h, zc = prs[i]
        for k in range(8):
            a = (k + 0.5 * (i % 2)) * TAU / 8
            c, s = math.cos(a), math.sin(a)
            base = Vector((w * c, y, zc + h * s))
            out = Vector((c, 0, s)).normalized()
            b.beam(base, base + out * 0.045, 0.012, m["fin"], segs=3, r2=0.0)


def bubbles(b, prs, ts, m):  # the splitter's bubbly bumps
    for i in (3, 5, 7):
        y, w, h, zc = prs[i]
        for k in range(5):
            a = TAU * (0.08 + 0.12 * k) + i
            b.ico(0.028 + 0.008 * (k % 2), (w * math.cos(a) * 0.98, y, zc + h * math.sin(a) * 0.98), m["dark"], subdiv=1)


KINDS = {
    # the plain one: a mackerel, blue-green with dark bars and a silver belly
    "Runner": dict(prof=[(0, 0.02, 0.03, 0.0), (0.12, 0.075, 0.11, 0.01), (0.38, 0.095, 0.16, 0.01), (0.75, 0.06, 0.09, 0.0), (1, 0.025, 0.04, 0.0)],
                   cols=[(0.16, 0.4, 0.48), (0.84, 0.87, 0.88), (0.26, 0.45, 0.5), (0.07, 0.19, 0.25)], pattern=stripes(2, side_too=False),
                   dorsal=(0.3, 0.5, 0.09, 0.05), anal=(0.62, 0.78, 0.05, 0.03)),
    # fast, long and low: a red barracuda with a pointed jaw and a scowl
    "Charger": dict(prof=[(0, 0.01, 0.015, -0.01), (0.1, 0.05, 0.07, 0.0), (0.4, 0.08, 0.11, 0.01), (0.8, 0.055, 0.075, 0.0), (1, 0.022, 0.035, 0.0)],
                    cols=[(0.74, 0.2, 0.14), (0.96, 0.82, 0.68), (0.84, 0.38, 0.18), (0.3, 0.08, 0.06)], eye_t=0.16, eye_r=0.035,
                    dorsal=(0.45, 0.6, 0.1, 0.08), anal=(0.66, 0.78, 0.06, 0.05), tail=(0.19, 0.17, 0.1), extra=brow),
    # sits in the shallows and spits: an archerfish, deep and flat-topped, mouth up, spots along the back
    "Spitter": dict(prof=[(0, 0.015, 0.025, 0.05), (0.12, 0.06, 0.15, 0.03), (0.4, 0.08, 0.22, 0.0), (0.78, 0.05, 0.13, -0.01), (1, 0.025, 0.05, 0.0)],
                    cols=[(0.36, 0.44, 0.56), (0.86, 0.88, 0.9), (0.42, 0.5, 0.62), (0.13, 0.15, 0.21)], pattern=stripes(3),
                    eye_t=0.12, eye_r=0.046, eye_up=0.45, dorsal=(0.55, 0.75, 0.08, 0.06), anal=(0.55, 0.78, 0.09, 0.05)),
    # blows up: a slate pufferfish, round with little spines
    "Bomber": dict(prof=[(0, 0.04, 0.04, 0.0), (0.12, 0.17, 0.16, 0.01), (0.42, 0.26, 0.25, 0.02), (0.78, 0.15, 0.14, 0.0), (1, 0.05, 0.05, 0.0)],
                   cols=[(0.3, 0.32, 0.42), (0.82, 0.82, 0.84), (0.42, 0.44, 0.54), (0.18, 0.19, 0.25)], eye_t=0.16, eye_r=0.05, eye_up=0.4,
                   tail=(0.14, 0.12, 0.05), pecs=(0.4, 0.09), extra=puffer_spines),
    # slow and armoured: a sandy grouper, deep, big-jawed, mottled
    "Tank": dict(prof=[(0, 0.04, 0.05, -0.03), (0.12, 0.13, 0.17, 0.0), (0.4, 0.17, 0.25, 0.02), (0.8, 0.1, 0.15, 0.0), (1, 0.04, 0.06, 0.0)],
                 cols=[(0.6, 0.46, 0.27), (0.88, 0.79, 0.6), (0.5, 0.37, 0.21), (0.36, 0.25, 0.13)], p=2.4,
                 pattern=lambda i, j, side: "dark" if side == "back" and i % 3 == 2 else spots(11, 0.12, ("side",))(i, j, side),
                 eye_t=0.14, eye_r=0.045, dorsal=(0.25, 0.7, 0.07, 0.04), anal=(0.62, 0.8, 0.06, 0.03), tail=(0.17, 0.17, 0.04)),
    # grabs and runs: a thin purple fish with a bandit's mask
    "Thief": dict(prof=[(0, 0.015, 0.025, 0.0), (0.12, 0.05, 0.13, 0.01), (0.4, 0.065, 0.19, 0.01), (0.78, 0.045, 0.1, 0.0), (1, 0.02, 0.04, 0.0)],
                  cols=[(0.44, 0.28, 0.64), (0.84, 0.78, 0.92), (0.52, 0.36, 0.72), (0.1, 0.08, 0.13)], pattern=mask, eye_t=0.14,
                  dorsal=(0.35, 0.62, 0.12, 0.07), anal=(0.6, 0.78, 0.07, 0.04)),
    # takes turret fire on its board: a boxy blue boxfish, its plates picked out
    "Shield": dict(prof=[(0, 0.03, 0.035, 0.0), (0.12, 0.13, 0.13, 0.01), (0.4, 0.17, 0.19, 0.01), (0.8, 0.12, 0.13, 0.0), (1, 0.04, 0.05, 0.0)],
                   cols=[(0.26, 0.45, 0.76), (0.8, 0.86, 0.95), (0.95, 0.76, 0.26), (0.6, 0.76, 0.95)], p=3.6,
                   pattern=lambda i, j, side: "dark" if (i % 2 == 0 and j % 3 == 1) and i > 1 and side != "belly" else None,
                   eye_t=0.14, eye_r=0.045, tail=(0.15, 0.13, 0.03), dorsal=(0.7, 0.82, 0.05, 0.02), anal=(0.7, 0.82, 0.04, 0.02)),
    # tunnels under walls: a flat, wide orange catfish with whiskers and a shovel snout
    "Digger": dict(prof=[(0, 0.04, 0.02, -0.02), (0.12, 0.15, 0.08, 0.0), (0.4, 0.16, 0.11, 0.01), (0.8, 0.08, 0.07, 0.0), (1, 0.03, 0.035, 0.0)],
                   cols=[(0.72, 0.42, 0.18), (0.94, 0.8, 0.58), (0.58, 0.34, 0.15), (0.28, 0.16, 0.08)], p=2.2, eye_t=0.12, eye_r=0.034, eye_up=0.6,
                   dorsal=(0.3, 0.42, 0.08, 0.03), pecs=(0.22, 0.13), extra=whiskers),
    # heals the others: a tall green angelfish with long fins and pale bands
    "Healer": dict(prof=[(0, 0.015, 0.03, 0.0), (0.12, 0.045, 0.16, 0.01), (0.4, 0.06, 0.24, 0.01), (0.78, 0.04, 0.13, 0.0), (1, 0.02, 0.05, 0.0)],
                   cols=[(0.24, 0.6, 0.36), (0.8, 0.93, 0.78), (0.3, 0.66, 0.42), (0.72, 0.92, 0.7)], pattern=stripes(3),
                   eye_t=0.13, dorsal=(0.22, 0.72, 0.22, 0.16), anal=(0.38, 0.76, 0.18, 0.13), tail=(0.16, 0.14, 0.03)),
    # bursts into three: a round lime goby covered in bubbles
    "Splitter": dict(prof=[(0, 0.03, 0.035, 0.0), (0.12, 0.15, 0.15, 0.02), (0.42, 0.2, 0.2, 0.02), (0.78, 0.11, 0.11, 0.0), (1, 0.04, 0.045, 0.0)],
                     cols=[(0.48, 0.68, 0.16), (0.88, 0.94, 0.72), (0.42, 0.6, 0.14), (0.86, 0.96, 0.66)], eye_t=0.15, eye_r=0.055, eye_up=0.5,
                     dorsal=(0.42, 0.62, 0.07, 0.04), tail=(0.15, 0.12, 0.04), extra=bubbles),
}

built = []
for kind, spec in KINDS.items():
    built.append(fish(kind, **spec))
for ob in built:
    print("FOE", ob.name, tuple(round(d, 3) for d in ob.dimensions), len(ob.data.polygons), "faces")
if not PREVIEW:
    export(built)

if PREVIEW:  # side and three-quarter views of all of them, for a look (nothing saved or exported)
    out = os.environ.get("FOE_PREVIEW", "/tmp")
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light, sh.studio_light, sh.color_type = "STUDIO", "paint.sl", "MATERIAL"
    sh.show_object_outline, sh.show_cavity, sh.cavity_type = False, True, "WORLD"
    sc.render.film_transparent = True
    sc.view_settings.view_transform = "Standard"
    sc.render.resolution_x = sc.render.resolution_y = 384
    for m in bpy.data.materials:
        bsdf = m.node_tree and next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf:
            m.diffuse_color = (*bsdf.inputs["Base Color"].default_value[:3], 1.0)
    for o in sc.objects:
        o.hide_render = True
    cd = bpy.data.cameras.new("FoeCam"); cd.type = "ORTHO"
    cam = bpy.data.objects.new("FoeCam", cd); sc.collection.objects.link(cam); sc.camera = cam
    for ob in built:
        ob.hide_render = False
        for view, d in (("side", (1, 0, 0.12)), ("q", (0.9, -1.1, 0.55))):
            dv = Vector(d).normalized()
            cam.rotation_euler = dv.to_track_quat("Z", "Y").to_euler()
            cam.location = Vector((0, 0, 0)) + dv * 10
            cd.ortho_scale = 1.35
            sc.render.filepath = os.path.join(out, f"{ob.name}_{view}.png")
            bpy.ops.render.render(write_still=True)
        ob.hide_render = True
