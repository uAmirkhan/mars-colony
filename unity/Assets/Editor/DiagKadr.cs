using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Диагностический проход по кадру: снимок, где каждый объект залит своим
/// плоским цветом, плюс опись того, где он обязан быть.
///
/// Зачем он вообще нужен. Обычный кадр отвечает на вопрос «красиво ли», и на
/// него отвечают глазами. Но целый класс дефектов глазами не ловится вовсе:
/// объект отрисован ЗА землёй, объект перекрыт другим на девяносто процентов,
/// объект вылез за рамку, двух объектов в кадре нет совсем. На красивом кадре
/// всё это выглядит просто как «нормальный кадр» — недостающего не видно
/// именно потому, что его не видно.
///
/// Проход отвечает на другой вопрос: СТОИТ ЛИ ОБЪЕКТ ТАМ, ГДЕ ДОЛЖЕН. Для
/// этого он даёт две вещи, которые потом сравнивает разборщик:
///
/// 1. Сколько пикселей объект занял НА САМОМ ДЕЛЕ (по цвету в снимке).
/// 2. Сколько он должен был занять, если бы его ничто не перекрывало
///    (проекция габарита на экран).
///
/// Разница между этими числами и есть диагноз. Ноль пикселей при непустой
/// проекции — объект не виден вовсе. Пять процентов от проекции — перекрыт
/// почти целиком.
///
/// Цвет объекта назначается не на глаз, а из грубой палитры — см. Palitra().
/// Сглаживание при этом обязано быть выключено: смешанный на кромке цвет
/// означал бы объект, которого нет.
/// </summary>
public static class DiagKadr
{
    private const int Shirina = 1600;
    private const int Vysota = 1000;

    [MenuItem("Mars/Snyat diagnostiku")]
    public static void Snyat()
    {
        var cam = NaytiKameru();
        if (cam == null)
        {
            Debug.LogError("[диаг] камеры в сцене нет — сначала собери колонию");
            return;
        }

        // ПРОЗРАЧНЫЕ СЛОИ В ПРОХОДЕ НЕ УЧАСТВУЮТ.
        //
        // Поверх земли лежат два полотна во всю карту — макрослой цвета и
        // тональные пятна. В кадре они полупрозрачные и землю показывают. Но в
        // проходе цвет пишется непрозрачным, и они закрывали собой всё: земля
        // выходила «видна на 1 пиксель», а вместе с ней и весь декор под ними.
        //
        // Их собственная видимость и не вопрос — они заведомо во весь кадр.
        // Вопрос в том, что под ними, поэтому на время прохода их гасим.
        var renderers = new List<Renderer>();
        var skrytye = new List<Renderer>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            var sm = r.sharedMaterial;
            if (sm != null && sm.renderQueue >= 2450) { skrytye.Add(r); continue; }
            renderers.Add(r);
        }

        if (renderers.Count == 0)
        {
            Debug.LogError("[диаг] в сцене нет ни одного видимого меша");
            return;
        }

        // ДВА КАПКАНА, оба пойманы на первом прогоне, оба давали одно и то же:
        // ни один пиксель не совпадал со своим номером.
        //
        // 1. MaterialPropertyBlock под SRP Batcher не работает. Батчер собирает
        //    объекты с одним материалом в один вызов и значения из блока
        //    попросту не доезжают. Поэтому здесь у КАЖДОГО рендерера свой
        //    материал — дороже, но надёжно, а проход снимается редко.
        //
        // 2. Постобработка камеры тонирует кадр. Цвет здесь несёт номер, а не
        //    вид, и любая кривая, экспозиция или блум превращают номер 1 в
        //    номер, которого нет. Выключаем на время прохода.
        var shUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        var svoi = new Material[renderers.Count];

        var byloMat = new Material[renderers.Count][];
        var byloTeni = new UnityEngine.Rendering.ShadowCastingMode[renderers.Count];
        var opis = new StringBuilder();
        opis.AppendLine("[");

        // Прежние настройки камеры — вернём как было, кадр снимается ею же
        var byloFlags = cam.clearFlags;
        var byloFon = cam.backgroundColor;
        var dop = cam.GetUniversalAdditionalCameraData();
        bool byloPost = dop != null && dop.renderPostProcessing;
        bool byloDither = dop != null && dop.dithering;
        bool byloHDR = cam.allowHDR;
        bool byloMSAA = cam.allowMSAA;

