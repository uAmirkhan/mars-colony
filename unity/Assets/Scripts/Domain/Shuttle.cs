using System;
using System.Collections.Generic;
using System.Linq;
using MarsColony.Domain.Config;

namespace MarsColony.Domain
{
    /// <summary>
    /// Грузовой шаттл: единственный источник строй-модулей (И-1) и главный
    /// гейт прогрессии. Источник истины — [[tz-shuttle-mars]], разделы 2-5;
    /// перенесено из `mars-colony/src/domain/shuttle.ts`.
    ///
    /// У шаттла нет отказа: заполнение последнего отсека само стартует рейс
    /// (п.2.3), а стартовавший рейс не отменяется. Награды роллятся в момент
    /// ОТПРАВКИ, а не сбора.
    /// </summary>
    public enum ShuttleState
    {
        ORDER,
        IN_TRANSIT,
        ARRIVED,
    }

    public sealed class ShuttleSlot
    {
        public int idx;
        public string good_id;
        public int qty_required;

        /// <summary>Сколько единиц в отсеке всего: погруженное со склада плюс докупленное.</summary>
        public int qty_filled;

        /// <summary>Источник ПОСЛЕДНЕГО, закрывающего взноса.</summary>
        public FilledBy filled_by;

        /// <summary>Сколько единиц отсека пришло докупкой за изотопы (И-12), минуя склад.</summary>
        public int qty_purchased;

        /// <summary>Награда, зафиксированная при отправке. До отправки — null.</summary>
        public ModuleId? reward;
        public bool collected;

        /// <summary>Отсек переписан гарантией И-11. Показывается игроку, не скрывается.</summary>
        public bool floor_forced;
    }

    public sealed class ShuttleTrip
    {
        public ShuttleState state;
        public List<ShuttleSlot> slots = new List<ShuttleSlot>();

        /// <summary>Длина рейса в минутах — нужна формуле скипа И-6 как знаменатель.</summary>
        public double trip_min;
        public double departed_at;
        public double arrives_at;
        public bool is_first_trip;

        /// <summary>Порядковый номер прибытия игрока. Вход для FTUE-удачи и И-11.</summary>
        public int arrival_no;
    }

    public sealed class ShuttleGenContext
    {
        public int level;
        public WarehouseState warehouse;
        public List<string> available_goods = new List<string>();

        /// <summary>Прошлый рейс — для анти-повтора (REPEAT_CAP считается к нему).</summary>
        public ShuttleTrip previous;

        /// <summary>Первый рейс игрока: форсированный состав и укороченный таймер (п.2.1).</summary>
        public bool is_first_trip;
        public int arrival_no;
        public Func<double> rng;

        /// <summary>И-13: локи дефицита от других механик.</summary>
        public DeficitLockState deficit_locks;
        public double now;
    }

    public sealed class LoadResult
    {
        public bool ok;
        public int loaded;
        public bool departed;

        /// <summary>Обновленные счетчики дропа — их хранит игрок, а не рейс.</summary>
        public ArrivalRoll drop_state;
        public double price;
    }

    public static class Shuttle
    {
        private static int SlotShort(ShuttleSlot slot) => Math.Max(0, slot.qty_required - slot.qty_filled);

        private static int StockedIn(ShuttleSlot slot) => Math.Max(0, slot.qty_filled - slot.qty_purchased);

        /// <summary>Хватает ли склада закрыть отсек целиком прямо сейчас.</summary>
        public static bool SlotCovered(ShuttleSlot slot, WarehouseState warehouse)
        {
            int short_ = SlotShort(slot);
            if (short_ == 0)
                return true;
            return Warehouse.AvailableOf(warehouse, slot.good_id) >= short_;
        }

        /// <summary>«Легкая» позиция по И-8: покрыта складом ИЛИ производится не дольше EASY_PRODUCE_MAX_MIN.</summary>
        private static bool IsEasy(string good_id, int qty, WarehouseState warehouse)
        {
            if (Warehouse.AvailableOf(warehouse, good_id) >= qty)
                return true;
            return RushCost.ProductionTimeMinutes(good_id, qty, warehouse)
                <= Economy.EASY_PRODUCE_MAX_MIN[Mechanic.shuttle];
        }

