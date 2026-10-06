using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Перепись библиотеки: что есть, сколько весит, какого цвета.
///
/// Нужна до раскладки, а не после. Плотность кадра упирается в бюджет
/// треугольников, и знать цену объекта надо ДО того, как их расставлено двести.
/// </summary>
public static class V2Perepis
{
    public static void Run()
    {
        string outPath = Arg("-out", "../mars-colony/loop/scene-v2/perepis.csv");

        var dirs = new[] { "Assets/OurAssets/v3", "Assets/OurAssets/v2", "Assets/OurAssets" };
        var rows = new List<(string sloy, string imya, int tris, float vys,
                             float dlina, bool tex)>();

        foreach (var dir in dirs)
        {
            foreach (var p in Directory.GetFiles(dir, "*.fbx", SearchOption.TopDirectoryOnly))
            {
                string path = p.Replace('\\', '/');
                string imya = Path.GetFileNameWithoutExtension(path);
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (src == null) continue;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;

                int tris = 0;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;

                float vys = 0f, dlina = 0f;
                if (V2Lib.WorldBounds(go, out var b))
                {
                    vys = b.size.y;
                    dlina = Mathf.Max(b.size.x, b.size.z);
                }

                rows.Add((Path.GetFileName(dir), imya, tris, vys, dlina,
                          V2Lib.FindTexture(imya) != null));
                Object.DestroyImmediate(go);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("sloy;imya;tris;vysota_m;dlina_m;est_tekstura");
        foreach (var r in rows.OrderByDescending(r => r.tris))
            sb.AppendLine($"{r.sloy};{r.imya};{r.tris};{r.vys:F2};{r.dlina:F2};{r.tex}");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        File.WriteAllText(Path.GetFullPath(outPath), sb.ToString());

        // сводка в лог
        var byLayer = rows.GroupBy(r => r.sloy);
        var itog = new StringBuilder();
        itog.AppendLine($"всего моделей: {rows.Count}");
        foreach (var g in byLayer)
            itog.AppendLine($"  {g.Key,-12} {g.Count(),3} шт, треугольников: " +
                            $"мин {g.Min(r => r.tris),6}  медиана {Mediana(g.Select(r => r.tris)),6}  " +
                            $"макс {g.Max(r => r.tris),6}  сумма {g.Sum(r => r.tris),8}");
        itog.AppendLine($"  без текстуры: {rows.Count(r => !r.tex)}");
        itog.AppendLine();
        itog.AppendLine("Самые тяжелые:");
        foreach (var r in rows.OrderByDescending(r => r.tris).Take(8))
            itog.AppendLine($"  {r.imya,-32} {r.tris,7} тр");
        itog.AppendLine("Самые легкие:");
        foreach (var r in rows.OrderBy(r => r.tris).Take(8))
            itog.AppendLine($"  {r.imya,-32} {r.tris,7} тр");

        Debug.Log("[v2] перепись:\n" + itog);
        File.WriteAllText(Path.GetFullPath(outPath).Replace(".csv", "-svodka.txt"),
                          itog.ToString());
    }

    private static int Mediana(IEnumerable<int> xs)
    {
        var a = xs.OrderBy(x => x).ToArray();
        return a.Length == 0 ? 0 : a[a.Length / 2];
    }

    private static string Arg(string key, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == key) return a[i + 1];
        return def;
    }
}
