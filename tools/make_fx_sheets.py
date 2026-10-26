# Key-frame storyboards of the effects, before / after the FX art pass, drawn to scale (1 m grid) from the numbers in
# Fx.cs / Weapons.cs / BossVfx.cs / BossFx.cs / AbsorberFx.cs: no game run needed. One sheet per effect in preview/fx_final/.
#   python3 tools/make_fx_sheets.py
# The projectile models' own sheet (projectiles.png) comes from Blender: tools/gun_preview.py over the projectile OBJs.
import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "thunderstore", "art"))
import ui

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "preview", "fx_final")
os.makedirs(OUT, exist_ok=True)
PANEL = (18, 23, 31)
GROUND = (52, 60, 74)
ORANGE, GOLD, SMOKE, SEA, FOAM, ROPE, IRON = (255, 140, 38), (255, 200, 90), (92, 84, 78), (128, 200, 255), (236, 246, 255), (200, 160, 100), (190, 200, 210)
LASER = (80, 230, 90)
W, H, PX = 420, 300, 42  # a frame, and pixels per metre


class Frame:
    """A side view: the ground a third up, x right in metres from the left edge, y up in metres."""

    def __init__(self, label):
        self.im = Image.new("RGBA", (W, H), PANEL)
        self.d = ImageDraw.Draw(self.im, "RGBA")
        self.gy = H - 70
        for i in range(0, W, PX):  # the metre grid
            self.d.line((i, 0, i, self.gy), fill=(28, 34, 44), width=1)
        self.d.rectangle((0, self.gy, W, H), fill=GROUND)
        self.d.line((0, self.gy, W, self.gy), fill=(90, 100, 118), width=2)
        self.d.line((W - 20 - PX, H - 18, W - 20, H - 18), fill=ui.SAND2, width=3)
        ui.text(self.im, (W - 20 - PX / 2, H - 34), "1 m", ui.body(13), ui.SAND2, "mm")
        ui.text(self.im, (12, H - 26), label, ui.body(15, "Bold"), ui.SAND, "lm")

    def p(self, x, y):
        return x * PX, self.gy - y * PX

    def ball(self, x, y, dia, fill, alpha=255):
        r = dia * PX / 2
        cx, cy = self.p(x, y)
        self.d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(*fill, alpha))

    def ring(self, x, radius, fill, alpha=200, width=5):
        # A ring lying on the ground, seen from a little above: an ellipse a quarter as tall as it is wide.
        r = radius * PX
        cx, cy = self.p(x, 0)
        self.d.ellipse((cx - r, cy - r * 0.25, cx + r, cy + r * 0.25), outline=(*fill, alpha), width=width)

    def sparks(self, x, y, n, fill, spread=1.2, seed=1, size=3):
        rng = random.Random(seed)
        for _ in range(n):
            a, s = rng.uniform(0, math.pi), rng.uniform(0.2, spread)
            px, py = self.p(x + math.cos(a) * s, y + abs(math.sin(a)) * s)
            self.d.rectangle((px - size, py - size, px + size, py + size), fill=fill)

    def puff(self, x, y, dia, fill, alpha=180):
        self.ball(x, y, dia, fill, alpha)

    def bar(self, a, b, width, fill):
        self.d.line((*self.p(*a), *self.p(*b)), fill=fill, width=max(1, int(width * PX)))

    def note(self, s, y=12):
        for i, line in enumerate(ui.wrap(s, ui.body(12), W - 24)[:2]):
            ui.text(self.im, (W / 2, y + i * 15), line, ui.body(12), ui.SAND2, "mm")

    def blur_glow(self, x, y, dia, fill):
        g = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        r = dia * PX / 2
        cx, cy = self.p(x, y)
        ImageDraw.Draw(g).ellipse((cx - r, cy - r, cx + r, cy + r), fill=(*fill, 120))
        self.im.alpha_composite(g.filter(ImageFilter.GaussianBlur(r * 0.5)))
        self.d = ImageDraw.Draw(self.im, "RGBA")


