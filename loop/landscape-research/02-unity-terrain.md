# Направление 2/10: штатная система ландшафта Unity (Terrain)

Разведка для Mars Colony. Версия движка — Unity 6000.0.64f1 (строка Unity 6),
взята из `mars-unity/ProjectSettings/ProjectVersion.txt`. Render pipeline —
Built-in (`mars-unity/ProjectSettings/GraphicsSettings.asset`,
`m_CustomRenderPipeline: {fileID: 0}`), не URP. Сборка — WebGL, сжатие Brotli.

Сцену сейчас строит `mars-unity/Assets/Editor/SceneBuilder3D.cs` из
`colony3d-scene.json`: 182 процедурные коробки на плоском цвете. Грунт —
один `PrimitiveType.Plane` без текстуры (`BuildGround`, строки 1172-1186),
рельефа нет вообще, ямы — отдельные несостыкованные модели.

## Число, ради которого писан этот отчет

Вместо оценки по форумам число измерено на этом самом проекте: собраны
настоящие WebGL-билды через batch mode Unity (`Unity.exe -batchmode
-nographics -quit -executeMethod ...`), теми же настройками сжатия, что и
боевая сборка витрины (`BuildWeb3D`/`BuildScript.BuildWeb`: Brotli,
`exceptionSupport = None`, `dataCaching = true`, `decompressionFallback =
true`). Метод и одноразовые пробные сцены — по образцу уже существующего в
проекте `Assets/Editor/BuildSizeProbe.cs` (прогон 7, `loop/run-7/coder-work.md`
строки 319-399), который меряет вес одной добавленной модели тем же приемом.
Новый файл `Assets/Editor/TerrainSizeProbe.cs` не трогает `SceneBuilder3D.cs`,
`colony3d-scene.json` и существующий `BuildSizeProbe.cs`.

| Сцена | Состав | Байт (папка билда) | МБ | Дельта к пустой сцене |
|---|---|---:|---:|---:|
| SizeProbeEmpty (уже было, прогон 7) | плоскость-грунт + свет + камера, БЕЗ terrain | 5 339 680 | 5.09 | — |
| SizeProbeTerrain | Terrain, heightmap 513×513, 1 TerrainLayer (текстура 4×4), БЕЗ деревьев/деталей | 5 698 202 | 5.69 | **+358 522 байт (+0.34 МБ, +6.7%)** |
| SizeProbeTerrainLowRes | то же, heightmap 129×129 | 5 697 266 | 5.69 | +357 586 байт (+0.34 МБ) — практически то же число |
| SizeProbeDeformedMesh | меш-плоскость 129×129 вершин, деформация из кода, MeshCollider, материал Standard без текстуры (тон mars_ground) | 5 603 185 | 5.60 | +263 505 байт (+0.25 МБ, +4.9%) |

Сырые отчеты: `mars-unity/size-probe-terrain.json`,
`mars-unity/size-probe-terrain-lowres.json`,
`mars-unity/size-probe-deformed-mesh.json` (плюс уже существовавший
`mars-unity/size-probe-empty.json`). Сами билды лежат в
`mars-unity/Build/SizeProbeTerrain`, `Build/SizeProbeTerrainLowRes`,
`Build/SizeProbeDeformedMesh`.

**Вывод по числу.** Terrain (513×513) и Terrain (129×129) дали ПОЧТИ
одинаковую дельту — 358 522 против 357 586 байт, разница меньше килобайта.
Значит вес добавляет не карта высот (она жмется Brotli почти в ноль — это
гладкие плавно меняющиеся float, идеальный случай для сжатия), а
ФИКСИРОВАННЫЙ налог: движковый модуль Terrain, terrain-шейдер
(`Nature/Terrain/Standard` в Built-in RP) и служебная сериализация
`TerrainData`/`TerrainLayer`/`TerrainCollider`. Этот налог не растет
заметно с разрешением карты высот в проверенном диапазоне (129-513) — но,
скорее всего, и не сожмется сильно ниже, даже если урезать heightmap до
минимума.

