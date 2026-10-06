using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Приведение текстур к экранной нужде.
///
/// То же правило, что и с треугольниками: размер выбирается по тому, сколько
/// пикселей объект занимает в кадре, а не по тому, сколько отдал генератор.
/// Постройка высотой 7 м занимает 130 пикселей при 17.9 пикселя на метр.
/// Текстура 2048 на 2048 на такой объект это шестнадцатикратный запас.
///
/// Замер до правки: 23 текстуры моделей по 2048 давали 122 МБ в собранном
/// билде из 221 МБ общего веса.
/// </summary>
public static class V2Tekstury
{
    public static void Privesti()
    {
        int modeli = Razmer("Assets/OurAssets/v3", 512)
                   + Razmer("Assets/OurAssets/v3lite", 512)
                   + Razmer("Assets/OurAssets/v3micro", 256)
                   + Razmer("Assets/OurAssets/v2", 512);
        int zemlya = Razmer("Assets/OurAssets/ground2", 1024)
                   + Razmer("Assets/OurAssets/ground", 1024);

        AssetDatabase.Refresh();
        Debug.Log($"[v2] приведено текстур: моделей {modeli}, земли {zemlya}");
    }

    private static int Razmer(string papka, int max)
    {
        if (!AssetDatabase.IsValidFolder(papka)) return 0;
        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { papka });
        int n = 0;
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) continue;
            if (ti.maxTextureSize == max && ti.textureCompression == TextureImporterCompression.Compressed)
                continue;
            ti.maxTextureSize = max;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.compressionQuality = 50;
            ti.mipmapEnabled = true;
            ti.streamingMipmaps = true;
            ti.SaveAndReimport();
            n++;
        }
        return n;
    }
}