        /// <summary>
        /// И-10: суммарное время рейса через общую TotalProductionMinutes.
        /// Маппинг ShuttleSlot -> {good_id, qty} в одном месте.
        /// </summary>
        public static double TripProductionMinutes(List<ShuttleSlot> slots, WarehouseState warehouse) =>
            RushCost.TotalProductionMinutes(
                slots.Select(s => new RushCost.ProductionGroupInput { good_id = s.good_id, qty = s.qty_required }).ToList(),
                warehouse
            );

        /// <summary>Доля легких отсеков в рейсе. Вход инварианта И-8.</summary>
        private static double EasyRatio(List<ShuttleSlot> slots, WarehouseState warehouse)
        {
            if (slots.Count == 0)
                return 1;
            int easy = slots.Count(s => IsEasy(s.good_id, s.qty_required, warehouse));
            return easy / (double)slots.Count;
        }

        private static double RepeatRatio(List<ShuttleSlot> slots, ShuttleTrip previous)
        {
            if (previous == null || slots.Count == 0)
                return 0;
            var ids = new HashSet<string>(slots.Select(s => s.good_id));
            var before = new HashSet<string>(previous.slots.Select(s => s.good_id));
            int shared = ids.Count(id => before.Contains(id));
            return shared / (double)ids.Count;
        }

        /// <summary>
        /// Крайний случай канона (1.6): пул товаров пуст или ни один кандидат не
        /// прошел отбор. Ступень берется первая, где есть ЛЕГКИЙ по И-8 кандидат.
        /// </summary>
        private static List<ShuttleSlot> FallbackMinimalSlots(
            ShuttleGenContext ctx,
            Economy.OrderGenerationDegradedReason reason
        )
        {
            List<string>[] pools =
            {
                ctx.available_goods,
                Drone.AvailableGoodsFor(ctx.level, new HashSet<string>()),
                Goods.ALL_GOOD_IDS.ToList(),
            };
            var easy_pools = pools
                .Select(pool => pool.Where(id => IsEasy(id, Goods.GOOD_BASE_QTY[id].min, ctx.warehouse)).ToList())
                .ToList();
            List<string> candidates =
                easy_pools.FirstOrDefault(p => p.Count > 0)
                ?? pools.FirstOrDefault(p => p.Count > 0)
                ?? Goods.ALL_GOOD_IDS.ToList();

            // Канон 1.6/1.8 + Khan 06.09: деградированный рейс — не один отсек, а SLOT_COUNT_MIN самых быстрых
            // разных товаров (сначала из кандидатов, потом из всех товаров по времени производства).
            var poryadok = candidates
                .Concat(Goods.ALL_GOOD_IDS)
                .Distinct()
                .OrderBy(id => candidates.Contains(id) ? 0 : 1)
                .ThenBy(id => Goods.Of(id).prod_time_sec)
                .ThenBy(id => Goods.Of(id).price)
                .Take(Economy.SLOT_COUNT_MIN)
                .ToList();

            DomainLog.Warn($"order_generation_degraded mechanic=shuttle reason={reason} player_level={ctx.level}");

            var res = new List<ShuttleSlot>();
            for (int i = 0; i < poryadok.Count; i++)
                res.Add(new ShuttleSlot
                {
                    idx = i,
                    good_id = poryadok[i],
                    qty_required = Goods.GOOD_BASE_QTY[poryadok[i]].min,
                    qty_filled = 0,
                    qty_purchased = 0,
                    filled_by = FilledBy.none,
                    reward = null,
                    collected = false,
                    floor_forced = false,
                });
            return res;
        }

