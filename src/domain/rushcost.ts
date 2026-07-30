/**
 * И-4: цена докупки = rush-cost цепочки x 1.2, округление к витринным числам.
 * Rush-cost = сумма (минуты звена x ставка ускорения типа здания), рекурсивно по входам.
 *
 * Одна функция на все витрины докупки — каркас, раздел 8, пункт 5.
 * Если у докупки появится второй расчет где-то еще, это баг реализации.
 */

import { GOODS } from './config/goods';
import { SPEEDUP_RATE_ISOTOPES_PER_MIN } from './config/economy';
import type { GoodId } from './types';

export const BUYOUT_MARKUP = 1.2;

/**
 * Витринное округление. Каркас называет ряд «50/100/150...», но это верхний
 * конец лестницы: для дешевого сырья шаг в 50 превращает цену в 50 при
 * себестоимости 12. Ниже — ступенчатый ряд по порядку величины.
 *
 * ВНИМАНИЕ: ступени ниже 50 — интерпретация, каркас их не задает явно.
 * Требует решения владельца каркаса перед плейтестом.
 */
export function roundToShowcase(value: number): number {
  const step = value < 20 ? 1 : value < 50 ? 5 : value < 100 ? 10 : value < 500 ? 25 : 50;
  return Math.max(step, Math.round(value / step) * step);
}

/**
 * Стоимость мгновенно произвести одну единицу товара со всей цепочкой входов,
 * в изотопах. Кропы считаются по ставке грядок, фабричные — по ставке фабрик.
 */
export function rushCost(good_id: GoodId): number {
  const good = GOODS[good_id];
  const rate =
    good.kind === 'crop' ? SPEEDUP_RATE_ISOTOPES_PER_MIN.crop : SPEEDUP_RATE_ISOTOPES_PER_MIN.factory;
  const own = (good.prod_time_sec / 60) * rate;
  const inputs = good.inputs.reduce(
    (sum, input) => sum + rushCost(input.good_id) * input.qty,
    0,
  );
  return own + inputs;
}

/** И-4: цена докупки qty единиц товара в слот заказа. */
export function buyoutPrice(good_id: GoodId, qty: number): number {
  return roundToShowcase(rushCost(good_id) * qty * BUYOUT_MARKUP);
}
