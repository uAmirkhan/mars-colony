using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Иконки кнопок хаба, снятые с настоящих моделей сцены.
///
/// Почему съемка, а не генерация. Иконку здания пытались получить у генератора
/// картинок: короткий промпт дает объемную игрушку в нужном языке, но цвет
/// уезжает (купол выходил розовым), в кадр лезут посторонние предметы, и
/// главное — нарисованное здание не совпадает с тем, которое реально
/// построится. Кнопка обязана показывать ровно ту постройку, что появится в
/// мире, иначе игрок нажимает вслепую. Снимок с модели дает это тождество
/// бесплатно и заодно наследует стиль сцены целиком: те же материалы, тот же
/// свет, та же степень условности.
///
/// Прозрачность берется двойным рендером, а не альфой буфера. В URP альфа
/// цветового буфера непредсказуема и часто приходит единицей, то есть фон
/// оказывается непрозрачным черным. Два прохода — по белому и по черному фону —
/// дают альфу арифметикой и работают в любом пайплайне:
///   на черном  Cb = C*a
///   на белом   Cw = C*a + (1-a)
///   отсюда     a = 1 - (Cw - Cb),  C = Cb / a
///
/// Запуск: меню Mars/Interfeys/Snyat ikonki
/// </summary>
public static class IkonkiIzModeley
{
    const string Scena = "Assets/Scenes/MAIN.unity";
    const string Vyhod = "Assets/UI/Ikonki";
    const int Razmer = 512;

    /// <summary>
    /// Что снимаем. Имя файла — договор со сборщиком интерфейса, имя объекта —
    /// подстрока, по которой узел ищется в сцене.
    /// </summary>
    /// Имена — точные имена объектов сцены, а не материалов. Первая версия
    /// списка была написана по строкам вида kras-zavod-pishchevoy-siniy-tishe:
    /// это имена КРАСОК (kras-), и по ним не нашлось ни одного объекта.
    ///
    /// Три иконки берутся с точных моделей. Две помечены как приблизительные:
    /// моделей атмосферного и текстильного модулей в сцене нет вообще, взяты
    /// ближайшие по силуэту. Это расхождение экономики со сценой, а не выбор
    /// художника, и лечится оно постройкой моделей, а не подбором иконки.
    static readonly (string fajl, string obekt, bool tochno)[] Spisok =
    {
        ("ikonka-kupol-gidroponiki", "kupol-geodezicheskiy", true),
        ("ikonka-pishchevoy-modul",  "zavod-pishchevoy",     true),
        ("ikonka-atmosferny-modul",  "modul-tonnelnyy",      false),
        ("ikonka-tekstilny-modul",   "kupol-grib",           false),
        ("ikonka-sklad",             "sklad-angar",          true),
    };

