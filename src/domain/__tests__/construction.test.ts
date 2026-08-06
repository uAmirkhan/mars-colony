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
  buyModules,
  type ConstructionState,
  createConstruction,
  createConstruction as fresh,
  missingFor,
  moduleCapacity,
  modulePurchasePrice,
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
    expect(moduleSpaceLeft(state)).toBe(MODULE_STOCK_CAP);
    expect(moduleCapacity(state)).toBe(MODULE_STOCK_CAP);
  });

  it('лимит общий на все типы, а не по типу', () => {
    const state = fresh();
    expect(addModule(state, 'panel', MODULE_STOCK_CAP - 1)).toBe(true);
    expect(addModule(state, 'cable', 1)).toBe(true);
    expect(addModule(state, 'filter', 1)).toBe(false);
  });

  it('переполнение отказывает целиком, а не принимает часть', () => {
    const state = fresh();
    addModule(state, 'panel', MODULE_STOCK_CAP - 2);
    expect(addModule(state, 'cable', 5)).toBe(false);
    expect(moduleTotal(state.stock)).toBe(MODULE_STOCK_CAP - 2);
  });

  it('неположительное количество не принимается', () => {
    const state = fresh();
    expect(addModule(state, 'panel', 0)).toBe(false);
    expect(addModule(state, 'panel', -3)).toBe(false);
    expect(moduleTotal(state.stock)).toBe(0);
  });

  /**
   * Каркас 6: «Строй-модули хранятся отдельным лимитом 100 и апгрейдятся тем же
   * зданием». Сторож против возврата Д-6: до починки потолок модулей был
   * константой, и расширение склада двигало только товарную емкость.
   *
   * Цена дефекта не косметическая: на забитом складе модулей `collectContainer`
   * отказывает, контейнеры прибывшего рейса висят несобранными, кулдаун не
   * стартует и новый заказ шаттла не выдается — механика встает целиком.
   */
  it('расширение склада поднимает потолок модулей тем же шагом, что и товарный', () => {
    const state = readyToBuild();
    const warehouse = createWarehouse();
    startBuild(state, 'warehouse_upgrade', NOW);
    const ends_at = state.builds.find((b) => b.kind === 'warehouse_upgrade')!.ends_at;
    state.builds = refreshBuilds(state, 21, ends_at, warehouse);

    expect(warehouse.capacity).toBe(WAREHOUSE_START_CAPACITY + WAREHOUSE_UPGRADE_STEP);
    expect(moduleCapacity(state)).toBe(MODULE_STOCK_CAP + WAREHOUSE_UPGRADE_STEP);

    // Место, которого до апгрейда не было: старый потолок больше не отказ.
    state.stock = { panel: MODULE_STOCK_CAP };
    expect(moduleSpaceLeft(state)).toBe(WAREHOUSE_UPGRADE_STEP);
    expect(addModule(state, 'frame')).toBe(true);
  });

  it('жилой блок потолок модулей не двигает: апгрейдит только склад', () => {
    const state = fresh();
    const habitat = state.builds.find((b) => b.kind === 'habitat_block')!;
    habitat.state = 'AVAILABLE';
    fill(state.stock, CONSTRUCTION_RECIPE.habitat_block.recipe);
    startBuild(state, 'habitat_block', NOW);

    const ends_at = state.builds.find((b) => b.kind === 'habitat_block')!.ends_at;
    state.builds = refreshBuilds(state, 21, ends_at, createWarehouse());
    expect(moduleCapacity(state)).toBe(MODULE_STOCK_CAP);
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

  /**
   * Потребность валовая: склад из нее не вычитается.
   *
   * [[tz-common-systems-mars]] 2.3 сравнивает с этим числом САМ ЗАПАС —
   * `activeNeed = activeContextNeed(item, colony_state)`, следом `if activeNeed
   * > 0 and avgStock24h > activeNeed * config.ANTISTOCKPILE_THRESHOLD`; 2.6 п.3
   * делает то же для гарантии: «`warehouseCoversConstruction` — сравнение
   * `ModuleStock.qty` игрока с оставшейся потребностью рецепта стройки».
   * Вычесть склад здесь — значит сравнить запас с самим собой: порог
   * анти-стокпайла превращается в «запас больше собственной нехватки» и бьет по
   * дефицитному модулю (Д-25), а покрытие И-11 — в «запас больше половины
   * рецепта» и гасит гарантию на почти собранном комплекте (Д-24).
   */
  it('склад потребность не уменьшает: с ней сравнивается сам запас', () => {
    const state = fresh();
    state.builds = refreshBuilds(state, 21, NOW, createWarehouse());
    state.stock.filter = 2;
    expect(activeNeed(state).filter).toBe(WAREHOUSE_RECIPE.recipe.filter);
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
    const build = state.builds.find((b) => b.kind === 'warehouse_upgrade')!;
    expect(missingFor(build, state.stock)).toEqual({});
  });

  /**
   * [[tz-production-mars]] 3.4: «при нескольких активных стройках функция
   * возвращает СУММАРНУЮ потребность, а приоритет отдачи — самой давно ждущей»;
   * там же — «`active_construction_need` не врет (обе линии читаются, не только
   * первая)». Герметик — единственный модуль в обоих рецептах, поэтому правило
   * проверяется на нем: максимум вернул бы больший из двух, канон — их сумму.
   */
  it('потребность нескольких доступных строек суммируется, а не берется максимумом', () => {
    const state = fresh();
    state.builds = refreshBuilds(state, 21, NOW, createWarehouse());

    const from_warehouse = WAREHOUSE_RECIPE.recipe.sealant ?? 0;
    const from_habitat = CONSTRUCTION_RECIPE.habitat_block.recipe.sealant ?? 0;

    expect(activeNeed(state).sealant).toBe(from_warehouse + from_habitat);
    // Явно: максимум здесь дал бы строго меньше суммы — тест обязан краснеть,
    // если правило снова прочитают как «самая требовательная стройка».
    expect(activeNeed(state).sealant).toBeGreaterThan(Math.max(from_warehouse, from_habitat));
  });

  /**
   * Та же 3.4, п.2: «`ModuleStock` не имеет `reserved` — начисленный модуль
   * сразу физически на складе, стройка резервирует его только логически в
   * момент `start`». Одна единица не может быть засчитана двум линиям: комплект
   * герметика, покрывающий склад, не уменьшает потребность второй линии, и
   * потребность обеих остается суммой рецептов.
   */
  it('полный комплект одной стройки не срезает потребность второй', () => {
    const state = fresh();
    state.builds = refreshBuilds(state, 21, NOW, createWarehouse());

    const from_warehouse = WAREHOUSE_RECIPE.recipe.sealant ?? 0;
    const from_habitat = CONSTRUCTION_RECIPE.habitat_block.recipe.sealant ?? 0;
    state.stock.sealant = from_warehouse;

    expect(activeNeed(state).sealant).toBe(from_warehouse + from_habitat);
  });
});

