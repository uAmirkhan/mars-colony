/**
 * Грузовой шаттл: единственный источник строй-модулей (И-1) и главный гейт
 * прогрессии. Источник истины — [[tz-shuttle-mars]], разделы 2-5.
 *
 * Роль в трио: у дрона отказ дешевый, у лайнера заказ сгорает по дедлайну,
 * у шаттла отказа нет вовсе. Поэтому здесь нет ни кнопки «Выбросить», ни
 * кнопки «Отправить»: заполнение последнего отсека само стартует рейс
 * (п.2.3), а стартовавший рейс не отменяется. Это осознанный анти-скип —
 * игрок не может передумать на полпути и тем обесценить ожидание.
 *
 * Награды роллятся в момент ОТПРАВКИ, а не сбора. Иначе переустановка клиента
 * или перевод часов дают бесплатный реролл содержимого контейнеров.
 */

import {
  COLLECT_COOLDOWN_MIN,
  COVERAGE_MIN,
  EASY_PRODUCE_MAX_MIN,
  FTUE_FIRST_TRIP_TIMER_MIN,
  flightTimerMin,
  GEN_MAX_ATTEMPTS,
  MAX_DEFICIT_SLOTS,
  PINCH_MAX,
  PINCH_MIN,
  REPEAT_CAP,
  SLOT_COUNT_MIN,
  shuttleSkipPrice,
  slotCountFor,
  TRANSPORT_XP_K,
} from './config/economy';
import { GOODS, slotQuantity } from './config/goods';
import { type DropContext, rollArrival } from './droproller';
import { buyoutPrice } from './rushcost';
import type { GoodId, ModuleId } from './types';
import { availableOf, reserve, shipReserved, type WarehouseState } from './warehouse';

export type ShuttleState = 'ORDER' | 'IN_TRANSIT' | 'ARRIVED' | 'COOLDOWN';

export interface ShuttleSlot {
  idx: number;
  good_id: GoodId;
  qty_required: number;
  qty_filled: number;
  /** Чем закрыт отсек. Докупка (И-12) помечена отдельно: она не возвратима. */
  filled_by: 'self' | 'purchase' | null;
  /** Награда, зафиксированная при отправке. До отправки — null. */
  reward: ModuleId | null;
  collected: boolean;
  /** Отсек переписан гарантией И-11. Показывается игроку, не скрывается. */
  floor_forced: boolean;
}

export interface ShuttleTrip {
  state: ShuttleState;
  slots: ShuttleSlot[];
  /** Длина рейса в минутах — нужна формуле скипа И-6 как знаменатель. */
  trip_min: number;
  departed_at: number;
  arrives_at: number;
  cooldown_until: number;
  is_first_trip: boolean;
  /** Порядковый номер прибытия игрока. Вход для FTUE-удачи и И-11. */
  arrival_no: number;
}

export interface ShuttleGenContext {
  level: number;
  warehouse: WarehouseState;
  available_goods: GoodId[];
  /** Прошлый рейс — для анти-повтора (REPEAT_CAP считается к нему). */
  previous?: ShuttleTrip | null;
  /** Первый рейс игрока: форсированный состав и укороченный таймер (п.2.1). */
  is_first_trip: boolean;
  arrival_no: number;
  rng: () => number;
}

function slotShort(slot: ShuttleSlot): number {
  return Math.max(0, slot.qty_required - slot.qty_filled);
}

/** Хватает ли склада закрыть отсек целиком прямо сейчас. */
export function slotCovered(slot: ShuttleSlot, warehouse: WarehouseState): boolean {
  const short = slotShort(slot);
  if (short === 0) return true;
  return availableOf(warehouse, slot.good_id) >= short;
}

/**
 * «Легкая» позиция по И-8. Инвариант написан через ИЛИ: покрыта складом ИЛИ
 * производится не дольше EASY_PRODUCE_MAX_MIN. Вторая половина условия важнее
 * первой — без нее генератор считает пустой склад безвыходным положением и
 * вырождает рейс до одного отсека, хотя игрок собирает водоросли за две минуты.
 */
function isEasy(good_id: GoodId, qty: number, warehouse: WarehouseState): boolean {
  if (availableOf(warehouse, good_id) >= qty) return true;
  return GOODS[good_id].prod_time_sec <= EASY_PRODUCE_MAX_MIN.shuttle * 60;
}

/** Доля легких отсеков в рейсе. Вход инварианта И-8. */
function easyRatio(slots: ShuttleSlot[], warehouse: WarehouseState): number {
  if (slots.length === 0) return 1;
  const easy = slots.filter((s) => isEasy(s.good_id, s.qty_required, warehouse)).length;
  return easy / slots.length;
}

function repeatRatio(slots: ShuttleSlot[], previous: ShuttleTrip | null | undefined): number {
  if (!previous || slots.length === 0) return 0;
  const ids = new Set(slots.map((s) => s.good_id));
  const before = new Set(previous.slots.map((s) => s.good_id));
  const shared = [...ids].filter((id) => before.has(id)).length;
  return shared / ids.size;
}

