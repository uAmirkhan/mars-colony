using System.Collections.Generic;
using System.IO;
using System.Linq;
using MarsColony.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Собирает изометрическую сцену колонии из вырезанных спрайтов — скриптом,
/// без единого клика в редакторе.
///
/// Три вещи здесь неочевидны и все три ломают картинку, если их пропустить.
///
/// 1. **Пивот спрайта — в основании, а не в центре.** Иначе здание висит над
///    своей клеткой, и вся сцена читается как сделанная наспех.
/// 2. **Сортировка по оси Y, а не по расстоянию до камеры.** В ортографии
///    камера равноудалена от всего, и без явной оси Unity рисует объекты в
///    случайном порядке: дальнее здание перекрывает ближнее.
/// 3. **Pixels Per Unit подбирается под размер спрайта**, иначе объекты,
///    нарезанные с разных листов, приезжают в разном масштабе — это ровно та
///    боль, из-за которой владелец четыре дня подгонял генерации вручную.
/// </summary>
public static class SceneBuilder
{
    private const string SpriteDir = "Assets/Sprites";
    private const string ScenePath = "Assets/Scenes/Main.unity";

    /// <summary>Целевая высота здания в мировых единицах — общий масштаб сцены.</summary>
    private const float TargetHeight = 2.6f;

    /// <summary>Габарит клетки. Изометрия 2:1 — ширина вдвое больше высоты.</summary>
    private const float TileWidth = 3.4f;
    private const float TileHeight = 1.7f;

    /// <summary>Настройка импорта: спрайт, пивот снизу по центру, без сжатия.</summary>
    private static void ImportSprites()
    {
        foreach (string path in Directory.GetFiles(SpriteDir, "*.png"))
        {
            string asset = path.Replace('\\', '/');
            var importer = AssetImporter.GetAtPath(asset) as TextureImporter;
            if (importer == null)
                continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePivot = new Vector2(0.5f, 0.08f);
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 1024;

            // Пивот применяется только через settings-объект: присвоение
            // spritePivot напрямую редактор молча игнорирует.
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(0.5f, 0.08f);
            importer.SetTextureSettings(settings);

            importer.SaveAndReimport();
        }
        AssetDatabase.Refresh();
    }

    private static Sprite Load(string name) =>
        AssetDatabase.LoadAssetAtPath<Sprite>($"{SpriteDir}/{name}.png");

    /// <summary>Ставит спрайт в мир по изометрическим координатам клетки.</summary>
    private static GameObject Place(string name, float cell_x, float cell_y, float scale = 1f)
    {
        Sprite sprite = Load(name);
        if (sprite == null)
        {
            Debug.LogWarning($"[scene] нет спрайта {name}");
            return null;
        }

        var go = new GameObject(name);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;

        // Изометрия 2:1 — классическая для мобильных ситибилдеров: край клетки
        // ложится на целые пиксели, и тайлы стыкуются без щелей.
        float x = (cell_x - cell_y) * TileWidth * 0.5f;
        float y = (cell_x + cell_y) * TileHeight * 0.5f;
        go.transform.position = new Vector3(x, y, 0);

        // Общий масштаб: приводим здания с разных листов к одной высоте.
        float h = sprite.bounds.size.y;
        float k = (TargetHeight * scale) / Mathf.Max(0.01f, h);
        go.transform.localScale = new Vector3(k, k, 1);

        // Чем ниже объект на экране, тем он ближе — тем позже рисуется.
        sr.sortingOrder = Mathf.RoundToInt(-y * 100);

        // Общий тональный сдвиг: у каждой генерации своя температура цвета, и
        // рядом они читаются как вырезки из разных журналов. Легкий теплый
        // тон, одинаковый для всех, собирает их в одну сцену.
        sr.color = new Color(1.0f, 0.965f, 0.925f);
        return go;
    }

