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

/**
 * Пол цены ускорения короткого остатка: мелкие суммы не продаем.
 *
 * Ключ `crop` добавлен разрешением противоречия в документах: таблица
 * [[tz-common-systems-mars]] 5.6 объявляет только `[factory] = 10`, а псевдокод
 * того же документа (5.3) индексирует пол по `link.kind`, где kind — crop|factory,
 * то есть читает ключ, которого в таблице нет. Берем то же значение, что у фабрик:
 * пол существует против продажи мелочи, а мелочь одинаково мелкая в обоих случаях.
 */
export const SPEEDUP_FLOOR_ISOTOPES = {
  crop: 10,
  factory: 10,
  shuttle: 15,
} as const;

/**
 * Ниже этого остатка ускорение бесплатно (ТЗ производства, AC7).
 *
 * Прежняя редакция ТЗ кнопку в этом диапазоне скрывала. Заменено по наблюдению
 * за референсом: в Township ниже примерно 30 секунд ускорение грядки бесплатно,
 * кнопка остается. Скрытие молча отнимает опцию у игрока, который уже тянулся
 * к кнопке; обнуление оставляет действие и показывает механику даром тому,
 * кто ей ни разу не пользовался.
 */
export const SPEEDUP_FREE_THRESHOLD_SEC = 30;

/**
 * И-5: цена ускорения производства по ОСТАВШЕМУСЯ времени, не по полному циклу.
 * Минуты округляются вниз: неполная минута не продается.
 */
export function productionSpeedupCost(remaining_sec: number, kind: 'crop' | 'factory'): number {
  if (remaining_sec <= 0) return 0;
  if (remaining_sec <= SPEEDUP_FREE_THRESHOLD_SEC) return 0;
  const minutes = Math.floor(remaining_sec / 60);
  return Math.max(SPEEDUP_FLOOR_ISOTOPES[kind], minutes * SPEEDUP_RATE_ISOTOPES_PER_MIN[kind]);
}

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

// --- Шаттл: отсеки, рейс, кулдаун ---------------------------------------

/** Только у шаттла число слотов переменное: 3-5 (ТЗ шаттла 4). */
export const SLOT_COUNT_MIN = 3;
export const SLOT_COUNT_MAX = 5;

/** Распределение числа отсеков по уровневым брекетам (ТЗ шаттла 4). */
export const SLOT_COUNT_WEIGHTS: Array<{
  from_level: number;
  weights: Record<number, number>;
}> = [
  { from_level: 15, weights: { 3: 0.15, 4: 0.4, 5: 0.45 } },
  { from_level: 9, weights: { 3: 0.3, 4: 0.45, 5: 0.25 } },
  { from_level: 5, weights: { 3: 0.6, 4: 0.3, 5: 0.1 } },
];

/** Предохранитель цикла подбора товаров генератором (ТЗ шаттла 4). */
export const GEN_MAX_ATTEMPTS = 40;

/** Длина рейса по брекетам уровня (ТЗ шаттла 4). */
export const FLIGHT_TIMER_MIN: Array<{ from_level: number; minutes: number }> = [
  { from_level: 15, minutes: 90 },
  { from_level: 9, minutes: 75 },
  { from_level: 5, minutes: 60 },
];

export function flightTimerMin(level: number): number {
  return FLIGHT_TIMER_MIN.find((b) => level >= b.from_level)?.minutes ?? 60;
}

export function slotCountFor(level: number, roll: number): number {
  const row = SLOT_COUNT_WEIGHTS.find((r) => level >= r.from_level);
  const weights = row?.weights ?? { [SLOT_COUNT_MIN]: 1 };
  let acc = 0;
  for (const [count, weight] of Object.entries(weights)) {
    acc += weight;
    if (roll <= acc) return Number(count);
  }
  return SLOT_COUNT_MIN;
}

/** Пауза после сбора всех контейнеров, защита от чейн-фарма (ТЗ шаттла 4). */
export const COLLECT_COOLDOWN_MIN = 5;

/**
 * FTUE: первые прибытия форсируют гарантию на 100% сверх И-11. Доверие к
 * механике без превью наград формируется серией, а не одним эпизодом.
 */
export const FRONT_LOADED_LUCK_ARRIVALS = 3;

/** FTUE: первый рейс короче брекета, чтобы весь цикл прошел в одну сессию. */
export const FTUE_FIRST_TRIP_TIMER_MIN = 12;

/** Порог «заказ брошен» для health-метрики (ТЗ шаттла 4). */
export const IDLE_ORDER_ABANDON_ALERT_H = 24;

/**
 * Пороги табло рейса (ТЗ шаттла 6.1/6.3). Живут в конфиге, а не в верстке:
 * это правила игры, а не оформление. Ниже минуты кнопка скипа исчезает —
 * платить пол цены за минуту ожидания игроку продавать нечестно.
 */
