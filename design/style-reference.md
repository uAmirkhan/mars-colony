# Style Reference — Mars Colony (читать ПЕРВЫМ перед любой генерацией)

Единый источник стиля для генерации ассетов через Gemini / Nano Banana.
Локальный агент (Antigravity) читает этот файл первым, затем грузит эталоны
по путям ниже, и только потом берётся за конкретный объект.

Правила стиля здесь не выдуманы — извлечены из принятого эталонного промпта
`design/prompts/etalon2-residential.txt` и рабочих разборов в
`design/asset-queue.md` и `design/raw-generations/README.md`. При конфликте
источник правды — эти файлы и принятые эталоны.

---

## 1. Что грузить как визуальный якорь (обязательно, по путям)

> **Правка 2026-07-31 по итогам приёмки** (`design/etalons-acceptance-2026-07-31.md`).
> Пути в этом разделе указывали на `design/etalons/etalon2-residential-1.png` и
> `-2.png`, которых нет ни в одной папке проекта. Инструкция «приложи принятый
> эталон» была невыполнима, и волна пошла без якоря — отсюда два несовместимых
> рендера в `design/etalons/`. Пути ниже проверены и существуют.

Перед генерацией приложи картинками, в этом порядке:

1. **Отгруженные ассеты — главный якорь.** `public/assets/transport/drone.png`,
   `buildings/drone_pad.png`, `transport/shuttle.png`. Это принятый владельцем
   набор, он согласован между собой и задаёт стиль всей волне: тёплая
   кремово-латунная палитра, терракотовые акценты, бирюзовое стекло, **контур
   по силуэту**.
2. **Герой-кадр колонии** —
   `design/etalons/Gemini_Generated_Image_kbfciekbfciekbfc.png`. Держит палитру
   и плотность застройки для сценовых генераций. Берётся рендер, не композиция
   и не подписи: текст в кадре нечитаем, ледяная добыча продублирована.
3. **Утверждённая база сцены** (последняя принятая) —
   `design/raw-generations/gemini-0731-0010-ff4hl8.png`.

**Чего якорем не прикладывать.** `...rv0qkj...` (площадка с дроном) — там дрон
нарисован второй раз и не совпадает с отгруженным `drone.png`; два источника
правды на один объект возвращают поломку «объект изобретается заново».
`...l657nn (1).png` и `...x7az8n...` — это макеты интерфейса, а не мир.

Правило, без которого цепочка рвётся: **скачал генерацию — сразу положил в
`design/raw-generations/`** по имени `<объект>-<версия>-<дата>.png`. Нет файла в
проекте = нечего приложить к следующей генерации = объект изобретается заново.

---

## 2. Нерушимые правила рендера (NON-NEGOTIABLE, копировать в промпт)

```
NON-NEGOTIABLE RENDER RULES. These override everything else.
1. NO OUTLINES. No contour line, ink line, edge stroke or dark rim on any object.
   Shapes separate through colour and value only.
2. VOLUME, NOT FLAT FILL. Every surface carries a two-step gradient from a lighter
   lit facet into a darker shaded facet. No wall, roof or prop is a single flat tone.
3. NO CUBES, NO BOXES. Buildings are soft rounded volumes: barrel vaults, domes,
   capsules, cylinders with rounded caps, fat shells with large corner radii.
   Nothing reads as a shipping crate or a rounded box.
4. EVERY BUILDING A DIFFERENT SILHOUETTE. Never the same shell repeated in another colour.
5. ONE KEY LIGHT from the upper left at 55 degrees. Every object drops a soft blurred
   shadow offset toward the lower right, past its own footprint. No vertical light,
   no shadow hidden under its object.

STYLE: soft volumetric shading baked into a 2D mobile game asset, rendering language
of Township and Hay Day. Stylized toy-like light falloff, not physically realistic.
Finish: semi-gloss painted metal and ceramic composite with a soft sheen, premium
mobile city-builder look.

CAMERA: three-quarter view at ~45 degrees. Horizon a quarter down from the top,
band of pale rosy haze under a peach-pale sky.
```

