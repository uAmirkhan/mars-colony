/**
 * Экономические инварианты стора как СВОЙСТВО произвольной последовательности
 * действий, а не как набор примеров.
 *
 * Мотив прямой: дефект Д-1 прошлого прогона («докупка поверх частичной
 * погрузки запирает резерв навсегда») не ловился тремя примерами и нашелся
 * только свойством. Механики с деньгами, резервом и лимитом — ровно тот класс,
 * где пример проверяет свою ветку, а ломается стык двух веток.
 *
 * Здесь проверяется то, что обязано держаться ПОСЛЕ ЛЮБОГО действия:
 *   кредиты и изотопы не уходят в минус (И-15 держит пол посева);
 *   склад не превышает вместимость ни через резерв, ни через сбор;
 *   reserved никогда не больше qty, и ни то, ни другое не отрицательно;
 *   склад строй-модулей не превышает текущий потолок (каркас 6: лимит
 *     модулей апгрейдится тем же зданием, что и товарный);
 *   вместимость склада не перепрыгивает потолок MVP.
 *
 * Генератор дрона и роллер дропа берут `Math.random` из стора напрямую, поэтому
 * на время прогона он подменяется детерминированным — иначе контрпример
 * fast-check не воспроизводится.
 *
 * Отдельный тест внизу держит сам прогон честным: свойство, которое ни разу не
 * довело рейс до прибытия и ни разу не отправило заказ, зеленое по построению
 * и ничего не проверяет. Список пройденных вех — защита от такой пустоты.
 */

import fc from 'fast-check';
import { afterEach, describe, expect, it } from 'vitest';
import { CREDITS_START, WAREHOUSE_MAX_CAPACITY } from '../../domain/config/economy';
import { ALL_BUILD_KINDS, type BuildKind } from '../../domain/config/modules';
import { createConstruction, moduleCapacity, moduleTotal } from '../../domain/construction';
import { createField } from '../../domain/production';
import type { GoodId } from '../../domain/types';
import { createWarehouse, deposit, totalQty } from '../../domain/warehouse';
import { useGame } from '../gameStore';

const NOW = 1_000_000;
const GOODS_POOL: GoodId[] = ['algae', 'soy', 'mushrooms', 'tomatoes', 'cotton'];

const original_random = Math.random;
afterEach(() => {
  Math.random = original_random;
});

/** Вехи, пройденные за весь прогон свойства. Пустой набор = тест ничего не проверил. */
const milestones = new Set<string>();