def blast_look(radius):  # Fx.BlastLook
    return min(max(radius * 1.1, 0.8), 2.4), max(0.6, radius), min(max(round(8 + radius * 4), 8), 32), min(max(round(3 + radius * 2), 3), 10)


# ---------- the sheets: rows of frames with a title over each row ----------

def sheet(name, title, rows, blurb):
    pad, head, rowh = 24, 96, 46
    cols = max(len(r[1]) for r in rows)
    im = Image.new("RGBA", (pad * 2 + cols * (W + pad), head + sum(rowh + H + pad for _ in rows) + 40), ui.NAVY)
    ui.text(im, (pad, 22), title, ui.title(34), ui.SAND, "la")
    lines = ui.wrap(blurb, ui.body(16), im.width - pad * 2)[:2]
    for i, line in enumerate(lines):
        ui.text(im, (pad, 62 + i * 20), line, ui.body(16), ui.SKY, "la")
    y = head + (20 if len(lines) > 1 else 0)
    for label, frames, colour in rows:
        ui.card(im, (pad, y, im.width - pad, y + rowh + H + 12), fill=colour, outline=ui.SAND3, radius=16)
        ui.text(im, (pad + 18, y + rowh / 2), label, ui.body(20, "Bold"), ui.INK, "lm")
        for i, f in enumerate(frames):
            im.alpha_composite(f.im, (pad + 12 + i * (W + pad), y + rowh))
        y += rowh + H + pad + 12
    im.convert("RGB").save(os.path.join(OUT, name + ".png"))
    print("wrote", name)


BEFORE, AFTER = (236, 206, 196), ui.SAND


def turret_blast():
    rows = []
    for level, radius in ((1, 2.0), (20, 2.0 * (1 + 0.055 * 19))):
        old = 3.5 * (1 + 0.055 * (level - 1))  # the old solid sphere, scale = its diameter
        before = []
        for t in (0.05, 0.12, 0.18):
            f = Frame(f"old Lv{level}, t={t:.2f}s")
            k = t / 0.18
            dia = (0.4 + (old - 0.4) * k) * (1 - k * 0.3)
            f.ball(5, 0.3, dia, ORANGE)
            f.note(f"one solid ball, {dia:.1f} m across (up to {old * 0.7:.1f} m)")
            before.append(f)
        flash, ring, sparks, smoke = blast_look(radius)
        after = []
        for t in (0.05, 0.12, 0.3):
            f = Frame(f"new Lv{level}, t={t:.2f}s")
            k = min(1, t / 0.22)
            if k < 1:
                f.blur_glow(5, 0.3, flash * (0.45 + 0.55 * (1 - (1 - k) ** 2)), ORANGE)
                f.ball(5, 0.3, flash * 0.35 * (1 - k), (255, 240, 200))
            e = 1 - (1 - min(1, t / 0.45)) ** 2
            f.ring(5, ring * max(0.05, e), ORANGE, int(230 * (1 - min(1, t / 0.45) ** 2)) + 20)
            f.sparks(5, 0.3, sparks, GOLD, 0.6 + t * 6, seed=level)
            for i in range(smoke):
                rng = random.Random(i * 7 + level)
                f.puff(5 + rng.uniform(-0.6, 0.6) * (1 + t * 3), 0.4 + t * rng.uniform(1, 2.5), 0.5 + t * 1.2, SMOKE, 150)
            f.note(f"flash ≤ {flash:.1f} m, ring = real radius {ring:.1f} m, {sparks} sparks, {smoke} smoke")
            after.append(f)
        rows.append((f"BEFORE  Lv{level}", before, BEFORE))
        rows.append((f"AFTER  Lv{level}", after, AFTER))
    sheet("turret-blast", "Turret / rocket blast: Fx.Explosion", rows,
          "The old blast was one opaque orange sphere that scaled with the level (7 m at Lv20, filling the screen). Now a capped flash, a ring on the ground at the real radius, bounded sparks and smoke.")


