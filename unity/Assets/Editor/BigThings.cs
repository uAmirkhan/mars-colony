using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Все рендереры сцены по убыванию габарита. Нужен после опыта, который показал,
/// что видимая в кадре поверхность — не «zemlya»: перекраска земли в зеленый не
/// изменила ни одного пикселя. Значит поверх нее что-то лежит, и это «что-то»
/// надо назвать по имени, а не угадывать.
/// </summary>
public static class BigThings
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/ColonyOurs.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();

        var all = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
            .OrderByDescending(r => r.bounds.size.x * r.bounds.size.z)
            .Take(15);

        sb.AppendLine("=== 15 самых крупных рендереров по площади ===");
        foreach (var r in all)
        {
            var m = r.sharedMaterial;
            string tex = "нет";
            if (m != null && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null)
                tex = m.GetTexture("_BaseMap").name;

            sb.AppendLine($"{r.gameObject.name,-24} | путь={FullPath(r.transform)}");
            sb.AppendLine($"    габарит={r.bounds.size:F1} центр={r.bounds.center:F1} вкл={r.enabled}");
            sb.AppendLine($"    мат={(m != null ? m.name : "null")} шейдер={(m != null ? m.shader.name : "-")} карта={tex}");
            sb.AppendLine($"    очередь={(m != null ? m.renderQueue.ToString() : "-")} тень={r.shadowCastingMode}");
        }

        File.WriteAllText("big-things.txt", sb.ToString());
        Debug.Log("[big] записано");
    }

    private static string FullPath(Transform t)
    {
        var parts = new System.Collections.Generic.List<string>();
        while (t != null) { parts.Add(t.name); t = t.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
