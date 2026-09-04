"""Постобработка иконок ресурсов: вырезать фон, обвести кремовым контуром.

Обводку нельзя просить у генератора: он рисует её как физический контур внутри
объекта — с дырками между ножками гриба и видом наклейки, вырезанной ножницами.
В Township контур принадлежит интерфейсу, а не картинке, поэтому здесь он
добавляется кодом: ровный, одинаковой толщины, одинаковый на всех иконках.

Толщина 2 px при 96 px ячейки склада снята с референсов Township (#FBEBBA).
Здесь она задана долей от размера, чтобы держаться при любом разрешении.

Запуск:
    python ikonka_obvodka.py <папка-с-png> [--tolshchina 0.021] [--cvet FBEBBA]
"""
import sys
import os
from PIL import Image, ImageFilter

CVET_PO_UMOLCHANIYU = "FBEBBA"   # кремовый контур Township, замер по референсам
DOLYA_TOLSHCHINY = 0.021         # 2 px на 96 — в долях, чтобы не зависеть от размера
PORog_FONA = 26                  # насколько пиксель должен отличаться от угла, чтобы считаться объектом


def vyrezat_fon(im: Image.Image) -> Image.Image:
    """Фон у генератора ровный, поэтому берём его цвет из углов и снимаем заливкой."""
    im = im.convert("RGBA")
    w, h = im.size
    px = im.load()
    ugly = [px[0, 0], px[w - 1, 0], px[0, h - 1], px[w - 1, h - 1]]
    fon = tuple(sum(u[k] for u in ugly) // 4 for k in range(3))
    maska = Image.new("L", (w, h), 0)
    mpx = maska.load()
    for y in range(h):
        for x in range(w):
            r, g, b, _ = px[x, y]
            otlichie = abs(r - fon[0]) + abs(g - fon[1]) + abs(b - fon[2])
            mpx[x, y] = 255 if otlichie > PORog_FONA else 0
    # сгладить край, иначе контур пойдёт зубцами
    maska = maska.filter(ImageFilter.MedianFilter(3))
    im.putalpha(maska)
    return im


def obvesti(im: Image.Image, cvet_hex: str, dolya: float) -> Image.Image:
    """Контур строится расширением альфы, а не рисованием по краю объекта."""
    w, h = im.size
    tolshchina = max(1, int(round(min(w, h) * dolya)))
    alpha = im.getchannel("A")
    razduto = alpha.filter(ImageFilter.MaxFilter(tolshchina * 2 + 1))
    r = int(cvet_hex[0:2], 16)
    g = int(cvet_hex[2:4], 16)
    b = int(cvet_hex[4:6], 16)
    kontur = Image.new("RGBA", (w, h), (r, g, b, 0))
    kontur.putalpha(razduto)
    itog = Image.alpha_composite(kontur, im)
    return itog


def obrabotat(put: str, cvet: str, dolya: float) -> str:
    im = Image.open(put)
    im = vyrezat_fon(im)
    im = obvesti(im, cvet, dolya)
    koren, _ = os.path.splitext(put)
    vyhod = koren + "-obvedeno.png"
    im.save(vyhod)
    return vyhod


def main() -> None:
    if len(sys.argv) < 2:
        print(__doc__)
        return
    papka = sys.argv[1]
    cvet = CVET_PO_UMOLCHANIYU
    dolya = DOLYA_TOLSHCHINY
    for i, a in enumerate(sys.argv):
        if a == "--cvet" and i + 1 < len(sys.argv):
            cvet = sys.argv[i + 1].lstrip("#")
        if a == "--tolshchina" and i + 1 < len(sys.argv):
            dolya = float(sys.argv[i + 1])
    faily = [f for f in os.listdir(papka)
             if f.lower().endswith((".png", ".jpg", ".jpeg")) and "-obvedeno" not in f]
    if not faily:
        print("в папке нет картинок")
        return
    for f in faily:
        vyhod = obrabotat(os.path.join(papka, f), cvet, dolya)
        print("готово:", os.path.basename(vyhod))
    print(f"обработано {len(faily)}, контур #{cvet}, толщина {dolya:.3f} от стороны")


if __name__ == "__main__":
    main()
