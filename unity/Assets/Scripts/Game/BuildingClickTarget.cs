using UnityEngine;

namespace MarsColony.Game
{
    /// <summary>
    /// Цель клика на постройке-витрине: здание, у которого пока нет доменного
    /// действия, только имя и подсветка.
    ///
    /// Раньше в это место целился ряд подписанных кнопок под кадром (см.
    /// снятый `InterfeysBuilder.Hab()`). Заказчик заменил его нажатием прямо в
    /// мире: «при клике на здание появляется его название и сам объект
    /// выделяется контуром». Здесь нет ни производства, ни склада — это
    /// виток UI, а не экономики (рубеж 2 еще не начат), поэтому
    /// <see cref="ClickTarget.OnClicked"/> ничего не считает. Отклик на клик
    /// (контур + карточка) ведет <see cref="VyborZdaniy"/> — единственный
    /// обработчик ввода этой сцены-витрины: он и решает, что значит клик.
    ///
    /// Компонент навешивается редакторским сборщиком (`ZdaniyaCeliBuilder`),
    /// а не руками и не в рантайме: сцена — продукт скрипта, как и весь
    /// остальной `MAIN.unity`.
    /// </summary>
    public sealed class BuildingClickTarget : ClickTarget
    {
        [SerializeField] private string chelovecheskoeImya;

        /// <summary>Ключ роли из словаря `Power.CONSUMPTION` — для отчетов и моста.</summary>
        [SerializeField] private string rol;

        // --- Макушка по вершинам меша (виток "ночь-4") ---
        //
        // Плашка с именем якорилась по экранному bbox видимых рендереров
        // (`VyborZdaniy.SchitatEkrannyyBboxRendererov`) — коробка ПО ОСЯМ
        // МИРА, а не по силуэту, и в изометрии она заметно шире и выше
        // видимой массы. Инспектор поймал разъезд числом: плашка легла на
        // 90 px ближе к соседнему пропу, чем к своему зданию. QuestMarker уже
        // решал ровно эту задачу для маяка цели (см. `QuestMarker.
        // MakushkaMir`/`SchitatMakushkuPoVersham`) — здесь тот же приём,
        // применённый к ЛЮБОМУ выбранному зданию, а не только к цели квеста.
        //
        // Печётся редактором (`InterfeysBuilder.ObespechitMakushkiZdaniy`,
        // `InterfeysBuilder.KontaktVydeleniya`), а не в рантайме — та же
        // причина, что и у QuestMarker: часть моделей импортирована с
        // isReadable: 0, `Mesh.vertices` в Play на них не читается, а вне
        // Play (редакторский контекст сборки) читается всегда.
        [SerializeField] private Vector3 _makushkaZapechena;
        [SerializeField] private bool _makushkaZapechenaEst;

        public override string TargetId => string.IsNullOrEmpty(rol) ? name : rol;

        public override string Label => string.IsNullOrEmpty(chelovecheskoeImya) ? name : chelovecheskoeImya;

        /// <summary>Заполняется сборщиком сразу после AddComponent.</summary>
        public void Nastroit(string rolKlyuch, string imya)
        {
            rol = rolKlyuch;
            chelovecheskoeImya = imya;
        }

        /// <summary>
        /// Мировая точка макушки: испечённое редактором значение, иначе
        /// честный пересчёт по вершинам (сработает, только если меш всё-таки
        /// читаем — тот же фолбэк на габарит, что у `QuestMarker.MakushkaMir`).
        /// </summary>
        public Vector3 MakushkaMir(Camera cam)
        {
            if (_makushkaZapechenaEst)
                return _makushkaZapechena;
            return SchitatMakushkuPoVersham(cam, out _, out _);
        }

        /// <summary>
        /// Печёт макушку ПРЯМО СЕЙЧАС и запоминает её в сериализуемое поле.
        /// Вызывается редактором вне Play — см. докстринг над полями выше.
        /// </summary>
        public void ZapechMakushkuRedaktorom(Camera cam)
        {
            _makushkaZapechena = SchitatMakushkuPoVersham(cam, out int vsegoVershin, out bool poVersham);
            _makushkaZapechenaEst = true;
            if (!poVersham)
                Debug.LogWarning($"[building-click-target] '{name}': макушка испечена ФОЛБЭКОМ на "
                                + $"габарит (вершин прочитано {vsegoVershin})");
        }

        private Vector3 SchitatMakushkuPoVersham(Camera cam, out int vsegoVershinProcheno, out bool nashliPoVersham)
        {
            float luchshiyEkranY = float.MinValue;
            Vector3 luchshayaMir = transform.position;
            bool nashli = false;
            int schetVershin = 0;

            if (cam != null)
            {
                foreach (var mf in GetComponentsInChildren<MeshFilter>(false))
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null)
                        continue;
                    // См. комментарий у QuestMarker.SchitatMakushkuPoVersham:
                    // guard спрашивает не "читаем ли меш вообще" (isReadable
                    // сам по себе false у части моделей и в редакторе тоже),
                    // а "мы сейчас в Play, где чтение действительно падает".
                    if (Application.isPlaying && !mesh.isReadable)
                        continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr != null && !mr.enabled)
                        continue;

                    Matrix4x4 mirIzMestnyh = mf.transform.localToWorldMatrix;
                    Vector3[] verts = mesh.vertices;
                    schetVershin += verts.Length;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        Vector3 mir = mirIzMestnyh.MultiplyPoint3x4(verts[i]);
                        float ekranY = cam.WorldToScreenPoint(mir).y;
                        if (ekranY > luchshiyEkranY)
                        {
                            luchshiyEkranY = ekranY;
                            luchshayaMir = mir;
                            nashli = true;
                        }
                    }
                }
            }

            vsegoVershinProcheno = schetVershin;
            nashliPoVersham = nashli;

            if (!nashli)
            {
                Bounds b = ClickBounds;
                luchshayaMir = b.center + Vector3.up * b.extents.y;
            }
            return luchshayaMir;
        }

        /// <summary>
        /// Пусто намеренно. У постройки-витрины нет доменного действия по
        /// клику — весь отклик (контур, карточка имени) держит
        /// <see cref="VyborZdaniy"/>, который сам делает подбор цели лучом и
        /// не нуждается в обратном вызове отсюда. Метод оставлен
        /// переопределенным, а не заброшенным без объяснения: следующий
        /// человек не должен гадать, потерялась логика или ее тут никогда и
        /// не было.
        /// </summary>
        public override void OnClicked(ColonyGame game, double now) { }
    }
}
