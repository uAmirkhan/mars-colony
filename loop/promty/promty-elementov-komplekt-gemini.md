# Промпты — комплект под Gemini (капсула, бар, ячейка, кнопки, замок)

Источники: `pasport-elementov-interfeysa.md` (элементы 3-6), `spec-sklad.md`
разделы 6-7 (крестик, замок), словарь провалов и приём — `REESTR.md`.
Генератор — только Gemini (`nano-banana`/`gemini-3.1-flash-image`), заказчик
генерирует сам. Разрешение 2K на всех девяти. Формат: капсула и бар —
широкий холст (рекомендую `--aspect 3:1`, у капсулы и бара есть выступающие
детали сверху/снизу — медальон и волосок, поэтому не 5:1 как у кнопки
действия из пилота, нужен запас по высоте); ячейка, обе кнопки и замок —
квадрат `--aspect 1:1`.

**Хвост держится одним словарём с пилотом** (`promty-elementov-pilot.md`):
глянец, слои, `left perfectly smooth and blank, reserved for X pasted
afterward` вместо отрицаний. Слова `matte/dusty/no gloss` из хвоста иконок
сюда не переносятся — это язык товара, не интерфейса.

**Почему капсула без шестигранной оправы в тексте промпта.** Задача прямо
требует не рисовать оправу — она добавляется кодом поверх (как и в паспорте
элемент 3 отмечает: «оправа медальона — сай-фай добавка», её можно
пришивать отдельно). Слот под медальон описан нейтрально: «пустая область,
ждущая жетон», без слова «hexagon» и без слова «no frame» (отрицание
вызвало бы рамку).

**Почему трек бара один, не два.** Цвета трека А (`#1E1810` тёплый) и Б
(`#101A24` холодный) почти неотличимы на глаз при такой плотности альфы —
это тёмное стекло в обоих случаях, различие тоньше, чем может держать один
промпт без риска увести генератор в две разные картинки. Один нейтральный
«тёмное стекло» под обе палитры, тонкая раскраска и волосок Б — на коде,
тем же приёмом, что и оправа капсулы.

**Почему у ячейки А и Б один и тот же фон (`#FF00AA`), а не пурпур/лайм по
палитре.** Нутро ячейки в обеих палитрах — тёмный тил/сине-зелёный
(`#435555→#2D372F` у А, `#2E4550→#22303A` у Б) — то есть сам предмет уже
живёт в зелёно-синей зоне спектра. Кислотно-салатовый фон `#7CFF00` для Б
лежал бы в той же зоне — риск размытой границы при вырезке (та же логика,
что уже отмечена в REESTR для кнопки 2Б на салатовом). Беру пурпур для
обеих версий ячейки — другая зона спектра, чем сам тил. Капсула Б осталась
на салатовом: её тело почти чёрное (низкая светлота держит контраст с
любым ярким фоном), в зелёно-синюю зону явно не попадает.

---

## 1. kapsula-a — капсула счётчика валюты, палитра А (тёплая)

```
Wide horizontal stadium-pill shaped UI currency counter body, fully rounded ends, seen face-on. The left end flares into a plain smooth circular cut-out socket, noticeably taller than the capsule body, left completely empty and undecorated, waiting for a coin medallion placed there later. The rest of the capsule is a smooth dark glossy glass surface, deep warm blackish-olive tone, softly translucent, with no seams, its right two-thirds left perfectly smooth and blank, reserved for a number pasted afterward. Clean flat vector game-UI render, soft even studio lighting, gentle glass sheen, solid uniform bright magenta-pink background #FF00AA, centered horizontally with generous empty margin above and below, wide flat crop.
```

## 2. kapsula-b — капсула счётчика валюты, палитра Б (холодная)

```
Wide horizontal UI currency counter body with moderately rounded corners, clearly not a full pill, seen face-on. The left end flares into a plain smooth circular cut-out socket, noticeably taller than the capsule body, left completely empty and undecorated, waiting for a coin medallion placed there later. The rest of the capsule is a smooth dark glossy glass surface, deep cold blackish-navy tone, softly translucent, with a thin glowing cyan hairline tracing the very top edge, its right two-thirds left perfectly smooth and blank, reserved for a number pasted afterward. Clean flat vector game-UI render, soft even studio lighting, gentle glass sheen, solid uniform bright acid-green background #7CFF00, centered horizontally with generous empty margin above and below, wide flat crop.
```

## 3. bar-trek — трек бара прогресса (пустой, обе палитры)