    /// <summary>Кладет тайл земли. Отдельно от зданий: у него свой слой и цвет.</summary>
    private static void PlaceGround(string name, float cell_x, float cell_y, Color tint)
    {
        Sprite sprite = Load(name);
        if (sprite == null)
            return;

        var go = new GameObject($"ground_{cell_x}_{cell_y}");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = tint;

        float x = (cell_x - cell_y) * TileWidth * 0.5f;
        float y = (cell_x + cell_y) * TileHeight * 0.5f;
        go.transform.position = new Vector3(x, y, 0);

        // Тайл масштабируется по ШИРИНЕ, а не по высоте: щели между клетками
        // видны по горизонтали, и подгонять надо именно ее.
        float k = (TileWidth * 1.02f) / Mathf.Max(0.01f, sprite.bounds.size.x);
        go.transform.localScale = new Vector3(k, k, 1);

        // Земля всегда под всем: отдельный слой сортировки, а не смещение
        // порядка. Иначе высокое здание с дальней клетки уйдет под ближний тайл.
        sr.sortingLayerName = "Default";
        sr.sortingOrder = -20000 + Mathf.RoundToInt(-y * 100);
    }


    /// <summary>
    /// Мягкое пятно тени. Генерируется кодом, а не берется файлом.
    ///
    /// Это самая дешевая и самая недооцененная деталь во всей сцене. Без
    /// контактной тени объект не стоит на земле, а лежит поверх фона, и любая
    /// расстановка читается как коллаж из вырезок. Одно пятно под зданием
    /// делает для достоверности больше, чем перерисовка самого здания.
    /// </summary>
    private static Sprite MakeShadowSprite()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Эллипс, а не круг: в изометрии тень лежит на земле, а земля
            // видна под углом и сжата по вертикали вдвое.
            float dx = (x - size / 2f) / (size / 2f);
            float dy = (y - size / 2f) / (size / 2f) * 2.0f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(1f - d);
            a = a * a * 0.55f; // квадрат дает мягкий край вместо кольца
            tex.SetPixel(x, y, new Color(0.12f, 0.05f, 0.02f, a));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite _shadow;

    /// <summary>Кладет тень под объект, привязывая ее к ширине спрайта.</summary>
    private static void PlaceShadow(GameObject under, float width_k)
    {
        if (under == null)
            return;
        _shadow ??= MakeShadowSprite();

        var go = new GameObject("shadow");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _shadow;

        var host = under.GetComponent<SpriteRenderer>();
        float w = host.bounds.size.x;
        go.transform.position = under.transform.position + new Vector3(0f, w * 0.06f, 0f);
        go.transform.localScale = new Vector3(w * width_k, w * width_k * 0.55f, 1f);

        // На единицу ниже хозяина: тень обязана лежать под своим зданием, но
        // поверх плиты и поверх зданий, стоящих дальше.
        sr.sortingOrder = host.sortingOrder - 1;
    }

    // --- Играбельность --------------------------------------------------
    //
    // Прогон 1 показал сцену, в которой не было ни одного объекта, реагирующего
    // на ввод: 38 объектов, 37 спрайтов, ни одного скрипта в плеере. Красивая
    // картинка проходит проверку сборки целиком, потому что проверка сборки про
    // другое. Ниже собирается то, чего не хватало: ведущий объект, кликабельная
    // грядка и полоса состояния.

    /// <summary>Отступ и шаг строк HUD. Верстка, а не баланс.</summary>
    private const float HudMargin = 18f;
    private const float HudPad = 12f;
    private const float HudLine = 32f;
    private const float HudPanelWidth = 470f;
    private const float HudPanelHeight = HudPad * 2f + HudLine * 4f;
    private const int HudFontSize = 20;
    private const int HudHintFontSize = 17;

    /// <summary>
    /// Шрифт HUD. Встроенный `LegacyRuntime.ttf` не годится: в собранном плеере
    /// он выкинул ВСЮ кириллицу молча — «Кредиты: 49» приехало на экран как
    /// «: 49». В редакторе этого не видно, там динамический шрифт добирает
    /// глифы из системных, а в браузере системных шрифтов нет. Поэтому шрифт
    /// с кириллицей лежит в проекте файлом; лицензия рядом.
    /// </summary>
    private const string FontPath = "Assets/Fonts/RobotoMono-Regular.ttf";

    private static void BuildInteraction(GameObject dome)
    {
        if (dome != null)
        {
            // Грядка №0 состояния колонии. Остальные три слота (FIELD_SLOTS_START)
            // существуют в домене, но своего здания на сцене пока не имеют:
            // добавить их — значит добавить вида, а не правила.
            var field = dome.AddComponent<FieldView>();
            field.field_index = 0;
        }
        else
        {
            Debug.LogWarning("[scene] купол не найден — нажимать в сборке будет не на что");
        }

        var game_go = new GameObject("Colony");
        game_go.AddComponent<ColonyGame>();

        BuildHud();
    }

