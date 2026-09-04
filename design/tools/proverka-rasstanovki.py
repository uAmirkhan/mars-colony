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
    # dekor-01 и dekor-09 остаются именами СЛОТОВ, но разрешаются сборщиком в
    # камни (таблица Zamena). Судить их по имени слота значит требовать фасад
    # у валуна. Проверяльщик читает таблицу раскладки, а не результат
    # разрешения, поэтому исключение приходится держать здесь вручную.
    "dekor-01", "dekor-09",
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

# Файл пишет САМ сборщик каждую сборку (см. ColonyOursBuilder, выгрузка
# фактических габаритов). В нём уже учтено всё, чего в таблице нет: стяжка
# зоны, разводка, привязка к сетке 1.25 м, поворот и настоящий габарит меша
# после среза плиты. Порядок строк совпадает с таблицей 1:1.
FAKT = r"c:\Ai\Jarvis\mars-unity\raskladka-fakt.md"

# Круглые в плане: описанный прямоугольник у них завышает углы. Два купола,
# сошедшиеся углами пятен на полметра, на кропе разделены чистым грунтом.
# Список короткий и явный по той же причине, что и BEZ_OPOZNAYUSHCHEY_GRANI:
# судить по префиксу «kupol-» нельзя, kupol-tunnel — капсула, а не купол.
KRUGLYE = ("zhiloy-kupol", "kupol-grib", "kupol-geodezicheskiy",
           "ploshchadka-shattla")

# Ниже этого проникновения меряется не постройка, а мягкий край меша.
POL_PRONIKANIYA = 0.30
# Для пары круглых требуется доля поглощения меньшего пятна: угловое касание
# двух куполов даёт 0.06, настоящее вложение — от 0.20.
DOLYA_KRUGLYH = 0.15

# Восьмое поле строки — НОМЕР ЗОНЫ, и без него проверка меряет не сцену.
# Координаты в таблице заданы ОТНОСИТЕЛЬНО зоны: статический конструктор
# сборщика прибавляет к каждой строке вектор Styazhka[зона] (и тот же вектор
# к центру круга зоны). До этой правки скрипт читал сырые числа таблицы, то
# есть считал, что все пять зон стоят в одной точке. Следствие было не
# теоретическим: перенос объекта промзоны в мировой центр колонии скрипт
# показывал как семь новых наложений, которых в собранной сцене нет, — а
# настоящие наложения между зонами он не видел вовсе (лог сборки той же
# минуты: «разводка: 38 пересечений, худшее 1,83 м»).
RX = re.compile(
    r'new object\[\]\s*\{\s*"([^"]+)"\s*,\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f\s*,'
    r'\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f'
    r'\s*,\s*(\d+)')

RX_STYAZHKA = re.compile(
    r'float\[\]\[\]\s+Styazhka\s*=\s*\{(.*?)^    \};', re.S | re.M)
RX_PARA = re.compile(r'new\[\]\s*\{\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f\s*\}')


def styazhka(t):
    """Вектор стяжки по зонам, прочитанный из самого сборщика.

    Читаем, а не прописываем числами: стяжка — главная ручка кругов, и
    захардкоженная копия разошлась бы с раскладкой на первой же правке.
    """
    m = RX_STYAZHKA.search(t)
    if not m:
        return {}
    return {i: (float(a), float(b))
            for i, (a, b) in enumerate(RX_PARA.findall(m.group(1)))}


def prochitat(put):
    with open(put, encoding="utf-8") as f:
        t = f.read()
    sdvig = styazhka(t)
    ryad = []
    for i, x, z, r, a, _p1, _p2, zona in RX.findall(t):
        dx, dz = sdvig.get(int(zona), (0.0, 0.0))
        ryad.append({"imya": i, "x": float(x) + dx, "z": float(z) + dz,
                     "razmer": float(r), "rot": float(a) % 360.0,
                     "zona": int(zona)})
    return ryad


RX_STROKA = re.compile(r'new object\[\]\s*\{\s*"([^"]+)"\s*,(.*?)\}\s*,\s*$', re.M)


def svyazki(t):
    """Имя связки по каждой строке таблицы, в порядке строк.

    Связка (шаттл на площадке, груз у склада, штабель) внутри себя не
    расталкивается сборщиком намеренно, и меряться на пересечение тоже не
    должна: «стоят вместе» здесь задано руками.
    """
    out = []
    for _imya, hvost in RX_STROKA.findall(t):
        kavychki = re.findall(r'"([^"]*)"', hvost)
        out.append(kavychki[-1] if kavychki else "")
    return out


