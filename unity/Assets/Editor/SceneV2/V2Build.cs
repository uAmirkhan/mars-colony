using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Строитель сцены v2. Только строит и сохраняет, снимков не делает.
///
/// Раскладка ведется НЕ в мировых координатах, а в экранных: u это метры вправо
/// по кадру, v это метры вверх по кадру вдоль земли. Так композиция задается
/// прямо там, где ее видит зритель, а не пересчитывается в голове через разворот
/// камеры на 45 градусов.
///
/// Это и есть «от кадра к объектам» - порядок, который в передаче опыта записан
/// как непробованный.
/// </summary>
public static class V2Build
{
    // Границы кадра в экранных метрах при орто 22 и наклоне 38.
    // Половина вертикали кадра = 22 м, вдоль земли это 22 / sin(38) = 35.7 м.
    // Половина горизонтали = 22 * 1.6 = 35.2 м.
    private const float U_KRAY = 45f;
    private const float V_KRAY = 46f;

    // Поле, внутри которого обязаны целиком помещаться ГЕРОИ - шаттлы, купола,
    // заводы. Мелочи и россыпи резаться краем можно и нужно: мир продолжается
    // за кадром. Резать нельзя то, на что смотрят.
    private const float U_GEROY = 32f;
    private const float V_GEROY = 34f;

    private static System.Random _rnd;
    private static Transform _stroyki, _rekvizit, _dekor, _lyudi;

    public static void Build()
    {
        _rnd = new System.Random(20260824);
        _zanyato.Clear();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                                                NewSceneMode.Single);

        _stroyki = new GameObject("Stroyki").transform;
        _rekvizit = new GameObject("Rekvizit").transform;
        _dekor = new GameObject("Dekor").transform;
        _lyudi = new GameObject("Lyudi").transform;

        Zemlya();
        var az = Arg("-azimut", "");
        if (!string.IsNullOrEmpty(az) && float.TryParse(az,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float a))
            V2Svet.Azimut = a;
        var vs = Arg("-vysota", "");
        if (!string.IsNullOrEmpty(vs) && float.TryParse(vs,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float vv))
            V2Svet.Vysota = vv;
        V2Svet.Postavit(V2Lib.CAM_YAW);

        Kosmoport();
        ZhiloyKvartal();
        Teplitsy();
        PromZona();
        Karier();
        LedyanoePole();
        Drony();
        Dorogi();
        Rossyp();
        Lyudi();

        Perepis();

        // СВЕДЕНИЕ МЕШЕЙ ВЫКЛЮЧЕНО ПО УМОЛЧАНИЮ.
        //
        // Сведение - операция выпуска, а не сборки. В соседней сцене оно было
        // сделано рано, и цена оказалась такой: из шестнадцати труб живых
        // осталось шесть, остальные превратились в пустые оболочки, а геометрия
        // уехала в общий кусок. После этого нельзя убрать ни одну брошенную
        // поперек карьера трубу - она не объект, а треугольники в чужом меше.
        // Владелец назвал это прямо: поправить локально нельзя ничего, только
        // перестроить все.
        //
        // Включается флагом `-svesti` перед сборкой билда, когда раскладка уже
        // принята и правки кончились.
        if (Arg("-svesti", "") == "da") Spakovat();
        else V2Lib.Log("меши НЕ сведены: правки остаются возможными поштучно");

