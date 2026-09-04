#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Перекраска PNG-спрайтов готового UI-кита под палитру Mars Colony.

Заказчик запретил собирать интерфейс из примитивов: основа — готовый CC0-кит
(`raw/ui-kits/pzuh/png/`, `raw/ui-kits/wenrexa/Blue/`). Киты кремовые/синие,
курс проекта — холодное тёмное стекло. Наивная замена цвета (залить весь
спрайт одним HEX) убивает объём: у панелей и кнопок кита есть ход по
светлоте (блик сверху, тень снизу) — это и есть механика сочности Township.
Значит перекраска обязана трогать только цвет (H, S), а светлоту (V) —
переносить как есть, пиксель в пиксель.

Два режима:

    tint  — весь спрайт целиком в один целевой цвет (H и S берутся из --hex).
            python kit_perekraska.py <файл_или_папка> tint --hex 102842 --out <папка>

    karta — перекраска по карте соответствия: несколько исходных оттенков
            уходят в несколько разных целевых (кремовое тело кита в один
            цвет, красная лента — в другой). Пары "hue источника:hue цели"
            в градусах 0-360, через запятую.
            python kit_perekraska.py <файл_или_папка> karta --karta "40:210,355:190" --out <папка>

Блики (почти белые пиксели, S<10%, V>92%) режим tint по умолчанию не трогает
вообще — ни H, ни S, ни V — блик остаётся белым, как у Township
(флаг --sohranit-bliki, включён по умолчанию, выключается --no-sohranit-bliki).

Опционально `--kant HEX --kant-px N` — внешний кант расширением альфы, тот же
приём, что в `ikonka_vyrezka.py`/`ikonka_obvodka.py`: маска альфы раздувается
MaxFilter, под спрайт подкладывается плашка канта. Годится, чтобы кремовую
рамку кита превратить в тёмную оправу.

Решения там, где ТЗ молчит:

- В режиме karta задаются только пары hue, без целевой насыщенности —
  значит S каждого совпавшего пикселя остаётся его собственной (меняется
  только H), V — по той же логике, что в tint (не трогать, опционально
  --v-gain/--v-shift/--v-gradient). Если пиксель попадает в допуск сразу двух
  пар карты, побеждает первая по порядку в --karta.
- `--v-gradient VERH:NIZ` — некоторые киты (Wenrexa) рисуют весь объём одним
  сплошным бликом сверху и тенью в нижних 30-35% спрайта, чего мало для хода
  по V, который требует Township (60-75 единиц, `ux-interfeys-referensy-...`,
  2.1). Это дополнительный множитель V построчно: строка с самым верхним
  непрозрачным пикселем спрайта умножается на VERH, с самым нижним — на NIZ,
  между ними линейно. Считается после --v-gain/--v-shift и подчиняется тому
  же --sohranit-bliki — блик не темнеет, даже если попал в нижнюю половину
  строк (у больших кнопок это не так, у иконок теоретически может быть).
- Доминантный hue для отчёта считается по гистограмме, взвешенной
  насыщенностью, и только по пикселям с S ≥ 15% и alpha > 0 — иначе серые
  и почти прозрачные пиксели забивают статистику шумом без цвета.
- Порог блика для отчёта "доля V>97" — это не то же самое, что порог
  сохранения блика (S<10, V>92) из задания: первый просто мера "не потерялся
  ли самый яркий блик", второй — правило, какие пиксели не трогать красящим
  преобразованием. Числа разные и оба взяты из формулировки задачи.
