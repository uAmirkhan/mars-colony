# Промпт генерации моделей: кит на 13 объектов

Собран 2026-08-17 из отчётов четырёх ролей: арт-директор (блок стиля), критик
кадра (силуэты), промпт-инженер (механика Gemini), кузнец ассетов (бюджет
детализации). Конфликты между ними разведены здесь, ниже отмечено где и как.

Конвейер: картинка в Gemini бесплатно → `tripo3d/h3.1/image-to-3d` с
`face_limit` → приёмка по пяти пунктам `konveyer-fal.md` → чистка в Blender →
FBX в сцену.

## Как пользоваться

Промпт собирается из двух частей. Первая строка — дизайн-блок конкретного
объекта из таблицы ниже. Дальше без изменений идёт фиксированный блок. Ничего
местами не менять: дизайн идёт первым, потому что Gemini сильнее взвешивает
начало.

---

## Фиксированный блок (не меняется никогда)

```
RENDER REGISTER. A clean CG product render of one object, smooth-shaded as if
captured in a 3D engine viewport. This is a 3D render, not concept art, not an
illustration. No ink outline, no contour stroke around any silhouette or seam,
no comic linework, no visible brushwork, no canvas or paper texture, no
painterly shading, no sketch marks. Shapes separate through shading and colour,
never through a line.

TONE. Bright, clean, optimistic, toy-like — a sunlit, well-run colony. Never a
rugged industrial wasteland, never weathered realistic science fiction. If in
doubt, render it friendlier and cleaner, not grittier.

FORM. Large simple volumes only: domes, barrel vaults, capsules, cylinders,
rounded boxes. Every edge and corner heavily rounded. A handful of large clearly
readable features rather than many small ones — the object must still read as a
simple recognisable silhouette shrunk to a thumbnail.

DETAIL BUDGET. Solid closed volumes only. No open lattice, no separate railings,
no ladder rungs, no cables, no guy wires, no hairline antennas. Every protruding
detail is thick and chunky relative to the whole shape. Any grid or mesh pattern
— vents, grating, panel seams, girder crosses, solar cell borders — is painted
as flat surface marking, never modelled as open framework. Bold simple readable
silhouette.

SURFACE. Every surface is a clean factory-fresh matte-to-satin painted finish,
ceramic composite or painted metal, one uniform colour per panel, crisp moulded
edges. The object is new and cared-for: intact smooth paint everywhere. A soft
gentle sheen only on painted panels and brass trim, never a hard glossy hotspot,
never a mirror reflection.

COLOUR. Body is one saturated clean colour from this closed set: brick red
#C1432B, teal blue #1E93A0, mustard yellow #D6A02A, terracotta #C2643C, sage
green #7C9B63. Trim, where the object has a natural collar or band, takes a
second colour from the same set, a different hue. Metal on rivets, frames and
structural trim is warm brass #C7A233, matte to satin. Body and trim sit at 65
to 85 percent saturation: clearly vivid, never washed out, never pastel, never
dusty. Never pure interface blue #2E9BE0, never yellow-and-black hazard
chevrons, never khaki or battleship grey, never rust-orange streaking.

MARKINGS. Bare factory-fresh painted surfaces with only mechanical detailing —
rivets, panel seams, bolts. Unmarked, as if photographed on the production line
before any signage, decals, stencils, numbering or paint markings were applied.

LIGHT. Soft even light from a large surrounding source, like a bright overcast
sky. Every surface carries a gentle two-step gradient, lighter where it faces
the light, darker where it turns away, with soft ambient occlusion only in deep
seams and inner corners. No hard directional key light, no cast shadow anywhere
in the frame, no contact shadow beneath the object, no specular hotspot, no rim
light, no coloured light.

CAMERA AND FRAME. Three-quarter view, camera elevated about 40 degrees above the
object — the angle a tabletop miniature is photographed from, not a top-down
plan and not eye level. Square 1:1 frame. The subject fills nearly the entire
frame edge to edge with only a thin even margin, tightly cropped, no wide
establishing view, nothing cropped by the frame edge.

SCENE. Floating product photography: the object is suspended weightless in a
seamless white void, shot in the style of a levitation product campaign. Pure
white #FFFFFF continues directly underneath the object with no line, seam or
colour change where a floor would be. The object's own flat manufactured
underside is its lowest visible surface, with empty white void below it. No
floor, no ground, no terrain patch, no horizon, no table, no pedestal, no
plinth, no display stand, no turntable, no base slab, no platform, no shadow,
no reflection beneath the object.
```

