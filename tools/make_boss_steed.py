# The Silver Steed (wave 40): a winged unicorn in knight's armour (Owen: 「可以有獨角有裝備也會很好！」), made in the parts the
# game moves (BossSteed.cs):
#   BossSteedBody      the barrel, its chest plate, a blue saddle cloth, the crupper over its hindquarters
#   BossSteedHead      the neck and the head: crinet plates under a gold mane, the chamfron, the horn
#   BossSteedTail      a gold tail
#   BossSteedForeArm / BossSteedForeShin, BossSteedThigh / BossSteedHindShin   a leg in two, the same both sides
#   BossSteedWing      the left wing, spread (the right one is it mirrored)
# Each part's origin is the joint it turns on (BossSteed.cs has the same joints), so the parts are drawn in place round the
# root (where the game's horse has its middle, hooves 5.7 m under it) and moved back onto their joints for export.
# Metres at the size the game shows it, front = -Y, up = Z; the horse's left is +X.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_boss_steed.py
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Matrix, Vector
from bkit import TAU, Builder, export, fin, loft, mat, tube

random.seed(40)
GROUND = -5.7  # the hooves (BossBrain.Foot: the game horse's root sits this high over its hooves)
FORE_X, HIND_X = 0.62, 0.66
SHOULDER, KNEE, FORE_HOOF = Vector((0, -2.55, -1.3)), Vector((0, -2.62, -4.0)), Vector((0, -2.7, GROUND))
HIP, HOCK, HIND_HOOF = Vector((0, 2.35, -0.75)), Vector((0, 3.0, -3.75)), Vector((0, 2.88, GROUND))
NECK = Vector((0, -2.9, -0.2))  # where the neck turns, inside the chest
DOCK = Vector((0, 3.62, -0.15))  # the tail's root
WING = Vector((0.7, -1.5, 0.55))  # the left wing's shoulder, on the back behind the withers
MUZZLE = Vector((0, -6.3, 0.55))
HORN_LEN, HORN_TILT = 2.5, 74.0  # the horn: from the brow, turned up from the face's line (degrees)
X, Z = Vector((1, 0, 0)), Vector((0, 0, 1))

coat = mat("SteedCoat", (0.88, 0.9, 0.95), force=True)  # silver-white
coat_lo = mat("SteedCoatShade", (0.7, 0.73, 0.82), force=True)  # its belly, the inside of its legs
steel = mat("SteedSteel", (0.5, 0.55, 0.66), force=True)
steel_hi = mat("SteedSteelHi", (0.72, 0.77, 0.86), force=True)
gold = mat("SteedGold", (1.0, 0.74, 0.25), force=True)
gold_glow = mat("SteedGoldGlow", (1.0, 0.8, 0.35), (1.0, 0.68, 0.18), force=True)  # mane, tail
horn_mat = mat("SteedHorn", (1.0, 0.96, 0.85), (1.0, 0.9, 0.62), force=True)
cloth = mat("SteedCloth", (0.13, 0.2, 0.55), force=True)
dark = mat("SteedDark", (0.06, 0.06, 0.09), force=True)  # nostrils
shine = mat("SteedShine", (1.0, 1.0, 1.0), (1.0, 1.0, 1.0), force=True)  # the glint in its eyes
feather = mat("SteedFeather", (0.96, 0.97, 1.0), force=True)
feather_lo = mat("SteedFeatherShade", (0.78, 0.82, 0.92), force=True)


def sp(v, e):
    return math.copysign(abs(v) ** e, v)


def ring(c, side, up, w, h, n=12, p=2.2, a0=None, a1=None):
    # A rounded-box section round c in the plane of side/up (angle 0 = +side, 90 = +up); an arc from a0 to a1 (degrees,
    # n points) when they're given.
    if a0 is None:
        angs = [(k + 0.5) * TAU / n for k in range(n)]
    else:
        angs = [math.radians(a0 + (a1 - a0) * k / (n - 1)) for k in range(n)]
    return [c + side * (w * sp(math.cos(a), 2 / p)) + up * (h * sp(math.sin(a), 2 / p)) for a in angs]


def smooth(prof, t):
    # prof: [(t, values...)] sorted by t; smoothstep between neighbours
    for (t0, *a), (t1, *b) in zip(prof, prof[1:]):
        if t0 <= t <= t1:
            k = (t - t0) / (t1 - t0) if t1 > t0 else 0.0
            k = k * k * (3 - 2 * k)
            return [x + (y - x) * k for x, y in zip(a, b)]
    return list(prof[0][1:] if t < prof[0][0] else prof[-1][1:])


