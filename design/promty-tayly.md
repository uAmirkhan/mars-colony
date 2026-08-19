# Тайлы поверхности: промпты генерации

Текстуры земли, которыми красится ландшафт. Сейчас в деле три:
`mars-ground.png` (песок, вся карта), `mars-ice.png` (ледяной биом),
`mars-rock.png` (дно карьера). Плюс четвёртый, металл, пока не подключён.

---

## Две вещи, которые надо понять до генерации

### 1. Не просить бесшовность у генератора

На слова «seamless», «tileable», «repeating pattern» модели отвечают **зеркальной
симметрией**: сшивают картинку саму с собой отражением. Внутри плитки от этого
появляются ромбы и калейдоскоп, и они видны даже без повтора.

Именно этот дефект три витка ловили у нас на притенённой стороне дюны, пока не
выяснилось, что он зашит в саму текстуру, а не в тайлинг.

**Бесшовность делаем мы, кольцевой вклейкой краёв, без единого отражения.**
Запекатель: `design/tools/bake-ground-tex.py`. От генератора нужен просто ровный
кусок поверхности, снятый сверху.

### 2. Крупные пятна на тайле — враг

Замер: крупные детали исходника переживают запекание и читаются повтором при
замощении. Чем однороднее тайл, тем незаметнее, что он повторяется.

**База обязана быть почти плоской заливкой.** Вариацию в сцене несут слои
поверх неё: макрослой цвета без повтора на всю карту и слой тональных пятен.
Плоский тайл здесь не потеря качества, а требование конвейера.

Поэтому в промпте прямо просим **мелкое ровное зерно без крупных пятен**.

---

## Общий блок

Одинаков для всех четырёх поверхностей. Меняется только первая строка.

```
RENDER. A flat top-down photograph of a patch of ground, shot straight down from directly above, orthographic, no perspective, no horizon, no sky, no objects, no debris, no footprints, no tracks. The frame is filled edge to edge with the surface itself and nothing else.

LIGHT. Completely flat even lighting, as under a bright overcast sky. No directional sunlight, no cast shadows, no highlights, no dark corners, no vignette, no gradient across the frame. Every part of the frame is lit identically — the texture will be lit by the game engine later, so any baked lighting is a defect.

GRAIN. Fine, even, uniform grain across the whole frame. No large blotches, no big stains, no dominant features, no single element that would catch the eye. If any spot is noticeably lighter, darker or more contrasty than the rest, it will read as a repeating stamp when the texture is tiled across the map.

FRAME. Square 1:1. The surface fills the whole frame with no border, no edge, no frame, no rounded corners.

FORBIDDEN. No seams, no tiling pattern, no mirrored symmetry, no kaleidoscope, no radial or diagonal repeats, no grid. No text, no watermark, no logo. No plants, no twigs, no sticks, no roots, no organic matter of any kind, no rocks sticking out, no equipment, no shadows of anything outside the frame.
```

---

## 1. Марсианский песок — база всей карты

Самый важный из четырёх: им покрыто больше половины кадра.

```
A patch of fine dry Martian regolith: warm ochre sand with a faint scattering of tiny pebbles, gently uneven surface with shallow wind ripples, matte and dusty.
```

**Целевой средний цвет:** `#EC9A6D`, то есть примерно (0.93, 0.60, 0.43).

Это число не эстетическое, а техническое: цвет земли в кадре есть произведение
текстуры на тон материала, а тон настраивался замерами много витков. Если
средний цвет плитки уедет, уедет и вся земля. Запекатель приводит средний цвет к
цели сам, но чем ближе исходник, тем меньше он его искажает.

## 2. Лёд — ледяной биом

```
A patch of pale glacial ice seen from above: a cracked frozen surface of flat crystalline plates fitted together, thin darker fracture lines between them, a dry frosted matte finish.
```

**Целевой средний цвет:** `#C9D6D2`, примерно (0.79, 0.84, 0.82).

Бледный холодный нейтрал, **не бирюза**. Насыщенный лёд читается наклейкой,
которую наклеили на карту — за это судьи ругали биом четыре витка подряд. Холод
даёт разница с охрой вокруг, а не собственная яркость.

Трещины и сколы — то, чем биом отличается языком формы: у колонии всё
скруглённое, у льда гранёное и колотое.

## 3. Камень — дно карьера

```
A patch of dry crushed rock: uniformly sized angular chips of pale stone packed edge to edge, every fragment roughly the same size as every other one. The stone is sun-bleached warm grey-tan, chalky and matte, and the whole field is coated in fine pale dust that also fills the gaps between the fragments.

VALUE. The whole field is light. It sits closer in brightness to dry pale sand than to dark wet stone. No fragment is near-black, no deep shadow pockets between fragments, no dark patches anywhere. The gaps between chips are filled with pale dust, not with darkness.

UNIFORMITY. One fragment size and one material, edge to edge. No corner where the stones are larger, no band or streak where they are finer, no area of bare soil between them, no mixture of gravel sizes.
```

> **Редакция 2.** Первая редакция дала `#986940` вместо `#B38A6B` (темнее цели
> на десятую по всем каналам) и контраст 0.167 против 0.031 у песка. Причины
> названы буквально в промпте: `mixed small fragments` заказал разнокалиберность,
> `freshly broken stone without dust cover` — тёмный насыщенный скол. Плюс в
> кадр попала веточка, поэтому в общий блок добавлен запрет на органику.

