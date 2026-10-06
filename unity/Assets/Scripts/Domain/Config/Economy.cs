using System;
using System.Collections.Generic;

namespace MarsColony.Domain.Config
{
    /// <summary>
    /// Экономические константы. Источник истины — [[mars-colony-frame]], раздел 5;
    /// перенесено один в один из `mars-colony/src/domain/config/economy.ts`.
    ///
    /// ЗЕРКАЛО, а не второй источник истины: см. шапку `Goods.cs`.
    ///
    /// Дописано ночью 3 (спека `SPEC-NOCH-3-igra.md`): слоты, ускорения (И-5,
    /// И-6), рейс шаттла, pity/анти-стокпайл (И-7), floor guarantee (И-11),
    /// анти-фрустрация генератора (И-8, И-10), изоляция дефицита (И-13),
    /// дрон-доска и премии, магазин изотопов (И-9), цены зданий.
    /// </summary>
    public static class Economy
    {
        /// <summary>И-14: сбор урожая и готового товара дает XP напрямую, K = 1.</summary>
        public const int PRODUCTION_XP_K = 1;

        // --- И-3: XP транспорта ---------------------------------------------

        /// <summary>Коэффициент XP механики. Апгрейдами НЕ меняется. Значения по И-3: дрон 2, шаттл 8, лайнер 8.</summary>
        public static readonly Dictionary<Mechanic, int> XP_MULTIPLIER_K = new Dictionary<
            Mechanic,
            int
        >
        { { Mechanic.drone, 2 }, { Mechanic.shuttle, 8 }, { Mechanic.liner, 8 } };

        /// <summary>Кэп лайнера: доля XP_to_next(level) на один контейнер и на весь рейс.</summary>
        public const double LINER_XP_CAP_PER_CONTAINER = 0.05;
        public const double LINER_XP_CAP_PER_TRIP = 0.4;

        // --- И-5: ставки ускорения (изотопы за минуту остатка) --------------

        public static readonly Dictionary<GoodKind, int> SPEEDUP_RATE_ISOTOPES_PER_MIN =
            new Dictionary<GoodKind, int> { { GoodKind.crop, 5 }, { GoodKind.factory, 6 } };

        /// <summary>
        /// Пол цены ускорения короткого остатка. Ключи — строки "crop"/"factory"/
        /// "shuttle": та же смесь ключей, что в economy.ts (`GoodKind | 'shuttle'`),
        /// C# enum их не покрывает одним типом, поэтому таблица строковая.
        /// </summary>
        public static readonly Dictionary<string, int> SPEEDUP_FLOOR_ISOTOPES = new Dictionary<
            string,
            int
        > { { "crop", 10 }, { "factory", 10 }, { "shuttle", 15 } };

        /// <summary>Ниже этого остатка ускорение бесплатно (ТЗ производства, AC7).</summary>
        public const int SPEEDUP_FREE_THRESHOLD_SEC = 30;

        /// <summary>И-5: цена ускорения производства по ОСТАВШЕМУСЯ времени, не по полному циклу.</summary>
        public static int ProductionSpeedupCost(double remaining_sec, GoodKind kind)
        {
            if (remaining_sec <= 0)
                return 0;
            if (remaining_sec <= SPEEDUP_FREE_THRESHOLD_SEC)
                return 0;
            int minutes = (int)Math.Floor(remaining_sec / 60);
            return Math.Max(SPEEDUP_FLOOR_ISOTOPES[kind.ToString()], minutes * SPEEDUP_RATE_ISOTOPES_PER_MIN[kind]);
        }

        // --- И-6: цена скипа шаттла ------------------------------------------

        public const int SPEEDUP_TARIFF_ISOTOPES_PER_SLOT_SHUTTLE = 70;

        /// <summary>И-6: skip = max(15, round((remaining_min / trip_min) x 70 x slot_count)).</summary>
        public static int ShuttleSkipPrice(double remaining_min, double trip_min, int slot_count)
        {
            double raw = (remaining_min / trip_min) * SPEEDUP_TARIFF_ISOTOPES_PER_SLOT_SHUTTLE * slot_count;
            return Math.Max(SPEEDUP_FLOOR_ISOTOPES["shuttle"], Numeric.RoundHalfUpToInt(raw));
        }