def shade(rings, top, bottom, under=-0.35):
    # a face's material by which way it faces: the underside darker
    def mf(i, j):
        r = rings[i]
        n = len(r)
        if n < 3:
            return top
        mid = (r[j] + r[(j + 1) % n]) / 2
        c = sum(r, Vector()) / n
        return bottom if (mid - c).normalized().z < under else top
    return mf


def bezier(p0, p1, p2, t):
    return p0 * (1 - t) ** 2 + p1 * 2 * t * (1 - t) + p2 * t * t


def slab(b, stations, thick, m, trim=None, tr=0.05):
    # A plate: stations [(centre, outer points)] of equal length, thickened inward toward each centre; a gold rim round it.
    outer = [[b.bm.verts.new(p) for p in pts] for c, pts in stations]
    inner = [[b.bm.verts.new(p + (c - p).normalized() * thick) for p in pts] for c, pts in stations]
    mi = b.mats.index(m)

    def f(vs):
        b.bm.faces.new(vs).material_index = mi

    S, N = len(stations), len(stations[0][1])
    for i in range(S - 1):
        for j in range(N - 1):
            f((outer[i][j], outer[i][j + 1], outer[i + 1][j + 1], outer[i + 1][j]))
            f((inner[i][j], inner[i + 1][j], inner[i + 1][j + 1], inner[i][j + 1]))
        for j in (0, N - 1):
            f((outer[i][j], outer[i + 1][j], inner[i + 1][j], inner[i][j]))
    for i in (0, S - 1):
        for j in range(N - 1):
            f((outer[i][j], inner[i][j], inner[i][j + 1], outer[i][j + 1]))
    if trim:
        pts = [p for c, p in stations]
        loop = pts[0] + [r[-1] for r in pts[1:]] + pts[-1][-2::-1] + [r[0] for r in pts[-2:0:-1]]
        tube(b, loop, tr, trim, segs=4, closed=True)


# ---------- the barrel and what it wears ----------

BODY = [  # y, half width, top, bottom: the back dips behind the withers and rises to the croup, the belly tucks up to the flank
    (-3.68, 0.22, -0.55, -1.35),
    (-3.5, 0.62, -0.05, -1.8),
    (-3.1, 0.98, 0.45, -2.15),
    (-2.4, 1.12, 0.95, -2.35),
    (-1.2, 1.2, 0.62, -2.3),
    (0.3, 1.22, 0.66, -2.15),
    (1.3, 1.18, 0.85, -1.85),
    (2.2, 1.16, 0.95, -1.95),
    (3.0, 1.0, 0.75, -1.85),
    (3.45, 0.75, 0.35, -1.5),
    (3.7, 0.3, 0.0, -1.1),
]
BODY_P = 2.3


def body_at(y, off=0.0, n=14, a0=None, a1=None):
    w, top, bot = smooth(BODY, y)
    c = Vector((0, y, (top + bot) / 2))
    return c, ring(c, X, Z, w + off, (top - bot) / 2 + off, n=n, p=BODY_P, a0=a0, a1=a1)


def body():
    b = Builder("BossSteedBody", [coat, coat_lo, steel, steel_hi, gold, cloth])
    ys = [-3.68 + 7.38 * k / 18 for k in range(19)]
    rings = [[Vector((0, -3.74, -0.95))]] + [body_at(y)[1] for y in ys] + [[Vector((0, 3.76, -0.55))]]
    loft(b, rings, coat, mf=shade(rings, coat, coat_lo))
    # the saddle cloth, down both sides to below the middle; the crupper over the hindquarters
    slab(b, [body_at(-1.95 + 3.2 * k / 8, 0.03, n=13, a0=-32, a1=212) for k in range(9)], 0.05, cloth, gold, 0.045)
    slab(b, [body_at(1.15 + 2.3 * k / 7, 0.1, n=11, a0=12, a1=168) for k in range(8)], 0.1, steel, gold)
    # the chest plate: a shell round the front of the chest, clear of the forelegs below
    c, r = Vector((0, -2.72, -0.95)), Vector((1.12, 1.04, 1.4))

    def on(az, el):
        az, el = math.radians(az), math.radians(el)
        return c + Vector((r.x * math.sin(az) * math.cos(el), -r.y * math.cos(az) * math.cos(el), r.z * math.sin(el)))
    slab(b, [(c, [on(az, el) for az in range(-78, 79, 13)]) for el in range(-38, 43, 10)], 0.1, steel, gold)
    b.ico(0.32, on(0, 2) + Vector((0, -0.06, 0)), gold, subdiv=1)  # its boss: a gold sun
    for k in range(8):
        a = TAU * k / 8
        p = on(0, 2) + Vector((math.cos(a) * 0.5, -0.02, math.sin(a) * 0.5))
        b.beam(tuple(on(0, 2) + Vector((math.cos(a) * 0.25, -0.05, math.sin(a) * 0.25))), tuple(p), 0.07, gold, segs=3, r2=0.0)
    return b


