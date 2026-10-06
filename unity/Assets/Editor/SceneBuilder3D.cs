using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Пробная 3D-витрина колонии из набора KayKit Space Base Bits.
///
/// Отдельный файл и отдельная сцена — 2D-сборщик и `Main.unity` не трогаются
/// вообще: на них держится доказательство играбельности.
///
/// Три места, где 3D-конвейер ломается молча, и что здесь с ними сделано.
///
/// 1. **Материал.** FBX приезжает без текстуры — белым или розовым. Импорт
///    материалов отключен целиком (`materialImportMode = None`), и один общий
///    материал с атласом раздается по всем рендерерам руками. Набор нарисован
///    одной текстурой, поэтому один материал — не упрощение, а верный ответ.
/// 2. **Тени.** Галка на источнике света — половина дела. Тени включаются еще
///    и уровнем качества, а для WebGL уровень свой; поэтому тени включаются на
///    ВСЕХ уровнях качества, а не на текущем.
/// 3. **Масштаб.** Он не берется на веру: `AuditKit` пишет реальные габариты
///    каждой модели в `kit-bounds.json`, и раскладка считается от этих чисел.
/// </summary>
public static class SceneBuilder3D
{
    private const string ModelDir = "Assets/Kit/models";
    private const string TexturePath = "Assets/Kit/textures/spacebits_texture.png";
    private const string MaterialPath = "Assets/Kit/spacebits.mat";
    private const string GroundMaterialPath = "Assets/Kit/mars_ground.mat";
    private const string ScenePath = "Assets/Scenes/Colony3D.unity";

    // ---- Подготовка ассетов ------------------------------------------------

    /// <summary>
    /// Настройка импорта текстуры-атласа.
    ///
    /// Атлас — сетка 8x4 из градиентных плашек по 128 пикселей. Мип-уровни на
    /// такой сетке смешивают соседние плашки, и объект вдали получает цвет,
    /// которого нет в палитре. Поэтому мипы выключены.
    /// </summary>
    private static void PrepareTexture()
    {
        var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[3d] текстура не найдена: {TexturePath}");
            return;
        }

        importer.textureType = TextureImporterType.Default;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    /// <summary>Общий материал набора: атлас, матовый, без металла.</summary>
    private static Material PrepareMaterial()
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        material.shader = Shader.Find("Standard");
        material.mainTexture = texture;
        material.SetFloat("_Glossiness", 0.18f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();

        if (texture == null)
            Debug.LogError("[3d] атлас не загрузился — набор приедет белым");

        return material;
    }

