# Заказ на партию 3

Для Claude Code на машине с RTX 4090. Всё, что нужно знать, — здесь; в
`GID-GENERACII.md` лежат ворота приёмки, они не менялись.

---

## 1. Что было и что из этого вышло

Партия 2 пришла целиком в контракте: **18 454 - 19 986 треугольников**, один
меш, один материал, одна текстура 2048, ориентация по картинке. Числа
безупречны, конвертация в FBX ничего не потеряла. Двадцать моделей стоят в
сцене прямо сейчас.

Но глазами приняты не все, и разбор дал находку, ради которой написан этот
раздел.

### Параметры были одинаковые. Результат — нет

Вся партия сделана одним прогоном: один seed, одна сетка, одно целевое число.
При этом:

| вышло отлично | вышло картофелиной |
|---|---|
| четыре космонавта | `kamen-gladkiy` |
| `modul-tonnelnyy` | `kamen-granenyy` |
| `tsisterna` | `kamni-para` |
| `angar-s-panelyami` | половина ледяных глыб |

**Значит дело не в настройках, а в предмете.** Хорошо вышло всё рукотворное с
отдельными частями: иллюминаторы, баки, вентили, шлем, ранец. Плохо — всё
природное, опознаваемое **гранями**.

Причина: пересборка поверхности идёт равномерным шагом. Отдельные части она
сохраняет, они крупные. Грани стирает, они высокочастотные. Число треугольников
тут ни при чём — мы подняли его с 2 400 до 19 700, и камни остались теми же.

### Что из этого следует для тебя

**Камни и ледяные глыбы больше не генерируем.** Мы считаем их процедурно:
гранёный валун — это случайный выпуклый многогранник, и такой путь даёт
настоящую огранку при любом весе. Не трать на них прогоны.

**Всё рукотворное генерируем теми же параметрами, что партию 2.** Они работают,
менять их незачем.

```
decimation_target   20000
remesh              True
pipeline_type       1024
разрешение текстуры 2048
seed                фиксированный
```

---

## 2. Список А: доделать отложенное из партии 2

Шесть объектов из первоначальных двадцати семи так и не пришли. Картинки для
них уже есть, в `design/etalons/to generate/`.

| объект | картинка | зачем |
|---|---|---|
| `ballony-na-poddone` | `5fufag5fufag5fuf` | **самый нужный.** Владелец забраковал нынешние прямым текстом |
| `truba-na-kozlakh` | `re8c2dre8c2dre8c` | закрывает закон про трубы: сегмент с двумя стыкующимися концами |
| `yashchiki-shtabel` | `85bzmp85bzmp85bz` | предметный слой, сейчас его несут одни цистерны |
| `kadka-s-zelenyu` | `8j0jus8j0jus8j0j` | зелень у жилья, ход к плотности ядра |
| `antenna-tarelka` | `gnaywegnaywegnay` | в партии 1 чаша схлопнулась в диск; на 20 000 должна выжить |
| `solnechnaya-batareya` | `oyaif6oyaif6oyai` | в партии 1 вышла плоским пятном без силуэта |

Нынешние версии трёх из них (`ballony`, `truba`, `kadka`) стоят в сцене из
партии 1 и весят 2 376 - 2 990 треугольников. Это и есть причина, по которой
они выглядят плохо: они рукотворные, им параметры партии 2 помогут.

---

## 3. Список Б: пересоздать забракованное глазами

Числа у них в контракте, а формы нет. Диагноз по каждому — чтобы ты знал, что
именно смотреть на приёмке.

| объект | что не так сейчас | на что смотреть |
|---|---|---|
| `led-skvazhina` | тёмно-красное пятно в ледяной обойме, механизм не читается | вентиль и трубная головка обязаны быть отдельными частями |
| `led-valun-v-shube` | тёмно-красный диск с ледяной бахромой | валун и шуба должны различаться формой, не только цветом |
| `led-glyba-kristallicheskaya` | серо-синяя, читается камнем, а не льдом | прозрачность и холодный тон; сейчас темнее всей партии |
| `led-greben` | тонкая кривая палка | гребень это ряд шипов, силуэт должен читаться зубчатым |
| `kupol-grib` | бирюзовый диск на ножке, ни окон, ни двери | шляпка, цоколь, дверь и окна — три отдельные части |

Ледяные — пограничный случай: у них есть и грани (плохо генерируются), и
отдельные части (хорошо). Пробуй; не выйдет за четыре попытки — откладывай, мы
посчитаем их процедурно, как камни.

---

## 4. Список В: новые объекты, которых не было

