# blender -b -P aurelion_meshy_views.py -- <sheet.png> <out dir>
# Cuts the FRONT / SIDE / BACK turnaround views out of the Aurelion model sheet (01-aurelion-v1.png, 1536x1024) and
# saves each centred on a 1024 px square of the sheet's paper colour, ready for Meshy's multi-view image-to-3D.
# Boxes are in sheet pixels (x0, y0, x1, y1), top-left origin, chosen to exclude the labels, the height ruler and
# the details panel.
import bpy, sys, os, numpy as np
args = sys.argv[sys.argv.index('--') + 1:]
src, out = args[0], args[1]; os.makedirs(out, exist_ok=True)
BOXES = {
    'front': (226, 56, 548, 545),
    'side': (572, 56, 790, 545),
    'back': (812, 56, 1085, 545),
}
ERASE = [(226, 56, 340, 74), (226, 255, 236, 290)]   # the "4.2 m." labels (top and the ruler's mid label)
RULE_ROWS = (70, 82)                                  # the thin brass height rule across the top of the views
SIZE, MARGIN = 1024, 0.08

sheet = bpy.data.images.load(src)
W, H = sheet.size
px = np.array(sheet.pixels[:], dtype=np.float32).reshape(H, W, 4)[::-1]   # flip to top-left origin

def paper_near(x0, y0, x1, y1):
    # the panel's own paper colour: median of the box's top corners (the turnaround panels are lighter than the sheet)
    return np.median(np.concatenate([px[y0:y0 + 12, x0:x0 + 12].reshape(-1, 4), px[y0:y0 + 12, x1 - 12:x1].reshape(-1, 4)]), axis=0)

def is_paper(c, paper): return np.abs(c[..., :3] - paper[:3]).max(-1) < 0.06

for name, (x0, y0, x1, y1) in BOXES.items():
    paper = paper_near(x0, y0, x1, y1)
    work = px.copy()
    for ex0, ey0, ex1, ey1 in ERASE:
        work[ey0:ey1, ex0:ex1] = paper
    # rule line: repaint a rule pixel unless the figure continues both above and below it, so the spire and halo
    # tips that cross the line survive
    r0, r1 = RULE_ROWS
    above, below = is_paper(work[r0 - 3], paper), is_paper(work[r1 + 3], paper)
    for y in range(r0, r1): work[y, above | below] = paper
    crop = work[y0:y1, x0:x1].copy()
    crop[is_paper(crop, paper)] = paper   # flatten paper grain so the crop has no visible edge on the canvas
    h, w = crop.shape[:2]
    k = SIZE * (1 - 2 * MARGIN) / max(w, h)
    nw, nh = max(1, round(w * k)), max(1, round(h * k))
    tmp = bpy.data.images.new('tmp_' + name, w, h, alpha=True)
    tmp.pixels[:] = crop[::-1].ravel()
    tmp.scale(nw, nh)                                                      # Blender's bilinear upscale
    big = np.array(tmp.pixels[:], dtype=np.float32).reshape(nh, nw, 4)[::-1]
    canvas = np.tile(paper, (SIZE, SIZE, 1))
    ox, oy = (SIZE - nw) // 2, (SIZE - nh) // 2
    canvas[oy:oy + nh, ox:ox + nw] = big
    canvas[..., 3] = 1
    img = bpy.data.images.new('aurelion_' + name, SIZE, SIZE, alpha=True)
    img.pixels[:] = canvas[::-1].ravel()
    img.filepath_raw = os.path.join(out, f'aurelion-{name}.png'); img.file_format = 'PNG'; img.save()
    print('VIEW', name, (w, h), '->', (nw, nh))
