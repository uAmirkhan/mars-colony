using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Работа с плитой-подложкой БЕЗ правки самой модели.
///
/// Задача. У четырнадцати построек корневого слоя под зданием сидит квадратная
/// плита два на два в единицах модели - земля, достроенная генератором
/// image-to-3d. Замер: у `zhiloy-kupol` нижний срез 2.00 при теле 0.55.
///
/// Последствие не косметическое: масштабирование по ОБЩЕЙ высоте растягивает
/// плиту вместе со зданием, и купол высотой 6 м занимает в плане 41 м.
/// Разложить такое в кадре 90 на 91 м невозможно - отсюда наложения построек.
///
/// Прежнее решение резало плиту в Blender и клало обрезанные копии рядом.
/// Владелец потребовал использовать модели В ИСХОДНОМ ВИДЕ. Требование
/// выполнимо без резки: плиту не обязательно удалять, ее достаточно **увести
/// под землю**.
///
/// Как: высота считается по ТЕЛУ здания, а не по габариту целиком, и объект
/// опускается так, чтобы низ тела встал на грунт. Плита при этом оказывается
/// ниже нуля и в кадр не попадает. У купола плита это 14% высоты - при здании
/// в 6 м она уходит под грунт на 0.8 м.
/// </summary>
public static class V2Plita
{
    /// <summary>Насколько низ должен быть шире тела, чтобы считаться плитой.</summary>
    private const float PORog = 1.6f;

    private static readonly Dictionary<string, float> _kesh = new();

    /// <summary>
    /// Доля высоты, занятая плитой. Ноль означает, что плиты нет.
    ///
    /// Ищется как уровень, выше которого ширина перестает резко падать. Мерить
    /// по фиксированной высоте нельзя: у объекта на ножках тело внизу тонкое по
    /// устройству, и отношение растет само - на этом уже обжигались, ворота
    /// давали восемь ложных срабатываний из двадцати семи.
    /// </summary>
    public static float DolyaPlity(GameObject go, string imya)
    {
        if (imya != null && _kesh.TryGetValue(imya, out var gotovo)) return gotovo;

        var tochki = new List<Vector3>();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var m = mf.transform.localToWorldMatrix;
            foreach (var v in mf.sharedMesh.vertices) tochki.Add(m.MultiplyPoint3x4(v));
        }
        if (tochki.Count < 32) return Zapomnit(imya, 0f);

        float lo = tochki.Min(t => t.y), hi = tochki.Max(t => t.y);
        float H = hi - lo;
        if (H <= 1e-4f) return Zapomnit(imya, 0f);

        float Shirina(float f0, float f1)
        {
            float y0 = lo + H * f0, y1 = lo + H * f1;
            var sloy = tochki.Where(t => t.y >= y0 && t.y <= y1).ToList();
            if (sloy.Count < 8) return 0f;
            return Mathf.Max(sloy.Max(t => t.x) - sloy.Min(t => t.x),
                             sloy.Max(t => t.z) - sloy.Min(t => t.z));
        }

        float telo = Shirina(0.35f, 0.75f);
        float niz = Shirina(0f, 0.06f);
        if (telo <= 1e-4f || niz / telo < PORog) return Zapomnit(imya, 0f);

        for (int i = 1; i < 40; i++)
        {
            float f = i / 40f;
            if (Shirina(f, Mathf.Min(f + 0.05f, 1f)) <= telo * 1.4f)
                return Zapomnit(imya, f);
        }
        return Zapomnit(imya, 0f);
    }

    private static float Zapomnit(string imya, float v)
    {
        if (imya != null) _kesh[imya] = v;
        return v;
    }
}
