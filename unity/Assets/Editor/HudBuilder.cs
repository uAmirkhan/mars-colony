using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Интерфейс в визуальном языке Township, собираемый кодом.
///
/// Зачем он в цикле «Двор». Ощущение «это мобильная игра» на существенную долю
/// создает интерфейс, а не трехмерная сцена: на любом кадре Township есть
/// кремовые панели, счетчик монет, звезда уровня, хаб иконок. Голый рендер
/// сцены, даже безупречный, сторонний человек называет рендером — у него нет ни
/// одного признака игры. Этот слой не зависит от конвейера моделей: он не
/// упирается в дефекты геометрии и не требует генерации ассетов.
///
/// Числа взяты из ux-township-artlanguage.md разделы 1, 3, 5 и не выдумываются.
/// Спрайты рисуются процедурно: внешние ассеты в проект не берутся.
///
/// Запуск: -executeMethod HudBuilder.Build
/// </summary>
public static class HudBuilder
{
    // Раздел 1 арт-библии. Значения читать как «в этой зоне спектра».
    private static readonly Color Panel = Hex("F7E8C6");   // кремовый с песочным подтоном
    private static readonly Color Edge = Hex("A8763E");    // теплая коричневая обводка
    private static readonly Color Title = Hex("6B3E1E");   // темно-коричневый текст
    private static readonly Color Soft = Hex("F2B705");    // мягкая валюта, круг
    private static readonly Color Prem = Hex("5FBF4A");    // премиум, шестигранник
    private static readonly Color Xp = Hex("2E9BE0");      // опыт, звезда
    private static readonly Color Act = Hex("4CAF2E");     // главное действие
    private static readonly Color Sec = Hex("F5901E");     // второстепенное

    private const int RefW = 1280, RefH = 800;
    private const string SpriteDir = "Assets/UI/Generated";
    private const string ScenePath = "Assets/Scenes/ColonyOurs.unity";

    [MenuItem("Mars/Sobrat interfeys")]
    public static void Build()
    {
        Directory.CreateDirectory(SpriteDir);
        AssetDatabase.Refresh();

        // Сцену открываем явно. В пакетном запуске активной оказывается та, что
        // была открыта последней, — и интерфейс молча собирался в чужой сцене.
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var old = GameObject.Find("HUD");
        if (old != null) Object.DestroyImmediate(old);

        var canvas = MakeCanvas();

        // Композиция раздела 5: интерфейс прижат к четырем краям, центр экрана
        // отдан миру целиком. Это правило важнее любого отдельного компонента:
        // стоит заполнить середину — и кадр перестает читаться игрой.
        BuildTopBar(canvas.transform);
        BuildHub(canvas.transform);
        BuildActionButton(canvas.transform);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[hud] интерфейс собран");
    }

    /// <summary>
    /// Кадр витка: сцена вместе с интерфейсом. Отдельный метод, потому что
    /// ColonyOursBuilder.Build пересобирает сцену с нуля и сносит холст —
    /// порядок обязателен: сборка сцены, затем интерфейс, затем съемка.
    /// </summary>
    [MenuItem("Mars/Snyat kadr")]
    public static void Shoot()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var camGo = GameObject.Find("kamera");
        var cam = camGo != null ? camGo.GetComponent<Camera>() : Camera.main;
        if (cam == null) { Debug.LogError("[hud] камеры нет, снимать нечем"); return; }

        // Камеру НЕ трогаем: она уже наведена сборщиком колонии на фактический
        // центр застройки и в таком виде сохранена в сцене.
        //
        // Здесь стояла своя точка съемки числами — цель (1.5, 0.4, -0.5) и
        // дистанция 30. После того как раскладка разъехалась, эти числа стали
        // указывать в пустой грунт, и кадр приемки показывал не ту колонию,
        // которую собирал ColonyOursBuilder. Две точки съемки на один кадр —
        // это гарантированная рассинхронизация; точка должна быть одна.
        Debug.Log($"[hud] снимаю по камере сцены: {cam.transform.position}, "
                + $"поле зрения {cam.fieldOfView:0.0}");

