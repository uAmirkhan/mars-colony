# -*- coding: utf-8 -*-
"""Полотно дороги печём числами: утрамбованная крошка без направления.

Почему не генератором. Дорога — единственная поверхность, где направление
рисунка вредно: полотна собраны из растянутых кубов, UV у куба идут 0..1 на
всю длину сегмента, и любой направленный рисунок либо размазывается вдоль,
либо разъезжается поперёк. Нужна поверхность, у которой направления нет
вовсе, а такую проще посчитать, чем выпросить.

Что несёт материал. Урок камня: на мелком масштабе материал читается не
оттенком, а РАЗДЕЛЕНИЕМ — тёмной линией между кусками. Поэтому основа здесь
не шум, а ячейки Вороного: крошка, вмятая в связующее, и тонкий тёмный шов по
границам. Шум идёт поверх, мелкой рябью.

Ячейки строятся по дрожащей сетке и замыкаются по кольцу, поэтому плитка
бесшовна по построению — вклейка краёв не нужна.

Контраст держим в полосе 0.03-0.05: у песка 0.018, у дна карьера 0.071.
Ниже — дорога сольётся с землёй, выше — начнёт рябить, а её в кадре 12%.

Запуск:
    python design/tools/pech-dorogu.py <выход.png>
"""
import sys

import numpy as np
from PIL import Image

N = 1024
KLETKA = 26          # шаг сетки семян: средний размер крошки в пикселях
CEL = np.array([0.769, 0.659, 0.510], dtype=np.float32)   # #C4A882
KOEF = np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)

rng = np.random.default_rng(4)


def voronoy_koltsevoy(n, kletka):
    """F2 - F1 по дрожащей сетке, замкнутой по кольцу.

    Считаем не по всем семенам, а по девяти соседним клеткам: семя не выходит
    за свою клетку, дальше искать нечего.

    ГЛАВНОЕ, И НА ЭТОМ ПЕРВЫЙ ЗАХОД СЛОМАЛСЯ. Число клеток берётся округлением,
    а размер клетки — делением ОБРАТНО, дробным числом. Если взять целый шаг,
    который не делит сторону нацело (26 при 1024), сетка накроет 1014 пикселей
    из 1024, период разойдётся со стороной плитки, и правый край перестанет
    сходиться с левым. Замер тогда дал шов втрое выше обычного соседства.
    """
    k = max(2, int(round(n / float(kletka))))
    shag = n / float(k)                      # дробный шаг, сетка кроет ровно n
    tochki = (np.stack(np.meshgrid(np.arange(k), np.arange(k), indexing="ij"),
                       -1).astype(np.float32) + rng.random((k, k, 2))) * shag

    yy, xx = np.meshgrid(np.arange(n), np.arange(n), indexing="ij")
    yy = yy.astype(np.float32); xx = xx.astype(np.float32)
    ci = np.minimum((yy / shag).astype(np.int32), k - 1)
    cj = np.minimum((xx / shag).astype(np.int32), k - 1)

    bliz = np.full((n, n), 1e9, dtype=np.float32)
    vtor = np.full((n, n), 1e9, dtype=np.float32)
    for di in (-1, 0, 1):
        for dj in (-1, 0, 1):
            si, sj = (ci + di) % k, (cj + dj) % k
            # Смещение внутри клетки берём у соседа, а саму клетку — свою со
            # сдвигом. Иначе на стыке кольца семя прыгнет через весь кадр.
            ty = tochki[si, sj, 0] - si * shag + (ci + di) * shag
            tx = tochki[si, sj, 1] - sj * shag + (cj + dj) * shag
            d = np.sqrt((yy - ty) ** 2 + (xx - tx) ** 2).astype(np.float32)
            vtor = np.minimum(vtor, np.maximum(bliz, d))
            bliz = np.minimum(bliz, d)
    return bliz, vtor


def shum(n, kletka):
    """Периодический шум любого шага.

    Прежний вариант дописывал к сетке дублирующий ряд и растягивал её до
    стороны плитки. Это давало равенство первого и последнего ПИКСЕЛЯ, а нужно
    другое — чтобы последний пиксель нормально соседствовал с первым. При
    шаге, не делящем сторону, край расходился вдесятеро против соседства.

    Здесь источник размножается три на три и растягивается целиком, а берётся
    середина. Кольцо тогда получается по построению, при любом шаге.
    """
    k = max(2, int(round(n / float(kletka))))
    z = rng.random((k, k)).astype(np.float32)
    tri = np.tile(z, (3, 3))
    im = Image.fromarray((tri * 255).astype(np.uint8)).resize((n * 3, n * 3),
                                                              Image.BICUBIC)
    a = np.asarray(im, dtype=np.float32)[n:2 * n, n:2 * n] / 255.0
    return (a - a.mean()) / (a.std() + 1e-9)


def main(vykhod):
    bliz, vtor = voronoy_koltsevoy(N, KLETKA)

    # Шов между крошками: узкий там, где второе семя почти так же близко
    kray = np.clip((vtor - bliz) / (KLETKA * 0.28), 0.0, 1.0)
    kray = kray ** 0.55                       # шов тонкий, а не размазанный

    # Мелкая рябь поверх: две октавы, слабые
    ryab = shum(N, 3) * 0.55 + shum(N, 9) * 0.45
    # Медленная неровность утрамбовки — очень слабая, иначе станет пятном
    volna = shum(N, 160)

    yark = (0.82 + 0.18 * kray) * (1.0 + ryab * 0.030 + volna * 0.012)

    a = np.stack([yark * c for c in CEL], axis=-1)
    for i in range(3):
        a[..., i] *= CEL[i] / a[..., i].mean()
    a = np.clip(a, 0.0, 1.0)

    Image.fromarray((a * 255 + 0.5).astype(np.uint8)).save(vykhod)

    L = a @ KOEF
    k = N // 16
    n16 = L[:16 * k, :16 * k].reshape(16, k, 16, k).mean(axis=(1, 3))
    sr = a.reshape(-1, 3).mean(0)
    shov_g = float(np.abs(L[:, :1].mean(1) - L[:, -1:].mean(1)).mean())
    shov_v = float(np.abs(L[:1, :].mean(0) - L[-1:, :].mean(0)).mean())
    print("цвет      #%02X%02X%02X   цель #C4A882" % tuple(int(c * 255 + .5) for c in sr))
    print(f"пятна     {float(n16.max() - n16.min()):.3f}")
    print(f"контраст  {float(L.std()):.4f}   (песок 0.018, карьер 0.071)")
    print(f"швы       гор {shov_g:.5f}  вер {shov_v:.5f}")
    print("записан:", vykhod)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "tile_road.png")
