/**
 * Метрика баланса: кредиты за час занятости узкого места.
 *
 * Существует потому, что цена товара в отрыве от времени не значит ничего.
 * Игрок расплачивается не деньгами, а занятостью узкого места: у культуры это
 * грядка (занята от посева до сбора), у фабричного товара — слот очереди
 * (занят на время рецепта). Сравнение «маржа за штуку» показывает переработку
 * прибыльной там, где за час она убыточна — именно эта подмена однажды пропустила
 * дефект комбинезона мимо зелёного теста.
 *
 * Формулы: [[tz-rebalans-ekonomiki]] раздел 2.
 */

import { plantingCost } from './config/economy';
import { GOODS, harvestQty } from './config/goods';
import type { Good, GoodId } from './types';

const SEC_PER_HOUR = 3600;

/** Кредиты за час занятости ГРЯДКИ. Маржа цикла = цена x урожай - посев. */
export function creditsPerHourPot(good_id: GoodId): number {
  const good = GOODS[good_id];
  if (good.kind !== 'crop') {
    throw new Error(`creditsPerHourPot: ${good_id} не культура, грядку не занимает`);
  }
  const margin = good.price * harvestQty(good_id) - plantingCost(good.price);
  return margin / (good.prod_time_sec / SEC_PER_HOUR);
}

/**
 * Стоимость и время цепочки ОТ СЕМЕЧКИ: игрок не покупает вход по рыночной
 * цене, он его выращивает. Затраты — только посевы; время — только рецепты,
 * потому что меряем занятость слота, а грядка это отдельный ресурс.
 */
export function chainCost(good_id: GoodId): { seeds: number; recipe_sec: number } {
  const good: Good = GOODS[good_id];

  if (good.kind === 'crop') {
    // Одна грядка отдаёт harvestQty штук за один посев.
    return { seeds: plantingCost(good.price), recipe_sec: 0 };
  }

  let seeds = 0;
  let recipe_sec = good.prod_time_sec;

  for (const input of good.inputs) {
    const src = GOODS[input.good_id];
    if (src.kind === 'crop') {
      // Чтобы получить qty штук, нужно ceil(qty / урожай) посевов: грядки дискретны.
      const plantings = Math.ceil(input.qty / harvestQty(input.good_id));
      seeds += plantingCost(src.price) * plantings;
    } else if (src.kind === 'factory') {
      const sub = chainCost(input.good_id);
      seeds += sub.seeds * input.qty;
      recipe_sec += sub.recipe_sec * input.qty;
    } else {
      // Руда и прочее добывается, а не выращивается: считаем по рыночной цене.
      seeds += src.price * input.qty;
    }
  }

  return { seeds, recipe_sec };
}

/** Кредиты за час занятости СЛОТА очереди, цепочка считается от семечки. */
export function creditsPerHourSlot(good_id: GoodId): number {
  const good = GOODS[good_id];
  if (good.kind !== 'factory') {
    throw new Error(`creditsPerHourSlot: ${good_id} не фабричный товар, слот не занимает`);
  }
  const { seeds, recipe_sec } = chainCost(good_id);
  return (good.price - seeds) / (recipe_sec / SEC_PER_HOUR);
}

/** Культуры, которые входят хотя бы в один рецепт: судятся по цепочке (И-Э2). */
export function recipeInputCrops(): Set<GoodId> {
  const inputs = new Set<GoodId>();
  for (const id of Object.keys(GOODS) as GoodId[]) {
    for (const inp of GOODS[id].inputs) {
      if (GOODS[inp.good_id].kind === 'crop') inputs.add(inp.good_id);
    }
  }
  return inputs;
}

/** Лучшая культура по кредитам за час грядки — база порога И-Э1. */
export function bestCropPerHour(): number {
  const crops = (Object.keys(GOODS) as GoodId[]).filter((id) => GOODS[id].kind === 'crop');
  return Math.max(...crops.map(creditsPerHourPot));
}

/** И-Э1: во сколько раз переработка обязана обгонять лучшую культуру. */
export const PROCESSING_ADVANTAGE = 1.5;
