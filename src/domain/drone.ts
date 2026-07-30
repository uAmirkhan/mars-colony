/**
 * Дрон-курьер: доход и ритм сессии, дешевый отказ.
 * Источник истины — [[tz-drone-mars]], разделы 3, 4 и 9.
 *
 * Роль механики в трио: дрон — единственная, где отказ от заказа дешев.
 * Поэтому здесь есть выброс с таймером, а платный рефреш стоит дорого
 * НАМЕРЕННО: отказ обязан быть дешевым по бесплатному пути (подождать),
 * а не потому, что платный путь ничего не стоит.
 *
 * Резервирование происходит в момент тапа «Погрузить», а не при наличии
 * товара на складе. Иначе товар оказался бы недоступен производству просто
 * потому, что где-то на доске висит заказ, который игрок и не собирался брать.
 */

import {
  COVERAGE_MIN,
  DRONE_PREMIUM_RANGE,
  DRONE_REFRESH_FREE_SEC,
  MAX_DEFICIT_SLOTS,
  PINCH_MAX,
  PINCH_MIN,
  REPEAT_CAP,
  TRANSPORT_XP_K,
} from './config/economy';
import { ALL_GOOD_IDS, GOODS, slotQuantity } from './config/goods';
import { roundToShowcase } from './rushcost';
import type { GoodId } from './types';
import {
  availableOf,
  reserve,
  shipReserved,
  unreserve,
  type WarehouseState,
} from './warehouse';

export type OrderSlotState = 'active' | 'in_progress' | 'ready' | 'empty_cooldown';

export interface OrderPosition {
  good_id: GoodId;
  qty: number;
  /** Погружено тапом «Погрузить» или докуплено. Товар при этом зарезервирован. */
  filled: boolean;
}

export interface OrderSlot {
  idx: number;
  state: OrderSlotState;
  npc_name: string;
  positions: OrderPosition[];
  credits_reward: number;
  xp_reward: number;
  /** Когда истекает бесплатный рефреш. Значимо только в `empty_cooldown`. */
  refresh_at: number;
}

/** ТЗ 4.1: сколько заказов видно на доске. Верхняя граница, не цель наполнения. */
export function slotsAtLevel(level: number): number {
  if (level >= 15) return 9;
  if (level >= 12) return 8;
  if (level >= 10) return 7;
  if (level >= 8) return 6;
  if (level >= 6) return 5;
  if (level >= 4) return 4;
  return 3;
}

/**
 * ТЗ 4.2.1: веса числа позиций по уровню. Явные веса, а не формула, —
 * распределение тюнится отдельно от среднего значения.
 */
const POSITIONS_COUNT_WEIGHTS: Array<{ from_level: number; weights: Record<number, number> }> =
  [
    { from_level: 15, weights: { 4: 0.15, 5: 0.35, 6: 0.5 } },
    { from_level: 12, weights: { 3: 0.15, 4: 0.3, 5: 0.3, 6: 0.25 } },
    { from_level: 10, weights: { 3: 0.25, 4: 0.35, 5: 0.25, 6: 0.15 } },
    { from_level: 8, weights: { 2: 0.2, 3: 0.35, 4: 0.3, 5: 0.15 } },
    { from_level: 6, weights: { 2: 0.35, 3: 0.4, 4: 0.25 } },
    { from_level: 4, weights: { 1: 0.25, 2: 0.45, 3: 0.3 } },
    { from_level: 2, weights: { 1: 0.55, 2: 0.45 } },
  ];

export function positionsCountFor(level: number, roll: number): number {
  const row = POSITIONS_COUNT_WEIGHTS.find((r) => level >= r.from_level);
  const weights = row?.weights ?? { 1: 1 };
  let acc = 0;
  for (const [count, weight] of Object.entries(weights)) {
    acc += weight;
    if (roll <= acc) return Number(count);
  }
  return Number(Object.keys(weights).at(-1) ?? 1);
}