"""
import argparse
import colorsys
import os

import numpy as np
from PIL import Image, ImageFilter

# --- пороги блика (какие пиксели красящее преобразование не трогает) ---
BLIK_S_MAX = 0.10   # S ниже этого — кандидат в блик (из задания: S<10)
BLIK_V_MIN = 0.92   # V выше этого — кандидат в блик (из задания: V>92)

# --- порог отчёта "не потерялся ли блик" (иная величина, для печати) ---
OTCHET_BLIK_V = 0.97   # доля пикселей V>97 до/после, из задания

# --- порог доминантного hue: ниже этой насыщенности пиксель серый/белый ---
HUE_NEYTRAL_S_MIN = 0.15   # решение автора, в задании не оговорено


def rgb_v_hsv(rgb01: np.ndarray):
    """Векторизованный RGB->HSV, rgb01 — массив (...,3) во float [0,1]."""
    r, g, b = rgb01[..., 0], rgb01[..., 1], rgb01[..., 2]
    maxc = np.max(rgb01, axis=-1)
    minc = np.min(rgb01, axis=-1)
    v = maxc
    delta = maxc - minc
    s = np.where(maxc == 0, 0.0, delta / np.where(maxc == 0, 1, maxc))
    bezopasnaya_delta = np.where(delta == 0, 1, delta)
    rc = (maxc - r) / bezopasnaya_delta
    gc = (maxc - g) / bezopasnaya_delta
    bc = (maxc - b) / bezopasnaya_delta
    h = np.zeros_like(maxc)
    h = np.where(r == maxc, bc - gc, h)
    h = np.where(g == maxc, 2.0 + rc - bc, h)
    h = np.where(b == maxc, 4.0 + gc - rc, h)
    h = (h / 6.0) % 1.0
    h = np.where(delta == 0, 0.0, h)
    return h, s, v


def hsv_v_rgb(h: np.ndarray, s: np.ndarray, v: np.ndarray):
    """Векторизованный HSV->RGB, h/s/v — float [0,1], возвращает r,g,b [0,1]."""
    i = np.floor(h * 6.0)
    f = h * 6.0 - i
    p = v * (1.0 - s)
    q = v * (1.0 - s * f)
    t = v * (1.0 - s * (1.0 - f))
    ii = i.astype(np.int64) % 6
    vybor = [ii == 0, ii == 1, ii == 2, ii == 3, ii == 4, ii == 5]
    r = np.select(vybor, [v, q, p, p, t, v])
    g = np.select(vybor, [t, v, v, q, p, p])
    b = np.select(vybor, [p, p, t, v, v, q])
    return r, g, b


def hex_v_hs(hex_str: str):
    """HEX -> (H, S) целевого цвета в диапазоне [0,1] (V из hex не берём)."""
    hex_str = hex_str.lstrip("#")
    r = int(hex_str[0:2], 16) / 255.0
    g = int(hex_str[2:4], 16) / 255.0
    b = int(hex_str[4:6], 16) / 255.0
    h, s, _ = colorsys.rgb_to_hsv(r, g, b)
    return h, s


def razobrat_kartu(karta_str: str):
    """"40:210,355:190" -> [(40.0, 210.0), (355.0, 190.0)] в градусах."""
    pary = []
    for zveno in karta_str.split(","):
        zveno = zveno.strip()
        if not zveno:
            continue
        istochnik, tsel = zveno.split(":")
        pary.append((float(istochnik), float(tsel)))
    if not pary:
        raise ValueError("--karta пуста, нужна хотя бы одна пара hue:hue")
    return pary


def razobrat_gradient(gradient_str: str):
    """"1.0:0.62" -> (1.0, 0.62). None, если флаг не передан."""
    if gradient_str is None:
        return None
    verh_str, niz_str = gradient_str.split(":")
    return float(verh_str), float(niz_str)


def maska_blika(s: np.ndarray, v: np.ndarray) -> np.ndarray:
    return (s < BLIK_S_MAX) & (v > BLIK_V_MIN)


def primenit_v_gain_shift(v: np.ndarray, v_gain: float, v_shift: float) -> np.ndarray:
    return np.clip(v * v_gain + v_shift / 255.0, 0.0, 1.0)


def gradient_mnozhitel(alpha: np.ndarray, verh: float, niz: float) -> np.ndarray:
    """Множитель V построчно: от VERH на самой верхней непрозрачной строке
    до NIZ на самой нижней, линейно между ними. Строки вне непрозрачной части
    (alpha==0 по всей ширине) роли не играют — на них ничего не видно.
    """
    stroki_s_alfa = np.any(alpha > 0, axis=-1)
    vysota = alpha.shape[0]
    indeksy = np.nonzero(stroki_s_alfa)[0]
    if indeksy.size == 0:
        return np.ones((vysota,) + alpha.shape[1:], dtype=np.float64)
    verhnyaya, nizhnyaya = int(indeksy[0]), int(indeksy[-1])
    nomera_strok = np.arange(vysota, dtype=np.float64)
    if nizhnyaya > verhnyaya:
        dolya = np.clip((nomera_strok - verhnyaya) / (nizhnyaya - verhnyaya), 0.0, 1.0)
    else:
        dolya = np.zeros(vysota, dtype=np.float64)
    mnozhitel_po_strokam = verh + (niz - verh) * dolya
    forma_bez_stroki = (1,) * (alpha.ndim - 1)
    return np.broadcast_to(mnozhitel_po_strokam.reshape((vysota,) + forma_bez_stroki), alpha.shape)


def perekrasit_tint(rgb01: np.ndarray, tsel_h: float, tsel_s: float,
                     sohranit_bliki: bool, v_gain: float, v_shift: float,
                     alpha: np.ndarray, v_gradient):
    h, s, v = rgb_v_hsv(rgb01)
    novy_v = primenit_v_gain_shift(v, v_gain, v_shift)
    if v_gradient is not None:
        verh, niz = v_gradient
        novy_v = np.clip(novy_v * gradient_mnozhitel(alpha, verh, niz), 0.0, 1.0)
    if sohranit_bliki:
        blik = maska_blika(s, v)
    else:
        blik = np.zeros_like(s, dtype=bool)
    h2 = np.where(blik, h, tsel_h)
    s2 = np.where(blik, s, tsel_s)
    v2 = np.where(blik, v, novy_v)
    return hsv_v_rgb(h2, s2, v2), (h, s, v)


def perekrasit_kartu(rgb01: np.ndarray, pary_deg, dopusk_hue: float,
                      sohranit_bliki: bool, v_gain: float, v_shift: float,
                      alpha: np.ndarray, v_gradient):
    h, s, v = rgb_v_hsv(rgb01)
    h_deg = h * 360.0
    sovpalo = np.zeros_like(s, dtype=bool)
    h2 = h.copy()
    for istochnik_deg, tsel_deg in pary_deg:
        raznitsa = np.abs(h_deg - istochnik_deg)
        raznitsa = np.minimum(raznitsa, 360.0 - raznitsa)   # круговое расстояние
        sovpadenie = (raznitsa <= dopusk_hue) & (~sovpalo)
        h2 = np.where(sovpadenie, (tsel_deg % 360.0) / 360.0, h2)
        sovpalo |= sovpadenie
    if sohranit_bliki:
        blik = maska_blika(s, v)
        h2 = np.where(blik, h, h2)
        sovpalo &= ~blik
    novy_v = primenit_v_gain_shift(v, v_gain, v_shift)
    if v_gradient is not None:
        verh, niz = v_gradient
        novy_v = np.clip(novy_v * gradient_mnozhitel(alpha, verh, niz), 0.0, 1.0)
    v2 = np.where(sovpalo, novy_v, v)
    s2 = s   # карта задаёт только hue, насыщенность пикселя не трогаем (см. докстринг)
    return hsv_v_rgb(h2, s2, v2), (h, s, v)


def dobavit_kant(im: Image.Image, kant_hex: str, kant_px: int) -> Image.Image:
    """Внешний кант расширением альфы — приём из ikonka_vyrezka.py."""
    kant_hex = kant_hex.lstrip("#")
    r, g, b = int(kant_hex[0:2], 16), int(kant_hex[2:4], 16), int(kant_hex[4:6], 16)
    alpha = im.getchannel("A")
    razduto = alpha.filter(ImageFilter.MaxFilter(kant_px * 2 + 1))
    kontur = Image.new("RGBA", im.size, (r, g, b, 0))
    kontur.putalpha(razduto)
    return Image.alpha_composite(kontur, im)


def dominantny_hue_deg(h: np.ndarray, s: np.ndarray, maska: np.ndarray) -> float:
    """Мода hue по гистограмме 5°, взвешенной насыщенностью, среди цветных пикселей."""
    otbor = maska & (s >= HUE_NEYTRAL_S_MIN)
    if not np.any(otbor):
        return float("nan")
    h_deg = (h[otbor] * 360.0)
    vesa = s[otbor]
    biny = np.floor(h_deg / 5.0).astype(np.int64) % 72
    summy = np.bincount(biny, weights=vesa, minlength=72)
    return float(np.argmax(summy) * 5.0 + 2.5)


def statistika(rgb01: np.ndarray, alpha: np.ndarray, h=None, s=None, v=None) -> dict:
    maska = alpha > 0
    if h is None:
        h, s, v = rgb_v_hsv(rgb01)
    if not np.any(maska):
        return dict(hue=float("nan"), vmin=float("nan"), vmax=float("nan"), dolya_blika=float("nan"))
    return dict(
        hue=dominantny_hue_deg(h, s, maska),
        vmin=float(v[maska].min() * 100.0),
        vmax=float(v[maska].max() * 100.0),
        dolya_blika=float((v[maska] > OTCHET_BLIK_V).mean() * 100.0),
    )


def obrabotat_fail(put: str, vyhod: str, rezhim: str, args) -> None:
    im = Image.open(put).convert("RGBA")
    arr = np.asarray(im).astype(np.float64)
    rgb01 = arr[..., :3] / 255.0
    alpha = arr[..., 3]

    do = statistika(rgb01, alpha)
    v_gradient = razobrat_gradient(args.v_gradient)

    if rezhim == "tint":
        tsel_h, tsel_s = hex_v_hs(args.hex)
        (r2, g2, b2), (h, s, v) = perekrasit_tint(
            rgb01, tsel_h, tsel_s, args.sohranit_bliki, args.v_gain, args.v_shift,
            alpha, v_gradient)
    else:
        pary = razobrat_kartu(args.karta)
        (r2, g2, b2), (h, s, v) = perekrasit_kartu(
            rgb01, pary, args.dopusk_hue, args.sohranit_bliki, args.v_gain, args.v_shift,
            alpha, v_gradient)

    novy_rgb01 = np.stack([r2, g2, b2], axis=-1)
    posle = statistika(novy_rgb01, alpha, *rgb_v_hsv(novy_rgb01))

    itog = np.clip(np.round(novy_rgb01 * 255.0), 0, 255).astype(np.uint8)
    itog_im = Image.fromarray(np.dstack([itog, alpha.astype(np.uint8)]), mode="RGBA")

    if args.kant:
        itog_im = dobavit_kant(itog_im, args.kant, args.kant_px)

    itog_im.save(vyhod)
    print(
        "%-28s hue %6.1f -> %6.1f   Vmin %5.1f -> %5.1f   Vmax %5.1f -> %5.1f   "
        "блик(V>%.0f) %5.2f%% -> %5.2f%%"
        % (os.path.basename(vyhod), do["hue"], posle["hue"], do["vmin"], posle["vmin"],
           do["vmax"], posle["vmax"], OTCHET_BLIK_V * 100, do["dolya_blika"], posle["dolya_blika"])
    )


def sobrat_faily(vhod: str):
    if os.path.isdir(vhod):
        return [os.path.join(vhod, f) for f in sorted(os.listdir(vhod))
                if f.lower().endswith((".png", ".jpg", ".jpeg"))]
    return [vhod]


def main() -> None:
    ap = argparse.ArgumentParser(
        description="Перекраска PNG-спрайтов готового UI-кита под палитру Mars Colony "
                    "с сохранением слоёв объёма (блик/тень, ход по V).")
    ap.add_argument("vhod", help="файл или папка с PNG-спрайтами")

    # общие флаги вынесены в родителя и подключены к обоим режимам, а не к
    # верхнему парсеру: субпарсер argparse забирает все токены после имени
    # режима себе, поэтому --out/--kant и т.п. должны быть известны именно
    # subparser'у, чтобы работать в порядке "vhod режим --флаги", как в ТЗ.
    obshchie = argparse.ArgumentParser(add_help=False)
    obshchie.add_argument("--out", required=True, help="папка для результата")
    obshchie.add_argument("--sohranit-bliki", action=argparse.BooleanOptionalAction, default=True,
                           help="не трогать почти белые блики (S<10%%, V>92%%), включено по умолчанию")
    obshchie.add_argument("--v-gain", type=float, default=1.0, help="множитель V (общее затемнение/осветление)")
    obshchie.add_argument("--v-shift", type=float, default=0.0, help="сдвиг V в единицах 0..255")
    obshchie.add_argument("--v-gradient", dest="v_gradient", default=None, metavar="VERH:NIZ",
                           help="линейный множитель V по вертикали внутри непрозрачной части "
                                "спрайта: верхняя непрозрачная строка *VERH, нижняя *NIZ, "
                                "между ними линейно, напр. 1.0:0.62")
    obshchie.add_argument("--kant", default=None, help="HEX внешнего канта (расширение альфы)")
    obshchie.add_argument("--kant-px", type=int, default=3, help="толщина канта в пикселях")

    sub = ap.add_subparsers(dest="rezhim", required=True)

    p_tint = sub.add_parser("tint", parents=[obshchie], help="весь спрайт в один целевой цвет")
    p_tint.add_argument("--hex", required=True, help="целевой цвет, H и S берутся из него")

    p_karta = sub.add_parser("karta", parents=[obshchie], help="перекраска по карте соответствия hue:hue")
    p_karta.add_argument("--karta", required=True, help='пары "hue_источника:hue_цели" через запятую, градусы 0-360')
    p_karta.add_argument("--dopusk-hue", type=float, default=25.0, dest="dopusk_hue",
                          help="допуск сравнения по кругу, градусов (по умолчанию 25)")

    args = ap.parse_args()

    faily = sobrat_faily(args.vhod)
    if not faily:
        print("нет PNG/JPG во входе:", args.vhod)
        return
    os.makedirs(args.out, exist_ok=True)
    for put in faily:
        vyhod = os.path.join(args.out, os.path.splitext(os.path.basename(put))[0] + ".png")
        obrabotat_fail(put, vyhod, args.rezhim, args)


if __name__ == "__main__":
    main()