# ---------- the neck and the head ----------

CREST = (Vector((0, -2.2, 1.05)), Vector((0, -3.75, 2.75)), Vector((0, -4.68, 2.78)))   # the neck's top, withers to poll
THROAT = (Vector((0, -3.45, -0.8)), Vector((0, -3.95, 0.55)), Vector((0, -4.52, 1.5)))  # its underside, chest to throat latch
HEAD = [  # t from the poll to the muzzle: half width, half height, how far the middle sits below the axis (the jowl)
    (0.0, 0.4, 0.62, 0.0), (0.16, 0.47, 0.66, 0.1), (0.34, 0.42, 0.5, 0.04), (0.62, 0.31, 0.37, -0.02),
    (0.86, 0.28, 0.33, 0.0), (0.97, 0.24, 0.27, 0.02)]
HEAD_AXIS = (CREST[2] + THROAT[2]) / 2  # the middle of the poll
HEAD_DIR = (MUZZLE - HEAD_AXIS).normalized()
HEAD_UP = Vector((0, HEAD_DIR.z, -HEAD_DIR.y)) * (1 if -HEAD_DIR.y > 0 else -1)  # toward the forehead
HEAD_LEN = (MUZZLE - HEAD_AXIS).length


def neck_at(t, off=0.0, n=12, a0=None, a1=None):
    top, bot = bezier(*CREST, t), bezier(*THROAT, t)
    up = (top - bot).normalized()
    w = 0.95 + (0.4 - 0.95) * t ** 0.9
    c = (top + bot) / 2
    return c, ring(c, X, up, w + off, (top - bot).length / 2 + off, n=n, p=2.2, a0=a0, a1=a1)


def head_at(t, off=0.0, n=12, a0=None, a1=None):
    w, h, below = smooth(HEAD, t)
    c = HEAD_AXIS + HEAD_DIR * (t * HEAD_LEN) - HEAD_UP * below
    return c, ring(c, X, HEAD_UP, w + off, h + off, n=n, p=2.2, a0=a0, a1=a1)


def eye(b, sx):
    # A big dark eye on the side of the head, just under the chamfron's edge: an almond standing out of the face, a white
    # glint at its front, a gold ring round it (the chamfron's eye guard). sx: +1 the left eye, -1 the right.
    t, th = 0.3, 10.0  # how far down the head, how far round it from the side (degrees)
    ang = th if sx > 0 else 180 - th
    c, (p0, on, p2) = head_at(t, n=3, a0=ang - 2 * sx, a1=ang + 2 * sx)
    along = (head_at(t + 0.02, n=3, a0=ang - 2 * sx, a1=ang + 2 * sx)[1][1] - on).normalized()  # toward the muzzle
    out = (on - c)
    up = (p2 - p0).normalized()
    out = (out - along * out.dot(along) - up * out.dot(up)).normalized()
    up = out.cross(along).normalized() * (1 if out.cross(along).z > 0 else -1)
    centre = on - out * 0.03

    def dome(at, w, h, depth, m, n=12, k=4):
        rings = [[at + along * (w * math.cos(math.pi / 2 * i / k) * math.cos(TAU * j / n)) + up * (h * math.cos(math.pi / 2 * i / k) * math.sin(TAU * j / n))
                  + out * (depth * math.sin(math.pi / 2 * i / k)) for j in range(n)] for i in range(k)]
        loft(b, rings + [[at + out * depth]], m)
    dome(centre, 0.2, 0.13, 0.13, dark)
    b.ico(0.045, centre + along * 0.07 + up * 0.05 + out * 0.115, shine, subdiv=1)
    tube(b, [centre + out * 0.03 + along * (0.26 * math.cos(TAU * j / 14)) + up * (0.18 * math.sin(TAU * j / 14)) for j in range(14)], 0.04, gold, segs=4, closed=True)


