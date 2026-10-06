using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Портреты моделей: каждая снимается ОТДЕЛЬНЫМ файлом, одна в кадре.
///
/// Зачем отдельным. Ряд в одном кадре обманывает дважды: при развороте камеры
/// 45 градусов порядок на экране не совпадает с порядком в коде, и разобрать,
/// где какой вариант, нельзя; а приведение всех к одной высоте делает
/// космонавта ростом с ангар.
///
/// Здесь одна модель на кадр, имя файла - имя модели. Смотреть глазами и
/// заполнять таблицу наклонов в V2Lib.
/// </summary>
public static class V2Portrety
{
    public static void Snyat()
    {
        string spisok = Arg("-modeli", "");
        string outDir = Arg("-out", "../mars-colony/loop/scene-v2/portrety");
        Directory.CreateDirectory(Path.GetFullPath(outDir));

        if (string.IsNullOrEmpty(spisok))
        {
            var f = Path.GetFullPath("../mars-colony/loop/scene-v2/vseassety-spisok.txt");
            if (!File.Exists(f)) { Debug.LogError("[v2] нет списка моделей"); return; }
            spisok = string.Join(",", File.ReadAllLines(f));
        }

        foreach (var syroe in spisok.Split(','))
        {
            var imya = syroe.Trim();
            if (imya.Length == 0) continue;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var pol = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pol.transform.localScale = Vector3.one * 8f;
            pol.GetComponent<MeshRenderer>().sharedMaterial =
                V2Lib.NewLit(new Color(0.55f, 0.54f, 0.53f), null, 0.05f, imya: "port-pol");

            // Ставим В ТОМ ЖЕ виде, что и в сцене: через Place, с текущей
            // таблицей наклонов. Портрет должен показывать то, что реально
            // попадет в кадр, а не идеальную модель из файла.
            //
            // С -naklon снимается ПРОБНЫЙ разворот по X: так подбирается строка
            // таблицы наклонов для модели, которая приезжает лежащей.
            GameObject go;
            string naklonArg = Arg("-naklon", "");
            if (!string.IsNullOrEmpty(naklonArg))
            {
                float.TryParse(naklonArg, out float nx);
                var src = V2Lib.FindModel(imya);
                if (src == null) { Debug.LogWarning($"[v2] нет модели {imya}"); continue; }
                go = (GameObject)PrefabUtility.InstantiatePrefab(src);
                go.transform.rotation = Quaternion.Euler(nx, 0f, 0f);
                if (V2Lib.WorldBounds(go, out var bb) && bb.size.y > 1e-4f)
                {
                    go.transform.localScale = Vector3.one * (6f / bb.size.y);
                    V2Lib.WorldBounds(go, out bb);
                    var pz = go.transform.position;
                    pz.x -= bb.center.x; pz.z -= bb.center.z; pz.y -= bb.min.y;
                    go.transform.position = pz;
                }
                V2Lib.ApplyTexture(go, imya);
            }
            else
            {
                go = V2Lib.Place(imya, Vector2.zero, 6f, 0f);
                if (go == null) { Debug.LogWarning($"[v2] нет модели {imya}"); continue; }
            }

            // Линейка: фигура 1.8 м рядом, чтобы масштаб был виден на портрете.
            // Линейка 1.8 м ставится ПОСЛЕ того, как известен габарит модели:
            // иначе она либо влезает в объект, либо уезжает за кадр.
            V2Lib.WorldBounds(go, out var gab);
            var kub = GameObject.CreatePrimitive(PrimitiveType.Cube);
            kub.name = "linejka-1m80";
            kub.transform.localScale = new Vector3(0.5f, 1.8f, 0.5f);
            kub.transform.position = new Vector3(gab.max.x + 1.5f, 0.9f, gab.center.z);
            kub.GetComponent<MeshRenderer>().sharedMaterial =
                V2Lib.NewLit(new Color(0.95f, 0.35f, 0.15f), null, 0.1f, imya: "port-linejka");

            var sun = new GameObject("Solnce").AddComponent<UnityEngine.Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.4f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45f, 200f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.48f, 0.48f, 0.50f);

            // КАМЕРА НАВОДИТСЯ НА ОБЪЕКТ, а не стоит на фиксированном охвате.
            //
            // Дважды подряд портреты выходили негодными: модель занимала
            // десятую часть кадра или вовсе улетала точкой к краю. Причина одна
            // и та же - охват задавался числом наугад, а модели разного размера.
            // Наводка по габариту убирает угадывание совсем.
            V2Lib.WorldBounds(go, out var ramka);
            var centr = ramka.center;
            var cam = V2Lib.MakeCamera(centr);
            float ohvat = Mathf.Max(ramka.size.x, ramka.size.y, ramka.size.z);
            cam.orthographicSize = Mathf.Max(2f, ohvat * 0.85f);
            cam.backgroundColor = new Color(0.30f, 0.31f, 0.34f);
                        string suff = string.IsNullOrEmpty(naklonArg) ? "" : "-X" + naklonArg;
            V2Lib.Shoot(cam, Path.Combine(Path.GetFullPath(outDir), imya + suff + ".png"));
        }
        Debug.Log("[v2] портреты сняты в " + Path.GetFullPath(outDir));
    }

    private static string Arg(string key, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
        return def;
    }
}
