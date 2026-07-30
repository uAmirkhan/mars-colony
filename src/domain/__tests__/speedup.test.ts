/**
 * И-5: цена ускорения производства. Критерии приемки AC6 и AC7
 * [[tz-production-mars]].
 *
 * Файл появился в круге качества 2. Формулы не было в коде вообще, при этом
 * тест страховки от разъезда имен был зеленым: константы ставки и пола в коде
 * присутствовали — их держали расчет докупки и скип шаттла. Имя-приманка
 * закрывало дыру, которую страховка обязана была показать.
 *
 * Отсюда правило, закрепленное этим файлом: проверка на присутствие имени
 * слабее проверки на присутствие поведения. Параметр считается реализованным,
 * когда есть тест на то, что он делает, а не когда строка встречается в коде.
 */

import { describe, expect, it } from 'vitest';
import {
  productionSpeedupCost,
  SPEEDUP_FLOOR_ISOTOPES,
  SPEEDUP_FREE_THRESHOLD_SEC,
  SPEEDUP_RATE_ISOTOPES_PER_MIN,
} from '../config/economy';

describe('AC6: цена считается по остатку, а не по полному циклу', () => {
  it('минуты округляются вниз — неполная минута не продается', () => {
    // 5 минут 59 секунд у фабрики: платим за 5 минут, не за 6.
    expect(productionSpeedupCost(359, 'factory')).toBe(
      5 * SPEEDUP_RATE_ISOTOPES_PER_MIN.factory,
    );
    expect(productionSpeedupCost(360, 'factory')).toBe(
      6 * SPEEDUP_RATE_ISOTOPES_PER_MIN.factory,
    );
  });

  it('цена падает по мере созревания', () => {
    const prices = [600, 480, 300, 120].map((sec) => productionSpeedupCost(sec, 'crop'));
    for (let i = 1; i < prices.length; i++) {
      expect(prices[i]!).toBeLessThan(prices[i - 1]!);
    }
  });

  it('фабрика дороже грядки при равном остатке', () => {
    // Ставки 6 против 5: переработка ценнее, значит и ускорять ее дороже.
    expect(productionSpeedupCost(600, 'factory')).toBeGreaterThan(
      productionSpeedupCost(600, 'crop'),
    );
  });

  it('ниже точки безубыточности цена упирается в пол и перестает падать', () => {
    // Пол 10 при ставке 5 дает breakeven на двух минутах.
    expect(productionSpeedupCost(119, 'crop')).toBe(SPEEDUP_FLOOR_ISOTOPES.crop);
    expect(productionSpeedupCost(90, 'crop')).toBe(SPEEDUP_FLOOR_ISOTOPES.crop);
    expect(productionSpeedupCost(60, 'crop')).toBe(SPEEDUP_FLOOR_ISOTOPES.crop);
  });
});

describe('AC7: ниже порога ускорение бесплатно', () => {
  it('на пороге и ниже цена равна нулю', () => {
    expect(productionSpeedupCost(SPEEDUP_FREE_THRESHOLD_SEC, 'crop')).toBe(0);
    expect(productionSpeedupCost(SPEEDUP_FREE_THRESHOLD_SEC, 'factory')).toBe(0);
    expect(productionSpeedupCost(10, 'crop')).toBe(0);
    expect(productionSpeedupCost(1, 'factory')).toBe(0);
  });

  it('на секунду выше порога цена уже не нулевая', () => {
    // Граница обязана быть резкой и проверяемой: иначе бесплатная зона
    // незаметно расползется при следующей правке порога.
    expect(productionSpeedupCost(SPEEDUP_FREE_THRESHOLD_SEC + 1, 'crop')).toBeGreaterThan(0);
  });

  it('нулевой и отрицательный остаток тоже бесплатны', () => {
    // Цикл уже готов, платить не за что. Отрицательное значение возможно,
    // если клиент отстал от серверного времени.
    expect(productionSpeedupCost(0, 'crop')).toBe(0);
    expect(productionSpeedupCost(-5, 'factory')).toBe(0);
  });

  it('бесплатная зона не переходит в отрицательную цену', () => {
    for (let sec = -60; sec <= 120; sec += 7) {
      expect(productionSpeedupCost(sec, 'crop')).toBeGreaterThanOrEqual(0);
    }
  });
});

describe('Порог согласован с полом', () => {
  it('порог лежит ниже точки безубыточности, иначе пол недостижим', () => {
    // Если бы бесплатная зона начиналась выше breakeven, цена пола не
    // показалась бы игроку ни разу — параметр существовал бы впустую.
    const breakeven_sec =
      (SPEEDUP_FLOOR_ISOTOPES.crop / SPEEDUP_RATE_ISOTOPES_PER_MIN.crop) * 60;
    expect(SPEEDUP_FREE_THRESHOLD_SEC).toBeLessThan(breakeven_sec);
  });
});
