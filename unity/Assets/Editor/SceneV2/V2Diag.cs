using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Диагностика сцены v2. Открывает сохраненную сцену и меряет ее числами:
/// габариты в метрах, габариты в пикселях кадра, зазор до земли, бюджеты.
///
/// Смысл: любое визуальное утверждение должно иметь числового двойника.
/// «Завод меньше склада» решается пикселями, а не впечатлением.
/// </summary>
public static class V2Diag
{
    public static void Diag()
    {
        string outPath = Arg("-out", "../mars-colony/loop/scene-v2/kadry/diag.txt");

        EditorSceneManager.OpenScene(V2Lib.SCENE_PATH, OpenSceneMode.Single);
        var cam = V2Lib.OurCamera();

        var sb = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;

        sb.AppendLine("=== КАМЕРА ===");
        if (cam == null) sb.AppendLine("КАМЕРЫ НЕТ");
        else
        {
            sb.AppendLine($"поз      {cam.transform.position.ToString("F2", ci)}");
            sb.AppendLine($"поворот  {cam.transform.eulerAngles.ToString("F2", ci)}");
            sb.AppendLine($"орто     {cam.orthographic}  размер {cam.orthographicSize:F2}");
            sb.AppendLine($"клип     near {cam.nearClipPlane:F2}  far {cam.farClipPlane:F1}");
        }

        sb.AppendLine();
        sb.AppendLine("=== ОБЪЕКТЫ ===");
        sb.AppendLine($"{"имя",-30} {"метры XxYxZ",-22} {"центр",-24} " +
                      $"{"пиксели ВхШ",-14} {"доляH",-7} {"зазор",-7}");

        // Постройки и люди - то, у чего проверяется иерархия размеров и посадка
        // на землю. Реквизит и декор не проверяются поштучно: их сотни, и
        // осмысленной иерархии между ящиками нет.
        var vetki = new[] { "Stroyki", "Lyudi" };
        var all = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                        .Where(g => g.transform.parent != null
                                 && vetki.Contains(g.transform.parent.name))
                        .Where(g => g.GetComponentsInChildren<Renderer>().Length > 0)
                        .OrderByDescending(g => { V2Lib.WorldBounds(g, out var bb);
                                                  return bb.size.y; })
                        .ToList();

        int plavaet = 0, utopleno = 0;
        foreach (var go in all)
        {
            if (!V2Lib.WorldBounds(go, out var b)) continue;

            string px = "-", dolya = "-";
            if (cam != null)
            {
                var r = V2Lib.ViewportRect(cam, b);
                px = $"{r.height * V2Lib.SHOT_H:F0}x{r.width * V2Lib.SHOT_W:F0}";
                dolya = r.height.ToString("F3", ci);
            }

            // Зазор до земли. Ноль это посадка вплотную. Положительный - парит,
            // отрицательный - утоплено. Порог 0.05 м это половина толщины
            // наклейки, ниже уже не видно.
            float gap = b.min.y;
            if (gap > 0.05f) plavaet++;
            if (gap < -0.05f) utopleno++;

            sb.AppendLine($"{go.name,-30} " +
                          $"{b.size.x,6:F2}x{b.size.y,5:F2}x{b.size.z,5:F2}     " +
                          $"{b.center.ToString("F1", ci),-24} " +
                          $"{px,-14} {dolya,-7} {gap,6:F3}");
        }

        sb.AppendLine();
        sb.AppendLine($"парит над землей: {plavaet}, утоплено: {utopleno}");

        // ---- бюджеты
        long tris = 0, verts = 0;
        int rend = 0;
        var meshes = new HashSet<Mesh>();
        foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || !mr.enabled) continue;
            rend++;
            if (mf.sharedMesh == null) continue;
            meshes.Add(mf.sharedMesh);
        }

        // Считаем по УНИКАЛЬНЫМ мешам, а не по рендерерам.
        //
        // После упаковки StaticBatchingUtility.Combine подменяет каждому
        // рендереру меш на общий склеенный. Подсчет по рендерерам берет весь
        // склеенный меш столько раз, сколько в нем объектов, и завышает итог в
        // разы: диагностика показывала 16.8 млн против 3.2 млн у сборки при
        // ОДИНАКОВОМ числе рендереров 601. Совпадение рендереров и было
        // подсказкой - считался один и тот же набор, но разными мерками.
        foreach (var m in meshes)
        {
            tris += m.triangles.Length / 3;
            verts += m.vertexCount;
        }

        long texBytes = 0;
        var texs = new HashSet<Texture>();
        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            foreach (var m in mr.sharedMaterials)
            {
                if (m == null) continue;
                var t = m.GetTexture("_BaseMap");
                if (t != null && texs.Add(t))
                    texBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
            }

        sb.AppendLine();
        sb.AppendLine("=== ОРИЕНТАЦИЯ И ЕДИНИЧНОСТЬ ===");
        sb.AppendLine("Шаг 5 спеки расстановки: у всего, что имеет опознающую");
        sb.AppendLine("грань, поворот строго кратен 90 градусам. Свободный угол");
        sb.AppendLine("только у камня, льда, грунта и пыли.");
        sb.AppendLine();

        string[] svobodnye = { "kamen", "kamni", "led-", "grunt-", "izmoroz",
                               "pyl-", "valun" };
        // Судим ТОЛЬКО то, что расставляли мы: прямых детей веток сцены.
        //
        // Первая версия обходила все объекты с рендерером подряд и ловила
        // `geometry_0` - внутренности импортированных FBX. У них свой поворот
        // ВНУТРИ модели, к расстановке он отношения не имеет. Из 601 объекта
        // так набралось 485 «нарушений», почти все ложные. Спека судит
        // расстановку, а не устройство меша.
        var vetkiScheta = new[] { "Stroyki", "Rekvizit", "Dekor", "Lyudi" };
        var vseGo = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                          .Where(g => g.transform.parent != null
                                   && vetkiScheta.Contains(g.transform.parent.name))
                          .ToList();

        int krivyh = 0, vsegoPrav = 0;
        foreach (var g in vseGo)
        {
            if (svobodnye.Any(p => g.name.StartsWith(p))) continue;
            vsegoPrav++;
            float y = g.transform.eulerAngles.y;
            float ostatok = Mathf.Abs(y - Mathf.Round(y / 90f) * 90f);
            if (ostatok > 1.0f)
            {
                krivyh++;
                if (krivyh <= 6)
                    sb.AppendLine($"    {g.name}: поворот {y:F1} градусов");
            }
        }
        sb.AppendLine($"  объектов с опознающей гранью: {vsegoPrav}, "
                    + $"нарушений ориентации: {krivyh}");

        // Единичность: список из проверяльщика дословно.
        var edinichnye = new (string prefiks, int predel)[]
        {
            ("sklad", 1), ("zavod", 1), ("fabrika", 1), ("stantsiya", 1),
            ("ploshchadka-shattla", 1),
        };
        int narushEd = 0;
        foreach (var e in edinichnye)
        {
            int n = vseGo.Count(g => g.name.StartsWith(e.prefiks));
            if (n > e.predel)
            {
                narushEd++;
                sb.AppendLine($"    {e.prefiks}: {n} штук при пределе {e.predel}");
            }
        }
        sb.AppendLine($"  нарушений единичности: {narushEd}");

        sb.AppendLine();
        sb.AppendLine("=== СОСТАВ НАБОРА ===");
        sb.AppendLine("Требование владельца: на сцене должны стоять ВСЕ модели");
        sb.AppendLine("из VseAssety и ни одной сверх. Список берется из файла,");
        sb.AppendLine("полученного разбором той сцены по ссылкам GUID.");
        sb.AppendLine();

        string putSpiska = System.IO.Path.GetFullPath(
            "../mars-colony/loop/scene-v2/vseassety-spisok.txt");
        if (!System.IO.File.Exists(putSpiska))
        {
            sb.AppendLine("  список не найден: " + putSpiska);
        }
        else
        {
            var nado = new HashSet<string>(System.IO.File.ReadAllLines(putSpiska)
                .Select(x => x.Trim()).Where(x => x.Length > 0));

            // Имя объекта в сцене равно имени модели: так их ставит V2Lib.Place.
            var est = new HashSet<string>(
                Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                      .Select(g => g.name)
                      .Where(n => nado.Contains(n)));

            var netu = nado.Where(n => !est.Contains(n)).OrderBy(x => x).ToList();
            sb.AppendLine($"  в списке {nado.Count}, на сцене {est.Count}");
            if (netu.Count > 0)
            {
                sb.AppendLine($"  НЕ ПОСТАВЛЕНО {netu.Count}:");
                foreach (var n in netu) sb.AppendLine("    " + n);
            }
            else sb.AppendLine("  все модели набора на сцене");
        }

        sb.AppendLine();
        sb.AppendLine("=== СВЯЗКИ АССЕТОВ ===");
        sb.AppendLine("Таблица назначений требует, чтобы часть моделей не стояла");
        sb.AppendLine("сама по себе: шаттл на своей площадке, груз вплотную к");
        sb.AppendLine("владельцу, фигура у конкретного здания. Одиночный груз");
        sb.AppendLine("посреди пустоши - нарушение, а не декор.");
        sb.AppendLine();

        var vseObj = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                           .Where(g => g.GetComponent<MeshRenderer>() != null
                                    || g.GetComponentsInChildren<MeshRenderer>().Length > 0)
                           .ToList();

        Vector3 Centr(GameObject g)
        {
            V2Lib.WorldBounds(g, out var bb);
            return new Vector3(bb.center.x, 0f, bb.center.z);
        }

        var postroyki = vseObj.Where(g => g.transform.parent != null
                                       && g.transform.parent.name == "Stroyki").ToList();

        float DoBlizhayshey(GameObject g, System.Func<GameObject, bool> otbor)
        {
            var c = Centr(g);
            float best = 9999f;
            foreach (var b in postroyki)
            {
                if (b == g || !otbor(b)) continue;
                V2Lib.WorldBounds(b, out var bb);
                // расстояние от центра до КОРОБКИ постройки, а не до ее центра:
                // у крупных зданий центр далеко, и мерить до него бессмысленно
                var d = Vector3.Max(Vector3.Max(bb.min - c, Vector3.zero), c - bb.max);
                best = Mathf.Min(best, new Vector2(d.x, d.z).magnitude);
            }
            return best;
        }

        int narusheno = 0;
        void Pravilo(string imya, System.Func<GameObject, bool> kto,
                     System.Func<GameObject, bool> ryadomS, float predel, string opis)
        {
            var spisok = vseObj.Where(kto).ToList();
            int plohih = 0;
            foreach (var g in spisok)
            {
                float d = DoBlizhayshey(g, ryadomS);
                if (d > predel)
                {
                    plohih++;
                    if (plohih <= 3)
                        sb.AppendLine($"    {g.name}: до ближайшего {d:F1} м "
                                    + $"при пределе {predel:F1}");
                }
            }
            narusheno += plohih;
            sb.AppendLine($"  {imya}: {spisok.Count} шт, нарушений {plohih}. {opis}");
        }

        Pravilo("шаттлы на площадке",
                g => g.name.StartsWith("shattl"),
                b => b.name.StartsWith("ploshchadka"), 12f,
                "шаттл без своей площадки не ставится");
        Pravilo("груз у владельца",
                g => g.name.StartsWith("ballony") || g.name.StartsWith("yashchiki-shtabel"),
                b => true, 10f,
                "груз обязан лежать вплотную к тому, что его возит");
        Pravilo("фигуры у здания",
                g => g.name.StartsWith("figura") || g.name.StartsWith("KALIBR"),
                b => true, 9f,
                "признак жизни принадлежит конкретному зданию");
        Pravilo("труба у трубопровода",
                g => g.name.StartsWith("truba-na-kozlakh"),
                b => true, 12f,
                "сегмент существует, чтобы закрыть разрыв");

        sb.AppendLine($"нарушений связок всего: {narusheno}");

        sb.AppendLine();
        sb.AppendLine("=== ВЫЗОВЫ ОТРИСОВКИ, ОЦЕНКА ПО СЦЕНЕ ===");
        sb.AppendLine("ЭТО ОЦЕНКА, А НЕ ЗАМЕР. Настоящий счет вызовов дает только");
        sb.AppendLine("запущенная сборка через UnityStats.batches; из batchmode");
        sb.AppendLine("режим игры не поднять. Оценка считает нижнюю границу:");
        sb.AppendLine("сколько групп по материалу есть в сцене. Реальное число");
        sb.AppendLine("будет НЕ МЕНЬШЕ, потому что пакетирование срывается на");
        sb.AppendLine("разных мешах, тенях и прозрачности.");
        sb.AppendLine();

        var vseR = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                         .Where(r => r.enabled && r.gameObject.activeInHierarchy)
                         .ToList();
        var poMat = vseR.GroupBy(r => r.sharedMaterial != null
                                      ? r.sharedMaterial.name : "<нет>")
                        .OrderByDescending(g => g.Count()).ToList();
        sb.AppendLine($"рендереров всего            : {vseR.Count}");
        sb.AppendLine($"уникальных материалов       : {poMat.Count}");
        int otbrasyvayut = vseR.Count(r => r.shadowCastingMode
                                     != UnityEngine.Rendering.ShadowCastingMode.Off);
        sb.AppendLine($"отбрасывают тень            : {otbrasyvayut}"
                    + "  (тень идет отдельным проходом, вызовы примерно удваиваются)");
        sb.AppendLine($"нижняя граница вызовов      : {poMat.Count}"
                    + $"  плюс проход теней ~{poMat.Count} = ~{poMat.Count * 2}");
        sb.AppendLine();
        sb.AppendLine("Самые частые материалы:");
        foreach (var g in poMat.Take(8))
            sb.AppendLine($"  {g.Key,-34} {g.Count(),4} рендереров");
        int odinochki = poMat.Count(g => g.Count() == 1);
        sb.AppendLine($"материалов ровно с одним рендерером: {odinochki}"
                    + "  (каждый такой это гарантированный отдельный вызов)");

        sb.AppendLine();
        sb.AppendLine("=== ПЕРЕСЕЧЕНИЯ ===");
        sb.AppendLine("Постройка внутри постройки или заметное взаимное");
        sb.AppendLine("проникновение габаритов. Легкое касание не считается:");
        sb.AppendLine("порог - перекрытие больше 25% объема меньшего из двух.");

        var stroyki = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                            .Where(g => g.transform.parent != null
                                     && g.transform.parent.name == "Stroyki")
                            .Where(g => g.GetComponentsInChildren<Renderer>().Length > 0)
                            .ToList();
        int peresecheniy = 0;
        for (int i = 0; i < stroyki.Count; i++)
        {
            if (!V2Lib.WorldBounds(stroyki[i], out var bi)) continue;
            for (int j = i + 1; j < stroyki.Count; j++)
            {
                if (!V2Lib.WorldBounds(stroyki[j], out var bj)) continue;
                if (!bi.Intersects(bj)) continue;

                // ПОВЕРХНОСТЬ НЕ СЧИТАЕТСЯ ПОСТРОЙКОЙ.
                //
                // Шаттл, стоящий на своей площадке, давал «перекрытие 100%
                // меньшего» - и это верно геометрически, но неверно по смыслу:
                // предмет НА поверхности не то же самое, что постройка внутри
                // постройки. Отличаем по пропорции: у площадки план к высоте
                // больше шести, у здания нет.
                float PlanKVysote(Bounds b) =>
                    Mathf.Max(b.size.x, b.size.z) / Mathf.Max(b.size.y, 0.01f);
                if (PlanKVysote(bi) > 6f || PlanKVysote(bj) > 6f) continue;
                var mn = Vector3.Max(bi.min, bj.min);
                var mx = Vector3.Min(bi.max, bj.max);
                var d = mx - mn;
                if (d.x <= 0 || d.y <= 0 || d.z <= 0) continue;
                float obshchee = d.x * d.y * d.z;
                float vi = bi.size.x * bi.size.y * bi.size.z;
                float vj = bj.size.x * bj.size.y * bj.size.z;
                float dolya = obshchee / Mathf.Max(0.001f, Mathf.Min(vi, vj));
                if (dolya < 0.25f) continue;
                peresecheniy++;
                if (peresecheniy <= 10)
                    sb.AppendLine($"  {stroyki[i].name} и {stroyki[j].name}: "
                                + $"перекрытие {dolya * 100f:F0}% меньшего");
            }
        }
        sb.AppendLine($"пересечений построек: {peresecheniy}");

        sb.AppendLine();
        sb.AppendLine("=== ИЕРАРХИЯ РАЗМЕРОВ, ВТОРОЙ КАНАЛ ===");
        sb.AppendLine("Метры из кода против пикселей на кадре. Пиксели считаются");
        sb.AppendLine("по ВЕРТИКАЛИ объекта (низ центра к верху центра), а не по");
        sb.AppendLine("габариту целиком: габарит включает глубину, и широкий");
        sb.AppendLine("низкий объект по нему выходит крупнее узкого высокого.");
        sb.AppendLine();
        sb.AppendLine($"{"имя",-32} {"метры",-8} {"пиксели",-9} {"пикс/м",-8}");

        var pary = new List<(string imya, float m, float px)>();
        foreach (var go in all)
        {
            if (!V2Lib.WorldBounds(go, out var b) || cam == null) continue;
            var niz = new Vector3(b.center.x, b.min.y, b.center.z);
            var verh = new Vector3(b.center.x, b.max.y, b.center.z);
            float px = Mathf.Abs(cam.WorldToViewportPoint(verh).y
                               - cam.WorldToViewportPoint(niz).y) * V2Lib.SHOT_H;
            pary.Add((go.name, b.size.y, px));
            sb.AppendLine($"{go.name,-32} {b.size.y,-8:F2} {px,-9:F0} {px / b.size.y,-8:F1}");
        }

        // Нарушение порядка: объект выше в метрах, но ниже в пикселях. Ровно
        // этот дефект прошлый заход поймал только вторым замером - в коде числа
        // стояли верные, а на картинке пара «завод меньше склада» шла наоборот.
        int narusheniy = 0;
        for (int i = 0; i < pary.Count; i++)
            for (int j = i + 1; j < pary.Count; j++)
                if (pary[i].m > pary[j].m + 0.15f && pary[i].px < pary[j].px - 2f)
                {
                    narusheniy++;
                    if (narusheniy <= 6)
                        sb.AppendLine($"  НАРУШЕНИЕ: {pary[i].imya} {pary[i].m:F2} м / "
                                    + $"{pary[i].px:F0} пикс НИЖЕ чем {pary[j].imya} "
                                    + $"{pary[j].m:F2} м / {pary[j].px:F0} пикс");
                }
        sb.AppendLine($"нарушений порядка: {narusheniy}");

        // Разброс отношения пиксели на метр: при ортографии он обязан быть
        // постоянным, разброс означает ошибку проекции или масштаба.
        if (pary.Count > 0)
        {
            var k = pary.Select(p => p.px / p.m).ToList();
            sb.AppendLine($"пикселей на метр: мин {k.Min():F2}, макс {k.Max():F2}, "
                        + $"разброс {(k.Max() - k.Min()) / k.Average() * 100f:F1}%");
        }

        sb.AppendLine();
        sb.AppendLine("=== МАТЕРИАЛЫ ===");
        sb.AppendLine($"{"объект",-26} {"шейдер",-34} {"_BaseMap",-16} {"_BaseColor",-22}");
        foreach (var go in all)
        {
            var mr = go.GetComponentInChildren<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null)
            { sb.AppendLine($"{go.name,-26} МАТЕРИАЛА НЕТ"); continue; }
            var m = mr.sharedMaterial;
            var t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
            var col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                                                  : new Color(-1, -1, -1);
            sb.AppendLine($"{go.name,-26} {m.shader.name,-34} " +
                          $"{(t == null ? "НЕТ" : t.name),-16} {col.ToString("F2", ci),-22}");
        }

        sb.AppendLine();
        sb.AppendLine("=== UV ===");
        sb.AppendLine($"{"объект",-26} {"вершин",-9} {"uv",-9} {"uv диапазон",-30}");
        foreach (var go in all)
        {
            var mf = go.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var mesh = mf.sharedMesh;
            var uv = mesh.uv;
            if (uv == null || uv.Length == 0)
            { sb.AppendLine($"{go.name,-26} {mesh.vertexCount,-9} {"UV НЕТ",-9}"); continue; }
            float ux0 = float.MaxValue, ux1 = float.MinValue,
                  uy0 = float.MaxValue, uy1 = float.MinValue;
            foreach (var u in uv)
            {
                ux0 = Mathf.Min(ux0, u.x); ux1 = Mathf.Max(ux1, u.x);
                uy0 = Mathf.Min(uy0, u.y); uy1 = Mathf.Max(uy1, u.y);
            }
            sb.AppendLine($"{go.name,-26} {mesh.vertexCount,-9} {uv.Length,-9} " +
                          $"x[{ux0:F3}..{ux1:F3}] y[{uy0:F3}..{uy1:F3}]");
        }

        sb.AppendLine();
        sb.AppendLine("=== БЮДЖЕТЫ ===");
        sb.AppendLine($"треугольников в сцене : {tris}  (потолок 500000)");
        sb.AppendLine($"вершин                : {verts}");
        sb.AppendLine($"рендереров            : {rend}  (потолок вызовов 200)");
        sb.AppendLine($"уникальных мешей      : {meshes.Count}");
        sb.AppendLine($"текстур               : {texs.Count}");
        sb.AppendLine($"текстурная память     : {texBytes / 1024 / 1024} МБ  (потолок 256)");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        File.WriteAllText(Path.GetFullPath(outPath), sb.ToString());
        Debug.Log("[v2] диагностика:\n" + sb);
    }

    private static string Arg(string key, string def)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == key) return a[i + 1];
        return def;
    }
}
