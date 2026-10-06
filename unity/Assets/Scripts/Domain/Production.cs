using System.Collections.Generic;
using MarsColony.Domain.Config;

namespace MarsColony.Domain
{
    /// <summary>
    /// Производство: грядки и очередь фабрики. Источник истины —
    /// [[tz-production-mars]] разделы 2.1, 2.2, 3.2, 3.4; перенесено из
    /// `mars-colony/src/domain/production.ts`.
    ///
    /// Ночь 3 (`SPEC-NOCH-3-igra.md`) дописала очередь фабрики (enqueue,
    /// tryStart, onWarehouseStockIncreased, collectFactory) и продажу — до этого
    /// был перенесен только цикл грядки.
    ///
    /// Два правила, которые ломаются первыми при небрежной реализации:
    ///  1. Сбор при переполнении склада отклоняется ЦЕЛИКОМ. Слот остается
    ///     READY, ни товар, ни XP не начисляются. Урожай не портится и не пропадает.
    ///  2. Слот в QUEUED никогда не резервирует входы заранее. Иначе он держит
    ///     товар недоступным для заказов, пока сам ничего не производит.
    /// </summary>
    public enum FieldState
    {
        EMPTY,
        GROWING,
        READY,
    }

    public sealed class FieldSlot
    {
        public int idx;
        public FieldState state;
        public string good_id;

        /// <summary>Момент готовности в секундах той же шкалы, что и ctx.now.</summary>
        public double ends_at;
    }

    public enum FactorySlotState
    {
        EMPTY,
        QUEUED,
        PRODUCING,
        READY,
    }

    public sealed class FactorySlot
    {
        public int idx;
        public BuildingType building_type;
        public FactorySlotState state;
        public string good_id;
        public double queued_at;
        public double ends_at;
    }

    public enum ActionReason
    {
        none,
        slot_busy,
        not_ready,
        warehouse_full,
        insufficient_balance,
        locked,
        no_inputs,
    }

    public sealed class ActionResult
    {
        public bool ok;

        /// <summary>Причина отказа — для UI и для тестов, не для логов.</summary>
        public ActionReason reason;
        public int credits_delta;
        public int xp_gained;
        public bool softlock_rescued;

        public static ActionResult Fail(ActionReason reason) =>
            new ActionResult { ok = false, reason = reason };
    }

    public sealed class ProductionContext
    {
        public double now;
        public WarehouseState warehouse;
        public int credits;
        public int level;

        /// <summary>Отклонения от конфига. Null — игра работает на умолчаниях (DEFAULT_TUNING).</summary>
        public Tuning tuning;
    }

    public static class Production
    {
        /// <summary>Отклонение от конфига или дефолт — тот же прием, что и в production.ts.</summary>
        private static Tuning TuningOf(ProductionContext ctx) => ctx.tuning ?? TuningDefaults.Default();

        public static FieldSlot CreateField(int idx) =>
            new FieldSlot
            {
                idx = idx,
                state = FieldState.EMPTY,
                good_id = null,
                ends_at = 0,
            };

        /// <summary>READY вычисляется лениво: состояние не хранится, а выводится из времени.</summary>
        public static FieldSlot RefreshField(FieldSlot field, double now)
        {
            if (field.state == FieldState.GROWING && now >= field.ends_at)
                field.state = FieldState.READY;
            return field;
        }

        /// <summary>Самый дешевый посев среди разблокированных культур — нужен для И-15.</summary>
        public static int CheapestPlantingCost(int level)
        {
            int cheapest = 0;
            bool found = false;
            foreach (var good in Goods.GOODS.Values)
            {
                if (good.kind != GoodKind.crop || good.unlock_level > level)
                    continue;
                int cost = Economy.PlantingCost(good.price);
                if (!found || cost < cheapest)
                {
                    cheapest = cost;
                    found = true;
                }
            }
            return found ? cheapest : 0;
        }

