/**
 * Экономические инварианты И-1..И-14 как константы и функции.
 * Источник истины — [[mars-colony-frame]], раздел 5. Противоречие каркасу = баг.
 */

import type { Mechanic, ModuleTier } from '../types';

// --- И-3: XP транспорта -------------------------------------------------

/** Коэффициент XP механики. Апгрейдами НЕ меняется. */
export const TRANSPORT_XP_K: Record<Mechanic, number> = {
  drone: 2,
  shuttle: 8,
  liner: 8,
};

/** И-14: сбор урожая и готового товара дает XP напрямую, K = 1. */
export const PRODUCTION_XP_K = 1;

/** Кэп лайнера: доля XP_to_next(level) на один контейнер и на весь рейс. */
export const LINER_XP_CAP_PER_CONTAINER = 0.05;
export const LINER_XP_CAP_PER_TRIP = 0.4;

// --- И-5: ставки ускорения (изотопы за минуту остатка) ------------------

export const SPEEDUP_RATE_ISOTOPES_PER_MIN = {
  crop: 5,
  factory: 6,
} as const;

/** Пол цены ускорения короткого остатка: мелкие суммы не продаем. */
export const SPEEDUP_FLOOR_ISOTOPES = {
  factory: 10,
  shuttle: 15,
} as const;

// --- И-6: цена скипа шаттла ---------------------------------------------

export const SPEEDUP_TARIFF_ISOTOPES_PER_SLOT = { shuttle: 70 } as const;

/**
 * И-6: skip = max(15, round((remaining_min / trip_min) x 70 x slot_count)).
 * Инвариант: полный скип всегда дороже трети shadow-ценности груза.
 */
export function shuttleSkipPrice(
  remaining_min: number,
  trip_min: number,
  slot_count: number,
): number {
  const raw =
    (remaining_min / trip_min) * SPEEDUP_TARIFF_ISOTOPES_PER_SLOT.shuttle * slot_count;
  return Math.max(SPEEDUP_FLOOR_ISOTOPES.shuttle, Math.round(raw));
}

// --- Строй-модули: дроп, докупка, EV ------------------------------------

export const MODULE_BUYOUT_ISOTOPES: Record<ModuleTier, number> = {
  basic: 150,
  rare: 200,
  gated: 400,
};

/** Веса дропа по тирам (каркас, раздел 4). Сумма = 1. */
export const MODULE_DROP_WEIGHTS: Record<ModuleTier, number> = {
  basic: 0.62,
  rare: 0.33,
  gated: 0.05,
};

/**
 * Shadow-ценность одного отсека шаттла в изотопах: сколько стоило бы
 * докупить то, что отсек привезет в среднем. Основание тарифа И-6.
 */
export function shuttleSlotExpectedValue(): number {
  return (
    MODULE_DROP_WEIGHTS.basic * MODULE_BUYOUT_ISOTOPES.basic +
    MODULE_DROP_WEIGHTS.rare * MODULE_BUYOUT_ISOTOPES.rare +
    MODULE_DROP_WEIGHTS.gated * MODULE_BUYOUT_ISOTOPES.gated
  );
}

// --- И-7: pity и анти-стокпайл ------------------------------------------

/** Счетчик на пару (игрок, модуль), не на стройку целиком. */
export const PITY_MISSES_BEFORE_BOOST = 4;
export const PITY_WEIGHT_MULTIPLIER = 2;
/** Запас > потребность x2 (среднее за 24ч) → вес делится. */
export const ANTI_STOCKPILE_THRESHOLD = 2;
export const ANTI_STOCKPILE_DIVISOR = 2;

// --- И-11: floor guarantee ----------------------------------------------

export const FLOOR_GUARANTEE_AFTER_EMPTY_ARRIVALS = 2;
export const FLOOR_GUARANTEE_COOLDOWN_ARRIVALS = 5;
/** Гарантия никогда не выдает гейтовый тир — иначе выгодно держать стройку голодной. */
export const FLOOR_GUARANTEE_ALLOWED_TIERS: ModuleTier[] = ['basic', 'rare'];

// --- И-8: анти-фрустрация генератора ------------------------------------

export const ORDER_COVERAGE_MIN = { drone: 0.6, shuttle: 0.6, liner: 0.7 };
export const ORDER_QUICK_PRODUCTION_MAX_SEC = 1800;
export const MAX_DEFICIT_SLOTS = 1;
export const ORDER_DEFICIT_EXTRA_RANGE = { min: 1, max: 3 };
export const ORDER_MAX_REPEAT_SHARE = 0.5;

/** И-10: суммарное время производства заказа <= 60% дедлайна. */
export const ORDER_FEASIBILITY_DEADLINE_SHARE = 0.6;

// --- Дрон: выброс и рефреш ----------------------------------------------

/** Каркас 7: слот пустует 22 минуты. Число сверено с замером референса по кадрам. */
export const DRONE_REFRESH_FREE_SEC = 22 * 60;

/**
 * Лестница платного рефреша. Цена падает по мере приближения к бесплатному.
 * Верхняя ступень = половина награды за уровень: рефреш должен быть решением,
 * а не рефлексом (обоснование — [[tz-drone-mars]] 3.3).
 */
export const DRONE_REFRESH_PRICE_LADDER: Array<{
  remaining_min_gt: number;
  isotopes: number;
}> = [
  { remaining_min_gt: 15, isotopes: 10 },
  { remaining_min_gt: 8, isotopes: 7 },
  { remaining_min_gt: 3, isotopes: 4 },
  { remaining_min_gt: 0, isotopes: 2 },
];

/** Таймер истек — рефреш уже произошел бесплатно, платить не за что. */
export const DRONE_REFRESH_EXPIRED_PRICE = 0;

