using System;
using System.Collections.Generic;
using System.Linq;
using MarsColony.Domain.Config;

namespace MarsColony.Domain
{
    /// <summary>
    /// Дрон-курьер: доход и ритм сессии, дешевый отказ. Источник истины —
    /// [[tz-drone-mars]], разделы 3, 4 и 9; перенесено из
    /// `mars-colony/src/domain/drone.ts`.
    ///
    /// Роль механики в трио: дрон — единственная, где отказ от заказа дешев.
    /// Резервирование происходит в момент тапа «Погрузить», а не при наличии
    /// товара на складе.
    /// </summary>
    public enum OrderSlotState
    {
        active,
        in_progress,
        ready,
        empty_cooldown,
    }

    public enum FilledBy
    {
        none,
        self,
        purchase,
    }

    public sealed class OrderPosition
    {
        public string good_id;
        public int qty;

        /// <summary>Сколько единиц позиции закрыто: погруженное со склада плюс докупленное.</summary>
        public int qty_filled;

        /// <summary>ЧЕМ закрыт ПОСЛЕДНИЙ взнос, а не то, чем закрыта позиция целиком.</summary>
        public FilledBy filled_by;

        /// <summary>Сколько из qty_filled пришло докупкой за изотопы (И-12), минуя склад.</summary>
        public int qty_purchased;

        /// <summary>И-8 на момент генерации: покрыто складом ИЛИ производится не дольше EASY_PRODUCE_MAX_MIN.</summary>
        public bool easy;
    }

    public sealed class OrderSlot
    {
        public int idx;
        public OrderSlotState state;
        public string npc_name;
        public List<OrderPosition> positions = new List<OrderPosition>();
        public int credits_reward;
        public int xp_reward;

        /// <summary>Когда истекает бесплатный рефреш. Значимо только в empty_cooldown.</summary>
        public double refresh_at;
    }

    public sealed class OrderRewardResult
    {
        public int credits;
        public int xp;
        public double premium;
    }

    public sealed class SendOrderResult
    {
        public bool ok;
        public int credits;
        public int xp;
    }

    public sealed class DiscardImpact
    {
        public int stock_positions;
        public int purchased_isotopes;
        public bool needs_confirm;
    }

    public sealed class GeneratorContext
    {
        public int level;
        public WarehouseState warehouse;

        /// <summary>Что игрок уже умеет производить: построенные здания и открытые культуры.</summary>
        public List<string> available_goods = new List<string>();

        /// <summary>Другие активные заказы доски — для анти-повтора (REPEAT_SCOPE=board).</summary>
        public List<OrderSlot> board = new List<OrderSlot>();

        public Func<double> rng;

        /// <summary>И-13: локи дефицита от ДРУГИХ механик (шаттл/лайнер). Null — как пустой лок-стейт.</summary>
        public DeficitLockState deficit_locks;

        /// <summary>Момент генерации — вход TTL И-13. Секунды, как весь остальной таймер игры.</summary>
        public double now;
    }

    public static class Drone
    {
        /// <summary>Недостача позиции: сколько еще нужно закрыть погрузкой или докупкой.</summary>
        private static int PositionShort(OrderPosition p) => Math.Max(0, p.qty - p.qty_filled);

        /// <summary>Складская часть позиции: то, что лежит в reserved и обязано уехать при отправке.</summary>
        private static int StockedIn(OrderPosition p) => Math.Max(0, p.qty_filled - p.qty_purchased);

        /// <summary>ТЗ 4.1: сколько заказов видно на доске. Верхняя граница, не цель наполнения.</summary>
        public static int SlotsAtLevel(int level)
        {
            foreach (var row in Economy.NUM_VISIBLE_ORDERS)
                if (level >= row.from_level)
                    return row.orders;
            return Economy.NUM_VISIBLE_ORDERS.Length > 0
                ? Economy.NUM_VISIBLE_ORDERS[Economy.NUM_VISIBLE_ORDERS.Length - 1].orders
                : 0;
        }

