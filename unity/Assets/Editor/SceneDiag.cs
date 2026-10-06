using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Что реально стоит в собранной сцене: материал, шейдер, карта цвета.
/// Отвечает на вопрос «текстура не доехала или доехала, но не видна» фактом,
/// а не рассуждением. Диагностика по ассетам этого вопроса не закрывает:
/// материал мог быть подменен уже после постановки объекта.
/// </summary>
public static class SceneDiag
{
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/ColonyOurs.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();

        var roots = scene.GetRootGameObjects();
        sb.AppendLine($"корневых объектов: {roots.Length}");

        int shown = 0;
        foreach (var root in roots)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                var m = r.sharedMaterial;
                if (m == null) { sb.AppendLine($"{Path(r)} | МАТЕРИАЛА НЕТ"); shown++; continue; }

                string tex = "нет";
                if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null)
                    tex = m.GetTexture("_BaseMap").name;
                else if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null)
                    tex = m.GetTexture("_MainTex").name;

                var col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                        : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.clear;

                sb.AppendLine($"{Path(r)} | мат={m.name} | шейдер={m.shader.name} | карта={tex} | цвет={col}");
                if (++shown >= 40) break;
            }
            if (shown >= 40) break;
        }

        // Сводка по всей сцене: сколько рендереров вообще без карты цвета.
        int total = 0, noTex = 0, notUrp = 0;
        foreach (var root in roots)
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var m = r.sharedMaterial;
                if (m == null) continue;
                total++;
                if (!m.shader.name.StartsWith("Universal Render Pipeline/")) notUrp++;
                bool has = (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null)
                        || (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null);
                if (!has) noTex++;
            }

        sb.AppendLine();
        sb.AppendLine($"ИТОГО рендереров с материалом: {total}");
        sb.AppendLine($"  без карты цвета: {noTex}");
        sb.AppendLine($"  шейдер НЕ из URP: {notUrp}");

        // Главный вопрос: ЧТО именно идет без текстуры. Группируем по материалу,
        // иначе список из сотни строк ничего не объясняет.
        sb.AppendLine();
        sb.AppendLine("=== БЕЗ КАРТЫ ЦВЕТА, по материалам ===");
        var groups = new System.Collections.Generic.Dictionary<string, (int n, Color c, string sample)>();
        foreach (var root in roots)
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var m = r.sharedMaterial;
                if (m == null) continue;
                bool has = (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null)
                        || (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null);
                if (has) continue;

                var col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.clear;
                if (groups.TryGetValue(m.name, out var g)) groups[m.name] = (g.n + 1, g.c, g.sample);
                else groups[m.name] = (1, col, Path(r));
            }

        foreach (var kv in groups.OrderByDescending(k => k.Value.n))
            sb.AppendLine($"{kv.Value.n,4} x {kv.Key} | цвет={kv.Value.c} | пример: {kv.Value.sample}");

        File.WriteAllText("scene-diag.txt", sb.ToString());
        Debug.Log($"[scene-diag] рендереров {total}, без карты {noTex}, не-URP {notUrp}");
    }

    private static string Path(Renderer r)
    {
        var t = r.transform;
        var parts = new System.Collections.Generic.List<string>();
        while (t != null && parts.Count < 4) { parts.Add(t.name); t = t.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
