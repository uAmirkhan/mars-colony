"""Разбор листа генерации на отдельные объекты.

Gemini отдает сетку из нескольких объектов на одном хромакее. TRELLIS требует
ровно один объект в кадре: два объекта сеть слепит в один меш. Резать руками по
координатам нельзя — сетка каждый раз своя, а подписи под объектами уезжают в
геометрию.

Скрипт снимает зеленый фон тем же способом, что и scripts/prep-assets.mjs
(порог отмеряется долей от зелени самого фона, а не абсолютным числом), потом
разбирает кадр на связные области и сохраняет каждую крупную область отдельным
PNG с прозрачностью. Подписи отсеиваются по размеру.

Запуск: python split-sheet.py лист.png папка_вывода [префикс]
"""

import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

# Доли от зелени фона: выше BG_CUT — фон, ниже BG_KEEP — своя краска объекта.
BG_CUT, BG_KEEP = 0.6, 0.25
DIST_CLEAR, DIST_SOLID = 60.0, 150.0
# Область меньше этой доли самой крупной — подпись или мусор, не объект.
MIN_SHARE = 0.06
PAD = 24  # поле вокруг объекта, чтобы силуэт не упирался в край


def build_alpha(rgb):
    corners = np.array([rgb[2, 2], rgb[2, -3], rgb[-3, 2], rgb[-3, -3]], dtype=float)
    bg = np.median(corners, axis=0)
    bg_excess = max(12.0, bg[1] - max(bg[0], bg[2]))
    cut, keep = bg_excess * BG_CUT, bg_excess * BG_KEEP

    a = rgb.astype(float)
    excess = a[:, :, 1] - np.maximum(a[:, :, 0], a[:, :, 2])
    dist = np.linalg.norm(a - bg, axis=2)

    by_green = np.clip((cut - excess) / (cut - keep), 0, 1)
    by_dist = np.clip((dist - DIST_CLEAR) / (DIST_SOLID - DIST_CLEAR), 0, 1)
    return np.maximum(by_green, by_dist), bg


def unblend(rgb, alpha, bg):
    """Полупрозрачный пиксель — цвет объекта, смешанный с фоном. Разворачиваем."""
    out = rgb.astype(float)
    m = (alpha > 0.12) & (alpha < 1)
    for k in range(3):
        ch = out[:, :, k]
        ch[m] = np.clip((ch[m] - (1 - alpha[m]) * bg[k]) / alpha[m], 0, 255)
    return out


def main(sheet, out_dir, prefix):
    img = Image.open(sheet).convert("RGB")
    rgb = np.array(img)
    alpha, bg = build_alpha(rgb)
    rgb = unblend(rgb, alpha, bg)

    mask = alpha > 0.5
    # Смыкаем разрывы: тонкие детали (антенны, буры) иначе отваливаются в свои
    # области и объект приезжает разобранным.
    mask = ndimage.binary_closing(mask, np.ones((9, 9)))
    labels, n = ndimage.label(mask)
    if n == 0:
        print("объектов не найдено")
        return

    sizes = ndimage.sum(mask, labels, range(1, n + 1))
    keep = [i + 1 for i in range(n) if sizes[i] >= sizes.max() * MIN_SHARE]

    # Порядок как на листе: сверху вниз, слева направо.
    boxes = ndimage.find_objects(labels)
    keep.sort(key=lambda i: (boxes[i - 1][0].start // 200, boxes[i - 1][1].start))

    Path(out_dir).mkdir(parents=True, exist_ok=True)
    for n_out, lab in enumerate(keep, 1):
        ys, xs = boxes[lab - 1]
        y0, y1 = max(0, ys.start - PAD), min(rgb.shape[0], ys.stop + PAD)
        x0, x1 = max(0, xs.start - PAD), min(rgb.shape[1], xs.stop + PAD)

        piece_alpha = np.where(labels[y0:y1, x0:x1] == lab, alpha[y0:y1, x0:x1], 0)
        piece = np.dstack([rgb[y0:y1, x0:x1], piece_alpha * 255]).astype(np.uint8)

        path = Path(out_dir) / f"{prefix}-{n_out:02d}.png"
        Image.fromarray(piece, "RGBA").save(path)
        print(f"{path.name}: {x1 - x0}x{y1 - y0}, пикселей {int(sizes[lab - 1])}")

    print(f"всего объектов: {len(keep)} (областей найдено {n})")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else "obj")