def neck_and_head():
    b = Builder("BossSteedHead", [coat, coat_lo, steel, steel_hi, gold, gold_glow, horn_mat, dark, shine])
    rings = [neck_at(k / 10)[1] for k in range(11)] + [head_at(k / 11 * 0.97)[1] for k in range(1, 12)] + [[MUZZLE + HEAD_DIR * 0.02]]
    loft(b, rings, coat, mf=shade(rings, coat, coat_lo, under=-0.55))
    a, d, hup, L = HEAD_AXIS, HEAD_DIR, HEAD_UP, HEAD_LEN
    for sx in (-1, 1):
        base = CREST[2] + Vector((sx * 0.2, -0.12, -0.05))
        b.beam(tuple(base), tuple(base + Vector((sx * 0.1, -0.16, 0.6))), 0.13, coat, segs=5, r2=0.0)  # an ear
        eye(b, sx)
        b.ico(0.06, MUZZLE - d * 0.2 + Vector((sx * 0.17, 0, 0)) + hup * 0.04, dark, subdiv=1)  # a nostril
    # the crinet: four plates down the top of the neck, each over the next
    for k in range(4):
        t0 = 0.12 + k * 0.2
        slab(b, [neck_at(t0 + 0.24 * i / 3, 0.06 + 0.03 * (k % 2), n=8, a0=38, a1=142) for i in range(4)], 0.09, steel, gold, 0.04)
    # the chamfron down the face, the horn out of its brow
    slab(b, [head_at(0.06 + 0.66 * i / 6, 0.05, n=8, a0=30, a1=150) for i in range(7)], 0.07, steel_hi, gold, 0.04)
    base = a + d * (0.3 * L) + hup * (smooth(HEAD, 0.3)[1] + 0.08)
    hdir = (d * math.cos(math.radians(HORN_TILT)) + hup * math.sin(math.radians(HORN_TILT))).normalized()
    side = hdir.cross(X).normalized()
    other = hdir.cross(side)
    rings = []
    for k in range(10):
        t = k / 9
        rad = 0.25 * (1 - t) ** 0.85
        tw = k * 0.6
        rings.append([base + hdir * (HORN_LEN * t) + (side * math.cos(tw + TAU * i / 6) + other * math.sin(tw + TAU * i / 6)) * rad for i in range(6)] if k < 9 else [base + hdir * HORN_LEN])
    loft(b, rings, horn_mat, tri=True)
    b.ico(0.3, base - hdir * 0.06, gold, subdiv=1)  # where it's set into the chamfron
    tube(b, [base + hdir * (HORN_LEN * t) + (side * math.cos(t * 9 + 0.3) + other * math.sin(t * 9 + 0.3)) * (0.25 * (1 - t) ** 0.85 + 0.015) for t in (k / 20 * 0.92 for k in range(21))],
         [0.035 * (1 - k / 21) for k in range(21)], gold, segs=3)
    # the mane: tongues of gold fire along the crest, leaning back, and a forelock past the horn
    for k in range(15):
        t = 0.04 + 0.92 * k / 14
        c, top = neck_at(t)[0], bezier(*CREST, t)
        up = (top - c).normalized()
        back = Vector((0, 1, 0)) - up * up.y
        back.normalize()
        L = 1.0 + 0.5 * math.sin(math.pi * t)
        sx = 1 if k % 2 else -1
        root = top + up * 0.06 + Vector((sx * 0.12, 0, 0))
        out = Vector((sx, 0, 0))
        pts = [root - back * 0.3, root + up * L * 0.45 + back * 0.05 + out * 0.15, root + up * L * 0.7 + back * L * 0.45 + out * 0.35,
               root + up * L * 0.5 + back * L * 0.95 + out * 0.55, root + up * L * 0.25 + back * L * 0.6 + out * 0.4, root + back * 0.35 + out * 0.1]
        fin(b, pts, Vector((1, 0, 0.2 * sx)), [0.14, 0.1, 0.06, 0.02, 0.06, 0.14], gold_glow)
    for sx in (-1, 1):
        root = CREST[2] + Vector((sx * 0.16, -0.25, 0.1))
        pts = [root, root + d * 0.5 + Vector((sx * 0.25, 0, 0.2)), root + d * 0.95 + Vector((sx * 0.35, 0, -0.1)), root + d * 0.45 + Vector((sx * 0.2, 0, -0.1))]
        fin(b, pts, Vector((1, 0, 0)), [0.1, 0.06, 0.02, 0.06], gold_glow)
    return b


