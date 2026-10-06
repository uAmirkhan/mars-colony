using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Снимок сцены v2. Отдельная точка входа: открывает СОХРАНЕННУЮ сцену с диска
/// и снимает то, что там лежит. Строитель сюда не вызывается.
///
/// Смысл разделения: в прошлом заходе кадр снимался тем же методом, который
/// строил сцену, поэтому одна ошибка постановки портила и сцену, и отчет о ней.
/// </summary>
public static class V2Shot
{
    public static void Shoot()
    {
        string outPath = Arg("-out", "../mars-colony/loop/scene-v2/kadry/kadr.png");

        var scene = EditorSceneManager.OpenScene(V2Lib.SCENE_PATH, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"[v2] сцена не открылась: {V2Lib.SCENE_PATH}");
            EditorApplication.Exit(2);
            return;
        }

        var cam = V2Lib.OurCamera();
        if (cam == null)
        {
            Debug.LogError($"[v2] в сцене нет камеры с именем {V2Lib.CAM_NAME}");
            EditorApplication.Exit(3);
            return;
        }

        // Контроль «то ли я вообще смотрю»: в кадре обязана быть калибровочная
        // фигура. Нет ее - снимок молча показал бы не ту сцену.
        var kalibr = GameObject.Find("KALIBR-figura-1m80");
        if (kalibr == null || !V2Lib.WorldBounds(kalibr, out var kb))
        {
            Debug.LogError("[v2] КАЛИБРОВКИ НЕТ В СЦЕНЕ");
            EditorApplication.Exit(4);
            return;
        }

        // Присутствие в сцене это не присутствие в кадре. Проход 0 снял чужой
        // камерой сцену, где калибровка была - и в кадр не попала.
        var vr = V2Lib.ViewportRect(cam, kb);
        bool vkadre = vr.xMin > -0.02f && vr.xMax < 1.02f &&
                      vr.yMin > -0.02f && vr.yMax < 1.02f;
        float pxH = vr.height * V2Lib.SHOT_H;
        if (!vkadre)
        {
            Debug.LogError($"[v2] КАЛИБРОВКА ВНЕ КАДРА: viewport {vr}. Снимок не доверять.");
            EditorApplication.Exit(5);
            return;
        }
        V2Lib.Log($"ЛИНЕЙКА: фигура {V2Lib.FIGURA_ROST} м = {pxH:F1} пикселей по высоте кадра "
                + $"({vr.height * 100f:F2}% кадра). Метр = {pxH / V2Lib.FIGURA_ROST:F1} пикс.");

        var gs = UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching;
        V2Lib.Log($"SRP Batcher на входе: {gs}");

        // сколько РАЗНЫХ материалов реально висит на объектах кадра
        var mats = new System.Collections.Generic.HashSet<Material>();
        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            foreach (var m in mr.sharedMaterials) if (m != null) mats.Add(m);
        V2Lib.Log($"разных материалов в сцене: {mats.Count}");
        foreach (var m in mats)
        {
            var t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
            V2Lib.Log($"   мат {m.name} | {m.shader.name} | тек {(t == null ? "нет" : t.name)} " +
                      $"| ассет {AssetDatabase.Contains(m)}");
        }

        // Разворот камеры для проверки облета. Сцена строилась с расчетом, что
        // облет возможен: земля уходит за кадр во все стороны, у объектов нет
        // спины. Это утверждение до сих пор не было проверено ни одним кадром
        // под другим углом - проверяем.
        string yawArg = Arg("-yaw", "");
        if (!string.IsNullOrEmpty(yawArg) && float.TryParse(yawArg,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float yaw))
        {
            V2Lib.AimCamera(cam, Vector3.zero, yaw, V2Lib.CAM_PITCH);
            V2Lib.Log($"облет: разворот камеры {yaw} градусов");
        }

        // Все стороны одним запуском: поднимать Unity ради каждого угла дорого.
        string krug = Arg("-krug", "");
        if (!string.IsNullOrEmpty(krug))
        {
            foreach (var t in krug.Split(','))
            {
                if (!float.TryParse(t, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float y))
                    continue;
                V2Lib.AimCamera(cam, Vector3.zero, y, V2Lib.CAM_PITCH);
                V2Lib.Shoot(cam, Path.GetFullPath(outPath)
                                     .Replace(".png", $"-{(int)y}.png"));
            }
            return;
        }

        V2Lib.Shoot(cam, Path.GetFullPath(outPath));
    }

    private static string Arg(string key, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == key) return a[i + 1];
        return def;
    }
}
