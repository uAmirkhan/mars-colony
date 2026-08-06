/**
 * Н-15: бейджи хаба внимания — доказательство перебором состояний, а не
 * взглядом на экран.
 *
 * `topHubBadge` — та же чистая функция, которую вызывает `useHubBadge` в
 * `App.tsx`; тест дергает ее напрямую и не рендерит DOM.
 */

import { describe, expect, it } from 'vitest';
import { CONSTRUCTION_RECIPE } from '../../domain/config/modules';
import { createConstruction } from '../../domain/construction';
import type { OrderSlot } from '../../domain/drone';
import type { FactorySlot } from '../../domain/production';
import type { ShuttleTrip } from '../../domain/shuttle';
import { createWarehouse, deposit } from '../../domain/warehouse';
import { type BadgeHub, type HubBadgeState, topHubBadge } from '../hub-badge';
import { WAREHOUSE_WARN_RATIO } from '../kit';

function baseState(): HubBadgeState {
  return {
    shuttle: null,
    orders: [],
    construction: createConstruction(),
    factory_slots: [],
    warehouse: createWarehouse(),
  };
}

function arrivedTrip(): ShuttleTrip {
  return {
    state: 'ARRIVED',
    slots: [],
    trip_min: 60,
    departed_at: 0,
    arrives_at: 0,
    cooldown_until: 0,
    is_first_trip: false,
    arrival_no: 1,
  };
}

function readyOrder(): OrderSlot {
  return {
    idx: 0,
    state: 'ready',
    npc_name: 'Тест',
    positions: [],
    credits_reward: 0,
    xp_reward: 0,
    refresh_at: 0,
  };
}

/** Первая стройка фикстуры — без `!`, линтер запрещает non-null assertion. */
function firstBuild(state: ReturnType<typeof createConstruction>) {
  const [build] = state.builds;
  if (!build) throw new Error('в фикстуре нет ни одной стройки');
  return build;
}

/** Стройка с полностью закрытым чек-листом: `AVAILABLE`, модулей хватает. */
function readyConstruction() {
  const state = createConstruction();
  const build = firstBuild(state);
  const recipe = CONSTRUCTION_RECIPE[build.kind].recipe;
  state.stock = { ...recipe };
  build.state = 'AVAILABLE';
  return state;
}

function readyFactorySlot(): FactorySlot {
  return {
    idx: 0,
    building_type: 'food_module',
    state: 'READY',
    good_id: 'protein_bar',
    queued_at: 0,
    ends_at: 0,
  };
}

/** Ровно на границе — тот же порог, что красит капасити-бар (ТЗ 9.2). */
function nearFullWarehouse() {
  const w = createWarehouse();
  deposit(w, 'algae', Math.ceil(w.capacity * WAREHOUSE_WARN_RATIO));
  return w;
}

describe('topHubBadge', () => {
  it('ничего не ждет игрока — индикатор не горит', () => {
    expect(topHubBadge(baseState())).toBeNull();
  });

  it('шаттл прибыл (ARRIVED) — горит бейдж шаттла', () => {
    const s = baseState();
    s.shuttle = arrivedTrip();
    expect(topHubBadge(s)).toBe('shuttle');
  });

  it('рейс еще в пути — бейджа шаттла нет', () => {
    const s = baseState();
    s.shuttle = { ...arrivedTrip(), state: 'IN_TRANSIT' };
    expect(topHubBadge(s)).toBeNull();
  });

  it('заказ дрона готов (ready) — горит бейдж дрона', () => {
    const s = baseState();
    s.orders = [readyOrder()];
    expect(topHubBadge(s)).toBe('drone');
  });

  it('заказ дрона в процессе — бейджа дрона нет', () => {
    const s = baseState();
    s.orders = [{ ...readyOrder(), state: 'in_progress' }];
    expect(topHubBadge(s)).toBeNull();
  });

  it('стройка полностью укомплектована и ждет тапа «Строить» — горит бейдж стройки', () => {
    const s = baseState();
    s.construction = readyConstruction();
    expect(topHubBadge(s)).toBe('construction');
  });

  it('стройка доступна, но модулей не хватает — бейджа стройки нет', () => {
    const s = baseState();
    const c = createConstruction();
    firstBuild(c).state = 'AVAILABLE';
    s.construction = c;
    expect(topHubBadge(s)).toBeNull();
  });

  it('стройка уже идет (IN_PROGRESS) — бейджа стройки нет: тут нечего тапать', () => {
    const s = baseState();
    const c = readyConstruction();
    firstBuild(c).state = 'IN_PROGRESS';
    s.construction = c;
    expect(topHubBadge(s)).toBeNull();
  });

  it('слот фабрики готов к сбору (READY) — горит бейдж фабрики', () => {
    const s = baseState();
    s.factory_slots = [readyFactorySlot()];
    expect(topHubBadge(s)).toBe('factory');
  });

  it('слот фабрики еще производит — бейджа фабрики нет', () => {
    const s = baseState();
    s.factory_slots = [{ ...readyFactorySlot(), state: 'PRODUCING' }];
    expect(topHubBadge(s)).toBeNull();
  });

  it('склад заполнен на пороге (WAREHOUSE_WARN_RATIO) и больше — горит бейдж склада', () => {
    const s = baseState();
    s.warehouse = nearFullWarehouse();
    expect(topHubBadge(s)).toBe('warehouse');
  });

  it('склад заполнен чуть ниже порога — бейджа склада еще нет', () => {
    const s = baseState();
    const w = createWarehouse();
    deposit(w, 'algae', Math.floor(w.capacity * WAREHOUSE_WARN_RATIO) - 1);
    s.warehouse = w;
    expect(topHubBadge(s)).toBeNull();
  });

  /**
   * Каркас раздел 13: «Одновременно горит не более одного индикатора в
   * хабе». Перебор всех 32 комбинаций пяти булевых флагов — доказательство,
   * а не мысленный прогон: для каждой комбинации функция обязана вернуть
   * ровно самый приоритетный из активных, либо `null`, если активных нет.
   */
  it('перебор состояний: всегда либо один бейдж по приоритету, либо ни одного', () => {
    const priority: BadgeHub[] = ['shuttle', 'drone', 'construction', 'factory', 'warehouse'];

    for (let mask = 0; mask < 1 << priority.length; mask++) {
      const active = new Set(priority.filter((_, i) => (mask & (1 << i)) !== 0));
      const s = baseState();
      if (active.has('shuttle')) s.shuttle = arrivedTrip();
      if (active.has('drone')) s.orders = [readyOrder()];
      if (active.has('construction')) s.construction = readyConstruction();
      if (active.has('factory')) s.factory_slots = [readyFactorySlot()];
      if (active.has('warehouse')) s.warehouse = nearFullWarehouse();

      const expected = priority.find((hub) => active.has(hub)) ?? null;
      expect(topHubBadge(s), `маска ${mask.toString(2)}`).toBe(expected);
    }
  });
});
