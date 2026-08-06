/**
 * Доказательства прогона 4, доменный слой.
 *
 * Общий корень двух находок ниже — одно поле. `DropContext.need` объявлено в
 * `droproller.ts` как ДЕФИЦИТ («Чего не хватает активным и доступным стройкам:
 * модуль -> дефицит»), и `activeNeed` действительно возвращает остаток после
 * вычитания склада (`missingFor` = рецепт минус запас). Но два правила из трех
 * читают то же поле как ВАЛОВУЮ потребность и вычитают склад второй раз.
 *
 * Состояние строек здесь не рисуется руками: оно собирается настоящими
 * `createConstruction` + `refreshBuilds` + `activeNeed`, то есть ровно теми же
 * вызовами, которыми его собирает стор в `dropCtx()`. Иначе доказательство
 * держалось бы на предпосылке, до которой игра не доходит.
 */

import { describe, expect, it } from 'vitest';
import {
  ANTISTOCKPILE_FACTOR,
  FLOOR_GUARANTEE_WINDOW,
  PITY_K,
  PITY_MULTIPLIER,
} from '../config/economy';
import { CONSTRUCTION_RECIPE } from '../config/modules';
import {
  activeNeed,
  type ConstructionState,
  createConstruction,
  missingFor,
  refreshBuilds,
} from '../construction';
import {
  type DropContext,
  floorGuaranteeAllowedFor,
  type ModuleCounts,
  moduleWeight,
} from '../droproller';
import { createWarehouse } from '../warehouse';

const NOW = 1_000_000;

/**
 * Колония с открытыми стройками и заданным складом модулей. Уровень 21 открывает
 * оба здания класса Б, поэтому обе стройки приходят AVAILABLE — то самое
 * состояние, которое читает `activeNeed`.
 */
function colony(stock: ModuleCounts): ConstructionState {
  const state = createConstruction();
  state.stock = { ...stock };
  state.builds = refreshBuilds(state, 21, NOW, createWarehouse());
  return state;
}

/**
 * Склад «не хватает ровно одной панели»: комплект расширения склада полный,
 * жилому блоку недостает одной панели. Ровно то, куда приходит игрок, собравший
 * почти весь рецепт.
 */
function oneShortOfHabitat(): ConstructionState {
  const habitat = CONSTRUCTION_RECIPE.habitat_block.recipe;
  const warehouse = CONSTRUCTION_RECIPE.warehouse_upgrade.recipe;
  return colony({
    filter: warehouse.filter ?? 0,
    cable: warehouse.cable ?? 0,
    sealant: Math.max(habitat.sealant ?? 0, warehouse.sealant ?? 0),
    panel: (habitat.panel ?? 0) - 1,
    frame: habitat.frame ?? 0,
  });
}

function ctx(over: Partial<DropContext> = {}): DropContext {
  return {
    pity: {},
    stock: {},
    need: {},
    warehouse_avg_24h: {},
    gated_open: false,
    arrival_no: 20,
    constructions: [],
    rng: () => 0.5,
    ...over,
  };
}

/**
 * Д-24. И-11 (floor guarantee) не срабатывает НИКОГДА, если у игрока есть хотя
 * бы половина каждого недостающего модуля.
 *
 * Что видит игрок: собрал пять панелей из шести, ждет шестую. Прибытие за
 * прибытием контейнеры дают что угодно, кроме панели, и обещанная каркасом
 * гарантия «два прибытия подряд без нужного модуля — третье выдает нужный» не
 * включается ни на третьем прибытии, ни на сороковом.
 *
 * Где ломалось (историческая причина, до перевода И-11 на счетчик по
 * стройкам): `floorGuaranteeAllowed` спрашивала `stockCoversNeed(ctx.stock,
 * ctx.need)`, то есть «запас >= дефицита». Но `need` УЖЕ есть разность рецепта
 * и запаса. Условие вырождалось в «запас >= рецепт/2» и гасило гарантию ровно
 * в тот момент, ради которого она написана.
 *
 * После перевода на `ConstructionNeed`/`floorGuaranteeAllowedFor` (три
 * инварианта дроп-роллера, прогон 4) отдельной сверки со складом внутри
 * роллера больше нет вовсе: `deficit` стройки уже приходит НЕТТО (`missingFor`
 * — рецепт минус склад минус докупленное, И-12), и «покрыта» — это просто
 * «дефицит пуст». Тест переживает как сторож формы: подает деффицит через ту
 * же `missingFor`, которой пользуется стор в `dropCtx()`.
 *
 * Каркас, И-11: «если 2 прибытия шаттла подряд не дали ни одного модуля,
 * нужного активной стройке, третье гарантирует один такой модуль... не
 * срабатывает, если склад модулей УЖЕ ПОКРЫВАЕТ активную стройку». Склад,
 * покрывающий стройку, — это склад, с которого стройку можно начать. Здесь
 * начать нельзя: не хватает панели.
 */
