// Одна карта для ручной работы: ландшафт в стилистике Township, на нем
// все наши ассеты в честном метровом масштабе. Расстановку владелец
// делает сам - здесь модели просто разложены рядами, чтобы их было
// удобно брать и двигать.
//
// Проект на URP: шейдера Standard нет, карта берется из _BaseMap.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SandboxBuilder
{
    private const string AssetsDir = "Assets/OurAssets";
    private const string ScenePath = "Assets/Scenes/Sandbox.unity";

    private const float Land = 90f;    // сторона игрового поля, метры
    private const float Apron = 34f;   // полоса под палитру за южным краем
    private const int Grid = 181;
    private const float Step = 7.5f;   // шаг между ассетами
    private const int Cols = 12;
    // Ассеты стоят ЗА полем, вдоль южной кромки: поле остается чистым,
    // владелец перетаскивает модели на него сам.
    private const float StartX = -44f;
    private const float StartZ = -54f;   // южнее края поля (Land/2 = 45)

    private static float SizeFor(string n)
    {
        if (n == "shuttle") return 8.5f;
        if (n == "ploshchadka-shattla") return 11f;
        if (n.StartsWith("zavod") || n.StartsWith("fabrika")) return 9.5f;
        if (n.StartsWith("sklad")) return 8.5f;
        if (n.StartsWith("stantsiya")) return 7.4f;
        if (n.StartsWith("kupol") || n.StartsWith("zhiloy")) return 5.6f;
        if (n.StartsWith("burovaya")) return 6.5f;
        if (n.StartsWith("dron")) return 1.4f;
        if (n.StartsWith("grunt")) return 4f;
        return 2.2f;
    }

    public static void Build()
    {
        // В пакетном режиме база ассетов не видит файлы, положенные извне,
        // пока ее не обновить - иначе текстуры не находятся и модели
        // приезжают серой заглушкой.
        AssetDatabase.Refresh();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildGround();
        Physics.SyncTransforms();
        int n = PlaceAssets();
        BuildLight();
        var cam = BuildCamera();

        EditorSceneManager.SaveScene(scene, ScenePath);
        Shoot(cam, new Vector3(0f, 0.5f, -20f), -90f, 38f, 118f, 40f, "sandbox.png");
        Debug.Log($"песочница: ландшафт + {n} ассетов, сцена {ScenePath}");
    }

    /// <summary>Материал под URP с запасным вариантом на встроенный рендер.</summary>
    private static Material NewMat(Color color, Texture tex = null, float smooth = 0f)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (tex != null)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        }
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        return m;
    }

    /// <summary>Township: земля почти ровная, теплая, с очень пологой волной.
    /// Никакого фотореалистичного рельефа - он уводит в марсианский симулятор.
    /// Край мира мягко загибается вверх, чтобы горизонт не обрывался доской.</summary>
    private static void BuildGround()
    {
        var go = new GameObject("zemlya");
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        int n = Grid;
        var verts = new Vector3[n * n];
        var uvs = new Vector2[n * n];

        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = (i / (float)(n - 1) - 0.5f) * Land;
                float z = (j / (float)(n - 1)) * (Land + Apron) - Land * 0.5f - Apron;
                // Поле строго ровное: владелец расставляет объекты сам,
                // любая волна мешает ставить здания.
                verts[j * n + i] = new Vector3(x, 0f, z);
                uvs[j * n + i] = new Vector2(x / 10f, z / 10f);
            }

        var tri = new List<int>((n - 1) * (n - 1) * 6);
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                tri.AddRange(new[] { a, c, b, b, c, d });
            }

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.SetTriangles(tri, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var mat = NewMat(new Color(0.86f, 0.58f, 0.40f));

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    /// <summary>Все модели рядами на этой же земле, посажены лучом.
    /// Материал строим сами: импорт FBX терял привязку текстуры.</summary>
    private static int PlaceAssets()
    {
        var root = new GameObject("ASSETY");
        var names = Directory.GetFiles(AssetsDir, "*.fbx")
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .Where(x => x != "shuttle-2000" && x != "shuttle-20000" && x != "shuttle-v2-1536")
            .OrderBy(x => x)
            .ToArray();

        int placed = 0;
        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i];
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{AssetsDir}/{name}.fbx");
            if (asset == null) continue;
            var go = Object.Instantiate(asset);
            go.name = name;
            go.transform.SetParent(root.transform, false);

            ApplyTexture(go, name);

            var b = BoundsOf(go);
            if (b.size.x <= 0.0001f) { Object.DestroyImmediate(go); continue; }
            float k = SizeFor(name) / Mathf.Max(b.size.x, b.size.z);
            go.transform.localScale = go.transform.localScale * k;
            b = BoundsOf(go);

            float x = StartX + (i % Cols) * Step;
            float z = StartZ - (i / Cols) * Step;
            float y = 0f;
            if (Physics.Raycast(new Vector3(x, 60f, z), Vector3.down, out var hit, 200f))
                y = hit.point.y;
            go.transform.position += new Vector3(x - b.center.x, y - b.min.y, z - b.center.z);
            placed++;
        }
        return placed;
    }

    private static void ApplyTexture(GameObject go, string name)
    {
        string dir = $"{AssetsDir}/{name}_textures";
        Texture2D tex = null;
        if (Directory.Exists(dir))
        {
            var file = Directory.GetFiles(dir)
                .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg"))
                .OrderBy(f => f).FirstOrDefault();
            if (file != null)
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(file.Replace('\\', '/'));
        }
        if (tex == null) Debug.LogWarning($"нет текстуры: {name} (искал в {dir})");
        var mat = tex != null
            ? NewMat(Color.white, tex, 0.05f)
            : NewMat(new Color(0.78f, 0.76f, 0.72f), null, 0.05f);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
            r.sharedMaterial = mat;
        if (name == "sklad-angar" || name == "kupol-grib")
            Debug.Log($"[диаг] {name}: шейдер={mat.shader.name} " +
                      $"_BaseMap={(mat.HasProperty("_BaseMap") ? (mat.GetTexture("_BaseMap")?.name ?? "null") : "нет свойства")} " +
                      $"_MainTex={(mat.HasProperty("_MainTex") ? (mat.GetTexture("_MainTex")?.name ?? "null") : "нет свойства")} " +
                      $"текстура={(tex != null ? tex.name : "НЕ НАЙДЕНА")} uv={go.GetComponentInChildren<MeshFilter>()?.sharedMesh?.uv?.Length}");
    }

    private static Bounds BoundsOf(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    private static void BuildLight()
    {
        var sun = new GameObject("solnce").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.93f, 0.82f);
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.5f;
        sun.transform.rotation = Quaternion.Euler(52f, 40f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.62f, 0.54f, 0.48f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.93f, 0.76f, 0.62f);
        RenderSettings.fogDensity = 0.0028f;
    }

    private static Camera BuildCamera()
    {
        var cam = new GameObject("kamera").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.93f, 0.76f, 0.62f);
        cam.farClipPlane = 500f;
        return cam;
    }

    private static void Shoot(Camera cam, Vector3 target, float azDeg, float elDeg,
                              float dist, float fov, string file)
    {
        float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
        var dir = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el),
                              Mathf.Cos(el) * Mathf.Sin(az));
        cam.transform.position = target + dir * dist;
        cam.transform.rotation = Quaternion.LookRotation(target - cam.transform.position);
        cam.fieldOfView = fov;

        var rt = new RenderTexture(1600, 1000, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tx = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        tx.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
        tx.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        File.WriteAllBytes(file, tx.EncodeToPNG());
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tx);
    }
}
