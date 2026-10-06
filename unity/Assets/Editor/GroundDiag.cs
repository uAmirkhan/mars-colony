using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Почему земля темная. Замеряет три вещи, каждая из которых объясняет темноту
/// по-своему, и потому их надо различать, а не гадать:
///   1) куда смотрят нормали меша — если вниз, прямой свет не достает вовсе;
///   2) сколько света реально приходит на верхнюю грань от текущего солнца;
///   3) что в сцене вообще способно отбрасывать тень на всю площадь.
/// </summary>
public static class GroundDiag
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/ColonyOurs.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();

        var ground = GameObject.Find("zemlya");
        if (ground == null) { File.WriteAllText("ground-diag.txt", "объекта zemlya нет"); return; }

        var mesh = ground.GetComponent<MeshFilter>().sharedMesh;
        var n = mesh.normals;
        sb.AppendLine($"вершин: {mesh.vertexCount}, нормалей: {n.Length}, субмешей: {mesh.subMeshCount}");

        int up = 0, down = 0, side = 0;
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < n.Length; i++)
        {
            sum += n[i];
            if (n[i].y > 0.3f) up++;
            else if (n[i].y < -0.3f) down++;
            else side++;
        }
        sb.AppendLine($"нормали вверх: {up}, вниз: {down}, вбок: {side}");
        sb.AppendLine($"средняя нормаль: {(n.Length > 0 ? (sum / n.Length).ToString("F3") : "-")}");

        // Свет, который приходит на горизонтальную грань от текущего солнца.
        Light sun = null;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional && (sun == null || l.intensity > sun.intensity)) sun = l;

        if (sun != null)
        {
            var toSun = -sun.transform.forward;
            float ndl = Mathf.Max(0f, Vector3.Dot(Vector3.up, toSun));
            sb.AppendLine($"солнце: поворот={sun.transform.rotation.eulerAngles:F1} интенсивность={sun.intensity} тени={sun.shadows} сила={sun.shadowStrength}");
            sb.AppendLine($"направление НА солнце: {toSun:F3}");
            sb.AppendLine($"N*L для горизонтальной грани: {ndl:F3}  <- ноль означает, что прямой свет землю не достает");
        }
        else sb.AppendLine("направленного света в сцене НЕТ");

        sb.AppendLine($"ambientMode={RenderSettings.ambientMode} sky={RenderSettings.ambientSkyColor} ground={RenderSettings.ambientGroundColor} intensity={RenderSettings.ambientIntensity}");
        sb.AppendLine($"туман: {RenderSettings.fog} цвет={RenderSettings.fogColor} плотность={RenderSettings.fogDensity}");

        // Кто способен затенить всю площадь: крупный объект с включенной тенью.
        sb.AppendLine();
        sb.AppendLine("=== крупные объекты, отбрасывающие тень ===");
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off) continue;
            var s = r.bounds.size;
            if (s.x > 60f || s.z > 60f || s.y > 60f)
                sb.AppendLine($"{r.gameObject.name} | габарит={s:F1} | тень={r.shadowCastingMode}");
        }

        File.WriteAllText("ground-diag.txt", sb.ToString());
        Debug.Log("[ground-diag] записано");
    }
}
