# Gear for the newer enemies, worn on the fish's back (origin = top of the fish, front = -Y, about 1 unit = fish width).
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_enemy_gear.py
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bkit import Builder, M, export, mat

iron, wood, dark, rope, gold, steel, moss, crystal, leaf, slime, spark = (
    M(n) for n in ("Iron", "Wood", "DarkWood", "Rope", "Gold", "Steel", "Moss", "Crystal", "Leaf", "Slime", "Spark"))
cloth = mat("Cloth", (0.55, 0.42, 0.28))
hat = mat("HatYellow", (0.95, 0.74, 0.18))
heal = mat("HealGlow", (0.45, 1.0, 0.5), (0.3, 1.0, 0.4))
coral = mat("Coral", (0.95, 0.45, 0.55))
pearl = mat("Pearl", (0.95, 0.94, 0.9))
conch = mat("Conch", (0.95, 0.8, 0.66))
red = mat("Bandana", (0.8, 0.2, 0.18))
out = []
TAU = 2 * math.pi

# Thief: a fat loot sack on its back with coins spilling out, and a red bandana knot.
g = Builder("ThiefSack", [cloth, rope, gold, red])
g.ico(0.3, (0, 0.22, 0.26), cloth, subdiv=1)
g.ico(0.22, (0.08, 0.3, 0.46), cloth, subdiv=1)
g.cyl(0.09, 0.12, (0.05, 0.36, 0.64), rope, segs=6)
g.ico(0.08, (0.05, 0.38, 0.73), cloth)
for i, (x, y, z) in enumerate(((-0.18, 0.02, 0.5), (0.24, 0.12, 0.42), (-0.05, -0.08, 0.58))):
    g.cyl(0.07, 0.025, (x, y, z), gold, segs=8, axis="X" if i % 2 else "Y")
g.box((0.5, 0.12, 0.08), (0, -0.25, 0.06), red)
g.beam((0.18, -0.2, 0.08), (0.34, -0.05, 0.28), 0.04, red, segs=4)
out.append(g.finish())

# Shield bearer: a big round wooden shield held in front, iron rim and boss.
g = Builder("ShieldBoard", [wood, iron, dark, steel])
g.cyl(0.52, 0.08, (0, -0.62, -0.22), wood, segs=12, axis="Y")
g.cyl(0.55, 0.05, (0, -0.6, -0.22), iron, segs=12, axis="Y")
for x in (-0.18, 0.18):
    g.box((0.05, 0.1, 0.98), (x, -0.67, -0.22), dark)
g.ico(0.11, (0, -0.7, -0.22), iron)
g.beam((0, -0.75, -0.22), (0, -0.98, -0.22), 0.05, steel, segs=4, r2=0.0)
g.beam((0, -0.5, 0.0), (0, -0.1, 0.08), 0.04, dark, segs=4)  # strap to the body
out.append(g.finish())

# Digger: a yellow hard hat with a lamp, and a pickaxe on its back.
g = Builder("MinerHat", [hat, spark, iron, wood])
g.cyl(0.34, 0.05, (0, -0.05, 0.03), hat, segs=12)
g.cone(0.27, 0.12, 0.22, (0, -0.05, 0.16), hat, segs=12)
g.box((0.06, 0.5, 0.06), (0, -0.05, 0.28), hat)
g.cyl(0.07, 0.06, (0, -0.33, 0.14), spark, segs=8, axis="Y")
g.cyl(0.09, 0.04, (0, -0.3, 0.14), iron, segs=8, axis="Y")
g.beam((-0.3, 0.35, 0.05), (0.25, 0.25, 0.45), 0.035, wood, segs=4)
g.beam((0.17, 0.18, 0.55), (0.37, 0.36, 0.3), 0.04, iron, segs=4, r2=0.01)
out.append(g.finish())

# Healer: a staff with a glowing green crystal, and a leafy wreath.
g = Builder("HealerStaff", [wood, heal, leaf, gold])
g.beam((0.12, 0.2, 0.0), (0.2, 0.35, 1.05), 0.03, wood, segs=6)
g.cyl(0.07, 0.05, (0.2, 0.35, 1.06), gold, segs=8)
g.beam((0.2, 0.35, 1.08), (0.2, 0.35, 1.38), 0.1, heal, segs=6, r2=0.0)
g.beam((0.2, 0.35, 1.08), (0.2, 0.35, 0.98), 0.1, heal, segs=6, r2=0.0)
for i in range(8):
    a = i * TAU / 8
    g.box((0.14, 0.08, 0.05), (0.3 * math.cos(a), 0.3 * math.sin(a), 0.04), leaf, rot=(0, 0, a))
out.append(g.finish())

# Splitter: a cluster of green blobs on its back (it bursts into small ones).
g = Builder("SplitterBubbles", [slime])
for (x, y, z, r) in ((0, 0.1, 0.18, 0.24), (-0.22, 0.25, 0.12, 0.16), (0.2, 0.28, 0.14, 0.17), (0.04, -0.15, 0.1, 0.14), (0.0, 0.32, 0.36, 0.13)):
    g.ico(r, (x, y, z), slime, subdiv=1)
out.append(g.finish())

# Coral Queen: a tall coral crown with pearls.
g = Builder("QueenCrown", [gold, coral, pearl])
g.cyl(0.36, 0.1, (0, 0, 0.05), gold, segs=12)
for i in range(7):
    a = i * TAU / 7
    base = (0.3 * math.cos(a), 0.3 * math.sin(a), 0.08)
    tip = (0.42 * math.cos(a), 0.42 * math.sin(a), 0.62 + 0.1 * (i % 2))
    g.beam(base, tip, 0.05, coral, segs=5, r2=0.025)
    mid = tuple(base[k] + (tip[k] - base[k]) * 0.55 for k in range(3))
    g.beam(mid, (mid[0] * 1.35, mid[1] * 1.35, mid[2] + 0.18), 0.03, coral, segs=4, r2=0.015)
    g.ico(0.05, tip, pearl)
g.ico(0.12, (0, 0, 0.32), pearl, subdiv=2)
out.append(g.finish())

# Tidecaller: a big conch on its back, seaweed hanging off it, a glowing blue orb.
g = Builder("TideHorn", [conch, moss, crystal, coral])
for i in range(5):
    r = 0.3 - i * 0.05
    g.cone(r, r - 0.04, 0.16, (0.05 * i, 0.12 + 0.07 * i, 0.12 + 0.13 * i), conch, segs=10)
g.beam((0.25, 0.45, 0.72), (0.38, 0.6, 0.95), 0.07, conch, segs=8, r2=0.0)
g.cone(0.14, 0.32, 0.22, (0, -0.12, 0.14), coral, segs=10, axis="-Y")
for i in range(6):
    a = i * TAU / 6
    p = (0.3 * math.cos(a), 0.3 * math.sin(a), 0.1)
    g.beam(p, (p[0] * 1.2, p[1] * 1.2, -0.25), 0.03, moss, segs=4, r2=0.01)
g.ico(0.13, (-0.2, 0.3, 0.55), crystal, subdiv=2)
out.append(g.finish())

export(out)
