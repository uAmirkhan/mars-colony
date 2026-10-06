using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Разовая диагностика: почему новые модели из Assets/OurAssets/v2/ встают в
/// сцену повернутыми не той осью (купол лежит плоским диском вместо купола).
///
/// Печатает для каждой модели v2: габарит AABB в мировых координатах Unity
/// после инстанцирования с identity-трансформом, локальный поворот корневого
/// узла внутри префаба и ключевые настройки импортера FBX. Ничего не меняет.
/// </summary>
public static class OsDiag
{
    private const string Dir = "Assets/OurAssets/v2";

    // Контрольный список для сверки: та же геометрия в старой корневой
    // библиотеке (тот же конвейер glb-to-fbx.py, более ранний прогон), в
    // lite/ (независимый конвейер decimate) и явно НЕ-tripo модель burovaya-01
    // для контраста.
    private static readonly string[] ExtraPaths =
    {
        "Assets/OurAssets/kupol-geodezicheskiy.fbx",
        "Assets/OurAssets/zhiloy-bashnya.fbx",
        "Assets/OurAssets/burovaya-01.fbx",
        "Assets/OurAssets/lite/dekor-01.fbx",
    };

    [MenuItem("Mars/Diag os modeley")]
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("модель | AABB world (x,y,z) | root local rot (euler) | root local scale | bakeAxis | useFileScale | fileScale");

        var files = Directory.GetFiles(Dir, "*.fbx").OrderBy(p => p)
            .Concat(ExtraPaths.Where(File.Exists));

        foreach (var path in files)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string unityPath = path.Replace('\\', '/');
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(unityPath);
            if (asset == null)
            {
                sb.AppendLine($"{name} | НЕ ЗАГРУЖЕН ({unityPath})");
                continue;
            }

            // НЕ трогаем transform после Instantiate — именно он несёт то, что
            // задал импортер FBX на корне иерархии, и это ровно то, что нужно
            // измерить. Место в мировых координатах — (0,0,0), т.к. свежий
            // Instantiate от префаба с нулевым transform.
            var go = Object.Instantiate(asset);

            Bounds? acc = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (acc == null) acc = r.bounds;
                else { var t = acc.Value; t.Encapsulate(r.bounds); acc = t; }
            }

            var importer = AssetImporter.GetAtPath(unityPath) as ModelImporter;
            var ci = CultureInfo.InvariantCulture;

            string aabb = acc.HasValue
                ? string.Format(ci, "{0:0.000}x{1:0.000}x{2:0.000}", acc.Value.size.x, acc.Value.size.y, acc.Value.size.z)
                : "нет рендереров";

            var rootChild = go.transform.childCount > 0 ? go.transform.GetChild(0) : go.transform;

            string Fmt(Vector3 v) => string.Format(ci, "({0:0.0},{1:0.0},{2:0.0})", v.x, v.y, v.z);

            sb.AppendLine(
                $"{name} | AABB={aabb} | go.rot={Fmt(go.transform.localRotation.eulerAngles)} " +
                $"child0.rot={Fmt(rootChild.localRotation.eulerAngles)} (child0={rootChild.name}) | " +
                $"go.scale={Fmt(go.transform.localScale)} child0.scale={Fmt(rootChild.localScale)} | " +
                $"bakeAxis={importer?.bakeAxisConversion} useFileScale={importer?.useFileScale} fileScale={importer?.fileScale.ToString(ci)}");

            // Полная иерархия — до трёх уровней, чтобы увидеть, где именно
            // сидит компенсирующий поворот (частый паттерн FBX из Blender:
            // поворот на promежуточном узле, а не на самом корне).
            void Dump(Transform t, int depth)
            {
                if (depth > 3) return;
                sb.AppendLine($"    {new string('.', depth * 2)}{t.name} rot={Fmt(t.localRotation.eulerAngles)} scale={Fmt(t.localScale)} pos={Fmt(t.localPosition)}");
                foreach (Transform c in t) Dump(c, depth + 1);
            }
            Dump(go.transform, 0);

            Object.DestroyImmediate(go);
        }

        string report = sb.ToString();
        File.WriteAllText("os-diag.md", report);
        Debug.Log("[os-diag] " + report);
        Debug.Log("[os-diag] записан os-diag.md");
    }
}
