# Real-3D promo plates: composites of the actual rendered game models/scenes, not concept art.
# Run: python3 tools/make_real_promo.py
import os, sys, math
from PIL import Image, ImageDraw, ImageFilter, ImageChops
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "thunderstore", "art"))
import ui
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IMG = os.path.join(ROOT, "img")
A = os.path.join(ROOT, "preview", "promo3d")
T = os.path.join(A, "turrets")
TRAW = os.path.join(A, "turrets_raw")
W = os.path.join(A, "weapons")
S = os.path.join(ROOT, "preview", "scene_final")
H = os.path.join(ROOT, "preview", "helper-idle")
NAVY, GOLD, SAND, INK = ui.NAVY, ui.GOLD, ui.SAND, ui.INK
WHITE, CORAL = ui.WHITE, ui.CORAL

def cover(im, w, h):
    im = im.convert("RGB")
    k = max(w / im.width, h / im.height)
    im = im.resize((round(im.width * k), round(im.height * k)), Image.LANCZOS)
    return im.crop(((im.width - w) // 2, (im.height - h) // 2, (im.width + w) // 2, (im.height + h) // 2))

def cut(path):
    return Image.open(path).convert("RGBA")

def fit(im, box):
    im = im.copy(); im.thumbnail(box, Image.LANCZOS); return im

def shadow(base, cutout, center, scale=1.0, blur=18, alpha=115, offset=(0, 14)):
    im = cutout.copy()
    im.thumbnail((round(im.width * scale), round(im.height * scale)), Image.LANCZOS)
    mask = im.getchannel("A")
    sh = Image.new("RGBA", base.size, (0, 0, 0, 0))
    black = Image.new("RGBA", im.size, (0, 0, 0, alpha))
    sh.paste(black, (center[0] - im.width // 2 + offset[0], center[1] - im.height // 2 + offset[1]), mask)
    base.alpha_composite(sh.filter(ImageFilter.GaussianBlur(blur)))
    base.alpha_composite(im, (center[0] - im.width // 2, center[1] - im.height // 2))

def paste(im, cutout, center, maxw, maxh):
    c = fit(cutout, (maxw, maxh))
    im.alpha_composite(c, (center[0] - c.width // 2, center[1] - c.height // 2))

def rounded(im, radius):
    im = im.convert("RGBA"); mask = Image.new("L", im.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, im.width - 1, im.height - 1), radius, fill=255)
    im.putalpha(mask); return im

def card(base, x, y, w, h, fill=(242, 234, 215), outline=(190, 160, 96), radius=18):
    sh = Image.new("RGBA", base.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle((x + 8, y + 10, x + w + 8, y + h + 10), radius, fill=(10, 25, 42, 110))
    base.alpha_composite(sh.filter(ImageFilter.GaussianBlur(10)))
    d = ImageDraw.Draw(base); d.rounded_rectangle((x, y, x + w, y + h), radius, fill=fill, outline=outline, width=2)

def title(im, text, x, y, size, fill=WHITE, stroke=(21, 48, 78), align="la"):
    ui.text(im, (x, y), text, ui.title(size), fill=fill, anchor=align, stroke=max(2, size // 16), stroke_fill=stroke)

def chip(im, text, x, y, fill=(255, 180, 65)):
    d = ImageDraw.Draw(im); f = ui.body(18, "Bold"); tw = d.textlength(text, font=f)
    d.rounded_rectangle((x, y, x + tw + 34, y + 38), 12, fill=(*fill, 235))
    ui.text(im, (x + 17, y + 8), text, f, fill=INK)

# ---------- hero ----------
def hero():
    Wp, Hp = 1920, 1080
    bg = cover(Image.open(os.path.join(A, "island_close.png")), Wp, Hp).convert("RGBA")
    g = Image.new("RGBA", (Wp, Hp), (0, 0, 0, 0)); gd = ImageDraw.Draw(g)
    gd.rectangle((0, 0, Wp, 300), fill=(7, 25, 48, 155)); gd.rectangle((0, Hp - 160, Wp, Hp), fill=(5, 18, 32, 110))
    bg.alpha_composite(g)
    shadow(bg, cut(os.path.join(W, "Rocket.png")), (260, 940), 0.82, 22, 100, (0, 12))
    shadow(bg, cut(os.path.join(W, "Gatling.png")), (1660, 930), 0.72, 22, 100, (0, 12))
    shadow(bg, cut(os.path.join(W, "Boomerang.png")), (1630, 220), 0.60, 18, 80, (0, 8))
    chip(bg, "CO-OP FORTRESS DEFENSE  ·  100 WAVES", 74, 68)
    title(bg, "OVER 99", 88, 122, 128, (255, 200, 64), (17, 42, 70))
    title(bg, "FORTRESS ISLAND", 90, 250, 62, (255, 255, 255), (17, 42, 70))
    ui.text(bg, (94, 344), "BUILD. FISH. SURVIVE.", ui.title(28), fill=(255, 240, 190), stroke=2, stroke_fill=(17,42,70))
    ui.text(bg, (94, 390), "Raise the walls. Pick the turrets. Hold the lighthouse with friends.", ui.body(21, "Medium"), fill=(238,246,252), stroke=2, stroke_fill=(17,42,70))
    # corner badge
    chip(bg, "ACTUAL IN-GAME MODELS", 1460, 1010, (110, 220, 175))
    bg.convert("RGB").save(os.path.join(IMG, "promo-hero-0.3.0.png"), optimize=True)
    print("hero")

# ---------- turrets ----------
def turrets():
    Wp, Hp = 1600, 900
    bg = cover(Image.open(os.path.join(A, "bg-turrets.png")), Wp, Hp).convert("RGBA")
    band = Image.new("RGBA", (Wp, 180), (10, 30, 52, 210)); bg.alpha_composite(band, (0, 0))
    chip(bg, "9 TURRETS  ·  4 BRANCH PATHS  ·  NO UPGRADE CAP", 54, 28)
    title(bg, "PICK YOUR DEFENSE", 56, 70, 54, (255, 199, 67), (17, 42, 70))
    ui.text(bg, (58, 140), "Actual in-game 3D models on an AI-optimized backdrop.", ui.body(18, "Medium"), fill=(235,243,250))
    names = [("Gun","MACHINE GUN"),("Cannon","CANNON"),("Harpoon","HARPOON"),("Mortar","MORTAR"),("Tesla","TESLA"),("Flame","FLAME"),("Sniper","SNIPER"),("Bubble","BUBBLE"),("Mender","MENDER")]
    cw, ch = 355, 202; gx, gy = 18, 14; x0, y0 = 78, 192
    for i, (n, label) in enumerate(names):
        c, r = i % 3, i // 3; x, y = x0 + c * (cw + gx), y0 + r * (ch + gy)
        # the real studio render fills the card; the plate stays visible, no fake redraw
        sh = Image.new("RGBA", bg.size, (0,0,0,0))
        ImageDraw.Draw(sh).rounded_rectangle((x+8,y+10,x+cw+8,y+ch+10),18,fill=(10,25,42,110))
        bg.alpha_composite(sh.filter(ImageFilter.GaussianBlur(10)))
        plate = Image.new("RGBA", (cw - 8, ch - 58), (152,147,138,255))
        raw = Image.open(os.path.join(TRAW, n + ".png")).convert("RGB")
        diff = ImageChops.difference(raw, Image.new("RGB", raw.size, (152,147,138)))
        box = diff.convert("L").point(lambda p: 255 if p > 14 else 0).getbbox()
        if box:
            pad = 12
            raw = raw.crop((max(0, box[0]-pad), max(0, box[1]-pad), min(raw.width, box[2]+pad), min(raw.height, box[3]+pad))).convert("RGBA")
        raw = fit(raw, (cw - 18, ch - 70))
        plate.alpha_composite(raw, ((plate.width - raw.width)//2, (plate.height - raw.height)//2))
        mask = Image.new("L", plate.size, 0); ImageDraw.Draw(mask).rounded_rectangle((0,0,plate.width,plate.height),16,fill=255)
        bg.alpha_composite(Image.composite(plate, Image.new("RGBA", plate.size, (0,0,0,0)), mask), (x+4, y+4))
        foot = Image.new("RGBA", (cw-8, 58), (242,234,215,255)); bg.alpha_composite(foot, (x+4, y+ch-54))
        d = ImageDraw.Draw(bg); d.rounded_rectangle((x+2,y+2,x+cw-2,y+ch+2),18,outline=(222,188,116),width=2)
        ui.text(bg, (x + 14, y + ch - 46), label, ui.title(17), fill=NAVY)
        ui.text(bg, (x + 14, y + ch - 24), "Lv1 to Lv9 · four branch looks", ui.body(10, "Medium"), fill=(94,105,118))
    chip(bg, "REAL 3D MODELS  ·  AI BACKDROP", 1110, 852, (110,220,175))
    bg.convert("RGB").save(os.path.join(IMG, "promo-turrets-0.3.0.png"), optimize=True)
    print("turrets")

def weapons():
    Wp, Hp = 1600, 900
    bg = cover(Image.open(os.path.join(A, "bg-weapons.png")), Wp, Hp).convert("RGBA")
    band = Image.new("RGBA", (Wp, 180), (7, 28, 52, 170)); bg.alpha_composite(band, (0, 0))
    chip(bg, "17 CUSTOM WEAPONS  ·  REAL ITEMS  ·  LEGENDARY TRICKS", 54, 28)
    title(bg, "THE TOY ARSENAL", 56, 70, 54, (255, 199, 67), (17, 42, 70))
    ui.text(bg, (58, 140), "Actual in-game 3D models on an AI-optimized underwater backdrop.", ui.body(18, "Medium"), fill=(235,243,250))
    names = [("Rocket","ROCKET"),("Boomerang","TUNA BOOMERANG"),("Gatling","GATLING"),("Laser","LASER"),("PoisonGas","POISON GAS"),("BubbleGun","BUBBLE GUN"),("Hook","GRAPPLE"),("JellyGun","JELLYFISH"),("StarThrower","STARFISH"),("LobsterClaw","LOBSTER CLAW"),("Absorber","ABSORBER"),("Whirlpool","WHIRLPOOL"),("NetLauncher","NET LAUNCHER"),("WaterCannon","WATER CANNON"),("PufferBomb","PUFFER BOMB"),("Conch","CONCH"),("SharkWhistle","SHARK WHISTLE")]
    cw, ch = 285, 158; gx, gy = 16, 7; x0, y0 = 74, 190
    for i, (n, label) in enumerate(names):
        c, r = i % 5, i // 5; x, y = x0 + c * (cw + gx), y0 + r * (ch + gy)
        im = cut(os.path.join(W, n + ".png"))
        shadow(bg, im, (x + cw//2, y + 62), 0.34, 16, 85, (0, 10))
        ui.text(bg, (x + cw//2, y + ch - 36), label, ui.body(14, "Bold"), fill=(255,238,194), anchor="ma", stroke=2, stroke_fill=(9,28,48))
        ui.text(bg, (x + cw//2, y + ch - 16), "Common to Legendary", ui.body(10, "Medium"), fill=(192,219,236), anchor="ma")
    chip(bg, "REAL 3D MODELS  ·  AI BACKDROP", 1110, 852, (110,220,175))
    bg.convert("RGB").save(os.path.join(IMG, "promo-weapons-0.3.0.png"), optimize=True)
    print("weapons")

# ---------- co-op ----------
def coop():
    Wp, Hp = 1600, 900
    bg = cover(Image.open(os.path.join(A, "island_close.png")), Wp, Hp).convert("RGBA")
    g = Image.new("RGBA", (Wp, Hp), (0, 0, 0, 0)); gd = ImageDraw.Draw(g)
    gd.rectangle((0, 0, Wp, 250), fill=(7, 25, 48, 150)); gd.rectangle((0, Hp - 120, Wp, Hp), fill=(5, 18, 32, 100))
    bg.alpha_composite(g)
    shadow(bg, cut(os.path.join(W, "WaterCannon.png")), (1260, 745), 0.55, 20, 95, (0, 10))
    shadow(bg, cut(os.path.join(W, "Rocket.png")), (300, 800), 0.62, 20, 95, (0, 10))
    chip(bg, "1–4 PLAYERS  ·  ONE SHARED VAULT", 54, 34)
    title(bg, "BRING FRIENDS.", 56, 82, 74, (255, 199, 67), (17,42,70))
    ui.text(bg, (58, 180), "One lighthouse. 100 waves. A lot of screaming.", ui.title(26), fill=(255,255,255), stroke=2, stroke_fill=(17,42,70))
    ui.text(bg, (58, 222), "The actual island, actual turrets, actual villagers — and a shared vault for the whole crew.", ui.body(18, "Medium"), fill=(232,242,250), stroke=2, stroke_fill=(17,42,70))
    chip(bg, "REAL IN-GAME ISLAND & MODELS", 1060, 842, (110,220,175))
    bg.convert("RGB").save(os.path.join(IMG, "promo-coop-0.3.0.png"), optimize=True)
    print("coop")

hero(); turrets(); weapons(); coop()
