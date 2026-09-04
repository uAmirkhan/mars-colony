"""Вырезка предмета из генерации нейросетью (rembg), а не по цвету.

Эвристики по цвету/оттенку/мягкости (ikonka_vyrezka.py, element_narezka.py)
брали вместе с предметом его тень-подставку или отрезали серые детали.
Сеть видит предмет как предмет: подставка уходит, контур целый (проверено на
монете, баллоне, супе, реголите 2026-09-05). Модель по умолчанию u2net: BiRefNet
и isnet отрезают белые коробочки хлопка как фон; для элементов интерфейса на
сплошном фоне BiRefNet чище по кромке (--model birefnet-general-lite).

    python ikonka_rembg.py <папка|файл> --out <папка> [--razmer 512] [--cvet FBEBBA] [--tolshchina 0.021]
                          [--bez-obvodki] [--model birefnet-general-lite] [--pole 0.08]

Иконки товаров: квадрат, поля 8%, кремовая обводка. Элементы интерфейса:
--bez-obvodki --pole 0 (обрезка по bbox, без квадрата: --ne-kvadrat).
"""
import os, sys, argparse
import numpy as np
from PIL import Image, ImageFilter
from rembg import remove, new_session


def obrabotat(put, out, sess, razmer, cvet, dolya, obvodka, pole_dolya, kvadrat, erozia=0):
    im = Image.open(put).convert("RGB")
    # у сети берём только маску: под альфой 0 она обнуляет и цвет, а полости
    # силуэта нужно возвращать исходным цветом
    maska = remove(im, session=sess, only_mask=True).convert("L")
    rgba = im.convert("RGBA"); rgba.putalpha(maska)
    # мусор: крохи вне главного объекта — отбросить компоненты меньше 0.2% площади
    bb = rgba.getbbox()
    if bb is None:
        print("пусто:", put); return
    rgba = rgba.crop(bb)
    if kvadrat:
        s = max(rgba.size); pole = int(s * pole_dolya); s2 = s + pole * 2
        holst = Image.new("RGBA", (s2, s2), (0, 0, 0, 0))
        holst.paste(rgba, ((s2 - rgba.width) // 2, (s2 - rgba.height) // 2))
        holst = holst.resize((razmer, razmer), Image.LANCZOS)
    else:
        holst = rgba
    if erozia > 0:
        # сжать альфу на N px: убирает розовый/зелёный ореол JPEG-кромки от фона
        # генерации (у элементов интерфейса на сплошном фоне заметен как линия)
        alpha_e = holst.getchannel("A").filter(ImageFilter.MinFilter(erozia * 2 + 1))
        holst.putalpha(alpha_e)
    if obvodka:
        t = max(1, int(round(holst.width * dolya)))
        alpha = holst.getchannel("A")
        rasshir = alpha.filter(ImageFilter.MaxFilter(t * 2 + 1))
        r, g, b = int(cvet[0:2], 16), int(cvet[2:4], 16), int(cvet[4:6], 16)
        kontur = Image.new("RGBA", holst.size, (r, g, b, 0)); kontur.putalpha(rasshir)
        holst = Image.alpha_composite(kontur, holst)
    holst.save(out)
    dolya_obj = (np.asarray(holst.getchannel("A")) > 127).mean() * 100
    print("готово: %s  %dx%d  предмет %.0f%% кадра" % (os.path.basename(out), holst.width, holst.height, dolya_obj))


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("vhod"); ap.add_argument("--out", required=True)
    ap.add_argument("--razmer", type=int, default=512); ap.add_argument("--cvet", default="FBEBBA")
    ap.add_argument("--tolshchina", type=float, default=0.021)
    ap.add_argument("--bez-obvodki", action="store_true"); ap.add_argument("--ne-kvadrat", action="store_true")
    ap.add_argument("--erozia", type=int, default=0, help="сжать альфу на N px (ореол кромки у элементов интерфейса)")
    ap.add_argument("--pole", type=float, default=0.08); ap.add_argument("--model", default="u2net", help="u2net держит белые части предметов (хлопок), birefnet-general-lite чище по кромке для элементов интерфейса")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    sess = new_session(a.model)
    fs = [a.vhod] if os.path.isfile(a.vhod) else [os.path.join(a.vhod, f) for f in sorted(os.listdir(a.vhod))
          if f.lower().endswith((".png", ".jpg", ".jpeg"))]
    for f in fs:
        obrabotat(f, os.path.join(a.out, os.path.splitext(os.path.basename(f))[0] + ".png"), sess,
                  a.razmer, a.cvet, a.tolshchina, not a.bez_obvodki, a.pole, not a.ne_kvadrat, a.erozia)
