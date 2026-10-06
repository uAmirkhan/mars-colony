using System;
using System.Collections.Generic;
using System.Linq;

namespace MarsColony.Domain.Config
{
    /// <summary>
    /// Товарный субстрат MVP. Источник истины — [[mars-colony-frame]], раздел 3;
    /// перенесено один в один из `mars-colony/src/domain/config/goods.ts`.
    ///
    /// ВАЖНО. Это ЗЕРКАЛО чужого конфига, а не второй источник истины. Ни одно
    /// число здесь не выбрано в Unity-треке: все взяты из TypeScript-конфига,
    /// который сам взят из каркаса. Расхождение с ним — баг этого файла.
    /// Правится только сверкой с оригиналом, никогда не «под удобство кода».
    ///
    /// Перенесены таблицы, нужные производству и складу. Генератор заказов
    /// (GOOD_BASE_QTY, bracketMult, mechanicMult, slotQuantity) не перенесен:
    /// его шаг цикла в Unity еще не собран. Шов для него — этот же класс.
    /// </summary>
    public static class Goods
    {
        public const string ALGAE = "algae";
        public const string SOY = "soy";
        public const string MUSHROOMS = "mushrooms";
        public const string TOMATOES = "tomatoes";
        public const string COTTON = "cotton";
        public const string POTATO = "potato";
        public const string COFFEE_BEANS = "coffee_beans";
        public const string PROTEIN_BAR = "protein_bar";
        public const string MUSHROOM_SOUP = "mushroom_soup";
        public const string FABRIC = "fabric";
        public const string JUMPSUIT = "jumpsuit";
        public const string COFFEE_RATION = "coffee_ration";
        public const string OXYGEN_TANK = "oxygen_tank";
        public const string REGOLITH = "regolith";
        public const string WATER_ICE = "water_ice";
        public const string IRON_ORE = "iron_ore";
        public const string METHANE = "methane";
        public const string WATER = "water";

        private static readonly GoodInput[] NoInputs = new GoodInput[0];

