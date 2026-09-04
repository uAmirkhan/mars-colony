# Промпты пилота — панель-окно и кнопка действия

Источник требований: `pasport-elementov-interfeysa.md` (элементы 1 и 2), словарь
провалов — `REESTR.md`. Пилот — два элемента, каждый в палитре А (тёплая) и Б
(холодная) = 4 промпта под бесплатный генератор + 4 те же по смыслу под
Gemini на случай, если бесплатный не потянет.

## Какой генератор стоит за `image-gen` (`free`)

Прочитан `~/.claude/skills/image-gen/SKILL.md` и `gen.py`. Провайдер `free` —
**Pollinations** (`image.pollinations.ai/prompt`), обычный HTTP GET с
текстом промпта в URL. Какая именно диффузионная модель за ним стоит, в
скилле не написано — не выдумываю бренд.

Ограничения, важные для этой партии:

- **Фиксированный список соотношений сторон** (`SIZES` в `gen.py`), нет
  произвольного пикселя: `1:1 2:3 3:2 3:4 4:3 4:5 5:4 9:16 16:9 21:9`.
  Ближайшее к нужным паспорту ~2:1 (панель) и 5:1 (кнопка) — **21:9**
  (1536×656, реальное соотношение 2,33:1). Паспорт сам разрешает такое
  расхождение («любой поддерживаемый генератором размер этой пропорции...
  или обрезать до этой пропорции после») — беру 21:9 для обоих элементов и
  закладываю в промпт запас полей для последующей обрезки, особенно для
  кнопки (2,33:1 → 5:1 обрезкой по высоте, не по ширине).
- **Реальный сохранённый файл может быть уменьшен до ~768 px по большей
  стороне** независимо от запрошенного aspect — так прямо написано в
  SKILL.md. На пропорцию не влияет, только на разрешение.
- **Только текст-в-картинку, референс недоступен** (`--ref` только у
  Gemini). Единство стиля между А/Б держится буквально одинаковой
  структурой промпта, меняются только цветовые слова.
- Каждый вызов `free_call` — самостоятельный HTTP-запрос без общей сессии
  с соседними вызовами (в отличие от диалогового API) — насколько я вижу
  по коду `gen.py`, между вызовами нет состояния. Закон «соседние промпты
  тянутся к общей форме» описан для сессий с историей (диалог); здесь я не
  проверял это на практике и не выдаю за факт — если увижу смешение форм
  между независимыми вызовами, впишу в REESTR как новое наблюдение.

Команда:
```
python ~/.claude/skills/image-gen/gen.py "PROMPT" -o out/имя.png --aspect 21:9
```

---

## 1. Панель-окно с лентой-заголовком

### 1А (тёплая) — free

**Модель:** `image-gen`, provider `free` (Pollinations).
**Размер:** `--aspect 21:9` → 1536×656 px (ближайшее к паспортным ~2:1;
файл может лечь уменьшенным до ~768 px по большей стороне — это ограничение
самого провайдера, не промпта).

**Промпт:**
```
Game UI window panel, wide horizontal shape with large rounded corners on both top corners, front view showing the top edge in full width. A separate raised header ribbon sits centered above the top edge, overlapping it and reaching past the panel's sides, clamped by two riveted brass brackets at its ends. Interface layers on the rim: a bright cream rim light along the top inner edge, a glossy peach-tan frame band beneath it, a thin dark warm-brown contact line at the outer edge. The ribbon face is a smooth warm cream lacquered surface left perfectly blank, reserved for a title. The panel body below is a flat warm cream-to-tan vertical gradient, glossy ceramic finish, left perfectly smooth and empty for content pasted later. Clean flat vector game-UI render, even studio lighting, solid uniform bright magenta-pink background #FF00AA, symmetrical, wide flat crop.
```

**Что проверить числом:**
- Фон: угловые патчи 20×20 px по четырём углам — ≥95% пикселей в пределах
  ΔE ≤10 от `#FF00AA` (сплошной фон, не размыт в градиент).
