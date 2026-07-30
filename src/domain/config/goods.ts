/**
 * Товарный субстрат MVP. Источник истины — [[mars-colony-frame]], раздел 3.
 * Ни одно из этих чисел не должно дублироваться нигде в коде.
 */

import type { Good, GoodId, Mechanic } from '../types';

export const GOODS: Record<GoodId, Good> = {
  // --- Гидропоника (грядки) ---
  algae: {
    id: 'algae',
    name: 'Водоросли',
    kind: 'crop',
    unlock_level: 1,
    price: 2,
    base_xp: 1,
    prod_time_sec: 120,
    inputs: [],
    required_building: null,
  },
  soy: {
    id: 'soy',
    name: 'Соя',
    kind: 'crop',
    unlock_level: 2,
    price: 3,
    base_xp: 1,
    prod_time_sec: 150,
    inputs: [],
    required_building: null,
  },
  mushrooms: {
    id: 'mushrooms',
    name: 'Грибы',
    kind: 'crop',
    unlock_level: 4,
    price: 5,
    base_xp: 2,
    prod_time_sec: 300,
    inputs: [],
    required_building: null,
  },
  tomatoes: {
    id: 'tomatoes',
    name: 'Томаты-гидро',
    kind: 'crop',
    unlock_level: 7,
    price: 6,
    base_xp: 6,
    prod_time_sec: 3600,
    inputs: [],
    required_building: null,
  },
  cotton: {
    id: 'cotton',
    name: 'Хлопок-синт',
    kind: 'crop',
    unlock_level: 9,
    price: 4,
    base_xp: 4,
    prod_time_sec: 900,
    inputs: [],
    required_building: null,
  },
  coffee_beans: {
    id: 'coffee_beans',
    name: 'Кофе-бобы',
    kind: 'crop',
    unlock_level: 12,
    price: 9,
    base_xp: 8,
    prod_time_sec: 1800,
    inputs: [],
    required_building: null,
  },

  // --- Переработка (фабрики) ---
  protein_bar: {
    id: 'protein_bar',
    name: 'Протеин-батончик',
    kind: 'factory',
    unlock_level: 3,
    // Цена поднята с 5: соя на входе (2 x 3) стоила 6, переработка была убыточной.
    price: 12,
    base_xp: 5,
    prod_time_sec: 300,
    inputs: [{ good_id: 'soy', qty: 2 }],
    required_building: 'food_module',
  },
  mushroom_soup: {
    id: 'mushroom_soup',
    name: 'Грибной суп',
    kind: 'factory',
    unlock_level: 5,
    price: 25,
    base_xp: 11,
    prod_time_sec: 900,
    inputs: [{ good_id: 'mushrooms', qty: 2 }],
    required_building: 'food_module',
  },
  fabric: {
    id: 'fabric',
    name: 'Ткань-синт',
    kind: 'factory',
    unlock_level: 9,
    price: 37,
    base_xp: 16,
    prod_time_sec: 900,
    inputs: [{ good_id: 'cotton', qty: 2 }],
    required_building: 'textile_module',
  },
  jumpsuit: {
    id: 'jumpsuit',
    name: 'Комбинезон',
    kind: 'factory',
    unlock_level: 11,
    // Цена поднята с 45: две ткани на входе стоят 74, комбинезон был чистым убытком.
    price: 100,
    base_xp: 43,
    prod_time_sec: 1800,
    inputs: [{ good_id: 'fabric', qty: 2 }],
    required_building: 'textile_module',
  },
  coffee_ration: {
    id: 'coffee_ration',
    name: 'Кофе-паек',
    kind: 'factory',
    unlock_level: 13,
    price: 30,
    base_xp: 13,
    prod_time_sec: 1200,
    inputs: [
      { good_id: 'coffee_beans', qty: 2 },
      { good_id: 'soy', qty: 1 },
    ],
    required_building: 'food_module',
  },
  oxygen_tank: {
    id: 'oxygen_tank',
    name: 'Кислород-баллон',
    kind: 'factory',
    unlock_level: 8,
    price: 14,
    base_xp: 6,
    prod_time_sec: 600,
    inputs: [{ good_id: 'algae', qty: 3 }],
    required_building: 'atmospheric_module',
  },

  // --- Добыча (буровая площадка) ---
  //
  // Появились 2026-07-31: панорама колонии показывала ледяной карьер и буровую,
  // а в экономике добываемых ресурсов не было вообще. Мир обещал производство,
  // которого нет, — игрок видит технику и ждет, что она что-то дает.
  //
  // Технически это фабрика с пустыми входами: движок очереди переиспользуется
  // без единой новой функции. Ключевое ограничение — И-1: строй-модули приходят
  // ТОЛЬКО шаттлом. Поэтому реголит не превращается в модули, иначе шаттл теряет
  // роль гейта прогрессии. Добытое идет в заказы и на продажу, как культуры.
  //
  // Числа подобраны так, чтобы попасть в И-2 (XP около 0.43 от цены) без
  // исключения из инварианта: 2/5 = 0.40 и 6/14 = 0.43.
  regolith: {
    id: 'regolith',
    name: 'Реголит',
    kind: 'factory',
    unlock_level: 6,
    price: 5,
    base_xp: 2,
    prod_time_sec: 240,
    inputs: [],
    required_building: 'mining_site',
  },
  water_ice: {
    id: 'water_ice',
    name: 'Водяной лед',
    kind: 'factory',
    unlock_level: 6,
    price: 14,
    base_xp: 6,
    prod_time_sec: 600,
    inputs: [],
    required_building: 'mining_site',
  },
};

