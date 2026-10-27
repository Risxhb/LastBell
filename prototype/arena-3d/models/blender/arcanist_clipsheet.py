# python arcanist_clipsheet.py <render dir> <out dir> <clips.json>
# Contact sheets and GIFs from arcanist_cliprender.py frames. Sheet per clip: row 1 the gameplay-like high view (cropped
# around him at the render's own pixel scale, so his size is what the game camera shows), row 2 the close front
# three-quarter, row 3 his right profile; columns are the listed frames, labelled with frame / normalised time /
# event. GIF per clip: every frame, the three views side by side, slowed (FRAME_MS per 30 fps frame) with a pause.
import json, os, sys
from PIL import Image, ImageDraw
rdir, odir, cj = sys.argv[1:4]
clips = json.load(open(cj)); os.makedirs(odir, exist_ok=True)
COLS = {'MissileTap': range(8), 'MissileTap2': range(8), 'MissileCharge': [0, 2, 4, 5, 7, 9, 10, 11],
        'MissileVolley': [0, 1, 2, 3, 4, 5, 6, 8], 'DisintegrateStart': [0, 2, 4, 6, 8, 9, 11, 13],
        'DisintegrateHold': [0, 4, 8, 12, 16, 20, 24, 28], 'DisintegrateEnd': range(8),
        'RuneSigil': [0, 2, 3, 4, 5, 6, 7, 9, 12], 'AstralOrb': [0, 5, 8, 11, 15, 17, 19, 21],
        'ArcaneComet': [0, 4, 7, 9, 10, 11, 12, 16, 23], 'BlinkIn': [0, 2, 4, 6, 8, 10, 12, 14]}
FRAME_MS = 100
def high(im):   # crop 360 x 300 around the character's screen position (frame centre)
    w, h = im.size; return im.crop((w // 2 - 180, h // 2 - 170, w // 2 + 180, h // 2 + 130))
def load(clip, view, f):
    im = Image.open(os.path.join(rdir, clip, f'{view}_{f:03d}.png')).convert('RGB')
    return high(im) if view == 'high' else im.resize((im.width // 2, im.height // 2), Image.LANCZOS)
for clip, c in clips.items():
    n = len(c['times']); loop = c['loop']; ev = c['events']
    tn = lambda f: f / (n - 1)
    cols = [f for f in COLS.get(clip, range(n)) if f < n]
    cells = {v: [load(clip, v, f) for f in cols] for v in ('high', 'front', 'side')}
    cw = max(im.width for v in cells for im in cells[v]); rows = [max(im.height for im in cells[v]) for v in cells]
    head = 34; sheet = Image.new('RGB', (cw * len(cols), head + sum(rows) + 18 * 3), (235, 235, 238)); d = ImageDraw.Draw(sheet)
    fr = c['frames']
    d.text((6, 4), f"{clip}: {fr} frames{' loop (+closing frame)' if loop else ''}, keys at k/30 s from 0; events {ev or '-'}; rows: gameplay-like high view (native scale), front 3/4, right profile", fill=(0, 0, 0))
    y = head
    for (v, ims), rh in zip(cells.items(), rows):
        for i, (f, im) in enumerate(zip(cols, ims)):
            t = tn(f); tag = ' '.join(k for k, x in ev.items() if abs(x - t) < 0.5 / (n - 1))
            d.text((i * cw + 4, y + 2), f'f{f}  t={t:.2f} {tag}', fill=(150, 0, 0) if tag else (0, 0, 0))
            sheet.paste(im, (i * cw + (cw - im.width) // 2, y + 16))
        y += rh + 18
    sheet.save(os.path.join(odir, f'{clip}_sheet.png'))
    frames = []
    for f in range(n):
        ims = [load(clip, v, f) for v in ('high', 'front', 'side')]
        g = Image.new('RGB', (sum(i.width for i in ims), max(i.height for i in ims) + 16), (235, 235, 238)); x = 0
        for im in ims: g.paste(im, (x, 16)); x += im.width
        ImageDraw.Draw(g).text((4, 2), f'{clip} f{f} t={tn(f):.2f} (slowed: {FRAME_MS} ms per frame)', fill=(0, 0, 0)); frames.append(g)
    dur = [FRAME_MS] * n; dur[-1] = 700
    frames[0].save(os.path.join(odir, f'{clip}.gif'), save_all=True, append_images=frames[1:], duration=dur, loop=0)
    print('SHEET', clip, len(cols), 'cols')
