# -*- coding: utf-8 -*-
"""Проверка раскладки колонии на нарушения законов жанра.

Зачем скрипт, а не глаз. Доктрина расстановки писалась четыре раза, и каждый
раз выводы формулировались пожеланиями: «здания стоят по сетке», «склад
единичен», «объекты не наслаиваются». Замер показал, что к восемнадцатому
витку 39 процентов сцены стоит незаконно, склад продублирован восемь раз, а
девять объектов в кадре не видно вовсе. Пожелание, которое никто не считает,
нарушается молча и накапливается.

Отсюда правило работы: **правило без исполнимой проверки не принимается.**

## Про запрет разворота: у нас причина ДРУГАЯ, чем в Township

Township не умеет разворачивать здания вообще — там двумерный арт, и у модели
нарисован ровно один вид. Разработчики отвечали на форуме прямо: вращение
«cannot be implemented», потому что понадобилось бы дорисовать три ракурса на
каждое здание.

**У нас этой причины нет.** Наши модели трёхмерные и текстурированы со всех
сторон: развернуть можно любую. Но остаётся вторая причина, и она сильнее:

**на двухстах пикселях экрана здание опознаётся по фасаду.** Дверь, окна,
вывеска, крыльцо — всё, по чему объект читается, живёт на одной грани. Наши
же собственные ворота приёмки моделей требуют «определяющий признак цел».
Развернуть здание задом значит выбросить этот признак, оставив безликую
коробку.

Отсюда деление не «постройка против декора», а **есть ли у объекта грань,
которая его опознаёт**. У камня её нет — он узнаётся силуэтом с любой стороны.
У барака есть.
"""
import re
import sys
from collections import Counter

PO_UMOLCHANIYU = r"c:\Ai\Jarvis\mars-unity\Assets\Editor\ColonyOursBuilder.cs"

ZAKONNYE_UGLY = {0.0, 90.0}

# У этих объектов опознающей грани нет: камень, лёд, грунт, пыль узнаются
# силуэтом с любой стороны. Список НАМЕРЕННО короткий и явный.
#
# Первая редакция освобождала весь `dekor-*` скопом, и проверка отрапортовала
# 6 нарушений вместо 106. Рендер старого декора показал, почему это неверно:
# под именем dekor- лежат и валуны, и ящики, и площадки, и вездеход. Скопом
# по префиксу тут судить нельзя.
BEZ_OPOZNAYUSHCHEY_GRANI = (
    "kamen", "kamni", "led-", "grunt-", "izmoroz", "pyl-", "valun",
)

# Пока старый декор не заменён, он считается рукотворным: это строгая
# сторона, и она честнее — 45° под ящиком остаётся браком.
SOMNITELNYE = ("dekor-",)

EDINICHNYE = {
    "sklad": 1,
    "zavod": 1,
    "fabrika": 1,
    "stantsiya": 1,
    "ploshchadka-shattla": 1,
    "kupol-geodezicheskiy": 1,
}

# Пары, где один объект СТОИТ НА другом. Это не столкновение, а вложение, и
# проверка наложения обязана их пропускать: шаттл на площадке, буровая на
# пятне грунта. Ключ — префикс основания, значение — префиксы того, что на нём
# законно стоит.
NOSITELI = {
    "ploshchadka-shattla": ("shuttle", "shattl", "dron", "dekor", "figura"),
    "grunt-": ("burovaya", "dekor", "led-", "kamen"),
    "dno_karyera": ("burovaya", "dekor", "kamen"),
}

PREDEL_POVTORA = 12

RX = re.compile(
    r'new object\[\]\s*\{\s*"([^"]+)"\s*,\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f\s*,'
    r'\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f')


def prochitat(put):
    with open(put, encoding="utf-8") as f:
        t = f.read()
    return [{"imya": i, "x": float(x), "z": float(z),
             "razmer": float(r), "rot": float(a) % 360.0}
            for i, x, z, r, a in RX.findall(t)]


