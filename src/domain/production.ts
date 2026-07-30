/**
 * Производство: грядки и очередь фабрики.
 * Источник истины — [[tz-production-mars]] разделы 2.1, 2.2, 3.2, 3.4.
 *
 * Два правила, которые ломаются первыми при небрежной реализации:
 *  1. Сбор при переполнении склада отклоняется ЦЕЛИКОМ. Слот остается READY,
 *     ни товар, ни XP не начисляются. Урожай не портится и не пропадает.
 *  2. Слот в QUEUED никогда не резервирует входы заранее. Иначе он держит
 *     товар недоступным для заказов, пока сам ничего не производит.
 */

import { GOODS, harvestQty, FACTORY_OUTPUT_QTY } from './config/goods';
import {
  PRODUCTION_XP_K,
  plantingCost,
  isPlantingSoftlocked,
  SELL_PRICE_RATIO,
} from './config/economy';
import type { GoodId, BuildingType } from './types';
import {
  availableOf,
  canAccept,
  consume,
  deposit,
  occupiedGoods,
  type WarehouseState,
} from './warehouse';

export type FieldState = 'EMPTY' | 'GROWING' | 'READY';
export type FactorySlotState = 'EMPTY' | 'QUEUED' | 'PRODUCING' | 'READY';

export interface FieldSlot {
  idx: number;
  state: FieldState;
  good_id: GoodId | null;
  ends_at: number;
}

export interface FactorySlot {
  idx: number;
  building_type: BuildingType;
  state: FactorySlotState;
  good_id: GoodId | null;
  queued_at: number;
  ends_at: number;
}

export interface ProductionContext {
  now: number;
  warehouse: WarehouseState;
  credits: number;
  level: number;
}

export interface ActionResult {
  ok: boolean;
  /** Причина отказа — для UI и для тестов, не для логов. */
  reason?:
    | 'slot_busy'
    | 'not_ready'
    | 'warehouse_full'
    | 'insufficient_balance'
    | 'locked'
    | 'no_inputs';
  credits_delta?: number;
  xp_gained?: number;
  softlock_rescued?: boolean;
}

export function createField(idx: number): FieldSlot {
  return { idx, state: 'EMPTY', good_id: null, ends_at: 0 };
}

export function createFactorySlot(idx: number, building_type: BuildingType): FactorySlot {
  return { idx, building_type, state: 'EMPTY', good_id: null, queued_at: 0, ends_at: 0 };
}

/** READY вычисляется лениво: состояние не хранится, а выводится из времени. */
export function refreshField(field: FieldSlot, now: number): FieldSlot {
  if (field.state === 'GROWING' && now >= field.ends_at) field.state = 'READY';
  return field;
}

export function refreshFactorySlot(slot: FactorySlot, now: number): FactorySlot {
  if (slot.state === 'PRODUCING' && now >= slot.ends_at) slot.state = 'READY';
  return slot;
}

/** Самый дешевый посев среди разблокированных культур — нужен для проверки И-15. */
export function cheapestPlantingCost(level: number): number {
  const costs = (Object.keys(GOODS) as GoodId[])
    .map((id) => GOODS[id])
    .filter((g) => g.kind === 'crop' && g.unlock_level <= level)
    .map((g) => plantingCost(g.price));
  return costs.length ? Math.min(...costs) : 0;
}

/**
 * Посадка. Стоит кредиты (кредитный сток №1 каркаса), кроме случая И-15,
 * когда игрок иначе оказался бы в тупике без единого доступного действия.
 */
export function plant(
  field: FieldSlot,
  good_id: GoodId,
  ctx: ProductionContext,
  all_fields: FieldSlot[],
): ActionResult {
  if (field.state !== 'EMPTY') return { ok: false, reason: 'slot_busy' };

  const good = GOODS[good_id];
  if (good.kind !== 'crop' || good.unlock_level > ctx.level) {
    return { ok: false, reason: 'locked' };
  }

  const cost = plantingCost(good.price);
  let charged = cost;
  let rescued = false;

  if (ctx.credits < cost) {
    const cheapest = cheapestPlantingCost(ctx.level);
    const softlocked =
      cost === cheapest &&
      isPlantingSoftlocked({
        credits: ctx.credits,
        cheapest_planting_cost: cheapest,
        has_growing_crops: all_fields.some((f) => f.state !== 'EMPTY'),
        has_sellable_stock: occupiedGoods(ctx.warehouse).length > 0,
      });
    if (!softlocked) return { ok: false, reason: 'insufficient_balance' };
    charged = 0;
    rescued = true;
  }

  field.state = 'GROWING';
  field.good_id = good_id;
  field.ends_at = ctx.now + good.prod_time_sec;

  return { ok: true, credits_delta: charged > 0 ? -charged : 0, softlock_rescued: rescued };
}

