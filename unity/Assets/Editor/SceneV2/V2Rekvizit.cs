using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Мелкий реквизит из примитивов: ящики, бочки, трубы, мачты, панели, ограды.
///
/// Зачем он вообще. Замер: у Township деталь занимает 0.73 .. 0.86 кадра, у нас
/// было 0.185. Добрать это моделями библиотеки нельзя - медиана v3 даже после
/// облегчения 5900 треугольников, и двести таких штук не влезут в бюджет.
/// Ящик из примитива стоит 12 треугольников, то есть в пятьсот раз дешевле.
///
/// Второе назначение важнее первого. Реквизит красится в НАЗНАЧЕННЫЕ цвета, и
/// именно он закрывает норму по семействам: у Township ахроматика 0.158, желтый
/// 0.126, синий 0.060, у нас было 0.048 / 0.006 / 0.002. Марс монохромный, цвет
/// на нем может появиться только с того, что привезли люди.
///
/// Риск назван прямо: в прошлом заходе стройплощадка делалась примитивами и
/// «выглядела соответственно». Разница в трех вещах - реквизит крашеный, стоит
/// кластерами вплотную к постройкам, и в кадре он мелкий (ящик 1 м это 24
/// пикселя). Одиночный серый куб посреди пустоты выглядел бы так же плохо.
/// </summary>
public static class V2Rekvizit
{
    // Палитра назначена под замеренную норму, а не подобрана на глаз.
    public static readonly Color KREM = new Color(0.90f, 0.88f, 0.83f);
    public static readonly Color BELYY = new Color(0.94f, 0.94f, 0.92f);
    public static readonly Color GRAFIT = new Color(0.24f, 0.26f, 0.29f);
    public static readonly Color SINIY = new Color(0.20f, 0.38f, 0.58f);
    public static readonly Color GOLUBOY = new Color(0.44f, 0.70f, 0.82f);
    public static readonly Color ZHELTYY = new Color(0.89f, 0.72f, 0.20f);
    public static readonly Color ZELENYY = new Color(0.36f, 0.60f, 0.28f);
    public static readonly Color RZHAVYY = new Color(0.72f, 0.36f, 0.18f);

    private static readonly Dictionary<string, Material> _mats = new();

    private static Material Mat(Color c, string imya, float gladkost = 0.20f)
    {
        if (_mats.TryGetValue(imya, out var m)) return m;
        m = V2Lib.NewLit(c, null, gladkost, imya: "rekv-" + imya);
        _mats[imya] = m;
        return m;
    }