        // --- Шаттл: отсеки, рейс, кулдаун ------------------------------------

        /// <summary>Только у шаттла число слотов переменное: 3-5 (ТЗ шаттла 4).</summary>
        public const int SLOT_COUNT_MIN = 3;
        public const int SLOT_COUNT_MAX = 5;

        public sealed class LevelWeightsRow
        {
            public int from_level;
            public Dictionary<int, double> weights;
        }

        /// <summary>Распределение числа отсеков по уровневым брекетам (ТЗ шаттла 4).</summary>
        public static readonly LevelWeightsRow[] SLOT_COUNT_WEIGHTS =
        {
            new LevelWeightsRow { from_level = 15, weights = new Dictionary<int, double> { { 3, 0.15 }, { 4, 0.4 }, { 5, 0.45 } } },
            new LevelWeightsRow { from_level = 9, weights = new Dictionary<int, double> { { 3, 0.3 }, { 4, 0.45 }, { 5, 0.25 } } },
            new LevelWeightsRow { from_level = 5, weights = new Dictionary<int, double> { { 3, 0.6 }, { 4, 0.3 }, { 5, 0.1 } } },
        };

        /// <summary>Предохранитель цикла подбора товаров генератором (ТЗ шаттла 4).</summary>
        public const int GEN_MAX_ATTEMPTS = 40;

        public sealed class LevelMinutesRow
        {
            public int from_level;
            public double minutes;
        }

        /// <summary>Длина рейса по брекетам уровня (ТЗ шаттла 4).</summary>
        public static readonly LevelMinutesRow[] FLIGHT_TIMER_MIN =
        {
            new LevelMinutesRow { from_level = 15, minutes = 90 },
            new LevelMinutesRow { from_level = 9, minutes = 75 },
            new LevelMinutesRow { from_level = 5, minutes = 60 },
        };

        public static double FlightTimerMin(int level)
        {
            foreach (var row in FLIGHT_TIMER_MIN)
                if (level >= row.from_level)
                    return row.minutes;
            return 60;
        }

        public static int SlotCountFor(int level, double roll)
        {
            Dictionary<int, double> weights = null;
            foreach (var row in SLOT_COUNT_WEIGHTS)
                if (level >= row.from_level)
                {
                    weights = row.weights;
                    break;
                }
            if (weights == null)
                weights = new Dictionary<int, double> { { SLOT_COUNT_MIN, 1 } };

            double acc = 0;
            foreach (var pair in weights)
            {
                acc += pair.Value;
                if (roll <= acc)
                    return pair.Key;
            }
            return SLOT_COUNT_MIN;
        }

        /// <summary>Пауза после сбора всех контейнеров, защита от чейн-фарма (ТЗ шаттла 4).</summary>
        public const int COLLECT_COOLDOWN_MIN = 5;

        /// <summary>FTUE: первые прибытия форсируют гарантию на 100% сверх И-11.</summary>
        public const int FRONT_LOADED_LUCK_ARRIVALS = 3;

        /// <summary>FTUE: первый рейс короче брекета, чтобы весь цикл прошел в одну сессию.</summary>
        public const double FTUE_FIRST_TRIP_TIMER_MIN = 12;

        /// <summary>Порог «заказ брошен» для health-метрики (ТЗ шаттла 4).</summary>
        public const int IDLE_ORDER_ABANDON_ALERT_H = 24;

        /// <summary>Пороги табло рейса (ТЗ шаттла 6.1/6.3).</summary>
        public const int SKIP_HIDE_BELOW_SEC = 60;
        public const int SKIP_ARRIVING_SOON_SEC = 5 * 60;

        // --- Стройка: ускорение ----------------------------------------------

        /// <summary>Ставка ускорения стройки. Экстраполяция из И-5 (ТЗ производства).</summary>
        public const int CONSTRUCTION_SPEEDUP_RATE_ISO_PER_MIN = 3;
        public const int SPEEDUP_FLOOR_ISO_CONSTRUCTION = 20;
        public const int CONSTRUCTION_ACTIVE_LINES_BASE = 1;

