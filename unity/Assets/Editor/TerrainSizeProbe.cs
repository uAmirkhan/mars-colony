using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Разведка направления 2/10 (landscape-research): сколько байт в WebGL-сборке
/// стоит штатный Unity Terrain сам по себе — движковый модуль + шейдер +
/// минимальные данные TerrainData, БЕЗ деревьев/деталей.
///
/// Не трогает `SceneBuilder3D.cs`, `colony3d-scene.json` и существующий
/// `BuildSizeProbe.cs` — отдельный одноразовый файл по тому же паттерну
/// (см. `BuildSizeProbe.cs`: временная сцена, те же настройки сжатия WebGL,
/// сборка в отдельную папку, замер байт папки).
///
/// База для сравнения — уже посчитанный `size-probe-empty.json`
/// (грунт-плоскость + свет + камера, БЕЗ terrain): 5 339 680 байт.
///
/// Запуск:
///   Unity.exe -batchmode -nographics -quit -projectPath . \
///             -executeMethod TerrainSizeProbe.BuildWithTerrain -logFile -
/// </summary>
public static class TerrainSizeProbe
{
    private const string ScenePath = "Assets/Scenes/SizeProbeTerrain.unity";
    private const string ReportFile = "size-probe-terrain.json";
    private const string OutputDir = "Build/SizeProbeTerrain";

    public static void BuildWithTerrain() => RunTerrain(513, "Build/SizeProbeTerrain", "size-probe-terrain.json");

    public static void BuildWithTerrainLowRes() =>
        RunTerrain(129, "Build/SizeProbeTerrainLowRes", "size-probe-terrain-lowres.json");

    private static void RunTerrain(int res, string outputDir, string reportFile)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // TerrainData: 513 — дефолт Unity 6 для нового terrain; 129 — вариант
        // с той же плотностью данных, что и деформированный меш в
        // BuildWithDeformedMesh, для честного сравнения "движковый налог"
        // против "цена данных". Карта 200x50x200 — тот же масштаб, что
        // текущий mars_ground (Plane 20x20 -> 200x200).
        var terrainData = new TerrainData();
        terrainData.heightmapResolution = res;
        terrainData.size = new Vector3(200f, 50f, 200f);