def rocket():
    before = []
    for t in (0.2, 0.6, 1.0):
        f = Frame(f"old, t={t:.1f}s")
        x = 1 + t * 9
        f.bar((x - 0.5, 1.5), (x, 1.5), 0.12, (120, 150, 90))
        for i in range(int(t / 0.025)):
            xi = 1 + i * 0.025 * 9
            f.puff(xi - 0.3, 1.5 + random.Random(i).uniform(-0.2, 0.2), 0.45, (190, 190, 190), 120)
        f.sparks(x - 0.4, 1.5, 6, ORANGE, 0.4, seed=3)
        if t >= 1.0:
            f.ball(x, 1.4, 1.6, ORANGE)
            f.sparks(x, 1.4, 16, ORANGE, 1.8, seed=5)
            f.ball(x, 0.3, 3.0, ORANGE, 160)
            f.note("end: its own SmallExplosion + 16 sparks + sound, AND the host's Blast ball")
        else:
            f.note("grey dust puffs + orange sparks every 25 ms")
        before.append(f)
    after = []
    for t in (0.2, 0.6, 1.0):
        f = Frame(f"new, t={t:.1f}s")
        x = 1 + t * 9
        n = int(t / 0.03)
        for i in range(max(0, n - 40), n):  # smoke puffs of the last 1.2 s, growing and rising
            age = (n - i) * 0.03
            xi = 1 + i * 0.03 * 9 - 0.5
            f.puff(xi, 1.5 + 0.3 * age, 0.35 + age * 0.5, (200, 196, 190), int(170 * max(0, 1 - age / 1.2)))
        for i in range(4):  # the plume of flame right behind it
            f.ball(x - 0.35 - i * 0.12, 1.5, 0.4 - i * 0.07, (255, 170 - i * 25, 40))
        f.sparks(x - 0.4, 1.5, 3, ORANGE, 0.3, seed=3)
        f.bar((x - 0.5, 1.5), (x, 1.5), 0.12, (120, 150, 90))
        if t >= 1.0:
            flash, ring, sparks, smoke = blast_look(3.0)
            f.blur_glow(x, 1.4, flash * 0.8, ORANGE)
            f.ring(x, ring * 0.6, ORANGE, 220)
            f.sparks(x, 0.5, sparks, GOLD, 1.5, seed=9)
            for i in range(smoke):
                f.puff(x + random.Random(i).uniform(-0.8, 0.8), 0.6 + i * 0.15, 0.8, SMOKE, 150)
            f.note("end: a wink of light; the explosion is the host's Blast once, at the real radius")
        else:
            f.note("flame plume + a rope of smoke every 30 ms (RocketPuffEvery), muzzle smoke at launch")
        after.append(f)
    sheet("rocket", "Rocket: trail and explosion (Weapons.RocketView + Fx.Explosion)", [("BEFORE", before, BEFORE), ("AFTER", after, AFTER)],
          "The rocket drew its own explosion and sound on top of the host's Blast (two bangs). Now one explosion, and a fire-and-smoke trail whose count is fixed per second, not per frame.")


