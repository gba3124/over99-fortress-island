import os, random
from PIL import Image, ImageDraw, ImageFilter
from ui import *

W, H = 1200, 480
im = vgrad(W, H, (92, 168, 232), (200, 232, 252))
# a few soft clouds in the sky (they only show where the render is open sky)
clouds = Image.new("RGBA", (W, H), (0, 0, 0, 0))
d = ImageDraw.Draw(clouds)
for cx, cy, k in ((760, 40, 1.0), (1010, 78, 0.8), (1150, 26, 0.6), (560, 92, 0.7), (330, 30, 0.9)):
    for dx, dy, r in ((0, 0, 30), (34, -10, 36), (70, 2, 28), (36, 12, 30), (-30, 8, 22), (100, 10, 20)):
        d.ellipse((cx + (dx - r) * k, cy + (dy - r) * k, cx + (dx + r) * k, cy + (dy + r) * k), fill=(255, 255, 255, 215))
im.alpha_composite(clouds.filter(ImageFilter.GaussianBlur(1.2)))
scene = Image.open(os.path.join(R, "banner_0.png")).convert("RGBA").resize((W, H), Image.LANCZOS)
im.alpha_composite(scene)

# the title: a soft dark wash behind it, then chunky type
wash = Image.new("RGBA", (W, H), (0, 0, 0, 0))
ImageDraw.Draw(wash).ellipse((-260, -230, 640, 250), fill=(10, 36, 64, 95))
im.alpha_composite(wash.filter(ImageFilter.GaussianBlur(40)))
text(im, (34, 18), "OVER 99", title(96), fill=GOLD, stroke=7, stroke_fill=NAVY, drop=8)
text(im, (40, 122), "Fortress Island", title(44), fill=WHITE, stroke=5, stroke_fill=NAVY, drop=6)
f = body(23, "Bold")
s = "Hold the lighthouse for 100 waves!"
w = f.getlength(s)
card(im, (36, 186, 36 + w + 40, 228), fill=SAND, outline=SAND3, radius=21, width=3)
text(im, (56, 207), s, f, fill=INK, anchor="lm")
os.makedirs(IMG, exist_ok=True)
im.convert("RGB").save(os.path.join(IMG, "banner.png"), optimize=True)
print("banner", im.size)
