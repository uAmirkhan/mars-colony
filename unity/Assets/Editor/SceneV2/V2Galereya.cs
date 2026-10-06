using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Галерея построек: ряд моделей с подписями габаритов. Нужна, чтобы увидеть
/// глазами то, что числа описывают неоднозначно - например не легла ли модель
/// на бок после смены осей при экспорте.
/// </summary>
public static class V2Galereya
{
    public static void Postroit()
    {
        string outPath = Arg("-out", "../mars-colony/loop/scene-v2/kadry/galereya.png");
        string spisok = Arg("-modeli",
            "zhiloy-kupol,zhiloy-barak,zhiloy-bashnya,zavod-pishchevoy,"
          + "sklad-angar,sklad-bunkery,kupol-geodezicheskiy,kupol-tunnel,"
          + "stantsiya-atmosfernaya,fabrika-tekstilnaya,burovaya-02,ploshchadka-shattla");
        var imena = spisok.Split(',');

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var pol = GameObject.CreatePrimitive(PrimitiveType.Plane);
        pol.transform.localScale = Vector3.one * 40f;
        pol.GetComponent<MeshRenderer>().sharedMaterial =
            V2Lib.NewLit(new Color(0.62f, 0.60f, 0.58f), null, 0.05f, imya: "gal-pol");

        int kol = 4;
        float shag = 16f;
        for (int i = 0; i < imena.Length; i++)
        {
            float x = (i % kol - (kol - 1) * 0.5f) * shag;
            float z = -(i / kol) * shag;
            // Разворот можно задать списком: нужен, когда у модели есть
            // лицевая сторона и надо найти, при каком угле она смотрит в кадр.
            string yawSpisok = Arg("-yaw", "");
            float yaw = 30f;
            if (!string.IsNullOrEmpty(yawSpisok))
            {
                var yy = yawSpisok.Split(',');
                float.TryParse(yy[i % yy.Length],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out yaw);
            }
            var go = V2Lib.Place(imena[i].Trim(), new Vector2(x, z), 6f, yaw);
            if (go == null) { Debug.LogWarning($"[v2] нет модели {imena[i]}"); continue; }
            V2Lib.WorldBounds(go, out var b);
            Debug.Log($"[v2] ГАБАРИТ {imena[i],-24} X{b.size.x,6:F2} Y{b.size.y,6:F2} "
                    + $"Z{b.size.z,6:F2}  отношение план/высота {Mathf.Max(b.size.x, b.size.z) / b.size.y:F2}");
        }

        var sun = new GameObject("Solnce").AddComponent<UnityEngine.Light>();
        sun.type = LightType.Directional; sun.intensity = 1.4f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(45f, 200f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.47f);

        var cam = V2Lib.MakeCamera(new Vector3(0f, 0f, -16f));
        cam.orthographicSize = 26f;
        cam.backgroundColor = new Color(0.35f, 0.36f, 0.40f);
        V2Lib.Shoot(cam, Path.GetFullPath(outPath));
    }

    private static string Arg(string key, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
        return def;
    }
}
