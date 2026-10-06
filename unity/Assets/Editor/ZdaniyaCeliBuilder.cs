using UnityEngine;

namespace MarsColony.Editor
{
    /// <summary>
    /// Навешивает <see cref="MarsColony.Game.BuildingClickTarget"/> на
    /// постройки сцены MAIN по имени объекта.
    ///
    /// Зачем по имени, а не по компоненту `BuildingRole`. `BuildingRole`
    /// заведен для энергии и живет не на всех постройках сразу — часть их
    /// проставлена вручную позже раскладки (см. `Power.CONSUMPTION`,
    /// комментарий там же: «сцена собрана раскладкой по именам, отдельного
    /// реестра типов у объектов пока нет»). Здесь ровно тот же договор:
    /// список ролей и человеческих имен продиктован интервью цикла UI-3.
    ///
    /// Зачем сравнение не строгим равенством. Часть построек — клоны одной
    /// модели (три жилых купола, три теплицы), и у клонов, продублированных
    /// руками поверх раскладки, имя получает суффикс Unity вида
    /// " (1)". Отдельные объекты донесли иной суффикс через дефис
    /// (`zhiloy-kupol-odin-12`) — с той же семантикой «это тоже эта модель».
    /// Сравнение — точное совпадение ИЛИ совпадение до первого пробела/дефиса
    /// после ключа: это шире точного равенства ровно настолько, чтобы поймать
    /// оба варианта суффикса, и не шире — оно не тронет соседа с похожим, но
    /// другим корнем (`burovaya-02` не заденет `burovaya-05`).
    /// </summary>
    public static class ZdaniyaCeliBuilder
    {
        /// <summary>
        /// Роль -> человеческое имя. Ключи и подписи — из интервью цикла
        /// UI-3, они же ключи `Power.CONSUMPTION`. Порядок не имеет значения.
        /// </summary>
        public static readonly (string rol, string imya)[] SPISOK =
        {
            ("zhiloy-kupol", "Жилой купол"),
            ("zhiloy-bashnya", "Жилая башня"),
            ("kupol-geodezicheskiy", "Теплица"),
            ("zavod-pishchevoy", "Пищевой завод"),
            ("sklad-angar", "Ангар спецтехники"),   // заказчик 05.09: бывший «Склад» — ангар для спецтехники
            ("sklad-bunkery", "Склад"),             // заказчик 05.09: бывший «Грузовой терминал»
            ("stantsiya-plavilnya-lda", "Плавильня льда"),
            ("mashina-ledosbor", "Ледосборщик"),
            ("mashina-regolitosbor", "Сборщик реголита"),
            ("modul-tonnelnyy", "Туннельный дом"),   // заказчик 05.09: не «Общежитие»; каждый модуль — отдельная цель
            ("burovaya-02", "Карьер, добыча руды"),
            ("burovaya-05", "Буровая, добыча метана"),
            ("o2-skvazhina", "Кислородная скважина"),
        };

        /// <summary>
        /// Расставляет цели по уже открытой сцене. Идемпотентна: объект с уже
        /// навешенным `ClickTarget` пропускается, повторный запуск на той же
        /// сцене ничего не задваивает.
        ///
        /// Уже навешенная цель раньше пропускалась ДО сопоставления с
        /// `SPISOK` (`continue` на строке проверки `ClickTarget`), и
        /// `naydenyRoli` наполнялась только заново навешенными объектами.
        /// На втором и любом следующем запуске это давало ложный отчёт:
        /// все 13 ролей "не нашли объекта", хотя все 26 целей стоят с
        /// прошлого раза — просто их роль никто не зачёл. Уже навешенная
        /// цель теперь тоже засчитывается в `naydenyRoli` (по `TargetId`,
        /// который для `BuildingClickTarget` и есть ключ роли, см. там же),
        /// прежде чем пропустить её повторную разметку.
        /// </summary>
        public static int Rasstavit()
        {
            int postavleno = 0;
            int uzheStoyalo = 0;
            var naydenyRoli = new System.Collections.Generic.HashSet<string>();

            var vseTransformy = Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var t in vseTransformy)
            {
                var uzheEst = t.GetComponent<MarsColony.Game.ClickTarget>();
                if (uzheEst != null)
                {
                    if (uzheEst is MarsColony.Game.BuildingClickTarget uzheZdanie)
                    {
                        naydenyRoli.Add(uzheZdanie.TargetId);
                        uzheStoyalo++;
                    }
                    continue;
                }

                foreach (var (rol, imya) in SPISOK)
                {
                    if (!Sovpadaet(t.name, rol))
                        continue;

                    var tsel = t.gameObject.AddComponent<MarsColony.Game.BuildingClickTarget>();
                    tsel.Nastroit(rol, imya);
                    naydenyRoli.Add(rol);
                    postavleno++;
                    break;
                }
            }

            foreach (var (rol, imya) in SPISOK)
                if (!naydenyRoli.Contains(rol))
                    Debug.LogWarning($"[celi] роль '{rol}' ({imya}) не нашла ни одного объекта в сцене");

            Debug.Log($"[celi] целей всего {uzheStoyalo + postavleno}, новых {postavleno}");

            return postavleno;
        }

        private static bool Sovpadaet(string imya, string rol) =>
            imya == rol || imya.StartsWith(rol + " ") || imya.StartsWith(rol + "-");
    }
}
