using MarsColony.Domain.Config;

namespace MarsColony.Domain
{
    /// <summary>
    /// Настраиваемые параметры для балансного симулятора. Источник истины —
    /// `mars-colony/src/domain/tuning.ts`. Игра всегда работает на значениях
    /// по умолчанию — конфиг остается единственным источником истины.
    ///
    /// `time_scale` — добавка спеки ночи 3 (`SPEC-NOCH-3-igra.md`, п.4), которой
    /// в TS-домене нет: переключатель ускорения x10 в инспекторе только для
    /// проверки. Единственное место, где домен читает время производства,
    /// обязано умножать его на `time_scale` — так ускорение действует на все
    /// таймеры разом, а не расползается по вызывающему коду.
    /// </summary>
    public sealed class Tuning
    {
        /// <summary>Крутизна XP-кривой. Главный рычаг темпа прогрессии.</summary>
        public double xp_curve_exponent;

        /// <summary>Базовый множитель XP-кривой. Сдвигает всю кривую целиком.</summary>
        public double xp_curve_base_coef;

        /// <summary>Множитель наград за уровень.</summary>
        public double level_up_credits_coef;

        /// <summary>Крутизна роста наград.</summary>
        public double level_up_credits_exponent;

        /// <summary>Доля цены продажи в стоимости посева. Первый кредитный сток.</summary>
        public double plant_cost_price_share;

        /// <summary>Минимальная цена посева.</summary>
        public int plant_cost_floor;

        /// <summary>Доля цены при продаже на рынок.</summary>
        public double sell_price_ratio;

        /// <summary>Стартовая вместимость склада.</summary>
        public int warehouse_start_capacity;

        /// <summary>
        /// Множитель длительности всех таймеров домена. 1.0 — обычная скорость,
        /// 10.0 — ускорение x10 для проверки (спека ночи 3, п.4).
        /// </summary>
        public double time_scale = 1.0;
    }

    public static class TuningDefaults
    {
        /// <summary>
        /// Новый экземпляр со значениями по умолчанию. Метод, а не общий
        /// статический объект: инспектор может держать свой мутируемый
        /// экземпляр (крутить `time_scale`), не задевая чужие вызовы,
        /// которые попросили дефолт.
        /// </summary>
        public static Tuning Default() =>
            new Tuning
            {
                xp_curve_exponent = Levels.XP_CURVE_EXPONENT,
                xp_curve_base_coef = Levels.XP_CURVE_BASE_COEF,
                level_up_credits_coef = Levels.LEVEL_UP_CREDITS_COEF,
                level_up_credits_exponent = Levels.LEVEL_UP_CREDITS_EXPONENT,
                plant_cost_price_share = Economy.PLANT_COST_PRICE_SHARE,
                plant_cost_floor = Economy.PLANT_COST_FLOOR,
                sell_price_ratio = Economy.SELL_PRICE_RATIO,
                warehouse_start_capacity = Economy.WAREHOUSE_START_CAPACITY,
                time_scale = 1.0,
            };

        public static Tuning DEFAULT_TUNING => Default();
    }
}