        /// <summary>
        /// Посадка. Стоит кредиты (кредитный сток №1 каркаса), кроме случая И-15,
        /// когда игрок иначе оказался бы в тупике без единого доступного действия.
        /// </summary>
        public static ActionResult Plant(
            FieldSlot field,
            string good_id,
            ProductionContext ctx,
            IList<FieldSlot> all_fields
        )
        {
            if (field.state != FieldState.EMPTY)
                return ActionResult.Fail(ActionReason.slot_busy);

            Good good = Goods.Of(good_id);
            if (good.kind != GoodKind.crop || good.unlock_level > ctx.level)
                return ActionResult.Fail(ActionReason.locked);

            int cost = Economy.PlantingCost(good.price);
            int charged = cost;
            bool rescued = false;

            if (ctx.credits < cost)
            {
                int cheapest = CheapestPlantingCost(ctx.level);
                bool has_growing = false;
                foreach (var f in all_fields)
                    if (f.state != FieldState.EMPTY)
                        has_growing = true;

                bool softlocked =
                    cost == cheapest
                    && Economy.IsPlantingSoftlocked(
                        ctx.credits,
                        cheapest,
                        has_growing,
                        Warehouse.OccupiedGoods(ctx.warehouse).Count > 0
                    );
                if (!softlocked)
                    return ActionResult.Fail(ActionReason.insufficient_balance);
                charged = 0;
                rescued = true;
            }

            field.state = FieldState.GROWING;
            field.good_id = good_id;
            // time_scale — добавка ночи 3, переключатель ускорения x10 для
            // проверки: единственное место, где считается ends_at грядки.
            field.ends_at = ctx.now + good.prod_time_sec * TuningOf(ctx).time_scale;

            return new ActionResult
            {
                ok = true,
                credits_delta = charged > 0 ? -charged : 0,
                softlock_rescued = rescued,
            };
        }

        /// <summary>
        /// Сбор урожая. При переполнении склада отклоняется целиком: слот
        /// остается READY, XP не начисляется.
        /// </summary>
        public static ActionResult CollectField(FieldSlot field, ProductionContext ctx)
        {
            RefreshField(field, ctx.now);
            if (field.state != FieldState.READY || field.good_id == null)
                return ActionResult.Fail(ActionReason.not_ready);

            Good good = Goods.Of(field.good_id);

            // Выход культуры за цикл, не единица: цифры в HARVEST_QTY, раздел 7 ТЗ.
            // Одно и то же число обязано идти и в проверку места, и в депозит, и в XP.
            int yield_qty = Goods.HarvestQty(field.good_id);

            if (!Warehouse.CanAccept(ctx.warehouse, yield_qty))
                return ActionResult.Fail(ActionReason.warehouse_full);

            Warehouse.Deposit(ctx.warehouse, field.good_id, yield_qty);
            int xp = good.base_xp * Economy.PRODUCTION_XP_K * yield_qty;

            field.state = FieldState.EMPTY;
            field.good_id = null;
            field.ends_at = 0;

            return new ActionResult { ok = true, xp_gained = xp };
        }

        public static FactorySlot CreateFactorySlot(int idx, BuildingType building_type) =>
            new FactorySlot
            {
                idx = idx,
                building_type = building_type,
                state = FactorySlotState.EMPTY,
                good_id = null,
                queued_at = 0,
                ends_at = 0,
            };

        /// <summary>READY вычисляется лениво: состояние не хранится, а выводится из времени.</summary>
        public static FactorySlot RefreshFactorySlot(FactorySlot slot, double now)
        {
            if (slot.state == FactorySlotState.PRODUCING && now >= slot.ends_at)
                slot.state = FactorySlotState.READY;
            return slot;
        }