        public static readonly Dictionary<string, Good> GOODS = new Dictionary<string, Good>
        {
            // --- Гидропоника (грядки) ---
            {
                ALGAE,
                new Good
                {
                    id = ALGAE,
                    name = "Капуста",
                    kind = GoodKind.crop,
                    unlock_level = 1,
                    price = 2,
                    base_xp = 1,
                    prod_time_sec = 900,
                    inputs = NoInputs,
                    required_building = null,
                }
            },
            {
                SOY,
                new Good
                {
                    id = SOY,
                    name = "Соя",
                    kind = GoodKind.crop,
                    unlock_level = 2,
                    price = 3,
                    base_xp = 1,
                    prod_time_sec = 150,
                    inputs = NoInputs,
                    required_building = null,
                }
            },
            {
                MUSHROOMS,
                new Good
                {
                    id = MUSHROOMS,
                    name = "Грибы",
                    kind = GoodKind.crop,
                    unlock_level = 4,
                    price = 5,
                    base_xp = 2,
                    prod_time_sec = 600,
                    inputs = NoInputs,
                    required_building = null,
                }
            },
            {
                TOMATOES,
                new Good
                {
                    id = TOMATOES,
                    name = "Томаты-гидро",
                    kind = GoodKind.crop,
                    unlock_level = 7,
                    price = 6,
                    base_xp = 6,
                    prod_time_sec = 1200,
                    inputs = NoInputs,
                    required_building = null,
                }
            },
            {
                COTTON,
                new Good
                {
                    id = COTTON,
                    name = "Хлопок-синт",
                    kind = GoodKind.crop,
                    unlock_level = 9,
                    price = 4,
                    base_xp = 4,
                    prod_time_sec = 2400,
                    inputs = NoInputs,
                    required_building = null,
                }
            },
            {
                POTATO,
                new Good
                {
                    id = POTATO,
                    name = "Картошка",
                    kind = GoodKind.crop,
                    unlock_level = 4,
                    price = 2,
                    base_xp = 1,
                    prod_time_sec = 300,
                    inputs = NoInputs,
                    required_building = null,
                }
            },
            {
                COFFEE_BEANS,
                new Good
                {
                    id = COFFEE_BEANS,
                    name = "Кофе-бобы",
                    kind = GoodKind.crop,
                    unlock_level = 12,
                    price = 9,
                    base_xp = 8,
                    prod_time_sec = 3600,
                    inputs = NoInputs,
                    required_building = null,
                }
            },
            // --- Переработка (фабрики) ---
            {
                PROTEIN_BAR,
                new Good
                {
                    id = PROTEIN_BAR,
                    name = "Протеин-батончик",
                    kind = GoodKind.factory,
                    unlock_level = 3,
                    price = 12,
                    base_xp = 5,
                    prod_time_sec = 300,
                    inputs = new[] { new GoodInput(SOY, 2) },
                    required_building = BuildingType.food_module,
                }
            },
            {
                MUSHROOM_SOUP,
                new Good
                {
                    id = MUSHROOM_SOUP,
                    name = "Грибной суп",
                    kind = GoodKind.factory,
                    unlock_level = 5,
                    price = 25,
                    base_xp = 11,
                    prod_time_sec = 900,
                    inputs = new[] { new GoodInput(MUSHROOMS, 2) },
                    required_building = BuildingType.food_module,
                }
            },
            {
                FABRIC,
                new Good
                {
                    id = FABRIC,
                    name = "Ткань-синт",
                    kind = GoodKind.factory,
                    unlock_level = 9,
                    price = 37,
                    base_xp = 16,
                    prod_time_sec = 900,
                    inputs = new[] { new GoodInput(COTTON, 2) },
                    required_building = BuildingType.textile_module,
                }
            },
            {
                JUMPSUIT,
                new Good
                {
                    id = JUMPSUIT,
                    name = "Комбинезон",
                    kind = GoodKind.factory,
                    unlock_level = 11,
                    price = 100,
                    base_xp = 43,
                    prod_time_sec = 1800,
                    inputs = new[] { new GoodInput(FABRIC, 2) },
                    required_building = BuildingType.textile_module,
                }
            },
            {
                COFFEE_RATION,
                new Good
                {
                    id = COFFEE_RATION,
                    name = "Кофе-паек",
                    kind = GoodKind.factory,
                    unlock_level = 13,
                    price = 30,
                    base_xp = 13,
                    prod_time_sec = 1200,
                    inputs = new[] { new GoodInput(COFFEE_BEANS, 2), new GoodInput(SOY, 1) },
                    required_building = BuildingType.food_module,
                }
            },
            {
                OXYGEN_TANK,
                new Good
                {
                    id = OXYGEN_TANK,
                    name = "Кислород-баллон",
                    kind = GoodKind.factory,
                    unlock_level = 8,
                    price = 20,
                    base_xp = 9,
                    prod_time_sec = 600,
                    // Кислород из оксидов железа, а не из капусты (Khan 07.09). Зеркало goods.ts.
                    inputs = new[] { new GoodInput(IRON_ORE, 1) },
                    required_building = BuildingType.atmospheric_module,
                }
            },
            // --- Добыча (буровая площадка) ---
            // Технически фабрика с пустыми входами. И-1: реголит НЕ превращается
            // в строй-модули, иначе шаттл теряет роль гейта прогрессии.
            {
                REGOLITH,
                new Good
                {
                    id = REGOLITH,
                    name = "Реголит",
                    kind = GoodKind.factory,
                    unlock_level = 6,
                    price = 5,
                    base_xp = 2,
                    prod_time_sec = 240,
                    inputs = NoInputs,
                    required_building = BuildingType.mining_site,
                }
            },
            {
                WATER_ICE,
                new Good
                {
                    id = WATER_ICE,
                    name = "Водяной лед",
                    kind = GoodKind.factory,
                    unlock_level = 6,
                    price = 14,
                    base_xp = 6,
                    prod_time_sec = 600,
                    inputs = NoInputs,
                    required_building = BuildingType.mining_site,
                }
            },
            // Зеркало goods.ts, добавлено 2026-09-03. Каждая точка добычи в
            // сцене получила свой ресурс: карьер — руду, буровая на леднике —
            // метан, скважина — кислород, сборщики — лед. До этого две точки
            // давали одно и то же, а лед и реголит были тупиками без потребителя.
            {
                IRON_ORE,
                new Good
                {
                    id = IRON_ORE,
                    name = "Железная руда",
                    kind = GoodKind.factory,
                    // Уровень 7, а не 6: на шестом руда садится рядом с реголитом
                    // и льдом и ломает инвариант симуляции (щедрые награды за
                    // уровень перестают опережать скупые). Перебор показал:
                    // уровни 7 и 8 держат, 6 и 9 ломают.
                    unlock_level = 7,
                    price = 10,
                    base_xp = 4,
                    prod_time_sec = 400,
                    inputs = NoInputs,
                    required_building = BuildingType.mining_site,
                }
            },
            {
                METHANE,
                new Good
                {
                    id = METHANE,
                    name = "Метан",
                    kind = GoodKind.factory,
                    unlock_level = 10,
                    price = 25,
                    base_xp = 11,
                    prod_time_sec = 900,
                    inputs = NoInputs,
                    required_building = BuildingType.mining_site,
                }
            },
            {
                WATER,
                new Good
                {
                    id = WATER,
                    name = "Вода",
                    kind = GoodKind.factory,
                    unlock_level = 6,
                    price = 28,
                    base_xp = 12,
                    prod_time_sec = 400,
                    inputs = new[] { new GoodInput(WATER_ICE, 1) },
                    required_building = BuildingType.water_plant,
                }
            },
        };