        // Нетривиальный рельеф (не все нули), чтобы не измерить best-case
        // сжатие плоской карты высот — несколько холмов синусоидой.
        var heights = new float[res, res];
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float nx = (float)x / res;
            float ny = (float)y / res;
            heights[y, x] =
                0.5f
                + 0.15f * Mathf.Sin(nx * Mathf.PI * 4f)
                + 0.15f * Mathf.Cos(ny * Mathf.PI * 3f)
                + 0.05f * Mathf.Sin((nx + ny) * Mathf.PI * 9f);
        }
        terrainData.SetHeights(0, 0, heights);

        // Один слой с маленькой процедурной текстурой (4x4, без сжатия
        // текстуры на диске — изолируем именно ЦЕНУ TERRAIN, а не цену
        // произвольной диффузной текстуры).
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var pixels = new Color32[16];
        for (int i = 0; i < 16; i++)
            pixels[i] = new Color32(69, 40, 29, 255); // тон mars_ground
        tex.SetPixels32(pixels);
        tex.Apply();
        Directory.CreateDirectory("Assets/Scenes");
        AssetDatabase.CreateAsset(tex, "Assets/Scenes/size_probe_terrain_tex.asset");

        var layer = new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(50f, 50f) };
        AssetDatabase.CreateAsset(layer, "Assets/Scenes/size_probe_terrain_layer.asset");
        terrainData.terrainLayers = new[] { layer };

        AssetDatabase.CreateAsset(terrainData, "Assets/Scenes/size_probe_terrain_data.asset");

        var terrainGo = Terrain.CreateTerrainGameObject(terrainData);
        terrainGo.name = "terrain_probe";
        // Явно без деревьев/деталей — не создаем detailPrototypes/treePrototypes.

        var lightGo = new GameObject("sun");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        lightGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);

        var camGo = new GameObject("camera");
        var cam = camGo.AddComponent<Camera>();
        camGo.transform.position = new Vector3(100f, 30f, -30f);
        camGo.transform.LookAt(new Vector3(100f, 0f, 100f));
        camGo.tag = "MainCamera";

        string scenePath = $"Assets/Scenes/SizeProbeTerrain_{res}.unity";
        EditorSceneManager.SaveScene(scene, scenePath);

        // Те же настройки сжатия, что и BuildSizeProbe/BuildWeb3D — иначе
        // разница в байтах отражала бы разницу в сжатии, а не в terrain.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.decompressionFallback = true;

        var options = new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = outputDir,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        long bytes = DirBytes(outputDir);

        var json =
            "{\n"
            + "  \"withTerrain\": true,\n"
            + $"  \"heightmapResolution\": {res},\n"
            + $"  \"result\": \"{summary.result}\",\n"
            + $"  \"bytes\": {bytes},\n"
            + $"  \"outputDir\": \"{outputDir}\"\n"
            + "}\n";
        File.WriteAllText(reportFile, json);

        Debug.Log(
            $"[terrain-size-probe] result={summary.result} bytes={bytes} ({bytes / 1024.0 / 1024.0:F2} МБ)"
        );

        if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            EditorApplication.Exit(1);
            return;
        }
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// Альтернатива: обычный меш-плоскость с деформацией вершин из кода,
    /// БЕЗ компонента Terrain и БЕЗ TerrainData. Тот же масштаб (200x200),
    /// сравнимая плотность решетки (129x129 вершин — этого достаточно для
    /// пологих марсианских дюн), материал "Standard" без текстуры — как у
    /// текущего mars_ground (PrepareGroundMaterial в SceneBuilder3D.cs).
    /// </summary>
    public static void BuildWithDeformedMesh()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        int gridRes = 129; // вершин на сторону
        float sizeXZ = 200f;
        var mesh = BuildDeformedGrid(gridRes, sizeXZ);
        Directory.CreateDirectory("Assets/Scenes");
        AssetDatabase.CreateAsset(mesh, "Assets/Scenes/size_probe_deformed_mesh.asset");

        var go = new GameObject("deformed_ground");
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.27f, 0.155f, 0.115f); // тон mars_ground, без текстуры
        mat.SetFloat("_Glossiness", 0.05f);
        mat.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(mat, "Assets/Scenes/size_probe_deformed_mat.asset");
        mr.sharedMaterial = mat;
        go.AddComponent<MeshCollider>();

        var lightGo = new GameObject("sun");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        lightGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);

        var camGo = new GameObject("camera");
        var cam = camGo.AddComponent<Camera>();
        camGo.transform.position = new Vector3(0f, 30f, -30f);
        camGo.transform.LookAt(Vector3.zero);
        camGo.tag = "MainCamera";

        const string scenePath = "Assets/Scenes/SizeProbeDeformedMesh.unity";
        EditorSceneManager.SaveScene(scene, scenePath);

        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.decompressionFallback = true;

        const string outputDir = "Build/SizeProbeDeformedMesh";
        var options = new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = outputDir,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        long bytes = DirBytes(outputDir);

        File.WriteAllText(
            "size-probe-deformed-mesh.json",
            "{\n"
                + $"  \"gridRes\": {gridRes},\n"
                + $"  \"result\": \"{summary.result}\",\n"
                + $"  \"bytes\": {bytes},\n"
                + $"  \"outputDir\": \"{outputDir}\"\n"
                + "}\n"
        );

        Debug.Log(
            $"[deformed-mesh-size-probe] result={summary.result} bytes={bytes} ({bytes / 1024.0 / 1024.0:F2} МБ)"
        );

        EditorApplication.Exit(summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    private static Mesh BuildDeformedGrid(int res, float sizeXZ)
    {
        var verts = new Vector3[res * res];
        var uvs = new Vector2[res * res];
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float nx = (float)x / (res - 1);
            float ny = (float)y / (res - 1);
            float h =
                2.5f * Mathf.Sin(nx * Mathf.PI * 4f)
                + 2.5f * Mathf.Cos(ny * Mathf.PI * 3f)
                + 0.8f * Mathf.Sin((nx + ny) * Mathf.PI * 9f);
            verts[y * res + x] = new Vector3((nx - 0.5f) * sizeXZ, h, (ny - 0.5f) * sizeXZ);
            uvs[y * res + x] = new Vector2(nx, ny);
        }

        var tris = new int[(res - 1) * (res - 1) * 6];
        int t = 0;
        for (int y = 0; y < res - 1; y++)
        for (int x = 0; x < res - 1; x++)
        {
            int i0 = y * res + x;
            int i1 = y * res + x + 1;
            int i2 = (y + 1) * res + x;
            int i3 = (y + 1) * res + x + 1;
            tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
            tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
        }

        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static long DirBytes(string dir)
    {
        if (!Directory.Exists(dir))
            return 0;
        long total = 0;
        foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            total += new FileInfo(file).Length;
        return total;
    }
}
