# -*- coding: utf-8 -*-
"""Разбор диагностического прохода: что в кадре не так, числами.

На вход — `diag-id.png` (каждый объект залит своим плоским цветом из грубой
палитры) и `diag-opis.json` (где объект обязан быть). На выход — список
дефектов, которые глазами на красивом кадре не видны, потому что не видно как
раз недостающего.

## Как читается номер объекта

Прямое кодирование номера в цвет (r = i & 255) не работает: по дороге от
шейдера к PNG цвет проходит преобразование, и номер 1 приезжает как 11.
Искать, где именно оно применяется, бессмысленно — любое изменение настроек
рендера сломает результат заново.

Поэтому цвет берётся из палитры в семь заведомо далёких уровней на канал.
Преобразование эти уровни СДВИГАЕТ, но не переставляет: оно монотонно. Значит
уровень восстанавливается по ПОРЯДКУ встреченных значений, а не по их
величине, и гамма разборщику знать не нужна вовсе.

## Что ловится и почему именно так

**Объекта нет в кадре совсем.** Проекция габарита непустая, а пикселей ноль.
Значит его целиком закрыли, либо он отрисовался за землёй, либо провалился под
неё. На кадре это выглядит просто как кадр без него — заметить пропажу одного
камня из ста восьмидесяти семи глазами нельзя.

**Объект закрыт почти целиком.** Видно меньше десятой доли ожидаемого. Обычно
это здание, залезшее в соседа, или декор, утонувший в склоне.

**Объект вылез за рамку.** Проекция упирается в край кадра.

Ожидаемая площадь считается по ГАБАРИТУ, а он шире силуэта: у сферы на
четверть, у тонкой антенны в разы. Поэтому сравниваются ДОЛИ, а пороги нарочно
занижены. Разборщик ищет провалы на порядок, а не расхождения на проценты.

Запуск:
    python design/tools/razbor-diagnostiki.py <папка mars-unity>
"""
import json
import os
import sys
from collections import Counter

import numpy as np
from PIL import Image

POROG_ZAKRYT = 0.10      # доля от ожидаемого, ниже которой объект считаем закрытым
POROG_PROPAL = 4         # пикселей меньше этого — объекта в кадре нет