        public static int ConstructionSpeedupCost(double remaining_sec)
        {
            if (remaining_sec <= 0)
                return 0;
            if (remaining_sec <= SPEEDUP_FREE_THRESHOLD_SEC)
                return 0;
            int minutes = (int)Math.Floor(remaining_sec / 60);
            return Math.Max(SPEEDUP_FLOOR_ISO_CONSTRUCTION, minutes * CONSTRUCTION_SPEEDUP_RATE_ISO_PER_MIN);
        }

        // --- Строй-модули: дроп, докупка, EV ----------------------------------

        /// <summary>Цена докупки модуля за изотопы (И-12: минуя склад).</summary>
        public static readonly Dictionary<ModuleTier, int> MODULE_PRICE_ISOTOPES = new Dictionary<
            ModuleTier,
            int
        > { { ModuleTier.basic, 150 }, { ModuleTier.rare, 200 }, { ModuleTier.gated, 400 } };

        /// <summary>Веса дропа по тирам (каркас, раздел 4). Сумма = 1.</summary>
        public static readonly Dictionary<ModuleTier, double> TIER_WEIGHTS = new Dictionary<
            ModuleTier,
            double
        > { { ModuleTier.basic, 0.62 }, { ModuleTier.rare, 0.33 }, { ModuleTier.gated, 0.05 } };

        /// <summary>Shadow-ценность одного отсека шаттла в изотопах.</summary>
        public static double ShuttleSlotExpectedValue() =>
            TIER_WEIGHTS[ModuleTier.basic] * MODULE_PRICE_ISOTOPES[ModuleTier.basic]
            + TIER_WEIGHTS[ModuleTier.rare] * MODULE_PRICE_ISOTOPES[ModuleTier.rare]
            + TIER_WEIGHTS[ModuleTier.gated] * MODULE_PRICE_ISOTOPES[ModuleTier.gated];

        // --- И-7: pity и анти-стокпайл ----------------------------------------

        /// <summary>Счетчик на пару (игрок, модуль), не на стройку целиком.</summary>
        public const int PITY_K = 4;
        public const int PITY_MULTIPLIER = 2;
        /// <summary>Запас > потребность x2 (среднее за 24ч) → вес умножается на фактор ниже.</summary>
        public const double ANTISTOCKPILE_THRESHOLD = 2;
        public const double ANTISTOCKPILE_FACTOR = 0.5;

        /// <summary>Окно скользящего среднего склада модулей (`warehouse_avg_24h`).</summary>
        public const int WAREHOUSE_AVG_WINDOW_SEC = 24 * 60 * 60;

        // --- И-11: floor guarantee ---------------------------------------------

        /// <summary>Окно из трех прибытий: если за него не выпало ничего, третье выдает гарантию.</summary>
        public const int FLOOR_GUARANTEE_WINDOW = 3;
        public const int FLOOR_GUARANTEE_MIN_GAP = 5;

        /// <summary>Гарантия никогда не выдает гейтовый тир.</summary>
        public static readonly ModuleTier[] FLOOR_GUARANTEE_ALLOWED_TIERS =
        {
            ModuleTier.basic,
            ModuleTier.rare,
        };

        // --- И-8: анти-фрустрация генератора ------------------------------------

        public static readonly Dictionary<Mechanic, double> COVERAGE_MIN = new Dictionary<
            Mechanic,
            double
        > { { Mechanic.drone, 0.6 }, { Mechanic.shuttle, 0.6 }, { Mechanic.liner, 0.7 } };

        /// <summary>Канон 1.8: причины деградации генератора заказов.</summary>
        public enum OrderGenerationDegradedReason
        {
            empty_pool,
            max_attempts,
            unresolvable_invariant,
        }

        /// <summary>Порог «легко произвести». Канон задает его в МИНУТАХ, не в секундах.</summary>
        public static readonly Dictionary<Mechanic, int> EASY_PRODUCE_MAX_MIN = new Dictionary<
            Mechanic,
            int
        > { { Mechanic.drone, 30 }, { Mechanic.shuttle, 30 }, { Mechanic.liner, 30 } };

