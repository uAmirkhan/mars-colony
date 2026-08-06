/**
 * Дроп-роллер: И-7 (pity, анти-стокпайл) и И-11 (floor guarantee).
 *
 * Проверяется числом, а не наблюдением за выборкой: каждое из ограничений
 * гарантии закрывает свой эксплойт, и если проверять их одним прогоном роллера,
 * то при поломке одного упадет неизвестно какое.
 */

import fc from 'fast-check';
import { describe, expect, it } from 'vitest';
import { makeRng } from '../../sim/rng';
import {
  ANTISTOCKPILE_FACTOR,
  ANTISTOCKPILE_THRESHOLD,
  FLOOR_GUARANTEE_MIN_GAP,
  FLOOR_GUARANTEE_WINDOW,
  FRONT_LOADED_LUCK_ARRIVALS,
  PITY_K,
  PITY_MULTIPLIER,
  TIER_WEIGHTS,
} from '../config/economy';
import {
  ALL_MODULE_IDS,
  CONSTRUCTION_RECIPE,
  MODULE_TIER_POOL,
  MODULES,
} from '../config/modules';
import {
  type ConstructionNeed,
  type DropContext,
  floorGuaranteeAllowedFor,
  moduleWeight,
  rollArrival,
  stockCoversNeed,
} from '../droproller';

function ctx(patch: Partial<DropContext> = {}): DropContext {
  return {
    pity: {},
    stock: {},
    need: {},
    warehouse_avg_24h: {},
    gated_open: false,
    arrival_no: 10,
    constructions: [],
    rng: () => 0.5,
    ...patch,
  };
}

/** Одна стройка со своим окном И-11 — короткий конструктор для тестов. */
function construction(patch: Partial<ConstructionNeed> = {}): ConstructionNeed {
  return {
    construction_id: 'test',
    deficit: {},
    arrivals_without_needed: 0,
    last_floor_arrival: 0,
    ...patch,
  };
}

