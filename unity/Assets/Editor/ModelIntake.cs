using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Приемка входящей модели: замер и примерка.
///
/// Владелец лепит модели сам и присылает их по одной. Без этого файла каждая
/// присланная модель означала час ручной работы: открыть редактор, перетащить,
/// на глаз сравнить с набором, на глаз найти сдвинутый пивот. Ручной замер еще
/// и врет — «основание примерно в нуле» на кадре выглядит нормально, а в сцене
/// объект висит над грунтом на два сантиметра, и это видно только в упор.
///
/// Здесь замер делает редактор и кладет числа в файл. Вердикт по числам выносит
/// `fit-model.mjs`, он же и вызывает эти методы. Разделение не косметическое:
/// правила посадки меняются чаще, чем способ измерить габарит, а перезапуск
/// редактора стоит сорок секунд.
///
/// FBX, OBJ и DAE читает редактор — родным импортером. glTF и GLB он не умеет
/// вовсе (пакета в проекте нет), их меряет узел без Unity, по спецификации
/// формата. Поэтому сюда приходит не всякий файл.
/// </summary>
public static class ModelIntake
{
    private const string IncomingDir = "Assets/Incoming";
    private const string BoundsFile = "incoming-bounds.json";
    private const string ShotFile = "incoming-fit.png";

