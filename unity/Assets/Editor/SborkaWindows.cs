using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MarsColony.EditorTools
{
    /// <summary>
    /// Сборка под Windows из командной строки. Нужна потому, что вызов BuildPipeline
    /// через MCP отклоняется: сборка показывает диалог прогресса, а MCP запрещает
    /// любые взаимодействия с пользователем.
    ///
    /// Запуск:
    ///   Unity.exe -batchmode -quit -projectPath "C:\Ai\Jarvis\mars-unity"
    ///             -executeMethod MarsColony.EditorTools.SborkaWindows.Sobrat
    ///             -logFile "C:\Ai\Jarvis\mars-unity\build-windows.log"
    /// </summary>
    public static class SborkaWindows
    {
        private const string PAPKA = "Build/MarsColony-Windows";
        private const string SCENA = "Assets/Scenes/MAIN.unity";

        public static void Sobrat()
        {
            string koren = Directory.GetCurrentDirectory();
            string dir = Path.Combine(koren, PAPKA.Replace('/', Path.DirectorySeparatorChar));

            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception e) { Debug.LogWarning("[сборка] старую папку снести не вышло: " + e.Message); }
            Directory.CreateDirectory(dir);

            PlayerSettings.companyName = "Khan";
            PlayerSettings.productName = "Mars Colony";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            var opt = new BuildPlayerOptions
            {
                scenes = new[] { SCENA },
                locationPathName = Path.Combine(dir, "MarsColony.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            Debug.Log("[сборка] старт, цель: " + opt.locationPathName);
            BuildReport report = BuildPipeline.BuildPlayer(opt);
            BuildSummary s = report.summary;

            Debug.Log($"[сборка] ИТОГ: {s.result}, время {s.totalTime}, размер {s.totalSize / 1048576} МБ, ошибок {s.totalErrors}, предупреждений {s.totalWarnings}");

            if (s.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                    foreach (var m in step.messages)
                        if (m.type == LogType.Error || m.type == LogType.Exception)
                            Debug.Log("[сборка] ОШИБКА: " + m.content);
                EditorApplication.Exit(1);
            }

            EditorApplication.Exit(0);
        }
    }
}
