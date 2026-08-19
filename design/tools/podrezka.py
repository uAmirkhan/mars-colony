"""Подрезка входной картинки под объект и добеливание фона.

Лечение плиты по таблице раздела 6 гида. Генератор достраивает основание из
пустого поля вокруг объекта: чем больше пустоты и чем грязнее её тон, тем
охотнее он лепит под объект блин. Обрезка вплотную и чистый белый фон эту
подсказку убирают.

Запуск: python podrezka.py вход.jpg выход.png [поле_в_долях]
"""

import sys

import numpy as np
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]
margin = float(sys.argv[3]) if len(sys.argv) > 3 else 0.04

img = Image.open(src).convert("RGB")
a = np.array(img).astype(int)

# Фон у этих генераций белый, но не идеально: тени и виньетка дают 240-250.
# Порог 235 отделяет объект вместе с его тенью, а не по одному пикселю.
not_white = (a.sum(axis=2) < 235 * 3)
ys, xs = np.where(not_white)
if len(xs) == 0:
    raise SystemExit("объект не найден: кадр весь белый")

pad = int(max(xs.max() - xs.min(), ys.max() - ys.min()) * margin)
x0, x1 = max(0, xs.min() - pad), min(a.shape[1], xs.max() + pad)
y0, y1 = max(0, ys.min() - pad), min(a.shape[0], ys.max() + pad)
crop = img.crop((x0, y0, x1, y1))

# Квадрат на чистом белом: генератор не любит вытянутый кадр, а серый фон
# читает как поверхность и достраивает под объект основание.
side = max(crop.size)
canvas = Image.new("RGB", (side, side), (255, 255, 255))
canvas.paste(crop, ((side - crop.width) // 2, (side - crop.height) // 2))

clean = np.array(canvas).astype(int)
clean[clean.sum(axis=2) > 245 * 3] = 255
Image.fromarray(clean.astype(np.uint8)).save(dst)
print(f"{dst}: было {img.size}, стало {canvas.size}, поле {pad} px")
