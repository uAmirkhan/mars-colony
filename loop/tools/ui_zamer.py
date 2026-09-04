#!/usr/bin/env python3
"""Замер интерфейса по кадру: четыре механизма сочности числами.

Пороги взяты из wiki/saas/projects/mars-colony/ux-interfeys-referensy-2026-09-03.md,
раздел 2.1 — они замерены по 15 кадрам Township, а не назначены на глаз.

    python ui_zamer.py кадр.png --ui 0,0,1600,110
    python ui_zamer.py кадр.png --ui 0,0,1600,110 --mir 400,300,800,400
    python ui_zamer.py кадр.png --element 120,20,180,70     # одна кнопка/плашка

Область задаётся как x,y,w,h в пикселях кадра. Без --mir мир берётся как
центральная треть кадра ниже полосы интерфейса.

Режим маски (рекомендуется). Прямоугольник рамки почти всегда содержит и
интерфейс, и мир — если мира больше, доминирующий тон гистограммы окажется
тоном мира, чем ни крась сами элементы. Судья поймал это на строке UI-8:
рамка правого якоря `1059,10,530,90` содержала пустой грунт между плашкой
кредитов и кнопкой меню, и тон рамки остался тоном мира (10°), хотя сами
элементы — 210°.

    python ui_zamer.py кадр.png --ui 1059,10,530,90 --fon fon-bez-ui.png

`--fon` — тот же кадр, снятый с выключенным холстом интерфейса. Маска
интерфейса — пиксели, где кадр и фон отличаются больше чем на MASK_POROG
(сумма |Δ| по трём каналам). Все четыре метрики UI считаются только по
пикселям маски внутри рамки. Мир берётся из фона в той же рамке (или в
`--mir`, если она задана отдельно) — там интерфейса нет по построению,
фильтровать нечего.

Без `--fon` замер остаётся прямоугольным, как раньше, но в вывод добавлена
явная пометка режима и доля пикселей рамки с S>25 — это и есть количество
«голосов» в гистограмме тона, чтобы было видно, насколько ему можно верить.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# Пороги приёмки. Значение — (минимум, как называется, у Township)
POROGI = {
    "hue_dist": (100.0, "дистанция по тону от мира, градусов", "111-128"),
    "dark_share": (3.0, "доля пикселей темнее V40 внутри UI, %", "тёмный якорь есть всегда"),
    "sat_median": (65.0, "медиана насыщенности интерактива, %", "74-100"),
    "v_range": (27.0, "ход по светлоте внутри поверхности, единиц V", "60-75"),
}

# Порог наличия интерфейса в пикселе: сумма |Δ| по каналам между кадром и
# фоном без UI. Ниже — считается тем же миром (сжатие, дизеринг, лёгкий шум
# сцены между кадрами), не интерфейсом.
MASK_POROG = 24.0

# Доля рамки, занятая маской, ниже которой считаем, что интерфейса в рамке
# нет вовсе (случайные единичные пиксели шума, а не элемент UI).
MASK_MIN_SHARE = 1.0


def hsv(arr: np.ndarray) -> tuple:
    """RGB uint8 (N,3) -> H в градусах, S и V в процентах."""
    a = arr.astype(np.float32) / 255.0
    mx = a.max(axis=1)
    mn = a.min(axis=1)
    d = mx - mn
    h = np.zeros_like(mx)
    nz = d > 1e-6
    r, g, b = a[:, 0], a[:, 1], a[:, 2]
    idx = nz & (mx == r)
    h[idx] = (60 * ((g[idx] - b[idx]) / d[idx])) % 360
    idx = nz & (mx == g)
    h[idx] = 60 * ((b[idx] - r[idx]) / d[idx]) + 120
    idx = nz & (mx == b)
    h[idx] = 60 * ((r[idx] - g[idx]) / d[idx]) + 240
    s = np.where(mx > 1e-6, d / mx, 0.0) * 100
    return h, s, mx * 100


def parse_box(box: str) -> tuple:
    x, y, w, h = [int(v) for v in box.split(",")]
    return x, y, w, h


def crop_2d(img: Image.Image, box: str) -> np.ndarray:
    """Вырезка рамки как HxWx3 uint8 — сохраняет форму для построения маски."""
    x, y, w, h = parse_box(box)
    return np.asarray(img.crop((x, y, x + w, y + h)).convert("RGB"))


def crop(img: Image.Image, box: str) -> np.ndarray:
    return crop_2d(img, box).reshape(-1, 3)


def interface_mask(target_2d: np.ndarray, fon_2d: np.ndarray) -> np.ndarray:
    """Маска интерфейса: HxW bool, там где кадр отличается от фона без UI."""
    diff = np.abs(target_2d.astype(np.int16) - fon_2d.astype(np.int16)).sum(axis=2)
    return diff > MASK_POROG


def dominant_hue(h: np.ndarray, s: np.ndarray) -> float:
    """Тон по насыщенным пикселям: бледные не несут цвета и голос не имеют."""
    sel = h[s > 25]
    if sel.size == 0:
        return float("nan")
    hist, edges = np.histogram(sel, bins=36, range=(0, 360))
    i = int(hist.argmax())
    return float((edges[i] + edges[i + 1]) / 2)


def hue_gap(a: float, b: float) -> float:
    d = abs(a - b) % 360
    return min(d, 360 - d)


def vertical_v_range(img: Image.Image, box: str, fon_img: Image.Image | None = None) -> float:
    """Ход по светлоте сверху вниз внутри поверхности — объём светом, а не линией.

    Считается по средней светлоте строк, крайние 15% отбрасываются: там кант
    и контакт, они дают ложный перепад при плоской заливке.

    С `--fon` в среднее каждой строки берутся только пиксели маски интерфейса
    — иначе строка, где элемент занимает половину ширины, а другую половину
    мир, размывает свою же светлоту миром.
    """
    a = crop_2d(img, box).astype(np.float32)
    h, w, _ = a.shape
    if fon_img is not None:
        fon_a = crop_2d(fon_img, box).astype(np.float32)
        mask = interface_mask(a, fon_a)
    else:
        mask = np.ones((h, w), dtype=bool)
    v = a.max(axis=2) / 255.0 * 100
    rows = []
    for i in range(h):
        row_mask = mask[i]
        if row_mask.any():
            rows.append(float(v[i][row_mask].mean()))
    if not rows:
        return float("nan")
    rows_arr = np.array(rows)
    cut = max(1, int(len(rows_arr) * 0.15))
    core = rows_arr[cut:-cut] if len(rows_arr) > 2 * cut else rows_arr
    return float(core.max() - core.min())


def main() -> None:
    ap = argparse.ArgumentParser(description="Замер интерфейса по кадру")
    ap.add_argument("frame")
    ap.add_argument("--ui", required=True, help="область интерфейса x,y,w,h")
    ap.add_argument("--mir", help="область мира x,y,w,h (по умолчанию центр кадра / та же рамка с --fon)")
    ap.add_argument("--element", help="одна кнопка или плашка x,y,w,h — для хода по светлоте")
    ap.add_argument("--fon", help="тот же кадр с выключенным холстом интерфейса — включает замер по маске")
    ap.add_argument("--json", action="store_true", help="выдать json вместо таблицы")
    args = ap.parse_args()

    path = Path(args.frame)
    if not path.exists():
        print("нет файла: {}".format(path))
        sys.exit(2)
    img = Image.open(path)
    W, H = img.size

    fon_img = None
    mode = "прямоугольник"
    if args.fon:
        fon_path = Path(args.fon)
        if not fon_path.exists():
            print("нет файла фона: {}".format(fon_path))
            sys.exit(2)
        fon_img = Image.open(fon_path)
        if fon_img.size != (W, H):
            print("фон {}x{} не совпадает по размеру с кадром {}x{}".format(
                fon_img.size[0], fon_img.size[1], W, H))
            sys.exit(2)
        mode = "маска"

    ui_2d = crop_2d(img, args.ui)
    mask_share = None
    voice_share = None

    if fon_img is not None:
        fon_ui_2d = crop_2d(fon_img, args.ui)
        mask = interface_mask(ui_2d, fon_ui_2d)
        mask_share = float(mask.mean() * 100)
        ui_pixels = ui_2d[mask]

        mir_box = args.mir or args.ui
        mir_pixels = crop(fon_img, mir_box)
    else:
        ui_pixels = ui_2d.reshape(-1, 3)
        uh_all, us_all, _ = hsv(ui_pixels)
        voice_share = float((us_all > 25).mean() * 100)

        mir_box = args.mir or "{},{},{},{}".format(W // 3, H // 2, W // 3, H // 4)
        mir_pixels = crop(img, mir_box)

    empty_ui = fon_img is not None and (mask_share is None or mask_share < MASK_MIN_SHARE)

    mh, ms, mv = hsv(mir_pixels)
    hue_mir = dominant_hue(mh, ms)
    sat_mir = float(np.median(ms[ms > 10])) if (ms > 10).any() else 0.0

    res = {
        "frame": str(path),
        "size": [W, H],
        "ui_box": args.ui,
        "mir_box": mir_box,
        "mode": mode,
        "fon": str(args.fon) if args.fon else None,
        "mask_share": mask_share,
        "voice_share_s25": voice_share,
        "empty_ui": empty_ui,
        "hue_mir": hue_mir,
        "sat_mir": sat_mir,
    }

    if empty_ui:
        res.update({
            "hue_ui": None,
            "dark_share": None,
            "sat_median": None,
            "hue_dist": None,
            "v_range": None,
        })
    else:
        uh, us, uv = hsv(ui_pixels)
        res["hue_ui"] = dominant_hue(uh, us)
        res["dark_share"] = float((uv < 40).mean() * 100)
        res["sat_median"] = float(np.median(us[us > 10])) if (us > 10).any() else 0.0
        res["hue_dist"] = hue_gap(res["hue_ui"], hue_mir)
        res["v_range"] = (
            vertical_v_range(img, args.element, fon_img) if args.element else None
        )

    if args.json:
        print(json.dumps(res, ensure_ascii=False, indent=2))
        sys.exit(3 if empty_ui else 0)

    print("Кадр {}  {}x{}  рамка {}  режим {}".format(path.name, W, H, args.ui, mode))
    print("UI {}   мир {}".format(args.ui, mir_box))

    if fon_img is not None:
        print("фон {}   маска рамки: {:.1f}% пикселей — интерфейс".format(args.fon, mask_share))
    else:
        print("ПРЕДУПРЕЖДЕНИЕ: замер по прямоугольнику, тон может принадлежать миру")
        print("голосов в гистограмме (доля рамки с S>25): {:.1f}%".format(voice_share))

    if empty_ui:
        print("-" * 62)
        print("интерфейса в рамке нет: маска занимает {:.2f}% рамки (порог {:.0f}%)".format(
            mask_share if mask_share is not None else 0.0, MASK_MIN_SHARE))
        print("тон мира в рамке {:.0f}°, насыщенность мира {:.0f}%".format(hue_mir, sat_mir))
        sys.exit(3)

    print("-" * 62)
    fail = 0
    for key, (floor, label, tsh) in POROGI.items():
        val = res.get(key)
        if val is None:
            print("{:<44} {:>8}   (нужен --element)".format(label, "—"))
            continue
        good = val >= floor
        fail += 0 if good else 1
        print("{:<44} {:>8.1f}   порог {:>5.0f}  {}  Township {}".format(
            label, val, floor, "OK" if good else "НЕТ", tsh))
    print("-" * 62)
    print("тон UI {:.0f}°, тон мира {:.0f}°, насыщенность мира {:.0f}%".format(
        res["hue_ui"], res["hue_mir"], res["sat_mir"]))
    print("провалено порогов: {}".format(fail))
    sys.exit(1 if fail else 0)


if __name__ == "__main__":
    main()