    private static GameObject Prim(PrimitiveType t, Transform parent, string imya,
                                   Vector3 poz, Vector3 masshtab, Vector3 povorot,
                                   Color c, string matImya)
    {
        var go = GameObject.CreatePrimitive(t);
        go.name = imya;
        go.transform.SetParent(parent, false);
        go.transform.position = poz;
        go.transform.localScale = masshtab;

        // Реквизит подчиняется тому же закону, что и модели: ящик, бочка,
        // ограда - рукотворные, у них есть грань. Спека прямо говорит: «45
        // градусов под ящиком остается браком».
        //
        // Наклон (поворот по X и Z) не трогаем - он задает форму предмета,
        // например уклон солнечной панели. Закон касается разворота по земле.
        povorot.y = V2Lib.ZakonUgla(imya, povorot.y);
        go.transform.rotation = Quaternion.Euler(povorot);
        go.GetComponent<MeshRenderer>().sharedMaterial = Mat(c, matImya);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    // ------------------------------------------------------------ штучное

    /// <summary>Ящик. 12 треугольников.</summary>
    public static void Yashchik(Transform p, Vector3 xz, float storona,
                                float yaw, Color c, string matImya)
    {
        Prim(PrimitiveType.Cube, p, "yashchik",
             new Vector3(xz.x, storona * 0.5f, xz.z),
             new Vector3(storona, storona, storona * 0.85f),
             new Vector3(0, yaw, 0), c, matImya);
    }

    /// <summary>Штабель ящиков: три-четыре штуки друг на друге со сдвигом.
    /// Кластер читается как склад, одиночный ящик как мусор.</summary>
    public static void Shtabel(Transform p, Vector3 xz, float storona, float yaw,
                               Color c, string matImya, int etazhey = 3)
    {
        for (int i = 0; i < etazhey; i++)
        {
            float s = storona * (1f - i * 0.08f);
            float dx = (i % 2 == 0 ? 0.08f : -0.06f) * storona;
            Prim(PrimitiveType.Cube, p, "yashchik",
                 new Vector3(xz.x + dx, storona * (i + 0.5f) * 0.92f, xz.z + dx * 0.6f),
                 new Vector3(s, storona * 0.9f, s * 0.85f),
                 new Vector3(0, yaw + i * 7f, 0), c, matImya);
        }
    }

    /// <summary>Бочка.</summary>
    public static void Bochka(Transform p, Vector3 xz, float r, float h,
                              Color c, string matImya)
    {
        Prim(PrimitiveType.Cylinder, p, "bochka",
             new Vector3(xz.x, h * 0.5f, xz.z),
             new Vector3(r * 2f, h * 0.5f, r * 2f),
             Vector3.zero, c, matImya);
    }

    /// <summary>Труба на подставках, лежит горизонтально.</summary>
    public static void Truba(Transform p, Vector3 a, Vector3 b, float r,
                             Color c, string matImya)
    {
        var d = b - a; d.y = 0f;
        float dlina = d.magnitude;
        if (dlina < 0.2f) return;
        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        var centr = (a + b) * 0.5f;
        float h = r * 2.2f;
        Prim(PrimitiveType.Cylinder, p, "truba",
             new Vector3(centr.x, h, centr.z),
             new Vector3(r * 2f, dlina * 0.5f, r * 2f),
             new Vector3(90f, yaw, 0f), c, matImya);
        int opor = Mathf.Max(2, Mathf.RoundToInt(dlina / 4f));
        for (int i = 0; i <= opor; i++)
        {
            var t = Vector3.Lerp(a, b, i / (float)opor);
            Prim(PrimitiveType.Cube, p, "opora",
                 new Vector3(t.x, h * 0.5f, t.z),
                 new Vector3(r * 0.7f, h, r * 0.7f),
                 new Vector3(0, yaw, 0), GRAFIT, "grafit");
        }
    }

    /// <summary>Мачта с фонарем наверху.</summary>
    public static void Machta(Transform p, Vector3 xz, float h, Color c, string matImya)
    {
        Prim(PrimitiveType.Cylinder, p, "machta",
             new Vector3(xz.x, h * 0.5f, xz.z),
             new Vector3(0.18f, h * 0.5f, 0.18f), Vector3.zero, c, matImya);
        Prim(PrimitiveType.Cube, p, "fonar",
             new Vector3(xz.x, h + 0.15f, xz.z),
             new Vector3(0.7f, 0.25f, 0.4f),
             new Vector3(0, 25f, 0), ZHELTYY, "zheltyy");
    }

    /// <summary>Солнечная панель на опоре. Главный носитель синего.</summary>
    public static void Panel(Transform p, Vector3 xz, float shirina, float yaw,
                             float naklon = 28f)
    {
        float h = shirina * 0.42f;
        Prim(PrimitiveType.Cube, p, "opora-paneli",
             new Vector3(xz.x, h * 0.5f, xz.z),
             new Vector3(0.16f, h, 0.16f), Vector3.zero, GRAFIT, "grafit");
        Prim(PrimitiveType.Cube, p, "panel",
             new Vector3(xz.x, h + shirina * 0.16f, xz.z),
             new Vector3(shirina, 0.16f, shirina * 0.62f),
             new Vector3(naklon, yaw, 0f), SINIY, "siniy");
    }

    /// <summary>Ограждение: столбики и две нитки поручня.</summary>
    public static void Ograda(Transform p, Vector3 a, Vector3 b, Color c, string matImya)
    {
        var d = b - a; d.y = 0f;
        float dlina = d.magnitude;
        if (dlina < 0.5f) return;
        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        int n = Mathf.Max(2, Mathf.RoundToInt(dlina / 2.5f));
        for (int i = 0; i <= n; i++)
        {
            var t = Vector3.Lerp(a, b, i / (float)n);
            Prim(PrimitiveType.Cube, p, "stolb",
                 new Vector3(t.x, 0.55f, t.z),
                 new Vector3(0.10f, 1.1f, 0.10f),
                 new Vector3(0, yaw, 0), c, matImya);
        }
        var centr = (a + b) * 0.5f;
        for (int j = 0; j < 2; j++)
            Prim(PrimitiveType.Cube, p, "poruchen",
                 new Vector3(centr.x, 0.45f + j * 0.45f, centr.z),
                 new Vector3(0.06f, 0.06f, dlina),
                 new Vector3(0, yaw, 0), c, matImya);
    }

    /// <summary>Поддон с грузом под тканью.</summary>
    public static void Poddon(Transform p, Vector3 xz, float storona, float yaw)
    {
        Prim(PrimitiveType.Cube, p, "poddon",
             new Vector3(xz.x, 0.09f, xz.z),
             new Vector3(storona, 0.18f, storona * 0.8f),
             new Vector3(0, yaw, 0), RZHAVYY, "rzhavyy");
        Prim(PrimitiveType.Cube, p, "gruz",
             new Vector3(xz.x, 0.18f + storona * 0.28f, xz.z),
             new Vector3(storona * 0.86f, storona * 0.56f, storona * 0.68f),
             new Vector3(0, yaw + 4f, 0), KREM, "krem");
    }

    /// <summary>Контейнер: длинный ящик с ребрами. Крупная единица склада.</summary>
    public static void Konteyner(Transform p, Vector3 xz, float dlina, float yaw, Color c,
                                 string matImya)
    {
        float h = dlina * 0.42f, w = dlina * 0.45f;
        Prim(PrimitiveType.Cube, p, "konteyner",
             new Vector3(xz.x, h * 0.5f, xz.z),
             new Vector3(dlina, h, w), new Vector3(0, yaw, 0), c, matImya);
        // Ребра по верху. Без них контейнер сверху это один плоский
        // прямоугольник насыщенного цвета, и в кадре он читается флагом, а не
        // предметом. Поймано на кадре p3: синий и желтый контейнеры рядом
        // выглядели полотнищем.
        int reber = Mathf.Max(3, Mathf.RoundToInt(dlina / 0.8f));
        for (int i = 1; i < reber; i++)
        {
            float t = (i / (float)reber - 0.5f) * dlina;
            var off = Quaternion.Euler(0, yaw, 0) * new Vector3(t, 0, 0);
            Prim(PrimitiveType.Cube, p, "rebro",
                 new Vector3(xz.x + off.x, h * 0.5f, xz.z + off.z),
                 new Vector3(0.07f, h * 0.98f, w * 1.03f),
                 new Vector3(0, yaw, 0), c * 0.78f, matImya + "-rebro");
        }

        // торцы светлее, дает окантовку и вклад в ахроматику
        for (int s = -1; s <= 1; s += 2)
        {
            var off = Quaternion.Euler(0, yaw, 0) * new Vector3(dlina * 0.5f * s, 0, 0);
            Prim(PrimitiveType.Cube, p, "torets",
                 new Vector3(xz.x + off.x, h * 0.5f, xz.z + off.z),
                 new Vector3(0.10f, h * 0.92f, w * 0.94f),
                 new Vector3(0, yaw, 0), KREM, "krem");
        }
    }
}