/**
 * Второй канал получения строй-модулей: изотопы вместо шаттла.
 *
 * Инвариант И-1 разрешает ровно два пути, и до этих тестов существовал один.
 * Цена лежала в конфиге и читалась единственной формулой ожидаемой ценности
 * отсека шаттла — то есть параметр работал только как множитель в чужом
 * расчете, а купить модуль было нельзя вовсе.
 *
 * Числа в проверках выписаны литералами из каркаса (раздел 5: «базовый 150,
 * редкий 200, гейтовый 400»), а не взяты из `MODULE_PRICE_ISOTOPES`. Тест,
 * который считает ожидаемое той же таблицей, что проверяет, зеленеет при любом
 * ее содержимом — в этом проекте такой уже ловили.
 */
describe('Докупка модулей за изотопы (И-1, И-12)', () => {
  /** Жилой блок доступен, склад пуст. Рецепт: панель 6, каркас 5, герметик 7. */
  function habitatAvailable(): ConstructionState {
    const state = createConstruction();
    const build = state.builds.find((b) => b.kind === 'habitat_block')!;
    build.state = 'AVAILABLE';
    return state;
  }

  function habitat(state: ConstructionState) {
    return state.builds.find((b) => b.kind === 'habitat_block')!;
  }

  it('цена линейна и берет тир модуля по каркасу', () => {
    expect(modulePurchasePrice('panel', 6)).toBe(900); // базовый 150
    expect(modulePurchasePrice('sealant', 7)).toBe(1400); // редкий 200
    expect(modulePurchasePrice('drill_head', 1)).toBe(400); // гейтовый 400
    expect(modulePurchasePrice('panel', 0)).toBe(0);
  });

  it('докупка закрывает дефицит своего модуля и не трогает соседние', () => {
    const state = habitatAvailable();
    const build = habitat(state);

    const bought = buyModules(build, state.stock, 'sealant');

    expect(bought).toEqual({ qty: 7, price: 1400 });
    expect(missingFor(build, state.stock).sealant ?? 0).toBe(0);
    expect(missingFor(build, state.stock).panel).toBe(6);
    expect(missingFor(build, state.stock).frame).toBe(5);
  });

  it('докупленное не попадает на склад модулей', () => {
    // И-12 дословно: докупленное «не может быть изъято, продано или
    // переиспользовано». Попади оно на общий склад — и открывается арбитраж
    // «докупить под дешевую стройку, потратить на дорогую».
    const state = habitatAvailable();
    const before = moduleTotal(state.stock);

    buyModules(habitat(state), state.stock, 'panel');

    expect(moduleTotal(state.stock)).toBe(before);
    expect(state.stock.panel ?? 0).toBe(0);
  });

  it('докупка не упирается в лимит склада модулей', () => {
    // Склад забит под потолок чужим модулем. Обычное зачисление тут откажет,
    // и если бы докупка шла через склад, игрок платил бы изотопы в никуда.
    const state = habitatAvailable();
    state.stock.drill_head = MODULE_STOCK_CAP;
    expect(moduleSpaceLeft(state)).toBe(0);
    expect(addModule(state, 'panel', 1)).toBe(false);

    expect(buyModules(habitat(state), state.stock, 'panel')).toEqual({ qty: 6, price: 900 });
  });

  it('повторная докупка на закрытом дефиците не берет денег', () => {
    const state = habitatAvailable();
    const build = habitat(state);
    buyModules(build, state.stock, 'panel');

    expect(buyModules(build, state.stock, 'panel')).toBeNull();
    expect(build.purchased.panel).toBe(6);
  });

  it('докупка недоступна, пока стройка не открыта', () => {
    const state = createConstruction(); // все стройки LOCKED
    const build = state.builds.find((b) => b.kind === 'habitat_block')!;

    expect(buyModules(build, state.stock, 'panel')).toBeNull();
    expect(build.purchased.panel ?? 0).toBe(0);
  });

  it('старт стройки тратит сначала докупленное, потом склад', () => {
    // Порядок не косметический: склад после старта остается общим ресурсом, а
    // докупленное привязано к этой стройке и больше нигде не годится. Спиши
    // сначала склад — и оплаченные модули останутся заперты на стройке,
    // которая уже идет, то есть игрок потеряет изотопы дважды.
    const state = habitatAvailable();
    const build = habitat(state);
    state.stock.panel = 6;
    state.stock.frame = 5;
    state.stock.sealant = 3;

    expect(buyModules(build, state.stock, 'sealant')).toEqual({ qty: 4, price: 800 });
    expect(startBuild(state, 'habitat_block', NOW).ok).toBe(true);

    expect(state.stock.sealant).toBe(0);
    expect(build.purchased.sealant).toBe(0);
    expect(moduleTotal(state.stock)).toBe(0);
  });

  it('докупленное считается комплектом: стройка стартует без единого модуля на складе', () => {
    const state = habitatAvailable();
    const build = habitat(state);
    for (const id of ['panel', 'frame', 'sealant'] as ModuleId[]) {
      buyModules(build, state.stock, id);
    }

    expect(startBuild(state, 'habitat_block', NOW)).toEqual({ ok: true });
    expect(habitat(state).state).toBe('IN_PROGRESS');
    expect(moduleTotal(state.stock)).toBe(0);
  });
});
