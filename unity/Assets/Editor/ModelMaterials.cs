using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Материалы моделей как ЯВНЫЕ ассеты, а не как побочный продукт импорта.
///
/// История вопроса, чтобы это не переделывали по кругу. FBX нашего конвейера
/// не несет описания материала вообще: импортер Unity подставляет стандартную
/// серую заглушку с именем Lit, а текстура остается лежать рядом на диске
/// непривязанной. Проверено тремя способами:
///   - materialLocation = External отработал без ошибок и не создал ни одного .mat;
///   - materialImportMode = ImportStandard дал заглушку Lit, серый 0.5;
///   - materialImportMode = ImportViaMaterialDescription дал ту же заглушку,
///     а OnPostprocessMaterial не вызвался ни разу (ноль записей в логе).
/// Именно поэтому в проекте раньше «привязывали текстуры руками» — и именно
/// поэтому привязка пропала при переходе в линейное пространство, который
/// переимпортировал библиотеку.
///
/// Вывод: на импортер полагаться нельзя. Материал создается нами, лежит в
/// репозитории отдельным файлом и назначается при постановке модели в сцену
/// (<see cref="ColonyOursBuilder"/>). Это же дает возможность красить корпуса в
/// разные цвета по ротации арт-библии, раздел 7.4 — со встроенным материалом
/// все здания обречены быть одного тона.
/// </summary>
public static class ModelMaterials
{
    public const string Dir = "Assets/OurAssets/Materials";
    private const string Root = "Assets/OurAssets";

    /// <summary>Материал модели, если он собран. Иначе null.</summary>
    public static Material For(string model)
        => AssetDatabase.LoadAssetAtPath<Material>($"{Dir}/{model}.mat");

    public static void Run()
    {
        Directory.CreateDirectory(Dir);
        AssetDatabase.Refresh();

        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) { Debug.LogError("[model-mat] шейдер URP/Lit не найден"); return; }

        var models = AssetDatabase.FindAssets("t:Model", new[] { Root })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".fbx"))
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(s => s)
            .ToArray();

        int made = 0;
        var noTex = new List<string>();

        foreach (var model in models)
        {
            var tex = FindTexture(model);
            if (tex == null) { noTex.Add(model); continue; }

            string path = $"{Dir}/{model}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(lit);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = lit;
            mat.SetTexture("_BaseMap", tex);
            // Тон задает текстура. Серый 0.5 заглушки глушил ее вдвое и был
            // прямой причиной выцветшего кадра.
            mat.SetColor("_BaseColor", Color.white);
            // Township-вид: матовая поверхность без металлического отблеска.
            mat.SetFloat("_Smoothness", 0.1f);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            made++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[model-mat] материалов собрано: {made} из {models.Length}");
        if (noTex.Count > 0)
            Debug.LogWarning($"[model-mat] БЕЗ ТЕКСТУРЫ ({noTex.Count}): {string.Join(", ", noTex)}");
    }

    private static Texture2D FindTexture(string model)
    {
        // Тот же порядок поиска, что и в ColonyOursBuilder.EnsureTextured:
        // сперва новая библиотека v2/, потом старая. Раньше здесь стоял
        // только старый путь, и для модели, у которой есть и v2-версия, и
        // одноимённая папка текстур в корне OurAssets, материал собирался бы
        // по СТАРОЙ картинке — тот же класс тихой порчи (текстура другой
        // развёртки), который уже один раз ловили и чинили в EnsureTextured.
        string dir = $"{Root}/v2/{model}_textures";
        if (!AssetDatabase.IsValidFolder(dir))
            dir = $"{Root}/{model}_textures";
        if (!AssetDatabase.IsValidFolder(dir)) return null;

        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { dir });
        // tex_0 — карта цвета конвейера, Image_0 — запасное имя части моделей.
        foreach (var pref in new[] { "tex_0", "Image_0" })
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileName(p).StartsWith(pref))
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }

        return guids.Length > 0
            ? AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guids[0]))
            : null;
    }
}
