/**
 * Н-6: у экрана склада не было вкладки «Модули» — ТЗ производства 9.2
 * требует ее прямо в композиции. `warehousePanelView` — та же чистая
 * функция, которую вызывает `WarehousePanel`; тест дергает ее напрямую и не
 * рендерит DOM (для `screens.tsx` в проекте нет DOM-рендер-инфраструктуры,
 * см. `shuttle-station.test.ts` и его докстринг про тот же компромисс).
 */

import { describe, expect, it } from 'vitest';
import { ALL_GOOD_IDS, GOODS } from '../../domain/config/goods';
import { MECHANIC_UNLOCK_LEVEL } from '../../domain/config/levels';
import { ALL_MODULE_IDS, CONSTRUCTION_RECIPE, MODULES } from '../../domain/config/modules';
import { type ConstructionState, createConstruction } from '../../domain/construction';
import { createWarehouse, deposit, reserve, type WarehouseState } from '../../domain/warehouse';
import { WAREHOUSE_WARN_RATIO } from '../kit';
import { warehousePanelView } from '../screens';

function view(level: number, warehouse: WarehouseState, construction: ConstructionState) {
  return warehousePanelView({ level, warehouse, construction });
}

describe('warehousePanelView — вкладка «Товары»', () => {
  it('на первом уровне видна только разблокированная позиция (водоросли), приглушена при qty=0', () => {
    const v = view(1, createWarehouse(), createConstruction());
    expect(v.goods.map((g) => g.id)).toEqual(
      ALL_GOOD_IDS.filter((id) => GOODS[id].unlock_level <= 1),
    );
    const algae = v.goods.find((g) => g.id === 'algae');
    expect(algae?.dimmed).toBe(true);
    expect(algae?.qty).toBe(0);
  });

  it('позиция с товаром не приглушена, «Все» указывает на первую продаваемую строку', () => {
    const w = createWarehouse();
    deposit(w, 'algae', 5);
    const v = view(1, w, createConstruction());
    const algae = v.goods.find((g) => g.id === 'algae');
    expect(algae?.dimmed).toBe(false);
    expect(algae?.qty).toBe(5);
    expect(algae?.free).toBe(5);
    expect(v.firstSellableGoodId).toBe('algae');
  });

  it('зарезервированное под заказ не продается: free меньше qty', () => {
    const w = createWarehouse();
    deposit(w, 'algae', 5);
    reserve(w, 'algae', 3);
    const v = view(1, w, createConstruction());
    const algae = v.goods.find((g) => g.id === 'algae');
    expect(algae?.qty).toBe(5);
    expect(algae?.free).toBe(2);
  });

  it('список растет по уровню и остается в порядке категория→уровень открытия', () => {
    const v = view(12, createWarehouse(), createConstruction());
    expect(v.goods.map((g) => g.id)).toEqual(
      ALL_GOOD_IDS.filter((id) => GOODS[id].unlock_level <= 12),
    );
  });

  it('капасити-бар товаров краснеет на пороге WAREHOUSE_WARN_RATIO', () => {
    const w = createWarehouse();
    deposit(w, 'algae', Math.ceil(w.capacity * WAREHOUSE_WARN_RATIO));
    const v = view(1, w, createConstruction());
    expect(v.goodsCapacity.warn).toBe(true);
  });

  it('капасити-бар товаров ниже порога не краснеет', () => {
    const w = createWarehouse();
    deposit(w, 'algae', Math.floor(w.capacity * WAREHOUSE_WARN_RATIO) - 1);
    const v = view(1, w, createConstruction());
    expect(v.goodsCapacity.warn).toBe(false);
  });
});

describe('warehousePanelView — вкладка «Модули»', () => {
  it('до открытия шаттла (ур. < 5) вкладка не скрыта, но помечена недоступной', () => {
    const v = view(MECHANIC_UNLOCK_LEVEL.shuttle - 1, createWarehouse(), createConstruction());
    expect(v.modulesUnlocked).toBe(false);
    // Список позиций считается независимо от видимости — эдж ТЗ 9.2
    // требует именно заглушку поверх скрытых данных, а не пустой массив.
    expect(v.modules.length).toBe(ALL_MODULE_IDS.length);
  });

  it('после открытия шаттла вкладка доступна', () => {
    const v = view(MECHANIC_UNLOCK_LEVEL.shuttle, createWarehouse(), createConstruction());
    expect(v.modulesUnlocked).toBe(true);
  });

  it('гейтовый тир всегда заблокирован в этом срезе', () => {
    const v = view(20, createWarehouse(), createConstruction());
    const gated = v.modules.filter((m) => MODULES[m.id].tier === 'gated');
    expect(gated.length).toBeGreaterThan(0);
    for (const m of gated) expect(m.locked).toBe(true);
  });

  it('модуль, нужный доступной стройке (AVAILABLE, дефицит), несет бейдж needed', () => {
    const construction = createConstruction();
    const build = construction.builds[0];
    if (!build) throw new Error('в фикстуре нет ни одной стройки');
    build.state = 'AVAILABLE';
    // Стока нет вовсе — весь рецепт в дефиците.
    const recipe = CONSTRUCTION_RECIPE[build.kind].recipe;
    const [needed_id] = Object.keys(recipe) as Array<keyof typeof recipe>;
    if (!needed_id) throw new Error('в рецепте фикстуры нет ни одного модуля');

    const v = view(20, createWarehouse(), construction);
    const row = v.modules.find((m) => m.id === needed_id);
    expect(row?.needed).toBe(true);
  });

  it('модуль стройке в IN_PROGRESS не считается нужным — рецепт уже списан при старте', () => {
    const construction = createConstruction();
    const build = construction.builds[0];
    if (!build) throw new Error('в фикстуре нет ни одной стройки');
    build.state = 'IN_PROGRESS';
    const recipe = CONSTRUCTION_RECIPE[build.kind].recipe;
    const [needed_id] = Object.keys(recipe) as Array<keyof typeof recipe>;
    if (!needed_id) throw new Error('в рецепте фикстуры нет ни одного модуля');

    const v = view(20, createWarehouse(), construction);
    const row = v.modules.find((m) => m.id === needed_id);
    expect(row?.needed).toBe(false);
  });

  it('модуль, которого рецепт уже не требует (стока хватает), needed=false', () => {
    const construction = createConstruction();
    const build = construction.builds[0];
    if (!build) throw new Error('в фикстуре нет ни одной стройки');
    build.state = 'AVAILABLE';
    const recipe = CONSTRUCTION_RECIPE[build.kind].recipe;
    construction.stock = { ...recipe };

    const v = view(20, createWarehouse(), construction);
    for (const id of Object.keys(recipe) as Array<keyof typeof recipe>) {
      const row = v.modules.find((m) => m.id === id);
      expect(row?.needed).toBe(false);
    }
  });
});
