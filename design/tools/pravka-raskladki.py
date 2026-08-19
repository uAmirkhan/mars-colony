# -*- coding: utf-8 -*-
"""Хирургия по таблице раскладки: удаления, прореживание, ориентация.

Почему скриптом, а не правками руками. Правок полторы сотни, и каждая — строка
в таблице на 275 позиций. Руками это и долго, и невоспроизводимо: повторить
тот же результат на следующем витке будет нечем. Скрипт же можно прогнать
заново после любой пересборки таблицы.

Работает по исходнику сборщика, строки вида
    new object[] { "имя", x, z, целевой_размер, поворот, ... }

Запуск:
    python design/tools/pravka-raskladki.py [--primenit]
Без ключа — только показывает, что будет сделано.
"""
import io
import re
import sys
from collections import Counter

PUT = r"c:\Ai\Jarvis\mars-unity\Assets\Editor\ColonyOursBuilder.cs"

# --- Шаг 1: удалить из сцены -------------------------------------------
# Владелец назвал эти объекты браком. Замены нет ни в одной партии, а
# оставлять то, что бросается в глаза первым, — сознательно держать дефект.
# Пустое место честнее плохой модели. Файлы на диске не трогаются.
UDALIT = {
    "dekor-08": "колёсный ровер",
    "dekor-05": "флажок на постаменте",
    "dekor-03": "антенна-тарелка",
    "dron-t1": "дрон", "dron-t3": "дрон", "dron-kurier": "дрон",
    "fabrika-tekstilnaya": "фабрика костюмов",
}

# --- Шаг 3: шаттл -------------------------------------------------------
PEREIMENOVAT = {"shuttle-2000": "shattl-zakrytyy"}

# --- Шаг 4: камни -------------------------------------------------------
KAMNI = ("dekor-09", "dekor-01", "grunt-regolit-2")
CEL_KAMNEY = 40
# Радиус, внутри которого камней не остаётся вовсе: это застроенное ядро.
# Число подобрано по разбросу построек, печатается при прогоне.
R_YADRA = 16.0

# --- Шаг 5: ориентация --------------------------------------------------
BEZ_GRANI = ("kamen", "kamni", "led-", "grunt-", "izmoroz", "pyl-", "valun",
             "dekor-09", "dekor-01")

RX = re.compile(
    r'(new object\[\]\s*\{\s*")([^"]+)("\s*,\s*)(-?[\d.]+)(f\s*,\s*)(-?[\d.]+)'
    r'(f\s*,\s*)(-?[\d.]+)(f\s*,\s*)(-?[\d.]+)(f)')


def bez_grani(imya):
    return imya.startswith(BEZ_GRANI)


def sobrat(t):
    """Все строки раскладки: имя, x, z, размер, поворот, границы в тексте."""
    ryad = []
    for m in RX.finditer(t):
        ryad.append({
            "m": m, "imya": m.group(2),
            "x": float(m.group(4)), "z": float(m.group(6)),
            "razmer": float(m.group(8)), "rot": float(m.group(10)) % 360.0,
        })
    return ryad


def otobrat_kamni(kamni):
    """Какие камни оставить: кучками по краю, а не ровным слоем.

    Внутри ядра не остаётся ни одного — это и есть закон «плотное ядро,
    пустая округа». Снаружи набираются кучки: берётся затравка, к ней
    добавляются ближайшие соседи, затем следующая затравка подальше.
    """
    snaruzhi = [k for k in kamni if (k["x"] ** 2 + k["z"] ** 2) ** 0.5 >= R_YADRA]
    snaruzhi.sort(key=lambda k: -(k["x"] ** 2 + k["z"] ** 2))

    ostavit, zatravki = [], []
    for k in snaruzhi:
        if len(ostavit) >= CEL_KAMNEY:
            break
        # Затравка должна стоять далеко от прежних, иначе кучки сольются
        if all(((k["x"] - z["x"]) ** 2 + (k["z"] - z["z"]) ** 2) ** 0.5 > 11.0
               for z in zatravki):
            zatravki.append(k)
            ostavit.append(k)
            # к затравке — до трёх ближайших соседей, это и есть кучка
            sosedi = sorted(
                (s for s in snaruzhi if s is not k and s not in ostavit),
                key=lambda s: (s["x"] - k["x"]) ** 2 + (s["z"] - k["z"]) ** 2)
            for s in sosedi[:3]:
                if len(ostavit) < CEL_KAMNEY:
                    ostavit.append(s)
    return set(id(k) for k in ostavit)


def main(primenit):
    t = io.open(PUT, encoding="utf-8").read()
    ryad = sobrat(t)
    print(f"позиций до правки: {len(ryad)}")

    kamni = [r for r in ryad if r["imya"] in KAMNI]
    ostavit_kamni = otobrat_kamni(kamni)
    print(f"камней было {len(kamni)}, остаётся {len(ostavit_kamni)}")

    udalyaem, povorot = Counter(), 0
    novyy, kursor = [], 0

    for r in ryad:
        m = r["m"]
        novyy.append(t[kursor:m.start()])
        kursor = m.end()
        stroka = m.group(0)

        if r["imya"] in UDALIT:
            udalyaem[r["imya"]] += 1
            novyy.append("__UDALIT__" + stroka)
            continue
        if r["imya"] in KAMNI and id(r) not in ostavit_kamni:
            udalyaem["камень"] += 1
            novyy.append("__UDALIT__" + stroka)
            continue

        imya = PEREIMENOVAT.get(r["imya"], r["imya"])
        rot = r["rot"]
        if not bez_grani(imya):
            novrot = (round(rot / 90.0) * 90) % 180
            if abs(novrot - rot) > 0.01:
                povorot += 1
                rot = novrot
        novyy.append(f'{m.group(1)}{imya}{m.group(3)}{m.group(4)}{m.group(5)}'
                     f'{m.group(6)}{m.group(7)}{m.group(8)}{m.group(9)}{rot:g}{m.group(11)}')

    novyy.append(t[kursor:])
    itog = "".join(novyy)

    # Помеченные строки вырезаем целиком, вместе с хвостом до конца строки
    itog = re.sub(r'^[^\n]*__UDALIT__[^\n]*\n', '', itog, flags=re.M)

    print("\nудаляется:")
    for k, v in udalyaem.most_common():
        print(f"  {k:24} {v}")
    print(f"\nповоротов выправлено: {povorot}")
    ostalos = len(sobrat(itog))
    print(f"позиций после правки: {ostalos}")

    if primenit:
        io.open(PUT, "w", encoding="utf-8").write(itog)
        print("\nЗАПИСАНО")
    else:
        print("\n(пробный прогон, ключ --primenit чтобы записать)")


if __name__ == "__main__":
    main("--primenit" in sys.argv)
