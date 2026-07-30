/**
 * Правила расчета докупки по [[tz-common-systems-mars]] раздел 5.
 *
 * Почему этот файл появился отдельно. Реализация округления полгода расходилась
 * со спекой в трех полосах лестницы из четырех, и ни один из 148 тестов не
 * покраснел: они проверяли отношения между ценами, а не сами цены. Отношения
 * пережили смену лестницы, ошибка выжила вместе с ними.
 *
 * Вывод, закрепленный этим файлом: если спека задает конкретные числа,
 * тест обязан проверять числа, а не «больше — меньше».
 */

import { describe, expect, it } from 'vitest';
import { SPEEDUP_FLOOR_ISOTOPES, SPEEDUP_RATE_ISOTOPES_PER_MIN } from '../config/economy';
import { ALL_GOOD_IDS, GOODS } from '../config/goods';
import {
  buyoutPrice,
  PURCHASE_MARGIN,
  roundToShowcase,
  rushCost,
  SPEEDUP_MARGIN_CEILING,
} from '../rushcost';
import { createWarehouse, deposit } from '../warehouse';

describe('5.4: витринная лестница округления', () => {
  it.each([
    // [значение, ожидаемая ступень] — ниже 50 шаг 5
    [12, 10],
    [13, 15],
    [47, 45],
    [49, 50],
    // 50-200 шаг 10
    [51, 50],
    [175, 180],
    [195, 200],
    // 200-1000 шаг 50
    [210, 200],
    [225, 250],
    [975, 1000],
    // выше 1000 шаг 100
    [1040, 1000],
    [1050, 1100],
  ])('%d округляется до %d', (value, expected) => {
    expect(roundToShowcase(value)).toBe(expected);
  });

  it('никогда не выдает ноль: бесплатная докупка была бы дырой', () => {
    // Спека молчит про нулевой случай, и roundToStep(2, 5) дал бы 0.
    for (const value of [0, 0.4, 1, 2, 2.5]) {
      expect(roundToShowcase(value)).toBeGreaterThan(0);
    }
  });
});

describe('5.2: склад вычитается из цепочки', () => {
  it('игрок не платит за то, что у него уже есть', () => {
    const empty = createWarehouse();
    const stocked = createWarehouse();
    deposit(stocked, 'cotton', 50);

    // Ткань-синт требует хлопок. С полным складом хлопка платим только за ткань.
    expect(rushCost('fabric', 1, stocked)).toBeLessThan(rushCost('fabric', 1, empty));
  });

  it('готовый товар на складе обнуляет всю ветку', () => {
    const stocked = createWarehouse();
    deposit(stocked, 'fabric', 10);
    expect(rushCost('fabric', 1, stocked)).toBe(0);
  });

  it('частичный запас уменьшает цену, но не до нуля', () => {
    const partial = createWarehouse();
    deposit(partial, 'fabric', 1);
    const price = rushCost('fabric', 3, partial);
    expect(price).toBeGreaterThan(0);
    expect(price).toBeLessThan(rushCost('fabric', 3));
  });

  it('зарезервированное под заказ не считается своим', () => {
    // available, а не qty: товар под другой заказ игроку недоступен.
    const w = createWarehouse();
    deposit(w, 'fabric', 5);
    const free = rushCost('fabric', 5, w);
    w.cells.fabric!.reserved = 5;
    expect(rushCost('fabric', 5, w)).toBeGreaterThan(free);
  });
});

describe('5.3: пол применяется на каждое звено, а не на итог', () => {
  it('дешевое звено стоит не меньше пола', () => {
    // Водоросли зреют 30 секунд: по ставке это 2.5 изотопа, пол 10.
    expect(rushCost('algae', 1)).toBe(SPEEDUP_FLOOR_ISOTOPES.crop);
  });

  it('цепочка из дешевых звеньев дороже, чем один пол', () => {
    // Если бы пол применялся к итогу, многозвенная цепочка стоила бы столько же,
    // сколько одно звено. Это и есть проверка «на каждое звено».
    expect(rushCost('jumpsuit', 1)).toBeGreaterThan(SPEEDUP_FLOOR_ISOTOPES.factory * 2);
  });

  it('дорогое звено считается по ставке, а не по полу', () => {
    const coffee = GOODS.coffee_beans;
    const by_rate = (coffee.prod_time_sec / 60) * SPEEDUP_RATE_ISOTOPES_PER_MIN.crop;
    expect(by_rate).toBeGreaterThan(SPEEDUP_FLOOR_ISOTOPES.crop);
    expect(rushCost('coffee_beans', 1)).toBeCloseTo(by_rate, 5);
  });
});

describe('5.5: коридор наценки', () => {
  it('наценка докупки лежит внутри разрешенного коридора', () => {
    expect(PURCHASE_MARGIN).toBeGreaterThanOrEqual(1.0);
    expect(PURCHASE_MARGIN).toBeLessThanOrEqual(SPEEDUP_MARGIN_CEILING);
  });

  it.each(ALL_GOOD_IDS)('%s: витринная цена не выходит за коридор', (id) => {
    const raw = rushCost(id, 1);
    const shown = buyoutPrice(id, 1);
    // Округление к витрине может подвинуть цену, но не за потолок коридора.
    expect(shown).toBeLessThanOrEqual(raw * SPEEDUP_MARGIN_CEILING + 5);
  });
});
