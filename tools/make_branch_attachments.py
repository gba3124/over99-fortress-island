# Branch ornaments: what a turret grows when its upgrade path changes (Owen: different paths should look different).
# Authored around the head origin (front -Y, up z), attached to the head at runtime by BuildSystem.SetTurretLook.
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_branch_attachments.py
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bkit import Builder, M, export, mat, tube, torus

iron, brass, steel, dark, crystal, copper = (M(n) for n in ("Iron", "Brass", "Steel", "DarkWood", "Crystal", "Copper"))
amber = mat("BranchAmber", (1.0, 0.62, 0.16), (0.9, 0.45, 0.05))
cyan = mat("BranchCyan", (0.35, 0.85, 1.0), (0.2, 0.7, 1.0))
ice = mat("BranchIce", (0.72, 0.95, 1.0), (0.35, 0.8, 1.0))
red = mat("BranchRed", (0.85, 0.22, 0.16), (0.6, 0.1, 0.05))
teal = mat("BranchTeal", (0.3, 1.0, 0.72), (0.1, 0.8, 0.5))
out, TAU = [], 2 * math.pi


def ring(r, n, z, phase=0.0):
    return [(r * math.cos(phase + i * TAU / n), r * math.sin(phase + i * TAU / n), z) for i in range(n)]


def bolts(b, r, n, z, size=0.04, m=brass):
    for p in ring(r, n, z):
        b.box((size, size, size), p, m)


# ---------- first branch A: amber swept fins + a spike ----------
a = Builder("BranchA", [iron, brass, amber, crystal])
a.cyl(0.26, 0.06, (0, 0.08, 0.26), iron, segs=24)
bolts(a, 0.2, 6, 0.3, 0.03)
for s in (-1, 1):
    a.beam((s * 0.16, 0.06, 0.3), (s * 0.46, 0.2, 0.62), 0.04, amber, segs=5, r2=0.012)  # swept fin
    a.beam((s * 0.3, 0.14, 0.46), (s * 0.54, 0.28, 0.74), 0.022, brass, segs=4)
a.beam((0, 0.08, 0.3), (0, 0.02, 0.74), 0.035, iron, segs=6, r2=0.015)                    # spine
a.ico(0.07, (0, 0.02, 0.78), crystal, subdiv=1)
a.cyl(0.05, 0.06, (0, 0.02, 0.7), brass, segs=8)
out.append(a.finish())

# ---------- first branch B: cyan armour wings + rail prongs ----------
b = Builder("BranchB", [iron, steel, cyan, brass])
b.cyl(0.28, 0.06, (0, 0.06, 0.26), iron, segs=24)
bolts(b, 0.21, 6, 0.3, 0.03)
for s in (-1, 1):
    b.box((0.2, 0.34, 0.08), (s * 0.3, 0.12, 0.46), steel, rot=(0, 0, s * 0.35))
    b.box((0.06, 0.3, 0.05), (s * 0.4, 0.16, 0.56), cyan, rot=(0, 0, s * 0.35))
    b.beam((s * 0.4, 0.16, 0.56), (s * 0.15, 0.1, 0.72), 0.022, cyan, segs=4)
b.beam((0, 0.06, 0.3), (0, 0.0, 0.6), 0.04, iron, segs=6)
b.ico(0.08, (0, 0.0, 0.74), cyan, subdiv=1)
out.append(b.finish())

# ---------- A1 (Storm): turbine crown + twin vents ----------
a1 = Builder("BranchA1", [iron, brass, amber, crystal])
a1.cyl(0.3, 0.07, (0, 0.04, 0.28), iron, segs=20)
bolts(a1, 0.23, 8, 0.32, 0.03)
a1.cone(0.1, 0.34, 0.16, (0, 0.04, 0.42), brass, segs=20)
a1.cyl(0.06, 0.2, (0, 0.04, 0.52), amber, segs=22)
for s in (-1, 1):
    a1.cyl(0.07, 0.26, (s * 0.26, 0.12, 0.48), amber, segs=8, axis="Y")
    a1.cyl(0.085, 0.04, (s * 0.26, 0.25, 0.48), brass, segs=8, axis="Y")
    a1.beam((s * 0.3, 0.1, 0.4), (s * 0.5, 0.24, 0.7), 0.02, brass, segs=4)
for k in range(4):
    p = ring(0.1, 4, 0.56)[k]
    a1.beam(p, (p[0] * 1.8, p[1] * 1.8, 0.8), 0.018, amber, segs=4)
a1.ico(0.09, (0, 0.04, 0.84), amber, subdiv=1)
out.append(a1.finish())

# ---------- A2 (Hailstorm): ice crystal cluster + frost ring ----------
a2 = Builder("BranchA2", [iron, brass, ice, cyan, crystal])
a2.cyl(0.3, 0.07, (0, 0.04, 0.28), iron, segs=20)
bolts(a2, 0.23, 8, 0.32, 0.03)
torus(a2, (0, 0.04, 0.44), (0, 0, 1), 0.27, 0.022, ice, segs=24, tsegs=5)
for s in (-1, 0, 1):
    a2.beam((s * 0.1, 0.04, 0.32), (s * 0.22, 0.02, 0.78 + abs(s) * 0.08), 0.045, ice, segs=6, r2=0.008)  # shards
    a2.box((0.06, 0.06, 0.06), (s * 0.1, 0.04, 0.34), brass)
for s in (-1, 1):
    a2.beam((s * 0.24, 0.04, 0.36), (s * 0.38, 0.16, 0.6), 0.025, cyan, segs=4)
out.append(a2.finish())

# ---------- B1 (Railgun/Siege): heavy rail + red armour ----------
b1 = Builder("BranchB1", [iron, steel, red, brass, dark])
b1.cyl(0.3, 0.07, (0, 0.04, 0.28), iron, segs=20)
bolts(b1, 0.23, 8, 0.32, 0.035)
for s in (-1, 1):
    b1.box((0.1, 0.6, 0.14), (s * 0.22, -0.04, 0.46), steel, rot=(0.08, 0, 0))
    b1.box((0.07, 0.4, 0.06), (s * 0.22, -0.32, 0.46), red)
    b1.beam((s * 0.22, 0.1, 0.36), (s * 0.22, -0.4, 0.52), 0.03, brass, segs=4)
b1.box((0.22, 0.5, 0.1), (0, 0.2, 0.5), red)
b1.beam((0, 0.04, 0.34), (0, 0.0, 0.78), 0.045, steel, segs=6, r2=0.02)
b1.box((0.12, 0.16, 0.08), (0, 0.0, 0.8), brass)
out.append(b1.finish())

# ---------- B2 (Shockwave/Arc): coil crown + green orbs ----------
b2 = Builder("BranchB2", [iron, brass, teal, crystal, copper])
b2.cyl(0.3, 0.07, (0, 0.04, 0.28), iron, segs=20)
bolts(b2, 0.23, 8, 0.32, 0.03)
b2.cyl(0.08, 0.5, (0, 0.04, 0.56), copper, segs=8)
for z in (0.4, 0.52, 0.64):
    torus(b2, (0, 0.04, z), (0, 0, 1), 0.22, 0.02, teal, segs=24, tsegs=5)
for k in range(3):
    p = ring(0.24, 3, 0.66)[k]
    b2.ico(0.07, p, teal, subdiv=1)
    b2.beam((0, 0.04, 0.66), p, 0.015, copper, segs=4)
b2.ico(0.09, (0, 0.04, 0.86), teal, subdiv=2)
out.append(b2.finish())

export(out)
