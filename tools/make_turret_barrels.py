# Extra barrels a turret grows at Lv5 / Lv10 / Lv15 (Owen: upgrades should make the gun bigger and give it more barrels):
# each is the turret's own kind of barrel/prong/coil, mounted beside the head by BuildSystem.SetTurretLook.
# Run: Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_turret_barrels.py
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bkit import Builder, M, export, mat, torus

iron, brass, steel, copper, coil, crystal, dark, wood, black, rope = (M(n) for n in ("Iron", "Brass", "Steel", "Copper", "Coil", "Crystal", "DarkWood", "Wood", "BombBlack", "Rope"))
glass = mat("Glass", (0.25, 0.55, 0.7), (0.12, 0.35, 0.5))
out, TAU = [], 2*math.pi

def ring(r,n,z,phase=0.0): return [(r*math.cos(phase+i*TAU/n), r*math.sin(phase+i*TAU/n), z) for i in range(n)]

# gatling cluster: six slim barrels round a spindle
g=Builder("GunBarrel",[iron,steel,brass,dark])
g.cyl(0.16,0.1,(0,0,0.05),iron,segs=22)
for p in ring(0.08,6,0.0):
    g.beam((p[0],0.0,0.16+p[1]),(p[0],-0.92,0.16+p[1]),0.026,steel,segs=8)
    g.cyl(0.045,0.04,(p[0],-0.93,0.16+p[1]),brass,segs=8,axis="Y")
g.beam((0,0,0.16),(0,-0.9,0.16),0.035,iron,segs=8)
for y in (-0.35,-0.7): g.cyl(0.11,0.05,(0,y,0.16),brass,segs=22,axis="Y")
out.append(g.finish())

# cannon barrel
c=Builder("CannonBarrel",[iron,brass,black])
c.cyl(0.16,0.12,(0,0,0.1),iron,segs=24)
c.cone(0.17,0.12,1.0,(0,-0.5,0.2),iron,segs=20,axis="-Y")
c.cyl(0.18,0.08,(0,-1.0,0.2),iron,segs=20,axis="Y")
c.cyl(0.09,0.02,(0,-1.045,0.2),black,segs=22,axis="Y")
for y in (-0.18,-0.62): c.cyl(0.19,0.05,(0,y,0.2),brass,segs=20,axis="Y")
out.append(c.finish())

# harpoon prong
h=Builder("HarpoonProng",[dark,steel,iron,rope])
h.box((0.12,0.5,0.1),(0,-0.2,0.12),dark)
h.beam((0,0.2,0.15),(0,-0.55,0.15),0.04,steel,segs=6)
h.beam((0,-0.55,0.15),(0,-0.85,0.15),0.08,steel,segs=4,r2=0.0)
for s in (-1,1): h.beam((0,-0.6,0.15),(s*0.1,-0.52,0.15),0.015,steel,segs=4)
h.cyl(0.06,0.18,(0,0.32,0.12),rope,segs=8,axis="X")
out.append(h.finish())

# mortar tube
m=Builder("MortarTube",[iron,brass,black])
m.cyl(0.2,0.1,(0,0,0.06),iron,segs=24)
m.beam((0,0,0.1),(0,-0.4,0.75),0.15,iron,segs=24,r2=0.13)
m.beam((0,-0.37,0.69),(0,-0.44,0.79),0.16,brass,segs=24)
m.beam((0,-0.43,0.77),(0,-0.45,0.8),0.1,black,segs=22)
for k in (0.3,0.6): m.beam((0,-0.18*k,0.1+0.65*k),(0,-0.15*k,0.1+0.65*k),0.155,brass,segs=24)
out.append(m.finish())

# tesla coil column
t=Builder("TeslaCoil",[iron,copper,coil,brass,crystal])
t.cyl(0.18,0.1,(0,0,0.06),iron,segs=22)
t.cyl(0.07,0.62,(0,0,0.4),copper,segs=8)
for z in (0.2,0.33,0.46,0.59): torus(t,(0,0,z),(0,0,1),0.13,0.016,coil,segs=20,tsegs=4)
t.ico(0.09,(0,0,0.74),crystal,subdiv=1)
out.append(t.finish())

# flame nozzle
f=Builder("FlameNozzle",[iron,brass,black,copper])
f.cyl(0.14,0.1,(0,0,0.05),iron,segs=22)
f.beam((0,0,0.12),(0,-0.5,0.16),0.07,iron,segs=8)
f.beam((0,-0.5,0.16),(0,-0.72,0.18),0.08,iron,segs=8,r2=0.12)
f.cyl(0.09,0.02,(0,-0.73,0.18),black,segs=8,axis="Y")
f.cyl(0.11,0.04,(0,-0.45,0.16),brass,segs=22,axis="Y")
out.append(f.finish())

# sniper barrel
s=Builder("SniperBarrel",[iron,steel,brass,black])
s.cyl(0.12,0.1,(0,0,0.08),iron,segs=22)
s.beam((0,0,0.13),(0,-1.15,0.13),0.04,steel,segs=22)
s.cyl(0.06,0.12,(0,-1.12,0.13),iron,segs=22,axis="Y")
for y in (-0.4,-0.8): s.cyl(0.055,0.03,(0,y,0.13),brass,segs=22,axis="Y")
out.append(s.finish())

# bubble emitter arm
b=Builder("BubbleArm",[iron,brass,copper,glass])
b.cyl(0.14,0.08,(0,0,0.05),iron,segs=22)
b.beam((0,0,0.1),(0,-0.35,0.4),0.04,copper,segs=6)
b.cyl(0.1,0.05,(0,-0.36,0.42),brass,segs=22,axis="Y")
b.ico(0.09,(0,-0.42,0.44),glass,subdiv=2)
out.append(b.finish())

# mender arm
md=Builder("MenderArm",[iron,brass,steel,copper,coil])
md.cyl(0.14,0.08,(0,0,0.05),iron,segs=22)
md.beam((0,0,0.12),(0,-0.18,0.55),0.04,steel,segs=6)
md.box((0.12,0.1,0.08),(0,-0.18,0.58),brass)
md.cyl(0.03,0.16,(0,-0.26,0.52),brass,segs=8,axis="X")
md.box((0.03,0.14,0.14),(0,-0.32,0.47),steel)
torus(md,(0,0,0.3),(0,0,1),0.12,0.015,coil,segs=24,tsegs=4)
out.append(md.finish())

export(out)
