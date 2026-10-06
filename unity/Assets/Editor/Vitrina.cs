// Выкладка библиотеки моделей рядом с колонией и сохранение ручной
// расстановки владельца между пересборками.
//
// Владелец 2026-08-16: «можешь отдельно за ландшафтом неподалеку разместить
// все ассеты, которые мы разрабатывали. Я сам вручную хотел бы оставить пару
// штук, чтобы дать тебе пример видения».
//
// Отсюда две части, и вторая важнее первой. Сборщик колонии создает СЦЕНУ С
// НУЛЯ — всё, что владелец подвинул руками, он снесет молча. Поэтому ручная
// расстановка живет в отдельном корне RUCHNOE, выгружается на диск и
// возвращается после каждой пересборки.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Vitrina
{
    private const string Dir = "Assets/OurAssets";
    private const string ScenePath = "Assets/Scenes/ColonyOurs.unity";
    public const string RootName = "RUCHNOE";
    private const string StateFile = "ruchnaya-rasstanovka.json";

    /// <summary>
    /// Выкладывает КАЖДУЮ модель библиотеки на ровную полку к юго-западу от
    /// колонии, рядами, с подписью расстояния между рядами.
    ///
    /// Масштаб тот же, что в колонии (по длинной горизонтали после среза
    /// плиты), чтобы выкладка показывала модели такими, какими они встанут в
    /// сцену, а не приведенными к одному размеру.
    /// </summary>
    [MenuItem("Mars/Vylozhit vitrinu")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var old = GameObject.Find("vitrina");
        if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject("vitrina");

        var files = new List<string>(Directory.GetFiles(Dir, "*.fbx"));
        files.Sort();

        // Полка стоит за краем застройки: колония занимает примерно ±23 м,
        // поэтому выкладка уходит на юг, где пусто.
        const float X0 = -26f, Z0 = -30f;
        const float Step = 9f;
        const int Cols = 7;

        int i = 0;
        foreach (var path in files)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/{name}.fbx");
            if (asset == null) continue;

            float x = X0 + (i % Cols) * Step;
            float z = Z0 - (i / Cols) * Step;
            i++;

            var go = Object.Instantiate(asset);
            go.name = name;
            go.transform.SetParent(root.transform, true);

            var mat = ModelMaterials.For(name);
            if (mat != null)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var arr = r.sharedMaterials;
                    for (int k = 0; k < arr.Length; k++) arr[k] = mat;
                    r.sharedMaterials = arr;
                }

            var b = Gabarit(go);
            float k2 = 5.0f / Mathf.Max(b.size.x, b.size.z, 0.01f);
            go.transform.localScale *= k2;

            b = Gabarit(go);
            float y = 0f;
            if (Physics.Raycast(new Vector3(x, 60f, z), Vector3.down, out var hit, 160f))
                y = hit.point.y;
            go.transform.position += new Vector3(x - b.center.x, y - b.min.y, z - b.center.z);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"витрина: выложено {i} моделей от ({X0}, {Z0}), шаг {Step} м. "
                + "Двигать можно как угодно — то, что перенесешь в корень "
                + $"«{RootName}», переживет пересборку колонии.");
    }

    /// <summary>
    /// Заводит пустой корень для ручной расстановки, если его еще нет.
    /// Всё, что владелец положит внутрь, сборщик колонии сохранит и вернет.
    /// </summary>
    [MenuItem("Mars/Sozdat koren RUCHNOE")]
    public static void CreateRoot()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (GameObject.Find(RootName) == null)
        {
            new GameObject(RootName);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        Debug.Log($"корень «{RootName}» готов. Перетаскивай в него всё, что "
                + "расставил руками, — пересборка колонии это не тронет.");
    }

    // ---- сохранение и возврат ручной расстановки ---------------------------

    [System.Serializable]
    private class Zapis
    {
        public string asset;
        public Vector3 pos;
        public Vector3 rot;
        public Vector3 scale;
    }

    [System.Serializable]
    private class Spisok { public List<Zapis> items = new List<Zapis>(); }

    /// <summary>Выгружает содержимое RUCHNOE на диск. Вызывается сборщиком
    /// колонии ДО того, как он создаст сцену с нуля.</summary>
    public static void Sohranit()
    {
        var root = GameObject.Find(RootName);
        if (root == null) return;

        var sp = new Spisok();
        foreach (Transform t in root.transform)
        {
            // Имя объекта = имя ассета: так его кладет и витрина, и сборщик.
            string asset = t.name;
            if (!File.Exists($"{Dir}/{asset}.fbx")) continue;
            sp.items.Add(new Zapis
            {
                asset = asset,
                pos = t.position,
                rot = t.eulerAngles,
                scale = t.localScale,
            });
        }
        File.WriteAllText(StateFile, JsonUtility.ToJson(sp, true));
        Debug.Log($"ручная расстановка: сохранено {sp.items.Count} объектов "
                + $"в {StateFile}");
    }

    /// <summary>Возвращает ручную расстановку в свежесобранную сцену.</summary>
    public static void Vernut()
    {
        if (!File.Exists(StateFile)) return;
        var sp = JsonUtility.FromJson<Spisok>(File.ReadAllText(StateFile));
        if (sp == null || sp.items.Count == 0) return;

        var root = new GameObject(RootName);
        foreach (var z in sp.items)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/{z.asset}.fbx");
            if (asset == null) continue;
            var go = Object.Instantiate(asset);
            go.name = z.asset;
            go.transform.SetParent(root.transform, true);
            go.transform.position = z.pos;
            go.transform.eulerAngles = z.rot;
            go.transform.localScale = z.scale;

            var mat = ModelMaterials.For(z.asset);
            if (mat != null)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var arr = r.sharedMaterials;
                    for (int k = 0; k < arr.Length; k++) arr[k] = mat;
                    r.sharedMaterials = arr;
                }
        }
        Debug.Log($"ручная расстановка: возвращено {sp.items.Count} объектов");
    }

    private static Bounds Gabarit(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var acc = rs[0].bounds;
        foreach (var r in rs) acc.Encapsulate(r.bounds);
        return acc;
    }
}
