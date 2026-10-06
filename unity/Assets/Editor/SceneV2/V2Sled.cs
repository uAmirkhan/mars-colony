using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// След на земле: сколько модель занимает В ПЛАНЕ при заданной высоте.
///
/// Написан после того, как пять построек не встали в новую раскладку, а
/// остальные разъехались на 3-14 метров. Причина была не в спирали
/// расталкивания, а во мне: координаты назначались по клеткам 2.5 м, а
/// фактический след модели никто не мерил. Ангар при высоте 7 метров занимает
/// в плане 26 - на глаз это не считается никак.
///
/// Правило, которое отсюда следует: шаг между постройками задается ПОСЛЕ
/// замера следа, а не до. Иначе доктринная сетка расходится с геометрией, и
/// расталкивание молча превращает ряды в россыпь.
/// </summary>
public static class V2Sled
{
    /// <summary>Модель и высота, на которую она назначена в раскладке.</summary>
    private static readonly (string imya, float vysota)[] Nabor =
    {
        ("zhiloy-bashnya", 13.0f),
        ("zavod-pishchevoy", 10.0f),
        ("sklad-angar", 9.0f),
        ("burovaya-02", 9.0f),
        ("angar-s-panelyami", 8.5f),
        ("burovaya-05", 8.0f),
        ("sklad-bunkery", 7.0f),
        ("kupol-geodezicheskiy", 6.5f),
        ("kupol-grib", 5.5f),
        ("zhiloy-kupol", 5.0f),
        ("shattl-zakrytyy", 5.0f),
        ("shattl-otkrytyy", 5.0f),
        ("kupol-tunnel", 4.5f),
        ("modul-tonnelnyy", 4.2f),
        ("ploshchadka-shattla", 3.5f),
        ("burovaya-03", 3.5f),
        ("tsisterna", 3.2f),
        ("burovaya-04", 3.2f),
        ("led-greben", 6.0f),
        ("ballony-na-poddone", 1.9f),
        ("truba-na-kozlakh", 1.6f),
        ("kadka-s-zelenyu", 1.2f),
    };

    public static void Zamerit()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var sb = new StringBuilder();
        sb.AppendLine("модель                     высота   след X x Z    шаг с зазором 1.0");

        foreach (var n in Nabor)
        {
            var go = V2Lib.Place(n.imya, Vector2.zero, n.vysota, 0f);
            if (go == null) { sb.AppendLine($"{n.imya,-26} НЕ НАЙДЕНА"); continue; }
            if (!V2Lib.WorldBounds(go, out var b))
            { sb.AppendLine($"{n.imya,-26} БЕЗ ГЕОМЕТРИИ"); Object.DestroyImmediate(go); continue; }

            float sx = b.size.x, sz = b.size.z;
            sb.AppendLine($"{n.imya,-26} {n.vysota,5:F1}   {sx,5:F1} x {sz,5:F1}   "
                        + $"{Mathf.Max(sx, sz) + 1.0f,5:F1}");
            Object.DestroyImmediate(go);
        }

        Debug.Log("[v2] след на земле\n" + sb);
    }
}
