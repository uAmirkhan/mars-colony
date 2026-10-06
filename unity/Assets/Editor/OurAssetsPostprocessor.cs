using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Привязывает текстуру цвета к материалу модели В МОМЕНТ ИМПОРТА.
///
/// Почему именно так, а не «привязать один раз руками».
/// Материалы моделей встроены в FBX и пересоздаются импортером при каждом
/// переимпорте. Перевод проекта в линейное пространство переимпортировал всё —
/// и стер ручную привязку, сделанную раньше: в кадре постройки оказались залиты
/// плоским цветом при живых .png рядом на диске, без единой ошибки в логе.
/// Попытка вынести материалы наружу (materialLocation = External) отработала без
/// ошибок и не создала ни одного .mat.
///
/// Привязка на импорте — единственный вариант, который не отваливается: она
/// выполняется тем же процессом, который материал и создает.
///
/// Конвейер кладет текстуру в Assets/OurAssets/<имя-модели>_textures/.
/// Имя карты цвета — tex_0_png.png, у части моделей Image_0_png.png.
/// </summary>
public class OurAssetsPostprocessor : AssetPostprocessor
{
    private const string Root = "Assets/OurAssets/";

    private void OnPostprocessMaterial(Material material)
    {
        if (!assetPath.StartsWith(Root)) return;

        string model = Path.GetFileNameWithoutExtension(assetPath);
        var tex = FindTexture(model);
        if (tex == null)
        {
            Debug.LogWarning($"[our-assets] текстура не найдена для {model} — материал останется плоским");
            return;
        }

        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", tex);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", tex);

        // Тон задает текстура. Серый 0.8 в базовом цвете приглушал ее на четверть
        // и был одной из причин, почему сцена читалась выцветшей.
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);

        // Township-вид: матовые поверхности, никакого металлического отблеска.
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
    }

    private static Texture2D FindTexture(string model)
    {
        string dir = $"{Root}{model}_textures";
        if (!Directory.Exists(dir)) return null;

        foreach (var pref in new[] { "tex_0", "Image_0" })
        {
            foreach (var f in Directory.GetFiles(dir, pref + "*.png"))
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(f.Replace('\\', '/'));
                if (t != null) return t;
            }
        }

        foreach (var f in Directory.GetFiles(dir, "*.png"))
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(f.Replace('\\', '/'));
            if (t != null) return t;
        }
        return null;
    }
}