        /// <summary>
        /// И-10 (реализуемость заказа): канон 1.4, rebalanceForAchievability.
        /// Урезает количество (шаг 1 единица), а когда некуда — меняет товар на
        /// самую быструю доступную альтернативу пула. Запоминает лучшее из
        /// посещенных состояний (минимальное превышение бюджета).
        /// </summary>
        public static List<ShuttleSlot> RebalanceForAchievability(
            List<ShuttleSlot> slots,
            WarehouseState warehouse,
            double budget,
            List<string> available_goods,
            DeficitLockState deficit_locks = null,
            double now = 0
        )
        {
            deficit_locks ??= DeficitLock.Create();
            var used = new HashSet<string>(slots.Select(s => s.good_id));
            int guard_limit = Economy.GEN_MAX_ATTEMPTS * Economy.SLOT_COUNT_MAX;

            List<ShuttleSlot> Snapshot(List<ShuttleSlot> s) =>
                s.Select(slot => new ShuttleSlot
                {
                    idx = slot.idx,
                    good_id = slot.good_id,
                    qty_required = slot.qty_required,
                    qty_filled = slot.qty_filled,
                    filled_by = slot.filled_by,
                    qty_purchased = slot.qty_purchased,
                    reward = slot.reward,
                    collected = slot.collected,
                    floor_forced = slot.floor_forced,
                }).ToList();

            List<ShuttleSlot> best = Snapshot(slots);
            double best_excess = Math.Max(0, TripProductionMinutes(best, warehouse) - budget);
            double best_minutes = TripProductionMinutes(best, warehouse);

            void ConsiderBest(List<ShuttleSlot> s)
            {
                double minutes = TripProductionMinutes(s, warehouse);
                double excess = Math.Max(0, minutes - budget);
                if (excess < best_excess || (excess == best_excess && minutes < best_minutes))
                {
                    best = Snapshot(s);
                    best_excess = excess;
                    best_minutes = minutes;
                }
            }

            for (int guard = 0; guard < guard_limit; guard++)
            {
                ConsiderBest(slots);
                if (slots.Count == 0)
                    break;
                if (TripProductionMinutes(slots, warehouse) <= budget)
                    break;

                // Резать нужно там, где срез РЕАЛЬНО уменьшает время рейса, а не у
                // самой долгой позиции. Время рейса — максимум по зданиям от суммы
                // внутри здания: самая долгая позиция сплошь и рядом уже стоит на
                // полу количества, резать у неё нечего, и функция уходит менять
                // товар на товар по кругу, пока не выйдет счётчик. Зеркало shuttle.ts.
                double current_minutes = TripProductionMinutes(slots, warehouse);
                int cut_idx = -1;
                double cut_gain = 0;
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot_i = slots[i];
                    int base_min = Goods.GOOD_BASE_QTY[slot_i.good_id].min;
                    // Строго «меньше»: отсек ровно на полу не режется. Ниже пола
                    // количество могло попасть только через ApplyPinch — такой
                    // дефицитный отсек разрешено уменьшать до PINCH_MIN.
                    int floor_i = slot_i.qty_required < base_min ? Economy.PINCH_MIN : base_min;
                    if (slot_i.qty_required <= floor_i)
                        continue;
                    var proba = Snapshot(slots);
                    proba[i].qty_required -= 1;
                    double gain = current_minutes - TripProductionMinutes(proba, warehouse);
                    if (gain > cut_gain)
                    {
                        cut_gain = gain;
                        cut_idx = i;
                    }
                }
                if (cut_idx >= 0)
                {
                    slots[cut_idx].qty_required -= 1;
                    continue;
                }

                // Резать нечего: цель замены — самая долгая позиция, как в каноне.
                int target_idx = 0;
                double target_minutes = -1;
                for (int i = 0; i < slots.Count; i++)
                {
                    double minutes = RushCost.ProductionTimeMinutes(slots[i].good_id, slots[i].qty_required, warehouse);
                    if (minutes > target_minutes)
                    {
                        target_minutes = minutes;
                        target_idx = i;
                    }
                }
                ShuttleSlot target = slots[target_idx];

                var candidates = available_goods.Where(id =>
                {
                    if (id == target.good_id || used.Contains(id))
                        return false;
                    int floor = Goods.GOOD_BASE_QTY[id].min;
                    if (IsEasy(id, floor, warehouse))
                        return true;
                    return !DeficitLock.IsDeficitLockedByOtherMechanic(deficit_locks, id, Mechanic.shuttle, now);
                }).ToList();
                if (candidates.Count == 0)
                    break;

                // Замена выбирается по ВРЕМЕНИ РЕЙСА, а не по скорости товара: быстрый
                // товар с уже загруженного здания хуже медленного со свободного.
                int target_i = target_idx;
                string replacement = candidates
                    .OrderBy(id =>
                    {
                        var proba = Snapshot(slots);
                        proba[target_i].good_id = id;
                        proba[target_i].qty_required = Goods.GOOD_BASE_QTY[id].min;
                        return TripProductionMinutes(proba, warehouse);
                    })
                    .ThenBy(id => Goods.Of(id).prod_time_sec)
                    .ThenBy(id => Goods.Of(id).price)
                    .First();

                used.Remove(target.good_id);
                used.Add(replacement);
                target.good_id = replacement;
                target.qty_required = Goods.GOOD_BASE_QTY[replacement].min;
            }

