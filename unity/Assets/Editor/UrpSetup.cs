using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Перевод проекта на URP: ассет конвейера, рендерер, линейное пространство,
/// конвертация материалов, каркас профиля постобработки.
///
/// Запускается один раз из batchmode:
///   Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod UrpSetup.Run -logFile urp-setup.log
///
/// Свет НЕ настраивает. Профиль MarsDay создается пустым — его наполняет
/// художник по свету, иначе числа появятся дважды и разойдутся.
/// </summary>
public static class UrpSetup
{
    private const string Dir = "Assets/Settings";
    private const string PipelinePath = Dir + "/MarsUrpAsset.asset";
    private const string RendererPath = Dir + "/MarsUrpRenderer.asset";
    private const string ProfilePath = Dir + "/MarsDay.asset";

    public static void Run()
    {
        Directory.CreateDirectory(Dir);

        var renderer = MakeRenderer();
        var pipeline = MakePipeline(renderer);
        Assign(pipeline);
        MakeProfile();
        int converted = ConvertMaterials();

        // Линейное пространство ставится последним: оно тянет переимпорт всех
        // текстур, и делать его до создания ассетов значит ждать дважды.
        if (PlayerSettings.colorSpace != ColorSpace.Linear)
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            Debug.Log("[urp-setup] цветовое пространство переведено в Linear");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[urp-setup] готово: материалов сконвертировано {converted}");
    }

    private static UniversalRendererData MakeRenderer()
    {
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (data != null) return data;

        data = ScriptableObject.CreateInstance<UniversalRendererData>();
        data.renderingMode = RenderingMode.ForwardPlus;
        AssetDatabase.CreateAsset(data, RendererPath);
        Debug.Log("[urp-setup] создан рендерер Forward+");
        return data;
    }

    private static UniversalRenderPipelineAsset MakePipeline(UniversalRendererData renderer)
    {
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (asset != null) return asset;

        asset = UniversalRenderPipelineAsset.Create(renderer);
        asset.supportsHDR = true;
        asset.msaaSampleCount = 4;
        asset.shadowDistance = 120f;
        asset.shadowCascadeCount = 2;
        asset.supportsCameraDepthTexture = true;
        AssetDatabase.CreateAsset(asset, PipelinePath);
        Debug.Log("[urp-setup] создан ассет конвейера");
        return asset;
    }

    private static void Assign(UniversalRenderPipelineAsset pipeline)
    {
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        Debug.Log("[urp-setup] конвейер прописан в Graphics и Quality");
    }

    private static void MakeProfile()
    {
        if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath) != null) return;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, ProfilePath);
        Debug.Log("[urp-setup] создан пустой профиль MarsDay (наполняет художник по свету)");
    }

    /// <summary>
    /// Standard -> URP/Lit с переносом свойств. Штатный конвертер живет в окне
    /// редактора и в batchmode не открывается, поэтому свойства переносим руками.
    /// </summary>
    private static int ConvertMaterials()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Debug.LogError("[urp-setup] шейдер URP/Lit не найден — пакет не встал");
            return 0;
        }

        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader == null) continue;

            string name = m.shader.name;
            if (name.StartsWith("Universal Render Pipeline/")) continue;
            if (!(name == "Standard" || name == "Standard (Specular setup)" ||
                  name.StartsWith("Legacy Shaders/") || name.StartsWith("Mobile/") ||
                  name == "Unlit/Texture" || name == "Unlit/Color"))
                continue;

            var carried = Carry(m);
            m.shader = lit;
            Apply(m, carried);
            EditorUtility.SetDirty(m);
            n++;
        }

        Debug.Log($"[urp-setup] материалов переведено на URP/Lit: {n}");
        return n;
    }

    private struct Carried
    {
        public Texture main;
        public Vector2 scale, offset;
        public Color color;
        public float metallic, smoothness;
        public Texture normal, occlusion;
    }

    private static Carried Carry(Material m)
    {
        var c = new Carried
        {
            color = Color.white,
            metallic = 0f,
            // Township-вид: матовые поверхности без металлического отблеска.
            // Точное значение доводит художник по материалам, здесь только пол.
            smoothness = 0.1f,
        };

        if (m.HasProperty("_MainTex"))
        {
            c.main = m.GetTexture("_MainTex");
            c.scale = m.GetTextureScale("_MainTex");
            c.offset = m.GetTextureOffset("_MainTex");
        }
        if (m.HasProperty("_Color")) c.color = m.GetColor("_Color");
        if (m.HasProperty("_Metallic")) c.metallic = m.GetFloat("_Metallic");
        if (m.HasProperty("_Glossiness")) c.smoothness = m.GetFloat("_Glossiness");
        if (m.HasProperty("_BumpMap")) c.normal = m.GetTexture("_BumpMap");
        if (m.HasProperty("_OcclusionMap")) c.occlusion = m.GetTexture("_OcclusionMap");
        return c;
    }

    private static void Apply(Material m, Carried c)
    {
        if (c.main != null)
        {
            m.SetTexture("_BaseMap", c.main);
            m.SetTextureScale("_BaseMap", c.scale == Vector2.zero ? Vector2.one : c.scale);
            m.SetTextureOffset("_BaseMap", c.offset);
        }
        m.SetColor("_BaseColor", c.color);
        m.SetFloat("_Metallic", c.metallic);
        m.SetFloat("_Smoothness", c.smoothness);
        if (c.normal != null)
        {
            m.SetTexture("_BumpMap", c.normal);
            m.EnableKeyword("_NORMALMAP");
        }
        if (c.occlusion != null) m.SetTexture("_OcclusionMap", c.occlusion);
    }
}
