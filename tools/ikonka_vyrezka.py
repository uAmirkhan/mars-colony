"""Вырезка предмета из генерации с НЕРОВНЫМ фоном: заливка от краёв.

`ikonka_obvodka.py` берёт цвет фона по углам и режет порогом от него. На фоне
с виньеткой и тёплым свечением (Gemini, партия 2026-09-04) это захватывает
полкадра. Здесь фон определяется заливкой от рамки кадра с допуском К СОСЕДУ,
а не к углу: плавный градиент фона проходится шаг за шагом, а резкий контур
предмета заливку останавливает.

    python ikonka_vyrezka.py <папка> [--dopusk 6] [--razmer 512] [--cvet FBEBBA] [--tolshchina 0.021]

На выходе: <имя>.png с альфой, предмет по центру, вписан в квадрат с полями,
кремовый контур как в ikonka_obvodka.
"""
import sys, os, argparse
from collections import deque
import numpy as np
from PIL import Image, ImageFilter


def zalivka_fona(a: np.ndarray, dopusk: int) -> np.ndarray:
    h, w, _ = a.shape
    fon = np.zeros((h, w), dtype=bool)
    q = deque()
    for x in range(w):
        for y in (0, h - 1):
            fon[y, x] = True; q.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            fon[y, x] = True; q.append((y, x))
    a16 = a.astype(np.int16)
    while q:
        y, x = q.popleft()
        c = a16[y, x]
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and not fon[ny, nx]:
                if np.abs(a16[ny, nx] - c).sum() <= dopusk:
                    fon[ny, nx] = True; q.append((ny, nx))
    return fon


def obrabotat(put: str, out: str, dopusk: int, razmer: int, cvet: str, dolya: float):
    im = Image.open(put).convert("RGB")
    # заливка в уменьшенном размере ради скорости, маска потом поднимается
    k = 4
    small = im.resize((im.width // k, im.height // k), Image.LANCZOS)
    fon = zalivka_fona(np.asarray(small), dopusk)
    maska = Image.fromarray((~fon).astype(np.uint8) * 255).resize(im.size, Image.LANCZOS)
    maska = maska.filter(ImageFilter.MedianFilter(5))
    rgba = im.convert("RGBA"); rgba.putalpha(maska)
    bbox = maska.getbbox()
    if bbox is None:
        print("пусто:", put); return
    rgba = rgba.crop(bbox)
    # вписать в квадрат с полем 8%
    s = max(rgba.size); pole = int(s * 0.08); s2 = s + pole * 2
    holst = Image.new("RGBA", (s2, s2), (0, 0, 0, 0))
    holst.paste(rgba, ((s2 - rgba.width) // 2, (s2 - rgba.height) // 2))
    holst = holst.resize((razmer, razmer), Image.LANCZOS)
    # контур расширением альфы, как в ikonka_obvodka
    t = max(1, int(round(razmer * dolya)))
    alpha = holst.getchannel("A")
    rasshir = alpha.filter(ImageFilter.MaxFilter(t * 2 + 1))
    r, g, b = int(cvet[0:2], 16), int(cvet[2:4], 16), int(cvet[4:6], 16)
    kontur = Image.new("RGBA", holst.size, (r, g, b, 0)); kontur.putalpha(rasshir)
    itog = Image.alpha_composite(kontur, holst)
    itog.save(out)
    dolya_obj = (np.asarray(alpha) > 127).mean() * 100
    print("готово: %s  предмет %.0f%% квадрата" % (os.path.basename(out), dolya_obj))


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("papka"); ap.add_argument("--dopusk", type=int, default=6)
    ap.add_argument("--razmer", type=int, default=512); ap.add_argument("--cvet", default="FBEBBA")
    ap.add_argument("--tolshchina", type=float, default=0.021); ap.add_argument("--out", default=None)
    a = ap.parse_args()
    out = a.out or os.path.join(a.papka, "vyrezano"); os.makedirs(out, exist_ok=True)
    for f in sorted(os.listdir(a.papka)):
        if f.lower().endswith((".png", ".jpg", ".jpeg")) and "-obvedeno" not in f:
            obrabotat(os.path.join(a.papka, f), os.path.join(out, os.path.splitext(f)[0] + ".png"),
                      a.dopusk, a.razmer, a.cvet, a.tolshchina)
