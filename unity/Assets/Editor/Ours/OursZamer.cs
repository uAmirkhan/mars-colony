using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Замер: где у постройки точка опоры, а где её видимая масса.
///
/// Появился после того, как перенесённый ангар не нашёлся в кадре на новом
/// месте. Позиция трансформа и центр того, что реально рисуется, могут не
/// совпадать: у сведённых и перепивоченных моделей геометрия уезжает от точки
/// опоры на десятки метров. Пока это не замерено, любой перенос по координате
/// трансформа — стрельба вслепую, и проверка пересечений врёт вместе с ним.
/// </summary>
public static class OursZamer
{
    private const string Scena = "Assets/Scenes/ColonyOurs 2.unity";

    public static void Vypolnit()
    {
        var scena = EditorSceneManager.OpenScene(Scena, OpenSceneMode.Single);
        var sb = new StringBuilder();
        sb.AppendLine("объект                     опора(X,Z)      масса(X,Z)      снос   размер(X,Y,Z)");

        foreach (var go in scena.GetRootGameObjects().OrderBy(g => g.name))
        {
            if (!Interesno(go.name)) continue;
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) { sb.AppendLine($"{go.name,-26} БЕЗ РЕНДЕРЕРОВ"); continue; }
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);

            var o = go.transform.position;
            float snos = Vector2.Distance(new Vector2(o.x, o.z), new Vector2(b.center.x, b.center.z));
            sb.AppendLine($"{go.name,-26} ({o.x,6:F1},{o.z,6:F1})  ({b.center.x,6:F1},{b.center.z,6:F1})"
                        + $"  {snos,5:F1}  {b.size.x,5:F1} {b.size.y,5:F1} {b.size.z,5:F1}"
                        + $"  рендереров {rs.Length}");
        }
        Debug.Log("[ours] замер опор\n" + sb);
    }

    private static bool Interesno(string imya)
    {
        string n = Regex.Replace(imya, @" \(\d+\)$", "");
        string[] da = { "sklad-", "angar-", "zavod-", "zhiloy-", "kupol-", "modul-",
                        "burovaya-", "ploshchadka-", "shattl-" };
        return da.Any(p => n.StartsWith(p));
    }
}
