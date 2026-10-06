using System.IO;
using UnityEditor;

// NamedBuildTarget лежит именно здесь, а не в UnityEditor. Забытая строка
// уронила первую сборку на компиляции — и это ровно то, ради чего гейт
// существует: ошибка вылезла в первый день, а не на шестой.
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Сборка веб-плеера из командной строки, без единого клика в редакторе.
///
/// Это гейт первого дня Unity-трека. Смысл проверки не в том, что билд
/// красивый, а в том, что весь путь — создать сцену, положить в настройки
/// сборки, собрать под Web — проходится headless. Если хоть один шаг требует
/// мыши, Unity-трек нереален в заявленный срок, и узнать это надо в первый
/// день, а не на шестой.
///
/// Сцена создается скриптом, если ее нет. Файл сцены — это YAML, который можно
/// написать руками, но он привязан к версии редактора и ломается от нее
/// молча; надежнее попросить редактор создать сцену самому.
///
/// Запуск:
///   Unity.exe -batchmode -nographics -quit -projectPath . \
///             -executeMethod BuildScript.BuildWeb -logFile -
/// </summary>
public static class BuildScript
{
    private const string SceneDir = "Assets/Scenes";
    private const string ScenePath = SceneDir + "/Main.unity";
    private const string OutputDir = "Build/Web";

    /// <summary>Создает пустую сцену, если ее еще нет, и регистрирует в сборке.</summary>
    public static void EnsureScene()
    {
        if (!Directory.Exists(SceneDir))
        {
            Directory.CreateDirectory(SceneDir);
            AssetDatabase.Refresh();
        }

        if (!File.Exists(ScenePath))
        {
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects,
                NewSceneMode.Single
            );
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[build] сцена создана: {ScenePath}");
        }

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    /// <summary>
    /// Убирает из артефакта чужой бренд и белые поля.
    ///
    /// Дефолтный шаблон Unity зажимает сборку в прямоугольник 960 на 600 посреди
    /// белой страницы и вешает снизу полосу с логотипом движка и служебным
    /// именем проекта `mars-unity`. Это первое, что видит открывший ссылку, и
    /// арт-инспектор поймал это на обеих сборках сразу.
    ///
    /// Метод публичный и зовется из обоих сборщиков: витрина собирается своим
    /// путем, и брендинг, поставленный только в одном месте, разъехался бы молча.
    /// </summary>
    public static void ApplyBranding()
    {
        PlayerSettings.WebGL.template = "PROJECT:MarsColony";
        PlayerSettings.companyName = "Amirkhan";
        PlayerSettings.productName = "Колония на Марсе";
        // Заставку «Made with Unity» на бесплатной лицензии не отключить, и
        // трогать ее настройку не надо: на Personal попытка снять флаг либо
        // игнорируется, либо роняет сборку. Логотип и подпись в самой странице
        // от заставки не зависят и убираются шаблоном.
    }

    public static void BuildWeb()
    {
        EnsureScene();

        // Brotli и отключенные исключения — то, что держит вес сборки в
        // разумных рамках. Без этого пустой проект едет на десятки мегабайт,
        // и обещание «ссылка открывается за секунду» рушится сразу.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.SetIl2CppCompilerConfiguration(
            NamedBuildTarget.WebGL,
            Il2CppCompilerConfiguration.Master
        );
        PlayerSettings.stripEngineCode = true;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.High);

        ApplyBranding();

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = OutputDir,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        Debug.Log(
            $"[build] результат: {summary.result}, "
                + $"вес: {summary.totalSize / 1024 / 1024} МБ, "
                + $"время: {summary.totalTime.TotalSeconds:F0} с"
        );

        if (summary.result != BuildResult.Succeeded)
        {
            // Ненулевой код возврата обязателен: без него провал сборки
            // выглядит в консоли так же, как успех, и гейт перестает быть
            // гейтом. Тот же принцип, что у веб-версии с хуком на коммит.
            EditorApplication.Exit(1);
        }

        EditorApplication.Exit(0);
    }
}
