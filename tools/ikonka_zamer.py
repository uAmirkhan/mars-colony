#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Приёмка иконок ресурсов Mars Colony числами, а не словами «тусклые».

Заказчик генерирует иконки сам и уже дважды прислал провальную партию,
которую поймали только на глаз. Этот замер должен ловить брак ДО того,
как заказчик потратит остальные генерации.

Пороги — из wiki/saas/projects/mars-colony/ux-interfeys-referensy-2026-09-03.md,
раздел 5 «НАБОР ИКОНОК РЕСУРСОВ», пункт «Приёмочные требования»:

    1. Блик — порог доли снят с эталонов Township в
       mars-colony/loop/promty/etalon/ (--json прогон сохранён в
       mars-colony/loop/promty/etalon-zamer.json). На честных (фон-ровных)
       эталонах минимум был у сахара-плитки, дальше вниз только у брака
       (0.00%). Порог 3.0 даёт запас между 0.00% брака и живыми эталонами.
    2. Медиана насыщенности тела: S 58-90%, цель 65-85 (раздел 5, п.1) —
       это порог для СОЧНЫХ товаров (все 17 ресурсов Mars Colony по их
       доминантам из раздела 5 — сочные, ни один не задуман бледным).
       У самого Township в одной семье значков есть товары, чей настоящий
       цвет бледный по природе (молоко, яйцо, сахар, йогурт, сметана,
       масло) — паспорт Township прямо требует «S предмета не ниже
       насыщенности его настоящего цвета» (арт-директор, замер эталонов
       2026-09-04), поэтому для них порог ниже. Это калибровочная величина
       по факту (см. KLASS_TOVAROV) — Mars Colony пока не планирует ни
       одного бледного ресурса, поэтому диапазон для сочных остаётся
       единственным действующим порогом приёмки самой игры.
    3. Ход по светлоте: Vmin 13-43%, Vmax практически 100% (раздел 5, п.2).
       Vmax «ровно 100» на реальном PNG недостижимо пиксель-в-пиксель,
       поэтому проверяется тем же порогом V97, что и блик.
    4. Доля площади объекта в кадре. В вики есть родственная, но другая
       метрика — «предмет занимает 84-88% кадра по большей стороне»
       (раздел 5, вступление) — это габарит по bbox, не площадь. Площадь
       ловит другой брак: тонкий вытянутый предмет может занимать почти
       весь габарит по стороне и почти ничего по площади, а именно он
       «превращается в кашу» в ячейке склада 96 px. Числового порога по
       площади в вики нет, порог остаётся параметром со значением 55 —
       калибровка по эталонам Township не подтверждает его: у арт-
       директора вырезки взяты с запасом или с захватом соседей, площадь
       по ним не показательна (см. отчёт по калибровке 2026-09-04).
       Порог ждёт первой удачной генерации Mars Colony.
    5. Разброс медиан насыщенности внутри набора (только для папки).
       У забракованной партии разброс 19.5%..73.2% — «набор не семья».
       Считается ТОЛЬКО внутри одного класса сочности (см. п.2) — иначе
       честная смесь сочных и бледных товаров (как у самого Township,
       68.6 п.п.) ложно бракуется как «не семья». Порог разброса внутри
       класса остаётся прежним, 20 п.п., с запасом от разброса брака.
    6. Читаемость при уменьшении до 96х96 — своя метрика (в вики нет):
       ужимаем PNG до размера ячейки склада (96 px, см. раздел 4) и
       сравниваем долю площади объекта после уменьшения с долей до него.
       LANCZOS-ресайз стирает субпиксельные линии раньше, чем крупные
       заливки, поэтому «утекшая» площадь — это утекшая тонкая деталь.
       Порог — не более 10% относительной просадки. Метрика имеет смысл
       только когда исходник УЖИМАЕТСЯ, а не растягивается: на вырезках
       эталонов Township (85-105 px по стороне) thumbnail до 96 px почти
       не меняет размер, и метрика мерит интерполяционный шум, а не
       потерю детали (проверено: у одного и того же эталона на исходном
       размере читаемость 78%, а после ресемплинга бикубиком до 1024 —
       95% при той же геометрии, см. отчёт по калибровке). Поэтому метрика
       считается только для источников от 256 px по меньшей стороне,
       иначе выводится «н/д».

