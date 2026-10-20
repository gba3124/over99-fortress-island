# render.py's pictures on one page, a row per state:
#   python3 tools/boss_eye_preview/sheet.py <OUT> <sheet.png> '[["open", "睜開"]]' '[["island", "從島上看"]]'
import os, sys, json
from PIL import Image, ImageDraw, ImageFont
d, out, rows, views = sys.argv[1], sys.argv[2], json.loads(sys.argv[3]), json.loads(sys.argv[4])
cell, pad, head = 300, 8, 40
font = ImageFont.truetype("/System/Library/Fonts/STHeiti Medium.ttc", 22)
small = ImageFont.truetype("/System/Library/Fonts/STHeiti Medium.ttc", 18)
W = pad + len(views) * (cell + pad); H = len(rows) * (head + cell + 30 + pad) + pad
sheet = Image.new("RGBA", (W, H), (200, 214, 226, 255)); dr = ImageDraw.Draw(sheet); y = pad
for key, title in rows:
    dr.text((pad + 4, y + 6), title, font=font, fill=(20, 30, 50)); y += head
    for i, (v, label) in enumerate(views):
        x = pad + i * (cell + pad)
        dr.rectangle([x, y, x + cell, y + cell], fill=(150, 170, 196))
        sheet.alpha_composite(Image.open(os.path.join(d, f"{key}_{v}.png")).convert("RGBA").resize((cell, cell), Image.LANCZOS), (x, y))
        dr.text((x + 6, y + cell + 4), label, font=small, fill=(20, 30, 50))
    y += cell + 30 + pad
sheet.convert("RGB").save(out); print("ok")