```
Wide horizontal progress bar track, a slim rounded-pill shaped groove with fully rounded ends, seen face-on. The groove is a smooth dark recessed glass surface, neutral near-black tone, softly translucent, left perfectly smooth and blank along its entire length, reserved for a colored fill pasted afterward. Clean flat vector game-UI render, soft even studio lighting, subtle inner shadow along the top inner edge suggesting a recessed groove, solid uniform bright magenta-pink background #FF00AA, centered horizontally with generous empty margin above and below, wide flat crop.
```

## 4. bar-zalivka — заливка бара прогресса (циан-синий градиент)

```
Wide horizontal progress bar fill, a slim rounded-pill shaped bar with fully rounded ends, seen face-on, filling the entire frame edge to edge as if at full charge. Glossy layered glass surface: a bright cyan highlight along the top edge, a saturated cyan-blue gradient through the middle, deepening to a rich blue along the bottom edge, with a thin darker blue outline tracing the whole silhouette. The surface reads as one continuous smooth band from end to end. Clean flat vector game-UI render, soft even studio lighting, solid uniform bright magenta-pink background #FF00AA, centered horizontally with generous empty margin above and below, wide flat crop.
```

## 5. yacheyka-a — ячейка товара для склада, палитра А

```
Square UI storage cell, a recessed rounded-square slot seen face-on. The upper portion holds an empty rectangular reserved area, left completely bare, slightly overlapping the cell's top edge, waiting for an item icon placed there later. The cell floor is a smooth glossy dark teal-grey material, with a lighter rim tracing the top edge and a subtly bright lower edge. In the bottom right corner, a small blank rounded plate overlaps the corner, its surface left perfectly smooth, reserved for a quantity number pasted afterward. Clean flat vector game-UI render, soft even studio lighting, solid uniform bright magenta-pink background #FF00AA, centered, generous empty margin, square crop.
```

## 6. yacheyka-b — ячейка товара для склада, палитра Б

```
Square UI storage cell, a recessed rounded-square slot seen face-on. The upper portion holds an empty rectangular reserved area, left completely bare, slightly overlapping the cell's top edge, waiting for an item icon placed there later. The cell floor is a smooth glossy dark cold navy-teal material, with a lighter cool rim tracing the top edge and a subtly bright lower edge. In the bottom right corner, a small blank rounded plate overlaps the corner, its surface left perfectly smooth, reserved for a quantity number pasted afterward. Clean flat vector game-UI render, soft even studio lighting, solid uniform bright magenta-pink background #FF00AA, centered, generous empty margin, square crop.
```

## 7. knopka-menyu — круглая/сквиркл кнопка меню (одна на обе палитры)

```
Square UI menu button icon, a squircle rounded-square shape seen face-on, filling most of the frame. Glossy layered glass surface: a bright cyan-blue dome highlight along the top, a saturated blue gradient through the body, deepening to a dark navy-blue along the bottom, with a thin dark navy contact rim tracing the whole silhouette. Centered on the button, three short horizontal rounded white bars stacked with even gaps between them, each with a soft inner shadow. Clean flat vector game-UI render, soft even studio lighting, solid uniform bright magenta-pink background #FF00AA, centered, generous empty margin around the button for a soft shadow, square crop.
```

## 8. knopka-krest — круглая красная кнопка закрытия с белым крестом

```
Square UI close button icon, a circle seen face-on, filling most of the frame. Glossy layered surface: a bright warm-red highlight along the top, a saturated red-orange gradient through the body, deepening to a darker red-brown rim along the bottom, giving a rounded three-dimensional look. Centered on the circle, a bold white X glyph with clean rounded strokes. Clean flat vector game-UI render, soft even studio lighting, solid uniform bright magenta-pink background #FF00AA, centered, generous empty margin around the circle for a soft shadow, square crop.
```

## 9. zamok — иконка замка (закрытая ячейка)

```
Square UI icon of a closed padlock, seen face-on, filling most of the frame. Flat simple silhouette style: a rounded rectangular lock body with a curved shackle arc on top, rendered as a single smooth light grey-white shape with soft subtle shading suggesting gentle volume, no other visual details. Clean flat vector game-UI render, soft even studio lighting, solid uniform bright magenta-pink background #FF00AA, centered, generous empty margin around the icon, square crop.
```

---

## Порядок прогона

Не всей партией из девяти разом. Первый заход — 2-3 несмежных: например
`kapsula-b` + `yacheyka-a` + `knopka-krest` (разные формы, разные палитры,
не тянутся к общей форме). По результату — решать про остальные шесть.
После генерации замерить фон (ΔE к заявленному хексу по углам), пустоту
зарезервированных полей (OCR=0, низкое std по каналам), и для капсулы Б —
отдельно проверить, не съел ли зелёный фон тонкий циановый волосок. Замер
дописать сюда же и в `REESTR.md`.
