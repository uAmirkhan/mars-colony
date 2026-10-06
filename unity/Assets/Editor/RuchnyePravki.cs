using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Закрепление ручных правок владельца.
///
/// Зачем это есть. Сцена собирается кодом целиком: `Mars/Sobrat koloniyu`
/// сносит всё и раскладывает заново по таблице Layout. Пока владелец только
/// смотрел на результат, это было безопасно. 2026-08-16 он подвинул руками
/// шаттл на причале и дрон-курьер — и эти правки пережили сессию только
/// потому, что сцена оказалась сохранена, а я успел вынуть координаты из
/// файла до пересборки. Второй раз так не повезёт.
///
/// Механизм. Владелец выделяет в сцене объекты, которые поставил сам, и
/// жмёт пункт меню. Их трансформы уезжают в `ruchnye-pravki.json`. При
/// следующей сборке эти объекты ставятся по записи, а не по таблице, и
/// разводка их не двигает — расталкиваются вокруг них все остальные.
///
/// Почему поиск по имени и ближайшей точке, а не по индексу. Одинаковых имён
/// в сцене много: девять `dekor-09`, три `zhiloy-barak`. Порядковый номер в
/// таблице сдвигается при каждой моей правке раскладки, и закрепление
/// молча переехало бы на чужой объект — ровно тот класс тихой порчи данных,
/// который в этом проекте уже ловили. Имя плюс ближайшая позиция переживают
/// перестановку строк в таблице.
/// </summary>
public static class RuchnyePravki
{
    [System.Serializable]
    public sealed class Pin
    {
        public string name;
        public Vector3 pos;
        public Vector3 rot;
        public Vector3 scale;
    }

    [System.Serializable]
    private sealed class Fayl
    {
        public List<Pin> items = new List<Pin>();
    }

    private static string Put =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                     "ruchnye-pravki.json");

    public static List<Pin> Zagruzit()
    {
        if (!File.Exists(Put)) return new List<Pin>();
        var f = JsonUtility.FromJson<Fayl>(File.ReadAllText(Put));
        return f?.items ?? new List<Pin>();
    }

    private static void Zapisat(List<Pin> items)
    {
        File.WriteAllText(Put, JsonUtility.ToJson(new Fayl { items = items }, true));
        AssetDatabase.Refresh();
    }

    [MenuItem("Mars/Zakrepit vydelennoe")]
    public static void Zakrepit()
    {
        var vydelennoe = Selection.gameObjects;
        if (vydelennoe.Length == 0)
        {
            Debug.LogWarning("[ручное] ничего не выделено. Выдели в сцене объекты, "
                           + "которые поставил руками, и повтори.");
            return;
        }

        var items = Zagruzit();
        int dobavleno = 0, obnovleno = 0;

        foreach (var go in vydelennoe)
        {
            var t = go.transform;
            // Тот же ключ, что и при применении: имя плюс ближайшая точка.
            var staryy = Blizhayshiy(items, go.name, t.position, 3.0f);
            if (staryy != null)
            {
                staryy.pos = t.position;
                staryy.rot = t.eulerAngles;
                staryy.scale = t.localScale;
                obnovleno++;
            }
            else
            {
                items.Add(new Pin
                {
                    name = go.name,
                    pos = t.position,
                    rot = t.eulerAngles,
                    scale = t.localScale,
                });
                dobavleno++;
            }
            Debug.Log($"[ручное] закреплён {go.name} в "
                    + $"({t.position.x:0.00}, {t.position.y:0.00}, {t.position.z:0.00})");
        }

        Zapisat(items);
        Debug.Log($"[ручное] в файле {items.Count} закреплений "
                + $"(добавлено {dobavleno}, обновлено {obnovleno}). "
                + "Пересборка их больше не сотрёт.");
    }

    [MenuItem("Mars/Snyat zakreplenie s vydelennogo")]
    public static void Snyat()
    {
        var items = Zagruzit();
        int snyato = 0;
        foreach (var go in Selection.gameObjects)
        {
            var p = Blizhayshiy(items, go.name, go.transform.position, 3.0f);
            if (p != null) { items.Remove(p); snyato++; }
        }
        Zapisat(items);
        Debug.Log($"[ручное] снято закреплений: {snyato}, осталось {items.Count}");
    }

    [MenuItem("Mars/Pokazat zakreplyonnoe")]
    public static void Pokazat()
    {
        var items = Zagruzit();
        if (items.Count == 0)
        {
            Debug.Log("[ручное] закреплений нет, сцена целиком собирается кодом");
            return;
        }
        foreach (var p in items)
            Debug.Log($"[ручное] {p.name}: ({p.pos.x:0.00}, {p.pos.y:0.00}, {p.pos.z:0.00}) "
                    + $"поворот {p.rot.y:0} масштаб {p.scale.x:0.00}");
        Debug.Log($"[ручное] всего закреплений: {items.Count}");
    }

    /// <summary>
    /// Ближайшее закрепление того же имени в пределах радиуса.
    /// Радиус нужен, чтобы закрепление не перескочило на однофамильца в
    /// другом конце карты, когда его собственный объект из раскладки убран.
    /// </summary>
    public static Pin Blizhayshiy(List<Pin> items, string imya, Vector3 tochka,
                                  float radius)
    {
        Pin best = null;
        float bd = radius * radius;
        foreach (var p in items)
        {
            if (p.name != imya) continue;
            float d = (p.pos - tochka).sqrMagnitude;
            if (d <= bd) { bd = d; best = p; }
        }
        return best;
    }
}
