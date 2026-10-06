using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Земля сцены. Не одна плоскость с одной текстурой, а набор участков с
/// ЖЕСТКИМИ краями.
///
/// Замер по одиннадцати кадрам Township: голой земли там 0.118 .. 0.243 кадра, а
/// мертвых клеток ноль на всех одиннадцати без исключения. Ровный грунт до
/// горизонта этого не даст никогда. В их кадре одновременно видно пять и больше
/// поверхностей: трава, дорожка, асфальт, вспаханное поле, вода, плитка. Граница
/// между ними и читается как «построенное место», а не «местность».
/// </summary>
public static class V2Zemlya
{
    public const float Y_BAZA = 0f;
    public const float Y_UCHASTOK = 0.02f;   // участок над базой
    public const float Y_DOROGA = 0.42f;
    public const float Y_NAKLEYKA = 0.46f;   // наклейка поверх всего

    private static Transform _root;

    /// <summary>
    /// Счетчик высоты участков. Каждый следующий кладется чуть выше
    /// предыдущего.
    ///
    /// Компланарные квады дают z-fighting, и при взгляде под углом он выглядит
    /// не мерцанием, а СМАЗОМ текстуры одного участка на соседний. Именно это
    /// нашел смотрящий на границах песчаных пятен: и `Uchastok`, и `Pyatno`
    /// клались на одну и ту же высоту 0.02.
    /// </summary>
    private static int _sloy;

    public static float SleduyushchayaVysota()
    {
        _sloy++;
        return Y_UCHASTOK + _sloy * 0.006f;
    }

    public static void Nachat()
    {
        _root = new GameObject("Zemlya").transform;
        _sloy = 0;
    }