/** Тот же генератор, что у симулятора: mulberry32. Нужен ради воспроизводимости. */
function seededRandom(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

type Action =
  | { kind: 'tick'; dt: number }
  | { kind: 'plant'; idx: number; good: number }
  | { kind: 'collect_field'; idx: number }
  | { kind: 'sell'; good: number; qty: number }
  | { kind: 'enqueue'; idx: number; good: number }
  | { kind: 'collect_factory'; idx: number }
  | { kind: 'load_order'; slot: number; position: number }
  | { kind: 'send_order'; slot: number }
  | { kind: 'discard_order'; slot: number }
  | { kind: 'refresh_slot'; slot: number }
  | { kind: 'load_shuttle'; idx: number }
  | { kind: 'load_all_shuttle' }
  | { kind: 'load_all_order'; slot: number }
  | { kind: 'buyout_shuttle'; idx: number }
  | { kind: 'skip_shuttle' }
  | { kind: 'collect_containers' }
  | { kind: 'start_build'; kind_idx: number }
  | { kind: 'speedup_build'; kind_idx: number }
  | { kind: 'speedup_field'; idx: number }
  | { kind: 'buy_building'; idx: number };

const idx = fc.integer({ min: 0, max: 6 });
const good_idx = fc.integer({ min: 0, max: GOODS_POOL.length - 1 });

const action: fc.Arbitrary<Action> = fc.oneof(
  fc.record({ kind: fc.constant('tick' as const), dt: fc.integer({ min: 0, max: 9000 }) }),
  fc.record({ kind: fc.constant('plant' as const), idx, good: good_idx }),
  fc.record({ kind: fc.constant('collect_field' as const), idx }),
  fc.record({
    kind: fc.constant('sell' as const),
    good: good_idx,
    qty: fc.integer({ min: 0, max: 20 }),
  }),
  fc.record({ kind: fc.constant('enqueue' as const), idx, good: good_idx }),
  fc.record({ kind: fc.constant('collect_factory' as const), idx }),
  fc.record({ kind: fc.constant('load_order' as const), slot: idx, position: idx }),
  fc.record({ kind: fc.constant('send_order' as const), slot: idx }),
  fc.record({ kind: fc.constant('discard_order' as const), slot: idx }),
  fc.record({ kind: fc.constant('refresh_slot' as const), slot: idx }),
  fc.record({ kind: fc.constant('load_shuttle' as const), idx }),
  // Макро-действия: игрок кликает подряд все отсеки рейса и все позиции
  // заказа. Без них случайная последовательность почти никогда не закрывает
  // последний отсек, и свойство не доходит ни до отправки, ни до прибытия.
  fc.record({ kind: fc.constant('load_all_shuttle' as const) }),
  fc.record({ kind: fc.constant('load_all_order' as const), slot: idx }),
  fc.record({ kind: fc.constant('buyout_shuttle' as const), idx }),
  fc.record({ kind: fc.constant('skip_shuttle' as const) }),
  fc.record({ kind: fc.constant('collect_containers' as const) }),
  fc.record({
    kind: fc.constant('start_build' as const),
    kind_idx: fc.integer({ min: 0, max: 1 }),
  }),
  fc.record({
    kind: fc.constant('speedup_build' as const),
    kind_idx: fc.integer({ min: 0, max: 1 }),
  }),
  fc.record({ kind: fc.constant('speedup_field' as const), idx }),
  fc.record({
    kind: fc.constant('buy_building' as const),
    idx: fc.integer({ min: 0, max: 3 }),
  }),
);

const BUILDINGS = [
  'food_module',
  'mining_site',
  'atmospheric_module',
  'textile_module',
] as const;

function reset(seed: number, isotopes: number, stock: number) {
  const warehouse = createWarehouse(50);
  for (const id of GOODS_POOL) deposit(warehouse, id, stock);

  useGame.setState({
    now: NOW,
    level: 9,
    xp_into_level: 0,
    credits: CREDITS_START,
    isotopes,
    warehouse,
    fields: [createField(0), createField(1), createField(2)],
    factory_slots: [],
    buildings: [],
    toasts: [],
    orders: [],
    shuttle: null,
    shuttle_arrivals: 0,
    drop_pity: {},
    drop_without_needed: 0,
    drop_last_floor: 0,
    construction: createConstruction(),
  });
  Math.random = seededRandom(seed);
  useGame.getState().tick(NOW);
}

function apply(a: Action, now: { value: number }) {
  const s = useGame.getState();
  switch (a.kind) {
    case 'tick':
      now.value += a.dt;
      s.tick(now.value);
      break;
    case 'plant':
      s.plant(a.idx, GOODS_POOL[a.good]!);
      break;
    case 'collect_field':
      s.collectField(a.idx);
      break;
    case 'sell':
      s.sell(GOODS_POOL[a.good]!, a.qty);
      break;
    case 'enqueue':
      s.enqueue(a.idx, GOODS_POOL[a.good]!);
      break;
    case 'collect_factory':
      s.collectFactory(a.idx);
      break;
    case 'load_order':
      s.loadOrderPosition(a.slot, a.position);
      break;
    case 'send_order':
      s.sendOrderAt(a.slot);
      break;
    case 'discard_order':
      s.discardOrderAt(a.slot);
      break;
    case 'refresh_slot':
      s.refreshSlotNow(a.slot);
      break;
    case 'load_shuttle':
      s.loadShuttleSlot(a.idx);
      break;
    case 'load_all_shuttle': {
      const count = s.shuttle?.slots.length ?? 0;
      for (let i = 0; i < count; i++) useGame.getState().loadShuttleSlot(i);
      break;
    }
    case 'load_all_order': {
      const count = s.orders[a.slot]?.positions.length ?? 0;
      for (let i = 0; i < count; i++) useGame.getState().loadOrderPosition(a.slot, i);
      break;
    }
    case 'buyout_shuttle':
      s.buyoutShuttleSlot(a.idx);
      break;
    case 'skip_shuttle':
      s.skipShuttle();
      break;
    case 'collect_containers':
      s.collectAllContainers();
      break;
    case 'start_build':
      s.startConstruction(ALL_BUILD_KINDS[a.kind_idx] as BuildKind);
      break;
    case 'speedup_build':
      s.speedupConstruction(ALL_BUILD_KINDS[a.kind_idx] as BuildKind);
      break;
    case 'speedup_field':
      s.speedupField(a.idx);
      break;
    case 'buy_building':
      s.buyBuilding(BUILDINGS[a.idx]!);
      break;
  }
}

function noteMilestones() {
  const s = useGame.getState();
  if (s.shuttle) milestones.add(`shuttle:${s.shuttle.state}`);
  if (moduleTotal(s.construction.stock) > 0) milestones.add('modules_collected');
  for (const build of s.construction.builds) milestones.add(`build:${build.state}`);
  for (const order of s.orders) milestones.add(`order:${order.state}`);
  for (const field of s.fields) milestones.add(`field:${field.state}`);
  if (s.credits !== CREDITS_START) milestones.add('credits_moved');
  for (const cell of Object.values(s.warehouse.cells)) {
    if (cell.reserved > 0) milestones.add('reserved_held');
  }
}

function checkInvariants(step: string) {
  const s = useGame.getState();

  expect(s.credits, `${step}: кредиты ушли в минус`).toBeGreaterThanOrEqual(0);
  expect(s.isotopes, `${step}: изотопы ушли в минус`).toBeGreaterThanOrEqual(0);

  for (const [good_id, cell] of Object.entries(s.warehouse.cells)) {
    expect(cell.qty, `${step}: ${good_id}.qty отрицателен`).toBeGreaterThanOrEqual(0);
    expect(cell.reserved, `${step}: ${good_id}.reserved отрицателен`).toBeGreaterThanOrEqual(0);
    expect(cell.reserved, `${step}: ${good_id} reserved > qty`).toBeLessThanOrEqual(cell.qty);
  }

  expect(totalQty(s.warehouse), `${step}: склад переполнен`).toBeLessThanOrEqual(
    s.warehouse.capacity,
  );
  expect(s.warehouse.capacity, `${step}: вместимость выше потолка MVP`).toBeLessThanOrEqual(
    WAREHOUSE_MAX_CAPACITY,
  );
  // Лимит модулей не константа: он растет тем же зданием, что и товарный
  // (каркас 6), поэтому сравниваем с текущим потолком, а не со стартовым.
  expect(
    moduleTotal(s.construction.stock),
    `${step}: склад модулей выше лимита`,
  ).toBeLessThanOrEqual(moduleCapacity(s.construction));
}

describe('Экономика стора: инварианты на произвольной последовательности действий', () => {
  it('ни одно действие не выводит состояние за границы инвариантов', () => {
    fc.assert(
      fc.property(
        fc.array(action, { minLength: 1, maxLength: 90 }),
        fc.integer({ min: 1, max: 99_999 }),
        fc.integer({ min: 0, max: 5000 }),
        fc.integer({ min: 0, max: 8 }),
        (actions, seed, isotopes, stock) => {
          reset(seed, isotopes, stock);
          const now = { value: NOW };

          actions.forEach((a, i) => {
            apply(a, now);
            noteMilestones();
            checkInvariants(`шаг ${i} (${a.kind})`);
          });
        },
      ),
      { numRuns: 400 },
    );
  });

  /**
   * Направленный прогон полного цикла со случайными параметрами. Случайная
   * последовательность до прибытия и сбора контейнеров доходит редко: чтобы
   * рейс ушел, нужно закрыть ВСЕ отсеки подряд, а потом еще и попасть тиком за
   * таймер. Здесь порядок задан, случайны наполнение склада, изотопы, зерно и
   * длина шагов времени — и инварианты проверяются на каждом шаге.
   */
  it('полный цикл: погрузка, рейс, прибытие, сбор, стройка — инварианты держатся', () => {
    fc.assert(
      fc.property(
        fc.integer({ min: 1, max: 99_999 }),
        fc.integer({ min: 0, max: 5000 }),
        fc.integer({ min: 0, max: 9 }),
        fc.integer({ min: 600, max: 20_000 }),
        fc.integer({ min: 1, max: 6 }),
        (seed, isotopes, stock, dt, laps) => {
          reset(seed, isotopes, stock);
          const now = { value: NOW };

          for (let lap = 0; lap < laps; lap++) {
            for (const a of [
              { kind: 'plant', idx: 0, good: 0 },
              { kind: 'load_all_shuttle' },
              { kind: 'buyout_shuttle', idx: 0 },
              { kind: 'load_all_shuttle' },
              { kind: 'tick', dt },
              { kind: 'collect_field', idx: 0 },
              { kind: 'collect_containers' },
              { kind: 'start_build', kind_idx: 0 },
              { kind: 'load_all_order', slot: 0 },
              { kind: 'send_order', slot: 0 },
              { kind: 'tick', dt },
              { kind: 'collect_containers' },
              { kind: 'start_build', kind_idx: 1 },
            ] as Action[]) {
              apply(a, now);
              noteMilestones();
              checkInvariants(`круг ${lap}, ${a.kind}`);
            }
          }
        },
      ),
      { numRuns: 120 },
    );
  });

  it('прогон свойства действительно дошел до значимых состояний', () => {
    // Без этой проверки предыдущий тест зеленеет и на игре, где ничего
    // не происходит: инварианты нетронутого состояния держатся сами собой.
    for (const required of [
      'shuttle:ORDER',
      'shuttle:IN_TRANSIT',
      'shuttle:ARRIVED',
      'modules_collected',
      'order:ready',
      'reserved_held',
      'credits_moved',
      'field:GROWING',
    ]) {
      expect(milestones, `свойство ни разу не дошло до «${required}»`).toContain(required);
    }
  });
});