**Приговора нет.** +0.34 МБ на фоне текущих 5.8 МБ дает ~6.14 МБ — с запасом
до потолка 8 МБ (было 2.2 МБ запаса, остается ~1.86 МБ). До штрафа в -25
баллов на 15 МБ вообще далеко. Terrain не тянет тяжелых WebGL-специфичных
зависимостей — ни compute shaders, ни geometry shaders терrain-компонент сам
по себе не требует (это условие ломается, если включить траву/детали, см.
ниже).

Меш-альтернатива дешевле процентов на 25-35 (263 505 против 358 522 байт,
разница ~95 КБ) — но при сопоставимом визуальном результате обе цифры
малы относительно бюджета. Разница есть, но не решающая сама по себе.

## Создание Terrain из кода в редакторе

`Terrain.CreateTerrainGameObject(TerrainData)` — стандартный способ поднять
Terrain-объект в editor-скрипте, аналогично тому, как `SceneBuilder3D`
поднимает остальные объекты через `GameObject.CreatePrimitive`/
`PrefabUtility.InstantiatePrefab`:

```csharp
var terrainData = new TerrainData();
terrainData.heightmapResolution = 129;       // или 513 — на вес почти не влияет
terrainData.size = new Vector3(200f, 30f, 200f); // совпадает с текущим mars_ground (200x200)

var heights = new float[129, 129]; // heights[y, x], 0..1
// ...заполнить высоты...
terrainData.SetHeights(0, 0, heights);

AssetDatabase.CreateAsset(terrainData, "Assets/Kit/mars_terrain_data.asset");
var terrainGo = Terrain.CreateTerrainGameObject(terrainData);
```

