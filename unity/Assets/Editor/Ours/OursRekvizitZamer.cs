using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Замер реквизита: длина секции трубы, размер поддона, и кто где стоит.
///
/// Нужен перед тем, как складывать трубы в магистраль: чтобы секции стыковались
/// встык, надо знать их длину в метрах, а не подбирать шаг на глаз. Подбор на
/// глаз даёт либо разрывы, либо наложение секций друг на друга — и то и другое
/// читается как брошенные палки, а не как трубопровод.
/// </summary>
public static class OursRekvizitZamer
{
    private const string Scena = "Assets/Scenes/ColonyOurs 2.unity";

    public static void Vypolnit()
    {
        var scena = EditorSceneManager.OpenScene(Scena, OpenSceneMode.Single);
        var sb = new StringBuilder();

        foreach (var imya in new[] { "truba-na-kozlakh", "ballony-na-poddone", "tsisterna" })
        {
            sb.AppendLine($"\n=== {imya} ===");
            foreach (var go in scena.GetRootGameObjects())
            {
                if (Regex.Replace(go.name, @" \(\d+\)$", "") != imya) continue;
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) { sb.AppendLine("   пустая оболочка"); continue; }
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.AppendLine($"   масса ({b.center.x,6:F1},{b.center.z,6:F1}) низ {b.min.y,5:F2}"
                            + $"  размер {b.size.x,5:F2} x {b.size.y,5:F2} x {b.size.z,5:F2}"
                            + $"  угол {go.transform.eulerAngles.y,4:F0}");
            }
        }
        Debug.Log("[ours] замер реквизита" + sb);
    }
}
