using System;
using System.Collections.Generic;
using MarsColony.Domain.Config;

namespace MarsColony.Domain
{
    /// <summary>
    /// И-4: цена докупки = rush-cost цепочки x наценка, округление к витринным
    /// числам. Источник истины — [[tz-common-systems-mars]] раздел 5, а НЕ
    /// каркас раздел 8. Перенесено из `mars-colony/src/domain/rushcost.ts`.
    ///
    /// Одна функция на все витрины докупки и ускорения — второй расчет цены
    /// докупки где-либо еще считается багом реализации.
    /// </summary>
    public static class RushCost
    {
        /// <summary>И-4: наценка докупки в слот заказа.</summary>
        public const double PURCHASE_MARGIN = 1.2;

        /// <summary>Потолок наценки любой витрины ускорения (ТЗ 5.5).</summary>
        public const double SPEEDUP_MARGIN_CEILING = 1.5;

        /// <summary>Шаг лестницы для величины. Вынесен, чтобы округление вниз и вверх брало тот же.</summary>
        private static double ShowcaseStep(double value) =>
            value < 50 ? 5 : value < 200 ? 10 : value < 1000 ? 50 : 100;

        /// <summary>
        /// Витринное округление к ближайшей ступени. Пол в один шаг лестницы:
        /// спека молчит про нулевой случай, а округление 2 к шагу 5 дает ноль,
        /// то есть бесплатную докупку.
        /// </summary>
        public static double RoundToShowcase(double value)
        {
            double step = ShowcaseStep(value);
            return Math.Max(step, Numeric.RoundHalfUp(value / step) * step);
        }

        /// <summary>Ближайшая ступень лестницы НЕ ВЫШЕ значения.</summary>
        public static double ShowcaseFloor(double value)
        {
            double step = ShowcaseStep(value);
            return Math.Max(step, Math.Floor(value / step) * step);
        }

        /// <summary>Ближайшая ступень лестницы НЕ НИЖЕ значения.</summary>
        public static double ShowcaseCeil(double value)
        {
            double step = ShowcaseStep(value);
            return Math.Max(step, Math.Ceiling(value / step) * step);
        }

        private sealed class ChainLink
        {
            public string good_id;
            public int qty_needed;
            public GoodKind kind;
            public double minutes_per_unit;
        }

        /// <summary>
        /// Рекурсивный обход графа рецептов (ТЗ 5.3). `visited` защищает от
        /// циклов рецептов.
        /// </summary>
        private static List<ChainLink> ExpandProductionChain(
            string good_id,
            int qty,
            WarehouseState warehouse,
            HashSet<string> visited
        )
        {
            var links = new List<ChainLink>();
            if (visited.Contains(good_id))
                return links;
            visited.Add(good_id);

            Good good = Goods.Of(good_id);
            int owned = Warehouse.AvailableOf(warehouse, good_id);
            int needed = Math.Max(0, qty - owned);

            links.Add(
                new ChainLink
                {
                    good_id = good_id,
                    qty_needed = qty,
                    kind = good.kind,
                    minutes_per_unit = good.prod_time_sec / 60.0,
                }
            );

            // Во входы спускаемся только за недостающим: то, что уже лежит на
            // складе, производить не нужно, и сырье под него — тоже.
            if (needed > 0)
            {
                foreach (var input in good.inputs)
                    links.AddRange(
                        ExpandProductionChain(input.good_id, needed * input.qty, warehouse, visited)
                    );
            }

            return links;
        }

        /// <summary>Сколько единиц звена реально придется произвести: склад уже лежит готовым.</summary>
        private static int MissingOf(ChainLink link, WarehouseState warehouse) =>
            Math.Max(0, link.qty_needed - Warehouse.AvailableOf(warehouse, link.good_id));

        /// <summary>
        /// Сколько минут займет получить qty единиц товара вместе со всей
        /// недостающей цепочкой рецепта. Вход порога «легко произвести» (И-8) и
        /// проверки реализуемости (И-10).
        /// </summary>
        public static double ProductionTimeMinutes(
            string good_id,
            int qty = 1,
            WarehouseState warehouse = null
        )
        {
            warehouse ??= Warehouse.Create();
            var chain = ExpandProductionChain(good_id, qty, warehouse, new HashSet<string>());
            double minutes = 0;
            foreach (var link in chain)
                minutes += MissingOf(link, warehouse) * link.minutes_per_unit;
            return minutes;
        }

        public sealed class ProductionGroupInput
        {
            public string good_id;
            public int qty;
        }

        /// <summary>
        /// И-10 (реализуемость заказа): суммарное время производства ВСЕХ позиций
        /// заказа/рейса, сгруппированных по required_building (максимум по
        /// группам — разные здания работают параллельно).
        /// </summary>
        public static double TotalProductionMinutes(
            IList<ProductionGroupInput> positions,
            WarehouseState warehouse = null
        )
        {
            warehouse ??= Warehouse.Create();
            var by_building = new Dictionary<string, double>();
            foreach (var position in positions)
            {
                string key = Goods.Of(position.good_id).required_building?.ToString() ?? "";
                double minutes = ProductionTimeMinutes(position.good_id, position.qty, warehouse);
                by_building[key] = (by_building.TryGetValue(key, out var v) ? v : 0) + minutes;
            }

            double max_minutes = 0;
            foreach (var minutes in by_building.Values)
                max_minutes = Math.Max(max_minutes, minutes);
            return max_minutes;
        }

        /// <summary>
        /// Стоимость мгновенно получить qty единиц товара со всей недостающей
        /// цепочкой, в изотопах, до наценки. Склад по умолчанию пуст.
        /// </summary>
        public static double RushCostOf(string good_id, int qty = 1, WarehouseState warehouse = null)
        {
            warehouse ??= Warehouse.Create();
            var chain = ExpandProductionChain(good_id, qty, warehouse, new HashSet<string>());

            double total = 0;
            foreach (var link in chain)
            {
                int missing = MissingOf(link, warehouse);
                if (missing == 0)
                    continue;

                int rate = Economy.SPEEDUP_RATE_ISOTOPES_PER_MIN[link.kind];
                double minutes = missing * link.minutes_per_unit;
                double floor = Economy.SPEEDUP_FLOOR_ISOTOPES[link.kind.ToString()];
                // Пол на каждое звено, а не на итог: короткий остаток по мелочи не продаем.
                total += Math.Max(minutes * rate, floor);
            }
            return total;
        }

        /// <summary>
        /// И-4: цена докупки qty единиц товара в слот заказа. Склад передает
        /// только тот вызывающий, который этот склад и спишет (докупка идет
        /// мимо склада — И-12).
        /// </summary>
        public static int BuyoutPrice(string good_id, int qty, WarehouseState warehouse = null) =>
            (int)Math.Round(RoundToShowcase(RushCostOf(good_id, qty, warehouse) * PURCHASE_MARGIN));

        /// <summary>
        /// Прямое ускорение производства (ТЗ 5.6): та же функция без наценки.
        /// Цепочка не раскрывается — склад считается полным по входам.
        /// </summary>
        public static int ProductionSpeedupPrice(string good_id, int qty = 1)
        {
            Good good = Goods.Of(good_id);
            double minutes = qty * (good.prod_time_sec / 60.0);
            int rate = Economy.SPEEDUP_RATE_ISOTOPES_PER_MIN[good.kind];
            double floor = Economy.SPEEDUP_FLOOR_ISOTOPES[good.kind.ToString()];
            return (int)Math.Round(RoundToShowcase(Math.Max(minutes * rate, floor)));
        }
    }
}