def water_jet():
    reach = 8.0
    before = []
    for t in (0.1, 0.3, 0.5):
        f = Frame(f"old, t={t:.1f}s")
        rng = random.Random(2)
        for ev in range(int(t / 0.1) + 1):  # each 100 ms event: 6 drops scattered along the line, 0.35 s life, 8-14 m/s
            for i in range(6):
                age = t - ev * 0.1
                if age > 0.35:
                    continue
                k = rng.random()
                x = 1 + k * reach + age * rng.uniform(8, 14)
                y = 1.3 - 0.5 * age * age * 9.8 * 0.14
                if x < 1 + reach:
                    f.ball(x, y, 0.12, SEA)
        f.sparks(1 + reach, 0.1, 3, FOAM, 0.4, seed=1)
        f.note("6 drops an event scattered along the line, dying after 0.35 s: a thin dotted spray")
        before.append(f)
    after = []
    life = min(max(reach / 16, 0.25), 0.7)
    for t in (0.1, 0.3, 0.5):
        f = Frame(f"new, t={t:.1f}s")
        rng = random.Random(3)
        for ev in range(int(t / 0.1) + 1):
            age = t - ev * 0.1
            if age > life:
                continue
            for i in range(10):
                k = rng.random() * 0.25
                x = 1 + k * reach + age * reach / life * rng.uniform(0.9, 1.1)
                y = 1.3 + rng.uniform(-0.08, 0.08) - 0.5 * 9.8 * 0.14 * age * age
                if x < 1 + reach:
                    f.ball(x, y, 0.14, SEA)
            f.puff(1 + reach * rng.uniform(0.3, 0.8), 1.25, 0.3, FOAM, 90)
        f.sparks(1 + reach, 0.15, 6, FOAM, 0.5, seed=4)
        f.sparks(1 + reach, 0.15, 3, SEA, 0.4, seed=5)
        f.note(f"10 drops an event from the nozzle, {life:.2f} s life = the reach; mist along it, foam + drops at the hit")
        after.append(f)
    sheet("water-jet", "Water cannon: the jet (Weapons.WaterView)", [("BEFORE", before, BEFORE), ("AFTER", after, AFTER)],
          "Drops now leave the nozzle fast enough to reach what they hit, so the spray reads as one jet instead of dots that die halfway.")


def laser_foot():
    before, after = [], []
    for i, ang in enumerate((0, 12, 24)):
        for row, follow in ((before, False), (after, True)):
            f = Frame(f"{'new' if follow else 'old'}, gun turned {ang}°, {i * 33} ms after the host's update")
            gx, gy = 1.2, 1.4
            a = math.radians(ang)
            f.bar((gx - 0.3, gy), (gx + 0.3 * math.cos(a), gy + 0.3 * math.sin(a)), 0.1, IRON)
            mx, my = gx + 0.3 * math.cos(a), gy + 0.3 * math.sin(a)
            fx, fy = (mx, my) if follow else (gx + 0.3, gy)  # the old foot: where the host last said, up to 100 ms ago
            ex, ey = fx + 7 * math.cos(a), fy + 7 * math.sin(a)
            f.bar((fx, fy), (ex, ey), 0.22, (*LASER[:2], 120))
            f.bar((fx, fy), (ex, ey), 0.08, (230, 255, 230))
            f.ball(ex, ey, 0.25, (230, 255, 230))
            if not follow and ang:
                f.d.line((*f.p(mx, my), *f.p(fx, fy)), fill=ui.CORAL, width=3)
                ui.text(f.im, (f.p(fx, fy)[0] + 10, f.p(fx, fy)[1] + 16), "foot hangs behind the muzzle", ui.body(13, "Bold"), ui.CORAL, "la")
            f.note("the host sends the beam's foot 10x a second" if not follow else "AnimateBeams moves the foot to the gun's muzzle every frame")
            row.append(f)
    sheet("laser-foot", "Laser: the beam's foot follows the gun (Weapons.AnimateBeams)", [("BEFORE", before, BEFORE), ("AFTER", after, AFTER)],
          "Between the host's 100 ms updates the gun turns on; the beam used to stay put, its foot hanging in the air behind the muzzle. Same fix for a friend's beam as seen on your screen.")


def harpoon():
    before, after = [], []
    for t in (0.1, 0.2, 0.3):
        k = t / 0.3
        x = 1 + k * 8
        f = Frame(f"old, t={t:.1f}s")
        f.bar((x - 1.8, 1.0), (x, 1.0), 0.06, IRON)
        f.note("a bare bar flying the line; nothing says which turret fired it")
        before.append(f)
        f = Frame(f"new, t={t:.1f}s")
        f.d.line((*f.p(1, 1.0), *f.p(x - 0.9, 1.0)), fill=ROPE, width=3)
        f.bar((x - 1.8, 1.0), (x, 1.0), 0.06, IRON)
        if t >= 0.3:
            f.sparks(9, 0.6, 6, SEA, 0.5, seed=7)
        f.note("its rope pays out from the turret behind it; drops where it ends; rope gone with the bolt")
        after.append(f)
    sheet("harpoon-rope", "Harpoon turret: the bolt on its rope (Fx.Bolt)", [("BEFORE", before, BEFORE), ("AFTER", after, AFTER)],
          "A rope (the hook gun's rope texture) from the turret to the bolt, so the shot reads as a harpoon. Tracers, zaps and bolts now use the shared glowing material the gatling's bullets use.")


