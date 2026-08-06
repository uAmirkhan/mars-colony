/**
 * Дроп-роллер: И-7 (pity, анти-стокпайл) и И-11 (floor guarantee).
 *
 * Проверяется числом, а не наблюдением за выборкой: каждое из пяти ограничений
 * гарантии закрывает свой эксплойт, и если проверять их одним прогоном роллера,
 * то при поломке одного упадет неизвестно какое.
 */

import { describe, expect, it } from 'vitest';
import {
  ANTISTOCKPILE_FACTOR,
  FLOOR_GUARANTEE_MIN_GAP,
  FLOOR_GUARANTEE_WINDOW,
  FRONT_LOADED_LUCK_ARRIVALS,
  PITY_K,
  PITY_MULTIPLIER,
  TIER_WEIGHTS,
} from '../config/economy';
import { CONSTRUCTION_RECIPE, MODULE_TIER_POOL, MODULES } from '../config/modules';
import {
  type DropContext,
  floorGuaranteeAllowed,
  moduleWeight,
  rollArrival,
  stockCoversNeed,
} from '../droproller';

function ctx(patch: Partial<DropContext> = {}): DropContext {
  return {
    pity: {},
    stock: {},
    need: {},
    gated_open: false,
    arrival_no: 10,
    arrivals_without_needed: 0,
    last_floor_arrival: 0,
    rng: () => 0.5,
    ...patch,
  };
}

describe('Веса тиров', () => {
  it('вес тира делится между модулями пула поровну', () => {
    const base = moduleWeight('panel', ctx());
    expect(base).toBeCloseTo(TIER_WEIGHTS.basic / MODULE_TIER_POOL.basic.length);
  });

  it('гейтовый тир весит ноль, пока гейт закрыт', () => {
    expect(moduleWeight('drill_head', ctx())).toBe(0);
    expect(moduleWeight('drill_head', ctx({ gated_open: true }))).toBeGreaterThan(0);
  });

  it('закрытый гейт не выпадает ни разу за длинную серию', () => {
    let rng_state = 0;
    const gated = new Set(MODULE_TIER_POOL.gated);
    for (let i = 0; i < 500; i++) {
      rng_state = (rng_state + 0.0137) % 1;
      const roll = rollArrival(5, ctx({ rng: () => rng_state }));
      expect(roll.modules.some((m) => gated.has(m))).toBe(false);
    }
  });
});

describe('И-7: pity', () => {
  it('удваивает вес нужного модуля после PITY_K пустых прибытий', () => {
    const before = moduleWeight('filter', ctx({ need: { filter: 3 } }));
    const after = moduleWeight(
      'filter',
      ctx({ need: { filter: 3 }, pity: { filter: PITY_K } }),
    );
    expect(after).toBeCloseTo(before * PITY_MULTIPLIER);
  });

  it('не разгоняет вес модуля, который ни одной стройке не нужен', () => {
    const base = moduleWeight('filter', ctx());
    const pitied = moduleWeight('filter', ctx({ pity: { filter: PITY_K * 3 } }));
    expect(pitied).toBeCloseTo(base);
  });

  it('счетчик растет у невыпавших НУЖНЫХ и обнуляется у выпавших', () => {
    // Потребность в контексте обязательна: канон 2.4 растит счетчик «на каждое
    // событие, где предмет НУЖЕН и не выпал» (2.3: `elif
    // isNeededForActiveContext`). Прежняя редакция теста роллила с пустым
    // `need` и ждала роста — то есть меряла прежнюю реализацию, где инкремент
    // был безусловным, а не правило движка. Дефект Д-3 держит вторую половину
    // правила: у НЕнужного модуля счетчик не растет.
    const roll = rollArrival(
      2,
      ctx({ need: { panel: 1, cable: 1 }, pity: { panel: 3, cable: 3 } }),
    );
    for (const id of roll.modules) expect(roll.next_pity[id]).toBe(0);
    const not_dropped = (['panel', 'cable'] as const).filter(
      (id) => !roll.modules.includes(id),
    );
    for (const id of not_dropped) expect(roll.next_pity[id]).toBe(4);
  });
});

