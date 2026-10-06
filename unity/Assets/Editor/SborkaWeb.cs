using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MarsColony.EditorTools
{
    /// <summary>
    /// Сборка под веб из командной строки. Настройки взяты по итогам разбора
    /// типовых провалов Unity 6 WebGL: обрезка кода ломает Newtonsoft (лечится
    /// link.xml плюс мягкий уровень обрезки), экспериментальный WebGPU даёт
    /// пустой холст после загрузчика (лечится порядком графических интерфейсов),
    /// падения вкладки идут от памяти (лечится явными лимитами).
    ///
    /// Запуск: Unity.exe -batchmode -quit -projectPath ... 
    ///         -executeMethod MarsColony.EditorTools.SborkaWeb.Sobrat -logFile ...
    /// </summary>
    public static class SborkaWeb
    {
        private const string PAPKA = "Build/MarsColony-Web";   // gzip-версия под itch.io
        private const string SCENA = "Assets/Scenes/MAIN.unity";

        public static void Sobrat()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), PAPKA.Replace('/', Path.DirectorySeparatorChar));
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception e) { Debug.LogWarning("[веб] старую папку снести не вышло: " + e.Message); }
            Directory.CreateDirectory(dir);

            var nbt = NamedBuildTarget.WebGL;

            // Только WebGL2. WebGPU в Unity 6 экспериментальный и даёт чёрный холст.
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });

            // Обрезка мягкая: агрессивная выкидывает типы, нужные Newtonsoft через рефлексию.
            PlayerSettings.SetManagedStrippingLevel(nbt, ManagedStrippingLevel.Minimal);

            // Gzip, а не Brotli: itch.io не отдаёт заголовок Content-Encoding для brotli,
            // и игра виснет на загрузке. Gzip браузеры понимают везде, вес больше процентов на 15.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.WebGL.initialMemorySize = 256;
            PlayerSettings.WebGL.maximumMemorySize = 1024;
            PlayerSettings.runInBackground = true;

            Debug.Log($"[веб] настройки: обрезка {PlayerSettings.GetManagedStrippingLevel(nbt)}, сжатие {PlayerSettings.WebGL.compressionFormat}, память {PlayerSettings.WebGL.initialMemorySize}..{PlayerSettings.WebGL.maximumMemorySize} МБ");

            var opt = new BuildPlayerOptions
            {
                scenes = new[] { SCENA },
                locationPathName = dir,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            Debug.Log("[веб] старт сборки в " + dir);
            BuildReport report = BuildPipeline.BuildPlayer(opt);
            BuildSummary s = report.summary;
            Debug.Log($"[веб] ИТОГ: {s.result}, время {s.totalTime}, размер {s.totalSize / 1048576} МБ, ошибок {s.totalErrors}");

            if (s.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                    foreach (var m in step.messages)
                        if (m.type == LogType.Error || m.type == LogType.Exception)
                            Debug.Log("[веб] ОШИБКА: " + m.content);
                EditorApplication.Exit(1);
            }
            EditorApplication.Exit(0);
        }
    }
}