    /// <summary>
    /// Полоса состояния. Собирается скриптом, как и вся сцена: ни одного клика
    /// в редакторе, иначе сборка перестает воспроизводиться с чистого состояния.
    ///
    /// Шрифт присваивается ЗДЕСЬ, а не в рантайме: ссылка из сцены гарантирует,
    /// что он попадет в сборку. Запрошенный по имени на старте плеера, он может
    /// не доехать после вырезания неиспользуемого — и HUD окажется пустым
    /// прямоугольником без единой ошибки в консоли.
    /// </summary>
    private static void BuildHud()
    {
        EnsureUiShaderIncluded();

        var canvas_go = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvas_go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvas_go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 800f);
        scaler.matchWidthOrHeight = 0.5f;

        var panel_go = new GameObject("panel", typeof(Image));
        panel_go.transform.SetParent(canvas_go.transform, false);
        var panel = (RectTransform)panel_go.transform;
        panel.anchorMin = new Vector2(0f, 1f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(HudMargin, -HudMargin);
        panel.sizeDelta = new Vector2(HudPanelWidth, HudPanelHeight);
        var background = panel_go.GetComponent<Image>();
        background.color = new Color(0.07f, 0.04f, 0.03f, 0.78f);
        background.material = UiMaterial();

        var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (font == null)
        {
            Debug.LogWarning($"[scene] шрифт {FontPath} не найден — кириллица в HUD пропадет");
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        var hud = canvas_go.AddComponent<Hud>();
        hud.panel = panel;
        hud.credits_label = MakeLabel(panel, font, "credits", 0, HudFontSize, Color.white);
        hud.warehouse_label = MakeLabel(panel, font, "warehouse", 1, HudFontSize, Color.white);
        hud.xp_label = MakeLabel(panel, font, "xp", 2, HudFontSize, Color.white);
        hud.hint_label = MakeLabel(
            panel,
            font,
            "hint",
            3,
            HudHintFontSize,
            new Color(1f, 0.82f, 0.45f)
        );
    }

    /// <summary>
    /// Материал интерфейса, назначенный ЯВНО.
    ///
    /// Первая сборка HUD приехала сплошным розовым прямоугольником: у проекта
    /// пустой список `m_AlwaysIncludedShaders`, интерфейса в сцене раньше не
    /// было, и шейдер `UI/Default` в плеер просто не попал. Розовый — это Unity
    /// говорит «шейдер не найден», и никакой ошибки в консоли при этом нет.
    /// Ссылка на материал из сцены тянет шейдер за собой.
    /// </summary>
    private static Material UiMaterial()
    {
        var material = AssetDatabase.GetBuiltinExtraResource<Material>("Default UI Material.mat");
        if (material == null)
            material = Canvas.GetDefaultCanvasMaterial();
        if (material == null)
            Debug.LogWarning("[scene] материал интерфейса не найден — HUD будет розовым");
        return material;
    }

    /// <summary>
    /// Кладет шейдер интерфейса в список обязательных к сборке. Ссылки из сцены
    /// одной бывает мало: вырезание неиспользуемого работает по своим правилам,
    /// и проверить, что оно оставило шейдер, можно только собранным плеером.
    /// Две страховки дешевле одного розового HUD у рекрутера на экране.
    /// </summary>
    private static void EnsureUiShaderIncluded()
    {
        Shader shader = Shader.Find("UI/Default");
        if (shader == null)
        {
            Debug.LogWarning("[scene] шейдер UI/Default не найден");
            return;
        }

        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (assets == null || assets.Length == 0)
            return;

        var settings = new SerializedObject(assets[0]);
        SerializedProperty list = settings.FindProperty("m_AlwaysIncludedShaders");
        if (list == null)
            return;

        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                return;

        list.InsertArrayElementAtIndex(list.arraySize);
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
        settings.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        Debug.Log("[scene] шейдер UI/Default добавлен в обязательные");
    }

    private static Text MakeLabel(
        RectTransform parent,
        Font font,
        string name,
        int row,
        int size,
        Color color
    )
    {
        var go = new GameObject(name, typeof(Text));
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(HudPad, -(HudPad + row * HudLine));
        rt.sizeDelta = new Vector2(HudPanelWidth - HudPad * 2f, HudLine);

        var text = go.GetComponent<Text>();
        text.material = UiMaterial();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.text = name;
        return text;
    }

    public static void BuildScene()
    {
        ImportSprites();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Без явной оси прозрачности Unity сортирует по расстоянию до камеры,
        // а в ортографии оно у всех одинаковое — порядок отрисовки становится
        // случайным. Это первое, что ломает изометрию.
        GraphicsSettings.transparencySortMode = TransparencySortMode.CustomAxis;
        GraphicsSettings.transparencySortAxis = new Vector3(0f, 1f, 0f);

        var cam_go = new GameObject("Main Camera");
        var cam = cam_go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5.4f;
        cam.transform.position = new Vector3(0f, 1.1f, -10f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.31f, 0.19f); // марсианский песок
        cam_go.tag = "MainCamera";

        // Плиты под здания. Тип грунта объясняет, что на нем стоит:
        // металлический настил под транспортом, каменистый под добычей,
        // песок под жилой и производственной частью.
        var plots = new (string tile, float x, float y)[]
        {
            ("tile_metal", 2f, -1f),
            ("tile_metal", 3f, 0f),
            ("tile_rock", -2f, 1f),
            ("tile_rock", -3f, 2f),
            ("tile_ice", -3f, 3f),
            ("tile_sand", -1f, -1f),
            ("tile_sand", 0f, 0f),
            ("tile_sand", 1f, 1f),
            ("tile_sand", 1f, 0f),
            ("tile_sand", 0f, -1f),
            ("tile_sand", -1f, 1f),
            ("tile_sand", 0f, 2f),
            ("tile_sand", -2f, 0f),
            ("tile_sand", 2f, 1f),
            ("tile_sand", -1f, 2f),
        };
        foreach (var (tile, px, py) in plots)
            PlaceGround(tile, px, py, Color.white);

        // Колония: добыча слева на камне, производство в центре, транспорт
        // справа на настиле. Здания слегка перекрываются — город узнается
        // именно по перекрытию силуэтов, а не по объектам, стоящим порознь.
        PlaceShadow(Place("drill", -2f, 1f, 0.85f), 0.7f);

        // Купол — грядка гидропоники и единственная кликабельная точка среза.
        // Ссылка на объект нужна ниже, поэтому он не уходит в PlaceShadow сразу.
        var dome = Place("dome", -1f, 2f, 1.1f);
        PlaceShadow(dome, 0.78f);

        PlaceShadow(Place("food_factory", -1f, -1f, 0.95f), 0.72f);
        PlaceShadow(Place("warehouse", 0.05f, 0.05f, 1.0f), 0.74f);
        PlaceShadow(Place("atmospheric", 1f, 1.05f, 0.95f), 0.7f);
        PlaceShadow(Place("textile", 0f, 2f, 0.95f), 0.72f);
        PlaceShadow(Place("construction", -2f, 0f, 0.9f), 0.7f);
        PlaceShadow(Place("habitat", 1f, 0f, 0.75f), 0.66f);
        PlaceShadow(Place("landing_pad", 2.05f, -1f, 1.0f), 0.8f);
        PlaceShadow(Place("drone_pad", 2f, 1f, 0.45f), 0.6f);

        // Транспорт стоит НАД своей площадкой: тот же порядок сортировки
        // плюс единица, иначе шаттл уходит под настил.
        var shuttle = Place("shuttle", 2.0f, -1.05f, 0.72f);
        if (shuttle != null)
            shuttle.GetComponent<SpriteRenderer>().sortingOrder += 2;

        var drone = Place("drone", 1.95f, 1.05f, 0.3f);
        if (drone != null)
        {
            drone.GetComponent<SpriteRenderer>().sortingOrder += 2;
            // Дрон летит: приподнят над тумбой, тень под ним шире и бледнее.
            drone.transform.position += new Vector3(0f, 0.9f, 0f);
        }

        BuildInteraction(dome);

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log($"[scene] собрана: {ScenePath}");
    }

    /// <summary>Собрать сцену и сразу веб-плеер — одной командой из терминала.</summary>
    public static void BuildSceneAndWeb()
    {
        BuildScene();
        BuildScript.BuildWeb();
    }
}
