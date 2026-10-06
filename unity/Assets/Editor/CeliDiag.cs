using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MarsColony.Editor
{
    /// <summary>
    /// Проверка витка UI-3 одной командой, без глаз в редакторе.
    ///
    /// Три вещи, которые здесь проверяются числом, а не рассказом:
    ///  1. Расстановка целей: сколько ролей из словаря нашли объект в сцене.
    ///  2. Хит-тест: луч из НАСТОЯЩЕЙ камеры сцены в экранную точку центра
    ///     здания действительно засчитывает попадание по `ClickTarget`.
    ///  3. Контур и карточка имени рендерятся, а не только компилируются —
    ///     кадр с принудительно включенным выбором сохраняется на диск, и
    ///     его можно посмотреть глазами, не поднимая Play.
    ///
    /// Ничего из проверки не остается в сцене: изменения, сделанные здесь,
    /// в файл не сохраняются (`EditorSceneManager.SaveScene` не вызывается).
    ///
    /// ГРАБЛИ, ПРО КОТОРЫЕ ПРЕДУПРЕДИЛА ПРИЕМКА ПОПЫТКИ 1. `Camera.
    /// WorldToScreenPoint`/`ScreenPointToRay`, вызванные здесь (вне Play, из
    /// редакторского скрипта), считают в разрешении окна Game, а не
    /// рендер-таргета 1600x900, даже когда `targetTexture` уже назначен —
    /// инспектор поймал на этом разъезд (1601x875 вместо 1600x900). Числа
    /// этого файла (центр карточки, попадание луча) поэтому могут разойтись
    /// на единицы-десятки пикселей с тем, что реально уйдет в PNG. В самой
    /// игре (`VyborZdaniy`) этой беды нет: там код всегда работает в Play, а
    /// в Play `Camera.pixelWidth/pixelHeight` верны. Здесь эту неточность не
    /// вычищаю — цена (ручные матрицы проекции только ради диагностики)
    /// больше пользы, а факт зафиксирован, чтобы не читать разъезд как баг
    /// продакшн-кода.
    ///
    /// Запуск: -executeMethod MarsColony.Editor.CeliDiag.Proverit
    /// </summary>
    public static class CeliDiag
    {
        const string Scena = "Assets/Scenes/MAIN.unity";

        public static void Proverit()
        {
            EditorSceneManager.OpenScene(Scena, OpenSceneMode.Single);

            // 1. Расстановка.
            int postavleno = ZdaniyaCeliBuilder.Rasstavit();
            Debug.Log($"[celi-diag] расставлено целей: {postavleno} "
                    + $"(ролей в словаре: {ZdaniyaCeliBuilder.SPISOK.Length})");

            var kam = GameObject.Find("kamera");
            var cam = kam != null ? kam.GetComponent<Camera>() : Camera.main;
            if (cam == null)
            {
                Debug.LogError("[celi-diag] камеры нет, дальше проверять нечем");
                return;
            }

            // 2. Хит-тест на каждой найденной цели: луч из камеры в экранную
            // проекцию центра габарита обязан попасть в СВОЙ же объект.
            var vse = Object.FindObjectsByType<MarsColony.Game.BuildingClickTarget>(
                FindObjectsSortMode.None);
            int ok = 0, fail = 0;
            MarsColony.Game.BuildingClickTarget dlyaKadra = null;
            foreach (var t in vse)
            {
                Bounds b = t.ClickBounds;
                Vector3 ekran = cam.WorldToScreenPoint(b.center);
                Ray luch = cam.ScreenPointToRay(ekran);

                float blizh = float.MaxValue;
                MarsColony.Game.ClickTarget best = null;
                foreach (var kandidat in vse)
                {
                    if (!kandidat.Popadanie(luch, out float d)) continue;
                    if (d < blizh) { blizh = d; best = kandidat; }
                }
                bool poopalo = best == (MarsColony.Game.ClickTarget)t;

                if (poopalo) ok++; else { fail++; Debug.LogWarning(
                    $"[celi-diag] хит-тест НЕ прошел: {t.name} ({t.Label}), "
                  + $"луч в центр {t.name} попал в {(best != null ? best.name : "ничто")}"); }

                if (dlyaKadra == null && t.TargetId == "burovaya-05") dlyaKadra = t;
            }
            Debug.Log($"[celi-diag] хит-тест: {ok} ок, {fail} провалов из {vse.Length}");

            if (dlyaKadra == null && vse.Length > 0) dlyaKadra = vse[0];
            if (dlyaKadra == null)
            {
                Debug.LogError("[celi-diag] целей нет вообще, контур и карточку показать не на чем");
                return;
            }

            // 3. Контур и карточка — на самой длинной подписи словаря
            // ("Буровая, добыча метана"), это худший случай ширины карточки.
            var kontur = MarsColony.Game.KonturZdaniya.Vklyuchit(dlyaKadra.gameObject);

            var zhivoy = Object.FindFirstObjectByType<MarsColony.Game.ZhivoyInterfeys>();
            if (zhivoy == null)
            {
                Debug.LogError("[celi-diag] ZhivoyInterfeys не найден — карточку имени показать нечем");
            }
            else
            {
                // Та же формула, что у VyborZdaniy.SchitatTochkuKartochki, минус
                // обход бейджей — диагностике важен факт рендера, не точность
                // до пикселя мимо соседних объектов.
                Rect bbox = MarsColony.Game.VyborZdaniy.SchitatEkrannyyBbox(dlyaKadra.ClickBounds, cam);
                Vector2 tochka = new Vector2(bbox.center.x, bbox.yMax + 14f);
                zhivoy.PokazatImyaZdaniya(dlyaKadra.Label, tochka, cam);
            }

            DynamicGI.UpdateEnvironment();
            for (int i = 0; i < 6; i++) { cam.Render(); System.Threading.Thread.Sleep(120); }

            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.antiAliasing = 8;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tx = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tx.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tx.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            const string vyhod = "C:/Ai/Jarvis/mars-unity/zamer-kadra/ui-3-kontur-diag.png";
            Directory.CreateDirectory(Path.GetDirectoryName(vyhod));
            File.WriteAllBytes(vyhod, tx.EncodeToPNG());
            Object.DestroyImmediate(tx);
            rt.Release();
            Object.DestroyImmediate(rt);
            Debug.Log($"[celi-diag] кадр с контуром на '{dlyaKadra.Label}' сохранен: {vyhod}");

            // Сцену НЕ сохраняем: диагностика не должна оставлять в MAIN.unity
            // ни принудительно включенный контур, ни выбранную карточку.
            kontur.Ubrat();
        }
    }
}
