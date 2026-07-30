/**
 * Настраиваемые параметры для балансного симулятора.
 *
 * Зачем отдельный слой. Конфиг остается единственным источником истины: значения
 * по умолчанию берутся ровно из него, ни одно число здесь не дублируется руками.
 * Но симулятор обязан уметь спросить «а что будет, если показатель кривой не 1.35,
 * а 1.55» — не переписывая конфиг и не трогая игру.
 *
 * Правило: игра всегда работает на значениях по умолчанию. Отклонения существуют
 * только внутри прогона симулятора и наружу не протекают.
 */

import {
  PLANT_COST_FLOOR,
  PLANT_COST_PRICE_SHARE,
  SELL_PRICE_RATIO,
  WAREHOUSE_START_CAPACITY,
} from './config/economy';
import {
  LEVEL_UP_CREDITS_COEF,
  LEVEL_UP_CREDITS_EXPONENT,
  XP_CURVE_BASE_COEF,
  XP_CURVE_EXPONENT,
} from './config/levels';

export interface Tuning {
  /** Крутизна XP-кривой. Главный рычаг темпа прогрессии. */
  xp_curve_exponent: number;
  /** Базовый множитель XP-кривой. Сдвигает всю кривую целиком. */
  xp_curve_base_coef: number;
  /** Множитель наград за уровень. */
  level_up_credits_coef: number;
  /** Крутизна роста наград. Ниже показателя стока расширений — по каркасу. */
  level_up_credits_exponent: number;
  /** Доля цены продажи в стоимости посева. Первый кредитный сток. */
  plant_cost_price_share: number;
  /** Минимальная цена посева. */
  plant_cost_floor: number;
  /** Доля цены при продаже на рынок. */
  sell_price_ratio: number;
  /** Стартовая вместимость склада. Конверсионный узел. */
  warehouse_start_capacity: number;
}

/** Значения по умолчанию собираются из конфига, а не переписываются. */
export const DEFAULT_TUNING: Tuning = {
  xp_curve_exponent: XP_CURVE_EXPONENT,
  xp_curve_base_coef: XP_CURVE_BASE_COEF,
  level_up_credits_coef: LEVEL_UP_CREDITS_COEF,
  level_up_credits_exponent: LEVEL_UP_CREDITS_EXPONENT,
  plant_cost_price_share: PLANT_COST_PRICE_SHARE,
  plant_cost_floor: PLANT_COST_FLOOR,
  sell_price_ratio: SELL_PRICE_RATIO,
  warehouse_start_capacity: WAREHOUSE_START_CAPACITY,
};

/** Границы ползунков. Выход за них не запрещен, но помечается как заведомо ломающий. */
export const TUNING_RANGES: Record<
  keyof Tuning,
  { min: number; max: number; step: number; label: string }
> = {
  xp_curve_exponent: { min: 1.0, max: 2.0, step: 0.05, label: 'Крутизна XP-кривой' },
  xp_curve_base_coef: { min: 40, max: 300, step: 10, label: 'База XP-кривой' },
  level_up_credits_coef: { min: 20, max: 300, step: 10, label: 'Награда за уровень' },
  level_up_credits_exponent: { min: 1.0, max: 2.0, step: 0.05, label: 'Рост наград' },
  plant_cost_price_share: { min: 0, max: 1, step: 0.05, label: 'Цена посева от цены' },
  plant_cost_floor: { min: 0, max: 10, step: 1, label: 'Минимум за посев' },
  sell_price_ratio: { min: 0.3, max: 1.2, step: 0.05, label: 'Коэффициент продажи' },
  warehouse_start_capacity: { min: 20, max: 200, step: 5, label: 'Склад на старте' },
};