        public const int MAX_DEFICIT_SLOTS = 1;
        public const int PINCH_MIN = 1;
        public const int PINCH_MAX = 3;
        public const double REPEAT_CAP = 0.5;

        /// <summary>И-10: суммарное время производства заказа <= 60% дедлайна.</summary>
        public const double ORDER_FEASIBILITY_DEADLINE_SHARE = 0.6;

        /// <summary>Канон 1.7: включена ли проверка И-10 для механики. Выключена у дрона.</summary>
        public static readonly Dictionary<Mechanic, bool> ACHIEVABILITY_CHECK = new Dictionary<
            Mechanic,
            bool
        > { { Mechanic.drone, false }, { Mechanic.shuttle, true }, { Mechanic.liner, true } };

        // --- И-13: изоляция дефицита между механиками (контракт лока) -----------

        /// <summary>Потолок TTL лока И-13 — даже забытый дрон-заказ отпускает лок через сутки.</summary>
        public const int DEFICIT_LOCK_TTL_MAX = 24 * 60 * 60;

        /// <summary>Канон 1.5: TTL по умолчанию равен потолку — нормальный путь снятия от него не зависит.</summary>
        public const int DEFICIT_LOCK_TTL = DEFICIT_LOCK_TTL_MAX;

        // --- Дрон: доска, выброс и рефреш ----------------------------------------

        public sealed class LevelOrdersRow
        {
            public int from_level;
            public int orders;
        }

        /// <summary>ТЗ дрона 4.1: сколько карточек заказов видно на доске, по уровню колонии.</summary>
        public static readonly LevelOrdersRow[] NUM_VISIBLE_ORDERS =
        {
            new LevelOrdersRow { from_level = 15, orders = 9 },
            new LevelOrdersRow { from_level = 12, orders = 8 },
            new LevelOrdersRow { from_level = 10, orders = 7 },
            new LevelOrdersRow { from_level = 8, orders = 6 },
            new LevelOrdersRow { from_level = 6, orders = 5 },
            new LevelOrdersRow { from_level = 4, orders = 4 },
            new LevelOrdersRow { from_level = 2, orders = 3 },
        };

        /// <summary>Каркас 7: слот пустует 22 минуты.</summary>
        public const int DRONE_REFRESH_FREE_SEC = 22 * 60;

        public sealed class DroneRefreshStep
        {
            public double remaining_min_gt;
            public int isotopes;
        }

        /// <summary>Лестница платного рефреша. Цена падает по мере приближения к бесплатному.</summary>
        public static readonly DroneRefreshStep[] DRONE_REFRESH_PRICE_LADDER =
        {
            new DroneRefreshStep { remaining_min_gt = 15, isotopes = 10 },
            new DroneRefreshStep { remaining_min_gt = 8, isotopes = 7 },
            new DroneRefreshStep { remaining_min_gt = 3, isotopes = 4 },
            new DroneRefreshStep { remaining_min_gt = 0, isotopes = 2 },
        };

        /// <summary>Таймер истек — рефреш уже произошел бесплатно, платить не за что.</summary>
        public const int DRONE_REFRESH_EXPIRED_PRICE = 0;

        public static int DroneRefreshPrice(double remaining_sec)
        {
            double remaining_min = remaining_sec / 60;
            foreach (var step in DRONE_REFRESH_PRICE_LADDER)
                if (remaining_min > step.remaining_min_gt)
                    return step.isotopes;
            return DRONE_REFRESH_EXPIRED_PRICE;
        }

        /// <summary>
        /// Премия дрона к рыночной цене: клемп итогового множителя. Принято
        /// значение каркаса (раздел 7): min 0.25, max 0.7. Владелец подтвердил
        /// выбор каркаса 2026-08-05 (ТЗ дрона цитирует каркас неточно).
        /// </summary>
        public const double DRONE_PREMIUM_RANGE_MIN = 0.25;
        public const double DRONE_PREMIUM_RANGE_MAX = 0.7;