        /// <summary>
        /// Выход урожая с одного цикла грядки ([[tz-production-mars]] раздел 7).
        /// Не путать с GOOD_BASE_QTY: та про количества в заказе, эта про сбор.
        /// </summary>
        public static readonly Dictionary<string, int> HARVEST_QTY = new Dictionary<string, int>
        {
            { ALGAE, 4 },
            { SOY, 4 },
            { MUSHROOMS, 3 },
            { TOMATOES, 2 },
            { COTTON, 3 },
            { POTATO, 4 },
            { COFFEE_BEANS, 2 },
        };

        public static int HarvestQty(string good_id) =>
            HARVEST_QTY.TryGetValue(good_id, out int qty) ? qty : 1;

        /// <summary>Выход одного цикла фабрики: 1 рецепт = 1 единица, не тюнится.</summary>
        public const int FACTORY_OUTPUT_QTY = 1;

        public static Good Of(string good_id) => GOODS[good_id];

        /// <summary>Все идентификаторы товаров, в порядке объявления в GOODS.</summary>
        public static readonly string[] ALL_GOOD_IDS = GOODS.Keys.ToArray();

        public struct MinMax
        {
            public int min;
            public int max;

            public MinMax(int min, int max)
            {
                this.min = min;
                this.max = max;
            }
        }

        /// <summary>Каркас 3.1: базовые количества на один слот заказа.</summary>
        public static readonly Dictionary<string, MinMax> GOOD_BASE_QTY = new Dictionary<
            string,
            MinMax
        >
        {
            { REGOLITH, new MinMax(3, 7) },
            { WATER_ICE, new MinMax(2, 4) },
            { IRON_ORE, new MinMax(2, 5) },
            { METHANE, new MinMax(1, 3) },
            { WATER, new MinMax(1, 3) },
            { ALGAE, new MinMax(5, 9) },
            { SOY, new MinMax(4, 8) },
            { MUSHROOMS, new MinMax(3, 6) },
            { TOMATOES, new MinMax(2, 4) },
            { COTTON, new MinMax(3, 6) },
            { POTATO, new MinMax(5, 9) },
            { COFFEE_BEANS, new MinMax(2, 4) },
            { PROTEIN_BAR, new MinMax(2, 4) },
            { MUSHROOM_SOUP, new MinMax(2, 3) },
            { FABRIC, new MinMax(1, 2) },
            { JUMPSUIT, new MinMax(1, 2) },
            { COFFEE_RATION, new MinMax(1, 2) },
            { OXYGEN_TANK, new MinMax(2, 3) },
        };

        public sealed class BracketRow
        {
            public int from_level;
            public double mult;
        }

        /// <summary>Каркас 3.1: множитель по брекету уровня колонии.</summary>
        public static readonly BracketRow[] BRACKET_LEVEL_THRESHOLDS =
        {
            new BracketRow { from_level = 20, mult = 2.5 },
            new BracketRow { from_level = 15, mult = 2.0 },
            new BracketRow { from_level = 10, mult = 1.5 },
            new BracketRow { from_level = 1, mult = 1.0 },
        };

        public static double BracketMult(int level)
        {
            foreach (var row in BRACKET_LEVEL_THRESHOLDS)
                if (level >= row.from_level)
                    return row.mult;
            return 1.0;
        }

        /// <summary>Граница «быстрого» кропа для группировки множителей лайнера — цикл <= 15 минут.</summary>
        public const int LINER_FAST_CROP_MAX_SEC = 900;

        /// <summary>Каркас 3.1: множитель механики. У лайнера три группы по скорости производства.</summary>
        public static double MechanicMult(Mechanic mechanic, string good_id)
        {
            if (mechanic != Mechanic.liner)
                return 1.0;
            Good good = Of(good_id);
            if (good.kind == GoodKind.factory)
                return 2.5;
            return good.prod_time_sec <= LINER_FAST_CROP_MAX_SEC ? 6.0 : 3.0;
        }

        /// <summary>
        /// Каркас 3.1: qty = rand(min,max) x bracket_mult x mechanic_mult,
        /// округление вниз, но не ниже min.
        /// </summary>
        public static int SlotQuantity(string good_id, Mechanic mechanic, int level, double roll)
        {
            MinMax range = GOOD_BASE_QTY[good_id];
            double baseVal = range.min + roll * (range.max - range.min);
            double scaled = baseVal * BracketMult(level) * MechanicMult(mechanic, good_id);
            return Math.Max(range.min, (int)Math.Floor(scaled));
        }
    }
}