export const SKIP_HIDE_BELOW_SEC = 60;
export const SKIP_ARRIVING_SOON_SEC = 5 * 60;

// --- Стройка: ускорение --------------------------------------------------

/**
 * Ставка ускорения стройки. ТЗ производства помечает ее экстраполяцией из И-5,
 * а не прямым чтением: И-5 описывает грядки, фабрики, шаттл и лайнер, стройку —
 * нет. Число между фабрикой (6) и фоновым шаттлом (2.3-3.9).
 */
export const CONSTRUCTION_SPEEDUP_RATE_ISO_PER_MIN = 3;
/** Стройка идет часами, минимальный чек выше производственного. */
export const SPEEDUP_FLOOR_ISO_CONSTRUCTION = 20;
export const CONSTRUCTION_ACTIVE_LINES_BASE = 1;

export function constructionSpeedupCost(remaining_sec: number): number {
  if (remaining_sec <= 0) return 0;
  if (remaining_sec <= SPEEDUP_FREE_THRESHOLD_SEC) return 0;
  const minutes = Math.floor(remaining_sec / 60);
  return Math.max(
    SPEEDUP_FLOOR_ISO_CONSTRUCTION,
    minutes * CONSTRUCTION_SPEEDUP_RATE_ISO_PER_MIN,
  );
}

// --- Строй-модули: дроп, докупка, EV ------------------------------------

export const MODULE_BUYOUT_ISOTOPES: Record<ModuleTier, number> = {
  basic: 150,
  rare: 200,
  gated: 400,
};

/** Веса дропа по тирам (каркас, раздел 4). Сумма = 1. */
export const TIER_WEIGHTS: Record<ModuleTier, number> = {
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
    TIER_WEIGHTS.basic * MODULE_BUYOUT_ISOTOPES.basic +
    TIER_WEIGHTS.rare * MODULE_BUYOUT_ISOTOPES.rare +
    TIER_WEIGHTS.gated * MODULE_BUYOUT_ISOTOPES.gated
  );
}

// --- И-7: pity и анти-стокпайл ------------------------------------------

/** Счетчик на пару (игрок, модуль), не на стройку целиком. */
export const PITY_K = 4;
export const PITY_MULTIPLIER = 2;
/** Запас > потребность x2 (среднее за 24ч) → вес умножается на фактор ниже. */
export const ANTISTOCKPILE_THRESHOLD = 2;
/**
 * Канон задает фактор x0.5, а не делитель 2. Значение то же, но форма важна:
 * следующая правка диапазона (x0.3-x0.7 по спеке) в терминах делителя читается
 * наизнанку и приглашает ошибиться.
 */
export const ANTISTOCKPILE_FACTOR = 0.5;

// --- И-11: floor guarantee ----------------------------------------------

/**
 * Окно из трех прибытий: если за него не выпало ничего, третье выдает гарантию.
 *
 * Прежнее имя `AFTER_EMPTY_ARRIVALS` со значением 2 описывало ровно то же
 * правило с другого конца. Переименование без правки числа посадило бы
 * константу, которая врет собственным именем, — поэтому 2 стало 3.
 */
export const FLOOR_GUARANTEE_WINDOW = 3;
export const FLOOR_GUARANTEE_MIN_GAP = 5;
/** Гарантия никогда не выдает гейтовый тир — иначе выгодно держать стройку голодной. */
export const FLOOR_GUARANTEE_ALLOWED_TIERS: ModuleTier[] = ['basic', 'rare'];

// --- И-8: анти-фрустрация генератора ------------------------------------

export const COVERAGE_MIN = { drone: 0.6, shuttle: 0.6, liner: 0.7 };
/** Порог «легко произвести». Канон задает его в МИНУТАХ, не в секундах. */
export const EASY_PRODUCE_MAX_MIN = { drone: 30, shuttle: 30, liner: 30 };
export const MAX_DEFICIT_SLOTS = 1;
export const PINCH_MIN = 1;
export const PINCH_MAX = 3;
export const REPEAT_CAP = 0.5;

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
  // Буровая: между пищевым и атмосферным по цене и по уровню. Открывает
  // добычу — единственный источник товаров, не требующий ни грядки, ни сырья.
  mining_site: { unlock_level: 6, first: 1200, second: 2400 },
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
  mining_site: FACTORY_PRICES.mining_site.first,
  atmospheric_module: FACTORY_PRICES.atmospheric_module.first,
  textile_module: FACTORY_PRICES.textile_module.first,
} as const;

export const FACTORY_SECOND_INSTANCE_PRICE_CREDITS = {
  food_module: FACTORY_PRICES.food_module.second,
  mining_site: FACTORY_PRICES.mining_site.second,
  atmospheric_module: FACTORY_PRICES.atmospheric_module.second,
  textile_module: FACTORY_PRICES.textile_module.second,
} as const;
