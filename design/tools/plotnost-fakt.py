# -*- coding: utf-8 -*-
"""Профиль плотности по СОБРАННОЙ сцене, а не по таблице раскладки.

Зачем отдельный скрипт. Прежний замер плотности читал сырые числа из
`ColonyOursBuilder.cs` и печатал «0-10 м: 81.1%» — то есть докладывал, что
ядро вчетверо плотнее кольца. Замер той же минуты по `raskladka-fakt.md`,
который пишет сама сборка, давал 35.9% в ядре против 63.3% в кольце 10-20 м:
кольцо вдвое плотнее ядра, ровно наоборот.

Расходятся они по двум причинам, и обе не мелочь.

1. Координаты в таблице заданы ОТНОСИТЕЛЬНО зоны. Статический конструктор
   сборщика прибавляет к каждой строке вектор `Styazhka[зона]`. Читать таблицу
   без него значит считать, что все пять зон стоят в одной точке.
2. После расстановки работают разводка (расталкивание пятен) и зажим `VZonu`
   (возврат в круг своей зоны). Незакреплённый объект уезжает от табличной
   точки на метры, а отдельные — на полтора десятка метров.

Отсюда правило: **числом считается то, что напечатала сборка.** Таблица —
это намерение, `raskladka-fakt.md` — результат.

Второе отличие от прежнего замера: центр колец берётся не в мировом нуле, а
в центроиде застройки по площади пятен, и зона 6 (пустошь — рамка кадра, не
колония) в центроид не входит. Мировой ноль к композиции отношения не имеет:
камера смотрит на массу, а не на начало координат.

Запуск:
    python plotnost-fakt.py [путь к raskladka-fakt.md]
"""
import io
import math
import re
import sys

PO_UMOLCHANIYU = r"c:\Ai\Jarvis\mars-unity\raskladka-fakt.md"

# Экранные оси при азимуте камеры -50 градусов: u — горизонталь кадра,
# v — глубина. Размах по ним и есть то, во что камера вписывает колонию.
UX, UZ = 0.766, 0.643
VX, VZ = -0.643, 0.766

RX_POS = re.compile(r"^(-?\d+[.,]\d+),\s*(-?\d+[.,]\d+)$")


def chislo(s):
    return float(s.replace(",", "."))


def prochitat(put):
    ryad = []
    for stroka in io.open(put, encoding="utf-8"):
        kletki = [k.strip() for k in stroka.strip().strip("|").split("|")]
        if len(kletki) != 4 or kletki[0] in ("объект", "---"):
            continue
        try:
            zona = int(kletki[1])
        except ValueError:
            continue
        m = RX_POS.match(kletki[2])
        if not m:
            continue
        gab = [chislo(v) for v in re.split(r"\s*x\s*", kletki[3])]
        ryad.append({
            "imya": kletki[0], "zona": zona,
            "x": chislo(m.group(1)), "z": chislo(m.group(2)),
            "gx": gab[0], "gy": gab[1], "gz": gab[2],
        })
    return ryad


def centroid(ryad):
    """Центр застройки по площади пятен. Пустошь не считается."""
    zn = [o for o in ryad if o["zona"] != 6]
    ves = sum(o["gx"] * o["gz"] for o in zn)
    return (sum(o["x"] * o["gx"] * o["gz"] for o in zn) / ves,
            sum(o["z"] * o["gx"] * o["gz"] for o in zn) / ves)


def main(put):
    ryad = prochitat(put)
    if not ryad:
        print(f"пусто: {put}")
        return 1
    cx, cz = centroid(ryad)
    print(f"объектов в сцене: {len(ryad)}")
    print(f"центроид застройки: ({cx:.1f}, {cz:.1f})\n")

    dolya = []
    for a, b in ((0, 10), (10, 20), (20, 30), (30, 50)):
        v = [o for o in ryad if a <= math.hypot(o["x"] - cx, o["z"] - cz) < b]
        ploshchad = math.pi * (b * b - a * a)
        zanyato = sum(o["gx"] * o["gz"] for o in v)
        d = zanyato / ploshchad * 100
        dolya.append(d)
        print(f"{a:2}-{b:2} м: объектов {len(v):3}, занято {d:5.1f}%")
    print("\nПРОФИЛЬ: " + " / ".join(f"{d:.1f}%" for d in dolya))
    otn = dolya[0] / dolya[2] if dolya[2] else float("inf")
    print(f"ядро / кольцо 20-30: {otn:.2f} при требовании §2 не ниже 2.00 "
          + ("— НОРМА" if otn >= 2.0 else "— НАРУШЕНО"))
    otn2 = dolya[0] / dolya[1] if dolya[1] else float("inf")
    print(f"ядро / кольцо 10-20: {otn2:.2f} "
          + ("— ядро плотнее" if otn2 >= 1.0 else "— КОЛЬЦО ПЛОТНЕЕ ЯДРА"))

    zn = [o for o in ryad if o["zona"] != 6]
    u = [UX * o["x"] + UZ * o["z"] for o in zn]
    w = [VX * o["x"] + VZ * o["z"] for o in zn]
    print(f"\nэкранный размах застройки: u {max(u) - min(u):.1f} м, "
          f"v {max(w) - min(w):.1f} м")

    print("\nсамые высокие:")
    for o in sorted(ryad, key=lambda q: -q["gy"])[:5]:
        r = math.hypot(o["x"] - cx, o["z"] - cz)
        print(f"  {o['imya']:24} высота {o['gy']:4.1f} м, от центра {r:5.1f} м")
    print("самые крупные по пятну:")
    for o in sorted(ryad, key=lambda q: -(q["gx"] * q["gz"]))[:5]:
        r = math.hypot(o["x"] - cx, o["z"] - cz)
        print(f"  {o['imya']:24} пятно {o['gx']:4.1f}x{o['gz']:4.1f}, "
              f"от центра {r:5.1f} м")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else PO_UMOLCHANIYU))