        /// <summary>Надбавка к премии за целевую дефицитную позицию ([[tz-drone-mars]] 4.3).</summary>
        public const double DRONE_PREMIUM_DEFICIT_BONUS = 0.05;

        // --- И-9: магазин изотопов ------------------------------------------------

        public sealed class IsotopeShopStep
        {
            public double usd;
            public int isotopes;
        }

        public static readonly IsotopeShopStep[] ISOTOPE_SHOP_LADDER =
        {
            new IsotopeShopStep { usd = 1.99, isotopes = 1000 },
            new IsotopeShopStep { usd = 4.99, isotopes = 2500 },
            new IsotopeShopStep { usd = 9.99, isotopes = 6000 },
            new IsotopeShopStep { usd = 19.99, isotopes = 14000 },
            new IsotopeShopStep { usd = 49.99, isotopes = 40000 },
            new IsotopeShopStep { usd = 99.99, isotopes = 100000 },
        };

        // --- Мощности и вместимости --------------------------------------------

        public const int FIELD_SLOTS_START = 4;
        /// <summary>Уровни, на которых выдается бесплатная грядка.</summary>
        public static readonly int[] FIELD_SLOT_UNLOCK_LEVELS = { 3, 6, 9, 13, 17 };
        public const int FIELD_SLOT_PURCHASE_PRICE_ISO = 150;
        public const int FIELD_SLOT_MAX_PURCHASED = 3;

        public const int WAREHOUSE_START_CAPACITY = 50;
        public const int WAREHOUSE_UPGRADE_STEP = 10;
        public const int WAREHOUSE_MAX_CAPACITY = 300;
        /// <summary>Цена расширения склада в кредитах за единицу текущей вместимости. Зеркало economy.ts.</summary>
        public const int WAREHOUSE_UPGRADE_PRICE_PER_CAPACITY = 5;
        public const int MODULE_STOCK_CAP = 100;

        public const int FACTORY_QUEUE_BASE_SLOTS = 2;
        public const int FACTORY_QUEUE_SLOT3_PRICE_ISO = 200;
        public const int FACTORY_QUEUE_SLOT4_PRICE_ISO = 400;

        public const int CONSTRUCTION_SECOND_LINE_PRICE_ISO = 300;

        public static int FieldsAtLevel(int level)
        {
            int count = 0;
            foreach (var l in FIELD_SLOT_UNLOCK_LEVELS)
                if (l <= level)
                    count++;
            return FIELD_SLOTS_START + count;
        }

        // --- Кредитные стоки -----------------------------------------------

        /// <summary>Сток 1: посев культуры стоит кредиты. Имена — из конфиг-таблицы ТЗ.</summary>
        public const double PLANT_COST_PRICE_SHARE = 0.4;
        public const int PLANT_COST_FLOOR = 1;

        public static int PlantingCost(
            int sell_price,
            double price_share = PLANT_COST_PRICE_SHARE,
            int floor = PLANT_COST_FLOOR
        )
        {
            return Math.Max(floor, Numeric.RoundHalfUpToInt(price_share * sell_price));
        }

        /// <summary>
        /// Стартовый баланс кредитов. Без него игра не запускается: посев стоит
        /// кредиты, а заработать их нечем, пока ничего не посеяно.
        /// </summary>
        public const int CREDITS_START = 50;

        /// <summary>
        /// И-15 (анти-софтлок посева). Если кредитов не хватает на самый дешевый
        /// посев, при этом ничего не растет и продать нечего — посев бесплатен.
        /// Инвариант, а не подарок: сток на посеве оправдан, только пока у него есть пол.
        /// </summary>
        public static bool IsPlantingSoftlocked(
            int credits,
            int cheapest_planting_cost,
            bool has_growing_crops,
            bool has_sellable_stock
        )
        {
            return credits < cheapest_planting_cost && !has_growing_crops && !has_sellable_stock;
        }

        /// <summary>
        /// Цена продажи товара со склада за кредиты: доля от `Good.price`.
        /// Тюнимый диапазон по ТЗ 0.7-1.0, старт 1.0 — канал «продажа с рынка».
        /// </summary>
        public const double SELL_PRICE_RATIO = 1.0;

