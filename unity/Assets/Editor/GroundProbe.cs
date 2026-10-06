using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Решающий опыт: заливает материалы земли плоским ярким цветом без текстуры и
/// пересиимает общий план. Если поверхность станет ровно этим цветом — значит
/// материал земли действительно тот, что мы думаем, и виновата текстура или ее
/// мип. Если рябь останется — в кадре не земля, а что-то другое поверх нее.
///
/// Диагностика по ассетам этот вопрос закрыть не может: она читает материал, а
/// не то, что физически попало в пиксель.
/// </summary>
public static class GroundProbe
{
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/ColonyOurs.unity", OpenSceneMode.Single);

        var ground = GameObject.Find("zemlya");
        if (ground == null) { Debug.LogError("[probe] zemlya не найдена"); return; }

        var mr = ground.GetComponent<MeshRenderer>();
        var mats = mr.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            var m = new Material(mats[i]);
            m.SetTexture("_BaseMap", null);
            m.SetColor("_BaseColor", new Color(0f, 1f, 0f)); // ядовито-зеленый: в палитре сцены его нет
            mats[i] = m;
        }
        mr.sharedMaterials = mats;
        Debug.Log($"[probe] материалов земли перекрашено: {mats.Length}");

        var camGo = GameObject.Find("kamera");
        var cam = camGo != null ? camGo.GetComponent<Camera>() : null;
        if (cam == null) { Debug.LogError("[probe] камеры нет"); return; }

        // Та же точка съемки, что у общего плана в ColonyOursBuilder.
        Shoot(cam, new Vector3(1.5f, 0.4f, -0.5f), -52f, 30f, 30f, 34f, "probe-ground.png");
        Debug.Log("[probe] кадр снят");
    }

    private static void Shoot(Camera cam, Vector3 target, float azDeg, float elDeg,
                              float dist, float fov, string file)
    {
        float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
        var dir = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az));
        cam.transform.position = target + dir * dist;
        cam.transform.rotation = Quaternion.LookRotation(target - cam.transform.position);
        cam.fieldOfView = fov;

        var rt = new RenderTexture(1600, 1000, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tx = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        tx.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
        tx.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        File.WriteAllBytes(file, tx.EncodeToPNG());
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tx);
    }
}
