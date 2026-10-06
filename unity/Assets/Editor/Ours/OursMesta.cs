using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Логика места: каждое здание переезжает туда, где у него есть адресат.
///
/// Разбор сошёлся у трёх независимых источников — владельца, геймдизайнера и
/// системщика — на одном и том же: постройки стоят там, где было свободно, а не
/// там, где они кому-то нужны. Склад в сорока метрах от всего, что он хранит.
/// Буровая на дальнем западе, рядом со льдом, но ничем со льдом не связанная.
/// У ледодобычи нет ангара, хотя техника там работает.
///
/// Правило переезда: у постройки должен быть сосед, объясняющий её присутствие.
/// Склад — у того, чью продукцию хранит. Ангар техники — у того забоя, где эта
/// техника работает.
///
/// ЦЕЛИМСЯ ПО МАССЕ, А НЕ ПО ОПОРЕ. В этой сцене сборщик сводил меши, и у части
/// объектов точка опоры отстоит от видимой геометрии на десятки метров — у
/// шаттлов замерено 23 метра. Если ставить объект «опорой в точку», его
/// геометрия окажется в двадцати метрах оттуда, и в кадре не изменится ничего
/// либо изменится не то. Поэтому считается сдвиг массы, и он прибавляется к
/// опоре.
///
/// ПУСТЫЕ ОБОЛОЧКИ ПРОПУСКАЮТСЯ. Объект без рендереров — husk, его геометрия
/// уехала в общий сведённый меш. Один такой «переезд» в этой сессии уже
/// отработал вхолостую: отчёт написал «сдвинут на 40 метров», а в сцене не
/// поменялось ничего.
///
/// ПЕРЕСЕЧЕНИЯ считаются только между постройками разумного размера. Сведённые
/// полотна — земля, дороги, тени — имеют габарит в полсотни метров и лежат под
/// всей колонией; включать их в проверку значит получить список, где всё
/// пересекается со всем. Ровно это и вышло в первой версии: 29 «накладок»
/// между домами, стоящими в двадцати метрах друг от друга.
/// </summary>
public static class OursMesta
{
    private const string Scena = "Assets/Scenes/ColonyOurs 2.unity";

    /// <summary>Куда и зачем переезжает постройка. Точка — по видимой массе.</summary>
    private struct Pereezd
    {
        public string imya;      // имя объекта в сцене
        public Vector2 otkuda;   // где его масса сейчас — по ней и опознаём
        public Vector2 kuda;     // куда должна встать масса (X, Z)
        public float ugol;       // разворот по Y, кратно 90
        public string zachem;    // причина — идёт в отчёт
    }

    private static readonly Pereezd[] Plan =
    {
        // Склад стоял на северной кромке карты у отвала, в тридцати метрах от
        // ближайшего производителя, без двора и без подъезда. Переезжает в
        // промышленный пояс: с севера приходит дорога из карьера, рядом
        // батарея цистерн, рядом ангар.
        new Pereezd { imya = "sklad-angar", otkuda = new Vector2(-0.5f, 49.6f),
                      kuda = new Vector2(25f, 17f), ugol = 270f,
                      zachem = "к промышленному поясу и карьерной дороге" },

        // Буровая стояла одна на дальнем западе среди валунов, ничем со льдом
        // не связанная. Подтягивается к кромке ледника — теперь видно, что
        // именно она бурит.
        new Pereezd { imya = "burovaya-05", otkuda = new Vector2(-36.5f, 4.0f),
                      kuda = new Vector2(-26f, 12f), ugol = 90f,
                      zachem = "на кромку ледника, к скважине" },

        // Гусеничная машина стояла в пяти метрах восточнее ямы, на голом
        // грунте. Ставится на кромку забоя, развёрнута к яме — она тут работает.
        new Pereezd { imya = "burovaya-04", otkuda = new Vector2(23.2f, 29.8f),
                      kuda = new Vector2(17.5f, 31f), ugol = 180f,
                      zachem = "на кромку забоя, лицом в яму" },

        // Две буровые стояли внутри построек: одна пробивала жилую башню и
        // пищевой завод, вторая заходила заводу в угол. Владелец назвал
        // непересекающиеся здания отдельным требованием. Уходят на кромку
        // ледника, к своим двум собратьям — там их работа и есть.
        new Pereezd { imya = "burovaya-03", otkuda = new Vector2(-5.1f, 17.1f),
                      kuda = new Vector2(-10.5f, 20f), ugol = 90f,
                      zachem = "из жилой башни и завода — на кромку ледника" },

        new Pereezd { imya = "burovaya-03", otkuda = new Vector2(-2.2f, 28.6f),
                      kuda = new Vector2(-8.5f, 30f), ugol = 90f,
                      zachem = "из угла завода — на кромку ледника" },
    };

