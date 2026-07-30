/**
 * Проверка, что настройки реально влияют на прогон.
 *
 * Без этих тестов ползунки в интерфейсе могут молча ничего не делать: параметр
 * прокинут, но где-то в цепочке используется значение конфига. Симулятор при
 * этом честно нарисует график — просто всегда один и тот же.
 */

import { describe, expect, it } from 'vitest';
import { DEFAULT_TUNING } from '../../domain/tuning';
import { DEFAULT_SIM, simulate } from '../simulate';

const base = { ...DEFAULT_SIM, days: 30 };

describe('Настройки влияют на результат прогона', () => {
  it('на умолчаниях прогон воспроизводится по seed', () => {
    const a = simulate(base);
    const b = simulate(base);
    expect(a.rows).toEqual(b.rows);
  });

  it('крутая XP-кривая замедляет прогрессию', () => {
    const soft = simulate({ ...base, tuning: { ...DEFAULT_TUNING, xp_curve_exponent: 1.2 } });
    const hard = simulate({ ...base, tuning: { ...DEFAULT_TUNING, xp_curve_exponent: 1.6 } });
    expect(hard.final_level).toBeLessThan(soft.final_level);
  });

  it('дорогой посев съедает деньги, которые ушли бы в стройку', () => {
    const cheap = simulate({
      ...base,
      tuning: { ...DEFAULT_TUNING, plant_cost_price_share: 0.1 },
    });
    const dear = simulate({
      ...base,
      tuning: { ...DEFAULT_TUNING, plant_cost_price_share: 0.9 },
    });
    // Сравниваем то, на что сток влияет напрямую, а не опыт.
    //
    // Написание этого теста вскрыло факт баланса: на легком профиле цена посева
    // почти не двигает опыт — разница между долей 0.1 и 0.9 меньше процента,
    // и знак случайный. Узкое место там не деньги, а время сессии и склад.
    // Проверять «дорогой посев тормозит прогресс» на этом профиле бессмысленно:
    // утверждение попросту неверно, и тест был бы зеленым по совпадению.
    expect(dear.planting_starved).toBeGreaterThanOrEqual(cheap.planting_starved);
    expect(dear.credits_spent_on_buildings).toBeLessThanOrEqual(
      cheap.credits_spent_on_buildings,
    );
  });

  /**
   * ПРАВИЛО, выведенное дважды и потому записанное здесь явно.
   *
   * Остаток кредитов НЕ является мерой дохода, как только в экономике есть
   * стоки. Бедный игрок не может позволить себе следующее здание и остается
   * с деньгами на руках; богатый тратит и остается почти без них. Сравнение
   * остатков дает обратный знак и зеленый тест при сломанной экономике.
   *
   * Меру дохода искать в прогрессии (уровень, XP) или в потраченном на стоки.
   */
  it('низкий коэффициент продажи снижает доход', () => {
    const full = simulate({ ...base, tuning: { ...DEFAULT_TUNING, sell_price_ratio: 1.0 } });
    const half = simulate({ ...base, tuning: { ...DEFAULT_TUNING, sell_price_ratio: 0.4 } });
    // Меру дохода берем в потраченном на стоки и в прогрессии, а не в остатке:
    // бедный игрок не может позволить себе следующее здание и сидит с деньгами,
    // богатый тратит и остается почти без них. Сравнение остатков дает
    // обратный знак и зеленый тест при сломанной экономике.
    expect(half.credits_spent_on_buildings).toBeLessThan(full.credits_spent_on_buildings);
    expect(half.final_level).toBeLessThanOrEqual(full.final_level);
  });

  it('тесный склад чаще блокирует сбор', () => {
    const tight = simulate({
      ...base,
      tuning: { ...DEFAULT_TUNING, warehouse_start_capacity: 20 },
    });
    const roomy = simulate({
      ...base,
      tuning: { ...DEFAULT_TUNING, warehouse_start_capacity: 200 },
    });
    expect(tight.warehouse_blocks).toBeGreaterThan(roomy.warehouse_blocks);
  });

  it('щедрые награды за уровень ускоряют накопление кредитов', () => {
    const stingy = simulate({
      ...base,
      tuning: { ...DEFAULT_TUNING, level_up_credits_coef: 20 },
    });
    const generous = simulate({
      ...base,
      tuning: { ...DEFAULT_TUNING, level_up_credits_coef: 300 },
    });
    expect(generous.rows.at(-1)?.credits).toBeGreaterThan(stingy.rows.at(-1)?.credits ?? 0);
  });
});

