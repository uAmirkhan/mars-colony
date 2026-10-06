using MarsColony.Domain;
using MarsColony.Domain.Config;
using UnityEngine;

namespace MarsColony.Game
{
    /// <summary>
    /// Вид грядки: здание сцены, которое принимает клик и показывает, что с
    /// посевом происходит прямо сейчас.
    ///
    /// Клик по пустой грядке — «заказать производство», клик по готовой —
    /// «забрать готовое на склад». Это первые два шага основного цикла среза.
    /// Правила обоих действий целиком в домене (`Production.Plant`,
    /// `Production.CollectField`); здесь только вызов и картинка.
    ///
    /// Табличка таймера над зданием рисуется кодом из двух прямоугольников, а не
    /// берется файлом: у художника свои спрайты, и любой добавленный сюда файл
    /// пришлось бы согласовывать. Прямоугольник ничего не согласовывает.
    /// </summary>
    public sealed class FieldView : ClickTarget
    {
        /// <summary>Индекс слота грядки в состоянии колонии. Ставится сборщиком сцены.</summary>
        public int field_index;

        // --- Оформление таблички. Это верстка, а не баланс: игровых чисел тут нет.
        private const float BAR_WIDTH_OF_BUILDING = 0.95f;
        private const float BAR_HEIGHT_OF_BUILDING = 0.16f;
        private const float BAR_GAP_OF_HEIGHT = 0.05f;
        private const int BAR_SORTING_ORDER = 30000;

        private static readonly Color BarBack = new Color(0.10f, 0.07f, 0.06f, 0.92f);
        private static readonly Color BarGrowing = new Color(1.00f, 0.66f, 0.16f, 1f);
        private static readonly Color BarReady = new Color(0.38f, 0.94f, 0.42f, 1f);

        private SpriteRenderer _bar_back;
        private SpriteRenderer _bar_fill;

        public override string TargetId => $"field_{field_index}";

        public override string Label => "Теплица " + (field_index + 1);

        public override void OnClicked(ColonyGame game, double now)
        {
            FieldSlot slot = SlotOf(game);
            if (slot == null)
                return;

            ProductionContext ctx = game.State.ContextAt(now);

            if (slot.state == FieldState.EMPTY)
            {
                string good_id = CropChoice.CheapestUnlocked(game.State.level);
                if (good_id == null)
                    return;
                game.State.Apply(Production.Plant(slot, good_id, ctx, game.State.fields));

                // Дефицит энергии удлиняет цикл. Правило применяется ЗДЕСЬ, а не
                // в `Production.Plant`: домен — зеркало TypeScript-каркаса, где
                // энергии нет, и добавка туда сделала бы файл расходящимся с
                // оригиналом. Колонийское правило живёт в игровом слое и лишь
                // растягивает уже назначенный доменом срок.
                if (slot.state == FieldState.GROWING && !MarsColony.Domain.Config.Power.IsEnough(
                        MarsColony.Domain.Config.Power.generated,
                        MarsColony.Domain.Config.Power.consumed))
                {
                    double left = slot.ends_at - now;
                    slot.ends_at = now + left * MarsColony.Domain.Config.Power.DEFICIT_FACTOR;
                }
                return;
            }

            if (slot.state == FieldState.READY)
                game.State.Apply(Production.CollectField(slot, ctx));
        }

        public override void Render(ColonyGame game, double now)
        {
            FieldSlot slot = SlotOf(game);
            if (slot == null)
                return;

            EnsureBar();

            if (slot.state == FieldState.EMPTY)
            {
                _bar_back.gameObject.SetActive(false);
                _bar_fill.gameObject.SetActive(false);
                return;
            }

            _bar_back.gameObject.SetActive(true);
            _bar_fill.gameObject.SetActive(true);

            // Габарит берётся у мешей, а не у спрайта: в трёхмерной сцене
            // спрайта нет вовсе, и табличка не появлялась ни разу.
            Bounds b = ClickBounds;
            float width = b.size.x * BAR_WIDTH_OF_BUILDING;
            float height = Mathf.Max(b.size.y * BAR_HEIGHT_OF_BUILDING, width * 0.12f);
            float y = b.max.y + b.size.y * BAR_GAP_OF_HEIGHT + height * 0.5f;

            // Табличка разворачивается к камере. Плоский спрайт в трёхмерной
            // сцене иначе виден с ребра и читается как чёрточка.
            Camera cam = Camera.main;
            Quaternion k_kamere = cam != null
                ? Quaternion.LookRotation(cam.transform.forward, Vector3.up)
                : Quaternion.identity;
            Vector3 vpravo = k_kamere * Vector3.right;

            Vector3 centr = new Vector3(b.center.x, y, b.center.z);
            _bar_back.transform.position = centr;
            _bar_back.transform.rotation = k_kamere;
            _bar_back.transform.localScale = new Vector3(width, height, 1f);

            float fill = Progress(slot, now);
            _bar_fill.transform.position = centr - vpravo * (width * 0.5f);
            _bar_fill.transform.rotation = k_kamere;
            _bar_fill.transform.localScale = new Vector3(width * fill, height, 1f);
            _bar_fill.color = slot.state == FieldState.READY ? BarReady : BarGrowing;
        }