            ConsiderBest(slots);
            return best;
        }

        /// <summary>
        /// Генерация заказа. Тот же движок, что у дрона: переменное число
        /// отсеков 3-5 и анти-повтор к прошлому рейсу (у шаттла рейс один).
        /// FTUE переопределяет генератор целиком (ТЗ шаттла 2.1): ровно три
        /// отсека, все easy, COVERAGE_MIN=1.0, MAX_DEFICIT_SLOTS=0.
        /// </summary>
        public static ShuttleTrip GenerateTrip(ShuttleGenContext ctx)
        {
            double trip_min = ctx.is_first_trip
                ? Economy.FTUE_FIRST_TRIP_TIMER_MIN
                : Economy.FlightTimerMin(ctx.level);
            List<ShuttleSlot> best = new List<ShuttleSlot>();

            for (int attempt = 0; attempt < Economy.GEN_MAX_ATTEMPTS; attempt++)
            {
                int count = ctx.is_first_trip
                    ? Economy.SLOT_COUNT_MIN
                    : Economy.SlotCountFor(ctx.level, ctx.rng());
                var pool = new List<string>(ctx.available_goods);
                var slots = new List<ShuttleSlot>();
                int deficit_used = 0;

                while (slots.Count < count && pool.Count > 0)
                {
                    int pick_at = Math.Min(pool.Count - 1, (int)Math.Floor(ctx.rng() * pool.Count));
                    string good_id = pool[pick_at];
                    pool.RemoveAt(pick_at);
                    int qty = Goods.SlotQuantity(good_id, Mechanic.shuttle, ctx.level, ctx.rng());
                    int have = Warehouse.AvailableOf(ctx.warehouse, good_id);
                    bool easy = IsEasy(good_id, qty, ctx.warehouse);

                    if (ctx.is_first_trip)
                    {
                        if (!easy)
                        {
                            if (have == 0)
                                continue;
                            qty = have;
                        }
                    }
                    else if (!easy)
                    {
                        bool locked_elsewhere = DeficitLock.IsDeficitLockedByOtherMechanic(
                            ctx.deficit_locks ?? DeficitLock.Create(),
                            good_id,
                            Mechanic.shuttle,
                            ctx.now
                        );
                        if (deficit_used >= Economy.MAX_DEFICIT_SLOTS || locked_elsewhere)
                        {
                            if (have == 0)
                                continue;
                            qty = have;
                        }
                        else
                        {
                            deficit_used += 1;
                            qty = Drone.ApplyPinch(have, qty);
                        }
                    }

                    slots.Add(
                        new ShuttleSlot
                        {
                            idx = slots.Count,
                            good_id = good_id,
                            qty_required = qty,
                            qty_filled = 0,
                            qty_purchased = 0,
                            filled_by = FilledBy.none,
                            reward = null,
                            collected = false,
                            floor_forced = false,
                        }
                    );
                }

                bool coverage_ok = EasyRatio(slots, ctx.warehouse) >= Economy.COVERAGE_MIN[Mechanic.shuttle];
                bool repeat_ok = RepeatRatio(slots, ctx.previous) <= Economy.REPEAT_CAP;
                if (coverage_ok && repeat_ok)
                {
                    best = slots;
                    break;
                }
                if (slots.Count > best.Count)
                    best = slots;
            }

            bool empty = best.Count == 0;
            bool covered = !empty && EasyRatio(best, ctx.warehouse) >= Economy.COVERAGE_MIN[Mechanic.shuttle];
            Economy.OrderGenerationDegradedReason reason = empty
                ? (ctx.available_goods.Count == 0
                    ? Economy.OrderGenerationDegradedReason.empty_pool
                    : Economy.OrderGenerationDegradedReason.max_attempts)
                : Economy.OrderGenerationDegradedReason.unresolvable_invariant;
            List<ShuttleSlot> slots_final = covered ? best : FallbackMinimalSlots(ctx, reason);

            // И-10: не применяется к is_first_trip — FTUE явный override обычного генератора.
            if (Economy.ACHIEVABILITY_CHECK[Mechanic.shuttle] && !ctx.is_first_trip)
            {
                double budget = Economy.FlightTimerMin(ctx.level) * Economy.ORDER_FEASIBILITY_DEADLINE_SHARE;
                if (TripProductionMinutes(slots_final, ctx.warehouse) > budget)
                {
                    slots_final = RebalanceForAchievability(
                        slots_final,
                        ctx.warehouse,
                        budget,
                        ctx.available_goods,
                        ctx.deficit_locks ?? DeficitLock.Create(),
                        ctx.now
                    );
                }
            }

            foreach (var s in slots_final)
            {
                if (!IsEasy(s.good_id, s.qty_required, ctx.warehouse))
                {
                    DeficitLock.RegisterDeficitLock(
                        ctx.deficit_locks ?? DeficitLock.Create(),
                        s.good_id,
                        Mechanic.shuttle,
                        "shuttle:trip",
                        ctx.now
                    );
                }
            }

            return new ShuttleTrip
            {
                state = ShuttleState.ORDER,
                slots = slots_final,
                trip_min = trip_min,
                departed_at = 0,
                arrives_at = 0,
                is_first_trip = ctx.is_first_trip,
                arrival_no = ctx.arrival_no,
            };
        }

