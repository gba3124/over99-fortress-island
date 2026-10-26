# Builds the vault shed, the treasure chest and the loot gem into fortress_models.blend and exports each as OBJ.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_vault_models.py
# Blender axes: -Y is the front (it ends up facing the game's +Z, toward the dock path), Z is up, the base sits on z = 0.
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bkit import Builder, export, mat

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

# ---------- the vault: a storehouse you walk into (4.0 x 3.4, walls 2.6 tall), in the game's cabin style (tools/vkit.py) ----------
# An open doorway in the front (-Y, toward the road), a plank floor a step up, shelves of boxes and sacks round the walls,
# barrels, and the quartermaster's counter on the left inside (FortressIsland.cs puts him behind it; keep in step).
import vkit
from vkit import Frame
v = Builder("Vault", vkit.KIT)
F = Frame((0, 0, 0))
W, D, Z0, Z1 = 4.0, 3.4, 0.22, 2.6
HW, HD = W / 2, D / 2
DOOR = (-0.7, 0.7, Z0, Z0 + 2.15)                                            # the doorway: 1.4 wide, 2.15 tall
vkit.stones(v, F, -HW - 0.08, HW + 0.08, -HD - 0.08, HD + 0.08, 0.0, Z0, rows=1)
for k in range(int(W / 0.2)):                                                # the floor inside: boards
    x = -HW + 0.1 + (k + 0.5) * (W - 0.2) / int(W / 0.2)
    vkit.box(v, F, ((W - 0.2) / int(W / 0.2) - 0.01, D - 0.2, 0.04), (x, 0, Z0 + 0.02), vkit.plank if k % 2 else vkit.plank2)
vkit.box(v, F, (1.6, 0.5, 0.11), (0, -HD - 0.3, 0.055), vkit.stone)         # a step up to the doorway
# The walls, two boards thick (the planks face out; an inner face of boards so it reads from inside too).
vkit.plank_wall(v, F, -HW, HW, Z0, Z1, y=-HD, skip=[DOOR, (1.52, 1.9, 1.2, 1.7)])
vkit.plank_wall(v, F.sub((0, 0, 0), 180), -HW, HW, Z0, Z1, y=-HD)
for yaw in (90, -90):
    S = F.sub((0, 0, 0), yaw)
    vkit.plank_wall(v, S, -HD, HD, Z0, Z1, y=-HW, skip=[(-0.35, 0.35, 1.2, 1.8)])
    vkit.window(v, S, 0, 1.5, w=0.6, h=0.56, y=-HW - 0.02)
vkit.window(v, F, 1.71, 1.45, w=0.36, h=0.48, y=-HD - 0.02, shutters=False)
for x in (-HW, HW):
    for y in (-HD, HD):
        vkit.box(v, F, (0.16, 0.16, Z1 - Z0 - 0.04), (x, y, (Z0 + Z1) / 2 - 0.02), vkit.trim)
for dx in (-0.78, 0.78):                                                    # the doorway's frame
    vkit.box(v, F, (0.14, 0.22, 2.2), (dx, -HD, Z0 + 1.1), vkit.trim)
vkit.box(v, F, (1.74, 0.22, 0.14), (0, -HD, Z0 + 2.22), vkit.trim)
vkit.box(v, F, (W + 0.16, 0.12, 0.1), (0, -HD - 0.02, Z1 - 0.06), vkit.trim)
vkit.box(v, F, (W + 0.16, 0.12, 0.1), (0, HD + 0.02, Z1 - 0.06), vkit.trim)
vkit.gable_end(v, F, W, Z1, 0.9, -HD)
vkit.gable_end(v, F.sub((0, 0, 0), 180), W, Z1, 0.9, -HD)
vkit.gable_roof(v, F, W, D, Z1 + 0.05, 0.9, over=0.32, m=vkit.roofr, m2=vkit.roofr2, ridge_x=False)
for x in (-1.0, 0.0, 1.0):                                                  # beams across inside, a lantern from the middle one
    vkit.box(v, F, (0.12, D, 0.14), (x, 0, Z1 - 0.12), vkit.trim)