        /// <summary>
        /// ТЗ 4.2.1: веса числа позиций по уровню. Потолок понижен с шести до
        /// пяти — решение владельца 2026-08-05 (анти-повтор на верхних уровнях
        /// не сходится при шести).
        /// </summary>
        private static readonly Economy.LevelWeightsRow[] POSITIONS_COUNT_WEIGHTS =
        {
            new Economy.LevelWeightsRow { from_level = 15, weights = new Dictionary<int, double> { { 4, 0.4 }, { 5, 0.6 } } },
            new Economy.LevelWeightsRow { from_level = 12, weights = new Dictionary<int, double> { { 3, 0.2 }, { 4, 0.4 }, { 5, 0.4 } } },
            new Economy.LevelWeightsRow { from_level = 10, weights = new Dictionary<int, double> { { 3, 0.3 }, { 4, 0.4 }, { 5, 0.3 } } },
            new Economy.LevelWeightsRow { from_level = 8, weights = new Dictionary<int, double> { { 2, 0.2 }, { 3, 0.35 }, { 4, 0.3 }, { 5, 0.15 } } },
            new Economy.LevelWeightsRow { from_level = 6, weights = new Dictionary<int, double> { { 2, 0.35 }, { 3, 0.4 }, { 4, 0.25 } } },
            new Economy.LevelWeightsRow { from_level = 4, weights = new Dictionary<int, double> { { 1, 0.25 }, { 2, 0.45 }, { 3, 0.3 } } },
            new Economy.LevelWeightsRow { from_level = 2, weights = new Dictionary<int, double> { { 1, 0.55 }, { 2, 0.45 } } },
        };

        public static int PositionsCountFor(int level, double roll)
        {
            Dictionary<int, double> weights = null;
            foreach (var row in POSITIONS_COUNT_WEIGHTS)
                if (level >= row.from_level)
                {
                    weights = row.weights;
                    break;
                }
            if (weights == null)
                weights = new Dictionary<int, double> { { 1, 1 } };

            double acc = 0;
            foreach (var pair in weights)
            {
                acc += pair.Value;
                if (roll <= acc)
                    return pair.Key;
            }
            return weights.Keys.Last();
        }

        private static readonly string[] NPC_NAMES =
        {
            "Ирина, гидропоника",
            "Марк, столовая",
            "Лу, мастерская",
            "Дана, медблок",
            "Петр, склад",
            "Сати, оранжерея",
            "Олаф, энергоузел",
            "Ева, лаборатория",
            "Ким, шлюз",
        };

        /// <summary>
        /// «Легкая» позиция по И-8: покрыта складом ИЛИ производится не дольше
        /// EASY_PRODUCE_MAX_MIN. Время считается по всей цепочке рецепта, а не
        /// по одному циклу товара.
        /// </summary>
        private static bool IsEasy(string good_id, int qty, WarehouseState warehouse)
        {
            if (Warehouse.AvailableOf(warehouse, good_id) >= qty)
                return true;
            return RushCost.ProductionTimeMinutes(good_id, qty, warehouse)
                <= Economy.EASY_PRODUCE_MAX_MIN[Mechanic.drone];
        }

        /// <summary>
        /// Наибольшее количество, которое покрыто складом ИЛИ производится в
        /// пределах порога easy. Перебор сверху вниз: время цепочки не обратимо
        /// аналитически.
        /// </summary>
        private static int MaxEasyQty(string good_id, int target_qty, WarehouseState warehouse)
        {
            for (int qty = target_qty; qty >= 1; qty--)
                if (IsEasy(good_id, qty, warehouse))
                    return qty;
            return 0;
        }

        /// <summary>Доля позиций, которые игрок может закрыть прямо сейчас или быстро произвести.</summary>
        private static double EasyRatio(List<OrderPosition> positions)
        {
            if (positions.Count == 0)
                return 1;
            return positions.Count(p => p.easy) / (double)positions.Count;
        }

        /// <summary>
        /// Канон 1.4, PINCH_MODE=absolute: stock + clamp(targetQty - stock,
        /// PINCH_MIN, PINCH_MAX).
        /// </summary>
        public static int ApplyPinch(int stock, int target_qty)
        {
            int gap = target_qty - stock;
            return stock + Math.Min(Economy.PINCH_MAX, Math.Max(Economy.PINCH_MIN, gap));
        }

