# blender -b -P trims.py -- <out dir>
# Paints the two shared Area 4 trim sheets inside Blender (numpy pixel painting, saved through bpy images):
#   StoneTrim_BL.png / StoneTrim_BL_Height.png  2048², 2.4 m tile: four 0.6 m courses of staggered chamfered blocks,
#                                               broad planar value shifts, chipped edges, soft joint AO (house style)
#   BrassTrim_BL.png                            2048², aged brass breakup: warm base, umber/green shadow mottling,
#                                               soft brushed streaks (no mirror look; the colour comes from the material)
import bpy, sys, os, numpy as np

args = sys.argv[sys.argv.index('--') + 1:]
out = args[0]; os.makedirs(out, exist_ok=True)
N = 2048
rng = np.random.default_rng(3)

def tile_noise(n, cells, seed):
    """Tileable value noise, n x n, `cells` lattice cells per side, smooth-stepped bilinear."""
    g = np.random.default_rng(seed).random((cells, cells))
    t = np.arange(n) / n * cells; i0 = np.floor(t).astype(int); f = t - i0; f = f * f * (3 - 2 * f); i1 = (i0 + 1) % cells
    a = g[np.ix_(i0, i0)]; b = g[np.ix_(i0, i1)]; c = g[np.ix_(i1, i0)]; d = g[np.ix_(i1, i1)]
    fy = f[:, None]; fx = f[None, :]
    return a * (1 - fx) * (1 - fy) + b * fx * (1 - fy) + c * (1 - fx) * fy + d * fx * fy

def save(name, rgb, alpha=None):
    h, w = rgb.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=alpha is not None)
    px = np.ones((h, w, 4), np.float32); px[..., :3] = rgb
    if alpha is not None: px[..., 3] = alpha
    img.pixels.foreach_set(px.ravel()); img.filepath_raw = os.path.join(out, name + '.png'); img.file_format = 'PNG'; img.save()
    print('wrote', img.filepath_raw)

def hexc(h): return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)])

# ---- stone blocks ----
row = N // 4
alb = np.zeros((N, N, 3)); hgt = np.zeros((N, N))
low, high, mortar = hexc('2B2A3A'), hexc('3E3C53'), hexc('17161F')
broad = tile_noise(N, 16, 4); fine = tile_noise(N, 160, 5); chips = tile_noise(N, 90, 6)
ys, xs = np.mgrid[0:N, 0:N]
for k in range(4):
    cuts = [int(rng.integers(0, 380))]
    while cuts[-1] < N + cuts[0] - 450: cuts.append(cuts[-1] + int(rng.integers(440, 820)))
    cuts.append(cuts[0] + N)
    band = slice(k * row, (k + 1) * row)
    x = xs[band]; y = ys[band] - k * row
    xx = np.where(x < cuts[0], x + N, x)
    bi = np.searchsorted(cuts, xx, side='right') - 1; bi = np.clip(bi, 0, len(cuts) - 2)
    l = xx - np.array(cuts)[bi]; r = np.array(cuts)[bi + 1] - xx
    e = np.minimum(np.minimum(y, row - y), np.minimum(l, r)).astype(float)
    tone = np.random.default_rng(k * 31).random(len(cuts))[bi]
    c = low + (high - low) * (tone * 0.55 + broad[band] * 0.35)[..., None]
    c *= (0.93 + 0.12 * fine[band])[..., None]
    c *= (1 + 0.07 * (l / (l + r) - 0.5))[..., None]                     # planar shift across each block
    lit = (y > row - 26) | (l < 26)
    cham = e < 26
    c = np.where((cham & lit)[..., None], c * 1.17, c); c = np.where((cham & ~lit)[..., None], c * 0.78, c)
    chip = (chips[band] < 0.12) & (e < 70); c = np.where(chip[..., None], c * 0.8, c)
    ao = np.clip((e - 6) / 30, 0, 1); c *= (0.8 + 0.2 * ao)[..., None]
    c = np.where((e < 6)[..., None], mortar + (c - mortar) * (e / 12)[..., None], c)
    alb[band] = c
    h = 0.55 + 0.45 * np.clip((e - 3) / 23, 0, 1) - chip * 0.15 + (broad[band] - 0.5) * 0.08
    hgt[band] = np.where(e < 6, 0.1, h)
save('StoneTrim_BL', np.clip(alb, 0, 1))
save('StoneTrim_BL_Height', np.repeat(np.clip(hgt, 0, 1)[..., None], 3, 2))

# ---- aged brass ----
m = tile_noise(N, 10, 8); s = tile_noise(N, 220, 9); streak = np.repeat(tile_noise(N, 256, 10)[:, :1], N, 1).T  # vertical streaks
umber = np.array([0.6, 0.53, 0.38])
t = np.clip((m - 0.45) / 0.35, 0, 1); t = t * t * (3 - 2 * t)
br = np.ones((N, N, 3)) * (1 - 0.35 * t[..., None]) + umber * 0.35 * t[..., None]
br *= (0.94 + 0.06 * s + 0.04 * streak)[..., None]
save('BrassTrim_BL', np.clip(br, 0, 1))
