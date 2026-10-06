using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Опыт: почему общий кадр выходит одноцветно-коричневым.
///
/// С точки родной камеры сцены живой редактор даёт полный цвет, а батч — сплошную
/// сепию. Гипотез две: туман сцены и крупные полупрозрачные полотна поверх карты
/// (`tonalnye_pyatna`, `makro_tsvet`, `zalivka`). Гадать дороже, чем снять один и
/// тот же кадр четырьмя способами и посмотреть, какой из них вернёт цвет.
/// </summary>
public static class OursProbaTumana
{
    private const string Scena = "Assets/Scenes/ColonyOurs 2.unity";

    public static void Vypolnit()
    {
        EditorSceneManager.OpenScene(Scena, OpenSceneMode.Single);
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../mars-colony/loop/scene-v2/kadry-ours/proba"));
        Directory.CreateDirectory(dir);

        var go = new GameObject("__proba");
        var cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 32f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 500f;
        var dop = go.AddComponent<UniversalAdditionalCameraData>();
        dop.renderPostProcessing = true;
        go.transform.position = new Vector3(41.8f, 47.1f, -37f);
        go.transform.LookAt(new Vector3(2f, 0f, 6f));

        bool bylTuman = RenderSettings.fog;
        var pyatna = GameObject.Find("tonalnye_pyatna");
        var zalivka = GameObject.Find("zalivka");
        var makro = GameObject.Find("makro_tsvet");

        Debug.Log($"[ours] туман={RenderSettings.fog} режим={RenderSettings.fogMode} "
                + $"плотность={RenderSettings.fogDensity} цвет={RenderSettings.fogColor} "
                + $"начало={RenderSettings.fogStartDistance} конец={RenderSettings.fogEndDistance}");

        Snyat(cam, Path.Combine(dir, "a-kak-est.png"));

        RenderSettings.fog = false;
        Snyat(cam, Path.Combine(dir, "b-bez-tumana.png"));
        RenderSettings.fog = bylTuman;

        // Полотна поверх карты: гасим и смотрим, вернётся ли цвет.
        if (pyatna != null) pyatna.SetActive(false);
        if (zalivka != null) zalivka.SetActive(false);
        if (makro != null) makro.SetActive(false);
        Snyat(cam, Path.Combine(dir, "v-bez-polotеn.png"));

        RenderSettings.fog = false;
        Snyat(cam, Path.Combine(dir, "g-bez-vsego.png"));
        RenderSettings.fog = bylTuman;

        if (pyatna != null) pyatna.SetActive(true);
        if (zalivka != null) zalivka.SetActive(true);
        if (makro != null) makro.SetActive(true);

        Object.DestroyImmediate(go);
        // Сцену НЕ сохраняем: это опыт, а не правка.
        Debug.Log("[ours] проба снята в " + dir);
    }

    private static void Snyat(Camera cam, string put)
    {
        int w = 1200, h = 675;
        cam.aspect = (float)w / h;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4; rt.Create();
        cam.targetTexture = rt;
        var z = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, z)) RenderPipeline.SubmitRenderRequest(cam, z);
        else cam.Render();
        var byl = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = byl; cam.targetTexture = null;
        File.WriteAllBytes(put, tex.EncodeToPNG());
        Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
    }
}
