/**
 * Дефекты стройки, прогон 1. Как и в `defects-run1.test.ts`, тесты здесь
 * ничего не чинят — они фиксируют расхождение кода с каркасом.
 */

import { describe, expect, it } from 'vitest';
import { MODULE_STOCK_CAP } from '../config/economy';
import { CONSTRUCTION_RECIPE } from '../config/modules';
import {
  addModule,
  createConstruction,
  moduleSpaceLeft,
  refreshBuilds,
  startBuild,
} from '../construction';
import { createWarehouse, type WarehouseState } from '../warehouse';

const NOW = 1_000_000;

/* ------------------------------------------------------------------------ *
 * Д-6. Расширение склада не двигает лимит строй-модулей.
 *
 * Каркас, раздел 6: «Склад: старт 50 единиц суммарно; апгрейд +10 [...].
 * Строй-модули хранятся отдельным лимитом 100 и АПГРЕЙДЯТСЯ ТЕМ ЖЕ ЗДАНИЕМ.»
 *
 * `applyBuildEffect` (construction.ts:129-131) поднимает только товарную
 * емкость. `moduleSpaceLeft` (construction.ts:56-58) читает константу
 * `MODULE_STOCK_CAP` напрямую, и поднять ее нечем — механики апгрейда лимита
 * модулей в коде нет вовсе, как нет и теста на нее.
 *
 * Цена дефекта не косметическая: при забитом складе модулей `collectContainer`
 * отказывает, контейнеры прибывшего рейса зависают несобранными, кулдаун не
 * стартует, новый заказ не выдается — шаттл встает целиком.
 * ------------------------------------------------------------------------ */
describe('Д-6: лимит склада модулей не апгрейдится', () => {
  function buildWarehouseUpgrade(warehouse: WarehouseState) {
    const state = createConstruction();
    const build = state.builds.find((b) => b.kind === 'warehouse_upgrade');
    if (!build) throw new Error('нет рецепта расширения склада');
    build.state = 'AVAILABLE';

    // Кладем ровно рецепт, чтобы старт прошел.
    for (const [id, qty] of Object.entries(CONSTRUCTION_RECIPE.warehouse_upgrade.recipe)) {
      state.stock[id as keyof typeof state.stock] = qty;
    }
    expect(startBuild(state, 'warehouse_upgrade', NOW).ok).toBe(true);

    const done = NOW + CONSTRUCTION_RECIPE.warehouse_upgrade.build_time_min * 60;
    state.builds = refreshBuilds(state, 21, done, warehouse);
    return state;
  }

  it('после расширения склада емкость модулей растет вместе с товарной', () => {
    const warehouse = createWarehouse();
    const capacity_before = warehouse.capacity;

    const state = buildWarehouseUpgrade(warehouse);
    expect(warehouse.capacity).toBeGreaterThan(capacity_before); // товарная выросла

    // Забиваем склад модулей под старый потолок и проверяем, что он подвинулся.
    state.stock = { panel: MODULE_STOCK_CAP };
    expect(moduleSpaceLeft(state.stock)).toBeGreaterThan(0);
    expect(addModule(state.stock, 'frame')).toBe(true);
  });
});