        /// <summary>Доля пройденного цикла. Готовая грядка — всегда полная полоса.</summary>
        public static float Progress(FieldSlot slot, double now)
        {
            if (slot.state == FieldState.READY)
                return 1f;
            if (slot.state != FieldState.GROWING || slot.good_id == null)
                return 0f;
            int total = Goods.Of(slot.good_id).prod_time_sec;
            if (total <= 0)
                return 1f;
            double left = slot.ends_at - now;
            return Mathf.Clamp01((float)((total - left) / total));
        }

        public static int RemainingSec(FieldSlot slot, double now)
        {
            if (slot.state != FieldState.GROWING)
                return 0;
            return Mathf.Max(0, Mathf.CeilToInt((float)(slot.ends_at - now)));
        }

        /// <summary>
        /// Индекс поля, на которое сейчас указывает карточка цели снизу
        /// экрана (виток UI-10). Единственный источник для карточки
        /// (<see cref="ZhivoyInterfeys.Glagol"/>/<see cref="ZhivoyInterfeys.Zadanie"/>)
        /// и для маяка на карте (<see cref="MayakTseli"/> через
        /// <see cref="QuestMarker"/>) — раньше оба места держали свой
        /// собственный хардкод "[0]", и разъезд этих двух чисел был
        /// вопросом времени, а не гипотезой.
        ///
        /// Возвращает -1, если полей нет вовсе.
        /// </summary>
        public static int TekushchayaCelPolya(ColonyState state)
        {
            if (state == null || state.teplitsy.Count == 0)
                return -1;

            // Приоритет состояния (заказчик 06.09: «посеял по заданию в другую теплицу — задание не пропало»):
            // READY — собрать раньше всего; иначе GROWING — ждать; иначе первая с пустым горшком — посеять.
            // Теплица представлена своим лучшим горшком (`ColonyState.PoleTeplitsy`).
            for (int i = 0; i < state.teplitsy.Count; i++) { var f = state.PoleTeplitsy(i); if (f != null && f.state == FieldState.READY) return i; }
            for (int i = 0; i < state.teplitsy.Count; i++) { var f = state.PoleTeplitsy(i); if (f != null && f.state == FieldState.GROWING) return i; }
            for (int i = 0; i < state.teplitsy.Count; i++) { var f = state.PoleTeplitsy(i); if (f != null && f.state == FieldState.EMPTY) return i; }
            return 0;
        }

        private FieldSlot SlotOf(ColonyGame game)
        {
            if (game == null) return null;
            return game.State.PoleTeplitsy(field_index);   // теплица = набор горшков, здание отвечает лучшим из них
        }

        private void EnsureBar()
        {
            if (_bar_back != null)
                return;

            _bar_back = MakePart("timer_back", BarBack, new Vector2(0.5f, 0.5f), 0);
            _bar_fill = MakePart("timer_fill", BarGrowing, new Vector2(0f, 0.5f), 1);
        }

        private SpriteRenderer MakePart(string name, Color color, Vector2 pivot, int order_shift)
        {
            Texture2D tex = Texture2D.whiteTexture;
            var go = new GameObject($"{TargetId}_{name}");
            // Табличка живёт под своим зданием: иначе она остаётся сиротой в
            // корне сцены и переживает удаление грядки.
            go.transform.SetParent(transform, true);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                pivot,
                tex.width
            );
            sr.color = color;
            sr.sortingOrder = BAR_SORTING_ORDER + order_shift;
            return sr;
        }
    }
}