        /// <summary>Максимальная доля пересечения с любым заказом доски (ТЗ 4.2, REPEAT_SCOPE=board).</summary>
        private static double MaxRepeatRatio(List<OrderPosition> positions, List<OrderSlot> board)
        {
            var ids = new HashSet<string>(positions.Select(p => p.good_id));
            if (ids.Count == 0)
                return 0;

            double worst = 0;
            foreach (var slot in board)
            {
                if (slot.state == OrderSlotState.empty_cooldown)
                    continue;
                var other = new HashSet<string>(slot.positions.Select(p => p.good_id));
                int shared = ids.Count(id => other.Contains(id));
                worst = Math.Max(worst, shared / (double)ids.Count);
            }
            return worst;
        }

        /// <summary>Пул товаров, доступных игроку: открытые культуры и рецепты построенных зданий.</summary>
        public static List<string> AvailableGoodsFor(int level, HashSet<string> buildings)
        {
            var result = new List<string>();
            foreach (var id in Goods.ALL_GOOD_IDS)
            {
                Good good = Goods.Of(id);
                if (good.unlock_level > level)
                    continue;
                if (good.kind == GoodKind.crop)
                {
                    result.Add(id);
                    continue;
                }
                if (good.required_building != null && buildings.Contains(good.required_building.ToString()))
                    result.Add(id);
            }
            return result;
        }

        /// <summary>Крайний случай канона (1.6): пул пуст или ни одна попытка не собрала ни одной позиции.</summary>
        private static List<OrderPosition> FallbackMinimalPositions(
            GeneratorContext ctx,
            Economy.OrderGenerationDegradedReason reason
        )
        {
            List<string>[] pools =
            {
                ctx.available_goods,
                AvailableGoodsFor(ctx.level, new HashSet<string>()),
                Goods.ALL_GOOD_IDS.ToList(),
            };
            List<string> candidates = pools.FirstOrDefault(p => p.Count > 0) ?? Goods.ALL_GOOD_IDS.ToList();
            string good_id = candidates
                .OrderBy(id => Goods.Of(id).prod_time_sec)
                .ThenBy(id => Goods.Of(id).price)
                .First();

            // Канон 1.6/1.8: деградация генератора логируется, а не глотается молча.
            DomainLog.Warn($"order_generation_degraded mechanic=drone reason={reason} player_level={ctx.level}");

            int qty = Goods.GOOD_BASE_QTY[good_id].min;
            return new List<OrderPosition>
            {
                new OrderPosition
                {
                    good_id = good_id,
                    qty = qty,
                    qty_filled = 0,
                    filled_by = FilledBy.none,
                    qty_purchased = 0,
                    easy = IsEasy(good_id, qty, ctx.warehouse),
                },
            };
        }

