/**
 * Стройка класса Б: склад модулей, рецепты, транзакционный старт, эффект.
 *
 * Главная опасность здесь — не падение, а частичное списание: рецепт из трех
 * позиций, две списались, третьей не хватило. Модули не возвращаются ниоткуда,
 * кроме шаттла, поэтому такая потеря стоит игроку часов ожидания.
 */

import { describe, expect, it } from 'vitest';
import {
  MODULE_STOCK_CAP,
  WAREHOUSE_MAX_CAPACITY,
  WAREHOUSE_START_CAPACITY,
  WAREHOUSE_UPGRADE_STEP,
} from '../config/economy';
import { CONSTRUCTION_RECIPE } from '../config/modules';
import {
  activeLinesUsed,
  activeNeed,
  addModule,
  type ConstructionState,
  createConstruction,
  createConstruction as fresh,
  missingFor,
  moduleSpaceLeft,
  moduleTotal,
  refreshBuilds,
  startBuild,
} from '../construction';
import type { ModuleCounts } from '../droproller';
import type { ModuleId } from '../types';
import { createWarehouse } from '../warehouse';

/** Досыпать рецепт на склад. Отдельная функция — иначе каждый тест кастует типы. */
function fill(stock: ModuleCounts, recipe: Partial<Record<ModuleId, number>>): void {
  for (const [id, qty] of Object.entries(recipe) as Array<[ModuleId, number]>) {
    stock[id] = (stock[id] ?? 0) + qty;
  }
}

const NOW = 1_000_000;
const WAREHOUSE_RECIPE = CONSTRUCTION_RECIPE.warehouse_upgrade;

/** Стройка со складом, точно покрывающим рецепт расширения склада. */
function readyToBuild(): ConstructionState {
  const state = createConstruction();
  fill(state.stock, WAREHOUSE_RECIPE.recipe);
  const build = state.builds.find((b) => b.kind === 'warehouse_upgrade')!;
  build.state = 'AVAILABLE';
  return state;
}

describe('Склад модулей', () => {
  it('стартует пустым и с полным свободным местом', () => {
    const state = fresh();
    expect(moduleTotal(state.stock)).toBe(0);
    expect(moduleSpaceLeft(state.stock)).toBe(MODULE_STOCK_CAP);
  });

  it('лимит общий на все типы, а не по типу', () => {
    const stock = {};
    expect(addModule(stock, 'panel', MODULE_STOCK_CAP - 1)).toBe(true);
    expect(addModule(stock, 'cable', 1)).toBe(true);
    expect(addModule(stock, 'filter', 1)).toBe(false);
  });

  it('переполнение отказывает целиком, а не принимает часть', () => {
    const stock = {};
    addModule(stock, 'panel', MODULE_STOCK_CAP - 2);
    expect(addModule(stock, 'cable', 5)).toBe(false);
    expect(moduleTotal(stock)).toBe(MODULE_STOCK_CAP - 2);
  });

  it('неположительное количество не принимается', () => {
    const stock = {};
    expect(addModule(stock, 'panel', 0)).toBe(false);
    expect(addModule(stock, 'panel', -3)).toBe(false);
    expect(moduleTotal(stock)).toBe(0);
  });
});

describe('Доступность по уровню', () => {
  it('до уровня открытия здание заперто', () => {
    const state = fresh();
    const builds = refreshBuilds(
      state,
      WAREHOUSE_RECIPE.unlock_level - 1,
      NOW,
      createWarehouse(),
    );
    expect(builds.find((b) => b.kind === 'warehouse_upgrade')?.state).toBe('LOCKED');
  });

  it('на уровне открытия становится доступным без действия игрока', () => {
    const state = fresh();
    const builds = refreshBuilds(state, WAREHOUSE_RECIPE.unlock_level, NOW, createWarehouse());
    expect(builds.find((b) => b.kind === 'warehouse_upgrade')?.state).toBe('AVAILABLE');
  });
});

describe('Старт стройки', () => {
  it('списывает весь рецепт одной транзакцией', () => {
    const state = readyToBuild();
    expect(startBuild(state, 'warehouse_upgrade', NOW).ok).toBe(true);
    expect(moduleTotal(state.stock)).toBe(0);

    const build = state.builds.find((b) => b.kind === 'warehouse_upgrade')!;
    expect(build.state).toBe('IN_PROGRESS');
    expect(build.ends_at).toBe(NOW + WAREHOUSE_RECIPE.build_time_min * 60);
  });

  it('при нехватке одной позиции не списывает ни одной', () => {
    const state = readyToBuild();
    state.stock.cable = (state.stock.cable ?? 0) - 1;
    const before = { ...state.stock };

    const result = startBuild(state, 'warehouse_upgrade', NOW);
    expect(result).toEqual({ ok: false, reason: 'missing_modules' });
    expect(state.stock).toEqual(before);
  });

  it('запертое здание не строится даже при полном складе', () => {
    const state = readyToBuild();
    state.builds.find((b) => b.kind === 'warehouse_upgrade')!.state = 'LOCKED';
    expect(startBuild(state, 'warehouse_upgrade', NOW).reason).toBe('not_available');
  });

  it('повторный старт идущей стройки отклоняется и модули не трогает', () => {
    const state = readyToBuild();
    startBuild(state, 'warehouse_upgrade', NOW);
    fill(state.stock, WAREHOUSE_RECIPE.recipe);
    const before = { ...state.stock };

    expect(startBuild(state, 'warehouse_upgrade', NOW).ok).toBe(false);
    expect(state.stock).toEqual(before);
  });

  it('одна линия — вторая стройка ждет, пока не освободится', () => {
    const state = readyToBuild();
    const habitat = state.builds.find((b) => b.kind === 'habitat_block')!;
    habitat.state = 'AVAILABLE';
    fill(state.stock, CONSTRUCTION_RECIPE.habitat_block.recipe);

    expect(startBuild(state, 'warehouse_upgrade', NOW).ok).toBe(true);
    expect(startBuild(state, 'habitat_block', NOW).reason).toBe('no_free_line');
    expect(activeLinesUsed(state)).toBe(1);
  });
});

