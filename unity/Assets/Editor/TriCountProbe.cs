using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Разовый замер под пересчёт бюджета "телефон -> десктопный веб" (запрос
/// владельца 2026-08-17): сколько треугольников десктопный браузер держит на
/// РЕАЛЬНОМ GPU этой машины при 60 FPS без просадки.
///
/// Строит WebGL-сборку с N копиями существующей модели `grunt-regolit-4`
/// (60000 треугольников, `Assets/Incoming/grunt-regolit-4.fbx`, тот же файл,
/// что уже использовался в `BuildSizeProbe`), расставленными сеткой в кадре
/// камеры без выхода за фрустум — иначе Unity сам отсечёт их culling'ом и
/// число окажется меньше заявленного.
///
/// Сборка — под `-nographics` (GPU редактору не нужен, он не рисует кадр,
/// только компилирует шейдеры и упаковывает данные). Замер FPS — снаружи,
/// РЕАЛЬНЫМ браузером с включённым GPU (см. `scratchpad/tricount-fps.mjs`),
/// иначе число измеряло бы CPU-рендер software-ANGLE, а не десктопный GPU.
///
/// Запуск:
///   Unity.exe -batchmode -nographics -quit -projectPath . \
///             -executeMethod TriCountProbe.Build -triCount 5 -logFile -
/// </summary>
public static class TriCountProbe
{
    private const string IncomingFbx = "Assets/Incoming/grunt-regolit-4.fbx";

    public static void Build()
    {
        int count = ReadIntArg("-triCount", 1);
        string outputDir = $"Build/TriCountProbe_{count}";
        string reportFile = $"tricount-probe-{count}.json";

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(IncomingFbx);
        if (prefab == null)
        {
            Debug.LogError($"[tricount-probe] {IncomingFbx} не импортируется как GameObject");
            EditorApplication.Exit(1);
            return;
        }

        // Сетка вплотную к камере, без выхода за фрустум — иначе Unity сам
        // отсечёт лишние инстансы culling'ом, и замер окажется меньше
        // заявленного числа треугольников.
        int gridSize = Mathf.CeilToInt(Mathf.Sqrt(count));
        const float spacing = 2.4f; // габарит модели ~2м (zamer-modeley.md) + запас
        float half = (gridSize - 1) * spacing / 2f;
        int placed = 0;
        for (int row = 0; row < gridSize && placed < count; row++)
        {
            for (int col = 0; col < gridSize && placed < count; col++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.position = new Vector3(col * spacing - half, 0f, row * spacing - half);
                placed++;
            }
        }

        float extent = gridSize * spacing;

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "ground";
        ground.transform.localScale = new Vector3(extent / 5f, 1f, extent / 5f);

        var lightGo = new GameObject("sun");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        lightGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);

        var camGo = new GameObject("camera");
        var cam = camGo.AddComponent<Camera>();
        cam.farClipPlane = Mathf.Max(1000f, extent * 4f);
        camGo.transform.position = new Vector3(0f, extent * 0.65f + 4f, -extent * 0.85f - 4f);
        camGo.transform.LookAt(new Vector3(0f, 0f, 0f));
        camGo.tag = "MainCamera";

        Directory.CreateDirectory("Assets/Scenes");
        string scenePath = $"Assets/Scenes/TriCountProbe_{count}.unity";
        EditorSceneManager.SaveScene(scene, scenePath);

        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.template = "PROJECT:MarsColony";

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
        long triangles = (long)placed * 60000L;

        var json =
            "{\n"
            + $"  \"instances\": {placed},\n"
            + $"  \"trianglesApprox\": {triangles},\n"
            + $"  \"gridSize\": {gridSize},\n"
            + $"  \"result\": \"{summary.result}\",\n"
            + $"  \"bytes\": {bytes},\n"
            + $"  \"outputDir\": \"{outputDir}\"\n"
            + "}\n";
        File.WriteAllText(reportFile, json);

        Debug.Log(
            $"[tricount-probe] instances={placed} triangles~{triangles} result={summary.result} "
                + $"bytes={bytes} ({bytes / 1024.0 / 1024.0:F2} МБ) dir={outputDir}"
        );

        EditorApplication.Exit(summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    private static int ReadIntArg(string name, int fallback)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name && int.TryParse(args[i + 1], out int v))
                return v;
        return fallback;
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
