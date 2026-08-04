/**
 * Форма заказа генераторов как СВОЙСТВО, а не как три примера.
 *
 * Прогон 1 нашел софтлок первого рейса (Д-2) на пустом складе. Правку делал
 * другой исполнитель, и три примера в `shuttle.test.ts` ее не доказывают: они
 * кормят генератор одним и тем же пулом POOL и одним и тем же складом. Здесь
 * перебирается вся достижимая область входа — уровень, набор построенных
 * зданий, наполнение склада, FTUE-флаг, зерно — и проверяется то, из-за чего
 * тот софтлок был смертельным: рейс из нуля отсеков нельзя ни закрыть, ни
 * отменить, а новый выдается только из кулдауна.
 *
 * Тест зеленый и обязан таким остаться. Красный означает возврат Д-2.
 */

import fc from 'fast-check';
import { describe, expect, it } from 'vitest';
import { makeRng } from '../../sim/rng';
import {
  COVERAGE_MIN,
  EASY_PRODUCE_MAX_MIN,
  SLOT_COUNT_MAX,
  SLOT_COUNT_MIN,
} from '../config/economy';
import { GOOD_BASE_QTY, GOODS } from '../config/goods';
import { availableGoodsFor, generateOrder, positionsCountFor } from '../drone';
import { allSlotsLoaded, generateTrip } from '../shuttle';
import type { GoodId } from '../types';
import { availableOf, createWarehouse, deposit, type WarehouseState } from '../warehouse';

const BUILDINGS = ['food_module', 'mining_site', 'atmospheric_module', 'textile_module'];

/** Склад с одинаковым запасом каждого доступного товара. Ноль — пустой склад. */
function stocked(pool: GoodId[], per_good: number): WarehouseState {
  const w = createWarehouse(500);
  if (per_good > 0) for (const id of pool) deposit(w, id, per_good);
  return w;
}

const gen_input = fc.record({
  level: fc.integer({ min: 5, max: 21 }),
  building_mask: fc.integer({ min: 0, max: 15 }),
  per_good: fc.integer({ min: 0, max: 12 }),
  seed: fc.integer({ min: 1, max: 100_000 }),
  first_trip: fc.boolean(),
});

describe('Шаттл: форма рейса на всей достижимой области входа', () => {
  it('рейс никогда не выходит пустым — иначе прогрессия умирает вместе с ним', () => {
    fc.assert(
      fc.property(gen_input, (input) => {
        const buildings = new Set(BUILDINGS.filter((_, i) => (input.building_mask >> i) & 1));
        const pool = availableGoodsFor(input.level, buildings);
        const warehouse = stocked(pool, input.per_good);

        const trip = generateTrip({
          level: input.level,
          warehouse,
          available_goods: pool,
          previous: null,
          is_first_trip: input.first_trip,
          arrival_no: 1,
          rng: makeRng(input.seed),
        });

        expect(trip.slots.length).toBeGreaterThan(0);
        // Пустой рейс мгновенно «загружен» и потому неотличим от готового.
        expect(allSlotsLoaded(trip)).toBe(false);
        for (const slot of trip.slots) {
          expect(slot.qty_required).toBeGreaterThan(0);
          expect(slot.qty_filled).toBe(0);
        }
      }),
      { numRuns: 400 },
    );
  });

  it('форма не короче трех отсеков, пока пул дает трех кандидатов', () => {
    fc.assert(
      fc.property(gen_input, (input) => {
        const buildings = new Set(BUILDINGS.filter((_, i) => (input.building_mask >> i) & 1));
        const pool = availableGoodsFor(input.level, buildings);
        const warehouse = stocked(pool, input.per_good);

        const trip = generateTrip({
          level: input.level,
          warehouse,
          available_goods: pool,
          previous: null,
          is_first_trip: input.first_trip,
          arrival_no: 1,
          rng: makeRng(input.seed),
        });

        // Канон 1.6 разрешает урезать форму только когда пул меньше формы.
        expect(trip.slots.length).toBeGreaterThanOrEqual(Math.min(SLOT_COUNT_MIN, pool.length));
        expect(trip.slots.length).toBeLessThanOrEqual(SLOT_COUNT_MAX);
      }),
      { numRuns: 400 },
    );
  });

  it('FTUE (ТЗ 2.1): ровно три отсека и КАЖДЫЙ easy по определению И-8', () => {
    fc.assert(
      fc.property(gen_input, (input) => {
        const buildings = new Set(BUILDINGS.filter((_, i) => (input.building_mask >> i) & 1));
        const pool = availableGoodsFor(input.level, buildings);
        const warehouse = stocked(pool, input.per_good);

        const trip = generateTrip({
          level: input.level,
          warehouse,
          available_goods: pool,
          previous: null,
          is_first_trip: true,
          arrival_no: 1,
          rng: makeRng(input.seed),
        });

        expect(trip.slots).toHaveLength(SLOT_COUNT_MIN);

        // COVERAGE_MIN=1.0 и MAX_DEFICIT_SLOTS=0 на этот заказ: ни одной
        // позиции, которую нельзя ни взять со склада, ни быстро вырастить.
        for (const slot of trip.slots) {
          const covered = availableOf(warehouse, slot.good_id) >= slot.qty_required;
          const quick = GOODS[slot.good_id].prod_time_sec <= EASY_PRODUCE_MAX_MIN.shuttle * 60;
          expect(covered || quick).toBe(true);
        }
        expect(COVERAGE_MIN.shuttle).toBeLessThanOrEqual(1);
      }),
      { numRuns: 400 },
    );
  });

  it('крайний случай 1.6: пустой пул дает ровно один отсек минимального объема', () => {
    for (let level = 1; level <= 21; level++) {
      const trip = generateTrip({
        level,
        warehouse: createWarehouse(500),
        available_goods: [],
        previous: null,
        is_first_trip: level === 5,
        arrival_no: 1,
        rng: makeRng(level + 1),
      });

      expect(trip.slots).toHaveLength(1);
      const slot = trip.slots[0]!;
      expect(slot.qty_required).toBe(GOOD_BASE_QTY[slot.good_id].min);
      expect(slot.filled_by).toBeNull();
      expect(slot.reward).toBeNull();
      expect(slot.collected).toBe(false);
    }
  });
});

describe('Дрон: форма заказа на всей достижимой области входа', () => {
  it('заказ не короче минимума весовой таблицы своего уровня', () => {
    fc.assert(
      fc.property(
        fc.record({
          level: fc.integer({ min: 2, max: 21 }),
          building_mask: fc.integer({ min: 0, max: 15 }),
          per_good: fc.integer({ min: 0, max: 12 }),
          seed: fc.integer({ min: 1, max: 100_000 }),
        }),
        (input) => {
          const buildings = new Set(BUILDINGS.filter((_, i) => (input.building_mask >> i) & 1));
          const pool = availableGoodsFor(input.level, buildings);
          const order = generateOrder(0, {
            level: input.level,
            warehouse: stocked(pool, input.per_good),
            available_goods: pool,
            board: [],
            rng: makeRng(input.seed),
          });

          const table_min = Math.min(
            ...Array.from({ length: 21 }, (_, i) => positionsCountFor(input.level, i / 20)),
          );
          expect(order.positions.length).toBeGreaterThanOrEqual(
            Math.min(table_min, pool.length),
          );
        },
      ),
      { numRuns: 400 },
    );
  });
});