    /// <summary>
    /// Ангар ледовой техники. Владелец просил его прямо, а свободного ангара в
    /// сцене нет: второй экземпляр `angar-s-panelyami` оказался пустой
    /// оболочкой. Поэтому размножается тот, что цел, — копия несёт свои
    /// материалы, в отличие от голой модели из библиотеки.
    /// </summary>
    private static readonly Vector2 AngarLda = new Vector2(-27f, 4f);
    private const float AngarLdaUgol = 90f;

    [MenuItem("Mars/Ours/2. Logika mesta")]
    public static void Perestavit()
    {
        var scena = EditorSceneManager.OpenScene(Scena, OpenSceneMode.Single);
        var otchet = new StringBuilder();

        foreach (var p in Plan)
        {
            var go = Opoznat(scena.GetRootGameObjects(), p.imya, p.otkuda);
            if (go == null)
            {
                otchet.AppendLine($"НЕ ОПОЗНАН: {p.imya} у ({p.otkuda.x:F1},{p.otkuda.y:F1}) — пропущен");
                continue;
            }

            var prepyatstviya = Postroyki(scena).Where(g => g != go).ToList();
            Postavit(go, p.kuda, p.ugol, prepyatstviya, out var stal);
            otchet.AppendLine($"{p.imya,-22} масса ({p.otkuda.x:F1},{p.otkuda.y:F1}) -> "
                            + $"({stal.x:F1},{stal.y:F1})  — {p.zachem}");
        }

        // Ангар ледовой техники — копия целого ангара.
        // Повторный прогон не должен плодить копии: если ангар уже поставлен,
        // второй не создаётся.
        var uzhe = scena.GetRootGameObjects().FirstOrDefault(g => g.name == "angar-ledovoy-tehniki");
        var obrazec = uzhe != null ? null
                    : Postroyki(scena).FirstOrDefault(g => Baza(g.name) == "angar-s-panelyami");
        if (uzhe != null) otchet.AppendLine("ангар ледовой техники: уже стоит, копия не создаётся");
        else if (obrazec == null) otchet.AppendLine("ангар ледовой техники: образец не найден");
        else
        {
            var kopiya = Object.Instantiate(obrazec, obrazec.transform.parent);
            kopiya.name = "angar-ledovoy-tehniki";
            Undo.RegisterCreatedObjectUndo(kopiya, "ангар ледовой техники");
            var prepyatstviya = Postroyki(scena).Where(g => g != kopiya).ToList();
            Postavit(kopiya, AngarLda, AngarLdaUgol, prepyatstviya, out var stal);
            otchet.AppendLine($"angar-ledovoy-tehniki   поставлен в ({stal.x:F1},{stal.y:F1})"
                            + "  — у буровой на кромке ледника");
        }

        var nakladki = Nakladki(Postroyki(scena));
        otchet.AppendLine($"\nпересечений построек: {nakladki.Count}");
        foreach (var n in nakladki) otchet.AppendLine("   " + n);

        EditorSceneManager.MarkSceneDirty(scena);
        EditorSceneManager.SaveScene(scena);
        Debug.Log("[ours] логика места\n" + otchet);
    }

    /// <summary>
    /// Ставит объект так, чтобы его ВИДИМАЯ МАССА встала в заданную точку, и
    /// низ остался на прежней высоте.
    /// </summary>
    private static void Postavit(GameObject go, Vector2 kuda, float ugol,
                                 List<GameObject> prepyatstviya, out Vector2 stal)
    {
        Gabarit(go, out var doPravki);
        Undo.RecordObject(go.transform, "логика места");
        go.transform.rotation = Quaternion.Euler(
            go.transform.eulerAngles.x, ugol, go.transform.eulerAngles.z);

        Gabarit(go, out var poslePovorota);
        var cel = SvobodnoeMesto(poslePovorota.size, kuda, prepyatstviya);

        var p = go.transform.position;
        p.x += cel.x - poslePovorota.center.x;
        p.z += cel.y - poslePovorota.center.z;
        go.transform.position = p;

        if (Gabarit(go, out var posle))
        {
            var q = go.transform.position;
            q.y += doPravki.min.y - posle.min.y;
            go.transform.position = q;
        }
        Gabarit(go, out var itog);
        stal = new Vector2(itog.center.x, itog.center.z);
    }

