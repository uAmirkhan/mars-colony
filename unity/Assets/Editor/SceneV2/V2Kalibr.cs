using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Контрольный щит: объекты с ЗАВЕДОМО известным видом в одном кадре.
///
/// Нужен, чтобы отделить «сцена собрана неверно» от «снимок врет». Если на щите
/// пурпурный квадрат вышел пурпурным, а текстурный - серым, значит сломана
/// именно выборка текстуры, а не материал, не свет и не снимок.
/// </summary>
public static class V2Kalibr
{
    public static void Build()
    {
        string outPath = Arg("-out", "../mars-colony/loop/scene-v2/kadry/kalibr.png");

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var tex = V2Lib.FindTexture("angar-s-panelyami");
        string texInfo = tex == null
            ? "НЕ НАЙДЕНА"
            : $"{tex.name} {tex.width}x{tex.height} формат {tex.format} мипов {tex.mipmapCount}";
        Debug.Log($"[v2] текстура ангара: {texInfo}");
        Debug.Log($"[v2] предел мипов в качестве: {QualitySettings.globalTextureMipmapLimit}, " +
                  $"уровень качества: {QualitySettings.GetQualityLevel()} " +
                  $"из {QualitySettings.names.Length}");

        // 1 пурпур без света  2 текстура  3 белый  4 зеленый  5 текстура без света
        Quad(-6f, V2Lib.NewUnlit(Color.magenta), "1-purpur-unlit");
        Quad(-3f, V2Lib.NewLit(Color.white, tex), "2-tekstura-lit");
        Quad(0f, V2Lib.NewLit(Color.white), "3-belyy-lit");
        Quad(3f, V2Lib.NewLit(Color.green), "4-zelenyy-lit");
        Quad(6f, V2Lib.NewUnlit(Color.white, tex), "5-tekstura-unlit");

        var sun = new GameObject("Solnce").AddComponent<UnityEngine.Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.2f;
        sun.transform.rotation = Quaternion.Euler(50f, 180f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.35f);

        var go = new GameObject(V2Lib.CAM_NAME);
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 3f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.transform.rotation = Quaternion.identity;

        V2Lib.Shoot(cam, Path.GetFullPath(outPath));
    }

    private static void Quad(float x, Material m, string name)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        q.transform.position = new Vector3(x, 0f, 0f);
        q.transform.localScale = Vector3.one * 2.6f;
        q.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    private static string Arg(string key, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == key) return a[i + 1];
        return def;
    }
}
