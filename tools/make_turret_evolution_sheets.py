# Evolution-tree concept sheets for the nine turrets: base, first split A/B, and four independent second splits.
# Run: python3 tools/make_turret_evolution_sheets.py
import os, sys, math
from PIL import Image, ImageDraw, ImageFilter
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "thunderstore", "art"))
import ui
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
ICONS = os.path.join(ROOT, "HtfFortress", "Icons")
OUT = os.path.join(ROOT, "preview", "turret-evolution")
os.makedirs(OUT, exist_ok=True)
NAVY, GOLD, SAND, SAND2, SAND3, INK = ui.NAVY, ui.GOLD, ui.SAND, ui.SAND2, ui.SAND3, ui.INK
SEA, SEA2, WHITE, CORAL = ui.SEA, ui.SEA2, ui.WHITE, ui.CORAL
MUTED = (84, 100, 118); GREEN = (76, 184, 126)

def cover(im,w,h):
    k=max(w/im.width,h/im.height); im=im.resize((round(im.width*k),round(im.height*k)),Image.LANCZOS)
    return im.crop(((im.width-w)//2,(im.height-h)//2,(im.width+w)//2,(im.height+h)//2))

def paste(im,ch,xy,anchor="mm"):
    x,y=xy
    if anchor=="mm": x-=ch.width//2; y-=ch.height//2
    elif anchor=="lm": y-=ch.height//2
    elif anchor=="ma": x-=ch.width//2
    im.alpha_composite(ch,(x,y))

def card(w,h,fill=SAND,outline=SAND3,radius=20,width=3,shadow=True):
    im=Image.new("RGBA",(w+30,h+30),(0,0,0,0))
    if shadow:
        sh=Image.new("RGBA",im.size,(0,0,0,0)); ImageDraw.Draw(sh).rounded_rectangle((15,20,15+w,20+h),radius,fill=(8,24,40,90))
        im.alpha_composite(sh.filter(ImageFilter.GaussianBlur(9)))
    ImageDraw.Draw(im).rounded_rectangle((15,15,15+w,15+h),radius,fill=fill,outline=outline,width=width)
    return im

def icon(name,size=150):
    im=Image.open(os.path.join(ICONS,name+".png")).convert("RGBA")
    im.thumbnail((size,size),Image.LANCZOS)
    return im

def tri(d,xy,c,color):
    x,y=xy; d.polygon([(x,y-10),(x+15,y),(x,y+10)],fill=color)

def arrow(d,a,b,color=(232,196,112),width=3):
    d.line((a,b),fill=color,width=width)
    ang=math.atan2(b[1]-a[1],b[0]-a[0]); L=16
    for s in (-0.5,0.5):
        d.line((b[0]-math.cos(ang+s)*L,b[1]-math.sin(ang+s)*L,b[0],b[1]),fill=color,width=width)

CONF={
 "Gun":dict(file="PieceGun",name="MACHINE GUN",base="7",base_desc="tall square stone plinth + twin barrels + side ammo box",size=100,
   A=("STORM","Lv3: longer twin barrels, round ammo drum, orange heat fins"),
   B=("ARMOUR-PIERCING","Lv3: one long barrel, brass rail, heavier receiver"),
   A1=("TYPHOON","Lv9: top rotary feed carousel + extra short barrels + ammo belt"),
   A2=("HAILSTORM","Lv9: blue coolant jackets + side coolant tanks + frost muzzle"),
   B1=("RAILGUN","Lv9: twin electromagnetic rails + three copper coils + blue arc"),
   B2=("EXECUTIONER","Lv9: thick short barrel + huge muzzle brake + rear counterweight")),
 "Cannon":dict(file="PieceCannon",name="CANNON",base="1",base_desc="round stone/wood carriage + one huge central muzzle",size=100,
   A=("CLUSTER","Lv3: three short barrels in a row + side shell rack"),
   B=("HEAVY","Lv3: thicker longer barrel + recoil cylinders + heavy breech"),
   A1=("CARPET","Lv9: stacked mortar rack + upward muzzles + ammo belt"),
   A2=("NAPOLM CLUSTER","Lv9: red fuel tanks + orange hoses + three burn nozzles"),
   B1=("SIEGE","Lv9: longest barrel + front armour shield + big wheels/spades"),
   B2=("SHOCKWAVE","Lv9: wide muzzle brake + blast ring + extra-wide low base")),
 "Harpoon":dict(file="PieceHarpoon",name="HARPOON",base="self",base_desc="square stone base + wooden A-frame + winch + long spear rail",size=100,
   A=("MULTI","Lv3: three prongs in parallel + three small reels"),
   B=("FREEZE","Lv3: cold blue shaft + ice block around the head"),
   A1=("VOLLEY","Lv9: three separate spear rails + wide triple winch"),
   A2=("CHAIN","Lv9: chain spool + secondary small hook behind the main tip"),
   B1=("GLACIER","Lv9: huge ice crystal head + blue coolant pipes"),
   B2=("SHATTER","Lv9: three split ice blades + shattered-ice hopper")),
 "Mortar":dict(file="PieceMortar",name="MORTAR",base="self",base_desc="squat stone bunker + angled tube + shell crate",size=100,
   A=("FIRE","Lv3: side fuel tank + burning muzzle ring"),
   B=("BARRAGE","Lv3: three tubes/racks + loading arm"),
   A1=("NAPLAM","Lv9: orange fuel barrels + oil hoses + fire ring"),
   A2=("FIRESTORM","Lv9: twin tubes + powder kegs + black smoke hood"),
   B1=("GRAND BARRAGE","Lv9: three-tier shell rack + long loader arm"),
   B2=("CLUSTER SHELLS","Lv9: wide muzzle + cluster warhead + split ammo crate")),
 "Tesla":dict(file="PieceTesla",name="TESLA",base="self",base_desc="tall stone/iron tower + triple copper coil + top electrode",size=100,
   A=("SPREAD","Lv3: four discharge prongs + glass insulators"),
   B=("OVERLOAD","Lv3: large transformer + capacitor drums + red gauge"),
   A1=("STORM CHAIN","Lv9: huge top electrode + side insulator cluster + chain arcs"),
   A2=("CONDUCTION","Lv9: grounded chain + wet conduit ring + blue coil"),
   B1=("OVERCHARGE","Lv9: twin capacitor drums + overcharge gauge + vents"),
   B2=("ARC BLAST","Lv9: spherical arc cage + six radial electrodes + white core")),
 "Flame":dict(file="PieceFlame",name="FLAME",base="self",base_desc="low stone bunker + twin fuel tanks + short nozzle",size=100,
   A=("INFERNO","Lv3: taller fuel tanks + wider burner"),
   B=("WIDE","Lv3: horizontal wide nozzle + extended side pipes"),
   A1=("HELLFIRE","Lv9: three fuel drums + huge black nozzle + vent stack"),
   A2=("MELT","Lv9: long white-hot nozzle + molten slag trough"),
   B1=("WALL OF FIRE","Lv9: very wide mouth + side burner wings + fuel distributor"),
   B2=("BLUE FLAME","Lv9: blue tanks + white-blue nozzle + low wide base")),
 "Sniper":dict(file="PieceSniper",name="SNIPER",base="self",base_desc="tall stone tripod + very long barrel + big scope",size=100,
   A=("ARMOUR-BREAKER","Lv3: huge muzzle brake + long scope"),
   B=("RAPID","Lv3: side magazine + shorter barrel + bipod"),
   A1=("BOSS SLAYER","Lv9: extra-long heavy barrel + giant brake + oversized scope"),
   A2=("PIERCING SHOT","Lv9: rail-like barrel + piercing tip + blue sighting coil"),
   B1=("MACHINE SNIPER","Lv9: large side magazine + twin bipod + high-rate receiver"),
   B2=("MARK","Lv9: giant observation scope + marker lamp + top radar")),
 "Bubble":dict(file="PieceBubble",name="BUBBLE",base="self",base_desc="round stone pedestal + central glass-dome generator",size=100,
   A=("EXPANSE","Lv3: wider dome + four support arms"),
   B=("HARDENED","Lv3: metal shell + pressure rings"),
   A1=("DOME","Lv9: huge glass dome + four braces + top pressure ring"),
   A2=("WARD","Lv9: three concentric rings + bright core + rune base"),
   B1=("FORTRESS","Lv9: armoured housing + front window + heavy base"),
   B2=("AEGIS","Lv9: hexagonal shield plates + crystal tip + side stabilizers")),
 "Mender":dict(file="PieceMender",name="MENDER",base="self",base_desc="stone platform + articulated arms + green core + toolbox",size=100,
   A=("WIDE","Lv3: two repair arms + wide beam"),
   B=("SURGE","Lv3: green core + injector pipes"),
   A1=("GRAND MENDER","Lv9: dual arms + two welders + tool carousel"),
   A2=("CORE MENDER","Lv9: big chest core + front healing lens + beam emitter"),
   B1=("OVERHEAL","Lv9: tall green tanks + three injectors + overflow valve"),
   B2=("CLEANSE","Lv9: white filter drums + cleanse halo + spray arms")),
}

def node_shell(im,xy,w,h,title,sub,star=0,accent=GOLD):
    p=card(w,h); paste(im,p,xy)
    d=ImageDraw.Draw(im)
    x=xy[0]-w//2; y=xy[1]-h//2
    ui.text(im,(x+20,y+14),title,ui.body(19,"Bold"),fill=NAVY)
    ui.text(im,(x+20,y+42),sub,ui.body(13,"Medium"),fill=(78,88,102))
    if star:
        for i in range(star):
            cx=x+w-24-i*18; cy=y+24
            pts=[]
            for k in range(10):
                rr=7 if k%2==0 else 3
                a=-math.pi/2+k*math.pi/5; pts.append((cx+math.cos(a)*rr,cy+math.sin(a)*rr))
            d.polygon(pts,fill=accent,outline=(125,90,25))
    return (x,y)

def sheet(key):
    c=CONF[key]; W,H=1800,1000
    im=ui.vgrad(W,H,(22,56,96),(13,36,62))
    # soft circles
    d=ImageDraw.Draw(im)
    for cx,cy,r in ((1600,110,190),(1680,740,230),(100,850,160)):
        d.ellipse((cx-r,cy-r,cx+r,cy+r),fill=(64,132,180,20))
    ui.text(im,(48,34),c["name"]+"  —  UPGRADE TREE",ui.title(39),fill=GOLD)
    ui.text(im,(50,88),"Base -> first split A/B -> each has its own independent second split A1/A2 or B1/B2",ui.body(18,"Medium"),fill=(206,224,240))
    # base
    bx,by=285,470; bw,bh=420,330
    p=card(bw,bh,fill=(248,243,228),radius=22); paste(im,p,(bx,by))
    x=bx-bw//2; y=by-bh//2
    base_ic=icon(c["file"],210)
    paste(im,base_ic,(x+bw//2,y+150))
    ui.text(im,(x+bw//2,y+22),"BASE",ui.body(18,"Bold"),fill=NAVY,anchor="ma")
    ui.text(im,(x+26,y+50),"Lv1-2",ui.title(20),fill=GOLD)
    ui.text(im,(x+26,y+bh-58),c["base_desc"],ui.body(14,"Medium"),fill=(72,84,98))
    # A and B
    ax,ay=790,280; bx2,by2=790,700
    node_shell(im,(ax,ay),440,160,"A  "+c["A"][0],"Lv3",1,(255,176,60))
    ui.text(im,(ax-198,ay+55),c["A"][1],ui.body(12,"Medium"),fill=(76,86,100))
    node_shell(im,(bx2,by2),440,160,"B  "+c["B"][0],"Lv3",3,(100,210,240))
    ui.text(im,(bx2-198,by2+55),c["B"][1],ui.body(12,"Medium"),fill=(76,86,100))
    # final four
    finals=[("A1",c["A1"],1360,190,"A1"),("A2",c["A2"],1360,390,"A2"),("B1",c["B1"],1360,610,"B1"),("B2",c["B2"],1360,810,"B2")]
    for tag,(name,desc),nx,ny,lab in finals:
        w,h=(390,150)
        node_shell(im,(nx,ny),w,h,lab+"  "+name,"Lv9",4,GOLD if tag.startswith("A") else (100,210,240))
        ui.text(im,(nx-w//2+22,ny+48),desc,ui.body(12,"Medium"),fill=(76,86,100))
    # connectors
    arrow(d,(bx+bw//2+16,by-10),(ax-220,ay))
    arrow(d,(bx+bw//2+16,by+10),(bx2-220,by2))
    arrow(d,(ax+220,ay+20),(1360-195,190))
    arrow(d,(ax+220,ay+60),(1360-195,390))
    arrow(d,(bx2+220,by2-20),(1360-195,610))
    arrow(d,(bx2+220,by2+30),(1360-195,810))
    # size and ornament bar
    d.rounded_rectangle((48,928,1752,984),14,fill=(24,54,84,220),outline=(74,130,170),width=2)
    ui.text(im,(70,940),"SIZE",ui.body(14,"Bold"),fill=GOLD)
    ui.text(im,(130,940),"Base 100%  ·  Lv3 112%  ·  Lv9 130-145%",ui.body(14,"Medium"),fill=WHITE)
    ui.text(im,(660,940),"ORNAMENT",ui.body(14,"Bold"),fill=GOLD)
    ui.text(im,(790,940),"A: amber parts / ammo+heat  ·  B: cyan parts / armour+rail  ·  A1/A2/B1/B2 each gets its own silhouette",ui.body(14,"Medium"),fill=WHITE)
    im.convert("RGB").save(os.path.join(OUT,key.lower()+"-evolution.png"),optimize=True)
    print(key,"ok")

for key in CONF: sheet(key)
print("done",OUT)