describe('Симулятор ловит поломку экономики настройками', () => {
  it('бесплатный посев при пустом кармане не нужен — анти-софтлок молчит', () => {
    const result = simulate({
      ...base,
      tuning: { ...DEFAULT_TUNING, plant_cost_price_share: 0, plant_cost_floor: 0 },
    });
    expect(result.softlock_rescues).toBe(0);
  });

  it('запредельно дорогой посев делает экономику убыточной, и это видно числом', () => {
    // Ради этого симулятор и существует: поломка обязана быть заметной числом,
    // а не проявиться на плейтесте через неделю.
    //
    // Анти-софтлок здесь молчит намеренно и правильно: у игрока что-то растет,
    // значит формально тупика нет. Но сеять он не может — каждый цикл убыточен.
    // Ловит это отдельный счетчик, и он для панели инвариантов важнее И-15.
    const broken = simulate({
      ...base,
      tuning: { ...DEFAULT_TUNING, plant_cost_price_share: 1, plant_cost_floor: 10 },
    });
    const healthy = simulate(base);

    expect(broken.planting_starved).toBeGreaterThan(0);
    expect(healthy.planting_starved).toBe(0);
    expect(broken.final_level).toBeLessThan(healthy.final_level);
  });
});

describe('Кредитные стоки: деньги обязаны тратиться', () => {
  /**
   * Целевой профиль каркаса: 4 захода по 20 минут, около 9 часов в неделю.
   * `base` — легкий профиль, а вопрос ТЗ 5.4 про два тяжелых чека поставлен
   * именно для целевого. Легкий до текстильного модуля не доходит за 30 дней,
   * и это отдельная находка, а не поломка теста.
   */
  const target = {
    ...base,
    session_starts_min: [480, 760, 1040, 1320],
    session_length_min: 20,
  };

  it('кредиты не копятся мертвым грузом', () => {
    // Пока симулятор знал один сток из трех, он показывал 24 тысячи на руках
    // к 30-му дню и выглядел здоровым. Экономика, где заработанное некуда деть,
    // сломана — но без этой проверки поломка невидима.
    const r = simulate(target);
    const left = r.rows.at(-1)?.credits ?? 0;
    expect(r.credits_spent_on_buildings).toBeGreaterThan(left * 3);
  });

  it('целевой профиль доходит до всех четырех зданий', () => {
    const r = simulate(target);
    expect(r.buildings_owned).toContain('food_module');
    expect(r.buildings_owned).toContain('atmospheric_module');
    expect(r.buildings_owned).toContain('textile_module');
    expect(r.buildings_owned).toContain('mining_site');
  });

  it('два тяжелых чека не слипаются в один барьер', () => {
    // ТЗ производства 5.4 предупреждало: 4000 и 5500 открываются на соседних
    // уровнях и могут встать стеной. Проверяем, что игрок берет оба и при этом
    // не остается без оборотных средств.
    const r = simulate(target);
    const left = r.rows.at(-1)?.credits ?? 0;
    expect(r.buildings_owned.length).toBe(4);
    expect(left).toBeGreaterThan(0);
    // Одна заминка с посевом за 30 дней появилась вместе с буровой: чек в 1200
    // кредитов на шестом уровне выгребает кассу почти досуха. Это ощущается
    // как «накопил и потратил», а не как тупик, поэтому терпимо — но потолок
    // ставим низкий, чтобы отследить, если заминок станет много.
    expect(r.planting_starved).toBeLessThanOrEqual(2);
  });

  it('добыча закрыла дыру легкого профиля: текстильный модуль стал достижим', () => {
    // Круг 2 зафиксировал как факт баланса: при 4 часах в неделю третье здание
    // за месяц недостижимо, рецепты ткани и комбинезона не открываются вообще.
    //
    // Добыча это починила, и починила побочно — ее добавляли ради согласия
    // мира с экономикой, а не ради баланса. Причина: буровая дает доход, не
    // требуя ни кредитов на посев, ни слота грядки, то есть работает и тогда,
    // когда игрок не заходит.
    //
    // Тест оставлен и перевернут намеренно: если добычу когда-нибудь ослабят,
    // дыра вернется молча, и без этой проверки об этом никто не узнает.
    const r = simulate(base);
    expect(r.buildings_owned).toContain('textile_module');
  });

  it('расширения купола реально покупаются, а не остаются на бумаге', () => {
    const r = simulate(target);
    expect(r.dome_expansions).toBeGreaterThan(0);
  });
});

describe('Достижимость товаров: по потолку и на практике — разные вещи', () => {
  it('целевой профиль за 30 дней не открывает часть товарного субстрата', () => {
    // Все двенадцать товаров открываются ниже потолка MVP (21), и по этому
    // признаку выглядят доступными. Но целевой профиль за месяц доходит до
    // одиннадцатого уровня — значит кофе-бобы (12) и кофе-паек (13) игрок
    // не увидит вообще, а комбинезон (11) откроется в последний день.
    //
    // Тест не объявляет это дефектом: месяц не предел жизни игрока. Он не дает
    // факту потеряться, потому что «товар есть в конфиге» читается как
    // «товар в игре есть», а это не одно и то же.
    const target = {
      ...base,
      session_starts_min: [480, 760, 1040, 1320],
      session_length_min: 20,
    };
    const r = simulate(target);
    expect(r.final_level).toBeLessThan(13);
    expect(r.final_level).toBeGreaterThanOrEqual(10);
  });
});
