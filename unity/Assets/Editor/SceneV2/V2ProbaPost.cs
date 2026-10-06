using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Опыт: доезжает ли постобработка до снимка.
/// Снимает один и тот же кадр с включенным и выключенным Volume.
/// Если картинки совпали - постобработки в снимке нет, и все настройки
/// тонмаппинга и насыщенности уходят в никуда молча.
/// </summary>
public static class V2ProbaPost
{
    public static void Proba()
    {
        string outPath = Arg("-out", "../mars-colony/loop/scene-v2/kadry/proba.png");
        EditorSceneManager.OpenScene(V2Lib.SCENE_PATH, OpenSceneMode.Single);
        var cam = V2Lib.OurCamera();
        var vol = Object.FindFirstObjectByType<Volume>();

        var d = cam.GetUniversalAdditionalCameraData();
        Debug.Log($"[v2] renderPostProcessing на камере: {d.renderPostProcessing}");
        Debug.Log($"[v2] Volume в сцене: {(vol == null ? "НЕТ" : vol.name)}, " +
                  $"профиль {(vol != null && vol.sharedProfile != null ? "есть" : "НЕТ")}");

        if (vol != null)
        {
            var ca = vol.sharedProfile.components.Find(c => c is ColorAdjustments)
                     as ColorAdjustments;
            Debug.Log($"[v2] насыщенность в профиле: " +
                      $"{(ca == null ? "компонента нет" : ca.saturation.value.ToString())}, " +
                      $"override {(ca == null ? "-" : ca.saturation.overrideState.ToString())}");

            V2Lib.Shoot(cam, Path.GetFullPath(outPath).Replace(".png", "-post-on.png"));

            // снимаем Volume насовсем: снятие галки стек может не пересчитать
            Object.DestroyImmediate(vol.gameObject);
            V2Lib.Shoot(cam, Path.GetFullPath(outPath).Replace(".png", "-post-off.png"));
        }
        else Debug.LogError("[v2] Volume в сцене НЕ НАЙДЕН");
    }

    private static string Arg(string key, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
        return def;
    }
}
