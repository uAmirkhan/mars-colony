using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Перестилка дорог: сведённое полотно снимается, сеть стелется заново
/// ОТДЕЛЬНЫМИ полосами.
///
/// Зачем. В сцене дороги и трубы сведены в общие меши: из шестнадцати труб
/// живых шесть, остальные — пустые оболочки, а геометрия лежит внутри куска
/// `truby`. Из-за этого брошенные поперёк карьера «палки» нельзя ни убрать, ни
/// переставить: они не объекты, а треугольники в чужом меше. Владелец с этим
/// согласился и назвал разварку обратно на объекты нужной работой.
///
/// Как стелется полоса. Сегмент — куб длиной 0.8 м, посаженный лучом на грунт
/// в обоих концах И В СЕРЕДИНЕ. Середина нужна потому, что на выпуклом
/// перегибе рельефа хорда между концами уходит под землю, и из ленты выедаются
/// клинья грунта. Рецепт взят у сборщика сцены, где он был выведен замером, а
/// не подобран.
///
/// Что стелется. Одна магистраль с юга на север (космопорт — центр — завод) и
/// пять веток, каждая упирается в постройку: жильё, ледник, промзона, карьер,
/// причал. Плюс двор под центральным кварталом. Дорога, которая никуда не
/// ведёт, — это и есть «дорога в никуда», которую видно в кадре.
///
/// Карьер обходится с востока в семи метрах от кромки ямы, а в саму яму ведёт
/// короткий съезд. Владелец назвал сквозную дорогу через рабочий забой
/// нелогичной, и это правда: по забою не ездят, в него съезжают.
/// </summary>
public static class OursDorogi
{
    private const string Scena = "Assets/Scenes/ColonyOurs 2.unity";

    /// <summary>Маршрут: имя, ширина в метрах, точки в плане.</summary>
    private struct Marshrut
    {
        public string imya;
        public float shirina;
        public Vector2[] tochki;
    }

    private static readonly Marshrut[] Set =
    {
        // Магистраль: причал -> ангар -> теплицы -> башня -> завод.
        new Marshrut { imya = "magistral", shirina = 3.2f, tochki = new[]
        {
            new Vector2(-3f, -21f), new Vector2(-3f, -14f), new Vector2(-2.5f, -6f),
            new Vector2(-2f, 0f), new Vector2(-1f, 8f), new Vector2(0f, 15f),
            new Vector2(1f, 18.5f),
        }},

        // Жилая ветка: вдоль ряда куполов и тоннельных модулей.
        new Marshrut { imya = "vetka-zhilyo", shirina = 2.4f, tochki = new[]
        {
            new Vector2(-2.5f, -6f), new Vector2(-9f, -5.5f), new Vector2(-15f, -5f),
            new Vector2(-16.5f, -2f), new Vector2(-16.5f, 3f),
        }},

        // Ледовая ветка: к ангару ледовой техники и к буровой на кромке льда.
        new Marshrut { imya = "vetka-lyod", shirina = 2.4f, tochki = new[]
        {
            new Vector2(-16.5f, 3f), new Vector2(-21f, 4f), new Vector2(-25.5f, 4.5f),
            new Vector2(-26f, 8f), new Vector2(-26f, 11f),
        }},

        // Промышленная ветка: к цистернам и складу.
        new Marshrut { imya = "vetka-promzona", shirina = 2.8f, tochki = new[]
        {
            new Vector2(-1f, 8f), new Vector2(6f, 8f), new Vector2(12f, 10f),
            new Vector2(18f, 13f), new Vector2(23f, 15.5f),
        }},

        // Карьерная ветка: обходит яму с востока, упирается в отвал.
        new Marshrut { imya = "vetka-karier", shirina = 2.8f, tochki = new[]
        {
            new Vector2(23f, 15.5f), new Vector2(23f, 22f), new Vector2(22f, 28f),
            new Vector2(20f, 32.5f), new Vector2(17f, 35.5f),
        }},

        // Съезд в яму: короткий, с востока, а не сквозной проезд по забою.
        new Marshrut { imya = "sezd-v-yamu", shirina = 3.0f, tochki = new[]
        {
            new Vector2(19.5f, 30f), new Vector2(15.5f, 29.5f),
        }},

        // Причальная ветка: обе площадки шаттла и склад-бункеры.
        new Marshrut { imya = "vetka-prichal", shirina = 2.8f, tochki = new[]
        {
            new Vector2(-3f, -21f), new Vector2(-11f, -22.5f), new Vector2(-19f, -22.5f),
            new Vector2(-23.5f, -22.5f),
        }},
    };

    // Двор центрального квартала: полосы кладутся рядом, образуя площадь.
    private static readonly Vector2 DvorOt = new Vector2(-12f, -6f);
    private static readonly Vector2 DvorDo = new Vector2(10f, 8f);
    private const float DvorShag = 2.6f;