    /// <summary>
    /// Список построек сцены с габаритами. Нужен потому, что имена вида
    /// kras-zavod-pishchevoy оказались именами МАТЕРИАЛОВ (kras — краска), а не
    /// объектов, и съемка по ним не находила ничего.
    /// </summary>
    [MenuItem("Mars/Interfeys/Spisok postroek")]
    public static void Imena()
    {
        EditorSceneManager.OpenScene(Scena, OpenSceneMode.Single);
        var vidno = new List<string>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.parent != null && t.parent.parent != null) continue; // только верх и второй уровень
            var rs = t.GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0) continue;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.magnitude < 0.8f) continue;
            vidno.Add(string.Format("{0} | детей-рендереров {1} | габарит {2:F1}x{3:F1}x{4:F1} | путь {5}",
                                    t.name, rs.Length, b.size.x, b.size.y, b.size.z, Put(t)));
        }
        vidno.Sort();
        Debug.Log("[imena] построек с геометрией: " + vidno.Count);
        foreach (var s in vidno) Debug.Log("[imena] " + s);
    }

    static string Put(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    [MenuItem("Mars/Interfeys/Snyat ikonki")]
    public static void Snyat()
    {
        Directory.CreateDirectory(Vyhod);
        EditorSceneManager.OpenScene(Scena, OpenSceneMode.Single);

        // Съемочный павильон стоит далеко от колонии: объект переносится туда
        // целиком, чтобы в кадр не попали соседние постройки и грунт.
        var mesto = new Vector3(0f, 5000f, 0f);
        var studiya = new GameObject("studiya-ikonok");
        studiya.transform.position = mesto;

        var camGo = new GameObject("kamera-ikonok", typeof(Camera));
        camGo.transform.SetParent(studiya.transform, false);
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.cullingMask = ~0;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 500f;

        // Свет раздела 2 арт-библии: ключ сверху и чуть спереди, теней в кадре
        // иконки нет — контактная тень рисуется интерфейсом отдельно.
        var svetGo = new GameObject("svet-ikonok", typeof(Light));
        svetGo.transform.SetParent(studiya.transform, false);
        var svet = svetGo.GetComponent<Light>();
        svet.type = LightType.Directional;
        svet.intensity = 1.15f;
        svet.shadows = LightShadows.None;
        svetGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

        var ambient = RenderSettings.ambientLight;
        var ambientMode = RenderSettings.ambientMode;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.62f, 0.60f, 0.58f);

        int snyato = 0;
        foreach (var (fajl, obekt, tochno) in Spisok)
        {
            var istochnik = Nayti(obekt);
            if (istochnik == null) { Debug.LogWarning("[ikonki] не найден объект " + obekt); continue; }

            var kopiya = Object.Instantiate(istochnik);
            kopiya.name = "snimok-" + fajl;
            kopiya.transform.SetParent(studiya.transform, false);
            kopiya.transform.localRotation = istochnik.transform.rotation;
            kopiya.SetActive(true);

            var b = Gabarit(kopiya);
            if (b.size == Vector3.zero) { Debug.LogWarning("[ikonki] пустой габарит у " + obekt); Object.DestroyImmediate(kopiya); continue; }

            kopiya.transform.position += mesto - b.center;
            b = Gabarit(kopiya);

            // Три четверти спереди-сверху: тот же ракурс, что у иконок Township,
            // и тот же, под которым игрок видит постройку в сцене.
            var napravlenie = new Vector3(0.62f, 0.58f, -1f).normalized;
            float radius = b.extents.magnitude;
            cam.transform.position = b.center + napravlenie * (radius * 3f);
            cam.transform.LookAt(b.center);
            cam.orthographicSize = radius * 0.92f;

            var chern = Kadr(cam, Color.black);
            var bel = Kadr(cam, Color.white);
            var itog = Sobrat(chern, bel);
            var obrez = Obrezat(itog, 0.06f);

            File.WriteAllBytes(Vyhod + "/" + fajl + ".png", obrez.EncodeToPNG());
            Object.DestroyImmediate(chern); Object.DestroyImmediate(bel);
            Object.DestroyImmediate(itog); Object.DestroyImmediate(obrez);
            Object.DestroyImmediate(kopiya);
            snyato++;
            Debug.Log("[ikonki] снята " + fajl + " с модели " + istochnik.name + (tochno ? "" : " (ПРИБЛИЗИТЕЛЬНО: точной модели в сцене нет)"));
        }

        Object.DestroyImmediate(studiya);
        RenderSettings.ambientLight = ambient;
        RenderSettings.ambientMode = ambientMode;

        AssetDatabase.Refresh();
        foreach (var (fajl, _, _) in Spisok) Nastroit(Vyhod + "/" + fajl + ".png");
        AssetDatabase.Refresh();

        // Сцену НЕ сохраняем: студия временная, а изменения в ней сцене не нужны.
        Debug.Log("[ikonki] готово, снято " + snyato + " из " + Spisok.Length);
    }

    static GameObject Nayti(string chast)
    {
        GameObject nayden = null;
        int luchshiy = int.MaxValue;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (!t.name.Contains(chast)) continue;
            // Берем самый короткий подходящий узел верхнего уровня: у длинных
            // имен обычно суффикс копии, а нам нужен цельный объект с детьми.
            if (t.GetComponentsInChildren<Renderer>(false).Length == 0) continue;
            int ves = t.name.Length + Glubina(t) * 100;
            if (ves < luchshiy) { luchshiy = ves; nayden = t.gameObject; }
        }
        return nayden;
    }

    static int Glubina(Transform t)
    {
        int d = 0;
        while (t.parent != null) { d++; t = t.parent; }
        return d;
    }

    static Bounds Gabarit(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(false);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) if (r.enabled) b.Encapsulate(r.bounds);
        return b;
    }

    static Texture2D Kadr(Camera cam, Color fon)
    {
        cam.backgroundColor = fon;
        var rt = new RenderTexture(Razmer, Razmer, 24, RenderTextureFormat.ARGB32,
                                   RenderTextureReadWrite.sRGB);
        rt.antiAliasing = 8;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tx = new Texture2D(Razmer, Razmer, TextureFormat.RGBA32, false);
        tx.ReadPixels(new Rect(0, 0, Razmer, Razmer), 0, 0);
        tx.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        rt.Release();
        Object.DestroyImmediate(rt);
        return tx;
    }

    /// <summary>Альфа из пары кадров: a = 1 - (белый - черный) по яркости.</summary>
    static Texture2D Sobrat(Texture2D chern, Texture2D bel)
    {
        var cb = chern.GetPixels();
        var cw = bel.GetPixels();
        var res = new Color[cb.Length];
        for (int i = 0; i < cb.Length; i++)
        {
            float raznitsa = ((cw[i].r - cb[i].r) + (cw[i].g - cb[i].g) + (cw[i].b - cb[i].b)) / 3f;
            float a = Mathf.Clamp01(1f - raznitsa);
            if (a < 0.004f) { res[i] = new Color(0, 0, 0, 0); continue; }
            res[i] = new Color(Mathf.Clamp01(cb[i].r / a), Mathf.Clamp01(cb[i].g / a), Mathf.Clamp01(cb[i].b / a), a);
        }
        var tx = new Texture2D(chern.width, chern.height, TextureFormat.RGBA32, false);
        tx.SetPixels(res);
        tx.Apply();
        return tx;
    }

    /// <summary>Обрезает пустоту по краям и оставляет поле долей от стороны.</summary>
    static Texture2D Obrezat(Texture2D src, float pole)
    {
        int w = src.width, h = src.height;
        var p = src.GetPixels();
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (p[y * w + x].a > 0.02f)
                {
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
        if (x1 < 0) return Object.Instantiate(src);

        int storona = Mathf.Max(x1 - x0, y1 - y0) + 1;
        int polya = Mathf.RoundToInt(storona * pole);
        storona += polya * 2;
        int cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;

        var res = new Color[storona * storona];
        for (int y = 0; y < storona; y++)
            for (int x = 0; x < storona; x++)
            {
                int sx = cx - storona / 2 + x, sy = cy - storona / 2 + y;
                res[y * storona + x] = (sx < 0 || sy < 0 || sx >= w || sy >= h)
                    ? new Color(0, 0, 0, 0) : p[sy * w + sx];
            }
        var tx = new Texture2D(storona, storona, TextureFormat.RGBA32, false);
        tx.SetPixels(res);
        tx.Apply();
        return tx;
    }

    static void Nastroit(string put)
    {
        var im = AssetImporter.GetAtPath(put) as TextureImporter;
        if (im == null) return;
        im.textureType = TextureImporterType.Sprite;
        im.spriteImportMode = SpriteImportMode.Single;
        im.alphaIsTransparency = true;
        im.mipmapEnabled = false;
        im.SaveAndReimport();
    }
}
