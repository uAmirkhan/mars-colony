/**
 * Софтлок шаттла на уровне стора: тот же дефект Д-2, но доказанный не вызовом
 * доменной функции, а игровым сценарием — «игрок дошел до пятого уровня с
 * пустым складом».
 *
 * Домашние тесты домена этого не ловят, потому что все они кормят генератор
 * заранее набитым складом. Стор — единственное место, где видно, что рейс
 * выдается автоматически по тику и второй раз уже не выдается никогда.
 */

import { beforeEach, describe, expect, it } from 'vitest';
import { CREDITS_START, SLOT_COUNT_MIN } from '../../domain/config/economy';
import { createConstruction } from '../../domain/construction';
import { createWarehouseAvg } from '../../domain/droproller';
import { createField } from '../../domain/production';
import { createWarehouse } from '../../domain/warehouse';
import { useGame } from '../gameStore';

const NOW = 1_000_000;
const s = () => useGame.getState();

beforeEach(() => {
  useGame.setState({
    now: NOW,
    level: 5, // шаттл открывается ровно здесь
    xp_into_level: 0,
    credits: CREDITS_START,
    isotopes: 0,
    warehouse: createWarehouse(), // склад пуст: все продано и роздано дрону
    fields: [createField(0), createField(1)],
    factory_slots: [],
    buildings: [],
    toasts: [],
    orders: [],
    shuttle: null,
    shuttle_arrivals: 0,
    drop_pity: {},
    drop_floor_guarantee: {},
    warehouse_avg: createWarehouseAvg({}, NOW),
    construction: createConstruction(),
    deficit_locks: {},
  });
});

describe('Шаттл: первый рейс с пустым складом', () => {
  it('первый рейс выдается с отсеками, а не пустым', () => {
    s().tick(NOW);
    expect(s().shuttle).not.toBeNull();
    expect(s().shuttle?.slots.length ?? 0).toBeGreaterThanOrEqual(SLOT_COUNT_MIN);
  });

  it('рейс не залипает в ЗАКАЗЕ навсегда', () => {
    s().tick(NOW);
    // Сутки игрового времени: урожай собран, склад полон, а рейс не меняется —
    // грузить в него нечего, отменить его нельзя, новый не выдается.
    s().tick(NOW + 24 * 3600);

    const trip = s().shuttle;
    expect(trip?.state).toBe('ORDER');
    expect(trip?.slots.length ?? 0).toBeGreaterThan(0);
  });
});
