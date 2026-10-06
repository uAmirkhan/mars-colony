using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Наведение порядка в сцене ColonyOurs 2 — той, что владелец разложил руками.
///
/// Главное правило файла: сцена НЕ пересобирается. `Mars/Sobrat koloniyu`
/// сносит всё и раскладывает заново по таблице, и ручная работа владельца
/// исчезнет. Здесь только точечные правки поверх существующего.
///
/// ЧТО ЧИНИТСЯ. Размер повторяющегося предмета. Поддон с газовыми баллонами —
/// это одно промышленное изделие. Их в сцене пятнадцать штук шести разных
/// размеров, от 0.55 до 1.40, разница в два с половиной раза. Владелец назвал
/// это первым: «бочки могут быть разного размера, а баллоны нет». Труба на
/// козлах — то же самое: секция трубопровода одного диаметра, иначе магистраль
/// не стыкуется. Цистерне разнобой положен, но десять размеров — это не
/// разнобой, а отсутствие решения; сводим к трём типоразмерам.
///
/// ПОЧЕМУ ДЕРЖИМСЯ ЗА МАССУ, А НЕ ЗА ОПОРУ. Первая версия этого файла меняла
/// масштаб и возвращала на место низ габарита, полагая, что предмет стоит там
/// же, где его точка опоры. В этой сцене это неверно: сборщик сводил меши, и у
/// части объектов геометрия отстоит от опоры на десятки метров — у поддона с
/// баллонами замерен снос 21.5 м. Масштаб множит именно это смещение, поэтому
/// правка размера физически уносила предмет на десять-семнадцать метров в
/// сторону. Ошибка не давала ни исключения, ни предупреждения: отчёт
/// рапортовал «размер 0.55 -> 1.00», а предмет тем временем уезжал за кадр.
/// Поэтому теперь запоминается центр видимой массы по горизонтали и низ по
/// вертикали, и после смены масштаба предмет возвращается к ним обоим.
///
/// ПУСТЫЕ ОБОЛОЧКИ пропускаются вовсе. Объект без единого рендерера — это
/// husk: его геометрия давно уехала в общий сведённый меш. Менять ему масштаб
/// бессмысленно, а в отчёте он создаёт видимость работы.
///
/// ЧУЖОЙ ГРУЗ НА ПРЕДМЕТЕ. К части объектов сборщик подцепил сведённые полотна:
/// у одной `tsisterna` габарит оказался 51 метр, потому что её ребёнок — всё
/// дорожное покрытие колонии. Смена масштаба такому «предмету» растягивает
/// дороги: первая версия этого файла увеличила полотно с 45.3 до 51.2 метра,
/// то есть на тринадцать процентов, и не сказала об этом ни слова. Поэтому
/// стоит порог: настоящий поддон, бочка или секция трубы не бывает шире трёх
/// метров в плане, и всё, что шире, в правку не пускается.
/// </summary>
public static class OursPoryadok
{
    private const string Scena = "Assets/Scenes/ColonyOurs 2.unity";

    // Один размер на предмет: изделие не бывает шести размеров.
    // Значение выбрано по самому частому в сцене — так меняется меньше объектов.
    private static readonly Dictionary<string, float> OdinRazmer = new Dictionary<string, float>
    {
        { "ballony-na-poddone", 1.00f },
        { "truba-na-kozlakh",   1.00f },
    };

    // Цистерне разнобой положен, но ступенями, а не сплошняком.
    private static readonly float[] StupeniTsisterny = { 0.75f, 1.10f, 2.60f };

