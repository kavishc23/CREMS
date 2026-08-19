from pathlib import Path
from PIL import Image, ImageDraw

files = sorted(Path('tmp/fsd_document/rendered').glob('page-*.png'), key=lambda p: int(p.stem.split('-')[1]))
for start in range(0, len(files), 6):
    images = []
    for path in files[start:start + 6]:
        im = Image.open(path).convert('RGB')
        im.thumbnail((306, 396))
        images.append((path, im.copy()))
    sheet = Image.new('RGB', (640, 1230), '#d8d8d8')
    draw = ImageDraw.Draw(sheet)
    for index, (path, im) in enumerate(images):
        x = (index % 2) * 320 + 7
        y = (index // 2) * 405 + 20
        sheet.paste(im, (x, y))
        draw.text((x, y - 16), path.stem, fill='black')
    sheet.save(f'tmp/fsd_document/contact-{start // 6 + 1}.png')
