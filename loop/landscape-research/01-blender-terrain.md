# Направление 1/10 — собственный ландшафт средствами Blender

Разведка для Mars Colony. Вопрос: как собрать связную марсианскую поверхность
(ровные участки под застройку + рельеф по краям, до бюджета треугольников,
запеченная текстура) целиком в Blender 5.2, headless, через `-b -P скрипт.py`.
Только Blender — Unity Terrain, готовые ассеты, TRELLIS и данные NASA не
разбираются.

## Вывод одной строкой

Ставить не на A.N.T. Landscape и не на скульптинг (оба плохо или совсем не
скриптуются headless), а на **программную height-карту**: сетка
(`bpy.ops.mesh.primitive_grid_add`) + высота вершины считается в Python через
`mathutils.noise` (встроен в Blender, без аддонов) + плоские площадки
вырезаются маской по расстоянию до центра площадки + Decimate (PLANAR) под
бюджет треугольников + запекание процедурного материала в один атлас через
Cycles bake в фоне. Все шаги — чистый bpy без интерфейса.

---

## 1. A.N.T. Landscape: встроен ли в 5.2

**Нет, не встроен.** Аддон был в комплекте до Blender 4.1 включительно, начиная
с 4.2 стандартная поставка аддонов вообще упразднена — все переехали на
Extensions Platform, и A.N.T. Landscape там числится как «offered as is, with
limited support» (по факту заброшен). Баг-трекер Blender прямо фиксирует, что
в 4.2 его нет из коробки: issue **#125546 «A.N.T Landscape not available on
Blender 4.2»**. В 5.2 ситуация та же — аддон нужно ставить отдельно с
extensions.blender.org, включать через `addon_utils.enable`, и его оператор
`bpy.ops.mesh.landscape_add(...)` дальше в целом скриптуется как обычный
mesh-add оператор (это не модальный/вьюпорт-зависимый инструмент, а
генератор меша, так что в `-b` он технически должен отрабатывать). Но
закладываться на легаси-аддон без активной поддержки под Blender 5.x — риск:
если он сломается на новом API, чинить будет некому. **Решение: не тянуть
внешнюю зависимость, делать высоту сами** — это не сложнее по коду и полностью
под контролем.

