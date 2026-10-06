namespace MarsColony.Domain
{
    /// <summary>
    /// Опорная модель данных. Канон полей — каркас, раздел 9; рабочая копия
    /// правды — `mars-colony/src/domain/types.ts`, оттуда этот файл и перенесен.
    ///
    /// Имена полей намеренно оставлены в snake_case, а члены перечислений — в
    /// нижнем регистре: это те же строки, что в TypeScript-домене и в ТЗ.
    /// Переименование в C#-стиль сделало бы поиск «параметр из спеки -> место в
    /// коде» невозможным, а разъезд имен в этом проекте повторялся шесть раз.
    /// </summary>
    public enum GoodKind
    {
        crop,
        factory,
    }

    public enum BuildingType
    {
        hydroponics,
        mining_site,
        food_module,
        atmospheric_module,
        textile_module,
        warehouse,
        construction,
        // Водная станция: топит добытый лед в воду. Единственное здание, чей
        // вход — добытое сырье, а не культура.
        water_plant,
    }

    /// <summary>Механика доставки. Из `types.ts`: drone | shuttle | liner.</summary>
    public enum Mechanic
    {
        drone,
        shuttle,
        liner,
    }

    /// <summary>Тир строй-модуля. Из `types.ts`: basic | rare | gated.</summary>
    public enum ModuleTier
    {
        basic,
        rare,
        gated,
    }

    /// <summary>Строй-модуль. Из `types.ts`: единственная валюта, не покупаемая за кредиты (И-1).</summary>
    public enum ModuleId
    {
        panel,
        frame,
        sealant,
        filter,
        cable,
        drill_head,
        reactor_cell,
    }

    public sealed class GoodInput
    {
        public string good_id;
        public int qty;

        public GoodInput(string good_id, int qty)
        {
            this.good_id = good_id;
            this.qty = qty;
        }
    }

    public sealed class Good
    {
        public string id;
        public string name;
        public GoodKind kind;
        public int unlock_level;

        /// <summary>Цена продажи в кредитах.</summary>
        public int price;
        public int base_xp;
        public int prod_time_sec;

        /// <summary>Пусто для кропов.</summary>
        public GoodInput[] inputs;

        /// <summary>Пусто для кропов: они растут в грядке, а не в здании.</summary>
        public BuildingType? required_building;
    }
}