        /// <summary>И-3: XP за отсек = базовый XP товара x K x количество. K шаттла = 8.</summary>
        public static int SlotXp(ShuttleSlot slot) =>
            Goods.Of(slot.good_id).base_xp * Economy.XP_MULTIPLIER_K[Mechanic.shuttle] * slot.qty_required;

        public static int TripXp(ShuttleTrip trip) => trip.slots.Sum(SlotXp);

        /// <summary>Цена докупки остатка отсека: И-4, rush-cost цепочки x 1.2 с округлением.</summary>
        public static int SlotBuyoutPrice(ShuttleSlot slot)
        {
            int short_ = SlotShort(slot);
            if (short_ == 0)
                return 0;
            return RushCost.BuyoutPrice(slot.good_id, short_);
        }

        public static bool AllSlotsLoaded(ShuttleTrip trip) =>
            trip.slots.Count > 0 && trip.slots.All(s => SlotShort(s) == 0);

        /// <summary>
        /// Отправка. Не действие игрока: вызывается из погрузки, когда закрылся
        /// последний отсек (п.2.3). Здесь же роллятся и фиксируются награды.
        /// </summary>
        private static ArrivalRoll Depart(
            ShuttleTrip trip,
            WarehouseState warehouse,
            double now,
            DropContext drop,
            DeficitLockState deficit_locks
        )
        {
            foreach (var slot in trip.slots)
                DeficitLock.ReleaseDeficitLock(deficit_locks, slot.good_id, Mechanic.shuttle, "shuttle:trip");

            foreach (var slot in trip.slots)
            {
                int from_stock = StockedIn(slot);
                if (from_stock > 0)
                    Warehouse.ShipReserved(warehouse, slot.good_id, from_stock);
            }

            ArrivalRoll roll = DropRoller.RollArrival(trip.slots.Count, drop);
            for (int i = 0; i < trip.slots.Count; i++)
            {
                trip.slots[i].reward = i < roll.modules.Count ? roll.modules[i] : (ModuleId?)null;
                trip.slots[i].floor_forced = roll.floor_forced_slot == i;
            }

            trip.state = ShuttleState.IN_TRANSIT;
            trip.departed_at = now;
            trip.arrives_at = now + trip.trip_min * 60;
            return roll;
        }