    private static readonly string[] Extensions = { ".fbx", ".obj", ".dae", ".blend" };
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".tif", ".tiff", ".exr" };

    /// <summary>Клетка набора KayKit. Каркас раскладки сцены считает по ней.</summary>
    private const float Grid = 2.0f;

    // ---- Замер -------------------------------------------------------------

    /// <summary>
    /// Меряет все модели в `Assets/Incoming` и пишет числа в `incoming-bounds.json`.
    /// </summary>
    public static void MeasureIncoming()
    {
        var files = IncomingFiles();
        if (files.Count == 0)
        {
            File.WriteAllText(BoundsFile, "{}\n");
            Debug.Log($"[intake] в {IncomingDir} нет моделей — мерить нечего");
            EditorApplication.Exit(0);
            return;
        }

        PrepareImporters(files);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var entries = new List<string>();
        foreach (string path in files)
        {
            entries.Add(MeasureOne(path));
        }

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine(string.Join(",\n", entries));
        sb.AppendLine("}");
        File.WriteAllText(BoundsFile, sb.ToString());

        Debug.Log($"[intake] измерено моделей: {entries.Count}, числа в {BoundsFile}");
        EditorApplication.Exit(0);
    }

    private static List<string> IncomingFiles()
    {
        if (!Directory.Exists(IncomingDir))
            return new List<string>();

        return Directory
            .GetFiles(IncomingDir)
            .Where(p => Extensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .Select(p => p.Replace('\\', '/'))
            .OrderBy(p => p)
            .ToList();
    }

    /// <summary>
    /// Приводит импортер к известному состоянию.
    ///
    /// Настройки импорта меняют РЕЗУЛЬТАТ ЗАМЕРА: включенная конвертация единиц
    /// умножает габарит на сто, если модель пришла из редактора, где метр это
    /// сантиметр. Мерить надо в одном и том же состоянии, иначе два прогона по
    /// одному файлу дадут разные числа, и виноватым окажется автор модели.
    ///
    /// Материалы импортируем намеренно, хотя сцене витрины они не нужны. Имена
    /// материалов автора — половина ответа на вопрос «прочитается ли модель
    /// частью набора». Взять их у импортера напрямую нельзя: свойства
    /// `sourceMaterials` в редакторе шестой версии больше нет, — поэтому имена
    /// снимаются с импортированных материалов на самих поверхностях.
    /// </summary>
    private static void PrepareImporters(List<string> files)
    {
        foreach (string path in files)
        {
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
                continue;

            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.SaveAndReimport();

            // Встроенное медиа FBX (embedded media) редактор не распаковывает
            // сам по себе — это отдельный вызов API, а не следствие
            // `materialImportMode`. Без него модель приходит белой/серой:
            // материал импортируется, ссылки на видео-клип в файле валидные
            // (проверено бинарным разбором FBX), но текстурный ассет для
            // материала не создается, и рендер остается плоским цветом.
            // Найдено и починено в прогоне 7, узел 0.
            string textureDir = TextureDirFor(path);
            bool extracted = importer.ExtractTextures(textureDir);
            if (extracted)
            {
                AssetDatabase.Refresh();

                // Второй слой того же дефекта: у извлеченного файла нет
                // распознаваемого расширения (FBX хранит имя embedded-медиа
                // без точки, а точка, добавленная в имя картинки на стороне
                // Blender, доезжает как "_png", а не ".png" — FBX заменяет
                // точку в имени объекта на подчеркивание). Ассетная база
                // Unity определяет тип импортера по расширению файла: без
                // ".png" файл получает `DefaultImporter`, а не
                // `TextureImporter`, и как текстура не читается вовсе. Чиним
                // переименованием на диске, раз имя внутри FBX не донести.
                RenameExtractedTexturesToPng(textureDir);
                AssetDatabase.Refresh();
                importer.SaveAndReimport();
            }
        }
        AssetDatabase.Refresh();
    }

    private static string TextureDirFor(string modelPath) =>
        Path.Combine(
            Path.GetDirectoryName(modelPath) ?? IncomingDir,
            Path.GetFileNameWithoutExtension(modelPath) + "_textures"
        ).Replace('\\', '/');

    private static void RenameExtractedTexturesToPng(string dir)
    {
        if (!Directory.Exists(dir))
            return;

        foreach (string file in Directory.GetFiles(dir))
        {
            if (file.EndsWith(".meta"))
                continue;
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ImageExtensions.Contains(ext))
                continue;

            string newPath = file + ".png";
            if (File.Exists(newPath))
                continue;
            File.Move(file, newPath);
            string oldMeta = file + ".meta";
            if (File.Exists(oldMeta))
                File.Delete(oldMeta);
        }
    }

    /// <summary>
    /// Первая распознанная текстура, извлеченная из embedded media модели.
    ///
    /// Ссылку материал->текстура из самого FBX Unity после `ExtractTextures`
    /// связывает по своей внутренней эвристике имен, и полагаться на нее
    /// нельзя — она уже подвела разок в этом же прогоне. Досвязываем сами,
    /// раз файл рядом с моделью гарантированно есть и гарантированно
    /// распознан текстурой (см. `RenameExtractedTexturesToPng`).
    /// </summary>
    private static Texture2D FindExtractedTexture(string modelPath)
    {
        string dir = TextureDirFor(modelPath);
        if (!Directory.Exists(dir))
            return null;

        string file = Directory
            .GetFiles(dir)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f)
            .FirstOrDefault();
        return file == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(file.Replace('\\', '/'));
    }

    /// <summary>
    /// Досвязывает материалы инстанса с извлеченной текстурой, если у них
    /// пусто и `_MainTex`, и `_BaseMap` (Standard и URP/Lit соответственно —
    /// шейдер материала заранее не известен).
    /// </summary>
    private static void EnsureTextured(GameObject instance, string modelPath)
    {
        var texture = FindExtractedTexture(modelPath);
        if (texture == null)
            return;

        foreach (var r in instance.GetComponentsInChildren<Renderer>())
        {
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == null)
                    continue;
                if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") == null)
                    mat.SetTexture("_MainTex", texture);
                if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") == null)
                    mat.SetTexture("_BaseMap", texture);
            }
        }
    }

    private static string MeasureOne(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            return Fail(name, "редактор не смог импортировать файл");

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (instance == null)
            return Fail(name, "импортированный объект не создается в сцене");

        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        var renderers = instance.GetComponentsInChildren<Renderer>();
        var bounds = new Bounds();
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

        int tris = 0;
        int submeshes = 0;
        int meshes = 0;
        foreach (var f in instance.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = f.sharedMesh;
            if (mesh == null)
                continue;
            meshes += 1;
            submeshes += mesh.subMeshCount;
            tris += mesh.triangles.Length / 3;
        }

        // Пивот объекта против центра его геометрии. Разъехавшийся пивот не
        // виден на габарите вовсе, а в сцене объект уезжает при любом повороте.
        Vector3 pivot_offset = bounds.center - instance.transform.position;

        var materials = new List<string>();
        foreach (var r in renderers)
        {
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == null || string.IsNullOrEmpty(mat.name))
                    continue;
                if (!materials.Contains(mat.name))
                    materials.Add(mat.name);
            }
        }

        var textures = new List<string>();
        foreach (var dep in AssetDatabase.GetDependencies(path, true))
        {
            string ext = Path.GetExtension(dep).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".tga" or ".psd" or ".exr")
                textures.Add(Path.GetFileName(dep));
        }

        // `GetDependencies` находит только то, на что реально ссылается сам
        // ассет модели. Текстуру, извлеченную из embedded media и
        // досвязанную вручную (`EnsureTextured`), она не всегда видит — а
        // молчание здесь означало бы соврать в отчете, что текстуры нет,
        // хотя рядом с моделью лежит распознанный файл.
        var extractedTexture = FindExtractedTexture(path);
        if (extractedTexture != null)
        {
            string extractedName = Path.GetFileName(AssetDatabase.GetAssetPath(extractedTexture));
            if (!textures.Contains(extractedName))
                textures.Add(extractedName);
        }

        float file_scale = AssetImporter.GetAtPath(path) is ModelImporter mi ? mi.fileScale : 1f;

        Object.DestroyImmediate(instance);

        if (!any)
            return Fail(name, "в файле нет ни одной видимой поверхности");

        return string.Format(
            CultureInfo.InvariantCulture,
            "  \"{0}\": {{ \"ok\": true, \"file\": \"{1}\", "
                + "\"size\": [{2:F4}, {3:F4}, {4:F4}], "
                + "\"min\": [{5:F4}, {6:F4}, {7:F4}], "
                + "\"max\": [{8:F4}, {9:F4}, {10:F4}], "
                + "\"center\": [{11:F4}, {12:F4}, {13:F4}], "
                + "\"pivot_offset\": [{14:F4}, {15:F4}, {16:F4}], "
                + "\"cells\": [{17:F3}, {18:F3}], "
                + "\"tris\": {19}, \"meshes\": {20}, \"submeshes\": {21}, "
                + "\"file_scale\": {22:F4}, "
                + "\"materials\": [{23}], \"textures\": [{24}] }}",
            name,
            Path.GetFileName(path),
            bounds.size.x, bounds.size.y, bounds.size.z,
            bounds.min.x, bounds.min.y, bounds.min.z,
            bounds.max.x, bounds.max.y, bounds.max.z,
            bounds.center.x, bounds.center.y, bounds.center.z,
            pivot_offset.x, pivot_offset.y, pivot_offset.z,
            bounds.size.x / Grid, bounds.size.z / Grid,
            tris, meshes, submeshes,
            file_scale,
            string.Join(", ", materials.Select(m => $"\"{m}\"")),
            string.Join(", ", textures.Distinct().Select(t => $"\"{t}\""))
        );
    }

    private static string Fail(string name, string why) =>
        $"  \"{name}\": {{ \"ok\": false, \"why\": \"{why}\" }}";

    // ---- Примерка ----------------------------------------------------------

    /// <summary>
    /// Ставит входящую модель на размеченный грунт рядом с эталоном роста и
    /// снимает кадр в `incoming-fit.png`.
    ///
    /// Числа отвечают на вопрос «влезает ли», кадр — на вопрос «выглядит ли она
    /// частью того же набора». Второй вопрос числами не берется: масштаб может
    /// быть верным до сотой доли, а модель все равно читается чужой, потому что
    /// у нее другая толщина деталей или другая палитра. Рубрика цикла ставит на
    /// это потолок в шестьдесят баллов по шкале сцены, и снять его косметикой
    /// нельзя.
    ///
    /// Разметка грунта — не украшение. Клетка 2.0 нарисована на земле, поэтому
    /// на кадре сразу видно, встает модель в сетку или торчит на треть клетки.
    /// </summary>
    public static void ShootIncoming()
    {
        var files = IncomingFiles();
        if (files.Count == 0)
        {
            Debug.LogError($"[intake] в {IncomingDir} нет моделей — примерять нечего");
            EditorApplication.Exit(1);
            return;
        }

        PrepareImporters(files);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildGround();
        BuildGrid();

        var bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;
        foreach (string path in files)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null)
                continue;
            instance.transform.position = Vector3.zero;
            EnsureTextured(instance, path);

            foreach (var r in instance.GetComponentsInChildren<Renderer>())
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
        }

        if (!any)
        {
            Debug.LogError("[intake] у модели нет видимых поверхностей — снимать нечего");
            EditorApplication.Exit(1);
            return;
        }

        PlaceReference(bounds);
        var cam = BuildCamera(bounds);
        BuildLight();

        const int width = 1400;
        const int height = 900;
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        shot.Apply();
        RenderTexture.active = previous;
        cam.targetTexture = null;

        File.WriteAllBytes(ShotFile, shot.EncodeToPNG());

        // Черный кадр в этом проекте уже четырежды выдавался за успех. Средняя
        // яркость стоит копейку и ловит ровно этот случай.
        Color[] pixels = shot.GetPixels();
        float sum = 0f;
        foreach (var p in pixels)
            sum += p.r + p.g + p.b;
        float brightness = sum / (pixels.Length * 3f);

        if (brightness < 0.02f)
        {
            Debug.LogError("[intake] кадр черный — графическое устройство не отдало картинку");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[intake] примерка снята: {ShotFile}, средняя яркость {brightness:F3}");
        EditorApplication.Exit(0);
    }

    private static Material FlatMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader);
        mat.color = color;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Glossiness"))
            mat.SetFloat("_Glossiness", 0.05f);
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0.05f);
        return mat;
    }

    private static void BuildGround()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "ground";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(4f, 1f, 4f); // плоскость Unity = 10 единиц
        ground.GetComponent<Renderer>().sharedMaterial = FlatMaterial(new Color(0.62f, 0.36f, 0.26f));
    }

    /// <summary>
    /// Разметка клетками 2.0: линии на грунте, по пять клеток в каждую сторону.
    ///
    /// Линии сдвинуты на полклетки, поэтому модель в начале координат стоит
    /// ВНУТРИ клетки, а не на перекрестье. Так и работает раскладка сцены —
    /// объекты сидят в центрах клеток. Первая редакция рисовала линии через
    /// ноль, модель оказывалась на стыке четырех клеток, и главный вопрос
    /// кадра — влезает ли она в свою — по нему не читался вовсе.
    /// </summary>
    private static void BuildGrid()
    {
        var line_mat = FlatMaterial(new Color(0.30f, 0.17f, 0.13f));
        const int cells = 5;
        const float thickness = 0.03f;
        float span = cells * Grid;

        for (int i = -cells; i <= cells; i++)
        {
            float at = (i + 0.5f) * Grid;

            var along_x = GameObject.CreatePrimitive(PrimitiveType.Cube);
            along_x.name = $"grid_x_{i}";
            along_x.transform.position = new Vector3(0f, 0.005f, at);
            along_x.transform.localScale = new Vector3(span * 2f, 0.01f, thickness);
            along_x.GetComponent<Renderer>().sharedMaterial = line_mat;

            var along_z = GameObject.CreatePrimitive(PrimitiveType.Cube);
            along_z.name = $"grid_z_{i}";
            along_z.transform.position = new Vector3(at, 0.005f, 0f);
            along_z.transform.localScale = new Vector3(thickness, 0.01f, span * 2f);
            along_z.GetComponent<Renderer>().sharedMaterial = line_mat;
        }
    }

    /// <summary>
    /// Эталон рядом с моделью.
    ///
    /// Ищем в проекте фигурку человека из набора Kenney: рост человека — самый
    /// быстрый способ прочитать масштаб, быстрее любой линейки. Если фигурки
    /// нет, ставим столбик в 1.8 метра — хуже, но лучше, чем ничего: без
    /// эталона кадр отвечает только на вопрос «красиво ли», а нужен ответ на
    /// вопрос «того ли она размера».
    /// </summary>
    private static void PlaceReference(Bounds model)
    {
        float at_x = model.min.x - 1.2f;

        string human = FindAsset("astronautA");
        if (human != null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(human);
            if (prefab != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                if (instance != null)
                {
                    instance.name = "reference_human";
                    instance.transform.position = new Vector3(at_x, 0f, 0f);
                    return;
                }
            }
        }

        var pole = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pole.name = "reference_pole_1m8";
        pole.transform.position = new Vector3(at_x, 0.9f, 0f);
        pole.transform.localScale = new Vector3(0.3f, 1.8f, 0.3f);
        pole.GetComponent<Renderer>().sharedMaterial = FlatMaterial(new Color(0.85f, 0.85f, 0.88f));
    }

    private static string FindAsset(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets($"{name} t:Model"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == name)
                return path;
        }
        return null;
    }

    /// <summary>
    /// Камера кадрирует по замеренным габаритам, а не по зашитым координатам.
    ///
    /// Модель может приехать любого размера, в том числе в чужих единицах —
    /// именно это и надо увидеть. Камера с фиксированной позицией на такой
    /// модели дает пустой кадр, и примерка молча превращается в «ничего не
    /// видно», хотя показать надо было ровно расхождение масштаба.
    /// </summary>
    private static Camera BuildCamera(Bounds model)
    {
        var go = new GameObject("camera");
        var cam = go.AddComponent<Camera>();
        cam.backgroundColor = new Color(0.10f, 0.11f, 0.16f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.fieldOfView = 35f;

        // Кадр держит и модель, и эталон рядом с ней: радиус считаем от общего
        // объема, иначе крупная модель выталкивает человека за край.
        var framed = model;
        framed.Encapsulate(new Bounds(new Vector3(model.min.x - 1.2f, 0.9f, 0f), new Vector3(0.6f, 1.8f, 0.6f)));

        float radius = framed.extents.magnitude;
        float distance = Mathf.Max(4f, radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.5f);
        var dir = new Vector3(0.75f, 0.55f, -1f).normalized;

        go.transform.position = framed.center + dir * distance;
        go.transform.LookAt(framed.center);
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = distance * 4f;
        return cam;
    }

    private static void BuildLight()
    {
        var go = new GameObject("sun");
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;
        light.color = new Color(1f, 0.96f, 0.90f);
        light.shadows = LightShadows.Soft;
        go.transform.rotation = Quaternion.Euler(48f, 35f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.35f, 0.33f, 0.38f);
    }
}
