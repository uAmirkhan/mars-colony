# Промпты — панель производства по референсу Township (06.09.2026)

Референс заказчика: `raw/Playrix/tawnship_train/photo_30…jpg` (Молокозавод) и `photo_35…jpg` (Ткацкая фабрика).
Генератор — Gemini, генерирует заказчик. Словарь — интерфейсный (глянец, слои, bevel, rim light),
пустые места задаются утверждением «left perfectly smooth and blank, reserved for X pasted afterward»,
без отрицаний (реестр `REESTR.md`). Фон под вырезку — сплошной пурпур `#FF00AA`, у тёмных элементов
(лоток, дорожка) пурпур тоже читается, тело в другой зоне спектра.

Что уже есть и остаётся: лента-панель с заголовком (`panel.png`), зелёная кнопка (`knopka-a`), крест
(`knopka-krest-2`), полоса прогресса (`bar-trek`/`bar-zalivka`), иконки товаров.

| # | Элемент | Где стоит | Холст | Девять долей | Файл |
|---|---------|-----------|-------|--------------|------|
| 1 | Стойка-конвейер | под лентой, во всю ширину панели, в ней слоты очереди | `--aspect 5:1`, 2K | торцы ~12 % ширины, верх/низ ~25 % высоты | `konveyer.png` |
| 2 | Открытая коробка (пустой слот) | внутри стойки, по одной на слот очереди | 1:1 | нет | `korobka-pustaya.png` |
| 3 | Коробка с крышкой-«+» (слот докупки) | последний слот, когда очередь можно расширить | 1:1 | нет | `korobka-plus.png` |
| 4 | Лоток рецептов | под стойкой, в нём иконки продуктов | `--aspect 4:1` | торцы ~15 %, верх/низ ~30 % | `lotok.png` |
| 5 | Карточка-подсказка с хвостиком | над лотком по тапу на иконку: имя, входы, XP, время | `--aspect 4:3` | борта ~12 %, хвостик внизу по центру вне долей | `kartochka-podskazka.png` |
| 6 | Пилюля значения | внутри карточки: «дом + число», «часы + время»; и таймер под продуктом в слоте | `--aspect 3:1` | торцы ~35 % | `pilyulya-znacheniya.png` |
| 7 | Карман выдачи | левый торец стойки, куда «выезжает» готовый продукт | 1:1 | нет (простая картинка) | `karman-vydachi.png` |

## 1. konveyer — стойка-конвейер
```
Wide horizontal production rack for a casual mobile game interface, seen straight on. A pale steel-blue rounded metal frame with a soft bevel, a thin cream rim light along the top edge and one small bright highlight in the upper left corner. Inside the frame a long recessed groove of dark blue-grey glass runs the whole width, with faint diagonal chevron marks on its floor, its surface left perfectly smooth and blank, reserved for product boxes pasted afterward. Township-style glossy layered UI, saturated but soft colors. Plain solid magenta #FF00AA background.
```

## 2. korobka-pustaya — открытая коробка
```
A single open cardboard box for a casual mobile game interface, three-quarter view from slightly above, warm sand-yellow cardboard with four flaps folded outward, soft inner shadow inside the box, thick cream #FBEBBA outline, one small bright highlight on the front edge, Township style. The box rests directly on its own soft shadow. Plain solid magenta #FF00AA background.
```

## 3. korobka-plus — коробка с плюсом
```
A single open cardboard box for a casual mobile game interface, three-quarter view from slightly above, pale grey-beige faded cardboard with four flaps folded outward, a big glossy green plus sign standing inside the box, thick cream #FBEBBA outline, one small bright highlight on the plus, Township style. The box rests directly on its own soft shadow. Plain solid magenta #FF00AA background.
```

## 4. lotok — лоток рецептов
```
Wide horizontal tray for a casual mobile game interface, seen straight on: a rounded rectangle of dark smoky translucent glass, near-black warm tone, matte with a soft inner shadow along the top and a faint lighter rim along the bottom edge, its whole surface left perfectly smooth and blank, reserved for product icons pasted afterward. Township-style layered UI. Plain solid magenta #FF00AA background.
```

## 5. kartochka-podskazka — карточка-подсказка
```
A rounded speech-bubble card for a casual mobile game interface, seen straight on, cream-white lacquered body with a soft sand gradient toward the bottom, a thin warm-sand outline and a soft bevel, a small pointed tail at the bottom center, one small bright highlight in the upper left. The card surface is left perfectly smooth and blank, reserved for a title, icons and numbers pasted afterward. Township-style glossy layered UI. Plain solid magenta #FF00AA background.
```

## 6. pilyulya-znacheniya — пилюля значения
```
A small horizontal stadium-pill for a casual mobile game interface, seen straight on, pale cream lacquered body with a thin sand outline and a soft bevel, the left end holding a plain smooth circular socket left empty and undecorated, waiting for a small icon placed there later, the rest of the pill left perfectly smooth and blank, reserved for a number pasted afterward. Township-style glossy UI. Plain solid magenta #FF00AA background.
```

## 7. karman-vydachi — карман выдачи
```
A square output pocket for a casual mobile game interface, seen straight on: a pale steel-blue rounded metal frame with a soft bevel around a recessed pocket of lighter blue-grey glass, a small cream rim light on the top edge and one small bright highlight in the upper left, the pocket interior left perfectly smooth and blank, reserved for a finished product pasted afterward. Township-style glossy layered UI. Plain solid magenta #FF00AA background.
```

Куда класть: `mars-unity/Assets/UI/Elementy/` с именами из таблицы. Пока их нет, панель собрана на элементах комплекта (pole-sklad как стойка и слоты, bar-trek как лоток) — это временная подмена, не финальный вид.