- Занятость кадра элементом: панель+лента по ширине ≥85% ширины холста; по
  высоте (верх ленты до низа кадра) 55-80% высоты холста — [решение
  промпт-инженера, в паспорте для элементов 1-2 процент заполнения не
  зафиксирован, только для круглой кнопки меню].
- Пустой центр: в центральном прямоугольнике тела панели (средние 60%
  ширины × средние 50% высоты тела, без рамки и ленты) стандартное
  отклонение канала по RGB <10 — подтверждает ровный градиент без текста и
  декора.
- Текст/цифры: 0 символов при прогоне OCR по вырезанному центру и по ленте.
- Лента отделена от тела: на границе ленты и верхней кромки панели должен
  читаться разрыв (скобы + промежуток), а не сплошная заливка одним пятном
  — проверяется глазами по паспортной ловушке (лента внутри = провал).

### 1Б (холодная) — free

**Модель/размер:** те же, что 1А.

**Промпт:**
```
Game UI window panel, wide horizontal shape with large rounded corners on both top corners, front view showing the top edge in full width. A separate raised header ribbon sits centered above the top edge, overlapping it and reaching past the panel's sides, clamped by two riveted brass brackets at its ends, with a thin glowing cyan hairline tracing the very top edge of the ribbon. Interface layers on the rim: a bright cream rim light along the top inner edge, a glossy peach-tan frame band beneath it, a thin dark warm-brown contact line at the outer edge. The ribbon face is a smooth warm cream lacquered surface left perfectly blank, reserved for a title. The panel body below is a flat warm cream-to-tan vertical gradient, glossy ceramic finish, left perfectly smooth and empty for content pasted later. Clean flat vector game-UI render, even studio lighting, solid uniform bright acid-green background #7CFF00, symmetrical, wide flat crop.
```

Разница с 1А — ровно одна фраза (`cyan hairline`) и цвет фона: паспорт
прямо требует, чтобы тело/рант/рамка/лента панели остались тёплыми и в
холодной палитре («единственный тёплый остров»), поэтому ядро не меняю.

**Что проверить числом:** то же, что у 1А, плюс — наличие тонкой цветной
линии `#6FE6FF`-подобного тона именно по верхней кромке ленты (не по всему
контуру панели): выборка полосы 2-3 px вдоль верхнего края ленты должна
показывать явный синевато-голубой тон, отличный от кремового тела ленты.

---

## 2. Кнопка действия

### 2А (тёплая, полная пилюля) — free

**Модель:** `image-gen`, provider `free`.
**Размер:** `--aspect 21:9` → 1536×656 px, с последующей обрезкой по
высоте до ~1536×307 (соотношение 5:1) — холст даёт запас сверху/снизу
специально под эту обрезку, паспорт разрешает такой обход явно.

**Промпт:**
```
Game UI action button, a long horizontal stadium-shaped pill with fully rounded ends, seen face-on. Glossy interface layers from top to bottom: a bright lime-green dome highlight along the very top edge, a smooth continuous green gradient body from vivid green at the top to a deeper green lower down, a darker green bottom rim, and a soft blurred contact shadow pooling directly beneath the pill on the backdrop. The button face is left perfectly smooth and blank, reserved for a label pasted afterward. Clean flat vector game-UI render, even studio lighting, solid uniform bright magenta-pink background #FF00AA, centered horizontally with generous empty margin above and below for cropping, wide flat crop.
```

**Что проверить числом:**
- Фон: те же угловые патчи, ≥95% в ΔE ≤10 от `#FF00AA`.
- Занятость кадра до обрезки: пилюля по ширине ≥80% ширины холста
  (торцы почти впритык к краям, под 9-slice); после обрезки до 5:1 пилюля
  должна занимать по высоте ≥85% кадра (запас на тень).
- Форма торцов: радиус скругления на обоих концах ≈ половине высоты
  пилюли (полная пилюля, не скруглённый прямоугольник) — мерить по
  профилю верхней/нижней кромки на 5% и 95% ширины.