describe('Завершение стройки', () => {
  it('до истечения таймера ничего не происходит', () => {
    const state = readyToBuild();
    const warehouse = createWarehouse();
    startBuild(state, 'warehouse_upgrade', NOW);

    const build = state.builds.find((b) => b.kind === 'warehouse_upgrade')!;
    state.builds = refreshBuilds(state, 21, build.ends_at - 1, warehouse);
    expect(state.builds.find((b) => b.kind === 'warehouse_upgrade')?.state).toBe('IN_PROGRESS');
    expect(warehouse.capacity).toBe(WAREHOUSE_START_CAPACITY);
  });

  it('по таймеру расширяет склад и открывает следующий тир', () => {
    const state = readyToBuild();
    const warehouse = createWarehouse();
    startBuild(state, 'warehouse_upgrade', NOW);

    const ends_at = state.builds.find((b) => b.kind === 'warehouse_upgrade')!.ends_at;
    state.builds = refreshBuilds(state, 21, ends_at, warehouse);

    const build = state.builds.find((b) => b.kind === 'warehouse_upgrade')!;
    expect(warehouse.capacity).toBe(WAREHOUSE_START_CAPACITY + WAREHOUSE_UPGRADE_STEP);
    expect(build.tier).toBe(1);
    expect(build.state).toBe('AVAILABLE');
  });

  it('эффект применяется ровно один раз, сколько бы раз ни пересчитывали', () => {
    const state = readyToBuild();
    const warehouse = createWarehouse();
    startBuild(state, 'warehouse_upgrade', NOW);
    const ends_at = state.builds.find((b) => b.kind === 'warehouse_upgrade')!.ends_at;

    for (let i = 0; i < 5; i++) state.builds = refreshBuilds(state, 21, ends_at + i, warehouse);
    expect(warehouse.capacity).toBe(WAREHOUSE_START_CAPACITY + WAREHOUSE_UPGRADE_STEP);
  });

  it('на потолке емкости повторяемое здание закрывается', () => {
    const state = readyToBuild();
    const warehouse = createWarehouse(WAREHOUSE_MAX_CAPACITY);
    startBuild(state, 'warehouse_upgrade', NOW);
    const ends_at = state.builds.find((b) => b.kind === 'warehouse_upgrade')!.ends_at;
    state.builds = refreshBuilds(state, 21, ends_at, warehouse);

    expect(warehouse.capacity).toBe(WAREHOUSE_MAX_CAPACITY);
    expect(state.builds.find((b) => b.kind === 'warehouse_upgrade')?.state).toBe('DONE');
  });

  it('одноразовое здание после постройки не возвращается в доступные', () => {
    const state = fresh();
    const habitat = state.builds.find((b) => b.kind === 'habitat_block')!;
    habitat.state = 'AVAILABLE';
    fill(state.stock, CONSTRUCTION_RECIPE.habitat_block.recipe);
    startBuild(state, 'habitat_block', NOW);

    const ends_at = state.builds.find((b) => b.kind === 'habitat_block')!.ends_at;
    state.builds = refreshBuilds(state, 21, ends_at, createWarehouse());
    expect(state.builds.find((b) => b.kind === 'habitat_block')?.state).toBe('DONE');
  });
});

describe('Потребность для дроп-роллера', () => {
  it('пустой склад дает потребность, равную рецепту', () => {
    const state = fresh();
    state.builds = refreshBuilds(state, 21, NOW, createWarehouse());
    const need = activeNeed(state);
    expect(need.filter).toBe(WAREHOUSE_RECIPE.recipe.filter);
    expect(need.panel).toBe(CONSTRUCTION_RECIPE.habitat_block.recipe.panel);
  });

  it('запас уменьшает потребность ровно на свой размер', () => {
    const state = fresh();
    state.builds = refreshBuilds(state, 21, NOW, createWarehouse());
    state.stock.filter = 2;
    expect(activeNeed(state).filter).toBe((WAREHOUSE_RECIPE.recipe.filter ?? 0) - 2);
  });

  it('идущая стройка потребности не создает: ее рецепт уже списан', () => {
    const state = readyToBuild();
    startBuild(state, 'warehouse_upgrade', NOW);
    const need = activeNeed(state);
    expect(need.filter ?? 0).toBe(0);
    expect(need.cable ?? 0).toBe(0);
  });

  it('покрытый рецепт не числится в потребности', () => {
    const state = readyToBuild();
    expect(missingFor('warehouse_upgrade', state.stock)).toEqual({});
  });
});