        var cam = V2Lib.MakeCamera(Vector3.zero);
        V2Svet.VklyuchitNaKamere(cam);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, V2Lib.SCENE_PATH);
        V2Lib.Log($"сцена сохранена: {V2Lib.SCENE_PATH}");
    }

    /// <summary>
    /// Перепись бюджета ДО упаковки.
    ///
    /// После StaticBatchingUtility.Combine все рендереры группы ссылаются на
    /// один склеенный меш, и наивный подсчет «сумма треугольников по всем
    /// MeshFilter» умножает его на число рендереров: замер выдал 7.4 миллиона
    /// вместо 632 тысяч. Считать надо до склейки.
    /// </summary>
    private static void Perepis()
    {
        long tris = 0;
        int rend = 0;
        var po_vetkam = new Dictionary<string, long>();
        foreach (var root in new[] { _stroyki, _rekvizit, _dekor, _lyudi })
        {
            if (root == null) continue;
            long t = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled || mf.sharedMesh == null) continue;
                t += mf.sharedMesh.triangles.Length / 3;
                rend++;
            }
            po_vetkam[root.name] = t;
            tris += t;
        }
        // земля считается отдельно, она не в этих ветках
        var zem = GameObject.Find("Zemlya");
        long tz = 0;
        if (zem != null)
            foreach (var mf in zem.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null) { tz += mf.sharedMesh.triangles.Length / 3; rend++; }
        tris += tz;
        po_vetkam["Zemlya"] = tz;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== БЮДЖЕТ, ЗАМЕР ДО УПАКОВКИ ===");
        foreach (var kv in po_vetkam)
            sb.AppendLine($"{kv.Key,-12} {kv.Value,10} треугольников");
        sb.AppendLine($"{"ВСЕГО",-12} {tris,10} треугольников  (потолок 500000)");
        sb.AppendLine($"{"рендереров",-12} {rend,10}");
        System.IO.File.WriteAllText(
            System.IO.Path.GetFullPath("../mars-colony/loop/scene-v2/byudzhet.txt"),
            sb.ToString());
        Debug.Log("[v2] " + sb);
    }

    /// <summary>
    /// Статическая упаковка. Сцена неподвижна целиком, поэтому меши одного
    /// материала склеиваются в один вызов отрисовки. Без этого 465 рендереров
    /// это 465 вызовов при объявленном потолке 200.
    /// </summary>
    private static void Spakovat()
    {
        foreach (var root in new[] { _rekvizit, _dekor, _stroyki, _lyudi })
        {
            if (root == null) continue;
            var deti = root.GetComponentsInChildren<MeshRenderer>()
                           .Select(r => r.gameObject).ToArray();
            foreach (var g in deti) GameObjectUtility.SetStaticEditorFlags(
                g, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
            if (deti.Length > 0) StaticBatchingUtility.Combine(deti, root.gameObject);
            V2Lib.Log($"упаковано {root.name}: {deti.Length} рендереров");
        }
    }

    // ------------------------------------------------------------ координаты кадра

    /// <summary>Экранные метры в мировую точку на земле.</summary>
    private static Vector3 P(float u, float v)
    {
        float az = V2Lib.CAM_YAW * Mathf.Deg2Rad;
        var fwd = new Vector3(-Mathf.Cos(az), 0f, -Mathf.Sin(az));
        var right = Vector3.Cross(Vector3.up, fwd).normalized;
        return right * u + fwd * v;
    }

    private static float Sluch(float a, float b) => a + (float)_rnd.NextDouble() * (b - a);
    private static int SluchInt(int a, int b) => _rnd.Next(a, b);

    /// <summary>Ставит модель по экранным координатам. Имя без суффикса
    /// облегчения: суффикс добавляется тут, чтобы список раскладки читался.</summary>
    /// <summary>
    /// Ставит модель в ИСХОДНОМ виде, без облегчения.
    ///
    /// Решение владельца от 2026-08-24, и оно разворачивает мое прежнее.
    /// Я завел два яруса децимации (30% и 4%) ради объявленного бюджета в
    /// 500 тысяч треугольников. Владелец посмотрел кадр и сказал прямо:
    /// упрощение делает модели уродливыми, а сейчас задача не оптимизировать
    /// игру, а собрать красивую сцену.
    ///
    /// Цена решения названа честно: одна только партия v3 в исходном виде это
    /// 451 тысяча треугольников на по одному экземпляру каждой модели, а
    /// корневой слой идет по 60 тысяч за штуку. Бюджет в 500 тысяч при этом
    /// заведомо пробивается, и это осознанный размен, а не промах замера.
    ///
    /// Облегченные партии оставлены на диске (`v3lite`, `v3micro`): вернуть
    /// оптимизацию, когда дойдем до нее, будет дешево.
    /// </summary>
    private static GameObject Stavit(string model, float u, float v, float vysota,
                                     float yaw, Transform parent = null,
                                     Color? kraska = null, string kraskaImya = null)
    {
        var p = P(u, v);
        return V2Lib.Place(model, new Vector2(p.x, p.z), vysota, yaw,
                           parent ?? _stroyki, kraska, kraskaImya);
    }

    private static string Arg(string key, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
        return def;
    }

    // Занятые пятна застройки в мировых координатах: центр XZ и полуразмеры.
    private static readonly List<(Vector2 c, Vector2 pol)> _zanyato = new();

    /// <summary>
    /// Ставит ПОСТРОЙКУ, не пересекая уже поставленные.
    ///
    /// Проверка пересечений нашла девять наложений в прежней раскладке, худшее -
    /// труба на 95% внутри ангара. Причина в том, что я задавал координаты на
    /// глаз, а габариты у моделей крупные: ангар при высоте 7 м занимает 26 м в
    /// плане. На глаз это не считается.
    ///
    /// Доктрина расстановки требует зазор между границами построек 0-3 клетки,
    /// то есть касание допустимо, а проникновение нет. Если объект не влезает,
    /// он отодвигается по спирали; если не влезает нигде рядом - не ставится, и
    /// об этом пишется в лог, а не замалчивается.
    /// </summary>
    // Зона, внутри которой разрешено расталкивание. Ставится перед вызовом
    // группы построек: без нее объект, которому не хватило места, улетал в
    // чужую зону через полкадра, и раскладка по доктрине рассыпалась.
    private static (float u, float v, float pu, float pv) _zona = (0f, 0f, 99f, 99f);

    private static void Zona(float u, float v, float pu, float pv) =>
        _zona = (u, v, pu, pv);

    private static GameObject Postroyka(string model, float u, float v, float vysota,
                                        float yaw, Color? kraska = null,
                                        string kraskaImya = null, float zazor = 0.7f)
    {
        var go = Stavit(model, u, v, vysota, yaw, _stroyki, kraska, kraskaImya);
        if (go == null || !V2Lib.WorldBounds(go, out var b)) return go;

        var pol = new Vector2(b.size.x * 0.5f + zazor, b.size.z * 0.5f + zazor);
        var c = new Vector2(b.center.x, b.center.z);

        if (Svobodno(c, pol)) { _zanyato.Add((c, pol)); return go; }

        // Спираль поиска свободного места, НО не выходя из своей зоны.
        for (int k = 1; k <= 40; k++)
        {
            float r = 2.0f + k * 1.1f;
            float a = k * 2.39996f;                 // золотой угол, точки не липнут
            var sm = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            var nc = c + sm;
            if (Mathf.Abs(u + sm.x - _zona.u) > _zona.pu
             || Mathf.Abs(v + sm.y - _zona.v) > _zona.pv) continue;
            if (!Svobodno(nc, pol)) continue;
            go.transform.position += new Vector3(sm.x, 0f, sm.y);
            _zanyato.Add((nc, pol));
            V2Lib.Log($"{model}: сдвинут на {sm.magnitude:F1} м, место было занято");
            return go;
        }

        Debug.LogWarning($"[v2] {model} НЕ ПОСТАВЛЕН: свободного места рядом нет");
        UnityEngine.Object.DestroyImmediate(go);
        return null;
    }

    private static void Zanyat(Vector2 c, Vector2 pol) => _zanyato.Add((c, pol));

    /// <summary>
    /// Ряд построек с шагом ПО ФАКТИЧЕСКОМУ СЛЕДУ, а не по клеткам.
    ///
    /// Написано после того, как пять построек не встали в раскладку, а
    /// остальные разъехались на 3-14 метров. Я назначал координаты по сетке
    /// 2.5 м, не зная, сколько модель занимает в плане. Замер показал:
    /// ангар при высоте 8.5 м занимает 21 на 24 метра, склад 18 на 19,
    /// завод 18 на 19. На глаз это не считается никак.
    ///
    /// Здесь шаг вычисляется: поставили, замерили половину следа вдоль
    /// направления ряда, следующего поставили на «своя половина + зазор + его
    /// половина». Ряд получается ровным по построению, и расталкивающая
    /// спираль в него не вмешивается - ей нечего расталкивать.
    ///
    /// Ставим сначала с запасом, потом подтягиваем: след следующей модели до
    /// ее постановки неизвестен, а гадать про него значит вернуться ровно к
    /// той ошибке, из-за которой этот метод и появился.
    /// </summary>
    private static void Ryad((string model, float vysota)[] spisok, Vector2 start,
                             Vector2 napravlenie, float yaw, float zazor = 2f)
    {
        var dir = napravlenie.normalized;
        var tochka = start;
        float prevPol = 0f;

        for (int i = 0; i < spisok.Length; i++)
        {
            // Запас заведомо больше любого следа в наборе: самый крупный
            // замерен 24 метра, половина 12.
            var proba = i == 0 ? tochka : tochka + dir * (prevPol + zazor + 14f);
            var go = Stavit(spisok[i].model, proba.x, proba.y, spisok[i].vysota, yaw, _stroyki);
            if (go == null) continue;
            if (!V2Lib.WorldBounds(go, out var b)) continue;

            // Половина следа ВДОЛЬ ряда: у вытянутой модели она зависит от того,
            // как ряд повернут, поэтому берется проекция габарита на направление.
            var mir = P(dir.x, dir.y).normalized;
            float pol = Mathf.Abs(b.size.x * mir.x) * 0.5f + Mathf.Abs(b.size.z * mir.z) * 0.5f;

            if (i > 0)
            {
                var nuzhno = tochka + dir * (prevPol + zazor + pol);
                var sdvig = P(nuzhno.x, nuzhno.y) - P(proba.x, proba.y);
                go.transform.position += sdvig;
                tochka = nuzhno;
            }
            V2Lib.WorldBounds(go, out b);
            Zanyat(new Vector2(b.center.x, b.center.z),
                   new Vector2(b.size.x * 0.5f + 0.5f, b.size.z * 0.5f + 0.5f));
            prevPol = pol;
        }
    }

    private static bool Svobodno(Vector2 c, Vector2 pol)
    {
        foreach (var z in _zanyato)
            if (Mathf.Abs(c.x - z.c.x) < pol.x + z.pol.x
             && Mathf.Abs(c.y - z.c.y) < pol.y + z.pol.y) return false;
        return true;
    }

    // ПЕРЕКРАСКА МОДЕЛЕЙ ОТМЕНЕНА.
    //
    // Владелец: «ты опять продолжаешь изменять цвета основных ассетов». Модели
    // идут в своем родном цвете - у них есть автор и свой материал, и подмена
    // его фильтром это не работа со сценой, а порча ассета.
    //
    // Цвет в кадр по-прежнему приносят предметы, но СВОИ: реквизит из
    // примитивов, который я и создаю, красить можно и нужно. Константы ниже
    // остались для него.
    //
    // Краски построек. Марс монохромный, поэтому цвет на нем может быть только
    // привезенный. Насыщенные тона взяты намеренно: серым и темным долю
    // оранжевого сбить можно, а цветность кадра нет - она растет только от
    // насыщенного не-оранжевого. Замер p5: доля оранжевого упала 0.65 -> 0.56
    // от светлых пятен земли, а цветность при этом упала 81.8 -> 75.8.
    private static readonly Color K_KREM = new Color(1.35f, 1.28f, 1.10f);
    private static readonly Color K_BELYY = new Color(1.55f, 1.55f, 1.50f);
    private static readonly Color K_SINIY = new Color(0.55f, 1.05f, 1.75f);
    private static readonly Color K_GOLUBOY = new Color(0.85f, 1.45f, 1.75f);
    private static readonly Color K_KRASNYY = new Color(1.85f, 0.72f, 0.55f);
    private static readonly Color K_ZHELTYY = new Color(1.70f, 1.30f, 0.42f);
    private static readonly Color K_ZELENYY = new Color(0.72f, 1.45f, 0.68f);
    private static readonly Color K_ZELENYY2 = new Color(0.95f, 1.35f, 0.60f);
    // скафандр: теплый оранжевый, чтобы человек читался человеком, а не бликом
    private static readonly Color K_ORANZH = new Color(1.90f, 0.95f, 0.45f);

    // ------------------------------------------------------------ земля

    /// <summary>
    /// Земля. Покрытие кладется ПОД кластеры, а не пятнами по всему полю.
    ///
    /// Доктрина: постройки прямо на нетронутой земле не бывает, у освоенного
    /// места есть граница - полоса технопокрытия. Поэтому у каждого кластера
    /// свой двор, а между дворами открытый грунт. Прежняя раскладка сыпала
    /// восемнадцать пятен по полю 82 на 80, и границы освоенного не читалось
    /// нигде: покрытие было везде и потому не значило ничего.
    ///
    /// Порядок слоев важен и оплачен: дорога обязана быть самым верхним
    /// наземным слоем. Она лежала ниже непрозрачного ледяного пятна и физически
    /// закрашивалась сверху. Высоту раздает `SleduyushchayaVysota`, поэтому
    /// порядок вызовов здесь и есть порядок слоев.
    /// </summary>
    private static void Zemlya()
    {
        V2Zemlya.Nachat();
        V2Zemlya.Baza("g-regolit", 26f);

        // Дальний фон: разнородность грунта ЗА пределами застройки. Внутри
        // кластеров ее делать нельзя - там читается покрытие, а не грунт.
        V2Zemlya.Pyatno("pesok-1", "g-pesok", P(44f, -30f), 13f, 0.80f, 10f, 10f, 21);
        V2Zemlya.Pyatno("pyl-1", "g-pyl", P(-46f, -34f), 12f, 0.85f, 200f, 12f, 22);
        V2Zemlya.Pyatno("pyl-2", "g-pyl", P(40f, 34f), 11f, 0.80f, 40f, 12f, 25);
        V2Zemlya.Pyatno("poroda-1", "g-poroda", P(-44f, 34f), 12f, 0.75f, 60f, 8f, 12);
        V2Zemlya.Pyatno("inei-1", "g-inei", P(-8f, 34f), 11f, 0.70f, 120f, 9f, 23);
        V2Zemlya.Pyatno("pesok-2", "g-pesok", P(6f, -36f), 12f, 0.80f, 20f, 10f, 29);

        // Ледяное поле: пятно грунта под биомом, крупнее самого биома, чтобы у
        // зоны была видимая граница, а не обрез по последней глыбе.
        V2Zemlya.Pyatno("led-pole", "g-led", P(-24f, 13f), 15f, 0.80f, 20f, 9f, 11);

        // Карьер: порода на месте выработки и отвал рядом с ней.
        V2Zemlya.Pyatno("karier", "g-poroda", P(25f, 15f), 11f, 0.85f, 30f, 8f, 30);
        V2Zemlya.Pyatno("otval", "g-poroda", P(17f, 19f), 6f, 0.80f, 300f, 7f, 24);

        // ДВОРЫ КЛАСТЕРОВ. Каждый чуть больше пятна застройки - так у квартала
        // появляется поле, а не обтяжка по стенам.
        //
        // Перрон космопорта НАМЕРЕННО темнее прочих: белый шаттл на светлом
        // покрытии рядом с белыми фигурами пропадал целиком, и единственный
        // однозначно марсианский объект кадра был самым нечитаемым на телефоне.
        var perron = V2Zemlya.Uchastok("perron", "z-plita", P(-27f, -20f),
                                       26f, 20f, 45f, 6f,
                                       kraska: new Color(0.46f, 0.47f, 0.52f));

        V2Zemlya.Uchastok("dvor-zhiloy", "z-plita", P(0f, -2f), 28f, 26f, 45f, 6f);
        V2Zemlya.Uchastok("dvor-teplits", "g-moshchenie", P(0f, 15f), 24f, 20f, 45f, 5f);
        V2Zemlya.Uchastok("dvor-prom", "z-nastil", P(25f, -9f), 24f, 22f, 45f, 4f);
        V2Zemlya.Uchastok("dvor-karier", "z-nastil", P(27f, 13f), 16f, 14f, 45f, 4f);
        V2Zemlya.Uchastok("dvor-led", "g-moshchenie", P(-15f, 12f), 12f, 14f, 45f, 5f);

        // Разметка круга на перроне: она объясняет, что это посадочное место.
        V2Zemlya.Nakleyka("krug-1", "d-ploshchadka", P(-32f, -23f), 9f, 45f);
        V2Zemlya.Nakleyka("krug-2", "d-ploshchadka", P(-22f, -23f), 9f, 45f);

        // Пятна проливов ставятся ТОЛЬКО у источника: под цистерной, у буровой,
        // у погрузки. Пролив без того, кто пролил, это текстурный мусор, и
        // ровно три таких пятна на голом покрытии уже были найдены глазами.
        (float u, float v)[] gde =
        {
            (20f, -4f),    // у батареи цистерн промзоны
            (30f, 11f),    // у цистерн карьера
            (-14f, 11f),   // у цистерн ледника
            (-34f, -14f),  // у топлива на перроне
            (-6f, 19f),    // у цистерны теплиц
        };
        for (int i = 0; i < gde.Length; i++)
        {
            var p = P(gde[i].u + Sluch(-1.5f, 1.5f), gde[i].v + Sluch(-1.5f, 1.5f));
            V2Zemlya.Nakleyka($"pyatno{i}", $"d-pyatno-{i % 4 + 1}", p,
                              Sluch(3f, 5.5f), Sluch(0f, 360f));
        }
    }

    /// <summary>
    /// Солнечная ферма: массив наклонных панелей.
    ///
    /// Роль солнечной энергетики в сцене не читалась никем. Модель
    /// `solnechnaya-batareya` ее не продает - со всех сторон это плита на
    /// кронштейне без фотоэлементов. Процедурная панель была написана, но ни
    /// разу не вызвана.
    ///
    /// Регулярный массив здесь УМЕСТЕН и не читается клоном: солнечные фермы в
    /// жизни ровные, и глаз опознает их именно по строю. Клоном читается
    /// повтор там, где повтора быть не должно.
    /// </summary>
    private static void SolnechnayaFerma(float u, float v, int ryadov, int vryadu,
                                         float shag, float yaw)
    {
        for (int r = 0; r < ryadov; r++)
            for (int i = 0; i < vryadu; i++)
            {
                var t = P(u + i * shag, v + r * shag * 1.15f);
                V2Rekvizit.Panel(_dekor, new Vector3(t.x, 0f, t.z),
                                 shag * 0.82f, yaw, 32f);
            }
    }
    /// <summary>
    /// Дроны. Ставятся низко над землей у зданий-владельцев: по доктрине
    /// признак жизни принадлежит конкретной постройке, а не пустоши.
    ///
    /// Владелец раньше называл эти три модели багованными, но состав набора
    /// берется из `VseAssety`, а они там есть. Поставлены; решение снять их -
    /// за владельцем.
    /// </summary>
    private static void Drony()
    {
        (string imya, float u, float v, float h, float yaw)[] spisok =
        {
            ("dron-t1", -24f, -16f, 1.5f, 0f),    // разгрузка на перроне
            ("dron-t2", -5f, -6f, 1.4f, 90f),     // курьер во дворе квартала
            ("dron-t3", 20f, -7f, 1.4f, 180f),    // погрузка у склада
        };
        foreach (var d in spisok)
        {
            var go = Stavit(d.imya, d.u, d.v, d.h, d.yaw, _dekor);
            if (go == null) continue;
            go.transform.position += new Vector3(0f, 2.2f, 0f);
        }
    }

    /// <summary>
    /// Дороги. Одна магистраль и три ветки, каждая упирается в двор кластера.
    ///
    /// Дорога, которая никуда не ведет, читается обрубком - это было названо
    /// глазами и в чужой сцене, и в нашей. Поэтому концы отрезков стоят внутри
    /// дворов, а не в чистом поле.
    ///
    /// Ширина магистрали 3.0 м, веток 2.5 м - клетка доктрины. Дорога в две
    /// клетки шириной перестает читаться дорогой и читается площадью.
    /// </summary>
    private static void Dorogi()
    {
        // Магистраль: перрон -> квартал -> теплицы.
        V2Zemlya.Doroga(P(-27f, -18f), P(-8f, -6f), 3.0f);
        V2Zemlya.Doroga(P(-8f, -6f), P(-1f, 0f), 3.0f);
        V2Zemlya.Doroga(P(-1f, 0f), P(0f, 12f), 3.0f);

        // Ветка в промзону и дальше на карьер.
        V2Zemlya.Doroga(P(6f, -4f), P(19f, -8f), 2.5f);
        V2Zemlya.Doroga(P(19f, -8f), P(27f, 8f), 2.5f);

        // Ветка на ледник.
        V2Zemlya.Doroga(P(-7f, 3f), P(-16f, 11f), 2.5f);
    }
    // ------------------------------------------------------------ свет

    // ------------------------------------------------------------ зоны
    //
    // Раскладка по доктрине расстановки (design/doktrina-rasstanovki.md,
    // раздел 3) и своду назначений ассетов. Карта соседства оттуда:
    //
    //   космопорт   рядом: лед, тропа      далеко: жилье, карьер
    //   жилой       рядом: вода, теплицы   далеко: карьер, промзона
    //   промзона    рядом: карьер, порт    далеко: жилье
    //   лед         рядом: все             далеко: карьер
    //   карьер      рядом: промзона        далеко: жилье, лед
    //
    // Плюс правило доктрины: точка входа игрока это самое дружелюбное здание
    // кадра, ближе к камере. Поэтому жилой квартал внизу, добыча наверху.

    /// <summary>
    /// КОСМОПОРТ. Юго-запад, дальний от жилья угол освоенного.
    ///
    /// Два причала рядами: площадка и ее шаттл в одном ряду, второй причал
    /// параллельно. Один причал читается стоянкой, а не портом - порт узнается
    /// по повторению.
    ///
    /// Шаттл стоит РЯДОМ с площадкой, а не на ней. Замер: площадка при высоте
    /// 3.5 м занимает 7.2 м в плане, шаттл при 5 м - 10.1 м. Шаттл крупнее
    /// своей площадки, посадить его сверху нельзя физически. Связка проверяется
    /// расстоянием, и пара в двух метрах ее держит.
    /// </summary>
    private static void Kosmoport()
    {
        Zona(-26f, -26f, 16f, 12f);

        Ryad(new[] { ("ploshchadka-shattla", 3.5f), ("shattl-zakrytyy", 5.0f) },
             new Vector2(-36f, -32f), new Vector2(1f, 0f), 0f);
        Ryad(new[] { ("ploshchadka-shattla", 3.5f), ("shattl-otkrytyy", 5.0f) },
             new Vector2(-36f, -21f), new Vector2(1f, 0f), 0f);

        // Накопитель груза на линии выгрузки, между причалами и дорогой в
        // поселение. Склад, отнесенный в сторону, не объясняет, что в нем.
        Postroyka("sklad-bunkery", -8f, -30f, 6.0f, 90f);

        // Груз всегда при постройке-хозяине, иначе это не груз, а мусор.
        Stavit("tsisterna", -38f, -26f, 3.2f, 0f, _dekor);
        Stavit("tsisterna", -35f, -26f, 3.2f, 0f, _dekor);
        Stavit("ballony-na-poddone", -24f, -33f, 1.9f, 0f, _dekor);
        Stavit("ballony-na-poddone", -21f, -33f, 1.9f, 0f, _dekor);
        Stavit("truba-na-kozlakh", -30f, -26f, 1.6f, 90f, _dekor);
        Stavit("truba-na-kozlakh", -26f, -26f, 1.6f, 90f, _dekor);

        Stavit("figura-ukazyvaet", -32f, -26f, V2Lib.FIGURA_ROST, 90f, _lyudi);
        Stavit("figura-neset", -20f, -22f, V2Lib.FIGURA_ROST, 180f, _lyudi);

        var r = _rekvizit;
        V2Rekvizit.Machta(r, P(-40f, -36f), 7f, V2Rekvizit.KREM, "krem");
        V2Rekvizit.Machta(r, P(-12f, -36f), 7f, V2Rekvizit.KREM, "krem");
    }

    /// <summary>
    /// ЖИЛОЙ КВАРТАЛ. Центр кадра и точка входа взгляда.
    ///
    /// Два ряда лицом друг к другу, между ними улица. Улица и есть то, чего не
    /// было в прежней раскладке: постройки стояли поодиночке, и между ними был
    /// не двор, а промежуток.
    ///
    /// Башня - единственная вертикаль выше десяти метров во всей сцене. Кадру
    /// нужна одна доминанта, а не три соперничающие.
    /// </summary>
    private static void ZhiloyKvartal()
    {
        Zona(0f, -4f, 16f, 15f);

        // Западный ряд: три жилых купола вдоль улицы.
        Ryad(new[] { ("zhiloy-kupol", 5.0f), ("zhiloy-kupol", 5.0f), ("zhiloy-kupol", 5.0f) },
             new Vector2(-11f, -14f), new Vector2(0f, 1f), 90f);

        // Восточный ряд: три тоннельных модуля торцами на ту же улицу.
        Ryad(new[] { ("modul-tonnelnyy", 4.2f), ("modul-tonnelnyy", 4.2f),
                     ("modul-tonnelnyy", 4.2f) },
             new Vector2(9f, -14f), new Vector2(0f, 1f), 90f);

        Postroyka("zhiloy-bashnya", -1f, 9f, 13.0f, 0f);
        Postroyka("kupol-tunnel", -1f, -6f, 4.5f, 0f);
        Postroyka("kupol-grib", -1f, -17f, 5.5f, 0f);

        // Признаки жизни у жилья: зелень в кадках и люди на улице.
        Stavit("kadka-s-zelenyu", -3f, 0f, 1.2f, 0f, _dekor);
        Stavit("kadka-s-zelenyu", -1f, 0f, 1.2f, 0f, _dekor);
        Stavit("kadka-s-zelenyu", 1f, -10f, 1.2f, 90f, _dekor);
        Stavit("figura-osmatrivaetsya", -2f, -4f, V2Lib.FIGURA_ROST, 0f, _lyudi);
        Stavit("figura-ukazyvaet-variant", 2f, 2f, V2Lib.FIGURA_ROST, 270f, _lyudi);
    }

    /// <summary>
    /// ТЕПЛИЦЫ И ПИЩЕВОЙ ЗАВОД. Примыкают к жилью с севера.
    ///
    /// Цепочку видно в кадре: грядки, купола, завод рядом, дорога от них в
    /// квартал. Три купола РАЗНОГО размера, а не три одинаковых - владелец
    /// просил несколько маленьких вместо одного большого.
    /// </summary>
    private static void Teplitsy()
    {
        Zona(-2f, 24f, 16f, 12f);

        Ryad(new[] { ("kupol-geodezicheskiy", 6.0f), ("kupol-geodezicheskiy", 5.2f),
                     ("kupol-geodezicheskiy", 4.5f) },
             new Vector2(-14f, 20f), new Vector2(1f, 0f), 0f);

        // Грибной купол: по смыслу это еда, поэтому он у теплиц, а не в
        // жилом ряду, куда я его сперва поставил и где он влезал в модули.
        Postroyka("kupol-grib", -16f, 32f, 5.5f, 0f);

        Postroyka("zavod-pishchevoy", 2f, 34f, 8.0f, 270f);

        // Грядки строем перед куполами: ряд читается возделанным, россыпь нет.
        for (int i = 0; i < 5; i++)
            Stavit("kadka-s-zelenyu", -12f + i * 2.2f, 14f, 1.2f, 0f, _dekor);

        Stavit("tsisterna", -8f, 28f, 3.2f, 90f, _dekor);
        Stavit("figura-neset", -8f, 17f, V2Lib.FIGURA_ROST, 0f, _lyudi);
    }

    /// <summary>
    /// ПРОМЗОНА. Восток, между причалом и карьером - там ее место по цепочке:
    /// сверху приходит руда, вниз уходит груз.
    ///
    /// Склад крупнее жилого купола в полтора раза по высоте и вдвое по следу.
    /// Владелец называл обратное соотношение дважды: «склад, ангар, бункер
    /// должны быть большего размера».
    /// </summary>
    private static void PromZona()
    {
        Zona(28f, -12f, 16f, 14f);

        // Два склада колонной. Третьей крупной постройки тут нет намеренно:
        // ряд из трех требует полусотни метров, а половина кадра сорок пять,
        // и ангар вылезал за край. Он ушел к леднику, где нужнее.
        Ryad(new[] { ("sklad-angar", 6.5f), ("sklad-angar", 6.5f) },
             new Vector2(26f, -22f), new Vector2(0f, 1f), 270f);

        // Батарея цистерн: три в ряд одного размера. Одиночная цистерна не
        // читается хранилищем, читается реквизитом для заполнения пустоты.
        Stavit("tsisterna", 36f, -18f, 3.2f, 0f, _dekor);
        Stavit("tsisterna", 39f, -18f, 3.2f, 0f, _dekor);
        Stavit("tsisterna", 36f, -15f, 3.2f, 0f, _dekor);

        // Магистраль из труб идет ВСТЫК вдоль дороги, а не лежит вразнобой.
        // Отдельная секция посреди двора читается брошенной палкой - ровно то,
        // что владелец назвал ненужными палками.
        for (int i = 0; i < 4; i++)
            Stavit("truba-na-kozlakh", 16f + i * 3.1f, -18f, 1.6f, 90f, _dekor);

        Stavit("ballony-na-poddone", 34f, -20f, 1.9f, 0f, _dekor);
        Stavit("ballony-na-poddone", 37f, -20f, 1.9f, 0f, _dekor);
        Stavit("ballony-na-poddone", 34f, -17f, 1.9f, 0f, _dekor);

        Stavit("figura-osmatrivaetsya", 30f, -16f, V2Lib.FIGURA_ROST, 180f, _lyudi);
        Stavit("figura-neset", 32f, -6f, V2Lib.FIGURA_ROST, 90f, _lyudi);

        SolnechnayaFerma(36f, -28f, 2, 4, 2.4f, 90f);
    }

    /// <summary>
    /// КАРЬЕР. Северо-восток, вплотную к промзоне и далеко от жилья.
    ///
    /// Вышка над выработкой, гусеничная машина на кромке забоя лицом в яму.
    /// Сквозной дороги через забой нет: по забою не ездят, в него съезжают.
    /// </summary>
    private static void Karier()
    {
        Zona(30f, 16f, 14f, 12f);

        Postroyka("burovaya-02", 34f, 22f, 9.0f, 180f);
        Postroyka("burovaya-04", 24f, 12f, 3.2f, 270f);

        Stavit("tsisterna", 38f, 10f, 3.2f, 90f, _dekor);
        Stavit("tsisterna", 38f, 7f, 3.2f, 90f, _dekor);
        Stavit("ballony-na-poddone", 26f, 24f, 1.9f, 0f, _dekor);
        Stavit("ballony-na-poddone", 29f, 24f, 1.9f, 0f, _dekor);

        Stavit("figura-ukazyvaet", 27f, 14f, V2Lib.FIGURA_ROST, 90f, _lyudi);

        // Отвал вскрыши: кучка колотого камня у кромки, а не по всей площади.
        for (int i = 0; i < 6; i++)
            Stavit(i % 2 == 0 ? "kamen-granenyy" : "kamni-para",
                   21f + Sluch(-2f, 2f), 22f + Sluch(-2f, 2f),
                   Sluch(1.0f, 2.2f), Sluch(0f, 360f), _dekor);
    }

    /// <summary>
    /// ЛЕДЯНОЕ ПОЛЕ. Запад-север, рядом с жильем (вода) и с причалом.
    ///
    /// Биом продается НАБОРОМ: девять разных моделей льда, а не одной
    /// растянутой. Растягивание одной модели уже дало плоский блоб - это
    /// оплаченная ошибка, записанная в разведке по земле.
    ///
    /// Внутри зоны есть якорь крупнее рядовых - гряда. Без якоря набор глыб
    /// читается россыпью, а не полем.
    /// </summary>
    private static void LedyanoePole()
    {
        Zona(-30f, 10f, 14f, 13f);

        // Ангар ледовой техники. Владелец просил его прямо: машины, которые
        // добывают лед, должны куда-то возвращаться. В промзоне он не помещался
        // по ширине кадра, а здесь у него есть работа.
        Postroyka("angar-s-panelyami", -28f, 2f, 5.5f, 90f);

        Postroyka("led-greben", -36f, 14f, 6.0f, 90f);

        Stavit("led-glyba-granenaya", -31f, 19f, 4.2f, 0f, _dekor);
        Stavit("led-glyba-kolotaya", -25f, 17f, 3.6f, 90f, _dekor);
        Stavit("led-glyba-kristallicheskaya", -36f, 5f, 3.8f, 180f, _dekor);
        Stavit("led-valun-v-shube", -22f, 12f, 2.8f, 0f, _dekor);
        Stavit("led-kristally", -32f, 1f, 2.4f, 270f, _dekor);
        Stavit("led-kristally-variant", -26f, 3f, 2.2f, 90f, _dekor);

        // Скважина и полынья - рабочие точки добычи, при них техника.
        Stavit("led-skvazhina", -29f, 10f, 2.6f, 0f, _dekor);
        Stavit("led-polynya", -23f, 7f, 1.4f, 0f, _dekor);

        Postroyka("burovaya-05", -23f, 13f, 8.0f, 270f);
        Postroyka("burovaya-03", -19f, 4f, 3.5f, 90f);

        // Вода из льда хранится тут же, у скважины.
        Stavit("tsisterna", -15f, 8f, 3.2f, 90f, _dekor);
        Stavit("tsisterna", -15f, 5f, 3.2f, 90f, _dekor);

        Stavit("figura-osmatrivaetsya", -24f, 10f, V2Lib.FIGURA_ROST, 270f, _lyudi);
    }

    /// <summary>
    /// Люди. Калибровочная фигура ростом 1.80 остается в кадре: по ней
    /// проверяется иерархия размеров пикселями, а не только числами.
    /// Остальные расставлены по зонам, каждая при своем деле.
    /// </summary>
    private static void Lyudi()
    {
        var kal = Stavit("figura-ukazyvaet", 14f, -6f, V2Lib.FIGURA_ROST, 270f, _lyudi);
        if (kal != null) kal.name = "KALIBR-figura-1m80";
    }

    /// <summary>
    /// Запретные зоны для природной россыпи: построенные поверхности.
    /// Прямоугольники в экранных метрах, как и вся раскладка.
    ///
    /// Валун, легший на посадочную площадку, читается ошибкой расстановки:
    /// площадку расчищают, в этом ее смысл.
    /// </summary>
    private static readonly (float u, float v, float pu, float pv)[] POSTROENO =
    {
        (-27f, -20f, 13f, 11f),   // космопорт
        (0f, -2f, 14f, 13f),      // жилой квартал
        (0f, 15f, 12f, 10f),      // теплицы
        (25f, -9f, 12f, 11f),     // промзона
        (26f, 14f, 12f, 11f),     // карьер
        (-24f, 13f, 13f, 12f),    // ледяное поле
    };
    private static bool NaPostroennom(float u, float v)
    {
        foreach (var z in POSTROENO)
            if (Mathf.Abs(u - z.u) < z.pu && Mathf.Abs(v - z.v) < z.pv) return true;
        return false;
    }

    /// <summary>
    /// Кластер реквизита вокруг точки. Плотно, вперемешку, с касанием.
    ///
    /// Оплачено кадром p3b: равномерная россыпь по всему кадру дала одиночные
    /// ящики посреди голого грунта, и они прочитались мусором, а не местом.
    /// Мое же рассуждение в V2Rekvizit говорило «кластерами вплотную к
    /// постройкам», а код сыпал случайно по всей площади. Ровно тот случай, про
    /// который в передаче опыта сказано «примитивами в коде, выглядело
    /// соответственно».
    /// </summary>
    private static void Klaster(float u, float v, float radius, int shtuk, int paletra)
    {
        var r = _rekvizit;
        var (c, n) = (new[] { V2Rekvizit.KREM, V2Rekvizit.GOLUBOY, V2Rekvizit.ZHELTYY,
                              V2Rekvizit.GRAFIT, V2Rekvizit.SINIY, V2Rekvizit.ZELENYY },
                      new[] { "krem", "goluboy", "zheltyy", "grafit", "siniy", "zelenyy" });
        for (int i = 0; i < shtuk; i++)
        {
            float a = Sluch(0f, 360f) * Mathf.Deg2Rad;
            float d = radius * Mathf.Sqrt((float)_rnd.NextDouble());
            var p = P(u + Mathf.Cos(a) * d, v + Mathf.Sin(a) * d);
            int k = (paletra + SluchInt(0, 3)) % c.Length;
            double vyb = _rnd.NextDouble();
            if (vyb < 0.42) V2Rekvizit.Shtabel(r, p, Sluch(0.7f, 1.1f), Sluch(0f, 360f),
                                               c[k], n[k], SluchInt(2, 4));
            else if (vyb < 0.66) V2Rekvizit.Poddon(r, p, Sluch(1.0f, 1.5f), Sluch(0f, 360f));
            else if (vyb < 0.85) V2Rekvizit.Bochka(r, p, 0.40f, 1.1f, c[k], n[k]);
            else V2Rekvizit.Yashchik(r, p, Sluch(0.7f, 1.0f), Sluch(0f, 360f), c[k], n[k]);
        }
    }

    /// <summary>
    /// Заполнение кадра. Рукотворное идет ТОЛЬКО кластерами у построек,
    /// голый грунт заполняется природным - камнями и мелочью ландшафта.
    ///
    /// Оплачено кадром p3b: равномерная россыпь по всему кадру дала одиночные
    /// ящики посреди голого грунта, и они прочитались мусором, а не местом.
    /// И оплачено вторично: я наращивал кластеры, догоняя метрику «деталь», а
    /// она считает плотность краев и не отличает осмысленный предмет от
    /// россыпи кубиков. В кадре это читалось конфетти. Поэтому кластеров ровно
    /// столько, сколько построек, которые их объясняют.
    /// </summary>
    private static void Rossyp()
    {
        // Каждый кластер стоит ПРИ постройке и объясняет себя ею.
        (float u, float v, float r, int n, int pal)[] kl =
        {
            (-30f, -16f, 2.6f, 5, 0),   // груз на перроне, западная площадка
            (-20f, -15f, 2.4f, 4, 4),   // груз на перроне, восточная площадка
            (-27f, -11f, 2.4f, 4, 2),   // у грузовых бункеров
            (-4f, -6f, 2.6f, 5, 3),     // двор квартала, западная половина
            (4f, -4f, 2.4f, 4, 1),      // двор квартала, восточная половина
            (-8f, 6f, 2.2f, 4, 0),      // у жилых куполов
            (-2f, 11f, 2.4f, 4, 5),     // у теплиц
            (20f, -13f, 2.8f, 6, 3),    // двор промзоны
            (27f, -8f, 2.6f, 5, 5),     // у складов
            (22f, 17f, 2.6f, 5, 1),     // забой карьера
            (30f, 10f, 2.4f, 4, 2),     // южный край карьера
            (-16f, 10f, 2.4f, 4, 5),    // у ледовой буровой
        };
        foreach (var k in kl) Klaster(k.u, k.v, k.r, k.n, k.pal);

        // КАМНИ КУЧКАМИ ПО ГРАНИЦЕ, А НЕ РОВНЫМ ПОСЫПАНИЕМ.
        //
        // Закон снят с настоящих кадров Township, а не придуман: плотное ядро,
        // пустая округа. Внутри застройки и на переднем плане камней нет вовсе;
        // вдоль границы освоенного стоят кучки по 4-5.
        string[] prirodnoe = { "kamen-gladkiy", "kamen-granenyy", "kamni-para" };

        // Якоря кучек по кольцу ВОКРУГ застройки. Застройка занимает
        // u от -40 до 37 и v от -31 до 25, поэтому кольцо идет снаружи этого.
        (float u, float v)[] yakorya =
        {
            (-46f, -38f), (-48f, -8f), (-44f, 20f), (-30f, 34f),
            (-6f, 38f), (16f, 36f), (34f, 30f), (46f, 6f),
            (44f, -20f), (26f, -36f), (2f, -40f), (-22f, -40f),
        };

        int postavleno = 0;
        foreach (var ya in yakorya)
        {
            int vKuchke = 4 + SluchInt(0, 2);            // 4..5 в пределах спеки 3..5
            for (int i = 0; i < vKuchke; i++)
            {
                float u = ya.u + Sluch(-3.2f, 3.2f);
                float v = ya.v + Sluch(-3.2f, 3.2f);
                if (NaPostroennom(u, v)) continue;
                // первый в кучке крупнее: у кучки должен быть свой якорь
                float h = i == 0 ? Sluch(2.2f, 3.4f) : Sluch(0.7f, 1.6f);
                Stavit(prirodnoe[SluchInt(0, prirodnoe.Length)], u, v,
                       h, Sluch(0f, 360f), _dekor);
                postavleno++;
            }
        }
        V2Lib.Log($"камней поставлено: {postavleno} (цель по спеке 40 +-5)");
    }

    // ------------------------------------------------------------ люди

}
