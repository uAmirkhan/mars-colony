"""Контактный лист: все модели набора в ряд, чтобы видеть, держится ли он одной рукой.

Запуск: python kontaktnyy-list.py папка_рендеров суффикс выход.png [колонок]
Суффикс — какой ракурс собирать: tri-chetverti, sboku, snizu, nogot.
"""

import sys
from pathlib import Path

from PIL import Image, ImageDraw

src_dir, suffix, dst = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
cols = int(sys.argv[4]) if len(sys.argv) > 4 else 6
cell, pad, label_h = 256, 6, 16

files = sorted(src_dir.glob(f"*-{suffix}.png"))
rows = (len(files) + cols - 1) // cols
sheet = Image.new("RGB", (cols * (cell + pad), rows * (cell + pad + label_h)), "white")
draw = ImageDraw.Draw(sheet)

for i, path in enumerate(files):
    x = (i % cols) * (cell + pad)
    y = (i // cols) * (cell + pad + label_h)
    sheet.paste(Image.open(path).convert("RGB").resize((cell, cell), Image.LANCZOS), (x, y))
    draw.text((x + 2, y + cell + 2), path.stem.replace(f"-{suffix}", "")[:34], fill="black")

sheet.save(dst)
print(f"{dst}: {len(files)} моделей, {cols} в ряд")