describe('Веса тиров', () => {
  /**
   * Двухэтапный выбор (канон 2.3, `rollReward`): сперва тир по `TIER_WEIGHTS`,
   * потом предмет ВНУТРИ тира. `moduleWeight` — вес ВТОРОГО этапа, тир уже
   * выбран, поэтому он делится поровну между модулями пула БЕЗ множителя
   * `TIER_WEIGHTS`. Раньше здесь стоял `TIER_WEIGHTS.basic / pool.length` —
   * ровно та формула, которая одним шагом мешала выбор тира и выбор предмета
   * и превращала pity/анти-стокпайл одного модуля в сдвиг доли всего тира.
   */
  it('внутри тира вес делится между модулями поровну (без множителя тира)', () => {
    const base = moduleWeight('panel', ctx());
    expect(base).toBeCloseTo(1 / MODULE_TIER_POOL.basic.length);
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

  /**
   * Дефект: плоский одноэтапный выбор. Модификатор ОДНОГО предмета внутри
   * тира (здесь — pity на 'panel', удваивающий его вес) не имеет права
   * менять долю самого ТИРА basic среди всех роллов — иначе заявленные
   * каркасом веса тиров (62/33/5%) ломаются каждый раз, когда pity или
   * анти-стокпайл трогают хотя бы один предмет.
   *
   * Свойство, а не три примера: гоняем большую детерминированную серию роллов
   * (makeRng — тот же генератор, что использует симулятор баланса) и меряем
   * ФАКТИЧЕСКУЮ долю тира basic статистически. При старом плоском выборе
   * pity на 'panel' поднимал бы долю basic заметно выше 0.62 (потому что вес
   * panel внутри общей суммы всех модулей растет, а вместе с ним растет и
   * шанс, что ролл попадет в 'panel' ИЛИ 'frame' против 'sealant'/'filter'/
   * 'cable'/гейта); при верном двухэтапном выборе доля тира держится у
   * TIER_WEIGHTS.basic независимо от того, что происходит внутри тира.
   */
  it('свойство: pity/анти-стокпайл ОДНОГО предмета не двигают долю его ТИРА', () => {
    const N = 20000;
    const rng = makeRng(20260806);
    const skewed = ctx({
      need: { panel: 1 },
      pity: { panel: PITY_K },
      rng,
    });

    let basic_count = 0;
    for (let i = 0; i < N; i++) {
      const roll = rollArrival(1, skewed);
      if (MODULE_TIER_POOL.basic.includes(roll.modules[0]!)) basic_count += 1;
    }

    const share = basic_count / N;
    // gated закрыт -> нормировка идет по basic/rare (0.62/0.33), ожидаемая
    // доля basic = 0.62/0.95. Допуск с большим запасом от статистического шума
    // (std err ~ 0.0035 при N=20000), но узкий относительно сдвига, который
    // дал бы старый одноэтапный алгоритм (десятки процентных пунктов).
    const expected = TIER_WEIGHTS.basic / (TIER_WEIGHTS.basic + TIER_WEIGHTS.rare);
    expect(share).toBeGreaterThan(expected - 0.03);
    expect(share).toBeLessThan(expected + 0.03);
  });

  /**
   * Свойство: при отсутствии модификаторов оба модуля тира basic (panel,
   * frame) выпадают с равной частотой, а не по формуле, где первый в списке
   * систематически перевешивает.
   */
  it('свойство: без модификаторов модули внутри тира равновероятны', () => {
    const N = 20000;
    const rng = makeRng(555);
    const flat = ctx({ rng });

    const counts: Record<string, number> = {};
    for (let i = 0; i < N; i++) {
      const roll = rollArrival(1, flat);
      const id = roll.modules[0]!;
      if (MODULE_TIER_POOL.basic.includes(id)) counts[id] = (counts[id] ?? 0) + 1;
    }

    const [panel_count, frame_count] = MODULE_TIER_POOL.basic.map((id) => counts[id] ?? 0);
    const total = (panel_count ?? 0) + (frame_count ?? 0);
    expect(total).toBeGreaterThan(N * 0.5); // basic и правда доминирует ролл
    expect((panel_count ?? 0) / total).toBeGreaterThan(0.47);
    expect((panel_count ?? 0) / total).toBeLessThan(0.53);
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
  it('режет вес, когда СРЕДНЕЕ ЗА 24Ч превышает потребность вдвое', () => {
    const base = moduleWeight('cable', ctx({ need: { cable: 2 } }));
    const piled = moduleWeight(
      'cable',
      ctx({ need: { cable: 2 }, warehouse_avg_24h: { cable: 5 } }),
    );
    expect(piled).toBeCloseTo(base * ANTISTOCKPILE_FACTOR);
  });

  /**
   * Канон 2.8 («активных строек нет вообще... нет фрустрации без цели») и
   * псевдокод 2.3 (`if activeNeed > 0 and avgStock24h > ...`) требуют охрану
   * на нулевую потребность. Раньше здесь стояло ОБРАТНОЕ утверждение —
   * «при нулевой потребности любой запас считается излишком» — это и был
   * дефект: модуль, не нужный ни одной стройке, терял половину веса просто
   * за то, что лежит на складе.
   */
  it('при нулевой потребности вес НЕ режется — нет активной нужды', () => {
    const base = moduleWeight('cable', ctx());
    const piled = moduleWeight('cable', ctx({ warehouse_avg_24h: { cable: 1000 } }));
    expect(piled).toBeCloseTo(base);
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
      ctx({ need: { panel: recipe }, warehouse_avg_24h: { panel: recipe - 1 } }),
    );
    expect(almost).toBeCloseTo(base);
  });

  it('ровно двойной запас еще не излишек — порог строгий', () => {
    const base = moduleWeight('cable', ctx({ need: { cable: 2 } }));
    const at_threshold = moduleWeight(
      'cable',
      ctx({ need: { cable: 2 }, warehouse_avg_24h: { cable: 4 } }),
    );
    expect(at_threshold).toBeCloseTo(base);
  });

  /**
   * Дефект: анти-стокпайл читал мгновенный остаток (`stock`), а не среднее за
   * 24ч. Мгновенный остаток дает эксплойт «продал все прямо перед прибытием —
   * веса как у пустого склада» и одновременно ломает крайний случай «купил
   * с рук прямо перед прибытием — веса режутся мгновенно», хотя весь день
   * склад был пуст. Свойство проверяет решение по КАЖДОМУ модулю на
   * произвольном наборе (need, avg) — мгновенный `stock` в формулу вообще не
   * подставляется и заведомо не совпадает со средним.
   */
  it('свойство: решение зависит только от warehouse_avg_24h, не от мгновенного stock', () => {
    fc.assert(
      fc.property(
        fc.constantFrom(...ALL_MODULE_IDS.filter((id) => MODULES[id].tier !== 'gated')),
        fc.integer({ min: 0, max: 20 }),
        fc.integer({ min: 0, max: 200 }),
        fc.integer({ min: 0, max: 200 }), // мгновенный stock — заведомо другое число
        (module_id, needed, avg, instant_stock) => {
          const context = ctx({
            need: { [module_id]: needed },
            warehouse_avg_24h: { [module_id]: avg },
            stock: { [module_id]: instant_stock },
          });
          const weight = moduleWeight(module_id, context);
          const base = moduleWeight(module_id, ctx());

          const should_cut = needed > 0 && avg > needed * ANTISTOCKPILE_THRESHOLD;
          if (should_cut) expect(weight).toBeCloseTo(base * ANTISTOCKPILE_FACTOR);
          else expect(weight).toBeCloseTo(base);
        },
      ),
      { numRuns: 500 },
    );
  });
});

describe('И-11: floor guarantee', () => {
  const needy = construction({ deficit: { sealant: 6 } });

  it('первые прибытия форсируют гарантию поверх И-11', () => {
    for (let n = 1; n <= FRONT_LOADED_LUCK_ARRIVALS; n++) {
      const allowed = floorGuaranteeAllowedFor(needy, ctx({ arrival_no: n }), [
        'panel',
        'panel',
      ]);
      expect(allowed).toBe(true);
    }
  });

  it('после FTUE-окна одной неудачи мало: нужно окно из трех прибытий', () => {
    const at = (without: number) =>
      floorGuaranteeAllowedFor(
        construction({ ...needy, arrivals_without_needed: without }),
        ctx({ arrival_no: 20 }),
        ['panel'],
      );
    expect(at(0)).toBe(false);
    expect(at(1)).toBe(false);
    expect(at(2)).toBe(true);
  });

  it('не срабатывает, если прибытие и так дало нужный модуль', () => {
    const allowed = floorGuaranteeAllowedFor(
      construction({ ...needy, arrivals_without_needed: 5 }),
      ctx({ arrival_no: 20 }),
      ['sealant'],
    );
    expect(allowed).toBe(false);
  });

  /**
   * Раньше здесь стояла отдельная сверка `stockCoversNeed(ctx.stock, ctx.need)`
   * внутри роллера. Теперь `deficit` стройки СЧИТАЕТСЯ снаружи (`missingFor`,
   * учитывает и склад, и докупленное по И-12 — [[spec-prototype-build]] 8.19),
   * поэтому «склад уже покрывает потребность» — это просто «дефицита нет»:
   * пустой `deficit` дает пустой `needed_ids` и гарантия не находит, что чинить.
   */
  it('не срабатывает, если стройка уже покрыта — дефицита нет вовсе', () => {
    const allowed = floorGuaranteeAllowedFor(
      construction({ deficit: {}, arrivals_without_needed: 5 }),
      ctx({ arrival_no: 20 }),
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
    const allowed = floorGuaranteeAllowedFor(
      construction({
        deficit: { panel: 1 },
        arrivals_without_needed: FLOOR_GUARANTEE_WINDOW - 1,
      }),
      ctx({ arrival_no: 20 }),
      ['cable'],
    );
    expect(allowed).toBe(true);
  });

  /**
   * Канон 2.4: счетчик окна растет «на прибытие без нужного предмета». Модуль,
   * которого на складе уже хватает на рецепт (нет в `deficit`), стройку не
   * двигает — такое прибытие окно не закрывает.
   */
  it('контейнер с модулем, которого и так хватает, окно И-11 не закрывает', () => {
    // 'cable' не входит в deficit этой стройки — уже покрыт складом отдельно.
    const allowed = floorGuaranteeAllowedFor(
      construction({
        deficit: { panel: 6 },
        arrivals_without_needed: FLOOR_GUARANTEE_WINDOW - 1,
      }),
      ctx({ arrival_no: 20 }),
      ['cable'],
    );
    expect(allowed).toBe(true);
  });

  /**
   * Канон 2.6 п.3: форс-выдача не создает избыточный сток. Из нужных рецепту
   * модулей выдается самый дефицитный, а не самый крупный по рецепту: крупный
   * может уже лежать на складе целиком (здесь — sealant, покрыт и в deficit
   * не входит).
   */
  it('форс-выдача не выдает модуль, которого на складе уже хватает', () => {
    const panel = CONSTRUCTION_RECIPE.habitat_block.recipe.panel ?? 0;
    const roll = rollArrival(
      3,
      ctx({
        constructions: [construction({ deficit: { panel } })],
        arrival_no: 1,
        // rng фиксирован так, чтобы естественный ролл падал в редкий тир
        // (никогда не 'panel'): иначе тест проверял бы совпадение, а не форс.
        rng: () => 0.9,
      }),
    );
    expect(roll.floor_forced_slot).not.toBeNull();
    expect(roll.modules.at(-1)).toBe('panel');
  });

  it('не чаще раза на пять прибытий — эксплойт «держи стройку голодной»', () => {
    const at = (arrival: number) =>
      floorGuaranteeAllowedFor(
        construction({ ...needy, arrivals_without_needed: 5, last_floor_arrival: 20 }),
        ctx({ arrival_no: arrival }),
        ['panel'],
      );
    for (let gap = 1; gap < FLOOR_GUARANTEE_MIN_GAP; gap++) expect(at(20 + gap)).toBe(false);
    expect(at(20 + FLOOR_GUARANTEE_MIN_GAP)).toBe(true);
  });

  /**
   * Дефект: MIN_GAP считался от общего скаляра на игрока. Стройка A только
   * что получила форс-выдачу — это не имеет права блокировать стройку B,
   * у которой собственное окно истекло впервые (канон 2.6 п.2: «считается ОТ
   * ЭТОЙ ЖЕ стройки, не глобально по игроку»).
   */
  it('MIN_GAP считается по каждой стройке отдельно, а не общим таймером на игрока', () => {
    const b = construction({
      construction_id: 'warehouse_upgrade',
      deficit: { cable: 1 },
      arrivals_without_needed: FLOOR_GUARANTEE_WINDOW - 1,
      last_floor_arrival: 0, // у B гарантия еще ни разу не срабатывала
    });
    // Стройка A только что сработала на прибытии 10 — до B (MIN_GAP=5) не долетит.
    const allowed = floorGuaranteeAllowedFor(b, ctx({ arrival_no: 11 }), ['panel']);
    expect(allowed, 'MIN_GAP стройки A не должен блокировать стройку B').toBe(true);
  });

  it('никогда не выдает гейтовый тир, даже когда гейт открыт и модуль нужен', () => {
    const roll = rollArrival(
      3,
      ctx({
        constructions: [
          construction({ deficit: { drill_head: 4 }, arrivals_without_needed: 9 }),
        ],
        gated_open: true,
        arrival_no: 1,
      }),
    );
    expect(roll.floor_forced_slot).toBeNull();
  });

  it('переписывает отсек, а не добавляет: контейнеров ровно столько, сколько отсеков', () => {
    for (const count of [3, 4, 5]) {
      const roll = rollArrival(count, ctx({ constructions: [needy], arrival_no: 1 }));
      expect(roll.modules).toHaveLength(count);
    }
  });

  it('выдает самый дефицитный из нужных, а не первый попавшийся', () => {
    const roll = rollArrival(
      3,
      ctx({
        constructions: [construction({ deficit: { filter: 1, cable: 9 } })],
        arrival_no: 1,
      }),
    );
    expect(roll.floor_forced_slot).not.toBeNull();
    expect(roll.modules.at(-1)).toBe('cable');
  });

  it('после срабатывания фиксирует номер прибытия для следующего разрыва ЭТОЙ стройки', () => {
    const roll = rollArrival(3, ctx({ constructions: [needy], arrival_no: 2 }));
    expect(roll.next_constructions[0]?.last_floor_arrival).toBe(2);
  });

  it('счетчик пустых прибытий обнуляется, когда нужное пришло', () => {
    const roll = rollArrival(
      3,
      ctx({
        constructions: [construction({ deficit: { sealant: 6 }, arrivals_without_needed: 7 })],
        arrival_no: 1,
      }),
    );
    expect(roll.next_constructions[0]?.arrivals_without_needed).toBe(0);
  });

  /**
   * Дефект: окно И-11 держалось одним скаляром на игрока — две активные
   * стройки подряд делили один счетчик, и гарантия срабатывала не там, где
   * обещана (канон 2.6 п.2, 2.8: «каждая ведет свой независимый
   * floor_guarantee_window_counter/last_triggered_at»).
   *
   * Свойство, не три примера: гоняем произвольную последовательность
   * прибытий, где вторая стройка (B) присутствует в контексте не на каждом
   * прибытии (то есть игрок то переключается между стройками, то нет), и
   * сверяем траекторию первой стройки (A) с эталоном — той же
   * последовательностью прибытий, но БЕЗ стройки B вовсе. A и B просят
   * непересекающиеся модули (sealant/cable) и стоят в разных позициях
   * приоритета (A всегда первая), поэтому единственный способ эталону
   * разойтись с опытом — если состояние B хоть как-то протекает в состояние
   * A. При исправленном коде утечки нет: результат identical на каждом шаге.
   */
  it('свойство: окно И-11 одной стройки не зависит от присутствия другой', () => {
    fc.assert(
      fc.property(fc.array(fc.boolean(), { minLength: 5, maxLength: 40 }), (b_present) => {
        const rng = () => 0; // детерминированный ролл: всегда 'panel', никогда sealant/cable
        let a: ConstructionNeed = construction({
          construction_id: 'A',
          deficit: { sealant: 5 },
        });
        let a_reference: ConstructionNeed = construction({
          construction_id: 'A',
          deficit: { sealant: 5 },
        });
        let b: ConstructionNeed = construction({ construction_id: 'B', deficit: { cable: 5 } });

        for (let i = 0; i < b_present.length; i++) {
          const arrival_no = i + 1;

          const with_b = rollArrival(
            3,
            ctx({ arrival_no, constructions: b_present[i] ? [a, b] : [a], rng }),
          );
          const next_a = with_b.next_constructions.find((c) => c.construction_id === 'A')!;
          const next_b = with_b.next_constructions.find((c) => c.construction_id === 'B');

          const reference = rollArrival(
            3,
            ctx({ arrival_no, constructions: [a_reference], rng }),
          );
          const next_a_reference = reference.next_constructions.find(
            (c) => c.construction_id === 'A',
          )!;

          expect(next_a).toEqual(next_a_reference);

          a = next_a;
          a_reference = next_a_reference;
          if (b_present[i] && next_b) b = next_b;
        }
      }),
      { numRuns: 200 },
    );
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