export const ALL_GOOD_IDS = Object.keys(GOODS) as GoodId[];

export const goodsOfKind = (kind: Good['kind']): Good[] =>
  ALL_GOOD_IDS.map((id) => GOODS[id]).filter((g) => g.kind === kind);

/**
 * Выход урожая с одного цикла грядки ([[tz-production-mars]] раздел 7).
 * Не путать с GOOD_BASE_QTY: та про количества в заказе, эта про сбор.
 */
export const HARVEST_QTY: Record<string, number> = {
  algae: 4,
  soy: 4,
  mushrooms: 3,
  tomatoes: 2,
  cotton: 3,
  coffee_beans: 2,
};

export function harvestQty(good_id: GoodId): number {
  return HARVEST_QTY[good_id] ?? 1;
}

/** Выход одного цикла фабрики: 1 рецепт = 1 единица, не тюнится. */
export const FACTORY_OUTPUT_QTY = 1;

/** Каркас 3.1: базовые количества на один слот заказа. */
export const GOOD_BASE_QTY: Record<GoodId, { min: number; max: number }> = {
  regolith: { min: 3, max: 7 },
  water_ice: { min: 2, max: 4 },
  algae: { min: 5, max: 9 },
  soy: { min: 4, max: 8 },
  mushrooms: { min: 3, max: 6 },
  tomatoes: { min: 2, max: 4 },
  cotton: { min: 3, max: 6 },
  coffee_beans: { min: 2, max: 4 },
  protein_bar: { min: 2, max: 4 },
  mushroom_soup: { min: 2, max: 3 },
  fabric: { min: 1, max: 2 },
  jumpsuit: { min: 1, max: 2 },
  coffee_ration: { min: 1, max: 2 },
  oxygen_tank: { min: 2, max: 3 },
};

/**
 * Каркас 3.1: множитель по брекету уровня колонии. Растет по брекетам, а не
 * упирается в потолок на пятом уровне — это фикс замечания приемки про qty_cap.
 */
export const BRACKET_LEVEL_THRESHOLDS: Array<{ from_level: number; mult: number }> = [
  { from_level: 20, mult: 2.5 },
  { from_level: 15, mult: 2.0 },
  { from_level: 10, mult: 1.5 },
  { from_level: 1, mult: 1.0 },
];

export function bracketMult(level: number): number {
  const bracket = BRACKET_LEVEL_THRESHOLDS.find((b) => level >= b.from_level);
  return bracket ? bracket.mult : 1.0;
}

/** Граница «быстрого» кропа для группировки множителей лайнера — цикл <= 15 минут. */
export const LINER_FAST_CROP_MAX_SEC = 900;

/** Каркас 3.1: множитель механики. У лайнера три группы по скорости производства. */
export function mechanicMult(mechanic: Mechanic, good_id: GoodId): number {
  if (mechanic !== 'liner') return 1.0;
  const good = GOODS[good_id];
  if (good.kind === 'factory') return 2.5;
  return good.prod_time_sec <= LINER_FAST_CROP_MAX_SEC ? 6.0 : 3.0;
}

/**
 * Каркас 3.1: qty = rand(min,max) x bracket_mult x mechanic_mult,
 * округление вниз, но не ниже min.
 */
export function slotQuantity(
  good_id: GoodId,
  mechanic: Mechanic,
  level: number,
  roll: number,
): number {
  const { min, max } = GOOD_BASE_QTY[good_id];
  const base = min + roll * (max - min);
  const scaled = base * bracketMult(level) * mechanicMult(mechanic, good_id);
  return Math.max(min, Math.floor(scaled));
}