        /// <summary>
        /// Генерация одного заказа в слот. И-8 соблюдается перегенерацией с
        /// ограниченным числом попыток: заказ, которого нет, хуже неидеального заказа.
        /// </summary>
        public static OrderSlot GenerateOrder(int idx, GeneratorContext ctx)
        {
            List<OrderPosition> best = new List<OrderPosition>();
            bool best_has_deficit = false;

            var board_sets = new List<HashSet<string>>();
            foreach (var slot in ctx.board)
            {
                if (slot.state == OrderSlotState.empty_cooldown)
                    continue;
                board_sets.Add(new HashSet<string>(slot.positions.Select(p => p.good_id)));
            }

            for (int attempt = 0; attempt < Economy.GEN_MAX_ATTEMPTS; attempt++)
            {
                int count = PositionsCountFor(ctx.level, ctx.rng());
                var pool = new List<string>(ctx.available_goods);
                var positions = new List<OrderPosition>();
                int deficit_used = 0;

                while (positions.Count < count && pool.Count > 0)
                {
                    var chosen = new HashSet<string>(positions.Select(p => p.good_id));
                    double best_worst = double.PositiveInfinity;
                    var from = new List<int>();
                    for (int i = 0; i < pool.Count; i++)
                    {
                        string candidate = pool[i];
                        double worst = 0;
                        foreach (var slot_ids in board_sets)
                        {
                            double shared = slot_ids.Contains(candidate) ? 1 : 0;
                            foreach (var id in chosen)
                                if (slot_ids.Contains(id))
                                    shared += 1;
                            worst = Math.Max(worst, shared);
                        }
                        if (worst < best_worst)
                        {
                            best_worst = worst;
                            from.Clear();
                        }
                        if (worst == best_worst)
                            from.Add(i);
                    }
                    int pick_at = from[Math.Min(from.Count - 1, (int)Math.Floor(ctx.rng() * from.Count))];
                    string good_id = pool[pick_at];
                    pool.RemoveAt(pick_at);
                    int qty = Goods.SlotQuantity(good_id, Mechanic.drone, ctx.level, ctx.rng());

                    int have = Warehouse.AvailableOf(ctx.warehouse, good_id);
                    bool locked_elsewhere = DeficitLock.IsDeficitLockedByOtherMechanic(
                        ctx.deficit_locks ?? DeficitLock.Create(),
                        good_id,
                        Mechanic.drone,
                        ctx.now
                    );

                    if (!IsEasy(good_id, qty, ctx.warehouse))
                    {
                        if (deficit_used >= Economy.MAX_DEFICIT_SLOTS || locked_elsewhere)
                        {
                            int easy_qty = MaxEasyQty(good_id, qty, ctx.warehouse);
                            if (easy_qty < Goods.GOOD_BASE_QTY[good_id].min)
                                continue;
                            qty = easy_qty;
                        }
                        else
                        {
                            qty = ApplyPinch(have, qty);
                            if (!IsEasy(good_id, qty, ctx.warehouse))
                                deficit_used += 1;
                        }
                    }

                    positions.Add(
                        new OrderPosition
                        {
                            good_id = good_id,
                            qty = qty,
                            qty_filled = 0,
                            filled_by = FilledBy.none,
                            qty_purchased = 0,
                            easy = IsEasy(good_id, qty, ctx.warehouse),
                        }
                    );
                }

                bool coverage_ok = EasyRatio(positions) >= Economy.COVERAGE_MIN[Mechanic.drone];
                bool repeat_ok = MaxRepeatRatio(positions, ctx.board) <= Economy.REPEAT_CAP;
                if (coverage_ok && repeat_ok && positions.Count > 0)
                {
                    best = positions;
                    best_has_deficit = deficit_used > 0;
                    break;
                }
                if (positions.Count > best.Count)
                {
                    best = positions;
                    best_has_deficit = deficit_used > 0;
                }
            }

            bool empty = best.Count == 0;
            bool unresolvable = !empty && EasyRatio(best) < Economy.COVERAGE_MIN[Mechanic.drone];
            bool degraded = empty || unresolvable;
            List<OrderPosition> positions_final = degraded
                ? FallbackMinimalPositions(
                    ctx,
                    empty
                        ? (ctx.available_goods.Count == 0
                            ? Economy.OrderGenerationDegradedReason.empty_pool
                            : Economy.OrderGenerationDegradedReason.max_attempts)
                        : Economy.OrderGenerationDegradedReason.unresolvable_invariant
                )
                : best;

            OrderRewardResult reward = OrderReward(
                positions_final,
                ctx.rng(),
                degraded ? false : best_has_deficit
            );

            foreach (var position in positions_final)
            {
                if (!position.easy)
                {
                    DeficitLock.RegisterDeficitLock(
                        ctx.deficit_locks ?? DeficitLock.Create(),
                        position.good_id,
                        Mechanic.drone,
                        $"drone:{idx}",
                        ctx.now
                    );
                }
            }

            return new OrderSlot
            {
                idx = idx,
                state = OrderSlotState.active,
                npc_name = NPC_NAMES[idx % NPC_NAMES.Length],
                positions = positions_final,
                credits_reward = reward.credits,
                xp_reward = reward.xp,
                refresh_at = 0,
            };
        }