Определение объекта — по фону: фон ровный, берётся средним по четырём
углам, порог отличия такой же, как в mars-colony/tools/ikonka_obvodka.py
(PORog_FONA = 26), чтобы объект не определялся дважды двумя способами.

Этот способ (среднее по 4 угловым пикселям) держится на предположении
самой вики (раздел 5, п.9): «Фон ровный, разброс меньше 5 единиц
яркости». Калибровка на эталонах Township показала, что это предположение
там почти всегда нарушено — это скриншоты из живой игры с полом, небом
или бейджами интерфейса в кадре, а не чистые вырезки предмета на пустом
фоне (см. mars-colony/loop/promty/etalon-zamer.json, поле fon_razbros_v).
Проверка по одной яркости (V, максимум канала) пропускает два случая.
Первый: предмет вплотную касается одного из четырёх углов — угол
становится цветным, но его V может случайно совпасть с остальными белыми
углами (белый и оранжевый блик дают одинаковый максимум канала при разном
цвете) — так было поймано на znachok-burger.png. Второй: клетчатый или
полосатый фон живого скриншота может случайно дать 4 похожих угла при
совсем другом рисунке между ними — так было поймано на znachok-maslo.png
(4 угла почти совпадали, а середины сторон — нет). Поэтому фактическую
пригодность маски замер проверяет по цвету целиком и по восьми опорным
точкам (4 угла + 4 середины сторон, см. opornye_tochki_fona): наибольшая
попарная разница тем же способом, что и «отличие от фона» у самого
объекта (сумма модулей по каналам), с отдельным порогом ROVNOST_FONA_CVET,
откалиброванным по разрыву в самом наборе эталонов между честно-ровным
фоном (макс. 70 по восьми точкам) и первым случаем со скрытой клеткой фона
(от 110). Поле fon_razbros_v в выводе остаётся — это буквальная проверка
по тексту вики (яркость), она показывается для прозрачности, но решение
«маске можно верить» принимает более строгая цветовая проверка. Для
честной генерации Mars Colony с «even empty backdrop» (см. промпт-хвост,
раздел 6) и предметом, не касающимся краёв кадра (84-88% по стороне, поля
равные), обе проверки проходят — это проверка входа, а не дополнительное
требование к игре.

Запуск:
    python ikonka_zamer.py znachok-vodorosli.png
    python ikonka_zamer.py papka-s-ikonkami/
    python ikonka_zamer.py papka-s-ikonkami/ --json
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# --- то же определение фона, что и в ikonka_obvodka.py -----------------
PORog_FONA = 26  # насколько пиксель должен отличаться от угла, чтобы считаться объектом

# --- пороги из вики (раздел 5) ------------------------------------------
SAT_MIN, SAT_MAX = 58.0, 90.0          # медиана насыщенности тела, товар «сочный»
SAT_CEL_MIN, SAT_CEL_MAX = 65.0, 85.0  # цель внутри диапазона, не жёсткий порог
VMIN_LO, VMIN_HI = 13.0, 43.0          # ход по светлоте, нижняя граница Vmin
V_YARKIY = 97.0                        # «практически V100»: и блик, и проверка Vmax

# --- проверка входа: «фон ровный» из раздела 5, п.9 ---------------------
# Разброс V (0-100) по четырём угловым пикселям. Число 5 — то же самое
# «разброс меньше 5 единиц яркости», что и в тексте вики, а не отдельная
# калибровочная величина. Показывается в выводе, но само по себе решения
# не принимает (см. докстринг про znachok-burger.png).
ROVNOST_FONA_V = 5.0