        /// <summary>Атомарный старт: входы списываются в момент старта, не при постановке.</summary>
        private static bool TryStart(FactorySlot slot, ProductionContext ctx)
        {
            if (slot.good_id == null)
                return false;
            Good good = Goods.Of(slot.good_id);

            foreach (var inp in good.inputs)
                if (Warehouse.AvailableOf(ctx.warehouse, inp.good_id) < inp.qty)
                    return false;

            foreach (var inp in good.inputs)
                Warehouse.Consume(ctx.warehouse, inp.good_id, inp.qty);

            slot.state = FactorySlotState.PRODUCING;
            // time_scale — то же место, что и у грядки: одна точка на весь домен.
            slot.ends_at = ctx.now + good.prod_time_sec * TuningOf(ctx).time_scale;
            return true;
        }

        /// <summary>
        /// Постановка рецепта в слот фабрики. Если входов не хватает — слот
        /// уходит в QUEUED и ждет пополнения склада, НЕ резервируя ничего заранее.
        /// </summary>
        public static ActionResult Enqueue(FactorySlot slot, string good_id, ProductionContext ctx)
        {
            if (slot.state != FactorySlotState.EMPTY)
                return ActionResult.Fail(ActionReason.slot_busy);

            Good good = Goods.Of(good_id);
            if (
                good.kind != GoodKind.factory
                || good.required_building != slot.building_type
                || good.unlock_level > ctx.level
            )
                return ActionResult.Fail(ActionReason.locked);

            slot.good_id = good_id;
            slot.queued_at = ctx.now;

            if (TryStart(slot, ctx))
                return new ActionResult { ok = true };

            slot.state = FactorySlotState.QUEUED;
            return new ActionResult { ok = true, reason = ActionReason.no_inputs };
        }

        /// <summary>
        /// Событие «остаток товара на складе вырос». Обходит ВСЕХ кандидатов до
        /// конца списка: провал одного слота ничего не говорит о шансах остальных
        /// (continue, а не break — иначе слот, чей товар физически есть, мог бы
        /// голодать бессрочно).
        /// </summary>
        public static int OnWarehouseStockIncreased(IList<FactorySlot> slots, ProductionContext ctx)
        {
            int started = 0;
            var waiting = new List<FactorySlot>();
            foreach (var s in slots)
                if (s.state == FactorySlotState.QUEUED)
                    waiting.Add(s);
            waiting.Sort((a, b) => a.queued_at.CompareTo(b.queued_at));

            foreach (var slot in waiting)
                if (TryStart(slot, ctx))
                    started += 1;
            return started;
        }

        /// <summary>Сбор с фабрики. Та же блокировка переполнением, что и у грядки.</summary>
        public static ActionResult CollectFactory(FactorySlot slot, ProductionContext ctx)
        {
            RefreshFactorySlot(slot, ctx.now);
            if (slot.state != FactorySlotState.READY || slot.good_id == null)
                return ActionResult.Fail(ActionReason.not_ready);

            Good good = Goods.Of(slot.good_id);
            if (!Warehouse.CanAccept(ctx.warehouse, Goods.FACTORY_OUTPUT_QTY))
                return ActionResult.Fail(ActionReason.warehouse_full);

            Warehouse.Deposit(ctx.warehouse, slot.good_id, Goods.FACTORY_OUTPUT_QTY);
            int xp = good.base_xp * Economy.PRODUCTION_XP_K * Goods.FACTORY_OUTPUT_QTY;

            slot.state = FactorySlotState.EMPTY;
            slot.good_id = null;
            slot.ends_at = 0;
            slot.queued_at = 0;

            return new ActionResult { ok = true, xp_gained = xp };
        }

        /// <summary>Продажа со склада по рыночной цене. Продается только available.</summary>
        public static ActionResult Sell(string good_id, int qty, ProductionContext ctx)
        {
            if (!Warehouse.Consume(ctx.warehouse, good_id, qty))
                return ActionResult.Fail(ActionReason.no_inputs);
            int sum = (int)System.Math.Floor(Goods.Of(good_id).price * TuningOf(ctx).sell_price_ratio * qty);
            return new ActionResult { ok = true, credits_delta = sum };
        }
    }
}