        /// <summary>ТЗ 4.3: награда считается дроном, а не генератором.</summary>
        public static OrderRewardResult OrderReward(
            List<OrderPosition> positions,
            double jitter_roll = 0.5,
            bool has_deficit_position = false
        )
        {
            double market_sum = positions.Sum(p => Goods.Of(p.good_id).price * p.qty);

            double premium = 1.48;
            bool has_factory = positions.Any(p => Goods.Of(p.good_id).kind == GoodKind.factory);
            bool all_crops = positions.Count > 0 && positions.All(p => Goods.Of(p.good_id).kind == GoodKind.crop);

            if (has_factory)
                premium += 0.08;
            else if (all_crops)
                premium -= 0.05;

            if (positions.Count <= 2)
                premium += 0.05;
            else if (positions.Count >= 5)
                premium -= 0.05;

            if (has_deficit_position)
                premium += Economy.DRONE_PREMIUM_DEFICIT_BONUS;

            premium += (jitter_roll - 0.5) * 0.04;
            premium = Math.Min(
                1 + Economy.DRONE_PREMIUM_RANGE_MAX,
                Math.Max(1 + Economy.DRONE_PREMIUM_RANGE_MIN, premium)
            );

            int xp = positions.Sum(p => Goods.Of(p.good_id).base_xp * Economy.XP_MULTIPLIER_K[Mechanic.drone] * p.qty);

            double credits = RushCost.RoundToShowcase(market_sum * premium);
            if (market_sum > 0)
            {
                double ceiling = market_sum * (1 + Economy.DRONE_PREMIUM_RANGE_MAX);
                double floor = market_sum * (1 + Economy.DRONE_PREMIUM_RANGE_MIN);
                if (credits > ceiling)
                    credits = RushCost.ShowcaseFloor(ceiling);
                if (credits < floor)
                    credits = RushCost.ShowcaseCeil(floor);
            }

            return new OrderRewardResult { credits = (int)Math.Round(credits), xp = xp, premium = premium };
        }

        /// <summary>Хватает ли склада на конкретную позицию.</summary>
        public static bool PositionCovered(OrderPosition position, WarehouseState warehouse)
        {
            int short_ = PositionShort(position);
            if (short_ == 0)
                return true;
            return Warehouse.AvailableOf(warehouse, position.good_id) >= short_;
        }

        /// <summary>Может ли игрок закрыть заказ прямо сейчас: склад покрывает все непогруженные позиции.</summary>
        public static bool CanFulfillNow(OrderSlot slot, WarehouseState warehouse)
        {
            if (slot.state == OrderSlotState.empty_cooldown)
                return false;
            if (slot.positions.Count == 0)
                return false;

            var needed = new Dictionary<string, int>();
            foreach (var position in slot.positions)
            {
                int short_ = PositionShort(position);
                if (short_ == 0)
                    continue;
                needed[position.good_id] = (needed.TryGetValue(position.good_id, out var v) ? v : 0) + short_;
            }

            foreach (var pair in needed)
                if (Warehouse.AvailableOf(warehouse, pair.Key) < pair.Value)
                    return false;
            return true;
        }

        /// <summary>Погрузка позиции со склада. Частичная допустима (ТЗ дрона 7.2).</summary>
        public static bool LoadPosition(OrderSlot slot, int position_idx, WarehouseState warehouse)
        {
            if (position_idx < 0 || position_idx >= slot.positions.Count)
                return false;
            OrderPosition position = slot.positions[position_idx];
            if (slot.state != OrderSlotState.active && slot.state != OrderSlotState.in_progress)
                return false;

            int short_ = PositionShort(position);
            if (short_ == 0)
                return false;

            int take = Math.Min(short_, Warehouse.AvailableOf(warehouse, position.good_id));
            if (take <= 0)
                return false;
            if (!Warehouse.Reserve(warehouse, position.good_id, take))
                return false;

            position.qty_filled += take;
            if (position.filled_by == FilledBy.none)
                position.filled_by = FilledBy.self;

            slot.state = slot.positions.All(p => PositionShort(p) == 0)
                ? OrderSlotState.ready
                : OrderSlotState.in_progress;
            return true;
        }

        /// <summary>Цена докупки НЕДОСТАЮЩЕЙ части позиции: И-4, rush-cost цепочки с наценкой.</summary>
        public static int PositionBuyoutPrice(OrderPosition position)
        {
            int short_ = PositionShort(position);
            if (short_ == 0)
                return 0;
            return RushCost.BuyoutPrice(position.good_id, short_);
        }

        /// <summary>Докупка НЕДОСТАЮЩЕЙ части позиции за изотопы (ТЗ дрона 7.2, И-12).</summary>
        public static bool BuyoutPosition(OrderSlot slot, int position_idx)
        {
            if (position_idx < 0 || position_idx >= slot.positions.Count)
                return false;
            OrderPosition position = slot.positions[position_idx];
            if (slot.state != OrderSlotState.active && slot.state != OrderSlotState.in_progress)
                return false;

            int short_ = PositionShort(position);
            if (short_ == 0)
                return false;

            position.qty_filled += short_;
            position.qty_purchased += short_;
            position.filled_by = FilledBy.purchase;

            slot.state = slot.positions.All(p => PositionShort(p) == 0)
                ? OrderSlotState.ready
                : OrderSlotState.in_progress;
            return true;
        }