# Попарная разница опорных точек фона по цвету целиком (сумма модулей по
# каналам, та же формула, что и «отличие от фона» у объекта). Опорные
# точки — не только 4 угла: клетчатый фон вроде фонового «пола» из
# скриншотов Township может случайно дать 4 одинаковых угла, а между ними
# — совсем другой рисунок (так было поймано на znachok-maslo.png, где
# углы совпали, а середины сторон — нет). Порог снят с самого набора
# эталонов: у честно-ровного фона максимум по 8 точкам 70, у первого
# случая со скрытой клеткой фона — уже 110 (см. отчёт по калибровке
# 2026-09-04). 90 — с запасом между ними, ближе к середине разрыва.
ROVNOST_FONA_CVET = 90.0

# --- насыщенность бледных по природе товаров -----------------------------
# У Mars Colony таких товаров в текущих 17 нет (см. раздел 5, все доминанты
# сочные), диапазон нужен только для приёмки чужого эталонного набора,
# где бледные товары есть на самом деле (молоко, яйцо, сахар, йогурт,
# сметана, масло). Границы — по факту с запасом: нижняя ниже самого
# бледного честного замера (молоко, ~7.5%), верхняя выше самого сочного
# из бледной группы (сметана, ~53%). Источник чисел —
# mars-colony/loop/promty/etalon-zamer.json.
SAT_BLEDNYY_MIN, SAT_BLEDNYY_MAX = 5.0, 55.0

# Классификация эталонов Township по природной сочности цвета (см. паспорт
# Township, «S предмета не ниже насыщенности его настоящего цвета»).
# Ключ — основа имени файла (без "znachok-" и расширения). Товар не из
# списка по умолчанию считается сочным (это и есть режим Mars Colony:
# все 17 ресурсов сочные по своим доминантам). Три товара из набора —
# инструмент, шерсть, стекло — не относятся ни к одному ресурсу Mars
# Colony (там нет строительных материалов такого рода), их однозначная
# сочность/бледность не определена вики и не проверяется по S вообще.
KLASS_TOVAROV: dict[str, str] = {
    "moloko": "blednyy",
    "yaytso": "blednyy",
    "sakhar": "blednyy",
    "sakhar-malaya": "blednyy",
    "yogurt": "blednyy",
    "smetana": "blednyy",
    "maslo": "blednyy",
    "morozhenoe": "blednyy",
    "molotok": "vne-domena",
    "sherst": "vne-domena",
    "steklo": "vne-domena",
}


def klass_tovara(put: Path) -> str:
    osnova = put.stem
    if osnova.startswith("znachok-"):
        osnova = osnova[len("znachok-"):]
    return KLASS_TOVAROV.get(osnova, "sochny")


# --- параметры без числа в вики: рабочие дефолты, калиброваны по эталонам ---
BLIK_PO_UMOLCHANIYU = 3.0        # % пикселей объекта ярче V97; снято с честных эталонов Township
PLOSHCHAD_PO_UMOLCHANIYU = 55.0  # % площади кадра; эталоны Township площадь не подтверждают (см. докстринг)
RAZBROS_PO_UMOLCHANIYU = 20.0    # верхний порог разброса медиан S ВНУТРИ ОДНОГО КЛАССА, п.п.
CHITAEMOST_PO_UMOLCHANIYU = 90.0  # % сохранённой площади после ужатия до 96x96 от исходной

CHITAEMOST_STORONA = 96  # px — ячейка склада, см. ux-interfeys-referensy-2026-09-03.md, раздел 4
CHITAEMOST_MIN_ISTOCHNIK = 256  # px — ниже этого читаемость до 96 не ужатие, а растяжение, метрика «н/д»


