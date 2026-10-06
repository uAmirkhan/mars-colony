using UnityEngine;

namespace MarsColony.Game
{
    /// <summary>
    /// Текущая цель задания: один источник и для подсветки в мире, и для
    /// карточки в интерфейсе.
    ///
    /// Зачем отдельный компонент. До него текст карточки писался руками, а
    /// кольцо подсветки выбирало здание своей эвристикой по месту в кадре. Два
    /// независимых решения об одном и том же — это гарантированный разъезд:
    /// карточка обещает одно, подсветка показывает другое, и заметить это можно
    /// только глазами на кадре (дефект B1c в `BAZA-KRITIKI-2026-09-01.md`).
    ///
    /// Теперь цель хранится здесь, а подсветка и карточка её ЧИТАЮТ. Разъехаться
    /// им больше негде: источник один.
    /// </summary>
    public sealed class QuestMarker : MonoBehaviour
    {
        /// <summary>Объект сцены, на который указывает задание.</summary>
        public GameObject target;

        /// <summary>Заголовок карточки. Обычно постоянный.</summary>
        public string zagolovok = "ЗАДАНИЕ";

        /// <summary>Тело карточки. Перенос строки — обычный \n.</summary>
        [TextArea(2, 4)]
        public string telo = "Посеять водоросли\nв теплице";

        /// <summary>Награда, как она показана игроку.</summary>
        public string nagrada = "+1 XP";

        /// <summary>
        /// Насколько высоко над целью висит остриё указателя. Маркер, поднятый
        /// выше, при наклонной камере визуально уезжает к соседнему зданию —
        /// это уже ловило приёмку, поэтому значение маленькое и вынесено сюда.
        /// </summary>
        public float marker_height = 0.30f;

        /// <summary>Есть ли на что указывать.</summary>
        public bool HasTarget => target != null;

        /// <summary>Габарит цели по видимым частям. Пустой, если цели нет.</summary>
        public Bounds TargetBounds
        {
            get
            {
                if (target == null) return new Bounds();
                var rs = target.GetComponentsInChildren<Renderer>(false);
                if (rs.Length == 0) return new Bounds(target.transform.position, Vector3.zero);
                var b = rs[0].bounds;
                foreach (var r in rs) if (r.enabled) b.Encapsulate(r.bounds);
                return b;
            }
        }

        // --- Макушка по вершинам меша (виток UI-10, приёмка попытки 3) ---
        //
        // И объединённый мировой Bounds ("верх центра" — занижал макушку купола
        // на 37 px), и объединение экранных bbox всех рендереров (задирал на
        // 51 px) — оба габарит, не силуэт: у купола каркас собран из нескольких
        // мешей, и ни один прямоугольный охват не совпадает с самой верхней НА
        // ЭКРАНЕ точкой настоящей геометрии. Единственный источник, который
        // совпал с кадром (замер: y=518 против видимых 512-520) — максимум по
        // экранной проекции всех вершин меша.
        //
        // ЛОВУШКА, ИЗ-ЗА КОТОРОЙ СЧЁТ ПЕРЕЕХАЛ В РЕДАКТОР. У модели купола
        // (`kupol-geodezicheskiy.fbx.meta`) `isReadable: 0` — Read/Write
        // Enabled выключен художником при импорте. Флаг управляет тем,
        // держит ли Unity копию вершин в CPU-памяти ПОСЛЕ загрузки в Play/
        // сборке — в РЕДАКТОРСКОМ контексте (вне Play) `Mesh.vertices`
        // читает данные импортированного ассета независимо от этого флага
        // (`ColonyOursBuilder`, `ModelProbe` в этом же проекте так и живут:
        // прогон через MCP подтвердил 17 367 вершин на этой же модели).
        //
        // ПЕРВАЯ ПОПЫТКА ЭТОЙ ПРАВКИ ошиблась: guard стоял на голом
        // `mesh.isReadable`, а это поле само по себе всегда `false` для
        // такого импорта — и в редакторе, и в Play. Guard обязан спрашивать
        // не "читаем ли меш вообще", а "мы сейчас в Play, где чтение
        // действительно падает" — `Application.isPlaying && !mesh.isReadable`.
        // В редакторе (`Application.isPlaying == false`) guard всегда
        // пропускает, в Play — режет ровно те меши, что кинули бы ошибку.
        //
        // Печём поэтому один раз из редактора
        // (`InterfeysBuilder.ObespechitQuestMarker`) в сериализуемое поле —
        // оно уезжает в MAIN.unity вместе со сценой и живо уже на первом
        // кадре Play без единого обращения к `Mesh.vertices` в рантайме.
        // Рантаймовый путь (`SchitatMakushkuPoVersham`) остаётся резервным на
        // случай не испечённой цели, тем же guard'ом защищён от падения в
        // Play и логирует результат — фолбэк никогда не молчит.
        [SerializeField] private Vector3 _makushkaZapechena;
        [SerializeField] private bool _makushkaZapechenaEst;

