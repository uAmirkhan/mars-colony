/**
 * Дефекты стройки, прогон 1. Как и в `defects-run1.test.ts`, тесты здесь
 * ничего не чинят — они фиксируют расхождение кода с каркасом.
 */

import { describe, expect, it } from 'vitest';
import { MODULE_STOCK_CAP, WAREHOUSE_UPGRADE_STEP } from '../config/economy';
import { CONSTRUCTION_RECIPE } from '../config/modules';
import {
  addModule,
  createConstruction,
  moduleCapacity,
  moduleSpaceLeft,
  refreshBuilds,
  startBuild,
} from '../construction';
import { createWarehouse, type WarehouseState } from '../warehouse';

const NOW = 1_000_000;

/* ------------------------------------------------------------------------ *
 * Д-6. ЗАКРЫТ. Блок оставлен сторожем.
 *
 * Было: расширение склада не двигало лимит строй-модулей. `applyBuildEffect`
 * поднимал только товарную емкость, а `moduleSpaceLeft` читал константу
 * `MODULE_STOCK_CAP` напрямую — механики апгрейда модульного лимита в коде не
 * было вовсе, как не было и теста на нее.
 *
 * Каркас, раздел 6: «Склад: старт 50 единиц суммарно; апгрейд +10 [...].
 * Строй-модули хранятся отдельным лимитом 100 и АПГРЕЙДЯТСЯ ТЕМ ЖЕ ЗДАНИЕМ.»
 *
 * Стало: `moduleCapacity(state)` считает потолок от тира того же здания —
 * `MODULE_STOCK_CAP + tier x WAREHOUSE_UPGRADE_STEP`. Разбор расхождения с
 * [[tz-production-mars]] (три места пишут «фиксировано») — в докстринге
 * `construction.ts` и в реестре [[spec-prototype-build]] 8.14.
 *
 * Цена дефекта была не косметическая: при забитом складе модулей
 * `collectContainer` отказывает, контейнеры прибывшего рейса зависают
 * несобранными, кулдаун не стартует, новый заказ не выдается — шаттл встает
 * целиком.
 * ------------------------------------------------------------------------ */
describe('Д-6 (закрыт): лимит склада модулей апгрейдится тем же зданием', () => {
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

    // Тем же зданием и тем же шагом: каркас не дает модульному лимиту ни
    // отдельного шага, ни отдельного потолка, а «то же здание» читается как
    // «тот же тир».
    expect(moduleCapacity(state)).toBe(MODULE_STOCK_CAP + WAREHOUSE_UPGRADE_STEP);

    // Забиваем склад модулей под старый потолок и проверяем, что он подвинулся.
    state.stock = { panel: MODULE_STOCK_CAP };
    expect(moduleSpaceLeft(state)).toBeGreaterThan(0);
    expect(addModule(state, 'frame')).toBe(true);
  });
});