vkit.beam(v, F, (0, 0.2, Z1 - 0.2), (0, 0.2, Z1 - 0.45), 0.008, vkit.iron, segs=4)
vkit.lantern(v, F, (0, 0.2, Z1 - 0.62))
# The doors stand open against the front wall outside, the sign over the doorway.
for s_, x in ((-1, -1.1), (1, 1.1)):
    vkit.box(v, F, (0.66, 0.05, 2.05), (x, -HD - 0.1, Z0 + 1.05), vkit.plank)
    vkit.box(v, F, (0.6, 0.03, 0.09), (x, -HD - 0.14, Z0 + 0.3), vkit.iron)
    vkit.box(v, F, (0.6, 0.03, 0.09), (x, -HD - 0.14, Z0 + 1.8), vkit.iron)
vkit.box(v, F, (1.3, 0.06, 0.34), (0, -HD - 0.12, Z1 + 0.2), vkit.plank)
vkit.box(v, F, (1.38, 0.04, 0.42), (0, -HD - 0.09, Z1 + 0.2), vkit.trim)
# Its emblem: a brass shield with an iron cross, a strapped crate either side (the quartermaster's stores).
vkit.box(v, F, (0.3, 0.04, 0.27), (0, -HD - 0.16, Z1 + 0.2), vkit.brass)
vkit.box(v, F, (0.22, 0.05, 0.05), (0, -HD - 0.165, Z1 + 0.2), vkit.iron)   # a band across the shield
vkit.box(v, F, (0.05, 0.05, 0.19), (0, -HD - 0.165, Z1 + 0.2), vkit.iron)   # and a bolt down it
for x in (-0.42, 0.42):
    vkit.box(v, F, (0.2, 0.04, 0.2), (x, -HD - 0.16, Z1 + 0.2), vkit.plank2)
    for ry in (0.785, -0.785):
        vkit.box(v, F, (0.26, 0.045, 0.03), (x, -HD - 0.165, Z1 + 0.2), vkit.trim, ry=ry)
vkit.box(v, F, (0.06, 0.34, 0.06), (-1.66, -HD - 0.15, 2.1), vkit.trim)  # a lantern beside the doorway
vkit.lantern(v, F, (-1.66, -HD - 0.3, 1.86), 0.9)


def shelf(S, x0, x1, levels=(0.5, 1.15, 1.8)):
    # A wall of shelves (against local +y), boxes, sacks and jars on it.
    for z in levels:
        vkit.box(v, S, (x1 - x0, 0.42, 0.04), ((x0 + x1) / 2, 0.0, Z0 + z), vkit.plank)
        x = x0 + 0.08
        while x < x1 - 0.25:
            kind = vkit.rng.random()
            if kind < 0.45:
                s2 = vkit.rng.uniform(0.22, 0.34)
                vkit.box(v, S, (s2, 0.3, s2 * 0.8), (x + s2 / 2, 0.0, Z0 + z + 0.02 + s2 * 0.4), vkit.plank2 if kind < 0.25 else vkit.trim)
                x += s2 + 0.06
            elif kind < 0.75:
                vkit.ball(v, S, 0.13, (x + 0.13, 0.0, Z0 + z + 0.13), vkit.cream, subdiv=1)  # a sack
                x += 0.3
            else:
                vkit.cyl(v, S, 0.07, 0.06, 0.2, (x + 0.08, 0.0, Z0 + z + 0.12), vkit.glass, segs=8)  # a jar
                x += 0.18
    for x in (x0, x1):
        vkit.box(v, S, (0.06, 0.42, 1.95), (x, 0.0, Z0 + 0.98), vkit.trim)


shelf(F.sub((0, HD - 0.3, 0), 0), -1.8, 1.8)                                # the back wall
shelf(F.sub((HW - 0.3, 0, 0), -90), -1.35, 0.9)                             # the right wall
vkit.barrel(v, F, 0.26, 0.74, (-1.55, 1.3, Z0))
vkit.barrel(v, F, 0.26, 0.74, (-1.0, 1.35, Z0))
vkit.crate(v, F, 0.5, (1.45, -1.25, Z0), 8)
# The quartermaster's counter, left inside, facing the doorway (he stands behind it at (-1.3, 0.05)).
vkit.box(v, F, (1.3, 0.5, 1.0), (-1.35, -0.55, Z0 + 0.5), vkit.plank2)
vkit.box(v, F, (1.42, 0.62, 0.06), (-1.35, -0.55, Z0 + 1.03), vkit.trim)
vkit.box(v, F, (0.36, 0.26, 0.03), (-1.15, -0.6, Z0 + 1.07), vkit.cream)   # his ledger
vkit.box(v, F, (0.16, 0.24, 0.035), (-1.3, -0.6, Z0 + 1.08), vkit.trim)
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

export([chest, vault, gem])