def urovni_po_poryadku(kanal, skolko):
    """Значения канала -> номер уровня палитры.

    Берём встреченные значения, отбрасываем фон (самое тёмное) и раскладываем
    оставшиеся по порядку. Сколько уровней ожидать — говорит палитра.
    """
    znach = sorted(int(v) for v in np.unique(kanal))
    # Фоновый ноль стоит заведомо ниже нижнего уровня палитры (тот с 40)
    znach = [v for v in znach if v > 0]
    if len(znach) < skolko:
        # Уровень мог не встретиться, если объектов мало. Дополняем сверху —
        # порядок снизу вверх от этого не ломается.
        return {v: i for i, v in enumerate(znach)}
    # Значений ровно столько, сколько уровней (или больше — тогда лишние это
    # кромки, и их отнесём к ближайшему по порядку)
    shag = max(1, len(znach) // skolko)
    karta = {}
    for i, v in enumerate(znach):
        karta[v] = min(skolko - 1, i // shag)
    return karta


def svarnoy(o):
    """Габарит — плохая мерка для сваренных и вытянутых объектов.

    Полотна дорог, каёмки и слои, сваренные в один меш (суффикс `_sveden`),
    растянуты на всю карту: габарит охватывает пол-кадра при видимой полосе в
    несколько пикселей ширины. Отношение «видно к ожидалось» у них всегда
    около нуля, и это свойство мерки, а не дефект сцены.

    То же у фигур: части колонистов сварены по всей сцене, поэтому габарит
    одного шлема покрывает миллион пикселей.

    Из списка перекрытых такие исключаются — иначе он состоит из них целиком и
    настоящее перекрытие в нём тонет. Проверка «не видно совсем» для них
    остаётся: там мерка не нужна, там ноль или не ноль.
    """
    imya = o["imya"]
    if imya.endswith("_sveden") or imya.startswith("figura_"):
        return True
    w, h = o["ramka_px"]
    return w > 0 and h > 0 and max(w / h, h / w) > 6.0


def razobrat(koren):
    put_png = os.path.join(koren, "diag-id.png")
    put_json = os.path.join(koren, "diag-opis.json")
    put_pal = os.path.join(koren, "diag-palitra.json")
    for p in (put_png, put_json, put_pal):
        if not os.path.exists(p):
            raise SystemExit(f"нет файла {p} — сними «Mars/Snyat diagnostiku» в Unity")

    pal = json.load(open(put_pal, encoding="utf-8"))
    U = pal["urovney"]
    opis = json.load(open(put_json, encoding="utf-8"))

    a = np.asarray(Image.open(put_png).convert("RGB"))
    # Сколько уровней реально задействовано по каждому каналу при N объектах
    N = len(opis)
    nado = [min(U, N), min(U, max(1, -(-N // U))), min(U, max(1, -(-N // (U * U))))]
    kartы = [urovni_po_poryadku(a[..., k], nado[k]) for k in range(3)]

    nomera = np.zeros(a.shape[:2], dtype=np.int32)
    for k, mult in enumerate((1, U, U * U)):
        sloy = np.zeros(a.shape[:2], dtype=np.int32)
        for v, ur in kartы[k].items():
            sloy[a[..., k] == v] = ur
        nomera += sloy * mult
    # Фон: там, где все каналы нулевые
    fon_maska = (a[..., 0] == 0) & (a[..., 1] == 0) & (a[..., 2] == 0)
    nomera[fon_maska] = -1

    schet = Counter(nomera.ravel().tolist())
    vsego_px = nomera.size
    fon = schet.get(-1, 0)

    propali, zakryty, za_ramkoy = [], [], []
    vidno = {}
    for o in opis:
        px = schet.get(o["nomer"] - 1, 0)      # индекс палитры на единицу меньше
        vidno[o["nomer"]] = px
        ozh = o["ozhidalos_px"]
        if not o["v_kadre"] or ozh < 30:
            continue
        if px < POROG_PROPAL:
            propali.append((o, px, ozh))
        elif px < ozh * POROG_ZAKRYT and not svarnoy(o):
            zakryty.append((o, px, ozh))
        w, h = o["ramka_px"]
        if w >= 1598 or h >= 998:
            za_ramkoy.append((o, w, h))

    print(f"объектов в описи   {N}")
    print(f"уровней палитры    {U}, задействовано по каналам {nado}")
    print(f"фон кадра          {fon / vsego_px * 100:.1f}%")
    print(f"занято объектами   {(vsego_px - fon) / vsego_px * 100:.1f}%")
    opoznano = sum(v for k, v in schet.items() if 0 <= k < N)
    print(f"опознано пикселей  {opoznano / vsego_px * 100:.1f}%"
          f"   (остальное — кромки и цвета вне палитры)")

    print(f"\n=== НЕ ВИДНО СОВСЕМ: {len(propali)} ===")
    for o, px, ozh in sorted(propali, key=lambda t: -t[2])[:20]:
        print(f"  {o['imya']:26} ожидалось {ozh:8.0f} px, видно {px:4d}"
              f"   низ Y {o['niz_y']:7.2f}  до камеры {o['do_kamery']:6.1f}")

    print(f"\n=== ЗАКРЫТЫ БОЛЬШЕ ЧЕМ НА 90%: {len(zakryty)} ===")
    print("  (сваренные полотна и фигуры исключены — у них габарит не мерка)")
    for o, px, ozh in sorted(zakryty, key=lambda t: -t[2])[:20]:
        print(f"  {o['imya']:26} ожидалось {ozh:8.0f} px, видно {px:6d}"
              f"  ({px / ozh * 100:4.1f}%)")

    print(f"\n=== УПЁРЛИСЬ В РАМКУ КАДРА: {len(za_ramkoy)} ===")
    for o, w, h in za_ramkoy[:12]:
        print(f"  {o['imya']:26} проекция {w:.0f}x{h:.0f} px")

    print("\n=== ЗАНИМАЮТ КАДР (топ 12) ===")
    po_imeni = Counter()
    for o in opis:
        po_imeni[o["imya"]] += vidno.get(o["nomer"], 0)
    for imya, px in po_imeni.most_common(12):
        print(f"  {imya:28} {px:8d} px  {px / vsego_px * 100:5.2f}%")


if __name__ == "__main__":
    razobrat(sys.argv[1] if len(sys.argv) > 1 else r"c:\Ai\Jarvis\mars-unity")
