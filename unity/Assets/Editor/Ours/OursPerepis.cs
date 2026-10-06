using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Перепись сцены: что в ней вообще можно двигать.
///
/// Зачем. Сцена собиралась сборщиком, который в конце СВОДИТ меши: геометрия
/// многих объектов уехала в общие сведённые куски, а исходные объекты остались
/// пустыми оболочками. Двигать оболочку бесполезно и опасно — перенос
/// отрабатывает без единой жалобы, отчёт рапортует успех, а в кадре не
/// меняется ничего. Один такой перенос в этой сессии уже случился.
///
/// Перепись делит всё на три разряда:
///   ЖИВОЙ    — свои рендереры, опора рядом с массой. Двигается как обычно.
///   СНЕСЁННЫЙ— свои рендереры есть, но опора далеко от массы. Двигать можно,
///              только целиться надо по массе, а не по опоре.
///   ПУСТОЙ   — рендереров нет вовсе. Геометрия в чужом сведённом меше,
///              отдельно не двигается.
/// </summary>
public static class OursPerepis
{
    private const string Scena = "Assets/Scenes/ColonyOurs 2.unity";

    public static void Vypolnit()
    {
        var scena = EditorSceneManager.OpenScene(Scena, OpenSceneMode.Single);
        var zhivye = new List<string>();
        var snesyonnye = new List<string>();
        var pustye = new List<string>();
        var svedyonnye = new List<string>();

        foreach (var go in scena.GetRootGameObjects().OrderBy(g => g.name))
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var o = go.transform.position;

            if (rs.Length == 0)
            {
                pustye.Add($"{go.name,-26} опора ({o.x,6:F1},{o.z,6:F1})  детей {go.transform.childCount}");
                continue;
            }

            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            float snos = Vector2.Distance(new Vector2(o.x, o.z), new Vector2(b.center.x, b.center.z));
            string opis = $"{go.name,-26} опора ({o.x,6:F1},{o.z,6:F1})  масса ({b.center.x,6:F1},{b.center.z,6:F1})"
                        + $"  снос {snos,5:F1}  размер {b.size.x,5:F1} x {b.size.z,5:F1}";

            // Кусок больше тридцати метров в плане — это не постройка, а
            // сведённое полотно: земля, дороги, тени, кайма.
            if (b.size.x > 30f || b.size.z > 30f) svedyonnye.Add(opis);
            else if (snos > 2f) snesyonnye.Add(opis);
            else zhivye.Add(opis);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"ЖИВЫЕ (двигаются как есть): {zhivye.Count}");
        foreach (var s in zhivye) sb.AppendLine("  " + s);
        sb.AppendLine($"\nСНЕСЁННЫЕ (опора далеко от массы, целиться по массе): {snesyonnye.Count}");
        foreach (var s in snesyonnye) sb.AppendLine("  " + s);
        sb.AppendLine($"\nПУСТЫЕ (рендереров нет, отдельно не двигаются): {pustye.Count}");
        foreach (var s in pustye) sb.AppendLine("  " + s);
        sb.AppendLine($"\nСВЕДЁННЫЕ ПОЛОТНА (земля, дороги, тени): {svedyonnye.Count}");
        foreach (var s in svedyonnye) sb.AppendLine("  " + s);

        Debug.Log("[ours] перепись\n" + sb);
        System.IO.File.WriteAllText(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(
                Application.dataPath, "../../mars-colony/loop/scene-v2/PEREPIS-COLONYOURS2.txt")),
            sb.ToString());
    }
}