Источники:
- [A.N.T Landscape not available on Blender 4.2 — issue #125546](https://projects.blender.org/blender/blender/issues/125546)
- [A.N.T.Landscape — Blender Extensions](https://extensions.blender.org/add-ons/antlandscape/)
- [Changes to Add-on and Themes Bundling (4.2 onwards) — Blender Developer Forum](https://devtalk.blender.org/t/changes-to-add-on-and-themes-bundling-4-2-onwards/34593)

## 2. Что вообще скриптуется в `-b`, а что нет — это критично

В настоящем headless-режиме (`-b`, без окна) у Blender нет `bpy.context.window`,
`area`, `region` — вообще нет экрана. Операторы, чей `poll()` требует активный
`VIEW_3D`-регион или поток событий мыши (stroke), в `-b` падают с
`RuntimeError: ...poll() failed, context is incorrect` либо просто не
выполняются.

**Не работает headless (вычеркнуть из плана):**
- `bpy.ops.sculpt.*` (`sculpt.brush_stroke` и весь скульптинг) — требует
  последовательность mouse-stroke точек, привязанных к региону вьюпорта.
  Скульптинг «руками» через скрипт в `-b` не воспроизвести.
- `bpy.ops.paint.*` (текстур-пейнт, weight-paint кистью) — та же причина.
- Любой оператор, вызванный с `'INVOKE_DEFAULT'` (модальные: интерактивный
  transform, box-select и т.п.). В скрипте нужно всегда явно задавать значения
  и использовать контекст выполнения по умолчанию (`EXEC_DEFAULT`, то есть
  просто вызов оператора с параметрами, без `INVOKE_*`).
- `bpy.ops.object.voxel_remesh()` — привязан к sculpt-тулбару, поведение в
  `-b` ненадежно; вместо него использовать **Remesh-модификатор**
  (`bpy.types.RemeshModifier`, mode `'VOXEL'`) — это датаблок, не оператор,
  контекста вьюпорта не требует в принципе.

**Работает headless (подтверждено практикой сообщества и уже используется в
проекте — `fit-glb.py`, `facet-glb.py` делают ровно это):**
- `bpy.ops.mesh.primitive_grid_add`, `primitive_plane_add` — генерация меша.
- `bpy.ops.object.modifier_add` / `modifier_apply` — весь модификаторный стек,
  включая Displace, Decimate, Remesh, Subdivision Surface.
- `bpy.ops.object.mode_set(mode='EDIT'/'OBJECT')` — переключение режима не
  требует вьюпорта, только активный объект.
- `bpy.ops.uv.smart_project` — разворот UV, чистая геометрическая операция.
- `bpy.ops.object.bake(...)` — запекание через Cycles. Подтверждено паттерном
  из сообщества: `blender file.blend --background --python bake_script.py`
  — рабочая связка, используется в реальных пайплайнах автоматизации.
- Прямая правка геометрии через `bmesh` — вообще не оператор, просто данные,
  никаких ограничений контекста.

Источники:
- [Render Baking — Blender 5.2 LTS Manual](https://docs.blender.org/manual/en/latest/render/cycles/baking.html)
- [cycles-bake-workaround (headless CLI bake pattern)](https://github.com/Mateusz-Grzelinski/cycles-bake-workaround)
- [Decimate Modifier — Blender 5.2 LTS Manual](https://docs.blender.org/manual/en/latest/modeling/modifiers/generate/decimate.html)
- [Mask Modifier — Blender 5.2 LTS Manual](https://docs.blender.org/manual/en/latest/modeling/modifiers/generate/mask.html)

## 3. Высота рельефа: `mathutils.noise`, без аддонов

Blender с самого начала носит встроенный модуль `mathutils.noise` — это то же
семейство шумов, что стоит за легаси-текстурами Clouds/Musgrave, но доступное
напрямую как чистые Python-функции, без создания текстур и материалов.
Ключевые вызовы: `noise.noise(vector)`, `noise.fractal(position, H,
lacunarity, octaves)`, `noise.turbulence(position, octaves, hard)`,
`noise.hetero_terrain(...)`, `noise.multi_fractal(...)`. Всё это работает
одинаково что в GUI, что в `-b` — это математика, не оператор.

Практический путь: создать сетку нужного разрешения, пройти по вершинам через
`bmesh`, для каждой вершины вычислить `fractal()` от её XY (домножить на
масштаб/амплитуду под марсианский рельеф — пологие дюны, редкие резкие валы),
записать в `co.z`.

Источник:
- [Noise Utilities (mathutils.noise) — Blender Python API](https://docs.blender.org/api/current/mathutils.noise.html)

## 4. Плоские площадки под застройку внутри неровного рельефа

Это ключевое требование, и оно решается не геометрией «руками», а **маской
смешения** между «шум» и «постоянная высота площадки»:

1. Заранее известен список площадок: `(x, y, target_z, радиус_ровной_зоны,
   радиус_растушевки)`, координаты кратны клетке сцены 2.0 (см. контракт
   модели в проекте).
2. Для каждой вершины сетки считается `d` — расстояние по XY до ближайшего
   центра площадки.
3. Строится коэффициент смешения `t` через smoothstep: `t = 0` внутри
   `радиус_ровной_зоны` (чистая плоскость), `t = 1` за пределами
   `радиус_растушевки` (чистый шум), между ними — плавный переход
   (`t = smoothstep(r_flat, r_blend, d)`).
4. Итоговая высота вершины: `z = lerp(target_z, noise_z, t)`.

Это дает физически ровный полигон под здание (без микро-уклона, на который
жаловался инспектор про терраformed-край) и плавный, не рваный переход к
рельефу — ровно то, что нужно, чтобы кратеры и площадки читались одной
поверхностью, а не коллажом.

**Альтернатива без ручного цикла (не рекомендую как основной путь, но фиксирую
как вариант):** тот же эффект можно получить модификаторным стеком —
`DisplaceModifier` с процедурной легаси-текстурой (`bpy.data.textures.new(...,
type='CLOUDS')`) плюс `vertex_group`, где вес вершины и есть тот же `t`
(вес 0 у площадки, 1 снаружи; Displace умножает силу смещения на вес группы).
Плюс — результат остается неразрушающим и его потом можно докрутить руками в
GUI. Минус — два разных источника шума (легаси-текстуры и `mathutils.noise`)
считают немного по-разному, и вес группы все равно приходится проставлять тем
же циклом по вершинам через `vertex_group.add(indices, weight, 'REPLACE')` —
то есть ручного Python-прохода это не убирает, только меняет, куда пишется
результат (в `co.z` напрямую или в вес группы). Поэтому основной путь —
прямая правка `co.z`, она проще для скрипта, который и так гоняется только
через `-P`, GUI-доводка не нужна.

Источники:
- [DisplaceModifier — Blender Python API (проверено по документированному
  поведению `vertex_group`/`strength` в модификаторах смещения)](https://docs.blender.org/api/current/bpy.types.DisplaceModifier.html)
- [Mask Modifier — Blender 5.2 LTS Manual](https://docs.blender.org/manual/en/latest/modeling/modifiers/generate/mask.html)

## 5. Бюджет треугольников: Decimate PLANAR + жесткий cap

1. Разрешение сетки изначально выбирается близко к целевому бюджету
   (`tris ≈ 2 × subdiv_x × subdiv_y` для чистой quad-сетки после
   триангуляции) — не генерировать заведомо переразмеренную сетку и не
   полагаться только на децимацию.
2. `bpy.ops.object.modifier_add(type='DECIMATE')`,
   `decimate_type='PLANAR'`, `angle_limit` в районе 2-5° (в радианах в API,
   в UI — градусы). Planar-режим убирает лишние треугольники именно на плоских
   и почти плоских участках — а это ровно площадки под застройку и пологие
   склоны дюн, — и не трогает высокочастотный рельеф у краев кратеров. Это
   решает бюджет там, где его выгоднее всего решать.
3. Если после planar-прохода бюджет все еще превышен — второй Decimate,
   `decimate_type='COLLAPSE'`, `ratio` подобрать под точный лимит
   треугольников (свойство `face_count` модификатора читается после
   `depsgraph.update()` и дает текущее число граней для проверки).
4. `bpy.ops.object.modifier_apply(modifier=...)` — применить оба модификатора
   по очереди, затем `bpy.ops.mesh.quads_convert_to_tris()` для финального
   точного числа треугольников (проверяется тем же способом, что и модели —
   через `check-model.mjs`-подобный подсчет).

Источник:
- [Decimate Modifier — Blender 5.2 LTS Manual](https://docs.blender.org/manual/en/latest/modeling/modifiers/generate/decimate.html)

## 6. Текстура террейна: процедурный материал → запекание в один атлас

Раз модели набора несут запеченную PBR-текстуру (TRELLIS), у террейна логично
быть в том же формате — иначе получится третий способ покраски в кадре
(атлас Kenney + плоские цвета + голая процедурка), а это тот самый «коллаж»,
которого проект уже избегает по правилу из BRIEF.md.

Путь:
1. Собрать материал на нодах (Shader Editor, но строится это тоже кодом,
   `material.node_tree.nodes.new(...)`, без GUI): базовый цвет — ColorRamp от
   `Noise Texture` (крупный масштаб, ржаво-красные/охристые тона) плюс
   примесь по уклону — `Geometry` → `Normal` → dot с `(0,0,1)` даёт «насколько
   вершина смотрит вверх», и через `Map Range`/`Mix` можно посветлить плоские
   площадки (осевшая пыль) и притемнить крутые сколы кратеров (голая порода).
   Это не геометрия, а чисто цветовая логика — риска для рельефа не несет.
2. `bpy.ops.uv.smart_project()` на террейн-объекте — развертка headless-safe.
3. Создать целевую картинку: `bpy.data.images.new('terrain_bake', W, H)`,
   добавить `Image Texture` узел в материал, назначить его активным
   (`node_tree.nodes.active = img_node`) — это то место, куда Cycles
   запишет результат бейка.
4. `bpy.context.scene.render.engine = 'CYCLES'`, `scene.cycles.device = 'CPU'`
   (гарантированно работает без настройки GPU-препдо в headless; на RTX 4090
   в системе GPU-бейк тоже возможен, но требует явного включения
   compute-device через `preferences.addons['cycles'].preferences` — лишний
   шаг ради того же результата, для одного терраин-объекта CPU-бейка
   достаточно по времени).
5. `bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'},
   use_clear=True)` — запекает итоговый цвет в `terrain_bake`, дальше
   `image.save_render(путь)` или `image.filepath_raw` + `image.save()`.

Вся цепочка — CLI-паттерн `blender -b file.blend -P bake_script.py`,
задокументированный сообществом именно для автоматизации без интерфейса.

Источники:
- [Render Baking — Blender 5.2 LTS Manual](https://docs.blender.org/manual/en/latest/render/cycles/baking.html)
- [cycles-bake-workaround — headless bake CLI pattern](https://github.com/Mateusz-Grzelinski/cycles-bake-workaround)

## 7. Стыковка ландшафта с моделями-объектами

Проблема нынешних пяти грунтовых моделей была не только в форме, а в том, что
объекты ставятся на плоский тайл «вслепую», без привязки к реальной высоте
поверхности под ними. Для нового террейна это решается raycast-посадкой,
тоже headless:

1. Построить `BVHTree` из финального меша террейна:
   `BVHTree.FromObject(terrain_obj, depsgraph)`.
2. Для каждого объекта-модели (шаттл, буровые и т.д.), который должен стоять
   на поверхности: взять его `(x, y)` в мировых координатах, бросить луч вниз
   из точки высоко над сценой: `bvh.ray_cast((x, y, high_z), (0, 0, -1))` —
   возвращает `(позиция_попадания, нормаль, index, distance)`.
2. Присвоить `obj.location.z = позиция_попадания.z` (плюс поправка, если
   пивот модели не в основании — но по контракту `check-model.mjs` пивот и так
   обязан быть в основании, так что поправка обычно нулевая).
3. Опционально: наклонить объект по нормали поверхности (для валунов и
   грунтовой техники это дает естественную посадку на склоне), для зданий и
   прямоугольных построек — не наклонять, только приподнимать/опускать, чтобы
   основание оставалось горизонтальным (площадки для этого и делались плоскими
   в разделе 4).

Это то самое место, где плоские площадки из раздела 4 становятся полезны на
практике: под здания raycast всегда попадает в идеально ровный `target_z`,
без дрожания по вершинам, и модель встает без наклона и без щели по контуру
основания.

Источники:
- [BVHTree Utilities (mathutils.bvhtree) — Blender Python API](https://docs.blender.org/api/current/mathutils.bvhtree.html)
- [NEW!! Python BVH Tree for ray cast and collisions and snapping — Blender Artists](https://blenderartists.org/t/new-python-bvh-tree-for-ray-cast-and-collisions-and-snapping/648141)

## 8. Скелет скрипта (`-b -P terrain.py`)

Каркас, не готовый продакшн-код — показывает порядок вызовов и какие API
реально используются на каждом шаге:

```python
import bpy, bmesh, math
from mathutils import noise, Vector
from mathutils.bvhtree import BVHTree

# --- параметры ---
SIZE = 60.0            # сторона террейна, метры (кратно клетке 2.0)
SUBDIV = 120            # 120x120 -> ~28800 треугольников до децимации
NOISE_SCALE = 0.06
NOISE_AMP = 1.4
PADS = [                # (x, y, target_z, r_flat, r_blend)
    (0.0, 0.0, 0.0, 4.0, 7.0),
    (10.0, -6.0, 0.15, 3.0, 5.0),
]
TARGET_TRIS = 18000

def smoothstep(a, b, x):
    if a == b:
        return 0.0 if x < a else 1.0
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)

# 1. база
bpy.ops.mesh.primitive_grid_add(
    x_subdivisions=SUBDIV, y_subdivisions=SUBDIV, size=SIZE)
terrain = bpy.context.active_object
terrain.name = "terrain_base"

# 2. высота по шуму + маска площадок, прямой bmesh-проход
bm = bmesh.new()
bm.from_mesh(terrain.data)
for v in bm.verts:
    x, y = v.co.x, v.co.y
    n = noise.fractal(Vector((x * NOISE_SCALE, y * NOISE_SCALE, 0.0)),
                       1.0, 2.0, 4)
    noise_z = n * NOISE_AMP

    z, best_t = noise_z, 1.0
    for px, py, pz, r_flat, r_blend in PADS:
        d = math.hypot(x - px, y - py)
        t = smoothstep(r_flat, r_blend, d)
        if t < best_t:
            best_t, z = t, pz * (1 - t) + noise_z * t
    v.co.z = z
bm.to_mesh(terrain.data)
bm.free()

# 3. бюджет треугольников: planar, потом жесткий cap
dec1 = terrain.modifiers.new("dec_planar", 'DECIMATE')
dec1.decimate_type = 'PLANAR'
dec1.angle_limit = math.radians(3.0)
bpy.ops.object.modifier_apply(modifier=dec1.name)

bpy.context.view_layer.update()
tri_count = sum(len(p.vertices) - 2 for p in terrain.data.polygons)
if tri_count > TARGET_TRIS:
    dec2 = terrain.modifiers.new("dec_collapse", 'DECIMATE')
    dec2.decimate_type = 'COLLAPSE'
    dec2.ratio = TARGET_TRIS / max(tri_count, 1)
    bpy.ops.object.modifier_apply(modifier=dec2.name)

bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.mesh.uv_texture_add() if False else None  # UV делается smart_project ниже
bpy.context.view_layer.objects.active = terrain
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project()
bpy.ops.object.mode_set(mode='OBJECT')

# 4. посадка объектов-моделей на поверхность (после того как импортированы)
depsgraph = bpy.context.evaluated_depsgraph_get()
bvh = BVHTree.FromObject(terrain, depsgraph)

def snap_to_terrain(obj):
    hit, normal, idx, dist = bvh.ray_cast(
        (obj.location.x, obj.location.y, 50.0), (0, 0, -1))
    if hit is not None:
        obj.location.z = hit.z

# snap_to_terrain(shuttle_obj)  # и так далее по каждой модели

# 5. запекание материала — см. раздел 6, отдельным проходом после того как
# материал с ColorRamp/Noise Texture/Geometry-normal собран на node_tree.
```

Это дает: связную поверхность без швов (потому что вся сетка — один меш, не
пять несостыкованных объектов), ровные документированные площадки под
здания, треугольный бюджет под контролем на этапе генерации плюс децимация,
и посадку моделей без парения и без утопания в грунте.

## 9. Что осталось решить владельцу (не разведка, а развилка)

- Итоговый `TARGET_TRIS` для террейна — не подобран, зависит от бюджета всей
  сцены (в контракте моделей есть потолок 4000-12000 на одну модель, но
  террейн — не «модель», под него в контракте числа нет).
- Список и координаты площадок под застройку берутся из раскладки сцены
  (клетка 2.0) — этого файла разведка не касалась, площадки в скелете —
  условные заглушки.
- Стоит ли вообще запекать процедурный марсианский материал или тонировать
  готовый bake под палитру набора (metal/rock/dark и т.д.) тем же постпроцессом,
  что уже применяется к TRELLIS-моделям — вопрос согласования с художником
  сцены, не Blender-техники.