- Пустой центр: в средней трети ширины кнопки, полная высота, стандартное
  отклонение канала <10 (ровный вертикальный градиент, без надписи).
- Текст: 0 символов OCR.

### 2Б (холодная, скруглённый прямоугольник) — free

**Модель/размер:** те же, что 2А.

**Промпт:**
```
Game UI action button, a long horizontal rounded rectangle with moderate corner radius, clearly not a full pill, seen face-on. Glossy interface layers from top to bottom: a thin bright bevel edge along the very top, a green gradient band, a glossy white glass-like stripe overlay across the upper third, a second deeper green gradient band below it, a darker transition band, and a dark green bottom rim, with a soft blurred contact shadow pooling directly beneath the button on the backdrop. The button face is left perfectly smooth and blank, reserved for a label pasted afterward. Clean flat vector game-UI render, even studio lighting, solid uniform bright acid-green background #7CFF00, centered horizontally with generous empty margin above and below for cropping, wide flat crop.
```

**Риск, который называю прямо, не проверив на генерации:** кнопка и в А, и
в Б остаётся зелёной (паспорт: элемент 2 не участвует в тёпло-холодном
контрасте по цвету, только по форме торцов и слою глянца). У Б это значит
зелёная кнопка на кислотно-салатовом фоне `#7CFF00` — обе зоны в
зелёно-жёлтом диапазоне спектра. Оттенки разные (фон холодный чистый лайм,
кнопка — более насыщенный сочный зелёный без жёлтого крена), но контур
может размыться сильнее, чем у панели на пурпуре. Не подменяю фон тайно
(это назначение из паспорта, общее правило 1), только фиксирую: если
вырезка 2Б покажет смазанный/дырявый контур, первым делом мерить ΔE между
цветом фона и цветом крайних пикселей кнопки — если <25, нужен либо другой
фон именно для этого промпта, либо обводка кнопки контрастным контуром,
решение за арт-директором, не за мной.

**Что проверить числом:** то же, что у 2А, плюс:
- Форма торцов: радиус ≈22% высоты кнопки (не половина высоты — иначе
  палитры перепутаны местами, паспорт п.5 чек-листа).
- Наличие глянцевой белой полосы в верхней трети: выборка полосы на 12-34%
  высоты должна показывать повышенную яркость (V) и пониженную
  насыщенность относительно соседних зелёных зон — если полосы нет
  (слилась с общим градиентом), фиксировать как повтор находки из паспорта
  «глянец не подтверждён на маленьком размере» и не считать браком, если
  остальное совпало.
- Контур на фоне: ΔE между углом фона и кромкой кнопки ≥25 (см. риск
  выше) — если меньше, засчитывать как повод для отдельной генерации с
  тонкой тёмной обводкой по контуру, не как провал промпта.

---

## Второй комплект — те же 4, формулировки под Gemini

Для случая, если заказчик генерирует сам через Gemini (`nano-banana` /
`gemini-3.1-flash-image` и т.п.). Смысл и словарь те же самые: законы
проекта (без отрицаний, без имени цвета в теле, интерфейсный, не товарный,
словарь) не зависят от генератора и специально не меняю их между
комплектами. Различия — только там, где отличается сам API:

- Gemini принимает `--resolution 1K/2K/4K` в дополнение к aspect ratio —
  выше запас пикселей до обрезки, меньше потерь качества при подгонке под
  точные 2:1 / 5:1, чем у бесплатного генератора с потолком ~768 px.
- Не проверено на практике, какие именно значения aspect ratio принимает
  Gemini API у заказчика вне этого скилла (в самом `gen.py` список тот же
  `SIZES`, но заказчик может слать запрос иначе). Рекомендация та же, что
  и для free: просить **21:9**, генерировать с запасом полей, обрезать
  вручную до 2:1 (панель) и 5:1 (кнопка) после получения файла. Если у
  заказчика в интерфейсе Gemini есть точный произвольный размер холста —
  это стоит проверить одной генерацией, не гадать.

### 1А-gemini (панель, тёплая)