export function droneRefreshPrice(remaining_sec: number): number {
  const remaining_min = remaining_sec / 60;
  for (const step of DRONE_REFRESH_PRICE_LADDER) {
    if (remaining_min > step.remaining_min_gt) return step.isotopes;
  }
  return DRONE_REFRESH_EXPIRED_PRICE;
}

/** Премия дрона к рыночной цене: +25..70%, медиана ~45%. */
export const DRONE_PREMIUM_RANGE = { min: 0.25, max: 0.7 };

// --- И-9: магазин изотопов ----------------------------------------------

export const ISOTOPE_SHOP_LADDER = [
  { usd: 1.99, isotopes: 1000 },
  { usd: 4.99, isotopes: 2500 },
  { usd: 9.99, isotopes: 6000 },
  { usd: 19.99, isotopes: 14000 },
  { usd: 49.99, isotopes: 40000 },
  { usd: 99.99, isotopes: 100000 },
];

// --- Мощности и вместимости ---------------------------------------------

export const FIELD_SLOTS_START = 4;
/** Уровни, на которых выдается бесплатная грядка. */
export const FIELD_SLOT_UNLOCK_LEVELS = [3, 6, 9, 13, 17];
export const FIELD_SLOT_PURCHASE_PRICE_ISO = 150;
export const FIELD_SLOT_MAX_PURCHASED = 3;

export const WAREHOUSE_START_CAPACITY = 50;
export const WAREHOUSE_UPGRADE_STEP = 10;
export const WAREHOUSE_MAX_CAPACITY = 300;
export const MODULE_STOCK_CAP = 100;

export const FACTORY_QUEUE_BASE_SLOTS = 2;
export const FACTORY_QUEUE_SLOT3_PRICE_ISO = 200;
export const FACTORY_QUEUE_SLOT4_PRICE_ISO = 400;

export const CONSTRUCTION_SECOND_LINE_PRICE_ISO = 300;

export function fieldsAtLevel(level: number): number {
  return FIELD_SLOTS_START + FIELD_SLOT_UNLOCK_LEVELS.filter((l) => l <= level).length;
}

// --- Кредитные стоки -----------------------------------------------------

/**
 * Цена продажи товара со склада за кредиты: доля от `Good.price`.
 * Тюнимый диапазон по ТЗ 0.7-1.0, старт 1.0 — канал «продажа с рынка».
 */
export const SELL_PRICE_RATIO = 1.0;

/** Сток 1: посев культуры стоит кредиты. Имена параметров — из конфиг-таблицы ТЗ. */
export const PLANT_COST_PRICE_SHARE = 0.4;
export const PLANT_COST_FLOOR = 1;

export function plantingCost(
  sell_price: number,
  price_share: number = PLANT_COST_PRICE_SHARE,
  floor: number = PLANT_COST_FLOOR,
): number {
  return Math.max(floor, Math.round(price_share * sell_price));
}

/**
 * Стартовый баланс кредитов. Без него игра не запускается: посев стоит кредиты,
 * а заработать их нечем, пока ничего не посеяно. Поймано симулятором на первом прогоне.
 */
export const CREDITS_START = 50;

/**
 * И-15 (анти-софтлок посева). Игрок не может оказаться в состоянии, где посеять
 * нечего и заработать не на чем. Если кредитов не хватает на самый дешевый посев,
 * при этом ничего не растет и продать нечего — этот посев становится бесплатным.
 *
 * Инвариант, а не подарок: сток на посеве оправдан только пока у него есть пол.
 */
export function isPlantingSoftlocked(ctx: {
  credits: number;
  cheapest_planting_cost: number;
  has_growing_crops: boolean;
  has_sellable_stock: boolean;
}): boolean {
  return (
    ctx.credits < ctx.cheapest_planting_cost &&
    !ctx.has_growing_crops &&
    !ctx.has_sellable_stock
  );
}

/** Сток 2: фабрики покупаются за кредиты. Второй экземпляр — вдвое дороже. */
export const FACTORY_PRICES = {
  food_module: { unlock_level: 3, first: 500, second: 1000 },
  atmospheric_module: { unlock_level: 8, first: 4000, second: 8000 },
  textile_module: { unlock_level: 9, first: 5500, second: 11000 },
} as const;

export const FACTORY_SECOND_INSTANCE_LEVEL = 15;

/** Сток 3: расширение зоны застройки, 200 x N^1.5, округление к сотням. */
export const DOME_EXPANSION_BASE = 200;
export const DOME_EXPANSION_EXPONENT = 1.5;
export const DOME_EXPANSION_ROUND_STEP = 100;

export function domeExpansionCost(n: number): number {
  const raw = DOME_EXPANSION_BASE * n ** DOME_EXPANSION_EXPONENT;
  return Math.round(raw / DOME_EXPANSION_ROUND_STEP) * DOME_EXPANSION_ROUND_STEP;
}

/**
 * Псевдонимы под имена конфиг-таблицы ТЗ. Существуют, чтобы поиск по имени
 * параметра из документа находил его в коде — это и есть защита от разъезда имен,
 * на котором проект уже спотыкался.
 */
export const FACTORY_UNLOCK_PRICE_CREDITS = {
  food_module: FACTORY_PRICES.food_module.first,
  atmospheric_module: FACTORY_PRICES.atmospheric_module.first,
  textile_module: FACTORY_PRICES.textile_module.first,
} as const;

export const FACTORY_SECOND_INSTANCE_PRICE_CREDITS = {
  food_module: FACTORY_PRICES.food_module.second,
  atmospheric_module: FACTORY_PRICES.atmospheric_module.second,
  textile_module: FACTORY_PRICES.textile_module.second,
} as const;
