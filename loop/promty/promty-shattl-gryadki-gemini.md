# Промпты — шаттл-поезд и грядки теплицы (06.09.2026, по ТЗ-поправкам п.1–2)

Генерирует заказчик в Gemini. Общее: 2K, фон сплошной пурпур `#FF00AA`, Township-стиль, слово «icon» не используется,
пустые места задаются утверждением «left perfectly smooth and blank, reserved for X pasted afterward».
Файлы класть в `mars-unity/Assets/Resources/UI/Elementy/` (элементы) и `.../Ikonki/` (маркеры).

| # | Элемент | Где | Холст | Файл |
|---|---------|-----|-------|------|
| 1 | Контейнер шаттла пустой | панель шаттла: ряд из 3–5 контейнеров, внутри иконка товара и «×N» | 1:1 | `konteyner-pustoy.png` |
| 2 | Контейнер шаттла закрытый | тот же ряд, отсек загружен | 1:1 | `konteyner-zakrytyy.png` |
| 3 | Табло рейса | панель шаттла в рейсе: пункт назначения буквами, таймер | 5:1 | `tablo-reysa.png` |
| 4 | Контейнер наград | площадка после прилёта: тап — зачисление модулей | 1:1 | `konteyner-nagrady.png` |
| 5 | Поддон пустой | экран теплицы: сетка 2×3 | 2:1 | `poddon-pustoy.png` |
| 6 | Поддон с ростками | тот же, растёт | 2:1 | `poddon-rostki.png` |
| 7 | Поддон со спелой зеленью | тот же, готово | 2:1 | `poddon-spelyy.png` |
| 8 | Фон теплицы | подложка экрана теплицы под сеткой поддонов | 3:1 | `fon-teplitsy.png` |
| 9 | Зелёный пузырь «готово» | маркер над зданием, заливка целиком зелёная | 1:1 | `marker-gotovo-zelenyy.png` |

## 1. konteyner-pustoy
```
A single open cargo container for a casual mobile game interface, seen straight on, slightly from above: a rounded rectangular pod with a cream-white body, sage-teal side stripes and a small round porthole, its open front face left perfectly smooth and blank, reserved for a product picture and a number pasted afterward. Township-style glossy layered UI, thick cream #FBEBBA outline, one small bright highlight on the top edge. It rests directly on its own soft shadow. Plain solid magenta #FF00AA background.
```

## 2. konteyner-zakrytyy
```
The same single cargo container for a casual mobile game interface, seen straight on, now sealed: a rounded rectangular pod with a cream-white body and sage-teal side stripes, its front hatch closed with a big glossy green circle holding a white check mark in the center. Township-style glossy layered UI, thick cream #FBEBBA outline, one small bright highlight on the top edge. It rests directly on its own soft shadow. Plain solid magenta #FF00AA background.
```

## 3. tablo-reysa
```
Wide horizontal split-flap departure board for a casual mobile game interface, seen straight on: a dark graphite frame with a soft bevel and a thin cream rim light, holding a single row of blank dark flap tiles, each tile left perfectly smooth and blank, reserved for letters and digits pasted afterward, the right end of the frame holding a small empty rounded window for a timer. Township-style glossy layered UI. Plain solid magenta #FF00AA background.
```

## 4. konteyner-nagrady
```
A single sealed reward crate for a casual mobile game interface, three-quarter view from slightly above: a chunky rounded crate in sand-cream with a teal lid and a big cream hexagon emblem on the front, a small glossy golden star badge on the lid corner, Township style, saturated colors, thick cream #FBEBBA outline, one small bright highlight on the lid. It rests directly on its own soft shadow. Plain solid magenta #FF00AA background.
```

## 5. poddon-pustoy
```
A single long rectangular growing tray for a casual mobile game interface, seen from above at a slight angle: a rounded sage-teal plastic trough filled with smooth dark brown soil, the soil surface left perfectly smooth and blank, reserved for plants pasted afterward, Township style, thick cream #FBEBBA outline, one small bright highlight on the near rim. It rests directly on its own soft shadow. Plain solid magenta #FF00AA background.
```

## 6. poddon-rostki
```
The same long rectangular growing tray for a casual mobile game interface, seen from above at a slight angle: a rounded sage-teal plastic trough with dark brown soil and a neat row of small bright green sprouts with two leaves each, Township style, saturated colors, thick cream #FBEBBA outline, one small bright highlight on the near rim. It rests directly on its own soft shadow. Plain solid magenta #FF00AA background.
```

## 7. poddon-spelyy
```
The same long rectangular growing tray for a casual mobile game interface, seen from above at a slight angle: a rounded sage-teal plastic trough overflowing with lush ripe green leafy plants, a few tiny bright fruits peeking out, Township style, saturated colors, thick cream #FBEBBA outline, one small bright highlight on the leaves. It rests directly on its own soft shadow. Plain solid magenta #FF00AA background.
```

## 8. fon-teplitsy
```
Wide horizontal interior backdrop of a greenhouse for a casual mobile game interface, seen straight on: a pale cream metal floor grating with soft sand shading, two thin teal irrigation pipes running along the top edge, the whole floor area left perfectly smooth and blank, reserved for growing trays pasted afterward. Township-style soft layered UI, no characters. Plain solid magenta #FF00AA background.
```

## 9. marker-gotovo-zelenyy
```
A rounded speech-bubble marker for a casual mobile city builder, seen straight on, the whole bubble filled bright glossy green with a darker green outline and a small pointed tail at the bottom center, a big bold white check mark inside, one small bright highlight in the upper left. The bubble floats above its own soft drop shadow. Plain solid magenta #FF00AA background.
```
