# The village's people, our own, drawn the way the game draws its people (the game's NPCs, copied as they stood, were
# sometimes sitting in mid-air): a tall cylinder of a head with big half-lidded eyes, a big nose and a mouth full of teeth,
# a slim body, long thin arms and legs, muted colours. The four keepers (fishmonger, smith, quartermaster, lighthouse
# keeper) and three looks for the stall's helpers. Each is six parts turning about shared joints, so the mod can walk
# them, turn their heads and wave while they talk:
#   Person<Id>Body (origin at the feet), Head (at the neck), ArmL / ArmR (at the shoulders), LegL / LegR (at the hips).
# Joints (Blender: Z up, -Y the front): neck (0, 0, 1.45), shoulders (+-0.19, 0, 1.38), hips (+-0.085, 0, 0.92); Person.cs
# has the same numbers. About 1.85 m tall without a hat; the helpers are the same drawn smaller.
# Run headless:  Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_people.py
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bkit import TAU, Builder, export, loft, mat, torus

PEOPLE = {
    "Market": dict(skin=(0.86, 0.74, 0.56), shirt=(0.24, 0.38, 0.62), stripe=(0.78, 0.78, 0.74), pants=(0.22, 0.24, 0.32), shoes=(0.85, 0.65, 0.1),
                   boots=True, apron=(0.85, 0.65, 0.1), hat="souwester", hatc=(0.85, 0.65, 0.1), hair=(0.32, 0.2, 0.12)),
    "Smith": dict(skin=(0.74, 0.56, 0.4), shirt=(0.3, 0.3, 0.32), pants=(0.26, 0.21, 0.17), shoes=(0.28, 0.17, 0.1), apron=(0.42, 0.26, 0.14),
                  hat="headband", hatc=(0.68, 0.14, 0.1), beard=(0.13, 0.1, 0.09), belly=0.2, arm=0.06, rolled=True),
    "Vault": dict(skin=(0.84, 0.7, 0.54), shirt=(0.34, 0.37, 0.22), pants=(0.29, 0.32, 0.19), shoes=(0.08, 0.08, 0.08), belt=(0.33, 0.21, 0.11),
                  hat="peaked", hatc=(0.34, 0.37, 0.22), hair=(0.15, 0.12, 0.1), mustache=(0.3, 0.2, 0.12), buttons=True),
    "Lighthouse": dict(skin=(0.86, 0.78, 0.6), shirt=(0.2, 0.25, 0.38), pants=(0.16, 0.18, 0.24), shoes=(0.08, 0.08, 0.09), coat=True,
                       hat="captain", hatc=(0.1, 0.11, 0.14), hair=(0.74, 0.74, 0.72), beard=(0.78, 0.78, 0.76), pipe=True, buttons=True),
    "Helper1": dict(skin=(0.86, 0.72, 0.56), shirt=(0.76, 0.74, 0.68), pants=(0.28, 0.42, 0.66), overalls=True, shoes=(0.4, 0.26, 0.15),
                    hat="straw", hatc=(0.84, 0.72, 0.42), hair=(0.38, 0.24, 0.12)),
    "Helper2": dict(skin=(0.55, 0.38, 0.27), shirt=(0.56, 0.58, 0.6), pants=(0.82, 0.45, 0.16), overalls=True, shoes=(0.1, 0.1, 0.1),
                    hat="beanie", hatc=(0.72, 0.16, 0.14), hair=(0.1, 0.08, 0.06)),
    "Helper3": dict(skin=(0.82, 0.66, 0.5), shirt=(0.76, 0.74, 0.68), pants=(0.3, 0.55, 0.33), overalls=True, shoes=(0.74, 0.72, 0.68),
                    hat="cap", hatc=(0.22, 0.36, 0.7), hair=(0.78, 0.55, 0.25)),
}

black = mat("PplBlack", (0.05, 0.05, 0.06), force=True)
white = mat("PplWhite", (0.86, 0.85, 0.8), force=True)
mouth = mat("PplMouth", (0.32, 0.1, 0.1), force=True)
gold = mat("PplGold", (0.9, 0.7, 0.25), force=True)
lens = mat("PplLens", (0.4, 0.7, 0.9), (0.15, 0.4, 0.6), force=True)
wood = mat("PplPipe", (0.4, 0.24, 0.12), force=True)


def ring(z, rx, ry, n=12):
    return [(rx * math.cos(k * TAU / n), ry * math.sin(k * TAU / n), z) for k in range(n)]


