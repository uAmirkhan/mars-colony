using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Диагностика материалов моделей: какой шейдер, привязана ли карта цвета.
/// Отвечает на вопрос, почему постройки в кадре залиты плоским цветом.
/// </summary>
public static class MatDiag
{
    public static void Run()
    {
        var sb = new StringBuilder();
        var names = new[]
        {
            "zhiloy-kupol", "sklad-angar", "zavod-pishchevoy",
            "kupol-grib", "shuttle", "dekor-04",
        };

        foreach (var n in names)
        {
            string path = $"Assets/OurAssets/{n}.fbx";
            var subs = AssetDatabase.LoadAllAssetsAtPath(path);
            var mats = subs.OfType<Material>().ToArray();
            sb.AppendLine($"--- {n}: материалов {mats.Length} ---");
            foreach (var m in mats)
            {
                string shader = m.shader != null ? m.shader.name : "НЕТ ШЕЙДЕРА";
                var baseMap = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                var mainTex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                var col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                        : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.clear;
                sb.AppendLine($"  {m.name} | шейдер: {shader}");
                sb.AppendLine($"    _BaseMap: {(baseMap != null ? baseMap.name : "ПУСТО")}" +
                              $" | _MainTex: {(mainTex != null ? mainTex.name : "ПУСТО")}" +
                              $" | цвет: {col}");
            }

            var texDir = $"Assets/OurAssets/{n}_textures";
            var found = AssetDatabase.FindAssets("t:Texture2D", new[] { texDir });
            sb.AppendLine($"  текстур на диске в {n}_textures: {found.Length}");
            foreach (var g in found.Take(3))
                sb.AppendLine($"    {AssetDatabase.GUIDToAssetPath(g)}");
        }

        System.IO.File.WriteAllText("mat-diag.txt", sb.ToString());
        Debug.Log("[mat-diag] записано в mat-diag.txt");
    }
}