    [MenuItem("Mars/Ours/3. Perestelit dorogi")]
    public static void Perestelit()
    {
        var scena = EditorSceneManager.OpenScene(Scena, OpenSceneMode.Single);
        var otchet = new StringBuilder();

        // Материал берётся у снимаемого полотна: свой цвет дорог у сцены уже
        // подобран, назначать новый значит менять вид без просьбы.
        Material mat = null;
        var snyat = new List<GameObject>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.name != "dorogi_sveden" && t.name != "truby") continue;
            var r = t.GetComponent<Renderer>();
            if (mat == null && r != null) mat = r.sharedMaterial;
            snyat.Add(t.gameObject);
        }
        if (mat == null)
        {
            Debug.LogError("[ours] не нашёл материал дорог — ничего не трогаю");
            return;
        }
        // Луч должен видеть грунт. Если коллайдера земли нет, полосы лягут на
        // нулевую высоту сплошной плитой поперёк рельефа — и это надо поймать
        // ДО того, как снято старое полотно, иначе останемся без обоих.
        Physics.SyncTransforms();
        if (!Physics.Raycast(new Vector3(0f, 60f, 0f), Vector3.down, out _, 200f))
        {
            Debug.LogError("[ours] луч не видит грунт: у земли нет коллайдера. "
                         + "Старое полотно не тронуто.");
            return;
        }

        otchet.AppendLine($"снимается сведённых кусков: {snyat.Count}, материал {mat.name}");
        foreach (var g in snyat) Undo.DestroyObjectImmediate(g);

        var koren = GameObject.Find("dorogi-novye");
        if (koren != null) Undo.DestroyObjectImmediate(koren);
        koren = new GameObject("dorogi-novye");
        Undo.RegisterCreatedObjectUndo(koren, "перестилка дорог");

        int vsego = 0;
        foreach (var m in Set)
        {
            var vetka = new GameObject(m.imya);
            vetka.transform.SetParent(koren.transform, false);
            int n = Stelit(m.tochki, m.shirina, mat, vetka.transform);
            vsego += n;
            otchet.AppendLine($"{m.imya,-16} полос {n,4}  ширина {m.shirina:F1} м");
        }

        // Двор: параллельные полосы с востока на запад.
        var dvor = new GameObject("dvor-centra");
        dvor.transform.SetParent(koren.transform, false);
        int nd = 0;
        for (float z = DvorOt.y; z <= DvorDo.y + 0.01f; z += DvorShag)
            nd += Stelit(new[] { new Vector2(DvorOt.x, z), new Vector2(DvorDo.x, z) },
                         DvorShag * 1.04f, mat, dvor.transform);
        vsego += nd;
        otchet.AppendLine($"{"dvor-centra",-16} полос {nd,4}");

        otchet.AppendLine($"\nвсего полос: {vsego} — каждая отдельный объект, двигается поштучно");
        EditorSceneManager.MarkSceneDirty(scena);
        EditorSceneManager.SaveScene(scena);
        Debug.Log("[ours] перестилка дорог\n" + otchet);
    }

    /// <summary>
    /// Стелет полосу по ломаной. Шаг 0.8 м и замер высоты в середине сегмента —
    /// иначе на выпуклом перегибе рельефа середина уходит под грунт и в ленте
    /// появляются проплешины.
    /// </summary>
    private static int Stelit(Vector2[] liniya, float shirina, Material mat, Transform roditel)
    {
        const float shag = 0.8f;
        const float tolshchina = 0.14f;
        int sdelano = 0;

        for (int i = 0; i < liniya.Length - 1; i++)
        {
            var a = liniya[i];
            var b = liniya[i + 1];
            float dlina = Vector2.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(dlina / shag));
            for (int k = 0; k < n; k++)
            {
                var p0 = Vector2.Lerp(a, b, k / (float)n);
                var p1 = Vector2.Lerp(a, b, (k + 1) / (float)n);

                float y0 = Vysota(p0), y1 = Vysota(p1);
                var pSer = (p0 + p1) * 0.5f;
                float ySer = Vysota(pSer);
                float podnyat = ySer - (y0 + y1) * 0.5f;
                if (podnyat > 0f) { y0 += podnyat; y1 += podnyat; }

                var a3 = new Vector3(p0.x, y0, p0.y);
                var b3 = new Vector3(p1.x, y1, p1.y);
                var seredina = (a3 + b3) * 0.5f;
                var vpered = b3 - a3;
                if (vpered.sqrMagnitude < 1e-6f) continue;

                var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seg.name = "polosa";
                seg.transform.SetParent(roditel, false);
                seg.transform.position = seredina + Vector3.up * (tolshchina * 0.5f);
                seg.transform.rotation = Quaternion.LookRotation(vpered.normalized, Vector3.up);
                seg.transform.localScale = new Vector3(shirina, tolshchina, vpered.magnitude * 1.02f);
                seg.GetComponent<MeshRenderer>().sharedMaterial = mat;
                // Коллайдер полосе не нужен: по ней никто не ходит, а лучи
                // высоты должны бить в грунт, а не в уже уложенную дорогу.
                Object.DestroyImmediate(seg.GetComponent<Collider>());
                sdelano++;
            }
        }
        return sdelano;
    }

    /// <summary>Высота грунта в точке. Луч бьёт сверху по коллайдеру земли.</summary>
    private static float Vysota(Vector2 p)
    {
        if (Physics.Raycast(new Vector3(p.x, 60f, p.y), Vector3.down, out var h, 200f))
            return h.point.y;
        return 0f;
    }
}