describe('Д-24: И-11 гасится собственной проверкой покрытия', () => {
  it('гарантия обязана сработать, когда до постройки не хватает одной панели', () => {
    const construction = oneShortOfHabitat();
    const build = construction.builds.find((b) => b.kind === 'habitat_block')!;
    const deficit = missingFor(build, construction.stock);

    // Предпосылка: стройку начать нельзя — склад ее НЕ покрывает, не хватает
    // ровно одной панели. Это и есть состояние, ради которого написана И-11.
    expect(deficit).toEqual({ panel: 1 });

    const allowed = floorGuaranteeAllowedFor(
      {
        construction_id: 'habitat_block',
        deficit,
        // Окно И-11 выбрано: два прибытия подряд не дали нужного.
        arrivals_without_needed: FLOOR_GUARANTEE_WINDOW - 1,
        last_floor_arrival: 0,
      },
      ctx({ arrival_no: 20 }),
      // Это прибытие тоже не дало ничего нужного — чинить есть что.
      ['cable', 'cable', 'cable'],
    );

    expect(allowed, 'И-11: третье прибытие подряд обязано выдать нужный модуль').toBe(true);
  });

  it('гарантия не включается и после двадцати пустых прибытий подряд', () => {
    const construction = oneShortOfHabitat();
    const build = construction.builds.find((b) => b.kind === 'habitat_block')!;
    const allowed = floorGuaranteeAllowedFor(
      {
        construction_id: 'habitat_block',
        deficit: missingFor(build, construction.stock),
        arrivals_without_needed: 20,
        last_floor_arrival: 0,
      },
      ctx({ arrival_no: 40 }),
      ['cable'],
    );
    expect(allowed, 'серия из двадцати неудач обязана пробить гарантию').toBe(true);
  });
});

/**
 * Д-25. Анти-стокпайл И-7 срабатывает на модуле, которого игроку НЕ ХВАТАЕТ, и
 * съедает удвоение pity того же И-7.
 *
 * Что видит игрок: пять панелей из шести, панель не выпадала четыре прибытия
 * подряд. Каркас обещает, что такой модуль «удваивает вес до выпадения». По
 * факту вес остается базовым: pity умножает на два, анти-стокпайл тут же делит
 * на два, и обещанная поблажка не существует.
 *
 * Где ломается: `owned > needed * ANTISTOCKPILE_THRESHOLD` читает `needed` как
 * валовую потребность, а приходит туда дефицит. Пять панелей против дефицита в
 * одну — «запас больше потребности вдвое», хотя строить не из чего.
 */
describe('Д-25: анти-стокпайл наказывает за почти собранный рецепт', () => {
  it('запас, которого не хватает на постройку, не должен считаться излишком', () => {
    const construction = oneShortOfHabitat();
    const build = construction.builds.find((b) => b.kind === 'habitat_block')!;
    const need = activeNeed(construction);
    expect(missingFor(build, construction.stock)).toEqual({ panel: 1 });

    // Анти-стокпайл (прогон 4) читает `warehouse_avg_24h`, не мгновенный
    // `stock` — сюда подставлен тот же склад как имитация «среднее равно
    // текущему остатку» (стабильный склад, ничего не продавали).
    const bare = moduleWeight('panel', ctx({ need }));
    const with_stock = moduleWeight(
      'panel',
      ctx({ need, warehouse_avg_24h: construction.stock }),
    );

    expect(with_stock / bare).not.toBeCloseTo(ANTISTOCKPILE_FACTOR);
    expect(with_stock, 'вес дефицитного модуля не режется за неполный комплект').toBeCloseTo(
      bare,
    );
  });

  it('pity дефицитного модуля обязан удваивать вес, а не возвращать базовый', () => {
    const construction = oneShortOfHabitat();
    const need = activeNeed(construction);

    const bare = moduleWeight('panel', ctx({ need }));
    const pitied = moduleWeight(
      'panel',
      ctx({ need, warehouse_avg_24h: construction.stock, pity: { panel: PITY_K } }),
    );

    expect(
      pitied,
      'И-7: модуль, дефицитный для доступной стройки и не выпавший PITY_K прибытий, удваивает вес',
    ).toBeCloseTo(bare * PITY_MULTIPLIER);
  });
});