**Целевой средний цвет:** `#B38A6B`, примерно (0.70, 0.52, 0.42).

Светлее настоящей породы намеренно: тон дна карьера в сборщике подобран под
светлую плитку, и тёмный камень уведёт яму в черноту — ровно та «чёрная яма»,
которую судьи ловили на шестом витке.

Зерно у камня должно быть **заметно крупнее и жёстче**, чем у песка: по этому
контрасту глаз и читает, что дно карьера сделано из другого материала.

## 4. Технопокрытие — дороги и площадки

Пока не подключено, но пригодится: свод города требует дорогу отдельным
материалом, а не подкрашенным песком.

> **Генератором не делаем.** Рёбра — строгая периодика в одном направлении,
> это чистая математика. Печём числами: `design/tools/pech-metall.py`, период
> ребра делит сторону текстуры нацело, поэтому бесшовность выходит по
> построению, а средний цвет ставится точно в цель. Замер напечённого:
> `#C4A882` в цель, пятна 0.050, швы 0.0015.
>
> Генератор на эту задачу давал плитку с тёмными швами и сменой направления
> рёбер через квадрат, а на исправленный промпт ответил отказом.

Промпт оставлен на случай, если понадобится вариант побогаче:

```
A patch of ribbed metal decking: one single continuous sheet of painted metal with shallow narrow anti-slip ribs pressed into it. Every rib runs parallel to every other rib, straight from one edge of the frame to the opposite edge, in one single direction across the entire image. The ribs are fine and closely spaced, low and rounded, barely raised above the surface.

COLOUR. Warm pale sand beige, the colour of dry desert dust on painted metal. Not grey, not green, not blue, not steel, not black. Matte, dry, faintly dusty.

ONE SHEET. This is a single unbroken surface, not tiles and not slabs. No panel joints, no seams, no grid lines, no dark gaps dividing the surface into squares or rectangles. Nothing on the surface repeats in blocks, and no rib ever changes direction.
```

**Целевой средний цвет:** `#C4A882`, примерно (0.77, 0.66, 0.51).

Рёбра должны идти **строго в одном направлении** и быть мелкими: крупный рисунок
на дороге при замощении читается повтором сильнее, чем на любой другой
поверхности, потому что дорога длинная и узкая.

---

## Приёмка тайла

1. **Нет крупных пятен.** Прищурьтесь на картинку: если выделяется хоть одно
   место, при замощении оно станет штампом.
2. **Нет симметрии.** Ни зеркальной, ни радиальной, ни диагональной.
3. **Нет запечённого света.** Ни одна сторона кадра не темнее другой, теней нет.
4. **Нет посторонних предметов.** Ни камней, ни следов, ни травинок.
5. **Средний цвет близок к целевому.** Проверяется размытием картинки до одного
   пикселя.

---

## Яркость исходника: требование к промпту, а не шаг обработки

Найдено на камне `q88zmh`. Исходник ровный — пятна 0.097. После запекания
0.233, вдвое хуже прежнего. **Пятна не пришли из картинки, их сделал
запекатель:** картинка была на 16 пунктов темнее цели, он вытянул её
множителем 1.29 и растянул вместе со средним весь разброс.

Отсюда правило, и оно про промпт, а не про обработку: **тайл должен
приходить из генератора уже светлым, близко к целевому цвету.** Тогда
запекателю нечего тянуть и пятен он не сделает.

Просить в промпте прямо: светлый, не тёмный, но при этом тёплый. Обе крайности
уже проверены и обе плохи — `rusty ochre` даёт тёмный и перенасыщенный,
`sun-bleached chalky` даёт светлый, но серый. Работает привязка к вещи, которая
светлая и тёплая одновременно: выгоревший терракотовый горшок, сухая глина,
саман.

Если исходник всё же пришёл далеко от цели, а перегенерить нельзя — есть
запасной ход:

```
python design/tools/podgotovit-tayl.py <вход.jpg> design/cut/tayly-poverkhnosti-4sht-chistye/tile_rock.png 0.700 0.520 0.420
```

Инструмент делает два движения. Снимает крупную волну (медленный перепад
«угол светлее, полоса темнее» — он и есть штамп при замощении). И поднимает к
цели **гаммой, а не множителем**: множитель тянет светлые и тёмные одинаково и
раздувает разброс, гамма жмёт светлые сильнее и разброс сохраняет.

На камне это дало пятна после запекания 0.233 -> 0.155, контраст 0.0756 ->
0.0511, зерно цело.

## Что делать после генерации

Положить исходники в `design/cut/tayly-poverkhnosti-4sht-chistye/` под именами
`tile_sand.png`, `tile_ice.png`, `tile_rock.png`, `tile_metal.png` и прогнать:

```
blender --background --python design/tools/bake-ground-tex.py -- design/cut <куда>
```

Запекатель сам сделает бесшовность кольцевой вклейкой, подожмёт крупные пятна,
вернёт зерно многооктавным шумом и приведёт средний цвет к целевому. На выходе
готовые `mars-ground.png`, `mars-ice.png`, `mars-rock.png`.
