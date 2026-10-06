using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Что реально стоит под конкретным пикселем кадра. Замер цвета по картинке
/// говорит «темно», но не говорит ЧЕМ: субмеш грунта, дорога, колея или тень.
/// Луч из камеры через тот же пиксель отвечает на это однозначно.
/// </summary>
public static class RayDiag
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/ColonyOurs.unity", OpenSceneMode.Single);

        var camGo = GameObject.Find("kamera");
        var cam = camGo != null ? camGo.GetComponent<Camera>() : Camera.main;
        if (cam == null) { File.WriteAllText("ray-diag.txt", "камеры нет"); return; }

        // Кадр снимается в 1600x1000 — те же координаты, что в замере пикселей.
        const int W = 1600, H = 1000;
        var sb = new StringBuilder();
        sb.AppendLine($"камера: поз={cam.transform.position:F1} ортографическая={cam.orthographic} size={cam.orthographicSize}");

        var pts = new (string name, int x, int y)[]
        {
            ("zemlya-lev", 200, 700), ("zemlya-prav", 1400, 650),
            ("ploshchadka", 700, 430), ("kamen", 250, 620), ("centr", 800, 500),
        };

        foreach (var (name, px, py) in pts)
        {
            // Пиксели картинки считаются сверху, экранные координаты — снизу.
            var ray = cam.ScreenPointToRay(new Vector3(px, H - py, 0));
            if (!Physics.Raycast(ray, out var hit, 5000f))
            {
                sb.AppendLine($"{name,-14} | луч ни во что не попал");
                continue;
            }

            var r = hit.collider.GetComponent<Renderer>();
            string mat = "нет рендерера";
            if (r != null)
            {
                // Какой именно субмеш: по индексу треугольника в меше.
                int sub = SubMeshOf(hit);
                var m = (sub >= 0 && sub < r.sharedMaterials.Length) ? r.sharedMaterials[sub] : r.sharedMaterial;
                var col = m != null && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.clear;
                var tex = m != null && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null
                          ? m.GetTexture("_BaseMap").name : "нет";
                mat = $"субмеш={sub} мат={(m != null ? m.name : "null")} цвет={col} карта={tex}";
            }

            sb.AppendLine($"{name,-14} | объект={hit.collider.gameObject.name} | нормаль={hit.normal:F2} | {mat}");
        }

        File.WriteAllText("ray-diag.txt", sb.ToString());
        Debug.Log("[ray-diag] записано");
    }

    private static int SubMeshOf(RaycastHit hit)
    {
        var mc = hit.collider as MeshCollider;
        if (mc == null || mc.sharedMesh == null) return 0;
        var mesh = mc.sharedMesh;
        int tri = hit.triangleIndex * 3;
        int acc = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            int count = (int)mesh.GetIndexCount(s);
            if (tri >= acc && tri < acc + count) return s;
            acc += count;
        }
        return 0;
    }
}
