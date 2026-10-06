using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Возвращает материалы моделей внутрь FBX и переимпортирует библиотеку,
/// чтобы отработал OurAssetsPostprocessor и привязал текстуры.
///
/// Режим External был выставлен предыдущей попыткой выноса материалов наружу;
/// он не создал ни одного .mat и оставил модели без внятного источника
/// материала. Возврат в InPrefab — это откат неудачной попытки, а не выбор
/// архитектуры: архитектура теперь в постпроцессоре.
/// </summary>
public static class ReimportModels
{
    public static void Run()
    {
        var paths = AssetDatabase.FindAssets("t:Model", new[] { "Assets/OurAssets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".fbx"))
            .OrderBy(p => p)
            .ToArray();

        int n = 0;
        foreach (var p in paths)
        {
            if (!(AssetImporter.GetAtPath(p) is ModelImporter im)) continue;
            im.materialLocation = ModelImporterMaterialLocation.InPrefab;
            // ImportStandard — легаси-путь встроенного конвейера: в URP он дает
            // не материал модели, а стандартную серую заглушку с именем Lit, и
            // постпроцессор при этом не вызывается вовсе. Путь через описание
            // материала создает материал под активный конвейер и вызывает
            // OnPostprocessMaterial, где и происходит привязка текстуры.
            im.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            im.materialLocation = ModelImporterMaterialLocation.InPrefab;
            im.SaveAndReimport();
            n++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[reimport] переимпортировано моделей: {n}");
    }
}