# ---------- the legs ----------

def leg_part(name, a, b_, prof):
    # A leg piece from joint a down to b_ (both at x = 0): prof (t, half width, half depth, forward offset) along it.
    b = Builder(name, [coat, coat_lo, gold, feather])
    d = (b_ - a).normalized()
    L = (b_ - a).length
    fwd = Vector((0, -1, 0))
    fwd = (fwd - d * fwd.dot(d)).normalized()
    side = d.cross(fwd).normalized()
    rings = [ring(a + d * (t * L) + fwd * off, side, fwd, w, dep, n=10, p=2.0) for t, w, dep, off in prof]
    loft(b, rings, coat)
    return b


def hoof(b, top, r_top, r_bot, h):
    # a gold hoof under `top` (its coronet), flat underneath, a little longer at the toe
    rings = [[top + Vector((math.cos(a) * r, math.sin(a) * r * 1.12 - 0.03 * (1 - math.sin(a)), z)) for a in (TAU * (i + 0.5) / 10 for i in range(10))]
             for z, r in ((0.0, r_top), (-h * 0.5, (r_top + r_bot) / 2), (-h, r_bot))]
    loft(b, rings, gold)


def fringe(b, at):
    # white hair over the hoof, as a draught horse has
    for k in range(9):
        a = TAU * (k + 0.5) / 9
        out = Vector((math.cos(a), math.sin(a), 0))
        b.beam(tuple(at + out * 0.18), tuple(at + out * 0.36 + Vector((0, 0, -0.42))), 0.08, feather, segs=3, r2=0.0)


def fore_arm():
    return leg_part("BossSteedForeArm", SHOULDER, KNEE,
                    [(0.0, 0.5, 0.6, 0.0), (0.3, 0.48, 0.55, -0.05), (0.45, 0.4, 0.48, -0.02), (0.8, 0.27, 0.32, 0.0), (1.0, 0.25, 0.29, 0.0)])


def shin(name, top, hoof_at, prof):
    b = leg_part(name, top, hoof_at + Vector((0, 0, 0.36)), prof)
    hoof(b, hoof_at + Vector((0, -0.06, 0.36)), 0.25, 0.33, 0.36)
    fringe(b, hoof_at + Vector((0, -0.03, 0.62)))
    return b


def thigh():
    return leg_part("BossSteedThigh", HIP, HOCK,
                    [(0.0, 0.62, 1.05, -0.1), (0.28, 0.6, 0.95, -0.15), (0.5, 0.44, 0.62, 0.02), (0.8, 0.3, 0.4, 0.08), (1.0, 0.25, 0.36, 0.12)])


# ---------- the tail and the wings ----------

def tail():
    b = Builder("BossSteedTail", [gold_glow])
    for k in range(11):
        s = (k - 5) / 5
        p0 = DOCK + Vector((s * 0.14, 0.02, -0.05))
        p1 = DOCK + Vector((s * 0.35, 1.0, -0.5))
        p2 = DOCK + Vector((s * 0.55 + random.uniform(-0.1, 0.1), 1.5 + random.uniform(-0.2, 0.2), -2.1 - abs(s) * 0.25))
        p3 = DOCK + Vector((s * 0.6, 1.3 + random.uniform(-0.2, 0.3), -3.3 + abs(s) * 0.55 + random.uniform(-0.2, 0.2)))
        tube(b, [bezier(p0, p1, p2, t) for t in (0, 0.33, 0.66, 1.0)] + [p3], [0.22, 0.27, 0.22, 0.14, 0.0], gold_glow, segs=5)
    return b


