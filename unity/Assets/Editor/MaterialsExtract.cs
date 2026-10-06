using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Вытаскивает материалы моделей из FBX в отдельные .mat и привязывает к ним
/// текстуру цвета.
///
/// Зачем это нужно, а не «привязать текстуру и забыть».
/// Материалы всех 36 моделей были ВСТРОЕНЫ в FBX (materialImportMode: 1,
/// ни одного .mat в проекте). Встроенный материал пересоздается импортером при
/// каждом переимпорте — а перевод в линейное пространство переимпортирует всё.
/// Ровно это и стерло ручную привязку текстур: в кадре постройки залиты плоским
/// цветом при живых .png рядом на диске. Пока материалы живут внутри FBX, любая
/// правка вида держится до следующего переимпорта и молча пропадает.
///
/// Побочная выгода: отдельные .mat — единственный способ покрасить корпуса в
/// разные цвета по ротации арт-библии (раздел 7.4). Со встроенным материалом
/// все здания обречены быть одного тона.
/// </summary>
public static class MaterialsExtract
{
    private const string Root = "Assets/OurAssets";
    private const string MatDir = Root + "/Materials";

    public static void Run()
    {
        Directory.CreateDirectory(MatDir);
        AssetDatabase.Refresh();

        var fbx = AssetDatabase.FindAssets("t:Model", new[] { Root })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".fbx"))
            .OrderBy(p => p)
            .ToArray();

        Debug.Log($"[mat-extract] моделей найдено: {fbx.Length}");

        int extracted = 0, bound = 0, noTex = 0;
        var missing = new List<string>();

        foreach (var path in fbx)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue;

            if (importer.materialLocation != ModelImporterMaterialLocation.External)
            {
                importer.materialLocation = ModelImporterMaterialLocation.External;
                importer.SaveAndReimport();
                extracted++;
            }
        }

        AssetDatabase.Refresh();

        // Привязка текстуры делается ОТДЕЛЬНЫМ проходом, после того как все
        // переимпорты отработали: материал, созданный импортером в этом же
        // цикле, до Refresh еще не виден как ассет.
        foreach (var path in fbx)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var tex = FindTexture(name);
            if (tex == null)
            {
                noTex++;
                missing.Add(name);
                continue;
            }

            foreach (var mat in MaterialsOf(path))
            {
                if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") == null)
                {
                    mat.SetTexture("_BaseMap", tex);
                    // Базовый цвет 0.8 серого приглушал текстуру на четверть.
                    // Тон задает текстура, материал ее не тонирует.
                    mat.SetColor("_BaseColor", Color.white);
                    mat.SetFloat("_Smoothness", 0.1f);
                    mat.SetFloat("_Metallic", 0f);
                    EditorUtility.SetDirty(mat);
                    bound++;
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[mat-extract] вынесено моделей: {extracted}, привязок текстуры: {bound}");
        if (noTex > 0)
            Debug.LogWarning($"[mat-extract] БЕЗ ТЕКСТУРЫ ({noTex}): {string.Join(", ", missing)}");
    }

    /// <summary>Материалы модели: и вынесенные в .mat, и оставшиеся внутри.</summary>
    private static IEnumerable<Material> MaterialsOf(string fbxPath)
    {
        string name = Path.GetFileNameWithoutExtension(fbxPath);
        var seen = new HashSet<Material>();

        foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { MatDir }))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
            if (m != null) seen.Add(m);
        }

        // Материалы могли лечь и в папку Materials рядом с моделью.
        string near = Path.GetDirectoryName(fbxPath).Replace('\\', '/') + "/Materials";
        if (AssetDatabase.IsValidFolder(near))
            foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { near }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                if (m != null) seen.Add(m);
            }

        // Привязываем только те, что реально стоят на рендерерах этой модели —
        // иначе одноименный материал «metal» соседа получит чужую текстуру.
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (go == null) yield break;

        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m != null) yield return m;

        // Подстраховка: имя материала совпадает с именем модели.
        foreach (var m in seen)
            if (m.name == name) yield return m;
    }

    private static Texture2D FindTexture(string modelName)
    {
        string dir = $"{Root}/{modelName}_textures";
        if (!AssetDatabase.IsValidFolder(dir)) return null;

        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { dir });
        if (guids.Length == 0) return null;

        // Предпочитаем tex_0: это карта цвета конвейера. Image_0 — запасное имя.
        foreach (var pref in new[] { "tex_0", "Image_0" })
        {
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileName(p).StartsWith(pref))
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }
}