---

## Дизайн-блоки, 13 штук

Первая строка промпта. Правки кузнеца в силуэты уже внесены: сплошные формы
вместо ферм, толстые стойки вместо тросов, швы краской вместо прорезанных
рёбер.

```
sklad-bunkery: A cluster of low earth-mounded storage bunkers with barrel-vault roofs and no doors or windows, one mound noticeably taller than the others breaking the row's rhythm.

burovaya-02: A tall narrow solid tapering derrick mast like an obelisk standing alone on a small anchored base, its cross-bracing painted as dark stripes on the solid faces, one thick solid triangular gusset brace running from the mast down to the base.

burovaya-03: A long low tracked drilling machine, a horizontal drum-and-auger body riding on chunky simplified treads, the drum housing pushed toward the front end, not centered, no vertical mast at all.

burovaya-04: A drilling rig built around one long thick diagonal boom arm angled up from a squat anchored base, a blocky counterweight at the base opposite the arm.

burovaya-05: A compact drilling rig raised on three thick solid splayed legs with open air beneath the body, the drill assembly hanging visibly in the gap between the legs.

zhiloy-bashnya: A tall residential tower built from stacked ring-shaped floor modules climbing upward, a solid external utility shaft bulging up one side, off-center.

zhiloy-kupol: A smooth solid opaque hemisphere habitat dome sitting low and wide, one airlock pod bulging off-center on its curve.

kupol-geodezicheskiy: A large glazed dome, one continuous solid glass shell divided into big flat crystalline facets, the geodesic seams drawn as thin dark painted lines on the glass rather than cut ribs, the single largest dome volume in the scene, one wide loading panel set into it off-axis near the base.

kupol-tunnel: A long low horizontal solid tube connecting two points at ground level, with one visible elbow bend where it changes direction, its end openings small relative to its length.

stantsiya-atmosfernaya: A cluster of bulbous ovoid pressure tanks of different sizes, one dominant tank larger than the rest, a single thick vent stack rising off-center above the group.

zavod-pishchevoy: A massive flat-roofed rectangular industrial block, the biggest blocky footprint in the colony, a compact silo-and-chimney cluster attached to one corner, off-center.

sklad-angar: One large standalone barrel-arch hangar with a dark open archway mouth set off-center into one end face, the opening moderate relative to the whole volume.

ploshchadka-shattla: A very wide extremely flat landing platform raised just off the ground on short stubby legs, ringed by short thick beacon posts with one post taller than the rest breaking the ring.
```

## Проверка перед запуском

Прищуриться на все тринадцать разом. Должно возникнуть тринадцать разных линий,
а не тринадцать коробок с разной мебелью на фасаде: вертикаль, горизонталь,
диагональ, просвет между опор, ряд насыпей, гранёная сфера, гладкая полусфера,
изогнутая труба, кластер капсул, прямоугольный блок, арка с проёмом, стек
колец, плоский диск.

Если получилось второе — виноват не генератор, а промпт.

---

## Приёмка картинки, три пункта

До траты денег на 3D.

1. **Под объектом ничего нет.** Ни плиты, ни пятна грунта, ни подложки, ни
   тени. Белое поле продолжается под объектом.
2. **Ни одной буквы, цифры или знака** на модели.
3. **Это рендер, а не рисунок.** Нет обводки, нет мазка, нет живописной
   фактуры.

## Если плита всё-таки вылезла

Не перегенерировать с нуля, а править в диалоге. Scope блокируется жёстко,
нужное состояние описывается утвердительно.

