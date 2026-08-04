/**
 * Дефекты, найденные на прогоне 1 Unity-трека. Файл существует только затем,
 * чтобы каждая находка краснела в репозитории, а не жила в отчете текстом.
 *
 * Ни один тест здесь не чинит код. Если тест позеленел — значит починили домен,
 * и это ровно то, что должно было случиться.
 */

import fc from 'fast-check';
import { describe, expect, it } from 'vitest';
import { makeRng } from '../../sim/rng';
import { EASY_PRODUCE_MAX_MIN, SLOT_COUNT_MIN } from '../config/economy';
import { GOODS } from '../config/goods';
import { ALL_MODULE_IDS } from '../config/modules';
import { type DropContext, rollArrival } from '../droproller';
import {
  allSlotsLoaded,
  buyoutSlot,
  generateTrip,
  loadSlot,
  type ShuttleGenContext,
  type ShuttleTrip,
} from '../shuttle';
import type { GoodId } from '../types';
import { createWarehouse, deposit, qtyOf, reservedOf, totalQty } from '../warehouse';

const NOW = 1_000_000;
const POOL: GoodId[] = ['algae', 'soy', 'mushrooms', 'tomatoes', 'cotton'];

function drop(patch: Partial<DropContext> = {}): DropContext {
  return {
    pity: {},
    stock: {},
    need: {},
    gated_open: false,
    arrival_no: 5,
    arrivals_without_needed: 0,
    last_floor_arrival: 0,
    rng: () => 0.5,
    ...patch,
  };
}

function genCtx(patch: Partial<ShuttleGenContext> = {}): ShuttleGenContext {
  const warehouse = createWarehouse(500);
  for (const id of POOL) deposit(warehouse, id, 30);
  return {
    level: 6,
    warehouse,
    available_goods: [...POOL],
    previous: null,
    is_first_trip: false,
    arrival_no: 1,
    rng: () => 0.5,
    ...patch,
  };
}

function oneSlotTrip(good_id: GoodId, qty_required: number): ShuttleTrip {
  return {
    state: 'ORDER',
    slots: [
      {
        idx: 0,
        good_id,
        qty_required,
        qty_filled: 0,
        filled_by: null,
        reward: null,
        collected: false,
        floor_forced: false,
      },
    ],
    trip_min: 60,
    departed_at: 0,
    arrives_at: 0,
    cooldown_until: 0,
    is_first_trip: false,
    arrival_no: 1,
  };
}

/* ------------------------------------------------------------------------ *
 * Д-1. Частичная погрузка + докупка запирает товар на складе навсегда.
 *
 * `buyoutSlot` перетирает `filled_by` в 'purchase' (shuttle.ts:307), а `depart`
 * пропускает `shipReserved` для любого отсека с этой меткой (shuttle.ts:226).
 * Уже зарезервированные складом единицы не уезжают и не возвращаются: qty
 * остается, reserved остается, `availableOf` навсегда ноль. Емкость склада
 * теряется молча — ничего не падает, счетчик «занято» просто не сходится.
 * ------------------------------------------------------------------------ */