/**
 * Сбор урожая. При переполнении склада отклоняется целиком: слот остается READY,
 * XP не начисляется. Урожай не портится, таймера увядания нет.
 */
export function collectField(field: FieldSlot, ctx: ProductionContext): ActionResult {
  refreshField(field, ctx.now);
  if (field.state !== 'READY' || !field.good_id) return { ok: false, reason: 'not_ready' };

  const good = GOODS[field.good_id];
  // Выход культуры за цикл, не единица: цифры в HARVEST_QTY, раздел 7 ТЗ.
  // Одно и то же число обязано идти и в проверку места, и в депозит, и в XP.
  const yield_qty = harvestQty(field.good_id);

  if (!canAccept(ctx.warehouse, yield_qty)) return { ok: false, reason: 'warehouse_full' };

  deposit(ctx.warehouse, field.good_id, yield_qty);
  const xp = good.base_xp * PRODUCTION_XP_K * yield_qty;

  field.state = 'EMPTY';
  field.good_id = null;
  field.ends_at = 0;

  return { ok: true, xp_gained: xp };
}

/**
 * Постановка рецепта в слот фабрики. Если входов не хватает — слот уходит
 * в QUEUED и ждет пополнения склада, НЕ резервируя ничего заранее.
 */
export function enqueue(
  slot: FactorySlot,
  good_id: GoodId,
  ctx: ProductionContext,
): ActionResult {
  if (slot.state !== 'EMPTY') return { ok: false, reason: 'slot_busy' };

  const good = GOODS[good_id];
  if (
    good.kind !== 'factory' ||
    good.required_building !== slot.building_type ||
    good.unlock_level > ctx.level
  ) {
    return { ok: false, reason: 'locked' };
  }

  slot.good_id = good_id;
  slot.queued_at = ctx.now;

  if (tryStart(slot, ctx)) return { ok: true };

  slot.state = 'QUEUED';
  return { ok: true, reason: 'no_inputs' };
}

/** Атомарный старт: входы списываются в момент старта, не при постановке. */
function tryStart(slot: FactorySlot, ctx: ProductionContext): boolean {
  if (!slot.good_id) return false;
  const good = GOODS[slot.good_id];
  const enough = good.inputs.every(
    (inp) => availableOf(ctx.warehouse, inp.good_id) >= inp.qty,
  );
  if (!enough) return false;

  for (const inp of good.inputs) consume(ctx.warehouse, inp.good_id, inp.qty);

  slot.state = 'PRODUCING';
  slot.ends_at = ctx.now + good.prod_time_sec;
  return true;
}

/**
 * Событие «остаток товара на складе вырос». Обходит ВСЕХ кандидатов до конца
 * списка: провал одного слота ничего не говорит о шансах остальных.
 * Здесь `continue`, а не `break` — ранняя остановка приводила к бессрочному
 * голоданию слота, у которого нужный товар физически есть на складе.
 */
export function onWarehouseStockIncreased(
  slots: FactorySlot[],
  ctx: ProductionContext,
): number {
  let started = 0;
  const waiting = slots
    .filter((s) => s.state === 'QUEUED')
    .sort((a, b) => a.queued_at - b.queued_at);

  for (const slot of waiting) {
    if (tryStart(slot, ctx)) started += 1;
    // continue, не break: следующий кандидат может ждать другой набор входов
  }
  return started;
}

/** Сбор с фабрики. Та же блокировка переполнением, что и у грядки. */
export function collectFactory(slot: FactorySlot, ctx: ProductionContext): ActionResult {
  refreshFactorySlot(slot, ctx.now);
  if (slot.state !== 'READY' || !slot.good_id) return { ok: false, reason: 'not_ready' };

  const good = GOODS[slot.good_id];
  if (!canAccept(ctx.warehouse, FACTORY_OUTPUT_QTY)) {
    return { ok: false, reason: 'warehouse_full' };
  }

  deposit(ctx.warehouse, slot.good_id, FACTORY_OUTPUT_QTY);
  const xp = good.base_xp * PRODUCTION_XP_K * FACTORY_OUTPUT_QTY;

  slot.state = 'EMPTY';
  slot.good_id = null;
  slot.ends_at = 0;
  slot.queued_at = 0;

  return { ok: true, xp_gained: xp };
}

/** Продажа со склада по рыночной цене. Продается только available. */
export function sell(good_id: GoodId, qty: number, ctx: ProductionContext): ActionResult {
  if (!consume(ctx.warehouse, good_id, qty)) return { ok: false, reason: 'no_inputs' };
  const sum = Math.floor(GOODS[good_id].price * SELL_PRICE_RATIO * qty);
  return { ok: true, credits_delta: sum };
}
