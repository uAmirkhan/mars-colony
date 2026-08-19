# -*- coding: utf-8 -*-
"""Подготовка исходника тайла ДО запекателя: снять волну, поднять к цели.

Зачем отдельный шаг, если запекатель и так приводит средний цвет к цели.

Средний цвет ничего не говорит о том, как яркость разложена по кадру. Замер на
камне `q88zmh`: в исходнике пятна 0.097, то есть кадр ровный, а после
запекания 0.233 — вдвое хуже нынешнего. Запекатель их не унаследовал, он их
СДЕЛАЛ: картинка была на 16 пунктов темнее цели, и он вытянул её множителем
1.29, растянув вместе со средним и весь разброс.

Отсюда два движения, оба до запекателя:

1. **Снять крупную волну.** Медленный перепад «угол светлее, полоса темнее»
   при замощении и есть штамп: он крупный, глаз ловит его именно как повтор.
   Вычитаем сильно размытую копию яркости. Размываем по кольцу (копия
   замкнута сама на себя), иначе у краёв поймаем рамку вместо волны.

2. **Поднять к цели гаммой, а не множителем.** Множитель тянет светлые и
   тёмные одинаково и раздувает разброс. Гамма жмёт светлые сильнее тёмных,
   поэтому средний приходит на место, а разброс остаётся.

Результат на том же камне: пятна после запекания 0.233 -> 0.155, контраст
0.0756 -> 0.0511, зерно цело.

Запуск:
    python design/tools/podgotovit-tayl.py <вход.jpg> <выход.png> <R> <G> <B>
"""
import sys

import numpy as np
from PIL import Image, ImageFilter

KOEF = np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
RADIUS = 45.0


def podgotovit(vhod, vykhod, cel):
    a = np.asarray(Image.open(vhod).convert("RGB"), dtype=np.float32) / 255.0
    cel = np.asarray(cel, dtype=np.float32)

    L = a @ KOEF
    H, W = L.shape
    tri = np.tile((np.clip(L, 0, 1) * 255).astype(np.uint8), (3, 3))
    volna = np.asarray(
        Image.fromarray(tri).filter(ImageFilter.GaussianBlur(RADIUS)),
        dtype=np.float32)[H:2 * H, W:2 * W] / 255.0
    a = np.clip(a * np.clip(L.mean() / np.maximum(volna, 1e-3), 0.72, 1.38)[..., None], 0, 1)

    for i in range(3):
        k = a[..., i]
        gam = np.log(max(float(cel[i]), 1e-3)) / np.log(max(float(k.mean()), 1e-3))
        a[..., i] = k ** float(np.clip(gam, 0.25, 4.0))
    for i in range(3):
        a[..., i] *= float(cel[i]) / float(a[..., i].mean())
    a = np.clip(a, 0.0, 1.0)

    Image.fromarray((a * 255 + 0.5).astype(np.uint8)).save(vykhod)

    L2 = a @ KOEF
    k = min(L2.shape) // 16
    n = L2[:16 * k, :16 * k].reshape(16, k, 16, k).mean(axis=(1, 3))
    s = a.reshape(-1, 3).mean(0)
    print("подготовлен %s: #%02X%02X%02X  пятна %.3f  контраст %.4f"
          % (vykhod, *[int(c * 255 + .5) for c in s],
             float(n.max() - n.min()), float(L2.std())))


if __name__ == "__main__":
    if len(sys.argv) < 6:
        raise SystemExit(__doc__)
    podgotovit(sys.argv[1], sys.argv[2], [float(x) for x in sys.argv[3:6]])