describe('Д-1: докупка поверх частичной погрузки теряет зарезервированный товар', () => {
  it('после отправки резерв не висит на складе', () => {
    const warehouse = createWarehouse(500);
    deposit(warehouse, 'algae', 3);
    const trip = oneSlotTrip('algae', 5);

    // Игрок грузит все, что есть (3 из 5), потом докупает остаток за изотопы.
    expect(loadSlot(trip, 0, warehouse, NOW, drop()).loaded).toBe(3);
    const bought = buyoutSlot(trip, 0, warehouse, NOW, drop());
    expect(bought.departed).toBe(true);

    // Три водоросли уехали в отсеке. На складе их быть не должно.
    expect(reservedOf(warehouse, 'algae')).toBe(0);
    expect(qtyOf(warehouse, 'algae')).toBe(0);
  });

  it('емкость склада не съедается запертым резервом', () => {
    const warehouse = createWarehouse(10);
    deposit(warehouse, 'algae', 8);
    const trip = oneSlotTrip('algae', 9);

    loadSlot(trip, 0, warehouse, NOW, drop());
    buyoutSlot(trip, 0, warehouse, NOW, drop());

    // Рейс улетел, склад обязан быть пуст и готов принять новый урожай.
    expect(totalQty(warehouse)).toBe(0);
  });

  /**
   * Свойство. Три примера тут слабы: дефект зависит от ПОРЯДКА действий, а не
   * от чисел. Перебираем любую последовательность «догрузить / докупить» по
   * отсекам и проверяем два закона склада:
   *   1. reserved <= qty всегда;
   *   2. у улетевшего рейса резерва на складе не остается — резерв держит
   *      только незакрытый заказ, а закрытых заказов у шаттла не бывает.
   */
  it('И-склад: улетевший рейс не оставляет резерва ни при какой последовательности', () => {
    const actions = fc.array(fc.record({ slot: fc.nat({ max: 4 }), buy: fc.boolean() }), {
      minLength: 1,
      maxLength: 12,
    });

    fc.assert(
      fc.property(actions, fc.integer({ min: 1, max: 999 }), (ops, seed) => {
        const warehouse = createWarehouse(500);
        for (const id of POOL) deposit(warehouse, id, 4);

        const trip = generateTrip(genCtx({ warehouse, rng: makeRng(seed) }));

        for (const op of ops) {
          if (op.buy) buyoutSlot(trip, op.slot, warehouse, NOW, drop());
          else loadSlot(trip, op.slot, warehouse, NOW, drop());
        }

        for (const id of POOL) {
          expect(reservedOf(warehouse, id)).toBeLessThanOrEqual(qtyOf(warehouse, id));
          if (trip.state !== 'ORDER') expect(reservedOf(warehouse, id)).toBe(0);
        }
      }),
      { numRuns: 300 },
    );
  });
});

/* ------------------------------------------------------------------------ *
 * Д-2. FTUE-рейс вырождается по бедному складу.
 *
 * ТЗ шаттла 2.1: «Первый заказ — форсированный: ровно 3 отсека (нижняя граница
 * диапазона), все три позиции easy». Генератор (shuttle.ts:144-151) на первом
 * рейсе выбрасывает любой товар, которого нет на складе, вместо того чтобы
 * взять другой, — и отдает рейс из скольки получилось.
 * ------------------------------------------------------------------------ */
describe('Д-2: FTUE-рейс короче трех отсеков при бедном складе', () => {
  it('склад с одним товаром все равно дает ровно три отсека', () => {
    const warehouse = createWarehouse(500);
    deposit(warehouse, 'algae', 40); // водорослей навалом, остального нет

    const trip = generateTrip(genCtx({ warehouse, is_first_trip: true }));
    expect(trip.slots).toHaveLength(SLOT_COUNT_MIN);
  });

  /**
   * Худший случай — и он же софтлок. Пустой склад в момент выдачи первого
   * рейса дает рейс из НУЛЯ отсеков: `allSlotsLoaded` для пустого массива
   * всегда false, грузить нечего, состояние ORDER не меняется никогда.
   * Шаттл — единственный источник строй-модулей (И-1), поэтому мертвый первый
   * рейс — это мертвая стройка и мертвая прогрессия.
   */
  it('пустой склад не убивает первый рейс насмерть', () => {
    const trip = generateTrip(genCtx({ warehouse: createWarehouse(500), is_first_trip: true }));
    expect(trip.slots.length).toBeGreaterThanOrEqual(SLOT_COUNT_MIN);
    expect(allSlotsLoaded(trip)).toBe(false);
    // Ни одного отсека — значит рейс нельзя ни закрыть, ни отменить.
    expect(trip.slots.length).toBeGreaterThan(0);
  });
});

