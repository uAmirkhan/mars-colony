"""Замер кадра. Один и тот же скрипт для чужих кадров и для наших.

Считает то, что отделяет «место» от «ассетов на сетке»:
  - доля неба;
  - доля мертвых клеток (ровные участки, где ничего не происходит);
  - доля кадра под деталью;
  - доли цветовых семейств отдельно по всему кадру и отдельно по пикселям детали
    (второе не зависит от того, сколько в кадре пустой земли);
  - цветность по Хаслеру-Сюсструнку, насыщенность, светлота;
  - крупность объектов.

Норма не зашита. Норма это то, что намеряно на чужих кадрах.

Запуск:
    python kadr.py <файл|папка> [...] [--json out.json] [--csv out.csv]
    python kadr.py --self-test
"""

import argparse
import glob
import json
import os
import sys

import numpy as np
from PIL import Image

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

W = 960          # рабочая ширина, все кадры приводятся к ней
GRID = (12, 8)   # сетка клеток для замера мертвых зон
SKY_MAX_ROW = 0.60  # ниже этой доли высоты неба не бывает

# Границы цветовых семейств по тону, в градусах. Ахроматика ловится отдельно
# по насыщенности, поэтому здесь только тон.
HUE_FAMILIES = [
    ("krasnyy", 345, 15),
    ("oranzhevyy", 15, 45),
    ("zheltyy", 45, 70),
    ("zelenyy", 70, 160),
    ("goluboy", 160, 200),
    ("siniy", 200, 260),
    ("fioletovyy", 260, 345),
]

SAT_AHROM = 0.18   # ниже этого пиксель считается ахроматичным
DARK_V = 0.18      # ниже этого пиксель считается темным


# ---------------------------------------------------------------- вспомогательное

def _box_blur(a, r):
    """Быстрое размытие боксом через интегральную сумму. Три прохода дают
    приближение гаусса."""
    if r < 1:
        return a
    for _ in range(3):
        pad = np.pad(a, ((r, r), (r, r)), mode="edge")
        cs = pad.cumsum(0).cumsum(1)
        cs = np.pad(cs, ((1, 0), (1, 0)), mode="constant")
        k = 2 * r + 1
        a = (cs[k:, k:] - cs[:-k, k:] - cs[k:, :-k] + cs[:-k, :-k]) / (k * k)
    return a


def _sobel(lum):
    gx = np.zeros_like(lum)
    gy = np.zeros_like(lum)
    gx[:, 1:-1] = lum[:, 2:] - lum[:, :-2]
    gy[1:-1, :] = lum[2:, :] - lum[:-2, :]
    return np.sqrt(gx * gx + gy * gy)


def _rgb_to_hsv(rgb):
    """rgb в [0,1], форма (h,w,3). Возвращает h в градусах, s и v в [0,1]."""
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(-1)
    mn = rgb.min(-1)
    d = mx - mn
    h = np.zeros_like(mx)
    nz = d > 1e-9
    # красный максимум
    m = nz & (mx == r)
    h[m] = ((g - b)[m] / d[m]) % 6
    m = nz & (mx == g)
    h[m] = ((b - r)[m] / d[m]) + 2
    m = nz & (mx == b)
    h[m] = ((r - g)[m] / d[m]) + 4
    h = h * 60.0
    s = np.where(mx > 1e-9, d / np.maximum(mx, 1e-9), 0.0)
    return h, s, mx


def _label_from_top(mask):
    """Метит компоненты маски, связанные с верхним краем. Обход в ширину по
    прореженной сетке, чтобы не тормозить на полном разрешении."""
    step = 4
    small = mask[::step, ::step]
    h, w = small.shape
    seen = np.zeros((h, w), bool)
    stack = [(0, x) for x in range(w) if small[0, x]]
    for y, x in stack:
        seen[y, x] = True
    while stack:
        y, x = stack.pop()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and small[ny, nx] and not seen[ny, nx]:
                seen[ny, nx] = True
                stack.append((ny, nx))
    out = np.kron(seen, np.ones((step, step), bool))
    return out[: mask.shape[0], : mask.shape[1]]


