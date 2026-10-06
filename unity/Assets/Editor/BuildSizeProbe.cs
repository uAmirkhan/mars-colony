using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Прогон 7, узел 0: сколько байт в WebGL-сборке стоит ОДНА добавленная модель.
///
/// Не трогает `colony3d-scene.json` и не строит настоящую витрину — та сцена
/// в этом прогоне "зона художника" (`loop/run-7/plan.md`, таблица зон файлов).
/// Вместо этого собирает временную одноразовую сцену (грунт + свет,
/// опционально одна модель из `Assets/Incoming`) теми же настройками сжатия
/// WebGL, что и `SceneBuilder3D.BuildWeb3D`, в отдельную папку, и меряет вес
/// папки.
///
/// Число — ОЦЕНКА, не точный вклад в реальную сборку витрины: настоящая
/// сцена уже делит на полторы сотни объектов общие шейдеры и рантайм-код
/// движка, и добавление модели туда может стоить дешевле предельных байт,
/// чем в изолированной сцене с нуля, где эти накладные расходы считаются
/// заново. Разница "с моделью минус пусто" отсекает именно эти накладные
/// расходы и должна оценивать стоимость модели честно.
///
/// Запуск:
///   Unity.exe -batchmode -nographics -quit -projectPath . \
///             -executeMethod BuildSizeProbe.BuildEmpty -logFile -
///   Unity.exe -batchmode -nographics -quit -projectPath . \
///             -executeMethod BuildSizeProbe.BuildWithModel -logFile -
/// </summary>
public static class BuildSizeProbe
{
    private const string ScenePath = "Assets/Scenes/SizeProbe.unity";
    private const string IncomingDir = "Assets/Incoming";
    private const string ReportFile = "size-probe.json";

    public static void BuildEmpty() => Run(withModel: false, "Build/SizeProbeEmpty");

    public static void BuildWithModel() => Run(withModel: true, "Build/SizeProbeModel");

    private static void Run(bool withModel, string outputDir)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "ground";

        var lightGo = new GameObject("sun");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        lightGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);

        var camGo = new GameObject("camera");
        var cam = camGo.AddComponent<Camera>();
        camGo.transform.position = new Vector3(0f, 3f, -6f);
        camGo.transform.LookAt(Vector3.zero);
        camGo.tag = "MainCamera";

        string modelName = "нет";
        if (withModel)
        {
            string fbx = FindFirstFbx();
            if (fbx == null)
            {
                Debug.LogError($"[size-probe] в {IncomingDir} нет ни одной .fbx — класть в сцену нечего");
                EditorApplication.Exit(1);
                return;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (prefab == null)
            {
                Debug.LogError($"[size-probe] {fbx} не импортируется как GameObject");
                EditorApplication.Exit(1);
                return;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = Vector3.zero;
            modelName = Path.GetFileNameWithoutExtension(fbx);
        }

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);

        // Те же настройки сжатия, что и в `SceneBuilder3D.BuildWeb3D` и
        // `BuildScript.BuildWeb` — иначе разница в байтах отражала бы разницу
        // в сжатии, а не в модели.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.decompressionFallback = true;

        // `EditorBuildSettings` не трогаем (тот же принцип, что у `BuildWeb3D`):
        // сцена передается списком явно в опциях сборки.
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
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
            + $"  \"withModel\": {(withModel ? "true" : "false")},\n"
            + $"  \"model\": \"{modelName}\",\n"
            + $"  \"result\": \"{summary.result}\",\n"
            + $"  \"bytes\": {bytes},\n"
            + $"  \"outputDir\": \"{outputDir}\"\n"
            + "}\n";
        File.WriteAllText(ReportFile, json);

        Debug.Log(
            $"[size-probe] withModel={withModel} model={modelName} "
                + $"result={summary.result} bytes={bytes} ({bytes / 1024.0 / 1024.0:F2} МБ)"
        );

        if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            EditorApplication.Exit(1);
            return;
        }
        EditorApplication.Exit(0);
    }

    private static string FindFirstFbx()
    {
        if (!Directory.Exists(IncomingDir))
            return null;
        foreach (var path in Directory.GetFiles(IncomingDir, "*.fbx"))
            return path.Replace('\\', '/');
        return null;
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
