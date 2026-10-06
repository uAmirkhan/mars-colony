using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Общая механика сборки сцены v2. Строитель, снимок и диагностика пользуются
/// одним и тем же кодом размещения, но живут в разных точках входа: снимок не
/// должен делить отказы со строителем.
/// </summary>
public static class V2Lib
{
    public const string SCENE_PATH = "Assets/Scenes/SceneV2.unity";
    public const int SHOT_W = 1600;
    public const int SHOT_H = 1000;

    // Ортографическая камера. Жанр изометрический, и главное: при ортографии
    // «земля до краев кадра» решается размером земли, а не подбором угла.
    public const float CAM_PITCH = 38f;    // наклон вниз от горизонта
    public const float CAM_YAW = 45f;      // разворот
    // Расширено с 22 до 28. Постройки после среза плиты все равно вытянуты по
    // земле: купол 18 м в плане при высоте 5.6, отношение 3.2 к 1, тогда как в
    // Township постройки примерно 1 к 1. Колония из восьми таких в кадр 70 м
    // физически не помещается, и расталкивание было следствием, а не причиной.
    public const float CAM_SIZE = 28f;    // половина вертикали кадра в метрах
    public const float GROUND_HALF = 120f; // земля 240 на 240, заведомо за кадр

    public const float FIGURA_ROST = 1.8f; // рост калибровочной фигуры, метры

    // ------------------------------------------------------------ ассеты

    private static readonly string[] ASSET_DIRS =
    {
        // РОВНО ДВЕ ПАПКИ, и это не сокращение поиска, а состав набора.
        //
        // Сцена `VseAssety.unity` - источник истины по тому, какие ассеты идут в
        // дело. Разбор ее ссылок по GUID дал 40 моделей: 25 из `v3` и 15 из
        // `v2`. Корневой слой `OurAssets/` не используется НИ ОДНОЙ ссылкой.
        //
        // Чем это стоило. Я искал модели по имени по всем папкам сразу и
        // подтянул корневые версии - те самые, у которых под зданием сидит
        // плита-подложка два на два. Плиты я нашел, замерил и вырезал в
        // Blender. В утвержденном наборе этих моделей нет вовсе: в `v2` плита
        // нашлась ровно у одной буровой, в `v3` не нашлась нигде. То есть
        // задача, на которую ушел целый заход, в нужном наборе не стояла.
        //
        // Урок: состав набора берется из того, на что ссылается эталон, а не из
        // совпадения имен файлов на диске.
        "Assets/OurAssets/v3",
        "Assets/OurAssets/v2",
    };

