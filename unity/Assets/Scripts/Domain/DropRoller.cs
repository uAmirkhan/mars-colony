using System;
using System.Collections.Generic;
using System.Linq;
using MarsColony.Domain.Config;

namespace MarsColony.Domain
{
    /// <summary>
    /// Дроп-роллер строй-модулей. Общий движок ([[tz-common-systems-mars]]),
    /// конфиг шаттла — [[tz-shuttle-mars]] раздел 5, инварианты И-7 и И-11.
    /// Перенесено из `mars-colony/src/domain/droproller.ts`.
    ///
    /// Роллер вызывается по разу НА ОТСЕК: 1 отсек = 1 контейнер = 1 модуль.
    /// Три поправки к чистому рандому: pity (И-7), анти-стокпайл (И-7), floor
    /// guarantee (И-11) — все три заявлены игроку, а не спрятаны.
    /// </summary>
    public sealed class ModuleCounts
    {
        public Dictionary<ModuleId, double> values = new Dictionary<ModuleId, double>();

        public double Get(ModuleId id) => values.TryGetValue(id, out var v) ? v : 0;

        public void Set(ModuleId id, double v) => values[id] = v;
    }

    /// <summary>Скользящее среднее склада модулей за WAREHOUSE_AVG_WINDOW_SEC (канон `warehouse_avg_24h`).</summary>
    public sealed class WarehouseAvgState
    {
        public ModuleCounts avg;
        public double updated_at;
    }

    /// <summary>
    /// Одна активная (доступная к постройке) стройка со своим независимым
    /// состоянием floor guarantee (канон 2.6 п.2 и 2.8).
    /// </summary>
    public sealed class ConstructionNeed
    {
        public string construction_id;
        public ModuleCounts deficit;
        public int arrivals_without_needed;
        public int last_floor_arrival;
    }

    public sealed class DropContext
    {
        /// <summary>Счетчик на пару (игрок, модуль): сколько прибытий подряд не выпадал (И-7).</summary>
        public ModuleCounts pity;

        /// <summary>Что лежит на складе модулей (мгновенный остаток).</summary>
        public ModuleCounts stock;

        /// <summary>Сколько модулей просят рецепты активных и доступных строек (валовая величина).</summary>
        public ModuleCounts need;

        /// <summary>Скользящее среднее склада за 24ч (канон `warehouse_avg_24h`).</summary>
        public ModuleCounts warehouse_avg_24h;

        /// <summary>Открыт ли гейтовый тир (буровая, реактор). В MVP обычно false.</summary>
        public bool gated_open;

        /// <summary>Номер прибытия игрока, считая с 1.</summary>
        public int arrival_no;

        /// <summary>Активные (доступные к постройке) стройки со своими независимыми окнами И-11.</summary>
        public List<ConstructionNeed> constructions = new List<ConstructionNeed>();

        public Func<double> rng;
    }

    public sealed class ArrivalRoll
    {
        /// <summary>По модулю на отсек, в порядке отсеков.</summary>
        public List<ModuleId> modules = new List<ModuleId>();

        /// <summary>Индекс отсека, который переписала гарантия. null — гарантия не срабатывала.</summary>
        public int? floor_forced_slot;
        public ModuleCounts next_pity;
        public List<ConstructionNeed> next_constructions = new List<ConstructionNeed>();
    }

    public static class DropRoller
    {
        /// <summary>Новый счетчик среднего: на старте среднее равно текущему остатку.</summary>
        public static WarehouseAvgState CreateWarehouseAvg(ModuleCounts stock, double now)
        {
            var avg = new ModuleCounts();
            foreach (var id in Modules.ALL_MODULE_IDS)
                avg.Set(id, stock.Get(id));
            return new WarehouseAvgState { avg = avg, updated_at = now };
        }

        /// <summary>
        /// Продвигает среднее вперед во времени методом экспоненциального
        /// сглаживания (EMA): вес свежего замера растет пропорционально
        /// прошедшему времени относительно окна.
        /// </summary>
        public static WarehouseAvgState AdvanceWarehouseAvg(
            WarehouseAvgState state,
            ModuleCounts stock,
            double now
        )
        {
            double elapsed = now - state.updated_at;
            if (elapsed <= 0)
                return state;

            double alpha = Math.Min(1, elapsed / Economy.WAREHOUSE_AVG_WINDOW_SEC);
            var avg = new ModuleCounts();
            foreach (var id in Modules.ALL_MODULE_IDS)
            {
                double prev = state.avg.Get(id);
                double cur = stock.Get(id);
                avg.Set(id, prev + (cur - prev) * alpha);
            }
            return new WarehouseAvgState { avg = avg, updated_at = now };
        }

