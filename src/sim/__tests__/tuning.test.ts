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

  it('дорогой посев тормозит экономику', () => {
    const cheap = simulate({
      ...base,
      tuning: { ...DEFAULT_TUNING, plant_cost_price_share: 0.1 },
    });
    const dear = simulate({
      ...base,
      tuning: { ...DEFAULT_TUNING, plant_cost_price_share: 0.9 },
    });
    expect(dear.rows.at(-1)?.credits).toBeLessThan(cheap.rows.at(-1)?.credits ?? 0);
  });

  it('низкий коэффициент продажи снижает доход', () => {
    const full = simulate({ ...base, tuning: { ...DEFAULT_TUNING, sell_price_ratio: 1.0 } });
    const half = simulate({ ...base, tuning: { ...DEFAULT_TUNING, sell_price_ratio: 0.4 } });
    expect(half.rows.at(-1)?.credits).toBeLessThan(full.rows.at(-1)?.credits ?? 0);
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