Картинок для них нет. **Их сделает владелец в Gemini** и положит туда же, в
`design/etalons/to generate/`. Промпты — в разделе 6 ниже.

| объект | зачем | сколько нужно в сцене |
|---|---|---|
| **колёсный марсоход** | заменяет удалённый `dekor-08`, был самым массовым | 4-6 |
| **дрон-курьер** | движение в кадре, признак жизни | 2-3 |
| **флажок на постаменте** | мелкий декор в промежутках ядра | 6-8 |
| **жилой купол, 2-3 разных** | **самый важный.** В центре кадра сейчас нет объекта-героя | 1 крупный + 2 поменьше |

Купол-герой — приоритет номер один во всём заказе. Замер по кольцам показал,
что у сцены нет середины: плотность растёт наружу, а самый крупный силуэт
стоит в двадцати семи метрах от центра.

---

## 5. Приёмка

Не менялась, `GID-GENERACII.md` раздел 7. Коротко:

```
треугольники     15 000 - 28 000
меш / материал / текстура   1 / 1 / 1
текстура         2048
плита            отсутствует (мерить низ к МАКСИМАЛЬНОМУ сечению по силуэту)
ориентация       по картинке
опознание вслепую  уменьшить рендер до ногтя и назвать объект
```

Главная проверка — **опознание вслепую**. Она ловит больше, чем все числа
вместе: партия 2 прошла все числовые ворота и всё равно дала картофелины.

Дополнительно к прежнему: **у рукотворного объекта отдельные части обязаны
оставаться отдельными.** Иллюминатор, вентиль, бак, ручка — если они слиплись
в один объём, модель в брак, какими бы ни были числа.

## 6. Куда класть

```
design/vygruzka-trellis-3/
    *.glb
    zamery.txt        строка на объект
    rendery/          три ракурса плюс ноготь
    zhurnal.md        по строке на попытку: что крутил, что вышло
```

Отправляй по ходу, не копи до конца.

---

## 7. Промпты картинок для новых объектов

Эти четыре генерирует владелец в Gemini. Промпты собраны по тем же правилам,
что дали чистые картинки прошлым партиям: один предмет, белая пустота под ним,
никакой земли, отдельные читаемые части.

### Марсоход

```
Stylised 3D game asset, single object floating in empty white space, product photography, no ground and no shadow beneath. A six-wheeled Mars rover: chunky ribbed wheels on visible suspension arms, a boxy instrument body with a hinged lid, a small mast with a camera head, and a folded robotic arm along one side. Cream and warm grey painted panels with orange accent stripes, teal glass on the camera head. Chunky rounded forms, thick parts, nothing thinner than a finger. Soft even studio light. Square 1:1.
```

### Дрон-курьер

```
Stylised 3D game asset, single object floating in empty white space, product photography, no ground and no shadow beneath. A small delivery drone: a smooth rounded hull with four ducted rotor rings on short arms, a teal camera lens at the front, a cargo box clamped underneath, and stubby landing skids. Cream body with orange accent bands and a charcoal underside. Chunky rounded forms, thick parts, nothing thinner than a finger. Soft even studio light. Square 1:1.
```

### Флажок на постаменте

```
Stylised 3D game asset, single object floating in empty white space, product photography, no ground and no shadow beneath. A colony marker post: a short thick mast on a chunky hexagonal base plinth, a stiff triangular pennant near the top, a small solar panel and a signal lamp on the mast. Cream and warm grey with an orange pennant and a teal lamp. Chunky rounded forms, thick parts, nothing thinner than a finger. Soft even studio light. Square 1:1.
```

### Жилой купол-герой

Самый важный. Нужен крупный, чтобы держать центр кадра.

```
Stylised 3D game asset, single object floating in empty white space, product photography, no ground and no shadow beneath. A large habitation dome: a ribbed hemispherical shell on a low cylindrical base, a prominent airlock entrance with a rounded door and a lamp above it, a row of round porthole windows around the base, and two small service tanks against one side. Cream ribs over a warm grey shell, teal glass in the portholes, orange trim on the airlock. Chunky rounded forms, thick parts, nothing thinner than a finger. Soft even studio light. Square 1:1.
```

И два поменьше, вариациями той же строки: заменить `A large habitation dome`
на `A medium habitation dome with a single porthole row and no service tanks` и
на `A small habitation pod, one airlock and two portholes, no base ring`.

**Обязательно** держать в каждом промпте `floating in empty white space` и
`no ground and no shadow beneath`: без этих слов генератор подкладывает под
объект плиту, и она потом переходит в модель. Проверено двадцатью объектами.
