# Copies the numbers the v5 turret models were built round (tools/layout5/<T>.json, written by tools/t5_<t>.py)
# into Catalog.cs (HeadHeight / MuzzleHeight / BarrelMount / BarrelStep / BarrelMirror). Run after any t5 script.
#   python3 tools/sync_layout5.py
import json, os, re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CAT = os.path.join(ROOT, "HtfFortress", "Catalog.cs")
ORDER = ["Gun", "Cannon", "Harpoon", "Mortar", "Tesla", "Flame", "Sniper", "Bubble", "Mender"]  # Mender is the default case
L = {t: json.load(open(os.path.join(ROOT, "tools", "layout5", t + ".json"))) for t in ORDER}

f = lambda v: f"{round(v, 3):g}f"
v3 = lambda v: f"new Vector3({', '.join(f(x) for x in v)})"

def switch(sig, rows, default):
    cases = "".join(f"                case PieceType.{t}: return {r};\n" for t, r in rows)
    return f"        public static {sig}\n        {{\n            switch (t)\n            {{\n{cases}                default: {default}\n            }}\n        }}\n"

podded = [t for t in ORDER if L[t].get("pod")]
mirror = [t for t in podded if L[t].get("mirror")]
block = (switch("float HeadHeight(PieceType t)", [(t, f(L[t]["head"])) for t in ORDER[:-1]], f"return {f(L['Mender']['head'])}; // Mender")
         + switch("float MuzzleHeight(PieceType t)", [(t, f(L[t]["muzzle"])) for t in ORDER[:-1]], f"return {f(L['Mender']['muzzle'])}; // Mender")
         + "        // Where the shot leaves at this level (the head, muzzle and all, grows with HeadScale).\n"
         + "        public static float MuzzleHeight(PieceType t, int level) => HeadHeight(t) + (MuzzleHeight(t) - HeadHeight(t)) * HeadScale(level);\n"
         + switch("Vector3 BarrelMount(PieceType t)", [(t, v3(L[t]["pod"])) for t in podded], "return Vector3.zero; // no extra barrels")
         + switch("Vector3 BarrelStep(PieceType t)", [(t, v3(L[t]["step"])) for t in podded], "return Vector3.zero;")
         + "        public static bool BarrelMirror(PieceType t) => "
         + (" || ".join(f"t == PieceType.{t}" for t in mirror) or "false") + ";\n")

src = open(CAT).read()
new, n = re.subn(r"        public static float HeadHeight\(PieceType t\)\n.*?        public static bool BarrelMirror\(PieceType t\) => [^\n]*\n",
                 lambda m: block, src, flags=re.S)
assert n == 1, "Catalog layout block not found"
open(CAT, "w").write(new)
print("synced", ", ".join(f"{t} {L[t]['head']}/{L[t]['muzzle']}" for t in ORDER))