        /// <summary>Базовый вес модуля ВНУТРИ своего тира: тир уже выбран, делим его вес поровну.</summary>
        private static double BaseItemWeight(ModuleId module_id)
        {
            ModuleTier tier = Modules.MODULES[module_id].tier;
            var pool = Modules.MODULE_TIER_POOL[tier];
            if (pool.Length == 0)
                return 0;
            return 1.0 / pool.Length;
        }

        /// <summary>
        /// Вес модуля с поправками И-7, ВНУТРИ своего тира (тир весит отдельно,
        /// см. PickTier).
        /// </summary>
        public static double ModuleWeight(ModuleId module_id, DropContext ctx)
        {
            if (Modules.MODULES[module_id].tier == ModuleTier.gated && !ctx.gated_open)
                return 0;

            double weight = BaseItemWeight(module_id);
            double needed = ctx.need.Get(module_id);

            // Pity — только для того, что реально требуется хотя бы одной стройке.
            if (needed > 0 && ctx.pity.Get(module_id) >= Economy.PITY_K)
                weight *= Economy.PITY_MULTIPLIER;

            // Анти-стокпайл: запас > потребность x2 (скользящее среднее за 24ч) → вес /2.
            double avg = ctx.warehouse_avg_24h.Get(module_id);
            if (needed > 0 && avg > needed * Economy.ANTISTOCKPILE_THRESHOLD)
                weight *= Economy.ANTISTOCKPILE_FACTOR;

            return weight;
        }

        /// <summary>Открытые тиры и их веса — гейтовый тир весит ноль, пока гейт закрыт.</summary>
        private static Dictionary<ModuleTier, double> TierWeights(DropContext ctx) =>
            new Dictionary<ModuleTier, double>
            {
                { ModuleTier.basic, Economy.TIER_WEIGHTS[ModuleTier.basic] },
                { ModuleTier.rare, Economy.TIER_WEIGHTS[ModuleTier.rare] },
                { ModuleTier.gated, ctx.gated_open ? Economy.TIER_WEIGHTS[ModuleTier.gated] : 0 },
            };

        private static readonly ModuleTier[] TIER_ORDER =
        {
            ModuleTier.basic,
            ModuleTier.rare,
            ModuleTier.gated,
        };

        /// <summary>
        /// Этап 1 двухэтапного выбора (канон 2.3): какой тир выпадает. Веса
        /// тиров фиксированы каркасом и не зависят от pity/анти-стокпайла.
        /// </summary>
        private static ModuleTier PickTier(DropContext ctx)
        {
            var weights = TierWeights(ctx);
            double total = TIER_ORDER.Sum(t => weights[t]);
            if (total <= 0)
                return ModuleTier.basic;

            double roll = ctx.rng() * total;
            ModuleTier last_eligible = ModuleTier.basic;
            foreach (var tier in TIER_ORDER)
            {
                double w = weights[tier];
                if (w <= 0)
                    continue;
                last_eligible = tier;
                roll -= w;
                if (roll <= 0)
                    return last_eligible;
            }
            return last_eligible;
        }

        /// <summary>Этап 2: какой предмет внутри уже выбранного тира.</summary>
        private static ModuleId PickWeighted(DropContext ctx)
        {
            ModuleTier tier = PickTier(ctx);
            var pool = Modules.MODULE_TIER_POOL[tier];
            var weights = pool.Select(id => ModuleWeight(id, ctx)).ToArray();
            double total = weights.Sum();

            if (total <= 0)
                return Modules.MODULE_TIER_POOL[ModuleTier.basic].Length > 0
                    ? Modules.MODULE_TIER_POOL[ModuleTier.basic][0]
                    : ModuleId.panel;

            double roll = ctx.rng() * total;
            ModuleId last_eligible = pool.Length > 0 ? pool[0] : ModuleId.panel;
            for (int i = 0; i < pool.Length; i++)
            {
                double weight = weights[i];
                if (weight <= 0)
                    continue;
                last_eligible = pool[i];
                roll -= weight;
                if (roll <= 0)
                    return last_eligible;
            }
            return last_eligible;
        }

        /// <summary>Покрывает ли склад модулей всю потребность активных строек.</summary>
        public static bool StockCoversNeed(ModuleCounts stock, ModuleCounts need)
        {
            foreach (var id in Modules.ALL_MODULE_IDS)
                if (need.Get(id) > stock.Get(id))
                    return false;
            return true;
        }

