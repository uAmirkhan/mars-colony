using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Съёмка сцены ColonyOurs 2 набором кадров — тем же набором и с тех же точек,
/// чтобы «до» и «после» можно было класть рядом.
///
/// Поле зрения 32 градуса взято у родной камеры сцены, а не назначено на глаз:
/// иначе кадр не совпадает с тем, что увидит игрок, и правки делаются вслепую.
/// </summary>
public static class OursSnimki
{
    private const string Scena = "Assets/Scenes/ColonyOurs 2.unity";

    private static readonly (string imya, Vector3 poz, Vector3 cel)[] Vidy =
    {
        // Точка родной камеры сцены. Отодвигать дальше нельзя: у сцены плотный
        // туман, и со ста метров он съедает весь цвет — кадр выходит
        // одноцветно-коричневым, и по нему нельзя судить ни о чём.
        ("01-obshchiy",       new Vector3(41.8f, 47.1f, -37f), new Vector3(  2f, 0f,   6f)),
        ("03-zhilyo",         new Vector3(  8f, 26f, -34f), new Vector3(-14f, 0f,  -3f)),
        ("04-centr",          new Vector3( 26f, 26f, -22f), new Vector3(  0f, 0f,   6f)),
        ("05-promzona",       new Vector3( 52f, 28f, -18f), new Vector3( 18f, 0f,   6f)),
        ("06-karier",         new Vector3( 48f, 26f,  16f), new Vector3( 13f, 0f,  32f)),
        ("07-kosmoport",      new Vector3( 22f, 24f, -54f), new Vector3( -4f, 0f, -23f)),
        ("08-lyod",           new Vector3(  6f, 30f, -14f), new Vector3(-20f, 0f,  22f)),
        ("09-sklad-sever",    new Vector3( 26f, 22f,  26f), new Vector3( -1f, 0f,  49f)),
        ("10-burovaya-zapad", new Vector3(-10f, 22f, -16f), new Vector3(-36f, 0f,   4f)),
    };

    public static void Snyat()
    {
        EditorSceneManager.OpenScene(Scena, OpenSceneMode.Single);

        string papka = Arg("-papka", "kadry-ours");
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../mars-colony/loop/scene-v2/" + papka));
        Directory.CreateDirectory(dir);

        var go = new GameObject("__obzor");
        var cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 32f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 500f;
        var dop = go.AddComponent<UniversalAdditionalCameraData>();
        dop.renderPostProcessing = true;
        dop.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        int w = 1600, h = 900;
        cam.aspect = (float)w / h;
        foreach (var v in Vidy)
        {
            go.transform.position = v.poz;
            go.transform.LookAt(v.cel);
            Snimok(cam, w, h, Path.Combine(dir, v.imya + ".png"));
        }

        // План сверху: крен задаётся явно, LookAt тут не годится — направление
        // взгляда совпадает с мировым «вверх», и крен получается произвольный.
        go.transform.position = new Vector3(2f, 150f, 8f);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.orthographic = true;
        cam.orthographicSize = 52f;
        cam.aspect = 1f;
        Snimok(cam, 1800, 1800, Path.Combine(dir, "02-sverhu.png"));

        Object.DestroyImmediate(go);
        Debug.Log("[ours] кадры сняты в " + dir);
    }

    private static void Snimok(Camera cam, int w, int h, string put)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        rt.Create();
        cam.targetTexture = rt;

        var zapros = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, zapros))
            RenderPipeline.SubmitRenderRequest(cam, zapros);
        else
            cam.Render();

        var byl = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = byl;
        cam.targetTexture = null;

        File.WriteAllBytes(put, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
    }

    private static string Arg(string key, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
        return def;
    }
}