        /// <summary>Отправка: зарезервированное физически уходит со склада, слот пустеет.</summary>
        public static SendOrderResult SendOrder(OrderSlot slot, WarehouseState warehouse)
        {
            if (slot.state != OrderSlotState.ready)
                return new SendOrderResult { ok = false, credits = 0, xp = 0 };

            var shipping = new Dictionary<string, int>();
            foreach (var position in slot.positions)
            {
                int from_stock = StockedIn(position);
                if (from_stock > 0)
                    shipping[position.good_id] = (shipping.TryGetValue(position.good_id, out var v) ? v : 0) + from_stock;
            }

            foreach (var pair in shipping)
                if (Warehouse.ReservedOf(warehouse, pair.Key) < pair.Value || Warehouse.QtyOf(warehouse, pair.Key) < pair.Value)
                    return new SendOrderResult { ok = false, credits = 0, xp = 0 };

            foreach (var pair in shipping)
                Warehouse.ShipReserved(warehouse, pair.Key, pair.Value);

            return new SendOrderResult { ok = true, credits = slot.credits_reward, xp = slot.xp_reward };
        }

        /// <summary>Выброс заказа. Уже погруженное возвращается на склад — выброс остается дешевым.</summary>
        public static void DiscardOrder(OrderSlot slot, double now)
        {
            if (slot.state == OrderSlotState.empty_cooldown)
                return;
            slot.state = OrderSlotState.empty_cooldown;
            slot.refresh_at = now + Economy.DRONE_REFRESH_FREE_SEC;
        }

        /// <summary>И-13: снятие лока дефицита при терминальном событии заказа-владельца.</summary>
        public static void ReleaseOrderDeficitLocks(OrderSlot slot, DeficitLockState locks)
        {
            foreach (var position in slot.positions)
                if (!position.easy)
                    DeficitLock.ReleaseDeficitLock(locks, position.good_id, Mechanic.drone, $"drone:{slot.idx}");
        }

        /// <summary>Возврат резерва при выбросе (действие clear каркаса). Докупленное аннулируется безвозвратно.</summary>
        public static void ReleaseReserved(OrderSlot slot, WarehouseState warehouse)
        {
            foreach (var position in slot.positions)
            {
                int from_stock = StockedIn(position);
                if (from_stock > 0)
                    Warehouse.Unreserve(warehouse, position.good_id, from_stock);

                position.qty_filled = 0;
                position.filled_by = FilledBy.none;
                position.qty_purchased = 0;
            }
        }

        /// <summary>
        /// Снять загрузку и вернуть заказ в active. Зеркало `cancelLoading` из drone.ts:
        /// LoadPosition переводит слот в in_progress с первой же частичной позиции, и после
        /// сорвавшейся отправки заказ залипал навсегда (проверка 08.09).
        /// </summary>
        public static void CancelLoading(OrderSlot slot, WarehouseState warehouse)
        {
            ReleaseReserved(slot, warehouse);
            if (slot.state == OrderSlotState.in_progress || slot.state == OrderSlotState.ready)
                slot.state = OrderSlotState.active;
        }

        /// <summary>Последствия выброса заказа — материал для confirm-диалога (ТЗ дрона 7.2).</summary>
        public static DiscardImpact GetDiscardImpact(OrderSlot slot)
        {
            int stock_positions = 0;
            int purchased_isotopes = 0;
            foreach (var position in slot.positions)
            {
                if (StockedIn(position) > 0)
                    stock_positions += 1;
                int purchased = position.qty_purchased;
                if (purchased > 0)
                    purchased_isotopes += RushCost.BuyoutPrice(position.good_id, purchased);
            }
            return new DiscardImpact
            {
                stock_positions = stock_positions,
                purchased_isotopes = purchased_isotopes,
                needs_confirm = stock_positions > 0 || purchased_isotopes > 0,
            };
        }
    }
}