`SetHeights(xBase, yBase, heights)` принимает массив 0..1 (доля от
`size.y`), индексация `[y, x]`, область правки задается размером массива и
смещением `xBase`/`yBase` — можно писать не всю карту, а только
прямоугольный кусок. Источник:
[Unity - Scripting API: TerrainData.SetHeights](https://docs.unity3d.com/ScriptReference/TerrainData.SetHeights.html).

Для одноразовой генерации в editor-скрипте (как `SceneBuilder3D.BuildScene`)
достаточно прямого `SetHeights` — это не runtime-редактирование по кадрам.
Если бы правка шла интерактивно (кисть в живой сцене), документация
рекомендует `SetHeightsDelayLOD` + `SyncHeightmap` в конце, чтобы не
пересчитывать LOD и вегетацию на каждый вызов — не наш случай, но стоит
знать: [Unity - Manual: Set the height of an area or tile](https://docs.unity3d.com/Manual/terrain-SetHeight.html).

## Splatmap: покраска несколькими материалами

Слой красится через `TerrainLayer` (заменил старые raw-текстуры) +
`TerrainData.SetAlphamaps`:

```csharp
var layerRegolith = new TerrainLayer { diffuseTexture = texA, tileSize = new Vector2(15, 15) };
var layerRock      = new TerrainLayer { diffuseTexture = texB, tileSize = new Vector2(15, 15) };
AssetDatabase.CreateAsset(layerRegolith, "Assets/Kit/layer_regolith.asset");
AssetDatabase.CreateAsset(layerRock,      "Assets/Kit/layer_rock.asset");
terrainData.terrainLayers = new[] { layerRegolith, layerRock };

int aw = terrainData.alphamapWidth, ah = terrainData.alphamapHeight;
var map = new float[ah, aw, terrainData.terrainLayers.Length]; // [y, x, layerIndex]
for (int y = 0; y < ah; y++)
for (int x = 0; x < aw; x++)
{
    float steepness = terrainData.GetSteepness(x / (float)aw, y / (float)ah);
    float rockWeight = Mathf.Clamp01(steepness / 30f); // круче — больше камня
    map[y, x, 0] = 1f - rockWeight;
    map[y, x, 1] = rockWeight;
}
terrainData.SetAlphamaps(0, 0, map);
```

Порядок массива — `[y, x, i]`, `i` — индекс слоя, веса по третьей оси должны
суммироваться в 1. Источник:
[Unity - Scripting API: TerrainData.SetAlphamaps](https://docs.unity3d.com/ScriptReference/TerrainData.SetAlphamaps.html).

Важно: вес самих диффузных текстур слоев в замере выше НЕ учтен — там
стоит текстура 4×4 пикселя, чтобы измерить именно налог Terrain, а не
случайной текстуры. Реальная текстура на слой (даже маленькая, сжатая,
256×256) добавит свои условные 50-150 КБ за штуку поверх измеренных 358 КБ,
и это не специфика Terrain — та же текстура столько же весила бы, будучи
покрашена на обычный меш.

## Как вырезать плоские площадки под здания

Прямого метода "вырезать плоскую площадку" в Terrain API нет — это
комбинация того же `SetHeights`, примененного не ко всей карте, а к
прямоугольнику под конкретное здание:

1. Перевести мировые координаты угла площадки в индексы heightmap:
   `xBase = (worldX - terrainPos.x) / size.x * heightmapResolution` (и то
   же для Z).
2. Собрать `float[,] patch` нужного размера, залить одним значением
   (целевая высота площадки), опционально размыть край
   (`Mathf.SmoothStep`/линейная растяжка на несколько клеток по периметру),
   чтобы не было вертикального обрыва в один тексель.
3. `terrainData.SetHeights(xBase, yBase, patch)` — меняется только этот
   кусок, остальной рельеф не трогается.

Ручной работы больше, чем кажется: код сам не решает про подпорную стенку
или пандус на границе площадки с уклоном вокруг — это отдельная
геометрия/декор поверх плоского пятна, Terrain ее не рисует. Источник по
Set Height (UI-версия того же приема, для понимания механики):
[Unity - Manual: Set the height of an area or tile](https://docs.unity3d.com/Manual/terrain-SetHeight.html).

## Деревья и детали (трава) — не нужны, но вес и риск проверены

По задаче они не нужны, но раз просили проверить — два независимых довода
не включать их вообще, а не только вес:

- **Вес**: `TreePrototype`/`DetailPrototype` тянут отдельные подсистемы —
  биллборд/импостор-рендер для деревьев, ветер (`WindZone`), и для травы —
  инстансинг через `DrawMeshInstancedIndirect`. Замер этой части отдельно
  не делался (незачем — см. ниже), но по составу подсистем это заведомо
  больше, чем голый Terrain из таблицы выше.
- **WebGL-риск важнее веса**: compute shaders в WebGL не поддерживаются
  вообще, часть пути рендера деталей на них завязана. В официальном issue
  tracker Unity есть открытые дефекты именно на стыке Terrain + Details +
  WebGL: [Terrain objects aren't rendered when built on WebGL](https://issuetracker.unity3d.com/issues/terrain-objects-arent-rendered-when-built-on-webgl)
  и [WebGL Player fails to render Scene when Terrain with Detail Mesh is added and WebGPU Graphics API is used](https://issuetracker.unity3d.com/issues/webgl-player-fails-to-render-scene-when-terrain-with-detail-mesh-is-added-and-webgpu-graphics-api-is-used).
  То есть трава/детали на WebGL — не только лишний вес, но и предметная
  зона известных багов рендера. Вывод практический: не заполнять
  `terrainData.treePrototypes` и `terrainData.detailPrototypes` вообще —
  оставить оба массива пустыми. В замерах выше они и не заполнялись.

## Альтернатива: меш-плоскость с деформацией вершин из кода

Вместо компонента Terrain — обычный `Mesh` (grid вершин), высота каждой
вершины считается функцией в коде (шум/синусоиды/явная разметка площадок),
рендерится обычным `MeshRenderer` с уже существующим материалом
`mars_ground` (`Shader.Find("Standard")`, без текстуры — как сейчас).

```csharp
int res = 129; // вершин на сторону — достаточно для пологого рельефа
var verts = new Vector3[res * res];
for (int y = 0; y < res; y++)
for (int x = 0; x < res; x++)
{
    float nx = (float)x / (res - 1), ny = (float)y / (res - 1);
    float h = HeightFunction(nx, ny); // сюда же логика плоских площадок под здания:
                                       // если (nx, ny) внутри footprint здания — h = целевая высота
    verts[y * res + x] = new Vector3((nx - 0.5f) * 200f, h, (ny - 0.5f) * 200f);
}
// построить треугольники по сетке (2 треугольника на клетку), mesh.vertices = verts, ...
mesh.RecalculateNormals();
```

Плоские площадки здесь решаются проще, чем в Terrain: не нужен пересчет
мировых координат в индексы heightmap — уже работаешь в той же функции,
что генерит рельеф, достаточно `if (InsideFootprint(nx, ny)) h = padHeight;`
с растяжкой по краю.

**Что дешевле по весу** — измерено выше: меш ~264 КБ дельты против ~358 КБ
у Terrain, то есть меш легче примерно на 95 КБ (~26%) при сопоставимой
плотности сетки (129×129 в обоих случаях).

**Что проще строить скриптом** — тут наоборот, Terrain удобнее для
итеративной правки (кисти, `SetHeights` на кусок, `SetAlphamaps` для
покраски по правилу), а меш требует писать свою логику UV/нормалей/
покраски по вершинным цветам или мультиматериалам вручную. Для
одноразовой процедурной генерации из JSON (как весь остальной
`SceneBuilder3D`) обе задачи одного порядка сложности — здесь уже стоит
своя система генерации коробок и площадок, добавить высоту по функции в
нее дешевле по трудозатратам, чем осваивать API `TerrainData`/
`TerrainLayer`/`SetAlphamaps` с нуля.

## Итог направления

- Terrain не запрещен бюджетом: измеренная дельта +0.34 МБ, это ~15% от
  оставшегося запаса в 2.2 МБ до порога -10 баллов (8 МБ). Утверждение
  "Terrain тянет тяжелые WebGL-зависимости" эмпирически не подтвердилось —
  сам компонент без деревьев/деталей легкий.
- Мера тяжести Terrain — не карта высот (та жмется почти в ноль), а
  фиксированный налог модуля+шейдера+сериализации, он не растет заметно от
  129 до 513 разрешения heightmap.
- Меш-деформация дешевле Terrain на ~95 КБ (~26%) при равной плотности
  сетки и решает задачу "плоская площадка под здание" тем же кодом, что уже
  генерит рельеф — без отдельного API. Прямой довод "почему нет Terrain"
  тут не вес, а то, что вся остальная сцена и так процедурная, и
  мешовая деформация ложится в тот же паттерн генерации, что уже есть в
  `SceneBuilder3D.cs`.
- Деревья/детали — не включать ни при каком варианте: не только лишний вес,
  а зона документированных багов рендера Terrain-деталей на WebGL.
- Решение "Terrain или деформированный меш" — не вопрос бюджета (обе цифры
  малы), это вопрос того, какой код проще встроить в уже существующий
  процедурный конвейер `SceneBuilder3D.cs`. Это уже вне зоны этого замера
  и решается на уровне архитектуры сборщика сцены, не веса.

## Как воспроизвести измерение

```
cd mars-unity
"C:\Program Files\Unity\Hub\Editor\6000.0.64f1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath . -executeMethod TerrainSizeProbe.BuildWithTerrain -logFile -
"C:\Program Files\Unity\Hub\Editor\6000.0.64f1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath . -executeMethod TerrainSizeProbe.BuildWithTerrainLowRes -logFile -
"C:\Program Files\Unity\Hub\Editor\6000.0.64f1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath . -executeMethod TerrainSizeProbe.BuildWithDeformedMesh -logFile -
```

Инструмент: `mars-unity/Assets/Editor/TerrainSizeProbe.cs` (новый файл, по
образцу существующего `Assets/Editor/BuildSizeProbe.cs`, не трогает
`SceneBuilder3D.cs`/`colony3d-scene.json`). Одноразовые пробные сцены и
ассеты остались в `Assets/Scenes/SizeProbeTerrain*.unity`,
`Assets/Scenes/size_probe_*.asset` и папках `Build/SizeProbeTerrain*`,
`Build/SizeProbeDeformedMesh` — не закоммичены, лежат в рабочем дереве как
улика на случай перепроверки, тем же способом, что и артефакты прогона 7
(`size-probe.json`, `size-probe-empty.json`, `Build/SizeProbeEmpty`,
`Build/SizeProbeModel`).