    /// <summary>Ищет модель по имени во всех слоях библиотеки, свежие первыми.</summary>
    public static GameObject FindModel(string name)
    {
        foreach (var dir in ASSET_DIRS)
        {
            string p = $"{dir}/{name}.fbx";
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go != null) return go;
        }
        Debug.LogWarning($"[v2] модель не найдена: {name}");
        return null;
    }

    /// <summary>Текстура модели. Соглашение библиотеки: имя_textures/tex_0.png.</summary>
    public static Texture2D FindTexture(string name)
    {
        // Облегченные копии называются имя-decNN и своей папки текстур не имеют:
        // текстуру берем у исходной модели, она та же самая.
        var kandidaty = new List<string> { name };
        int i = name.LastIndexOf("-dec");
        if (i > 0) kandidaty.Add(name.Substring(0, i));

        // Расширение обязано проверяться и png, и jpg. Часть партии выгружена в
        // jpg, и поиск только по png молча отдавал null: шаттлы рендерились
        // белыми, без единой ошибки в логе. Файл был на месте все это время.
        foreach (var n in kandidaty)
            foreach (var dir in ASSET_DIRS)
                foreach (var ext in new[] { "png", "jpg", "jpeg" })
                {
                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        $"{dir}/{n}_textures/tex_0.{ext}");
                    if (t != null) return t;
                }
        Debug.LogWarning($"[v2] текстура не найдена ни в одном расширении: {name}");
        return null;
    }

    /// <summary>Текстура земли. Ищет по трем папкам: сначала новые, потом
    /// старые. В tex-staraya лежат более насыщенные версии, чем в ground:
    /// у льда там 0.295 против 0.061, то есть настоящий голубой против серого.</summary>
    public static Texture2D NaydiZemlyu(string imya)
    {
        string[] dirs =
        {
            "Assets/OurAssets/ground2",
            "Assets/OurAssets/ground",
        };
        foreach (var d in dirs)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{d}/{imya}.png");
            if (t != null) return t;
        }
        Debug.LogWarning($"[v2] текстура земли не найдена: {imya}");
        return null;
    }

    // ------------------------------------------------------------ материалы

    public const string MAT_DIR = "Assets/OurAssets/Materials/V2";

    /// <summary>
    /// Материал URP. Три вещи, каждая оплачена отдельно:
    ///
    /// 1. Standard в URP-проекте дает пурпур, карта живет в _BaseMap, не в _MainTex.
    /// 2. Импортер FBX материал не приносит, создавать надо кодом.
    /// 3. Материал ОБЯЗАН быть сохранен ассетом. Материалы URP Lit, созданные в
    ///    памяти и живущие только внутри сцены, схлопываются: все Lit-объекты
    ///    кадра рендерятся одним и тем же цветом независимо от своих _BaseColor
    ///    и _BaseMap. Поймано контрольным щитом: пурпурный и текстурный Unlit
    ///    вышли верно, а три разных Lit - одинаковым зеленым. В сцене это
    ///    выглядело как «текстуры не применяются», хотя в инспекторе все на
    ///    месте: шейдер верный, _BaseMap привязан, UV есть.
    /// </summary>
    public static Material NewLit(Color tint, Texture2D tex = null,
                                  float smooth = 0.15f, float metal = 0f,
                                  string imya = null)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", tint);
        if (tex != null) m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        return SaveMat(m, imya ?? $"lit-{tint.r:F2}-{tint.g:F2}-{tint.b:F2}" +
                                  (tex != null ? "-" + tex.name : ""));
    }

    /// <summary>Кладет материал ассетом в проект и возвращает уже сохраненный.</summary>
    public static Material SaveMat(Material m, string imya)
    {
        if (!Directory.Exists(MAT_DIR)) Directory.CreateDirectory(MAT_DIR);

        // Путь ФИКСИРОВАННЫЙ, без GenerateUniqueAssetPath.
        //
        // С уникальным путем каждая сборка заводила новую копию каждого
        // материала: к этому замеру в папке лежало 7492 материала на 44 МБ,
        // из них 69 копий одного `rekv-krem`. Ошибки нет, сцена работает,
        // а база ассетов растет без конца - и замер текстурной памяти врет
        // в большую сторону, потому что считает все копии.
        //
        // Последствие серьезнее свалки: копии НЕ пакетируются между собой.
        // Два объекта с одинаковым по содержанию, но разным по ассету
        // материалом дают два вызова отрисовки вместо одного.
        string path = $"{MAT_DIR}/{imya}.mat";
        var est = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (est != null)
        {
            est.CopyPropertiesFromMaterial(m);
            est.shader = m.shader;
            EditorUtility.SetDirty(est);
            Object.DestroyImmediate(m);
            return est;
        }
        AssetDatabase.CreateAsset(m, path);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    /// <summary>
    /// Материал с вырезом по альфе. Для наклеек на землю: разметка, знаки,
    /// пятна. Взят вырез, а не прозрачность - у прозрачности порядок отрисовки
    /// зависит от расстояния, и наклейки начинают моргать при развороте камеры.
    /// </summary>
    public static Material NewLitVyrez(Color tint, Texture2D tex, string imya)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", tint);
        if (tex != null) m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Smoothness", 0.05f);
        m.SetFloat("_AlphaClip", 1f);
        m.SetFloat("_Cutoff", 0.5f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.renderQueue = 2450;
        return SaveMat(m, "vyrez-" + imya);
    }

    /// <summary>Материал без освещения. Нужен контрольному щиту: цвет на кадре
    /// обязан совпасть с заданным, свет в это не вмешивается.</summary>
    public static Material NewUnlit(Color tint, Texture2D tex = null, string imya = null)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", tint);
        if (tex != null) m.SetTexture("_BaseMap", tex);
        return SaveMat(m, imya ?? $"unlit-{tint.r:F2}-{tint.g:F2}-{tint.b:F2}");
    }

    // ------------------------------------------------------------ размещение

    /// <summary>Габарит объекта по всем видимым рендерерам, в мировых единицах.</summary>
    public static bool WorldBounds(GameObject go, out Bounds b)
    {
        b = new Bounds();
        var rs = go.GetComponentsInChildren<Renderer>();
        bool any = false;
        foreach (var r in rs)
        {
            if (!r.enabled) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return any;
    }

    /// <summary>
    /// Ставит модель на землю в точку xz и задает ей ВЫСОТУ в метрах.
    ///
    /// Задается именно высота, а не длинная горизонталь. Прошлый сборщик
    /// принимал горизонтальный размер, и у моделей с плоскими широкими
    /// пропорциями высота уезжала сама - так получилась инверсия «завод меньше
    /// склада» при верных числах в коде.
    /// </summary>
    /// <summary>
    /// Имена, которым разрешен СВОБОДНЫЙ угол поворота.
    ///
    /// Список взят из `design/tools/proverka-rasstanovki.py` дословно. У камня,
    /// льда, грунта и пыли нет опознающей грани - они узнаются силуэтом с любой
    /// стороны. У всего остального грань есть, и повернутая на 25 градусов
    /// постройка читается опрокинутой.
    /// </summary>
    private static readonly string[] SVOBODNYY_UGOL =
    {
        "kamen", "kamni", "led-", "grunt-", "izmoroz", "pyl-", "valun",
    };

    /// <summary>
    /// Приводит угол к закону расстановки: кратно 90 градусам для всего, у чего
    /// есть опознающая грань.
    ///
    /// Шаг 5 спеки `SPEKA-RASSTANOVKI.md`: «Ориентация: строго 0 и 90. Сейчас
    /// 105 нарушений, цель 0.» Я ставил постройки под 25, 45, 120, 205 - и
    /// владелец увидел ровно то, что спека и предсказывала: «они перевернуты».
    ///
    /// Разнообразие, которое хочется взять поворотом, спека велит брать
    /// перекраской. Поворот для этого не годится.
    /// </summary>
    /// <summary>
    /// Наклон модели, если она приходит из файла лежащей на боку.
    ///
    /// У части моделей ось «вверх» в файле не совпадает с осью Y сцены: купол
    /// приезжает лежащим, плоским основанием вбок, и читается опрокинутым яйцом.
    /// Владелец это увидел сразу и назвал прямо: «они перевернуты».
    ///
    /// Таблица явная и короткая. Судить по имени нельзя, автоматически - тем
    /// более: правило «низ шире верха» переворачивает гриб вверх ногами, потому
    /// что у гриба шляпка шире ножки. Каждая строка проверена кадром сцены.
    /// </summary>
    private static readonly Dictionary<string, Vector2> NAKLON = new()
    {
        { "kupol-geodezicheskiy", new Vector2(270f, 0f) },
        // Башня приезжает лежащей ребристым цилиндром. Башня обязана стоять.
        { "zhiloy-bashnya",       new Vector2(90f, 0f) },
        // Посадочная площадка приезжает стоящей на ребре, как прислоненная
        // тарелка. Площадка обязана лежать плашмя.
        { "ploshchadka-shattla",  new Vector2(90f, 0f) },
        // Разворот по X у моделей, приезжающих лежащими. Каждая строка
        // проверена портретом, а не выведена правилом: правило «низ шире
        // верха» переворачивает гриб вверх ногами, а гусеничную машину ставит
        // на попа. Механизм дефекта - перепутанные при импорте оси, см.
        // SVOD-PRAVIL.md.
        { "sklad-angar",          new Vector2(90f, 0f) },
        { "sklad-bunkery",        new Vector2(90f, 0f) },
        { "zavod-pishchevoy",     new Vector2(90f, 0f) },
        { "burovaya-02",          new Vector2(90f, 0f) },
        // burovaya-04 наклона НЕ получает: это гусеничная машина, как и
        // burovaya-03, и X=90 ставит ее на попа. Проверено портретом.
        { "burovaya-05",          new Vector2(90f, 0f) },
        { "zhiloy-kupol",         new Vector2(90f, 0f) },
    };

    public static Vector2 NaklonModeli(string model)
        => NAKLON.TryGetValue(model, out var n) ? n : Vector2.zero;

    public static float ZakonUgla(string model, float yawDeg)
    {
        foreach (var p in SVOBODNYY_UGOL)
            if (model.StartsWith(p)) return yawDeg;
        return Mathf.Round(yawDeg / 90f) * 90f;
    }

    public static GameObject Place(string model, Vector2 xz, float vysota,
                                   float yawDeg = 0f, Transform parent = null,
                                   Color? kraska = null, string kraskaImya = null)
    {
        yawDeg = ZakonUgla(model, yawDeg);
        var src = FindModel(model);
        if (src == null) return null;

        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        go.name = model;
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = Vector3.zero;
        var nk = NaklonModeli(model);
        go.transform.rotation = Quaternion.Euler(nk.x, yawDeg, nk.y);
        go.transform.localScale = Vector3.one;

        if (!WorldBounds(go, out var b) || b.size.y < 1e-5f)
        {
            Debug.LogWarning($"[v2] нет габарита у {model}");
            return go;
        }

        // Высота считается по ТЕЛУ, а не по габариту целиком.
        //
        // Если под зданием сидит плита-подложка, масштаб по общему габариту
        // растягивает ее вместе со зданием: купол высотой 6 м занимает в плане
        // 41 м. Модель при этом остается ИСХОДНОЙ - плита не удаляется, она
        // уводится под грунт.
        float dolyaPlity = V2Plita.DolyaPlity(go, model);
        float vysTela = b.size.y * (1f - dolyaPlity);
        if (vysTela < 1e-5f) vysTela = b.size.y;

        go.transform.localScale = Vector3.one * (vysota / vysTela);

        // пересчет после масштаба; на грунт садится низ ТЕЛА, а плита остается
        // ниже нуля и в кадр не попадает
        WorldBounds(go, out b);
        var pos = go.transform.position;
        pos.x += xz.x - b.center.x;
        pos.z += xz.y - b.center.z;
        pos.y += -(b.min.y + b.size.y * dolyaPlity);
        go.transform.position = pos;

        SnyatChuzhoe(go);
        ApplyTexture(go, model, kraska, kraskaImya);
        return go;
    }

    /// <summary>
    /// Ставит модель, задавая ей размер В ПЛАНЕ, а не по высоте.
    ///
    /// Нужно плоским объектам: посадочной площадке, террасе вскрыши, настилу.
    /// Масштаб по высоте у них бессмыслен - у площадки высота почти ноль, и
    /// попытка задать ее делает объект либо гигантским, либо невидимым.
    /// Документация задает площадке размер пятна 15 м, а не высоту.
    /// </summary>
    /// <param name="prizhat">Если больше нуля - высота принудительно
    /// прижимается к этому значению неравномерным масштабом. Нужно плоским
    /// вещам, у которых оси при импорте разошлись: посадочная площадка имеет в
    /// файле толщину 0.452 при ширине 0.928, а в Unity ее толщина встала по
    /// горизонтали, и задание плана в 17 м давало высоту 16.5 м - бочку
    /// размером с дом вместо площадки.</param>
    public static GameObject PlaceByPlan(string model, Vector2 xz, float plan,
                                         float yawDeg = 0f, Transform parent = null,
                                         Color? kraska = null, string kraskaImya = null,
                                         float prizhat = 0f)
    {
        var src = FindModel(model);
        if (src == null) return null;

        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        go.name = model;
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = Vector3.zero;
        var nk2 = NaklonModeli(model);
        go.transform.rotation = Quaternion.Euler(nk2.x, ZakonUgla(model, yawDeg), nk2.y);
        go.transform.localScale = Vector3.one;

        if (!WorldBounds(go, out var b)) return go;
        float shirina = Mathf.Max(b.size.x, b.size.z);
        if (shirina < 1e-5f) return go;

        go.transform.localScale = Vector3.one * (plan / shirina);

        if (prizhat > 0f)
        {
            WorldBounds(go, out b);
            if (b.size.y > prizhat)
            {
                var sc = go.transform.localScale;
                sc.y *= prizhat / b.size.y;
                go.transform.localScale = sc;
            }
        }

        WorldBounds(go, out b);
        var pos = go.transform.position;
        pos.x += xz.x - b.center.x;
        pos.z += xz.y - b.center.z;
        pos.y += -b.min.y;
        go.transform.position = pos;

        SnyatChuzhoe(go);
        ApplyTexture(go, model, kraska, kraskaImya);
        return go;
    }

    /// <summary>
    /// Снимает с префаба чужие камеры и источники света.
    ///
    /// Оплачено проходом 0: внутри одного из FBX сидела камера, и снимок ушел
    /// через нее. Кадр вышел «крупный камень во весь экран», ошибок в логе ноль,
    /// PNG на месте. Присутствие объекта в сцене не значит, что снимают его.
    /// </summary>
    public static int SnyatChuzhoe(GameObject go)
    {
        int n = 0;
        foreach (var c in go.GetComponentsInChildren<Camera>(true))
        { Object.DestroyImmediate(c.gameObject); n++; }
        foreach (var l in go.GetComponentsInChildren<UnityEngine.Light>(true))
        { Object.DestroyImmediate(l.gameObject); n++; }
        if (n > 0) Debug.LogWarning($"[v2] снято чужих камер и света с {go.name}: {n}");
        return n;
    }

    /// <summary>Наша камера ищется по имени, а не по типу.</summary>
    public const string CAM_NAME = "CameraV2";

    public static Camera OurCamera()
    {
        var go = GameObject.Find(CAM_NAME);
        return go != null ? go.GetComponent<Camera>() : null;
    }

    /// <summary>Габарит объекта в долях кадра. Не зависит от pixelWidth камеры,
    /// который в batchmode не равен разрешению снимка.</summary>
    public static Rect ViewportRect(Camera cam, Bounds b)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                (i & 2) == 0 ? b.min.y : b.max.y,
                                (i & 4) == 0 ? b.min.z : b.max.z);
            var p = cam.WorldToViewportPoint(c);
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
        }
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Импортер FBX материал не приносит, отдает серую заглушку. Материалы
    /// создаются кодом, иначе вся библиотека серая.
    /// </summary>
    public static void ApplyTexture(GameObject go, string model,
                                    Color? kraska = null, string kraskaImya = null)
    {
        var tex = FindTexture(model);
        if (tex == null) return;

        // Перекраска. Домножение текстуры на насыщенный цвет ПОВЫШАЕТ
        // насыщенность, если исходная текстура блеклая: ангар (85,113,98) имеет
        // насыщенность 0.25, после умножения на синий выходит около 0.7.
        // Обратное неверно: подкрасить и тем СНИЗИТЬ насыщенность нельзя, можно
        // только затемнить. Поэтому базовый грунт этим способом не чинится.
        var c = kraska ?? Color.white;
        string imya = kraska.HasValue
            ? $"kras-{model}-{kraskaImya ?? "x"}"
            : $"model-{model}";
        var mat = NewLit(c, tex, imya: imya);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }
    }

    // ------------------------------------------------------------ камера и свет

    public static Camera MakeCamera(Vector3 target)
    {
        var go = new GameObject(CAM_NAME);
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = CAM_SIZE;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 1000f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.86f, 0.72f, 0.62f);
        AimCamera(cam, target, CAM_YAW, CAM_PITCH);
        go.tag = "MainCamera";
        return cam;
    }

    /// <summary>
    /// Наводит камеру. Расстояние при ортографии на кадрирование НЕ влияет, но
    /// влияет на тени: дальность теней в конвейере считается от камеры и равна
    /// 120 м. При dist = 150 вся сцена оказывалась за этим пределом, и теней не
    /// было ни одной - при том, что свет стоял, тени были включены, разрешение
    /// карты 2048 и ошибок в логе ноль.
    /// </summary>
    public static void AimCamera(Camera cam, Vector3 target,
                                 float yawDeg, float pitchDeg, float dist = 70f)
    {
        float az = yawDeg * Mathf.Deg2Rad, el = pitchDeg * Mathf.Deg2Rad;
        var dir = new Vector3(Mathf.Cos(el) * Mathf.Cos(az),
                              Mathf.Sin(el),
                              Mathf.Cos(el) * Mathf.Sin(az));
        cam.transform.position = target + dir * dist;
        cam.transform.rotation = Quaternion.LookRotation(target - cam.transform.position);
    }

    // ------------------------------------------------------------ снимок

    /// <summary>
    /// Снимок кадра. Буфер ОБЯЗАН быть sRGB: проект в Linear, и конструктор без
    /// явного RenderTextureReadWrite пишет линейные числа в PNG как будто они
    /// уже sRGB. Кадр темнеет впятеро и уходит в серо-синее, при том что в
    /// самой сцене все верно. Это стоило прошлому заходу полдня поисков
    /// виноватого в свете и материалах.
    /// </summary>
    public static void Shoot(Camera cam, string file)
    {
        // ПЕРВЫЙ РЕНДЕР ПОСЛЕ ОТКРЫТИЯ СЦЕНЫ ВРЕТ. Обязателен прогрев.
        //
        // В batchmode первый cam.Render() рисует неверным состоянием материалов:
        // все объекты выходят цветом одного из материалов сцены, кадр
        // одноцветный. Второй и последующие рендеры верны. Ошибок в логе ноль,
        // PNG на месте, в сцене все правильно - шейдер тот, _BaseMap привязан,
        // UV есть, материалы лежат ассетами. Диагностика по инспектору
        // показывает полный порядок, врет именно снимок.
        //
        // Замер на реальной сцене, три рендера подряд в одном прогоне:
        //   рендер 1: ангар (230,107,65), уникальных цветов 2754   - неверно
        //   рендер 2: ангар  (59,109,99), уникальных цветов 41258  - верно
        //   рендер 3: то же, что рендер 2
        //
        // Ложный след по дороге: сначала это выглядело как вина SRP Batcher.
        // На контрольном щите снимок «с батчером» вышел сломанным, «без
        // батчера» верным - но верным он был не потому, что батчер выключен, а
        // потому что был вторым по счету. Отдельный опыт это развел.

        var rt = new RenderTexture(SHOT_W, SHOT_H, 24,
                                   RenderTextureFormat.ARGB32,
                                   RenderTextureReadWrite.sRGB);
        rt.antiAliasing = 4;
        // ПОСТОБРАБОТКА НЕ ДОЕЗЖАЕТ ЧЕРЕЗ cam.Render().
        //
        // cam.Render() это путь старого конвейера. В URP он рисует сцену, но
        // проходит мимо Volume: тонмаппинг, насыщенность, контраст, bloom и
        // виньетка просто не применяются. Ошибки при этом нет никакой -
        // renderPostProcessing на камере True, Volume в сцене есть, профиль
        // есть, лог чистый. Опыт: один и тот же кадр с включенным и выключенным
        // Volume дал разницу РОВНО НОЛЬ пикселей.
        //
        // Верный путь - запрос рендера у конвейера. Он же сам делает прогрев,
        // но холостой проход оставлен: первый рендер после открытия сцены врет
        // независимо от способа.
        cam.targetTexture = rt;
        cam.Render();   // прогрев, результат негоден

        var zapros = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
        if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, zapros))
        {
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, zapros);
        }
        else
        {
            Debug.LogWarning("[v2] конвейер не принимает запрос рендера, "
                           + "снимок идет без постобработки");
            cam.Render();
        }
        RenderTexture.active = rt;
        var tx = new Texture2D(SHOT_W, SHOT_H, TextureFormat.RGB24, false);
        tx.ReadPixels(new Rect(0, 0, SHOT_W, SHOT_H), 0, 0);
        tx.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllBytes(file, tx.EncodeToPNG());
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tx);
        Debug.Log($"[v2] снят кадр: {file}");
    }

    // ------------------------------------------------------------ разное

    public static void Log(string s) => Debug.Log("[v2] " + s);
}