def build(pid, cfg):
    M = {k: mat(f"Ppl{pid}{k}", v, force=True) for k, v in cfg.items() if isinstance(v, tuple)}
    mats = list(M.values()) + [black, white, mouth, gold, lens, wood]
    skin, shirt, pants, shoes = M["skin"], M["shirt"], M["pants"], M["shoes"]
    out = []

    # ---------- body: a slim torso from the hips to the collar, what's worn on it ----------
    b = Builder(f"Person{pid}Body", mats)
    w = cfg.get("belly", 0.155)  # the waist (the smith carries a belly)
    prof = [(0.86, 0.15, 0.11), (0.95, 0.165, 0.12), (1.1, w, w * 0.75 + 0.01), (1.27, 0.18, 0.125), (1.38, 0.19, 0.12), (1.44, 0.11, 0.09), (1.47, 0.07, 0.065)]
    front = -(w * 0.75 + 0.01)
    stripes = M.get("stripe")

    def band(i, j):
        if i == 0 or (i == 1 and cfg.get("overalls")):
            return pants
        if i >= 5:
            return skin
        return stripes if stripes and i in (2, 4) else shirt
    loft(b, [ring(z, rx, ry) for z, rx, ry in prof], shirt, mf=band, caps=(pants, skin))
    b.cyl(0.085, 0.05, (0, 0, 1.43), shirt, segs=12)  # the collar
    if cfg.get("coat"):  # a long coat to the knees, lapels in a V
        b.cone(0.2, 0.17, 0.36, (0, 0, 0.78), shirt, segs=12)
        for s in (-1, 1):
            b.box((0.05, 0.015, 0.2), (s * 0.05, -0.125, 1.33), shirt, rot=(0, s * 0.35, 0))
    if "belt" in M:
        torus(b, (0, 0, 0.98), (0, 0, 1), 0.168, 0.02, M["belt"], segs=16, tsegs=4)
        b.box((0.05, 0.025, 0.045), (0, -0.138, 0.98), gold)
    if "apron" in M:
        a = M["apron"]
        b.box((0.26, 0.02, 0.5), (0, front - 0.012, 1.0), a)
        b.box((0.18, 0.02, 0.15), (0, -0.13, 1.3), a)
        for s in (-1, 1):
            b.beam((s * 0.08, -0.13, 1.37), (s * 0.1, 0.11, 1.4), 0.01, a, segs=4)
    if cfg.get("overalls"):
        b.box((0.2, 0.025, 0.2), (0, -0.13, 1.22), pants)
        for s in (-1, 1):
            b.beam((s * 0.08, -0.13, 1.31), (s * 0.1, 0.12, 1.36), 0.016, pants, segs=4)
            b.ico(0.015, (s * 0.07, -0.145, 1.29), gold, subdiv=1)
    if cfg.get("buttons"):
        for k in range(3):
            for s in ((-1, 1) if cfg.get("coat") else (0,)):
                b.ico(0.014, (s * 0.045, front - 0.012, 1.05 + 0.1 * k), gold, subdiv=1)
    out.append(b.finish())

    # ---------- head: a tall rounded cylinder, big half-lidded eyes, a big nose, a mouth of teeth ----------
    b = Builder(f"Person{pid}Head", mats)
    loft(b, [ring(z, rx, ry, 14) for z, rx, ry in ((0.0, 0.11, 0.1), (0.04, 0.13, 0.12), (0.12, 0.145, 0.132), (0.3, 0.148, 0.134), (0.37, 0.138, 0.124), (0.405, 0.1, 0.09))], skin)
    for s in (-1, 1):
        b.ico(0.033, (s * 0.148, 0.0, 0.23), skin, subdiv=1)                  # ears
        b.ico(0.052, (s * 0.064, -0.112, 0.27), white, subdiv=2)               # big eyes
        b.ico(0.056, (s * 0.064, -0.116, 0.294), skin, subdiv=2)               # sleepy upper lids
        b.ico(0.017, (s * 0.064, -0.16, 0.257), black, subdiv=1)               # pupils, under the lids
        b.box((0.07, 0.018, 0.014), (s * 0.068, -0.14, 0.345), M.get("hair", M.get("beard", black)), rot=(0, s * 0.15, 0))  # brows
    b.cone(0.034, 0.012, 0.075, (0, -0.165, 0.195), skin, segs=8, axis="Y")   # the nose
    b.box((0.1, 0.02, 0.026), (0, -0.132, 0.108), mouth)
    b.box((0.075, 0.022, 0.011), (0, -0.136, 0.116), white)                   # teeth
    if "hair" in M:
        b.box((0.27, 0.12, 0.2), (0, 0.06, 0.28), M["hair"])
    if "beard" in M:
        loft(b, [ring(z, rx, ry, 12) for z, rx, ry in ((0.02, 0.1, 0.09), (0.1, 0.152, 0.138), (0.17, 0.152, 0.138))], M["beard"])
        b.box((0.16, 0.04, 0.035), (0, -0.14, 0.135), M["beard"])
    if "mustache" in M:
        b.box((0.15, 0.04, 0.032), (0, -0.142, 0.14), M["mustache"])
    if cfg.get("pipe"):
        b.beam((0.04, -0.14, 0.11), (0.09, -0.22, 0.09), 0.009, wood, segs=5)
        b.cyl(0.02, 0.05, (0.095, -0.225, 0.11), wood, segs=8)
    h, c = cfg.get("hat"), M.get("hatc")
    if h == "souwester":    # a fisherman's oilskin rain hat: a crown and a brim sloping to the back
        b.cyl(0.152, 0.1, (0, 0.01, 0.43), c, segs=16)
        b.cone(0.27, 0.16, 0.05, (0, 0.03, 0.39), c, segs=18)
    elif h == "headband":   # bald, a band round the head and goggles pushed up
        torus(b, (0, 0, 0.33), (0, 0, 1), 0.147, 0.02, c, segs=18, tsegs=4)
        for s in (-1, 1):
            b.cyl(0.04, 0.04, (s * 0.058, -0.135, 0.355), black, segs=10, axis="Y")
            b.cyl(0.03, 0.012, (s * 0.058, -0.157, 0.355), lens, segs=10, axis="Y")
    elif h == "peaked":     # a soldier's peaked cap
        b.cyl(0.15, 0.09, (0, 0, 0.43), c, segs=16)
        b.cyl(0.17, 0.035, (0, 0, 0.485), c, segs=16)
        b.box((0.2, 0.11, 0.018), (0, -0.15, 0.4), black, rot=(math.radians(-12), 0, 0))
        b.ico(0.02, (0, -0.152, 0.45), gold, subdiv=1)
    elif h == "captain":    # a flat black cap with a short peak and a gold badge
        b.cyl(0.155, 0.085, (0, 0, 0.43), c, segs=16)
        b.cyl(0.163, 0.02, (0, 0, 0.475), c, segs=16)
        b.box((0.19, 0.09, 0.016), (0, -0.15, 0.395), c, rot=(math.radians(-10), 0, 0))
        b.ico(0.02, (0, -0.158, 0.44), gold, subdiv=1)
    elif h == "straw":      # a wide straw hat with a red band
        b.cyl(0.29, 0.02, (0, 0, 0.405), c, segs=20)
        b.cyl(0.14, 0.11, (0, 0, 0.465), c, segs=16)
        b.cyl(0.145, 0.03, (0, 0, 0.43), mouth, segs=16)
    elif h == "beanie":     # a knitted hat with a bobble
        loft(b, [ring(z, rx, ry, 14) for z, rx, ry in ((0.3, 0.155, 0.14), (0.4, 0.15, 0.135), (0.46, 0.11, 0.1), (0.49, 0.05, 0.045))], c)
        torus(b, (0, 0, 0.31), (0, 0, 1), 0.153, 0.025, c, segs=18, tsegs=5)
        b.ico(0.045, (0, 0, 0.52), white, subdiv=1)
    elif h == "cap":        # a baseball cap
        loft(b, [ring(z, rx, ry, 14) for z, rx, ry in ((0.33, 0.155, 0.14), (0.42, 0.145, 0.13), (0.46, 0.09, 0.08))], c)
        b.box((0.22, 0.15, 0.016), (0, -0.17, 0.34), c, rot=(math.radians(-6), 0, 0))
    out.append(b.finish())

    # ---------- arms: long and thin, a sleeve (rolled up for the smith), a mitten of a hand ----------
    r = cfg.get("arm", 0.048)
    for side in ("L", "R"):
        b = Builder(f"Person{pid}Arm{side}", mats)
        b.ico(r * 1.15, (0, 0, 0), shirt, subdiv=1)
        b.beam((0, 0, 0), (0, 0, -0.29), r, shirt, segs=8, r2=r * 0.9)
        b.beam((0, 0, -0.29), (0, 0, -0.55), r * 0.88, skin if cfg.get("rolled") else shirt, segs=8, r2=r * 0.78)
        if "stripe" in M:
            b.beam((0, 0, -0.14), (0, 0, -0.19), r * 1.04, M["stripe"], segs=8)
        b.box((0.07, 0.04, 0.09), (0, -0.005, -0.6), skin)                  # the hand
        b.box((0.025, 0.03, 0.04), (0, -0.04, -0.57), skin, rot=(0.4, 0, 0))  # its thumb
        out.append(b.finish())

    # ---------- legs: long and thin, trousers down to a long shoe (a boot for the fishmonger) ----------
    for side in ("L", "R"):
        b = Builder(f"Person{pid}Leg{side}", mats)
        b.beam((0, 0, 0.02), (0, 0, -0.45), 0.062, pants, segs=8, r2=0.055)
        b.beam((0, 0, -0.45), (0, 0, -0.84), 0.054, pants, segs=8, r2=0.048)
        if cfg.get("boots"):
            b.cyl(0.06, 0.26, (0, 0, -0.75), shoes, segs=10)
        b.box((0.1, 0.25, 0.075), (0, -0.05, -0.88), shoes)
        out.append(b.finish())
    return out


objs = []
for pid, cfg in PEOPLE.items():
    objs += build(pid, cfg)
export(objs)
