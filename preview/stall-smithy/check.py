# What the C# relies on in the two stations (tools/make_station_models.py), checked on the exported OBJs:
#   python3 preview/stall-smithy/check.py [HtfFortress/Models]
# Counts and bounds; the keepers' spots clear (FortressIsland.cs); the counter's height; the lights not buried; the smithy's
# rack slots and price board clear (Village.cs WeaponRack); the gull nest (FortressIsland.NestSpots) resting on the smithy's
# roof with nothing over it. Everything in Blender's frame: x right, -y toward the customers, z up.
import math
import os
import sys

DIR = sys.argv[1] if len(sys.argv) > 1 else "HtfFortress/Models"


def load(name):
    v, tris = [], []
    for line in open(os.path.join(DIR, name + ".obj")):
        p = line.split()
        if p and p[0] == "v":
            v.append((float(p[1]), -float(p[3]), float(p[2])))  # the OBJ's (x, y up, z ahead) back to Blender's
        elif p and p[0] == "f":
            idx = [int(q.split("/")[0]) - 1 for q in p[1:]]
            tris += [(v[idx[0]], v[idx[i]], v[idx[i + 1]]) for i in range(1, len(idx) - 1)]
    return v, tris


def clip(poly, a, val, keep_above):
    out = []
    for i, p in enumerate(poly):
        q = poly[(i + 1) % len(poly)]
        pin, qin = (p[a] >= val) == keep_above, (q[a] >= val) == keep_above
        if pin:
            out.append(p)
        if pin != qin:
            t = (val - p[a]) / (q[a] - p[a])
            out.append(tuple(p[k] + (q[k] - p[k]) * t for k in range(3)))
    return out


def in_box(tri, lo, hi):  # does any of the triangle reach into the box?
    poly = list(tri)
    for a in range(3):
        poly = clip(clip(poly, a, lo[a], True), a, hi[a], False) if poly else poly
    return bool(poly)


def in_column(tri, x, y, r, z0, z1):  # does any of it come within r of the vertical line at (x, y), between z0 and z1?
    poly = clip(clip(list(tri), 2, z0, True), 2, z1, False)
    if not poly:
        return False
    inside = False
    for i, p in enumerate(poly):
        q = poly[(i + 1) % len(poly)]
        if (p[1] > y) != (q[1] > y) and x < p[0] + (y - p[1]) * (q[0] - p[0]) / (q[1] - p[1]):
            inside = not inside
        dx, dy = q[0] - p[0], q[1] - p[1]
        t = max(0.0, min(1.0, ((x - p[0]) * dx + (y - p[1]) * dy) / (dx * dx + dy * dy or 1)))
        if math.hypot(p[0] + dx * t - x, p[1] + dy * t - y) < r:
            return True
    return inside


def near(tris, c, r):  # anything within the cube of half size r round c?
    return any(in_box(t, [c[k] - r for k in range(3)], [c[k] + r for k in range(3)]) for t in tris)


def heights(tris, x, y):  # the heights of every surface over (x, y)
    out = []
    for a, b, c in tris:
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-12:
            continue
        l1 = ((b[1] - c[1]) * (x - c[0]) + (c[0] - b[0]) * (y - c[1])) / d
        l2 = ((c[1] - a[1]) * (x - c[0]) + (a[0] - c[0]) * (y - c[1])) / d
        if l1 >= 0 and l2 >= 0 and l1 + l2 <= 1:
            out.append(l1 * a[2] + l2 * b[2] + (1 - l1 - l2) * c[2])
    return sorted(out)


ok = True


def expect(cond, what):
    global ok
    ok &= bool(cond)
    print(("  ok  " if cond else "  FAIL") + " " + what)


OLD = {"MarketStall": ((-2.325, -1.502, -0.03), (1.73, 2.023, 3.51)), "Smithy": ((-2.3, -1.779, -0.12), (2.3, 1.879, 4.2))}
for name in ("MarketStall", "Smithy"):
    v, tris = load(name)
    lo = [min(p[k] for p in v) for k in range(3)]
    hi = [max(p[k] for p in v) for k in range(3)]
    print(f"{name}: {len(v)} verts, {len(tris)} tris ({3 * len(tris)} verts flat-shaded in the game); "
          f"x {lo[0]:.2f}..{hi[0]:.2f} y {lo[1]:.2f}..{hi[1]:.2f} z {lo[2]:.2f}..{hi[2]:.2f}")
    o_lo, o_hi = OLD[name]
    expect(all(lo[k] > o_lo[k] - 0.1 and hi[k] < o_hi[k] + 0.1 for k in range(3)), "within 10 cm of the old bounds")
    if name == "MarketStall":
        expect(not any(in_column(t, 0.0, 0.4, 0.28, 0.15, 1.9) for t in tris), "the keeper's spot (0, 0.4) clear")
        for x in (-0.95, 0.95):
            top = heights([t for t in tris if max(p[2] for p in t) < 1.3], x, -0.2)[-1]
            expect(abs(top - 0.995) < 0.005, f"the counter's top at x {x}: {top:.3f}")
        expect(not near(tris, (0, -0.6, 2.15), 0.05), "the stall's light (0, -0.6, 2.15) in the open")
    else:
        expect(not any(in_column(t, 0.4, 0.55, 0.28, 0.15, 1.9) for t in tris), "the smith's spot (0.4, 0.55) clear")
        expect(not near(tris, (-1.25, 0.9, 1.25), 0.1), "the forge's light (-1.25, 0.9, 1.25) in the open")
        expect(not near(tris, (0, -0.2, 2.3), 0.05), "the smithy's light (0, -0.2, 2.3) in the open")
        slots = [(-0.15 + k * 0.33, z) for z in (2.4, 1.85, 1.3) for k in range(7)]
        expect(not any(in_box(t, (x - 0.16, 1.0, z + 0.075), (x + 0.16, 1.3, z + 0.25)) for t in tris for x, z in slots), "the rack's 21 slots clear")
        expect(not any(in_box(t, (0.1, 1.27, 2.66), (1.6, 1.33, 2.87)) for t in tris), "the price board's place clear (its top 3 cm, as always, behind a rafter)")
        # The nest: a 0.16 m ring at (1.1, 0.5, 2.881) tilted 10 degrees down to the front, on a roof board, nothing over it.
        tilt, off, over = math.radians(10), 0.0, 0
        for k in range(13):
            a = k * math.pi / 6
            lx, lz = (0.16 * math.cos(a), 0.16 * math.sin(a)) if k < 12 else (0, 0)
            px, py, pz = 1.1 - lx, 0.5 - lz * math.cos(tilt), 2.881 - lz * math.sin(tilt)
            hs = heights(tris, px, py)
            off = max(off, min(abs(h - pz) for h in hs))
            over += sum(h > pz + 0.03 for h in hs)
        expect(off < 0.03 and over == 0, f"the gull's nest on the roof: off {off * 100:.1f} cm, {over} over it")
print("ALL OK" if ok else "SOMETHING FAILED")
sys.exit(0 if ok else 1)
