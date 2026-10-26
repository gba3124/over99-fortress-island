# Before/after pages from render.py's renders in <dir> (before_* on the left, after_* on the right, a view a row), then every PNG
# there cut to 256 colours (flat-shaded renders: it doesn't show) so they sit lightly in git.
#   python3 preview/plaza-gate/sheet.py <dir>
import glob
import os
import sys

from PIL import Image, ImageDraw, ImageFont

D = sys.argv[1]
font = ImageFont.load_default(size=22)
for what in ("plaza", "gate"):
    views = sorted(os.path.basename(p)[len("after_"):-4] for p in glob.glob(os.path.join(D, f"after_{what}_*.png")))
    page = Image.new("RGB", (1280, 400 * len(views)))
    for i, v in enumerate(views):
        for j, tag in enumerate(("before", "after")):
            page.paste(Image.open(os.path.join(D, f"{tag}_{v}.png")).convert("RGB").resize((640, 400), Image.LANCZOS), (640 * j, 400 * i))
            ImageDraw.Draw(page).text((640 * j + 10, 400 * i + 8), f"{tag}  {v}", font=font, fill="white", stroke_width=2, stroke_fill="black")
    page.save(os.path.join(D, f"compare_{what}.png"))
for p in glob.glob(os.path.join(D, "*.png")):
    Image.open(p).convert("RGB").quantize(256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.FLOYDSTEINBERG).save(p, optimize=True)