        // ТУМАН — третий капкан, и самый незаметный. URP/Unlit применяет его
        // наравне с прочими, поэтому «плоский» цвет объекта плыл с расстоянием:
        // вместо семи уровней в канале оказалось восемьдесят четыре значения,
        // размазанных подряд. Дизеринг камеры делает то же самое, только на
        // соседних пикселях.
        bool byloTuman = RenderSettings.fog;

        try
        {
            foreach (var r in skrytye) r.enabled = false;
            RenderSettings.fog = false;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            if (dop != null)
            {
                dop.renderPostProcessing = false;
                dop.dithering = false;
            }

            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                byloMat[i] = r.sharedMaterials;
                byloTeni[i] = r.shadowCastingMode;

                // Тень от объекта — тоже пиксели, и они лягут чужим цветом
                // поверх соседа. Для описи это ложное перекрытие.
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                int nomer = i + 1;                       // 0 занят фоном
                var col = Palitra(i);

                var m = new Material(shUnlit);
                m.SetColor("_BaseColor", col);
                m.SetColor("_Color", col);       // на случай другого имени слота
                // Отсечение задних граней выключаем: купол неба смотрят
                // ИЗНУТРИ, и с обычным отсечением он в проходе исчезал целиком.
                m.SetFloat("_Cull", 0f);
                m.doubleSidedGI = true;
                svoi[i] = m;

                var odin = new Material[byloMat[i].Length];
                for (int k = 0; k < odin.Length; k++) odin[k] = m;
                r.sharedMaterials = odin;

                opis.Append(OpisatObekt(r, cam, nomer, i == renderers.Count - 1));
            }

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 1f);
            Snimok(cam, "diag-id.png");
        }
        finally
        {
            foreach (var r in skrytye) if (r != null) r.enabled = true;
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].sharedMaterials = byloMat[i];
                    renderers[i].shadowCastingMode = byloTeni[i];
                }
                if (svoi[i] != null) Object.DestroyImmediate(svoi[i]);
            }
            cam.clearFlags = byloFlags;
            cam.backgroundColor = byloFon;
            cam.allowHDR = byloHDR;
            cam.allowMSAA = byloMSAA;
            RenderSettings.fog = byloTuman;
            if (dop != null)
            {
                dop.renderPostProcessing = byloPost;
                dop.dithering = byloDither;
            }
        }

        opis.AppendLine("]");
        File.WriteAllText("diag-opis.json", opis.ToString());
        File.WriteAllText("diag-palitra.json",
            "{\"urovney\": " + Urovney + ", \"obektov\": " + renderers.Count
            + ", \"skryto_prozrachnyh\": " + skrytye.Count + "}");
        Debug.Log($"[диаг] снято diag-id.png и diag-opis.json, объектов "
                  + $"{renderers.Count}, прозрачных скрыто {skrytye.Count}");
    }

    /// <summary>Строка описи: имя, номер, проекция габарита на экран,
    /// расстояние до камеры, низ габарита в мире.</summary>
    private static string OpisatObekt(Renderer r, Camera cam, int nomer, bool posledniy)
    {
        var b = r.bounds;
        // Проекция габарита: восемь углов в экранные координаты. Габарит
        // ШИРЕ силуэта, поэтому число «ожидаемых» пикселей завышено — это
        // осознанно, разборщик сравнивает доли, а не абсолют.
        float x0 = float.MaxValue, y0 = float.MaxValue;
        float x1 = float.MinValue, y1 = float.MinValue;
        bool speredi = false;
        for (int i = 0; i < 8; i++)
        {
            var ugol = new Vector3(
                (i & 1) == 0 ? b.min.x : b.max.x,
                (i & 2) == 0 ? b.min.y : b.max.y,
                (i & 4) == 0 ? b.min.z : b.max.z);
            var t = cam.WorldToViewportPoint(ugol);
            if (t.z <= 0f) continue;
            speredi = true;
            x0 = Mathf.Min(x0, t.x); x1 = Mathf.Max(x1, t.x);
            y0 = Mathf.Min(y0, t.y); y1 = Mathf.Max(y1, t.y);
        }
        if (!speredi) { x0 = y0 = x1 = y1 = 0f; }

        float pxW = Mathf.Max(0f, Mathf.Min(x1, 1f) - Mathf.Max(x0, 0f)) * Shirina;
        float pxH = Mathf.Max(0f, Mathf.Min(y1, 1f) - Mathf.Max(y0, 0f)) * Vysota;

        var sb = new StringBuilder();
        sb.Append("  {");
        sb.Append($"\"nomer\": {nomer}, ");
        sb.Append("\"imya\": \"" + Vidnoe(r) + "\", ");
        sb.Append("\"mesh\": \"" + r.gameObject.name + "\", ");
        sb.Append($"\"roditel\": \"{(r.transform.parent == null ? "" : r.transform.parent.name)}\", ");
        sb.Append($"\"v_kadre\": {(speredi ? "true" : "false")}, ");
        sb.Append("\"ramka_px\": [" + pxW.ToString("F0", Ci) + ", " + pxH.ToString("F0", Ci) + "], ");
        sb.Append("\"ozhidalos_px\": " + (pxW * pxH).ToString("F0", Ci) + ", ");
        sb.Append("\"do_kamery\": "
                  + Vector3.Distance(cam.transform.position, b.center).ToString("F2", Ci) + ", ");
        sb.Append("\"niz_y\": " + b.min.y.ToString("F3", Ci) + ", ");
        sb.Append("\"verkh_y\": " + b.max.y.ToString("F3", Ci));
        sb.Append(posledniy ? "}\n" : "},\n");
        return sb.ToString();
    }

    /// <summary>Уровней на канал. 7^3 = 343 объекта — с запасом на нынешние 265.
    /// Больше уровней брать нельзя: они должны оставаться различимыми ПОСЛЕ
    /// гаммы, а она жмёт тёмный край.</summary>
    private const int Urovney = 7;

    /// <summary>
    /// Цвет объекта берётся из грубой палитры, а не кодирует номер напрямую.
    ///
    /// Прямое кодирование (r = i &amp; 255) казалось точнее и оказалось
    /// нерабочим: по дороге к PNG цвет проходит преобразование, и номер 1
    /// приезжает как 11. Гоняться за тем, где именно оно применяется — в
    /// материале, в блите или в цветовом пространстве буфера — занятие на
    /// полдня, и любое изменение настроек рендера ломает результат снова.
    ///
    /// Устойчивое решение не зависит от преобразования вовсе. Каналу даётся
    /// семь заведомо далёких друг от друга уровней. Преобразование их
    /// сдвигает, но НЕ ПЕРЕСТАВЛЯЕТ — оно монотонно. Значит разборщик
    /// восстанавливает уровень по ПОРЯДКУ значений, а не по их величине, и
    /// ему всё равно, какая там гамма.
    ///
    /// Нижний уровень нарочно не ноль: ноль занят фоном, и он обязан
    /// остаться заметно ниже любого объекта.
    /// </summary>
    private static Color Palitra(int i)
    {
        int r = i % Urovney;
        int g = (i / Urovney) % Urovney;
        int b = (i / (Urovney * Urovney)) % Urovney;
        return new Color(Uroven(r), Uroven(g), Uroven(b), 1f);
    }

    private static float Uroven(int k)
    {
        // 40..255 равномерно: ниже 40 гамма сминает соседние уровни в один
        return (40f + k * (255f - 40f) / (Urovney - 1)) / 255f;
    }

    /// <summary>Числа в опись пишем инвариантной культурой. В русской локали
    /// C# отделяет дробную часть ЗАПЯТОЙ, и JSON выходит битым: «86,43»
    /// разбирается как два поля. Поймано на первом же прогоне.</summary>
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    /// <summary>Рендереры импортированных моделей все зовутся geometry_0.
    /// Осмысленное имя лежит на родителе — его и показываем, а собственное
    /// остаётся отдельным полем, чтобы не потерять привязку к мешу.</summary>
    private static string Vidnoe(Renderer r)
    {
        string imya = r.gameObject.name;
        if (!imya.StartsWith("geometry")) return imya;
        var p = r.transform.parent;
        return p == null ? imya : p.name;
    }

    private static Camera NaytiKameru()
    {
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (c.gameObject.name == "kamera") return c;
        return Camera.main;
    }

    /// <summary>Снимок БЕЗ сглаживания: цвет здесь несёт номер, а смешанный
    /// на кромке цвет означал бы номер несуществующего объекта.</summary>
    private static void Snimok(Camera cam, string file)
    {
        var rt = new RenderTexture(Shirina, Vysota, 24,
                                   RenderTextureFormat.ARGB32,
                                   RenderTextureReadWrite.Linear);
        rt.antiAliasing = 1;
        rt.filterMode = FilterMode.Point;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tx = new Texture2D(Shirina, Vysota, TextureFormat.RGB24, false);
        tx.ReadPixels(new Rect(0, 0, Shirina, Vysota), 0, 0);
        tx.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        File.WriteAllBytes(file, tx.EncodeToPNG());
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tx);
    }
}