def rgb_to_hsv(arr: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """RGB uint8 (N,3) -> S и V в процентах. H не нужен для приёмки иконок."""
    a = arr.astype(np.float32) / 255.0
    mx = a.max(axis=1)
    mn = a.min(axis=1)
    d = mx - mn
    with np.errstate(divide="ignore", invalid="ignore"):
        s = np.where(mx > 1e-6, d / mx, 0.0) * 100
    v = mx * 100
    return s, v


def uglovye_pikseli(arr: np.ndarray) -> np.ndarray:
    """4 угловых пикселя — опора для среднего фона в maska_obekta (см. ikonka_obvodka.py)."""
    h, w, _ = arr.shape
    return np.array([arr[0, 0], arr[0, w - 1], arr[h - 1, 0], arr[h - 1, w - 1]], dtype=np.float32)


def opornye_tochki_fona(arr: np.ndarray) -> np.ndarray:
    """4 угла + 4 середины сторон — опора для проверки «фон ровный».

    Только углов мало: клетчатый или полосатый фон может случайно дать
    4 похожих угла при совсем другом рисунке между ними (см. докстринг
    про ROVNOST_FONA_CVET). Для самого определения маски (maska_obekta)
    середины сторон не нужны — там достаточно грубой оценки среднего.
    """
    h, w, _ = arr.shape
    tochki = [
        arr[0, 0], arr[0, w - 1], arr[h - 1, 0], arr[h - 1, w - 1],
        arr[0, w // 2], arr[h - 1, w // 2], arr[h // 2, 0], arr[h // 2, w - 1],
    ]
    return np.array(tochki, dtype=np.float32)


def fon_razbros_v(arr: np.ndarray) -> float:
    """Разброс V по опорным точкам фона — буквальная проверка из раздела 5, п.9."""
    tochki = opornye_tochki_fona(arr)
    v_tochek = tochki.max(axis=1) / 255.0 * 100
    return float(v_tochek.max() - v_tochek.min())


def fon_razbros_cvet(arr: np.ndarray) -> float:
    """Наибольшая попарная разница опорных точек фона по цвету целиком.

    Ловит случай, где предмет касается угла кадра (яркость угла может
    совпасть с остальными при другом цвете), и случай клетчатого фона
    (углы могут совпасть между собой при другом рисунке между ними).
    """
    tochki = opornye_tochki_fona(arr)
    n = len(tochki)
    maks = 0.0
    for i in range(n):
        for j in range(i + 1, n):
            maks = max(maks, float(np.abs(tochki[i] - tochki[j]).sum()))
    return maks


def maska_obekta(im: Image.Image) -> np.ndarray:
    """Фон ровный, берётся по четырём углам — тот же приём, что в ikonka_obvodka.py."""
    rgb = im.convert("RGB")
    arr = np.asarray(rgb)
    fon = uglovye_pikseli(arr).mean(axis=0)
    otlichie = np.abs(arr.astype(np.float32) - fon).sum(axis=2)
    maska = (otlichie > PORog_FONA).astype(np.uint8) * 255
    # сгладить край медианным фильтром — как в ikonka_obvodka.py, иначе зубцы дают ложные пиксели
    maska_img = Image.fromarray(maska, mode="L").filter(ImageFilter.MedianFilter(3))
    return np.asarray(maska_img) > 127


def chitaemost_pri_umenshenii(im: Image.Image, maska: np.ndarray, storona: int) -> float:
    """Доля площади объекта после ужатия до storona x storona относительно доли до ужатия.

    Вырезаем объект по маске в RGBA (фон прозрачный), вписываем в квадрат storona
    с сохранением пропорций (LANCZOS — тот же ресемплинг, каким Pixi ужимает арт
    для инвентаря), считаем долю непрозрачных пикселей в новом квадрате и делим
    на долю объекта в исходном кадре. Субпиксельные линии LANCZOS размывает и
    гасит раньше, чем крупные заливки, поэтому просевшее отношение — это
    исчезнувшая тонкая деталь, а не общая усадка картинки. Вызывающий код обязан
    сам проверить CHITAEMOST_MIN_ISTOCHNIK — на маленьком исходнике thumbnail
    растягивает, а не ужимает, и число перестаёт что-либо мерить.
    """
    rgb = im.convert("RGB")
    alpha = Image.fromarray((maska.astype(np.uint8) * 255), mode="L")
    obekt = rgb.convert("RGBA")
    obekt.putalpha(alpha)
    obekt.thumbnail((storona, storona), Image.LANCZOS)
    kanvas = Image.new("RGBA", (storona, storona), (0, 0, 0, 0))
    x = (storona - obekt.width) // 2
    y = (storona - obekt.height) // 2
    kanvas.paste(obekt, (x, y), obekt)
    novaya_alpha = np.asarray(kanvas.getchannel("A"))
    dolya_posle = float((novaya_alpha > 127).mean() * 100)
    dolya_do = float(maska.mean() * 100)
    if dolya_do <= 0:
        return 0.0
    return dolya_posle / dolya_do * 100


def zamerit_ikonku(put: Path) -> dict:
    im = Image.open(put)
    w, h = im.size
    arr = np.asarray(im.convert("RGB"))
    razbros_fona = fon_razbros_v(arr)
    razbros_cveta = fon_razbros_cvet(arr)
    fon_priboden = razbros_cveta <= ROVNOST_FONA_CVET

    maska = maska_obekta(im)
    pikseli = arr[maska]

    if pikseli.size == 0:
        return {
            "file": str(put),
            "size": [w, h],
            "oshibka": "объект не найден (фон занял весь кадр)",
        }

    s, v = rgb_to_hsv(pikseli)

    if min(w, h) >= CHITAEMOST_MIN_ISTOCHNIK:
        chitaemost = chitaemost_pri_umenshenii(im, maska, CHITAEMOST_STORONA)
    else:
        chitaemost = None  # н/д — исходник меньше ячейки склада, ужатия не происходит

    return {
        "file": str(put),
        "size": [w, h],
        "klass": klass_tovara(put),
        "fon_razbros_v": razbros_fona,
        "fon_razbros_cvet": razbros_cveta,
        "fon_priboden": fon_priboden,
        "sat_median": float(np.median(s)),
        "v_min": float(v.min()),
        "v_max": float(v.max()),
        "blik_share": float((v >= V_YARKIY).mean() * 100),
        "ploshchad_share": float(maska.mean() * 100),
        "chitaemost_96": chitaemost,
    }


def stroka_porogov(res: dict, args: argparse.Namespace) -> tuple[list[tuple], int]:
    """Строит список (метрика, значение, порог, OK, township) + число провалов.

    Если фон в кадре не ровный (см. fon_priboden), маска ненадёжна: метрики
    тела показываются, но не считаются ни провалом, ни зачётом — «н/д».
    """
    stroki = []
    fail = 0
    fon_priboden = res.get("fon_priboden", True)

    def dobavit(label, val, ok, porog_str, township, na=False):
        nonlocal fail
        status = "н/д" if na else ("OK" if ok else "НЕТ")
        stroki.append((label, val, porog_str, status, township))
        if not na and not ok:
            fail += 1

    dobavit(
        "блик, % пикселей ярче V97 внутри объекта",
        res["blik_share"],
        res["blik_share"] >= args.blik_porog,
        f">= {args.blik_porog:.2f}",
        "есть жёсткий блик V100 (промпт-хвост, п.3 раздела 5)",
        na=not fon_priboden,
    )
    if res.get("klass") == "blednyy":
        sat_lo, sat_hi = SAT_BLEDNYY_MIN, SAT_BLEDNYY_MAX
        sat_township = f"бледный по природе, {SAT_BLEDNYY_MIN:.0f}-{SAT_BLEDNYY_MAX:.0f}"
    else:
        sat_lo, sat_hi = SAT_MIN, SAT_MAX
        sat_township = f"58-90, цель {SAT_CEL_MIN:.0f}-{SAT_CEL_MAX:.0f}"
    dobavit(
        "медиана насыщенности тела, %",
        res["sat_median"],
        sat_lo <= res["sat_median"] <= sat_hi,
        f"{sat_lo:.0f}-{sat_hi:.0f}",
        sat_township,
        na=not fon_priboden or res.get("klass") == "vne-domena",
    )
    dobavit(
        "Vmin, ход по светлоте снизу",
        res["v_min"],
        VMIN_LO <= res["v_min"] <= VMIN_HI,
        f"{VMIN_LO:.0f}-{VMIN_HI:.0f}",
        "13-43",
        na=not fon_priboden,
    )
    dobavit(
        "Vmax, ход по светлоте сверху",
        res["v_max"],
        res["v_max"] >= V_YARKIY,
        f">= {V_YARKIY:.0f}",
        "ровно 100",
        na=not fon_priboden,
    )
    dobavit(
        "доля площади объекта в кадре, %",
        res["ploshchad_share"],
        res["ploshchad_share"] >= args.ploshchad_porog,
        f">= {args.ploshchad_porog:.0f}",
        "предмет крупный, не теряется в колодце",
        na=not fon_priboden,
    )
    if res["chitaemost_96"] is None:
        stroki.append((
            f"читаемость после ужатия до {CHITAEMOST_STORONA}px, % от исходной площади",
            float("nan"),
            f">= {args.chitaemost_porog:.0f}",
            "н/д",
            f"источник меньше {CHITAEMOST_MIN_ISTOCHNIK}px, метрика не применима",
        ))
    else:
        dobavit(
            f"читаемость после ужатия до {CHITAEMOST_STORONA}px, % от исходной площади",
            res["chitaemost_96"],
            res["chitaemost_96"] >= args.chitaemost_porog,
            f">= {args.chitaemost_porog:.0f}",
            "силуэт держится в ячейке склада",
        )
    return stroki, fail


def napechatat_tablitsu(res: dict, args: argparse.Namespace) -> int:
    print("Иконка {}  {}x{}".format(Path(res["file"]).name, *res["size"]))
    if "oshibka" in res:
        print("  ОШИБКА: {}".format(res["oshibka"]))
        return 1
    if not res.get("fon_priboden", True):
        print(
            "  ВНИМАНИЕ: фон в кадре не ровный (разброс цвета углов {:.0f} > {:.0f}, разброс яркости"
            " {:.1f}, раздел 5 п.9). Маска по углам ненадёжна, метрики тела ниже помечены «н/д».".format(
                res["fon_razbros_cvet"], ROVNOST_FONA_CVET, res["fon_razbros_v"]
            )
        )
    stroki, fail = stroka_porogov(res, args)
    print("-" * 92)
    print("{:<48} {:>8} {:>12} {:>5}  {}".format("метрика", "значение", "порог", "OK", "как у Township"))
    for label, val, porog, ok, township in stroki:
        znachenie = "н/д" if val != val else "{:.1f}".format(val)  # val != val -> nan
        print("{:<48} {:>8} {:>12} {:>5}  {}".format(label, znachenie, porog, ok, township))
    print("-" * 92)
    print("провалено порогов: {}".format(fail))
    return fail


def sobrat_faily(put: Path) -> list[Path]:
    if put.is_file():
        return [put]
    faily = sorted(
        p for p in put.iterdir()
        if p.suffix.lower() in (".png", ".jpg", ".jpeg") and "-obvedeno" not in p.stem
    )
    return faily


def razbros_po_klassam(rezultaty: list[dict], porog: float) -> dict:
    """Разброс медиан насыщенности внутри класса «сочный».

    Проверка «набор — одна семья» имеет смысл только для сочных товаров:
    их доминанты у Mars Colony все насыщенные, и тесная группа — это и
    есть ожидание (см. раздел 5, п.5 в докстринге). Бледные по природе
    товары семьи не образуют по определению — молоко и масло оба честно
    бледные, но насыщенность между ними расходится сильнее, чем 20 п.п.
    (см. отчёт по калибровке 2026-09-04), это не брак, а нормальный
    разброс бледных цветов, поэтому для класса «бледный» разброс не
    считается вовсе. Товар «вне домена» (см. KLASS_TOVAROV) тоже не
    участвует.
    """
    klassy: dict[str, list[float]] = {}
    for r in rezultaty:
        if "sat_median" not in r or not r.get("fon_priboden", True):
            continue
        klass = r.get("klass", "sochny")
        if klass != "sochny":
            continue
        klassy.setdefault(klass, []).append(r["sat_median"])

    itog = {}
    for klass, mediany in klassy.items():
        if len(mediany) < 2:
            itog[klass] = {"razbros": None, "ok": None, "n": len(mediany)}
            continue
        razbros = max(mediany) - min(mediany)
        itog[klass] = {
            "razbros": razbros,
            "min": min(mediany),
            "max": max(mediany),
            "n": len(mediany),
            "ok": razbros <= porog,
        }
    return itog


def main() -> None:
    ap = argparse.ArgumentParser(description="Приёмка иконок ресурсов Mars Colony")
    ap.add_argument("put", help="файл иконки или папка с иконками")
    ap.add_argument("--json", action="store_true", help="выдать json вместо таблицы")
    ap.add_argument(
        "--blik-porog", type=float, default=BLIK_PO_UMOLCHANIYU,
        help="минимальная доля пикселей ярче V97 внутри объекта, %% (дефолт снят с эталонов Township)",
    )
    ap.add_argument(
        "--ploshchad-porog", type=float, default=PLOSHCHAD_PO_UMOLCHANIYU,
        help="минимальная доля площади объекта в кадре, %%",
    )
    ap.add_argument(
        "--chitaemost-porog", type=float, default=CHITAEMOST_PO_UMOLCHANIYU,
        help="минимальная доля сохранённой площади после ужатия до 96x96, %% от исходной",
    )
    ap.add_argument(
        "--razbros-porog", type=float, default=RAZBROS_PO_UMOLCHANIYU,
        help="максимальный разброс медиан насыщенности внутри класса сочности, п.п.",
    )
    args = ap.parse_args()

    put = Path(args.put)
    if not put.exists():
        print("нет пути: {}".format(put))
        sys.exit(2)

    faily = sobrat_faily(put)
    if not faily:
        print("иконок не найдено: {}".format(put))
        sys.exit(2)

    rezultaty = [zamerit_ikonku(f) for f in faily]

    obshchiy_fail = 0

    if args.json:
        vyhod = {"ikonki": []}
        for res in rezultaty:
            if "oshibka" in res:
                vyhod["ikonki"].append(res)
                obshchiy_fail += 1
                continue
            stroki, fail = stroka_porogov(res, args)
            obshchiy_fail += fail
            vyhod["ikonki"].append({**res, "provaleno_porogov": fail})
        if len(rezultaty) > 1:
            po_klassam = razbros_po_klassam(rezultaty, args.razbros_porog)
            vyhod["nabor"] = {"razbros_porog": args.razbros_porog, "po_klassam": po_klassam}
            for svedeniya in po_klassam.values():
                if svedeniya["ok"] is False:
                    obshchiy_fail += 1
        print(json.dumps(vyhod, ensure_ascii=False, indent=2))
        sys.exit(1 if obshchiy_fail else 0)

    for res in rezultaty:
        obshchiy_fail += napechatat_tablitsu(res, args)
        print()

    if len(rezultaty) > 1:
        po_klassam = razbros_po_klassam(rezultaty, args.razbros_porog)
        print("Сводка по набору ({} иконок):".format(len(rezultaty)))
        for klass, svedeniya in po_klassam.items():
            if svedeniya["ok"] is None:
                print("  класс «{}»: меньше двух фон-пригодных иконок, разброс н/д".format(klass))
                continue
            ok = svedeniya["ok"]
            obshchiy_fail += 0 if ok else 1
            print(
                "  класс «{}»: разброс медиан насыщенности {:.1f} п.п. (мин {:.1f}, макс {:.1f})"
                "  порог <= {:.0f}  {}".format(
                    klass, svedeniya["razbros"], svedeniya["min"], svedeniya["max"],
                    args.razbros_porog, "OK" if ok else "НЕТ",
                )
            )
            if not ok:
                print("  набор не семья значит перегенерировать выбивающиеся иконки, не весь комплект")

    print("итого провалено порогов: {}".format(obshchiy_fail))
    sys.exit(1 if obshchiy_fail else 0)


if __name__ == "__main__":
    main()