    /// <summary>Базовая плоскость. Уходит далеко за кадр во все стороны, чтобы
    /// край земли не попадал в кадр ни при каком развороте камеры.</summary>
    public static void Baza(string tekstura, float metrovNaTayl = 8f, Color? kraska = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = "baza";
        go.transform.SetParent(_root, false);
        go.transform.localScale = Vector3.one * (V2Lib.GROUND_HALF * 2f / 10f);
        go.transform.position = new Vector3(0f, Y_BAZA, 0f);

        var tex = V2Lib.NaydiZemlyu(tekstura);
        var m = V2Lib.NewLit(kraska ?? Color.white, tex, 0.03f, imya: "zem-baza");
        float povtor = V2Lib.GROUND_HALF * 2f / metrovNaTayl;
        m.SetTextureScale("_BaseMap", new Vector2(povtor, povtor));
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    /// <summary>
    /// Участок: прямоугольник своего материала с жестким краем.
    /// Ширина и глубина в метрах, разворот в градусах.
    /// </summary>
    public static GameObject Uchastok(string imya, string tekstura,
                                      Vector3 centr, float shirina, float glubina,
                                      float povorot, float metrovNaTayl = 4f,
                                      float y = -1f, Color? kraska = null)
    {
        if (y < 0f) y = SleduyushchayaVysota();
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "uch-" + imya;
        go.transform.SetParent(_root, false);
        go.transform.position = new Vector3(centr.x, y, centr.z);
        go.transform.rotation = Quaternion.Euler(90f, povorot, 0f);
        go.transform.localScale = new Vector3(shirina, glubina, 1f);

        var tex = V2Lib.NaydiZemlyu(tekstura);
        // Подсветка участка тем же приемом, что и перекраска построек:
        // множитель больше единицы поднимает и светлоту, и насыщенность.
        // Нужен зелени: у Township она яркая, а наша делянка имеет светлоту
        // 0.248 и тянет цветность кадра вниз.
        var m = V2Lib.NewLit(kraska ?? Color.white, tex, 0.05f, imya: "zem-" + imya);
        m.SetTextureScale("_BaseMap",
                          new Vector2(shirina / metrovNaTayl, glubina / metrovNaTayl));
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    /// <summary>
    /// Участок с НЕПРАВИЛЬНЫМ краем: несколько повернутых прямоугольников
    /// внахлест вместо одного.
    ///
    /// Оплачено кадром: одиночный прямоугольник на ровном грунте читается
    /// наклейкой, а не местностью. Прямая линия длиной в двадцать метров в
    /// природе не встречается, и глаз это ловит раньше, чем успевает
    /// сформулировать.
    /// </summary>
    public static void Klyaksa(string imya, string tekstura, Vector3 centr,
                               float shirina, float glubina, float povorot,
                               float metrovNaTayl, int kusok, int seed)
    {
        var rnd = new System.Random(seed);
        float S(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        for (int i = 0; i < kusok; i++)
        {
            float k = i == 0 ? 1f : S(0.45f, 0.85f);
            var sm = i == 0 ? Vector3.zero
                            : new Vector3(S(-shirina * 0.42f, shirina * 0.42f), 0f,
                                          S(-glubina * 0.42f, glubina * 0.42f));
            Uchastok($"{imya}-{i}", tekstura, centr + sm,
                     shirina * k, glubina * k, povorot + S(-40f, 40f), metrovNaTayl);
        }
    }

    /// <summary>
    /// Пятно с настоящим неправильным краем: многоугольник со случайным
    /// радиусом, а не набор прямоугольников внахлест.
    ///
    /// Клякса из прямоугольников край не спрятала: на кадре p4 у ледяного
    /// карьера и каменистого участка по-прежнему видны прямые кромки, теперь их
    /// стало даже больше. Прямоугольником неправильную форму не собрать, нужна
    /// своя сетка.
    /// </summary>
    public static void Pyatno(string imya, string tekstura, Vector3 centr,
                              float radius, float splyusnut, float povorot,
                              float metrovNaTayl, int seed, int uglov = 22)
    {
        var rnd = new System.Random(seed);
        var verts = new List<Vector3>(uglov + 1) { Vector3.zero };
        var uvs = new List<Vector2>(uglov + 1) { new Vector2(0.5f, 0.5f) };
        var tris = new List<int>(uglov * 3);

        // радиус гуляет двумя гармониками: крупная задает общую форму,
        // мелкая рвет кромку
        float f1 = 1.6f + (float)rnd.NextDouble() * 1.2f;
        float f2 = 4.5f + (float)rnd.NextDouble() * 2.5f;
        float p1 = (float)rnd.NextDouble() * 6.28f, p2 = (float)rnd.NextDouble() * 6.28f;

        for (int i = 0; i < uglov; i++)
        {
            float a = i / (float)uglov * Mathf.PI * 2f;
            float r = radius * (1f
                + 0.26f * Mathf.Sin(a * f1 + p1)
                + 0.13f * Mathf.Sin(a * f2 + p2)
                + 0.06f * ((float)rnd.NextDouble() - 0.5f));
            float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r * splyusnut;
            verts.Add(new Vector3(x, 0f, z));
            uvs.Add(new Vector2(0.5f + x / (radius * 2f), 0.5f + z / (radius * 2f)));
            // Обход по часовой, если смотреть сверху: при обходе против
            // часовой грань смотрит вниз и в кадре ее просто нет. Поймано на
            // p4b - пятна исчезли, голая земля выросла с 0.228 до 0.343.
            tris.Add(0); tris.Add(1 + (i + 1) % uglov); tris.Add(1 + i);
        }

        var mesh = new Mesh { name = "pyatno-" + imya };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("pyat-" + imya);
        go.transform.SetParent(_root, false);
        go.transform.position = new Vector3(centr.x, SleduyushchayaVysota(), centr.z);
        go.transform.rotation = Quaternion.Euler(0f, povorot, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var tex = V2Lib.NaydiZemlyu(tekstura);
        var m = V2Lib.NewLit(Color.white, tex, 0.05f, imya: "zem-" + imya);
        m.SetTextureScale("_BaseMap",
                          new Vector2(radius * 2f / metrovNaTayl, radius * 2f / metrovNaTayl));
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
    }

    /// <summary>Дорога от точки к точке. Ширина в метрах.</summary>
    public static void Doroga(Vector3 a, Vector3 b, float shirina = 4f,
                              string tekstura = "z-doroga")
    {
        var d = b - a; d.y = 0f;
        float dlina = d.magnitude;
        if (dlina < 0.1f) return;
        float ugol = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        var centr = (a + b) * 0.5f;
        // текстура колеи идет вдоль плитки по X, поэтому разворачиваем на 90
        var go = Uchastok($"doroga-{(int)centr.x}-{(int)centr.z}", tekstura,
                          centr, shirina, dlina, ugol + 90f, 6f, Y_DOROGA);
        var m = go.GetComponent<MeshRenderer>().sharedMaterial;
        m.SetTextureScale("_BaseMap", new Vector2(1f, dlina / 6f));
    }

    /// <summary>Наклейка с прозрачностью: разметка, пятно, знак.</summary>
    public static void Nakleyka(string imya, string tekstura, Vector3 centr,
                                float razmer, float povorot, float y = Y_NAKLEYKA)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "nakl-" + imya;
        go.transform.SetParent(_root, false);
        go.transform.position = new Vector3(centr.x, y, centr.z);
        go.transform.rotation = Quaternion.Euler(90f, povorot, 0f);
        go.transform.localScale = new Vector3(razmer, razmer, 1f);

        var tex = V2Lib.NaydiZemlyu(tekstura);
        go.GetComponent<MeshRenderer>().sharedMaterial =
            V2Lib.NewLitVyrez(Color.white, tex, "nakl-" + imya);
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    /// <summary>
    /// Кайма знаков опасности по КРАЮ участка, посчитанному из его же
    /// трансформа.
    ///
    /// Раньше края считались на глаз по экранным координатам, и полоса легла
    /// через середину посадочной площадки вместо ее кромки. Считать край
    /// участка вручную второй раз - значит заводить второй источник правды,
    /// который рано или поздно разойдется с первым.
    /// </summary>
    public static void Kayma(GameObject uchastok, float shirina = 0.9f,
                             bool tolkoDve = false)
    {
        // Углы берем преобразованием ЛОКАЛЬНЫХ углов квада, а не сложением
        // мировых осей. Квад лежит в локальных координатах от -0.5 до 0.5 по X
        // и Y, и TransformPoint учитывает и поворот, и масштаб сам.
        //
        // Попытка сложить t.right и t.up дала рамку втрое больше участка,
        // раскинутую поперек всего кадра: у квада, повернутого на 90 градусов
        // по X, мировые оси не совпадают с теми, что кажутся очевидными.
        var t = uchastok.transform;
        var ugly = new[]
        {
            t.TransformPoint(new Vector3(-0.5f, -0.5f, 0f)),
            t.TransformPoint(new Vector3( 0.5f, -0.5f, 0f)),
            t.TransformPoint(new Vector3( 0.5f,  0.5f, 0f)),
            t.TransformPoint(new Vector3(-0.5f,  0.5f, 0f)),
        };
        for (int i = 0; i < 4; i++) ugly[i].y = Y_NAKLEYKA;

        var mr = uchastok.GetComponent<MeshRenderer>();
        Debug.Log($"[v2] кайма {uchastok.name}: масштаб {t.localScale}, "
                + $"габарит рендерера {(mr != null ? mr.bounds.size.ToString("F1") : "-")}, "
                + $"углы {ugly[0].ToString("F1")} {ugly[1].ToString("F1")} "
                + $"{ugly[2].ToString("F1")} {ugly[3].ToString("F1")}");
        int n = tolkoDve ? 2 : 4;
        for (int i = 0; i < n; i++)
            Polosa(ugly[i], ugly[(i + 1) % 4], shirina);
    }

    /// <summary>Полоса знаков опасности вдоль края площадки.</summary>
    public static void Polosa(Vector3 a, Vector3 b, float shirina = 1.2f)
    {
        var d = b - a; d.y = 0f;
        float dlina = d.magnitude;
        if (dlina < 0.1f) return;
        float ugol = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        var centr = (a + b) * 0.5f;

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "polosa";
        go.transform.SetParent(_root, false);
        go.transform.position = new Vector3(centr.x, Y_NAKLEYKA, centr.z);

        // У квада, положенного плашмя через Euler(90, yaw, 0), локальная ось X
        // смотрит в мировом направлении с углом МИНУС yaw. Поэтому длина идет в
        // localScale.x, а разворот берется со знаком минус. Прежняя запись
        // (длина в Y, разворот ugol+90) клала полосу поперек отрезка, и кайма
        // площадки расходилась по кадру решеткой.
        go.transform.rotation = Quaternion.Euler(90f, -ugol, 0f);
        go.transform.localScale = new Vector3(dlina, shirina, 1f);

        var tex = V2Lib.NaydiZemlyu("d-opasnost");
        var m = V2Lib.NewLitVyrez(Color.white, tex, "polosa");
        m.SetTextureScale("_BaseMap", new Vector2(dlina / shirina, 1f));
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        Object.DestroyImmediate(go.GetComponent<Collider>());

        // Проверка числом: КОНЦЫ полосы обязаны совпасть с концами отрезка.
        //
        // Сравнивать осевой габарит нельзя: у полосы, лежащей под 45 градусов,
        // он равен длине, деленной на корень из двух. Первая версия проверки на
        // этом и погорела - она ругалась на 12 полос из 12, при том что полосы
        // были верны, а неверна была она сама.
        var k1 = go.transform.TransformPoint(new Vector3(-0.5f, 0f, 0f));
        var k2 = go.transform.TransformPoint(new Vector3(0.5f, 0f, 0f));
        float e = Mathf.Min(
            Vector3.Distance(new Vector3(k1.x, 0f, k1.z), new Vector3(a.x, 0f, a.z))
          + Vector3.Distance(new Vector3(k2.x, 0f, k2.z), new Vector3(b.x, 0f, b.z)),
            Vector3.Distance(new Vector3(k1.x, 0f, k1.z), new Vector3(b.x, 0f, b.z))
          + Vector3.Distance(new Vector3(k2.x, 0f, k2.z), new Vector3(a.x, 0f, a.z)));
        if (e > 0.5f)
            Debug.LogWarning($"[v2] полоса легла неверно: концы разошлись на {e:F2} м "
                           + $"при длине отрезка {dlina:F1}");
    }
}
