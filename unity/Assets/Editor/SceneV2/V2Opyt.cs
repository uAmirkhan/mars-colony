using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Опыт с облегчением моделей. Ставит исходник и облегченные копии в ряд и
/// снимает В ТОМ МАСШТАБЕ, в каком они попадут в настоящий кадр. Вопрос не
/// «сколько треугольников осталось», а «видно ли разницу на экране».
/// </summary>
public static class V2Opyt
{
    public static void Ryad()
    {
        string bazovoe = Arg("-model", "angar-s-panelyami");
        string outPath = Arg("-out", "../mars-colony/loop/scene-v2/kadry/opyt.png");
        float vysota = 7f;

        // Каждый вариант снимается ОТДЕЛЬНО одной и той же камерой из одной и
        // той же точки. Ряд в одном кадре не годится: разное место в кадре
        // означает разный свет и разный ракурс, и разница варианта мешается с
        // разницей положения.
        string[] varianty = { bazovoe, bazovoe + "-dec30", bazovoe + "-dec15",
                              bazovoe + "-dec08" };

        foreach (var v in varianty)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var pol = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pol.name = "pol";
            pol.transform.localScale = Vector3.one * 30f;
            pol.GetComponent<MeshRenderer>().sharedMaterial =
                V2Lib.NewLit(new Color(0.78f, 0.42f, 0.28f), null, 0.05f, imya: "opyt-pol-" + v);

            var go = V2Lib.Place(v, Vector2.zero, vysota, 25f);
            int tris = 0;
            if (go != null)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;

            var sun = new GameObject("Solnce").AddComponent<UnityEngine.Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.5f;
            sun.color = new Color(1f, 0.94f, 0.85f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, 200f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.50f, 0.48f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.33f, 0.28f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.18f, 0.13f);

            var cam = V2Lib.MakeCamera(Vector3.zero);
            cam.orthographicSize = 9f;    // ангар займет долю кадра как в сцене
            V2Lib.Log($"{v}: {tris} треугольников");
            V2Lib.Shoot(cam, Path.GetFullPath(outPath).Replace(".png", $"-{v}.png"));
        }
    }

    private static string Arg(string key, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == key) return a[i + 1];
        return def;
    }
}