const NPC_NAMES = [
  'Ирина, гидропоника',
  'Марк, столовая',
  'Лу, мастерская',
  'Дана, медблок',
  'Петр, склад',
  'Сати, оранжерея',
  'Олаф, энергоузел',
  'Ева, лаборатория',
  'Ким, шлюз',
];

export interface GeneratorContext {
  level: number;
  warehouse: WarehouseState;
  /** Что игрок уже умеет производить: построенные здания и открытые культуры. */
  available_goods: GoodId[];
  /** Другие активные заказы доски — для анти-повтора (REPEAT_SCOPE=board). */
  board: OrderSlot[];
  rng: () => number;
}

/** Доля позиций, которые игрок может закрыть прямо сейчас или быстро произвести. */
function easyRatio(positions: OrderPosition[], warehouse: WarehouseState): number {
  if (positions.length === 0) return 1;
  const easy = positions.filter((p) => availableOf(warehouse, p.good_id) >= p.qty).length;
  return easy / positions.length;
}

/** Максимальная доля пересечения с любым заказом доски (ТЗ 4.2, REPEAT_SCOPE=board). */
function maxRepeatRatio(positions: OrderPosition[], board: OrderSlot[]): number {
  const ids = new Set(positions.map((p) => p.good_id));
  if (ids.size === 0) return 0;

  let worst = 0;
  for (const slot of board) {
    if (slot.state === 'empty_cooldown') continue;
    const other = new Set(slot.positions.map((p) => p.good_id));
    const shared = [...ids].filter((id) => other.has(id)).length;
    worst = Math.max(worst, shared / ids.size);
  }
  return worst;
}

/**
 * Генерация одного заказа в слот.
 *
 * И-8 (анти-фрустрация) соблюдается перегенерацией с ограниченным числом
 * попыток, а не хитрым подбором: попытки конечны, и если ни одна не прошла,
 * заказ все равно выдается. Заказ, которого нет, хуже неидеального заказа —
 * пустая доска читается как поломка игры.
 */
export function generateOrder(idx: number, ctx: GeneratorContext): OrderSlot {
  const MAX_ATTEMPTS = 12;
  let best: OrderPosition[] = [];

  for (let attempt = 0; attempt < MAX_ATTEMPTS; attempt++) {
    const count = positionsCountFor(ctx.level, ctx.rng());
    const pool = [...ctx.available_goods];
    const positions: OrderPosition[] = [];
    let deficit_used = 0;

    for (let i = 0; i < count && pool.length > 0; i++) {
      const pick_at = Math.min(pool.length - 1, Math.floor(ctx.rng() * pool.length));
      const good_id = pool.splice(pick_at, 1)[0]!;
      let qty = slotQuantity(good_id, 'drone', ctx.level, ctx.rng());

      // И-8: не больше одной дефицитной позиции на заказ. Дефицит — это
      // «чуть больше, чем на складе», а не «недостижимо много».
      const have = availableOf(ctx.warehouse, good_id);

      if (qty > have) {
        if (deficit_used >= MAX_DEFICIT_SLOTS) {
          // Бюджет дефицита исчерпан. Урезать до складского остатка можно,
          // только если остаток есть. При нуле на складе позиция осталась бы
          // дефицитной при любом количестве — такую пропускаем целиком.
          // Заказ короче лучше, чем заказ, нарушающий инвариант.
          if (have === 0) {
            i -= 1; // позиция не засчитана, пробуем другой товар из пула
            continue;
          }
          qty = have;
        } else {
          deficit_used += 1;
          const pinch = PINCH_MIN + Math.floor(ctx.rng() * (PINCH_MAX - PINCH_MIN + 1));
          qty = have + pinch;
        }
      }

      positions.push({ good_id, qty, filled: false });
    }

    best = positions;
    const coverage_ok = easyRatio(positions, ctx.warehouse) >= COVERAGE_MIN.drone;
    const repeat_ok = maxRepeatRatio(positions, ctx.board) <= REPEAT_CAP;
    if (coverage_ok && repeat_ok) break;
  }

  const reward = orderReward(best, ctx.rng());
  return {
    idx,
    state: 'active',
    npc_name: NPC_NAMES[idx % NPC_NAMES.length]!,
    positions: best,
    credits_reward: reward.credits,
    xp_reward: reward.xp,
    refresh_at: 0,
  };
}

