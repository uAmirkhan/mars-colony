"""Вырезка элемента интерфейса с однотонного фона генерации и подбор зон 9-slice.

Отличие от ikonka_vyrezka.py: без кольцевой обводки (она портит прямые кромки),
фон задаётся цветом (#FF00AA / #7CFF00) или берётся с углов, а зоны 9-slice
ищутся по однородности: граница слева/справа/сверху/снизу = первая полоса,
дальше которой цвет элемента не меняется. Результат: PNG с альфой и JSON
{"border": [L, B, R, T]} в порядке Unity Vector4 (x=левый, y=нижний, z=правый, w=верхний).

    python element_narezka.py вход.png --out папка [--fon FF00AA] [--dopusk 40] [--imya panel-a]
"""
import argparse, json, os, sys
import numpy as np
from PIL import Image


def maska_fona(arr, fon, dopusk):
    d = np.abs(arr[:, :, :3].astype(np.int32) - np.array(fon, dtype=np.int32)).sum(2)
    return d <= dopusk


def zalivka_ot_kraev(fon_m):
    """Только фон, достижимый с краёв: полости внутри элемента остаются элементом."""
    h, w = fon_m.shape
    dost = np.zeros_like(fon_m)
    stek = [(y, x) for x in range(w) for y in (0, h - 1) if fon_m[y, x]] + \
           [(y, x) for y in range(h) for x in (0, w - 1) if fon_m[y, x]]
    for y, x in stek:
        dost[y, x] = True
    while stek:
        y, x = stek.pop()
        for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
            if 0 <= ny < h and 0 <= nx < w and fon_m[ny, nx] and not dost[ny, nx]:
                dost[ny, nx] = True
                stek.append((ny, nx))
    return dost