        private GameObject _kesh_cel;
        private Matrix4x4 _kesh_kamera_matrix;
        private Vector3 _kesh_makushka;
        private bool _kesh_est;

        /// <summary>
        /// Мировая точка самой верхней (по экранной проекции) вершины меша
        /// цели. Порядок источников: испечённое сборщиком значение → рантайм-
        /// кэш по цели+матрице камеры → честный пересчёт (сработает только
        /// если меш всё-таки читаем) → откат на габарит.
        /// </summary>
        public Vector3 MakushkaMir(Camera cam)
        {
            if (target == null)
                return transform.position;

            if (_makushkaZapechenaEst)
                return _makushkaZapechena;

            if (cam == null)
                return TargetBounds.center + Vector3.up * TargetBounds.extents.y;

            if (_kesh_est && _kesh_cel == target && _kesh_kamera_matrix == cam.worldToCameraMatrix)
                return _kesh_makushka;

            // Сюда рантайм попадает, только если цель не испечена
            // редактором (не должно случаться для MAIN — см. `ObespechitQuestMarker`,
            // но фолбэк обязан быть честным, а не молчаливым, если это всё же
            // произойдёт).
            _kesh_makushka = SchitatMakushkuPoVersham(cam, out int vsegoVershin, out bool poVersham);
            _kesh_cel = target;
            _kesh_kamera_matrix = cam.worldToCameraMatrix;
            _kesh_est = true;

            if (!poVersham)
                Debug.LogWarning($"[quest-marker] MakushkaMir в рантайме откатилась на габарит "
                                + $"(цель не испечена редактором, вершин прочитано {vsegoVershin})");

            return _kesh_makushka;
        }

        /// <summary>
        /// Печёт макушку ПРЯМО СЕЙЧАС и запоминает в сериализуемое поле.
        /// Вызывается редакторским сборщиком (`InterfeysBuilder.
        /// ObespechitQuestMarker`), то есть всегда вне Play — см. комментарий
        /// про `isReadable: 0` выше. Логирует число прочитанных вершин и
        /// итоговую точку: фолбэк на габарит не должен проходить незамеченным.
        /// </summary>
        public void ZapechMakushkuRedaktorom(Camera cam)
        {
            if (target == null || cam == null)
                return;

            _makushkaZapechena = SchitatMakushkuPoVersham(cam, out int vsegoVershin, out bool poVersham);
            _makushkaZapechenaEst = true;

            float ekranY = cam.WorldToScreenPoint(_makushkaZapechena).y;
            if (poVersham)
                Debug.Log($"[quest-marker] макушка испечена по вершинам: прочитано {vsegoVershin}, "
                        + $"точка мира {_makushkaZapechena}, экранный Y {ekranY:0}");
            else
                Debug.LogWarning($"[quest-marker] макушка испечена ФОЛБЭКОМ на габарит (вершин не нашлось) — "
                                + $"точка мира {_makushkaZapechena}, экранный Y {ekranY:0}");
        }

        private Vector3 SchitatMakushkuPoVersham(Camera cam, out int vsegoVershinProcheno, out bool nashliPoVersham)
        {
            float luchshiyEkranY = float.MinValue;
            Vector3 luchshayaMir = target.transform.position;
            bool nashli = false;
            int schetVershin = 0;

            foreach (var mf in target.GetComponentsInChildren<MeshFilter>(false))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null)
                    continue;
                // Guard спрашивает не "читаем ли меш вообще" (`isReadable`
                // сам по себе `false` для этой модели и в редакторе тоже —
                // на этом сломалась первая попытка правки), а "мы сейчас в
                // Play, где чтение вершин с такого импорта действительно
                // падает". В редакторе (`Application.isPlaying == false`)
                // guard всегда пропускает.
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

            vsegoVershinProcheno = schetVershin;
            nashliPoVersham = nashli;

            // Ни одного читаемого меша не нашлось (или Play режет все
            // isReadable=false) — откат на габарит, лучше приблизительная
            // макушка, чем полное отсутствие маяка. Молчать об этом нельзя:
            // вызывающий (`ZapechMakushkuRedaktorom` или `MakushkaMir`) обязан
            // залогировать факт отката, а не тихо подсунуть занижённую точку.
            return nashli ? luchshayaMir : TargetBounds.center + Vector3.up * TargetBounds.extents.y;
        }
    }
}
