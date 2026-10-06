using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Галерея всех наших сгенерированных моделей: сетка с подписями на
/// марсианском грунте. Запуск:
///   Unity.exe -batchmode -quit -projectPath . -executeMethod GalleryBuilder.Build
/// Дальше сцену Assets/Scenes/Gallery.unity открывают в редакторе руками.
///
/// Извлечение текстур повторяет проверенную связку из ModelIntake:
/// ExtractTextures + переименование в .png + досвязка материала. Без этих
/// трех шагов модели приходят серыми (найдено в прогоне 7, узел 0).
/// </summary>
public static class GalleryBuilder
{
    private const string AssetsDir = "Assets/OurAssets";
    private const string ScenePath = "Assets/Scenes/Gallery.unity";
    private const float Step = 3.6f;
    private const int Cols = 6;

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".tga" };

    public static void Build()
    {
        string[] models = Directory
            .GetFiles(AssetsDir, "*.fbx")
            .Select(p => p.Replace('\\', '/'))
            .OrderBy(p => p)
            .ToArray();
        Debug.Log($"галерея: найдено моделей {models.Length}");

        foreach (string path in models)
            PrepareImport(path);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildGround(models.Length);
        BuildLight();

        for (int i = 0; i < models.Length; i++)
        {
            string path = models[i];
            string name = Path.GetFileNameWithoutExtension(path);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"галерея: {name} не импортировался, пропуск");
                continue;
            }

            int col = i % Cols;
            int row = i / Cols;
            var pos = new Vector3(
                (col - (Cols - 1) / 2f) * Step,
                0f,
                -row * Step
            );

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;
            instance.transform.position = pos;
            EnsureTextured(instance, path);

            var label = new GameObject($"label_{name}");
            var text = label.AddComponent<TextMesh>();
            text.text = name;
            text.fontSize = 48;
            text.characterSize = 0.06f;
            text.anchor = TextAnchor.MiddleCenter;
            text.color = new Color(0.95f, 0.93f, 0.9f);
            label.transform.position = pos + new Vector3(0f, -0.05f, 1.55f);
            label.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        BuildCamera(models.Length);

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"галерея: сцена сохранена {ScenePath}");
    }

    private static void PrepareImport(string path)
    {
        if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
            return;

        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.addCollider = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.useFileScale = true;
        importer.globalScale = 1f;
        importer.SaveAndReimport();

        string textureDir = TextureDirFor(path);
        if (importer.ExtractTextures(textureDir))
        {
            AssetDatabase.Refresh();
            RenameExtractedTexturesToPng(textureDir);
            AssetDatabase.Refresh();
            importer.SaveAndReimport();
        }
    }

    private static string TextureDirFor(string modelPath) =>
        Path.Combine(
            Path.GetDirectoryName(modelPath) ?? AssetsDir,
            Path.GetFileNameWithoutExtension(modelPath) + "_textures"
        ).Replace('\\', '/');

    private static void RenameExtractedTexturesToPng(string dir)
    {
        if (!Directory.Exists(dir))
            return;
        foreach (string file in Directory.GetFiles(dir))
        {
            if (file.EndsWith(".meta"))
                continue;
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ImageExtensions.Contains(ext))
                continue;
            string newPath = file + ".png";
            if (File.Exists(newPath))
                continue;
            File.Move(file, newPath);
            string oldMeta = file + ".meta";
            if (File.Exists(oldMeta))
                File.Delete(oldMeta);
        }
    }

    private static void EnsureTextured(GameObject instance, string modelPath)
    {
        string dir = TextureDirFor(modelPath);
        if (!Directory.Exists(dir))
            return;
        string file = Directory
            .GetFiles(dir)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f)
            .FirstOrDefault();
        var texture = file == null
            ? null
            : AssetDatabase.LoadAssetAtPath<Texture2D>(file.Replace('\\', '/'));
        if (texture == null)
            return;

        foreach (var r in instance.GetComponentsInChildren<Renderer>())
        foreach (var mat in r.sharedMaterials)
        {
            if (mat == null)
                continue;
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", texture);
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", texture);
        }
    }

    private static void BuildGround(int count)
    {
        int rows = (count + Cols - 1) / Cols;
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "grunt";
        float depth = rows * Step + 8f;
        ground.transform.position = new Vector3(0f, -0.02f, -depth / 2f + Step * 1.5f);
        ground.transform.localScale = new Vector3((Cols * Step + 8f) / 10f, 1f, depth / 10f);
        var mat = new Material(Shader.Find("Standard"))
        {
            color = new Color(0.55f, 0.30f, 0.21f)
        };
        mat.SetFloat("_Glossiness", 0f);
        ground.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(ground.GetComponent<Collider>());
    }

    private static void BuildLight()
    {
        var sun = new GameObject("sun");
        var l = sun.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.15f;
        l.color = new Color(1f, 0.96f, 0.9f);
        l.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(48f, 38f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.36f, 0.34f);
    }

    private static void BuildCamera(int count)
    {
        int rows = (count + Cols - 1) / Cols;
        var cam = new GameObject("camera").AddComponent<Camera>();
        cam.transform.position = new Vector3(0f, rows * 1.6f + 6f, 7f);
        cam.transform.rotation = Quaternion.Euler(52f, 180f, 0f);
        cam.backgroundColor = new Color(0.24f, 0.16f, 0.14f);
        cam.clearFlags = CameraClearFlags.SolidColor;
    }
}