describe('И-7: анти-стокпайл', () => {
  it('режет вес, когда запас превышает потребность вдвое', () => {
    const base = moduleWeight('cable', ctx({ need: { cable: 2 } }));
    const piled = moduleWeight('cable', ctx({ need: { cable: 2 }, stock: { cable: 5 } }));
    expect(piled).toBeCloseTo(base * ANTISTOCKPILE_FACTOR);
  });

  it('при нулевой потребности любой запас считается излишком', () => {
    const base = moduleWeight('cable', ctx());
    const piled = moduleWeight('cable', ctx({ stock: { cable: 1 } }));
    expect(piled).toBeCloseTo(base * ANTISTOCKPILE_FACTOR);
  });

  /**
   * Каркас И-7: «запас > потребность x2 → вес /2», где потребность — рецепт
   * доступной стройки, а не остаток нехватки. Пока модуля не хватает на рецепт,
   * запас заведомо меньше порога, и резать нечего: игроку не из чего строить.
   */
  it('не режет вес модуля, которого не хватает на рецепт', () => {
    const recipe = CONSTRUCTION_RECIPE.habitat_block.recipe.panel ?? 0;
    const base = moduleWeight('panel', ctx({ need: { panel: recipe } }));
    const almost = moduleWeight(
      'panel',
      ctx({ need: { panel: recipe }, stock: { panel: recipe - 1 } }),
    );
    expect(almost).toBeCloseTo(base);
  });

  it('ровно двойной запас еще не излишек — порог строгий', () => {
    const base = moduleWeight('cable', ctx({ need: { cable: 2 } }));
    const at_threshold = moduleWeight(
      'cable',
      ctx({ need: { cable: 2 }, stock: { cable: 4 } }),
    );
    expect(at_threshold).toBeCloseTo(base);
  });
});

describe('И-11: floor guarantee', () => {
  const needy = { need: { sealant: 6 }, stock: {} };

  it('первые прибытия форсируют гарантию поверх И-11', () => {
    for (let n = 1; n <= FRONT_LOADED_LUCK_ARRIVALS; n++) {
      const allowed = floorGuaranteeAllowed(
        ctx({ ...needy, arrival_no: n, arrivals_without_needed: 0 }),
        ['panel', 'panel'],
      );
      expect(allowed).toBe(true);
    }
  });

  it('после FTUE-окна одной неудачи мало: нужно окно из трех прибытий', () => {
    const at = (without: number) =>
      floorGuaranteeAllowed(
        ctx({ ...needy, arrival_no: 20, arrivals_without_needed: without }),
        ['panel'],
      );
    expect(at(0)).toBe(false);
    expect(at(1)).toBe(false);
    expect(at(2)).toBe(true);
  });

  it('не срабатывает, если прибытие и так дало нужный модуль', () => {
    const allowed = floorGuaranteeAllowed(
      ctx({ ...needy, arrival_no: 20, arrivals_without_needed: 5 }),
      ['sealant'],
    );
    expect(allowed).toBe(false);
  });

  it('не срабатывает, если склад уже покрывает потребность', () => {
    const allowed = floorGuaranteeAllowed(
      ctx({
        need: { sealant: 6 },
        stock: { sealant: 6 },
        arrival_no: 20,
        arrivals_without_needed: 5,
      }),
      ['panel'],
    );
    expect(allowed).toBe(false);
  });

  /**
   * Каркас И-11: гарантия «не срабатывает, если склад модулей уже покрывает
   * активную стройку». Покрывает — значит стройку можно начать; на комплекте
   * без одного модуля начать нельзя, и гарантия обязана работать.
   */
  it('срабатывает, когда до старта стройки не хватает одного модуля', () => {
    const recipe = CONSTRUCTION_RECIPE.habitat_block.recipe.panel ?? 0;
    const allowed = floorGuaranteeAllowed(
      ctx({
        need: { panel: recipe },
        stock: { panel: recipe - 1 },
        arrival_no: 20,
        arrivals_without_needed: FLOOR_GUARANTEE_WINDOW - 1,
      }),
      ['cable'],
    );
    expect(allowed).toBe(true);
  });

  /**
   * Канон 2.4: счетчик окна растет «на прибытие без нужного предмета». Модуль,
   * которого на складе уже хватает на рецепт, стройку не двигает — такое
   * прибытие окно не закрывает.
   */
  it('контейнер с модулем, которого и так хватает, окно И-11 не закрывает', () => {
    const cable = CONSTRUCTION_RECIPE.warehouse_upgrade.recipe.cable ?? 0;
    const allowed = floorGuaranteeAllowed(
      ctx({
        need: { panel: CONSTRUCTION_RECIPE.habitat_block.recipe.panel ?? 0, cable },
        stock: { cable },
        arrival_no: 20,
        arrivals_without_needed: FLOOR_GUARANTEE_WINDOW - 1,
      }),
      ['cable'],
    );
    expect(allowed).toBe(true);
  });

  /**
   * Канон 2.6 п.3: форс-выдача не создает избыточный сток. Из нужных рецепту
   * модулей выдается самый дефицитный, а не самый крупный по рецепту: крупный
   * может уже лежать на складе целиком.
   */
  it('форс-выдача не выдает модуль, которого на складе уже хватает', () => {
    const sealant =
      (CONSTRUCTION_RECIPE.warehouse_upgrade.recipe.sealant ?? 0) +
      (CONSTRUCTION_RECIPE.habitat_block.recipe.sealant ?? 0);
    const panel = CONSTRUCTION_RECIPE.habitat_block.recipe.panel ?? 0;
    const roll = rollArrival(
      3,
      ctx({ need: { sealant, panel }, stock: { sealant }, arrival_no: 1 }),
    );
    expect(roll.floor_forced_slot).not.toBeNull();
    expect(roll.modules.at(-1)).toBe('panel');
  });

  it('не чаще раза на пять прибытий — эксплойт «держи стройку голодной»', () => {
    const at = (arrival: number) =>
      floorGuaranteeAllowed(
        ctx({
          ...needy,
          arrival_no: arrival,
          arrivals_without_needed: 5,
          last_floor_arrival: 20,
        }),
        ['panel'],
      );
    for (let gap = 1; gap < FLOOR_GUARANTEE_MIN_GAP; gap++) expect(at(20 + gap)).toBe(false);
    expect(at(20 + FLOOR_GUARANTEE_MIN_GAP)).toBe(true);
  });

  it('никогда не выдает гейтовый тир, даже когда гейт открыт и модуль нужен', () => {
    const roll = rollArrival(
      3,
      ctx({
        need: { drill_head: 4 },
        gated_open: true,
        arrival_no: 1,
        arrivals_without_needed: 9,
      }),
    );
    expect(roll.floor_forced_slot).toBeNull();
  });

  it('переписывает отсек, а не добавляет: контейнеров ровно столько, сколько отсеков', () => {
    for (const count of [3, 4, 5]) {
      const roll = rollArrival(
        count,
        ctx({ ...needy, arrival_no: 1, arrivals_without_needed: 0 }),
      );
      expect(roll.modules).toHaveLength(count);
    }
  });

  it('выдает самый дефицитный из нужных, а не первый попавшийся', () => {
    const roll = rollArrival(3, ctx({ need: { filter: 1, cable: 9 }, arrival_no: 1 }));
    expect(roll.floor_forced_slot).not.toBeNull();
    expect(roll.modules.at(-1)).toBe('cable');
  });

  it('после срабатывания фиксирует номер прибытия для следующего разрыва', () => {
    const roll = rollArrival(3, ctx({ ...needy, arrival_no: 2 }));
    expect(roll.next_last_floor_arrival).toBe(2);
  });

  it('счетчик пустых прибытий обнуляется, когда нужное пришло', () => {
    const roll = rollArrival(
      3,
      ctx({ need: { sealant: 6 }, arrival_no: 1, arrivals_without_needed: 7 }),
    );
    expect(roll.next_arrivals_without_needed).toBe(0);
  });
});