    /// <summary>
    /// Опознание по имени И ближайшей массе.
    ///
    /// Одного имени мало: два `angar-s-panelyami` названы в сцене одинаково,
    /// без хвоста-номера, и выбор первого попавшегося увёз бы не тот объект.
    /// Одной опоры тоже мало — она у части объектов уехала от геометрии.
    /// </summary>
    private static GameObject Opoznat(GameObject[] korni, string imya, Vector2 otkuda)
    {
        GameObject best = null;
        float bd = float.MaxValue;
        foreach (var g in korni)
        {
            if (Baza(g.name) != imya) continue;
            if (!Gabarit(g, out var b)) continue;   // пустышку не берём
            float d = (new Vector2(b.center.x, b.center.z) - otkuda).sqrMagnitude;
            if (d < bd) { bd = d; best = g; }
        }
        return bd <= 16f ? best : null;   // дальше четырёх метров — не тот объект
    }

    /// <summary>
    /// Постройки для проверки пересечений: только с рендерерами и только
    /// разумного размера. Сведённые полотна (земля, дороги, тени) лежат под
    /// всей колонией и в проверку не идут.
    /// </summary>
    private static List<GameObject> Postroyki(UnityEngine.SceneManagement.Scene scena)
    {
        var spisok = new List<GameObject>();
        foreach (var go in scena.GetRootGameObjects())
        {
            string n = Baza(go.name);
            string[] da = { "sklad-", "angar-", "zavod-", "zhiloy-", "kupol-", "modul-",
                            "burovaya-", "ploshchadka-", "shattl-" };
            if (!da.Any(p => n.StartsWith(p))) continue;
            if (!Gabarit(go, out var b)) continue;
            if (b.size.x > 30f || b.size.z > 30f) continue;
            spisok.Add(go);
        }
        return spisok;
    }

    private static string Baza(string imya) => Regex.Replace(imya, @" \(\d+\)$", "");

    /// <summary>
    /// Ближайшая к желаемой точка, где габарит не лезет в чужой.
    /// Спираль золотым углом: соседние пробы не выстраиваются в линию, и
    /// площадь вокруг цели обходится равномерно, а не полосами.
    /// </summary>
    private static Vector2 SvobodnoeMesto(Vector3 razmer, Vector2 hochu, List<GameObject> vse)
    {
        const float zolotoy = 137.508f * Mathf.Deg2Rad;
        for (int i = 0; i < 300; i++)
        {
            float r = 1.2f * Mathf.Sqrt(i);
            float a = i * zolotoy;
            var proba = hochu + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);

            bool chisto = true;
            foreach (var d in vse)
            {
                if (!Gabarit(d, out var b)) continue;
                if (Peresekaetsya(proba, razmer, new Vector2(b.center.x, b.center.z), b.size))
                { chisto = false; break; }
            }
            if (chisto) return proba;
        }
        return hochu;
    }

    /// <summary>Пересечение по плану: высота не в счёт, дома стоят на земле.</summary>
    private static bool Peresekaetsya(Vector2 ca, Vector3 sa, Vector2 cb, Vector3 sb)
    {
        return Mathf.Abs(ca.x - cb.x) * 2f < (sa.x + sb.x) * 0.95f &&
               Mathf.Abs(ca.y - cb.y) * 2f < (sa.z + sb.z) * 0.95f;
    }

    private static List<string> Nakladki(List<GameObject> vse)
    {
        var spisok = new List<string>();
        for (int i = 0; i < vse.Count; i++)
        for (int j = i + 1; j < vse.Count; j++)
        {
            Gabarit(vse[i], out var a); Gabarit(vse[j], out var b);
            if (Peresekaetsya(new Vector2(a.center.x, a.center.z), a.size,
                              new Vector2(b.center.x, b.center.z), b.size))
                spisok.Add($"{vse[i].name} и {vse[j].name} "
                         + $"у ({a.center.x:F0},{a.center.z:F0})");
        }
        return spisok;
    }

    private static bool Gabarit(GameObject go, out Bounds b)
    {
        b = default;
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return false;
        b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return true;
    }
}
