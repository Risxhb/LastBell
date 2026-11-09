# python crucible_skater_sheets.py <shots dir>
# Contact sheets from crucible_skater_shots.py: turnaround.png (rest pose, 4 views), clips_close.png (close camera, every
# clip) and clips_game.png (the gameplay camera: each tile is a native-pixel 320x240 crop around the boss, shown at 2x with
# nearest-neighbour so the critic sees exactly how big it is in a 1280x720 game frame).
import sys, os, re
from PIL import Image, ImageDraw, ImageFont
d = sys.argv[1]
font = ImageFont.load_default()
def label(im, text):
    dr = ImageDraw.Draw(im); dr.rectangle((0, 0, im.width, 18), fill=(0, 0, 0)); dr.text((4, 3), text, fill=(255, 255, 255), font=font); return im
def sheet(tiles, cols, name):
    if not tiles: return
    w, h = tiles[0].size; rows = (len(tiles) + cols - 1) // cols
    S = Image.new('RGB', (cols * w, rows * h), (30, 30, 30))
    for i, t in enumerate(tiles): S.paste(t, ((i % cols) * w, (i // cols) * h))
    S.save(os.path.join(d, name)); print('SHEET', name, S.size)
turn = [label(Image.open(os.path.join(d, f'turn_{t}.png')).convert('RGB').resize((360, 450)), t) for t in ('front', 'front34', 'side', 'back') if os.path.exists(os.path.join(d, f'turn_{t}.png'))]
sheet(turn, 4, 'turnaround.png')
keys = sorted({m.group(1) for f in os.listdir(d) for m in [re.match(r'close_(.+)\.png$', f)] if m}, key=lambda k: os.path.getmtime(os.path.join(d, f'close_{k}.png')))
close = [label(Image.open(os.path.join(d, f'close_{k}.png')).convert('RGB').resize((400, 400)), k) for k in keys]
sheet(close, 6, 'clips_close.png')
game = []
for k in keys:
    p = os.path.join(d, f'game_{k}.png')
    if not os.path.exists(p): continue
    im = Image.open(p).convert('RGB'); cx, cy = im.width // 2, im.height // 2 + 20
    game.append(label(im.crop((cx - 160, cy - 120, cx + 160, cy + 120)).resize((640, 480), Image.NEAREST), k + ' (2x)'))
sheet(game, 4, 'clips_game.png')
lad = [label(Image.open(os.path.join(d, f)).convert('RGB').resize((400, 400)), f[:-4]) for f in sorted(os.listdir(d)) if f.startswith('ladle_close')]
if os.path.exists(os.path.join(d, 'ladle_game.png')):
    im = Image.open(os.path.join(d, 'ladle_game.png')).convert('RGB'); cx, cy = im.width // 2, im.height // 2
    lad.append(label(im.crop((cx - 200, cy - 200, cx + 200, cy + 200)), 'ladle_game (native crop)'))
sheet(lad, 3, 'ladle.png')