/**
 * Генерация заказа. Тот же движок, что у дрона, с шаттл-специфичным конфигом:
 * переменное число отсеков 3-5 и собственный анти-повтор (сравнение с прошлым
 * рейсом, а не с доской — рейс у шаттла ровно один).
 *
 * FTUE переопределяет генератор целиком: ровно три отсека, все закрываются
 * складом, ни одного дефицитного. Первый цикл обязан пройтись бесплатно —
 * иначе игрок знакомится с необратимым таймером и платным выходом одновременно.
 */
export function generateTrip(ctx: ShuttleGenContext): ShuttleTrip {
  const trip_min = ctx.is_first_trip ? FTUE_FIRST_TRIP_TIMER_MIN : flightTimerMin(ctx.level);
  let best: ShuttleSlot[] = [];

  for (let attempt = 0; attempt < GEN_MAX_ATTEMPTS; attempt++) {
    const count = ctx.is_first_trip ? SLOT_COUNT_MIN : slotCountFor(ctx.level, ctx.rng());
    const pool = [...ctx.available_goods];
    const slots: ShuttleSlot[] = [];
    let deficit_used = 0;

    for (let i = 0; i < count && pool.length > 0; i++) {
      const pick_at = Math.min(pool.length - 1, Math.floor(ctx.rng() * pool.length));
      const good_id = pool.splice(pick_at, 1)[0]!;
      let qty = slotQuantity(good_id, 'shuttle', ctx.level, ctx.rng());
      const have = availableOf(ctx.warehouse, good_id);
      const quick = GOODS[good_id].prod_time_sec <= EASY_PRODUCE_MAX_MIN.shuttle * 60;

      // FTUE: дефицита нет вовсе (MAX_DEFICIT_SLOTS=0 на первом рейсе), и
      // быстрое производство поблажкой не считается — первый рейс обязан
      // закрываться прямо со склада, без похода на грядку.
      if (ctx.is_first_trip) {
        if (qty > have) {
          if (have === 0) {
            i -= 1;
            continue;
          }
          qty = have;
        }
      } else if (qty > have && !quick) {
        // Дефицит по И-8 — это то, что игрок не может ни взять со склада, ни
        // быстро произвести. Товар с коротким циклом дефицитом не считается,
        // иначе пустой склад делал бы дефицитным вообще все.
        if (deficit_used >= MAX_DEFICIT_SLOTS) {
          if (have === 0) {
            i -= 1;
            continue;
          }
          qty = have;
        } else {
          deficit_used += 1;
          const pinch = PINCH_MIN + Math.floor(ctx.rng() * (PINCH_MAX - PINCH_MIN + 1));
          qty = have + pinch;
        }
      }

      slots.push({
        idx: slots.length,
        good_id,
        qty_required: qty,
        qty_filled: 0,
        filled_by: null,
        reward: null,
        collected: false,
        floor_forced: false,
      });
    }

    best = slots;
    const coverage_ok = easyRatio(slots, ctx.warehouse) >= COVERAGE_MIN.shuttle;
    const repeat_ok = repeatRatio(slots, ctx.previous) <= REPEAT_CAP;
    if (coverage_ok && repeat_ok) break;
  }

  return {
    state: 'ORDER',
    slots: best,
    trip_min,
    departed_at: 0,
    arrives_at: 0,
    cooldown_until: 0,
    is_first_trip: ctx.is_first_trip,
    arrival_no: ctx.arrival_no,
  };
}

/** И-3: XP за отсек = базовый XP товара x K x количество. K шаттла = 8. */
export function slotXp(slot: ShuttleSlot): number {
  return GOODS[slot.good_id].base_xp * TRANSPORT_XP_K.shuttle * slot.qty_required;
}

export function tripXp(trip: ShuttleTrip): number {
  return trip.slots.reduce((sum, s) => sum + slotXp(s), 0);
}

/** Цена докупки остатка отсека: И-4, rush-cost цепочки x 1.2 с округлением. */
export function slotBuyoutPrice(slot: ShuttleSlot, warehouse: WarehouseState): number {
  const short = slotShort(slot);
  if (short === 0) return 0;
  return buyoutPrice(slot.good_id, short, warehouse);
}

export function allSlotsLoaded(trip: ShuttleTrip): boolean {
  return trip.slots.length > 0 && trip.slots.every((s) => slotShort(s) === 0);
}

/**
 * Отправка. Не действие игрока: вызывается из погрузки, когда закрылся
 * последний отсек (п.2.3). Здесь же роллятся и фиксируются награды.
 */