```
Keep this exact object, camera angle, lighting and background unchanged. Remove
the flat surface currently touching the bottom of the object — the object should
end at its own flat underside with nothing else below it, floating in the same
white void as the rest of the frame around it.
```

---

## Бюджет полигонов, поимённо

Ревизия кузнеца. Плоское число категории не знает про топологию конкретной
формы, поэтому таблица `konveyer-fal.md` уточнена на куполе и буровых.

| Модель | `face_limit` | Почему |
|---|---|---|
| `kupol-geodezicheskiy` | 6000 | герой, самый сложный объём набора |
| `zavod-pishchevoy` | 4000 | верхняя граница здания, запас на трубы и баки |
| `zhiloy-bashnya` | 3500 | самый высокий силуэт партии |
| `stantsiya-atmosfernaya` | 3000 | промышленная коробка с баками |
| `sklad-angar` | 3000 | простой объём, ворота текстурой |
| `ploshchadka-shattla` | 2500 | плоская площадка, тонких деталей нет |
| `sklad-bunkery` | 2500 | скруглённый объём, простая топология |
| `zhiloy-kupol` | 2500 | замкнутая одна оболочка |
| `kupol-tunnel` | 2500 | проём держать умеренным |
| `burovaya-02` | 2500 | +500 к базовым на венец и переход мачты |
| `burovaya-03` | 2500 | то же |
| `burovaya-04` | 2500 | то же |
| `burovaya-05` | 2500 | то же |
| **Сумма** | **39 500** | против нынешних 1,3 млн, снижение в 33 раза |

39 500 — это 16 процентов от потолка кадра в 250 000, и потолок считается на
весь кадр, не только на эти тринадцать. С учётом дублей (`sklad-bunkery` и
`sklad-angar` стоят по две копии) и примерно двадцати уже лёгких моделей сцены
запас остаётся.

## Оговорка, которую нельзя терять

Находка «решает топология, а не коэффициент сжатия» получена замером
**постдецимации в Blender**, quadric edge collapse после генерации. `face_limit`
у Tripo — параметр самой генерации, а не постобработка, и что его внутренний
мешинг рвётся по тому же механизму, **не проверено**. Логика картинки от этого
не зависит: сложная топология хрупка при любом способе сжатия. Но числа таблицы
подтверждаются только первым реальным прогоном.

Поэтому порядок прежний и он не обсуждается: **одна модель целиком, замер по
пяти пунктам, два кадра владельцу, и только после его «да» — партия.**

---

## Три конфликта между ролями и как разведены

**Свет.** Арт-директор требовал мягкий рассеянный без единой тени,
промпт-инженер оставлял слабую контактную тень. Взят арт-директор: любая тень
запечётся в текстуру как факт геометрии и всплывёт в Unity вторым пятном под
собственным светом сцены. Мягкое затенение в складках оставлено — совсем
плоский свет лишает Tripo подсказок по глубине.

**Фон.** Арт-директор дал белую пустоту с убранным полом, промпт-инженер —
жанр левитационной съёмки. Взято оба: жанр как причина, по которой пола нет,
плюс явный перечень того, что этот жанр исключает. Отрицание одно не работает,
жанр без перечня оставляет лазейку.

**Купол и вышки.** Критик кадра требовал гранёную решётку и открытую ферму как
определяющий силуэт, кузнец запрещал открытый каркас как худший случай для
сжатия. Разведено технически: силуэт сохранён, исполнение сплошное. Купол —
цельная оболочка с крупными фасетами и швами краской. Вышка — сплошная
сужающаяся мачта с раскосами краской и толстой косынкой вместо троса.

## Что записать в свод отдельно

Арт-директор поймал расхождение, которое иначе стоило бы партии. Свод фиксирует
свет **сцены**: ключ слева сверху под 55 градусов, короткая контактная тень.
Для картинки, уходящей в реконструкцию, эта схема вредна по причине выше.
Раздел свода писался под кадр, который смотрит зритель, а не под кадр, который
смотрит реконструктор. Нужно уточнение в `ux-township-artlanguage.md`, иначе
следующий скопирует 55 градусов в промпт генерации.