/* ------------------------------------------------------------------------ *
 * Д-3. Pity растет по модулям, которые никому не нужны.
 *
 * [[tz-common-systems-mars]] 2.3, `applyPityAndStockUpdates`:
 *     if item == rolledItem: pity_counter[item] = 0
 *     elif isNeededForActiveContext(item, ...): pity_counter[item] += 1
 * и таблица 2.4: «+1 на каждое событие, где предмет НУЖЕН и не выпал».
 * ТЗ шаттла 8.1 повторяет это словами: счетчик «перестает расти, пока модуль
 * не нужен активной стройке».
 *
 * droproller.ts:194 инкрементит безусловно. Следствие тихое: модуль, который
 * игроку был не нужен полгода, в момент старта стройки мгновенно приходит с
 * уже взведенным pity и удвоенным весом, отбирая его у того, чего реально ждут.
 * ------------------------------------------------------------------------ */
describe('Д-3: pity-счетчик растет вне активной потребности', () => {
  it('не нужный ни одной стройке модуль не копит pity', () => {
    // Нужен только герметик. Панель и каркас не нужны никому.
    const ctx = drop({ need: { sealant: 3 }, rng: () => 0.0 });
    const roll = rollArrival(1, ctx);

    for (const id of ALL_MODULE_IDS) {
      if ((ctx.need[id] ?? 0) > 0) continue;
      if (roll.modules.includes(id)) continue;
      expect(roll.next_pity[id] ?? 0).toBe(0);
    }
  });
});

/* ------------------------------------------------------------------------ *
 * Д-4. Роллер отдает индекс отсека, которого нет.
 *
 * droproller.ts:186 ставит `floor_forced_slot = modules.length - 1` без
 * проверки на пустой рейс. При slot_count = 0 наружу уезжает -1 — не null,
 * то есть «гарантия сработала на отсеке минус один», плюс на массив modules
 * вешается свойство "-1", которого нет ни в одном контракте.
 * ------------------------------------------------------------------------ */
describe('Д-4: floor guarantee на пустом рейсе', () => {
  it('пустой рейс не помечает несуществующий отсек', () => {
    // arrival_no=1 попадает в front-loaded удачу, гарантия разрешена всегда.
    const roll = rollArrival(0, drop({ need: { sealant: 2 }, arrival_no: 1 }));
    expect(roll.modules).toHaveLength(0);
    expect(roll.floor_forced_slot).toBeNull();
  });
});

/* ------------------------------------------------------------------------ *
 * Д-5. «Легко произвести» (И-8) считается без цепочки рецепта.
 *
 * И-8: позиция легкая, если покрыта складом ИЛИ производится <= 30 минут.
 * `isEasy` (shuttle.ts:95-98) смотрит только на `prod_time_sec` самого товара
 * и не спускается во входы, хотя обход цепочки в проекте уже написан
 * (`expandProductionChain`, rushcost.ts:54). Комбинезон формально проходит
 * порог (ровно 30 мин), фактически требует 2 ткани и 4 хлопка — два часа.
 * Генератор считает такой рейс полностью «легким» и не пересобирает состав.
 * ------------------------------------------------------------------------ */
describe('Д-5: порог «производимо за 30 минут» игнорирует входы рецепта', () => {
  function chainSeconds(good_id: GoodId, qty: number, seen = new Set<GoodId>()): number {
    if (seen.has(good_id)) return 0;
    seen.add(good_id);
    const good = GOODS[good_id];
    let total = good.prod_time_sec * qty;
    for (const input of good.inputs) {
      total += chainSeconds(input.good_id, qty * input.qty, seen);
    }
    return total;
  }

  it('рейс из цепочечных товаров на пустом складе не считается легким', () => {
    const warehouse = createWarehouse(500);
    const pool: GoodId[] = ['jumpsuit', 'fabric'];
    const trip = generateTrip(
      genCtx({ warehouse, available_goods: pool, is_first_trip: false }),
    );
    if (trip.slots.length === 0) return;

    const really_easy = trip.slots.filter(
      (s) => chainSeconds(s.good_id, 1) <= EASY_PRODUCE_MAX_MIN.shuttle * 60,
    );
    // И-8 требует >= 60% легких позиций. Здесь их фактически ноль.
    expect(really_easy.length / trip.slots.length).toBeGreaterThanOrEqual(0.6);
  });
});