    [MenuItem("Mars/Ours/1. Poryadok v rekvizite")]
    public static void Rekvizit()
    {
        var scena = EditorSceneManager.OpenScene(Scena, OpenSceneMode.Single);
        var otchet = new StringBuilder();
        int tronuto = 0, propushcheno = 0;

        foreach (var go in scena.GetRootGameObjects())
        {
            string baza = Baza(go.name);

            float novyy;
            if (OdinRazmer.TryGetValue(baza, out float fiks)) novyy = fiks;
            else if (baza == "tsisterna") novyy = Blizhayshaya(go.transform.localScale.x, StupeniTsisterny);
            else continue;

            float bylo = go.transform.localScale.x;
            float ugolBylo = go.transform.eulerAngles.y;
            float ugol = Mathf.Round(ugolBylo / 90f) * 90f;

            bool menyaemRazmer = Mathf.Abs(bylo - novyy) > 0.001f;
            bool menyaemUgol = Mathf.Abs(Mathf.DeltaAngle(ugolBylo, ugol)) > 0.5f;
            if (!menyaemRazmer && !menyaemUgol) continue;

            if (!Gabarit(go, out var doPravki))
            {
                propushcheno++;
                otchet.AppendLine($"{go.name,-24} ПРОПУЩЕН: пустая оболочка без рендереров");
                continue;
            }

            // Порог чужого груза: предмет шире трёх метров в плане — это не
            // предмет, а объект со сведённым полотном на шее.
            float plan = Mathf.Max(doPravki.size.x, doPravki.size.z);
            if (plan > 3f * Mathf.Max(1f, bylo))
            {
                propushcheno++;
                otchet.AppendLine($"{go.name,-24} ПРОПУЩЕН: габарит {plan:F1} м — "
                                + "на нём висит сведённое полотно, масштабировать нельзя");
                continue;
            }

            Undo.RecordObject(go.transform, "порядок в реквизите");
            if (menyaemRazmer) go.transform.localScale = Vector3.one * novyy;
            if (menyaemUgol) go.transform.rotation = Quaternion.Euler(
                go.transform.eulerAngles.x, ugol, go.transform.eulerAngles.z);

            // Возврат на место: центр массы по горизонтали, низ по вертикали.
            if (Gabarit(go, out var posle))
            {
                var p = go.transform.position;
                p.x += doPravki.center.x - posle.center.x;
                p.z += doPravki.center.z - posle.center.z;
                p.y += doPravki.min.y - posle.min.y;
                go.transform.position = p;
            }

            tronuto++;
            otchet.AppendLine($"{go.name,-24} размер {bylo:F2} -> {novyy:F2}   угол {ugolBylo:F0} -> {ugol:F0}"
                            + $"   масса ({doPravki.center.x:F1},{doPravki.center.z:F1}) удержана");
        }

        // Сторожевая проверка: дорожное полотно должно остаться того же
        // размера, что и было у владельца — 45.3 на 46.3 метра. Если оно
        // выросло, значит правка добралась до сведённого меша, и это надо
        // увидеть сразу, а не через два прогона по кадрам.
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.name != "dorogi_sveden") continue;
            var r = t.GetComponent<Renderer>();
            if (r == null) continue;
            otchet.AppendLine($"СТОРОЖ dorogi_sveden: {r.bounds.size.x:F1} x {r.bounds.size.z:F1} м "
                            + "(у владельца было 45.3 x 46.3)");
        }

        // Проверка на месте: ни один предмет не должен был уехать.
        var uehali = new List<string>();
        foreach (var go in scena.GetRootGameObjects())
        {
            string baza = Baza(go.name);
            if (baza != "ballony-na-poddone" && baza != "truba-na-kozlakh" && baza != "tsisterna") continue;
            if (!Gabarit(go, out var g)) continue;
            if (g.center.y < -6f || g.center.y > 20f) uehali.Add($"{go.name} по высоте {g.center.y:F1}");
        }

        EditorSceneManager.MarkSceneDirty(scena);
        EditorSceneManager.SaveScene(scena);
        Debug.Log($"[ours] реквизит: тронуто {tronuto}, пропущено пустых {propushcheno}, "
                + $"уехавших {uehali.Count}\n{otchet}"
                + (uehali.Count > 0 ? "УЕХАЛИ: " + string.Join("; ", uehali) : ""));
    }

    /// <summary>Имя без хвоста " (3)", который Unity вешает на копии.</summary>
    private static string Baza(string imya) => Regex.Replace(imya, @" \(\d+\)$", "");

    private static float Blizhayshaya(float v, float[] stupeni)
    {
        float best = stupeni[0];
        foreach (var s in stupeni) if (Mathf.Abs(s - v) < Mathf.Abs(best - v)) best = s;
        return best;
    }

    /// <summary>
    /// Габарит видимой массы. Берём именно рендереры, а не коллайдеры:
    /// коллайдеров на этих моделях нет, а глазами видно как раз рендер.
    /// Возвращает false для пустой оболочки — её трогать нельзя.
    /// </summary>
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
