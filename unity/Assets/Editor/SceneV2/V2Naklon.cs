using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Ищет верный наклон модели: не лежит ли она на боку.
///
/// Владелец сказал «они перевернуты», и по куполам-теплицам это оказалось
/// буквально: у `kupol-geodezicheskiy` плоское основание смотрело вбок, и купол
/// читался опрокинутым яйцом.
///
/// Проверка числом: у стоящей постройки низ ШИРЕ верха. Перебираем развороты по
/// X и Z и берем тот, при котором отношение «низ к верху» наибольшее.
/// </summary>
public static class V2Naklon
{
    /// <summary>
    /// Ставит одну модель в ряд под разными наклонами и снимает.
    ///
    /// Число - только подсказка. Правило «низ шире верха» врет на грибе: у него
    /// шляпка шире ножки, и правило перевернет его вверх ногами. Решает глаз.
    /// </summary>
    public static void Ryad()
    {
        string model = Arg("-model", "kupol-geodezicheskiy");
        string outPath = Arg("-out", "../mars-colony/loop/scene-v2/kadry/naklon.png");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var pol = GameObject.CreatePrimitive(PrimitiveType.Plane);
        pol.transform.localScale = Vector3.one * 20f;
        pol.GetComponent<MeshRenderer>().sharedMaterial =
            V2Lib.NewLit(new Color(0.60f, 0.58f, 0.56f), null, 0.05f, imya: "nak-pol");

        var src = V2Lib.FindModel(model);
        (int x, int z)[] varianty = { (0, 0), (270, 270), (180, 0), (270, 0) };

        // КАЖДЫЙ вариант снимается отдельным файлом с именем-подписью.
        // Ряд в одном кадре обманывает: при развороте камеры 45 градусов порядок
        // на экране не совпадает с порядком в коде, и разобрать, где какой
        // вариант, нельзя.
        var sunEarly = new GameObject("Solnce").AddComponent<UnityEngine.Light>();
        sunEarly.type = LightType.Directional; sunEarly.intensity = 1.4f;
        sunEarly.shadows = LightShadows.Soft;
        sunEarly.transform.rotation = Quaternion.Euler(45f, 200f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.47f);
        var camOdin = V2Lib.MakeCamera(Vector3.zero);
        camOdin.orthographicSize = 6f;
        camOdin.backgroundColor = new Color(0.32f, 0.33f, 0.36f);

        for (int i = 0; i < varianty.Length; i++)
        {
            var odin = (GameObject)PrefabUtility.InstantiatePrefab(src);
            odin.transform.rotation = Quaternion.Euler(varianty[i].x, 0f, varianty[i].z);
            if (V2Lib.WorldBounds(odin, out var bb) && bb.size.y > 1e-4f)
            {
                odin.transform.localScale = Vector3.one * (7f / bb.size.y);
                V2Lib.WorldBounds(odin, out bb);
                var pz = odin.transform.position;
                pz.x -= bb.center.x; pz.z -= bb.center.z; pz.y -= bb.min.y;
                odin.transform.position = pz;
            }
            V2Lib.ApplyTexture(odin, model);
            V2Lib.Shoot(camOdin, System.IO.Path.GetFullPath(outPath)
                .Replace(".png", $"-X{varianty[i].x}Z{varianty[i].z}.png"));
            Object.DestroyImmediate(odin);
        }
        Object.DestroyImmediate(camOdin.gameObject);
        Object.DestroyImmediate(sunEarly.gameObject);

        for (int i = 0; i < varianty.Length; i++)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.transform.rotation = Quaternion.Euler(varianty[i].x, 0f, varianty[i].z);
            go.transform.localScale = Vector3.one;
            if (V2Lib.WorldBounds(go, out var b) && b.size.y > 1e-4f)
            {
                float k = 7f / b.size.y;
                go.transform.localScale = Vector3.one * k;
                V2Lib.WorldBounds(go, out b);
                var poz = go.transform.position;
                poz.x += (i - 1.5f) * 13f - b.center.x;
                poz.z += -b.center.z;
                poz.y += -b.min.y;
                go.transform.position = poz;
            }
            V2Lib.ApplyTexture(go, model);
            V2Lib.Log($"вариант X{varianty[i].x} Z{varianty[i].z}");
        }

        var sun = new GameObject("Solnce").AddComponent<UnityEngine.Light>();
        sun.type = LightType.Directional; sun.intensity = 1.4f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(45f, 200f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.47f);

        var cam = V2Lib.MakeCamera(Vector3.zero);
        cam.orthographicSize = 15f;
        cam.backgroundColor = new Color(0.32f, 0.33f, 0.36f);
        V2Lib.Shoot(cam, System.IO.Path.GetFullPath(outPath));
    }

    public static void Probe()
    {
        string spisok = Arg("-modeli", "kupol-geodezicheskiy");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        foreach (var imya in spisok.Split(','))
        {
            var m = imya.Trim();
            var src = V2Lib.FindModel(m);
            if (src == null) { Debug.LogWarning($"[v2] нет модели {m}"); continue; }

            var luchshiy = (x: 0, z: 0, otn: -1f);
            var stroki = new List<string>();

            foreach (int rx in new[] { 0, 90, 180, 270 })
            foreach (int rz in new[] { 0, 90, 270 })
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
                go.transform.rotation = Quaternion.Euler(rx, 0f, rz);

                var t = Tochki(go);
                if (t.Count > 32)
                {
                    float lo = t.Min(v => v.y), hi = t.Max(v => v.y), H = hi - lo;
                    if (H > 1e-4f)
                    {
                        float niz = Shirina(t, lo, H, 0f, 0.15f);
                        float verh = Shirina(t, lo, H, 0.85f, 1f);
                        float otn = niz / Mathf.Max(verh, 0.001f);
                        stroki.Add($"    X{rx,3} Z{rz,3}: низ {niz:F2} верх {verh:F2} "
                                 + $"отношение {otn:F2} высота {H:F2}");
                        if (otn > luchshiy.otn) luchshiy = (rx, rz, otn);
                    }
                }
                Object.DestroyImmediate(go);
            }

            Debug.Log($"[v2] НАКЛОН {m}: лучший X={luchshiy.x} Z={luchshiy.z} "
                    + $"(низ/верх {luchshiy.otn:F2})\n" + string.Join("\n", stroki));
        }
    }

    private static List<Vector3> Tochki(GameObject go)
    {
        var t = new List<Vector3>();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var mtx = mf.transform.localToWorldMatrix;
            var v = mf.sharedMesh.vertices;
            // прореживаем: полный набор не нужен, форму видно и по каждой пятой
            for (int i = 0; i < v.Length; i += 5) t.Add(mtx.MultiplyPoint3x4(v[i]));
        }
        return t;
    }

    private static float Shirina(List<Vector3> t, float lo, float H, float f0, float f1)
    {
        var sloy = t.Where(v => v.y >= lo + H * f0 && v.y <= lo + H * f1).ToList();
        if (sloy.Count < 8) return 0.001f;
        return Mathf.Max(sloy.Max(v => v.x) - sloy.Min(v => v.x),
                         sloy.Max(v => v.z) - sloy.Min(v => v.z));
    }

    private static string Arg(string key, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
        return def;
    }
}
