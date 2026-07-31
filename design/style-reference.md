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

Перед генерацией приложи картинками, в этом порядке:

1. **Принятый эталон стиля** — `design/etalons/etalon2-residential-1.png` (и `-2.png`).
   Держит палитру, плотность, рендер.
2. **Утверждённая база сцены** (последняя принятая) —
   `design/raw-generations/gemini-0731-0010-ff4hl8.png`.
3. **Уже принятый ассет из текущей волны** — прикладывается к каждому следующему
   объекту как стилевой якорь. Первый принятый в волне задаёт стиль всей волне.

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
watermark; sparkles, glints, lens flare.
```

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