describe('Краевые значения генератора случайных чисел', () => {
  it.each([0, 0.999999, 1])('rng = %s не роняет ролл и дает валидный модуль', (value) => {
    const roll = rollArrival(5, ctx({ rng: () => value }));
    expect(roll.modules).toHaveLength(5);
    for (const id of roll.modules) expect(MODULES[id]).toBeDefined();
  });

  /**
   * rng ровно 1 уводит накопитель в самый хвост списка весов. Хвост списка —
   * гейтовые модули с нулевым весом, и запасной вариант «взять последний»
   * выдавал бы закрытый контент по краевому значению. Ошибка тихая: игра не
   * падает, игрок просто получает модуль, которого в его игре быть не может.
   */
  it.each([0, 1])('rng = %s не выдает гейтовый модуль при закрытом гейте', (value) => {
    const gated = new Set(MODULE_TIER_POOL.gated);
    const roll = rollArrival(5, ctx({ rng: () => value }));
    for (const id of roll.modules) expect(gated.has(id)).toBe(false);
  });
});

describe('stockCoversNeed', () => {
  it('пустая потребность покрыта всегда', () => {
    expect(stockCoversNeed({}, {})).toBe(true);
  });

  it('нехватка одного модуля ломает покрытие целиком', () => {
    expect(stockCoversNeed({ filter: 6, cable: 6 }, { filter: 6, cable: 7 })).toBe(false);
  });
});