# ---------- the boss side: flashes against the camera, the trophy pillar, the absorber's gust ring, the telegraphs ----------

def figure(f, x):  # a player standing at x, their camera (eye) marked
    f.d.rectangle((*f.p(x - 0.18, 1.6), *f.p(x + 0.18, 0)), fill=(40, 46, 58))
    ex, ey = f.p(x, 1.5)
    f.d.ellipse((ex - 5, ey - 5, ex + 5, ey + 5), fill=ui.CORAL)


def flash_camera():
    size, dist = 3 + 3 * 1.6, 1.2  # a legendary chest's flash (ChestBurst), opened at your feet
    before, after = [], []
    for t in (0.0, 0.5, 0.95):
        swell = 1 - (1 - t) ** 2
        for row, capped in ((before, False), (after, True)):
            across = (min(min(size, 4.0), 2 * max(0.1, dist - 0.4)) if capped else size) * (0.45 + 0.55 * swell)
            f = Frame(f"{'new' if capped else 'old'}, t={t:.2f}s of 0.55")
            f.blur_glow(5, 1.4, across, GOLD)
            f.ball(5, 1.4, across * 0.35 * (1 - t), (255, 245, 210))
            f.d.rectangle((*f.p(4.7, 0.6), *f.p(5.3, 0)), fill=(150, 100, 50))
            figure(f, 5 - dist)
            inside = across / 2 > dist
            f.note(f"halo {across:.1f} m across, camera {dist} m away: {'INSIDE IT, the screen tinted' if inside else 'clear'}")
            row.append(f)
    sheet("flash-camera", "Boss / chest flash against the camera (BossVfx.Flash)", [("BEFORE", before, BEFORE), ("AFTER", after, AFTER)],
          f"A legendary chest's flash swelled to {size:.1f} m round the player standing at it (the whole screen gold). The halo stops at HaloMax (4 m) and at Room(distance): never round the camera. Point lights capped at 4 at once.")


def trophy_pillar():
    before, after = [], []
    for t in (0.0, 1.25, 2.4):
        w = 0.5 * (1 - t / 2.5) + 0.05
        for row, fixed in ((before, False), (after, True)):
            f = Frame(f"{'new' if fixed else 'old'}, t={t:.2f}s of 2.5")
            foot, h = (0, 8) if fixed else (6, 6)
            f.bar((5, foot), (5, foot + h), w, (180, 110, 255))
            f.d.rectangle((*f.p(4.7, 0.5), *f.p(5.3, 0)), fill=GOLD)
            figure(f, 3.5)
            f.sparks(5, 3, 6, GOLD, 2.5, seed=int(t * 10))
            f.note(f"pillar from {foot} m to {foot + h} m" + ("" if fixed else ": hanging in the air over the trophy"))
            row.append(f)
    sheet("trophy-pillar", "Where a boss's trophy lands (BossFx.Trophy)", [("BEFORE", before, BEFORE), ("AFTER", after, AFTER)],
          "WeaponFx.Cyl runs 0..1 up from its foot; placed at ground + 6 the pillar hung 6-12 m up. Now it stands on the trophy, 8 m tall, thinning out. The death marker's gold pillar (Fx.DeathPillar) had the same bug at 15-30 m.")