def wing():
    # Spread: the arm reaches out and up from the shoulder, the feathers sweep back from it; white, the primaries tipped gold.
    b = Builder("BossSteedWing", [feather, feather_lo, gold, steel])
    P = WING
    s = Vector((1, 0.08, 0.42)).normalized()  # the span: out, a little back, up
    c = Vector((0, 1, -0.25))
    c = (c - s * c.dot(s)).normalized()  # the chord: back
    n = s.cross(c).normalized()  # the wing's upper face
    wrist, tip = P + s * 2.5 - c * 0.25, P + s * 5.4 + c * 0.6
    tube(b, [P - s * 0.3, P + s * 1.2 - c * 0.2, wrist, P + s * 3.9 + c * 0.12, tip], [0.36, 0.3, 0.22, 0.13, 0.02], feather, segs=6)

    def blade(root, dirv, length, width, m, lift, curl=0.0):
        # a feather: a long rounded leaf, its tip curling up off the wing by curl
        sd = n.cross(dirv).normalized()
        pts = [(-0.3, 0.0), (-0.5, 0.3), (-0.32, 0.86), (0.0, 1.0), (0.32, 0.86), (0.5, 0.3), (0.3, 0.0)]
        fin(b, [root + sd * (width * w) + dirv * (length * a) + n * (lift + curl * a * a) for w, a in pts], n, 0.07, m)

    # the web the feathers lie on
    fin(b, [P - c * 0.1, wrist - c * 0.05, tip - c * 0.1, tip + c * 1.6 - s * 0.4, wrist + c * 1.9, P + c * 1.9], n, 0.05, feather_lo)
    for k in range(9):  # primaries, from the tip in toward the wrist, turning from outward to back; gold-tipped both sides
        u = k / 8
        root = tip.lerp(wrist, u)
        ang = math.radians(12 + 70 * u)
        dirv = (s * math.cos(ang) + c * math.sin(ang)).normalized()
        L = 2.4 + 0.8 * math.sin(math.pi * min(1.0, u * 1.25))
        blade(root, dirv, L * 0.72, 0.85, feather, 0.04 - 0.012 * k, 0.12)
        blade(root + dirv * L * 0.55, dirv, L * 0.45, 0.7, gold, 0.04 - 0.012 * k + 0.07, 0.25)
    for k in range(9):  # secondaries along the arm, back
        root = P.lerp(wrist, 0.06 + 0.94 * k / 8)
        dirv = (c + s * 0.22 * (k / 8)).normalized()
        blade(root, dirv, 2.3 + 0.3 * math.sin(math.pi * k / 8), 0.85, feather_lo if k % 2 else feather, -0.03 - 0.008 * k, 0.15)
    for row, (L, lift) in enumerate(((1.4, 0.12), (0.8, 0.2), (1.1, -0.17))):  # coverts: two rows over the front, one under it
        for k in range(10):
            root = (P - s * 0.1).lerp(tip, 0.03 + 0.85 * k / 9) + c * (0.1 * (row % 2))
            dirv = (c + s * 0.3).normalized()
            blade(root, dirv, L * (1 - 0.3 * k / 9), 0.62, feather if (k + row) % 2 else feather_lo, lift)
    # armour on the arm by the shoulder: a steel bracer between gold bands
    arm = [P - s * 0.25, P + s * 0.7 - c * 0.12, P + s * 1.5 - c * 0.2]
    tube(b, arm, [0.44, 0.42, 0.36], steel, segs=8)
    for at, r in ((arm[0] + s * 0.02, 0.47), (arm[2], 0.39)):
        tube(b, [at - s * 0.06, at + s * 0.06], [r, r], gold, segs=8)
    return b


def place(b, pivot):
    # the part, drawn in place, moved so its joint is its origin
    ob = b.finish()
    ob.data.transform(Matrix.Translation(-pivot))
    return ob


parts = [
    place(body(), Vector()),
    place(neck_and_head(), NECK),
    place(tail(), DOCK),
    place(fore_arm(), SHOULDER),
    place(shin("BossSteedForeShin", KNEE, FORE_HOOF, [(0.0, 0.26, 0.29, 0.0), (0.12, 0.2, 0.24, 0.0), (0.5, 0.16, 0.2, 0.01), (0.72, 0.22, 0.26, 0.03),
                                                       (0.86, 0.19, 0.22, -0.02), (1.0, 0.24, 0.27, -0.06)]), KNEE),
    place(thigh(), HIP),
    place(shin("BossSteedHindShin", HOCK, HIND_HOOF, [(0.0, 0.26, 0.36, 0.06), (0.15, 0.2, 0.26, 0.0), (0.55, 0.16, 0.2, 0.0), (0.74, 0.22, 0.26, 0.02),
                                                       (0.87, 0.19, 0.22, -0.02), (1.0, 0.24, 0.27, -0.06)]), HOCK),
    place(wing(), WING),
]
if "--" in sys.argv and "nosave" in sys.argv[sys.argv.index("--") + 1:]:
    print("BUILT", [p.name for p in parts])
else:
    export(parts)
