"""Package ImageGen PNGs; only trim transparent margins, resize, and lay out QA sheets.

Usage: python package_icons.py PATH_TO_GENERATED_IMAGES
Requires Pillow. Does not generate or repaint artwork.
"""
import hashlib
import json
import re
import sys
import uuid
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageOps

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
DEST = ROOT / 'unity/Assets/LastBell/Run/Resources/Run/Icons'
SOURCE = Path(sys.argv[1])
manifest = json.loads((HERE / 'generation-manifest.json').read_text(encoding='utf-8'))
DEST.mkdir(parents=True, exist_ok=True)
template = (ROOT / 'unity/Assets/LastBell/Arenas/KeyArt/Generated/CloudPuff.png.meta').read_text()
report = []
icons = []
for item in manifest:
    source_name = item['source'].replace('\\', '/').split('/')[-1]
    item['source'] = source_name
    source = SOURCE / source_name
    with Image.open(source) as raw:
        assert raw.mode == 'RGBA', (item['file'], raw.mode)
        alpha = raw.getchannel('A')
        assert alpha.getextrema() == (0, 255), item['file']
        bounds = alpha.getbbox()
        glyph = raw.crop(bounds)
        glyph.thumbnail((194, 194), Image.Resampling.LANCZOS)
        result = Image.new('RGBA', (256, 256))
        result.alpha_composite(glyph, ((256-glyph.width)//2, (256-glyph.height)//2))
    target = DEST / item['file']
    result.save(target, optimize=True)
    box = result.getchannel('A').getbbox()
    assert all(v >= 31 for v in (box[0], box[1], 256-box[2], 256-box[3]))
    assert result.getpixel((0,0))[3] == 0
    assert result.getchannel('A').getextrema() == (0,255)
    guid = uuid.uuid5(uuid.NAMESPACE_URL, 'lastbell/ability-icons/' + item['file']).hex
    meta = re.sub(r'^guid: .*$', 'guid: ' + guid, template, flags=re.M)
    meta = meta.replace('enableMipMap: 1', 'enableMipMap: 0')
    meta = meta.replace('maxTextureSize: 2048', 'maxTextureSize: 256')
    meta = meta.replace('textureCompression: 1', 'textureCompression: 0')
    meta = meta.replace('nPOTScale: 1', 'nPOTScale: 0')
    (DEST / (item['file']+'.meta')).write_text(re.sub(r'[ \t]+$', '', meta, flags=re.M), encoding='utf-8')
    report.append({'file':item['file'], 'size':[256,256], 'mode':'RGBA', 'alpha_extrema':[0,255], 'visible_bounds':box, 'sha256':hashlib.sha256(target.read_bytes()).hexdigest(), 'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest()})
    icons.append(result)

folder_guid = uuid.uuid5(uuid.NAMESPACE_URL,'lastbell/ability-icons/').hex
DEST.with_suffix('.meta').write_text(f'fileFormatVersion: 2\nguid: {folder_guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
(HERE / 'generation-manifest.json').write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
(HERE / 'validation.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')

try:
    font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 18)
    small = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 14)
    heading = ImageFont.truetype('C:/Windows/Fonts/arialbd.ttf', 28)
except OSError:
    font = small = heading = ImageFont.load_default()

# Three columns of pairs; each pair shows real 256 px dark/light and 64 px dark/light/grayscale.
cell_w, cell_h = 568, 410
sheet = Image.new('RGB',(cell_w*3+40,cell_h*6+110),'#101825')
d = ImageDraw.Draw(sheet)
d.text((24,18),'LAST BELL / 18 ABILITY ICONS',font=heading,fill='#F6C968')
d.text((24,57),'256 px dark + light / 64 px dark + light + grayscale below each pair',font=font,fill='#F4E9D1')
# Display rows by class, using Vanguard + potion to fill six slots.
order = list(range(5))+[11]+list(range(5,11))+list(range(12,18))
for n,i in enumerate(order):
    x,y=20+(n%3)*cell_w,100+(n//3)*cell_h
    d.text((x,y),manifest[i]['name'],font=font,fill='#F4E9D1')
    d.text((x,y+24),manifest[i]['file'],font=small,fill='#9EAAC0')
    for k,bg in enumerate(('#101825','#F4E9D1')):
        panel = Image.new('RGBA',(256,256),bg)
        panel.alpha_composite(icons[i])
        sheet.paste(panel.convert('RGB'),(x+k*268,y+48))
    thumb=icons[i].resize((64,64),Image.Resampling.LANCZOS)
    gray=ImageOps.grayscale(thumb).convert('RGBA');gray.putalpha(thumb.getchannel('A'))
    for k,(bg,im) in enumerate((('#101825',thumb),('#F4E9D1',thumb),('#101825',gray),('#F4E9D1',gray))):
        panel=Image.new('RGBA',(64,64),bg);panel.alpha_composite(im)
        sheet.paste(panel.convert('RGB'),(x+k*88,y+318))
sheet.save(HERE/'icons-contact-sheet.png',optimize=True)

# Compact overview uses 128 px icons; native 64 px QA is separate for easy inspection.
for filename,size in [('icons-overview.png',128),('icons-64px-check.png',64)]:
    w=188 if size==128 else 188
    h=202 if size==128 else 188
    canvas=Image.new('RGB',(w*6+32,h*3+76),'#101825')
    dc=ImageDraw.Draw(canvas)
    dc.text((20,16),'LAST BELL / VANGUARD + POTION / CHIMESINGER / ARCANIST',font=font,fill='#F6C968')
    for n,i in enumerate(order):
        x,y=16+(n%6)*w,64+(n//6)*h
        im=icons[i].resize((size,size),Image.Resampling.LANCZOS)
        canvas.paste(im,(x+(w-size)//2,y),im)
        if size==64:
            gray=ImageOps.grayscale(im).convert('RGBA');gray.putalpha(im.getchannel('A'))
            panel=Image.new('RGBA',(64,64),'#F4E9D1');panel.alpha_composite(gray)
            canvas.paste(panel.convert('RGB'),(x+(w-size)//2,y+68))
        label=manifest[i]['name'].replace(' (Vanguard)','').replace(' (Arcanist)','')
        dc.text((x+8,y+(138 if size==128 else 142)),label,font=small,fill='#F4E9D1')
    canvas.save(HERE/filename,optimize=True)

prompts = '# Ability icon generation prompts\n\nTool: built-in `image_gen.imagegen`; transparent_background=true. Generated October 21, 2026. One call per icon, no reference images or third-party icon assets.\n\nArt brief: [issue #43](https://github.com/Risxhb/collab/issues/43) including its Arcanist addition; palette and icon grammar from the supplied Last Bell art guide sections 5 and 13.5.\n\nPackaging uses Pillow to trim fully transparent margins, proportionally downsample each glyph into a 194 px box, and center it on a transparent 256 px canvas (at least 31 px padding). Generated alpha is preserved. No glyph repainting or background removal is performed. Contact sheets composite copies on dark/light backgrounds and convert preview copies to grayscale.\n\n'
for item in manifest:
    prompts += '## '+item['file']+'\n\n'+item['prompt']+'\n\n'
(HERE/'icons-prompts.md').write_text(prompts.rstrip()+'\n',encoding='utf-8')
print(f'Packaged and validated {len(report)} RGBA icons; contact sheets and prompts written.')