        /// <summary>Разрешено ли гарантии сработать на этом прибытии ДЛЯ ОДНОЙ КОНКРЕТНОЙ стройки.</summary>
        public static bool FloorGuaranteeAllowedFor(
            ConstructionNeed construction,
            DropContext ctx,
            IList<ModuleId> rolled
        )
        {
            var needed_ids = Modules.ALL_MODULE_IDS.Where(id =>
                construction.deficit.Get(id) > 0
                && Economy.FLOOR_GUARANTEE_ALLOWED_TIERS.Contains(Modules.MODULES[id].tier)
            ).ToArray();

            // Дефицита из разрешенных тиров нет — либо стройка уже покрыта
            // складом, либо не хватает только гейтового. Чинить нечего.
            if (needed_ids.Length == 0)
                return false;

            // Прибытие уже дало нужное этой стройке — чинить нечего.
            if (rolled.Any(id => construction.deficit.Get(id) > 0))
                return false;

            // Front-loaded удача: первые прибытия форсируют гарантию поверх И-11.
            if (ctx.arrival_no <= Economy.FRONT_LOADED_LUCK_ARRIVALS)
                return true;

            // Окно И-11: два прибытия подряд без нужного модуля, третье форсирует.
            if (construction.arrivals_without_needed < Economy.FLOOR_GUARANTEE_WINDOW - 1)
                return false;

            // Анти-эксплойт «держи стройку голодной»: не чаще раза на пять прибытий.
            if (
                construction.last_floor_arrival > 0
                && ctx.arrival_no - construction.last_floor_arrival < Economy.FLOOR_GUARANTEE_MIN_GAP
            )
                return false;

            return true;
        }

        /// <summary>
        /// Прибытие рейса: по модулю на каждый отсек, плюс возможная форс-выдача.
        /// Гарантия переписывает результат одного уже сгенерированного отсека,
        /// а не добавляет бонусный.
        /// </summary>
        public static ArrivalRoll RollArrival(int slot_count, DropContext ctx)
        {
            var modules = new List<ModuleId>();
            for (int i = 0; i < slot_count; i++)
                modules.Add(PickWeighted(ctx));

            int? floor_forced_slot = null;
            string forced_construction_id = null;

            if (modules.Count > 0)
            {
                foreach (var construction in ctx.constructions)
                {
                    if (!FloorGuaranteeAllowedFor(construction, ctx, modules))
                        continue;

                    var candidates = Modules.ALL_MODULE_IDS.Where(id =>
                        construction.deficit.Get(id) > 0
                        && Economy.FLOOR_GUARANTEE_ALLOWED_TIERS.Contains(Modules.MODULES[id].tier)
                    ).ToArray();

                    // Из нужного выдаем самое дефицитное.
                    ModuleId forced = candidates[0];
                    foreach (var id in candidates)
                        if (construction.deficit.Get(id) > construction.deficit.Get(forced))
                            forced = id;

                    floor_forced_slot = modules.Count - 1;
                    modules[floor_forced_slot.Value] = forced;
                    forced_construction_id = construction.construction_id;
                    break; // одно прибытие форсит не больше одной стройки
                }
            }

            // Канон 2.3: ровно три исхода на модуль — выпал -> pity=0; нужен, не
            // выпал -> pity+=1; не нужен -> не трогаем (пауза, а не сброс).
            var dropped = new HashSet<ModuleId>(modules);
            var next_pity = new ModuleCounts();
            foreach (var id in Modules.ALL_MODULE_IDS)
            {
                double before = ctx.pity.Get(id);
                if (dropped.Contains(id))
                    next_pity.Set(id, 0);
                else
                    next_pity.Set(id, ctx.need.Get(id) > 0 ? before + 1 : before);
            }

            var next_constructions = ctx.constructions
                .Select(construction =>
                {
                    bool gave_needed = modules.Any(id => construction.deficit.Get(id) > 0);
                    return new ConstructionNeed
                    {
                        construction_id = construction.construction_id,
                        deficit = construction.deficit,
                        arrivals_without_needed = gave_needed
                            ? 0
                            : construction.arrivals_without_needed + 1,
                        last_floor_arrival =
                            construction.construction_id == forced_construction_id
                                ? ctx.arrival_no
                                : construction.last_floor_arrival,
                    };
                })
                .ToList();

            return new ArrivalRoll
            {
                modules = modules,
                floor_forced_slot = floor_forced_slot,
                next_pity = next_pity,
                next_constructions = next_constructions,
            };
        }
    }
}
