using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Фактическая сборка. Задание требует мерить бюджеты сборкой, а не оценкой по
/// сцене: вес билда по сцене не виден вообще, а число вызовов отрисовки после
/// статической упаковки не равно числу рендереров.
/// </summary>
public static class V2Sborka
{
    public static void Sobrat()
    {
        string vyhod = Arg("-buildout", "C:/Ai/Jarvis/mars-colony/loop/scene-v2/build");
        Directory.CreateDirectory(vyhod);

        var opt = new BuildPlayerOptions
        {
            scenes = new[] { V2Lib.SCENE_PATH },
            locationPathName = Path.Combine(vyhod, "MarsV2.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(opt);
        var s = report.summary;

        long ves = 0;
        if (Directory.Exists(vyhod))
            ves = Directory.GetFiles(vyhod, "*", SearchOption.AllDirectories)
                           .Sum(f => new FileInfo(f).Length);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== ФАКТИЧЕСКАЯ СБОРКА ===");
        sb.AppendLine($"результат      : {s.result}");
        sb.AppendLine($"ошибок         : {s.totalErrors}");
        sb.AppendLine($"время          : {s.totalTime}");
        sb.AppendLine($"вес по отчету  : {s.totalSize / 1024 / 1024} МБ");
        sb.AppendLine($"вес папки      : {ves / 1024 / 1024} МБ  (потолок 100)");
        File.WriteAllText(Path.GetFullPath(
            "../mars-colony/loop/scene-v2/sborka.txt"), sb.ToString());
        Debug.Log("[v2] " + sb);
    }

    private static string Arg(string key, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
        return def;
    }
}
