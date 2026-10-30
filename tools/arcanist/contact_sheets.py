"""Contact sheets and GIFs from ArcanistShowcase captures (docs/design/arcanist-fx-prompts.md review rounds).

    python -I tools/arcanist/contact_sheets.py <showcase dir> [frames per sheet row]

For every sequence folder (g_###.png gameplay camera, c_###.png review camera, 30 fps) writes next to it:
  <name>_close.png     review-camera frames, evenly spaced, labelled with their time
  <name>_game.png      gameplay-camera frames (full view, downscaled), same times
  <name>_close.gif / <name>_game.gif   every frame at 30 fps (half size)
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont


def frames(folder, prefix):
    names = sorted(n for n in os.listdir(folder) if n.startswith(prefix) and n.endswith('.png'))
    return [os.path.join(folder, n) for n in names]


def sheet(paths, picks, cell_w, out, title):
    if not paths:
        return
    idx = sorted(set(round(i * (len(paths) - 1) / max(1, picks - 1)) for i in range(picks)))
    first = Image.open(paths[0])
    cell_h = round(cell_w * first.height / first.width)
    cols = min(len(idx), 4)
    rows = (len(idx) + cols - 1) // cols
    img = Image.new('RGB', (cols * cell_w, rows * cell_h + 28), (18, 16, 24))
    d = ImageDraw.Draw(img)
    try:
        font = ImageFont.truetype('arial.ttf', 18)
    except OSError:
        font = ImageFont.load_default()
    d.text((8, 4), title, fill=(230, 220, 255), font=font)
    for k, i in enumerate(idx):
        im = Image.open(paths[i]).convert('RGB').resize((cell_w, cell_h), Image.LANCZOS)
        x, y = (k % cols) * cell_w, 28 + (k // cols) * cell_h
        img.paste(im, (x, y))
        d.rectangle([x, y, x + 74, y + 22], fill=(0, 0, 0))
        d.text((x + 4, y + 2), f't={i / 30:.2f}s', fill=(255, 255, 255), font=font)
    img.save(out)


def gif(paths, out, scale=0.5):
    if not paths:
        return
    ims = []
    for p in paths:
        im = Image.open(p).convert('RGB')
        ims.append(im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS))
    ims[0].save(out, save_all=True, append_images=ims[1:], duration=33, loop=0)


def main():
    root = sys.argv[1]
    picks = int(sys.argv[2]) if len(sys.argv) > 2 else 12
    for name in sorted(os.listdir(root)):
        folder = os.path.join(root, name)
        if not os.path.isdir(folder):
            continue
        c, g = frames(folder, 'c_'), frames(folder, 'g_')
        n = min(24, len(c)) if len(c) <= 36 else picks   # short sequences (blink): every frame
        sheet(c, n, 480, os.path.join(root, f'{name}_close.png'), f'{name} - review camera ({len(c)} frames, 30 fps)')
        sheet(g, n, 480, os.path.join(root, f'{name}_game.png'), f'{name} - gameplay camera ({len(g)} frames, 30 fps)')
        gif(c, os.path.join(root, f'{name}_close.gif'))
        gif(g, os.path.join(root, f'{name}_game.gif'))
        print(name, len(c), len(g))


if __name__ == '__main__':
    main()