def _components(mask, min_px=40):
    """Размеры компонент маски. Обход в ширину по прореженной сетке.
    Возвращает список высот компонент в пикселях полного кадра."""
    step = 4
    small = mask[::step, ::step]
    h, w = small.shape
    seen = np.zeros((h, w), bool)
    heights = []
    for sy in range(h):
        for sx in range(w):
            if not small[sy, sx] or seen[sy, sx]:
                continue
            stack = [(sy, sx)]
            seen[sy, sx] = True
            ys = []
            n = 0
            while stack:
                y, x = stack.pop()
                ys.append(y)
                n += 1
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < h and 0 <= nx < w and small[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        stack.append((ny, nx))
            if n * step * step >= min_px:
                heights.append((max(ys) - min(ys) + 1) * step)
    return heights


# ---------------------------------------------------------------- замер

def measure(path_or_img, label=None, crop=None):
    if isinstance(path_or_img, Image.Image):
        im = path_or_img.convert("RGB")
        name = label or "<image>"
    else:
        im = Image.open(path_or_img).convert("RGB")
        name = label or os.path.basename(path_or_img)

    if crop:
        l, t, r, b = crop
        cw, chh = im.size
        im = im.crop((int(cw * l), int(chh * t), int(cw * r), int(chh * b)))

    w0, h0 = im.size
    h = max(1, int(round(W * h0 / w0)))
    im = im.resize((W, h), Image.BILINEAR)
    rgb = np.asarray(im, dtype=np.float32) / 255.0
    hue, sat, val = _rgb_to_hsv(rgb)
    lum = 0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]

    # --- деталь: энергия краев
    edge = _sobel(lum)
    edge = _box_blur(edge, 2)
    e_hi = float(np.percentile(edge, 99))
    edge_n = edge / max(e_hi, 1e-6)
    busy = edge_n > 0.16

    # --- небо: ровный участок, связанный с верхним краем, близкий по цвету к
    # медиане верхней полосы
    top = rgb[: max(2, h // 20)].reshape(-1, 3)
    top_med = np.median(top, axis=0)
    cdist = np.sqrt(((rgb - top_med) ** 2).sum(-1))
    flat = (edge_n < 0.10) & (cdist < 0.13)
    # Небо живет только в верхней части кадра. Без этого ограничения ровная
    # земля, совпавшая по цвету с верхней полосой, целиком уезжает в небо -
    # самопроверка на ровной заливке это ловит.
    flat[int(h * SKY_MAX_ROW):] = False
    sky = _label_from_top(flat)
    # Ровная земля, дотянувшаяся до верхнего края кадра, неотличима от неба по
    # «ровный и сверху». Отличает ее то, что она идет и до низа тоже. Поймано на
    # первом же кадре ортографической камеры: неба ноль, инструмент дал 0.528.
    full = (edge_n < 0.10) & (cdist < 0.13)
    reach = _label_from_top(full)
    if reach[-1].mean() > 0.20:
        sky = np.zeros_like(sky)
    sky_share = float(sky.mean())

    # --- мертвые клетки: ровные и однотонные, небо не в счет
    gx, gy = GRID
    ch, cw = h // gy, W // gx
    dead = 0
    cells = 0
    for j in range(gy):
        for i in range(gx):
            y0, y1 = j * ch, (j + 1) * ch if j < gy - 1 else h
            x0, x1 = i * cw, (i + 1) * cw if i < gx - 1 else W
            c_edge = edge_n[y0:y1, x0:x1]
            c_sky = sky[y0:y1, x0:x1]
            if c_sky.mean() > 0.6:
                continue          # клетка неба, считается отдельно
            cells += 1
            if c_edge.mean() < 0.055:
                dead += 1
    dead_share = dead / cells if cells else 0.0

    # --- доли площади
    busy_share = float((busy & ~sky).mean())
    bare_share = float(np.clip(1.0 - sky_share - busy_share, 0.0, 1.0))

    # --- цветовые семейства
    def families(mask):
        m = mask
        tot = int(m.sum())
        if tot == 0:
            return {k: 0.0 for k, _, _ in HUE_FAMILIES} | {"ahromatika": 0.0, "temnoe": 0.0}
        hh, ss, vv = hue[m], sat[m], val[m]
        out = {}
        dark = vv < DARK_V
        ahrom = (~dark) & (ss < SAT_AHROM)
        out["temnoe"] = float(dark.mean())
        out["ahromatika"] = float(ahrom.mean())
        chrom = (~dark) & (~ahrom)
        for k, lo, hi in HUE_FAMILIES:
            if lo < hi:
                sel = (hh >= lo) & (hh < hi)
            else:
                sel = (hh >= lo) | (hh < hi)
            out[k] = float((sel & chrom).mean())
        return out

    all_mask = np.ones((h, W), bool)
    fam_all = families(all_mask)
    fam_busy = families(busy & ~sky)

    # --- цветность по Хаслеру-Сюсструнку, стандартная метрика
    R, G, B = rgb[..., 0] * 255, rgb[..., 1] * 255, rgb[..., 2] * 255
    rg = R - G
    yb = 0.5 * (R + G) - B
    colorfulness = float(np.sqrt(rg.std() ** 2 + yb.std() ** 2)
                         + 0.3 * np.sqrt(rg.mean() ** 2 + yb.mean() ** 2))

    # --- крупность объектов
    heights = _components(busy & ~sky)
    if heights:
        med_h = float(np.median(heights)) / h
        p90_h = float(np.percentile(heights, 90)) / h
    else:
        med_h = p90_h = 0.0

    nonsky = ~sky
    return {
        "kadr": name,
        "razmer": f"{w0}x{h0}",
        "nebo": round(sky_share, 4),
        "mertvye_kletki": round(dead_share, 4),
        "detal": round(busy_share, 4),
        "golaya_zemlya": round(bare_share, 4),
        "krupnost_med": round(med_h, 4),
        "krupnost_p90": round(p90_h, 4),
        "tsvetnost": round(colorfulness, 2),
        "nasyshchennost": round(float(sat[nonsky].mean()) if nonsky.any() else 0.0, 4),
        "svetlota": round(float(val[nonsky].mean()) if nonsky.any() else 0.0, 4),
        "semya_max_ves": round(max(fam_all.values()), 4),
        "semya_max": max(fam_all, key=fam_all.get),
        "semya_max_detal_ves": round(max(fam_busy.values()), 4),
        "semya_max_detal": max(fam_busy, key=fam_busy.get),
        "semyi_ves": {k: round(v, 4) for k, v in fam_all.items()},
        "semyi_detal": {k: round(v, 4) for k, v in fam_busy.items()},
    }


# ---------------------------------------------------------------- самопроверка

def self_test():
    """Контрольные картинки с заранее известным ответом. Если тут сломалось,
    всем числам ниже верить нельзя."""
    ok = True

    def chk(name, cond, got):
        nonlocal ok
        mark = "ok  " if cond else "СБОЙ"
        if not cond:
            ok = False
        print(f"  [{mark}] {name}: {got}")

    # 1. Ровная заливка: детали нет, клетки мертвые
    flat = Image.new("RGB", (800, 450), (200, 90, 50))
    m = measure(flat, "ровная заливка")
    chk("ровная заливка, детали почти нет", m["detal"] < 0.02, m["detal"])
    chk("ровная заливка, клетки мертвые", m["mertvye_kletki"] > 0.9, m["mertvye_kletki"])

    # 2. Шум: деталь везде, мертвых клеток нет
    rng = np.random.default_rng(7)
    noise = Image.fromarray(rng.integers(0, 255, (450, 800, 3), dtype=np.uint8))
    m = measure(noise, "шум")
    chk("шум, деталь почти везде", m["detal"] > 0.8, m["detal"])
    chk("шум, мертвых клеток нет", m["mertvye_kletki"] < 0.05, m["mertvye_kletki"])

    # 3. Верхняя треть ровная и отличается по цвету: это небо
    a = np.zeros((450, 800, 3), np.uint8)
    a[:150] = (220, 210, 200)
    a[150:] = rng.integers(0, 255, (300, 800, 3), dtype=np.uint8)
    m = measure(Image.fromarray(a), "небо сверху")
    chk("небо около трети", 0.25 < m["nebo"] < 0.40, m["nebo"])

    # 4. Небо не должно находиться там, где его нет
    m = measure(noise, "шум, неба нет")
    chk("в шуме неба нет", m["nebo"] < 0.02, m["nebo"])

    # 4b. Ровная заливка во весь кадр это земля, а не небо
    m = measure(flat, "ровная заливка, неба нет")
    chk("в ровной заливке неба нет", m["nebo"] < 0.02, m["nebo"])

    # 5. Серая картинка малоцветная, радуга цветная
    gray = Image.fromarray(np.full((450, 800, 3), 128, np.uint8))
    g = measure(gray, "серое")["tsvetnost"]
    hue_ramp = np.zeros((450, 800, 3), np.uint8)
    for x in range(800):
        import colorsys
        r, gg, b = colorsys.hsv_to_rgb(x / 800.0, 1, 1)
        hue_ramp[:, x] = (int(r * 255), int(gg * 255), int(b * 255))
    c = measure(Image.fromarray(hue_ramp), "радуга")["tsvetnost"]
    chk("серое малоцветное", g < 5, g)
    chk("радуга цветная", c > 60, c)

    # 6. Крупность: одна большая фигура против россыпи мелких
    big = np.full((450, 800, 3), 30, np.uint8)
    big[100:350, 250:550] = (240, 120, 60)
    mb = measure(Image.fromarray(big), "одна крупная")["krupnost_p90"]
    small = np.full((450, 800, 3), 30, np.uint8)
    for yy in range(40, 420, 60):
        for xx in range(40, 780, 60):
            small[yy:yy + 18, xx:xx + 18] = (240, 120, 60)
    ms = measure(Image.fromarray(small), "россыпь мелких")["krupnost_p90"]
    chk("крупная фигура крупнее россыпи", mb > ms * 2, f"{mb} против {ms}")

    print("\nСамопроверка:", "пройдена" if ok else "ПРОВАЛЕНА")
    return 0 if ok else 1


# ---------------------------------------------------------------- вывод

COLS = [
    ("kadr", 34, "s"),
    ("nebo", 6, ".3f"),
    ("mertvye_kletki", 7, ".3f"),
    ("golaya_zemlya", 7, ".3f"),
    ("detal", 6, ".3f"),
    ("krupnost_med", 7, ".3f"),
    ("tsvetnost", 6, ".1f"),
    ("nasyshchennost", 6, ".3f"),
    ("semya_max_detal", 12, "s"),
    ("semya_max_detal_ves", 5, ".2f"),
]

HEAD = {
    "kadr": "кадр", "nebo": "небо", "mertvye_kletki": "мертвo",
    "golaya_zemlya": "голая", "detal": "деталь", "krupnost_med": "крупн",
    "tsvetnost": "цветн", "nasyshchennost": "насыщ",
    "semya_max_detal": "семья", "semya_max_detal_ves": "доля",
}


def print_table(rows):
    line = ""
    for k, wdt, _ in COLS:
        line += HEAD[k].ljust(wdt)[:wdt] + " "
    print(line)
    print("-" * len(line))
    for r in rows:
        line = ""
        for k, wdt, fmt in COLS:
            v = r[k]
            s = v if fmt == "s" else format(v, fmt)
            line += str(s).ljust(wdt)[:wdt] + " "
        print(line)


def expand(paths):
    out = []
    for p in paths:
        if os.path.isdir(p):
            for ext in ("*.png", "*.jpg", "*.jpeg"):
                out += sorted(glob.glob(os.path.join(p, ext)))
        else:
            out += sorted(glob.glob(p)) or [p]
    return [p for p in out if not os.path.basename(p).startswith("_")]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("paths", nargs="*")
    ap.add_argument("--json")
    ap.add_argument("--crop", help="вырез, доли L,T,R,B - например 0.2,0.15,0.8,0.75")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()

    if a.self_test:
        sys.exit(self_test())

    files = expand(a.paths)
    if not files:
        ap.error("нечего мерить")
    crop = tuple(float(x) for x in a.crop.split(",")) if a.crop else None
    rows = [measure(f, crop=crop) for f in files]
    print_table(rows)
    if a.json:
        with open(a.json, "w", encoding="utf-8") as fh:
            json.dump(rows, fh, ensure_ascii=False, indent=1)
        print("\nзаписано:", a.json)


if __name__ == "__main__":
    main()
