# Направление 4: реальные данные рельефа Марса

Дата разведки: 2026-08-09.

Цель: понять, можно ли бесплатно достать настоящие карты высот Марса, превратить их в меш для витрины колонии, и годятся ли они на масштабе "сотня метров" (площадка под небольшую базу).

Короткий вывод вперед отчета: MOLA (глобальный рельеф) для масштаба в сотню метров не годится числом ниже. HiRISE DTM годится и дает запас детализации с большим запасом. CTX - промежуточный вариант для контекста вокруг площадки. Все три источника официально public domain, коммерческое использование разрешено без покупки лицензии.

---

## 1. Лицензия - дословно

### USGS (в т.ч. Astrogeology Science Center, откуда качается MOLA)

Со страницы официальной политики USGS про копирайт:

> "USGS-authored or produced data and information are considered to be in the U.S. Public Domain."

И там же про использование производных материалов:

> "All public domain imagery may be used, shared, transferred, or redistributed without restriction."

Единственная просьба - указать источник (не юридическое требование, а принятая практика):

> "there should be acknowledgement of the image source within any derived maps, products, or publications"

Источник: [Copyrights and Credits — USGS](https://www.usgs.gov/information-policies-and-instructions/copyrights-and-credits)

### JPL (управляет частью марсианских миссий, в т.ч. MRO/HiRISE, CTX)

> "images and video on JPL public web sites (public sites ending with a jpl.nasa.gov address) may be used for any purpose without prior permission"

Запрошенная кредитная строка:

> "the credit line should be 'Courtesy NASA/JPL-Caltech.'"

Ограничения касаются логотипов NASA/JPL и случаев, где на фото есть узнаваемый человек - к DEM рельефа планеты это не относится.

Источник: [JPL Image Use Policy](https://www.jpl.nasa.gov/jpl-image-use-policy/)

### HiRISE (University of Arizona) - конкретно про DTM, которые нужны нам

Дословно со страницы политики использования:

> "All of the images produced by HiRISE and accessible on this site are within the public domain: there are no restrictions on their usage by anyone in the public, including news or science organizations."

Запрошенный (не обязательный) credit line:

> "We do ask for a credit line where possible: Image: NASA/JPL/University of Arizona"

Источник: [HiRISE Image Usage Policy](https://www.uahirise.org/media/usage.php)

### NASA в целом (общая рамка, под которую попадают все миссии)

> "NASA content – images, audio, video, and media files used in the rendition of 3-dimensional models, such as texture maps and polygon data in any format – generally are not subject to copyright in the United States."

Про коммерческое использование - единственное реальное ограничение:

> "If the NASA material is to be used for commercial purposes, including advertisements, it must not explicitly or implicitly convey NASA's endorsement of commercial goods or services."

Источник: [NASA Images and Media Guidelines](https://www.nasa.gov/nasa-brand-center/images-and-media/)

**Вывод по лицензии:** для витрины в портфолио для геймдев-студии — данные MOLA/CTX/HiRISE использовать можно свободно, включая коммерческий контекст (портфолио, демонстрация студии). Единственное реальное правило — не заявлять и не намекать, что NASA/JPL/USGS одобряют или спонсируют проект. Указать источник в титрах ("Elevation data: NASA/JPL/University of Arizona, MOLA/HiRISE") — это не юридическое требование, а хороший тон и заодно тот самый аргумент "рельеф не выдуман" для защиты.

---

## 2. Разрешение и годится ли на масштаб "сотня метров"

Площадка под небольшую колонию — берем ориентир 100-300 м в поперечнике.

| Источник | Разрешение | Пикселей на площадку 100х100 м | Годится? |
|---|---|---|---|
| MOLA глобальный (Mars_MGS_MOLA_Shade_global_463m) | 463 м/пиксель (128 пикс/градус) | меньше 1 (площадка меньше одного пикселя) | **Нет.** Вся площадка колонии — это доля одного пикселя карты высот. Рельефа внутри участка просто нет в данных, будет плоское пятно одного значения. |
| MOLA+HRSC blended global 200m | 200 м/пиксель | меньше 1 | **Нет**, по той же причине — компромиссный продукт, но все еще в разы крупнее площадки. |
| HRSC (Mars Express) точечные DTM | ~50-100 м/пиксель | 1-2 | **Не годится для площадки**, но подходит для рельефа вокруг колонии в масштабе километров (склон кратера, долина). |
| CTX стереопары (DTM из Ames Stereo Pipeline) | ~20 м/пиксель (стандартный продукт USGS, напр. InSight landing site DTM) | ~5х5 | **Погранично.** Дает общий уклон, крупные формы (борт кратера, дюна), но никакой мелкой фактуры грунта — валунов, ряби, локальных ямок не будет. Годится как "подложка контекста" под колонию, не как сама площадка. |
| HiRISE DTM | 1-2 м/пиксель (сетка стандартно C=1.0 м, есть варианты 0.5 м и 0.25 м) | 50х50 до 400х400 | **Да.** Это единственный источник, где сама площадка в сотню метров содержит реальную детализацию рельефа — сотни точек высоты, а не единицы. |

Числовое обоснование почему MOLA отпадает: стандартный глобальный грид MOLA — 463 м/пиксель. Площадка колонии ~100-300 м. То есть вся площадка укладывается в 0.2-0.6 одного пикселя карты высот. Меш, построенный из MOLA на этом масштабе, физически не может показать ничего, кроме идеально плоской или слабо наклонной поверхности — данных с более высоким разрешением там просто не существует в этом продукте. MOLA хорош для рельефа планеты целиком или региона в сотни километров, не для одной посадочной площадки.

HiRISE, наоборот, снят с орбиты ~300 км с разрешением фотографий 25-50 см/пиксель, а сама DTM строится из стереопары с шагом сетки 1-2 м — то есть на площадку 100х100 м приходится порядка 2500-10000 точек высоты. Это с запасом достаточно для правдоподобного меша с камнями, кратерными валами, рябью грунта.

Источники:
- [Mars MGS MOLA Global Shaded Relief 463m — USGS Astrogeology](https://astrogeology.usgs.gov/search/map/Mars/GlobalSurveyor/MOLA/Mars_MGS_MOLA_Shade_global_463m)
- [Mars MGS MOLA - MEX HRSC Blended DEM Global 200m — USGS Astrogeology](https://astrogeology.usgs.gov/search/map/mars_mgs_mola_mex_hrsc_blended_dem_global_200m)
- [HiRISE — About Digital Terrain Models](https://www.uahirise.org/dtm/about.php)
- [MRO Context Camera (CTX) — Malin Space Science Systems](https://www.msss.com/mro/ctx/ctx_description.html)
- [CTX Digital Terrain Model of Mars InSight Landing Site — USGS Astrogeology](https://astrogeology.usgs.gov/search/map/Mars/InSight/landing_site/F02_036761_1828_F04_037262_1841_20m_DTM_destripe) (пример готового CTX DTM с разрешением 20 м/пиксель прямо в имени файла)

---

## 3. Точный маршрут: откуда качать и чем конвертировать

### Шаг 1 — найти подходящий HiRISE DTM

Идти на каталог DTM: [uahirise.org/dtm](https://www.uahirise.org/dtm/) — там список из 1000+ готовых DTM с именами и привязкой к месту. Для "рельеф не выдуман, это настоящий кратер такой-то" удобнее всего брать что-то с узнаваемым названием — например DTM в кратере Джезеро (посадочная площадка Perseverance):

- [DTM: Candidate Landing Site for 2020 Mission in Jezero Crater (ESP_045994_1985)](https://www.uahirise.org/dtm/dtm.php?ID=ESP_045994_1985) — заявленный масштаб 1.00 м/пиксель, координаты центра ~18.4° с.ш., 77.44° в.д.
- Или DTM в кратере Гейл (место посадки Curiosity), например [Inverted Riverbed in Gale Crater (PSP_009149_1750)](https://www.uahirise.org/dtm/dtm.php?ID=PSP_009149_1750).

Альтернативный вход — карта-браузер [DTM Map](https://www.uahirise.org/dtm/) или поиск через [PDS Geosciences Node Orbital Data Explorer](https://ode.rsl.wustl.edu/mars/) (фильтр по инструменту HiRISE, тип продукта DTM), если нужен конкретный регион без готового названия на сайте HiRISE.

Также есть зеркало на AWS в Cloud-Optimized GeoTIFF, без необходимости качать целиком: [NASA/USGS Released HiRISE Digital Terrain Models — Registry of Open Data on AWS](https://registry.opendata.aws/nasa-usgs-mars-hirise-dtms/).

Для более широкого контекста вокруг колонии (пейзаж за пределами самой площадки) — тот же кратер, но CTX DTM с разрешением ~20 м/пиксель через [Mars Trek](https://trek.nasa.gov/mars/) (визуальный браузер с прямой кнопкой Download) или через [USGS Astrogeology Search](https://astrogeology.usgs.gov/search) с фильтром "CTX DEM".

### Шаг 2 — скачать файл

На странице DTM ссылка на файл вида `DTEEC_045994_1985_046060_1985_U01.IMG` (в примере выше ~381 МБ). Формат: 32-bit floating point IMG, PDS-стандарт, значение пикселя = высота в метрах относительно ареоида (марсианский аналог геоида). Рядом обычно лежит ортофото (JP2) той же площадки — пригодится как текстура поверх меша.

### Шаг 3 — конвертация (GDAL, скриптуется)

GDAL понимает PDS IMG "из коробки" (включая систему координат Марса, зашитую в заголовок файла — руками ничего не выставлять не нужно).

```bash
# посмотреть метаданные — диапазон высот, разрешение, CRS
gdalinfo DTEEC_045994_1985_046060_1985_U01.IMG

# перегнать в GeoTIFF (совместим с большинством софта)
gdal_translate -of GTiff DTEEC_045994_1985_046060_1985_U01.IMG dtm.tif

# вырезать нужный участок под площадку колонии (пример bbox в проекции файла)
gdalwarp -te xmin ymin xmax ymax dtm.tif dtm_site.tif

# сделать 16-битный grayscale PNG heightmap (0-65535), диапазон высот из gdalinfo
gdal_translate -ot UInt16 -scale <h_min> <h_max> 0 65535 -of PNG dtm_site.tif heightmap16.png

# или 8-битный вариант, если движок/инструмент требует попроще
gdal_translate -ot Byte -scale <h_min> <h_max> 0 255 -of PNG dtm_site.tif heightmap8.png
```

То же самое скриптуется на Python через `rasterio` (читает те же GDAL-драйверы) — удобно, если нужен пакетный прогон по нескольким DTM или кастомная нормализация высот:

```python
import rasterio
import numpy as np
from PIL import Image

with rasterio.open("dtm_site.tif") as src:
    heights = src.read(1).astype(np.float32)

h_min, h_max = np.nanmin(heights), np.nanmax(heights)
normalized = ((heights - h_min) / (h_max - h_min) * 65535).astype(np.uint16)
Image.fromarray(normalized).save("heightmap16.png")
```

### Шаг 4 — что получится на выходе

16-битный (или 8-битный) серый PNG heightmap, где яркость пикселя = высота. Такой файл читает любой инструмент генерации меша из heightmap (displacement/height-based mesh tools). Разрешение файла для HiRISE-площадки 100х100 м при исходных 1 м/пиксель — примерно 100х100 px без апскейла, с запасом реальной детализации рельефа, не интерполяции.

Ортофото той же площадки (тоже IMG/JP2, конвертируется той же командой `gdal_translate` в PNG/JPEG) дает готовую текстуру поверхности, снятую с орбиты — можно наложить поверх меша или использовать как reference для текстурирования.

Источники:
- [HiRISE — How to Use Digital Terrain Models](https://www.uahirise.org/dtm/howto.php) (официальная инструкция HiRISE: IMG → GeoTIFF через `gdal_translate`, либо ISIS3 `pds2isis` для научного пайплайна)
- [gdal_translate — GDAL documentation](https://gdal.org/en/stable/programs/gdal_translate.html)
- [PDS Geosciences Node — Mars Orbital Data Explorer](https://ode.rsl.wustl.edu/mars/)

---

## Итог по трем вопросам

1. **Лицензия:** MOLA/CTX/HiRISE — public domain, коммерческое использование разрешено, единственное правило — не подразумевать одобрение NASA. Кредит желателен, не обязателен (см. дословные цитаты в разделе 1).
2. **Разрешение:** MOLA 463 м/пиксель и даже blended-продукт 200 м/пиксель для площадки в сотню метров бесполезны — вся площадка меньше одного пикселя данных. CTX (~20 м/пиксель) годится для окружения колонии, но не для самой площадки. HiRISE DTM (1-2 м/пиксель) — единственный источник, где сотня метров превращается в тысячи точек высоты, и это то, что нужно брать.
3. **Маршрут:** каталог [uahirise.org/dtm](https://www.uahirise.org/dtm/) → скачать .IMG (DTM) + ортофото → `gdal_translate`/`gdalwarp` → 16-битный PNG heightmap + PNG-текстура. Полностью скриптуется, GUI не требуется.