def granitsa_9slice(rgb, alpha, porog=6.0):
    """Зоны: от центра идём к краю, пока строка/столбец совпадает с центральным
    (в средней половине по другой оси); первая отличающаяся полоса — граница зоны.
    Возвращает (L, B, R, T) в px обрезанного элемента."""
    h, w = alpha.shape
    cy, cx = h // 2, w // 2
    f = rgb.astype(np.float32)
    stolb = lambda x: f[h // 4: 3 * h // 4, x]
    strok = lambda y: f[y, w // 4: 3 * w // 4]
    c_st, c_sr = stolb(cx), strok(cy)
    L = 0
    for x in range(cx, -1, -1):
        if np.abs(stolb(x) - c_st).mean() > porog: L = x + 1; break
    R = 0
    for x in range(cx, w):
        if np.abs(stolb(x) - c_st).mean() > porog: R = w - x; break
    T = 0
    for y in range(cy, -1, -1):
        if np.abs(strok(y) - c_sr).mean() > porog: T = y + 1; break
    B = 0
    for y in range(cy, h):
        if np.abs(strok(y) - c_sr).mean() > porog: B = h - y; break
    # запас 2 px, чтобы шов растяжения не попал на градиент кромки
    return [L + 2, B + 2, R + 2, T + 2]


def maska_po_ottenku(arr, fon, shirina_gr=28.0, min_nasysh=0.22):
    """Пиксель — фон, если его оттенок в пределах shirina_gr градусов от оттенка
    фона и насыщенность заметная. Так уходят и розовый ореол JPEG, и мягкая
    тень поверх фона (розово-серая), которых нет у самого элемента."""
    import colorsys
    f = arr[:, :, :3].astype(np.float32) / 255.0
    mx = f.max(2); mn = f.min(2); d = mx - mn
    sat = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    r, g, b = f[:, :, 0], f[:, :, 1], f[:, :, 2]
    hue = np.zeros_like(mx)
    m = d > 1e-6
    rc = np.where(m, (mx - r) / np.maximum(d, 1e-6), 0); gc = np.where(m, (mx - g) / np.maximum(d, 1e-6), 0); bc = np.where(m, (mx - b) / np.maximum(d, 1e-6), 0)
    hue = np.where(r == mx, bc - gc, np.where(g == mx, 2.0 + rc - bc, 4.0 + gc - rc))
    hue = (hue / 6.0) % 1.0 * 360.0
    hf = colorsys.rgb_to_hsv(*(np.array(fon) / 255.0))[0] * 360.0
    dh = np.abs((hue - hf + 180) % 360 - 180)
    return (dh < shirina_gr) & (sat > min_nasysh)


def erozia(m, px):
    out = m.copy()
    for _ in range(px):
        sh = out.copy()
        sh[1:, :] &= out[:-1, :]; sh[:-1, :] &= out[1:, :]; sh[:, 1:] &= out[:, :-1]; sh[:, :-1] &= out[:, 1:]
        out = sh
    return out


def main():
    p = argparse.ArgumentParser()
    p.add_argument("vhod"); p.add_argument("--out", required=True)
    p.add_argument("--fon", default=None, help="HEX фона; без него берётся медиана углов")
    p.add_argument("--dopusk", type=int, default=60, help="не используется в маске по оттенку, оставлен для совместимости")
    p.add_argument("--erozia", type=int, default=2, help="на сколько px съесть кромку")
    p.add_argument("--imya", default=None)
    p.add_argument("--dyrki", action="store_true", help="фон и внутри замкнутых полостей (кружок капсулы)")
    a = p.parse_args()
    im = Image.open(a.vhod).convert("RGB"); arr = np.asarray(im)
    if a.fon:
        fon = tuple(int(a.fon.lstrip("#")[i:i + 2], 16) for i in (0, 2, 4))
    else:
        ugly = np.array([arr[0, 0], arr[0, -1], arr[-1, 0], arr[-1, -1]]); fon = tuple(int(v) for v in np.median(ugly, 0))
    fon_m = maska_po_ottenku(arr, fon)
    fon_dost = fon_m if a.dyrki else zalivka_ot_kraev(fon_m)
    elem = erozia(~fon_dost, a.erozia)
    ys, xs = np.where(elem)
    if len(ys) == 0:
        print("элемент не найден: весь кадр — фон", file=sys.stderr); sys.exit(1)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    rgb = arr[y0:y1, x0:x1].copy(); alpha = elem[y0:y1, x0:x1]
    # сглаживание альфы: 1 px полупрозрачной кромки, цвет кромки берём с соседнего внутреннего пикселя
    al = alpha.astype(np.float32)
    sosed = np.zeros_like(al)
    sosed[1:, :] += al[:-1, :]; sosed[:-1, :] += al[1:, :]; sosed[:, 1:] += al[:, :-1]; sosed[:, :-1] += al[:, 1:]
    kroma = (~alpha) & (sosed > 0)
    al_out = np.where(alpha, 255, 0).astype(np.uint8)
    al_out[kroma] = (sosed[kroma] / 4.0 * 255).astype(np.uint8)
    # цвет кромочных пикселей — средний по видимым соседям, чтобы не тянуть розовое
    ky, kx = np.where(kroma)
    h, w = alpha.shape
    for y, x in zip(ky, kx):
        acc = np.zeros(3); n = 0
        for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
            if 0 <= ny < h and 0 <= nx < w and alpha[ny, nx]:
                acc += rgb[ny, nx]; n += 1
        if n: rgb[y, x] = (acc / n).astype(np.uint8)
    out = np.dstack([rgb, al_out])
    border = granitsa_9slice(rgb, alpha)
    imya = a.imya or os.path.splitext(os.path.basename(a.vhod))[0]
    os.makedirs(a.out, exist_ok=True)
    Image.fromarray(out, "RGBA").save(os.path.join(a.out, imya + ".png"))
    dolya = elem.mean() * 100
    tsentr = rgb[(y1 - y0) >> 1, (x1 - x0) >> 1]
    # контроль: доля видимых пикселей с оттенком фона
    ost = maska_po_ottenku(np.dstack([rgb, np.zeros_like(rgb[:, :, 0])]), fon) & (al_out > 40)
    info = {"border": border, "razmer": [int(x1 - x0), int(y1 - y0)], "fon": "#%02X%02X%02X" % fon,
            "dolya_elementa": round(float(dolya), 1), "tsentr_rgb": [int(v) for v in tsentr],
            "ostatok_fona_pct": round(float(ost.sum() / max((al_out > 40).sum(), 1) * 100), 3)}
    json.dump(info, open(os.path.join(a.out, imya + ".json"), "w"), ensure_ascii=False)
    print(f"{imya}: {info['razmer'][0]}x{info['razmer'][1]} px, элемент {dolya:.0f}% кадра, остаток фона {info['ostatok_fona_pct']}%, border L{border[0]} B{border[1]} R{border[2]} T{border[3]}")


if __name__ == "__main__":
    main()