    /// <summary>
    /// Материал плато — тот же атлас, но с тональным сдвигом.
    ///
    /// Текстура на весь набор одна, и весь рельеф приезжает ровно одним
    /// оранжевым. На кадре это читалось единой массой: где кончается нижняя
    /// земля и начинается плато, глаз узнавал только по тени от обрыва.
    /// Множитель альбедо — единственный способ развести два слоя грунта, не
    /// трогая текстуру: 0.90 по красному против 0.82 по синему дает разницу
    /// в тоне, а не в цвете, и порода остается той же породой.
    /// </summary>
    private static Material PreparePlateauMaterial()
    {
        const string path = "Assets/Kit/spacebits_plateau.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = Shader.Find("Standard");
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        material.color = new Color(0.90f, 0.855f, 0.82f);
        material.SetFloat("_Glossiness", 0.18f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    /// <summary>
    /// Материал дна карьера — тот же прием тонирования, что у
    /// <see cref="PreparePlateauMaterial"/> (умножение albedo одного и того
    /// же атласа), но в обратную сторону: плато тонировано СВЕТЛЕЕ грунта,
    /// дно карьера — ЗАМЕТНО ТЕМНЕЕ. Граница между уровнями до сих пор
    /// читалась только по тени от обрыва — этот же прием и держит эту
    /// границу для нового, третьего яруса: 0.55/0.50/0.48 против 0.90/0.855/
    /// 0.82 у плато — дно вдвое темнее плато и заметно темнее низкой земли,
    /// то есть само тонирование уже говорит «яма, тень», не дожидаясь угла
    /// падения света.
    /// </summary>
    private static Material PreparePitMaterial()
    {
        const string path = "Assets/Kit/spacebits_pit.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = Shader.Find("Standard");
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        material.color = new Color(0.55f, 0.50f, 0.48f);
        material.SetFloat("_Glossiness", 0.18f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    /// <summary>
    /// Стена карьера в тени — прогон 8, геометрическая вставка для двух
    /// ОТКРЫТЫХ граней ямы (юг и запад клетки (3,1) — у обеих сосед в
    /// `TerrainMap` пустой, край острова). Камера (`BuildCamera`) стоит в
    /// квадранте (−x,−z) и смотрит на
    /// (+x,+z) — именно эти две грани обращены к камере, и именно на них яма
    /// сейчас читается провалом в пустоту, а не обрывом породы. Тон числом
    /// темнее пола (0.55/0.50/0.48 у `PreparePitMaterial`), без текстуры —
    /// отдельная вставка, а не перекраска той же геометрии.
    /// </summary>
    private static Material PreparePitWallMaterial()
    {
        const string path = "Assets/Kit/spacebits_pit_wall.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = Shader.Find("Standard");
        material.mainTexture = null;
        material.color = new Color(40f / 255f, 36f / 255f, 34f / 255f);
        material.SetFloat("_Glossiness", 0.18f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    /// <summary>
    /// Материал солнечных панелей — тот же атлас, но с холодным тональным
    /// сдвигом (тот же прием, что и у `PreparePlateauMaterial`).
    ///
    /// В кадре почти нет холодного акцента (art-inspector: 0.025% насыщенных
    /// пикселей в диапазоне 150-260°, вес «заметно портит»). Панели — готовый
    /// повод: в жизни они синие по функции, не по прихоти. Замерено по
    /// id-буферу текущего кадра: `roofmodule_solarpanels` рендерится (100,91,
    /// 83), `solarpanel` — (77,62,53)/(73,60,52) — нейтрально-теплый металл.
    /// Множитель (0.35, 0.45, 0.95) при умножении на эти числа дает синий
    /// канал заведомо старшим (пример: 100,91,83 -> 35,41,79, H≈232°,
    /// S≈0.56) — можно только гасить каналы (albedo — множитель, не
    /// прибавка), поэтому синий не поднят, а красный с зеленым посажены
    /// ниже него.
    /// </summary>
    private static Material PrepareSolarMaterial()
    {
        const string path = "Assets/Kit/spacebits_solar.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = Shader.Find("Standard");
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        material.color = new Color(0.35f, 0.45f, 0.95f);
        material.SetFloat("_Glossiness", 0.18f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    /// <summary>
    /// Отдельный материал под стеклянную крышу `hangar_roundGlass` — тот же
    /// прием, что `PrepareSolarMaterial`: своя копия, не трогающая общий
    /// `_kenney["_defaultMat"]` (его делят еще 18 моделей набора — платформы,
    /// трубы, спутниковая тарелка, торец рельефа; перекрасить саму запись
    /// палитры значило бы утопить их все в холодном тоне заодно). Множитель
    /// гасит каналы, синий не поднимает: база палитры (212,219,222) дает
    /// зелено-холодный (76,166,191), H≈193°, S≈0.60 — в диапазоне отчета.
    /// </summary>
    private static Material PrepareGlassMaterial()
    {
        const string path = "Assets/Kit/kenney_hangarGlass.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = Shader.Find("Standard");
        material.mainTexture = null;
        material.color = new Color(0.30f, 0.65f, 0.75f);
        material.SetFloat("_Glossiness", 0.18f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static Material PrepareGroundMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, GroundMaterialPath);
        }
        material.shader = Shader.Find("Standard");
        material.mainTexture = null;
        material.color = new Color(0.27f, 0.155f, 0.115f);
        material.SetFloat("_Glossiness", 0.05f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    /// <summary>
    /// Настройка импорта моделей. Материалы не импортируем совсем — иначе на
    /// каждую модель заводится свой материал без текстуры, и сцена приезжает
    /// белой при полностью зеленом логе.
    /// </summary>
    private static void PrepareModels()
    {
        foreach (string path in Directory.GetFiles(ModelDir, "*.fbx"))
        {
            string asset = path.Replace('\\', '/');
            var importer = AssetImporter.GetAtPath(asset) as ModelImporter;
            if (importer == null)
                continue;

            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.SaveAndReimport();
        }
        AssetDatabase.Refresh();
    }

    // ---- Замер набора ------------------------------------------------------

    private static GameObject LoadModel(string name) =>
        AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{name}.fbx");

    /// <summary>Габариты модели в мировых единицах при масштабе 1.</summary>
    private static bool MeasureModel(GameObject prefab, out Bounds bounds)
    {
        bounds = new Bounds();
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (instance == null)
            return false;

        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;

        var renderers = instance.GetComponentsInChildren<Renderer>();
        bool any = false;
        foreach (var r in renderers)
        {
            if (!any)
            {
                bounds = r.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        Object.DestroyImmediate(instance);
        return any;
    }

    /// <summary>
    /// Пишет реальные габариты всех моделей набора в `kit-bounds.json`.
    ///
    /// Нужен ровно затем, чтобы раскладка считалась от измеренных чисел, а не
    /// от веры в то, что «набор сделан в одном масштабе».
    /// </summary>
    public static void AuditKit()
    {
        PrepareTexture();
        PrepareMaterial();
        PreparePlateauMaterial();
        PrepareGroundMaterial();
        PrepareModels();

        // Пустая сцена нужна, чтобы инстансы моделей было куда класть.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var lines = new List<string>();
        foreach (string path in Directory.GetFiles(ModelDir, "*.fbx").OrderBy(p => p))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var prefab = LoadModel(name);
            if (prefab == null || !MeasureModel(prefab, out Bounds b))
            {
                lines.Add($"  \"{name}\": null");
                continue;
            }

            lines.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "  \"{0}\": {{ \"size\": [{1:F3}, {2:F3}, {3:F3}], "
                        + "\"min_y\": {4:F3}, \"center\": [{5:F3}, {6:F3}, {7:F3}] }}",
                    name,
                    b.size.x,
                    b.size.y,
                    b.size.z,
                    b.min.y,
                    b.center.x,
                    b.center.y,
                    b.center.z
                )
            );
        }

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine(string.Join(",\n", lines));
        sb.AppendLine("}");
        File.WriteAllText("kit-bounds.json", sb.ToString());
        Debug.Log($"[3d] габариты {lines.Count} моделей записаны: kit-bounds.json");
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// Замер ориентации наклонных тайлов и диагональных переходов.
    ///
    /// У склона высокая сторона одна, и угадывать поворот — значит потом
    /// смотреть на ступеньку в кадре. Считаем максимальную высоту вершин у
    /// каждого из четырех краев: где 1.0, там верх плато.
    ///
    /// У скругленных тайлов габарит остается 2x2, и по нему не видно НИЧЕГО:
    /// срезан угол, а bounding box про это молчит. Поэтому отдельно считаются
    /// вершины в каждом из четырех углов площадки (|x|>0.85 и |z|>0.85): где
    /// их нет — там угол и срезан, туда скругление и смотрит.
    ///
    /// У диагональных переходов та же беда: 2.939 x 2.939 — это габарит
    /// повернутой на 45 градусов трубы, а не ее длина. Меряем протяженность
    /// вдоль самих диагоналей (x+z) и (x−z): длинная из двух и есть длина
    /// трубы, короткая — ширина.
    /// </summary>
    public static void AuditTerrain()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        string[] names =
        {
            "terrain_slope",
            "terrain_slope_inner_corner",
            "terrain_slope_outer_corner",
            "terrain_low_curved",
            "terrain_tall_curved",
            "terrain_mining",
            "tunnel_straight_A",
            "tunnel_diagonal_short_A",
            "tunnel_diagonal_short_B",
            "tunnel_diagonal_long_A",
            "tunnel_diagonal_long_B",
        };

        var sb = new StringBuilder();
        foreach (string name in names)
        {
            var prefab = LoadModel(name);
            if (prefab == null)
            {
                sb.AppendLine($"{name}: НЕТ МОДЕЛИ");
                continue;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = Vector3.zero;

            float x_minus = -99f,
                x_plus = -99f,
                z_minus = -99f,
                z_plus = -99f;
            // Углы: [-x-z, -x+z, +x-z, +x+z]
            var corner_count = new int[4];
            var corner_top = new float[4] { -99f, -99f, -99f, -99f };
            float d1_min = 99f,
                d1_max = -99f,
                d2_min = 99f,
                d2_max = -99f;

            foreach (var mf in instance.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null)
                    continue;
                foreach (Vector3 v_local in mesh.vertices)
                {
                    Vector3 v = mf.transform.TransformPoint(v_local);
                    if (v.x < -0.5f)
                        x_minus = Mathf.Max(x_minus, v.y);
                    if (v.x > 0.5f)
                        x_plus = Mathf.Max(x_plus, v.y);
                    if (v.z < -0.5f)
                        z_minus = Mathf.Max(z_minus, v.y);
                    if (v.z > 0.5f)
                        z_plus = Mathf.Max(z_plus, v.y);

                    if (Mathf.Abs(v.x) > 0.85f && Mathf.Abs(v.z) > 0.85f)
                    {
                        int k = (v.x > 0f ? 2 : 0) + (v.z > 0f ? 1 : 0);
                        corner_count[k]++;
                        corner_top[k] = Mathf.Max(corner_top[k], v.y);
                    }

                    float d1 = (v.x + v.z) * 0.70710678f;
                    float d2 = (v.x - v.z) * 0.70710678f;
                    d1_min = Mathf.Min(d1_min, d1);
                    d1_max = Mathf.Max(d1_max, d1);
                    d2_min = Mathf.Min(d2_min, d2);
                    d2_max = Mathf.Max(d2_max, d2);
                }
            }

            sb.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}\n  края: -x={1:F2} +x={2:F2} -z={3:F2} +z={4:F2}\n"
                        + "  углы: -x-z={5} -x+z={6} +x-z={7} +x+z={8}\n"
                        + "  диагонали: (x+z)={9:F2}..{10:F2} длина {11:F2}, "
                        + "(x-z)={12:F2}..{13:F2} длина {14:F2}",
                    name,
                    x_minus,
                    x_plus,
                    z_minus,
                    z_plus,
                    corner_count[0],
                    corner_count[1],
                    corner_count[2],
                    corner_count[3],
                    d1_min,
                    d1_max,
                    d1_max - d1_min,
                    d2_min,
                    d2_max,
                    d2_max - d2_min
                )
            );
            Object.DestroyImmediate(instance);
        }

        File.WriteAllText("kit-terrain.txt", sb.ToString());
        Debug.Log("[3d] ориентация склонов записана: kit-terrain.txt\n" + sb);
        EditorApplication.Exit(0);
    }

    // ---- Второй набор: Kenney Space Kit -------------------------------------
    //
    // Набор Kenney покрашен НЕ атласом и НЕ вершинными цветами (так было
    // записано в отчете о подборе — проверено и опровергнуто здесь же): у
    // каждой модели меш разбит на подмеши, и у каждого подмеша своя плоская
    // краска. Всего на все 153 модели двенадцать имен: metal, metalDark,
    // metalRed, dark, rock, rockDark, rockTrack, crystal, bone, skin и пара
    // служебных.
    //
    // Отсюда решение по материалу — см. `PrepareKenneyPalette`. Здесь только
    // замер, и он делает две вещи сразу: пишет габариты (как `AuditKit` для
    // KayKit) и выписывает имена материалов по подмешам В ПОРЯДКЕ СЛОТОВ.
    // Без второго раскраска по имени была бы догадкой: слот и имя связаны
    // порядком, который задает импортер, а не файл.

    private const string KenneyDir = "Assets/Models/kenney-space-kit/extracted/Models/FBX format";

    /// <summary>
    /// Импорт моделей Kenney. В отличие от KayKit материалы ИМПОРТИРУЮТСЯ —
    /// они нужны не сами по себе, а ради своих имен: по имени слота раскладка
    /// подставит краску из общей палитры (см. `PrepareKenneyPalette`).
    ///
    /// Масштаб — ЕДИНИЦА, и это не лень, а замер.
    ///
    /// Соблазн был натянуть тайл на тайл: у Kenney клетка рельефа 1.0 x 1.0
    /// против 2.0 x 2.0 у KayKit, и множитель 2.0 напрашивается сам. Он и был
    /// поставлен первым, и он неверный. Сетка — не то, что видно в кадре;
    /// видно ПОСТРОЙКИ, а по ним два набора совпадают при множителе 1.0
    /// один в один:
    ///   hangar_smallA   2.00 x 1.00 x 2.00  против basemodule_C 2.00 x 1.00 x 2.21
    ///   hangar_largeA   2.00 x 1.00 x 3.00  против cargodepot_A 2.07 x 1.12 x 2.08
    ///   chimney         0.40 x 2.00 x 0.40  против structure_tall 1.75 x 2.00 x 1.75
    ///   platform_large  2.00 x 0.10 x 2.00  против клетки 2.0
    /// При множителе 2.0 ангар вышел бы вдвое выше жилого модуля, и два набора
    /// читались бы двумя наборами с первого взгляда.
    ///
    /// Плата за это — сетка Kenney идет ПОЛОВИНОЙ клетки (1.0). Для дорог,
    /// платформ и труб это выигрыш, а не потеря: мелочь получает свой шаг,
    /// вдвое мельче застройки.
    ///
    /// Множитель ставится в ИМПОРТЕРЕ, а не в трансформе: тогда любой замер
    /// габаритов, любой луч и любой габарит на экране считаются от уже верного
    /// размера, и нигде не нужно помнить про «эту модель надо умножить».
    /// </summary>
    private const float KenneyScale = 1.0f;

    private static void PrepareKenneyModels()
    {
        if (!Directory.Exists(KenneyDir))
        {
            Debug.LogError($"[3d] набора Kenney нет: {KenneyDir}");
            return;
        }
        foreach (string path in Directory.GetFiles(KenneyDir, "*.fbx"))
        {
            string asset = path.Replace('\\', '/');
            var importer = AssetImporter.GetAtPath(asset) as ModelImporter;
            if (importer == null)
                continue;

            bool dirty = false;
            if (!Mathf.Approximately(importer.globalScale, KenneyScale))
            {
                importer.useFileScale = false;
                importer.globalScale = KenneyScale;
                dirty = true;
            }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                dirty = true;
            }
            if (importer.materialLocation != ModelImporterMaterialLocation.InPrefab)
            {
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                dirty = true;
            }
            if (importer.importAnimation || importer.importCameras || importer.importLights)
            {
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                dirty = true;
            }
            if (importer.addCollider)
            {
                importer.addCollider = false;
                dirty = true;
            }

            // Переимпорт 153 моделей идет минуты. Трогаем только те, у которых
            // настройки реально разошлись, иначе каждый прогон сборки платит
            // эти минуты заново.
            if (dirty)
                importer.SaveAndReimport();
        }
        AssetDatabase.Refresh();
    }

    private static GameObject LoadKenney(string name) =>
        AssetDatabase.LoadAssetAtPath<GameObject>($"{KenneyDir}/{name}.fbx");

    // ---- Палитра Kenney: мост между двумя наборами --------------------------
    //
    // ЗАДАЧА. Два набора в одном кадре не должны читаться двумя наборами.
    // KayKit покрашен одним атласом 8x4 плашек; Kenney — одиннадцатью плоскими
    // красками по именам подмешей. Атласа у Kenney нет, UV под чужой атлас у
    // него не разложены, и натянуть `spacebits.mat` на его меши нельзя: модель
    // приедет одним случайным цветом из плашки, в которую попал ее UV.
    //
    // ЧТО ОТВЕРГНУТО И ПОЧЕМУ.
    //   * Свой шейдер под вершинные цвета — не нужен вовсе. Вершинных цветов у
    //     Kenney НЕТ, это ошибка в отчете о подборе; цвет лежит в материалах
    //     подмешей. Шейдер решал бы задачу, которой нет.
    //   * Оставить родные краски Kenney — самый дешевый путь и самый заметный
    //     провал. Разница мелкая, но системная: у Kenney металл холоднее
    //     (215,222,232 против 212,219,222), полутон СИЛЬНО светлее
    //     (172,181,197 против 129,140,145), порода светлее и желтее. Одна
    //     такая расстройка по всему кадру — и глаз читает «две пачки моделей».
    //   * Перерисовать UV Kenney под атлас KayKit — правильный ответ для
    //     продакшена и невозможный здесь: это работа в 3D-редакторе, а не
    //     числами в сборщике.
    //
    // ЧТО СДЕЛАНО. Одиннадцать материалов, по одному на имя краски Kenney, и
    // цвет КАЖДОГО взят пипеткой из атласа KayKit — не подобран на глаз, а
    // считан из `spacebits_texture.png` по центру соответствующей плашки
    // (сетка 8x4, плашка 128 px). Шейдер, шероховатость и металличность — те
    // же, что у `spacebits.mat`. То есть общего у наборов не «похожий вид», а
    // буквально один набор чисел и один режим освещения.
    //
    // Пипетка (плашка -> RGB, sRGB 0..255):
    //   (0,1) 212,219,222  светлый металл   (0,2) 129,140,145  полутон
    //   (0,3)  74, 81, 85  темный металл    (0,4)  51, 51, 51  почти черный
    //   (0,5) 177,111, 82  порода           (0,6) 155, 90, 69  порода темная
    //   (0,7) 195,101, 50  рыжая порода     (3,2) 249,170, 78  золото-акцент
    //   (2,5) 231,205,180  кость            (1,5) 218,174,125  песок
    //   (3,6)  41,168,224  стекло/лед

    private const string KenneyMatDir = "Assets/Kit/kenney";

    /// <summary>Цвет из плашки атласа: sRGB 0..255 в Color.</summary>
    private static Color Swatch(int r, int g, int b) =>
        new Color(r / 255f, g / 255f, b / 255f);

    /// <summary>
    /// Имя краски Kenney -> цвет из атласа KayKit.
    ///
    /// `metalRed` у Kenney на самом деле не красный, а оранжевый (255,160,52) —
    /// это его главный акцент на ангарах и технике. Он уводится в золото KayKit
    /// (249,170,78), то есть в тот же цвет, которым покрашен купол жилого блока.
    /// Так новый транспорт оказывается одной семьи со старой застройкой, а не
    /// просто «рядом с ней».
    /// </summary>
    private static readonly (string name, Color color)[] KenneyPalette =
    {
        ("metal", default),
        ("metalDark", default),
        ("metalRed", default),
        ("dark", default),
        ("rock", default),
        ("rockDark", default),
        ("rockTrack", default),
        ("crystal", default),
        ("bone", default),
        ("skin", default),
        ("_defaultMat", default),
    };

    private static Color KenneyColor(string name) =>
        name switch
        {
            "metal" => Swatch(212, 219, 222),
            "metalDark" => Swatch(129, 140, 145),
            "metalRed" => Swatch(249, 170, 78),
            "dark" => Swatch(74, 81, 85),
            // Была плашка (0,5) 177,111,82 — «порода» нейтрального тона.
            // Замерено по кадру: реальные камни KayKit (`rock_A`/`rock_B`)
            // рендерятся заметно ярче и рыжее, ~(250,147,82) на освещенной
            // грани. Плашка (0,7) «рыжая порода» (195,101,50) в том же атласе
            // ближе к этому по насыщенности (S 0.74 против 0.54 у старой) и
            // тону — она и была в пипетке кода, но не назначена ни одному
            // имени. `rockDark` пересчитана тем же множителем затенения, что
            // был у старой пары (0.876/0.811/0.841 по каналам), а не
            // подобрана заново — иначе крупные камни и метеориты, у которых
            // много граней помечено `rockDark`, снова читались бы тусклой
            // серо-бурой массой на фоне насыщенного оранжевого грунта.
            "rock" => Swatch(195, 101, 50),
            "rockDark" => Swatch(171, 82, 42),
            // Колея темнее грунта НАМЕРЕННО — контраст с дорогой нужен, и это
            // остается так. Но 107,92,83 (S=0.22) — недоделка предыдущей
            // правки: перекрашены были только `rock` и `rockDark`, а
            // `rockTrack` остался нейтрально-серым от старой партии. Четыре
            // объекта в сцене (`meteor_half`, `meteor_detailed`, `meteor` x2)
            // по `kenney-bounds.json` состоят ТОЛЬКО из `rockTrack` — на них
            // это не полоска на дороге, а весь видимый цвет объекта, и они
            // читались серыми камнями среди перекрашенных теплых соседей
            // (замер инспектора: S 0.33-0.41 против 0.63-0.66).
            // Новое значение — та же пропорция затенения (0.70 по каналам),
            // что увела `rock` в `rockDark`, примененная еще раз: цвет
            // остается тем же теплым рыжим семейством (S=0.76, тот же тон,
            // что у rock/rockDark), но темнее rockDark — контраст колеи с
            // дорогой не теряется, а объекты из чистого rockTrack больше не
            // выпадают в серое.
            "rockTrack" => Swatch(120, 57, 29),
            "crystal" => Swatch(41, 168, 224),
            "bone" => Swatch(231, 205, 180),
            "skin" => Swatch(218, 174, 125),
            _ => Swatch(212, 219, 222),
        };

    private static Dictionary<string, Material> _kenney;

    /// <summary>
    /// Заводит материалы палитры как ассеты и складывает их в словарь по имени.
    /// </summary>
    private static Dictionary<string, Material> PrepareKenneyPalette()
    {
        Directory.CreateDirectory(KenneyMatDir);
        var map = new Dictionary<string, Material>();
        foreach (var (name, _) in KenneyPalette)
        {
            string path = $"{KenneyMatDir}/kenney_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find("Standard");
            material.mainTexture = null;
            material.color = KenneyColor(name);
            // Те же числа, что у `spacebits.mat`. Разойдись они — два набора
            // разъедутся по блику, даже совпав по цвету.
            material.SetFloat("_Glossiness", 0.18f);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            map[name] = material;
        }
        AssetDatabase.SaveAssets();
        return map;
    }

    /// <summary>
    /// Ставит модель Kenney, раздавая краску ПО ИМЕНИ материала подмеша.
    ///
    /// Имя читается у уже импортированного материала, а не берется из таблицы
    /// по номеру слота: порядок подмешей задает импортер FBX, и таблица по
    /// номеру разъедется на первой же модели, где он другой. Неизвестное имя —
    /// не молчаливая подмена, а ошибка в логе: молча покрашенный «не тем»
    /// объект в кадре не отличить от задуманного.
    /// </summary>
    private static GameObject PutK(
        string name,
        Vector3 pos,
        float rot_y = 0f,
        Transform parent = null
    )
    {
        var prefab = LoadKenney(name);
        if (prefab == null)
        {
            Debug.LogWarning($"[3d] нет модели Kenney {name}");
            return null;
        }

        var go = (GameObject)Object.Instantiate(prefab);
        go.name = name;
        go.transform.SetParent(parent ?? _root, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, rot_y, 0f);

        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            var src = r.sharedMaterials;
            var slots = new Material[Mathf.Max(1, src.Length)];
            for (int i = 0; i < slots.Length; i++)
            {
                string mat = i < src.Length && src[i] != null ? src[i].name : "_defaultMat";
                // Импортер иногда дописывает суффикс инстанса.
                mat = mat.Replace(" (Instance)", "").Trim();
                if (!_kenney.TryGetValue(mat, out var painted))
                {
                    Debug.LogError(
                        $"[3d] {name}: краски «{mat}» нет в палитре Kenney — объект покрашен металлом"
                    );
                    painted = _kenney["metal"];
                }
                slots[i] = painted;
            }
            r.sharedMaterials = slots;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
        }
        return go;
    }

    /// <summary>Модель Kenney подошвой на высоту y, координаты в клетках KayKit.</summary>
    private static GameObject PutKOn(string name, float i, float j, float y, float rot_y = 0f)
    {
        var go = PutK(name, Cell(i, j, y), rot_y);
        if (go == null)
            return null;
        var list = go.GetComponentsInChildren<Renderer>();
        if (list.Length == 0)
            return go;
        Bounds b = list[0].bounds;
        foreach (var r in list)
            b.Encapsulate(r.bounds);
        go.transform.position += new Vector3(0f, y - b.min.y, 0f);
        return go;
    }

    /// <summary>
    /// Замер набора Kenney: габариты, подошва, имена материалов по слотам.
    ///
    /// Отдельный файл `kenney-bounds.json`, а не дозапись в `kit-bounds.json`:
    /// это разные наборы с разной единицей длины, и путать их в одной таблице
    /// значит однажды взять размер KayKit для модели Kenney.
    /// </summary>
    public static void AuditKenney()
    {
        PrepareKenneyModels();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var lines = new List<string>();
        var palette = new SortedDictionary<string, int>();
        foreach (string path in Directory.GetFiles(KenneyDir, "*.fbx").OrderBy(p => p))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var prefab = LoadKenney(name);
            if (prefab == null)
            {
                lines.Add($"  \"{name}\": null");
                continue;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;

            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                lines.Add($"  \"{name}\": null");
                Object.DestroyImmediate(instance);
                continue;
            }

            Bounds b = renderers[0].bounds;
            var slots = new List<string>();
            foreach (var r in renderers)
            {
                b.Encapsulate(r.bounds);
                foreach (var m in r.sharedMaterials)
                {
                    string mat = m == null ? "НЕТ" : m.name;
                    slots.Add(mat);
                    palette[mat] = palette.TryGetValue(mat, out int n) ? n + 1 : 1;
                }
            }

            lines.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "  \"{0}\": {{ \"size\": [{1:F3}, {2:F3}, {3:F3}], "
                        + "\"min_y\": {4:F3}, \"center\": [{5:F3}, {6:F3}, {7:F3}], "
                        + "\"grid\": [{8:F2}, {9:F2}], \"slots\": [{10}] }}",
                    name,
                    b.size.x,
                    b.size.y,
                    b.size.z,
                    b.min.y,
                    b.center.x,
                    b.center.y,
                    b.center.z,
                    b.size.x / Grid,
                    b.size.z / Grid,
                    string.Join(", ", slots.Select(s => $"\"{s}\""))
                )
            );
            Object.DestroyImmediate(instance);
        }

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"_note\": \"Kenney Space Kit, CC0. globalScale в импортере = "
            + KenneyScale.ToString("F1", CultureInfo.InvariantCulture)
            + ", размеры ниже уже с ним. grid — доля клетки 2.0 по X и Z.\",");
        sb.AppendLine("  \"_palette\": {");
        sb.AppendLine(
            string.Join(",\n", palette.Select(p => $"    \"{p.Key}\": {p.Value}"))
        );
        sb.AppendLine("  },");
        sb.AppendLine(string.Join(",\n", lines));
        sb.AppendLine("}");
        File.WriteAllText("kenney-bounds.json", sb.ToString());
        Debug.Log(
            $"[3d] Kenney: габариты {lines.Count} моделей, палитра из {palette.Count} имен "
                + "-> kenney-bounds.json"
        );
        EditorApplication.Exit(0);
    }

    // ---- Раскладка ---------------------------------------------------------
    //
    // Все числа ниже взяты из `kit-bounds.json`, а не из головы.
    //
    //   * Шаг сетки 2.0 — ровно столько занимает любой тайл рельефа
    //     (`terrain_*` все ровно 2.000 x 2.000).
    //   * Пивот здания в основании (`min_y = 0.000` у всех построек), значит
    //     здание ставится на ту же высоту, что и верх тайла, без подгонки.
    //   * Верх низкого тайла — y = 0 (тайл занимает -1..0), верх высокого —
    //     y = +1 (тайл занимает -1..+1). Отсюда две высоты застройки и ровно
    //     одна ступень между ними.
    //   * Масштаб не трогается нигде. Набор нарисован в одном масштабе, и это
    //     измерено, а не принято на веру.
    //
    // Третий ярус, DeepTop. В сцене было ровно два уровня, и оба не ниже нуля
    // — карьер («M» на карте) физически строился приподнятым плато: тайл
    // `terrain_mining` ставился, как и все прочие, пивотом на 0, и его
    // собственная геометрия (`kit-bounds.json`: min_y −1.000, size.y 2.073)
    // давала верх 1.073 — выше соседнего плато. Ямы там нет и не может
    // читаться, сколько тайл ни двигай по сетке (проверено дважды, прогоны 4
    // и 5): дело не в раскладке, а в том, что уровня НИЖЕ нуля в системе
    // просто не существовало.
    //
    // DeepTop — на тот же шаг 1.0, что и HighTop от LowTop, только вниз:
    // LowTop 0, HighTop +1, DeepTop −1. Ровная лестница уровней, а не
    // случайное число. Тайлы этого яруса (пандус в `BuildTerrain`)
    // используют этот же прием, каким уже сделан переход Low<->High — тот же
    // `terrain_slope`, тот же пивот-на-0, та же логика полки, — просто
    // упирается низким краем в DeepTop вместо LowTop, потому что ставится
    // рядом с плато, а не рядом с низкой землёй.
    //
    // Дно самого карьера (модель `terrain_mining`) — особый случай: её
    // геометрия скроена по образцу «высокого» тайла (низ −1, верх +1
    // локально) плюс decorative нарост +0.073 (рудная жила, торчащая из
    // дна). Чтобы этот верх ушёл под ноль вместе с тайлом, пивот сдвинут не
    // на DeepTop, а на DeepTop минус тот же локальный офсет +1, которым эта
    // геометрия поднимается от своего пивота к «верху» (см. BuildTerrain).
    // Результат — дно на DeepTop, нарост чуть выше него, оба ниже нуля.

    private const float Grid = 2.0f;
    private const float LowTop = 0f;
    private const float HighTop = 1f;
    private const float DeepTop = -1f;

    private static Material _kit;
    private static Material _plateau;
    private static Material _pit;
    private static Material _pitWall;
    private static Material _solar;
    private static Material _glass;
    private static Transform _root;

    /// <summary>Центр клетки (i, j) сетки в мировых координатах. Карта 8x8.</summary>
    private static Vector3 Cell(float i, float j, float y) =>
        new Vector3((i - 3.5f) * Grid, y, (j - 3.5f) * Grid);

    /// <summary>
    /// Ставит модель. Материал раздается ЗДЕСЬ, на каждый подмеш: импорт
    /// материалов отключен, и без этой строки сцена приезжает белой.
    /// </summary>
    private static GameObject Put(string name, Vector3 pos, float rot_y = 0f, Transform parent = null)
    {
        var prefab = LoadModel(name);
        if (prefab == null)
        {
            Debug.LogWarning($"[3d] нет модели {name}");
            return null;
        }

        var go = (GameObject)Object.Instantiate(prefab);
        go.name = name;
        go.transform.SetParent(parent ?? _root, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, rot_y, 0f);

        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            var slots = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = _kit;
            r.sharedMaterials = slots;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
        }
        return go;
    }

    private static GameObject PutCell(string name, float i, float j, float y, float rot_y = 0f) =>
        Put(name, Cell(i, j, y), rot_y);

    /// <summary>
    /// Ставит модель ПОДОШВОЙ на заданную высоту, а не пивотом.
    ///
    /// У построек `min_y = 0`, и разницы нет. У техники его нет: `spacetruck`
    /// уходит на −0.078 ниже пивота, `spacetruck_large` на −0.134, прицеп на
    /// −0.143 — поставленные «на ноль», они втоплены в грунт колесами. Высота
    /// не берется из таблицы: она МЕРЯЕТСЯ у уже поставленного объекта с учетом
    /// поворота, и подъем считается от нее. Таблица разошлась бы с набором на
    /// первой же замене модели.
    /// </summary>
    private static GameObject PutOn(string name, float i, float j, float y, float rot_y = 0f)
    {
        var go = Put(name, Cell(i, j, y), rot_y);
        if (go == null)
            return null;

        var list = go.GetComponentsInChildren<Renderer>();
        if (list.Length == 0)
            return go;
        Bounds b = list[0].bounds;
        foreach (var r in list)
            b.Encapsulate(r.bounds);

        go.transform.position += new Vector3(0f, y - b.min.y, 0f);
        return go;
    }

    /// <summary>
    /// Рельеф. Карта читается построчно: строка — ряд по оси Z, символ в строке
    /// — шаг по оси X.
    ///   T — плато (верх +1), M — дно карьера (верх ниже 0, см. DeepTop),
    ///   S — съезд с поворотом 90 (между T и L), Q — съезд с поворотом 0,
    ///   R — съезд с поворотом 90 (Q и R — оба между L и дном карьера M,
    ///   см. DeepTop, разница только в повороте под сторону света соседней
    ///   ямы), L — низ (верх 0), . — пусто
    ///
    /// Карьер несколько раз переставлен внутри плато, и каждый раз число
    /// («мировой верх дна ниже нуля») сходилось, а кадр — нет: геометрия
    /// была верной с первой попытки, задача оказалась в том, ЧТО стоит перед
    /// тайлом на экране. Проверено буфером номеров (`colony3d-ids.png`) —
    /// сколько пикселей своего цвета у тайла реально доходит до кадра, а не
    /// сколько экранный бокс формально накрывает:
    ///   (6,5): 0 пикселей. Экранный бокс тайла почти целиком внутри бокса
    ///     ракеты на стартовом столе (`rocket_baseA`..`rocket_topB`, высота
    ///     до y=4.9) — перекрытие 156 px по Y из 211.
    ///   (7,6): тоже 0. Перекрытие с той же ракетой выросло, а не упало (165
    ///     px из 211) — экранный бокс дна растет вниз вместе с глубиной, и
    ///     чем глубже яма, тем больше ее тень на экране заезжает под ракету.
    ///   (4,6): 293 из 1.6М пикселей кадра — тоже фактически ноль. Ракета
    ///     тут ни при чем (боксы не пересекаются по X вовсе), перекрывает
    ///     жилой блок (`basemodule_B`, `basemodule_E`, переход, ангар) — эта
    ///     клетка стоит на стыке плато и жилого блока и по (x−z) проецируется
    ///     в ту же колонну.
    ///   (4,7): 12347 пикселей у тайла ДО правки (та же клетка, пока была
    ///     обычным `terrain_tall_curved`) — на два порядка больше, чем у
    ///     всех трех предыдущих клеток вместе. Ни ракета, ни жилой блок ее
    ///     не касаются: край плато, дальше по диагонали от них обоих.
    ///
    /// НАЙДЕННЫЙ БАГ (прогон 7, ночной узел 2, art-inspector/tester): число
    /// 12347 px для (4,7) выше — это площадь ПЛОСКОГО тайла `terrain_tall_curved`
    /// ДО того, как клетка стала ямой. У ямы другой силуэт (уходит вниз, а не
    /// вверх), и свежий замер тем же способом на готовой геометрии дал не
    /// 12347, а 3433 px из 39286 бокса — 8.7%. Купол `basemodule_E` один
    /// закрывает 12946 px в боксе тайла (id-буфер, `colony3d-ids.png`) — то
    /// есть купол физически стоит НА ТОЙ ЖЕ экранной диагонали, что и карьер:
    /// экранная координата X при этой камере зависит только от `(x − z)`
    /// (см. ниже), и у купола `x−z ∈ [-8.317, -3.817]`, у клетки (4,7)
    /// `x−z ∈ [-8, -4]` — почти то же самое. Старая запись выше сравнивала
    /// клетку-кандидата только с ракетой и жилым блоком ПО ЦЕНТРУ, до того как
    /// тайл стал ямой, и не пересчитала после — купол оказался единственным,
    /// кого не проверили заново.
    ///
    /// `(6,6)` для новой попытки не годится: это же плато уже перебрано ДО
    /// (4,7) в этой самой правке (см. комментарий у `BuildMining` — карьер
    /// стоял в (6,6) первым и «исчезал целиком» за буровой на той же
    /// диагонали (i−j=0), затем пробовались (6,5) и (7,6) — блокирует ракета,
    /// затем (4,6) — блокирует тот же жилой блок). Свободной клетки плато вне
    /// диагоналей ракеты/буровой/жилого блока в этой раскладке нет: плато
    /// 4x3 занято четырьмя объектами разной высоты, и каждый держит свою
    /// полосу `(x−z)`.
    ///
    /// ПОПРАВКА К СОБСТВЕННОМУ РАЗБОРУ (тот же прогон, следующий заход):
    /// более раннее «по всему острову диагонали идут сплошным покрытием, нет
    /// просвета» было НЕВЕРНО — проверялось только пересечение диапазонов
    /// `(x−z)`, без учета глубины `D`. Два объекта могут стоять в одной
    /// экранной колонне и НЕ мешать друг другу, если один из них дальше
    /// кандидата по `D` (тогда он не ближе камеры и не может его закрыть).
    /// Пересчитано для ВСЕХ клеток рельефа разом, уже С учетом `D`, без
    /// рендера каждой — метод проверен на всех четырех уже известных
    /// клетках ((6,5),(7,6),(4,6),(4,7) — расчет дал те же нули/8.7%, что и
    /// история). Нашлась `(3,1)`: расчетная видимость 75.5%, ни одного
    /// объекта на ее экранной колонне ближе камеры вообще (пустая
    /// низкая земля у переднего края острова, не плато и не жилой блок).
    /// Карьер переехал туда. Таблица всех клеток и обоснование выбора —
    /// `scene-artist-2.md`.
    ///
    /// На (4,7), пока карьер стоял там, была ПРОВЕРЕНА и ОПРОВЕРГНУТА
    /// гипотеза, что бОльшая глубина ямы сама по себе откроет ее из-под
    /// купола `basemodule_E` (стоял в той же колонне `(x−z)`, ближе камеры
    /// на любой глубине дна — три рендера подряд дали 8.7% → 3.9% → 0.1%
    /// видимости при углублении, монотонно хуже, не лучше). На `(3,1)`
    /// этого ограничения нет — купола-соперника в колонне не осталось,
    /// поэтому пивот здесь стоит на полную глубину замысла (`DeepTop − 1 =
    /// −2.0`), без компромисса. См. `BuildTerrain`.
    ///
    /// Куда что попадет на экране — считается, а не угадывается. При повороте
    /// камеры 30/45 экранное «вправо» пропорционально (x − z), а экранное
    /// «вверх» — (x + z) и мировая высота y (регрессия по дампу сцены,
    /// прогон 7: `screenX = 46.53*(x−z) + 799.8`,
    /// `screenY = 592.6 − 23.27*(x+z) − 56.98*y`, проверено на контрольных
    /// точках дословно). Отсюда карта читается ромбом:
    ///   клетка (0,0) — низ кадра, (6,6) — верх, (6,0) — правый угол,
    ///   (0,6) — левый.
    /// Плато поэтому стоит в дальнем углу (большие i и j): в первом прогоне оно
    /// стояло при малых i и j, оказалось на переднем плане и перекрыло собой
    /// половину колонии — ось глубины в изометрии проецируется в вертикаль,
    /// и все, что стоит по диагонали x=z, выстраивается в одну колонну.
    ///
    /// Съезд `terrain_slope` без поворота спускается в сторону +x
    /// (измерено, `kit-terrain.txt`). Плато здесь справа от съезда, значит
    /// съезду нужен разворот на 180.
    /// </summary>
    private static readonly string[] TerrainMap =
    {
        "....cb..",
        "...MLLLb",
        "..cRLLLL",
        ".cLLLLLL",
        ".LLLLLCS",
        ".LLLSTTT",
        ".LLLTTTT",
        "..dLhTTe",
    };

    /// <summary>
    /// Модель и разворот тайла.
    ///
    /// Скругленный тайл — это квадрат 2x2, у которого срезан ОДИН угол, радиус
    /// 1.0, и срезан он со стороны (+x,+z) (измерено: вершин в этом углу ноль,
    /// диагональ (x+z) дотягивает только до 1.00 вместо 1.41). Поворот выбирает,
    /// куда смотрит скругление: 0 — (+x,+z), 90 — (+x,−z), 180 — (−x,−z),
    /// 270 — (−x,+z). Ставится он только на ВЫПУКЛЫЙ угол острова, иначе
    /// в застройке появится дыра.
    ///
    /// Съезд `terrain_slope` без поворота спускается в сторону +x; поворот 90
    /// разворачивает его высокой стороной к +z, то есть вниз он идет к −z —
    /// от плато к городу. Внешний угол съезда при 180 высок в углу (+x,+z) и
    /// спускается сразу в две стороны, −x и −z: это угол обрыва, а не пандус.
    /// </summary>
    private static (string model, float rot) Tile(char c) =>
        c switch
        {
            'T' => ("terrain_tall", 0f),
            'e' => ("terrain_tall_curved", 0f),
            'h' => ("terrain_tall_curved", 270f),
            'M' => ("terrain_mining", 0f),
            'S' => ("terrain_slope", 90f),
            // Тот же ассет, что у 'S', без поворота: низкая сторона у
            // terrain_slope без поворота смотрит на +x (измерено,
            // kit-terrain.txt), и это ровно то, что нужно съезду в карьер —
            // его дно стоит соседом по +x. 'S' переиспользовать с другим
            // поворотом нельзя: Tile(c) дает один поворот на символ, а два
            // существующих 'S' уже используют 90 для своей пары соседей.
            // Пивот у 'Q' не 0, а DeepTop — см. BuildTerrain: это полка
            // Low<->Deep, а не Low<->High, и стоит она на своем уровне.
            // (Прогон 7, ночной узел 2: карьер съехал на (3,1), клетка (3,7)
            // с 'Q' вернулась в обычную низкую землю — символ 'Q' сейчас
            // нигде не используется, оставлен рабочим на случай, если такой
            // же пандус понадобится снова с той же ориентацией соседей.)
            'Q' => ("terrain_slope", 0f),
            // 'R' — тот же прием, что и 'Q' (пандус Low<->Deep, пивот
            // DeepTop, см. BuildTerrain), но поворот 90, не 0: число уже
            // измерено и используется тайлом 'S' ("поворот 90 разворачивает
            // его высокой стороной к +z, то есть вниз он идет к −z" — низкая
            // сторона смотрит на −z). Нужен там, где яма стоит к пандусу не
            // с востока (+x, как было у 'Q'/(4,7)), а с севера (−z) — ровно
            // случай (3,1)/(3,2) в этом прогоне. Не новое измерение,
            // переиспользование поворота 'S' под пивот 'Q'.
            'R' => ("terrain_slope", 90f),
            'C' => ("terrain_slope_outer_corner", 180f),
            'c' => ("terrain_low_curved", 180f),
            'b' => ("terrain_low_curved", 90f),
            'd' => ("terrain_low_curved", 270f),
            _ => ("terrain_low", 0f),
        };

    /// <summary>Верхний слой грунта: плато и съезды тонируются отдельно.</summary>
    private const string UpperTiles = "TMehSC";

    private static void BuildTerrain(Transform parent)
    {
        for (int j = 0; j < TerrainMap.Length; j++)
        for (int i = 0; i < TerrainMap[j].Length; i++)
        {
            char c = TerrainMap[j][i];
            if (c == '.')
                continue;

            var (model, rot) = Tile(c);

            // Тайлы рельефа приходят с пивотом в центре плана, поэтому обычно
            // ставятся на y = 0 независимо от своей высоты: верх сам
            // оказывается там, где нужно (0 у низкой земли, +1 у плато — это
            // дает сама геометрия меша, не сдвиг пивота).
            //
            // Дно карьера (`M`, модель `terrain_mining`) — исключение. Его
            // меш скроен по образцу «высокого» тайла: локально верх на +1 от
            // пивота (тот же офсет, что дает HighTop при пивоте 0), плюс
            // decorative нарост +0.073. Чтобы этот верх ушел под DeepTop,
            // пивот сдвинут на DeepTop минус тот же локальный офсет +1, каким
            // эта геометрия поднимается к своему верху — та же арифметика,
            // какой HighTop = 0 (пивот) + 1 (офсет меша), только в minus:
            // пивот = DeepTop − 1 = −1 − 1 = −2.0.
            //
            // НАЙДЕННЫЙ БАГ (прогон 7, ночной узел 2, art-inspector):
            // формула в этом комментарии верна, но в свиче ниже стояло
            // число −1.15, не −2.0 — расхождение с собственным замыслом на
            // 0.85. Результат: верх тайла (с наростом) выходил на
            // `−1.15 + 1.073 = −0.077` — на 7.7 см ниже LowTop=0, то есть
            // ПОЧТИ вровень с низкой землей, а не с дном ямы на DeepTop=−1.
            //
            // НА КЛЕТКЕ (4,7) полный фикс (пивот −2.0) был временно ослаблен
            // до −1.45 (прогон 7, ночной узел 2) — числом доказано, что
            // купол `basemodule_E` стоит в одной экранной колонне `(x−z)` с
            // (4,7) и физически ближе камеры на ЛЮБОЙ глубине ямы (истинная
            // глубина камеры `D = 0.6124*(x+z) − 0.5*y`, `dD/dy = −0.5`
            // строго — чем ниже дно, тем дальше от камеры и тем МЕНЬШЕ видно,
            // проверено рендером трижды: −1.15→8.7%, −1.45→3.9%, −2.0→0.1%).
            //
            // Дальше (тот же прогон, следующий заход) карьер СИСТЕМНО
            // переставлен: формулы проекции и глубины посчитаны для ВСЕХ
            // клеток рельефа разом (без рендера каждой), и клетка `(3,1)`
            // дала 75.5% расчетной видимости без единого объекта на своей
            // экранной колонне — купола-соперника здесь просто нет.
            // Ограничение, из-за которого пивот держали на −1.45, было
            // локальным свойством клетки (4,7), а не карьера как такового.
            // На (3,1) пивот возвращен к полному замыслу: `DeepTop − 1 =
            // −2.0`, дно на DeepTop=−1, видимый перепад к низкой земле —
            // полный метр, а не минимально требуемые 0.35. Подробности,
            // таблица всех клеток и обоснование выбора — `scene-artist-2.md`.
            //
            // Съезд в карьер (`Q`/`R`) — второе исключение, и по другой
            // причине: его меш (`terrain_slope`) устроен так же, как у
            // обычного съезда `S` (низкий локальный край на 0, высокий на
            // +1), но ЭТОТ съезд стоит между низкой землей (0) и дном
            // карьера (DeepTop), а не между низкой землей и плато. Пивот
            // сдвинут на DeepTop, и полка съезда встает на нужный уровень
            // целиком — тот же самый пивот-сдвиг, каким уже сделан переход
            // Low<->High (пивот 0 для полки на 0..1), только для пары
            // Low<->Deep. `Q` и `R` — один и тот же прием с разным поворотом
            // (см. `Tile`), под разную сторону света соседней ямы.
            float pivot_y = c switch
            {
                'M' => -2.0f,
                'Q' => DeepTop,
                'R' => DeepTop,
                _ => 0f,
            };
            var tile = Put(model, Cell(i, j, pivot_y), rot, parent);
            if (tile == null)
                continue;
            tile.name = $"{model}_{i}_{j}";

            // Дно карьера тонируется отдельно и в темную сторону (см.
            // PreparePitMaterial) — граница яруса должна читаться числом
            // тона, а не только высотой. Проверяется ПЕРЕД общим верхним
            // слоем: 'M' входит в UpperTiles (карьер остается частью верхнего
            // яруса рельефа для прочей логики), но красится не в плато, а в
            // дно.
            if (c == 'M')
                foreach (var r in tile.GetComponentsInChildren<MeshRenderer>())
                    r.sharedMaterial = _pit;
            else if (UpperTiles.IndexOf(c) >= 0)
                // Верхний слой грунта тонируется отдельно — иначе плато и
                // нижняя земля сливаются в одно оранжевое пятно.
                foreach (var r in tile.GetComponentsInChildren<MeshRenderer>())
                    r.sharedMaterial = _plateau;
        }
    }

    /// <summary>
    /// Объем карьера, прогон 8, ИТЕРАЦИЯ 2 — стены геометрией (`terrain_mining`
    /// сам один рендерер с одним слотом материала, проверено зондом в итерации
    /// 1, разводить дно/стену перекраской того же меша нельзя).
    ///
    /// Клетка (3,1): юг (j=0,i=3) и запад (i=2,j=1) в `TerrainMap` пустые —
    /// край острова, и это ровно те две грани, что видит камера (`BuildCamera`:
    /// forward ≈ (0.61,−0.5,0.61), камера стоит в квадранте −x,−z, значит
    /// видит грани −z и −x объектов). Там стоит темная вставка «стена в тени»
    /// (`_pitWall`) — снаружи от границы тайла (в пустоте за краем острова),
    /// поэтому она не спорит по глубине с уже отрисованным мешем самой ямы.
    ///
    /// ПРАВКА итерации 2 (ломатель, находка «щель в углу»): исходные боксы
    /// стен были ВРЕЗАНЫ по 0.075 от истинных границ клетки с каждой стороны
    /// (южная X∈[−1.925,−0.075], западная X∈[−2.15,−1.95]) — угол не сходился
    /// на 0.025 юнита, сквозь щель было видно фон. Теперь оба бокса вытянуты
    /// ЗА истинную границу соседней стены с запасом: южная стена доходит до
    /// X=−2.20 (запад) и Z∈[−6.20,−5.90] (толщина 0.30, была 0.20), западная —
    /// до Z=−6.20 (юг) и X∈[−2.20,−1.90]. В углу (X∈[−2.20,−1.90],
    /// Z∈[−6.20,−5.90]) боксы теперь ДВОЙНО перекрываются, а не соприкасаются
    /// гранью — щели физически нет ни при каком допуске. Заодно стены стали
    /// глубже (Y от +0.05 до −1.90, было до −1.025) — это и есть честный
    /// потолок для находки 3 (рудный камень): «дно короба», которое видит
    /// зритель, теперь ниже, и камню есть куда садиться по-настоящему, а не
    /// на верхнюю четверть видимого объема.
    /// </summary>
    private static void BuildPitDetail(Transform parent)
    {
        // Центр клетки (3,1): x=(3−3.5)*2=−1, z=(1−3.5)*2=−5 (см. `Cell`).
        const float cx = -1f;
        const float cz = -5f;
        // Толщина стен и запас перекрытия в углу — оба бокса заходят за
        // истинную границу соседа минимум на WALL_THICK (было 0, отсюда щель).
        const float wallThick = 0.30f;
        const float wallTop = 0.05f;
        // ВАЖНО: глубина стены НЕ увеличена сверх итерации 1 (−1.03, было
        // −1.025) — первая попытка этой итерации подняла ее до −1.90 "для
        // объема" и СЛОМАЛА видимость дна: при камере 30°/45° (`BuildCamera`,
        // forward ≈ 0.61,−0.5,0.61) высокая ближняя стена своей же гранью
        // (2.2 x 2.85 после того фикса) закрывала ВЕСЬ обзор внутрь ямы —
        // рудный камень на честной опоре (см. `RaycastSurfaceY`) стал
        // "закрыт целиком, ноль видимых пикселей" (`check-scene3d.mjs`).
        // Оптика при 30° элевации не прощает: и объем стены, и видимость
        // содержимого разом не берутся одной эту же стеной — глубина
        // оставлена как в итерации 1, единственная правка здесь — ПЕРЕКРЫТИЕ
        // в углу (см. выше), а не высота.
        const float wallBottom = -1.03f;
        float wallH = wallTop - wallBottom;
        float wallCy = (wallTop + wallBottom) / 2f;

        MakeBox(
            "pit_wall_south",
            parent,
            new Vector3(cx - 0.075f, wallCy, cz - 1.05f),
            new Vector3(2.20f, wallH, wallThick),
            _pitWall
        );
        MakeBox(
            "pit_wall_west",
            parent,
            new Vector3(cx - 1.05f, wallCy, cz - 0.075f),
            new Vector3(wallThick, wallH, 2.20f),
            _pitWall
        );
    }

    /// <summary>Примитив-коробка без коллайдера — чистая раскладочная деталь.</summary>
    private static void MakeBox(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = size;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.On;
        r.receiveShadows = true;
        Object.DestroyImmediate(go.GetComponent<BoxCollider>());
    }

    /// <summary>
    /// Иней у карьера — прогон 8, итерация 2. Инспектор: плоский синий брус без
    /// фаски читается контейнером/ограждением, а не намерзанием, и вдобавок
    /// сам факт "гладкий примитив на фоне фасетчатого меша" — признак склейки.
    /// Замена: КРИСТАЛЛЫ из набора (`rock_crystals`/`rock_crystalsLargeA/B`,
    /// материал `crystal`, тот же холодный свотч 41,168,224, что уже используется
    /// в сцене как честный, фасетчатый, "в стиле" акцент). Ставятся как prop
    /// (родитель `_root` по умолчанию у `PutK`/`PutKOn`, НЕ группа `terrain`) —
    /// это настоящие объекты, которым нужны обычные проверки опоры/видимости,
    /// не вставки-стены.
    ///
    /// Три грани: восток — на твердой земле у ровной кромки (граница с (4,1));
    /// юг и запад — на верхнем торце новых стен (`BuildPitDetail`, y=0.05),
    /// тем же смыслом, каким иней нарастает по верхней кромке выработки.
    /// Разные модели (`rock_crystals`/`LargeA`/`LargeB`) — не одна и та же
    /// форма шесть раз подряд, читается россыпью, а не одним склеенным рядом.
    /// </summary>
    private static void BuildPitIce()
    {
        const float wallTop = 0.05f;
        const float scale = 1.4f;

        void Crystal(string name, float i, float j, float y, float rot)
        {
            var go = PutKOn(name, i, j, y, rot);
            if (go != null)
                go.transform.localScale *= scale;
        }

        // ИСТОРИЯ ЧИСЕЛ этого метода (три захода одного и того же прогона,
        // каждый со своим замером `measure-cold.mjs`):
        //   1. Шесть кристаллов в одном углу — footprint'ы легли друг на
        //      друга (дамп сцены: `support_kind` кристалла на кристалле).
        //      Разложено по граням, не по углу — 4 объекта, без масштаба:
        //      0.425% холодных, ниже приемки 0.6%.
        //   2. Масштаб 1.25х, потом 1.7х, плюс попытка добавить объекты в
        //      те же тесные промежутки — два новых сели под `landingpad_small`
        //      и на макушку соседнего кристалла. Даже без коллизий 1.7х дал
        //      только 0.487%: холодная огранка — маленькая доля площади
        //      самой модели (подмеш `crystal` у этих трех моделей — это
        //      грань-другая на макушке, не весь силуэт), и площадь растет
        //      КВАДРАТОМ масштаба медленнее, чем нужно для кратного роста.
        //   3. Рычаг, который отвечает на это числом — не масштаб одного
        //      кристалла, а ИХ ЧИСЛО: густой ряд по всем трем граням
        //      (шаг ~0.5-0.6, кристаллы касаются друг друга сомкнутой
        //      россыпью — это "подозрение" силуэтов, не "ложная опора",
        //      проверено пересборкой, false_stacks остается 0). Масштаб
        //      снижен обратно до 1.4 — без него более крупные кристаллы на
        //      таком шаге начинают закрывать друг друга (самоперекрытие
        //      гасит же прирост, который дает численность).
        Crystal("rock_crystalsLargeB", 3.43f, 1.0f, LowTop, 40f);
        Crystal("rock_crystals", 3.43f, 1.25f, LowTop, 320f);
        Crystal("rock_crystals", 3.43f, 0.75f, LowTop, 260f);

        Crystal("rock_crystalsLargeA", 2.7f, 0.48f, wallTop, 10f);
        Crystal("rock_crystals", 3.0f, 0.48f, wallTop, 340f);
        Crystal("rock_crystals", 3.25f, 0.48f, wallTop, 200f);
        Crystal("rock_crystals", 3.55f, 0.48f, wallTop, 100f);

        Crystal("rock_crystalsLargeB", 2.48f, 1.15f, wallTop, 80f);
        Crystal("rock_crystals", 2.48f, 0.85f, wallTop, 150f);
        Crystal("rock_crystals", 2.48f, 0.6f, wallTop, 210f);
    }

    /// <summary>
    /// Честная опора лучом на РЕАЛЬНУЮ геометрию тайла — прогон 8, итерация 2,
    /// находка 3 (инспектор): рудный камень стоял на y=−1.0, числе, подобранном
    /// по `DeepTop`, а не измеренном у самого меша `terrain_mining` (верх тайла
    /// −0.93, низ −3.0 по его собственному bounding box из `colony3d-scene.json`)
    /// — камень висел у обода, не на дне.
    ///
    /// ПЕРВАЯ версия (единственный луч из центра) нашла реальную поверхность,
    /// но не ту, что учитывает `check-scene3d.mjs`: меш `terrain_mining`
    /// оказался НЕ плоским внутри (терраса/уступ), один луч из центра
    /// футпринта попадал то под козырек (находка «в породе целиком»), то в
    /// локальную впадину мельче, чем сама официальная опора под всей
    /// подошвой (находка «утоплено» — `MeasureSupport` дальше по файлу мерит
    /// то же самое ПЯТЬЮ лучами по площади объекта и берет САМУЮ ВЫСОКУЮ
    /// поверхность, не любую под центром). Здесь — тот же метод, пять лучей
    /// по будущему следу объекта (центр + 4 угла на 0.4 экстента, тот же
    /// паттерн, что и в `MeasureSupport`), берется максимум — тогда опора,
    /// которую поставлю я, СОВПАДАЕТ с той, что потом посчитает официальная
    /// проверка, и красного «утоплено»/«в породе» не будет по построению, а
    /// не по счастливой случайности числа.
    /// </summary>
    private static GameObject PutKOnSurface(string name, Transform tile, float x, float z, float rot_y = 0f)
    {
        if (tile == null)
        {
            Debug.LogWarning($"[3d] опорный тайл не найден — {name} не поставлен честно");
            return null;
        }
        var added = AddTempColliders(tile);
        Physics.SyncTransforms();
        var go = PlaceOnMeasuredSurface(name, x, z, rot_y);
        foreach (var c in added)
            Object.DestroyImmediate(c);
        if (go == null)
            Debug.LogWarning($"[3d] луч опоры мимо {tile.name} у ({x},{z}) — {name} без честной опоры");
        return go;
    }

    private static List<Collider> AddTempColliders(Transform t)
    {
        var added = new List<Collider>();
        if (t == null)
            return added;
        foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null)
                added.Add(mf.gameObject.AddComponent<MeshCollider>());
        return added;
    }

    /// <summary>
    /// Ядро честной посадки: предполагает, что нужные коллайдеры УЖЕ навешаны
    /// вызывающим кодом (и снимет их сам он же) — пять лучей по будущему
    /// следу объекта, максимум, см. `PutKOnSurface`.
    /// </summary>
    private static GameObject PlaceOnMeasuredSurface(string name, float x, float z, float rot_y)
    {
        const float sky = 40f;
        float Probe(float px, float pz) =>
            Physics.Raycast(new Vector3(px, sky, pz), Vector3.down, out var hit, sky + 200f)
                ? hit.point.y
                : float.NegativeInfinity;

        float roughY = Probe(x, z);
        if (float.IsNegativeInfinity(roughY))
            return null;

        var go = PutK(name, new Vector3(x, roughY, z), rot_y);
        if (go == null)
            return null;
        var list = go.GetComponentsInChildren<Renderer>();
        if (list.Length == 0)
            return go;
        Bounds b = list[0].bounds;
        foreach (var r in list)
            b.Encapsulate(r.bounds);
        Vector3 c0 = b.center;
        Vector3 e0 = b.extents;

        float best = Mathf.Max(
            Probe(c0.x, c0.z),
            Probe(c0.x - e0.x * 0.4f, c0.z - e0.z * 0.4f),
            Probe(c0.x + e0.x * 0.4f, c0.z - e0.z * 0.4f),
            Probe(c0.x - e0.x * 0.4f, c0.z + e0.z * 0.4f),
            Probe(c0.x + e0.x * 0.4f, c0.z + e0.z * 0.4f)
        );
        if (float.IsNegativeInfinity(best))
            return go; // не должно случиться — грубый луч уже что-то нашел

        go.transform.position += new Vector3(0f, best - b.min.y, 0f);
        return go;
    }

    /// <summary>
    /// Позиция камеры — тот же расчет, что в `BuildCamera`
    /// (`target − forward*25`), продублирован числом здесь, потому что на
    /// момент расстановки добычи (`BuildCargo`/`BuildMonorail`) камеры в
    /// сцене еще нет (`BuildCamera` идет последним шагом `BuildScene`).
    /// </summary>
    private static readonly Vector3 ApproxCameraPos =
        new Vector3(1.5f, 0.4f, 1.5f) - new Vector3(0.6124f, -0.5f, 0.6124f) * 25f;

    /// <summary>
    /// Честная опора (см. `PutKOnSurface`) плюс проверка видимости из камеры —
    /// прогон 8, итерация 2, второй заход находки 3. Первая версия (один
    /// луч из центра, потом пять лучей по площади) находила настоящую
    /// поверхность, но не проверяла, видна ли она вообще: три подряд честные
    /// точки внутри плотного кольца кристаллов (`BuildPitIce`, находка 1)
    /// оказались перекрыты этим же кольцом — камень был "закрыт целиком, ноль
    /// видимых пикселей" (`check-scene3d.mjs`) при абсолютно честной посадке.
    /// Вместо очередной точки на глаз — перебор кандидатов, каждый проверен
    /// ЛУЧОМ ОТ КАМЕРЫ (`Physics.RaycastAll`, с временными коллайдерами на
    /// тайле, стенах и уже стоящих кристаллах) до его же центра: если что-то
    /// стоит ближе камеры, кандидат отбрасывается, объект удаляется, пробуется
    /// следующий. Первый одновременно честный и видимый — остается.
    /// </summary>
    private static GameObject PutKOnSurfaceFirstVisible(
        string name,
        Transform tile,
        Transform[] supportBlockers,
        Transform[] sightBlockers,
        (float x, float z)[] candidates,
        float rot_y = 0f
    )
    {
        // Опора (пол/стены) и загородки-для-взгляда (кристаллы кромки) —
        // РАЗНЫЕ множества намеренно. Первая версия добавляла кристаллы и в
        // опору тоже: честный поиск исправно находил поверхность, но иногда
        // ей оказывалась макушка соседнего кристалла (`support_kind=prop`) —
        // технически не висит, но по смыслу камень садился на иней, а не на
        // дно выработки. Дно ищем только по тайлу и стенам; кристаллы мешают
        // только ВИДЕТЬ, не служат опорой.
        var supportColliders = AddTempColliders(tile);
        foreach (var b in supportBlockers)
            supportColliders.AddRange(AddTempColliders(b));
        Physics.SyncTransforms();

        GameObject placed = null;
        string reason = "нет ни одной честной точки";
        foreach (var (x, z) in candidates)
        {
            var go = PlaceOnMeasuredSurface(name, x, z, rot_y);
            if (go == null)
                continue;

            var sightColliders = new List<Collider>();
            foreach (var b in sightBlockers)
                sightColliders.AddRange(AddTempColliders(b));
            Physics.SyncTransforms();

            var list = go.GetComponentsInChildren<Renderer>();
            Vector3 center = go.transform.position;
            if (list.Length > 0)
            {
                Bounds b = list[0].bounds;
                foreach (var r in list)
                    b.Encapsulate(r.bounds);
                center = b.center;
            }

            Vector3 dir = center - ApproxCameraPos;
            float dist = dir.magnitude;
            bool blocked = false;
            if (dist > 0.05f)
            {
                var hits = Physics.RaycastAll(new Ray(ApproxCameraPos, dir.normalized), dist - 0.05f);
                foreach (var h in hits)
                    if (!h.collider.transform.IsChildOf(go.transform))
                    {
                        blocked = true;
                        break;
                    }
            }

            // Временные коллайдеры взгляда снимаются КАЖДУЮ итерацию, в обоих
            // исходах: иначе коллайдер неудавшегося кандидата доживает до
            // следующего луча и глушит его ложным заслоном.
            foreach (var c in sightColliders)
                Object.DestroyImmediate(c);

            if (!blocked)
            {
                placed = go;
                break;
            }
            reason = $"кандидат ({x},{z}) честный, но перекрыт с камеры";
            Object.DestroyImmediate(go);
        }

        foreach (var c in supportColliders)
            Object.DestroyImmediate(c);

        if (placed == null)
            Debug.LogWarning($"[3d] {name}: {reason} — не поставлен");
        return placed;
    }

    /// <summary>
    /// Накатанные колеи по грунту.
    ///
    /// Дорога здесь — не украшение, а единственный СВЯЗНЫЙ элемент, который
    /// ничего не загораживает: она лежит в плоскости земли. Пустой ровный грунт
    /// между постройками и есть то, из-за чего кадр читается витриной; колея
    /// говорит, что между ними ездят.
    ///
    /// Три числа, на которых это держится.
    ///   * Тайл дороги 1.000 x 1.000, то есть ПОЛКЛЕТКИ KayKit. Шаг по i и j —
    ///     0.5, иначе между тайлами будет разрыв в клетку.
    ///   * Колея внутри тайла идет по оси Z (замерено: полоса `rockTrack`
    ///     занимает x ∈ [−0.25, 0.25] при z ∈ [−0.5, 0.5]). Поворот 0 кладет
    ///     дорогу вдоль мировой Z, поворот 90 — вдоль X.
    ///   * Подъем 0.02 над верхом тайла. Ноль дал бы совпадающие плоскости и
    ///     мерцание, а 0.05 и выше проверка опоры уже считает «висит».
    ///
    /// Тайлы кладутся в группу `terrain`, а не в корень: они и есть поверхность,
    /// и проверки раскладки должны считать их рельефом, а не постройкой. Иначе
    /// каждый объект, стоящий на дороге, поедет в отчет как «стоит на предмете».
    /// </summary>
    private static void BuildRoads(Transform parent)
    {
        // Подъем 0.008, а не 0.02.
        //
        // 0.02 стоил пяти красных из тринадцати, и все пять — одна и та же
        // ошибка: дорога классифицируется РЕЛЬЕФОМ, и любой предмет, стоящий на
        // грунте рядом с колеей, отчитывался «утоплен в породу на 0.02..0.03».
        // Проверка права: подошва предмета действительно ниже верха дорожного
        // тайла. Лечится не порогом проверки, а высотой подъема — она должна
        // быть меньше допуска на утопание (0.02), но больше разрешения буфера
        // глубины. Проекция здесь ортографическая, то есть глубина ЛИНЕЙНА:
        // при дальности 80 и буфере даже в 16 бит шаг составляет 0.0012, и
        // 0.008 отстоит от мерцания в семь раз.
        const float lift = 0.008f;

        // Магистраль вдоль мировой X. Западный конец — на i = 2.5, а не дальше:
        // тайл шириной 1.0 в клетке 2.0 при i = 1.5 наполовину повис бы над
        // пустотой, клетка (1,2) на карте рельефа пустая.
        for (float i = 2.5f; i <= 7.01f; i += 0.5f)
            PutK("terrain_roadStraight", Cell(i, 2.5f, LowTop + lift), 90f, parent);
        PutK("terrain_roadEnd", Cell(2.0f, 2.5f, LowTop + lift), 270f, parent);

        // Съезд к стартовому столу: перекресток на магистрали и тайл к нему.
        PutK("terrain_roadCross", Cell(5.5f, 2.5f, LowTop + lift), 0f, parent);
        PutK("terrain_roadStraight", Cell(5.5f, 3.0f, LowTop + lift), 0f, parent);

        // Верхняя площадка: колея от съезда к карьеру, на высоте плато.
        for (float i = 4.25f; i <= 5.26f; i += 0.5f)
            PutK("terrain_roadStraight", Cell(i, 6.0f, HighTop + lift), 90f, parent);
    }

    /// <summary>Высота верха тайла под клеткой — чтобы не ставить дом в воздух.</summary>
    private static float TopAt(int i, int j)
    {
        if (j < 0 || j >= TerrainMap.Length || i < 0 || i >= TerrainMap[j].Length)
            return LowTop;
        char c = TerrainMap[j][i];
        return "TMeh".IndexOf(c) >= 0 ? HighTop : LowTop;
    }

    // ---- Сцена -------------------------------------------------------------

    public static void BuildScene()
    {
        PrepareTexture();
        _kit = PrepareMaterial();
        _plateau = PreparePlateauMaterial();
        _pit = PreparePitMaterial();
        _pitWall = PreparePitWallMaterial();
        _solar = PrepareSolarMaterial();
        _glass = PrepareGlassMaterial();
        Material ground_material = PrepareGroundMaterial();
        PrepareModels();
        PrepareKenneyModels();
        _kenney = PrepareKenneyPalette();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var root_go = new GameObject("Colony3D");
        _root = root_go.transform;

        BuildLighting();
        BuildGround(ground_material);

        var terrain = new GameObject("terrain").transform;
        terrain.SetParent(_root, false);
        BuildTerrain(terrain);
        BuildPitDetail(terrain);
        BuildRoads(terrain);
        BuildPitIce();

        BuildMining();
        BuildHabitat();
        BuildCargo();
        BuildLandingPads();
        BuildPower();
        BuildScatter();
        BuildKenneyScatter();

        BuildCamera();

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);

        int meshes = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Length;
        Debug.Log($"[3d] сцена собрана: {ScenePath}, рендереров: {meshes}");
    }

    /// <summary>
    /// Свет и среда.
    ///
    /// Угол света подобран так, чтобы из двух видимых камерой стен одна была
    /// освещена, а вторая ушла в полутень: именно эта разница и читается как
    /// объем. Тень при этом ложится вдоль экрана вправо — то есть она видна,
    /// а не прячется за самим зданием.
    /// </summary>
    private static void BuildLighting()
    {
        var light_go = new GameObject("Sun");
        light_go.transform.SetParent(_root, false);
        var light = light_go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(45f, 130f, 0f);
        light.color = new Color(1f, 0.94f, 0.84f);
        light.intensity = 1.28f;

        // Тени включаются в трех местах, и пропуск любого гасит их целиком:
        // здесь, в уровнях качества (см. ApplyShadowQuality) и в настройках
        // самой камеры. В WebGL это ломается отдельно от редактора.
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.82f;
        light.shadowBias = 0.02f;
        light.shadowNormalBias = 0.3f;
        light.shadowNearPlane = 0.2f;

        // Без наполняющего света теневая сторона уходит в черный, и низкополигональный
        // набор рассыпается на пятна. Небо марсианское, поэтому подсветка теплая,
        // а отраженный от грунта свет — рыжий.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.52f, 0.40f, 0.36f);
        RenderSettings.ambientEquatorColor = new Color(0.37f, 0.26f, 0.21f);
        RenderSettings.ambientGroundColor = new Color(0.22f, 0.13f, 0.09f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.skybox = null;
        RenderSettings.fog = false;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
    }

    /// <summary>
    /// Грунт под всей сценой.
    ///
    /// Это ответ на главный дефект 2D-витрины: там плиты висели в фоне, и сцена
    /// читалась коллажем. В ортографии луч камеры наклонный, поэтому достаточно
    /// большая плоскость закрывает кадр целиком — «пола» под колонией больше не
    /// видно, потому что он и есть весь кадр.
    /// </summary>
    private static void BuildGround(Material material)
    {
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = "mars_ground";
        plane.transform.SetParent(_root, false);
        // Тайлы рельефа стоят подошвой на -1. Плоскость чуть ниже, иначе
        // соприкасающиеся полигоны начнут мерцать.
        plane.transform.position = new Vector3(0f, -1.01f, 0f);
        plane.transform.localScale = new Vector3(20f, 1f, 20f); // примитив 10x10 -> 200x200
        var r = plane.GetComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = true;
        Object.DestroyImmediate(plane.GetComponent<MeshCollider>());
    }

    /// <summary>Добыча: карьер и буровая на плато, в дальнем углу и выше всех.
    ///
    /// Карьер (`M` в TerrainMap) стоял в клетке (6,6) — на той же диагонали
    /// (i−j=0), что и буровая (5,5). Камера смотрит вдоль (x−z), и одинаковый
    /// (i−j) укладывает оба объекта в одну экранную колонну: буровая ближе и
    /// выше, карьер за ней исчезает целиком (19px разницы на кадре). Карьер
    /// передвинут в клетку (6,5) — сосед буровой по X, (i−j)=1 вместо 0, разнос
    /// по (x−z) те же 2.0, что дают полный вывод из колонны у тары чуть ниже
    /// (см. cargo_A_stacked). Заодно это осмысленнее: карьер вплотную к
    /// буровой, а не спрятан за ней по диагонали.
    ///
    /// Второй переезд, той же природы: когда карьер получил настоящую глубину
    /// (см. `DeepTop`, `TerrainMap`), выяснилось, что клетка (6,5) все еще
    /// непригодна — не по силуэту буровой, а по ракете стартового стола, чей
    /// экранный бокс почти целиком накрывает бокс этой клетки. Клетка (7,6)
    /// пробовалась следующей и тоже не подошла: экранный бокс дна растет
    /// вниз вместе с глубиной, и перекрытие с ракетой у (7,6) оказалось даже
    /// больше, чем у (6,5). Карьер встал в (4,6) — единственная клетка
    /// плато, чей бокс с ракетой не пересекается по X вовсе (подробности в
    /// комментарии у `TerrainMap`). `structure_tall` осталась на исходном
    /// (7,6); камень `rock_crystalsLargeB`, который стоял на (4,6), переехал
    /// на освободившуюся (6,6) (см. ниже).
    /// </summary>
    private static void BuildMining()
    {
        PutCell("drill_structure", 5f, 5f, HighTop);
        PutCell("structure_tall", 7f, 6f, HighTop, 45f);
        PutCell("structure_low", 5f, 7f, HighTop);
        PutCell("lights", 5.2f, 6.2f, HighTop, 200f);

        // Ветряк переехал на плато, и это не украшательство. В клетке (7,4) он
        // стоял на съезде и уходил в породу на целую единицу. Плато — его место
        // и по смыслу: ветер берут с высоты. Поворот 15, а не 30: при 30 габарит
        // повернутой мачты 1.94 против клетки 2.0, и она свешивается за край.
        PutCell("windturbine_tall", 6f, 7f, HighTop, 15f);

        // Тара отодвинута из экранной колонны буровой. Буровая широкая (1.75 на
        // 1.75, на экране 163 px), и ящики позади нее садились ровно на ее
        // кромку. Разнос по (x−z) на 3.2 выводит их из этой колонны целиком.
        // Тара уехала на восточный край плато. Экранная колонна ракеты — это
        // sx = 819..986, и в клетке (6.2, 4.6) эта тара попадала ровно в нее:
        // на кадре красный штабель прирастал к плечу ракеты и портил ей силуэт.
        // Герой обязан читаться отдельным пятном, иначе он не герой.
        // На восточном краю плато остался ОДИН штабель. Второй, мелкий ящик,
        // убран: восточный край зажат между двумя экранными колоннами — ракеты
        // (sx 819..986) и фермы обслуживания (sx 990..1028), — и любое место
        // для него либо пряталось за штабель, либо садилось ферме на макушку.
        PutCell("cargo_A_stacked", 7.15f, 5.0f, HighTop, 18f);
        PutCell("rock_A", 4.7f, 5.6f, HighTop, 160f);

        // Связь плато с колонией — не по воздуху. Тарелки и антенна смотрят
        // вниз на стартовый стол, трубы уходят к обрыву: плато перестает быть
        // отдельной полкой с экспонатами и становится верхним этажом одного
        // хозяйства.
        PutKOn("satelliteDish_large", 5.75f, 6.55f, HighTop, 215f);
        PutKOn("satelliteDish_detailed", 6.3f, 6.75f, HighTop, 200f);
        PutKOn("machine_wirelessCable", 5.3f, 6.9f, HighTop, 160f);
        // Космонавт и тара стоят у САМОЙ кромки плато, а не в его глубине:
        // за спиной у них обрыв и небо, и силуэт читается. Клетка (4,6)
        // побывала карьером на одном из промежуточных шагов этой правки
        // (см. `TerrainMap`) и вернулась обратно в обычное плато — карьер
        // встал в (4,7), эта клетка снова просто край.
        PutK("astronautB", new Vector3(0.9f, HighTop, 4.4f), 250f);
        PutK("barrels", new Vector3(1.4f, HighTop, 4.3f), 20f);
        // Холодный акцент кадра почти нулевой (замер art-inspector: 0.0%
        // насыщенных пикселей в сине-голубом диапазоне против 98.8% в
        // теплом). Тут он оправдан функцией объекта, а не подкрашен для
        // галочки: минерал, вскрытый разработкой на этом же плато, не
        // случайный синий камень. Материал `crystal` в палитре Kenney дает
        // цвет (41,168,224), в холодном диапазоне отчета целиком, но у
        // базового `rock_crystals` крупная порода — это в основном подмеш
        // `rock` (теплый) с мелкими кристаллами (`kenney-bounds.json`:
        // высота 0.338, самая низкая из тройки) — акцент тонул в собственной
        // же породе. `rock_crystalsLargeB` в том же наборе материалов
        // ("rockTrack","rock","crystal") на 63% выше (0.550 против 0.338) и
        // на 10% шире по футпринту (0.939x0.932 против 0.851x0.801) — тот
        // же камень, но с кристаллом, который не приходится искать глазами.
        //
        // Клетка (6.55,5.45) не подошла: (x−z) там 2.2, ровно диагональ
        // ракеты на стартовом столе (ее центр x≈2.2, z≈0, тоже (x−z)≈2.2) —
        // тот же эффект одной экранной колонны, что ломал находку 2, только
        // в этот раз камень выше и уже ракеты и прячется за ней целиком,
        // кроме кончиков кристаллов.
        //
        // Клетка (4,6) — свободный высокий тайл ((i−j)=−2, далеко и от
        // ракеты (~1.1), и от буровой (0)). Побывала карьером на одном из
        // промежуточных шагов этой правки (см. `TerrainMap`) и вернулась в
        // обычное плато — карьер встал в (4,7), эта клетка снова свободна.
        PutKOn("rock_crystalsLargeB", 4.1f, 6.2f, HighTop, 40f);
        PutKOn("rocks_smallB", 4.55f, 6.55f, HighTop, 110f);

        // Тягач у верхнего края съезда. Длинная ось модели — Z (0.489 x 0.893),
        // поворот на -90 разворачивает его носом к спуску, то есть в −x.
        // Съезд занимает клетку (4,5) и поднимается к x = +2 — там и стоит тягач.
        PutOn("spacetruck", 4.75f, 6f, HighTop, -90f);
    }

    /// <summary>
    /// Жилой блок. Модули стоят ЧЕРЕЗ клетку, между ними переходы: тоннель
    /// длиной ровно 2.000 перекрывает пролет между двумя модулями и заходит
    /// краями внутрь стен — так набор и собран. Поставить модули вплотную
    /// нельзя: их план 2.0–2.44 при шаге сетки 2.0, и соседи режут друг другу
    /// стены.
    /// </summary>
    private static void BuildHabitat()
    {
        PutCell("basemodule_A", 1.1f, 4, LowTop);
        // Холодный акцент (art-inspector: 0.025% насыщенных пикселей в
        // холодном диапазоне против 99.7% в теплом). Панели синие по
        // функции, не по прихоти — тонированы отдельным материалом
        // `_solar` (см. `PrepareSolarMaterial`), тем же приемом, что и
        // плато.
        var solar_roof = PutCell("roofmodule_solarpanels", 1.1f, 4, LowTop + 1f);
        if (solar_roof != null)
            foreach (var r in solar_roof.GetComponentsInChildren<MeshRenderer>())
                r.sharedMaterial = _solar;

        PutCell("tunnel_straight_A", 2, 4, LowTop);

        PutCell("basemodule_B", 3, 4, LowTop);
        PutCell("roofmodule_cargo_A", 3, 4, LowTop + 1f);

        // Был tunnel_straight_B: тот же пролет (2.0x0.6x0.8 против 2.0x0.6x0.93
        // у A — разница только в глубине на 0.13, футпринт и высота совпадают),
        // но другой подмеш в атласе — сплошной оранжевый вместо белого с
        // ребрами и окнами. Атлас-текстура одна на весь кит и цвет подмеша
        // задан его собственными UV на этапе экспорта модели, тонировкой
        // (умножением albedo, как у _plateau) оранжевое в белое не превратить:
        // умножение может только гасить каналы, а не поднимать синий, которого
        // в оранжевой плашке почти нет. Три других перехода в этом жилом блоке
        // — tunnel_straight_A; ставим тот же вариант и здесь для связности.
        PutCell("tunnel_straight_A", 1.1f, 5, LowTop, 90f);

        // 1,5.9 вместо 1,6: план модуля 2.206 по Z при шаге 2.0, и в клетке 6
        // он свесился бы на 0.1 за край карты — клетка (1,7) пустая.
        PutCell("basemodule_C", 1.1f, 5.9f, LowTop);
        PutCell("roofmodule_cargo_B", 1.1f, 5.9f, LowTop + 1f);

        PutCell("tunnel_straight_A", 3, 5, LowTop, 90f);

        PutCell("basemodule_E", 3, 6, LowTop);

        PutCell("basemodule_garage", 2, 3, LowTop, 180f);
        // Выносное солнечное поле у края базы: клетка (1,3) свободна целиком,
        // два ряда по 0.8 не достают до модуля A (его край по Z на -0.088).
        // Тот же холодный `_solar`, что и у панелей на крыше — одно поле
        // одной раскраски, не два разных синих.
        foreach (var z in new[] { -1.6f, -0.8f })
        {
            var panel = Put("solarpanel", new Vector3(-5.2f, LowTop, z), 0f);
            if (panel != null)
                foreach (var r in panel.GetComponentsInChildren<MeshRenderer>())
                    r.sharedMaterial = _solar;
        }
        Put("rock_B", new Vector3(-3.3f, LowTop, -3.0f), 145f);
        Put("rock_A", new Vector3(-3.6f, LowTop, -3.7f), 25f);

        // Двор между модулями: свет и мелочь, иначе внутренность базы пустая.
        PutCell("tunnel_straight_A", 2, 6, LowTop);
        PutCell("lights", 1.9f, 5.3f, LowTop, 20f);
        PutCell("containers_A", 2.2f, 6.9f, LowTop);
        PutCell("containers_B", 2.3f, 5.35f, LowTop, 30f);
        // Ящик у СЕВЕРНОЙ стены гаража (2.2, 3.725) отдавал в кадр 41px из
        // бокса ~65000 — накрыт переходом tunnel_straight_A(2,4): у обоих
        // общий x-диапазон [-4,-2] (тот же столбец i=2), а по z зазор между
        // краем гаража (0.096) и низом трубы (0.535, минус 0.346 половины
        // бокса ящика — уже отрицательный) физически меньше самого ящика.
        // Тут не докрутить числа, тут в принципе нет щели такой ширины —
        // ящик передвинут к ЗАПАДНОЙ стене гаража (x = -4, туда труба не
        // доходит: она в том же столбце i=2, что и гараж, а не западнее его).
        PutCell("cargo_A_packed", 1.35f, 3.0f, LowTop, -20f);

        // Купольный ангар Kenney замыкает жилой блок с юга. Он ровно клетка
        // (2.0 x 2.0 при шаге 2.0) и ровно роста жилого модуля (1.0) — это и
        // была причина взять множитель 1.0, а не 2.0.
        //
        // Вариант hangar_roundGlass вместо hangar_roundA: та же геометрия
        // (kenney-bounds.json — 3.271x1.4x2.833 против 3.271x1.5x2.833, оба
        // подошвой в нуле, разница высоты 0.1 на общем множителе 1.0 —
        // раскладку не трогает), но один подмеш — `_defaultMat` вместо
        // `metal` — под стеклянную крышу. Красить его через общую палитру
        // Kenney (`KenneyColor`) нельзя: `_defaultMat` держат еще 18 других
        // моделей набора (платформы, трубы, тарелка, торец рельефа),
        // перекраска записи в холодный утопила бы их всех заодно. Поэтому
        // после раскладки эта деталь красится ТОЛЬКО на этом инстансе —
        // ссылкой на общий `_kenney["_defaultMat"]` находим ровно тот слот
        // на этом ангаре и подменяем его на `_glass` (см.
        // `PrepareGlassMaterial`), остальные три слота (metalRed, metalDark,
        // dark) остаются на общей палитре без изменений.
        var hangar = PutKOn("hangar_roundGlass", 3.05f, 2.45f, LowTop, 200f);
        if (hangar != null && _kenney.TryGetValue("_defaultMat", out var defaultMat))
            foreach (var r in hangar.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] == defaultMat)
                    {
                        mats[i] = _glass;
                        changed = true;
                    }
                if (changed)
                    r.sharedMaterials = mats;
            }
        PutKOn("gate_simple", 2.3f, 2.55f, LowTop, 90f);

        // Юго-западный двор — единственный свободный карман застройки: полоса
        // x = −3.5..−2.6, z = −3.0..−2.3 между воротами гаража, куполом ангара
        // и срезанным углом клетки (2,2). Угол срезан радиусом 1.0 от точки
        // (−4,−4), поэтому все, что ставится здесь, проверено на это расстояние.
        // Здесь стояла тарелка, а не ровер, и это правка по числу. Ровер Kenney
        // 0.30 x 0.39 x 0.35 — в этом кадре он давал ПЯТЬ видимых пикселей:
        // формально в сцене, фактически нет. Тарелка вдвое выше и читается.
        PutK("satelliteDish_detailed", new Vector3(-3.15f, LowTop, -2.65f), 60f);
        PutK("astronautA", new Vector3(-2.85f, LowTop, -2.5f), 15f);
        // Ровер прижат к торцу перехода (просвет 0.06). В полуметре от него он
        // на экране вставал переходу на крышу.
        // Двор — это полоса x = −3.68..−2.04 севернее перехода: между стенами
        // модулей A и B и за торцом тоннеля. Другого свободного места в жилом
        // блоке нет, и человек, поставленный «примерно во двор», оказывался
        // внутри объема модуля B.
        PutK("astronautA", new Vector3(-2.9f, LowTop, 1.85f), 40f);
        PutK("satelliteDish", new Vector3(-2.5f, LowTop, 1.8f), 120f);
        // Бочек в жилом блоке нет: во дворе они садились на крышу модуля A,
        // а у стены не давали в кадр ни пикселя. Двор держат ровер и человек.
        PutKOn("machine_barrel", 1.68f, 3.35f, LowTop, 0f);
        // Третьей тарелки в жилом блоке нет: две на плато уже сказали все, что
        // тарелки говорят, а любое место здесь оказывалось за спиной модуля.
    }

    /// <summary>Склад: депо в ряд, тара рядом с ними, а не по всей сцене.</summary>
    private static void BuildCargo()
    {
        BuildMonorail();

        // Депо шириной 2.07 при шаге 2.0 стоят вплотную: перехлест 0.07 — это
        // соседние стены, а не ошибка. Модульные депо так и собраны.
        //
        // Ряд уехал вправо и назад по сравнению с прошлым прогоном: клетки
        // (4,3) (5,3) (4,4) (5,4) отданы стартовому столу, и депо на прежнем
        // месте въезжали в него углом.
        // Депо уехали на клетку восточнее, а третьего склада (ангара Kenney)
        // здесь больше нет. Причина не композиционная, а арифметическая: на
        // острове кончилось место. Прошлый прогон дал шестнадцать объектов с
        // НУЛЕМ видимых пикселей — они честно стояли в сцене, весили в сборке
        // и не существовали в кадре. Освобожденная клетка (5,2) отдана топливной
        // ферме стартового комплекса.
        PutCell("cargodepot_A", 6, 2, LowTop);
        PutCell("cargodepot_B", 7, 2, LowTop);
        // Труба стоит в клетке (7,3) и НЕ доходит до ряда j = 4: там начинается
        // склон, и вертикаль в 2.0 воткнулась бы в породу основанием.
        PutKOn("chimney", 7.32f, 3.3f, LowTop, 0f);

        // Тара выстроена ВДОЛЬ задней стены депо, а не свалена в поле за ними.
        //
        // Прежняя раскладка держала ящики в 0.45–0.65 позади депо, и на экране
        // они садились ровно на кромку палубы: между ними и депо в мире воздух,
        // а в кадре они стоят на крыше. Прижатые к стене (просвет 0.07–0.15),
        // те же ящики читаются грузом у депо — и это правда, а не иллюзия.
        //
        // Разнос по X здесь тоже обязателен: в прежней раскладке два ящика и
        // контейнер стояли друг за другом по экранной колонне, и два из них не
        // давали в кадр ни пикселя.
        // Ряд начинается с i = 5.5, а не с 4.15: стол занимает x до 3.45, и
        // ящик в клетке 5.2 въезжал бы в него углом.
        // Ряд держится в полосе x = 3.59..6.40: слева его обрезает стартовый
        // стол (кончается на 3.45), справа — поле панелей (начинается на 6.55).
        // Два ящика из пяти прошлого прогона убраны совсем: они не помещались
        // в эту полосу и уходили за край острова, отдавая в кадр 0 и 24 пикселя.
        PutCell("cargo_A_stacked", 5.55f, 2.72f, LowTop, 10f);
        PutCell("cargo_B_stacked", 6.1f, 2.71f, LowTop, -8f);

        // Палуба depot_B (верх 1.02, измерено — `kit-bounds.json`) стояла
        // пустой: самое крупное светлое пятно кадра, восьмиугольник 2.08x2.18,
        // без единого пикселя груза. Два ящика на нее, оба через `PutOn` —
        // подошва садится на измеренный верх, а не на глаз: центр депо в
        // клетке (7,2), офсет ±0.3/±0.25 держит ящики внутри вписанной
        // окружности восьмиугольника (радиус ~0.83 при половине стороны
        // 1.04/1.09) и в 0.2-0.35 друг от друга, чтобы не слипались в одно
        // пятно.
        PutOn("cargo_A_stacked", 6.7f, 1.75f, LowTop + 1.02f, 25f);
        PutOn("cargo_B_stacked", 7.35f, 2.2f, LowTop + 1.02f, -15f);

        // Тягач с прицепом переехал НА ПЕРЕДНИЙ план, к площадкам.
        //
        // Стоял он в клетке (3.9, 4.6) и собрал сразу три дефекта: въехал в
        // съезд на 0.6, закрыл собой прицеп целиком и сел силуэтом на крышу
        // жилого модуля — при мировом просвете 0.85 между ними. Причина в
        // проекции: сдвиг по обеим осям сразу уводит объект строго вверх по
        // экрану. Здесь он разнесен по ОДНОЙ оси и стоит ниже депо по экрану,
        // то есть перекрывает их сам, а не садится на них.
        // Тягач с прицепом идет в коридоре между стеной депо (z = −4.09) и
        // трассой монорельса (z = −5.3): полоса шириной 1.21, машина 0.51.
        PutOn("spacetruck_large", 4.6f, 1.15f, LowTop, 90f);
        PutOn("spacetruck_trailer", 4.1f, 1.15f, LowTop, 90f);
    }

    /// <summary>
    /// Монорельс с составом — поперек всего переднего плана.
    ///
    /// Это главный ответ на «выставку»: линия, которая пересекает кадр из края
    /// в край и по дороге проходит МИМО построек, а не между ними. Пока каждый
    /// объект стоит в своей клетке, кадр читается таблицей; одна сквозная линия
    /// связывает клетки в место.
    ///
    /// Высоты замерены, а не подобраны:
    ///   monorail_trackSupport   0.20 x 0.50 x 0.20  -> верх опоры 0.50
    ///   monorail_trackStraight  0.20 x 0.13 x 1.00  -> верх пути  0.63
    ///   вагоны                  0.50 x 0.80 x 1.00  -> подошва на 0.63
    /// Путь длиной 1.0 по своей оси Z, поэтому разворот на 90 кладет его вдоль
    /// мировой X, а шаг между звеньями — ровно 1.0, то есть ПОЛКЛЕТКИ KayKit.
    ///
    /// Ряд j = 1.1 (z = −4.8) выбран не на глаз: депо стоят задней стеной на
    /// z = −4.09, и трасса на 0.6 ближе к камере проходит перед ними, не
    /// задевая. Клетки (4,1) (5,1) (6,1) низкие, (7,1) — скругленная, и срез
    /// у нее в углу (8,−6), куда трасса не достает.
    /// </summary>
    private static void BuildMonorail()
    {
        const float j = 0.8f;
        for (float i = 4.0f; i <= 6.51f; i += 0.5f)
            PutKOn("monorail_trackStraight", i, j, LowTop + 0.5f, 90f);
        for (float i = 4.0f; i <= 6.01f; i += 1.0f)
            PutKOn("monorail_trackSupport", i, j, LowTop, 0f);

        // Состав стоит НЕ по центру трассы: вагоны у правого края, слева путь
        // уходит пустым за обрез кадра. Состав, вписанный в трассу целиком,
        // читается моделью поезда; уходящий за край — дорогой, по которой ездят.
        PutKOn("monorail_trainFront", 6.4f, j, LowTop + 0.63f, 90f);
        PutKOn("monorail_trainPassenger", 5.9f, j, LowTop + 0.63f, 90f);
        PutKOn("monorail_trainCargo", 5.4f, j, LowTop + 0.63f, 90f);
        PutKOn("monorail_trainEnd", 4.9f, j, LowTop + 0.63f, 90f);

        // Рудный акцент у новой ямы (карьер переехал на (3,1), прогон 7,
        // ночной узел 2, см. `TerrainMap`) — тот же ассет и материал, что
        // уже стоит акцентом «вскрытая порода» на плато (`BuildMining`,
        // `rock_crystalsLargeB`), той же функции здесь.
        //
        // ПРАВКА прогона 8, итерация 1 (судья run-7, id120): камень стоял на
        // (3.7, 1.3, LowTop) — на краю низкой земли рядом с ямой, не в ней.
        // Перенесен на (3.15, 1.1, DeepTop=−1) — числом ближе к объему, но
        // инспектор итерации 2 поймал это же на РЕНДЕРЕ: DeepTop подобран по
        // расчету пивота тайла, а не измерен у самого меша (верх тайла −0.93,
        // низ −3.0, см. `colony3d-scene.json` id4) — камень визуально висел
        // у обода, до дна еще ~70px по кадру.
        //
        // ПРАВКА итерации 2, первый заход: константа заменена ЧЕСТНОЙ ОПОРОЙ
        // (`PutKOnSurface`) — луч бьет в РЕАЛЬНУЮ поверхность тайла
        // `terrain_mining_3_1`, кем бы она ни оказалась по числу, не в
        // константу, угаданную по формуле пивота.
        //
        // ВТОРОЙ заход (после того как плотное кольцо кристаллов у кромки,
        // `BuildPitIce`, закрыло камень с честной опорой целиком от камеры):
        // `PutKOnSurfaceFirstVisible` перебирает несколько точек внутри
        // футпринта клетки (3,1) (x∈[−2,0], z∈[−6,−4], с запасом от стен и
        // от съезда на севере) и берет первую, которая одновременно честно
        // стоит на тайле И не закрыта ни тайлом, ни стенами, ни уже
        // расставленными кристаллами с точки зрения камеры.
        var pitTile = _root != null ? _root.Find("terrain/terrain_mining_3_1") : null;
        // Опора и взгляд — разные множества (см. PutKOnSurfaceFirstVisible):
        // дно ищем по тайлу и стенам, кристаллы кромки только заслоняют.
        var pitWalls = new List<Transform>
        {
            _root != null ? _root.Find("terrain/pit_wall_south") : null,
            _root != null ? _root.Find("terrain/pit_wall_west") : null,
        };
        pitWalls.RemoveAll(t => t == null);
        // Кристаллы кромки (`BuildPitIce`) — тоже возможные заслоны с этого
        // ракурса, а они не в группе `terrain` (это prop, отдельная задача).
        // Собираются по имени прямо из `_root` на момент вызова.
        var pitSight = new List<Transform>(pitWalls);
        if (_root != null)
            foreach (Transform child in _root)
                if (child.name.StartsWith("rock_crystals"))
                    pitSight.Add(child);
        PutKOnSurfaceFirstVisible(
            "rock_crystalsLargeB",
            pitTile,
            pitWalls.ToArray(),
            pitSight.ToArray(),
            new[]
            {
                (-1.3f, -5.3f),
                (-1.0f, -4.45f),
                (-0.7f, -4.8f),
                (-1.0f, -5.0f),
                (-0.5f, -5.2f),
                (-1.3f, -4.3f),
                (-1.6f, -4.9f),
                (-0.4f, -4.9f),
            },
            300f
        );
    }

    /// <summary>
    /// Транспорт на переднем плане. Площадка 0.5 высотой (`landingpad_*`:
    /// min_y 0, size.y 0.5), поэтому аппарат на ней стоит на +0.5 — это
    /// координата, а не сдвиг порядка отрисовки: в 3D порядок считает
    /// глубинный буфер, и трюк 2D-сцены здесь просто не нужен.
    /// </summary>
    private static void BuildLandingPads()
    {
        BuildLaunch();

        // Вторая площадка, малая — у правого края стартового комплекса. Она не
        // герой, она масштабная линейка: рядом с ней видно, насколько ракета
        // больше обычного посадочного места.
        PutCell("landingpad_small", 3.6f, 1.9f, LowTop);
        PutCell("lander_B", 3.6f, 1.9f, LowTop + 0.5f, -20f);
    }

    /// <summary>
    /// ГЕРОЙ КАДРА: стартовый стол с собранной ракетой.
    ///
    /// Почему герой вообще нужен. Прошлый кадр читался выставкой не из-за
    /// моделей, а из-за того, что все объекты в нем одного роста и одной
    /// важности: глазу некуда сесть, и он перебирает витрину по очереди.
    /// Одна вещь, которая выше всего остального вдвое, эту очередь обрывает.
    ///
    /// Числа. Стол в клетке (4.6, 3.5) — это x = 2.2, z = 0.0, то есть
    /// экранные (902, 541) при кадре 1600x1000: чуть правее и ниже
    /// оптического центра. Ракета собрана из четырех частей Kenney стопкой,
    /// каждая ставится на макушку предыдущей по замеру из `kenney-bounds.json`:
    ///   rocket_baseA  h 1.6  ->  0.5 .. 2.1
    ///   rocket_sidesA h 1.0  ->  2.1 .. 3.1
    ///   rocket_finsB  h 0.7  ->  3.1 .. 3.8
    ///   rocket_topB   h 1.1  ->  3.8 .. 4.9
    /// Макушка на 4.9 против 3.0 у самого высокого объекта старого кадра
    /// (`windturbine_tall`) — то есть ракета выше всего в сцене в полтора раза,
    /// и это единственный объект такого роста.
    ///
    /// Стол 2.5 x 2.5 (`landingpad_large`) при клетке 2.0 свешивается на 0.25
    /// в каждую сторону, поэтому стоит на стыке четырех НИЗКИХ клеток
    /// (4,3) (5,3) (4,4) (5,4) — ни одна из них не склон и не плато.
    /// </summary>
    private static void BuildLaunch()
    {
        const float pi = 4.6f;
        const float pj = 3.5f;
        const float deck = LowTop + 0.5f; // верх стола

        PutCell("landingpad_large", pi, pj, LowTop);

        PutKOn("rocket_baseA", pi, pj, deck, 25f);
        PutKOn("rocket_sidesA", pi, pj, deck + 1.6f, 25f);
        PutKOn("rocket_finsB", pi, pj, deck + 2.6f, 25f);
        PutKOn("rocket_topB", pi, pj, deck + 3.3f, 25f);

        // Ферма обслуживания — три яруса решетки рядом со столом. Стоит на
        // ГРУНТЕ, а не на столе: стол кончается на x = 3.45, ферма шириной
        // 0.825 стоит в x = 3.59..4.41, то есть в 0.14 от края стола.
        // Ферма отодвинута на юг, в ряд j = 3.25. В ряду 3.75 ее основание
        // заезжало в клетку (6,4) — угловой обрыв, который там уже поднялся
        // на 0.165, и опора уходила в породу.
        for (int tier = 0; tier < 3; tier++)
            PutKOn("supports_high", 5.5f, 3.25f, LowTop + tier, 0f);

        // Заправка: баки, генератор и труба от них к столу. Это и есть
        // связность — не «два объекта рядом», а «одно питает другое».
        //
        // Баки стоят СЕВЕРО-ЗАПАДНЕЕ стола, а не южнее. Южнее они попадали в
        // экранную колонну купольного ангара и не давали в кадр ни пикселя —
        // три объекта мертвым весом. Здесь за ними открытый грунт до обрыва.
        // Клетка (5,2), к югу от магистрали: единственное место у стола, где
        // перед объектами открытый грунт до самого низа кадра, а не чужая
        // спина. Труба идет от баков вдоль дороги — линия, а не россыпь.
        PutK("machine_barrelLarge", new Vector3(2.5f, LowTop, -3.5f), 0f);
        PutK("machine_barrelLarge", new Vector3(3.4f, LowTop, -3.5f), 0f);
        PutK("machine_generatorLarge", new Vector3(1.5f, LowTop, -3.4f), 90f);
        // Трубы отсюда убраны. Они попадали в экранную колонну состава и
        // садились силуэтом ему на крышу при мировом просвете 1.33 — та самая
        // ложная опора. Связность и без них держат дорога, ферма и монорельс.

        // Люди у подножия. Ради них весь кадр и собирается: фигура ростом
        // 0.79 рядом с ракетой в 4.9 задает масштаб, которого низкополигональной
        // постройке взять неоткуда.
        //
        // Стоят на ГРУНТЕ у кромки стола, а не на самом столе. У площадок
        // KayKit верх 0.5 — это высота БОРТА, палуба внутри лежит на 0.4
        // (замерено прошлым прогоном по `landingpad_small`), и фигура, честно
        // поставленная на 0.5, висит над палубой. На грунте у борта она к тому
        // же читается силуэтом, а не сливается с настилом.
        PutKOn("astronautA", 4.05f, 2.78f, LowTop, 150f);
        // Второй — вплотную к юго-западному углу стола (просвет 0.01 по x и
        // 0.03 по z). Стоя в 0.5 от него, он на экране садился на крышу
        // посадочной площадки в двух метрах позади.
        PutK("astronautB", new Vector3(0.7f, LowTop, -1.45f), 205f);
        PutKOn("rover", 5.12f, 3.05f, LowTop, 40f);

        // Шаттл припаркован у стола — вторая по заметности вещь в кадре.
        // Клетка (2.6,2.7) пряталась в стыке гаража (x до -2, z до -2.278) и
        // купола ангара: замерено по id-буферу, видно 445px из 41010 (~1%),
        // накрыт basemodule_garage. Клетка (2.85,2.2) выводит центр машины
        // южнее заднего края гаража (z=-2.278) и восточнее его стены (x=-2).
        PutKOn("craft_speederA", 2.85f, 2.2f, LowTop, 200f);
        PutKOn("barrels", 5.25f, 2.62f, LowTop, 15f);
    }

    /// <summary>
    /// Энергетика: ветряк и солнечное поле на правом крыле карты.
    ///
    /// Ветряк тут ОДИН, а не два. Инспектор писал, что `windturbine_low` не
    /// читается ветряком вовсе и что два объекта одного назначения без общего
    /// силуэта, стоящие рядом, — дефект сами по себе. Второй экземпляр к тому
    /// же не оставлял места: два корпуса по 1.6 в ширину плюс поле панелей не
    /// помещаются в полосу x = 4..8, и любая раскладка кончалась пересечением.
    ///
    /// Поле стоит ПЕРЕД ветряком, а не за ним. Стоя позади, панели садились
    /// подошвой ровно на его макушку при мировом просвете 0.7 — та же ложная
    /// опора, что и тягач на крыше модуля. Впереди перед ними нет ничего своего
    /// роста, а сам ветряк стоит вплотную к заднему ряду (просвет 0.28) и
    /// читается честным соседом.
    /// </summary>
    private static void BuildPower()
    {
        PutCell("lights", 4.9f, 1.55f, LowTop, 250f);

        // Панель 0.900 x 0.450. Шаг 1.15 x 0.55 в МИРОВЫХ единицах, поле 2x3:
        // габарит 1.15 x 1.10, плюс половина панели по краям. Все три ряда
        // держатся внутри ряда клеток j=2, который на карте низкий.
        //
        // В первом прогоне эти же смещения считались как доли клетки, поле
        // уехало на x = 7.4 при краю карты 7.0 и повисло в воздухе за обрывом.
        // Отсюда правило: смещения внутри группы — только в мировых единицах,
        // и край группы проверяется по карте.
        // Поле уехало в клетки (6,3) (7,3): полосу j = 1..2 занял монорельс и
        // ряд складов. Верхний край поля держится на z = −0.075, то есть НЕ
        // доходит до ряда j = 4, где начинается склон к плато.
        // Поле — ОДИН ряд из трех панелей, а не шесть в две колонны.
        //
        // Шесть не помещаются никуда. На низком грунте вторая колонна уходила
        // за край острова (x = 8.0), на плато ее закрывал карьер: и там и там
        // ровно половина поля отдавала в кадр ноль пикселей. Три панели в один
        // ряд у восточного края читаются полем не хуже шести и все три видны.
        // Поля панелей в промышленном ряду НЕТ, и это не «не поместилось», а
        // измеренная невозможность.
        //
        // Полоса j = 3 восточнее стола — единственное свободное место здесь, и
        // вся она лежит СЕВЕРНЕЕ ряда депо. Депо высотой 1.02, панель — 0.37;
        // в изометрии север уходит вверх по экрану, то есть любая панель в этой
        // полосе стоит ровно за спиной депо и целиком им закрыта. Проверка
        // перекрытости дала на это ноль видимых пикселей в двух раскладках
        // подряд: и поперек стены, и вдоль нее.
        //
        // Здесь помещается только то, что ВЫШЕ депо, — труба (2.0) и стоит.
        // Панели остались там, где перед ними открытый грунт: две в жилом блоке
        // и одна крышевая на модуле A.
    }

    /// <summary>
    /// Камни. Нужны не для красоты: без них плоскость грунта вокруг колонии
    /// читается как пустой лист бумаги, и колония снова становится аппликацией.
    /// </summary>
    private static void BuildScatter()
    {
        // Камни идут ГРУППАМИ, а не по кругу через равные промежутки. Ровное
        // кольцо из первого прогона читалось горошком на скатерти: одинаковый
        // шаг мозг распознает как узор, а не как природу.
        // Границы кадра здесь — не на глаз. Проекция камеры измерена по дампу:
        //   px_x = 46.52*(x − z) + 800,  px_y = −23.26*(x + z) − 56.97*y + 592.6
        // при текстуре 1600x1000. То есть камень виден, пока |x − z| < 17.2
        // минус его половина ширины. Четыре камня из шестнадцати этого условия
        // не выполняли и не давали в кадр ни пикселя — вес в сборке без
        // единого пикселя на экране.
        var spots = new (string model, float x, float z, float rot)[]
        {
            ("rocks_A", -10.5f, -1.5f, 20f),
            ("rock_B", -10.8f, -4.6f, 140f),
            ("rock_A", -13.6f, -1.4f, 75f),
            ("rocks_B", -2.6f, 12.4f, 70f),
            ("rock_A", -4.4f, 11.2f, 10f),
            ("rock_B", -1.5f, 13.0f, 200f),
            ("rocks_A", 11.2f, 10.4f, 250f),
            ("rock_B", 12.8f, 8.9f, 30f),
            ("rocks_B", 14.2f, 7.6f, 115f),
            ("rock_B", 12.6f, -1.4f, 180f),
            ("rocks_A", 11.4f, -3.9f, 15f),
            ("rocks_B", 2.2f, -12.4f, 40f),
            ("rock_A", 3.9f, -11.1f, 130f),
            ("rock_B", 0.4f, -13.6f, 65f),
            ("rock_A", -9.8f, 5.6f, 305f),
            ("rock_B", 6.6f, -10.2f, 95f),
        };
        foreach (var (model, x, z, rot) in spots)
            Put(model, new Vector3(x, -1f, z), rot);

        // Камни на самой площадке: без них край острова читается как обрез
        // по линейке. Мелкие модели (rock_A 0.53, rock_B 0.70) — они не свесятся.
        PutCell("rock_A", 6.6f, 7.1f, HighTop, 45f);
        PutCell("rock_B", 1.4f, 2.7f, LowTop, 210f);
        // Клетка (4,7) стала высокой в прошлом прогоне, и камень, поставленный
        // на низ, ушел внутрь породы целиком. Ставился на верх плато — но
        // клетка (4,7) стала карьером (см. `TerrainMap`, находка «карьер не
        // читается ямой»), и тот же камень на HighTop повис бы на 2 над
        // новым дном (проверено: `check-scene3d.mjs` дал именно это, красным).
        // Переставлен в соседнюю (4,6), которая осталась плато — тот же край
        // острова, только с другой стороны от того, что теперь яма.
        PutCell("rock_A", 4.35f, 6.35f, HighTop, 160f);
        // Камень у подножия мачты, а не в экранной колонне ящиков: в клетке
        // (7,5.4) он вставал силуэтом ровно на кромку тары в 0.74 от нее.
        PutCell("rock_B", 6.7f, 6.7f, HighTop, 260f);
    }

    /// <summary>
    /// Мелочь набора Kenney вокруг колонии: кратеры, метеориты, камни.
    ///
    /// Пустое поле вокруг острова — вторая половина «выставочного» вида: сцена
    /// стоит на чистом листе, значит она макет. Кратеры и метеориты дают полю
    /// историю — сюда что-то падало, — и стоят они не по кольцу, а пятнами.
    ///
    /// Границы кадра считаются по той же проекции, что и у камней KayKit:
    /// объект виден, пока |x − z| < 17.2 минус его половина ширины.
    /// </summary>
    private static void BuildKenneyScatter()
    {
        // Координаты не «вокруг острова», а по ПРОЕКЦИИ. Для точки на грунте
        // (y = −1) экран считается так:
        //   px_x = 46.52*(x − z) + 800
        //   px_y = 592.6 − 23.26*(x + z) + 57
        // Отсюда два условия видимости, и прошлый прогон нарушил оба.
        //   * Кадр: px_y < 1000, то есть (x + z) > −15.4. Три камня стояли на
        //     сумме −16.5 и лежали ниже нижней кромки кадра целиком.
        //   * Остров: при большой сумме (x + z) точка уезжает ЗА остров и он
        //     ее закрывает. Два камня стояли на сумме +11 и +17.6 — ноль
        //     видимых пикселей при честном месте в сцене.
        // Поэтому вся россыпь держится в полосе (x + z) от −14 до +8 и
        // разнесена по |x − z|, то есть по горизонтали кадра.
        var spots = new (string model, float x, float z, float rot)[]
        {
            ("craterLarge", -8.6f, -4.4f, 0f),
            ("crater", -11.2f, -2.4f, 40f),
            ("meteor_half", -12.4f, -1.2f, 70f),
            // Второй холодный акцент, далеко от первого (тот — на плато у
            // карьера, этот — в левом нижнем углу кадра): россыпь минералов
            // среди обломков читается естественнее одного акцента, который
            // легко счесть случайностью. Тот же футпринт, что у прежнего
            // rock_largeB (0.928x0.939 против 0.834x0.919 — расхождение в
            // пределах остальных замен в этой же россыпи), место не пустует.
            ("rock_crystalsLargeA", -12.8f, 1.6f, 240f),
            ("crater", -4.6f, 9.2f, 25f),
            ("meteor_detailed", -5.9f, 9.8f, 310f),
            ("rocks_smallB", -7.2f, 8.4f, 80f),
            ("craterLarge", 1.2f, -11.0f, 45f),
            ("meteor", 4.2f, -11.6f, 95f),
            ("rocks_smallA", 0.8f, -13.2f, 170f),
            ("crater", 11.4f, -1.2f, 15f),
            ("craterLarge", 12.6f, 0.4f, 60f),
            ("meteor", 13.4f, -2.4f, 200f),
            ("rocks_smallA", 10.8f, -3.6f, 130f),
            ("rock_largeA", 11.2f, 5.4f, 20f),
        };
        foreach (var (model, x, z, rot) in spots)
            PutK(model, new Vector3(x, -1f, z), rot);
    }

    /// <summary>
    /// Ортографическая камера под изометрическим углом.
    ///
    /// Поворот 30/45 — тот самый угол мобильных ситибилдеров: наклон 30
    /// градусов дает соотношение вертикали к горизонтали 1:2, ровно как в
    /// 2D-версии, только теперь оно получается проекцией, а не рисуется.
    /// </summary>
    private static void BuildCamera()
    {
        var go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        go.transform.SetParent(_root, false);

        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 7.6f;
        cam.transform.rotation = Quaternion.Euler(30f, 45f, 0f);

        // Отодвигаем камеру вдоль ее же взгляда ровно настолько, чтобы ближняя
        // плоскость не срезала передний ряд, и НИ НА МЕТР больше.
        //
        // В ортографии дистанция не влияет на кадр, и соблазн отодвинуться «с
        // запасом» ничего не стоит на вид. Стоит он теней: карта теней кроится
        // по дальности ОТ КАМЕРЫ, и при отлете на 60 единиц вся колония
        // попадала в последние восемь процентов диапазона. Тексель карты
        // растягивался, и в собранном плеере по стыкам тайлов пошла рябь
        // самозатенения — в редакторе ее не было, потому что там свой уровень
        // качества. 25 единиц отлета против дальности теней 45 держат колонию
        // в середине диапазона.
        Vector3 target = new Vector3(1.5f, 0.4f, 1.5f);
        cam.transform.position = target - cam.transform.forward * 25f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 80f;

        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.50f, 0.33f, 0.26f); // марсианская дымка
        cam.allowMSAA = true;
        cam.useOcclusionCulling = false;
    }

    /// <summary>
    /// Тени в настройках качества — на ВСЕХ уровнях.
    ///
    /// Это и есть та поломка, которая не видна в редакторе. Редактор рисует
    /// текущим уровнем качества, а плеер WebGL берет свой, заданный для
    /// платформы; на нем тени могут быть выключены, и собранная сцена приезжает
    /// без единой тени при полностью зеленом логе. Ставим всем уровням.
    /// </summary>
    private static void ApplyShadowQuality()
    {
        int original = QualitySettings.GetQualityLevel();
        int levels = QualitySettings.names.Length;
        for (int i = 0; i < levels; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowCascades = 2;
            QualitySettings.shadowDistance = 45f;
            QualitySettings.pixelLightCount = 2;
            QualitySettings.antiAliasing = 4;
        }
        QualitySettings.SetQualityLevel(original, false);
        AssetDatabase.SaveAssets();
        Debug.Log($"[3d] тени включены на {levels} уровнях качества");
    }

    // ---- Кадр и сборка -----------------------------------------------------

    /// <summary>
    /// Снимает кадр прямо из редактора, без веб-сборки.
    ///
    /// Быстрый круг для правки раскладки: сборка плеера идет минуты, а этот
    /// кадр — секунды. Требует запуска редактора БЕЗ `-nographics`: без
    /// графического устройства рендерить нечем, и на выходе будет черный PNG.
    /// </summary>
    public static void BuildAndShoot()
    {
        ApplyShadowQuality();
        BuildScene();

        var cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null)
        {
            Debug.LogError("[3d] камеры в сцене нет — снимать нечем");
            EditorApplication.Exit(1);
            return;
        }

        const int width = 1600;
        const int height = 1000;
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 4,
        };
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        shot.Apply();
        RenderTexture.active = previous;

        File.WriteAllBytes("colony3d-editor.png", shot.EncodeToPNG());

        // Координаты снимаются, пока камера ЕЩЕ смотрит в текстуру. Отвязанная
        // камера считает WorldToScreenPoint по размеру окна редактора (в пакетном
        // режиме — 640x480), и числа расходятся с кадром в два с половиной раза.
        // Первый прогон дал ровно это, и разбор кадра поехал.
        var records = CollectRecords(cam, width, height);
        MeasureSupport(records);
        DumpScreenPositions(records);
        CaptureIds(cam, width, height, records);
        DumpSceneJson(records, cam, width, height);
        cam.targetTexture = null;

        // Средняя яркость — дешевая защита от «кадр снят» при черном PNG.
        // Ровно так в этом проекте уже отчитывались об успехе на пустом месте.
        Color[] pixels = shot.GetPixels();
        float sum = 0f;
        foreach (var p in pixels)
            sum += p.r + p.g + p.b;
        float brightness = sum / (pixels.Length * 3f);
        Debug.Log($"[3d] кадр снят: colony3d-editor.png, средняя яркость {brightness:F3}");

        if (brightness < 0.02f)
        {
            Debug.LogError("[3d] кадр черный — графическое устройство не отдало картинку");
            EditorApplication.Exit(1);
            return;
        }
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// Один объект сцены глазами машинных проверок.
    ///
    /// `id` — номер, которым объект покрашен в буфере идентификаторов. Он же
    /// стоит первой колонкой в `colony3d-objects.txt`, чтобы человек и скрипт
    /// говорили об одном и том же объекте: имен-дубликатов в сцене хватает
    /// (шесть `solarpanel`, три `spacetruck`).
    /// </summary>
    private sealed class ObjRecord
    {
        public int id;
        public string name;
        public string kind; // ground | terrain | prop
        public Transform node;
        public Bounds bounds;
        public Vector2 px; // центр объема на экране, начало сверху
        public Vector4 sbox; // габарит объема на экране: x0,y0,x1,y1
        public float support_y = float.NaN; // высота поверхности под подошвой
        public string support_kind = "нет";
        public string support_name = "";
        public float cover_y = float.NaN; // ближайшая поверхность НАД макушкой
        public string cover_kind = "нет";
        public string cover_name = "";
    }

    /// <summary>
    /// Собирает записи об объектах ОДНИМ проходом: и человекочитаемый дамп, и
    /// буфер идентификаторов, и JSON нумеруют объекты в одном порядке. Разойдись
    /// эти три списка — проверки начали бы обвинять не тех.
    /// </summary>
    private static List<ObjRecord> CollectRecords(Camera cam, int width, int height)
    {
        var nodes = new List<(Transform node, string kind)>();
        foreach (Transform child in _root)
        {
            if (child.name == "terrain")
            {
                foreach (Transform tile in child)
                    if (tile.GetComponentsInChildren<Renderer>().Length > 0)
                        nodes.Add((tile, "terrain"));
                continue;
            }
            if (child.GetComponentsInChildren<Renderer>().Length == 0)
                continue;
            nodes.Add((child, child.name == "mars_ground" ? "ground" : "prop"));
        }

        var records = new List<ObjRecord>();
        for (int k = 0; k < nodes.Count; k++)
        {
            var (node, kind) = nodes[k];
            var list = node.GetComponentsInChildren<Renderer>();
            Bounds b = list[0].bounds;
            foreach (var r in list)
                b.Encapsulate(r.bounds);

            // Экранный габарит считается по ВОСЬМИ углам объема, а не по
            // половине размера: при повороте камеры 30/45 проекция коробки —
            // шестиугольник, и половина размера по любой оси мимо.
            float x0 = float.MaxValue,
                y0 = float.MaxValue,
                x1 = float.MinValue,
                y1 = float.MinValue;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3(
                    (c & 1) == 0 ? b.min.x : b.max.x,
                    (c & 2) == 0 ? b.min.y : b.max.y,
                    (c & 4) == 0 ? b.min.z : b.max.z
                );
                Vector3 s = cam.WorldToScreenPoint(corner);
                float sy = height - s.y;
                x0 = Mathf.Min(x0, s.x);
                x1 = Mathf.Max(x1, s.x);
                y0 = Mathf.Min(y0, sy);
                y1 = Mathf.Max(y1, sy);
            }

            Vector3 sc = cam.WorldToScreenPoint(b.center);
            records.Add(
                new ObjRecord
                {
                    id = k + 1,
                    name = node.name,
                    kind = kind,
                    node = node,
                    bounds = b,
                    // У Unity начало экрана снизу, у картинки — сверху.
                    px = new Vector2(sc.x, height - sc.y),
                    sbox = new Vector4(x0, y0, x1, y1),
                }
            );
        }
        return records;
    }

    /// <summary>
    /// Поверхность под подошвой каждого объекта — ЛУЧОМ, а не по габаритам.
    ///
    /// Соблазн взять верх габарита тайла под объектом обходится дорого: у склона
    /// и у углового обрыва габарит от -1 до +1 по всей клетке, хотя поверхность
    /// на одном ее краю лежит на нуле. Считая по габариту, проверка объявляет
    /// утопленным все, что стоит у подножия склона, и молчит про то, что
    /// действительно въехало в породу. Луч спрашивает саму геометрию.
    ///
    /// Коллайдеры ставятся на время замера: импорт моделей идет без них
    /// намеренно (сцена — витрина, физика ей не нужна), и оставлять их в
    /// сохраненной сцене незачем — сцена уже записана к этому моменту.
    ///
    /// Пять лучей, а не один: у тягача центр плана приходится на просвет между
    /// осями, и одиночный луч из центра ловит не ту поверхность.
    /// </summary>
    private static void MeasureSupport(List<ObjRecord> records)
    {
        var owner = new Dictionary<Collider, ObjRecord>();
        var added = new List<Collider>();
        foreach (var o in records)
        foreach (var mf in o.node.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null)
                continue;
            var col = mf.gameObject.AddComponent<MeshCollider>();
            owner[col] = o;
            added.Add(col);
        }
        Physics.SyncTransforms();

        foreach (var o in records)
        {
            if (o.kind != "prop")
                continue;

            Vector3 c = o.bounds.center;
            Vector3 e = o.bounds.extents;
            // Луч пускается ЗАВЕДОМО высоко, а не от макушки объекта. Объект,
            // целиком проглоченный породой, начинается внутри меша: луч изнутри
            // видит только изнанку, а изнанку запросы физики не считают. Такой
            // объект отчитывался бы «висит над грунтом» — диагноз, обратный
            // истинному.
            const float sky = 40f;
            var probes = new[]
            {
                new Vector2(c.x, c.z),
                new Vector2(c.x - e.x * 0.4f, c.z - e.z * 0.4f),
                new Vector2(c.x + e.x * 0.4f, c.z - e.z * 0.4f),
                new Vector2(c.x - e.x * 0.4f, c.z + e.z * 0.4f),
                new Vector2(c.x + e.x * 0.4f, c.z + e.z * 0.4f),
            };

            float best = float.NegativeInfinity;
            ObjRecord best_owner = null;
            float cover = float.PositiveInfinity;
            ObjRecord cover_owner = null;
            foreach (var p in probes)
            {
                var hits = Physics.RaycastAll(
                    new Ray(new Vector3(p.x, sky, p.y), Vector3.down),
                    sky + 200f
                );
                foreach (var h in hits)
                {
                    if (!owner.TryGetValue(h.collider, out var ow) || ow == o)
                        continue;
                    if (h.point.y > o.bounds.max.y - 0.01f)
                    {
                        // Поверхность НАД макушкой. Для породы это значит, что
                        // объект внутри нее целиком.
                        if (h.point.y < cover)
                        {
                            cover = h.point.y;
                            cover_owner = ow;
                        }
                        continue;
                    }
                    if (h.point.y > best)
                    {
                        best = h.point.y;
                        best_owner = ow;
                    }
                }
            }

            // Луч не видит ПОЛЫХ тел, и это не мелочь: ступени ракеты Kenney —
            // цилиндрические оболочки радиуса 0.5 без дна и крышки, а ферма
            // обслуживания — решетка с металлом только по углам. Луч из центра
            // проходит такое тело насквозь и приносит поверхность этажом ниже.
            // Ступень, честно стоящая ободом на ободе, отчитывалась «висит на
            // 0.6», а ярус фермы — «висит на 1.8 над породой».
            //
            // Поэтому к лучу добавлена опора ПО ГАБАРИТАМ: сосед, у которого
            // макушка приходится ровно на мою подошву и план пересекается с
            // моим, — это и есть то, на чем я стою. Проверка от этого не
            // слабеет: допуск 0.05 не пропустит объект, висящий над чем-либо
            // заметно выше, а требование пересечения планов не пустит в опору
            // соседа, стоящего рядом.
            const float rim = 0.05f;
            foreach (var other in records)
            {
                if (other == o || other.kind != "prop")
                    continue;
                float top = other.bounds.max.y;
                if (top > o.bounds.min.y + rim || top <= best)
                    continue;
                bool plans_cross =
                    Mathf.Min(o.bounds.max.x, other.bounds.max.x)
                        > Mathf.Max(o.bounds.min.x, other.bounds.min.x)
                    && Mathf.Min(o.bounds.max.z, other.bounds.max.z)
                        > Mathf.Max(o.bounds.min.z, other.bounds.min.z);
                if (!plans_cross)
                    continue;
                best = top;
                best_owner = other;
            }

            if (best_owner != null)
            {
                o.support_y = best;
                o.support_kind = best_owner.kind;
                o.support_name = best_owner.name;
            }
            if (cover_owner != null)
            {
                o.cover_y = cover;
                o.cover_kind = cover_owner.kind;
                o.cover_name = cover_owner.name;
            }
        }

        foreach (var col in added)
            if (col != null)
                Object.DestroyImmediate(col);
    }

    /// <summary>
    /// Экранные координаты каждого объекта рядом с кадром.
    ///
    /// Без этого разбор кадра превращается в угадывание, какое из пятен —
    /// какая модель, а правка раскладки — в перебор. Числа рядом с картинкой
    /// превращают «что-то висит справа» в «solarpanel на 1180,300 вне плиты».
    /// </summary>
    private static void DumpScreenPositions(List<ObjRecord> records)
    {
        var rows = new List<string>();
        foreach (var o in records)
            rows.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "#{0,-3} {1,-32} px=({2,4:F0},{3,4:F0})  world=({4,6:F1},{5,5:F1},{6,6:F1})  h={7:F2}",
                    o.id,
                    o.name,
                    o.px.x,
                    o.px.y,
                    o.bounds.center.x,
                    o.bounds.min.y,
                    o.bounds.center.z,
                    o.bounds.size.y
                )
            );

        File.WriteAllText("colony3d-objects.txt", string.Join("\n", rows));
        Debug.Log($"[3d] экранные координаты {rows.Count} объектов: colony3d-objects.txt");
    }

    // ---- Буфер идентификаторов ---------------------------------------------
    //
    // Три проверки из отчета инспектора — столкновение силуэтов, опора на землю
    // и полная перекрытость — упираются в один и тот же вопрос: КАКИЕ ПИКСЕЛИ
    // кадра принадлежат какому объекту. По центрам объектов он не решается: два
    // центра могут быть в 25 пикселях друг от друга и не соприкасаться вовсе, а
    // объект может быть закрыт целиком, стоя в трех пикселях от чужого центра.
    //
    // Поэтому кадр снимается ВТОРОЙ раз, тем же кадрированием, но каждый объект
    // покрашен своим плоским цветом. Перекрытия в нем считает тот же буфер
    // глубины, что и в основном кадре, — то есть перекрытость меряется, а не
    // моделируется.

    /// <summary>
    /// Шесть уровней на канал: 216 цветов, между соседними 51. Такой шаг
    /// переживает и сглаживание, и любую путаницу гаммы — цвета не слипаются.
    /// </summary>
    private static readonly int[] IdLevels = { 0, 51, 102, 153, 204, 255 };

    private static Color32 IdColor(int id) =>
        new Color32(
            (byte)IdLevels[id % 6],
            (byte)IdLevels[(id / 6) % 6],
            (byte)IdLevels[(id / 36) % 6],
            255
        );

    /// <summary>
    /// Второй проход камеры: каждый объект плоским цветом-номером.
    ///
    /// Три места, где такой проход врет молча, и что здесь с ними сделано.
    ///
    /// 1. **Сглаживание.** На кромке MSAA смешивает два номера и рождает третий,
    ///    которого нет в палитре. Поэтому и текстура, и камера без сглаживания.
    /// 2. **Гамма.** Цвет, заданный через `SetColor`, в линейном пространстве
    ///    конвертируется при загрузке, и номер приезжает другим. Ставим через
    ///    `SetVector` — векторные свойства не конвертируются, — и целевая
    ///    текстура линейная, чтобы запись не конвертировала обратно.
    /// 3. **Свет.** `Unlit/Color` не берет ни свет, ни тени: пиксель либо ровно
    ///    цвет номера, либо не этот объект.
    ///
    /// Своя же проверка на выходе: доля пикселей, не совпавших с палитрой. Если
    /// она заметная — проход сломан, и об этом кричит лог, а не молчат числа.
    /// </summary>
    private static void CaptureIds(Camera cam, int width, int height, List<ObjRecord> records)
    {
        var shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            Debug.LogError("[3d] шейдера Unlit/Color нет — буфер номеров не снят");
            return;
        }

        var saved = new List<(Renderer r, Material[] mats)>();
        var temp = new List<Material>();
        foreach (var o in records)
        {
            Color32 c = IdColor(o.id);
            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            mat.SetVector("_Color", new Vector4(c.r / 255f, c.g / 255f, c.b / 255f, 1f));
            temp.Add(mat);

            foreach (var r in o.node.GetComponentsInChildren<Renderer>())
            {
                saved.Add((r, r.sharedMaterials));
                var slots = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = mat;
                r.sharedMaterials = slots;
            }
        }

        var rt = new RenderTexture(
            width,
            height,
            24,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Linear
        )
        {
            antiAliasing = 1,
            filterMode = FilterMode.Point,
        };

        RenderTexture prev_target = cam.targetTexture;
        CameraClearFlags prev_clear = cam.clearFlags;
        Color prev_bg = cam.backgroundColor;
        bool prev_msaa = cam.allowMSAA;

        cam.allowMSAA = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black; // фон = номер 0
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture prev_active = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev_active;

        File.WriteAllBytes("colony3d-ids.png", tex.EncodeToPNG());

        // Самопроверка палитры: сколько пикселей НЕ легло ни в один уровень.
        var pixels = tex.GetPixels32();
        int stray = 0;
        foreach (var p in pixels)
            if (!IsLevel(p.r) || !IsLevel(p.g) || !IsLevel(p.b))
                stray++;
        float stray_ratio = (float)stray / pixels.Length;
        string line = $"[3d] буфер номеров: colony3d-ids.png, вне палитры {stray_ratio * 100f:F2}%";
        if (stray_ratio > 0.01f)
            Debug.LogError(line + " — проход сломан, проверкам верить нельзя");
        else
            Debug.Log(line);

        cam.targetTexture = prev_target;
        cam.clearFlags = prev_clear;
        cam.backgroundColor = prev_bg;
        cam.allowMSAA = prev_msaa;
        foreach (var (r, mats) in saved)
            r.sharedMaterials = mats;
        foreach (var m in temp)
            Object.DestroyImmediate(m);
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
    }

    private static bool IsLevel(byte v)
    {
        foreach (int l in IdLevels)
            if (Mathf.Abs(v - l) <= 2)
                return true;
        return false;
    }

    /// <summary>
    /// Машинный дамп сцены: номер, объем в мире, габарит на экране.
    ///
    /// Пороги проверок живут не здесь, а в `check-scene3d.mjs`: число, которое
    /// нельзя подвинуть, не пересобирая сцену, никто двигать не станет.
    /// </summary>
    private static void DumpSceneJson(
        List<ObjRecord> records,
        Camera cam,
        int width,
        int height
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "  \"camera\": {{ \"width\": {0}, \"height\": {1}, \"ortho_size\": {2:F3}, "
                    + "\"px_per_unit\": {3:F4} }},",
                width,
                height,
                cam.orthographicSize,
                height / (2f * cam.orthographicSize)
            )
        );
        sb.AppendLine("  \"objects\": [");
        var rows = new List<string>();
        foreach (var o in records)
        {
            Color32 c = IdColor(o.id);
            rows.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "    {{ \"id\": {0}, \"name\": \"{1}\", \"kind\": \"{2}\", "
                        + "\"color\": [{3}, {4}, {5}], "
                        + "\"world_min\": [{6:F3}, {7:F3}, {8:F3}], "
                        + "\"world_max\": [{9:F3}, {10:F3}, {11:F3}], "
                        + "\"px\": [{12:F1}, {13:F1}], "
                        + "\"sbox\": [{14:F1}, {15:F1}, {16:F1}, {17:F1}], "
                        + "\"support_y\": {18}, \"support_kind\": \"{19}\", "
                        + "\"support_name\": \"{20}\", "
                        + "\"cover_y\": {21}, \"cover_kind\": \"{22}\", "
                        + "\"cover_name\": \"{23}\" }}",
                    o.id,
                    o.name,
                    o.kind,
                    c.r,
                    c.g,
                    c.b,
                    o.bounds.min.x,
                    o.bounds.min.y,
                    o.bounds.min.z,
                    o.bounds.max.x,
                    o.bounds.max.y,
                    o.bounds.max.z,
                    o.px.x,
                    o.px.y,
                    o.sbox.x,
                    o.sbox.y,
                    o.sbox.z,
                    o.sbox.w,
                    float.IsNaN(o.support_y)
                        ? "null"
                        : o.support_y.ToString("F3", CultureInfo.InvariantCulture),
                    o.support_kind,
                    o.support_name,
                    float.IsNaN(o.cover_y)
                        ? "null"
                        : o.cover_y.ToString("F3", CultureInfo.InvariantCulture),
                    o.cover_kind,
                    o.cover_name
                )
            );
        }
        sb.AppendLine(string.Join(",\n", rows));
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        File.WriteAllText("colony3d-scene.json", sb.ToString());
        Debug.Log($"[3d] машинный дамп сцены: colony3d-scene.json, {records.Count} объектов");
    }

    /// <summary>
    /// Сборка веб-плеера ИЗ 3D-сцены, в свою папку.
    ///
    /// Сцена передается списком явно, `EditorBuildSettings` не трогается: иначе
    /// следующий прогон `measure.mjs` собрал бы 3D-сцену вместо `Main.unity` и
    /// проверка играбельности упала бы на пустом месте.
    /// </summary>
    public static void BuildWeb3D()
    {
        ApplyShadowQuality();
        BuildScene();

        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.decompressionFallback = true;

        var options = new UnityEditor.BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Build/Web3D",
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None,
        };

        var report = UnityEditor.BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        Debug.Log(
            $"[3d] сборка: {summary.result}, {summary.totalSize / 1024 / 1024} МБ, "
                + $"{summary.totalTime.TotalSeconds:F0} с"
        );

        EditorApplication.Exit(
            summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1
        );
    }
}
