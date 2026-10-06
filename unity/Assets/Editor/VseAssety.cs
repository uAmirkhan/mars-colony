using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Сцена-каталог: ВСЁ, что есть в библиотеке, разложенное рядами по папкам.
///
/// Зачем отдельной сценой. В колонии стоит только то, что нужно колонии, и
/// сравнить версии там нельзя: старый дрон и новый никогда не окажутся рядом.
/// Здесь они стоят в соседних рядах, и видно, что изменилось.
///
/// Ряды идут по папкам, и это главное свойство каталога:
///   OurAssets/       первая библиотека, старая генерация
///   lite/            её же облегчённые версии
///   v2/              партия fal.ai, платная
///   v3/              партии брата на TRELLIS, вторая и третья
///
/// ЦВЕТА ИСХОДНЫЕ. Тон материала сбрасывается в белый, цвет несёт только
/// текстура модели — никаких подкрасок, которыми колония сводит объекты к
/// общей палитре. Каталог показывает, что пришло от генератора, а не что мы
/// с этим сделали.
///
/// Размер нормируется: каждая модель вписывается в куб заданной высоты. Иначе
/// шаттл в двадцать метров и камешек в полметра не помещаются в один кадр, и
/// каталогом пользоваться нельзя.
/// </summary>
public static class VseAssety
{
    private const float Shag = 4.0f;        // шаг сетки между моделями
    private const float ShagRyada = 6.0f;   // отступ между папками
    private const float Vysota = 2.2f;      // к этой высоте нормируется каждая
    private const int VRyadu = 12;

    private static readonly (string papka, string metka)[] Papki =
    {
        ("Assets/OurAssets", "1-staraya-biblioteka"),
        ("Assets/OurAssets/lite", "2-lite-oblegchyonnye"),
        ("Assets/OurAssets/v2", "3-v2-fal-ai"),
        ("Assets/OurAssets/v3", "4-v3-trellis"),
    };

    [MenuItem("Mars/Sobrat katalog assetov")]
    public static void Sobrat()
    {
        // ПАПКИ ИМПОРТИРУЮТСЯ ЯВНО, до всякой загрузки.
        //
        // У только что сконвертированных файлов нет .meta, Unity их ни разу не
        // видела, и LoadAssetAtPath возвращает null МОЛЧА — ни строки в логе.
        // На этом уже горели с lite/ и v2/, и наступили снова: первый прогон
        // каталога выложил 88 моделей из 100, а двенадцать свежих пропустил.
        foreach (var (p, _) in Papki)
            if (Directory.Exists(p))
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ImportRecursive);
        AssetDatabase.Refresh();

