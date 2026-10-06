using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Правка настроек проекта, без которых сцена не может выглядеть верно.
///
/// Это НЕ настройка сцены, а настройка конвейера. Вынесено отдельно и названо
/// прямо, чтобы правка не считалась молчаливой.
/// </summary>
public static class V2Nastroyki
{
    /// <summary>
    /// Прописывает рендереру PostProcessData.
    ///
    /// Без нее URP не запускает постобработку ВООБЩЕ: тонмаппинг, насыщенность,
    /// контраст, bloom и виньетка не применяются, сколько их ни настраивай.
    /// В `MarsUrpRenderer.asset` стояло `postProcessData: {fileID: 0}`.
    ///
    /// Отказ тихий на четырех уровнях сразу: renderPostProcessing на камере
    /// True, Volume в сцене есть, профиль заполнен и содержит четыре компонента,
    /// ошибок в логе ноль. Проверка каждого из трех верхних уровней проходит, а
    /// картинка не меняется ни на пиксель.
    /// </summary>
    public static void PochinitPostObrabotku()
    {
        var rd = AssetDatabase.FindAssets("t:UniversalRendererData")
                 .Select(g => AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                     AssetDatabase.GUIDToAssetPath(g)))
                 .Where(a => a != null).ToList();

        if (rd.Count == 0) { Debug.LogError("[v2] рендерер URP не найден"); return; }

        var ppdPath = AssetDatabase.FindAssets("t:PostProcessData")
                      .Select(AssetDatabase.GUIDToAssetPath)
                      .FirstOrDefault();
        if (string.IsNullOrEmpty(ppdPath))
        {
            Debug.LogError("[v2] PostProcessData не найдена в проекте и пакетах");
            return;
        }
        var ppd = AssetDatabase.LoadAssetAtPath<PostProcessData>(ppdPath);
        Debug.Log($"[v2] PostProcessData: {ppdPath}");

        foreach (var r in rd)
        {
            var so = new SerializedObject(r);
            var prop = so.FindProperty("postProcessData");
            if (prop == null) { Debug.LogWarning($"[v2] нет поля у {r.name}"); continue; }
            bool bylo = prop.objectReferenceValue != null;
            prop.objectReferenceValue = ppd;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(r);
            Debug.Log($"[v2] {r.name}: postProcessData было {(bylo ? "задано" : "ПУСТО")}, "
                    + "стало задано");
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