function depart(trip: ShuttleTrip, warehouse: WarehouseState, now: number, drop: DropContext) {
  for (const slot of trip.slots) {
    // Докупленное (И-12) в склад не заходило — списывать оттуда нечего.
    if (slot.filled_by === 'purchase') continue;
    shipReserved(warehouse, slot.good_id, slot.qty_required);
  }

  const roll = rollArrival(trip.slots.length, drop);
  trip.slots.forEach((slot, i) => {
    slot.reward = roll.modules[i] ?? null;
    slot.floor_forced = roll.floor_forced_slot === i;
  });

  trip.state = 'IN_TRANSIT';
  trip.departed_at = now;
  trip.arrives_at = now + trip.trip_min * 60;
  return roll;
}

export interface LoadResult {
  ok: boolean;
  loaded: number;
  departed: boolean;
  /** Обновленные счетчики дропа — их хранит игрок, а не рейс. */
  drop_state: ReturnType<typeof rollArrival> | null;
}

/**
 * Погрузка отсека со склада. Частичная допустима (п.6.2): списывает столько,
 * сколько есть, отсек остается открытым до догрузки. Запрет частичной погрузки
 * заставлял бы игрока копить весь объем целиком, а это ровно та фрустрация,
 * против которой написан И-8.
 */
export function loadSlot(
  trip: ShuttleTrip,
  idx: number,
  warehouse: WarehouseState,
  now: number,
  drop: DropContext,
): LoadResult {
  const empty: LoadResult = { ok: false, loaded: 0, departed: false, drop_state: null };
  if (trip.state !== 'ORDER') return empty;

  const slot = trip.slots[idx];
  if (!slot) return empty;

  const short = slotShort(slot);
  if (short === 0) return empty;

  const take = Math.min(short, availableOf(warehouse, slot.good_id));
  if (take <= 0) return empty;
  if (!reserve(warehouse, slot.good_id, take)) return empty;

  slot.qty_filled += take;
  if (slot.filled_by === null) slot.filled_by = 'self';

  const departed = allSlotsLoaded(trip);
  const drop_state = departed ? depart(trip, warehouse, now, drop) : null;
  return { ok: true, loaded: take, departed, drop_state };
}

/**
 * Докупка остатка за изотопы. И-12: товар зачисляется прямо в отсек, минуя
 * склад, и не может быть изъят обратно. Это закрывает арбитраж «докупить
 * дешево тут, скормить другой механике там».
 */
export function buyoutSlot(
  trip: ShuttleTrip,
  idx: number,
  warehouse: WarehouseState,
  now: number,
  drop: DropContext,
): LoadResult & { price: number } {
  const empty = { ok: false, loaded: 0, departed: false, drop_state: null, price: 0 };
  if (trip.state !== 'ORDER') return empty;

  const slot = trip.slots[idx];
  if (!slot) return empty;

  const short = slotShort(slot);
  if (short === 0) return empty;

  const price = slotBuyoutPrice(slot, warehouse);
  slot.qty_filled += short;
  slot.filled_by = 'purchase';

  const departed = allSlotsLoaded(trip);
  const drop_state = departed ? depart(trip, warehouse, now, drop) : null;
  return { ok: true, loaded: short, departed, drop_state, price };
}

/** И-6: цена скипа рейса. Растет с числом отсеков, падает с остатком таймера. */
export function skipPrice(trip: ShuttleTrip, now: number): number {
  if (trip.state !== 'IN_TRANSIT') return 0;
  const remaining_min = Math.max(0, (trip.arrives_at - now) / 60);
  return shuttleSkipPrice(remaining_min, trip.trip_min, trip.slots.length);
}

/** Перевод рейса в прибытие по времени. Ленивый, как и остальные таймеры. */
export function refreshTrip(trip: ShuttleTrip, now: number): ShuttleTrip {
  if (trip.state === 'IN_TRANSIT' && now >= trip.arrives_at) {
    trip.state = 'ARRIVED';
  }
  return trip;
}

/** Скип: рейс считается прибывшим немедленно. Цену списывает вызывающий. */
export function skipFlight(trip: ShuttleTrip, now: number): boolean {
  if (trip.state !== 'IN_TRANSIT') return false;
  trip.arrives_at = now;
  trip.state = 'ARRIVED';
  return true;
}

/** Вскрытие одного контейнера. Возвращает модуль или null, если брать нечего. */
export function collectContainer(trip: ShuttleTrip, idx: number): ModuleId | null {
  if (trip.state !== 'ARRIVED') return null;
  const slot = trip.slots[idx];
  if (!slot || slot.collected || slot.reward === null) return null;
  slot.collected = true;
  return slot.reward;
}

export function allCollected(trip: ShuttleTrip): boolean {
  return trip.slots.every((s) => s.collected);
}

/** Все контейнеры вскрыты — станция уходит в кулдаун перед новым заказом. */
export function startCooldown(trip: ShuttleTrip, now: number): void {
  trip.state = 'COOLDOWN';
  trip.cooldown_until = now + COLLECT_COOLDOWN_MIN * 60;
}