def bez_grani(imya):
    return imya.startswith(BEZ_OPOZNAYUSHCHEY_GRANI)


def vlozhenie(a, b):
    """Один стоит на другом — наложение законно."""
    for osnova, gosti in NOSITELI.items():
        if a["imya"].startswith(osnova) and b["imya"].startswith(gosti):
            return True
        if b["imya"].startswith(osnova) and a["imya"].startswith(gosti):
            return True
    # Природная россыпь: камни и лёд лежат кучей, это норма
    return bez_grani(a["imya"]) and bez_grani(b["imya"])


def vorota_orientatsiya(ryad):
    krivye, zadom, spornye = [], [], []
    for o in ryad:
        if bez_grani(o["imya"]):
            continue
        plohо = o["rot"] % 90 != 0 or o["rot"] not in ZAKONNYE_UGLY
        if not plohо:
            continue
        (spornye if o["imya"].startswith(SOMNITELNYE) else
         (krivye if o["rot"] % 90 != 0 else zadom)).append(o)
    return krivye, zadom, spornye


def vorota_naslo(ryad):
    bedy = []
    n = len(ryad)
    for i in range(n):
        a = ryad[i]
        ra = a["razmer"] * 0.5
        for j in range(i + 1, n):
            b = ryad[j]
            if vlozhenie(a, b):
                continue
            rb = b["razmer"] * 0.5
            d = ((a["x"] - b["x"]) ** 2 + (a["z"] - b["z"]) ** 2) ** 0.5
            zazor = d - (ra + rb)
            if zazor < -0.35 * min(ra, rb):
                bedy.append((a, b, d, zazor))
    return bedy


def vorota_edinichnost(ryad):
    schet = Counter()
    for o in ryad:
        for k in EDINICHNYE:
            if o["imya"].startswith(k):
                schet[k] += 1
                break
    return [(r, n, EDINICHNYE[r]) for r, n in schet.items() if n > EDINICHNYE[r]]


def main(put):
    ryad = prochitat(put)
    print(f"позиций в раскладке: {len(ryad)}\n")

    krivye, zadom, spornye = vorota_orientatsiya(ryad)
    vsego = len(krivye) + len(zadom) + len(spornye)
    print(f"=== ВОРОТА 1: ОРИЕНТАЦИЯ — нарушений {vsego} ===")
    print(f"  вне сетки (не кратен 90°):      {len(krivye)}")
    print(f"  задом или не той стороной:       {len(zadom)}")
    print(f"  старый декор (спорно, но строго): {len(spornye)}")
    for o in (krivye + zadom)[:10]:
        print(f"    {o['imya']:26} {o['rot']:6.1f}°")

    bedy = vorota_naslo(ryad)
    print(f"\n=== ВОРОТА 2: НАЛОЖЕНИЕ ПЯТЕН — пар {len(bedy)} ===")
    print("  (вложение «стоит на» и природная россыпь исключены)")
    for a, b, d, z in sorted(bedy, key=lambda t: t[3])[:12]:
        print(f"    {a['imya']:22} и {b['imya']:22} перекрытие {-z:5.2f} м")

    edin = vorota_edinichnost(ryad)
    print(f"\n=== ВОРОТА 3: ЕДИНИЧНОСТЬ — нарушений {len(edin)} ===")
    for r, n, nado in edin:
        print(f"    {r:24} стоит {n}, положено {nado}")

    schet = Counter(o["imya"] for o in ryad)
    povt = [(i, n) for i, n in schet.most_common() if n > PREDEL_POVTORA]
    print(f"\n=== ВОРОТА 4: ПОВТОР МОДЕЛИ (предел {PREDEL_POVTORA}) ===")
    for i, n in povt:
        print(f"    {i:24} x{n}")

    itog = vsego + len(bedy) + len(edin) + len(povt)
    print(f"\nвсего нарушений: {itog}")
    return 1 if itog else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else PO_UMOLCHANIYU))
