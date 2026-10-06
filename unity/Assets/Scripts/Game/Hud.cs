using MarsColony.Domain;
using MarsColony.Domain.Config;
using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Полоса состояния колонии: кредиты, склад, опыт и одна строка подсказки.
    ///
    /// Она же — единственное место, где игрок видит результат клика цифрами.
    /// Без нее посев отличается от непосева только табличкой над зданием, и
    /// «действие без видимой обратной связи» становится честной претензией.
    ///
    /// Ссылки на надписи проставляет сборщик сцены: HUD собирается скриптом,
    /// как и вся остальная сцена, без единого клика в редакторе.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        public RectTransform panel;
        public Text credits_label;
        public Text warehouse_label;
        public Text xp_label;
        public Text hint_label;

        private readonly Vector3[] _corners = new Vector3[4];

        public void Render(ColonyGame game, double now)
        {
            ColonyState state = game.State;

            if (credits_label != null)
                credits_label.text = $"Кредиты: {state.credits}";

            if (warehouse_label != null)
            {
                int qty = Warehouse.TotalQty(state.warehouse);
                warehouse_label.text = $"Склад: {qty} / {state.warehouse.capacity}";
            }

            if (xp_label != null)
                xp_label.text = $"Опыт: {state.xp}";

            if (hint_label != null)
                hint_label.text = Hint(state, now);
        }

        private static string Hint(ColonyState state, double now)
        {
            if (state.fields.Count == 0)
                return string.Empty;

            FieldSlot slot = state.fields[0];

            if (slot.state == FieldState.GROWING && slot.good_id != null)
            {
                int left = FieldView.RemainingSec(slot, now);
                Good growing = Goods.Of(slot.good_id);
                return $"{growing.name} растут: {left / 60:00}:{left % 60:00}";
            }

            if (slot.state == FieldState.READY && slot.good_id != null)
            {
                Good ready = Goods.Of(slot.good_id);
                int yield_qty = Goods.HarvestQty(slot.good_id);
                return $"Готово. Клик: забрать {yield_qty} шт, {ready.name}";
            }

            string good_id = CropChoice.CheapestUnlocked(state.level);
            if (good_id == null)
                return string.Empty;
            Good good = Goods.Of(good_id);
            return $"Клик по теплице: посеять {good.name}, {Economy.PlantingCost(good.price)} кр";
        }

        /// <summary>
        /// Рамка панели в долях экрана, начало координат — левый верхний угол,
        /// как в браузере. Нужна проверке играбельности: она сравнивает область
        /// HUD до и после клика и обязана знать, где эта область.
        /// </summary>
        public Rect ScreenRectNormalized()
        {
            if (panel == null || Screen.width == 0 || Screen.height == 0)
                return new Rect(0f, 0f, 0f, 0f);

            // Канвас режима Screen Space - Overlay живет прямо в пикселях экрана,
            // поэтому мировые углы панели — уже экранные координаты.
            panel.GetWorldCorners(_corners);
            float x0 = Mathf.Min(_corners[0].x, _corners[2].x);
            float x1 = Mathf.Max(_corners[0].x, _corners[2].x);
            float y0 = Mathf.Min(_corners[0].y, _corners[2].y);
            float y1 = Mathf.Max(_corners[0].y, _corners[2].y);

            return new Rect(
                x0 / Screen.width,
                1f - y1 / Screen.height,
                (x1 - x0) / Screen.width,
                (y1 - y0) / Screen.height
            );
        }
    }
}
