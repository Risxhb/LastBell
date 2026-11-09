# python silt_sheet.py <out.png> <cols> <cell height px> <png> [<png> ...]
# Tiles review renders into one labelled contact sheet (label = file name without .png). Runs outside Blender (PIL).
import sys, os
from PIL import Image, ImageDraw, ImageFont
out, cols, ch = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]); files = sys.argv[4:]
ims = []
for f in files:
    im = Image.open(f).convert('RGB'); w = round(im.width * ch / im.height); ims.append((os.path.basename(f)[:-4], im.resize((w, ch))))
cw = max(i.width for _, i in ims); rows = (len(ims) + cols - 1) // cols
sheet = Image.new('RGB', (cols * cw, rows * (ch + 22)), (20, 20, 24)); d = ImageDraw.Draw(sheet)
try: font = ImageFont.truetype('arial.ttf', 15)
except Exception: font = ImageFont.load_default()
for k, (n, im) in enumerate(ims):
    r, c = divmod(k, cols); x, y = c * cw, r * (ch + 22)
    sheet.paste(im, (x, y + 22)); d.text((x + 4, y + 3), n, fill=(235, 235, 210), font=font)
sheet.save(out); print('SHEET', out, sheet.size)