/**
 * ТЗ 4.3: награда считается дроном, а не генератором. Движок отдает состав,
 * деньги назначает механика — у шаттла и лайнера они другие при том же составе.
 */
export function orderReward(
  positions: OrderPosition[],
  jitter_roll = 0.5,
): { credits: number; xp: number; premium: number } {
  const market_sum = positions.reduce((sum, p) => sum + GOODS[p.good_id].price * p.qty, 0);

  let premium = 1.48;
  const has_factory = positions.some((p) => GOODS[p.good_id].kind === 'factory');
  const all_crops =
    positions.length > 0 && positions.every((p) => GOODS[p.good_id].kind === 'crop');

  // Фабричный товар дольше готовить — премия выше. Только грядки — ниже.
  if (has_factory) premium += 0.08;
  else if (all_crops) premium -= 0.05;

  // Мало позиций, крупный лот — премия выше. Замер вертолета: 4 позиции +64%,
  // 6 позиций +37%. Закономерность воспроизведена явным слагаемым.
  if (positions.length <= 2) premium += 0.05;
  else if (positions.length >= 5) premium -= 0.05;

  premium += (jitter_roll - 0.5) * 0.04; // джиттер +-0.02
  premium = Math.min(
    1 + DRONE_PREMIUM_RANGE.max,
    Math.max(1 + DRONE_PREMIUM_RANGE.min, premium),
  );

  const xp = positions.reduce(
    (sum, p) => sum + GOODS[p.good_id].base_xp * TRANSPORT_XP_K.drone * p.qty,
    0,
  );

  return { credits: roundToShowcase(market_sum * premium), xp, premium };
}

/** Погрузка одной позиции: резерв со склада в слот заказа. */
export function loadPosition(
  slot: OrderSlot,
  position_idx: number,
  warehouse: WarehouseState,
): boolean {
  const position = slot.positions[position_idx];
  if (!position || position.filled) return false;
  if (slot.state !== 'active' && slot.state !== 'in_progress') return false;
  if (!reserve(warehouse, position.good_id, position.qty)) return false;

  position.filled = true;
  slot.state = slot.positions.every((p) => p.filled) ? 'ready' : 'in_progress';
  return true;
}

/** Отправка: зарезервированное физически уходит со склада, слот пустеет. */
export function sendOrder(
  slot: OrderSlot,
  warehouse: WarehouseState,
): { ok: boolean; credits: number; xp: number } {
  if (slot.state !== 'ready') return { ok: false, credits: 0, xp: 0 };

  for (const position of slot.positions) {
    shipReserved(warehouse, position.good_id, position.qty);
  }
  return { ok: true, credits: slot.credits_reward, xp: slot.xp_reward };
}

/**
 * Выброс заказа. Уже погруженное возвращается на склад — игрок не должен
 * терять товар за отказ от заказа, иначе выброс перестает быть дешевым.
 */
export function discardOrder(slot: OrderSlot, now: number): void {
  if (slot.state === 'empty_cooldown') return;
  slot.state = 'empty_cooldown';
  slot.refresh_at = now + DRONE_REFRESH_FREE_SEC;
}

/** Возврат резерва при выбросе. Вызывается до смены состояния слота. */
export function releaseReserved(slot: OrderSlot, warehouse: WarehouseState): void {
  for (const position of slot.positions) {
    if (position.filled) {
      unreserve(warehouse, position.good_id, position.qty);
      position.filled = false;
    }
  }
}

/** Пул товаров, доступных игроку: открытые культуры и рецепты построенных зданий. */
export function availableGoodsFor(level: number, buildings: Set<string>): GoodId[] {
  return ALL_GOOD_IDS.filter((id) => {
    const good = GOODS[id];
    if (good.unlock_level > level) return false;
    if (good.kind === 'crop') return true;
    return good.required_building !== null && buildings.has(good.required_building);
  });
}
