# Заказ иконок интерфейса — 06.09.2026 (ночь 3, играбельная колония)

Заказчик делает иконки сам (решение 06.09: мои попытки собирать их из примитивов выглядят плохо).
Я иконки больше не рисую: где иконки нет, строка панели идёт без картинки, маркер над зданием — текстом.
Стиль — как уже принятые `znachok-*.png` (сочный предмет, блик, без подставки), PNG с прозрачным фоном.

| # | Что | Где стоит | Размер (px) | Пожелание | Файл |
|---|-----|-----------|-------------|-----------|------|
| 1 | Медальон изотопов (валюта ускорений) | HUD, рядом с медальоном монеты | 128×128 предмет + рамка как у монеты/звезды | Тот же язык, что медальоны кредитов и опыта; сам знак изотопа уже есть (`znachok-izotopy.png`), нужна версия в медальоне | `medalon-izotopy.png` |
| 2 | «Ускорить» | Строка «Ускорить» в панелях грядки, фабрики, добычи, шаттла | 128×128 | Молния или часы со стрелкой вперёд; цвет — под зелёную кнопку | `znachok-uskorit.png` |
| 3 | «Готово» над зданием | Маркер над теплицей, фабрикой, площадкой льда, когда таймер истёк | 256×256 плашка-облачко | Галочка или предмет в пузыре, как в Township над домами; сейчас — текст «ГОТОВО» | `marker-gotovo.png` |
| 4 | «Растёт / готовится» над зданием | Тот же маркер, пока таймер идёт | 256×256 | Песочные часы или росток в пузыре | `marker-zhdat.png` |
| 5 | Шаттл | Заголовок панели шаттла, строка «Шаттл в рейсе» | 128×128 | Силуэт нашего шаттла в три четверти | `znachok-shattl.png` |
| 6 | Дрон | Заголовок панели дрона, строка заказа | 128×128 | Наш квадрокоптер сверху-сбоку | `znachok-dron.png` |
| 7 | Строй-модуль (награда шаттла) | Строка отсека «награда: модуль», вкладка «Модули» склада | 128×128 | Ящик-контейнер с маркировкой; типы модулей: каркас, панель, шлюз — можно один общий | `znachok-modul.png` |

Товары (лёд, вода, водоросли, кислород и остальные `znachok-*`) уже есть и подключены через экран склада — их не надо.

Куда класть: `mars-unity/Assets/UI/Ikonki/` (медальон и маркеры) и `mars-unity/Assets/UI/Ikonki/resursy/` (товары, имя файла = `good_id` из `Goods.cs`: `water_ice`, `water`, `algae`, `oxygen`, `regolith`, `soy`, `mushrooms`, `coffee`, `cotton`, `protein_bar`, `mushroom_soup`, `coffee_ration`, `fabric`, `jumpsuit`).
После выкладки — скажи, подключу за один проход.

## Промпты для Gemini (английский, по реестру приёмов `loop/promty/REESTR.md`)

Общее: холст 1:1, 2K. Фон — сплошной пурпур `#FF00AA` для вырезки (у маркеров и медальона тело тёмное или тёплое, с пурпуром не сливается). Слово «icon» не используется (рисует постамент), отрицаний нет — вместо них утверждение, занимающее то же место. Для медальона приложить референс монеты заказчика и попросить «same family».

### 1. medalon-izotopy — медальон изотопов (HUD)
```
Round glossy currency medallion for a mobile game HUD, seen face-on, same family as the attached gold coin medallion: thick rounded rim with a soft bevel, lacquered dome surface, one small bright highlight in the upper left. In the center a stylized isotope symbol: a teal-glowing sphere with two thin orbit rings, a small cream glow around it. Rim colors warm cream and sand, center deep teal, outline in soft cream #FBEBBA about 2 px. The medallion rests directly on its own soft shadow. Plain solid magenta #FF00AA background.
```

### 2. znachok-uskorit — «Ускорить»
```
A single chunky cartoon lightning bolt, bright golden yellow with a warm orange gradient toward the bottom, thick cream #FBEBBA outline, one small bright highlight near the top, casual mobile game style like Township. The bolt leans slightly to the right and rests directly on its own soft shadow. Plain solid magenta #FF00AA background.
```

### 3. marker-gotovo — маркер «готово» над зданием
```
A rounded speech-bubble marker for a casual mobile city builder, seen face-on, cream-white body with a soft sand gradient, thick cream #FBEBBA outline, small pointed tail at the bottom center, glossy with one small bright highlight. Inside the bubble a big bold green check mark with a darker green outline. The bubble floats above its own soft drop shadow. Plain solid magenta #FF00AA background.
```

### 4. marker-zhdat — маркер «ждать»
```
A rounded speech-bubble marker for a casual mobile city builder, seen face-on, cream-white body with a soft sand gradient, thick cream #FBEBBA outline, small pointed tail at the bottom center, glossy with one small bright highlight. Inside the bubble a cute golden hourglass with sand-colored sand and a teal frame. The bubble floats above its own soft drop shadow. Plain solid magenta #FF00AA background.
```

### 5. znachok-shattl — шаттл
```
A small chunky cartoon cargo shuttle, three-quarter view from slightly above, short stubby wings, rounded cream-white hull with muted terracotta stripes, five round engine bells at the back, tall tail fin, casual mobile game style like Township, saturated colors, thick cream #FBEBBA outline, one small bright highlight on the nose. It rests directly on its own soft shadow. Plain solid magenta #FF00AA background.
```

### 6. znachok-dron — дрон-курьер
```
A small chunky cartoon delivery quadcopter, three-quarter view from slightly above, rounded sage-teal body with a cream belly, four short arms with bright rotor discs, a little cargo box hanging under the body, casual mobile game style like Township, saturated colors, thick cream #FBEBBA outline, one small bright highlight on the top. It rests directly on its own soft shadow. Plain solid magenta #FF00AA background.
```

### 7. znachok-modul — строй-модуль (награда шаттла)
```
A chunky cartoon building-kit crate, three-quarter view, sand-cream box with rounded corners and a teal lid, a big simple cream hexagon emblem printed on the front face, casual mobile game style like Township, saturated colors, thick cream #FBEBBA outline, one small bright highlight on the lid edge. It rests directly on its own soft shadow. Plain solid magenta #FF00AA background.
```
