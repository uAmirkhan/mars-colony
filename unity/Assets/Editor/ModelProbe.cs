// Замер библиотеки моделей: габарит целиком, габарит тела без плиты и их
// отношение. Написан после провала разводки 2026-08-16: нормировка масштаба
// по телу постройки раздула плиту-основание до 24 метров, и 126 объектов
// налезли друг на друга. Причина была в предположении о пропорции плиты,
// которое ни разу не мерили. Этот файл закрывает предположение числом.
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class ModelProbe
{
    private const string Dir = "Assets/OurAssets";

    [MenuItem("Mars/Zamer modeley")]
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Замер моделей: тело против плиты");
        sb.AppendLine();
        sb.AppendLine("Габарит в метрах модели как она лежит на диске, без масштаба.");
        sb.AppendLine("«Тело» — геометрия выше нижних 16% высоты.");
        sb.AppendLine();
        sb.AppendLine("| модель | габарит X×Y×Z | тело X×Z | плита/тело | h/w | тре-ки |");
        sb.AppendLine("|---|---|---|---|---|---|");

        foreach (var path in Directory.GetFiles(Dir, "*.fbx"))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/{name}.fbx");
            if (asset == null) continue;

            var go = Object.Instantiate(asset);
            var full = Gabarit(go, float.NegativeInfinity);
            float yCut = full.min.y + 0.16f * full.size.y;
            var body = Gabarit(go, yCut);

            float wFull = Mathf.Max(full.size.x, full.size.z);
            float wBody = Mathf.Max(body.size.x, body.size.z);
            float k = wBody > 0.001f ? wFull / wBody : 0f;
            float hw = wFull > 0.001f ? full.size.y / wFull : 0f;

            int tris = 0;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;

            sb.AppendLine($"| {name} "
                + $"| {full.size.x:0.00}×{full.size.y:0.00}×{full.size.z:0.00} "
                + $"| {body.size.x:0.00}×{body.size.z:0.00} "
                + $"| **{k:0.00}** | {hw:0.00} | {tris} |");

            Object.DestroyImmediate(go);
        }

        File.WriteAllText("zamer-modeley.md", sb.ToString());
        Debug.Log("замер моделей: записан zamer-modeley.md");
    }

    /// <summary>Габарит по вершинам выше отсечки. Renderer.bounds не годится:
    /// он даёт AABB целиком, а нужен именно корпус без стелющегося фартука.</summary>
    private static Bounds Gabarit(GameObject go, float yCut)
    {
        bool any = false;
        var res = new Bounds();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var m = mf.transform.localToWorldMatrix;
            foreach (var v in mf.sharedMesh.vertices)
            {
                var w = m.MultiplyPoint3x4(v);
                if (w.y < yCut) continue;
                if (!any) { res = new Bounds(w, Vector3.zero); any = true; }
                else res.Encapsulate(w);
            }
        }
        return res;
    }
}