Негатив (в конец каждого промпта):
```
NEGATIVE: any outline, contour or dark edge stroke; flat single-tone fill with no
gradient; cubic or box-shaped buildings; objects with no cast shadow; yellow tracked
excavators or real-world construction machinery; radiation symbols, hazard hatching,
warning tape, safety cones; craters, cracks, rubble, wreckage; text, numbers, labels,
watermark; sparkles, glints, lens flare; no user interface, no buttons, no panels,
no icons, no HUD anywhere in the frame, including the corners.
```

> **Правка 2026-07-31: UI протекает из якоря.** Хвост про интерфейс добавлен по
> факту. Когда якорем прикладывался герой-кадр, генератор скопировал из него
> кнопки в четыре угла кадра шаттла — вместе со стилем. Вырезать спрайт из
> такого кадра нельзя. Герой-кадр остаётся якорем, но негатив обязан явно
> запрещать интерфейс.

> [!warning] Правило 1 ОТМЕНЕНО решением владельца 2026-07-31
> Блок выше сохранён как есть, но пункт «NO OUTLINES» и запрет `any outline,
> contour or dark edge stroke` в негативе больше не действуют. Полгода промпты
> требовали «без обводки», и полгода лучшие отобранные кадры приходили с
> контуром. Решение принято фактом отбора: **итоговый набор — с обводкой**, и
> все четыре отгруженных ассета контурные и согласованы между собой.
>
> Пока текст правила не переписан, каждый новый промпт требует обратного тому,
> что уже лежит в `public/assets/`, и следующий объект приедет чужим. Что ещё
> надо переписать под это решение — `design/etalons-acceptance-2026-07-31.md`
> раздел 4.

---

## 3. Правило против дублей (главная свежая поломка)

На герой-кадре вылезло дважды: две зоны добычи льда и две буровые по руде,
одинаковые, разного размера — читается как баг «одно и то же нарисовано дважды».
Причина: в промпте не задано число и роль объекта, модель клонирует акцент.

Правило:
- **Один ресурс = ровно одна установка одного размера.** В промпте задавать числом:
  `exactly one ice quarry`, `exactly one ore drill rig`.
- **Разный ресурс = разный силуэт И цвет ресурса.** Лёд — бледные полупрозрачные
  кубы; руда — ржаво-красные куски; и установки разной формы (карьер с режущей
  машиной ≠ буровая-вышка с конвейером).
- Никогда не давать «своё видение» на боевых ассетах — только точный промпт со
  ссылкой на якорь. «Своё видение» = source дублей и разнобоя.

Типы ресурсов в игре (под каждый ровно одна установка): вода/лёд, руда/реголит,
энергия (солнечные панели). Больше на карте не плодить.

---

## 4. Как вообще генерим (workflow, проверено на 8 прогонах)

- **Объект доводится до принятого спрайта ОДИН раз, дальше не трогается.** Сцена
  складывается из принятых спрайтов, а не перерисовывается целиком.
- **Полная перегенерация запрещена как метод**: добавляет крупное, но переизобретает
  всё принятое (v6 переизобрёл шаттл, дрон, модули). Правка целого кадра наоборот
  не умеет добавить крупный объект.
- **Перенос объекта между кадрами** (две картинки: база + донор, модель копирует, а
  не изобретает) — единственный способ добавить крупное, не сломав принятое.
  Рабочий образец — `design/prompts/hero-colony-v8-transfer.txt`.
- **Одна цель за проход.** Чинить два объекта в одной генерации нельзя, ломается всё.
- Если генератор не справляется за 2-3 прохода — не воевать, собирать кадр коллажем
  из принятых спрайтов кодом.

---

## 5. Чеклист перед приёмкой любой генерации

1. Ни одного контура/обводки. Объёмный градиент на каждой поверхности.
2. Свет сверху-слева, тень мягкая вправо-вниз у каждого объекта.
3. Ни одного дубля: каждый ресурс добывается ровно одной установкой.
4. Здания разные по силуэту, нет коробок.
5. Палитра и плотность совпадают с эталоном.
6. Всё принятое ранее — на месте и не перерисовано (при переносе/правке).