def sky_ring():
    reach = 12.0
    before, after = [], []
    for k in (0.1, 0.5, 0.95):
        size = 5 * (0.12 + (0.9 - 0.12) * min(1, k / 0.8) if k < 0.8 else 0.9 + 0.1 * (k - 0.8) / 0.2)  # Rings' SizeOverLife
        for row, flies in ((before, False), (after, True)):
            x = 1.5 + (0.6 + reach * k if flies else 0)
            f = Frame(f"{'new' if flies else 'old'}, {int(k * 100)}% of the ring's 0.5 s")
            figure(f, 1.5)
            cx, cy = f.p(x, 1.5)
            r = size * PX / 2
            f.d.ellipse((cx - r * 0.18, cy - r, cx + r * 0.18, cy + r), outline=(255, 255, 255, 230), width=4)
            f.note(f"ring {size:.1f} m across, {x - 1.5:.1f} m from the shooter" + ("" if flies else ": round their own camera"))
            row.append(f)
    sheet("absorber-sky-ring", "Absorber's skyfish gust: the ring of air (AbsorberFx 'sky')", [("BEFORE", before, BEFORE), ("AFTER", after, AFTER)],
          "The ring was emitted at the muzzle with no speed, so it grew to 5 m round the shooter's own camera. Now it leaves 0.6 m ahead and flies down the gust as it grows, as the conch's rings do.")


def telegraphs():
    # BossFx.Warning: the red fills from the start of the move (middle / lane start / cone point) behind a bright front,
    # the rim in the boss's colour; k = how far through the wind-up.
    def col(u, k):
        filled = min(1, max(0, (k * 1.03 - u) / 0.05)); front = math.exp(-((u - k) ** 2) / 0.0016)
        return (242, int(255 * (0.05 + 0.35 * front)), 8, int(255 * (0.26 + 0.3 * filled + 0.35 * front)))
    rim = (190, 90, 230)
    rows = []
    for shape in ("circle", "lane", "cone"):
        frames = []
        for k in (0.15, 0.55, 0.9):
            f = Frame(f"{shape}, {int(k * 100)}% of the wind-up")
            f.d.rectangle((0, 0, W, H - 44), fill=(196, 170, 120))
            cx, cy, R = W / 2, (H - 44) / 2, 110
            for i in range(60, 0, -1):  # bands from the far edge in, so the fill paints over the rest
                u = i / 60
                c = col(u, k)
                if shape == "circle":
                    f.d.ellipse((cx - R * u, cy - R * u, cx + R * u, cy + R * u), fill=c)
                elif shape == "lane":
                    f.d.rectangle((cx - 28, cy + R - 2 * R * u, cx + 28, cy + R), fill=c)
                else:
                    a = math.radians(32)
                    f.d.polygon([(cx, cy + R), (cx - math.sin(a) * 2 * R * u, cy + R - math.cos(a) * 2 * R * u), (cx + math.sin(a) * 2 * R * u, cy + R - math.cos(a) * 2 * R * u)], fill=c)
            if shape == "circle":
                f.d.ellipse((cx - R, cy - R, cx + R, cy + R), outline=rim, width=4)
            elif shape == "lane":
                f.d.rectangle((cx - 28, cy - R, cx + 28, cy + R), outline=rim, width=4)
            else:
                a = math.radians(32)
                f.d.polygon([(cx, cy + R), (cx - math.sin(a) * 2 * R, cy + R - math.cos(a) * 2 * R), (cx + math.sin(a) * 2 * R, cy + R - math.cos(a) * 2 * R)], outline=rim, width=4)
            ui.text(f.im, (12, H - 26), f"{shape}, {int(k * 100)}% of the wind-up, from above", ui.body(15, "Bold"), ui.SAND, "lm")
            frames.append(f)
        rows.append((shape.upper(), frames, AFTER))
    sheet("boss-telegraphs", "Boss telegraphs: the warning on the ground (BossFx.Warning)", rows,
          "Unchanged this pass (they already read): red fills from where the move starts behind a bright front, so how far it has got says how long is left; the rim in the boss's colour throbs faster as it comes.")


if __name__ == "__main__":
    turret_blast()
    rocket()
    water_jet()
    laser_foot()
    harpoon()
    flash_camera()
    trophy_pillar()
    sky_ring()
    telegraphs()