def prochitat_fakt(put_cs, put_fakt):
    """Коробки СОБРАННОЙ сцены: позиция и габарит из выгрузки сборщика.

    Зачем отдельно от prochitat. Круговая мера ниже читает поле `razmer`
    таблицы — это НАМЕРЕНИЕ (во что вписать модель), а не сцена. Сверка
    каждой строки круговых ворот с собранным кадром дала: из девяти пар
    шесть в сцене расходятся с положительным зазором, включая верхнюю
    строку `shattl-otkrytyy x dekor-02` (ворота 4.23 м, факт +0.20 м).
    Встречно круги пропускают настоящие пересечения вытянутых тел: две
    капсулы kupol-tunnel сидели в жилых куполах на 1.00 м, и ворота молчали.
    Причина арифметическая: описанная окружность вокруг тела 5.0 x 2.0
    завышает его вдвое поперёк и занижает контакт вдоль.
    """
    try:
        with open(put_fakt, encoding="utf-8") as f:
            stroki = f.read().splitlines()
    except OSError:
        return None
    with open(put_cs, encoding="utf-8") as f:
        gruppy = svyazki(f.read())

    ryad = []
    for s in stroki[2:]:
        s = s.strip()
        if not s.startswith("|"):
            continue
        pole = [c.strip() for c in s.strip("|").split("|")]
        if len(pole) < 4:
            continue
        poz = re.findall(r"-?\d+,\d+|-?\d+", pole[2].replace(" ", ""))
        gab = re.findall(r"-?\d+,\d+|-?\d+", pole[3])
        if len(poz) < 2 or len(gab) < 3:
            continue
        chislo = lambda v: float(v.replace(",", "."))  # noqa: E731
        ryad.append({"imya": pole[0], "x": chislo(poz[0]), "z": chislo(poz[1]),
                     "gx": chislo(gab[0]), "gz": chislo(gab[2]), "gruppa": ""})
    if len(ryad) != len(gruppy):
        return None
    for o, g in zip(ryad, gruppy):
        o["gruppa"] = g
    return ryad


def vorota_naslo_korobki(ryad):
    """Пересечение ПРЯМОУГОЛЬНЫХ пятен собранной сцены."""
    bedy = []
    for i in range(len(ryad)):
        a = ryad[i]
        for j in range(i + 1, len(ryad)):
            b = ryad[j]
            if a["gruppa"] and a["gruppa"] == b["gruppa"]:
                continue
            if vlozhenie(a, b):
                continue
            ox = (a["gx"] + b["gx"]) / 2 - abs(a["x"] - b["x"])
            oz = (a["gz"] + b["gz"]) / 2 - abs(a["z"] - b["z"])
            if ox <= 0 or oz <= 0:
                continue
            vhod = min(ox, oz)
            dolya = ox * oz / min(a["gx"] * a["gz"], b["gx"] * b["gz"])
            oba_krugly = (a["imya"].startswith(KRUGLYE)
                          and b["imya"].startswith(KRUGLYE))
            if oba_krugly:
                if dolya < DOLYA_KRUGLYH:
                    continue
            elif vhod < POL_PRONIKANIYA:
                continue
            bedy.append((a, b, vhod, dolya))
    bedy.sort(key=lambda t: -t[2])
    return bedy


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

    fakt = prochitat_fakt(put, FAKT)
    if fakt is None:
        print("\n=== КОРОБКИ СОБРАННОЙ СЦЕНЫ — нет выгрузки, пропущено ===")
    else:
        kor = vorota_naslo_korobki(fakt)
        print(f"\n=== КОРОБКИ СОБРАННОЙ СЦЕНЫ — пар {len(kor)} "
              f"(круги {len(bedy)}) ===")
        print("  (замер по raskladka-fakt.md: стяжка, разводка, сетка и")
        print("   поворот уже учтены; в общий счёт пока НЕ входит)")
        for a, b, vhod, dolya in kor[:12]:
            print(f"    {a['imya']:22} и {b['imya']:22} "
                  f"вход {vhod:5.2f} м, доля {dolya:4.2f}")

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