        /// <summary>Погрузка отсека со склада. Частичная допустима (п.6.2).</summary>
        public static LoadResult LoadSlot(
            ShuttleTrip trip,
            int idx,
            WarehouseState warehouse,
            double now,
            DropContext drop,
            DeficitLockState deficit_locks = null
        )
        {
            deficit_locks ??= DeficitLock.Create();
            var empty = new LoadResult { ok = false, loaded = 0, departed = false, drop_state = null };
            if (trip.state != ShuttleState.ORDER)
                return empty;
            if (idx < 0 || idx >= trip.slots.Count)
                return empty;

            ShuttleSlot slot = trip.slots[idx];
            int short_ = SlotShort(slot);
            if (short_ == 0)
                return empty;

            int take = Math.Min(short_, Warehouse.AvailableOf(warehouse, slot.good_id));
            if (take <= 0)
                return empty;
            if (!Warehouse.Reserve(warehouse, slot.good_id, take))
                return empty;

            slot.qty_filled += take;
            if (slot.filled_by == FilledBy.none)
                slot.filled_by = FilledBy.self;

            bool departed = AllSlotsLoaded(trip);
            ArrivalRoll drop_state = departed ? Depart(trip, warehouse, now, drop, deficit_locks) : null;
            return new LoadResult { ok = true, loaded = take, departed = departed, drop_state = drop_state };
        }

        /// <summary>Докупка остатка за изотопы. И-12: товар зачисляется прямо в отсек, минуя склад.</summary>
        public static LoadResult BuyoutSlot(
            ShuttleTrip trip,
            int idx,
            WarehouseState warehouse,
            double now,
            DropContext drop,
            DeficitLockState deficit_locks = null
        )
        {
            deficit_locks ??= DeficitLock.Create();
            var empty = new LoadResult { ok = false, loaded = 0, departed = false, drop_state = null, price = 0 };
            if (trip.state != ShuttleState.ORDER)
                return empty;
            if (idx < 0 || idx >= trip.slots.Count)
                return empty;

            ShuttleSlot slot = trip.slots[idx];
            int short_ = SlotShort(slot);
            if (short_ == 0)
                return empty;

            int price = SlotBuyoutPrice(slot);
            slot.qty_filled += short_;
            slot.qty_purchased += short_;
            slot.filled_by = FilledBy.purchase;

            bool departed = AllSlotsLoaded(trip);
            ArrivalRoll drop_state = departed ? Depart(trip, warehouse, now, drop, deficit_locks) : null;
            return new LoadResult { ok = true, loaded = short_, departed = departed, drop_state = drop_state, price = price };
        }

        /// <summary>И-6: цена скипа рейса. Растет с числом отсеков, падает с остатком таймера.</summary>
        public static int SkipPrice(ShuttleTrip trip, double now)
        {
            if (trip.state != ShuttleState.IN_TRANSIT)
                return 0;
            double remaining_min = Math.Max(0, (trip.arrives_at - now) / 60);
            return Economy.ShuttleSkipPrice(remaining_min, trip.trip_min, trip.slots.Count);
        }

        /// <summary>Перевод рейса в прибытие по времени. Ленивый, как и остальные таймеры.</summary>
        public static ShuttleTrip RefreshTrip(ShuttleTrip trip, double now)
        {
            if (trip.state == ShuttleState.IN_TRANSIT && now >= trip.arrives_at)
                trip.state = ShuttleState.ARRIVED;
            return trip;
        }

        /// <summary>Скип: рейс считается прибывшим немедленно. Цену списывает вызывающий.</summary>
        public static bool SkipFlight(ShuttleTrip trip, double now)
        {
            if (trip.state != ShuttleState.IN_TRANSIT)
                return false;
            trip.arrives_at = now;
            trip.state = ShuttleState.ARRIVED;
            return true;
        }

        /// <summary>Вскрытие одного контейнера. Возвращает модуль или null, если брать нечего.</summary>
        public static ModuleId? CollectContainer(ShuttleTrip trip, int idx)
        {
            if (trip.state != ShuttleState.ARRIVED)
                return null;
            if (idx < 0 || idx >= trip.slots.Count)
                return null;
            ShuttleSlot slot = trip.slots[idx];
            if (slot.collected || slot.reward == null)
                return null;
            slot.collected = true;
            return slot.reward;
        }

        public static bool AllCollected(ShuttleTrip trip) => trip.slots.All(s => s.collected);

    }
}