        var scena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                                                NewSceneMode.Single);

        Svet();
        var fon = GameObject.CreatePrimitive(PrimitiveType.Plane);
        fon.name = "podlozhka";
        fon.transform.localScale = new Vector3(30f, 1f, 30f);
        fon.transform.position = new Vector3(20f, -0.01f, -20f);
        var matFon = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        matFon.SetColor("_BaseColor", new Color(0.62f, 0.62f, 0.64f));
        matFon.SetFloat("_Smoothness", 0f);
        fon.GetComponent<Renderer>().sharedMaterial = matFon;

        float z = 0f;
        int vsego = 0, netu = 0;

        foreach (var (papka, metka) in Papki)
        {
            if (!Directory.Exists(papka)) continue;
            var fayly = Directory.GetFiles(papka, "*.fbx")
                                 .OrderBy(f => Path.GetFileName(f)).ToArray();
            if (fayly.Length == 0) continue;

            var koren = new GameObject(metka);
            Podpis(metka, new Vector3(-4.5f, 0.3f, z), 0.55f, koren.transform);

            for (int i = 0; i < fayly.Length; i++)
            {
                string imya = Path.GetFileNameWithoutExtension(fayly[i]);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(
                    fayly[i].Replace('\\', '/'));
                if (asset == null) { netu++; continue; }

                int stolbec = i % VRyadu, stroka = i / VRyadu;
                var mesto = new Vector3(stolbec * Shag, 0f, z - stroka * Shag);

                var go = Object.Instantiate(asset);
                go.name = imya;
                go.transform.SetParent(koren.transform, false);
                // Поворот КОМПОЗИЦИЕЙ: у моделей из FBX на корне висит
                // корректирующий поворот осей Blender -> Unity, и присвоение
                // его стирает — модель ложится на бок.
                go.transform.rotation = Quaternion.Euler(0f, 0f, 0f) * go.transform.rotation;
                go.transform.position = mesto;

                Normirovat(go, Vysota);
                Tekstura(go, imya, papka);
                Podpis(imya, mesto + new Vector3(0f, 0f, -1.7f), 0.2f,
                       koren.transform);
                vsego++;
            }

            int strok = (fayly.Length + VRyadu - 1) / VRyadu;
            z -= strok * Shag + ShagRyada;
        }

        Kamera();
        var put = "Assets/Scenes/VseAssety.unity";
        EditorSceneManager.SaveScene(scena, put);
        Debug.Log($"[каталог] моделей выложено {vsego}, не загрузилось {netu}. "
                  + $"Сцена: {put}");
    }

    /// <summary>Вписать модель в куб заданной высоты, сохранив пропорции.</summary>
    private static void Normirovat(GameObject go, float vysota)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        float max = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (max < 1e-4f) return;
        float k = vysota / max;
        var mesto = go.transform.position;
        go.transform.localScale *= k;

        // Пересчитать габарит после масштаба и посадить низом на подложку
        b = go.GetComponentsInChildren<Renderer>()[0].bounds;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        go.transform.position = mesto + new Vector3(0f, mesto.y - b.min.y, 0f);
    }

    /// <summary>
    /// Текстура своя, тон белый. Копия материала берётся с импортированного,
    /// а тот несёт собственный базовый цвет из FBX и УМНОЖАЕТ его на карту —
    /// именно от этого жилая башня в колонии вышла ядовито-зелёной при
    /// серо-зелёной текстуре. В каталоге цвет обязан быть исходным.
    /// </summary>
    private static void Tekstura(GameObject go, string imya, string papka)
    {
        string dir = $"{papka}/{imya}_textures";
        if (!Directory.Exists(dir)) dir = $"Assets/OurAssets/{imya}_textures";
        if (!Directory.Exists(dir)) return;
        string file = Directory.GetFiles(dir)
            .FirstOrDefault(f =>
            {
                var e = Path.GetExtension(f).ToLowerInvariant();
                return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".tga";
            });
        if (file == null) return;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(file.Replace('\\', '/'));
        if (tex == null) return;

        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        var svoy = rends[0].sharedMaterial != null
            ? new Material(rends[0].sharedMaterial)
            : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        svoy.name = $"kat-{imya}";
        if (svoy.HasProperty("_BaseColor")) svoy.SetColor("_BaseColor", Color.white);
        if (svoy.HasProperty("_Color")) svoy.SetColor("_Color", Color.white);
        if (svoy.HasProperty("_MainTex")) svoy.SetTexture("_MainTex", tex);
        if (svoy.HasProperty("_BaseMap")) svoy.SetTexture("_BaseMap", tex);
        if (svoy.HasProperty("_Smoothness")) svoy.SetFloat("_Smoothness", 0f);
        foreach (var r in rends)
        {
            var nabor = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < nabor.Length; i++) nabor[i] = svoy;
            r.sharedMaterials = nabor;
        }
    }

    private static void Podpis(string text, Vector3 gde, float razmer,
                               Transform roditel)
    {
        var go = new GameObject("podpis-" + text);
        go.transform.SetParent(roditel, false);
        go.transform.position = gde;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.characterSize = razmer;
        tm.fontSize = 64;
        tm.anchor = TextAnchor.UpperCenter;
        tm.color = new Color(0.12f, 0.12f, 0.14f);
    }

    private static void Svet()
    {
        var go = new GameObject("solnce");
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.15f;
        l.color = new Color(1f, 0.97f, 0.92f);
        go.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.56f, 0.60f);
    }

    private static void Kamera()
    {
        var go = new GameObject("kamera");
        var c = go.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.70f, 0.71f, 0.74f);
        c.farClipPlane = 500f;
        go.transform.position = new Vector3(22f, 48f, -70f);
        go.transform.rotation = Quaternion.Euler(38f, 0f, 0f);
        go.tag = "MainCamera";
    }
}
