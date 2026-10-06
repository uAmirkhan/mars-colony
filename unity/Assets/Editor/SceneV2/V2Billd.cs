using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Сборка билда и замер его веса.
///
/// Последний незакрытый пункт бюджета. До сих пор вес билда был единственным
/// числом в приемке, про которое честно стояло «не мерен» - все остальное
/// меряется по сцене, а вес только настоящей сборкой.
/// </summary>
public static class V2Billd
{
    public static void WebGL()
    {
        Sobrat(BuildTarget.WebGL, BuildTargetGroup.WebGL,
               Arg("-out", "../mars-colony/loop/scene-v2/billd/webgl"));
    }

    public static void Windows()
    {
        Sobrat(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone,
               Arg("-out", "../mars-colony/loop/scene-v2/billd/win"));
    }

    private static void Sobrat(BuildTarget target, BuildTargetGroup group, string outDir)
    {
        string put = Path.GetFullPath(outDir);
        Directory.CreateDirectory(put);

        var opts = new BuildPlayerOptions
        {
            scenes = new[] { V2Lib.SCENE_PATH },
            locationPathName = target == BuildTarget.StandaloneWindows64
                               ? Path.Combine(put, "MarsColony.exe") : put,
            target = target,
            targetGroup = group,
            options = BuildOptions.None,
        };

        Debug.Log($"[v2] сборка {target} в {put}");
        var otchet = BuildPipeline.BuildPlayer(opts);
        var s = otchet.summary;

        Debug.Log($"[v2] итог сборки: {s.result}, ошибок {s.totalErrors}, "
                + $"время {s.totalTime.TotalMinutes:F1} мин");

        if (s.result != BuildResult.Succeeded)
        {
            Debug.LogError("[v2] СБОРКА НЕ УДАЛАСЬ");
            return;
        }

        // Вес считаем по диску, а не по summary.totalSize: сжатие WebGL дает
        // разницу между тем, что лежит, и тем, что поедет по сети.
        long vsego = 0;
        var faily = Directory.GetFiles(put, "*", SearchOption.AllDirectories);
        foreach (var f in faily) vsego += new FileInfo(f).Length;

        var krupnye = faily.Select(f => new FileInfo(f))
                           .OrderByDescending(fi => fi.Length).Take(8);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== ВЕС БИЛДА, {target} ===");
        sb.AppendLine($"файлов        : {faily.Length}");
        sb.AppendLine($"вес на диске  : {vsego / 1024 / 1024} МБ  (потолок 100)");
        sb.AppendLine($"по отчету     : {s.totalSize / 1024 / 1024} МБ");
        sb.AppendLine($"время сборки  : {s.totalTime.TotalMinutes:F1} мин");
        sb.AppendLine();
        sb.AppendLine("Самые тяжелые файлы:");
        foreach (var fi in krupnye)
            sb.AppendLine($"  {fi.Name,-46} {fi.Length / 1024,8} КБ");

        File.WriteAllText(Path.GetFullPath(
            "../mars-colony/loop/scene-v2/ves-billda.txt"), sb.ToString());
        Debug.Log("[v2] " + sb);
    }

    private static string Arg(string key, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
        return def;
    }
}