        // ЦВЕТОВОЕ ПРОСТРАНСТВО БУФЕРА — ОБЯЗАТЕЛЬНО sRGB, и сглаживание.
        //
        // Тот же дефект и то же лекарство, что уже записаны в
        // ColonyOursBuilder.Shoot: конструктор БЕЗ RenderTextureReadWrite
        // отдаёт буфер в пространстве проекта, а проект живёт в Linear, и
        // линейные числа уходят в PNG так, будто они уже sRGB. Кадр витка
        // выходил зелено-серым: охристый грунт мерялся (152, 168, 112)
        // вместо (196, 120, 96), все постройки читались в чужих цветах —
        // при том что colony-ours-obshchiy.png с той же камеры и той же
        // сцены выходил верным. Лекарство в этом файле просто не было
        // применено: здесь строка осталась короткой с самого начала.
        var rt = new RenderTexture(1600, 1000, 24,
                                   RenderTextureFormat.ARGB32,
                                   RenderTextureReadWrite.sRGB);
        rt.antiAliasing = 4;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tx = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        tx.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
        tx.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;

        File.WriteAllBytes("kadr-vitka.png", tx.EncodeToPNG());
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tx);
        Debug.Log("[hud] кадр витка снят: kadr-vitka.png");
    }

    private static Canvas MakeCanvas()
    {
        var go = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var c = go.GetComponent<Canvas>();

        // Режим камеры, а не экранного оверлея. Причина не стилистическая:
        // оверлейный холст рисуется поверх экрана и НЕ попадает в кадр,
        // снимаемый через Camera.Render() в RenderTexture. Вся приемка цикла
        // идет по такому кадру, значит интерфейс в оверлее был бы невидим для
        // судей — то есть его как будто и нет.
        var cam = GameObject.Find("kamera")?.GetComponent<Camera>() ?? Camera.main;
        if (cam != null)
        {
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
            c.planeDistance = 1f;
        }
        else
        {
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            Debug.LogWarning("[hud] камера не найдена — холст в оверлее, в кадр не попадет");
        }
        c.sortingOrder = 100;

        var s = go.GetComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(RefW, RefH);
        s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        s.matchWidthOrHeight = 0.5f;
        return c;
    }

    // ---- верхняя полоса: валюты и уровень ----------------------------------

    private static void BuildTopBar(Transform root)
    {
        // Три счетчика различаются НЕ ТОЛЬКО цветом, но и формой носителя:
        // круг, шестигранник, звезда. Цветовая слепота встречается у восьми
        // процентов мужчин, и на одном цвете читаемость держать нельзя.
        Counter(root, "credits", new Vector2(20, -20), Shape.Circle, Soft, "1 240");
        Counter(root, "isotopes", new Vector2(190, -20), Shape.Hex, Prem, "86");
        Counter(root, "level", new Vector2(-20, -20), Shape.Star, Xp, "7", right: true);
    }

    private static void Counter(Transform root, string name, Vector2 pos,
                                Shape shape, Color badge, string value, bool right = false)
    {
        var panel = Rect(root, name, 156, 52, pos, right ? Anchor.TopRight : Anchor.TopLeft);
        panel.GetComponent<Image>().sprite = RoundedSprite(156, 52, 22, 4);
        panel.GetComponent<Image>().color = Panel;
        // Обводка идет отдельным слоем позади: у Image нет своего контура, а
        // толстая теплая обводка — обязательный признак панели Township.
        Outline(panel, 156, 52, 22);

        var icon = Rect(panel.transform, "znak", 40, 40, new Vector2(8, 0), Anchor.Left);
        var im = icon.GetComponent<Image>();
        im.sprite = ShapeSprite(shape, 40);
        im.color = badge;

        var t = Rect(panel.transform, "chislo", 96, 40, new Vector2(-8, 0), Anchor.Right);
        Label(t, value, 26, TextAnchor.MiddleRight);
    }

    // ---- нижний хаб --------------------------------------------------------

    private static void BuildHub(Transform root)
    {
        // Хаб внимания: круглые иконки, крупные скругления, ни одного острого
        // угла. Четыре — минимум, при котором полоса читается хабом, а не
        // случайной кнопкой.
        string[] names = { "sklad", "fabrika", "stroyka", "dron" };
        Color[] tints = { Sec, Act, Edge, Xp };

        var bar = Rect(root, "hub", 4 * 84 + 24, 96, new Vector2(0, 16), Anchor.Bottom);
        bar.GetComponent<Image>().sprite = RoundedSprite(4 * 84 + 24, 96, 28, 4);
        bar.GetComponent<Image>().color = Panel;
        Outline(bar, 4 * 84 + 24, 96, 28);

        for (int i = 0; i < names.Length; i++)
        {
            float x = -((names.Length - 1) * 84f) / 2f + i * 84f;
            var b = Rect(bar.transform, names[i], 72, 72, new Vector2(x, 0), Anchor.Center);
            var im = b.GetComponent<Image>();
            im.sprite = ShapeSprite(Shape.Circle, 72);
            im.color = tints[i];
        }
    }

    private static void BuildActionButton(Transform root)
    {
        // Единственный элемент интерфейса, который догоняет мир по насыщенности.
        // Именно поэтому глаз находит его мгновенно (раздел 1, правило системы).
        var b = Rect(root, "deystvie", 210, 68, new Vector2(-20, 120), Anchor.BottomRight);
        b.GetComponent<Image>().sprite = RoundedSprite(210, 68, 18, 4);
        b.GetComponent<Image>().color = Act;
        Outline(b, 210, 68, 18, Darken(Act, 0.55f));

        var t = Rect(b.transform, "text", 210, 68, Vector2.zero, Anchor.Center);
        Label(t, "Погрузить", 24, TextAnchor.MiddleCenter, Color.white);
    }

    // ---- примитивы ---------------------------------------------------------

    private enum Anchor { TopLeft, TopRight, Left, Right, Center, Bottom, BottomRight }
    private enum Shape { Circle, Hex, Star }

    private static GameObject Rect(Transform parent, string name, float w, float h,
                                   Vector2 pos, Anchor a)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);

        Vector2 piv = new Vector2(0.5f, 0.5f), amin = piv, amax = piv;
        switch (a)
        {
            case Anchor.TopLeft: amin = amax = new Vector2(0, 1); piv = new Vector2(0, 1); break;
            case Anchor.TopRight: amin = amax = new Vector2(1, 1); piv = new Vector2(1, 1); break;
            case Anchor.Left: amin = amax = new Vector2(0, 0.5f); piv = new Vector2(0, 0.5f); break;
            case Anchor.Right: amin = amax = new Vector2(1, 0.5f); piv = new Vector2(1, 0.5f); break;
            case Anchor.Bottom: amin = amax = new Vector2(0.5f, 0); piv = new Vector2(0.5f, 0); break;
            case Anchor.BottomRight: amin = amax = new Vector2(1, 0); piv = new Vector2(1, 0); break;
        }
        rt.anchorMin = amin; rt.anchorMax = amax; rt.pivot = piv;
        rt.anchoredPosition = pos;
        return go;
    }

    /// <summary>Обводка отдельным слоем позади элемента: у Image контура нет.</summary>
    private static void Outline(GameObject target, float w, float h, int r, Color? col = null)
    {
        // Обводка кладется СОСЕДОМ перед панелью, а не ее ребенком. В uGUI
        // ребенок всегда рисуется поверх родителя, поэтому обводка-ребенок
        // закрашивала кремовую панель целиком и весь интерфейс выходил
        // коричневым. Порядок здесь задается индексом среди соседей.
        var o = new GameObject("obvodka", typeof(RectTransform), typeof(Image));
        var tRt = target.GetComponent<RectTransform>();
        o.transform.SetParent(target.transform.parent, false);
        o.transform.SetSiblingIndex(target.transform.GetSiblingIndex());

        var rt = o.GetComponent<RectTransform>();
        rt.anchorMin = tRt.anchorMin; rt.anchorMax = tRt.anchorMax;
        rt.pivot = tRt.pivot;
        rt.anchoredPosition = tRt.anchoredPosition;
        rt.sizeDelta = tRt.sizeDelta + new Vector2(8, 8);
        var im = o.GetComponent<Image>();
        im.sprite = RoundedSprite((int)w + 8, (int)h + 8, r + 4, 0);
        im.color = col ?? Edge;
    }

    private static void Label(GameObject go, string text, int size, TextAnchor a, Color? col = null)
    {
        Object.DestroyImmediate(go.GetComponent<Image>());
        var t = go.AddComponent<Text>();
        t.text = text;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.alignment = a;
        t.color = col ?? Title;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
    }

    // ---- процедурные спрайты -----------------------------------------------

    private static Sprite RoundedSprite(int w, int h, int r, int bevel)
    {
        string key = $"{SpriteDir}/rr_{w}x{h}_{r}_{bevel}.png";
        var cached = AssetDatabase.LoadAssetAtPath<Sprite>(key);
        if (cached != null) return cached;

        var tx = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = InRounded(x, y, w, h, r) ? 1f : 0f;
                // Легкий скос по верхней кромке: объем у Township всегда мягкий,
                // светлее сверху и темнее снизу, без единого резкого блика.
                float lift = bevel > 0 ? Mathf.Lerp(0.10f, -0.06f, (float)y / h) : 0f;
                tx.SetPixel(x, y, new Color(1f + lift, 1f + lift, 1f + lift, a));
            }
        tx.Apply();
        return SaveSprite(tx, key);
    }

    private static Sprite ShapeSprite(Shape s, int size)
    {
        string key = $"{SpriteDir}/{s}_{size}.png";
        var cached = AssetDatabase.LoadAssetAtPath<Sprite>(key);
        if (cached != null) return cached;

        var tx = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = size / 2f, rad = size / 2f - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c + 0.5f, dy = y - c + 0.5f;
                bool inside;
                switch (s)
                {
                    case Shape.Hex: inside = InHex(dx, dy, rad); break;
                    case Shape.Star: inside = InStar(dx, dy, rad); break;
                    default: inside = dx * dx + dy * dy <= rad * rad; break;
                }
                float lift = Mathf.Lerp(0.14f, -0.10f, (float)y / size);
                tx.SetPixel(x, y, new Color(1f + lift, 1f + lift, 1f + lift, inside ? 1f : 0f));
            }
        tx.Apply();
        return SaveSprite(tx, key);
    }

    private static bool InRounded(int x, int y, int w, int h, int r)
    {
        r = Mathf.Min(r, Mathf.Min(w, h) / 2);
        int cx = x < r ? r : (x >= w - r ? w - r - 1 : x);
        int cy = y < r ? r : (y >= h - r ? h - r - 1 : y);
        float dx = x - cx, dy = y - cy;
        return dx * dx + dy * dy <= (float)r * r;
    }

    private static bool InHex(float dx, float dy, float r)
    {
        float q = Mathf.Abs(dx) / r, p = Mathf.Abs(dy) / r;
        return p <= 0.866f && q * 0.866f + p * 0.5f <= 0.866f;
    }

    private static bool InStar(float dx, float dy, float r)
    {
        float ang = Mathf.Atan2(dy, dx), dist = Mathf.Sqrt(dx * dx + dy * dy);
        float step = Mathf.PI * 2f / 5f;
        float t = Mathf.Repeat(ang + Mathf.PI / 2f, step) / step;
        float edge = Mathf.Lerp(r * 0.45f, r, 1f - Mathf.Abs(t - 0.5f) * 2f);
        return dist <= edge;
    }

    private static Sprite SaveSprite(Texture2D tx, string path)
    {
        File.WriteAllBytes(path, tx.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var im = (TextureImporter)AssetImporter.GetAtPath(path);
        im.textureType = TextureImporterType.Sprite;
        im.spriteImportMode = SpriteImportMode.Single;
        im.alphaIsTransparency = true;
        im.mipmapEnabled = false;
        im.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Color Hex(string h) =>
        new Color(int.Parse(h.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                  int.Parse(h.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                  int.Parse(h.Substring(4, 2), System.Globalization.NumberStyles.HexNumber) / 255f);

    private static Color Darken(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
}