**Модель:** Gemini image (`gemini-3.1-flash-image` или лучше, по выбору
заказчика). **Размер:** `--aspect 21:9`, `--resolution 2K`, обрезать после
до ~2:1 с полей.

```
Game UI window panel, wide horizontal shape with large rounded corners on both top corners, front view showing the top edge in full width. A separate raised header ribbon sits centered above the top edge, overlapping it and reaching past the panel's sides, clamped by two riveted brass brackets at its ends. Interface layers on the rim: a bright cream rim light along the top inner edge, a glossy peach-tan frame band beneath it, a thin dark warm-brown contact line at the outer edge. The ribbon face is a smooth warm cream lacquered surface left perfectly blank, reserved for a title. The panel body below is a flat warm cream-to-tan vertical gradient, glossy ceramic finish, left perfectly smooth and empty for content pasted later. Clean flat vector game-UI render, even studio lighting, solid uniform bright magenta-pink background #FF00AA, symmetrical, wide flat crop.
```

**Проверка числом:** идентична 1А-free (фон ΔE, занятость кадра, пустой
центр, OCR=0).

### 1Б-gemini (панель, холодная)

```
Game UI window panel, wide horizontal shape with large rounded corners on both top corners, front view showing the top edge in full width. A separate raised header ribbon sits centered above the top edge, overlapping it and reaching past the panel's sides, clamped by two riveted brass brackets at its ends, with a thin glowing cyan hairline tracing the very top edge of the ribbon. Interface layers on the rim: a bright cream rim light along the top inner edge, a glossy peach-tan frame band beneath it, a thin dark warm-brown contact line at the outer edge. The ribbon face is a smooth warm cream lacquered surface left perfectly blank, reserved for a title. The panel body below is a flat warm cream-to-tan vertical gradient, glossy ceramic finish, left perfectly smooth and empty for content pasted later. Clean flat vector game-UI render, even studio lighting, solid uniform bright acid-green background #7CFF00, symmetrical, wide flat crop.
```

**Проверка числом:** идентична 1Б-free (плюс проверка цвета волоска на
кромке ленты).

### 2А-gemini (кнопка, полная пилюля)

```
Game UI action button, a long horizontal stadium-shaped pill with fully rounded ends, seen face-on. Glossy interface layers from top to bottom: a bright lime-green dome highlight along the very top edge, a smooth continuous green gradient body from vivid green at the top to a deeper green lower down, a darker green bottom rim, and a soft blurred contact shadow pooling directly beneath the pill on the backdrop. The button face is left perfectly smooth and blank, reserved for a label pasted afterward. Clean flat vector game-UI render, even studio lighting, solid uniform bright magenta-pink background #FF00AA, centered horizontally with generous empty margin above and below for cropping, wide flat crop.
```

**Проверка числом:** идентична 2А-free.

### 2Б-gemini (кнопка, скруглённый прямоугольник)

```
Game UI action button, a long horizontal rounded rectangle with moderate corner radius, clearly not a full pill, seen face-on. Glossy interface layers from top to bottom: a thin bright bevel edge along the very top, a green gradient band, a glossy white glass-like stripe overlay across the upper third, a second deeper green gradient band below it, a darker transition band, and a dark green bottom rim, with a soft blurred contact shadow pooling directly beneath the button on the backdrop. The button face is left perfectly smooth and blank, reserved for a label pasted afterward. Clean flat vector game-UI render, even studio lighting, solid uniform bright acid-green background #7CFF00, centered horizontally with generous empty margin above and below for cropping, wide flat crop.
```

**Проверка числом:** идентична 2Б-free, включая риск ΔE фон/кромка.

---

## Порядок прогона

Отдавать не всей партией из 8 разом. Первая проверка — 1А-free и 2Б-free
(панель и кнопка, разные элементы, разные палитры — несмежные по форме и
по цвету, минимальный риск усреднения). По их результату решать, нужен ли
Gemini-комплект вообще, и достраивать 1Б/2А следующим заходом.