        // --- Сток 2: фабрики покупаются за кредиты -----------------------------

        public sealed class FactoryPriceDef
        {
            public int unlock_level;
            public int first;
            public int second;
        }

        /// <summary>Сток 2: фабрики покупаются за кредиты. Второй экземпляр — вдвое дороже.</summary>
        public static readonly Dictionary<BuildingType, FactoryPriceDef> FACTORY_PRICES =
            new Dictionary<BuildingType, FactoryPriceDef>
            {
                { BuildingType.food_module, new FactoryPriceDef { unlock_level = 3, first = 500, second = 1000 } },
                { BuildingType.mining_site, new FactoryPriceDef { unlock_level = 6, first = 1200, second = 2400 } },
                { BuildingType.atmospheric_module, new FactoryPriceDef { unlock_level = 8, first = 4000, second = 8000 } },
                { BuildingType.textile_module, new FactoryPriceDef { unlock_level = 9, first = 5500, second = 11000 } },
            };

        public const int FACTORY_SECOND_INSTANCE_LEVEL = 15;

        /// <summary>Названия зданий класса А. Живут рядом с ценами — здание показывается на двух экранах.</summary>
        public static readonly Dictionary<BuildingType, string> FACTORY_NAMES = new Dictionary<
            BuildingType,
            string
        >
        {
            { BuildingType.food_module, "Пищевой модуль" },
            { BuildingType.mining_site, "Буровая площадка" },
            { BuildingType.atmospheric_module, "Атмосферный модуль" },
            { BuildingType.textile_module, "Текстильный модуль" },
        };

        /// <summary>Что здание делает. Одной строкой — подпись под кнопкой покупки.</summary>
        public static readonly Dictionary<BuildingType, string> FACTORY_HINTS = new Dictionary<
            BuildingType,
            string
        >
        {
            { BuildingType.food_module, "Перерабатывает сырье в товары подороже." },
            { BuildingType.mining_site, "Добывает реголит и водяной лед. Не требует ни грядки, ни сырья." },
            { BuildingType.atmospheric_module, "Дает кислород и воду для дальних заказов." },
            { BuildingType.textile_module, "Ткань и комбинезоны — самые дорогие товары среза." },
        };

        /// <summary>Сток 3: расширение зоны застройки, 200 x N^1.5, округление к сотням.</summary>
        public const double DOME_EXPANSION_BASE = 200;
        public const double DOME_EXPANSION_EXPONENT = 1.5;
        public const int DOME_EXPANSION_ROUND_STEP = 100;

        public static int DomeExpansionCost(double n)
        {
            double raw = DOME_EXPANSION_BASE * Math.Pow(n, DOME_EXPANSION_EXPONENT);
            return Numeric.RoundHalfUpToInt(raw / DOME_EXPANSION_ROUND_STEP) * DOME_EXPANSION_ROUND_STEP;
        }

        /// <summary>Псевдонимы под имена конфиг-таблицы ТЗ — поиск по имени параметра находит его в коде.</summary>
        public static readonly Dictionary<BuildingType, int> FACTORY_UNLOCK_PRICE_CREDITS =
            new Dictionary<BuildingType, int>
            {
                { BuildingType.food_module, FACTORY_PRICES[BuildingType.food_module].first },
                { BuildingType.mining_site, FACTORY_PRICES[BuildingType.mining_site].first },
                { BuildingType.atmospheric_module, FACTORY_PRICES[BuildingType.atmospheric_module].first },
                { BuildingType.textile_module, FACTORY_PRICES[BuildingType.textile_module].first },
            };

        public static readonly Dictionary<BuildingType, int> FACTORY_SECOND_INSTANCE_PRICE_CREDITS =
            new Dictionary<BuildingType, int>
            {
                { BuildingType.food_module, FACTORY_PRICES[BuildingType.food_module].second },
                { BuildingType.mining_site, FACTORY_PRICES[BuildingType.mining_site].second },
                { BuildingType.atmospheric_module, FACTORY_PRICES[BuildingType.atmospheric_module].second },
                { BuildingType.textile_module, FACTORY_PRICES[BuildingType.textile_module].second },
            };
    }
}
