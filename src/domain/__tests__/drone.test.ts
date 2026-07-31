/**
 * Дрон-курьер: доска заказов, погрузка, отправка, выброс.
 * Критерии приемки — [[tz-drone-mars]] раздел 9, генератор — раздел 4.
 */

import { describe, expect, it } from 'vitest';
import { makeRng } from '../../sim/rng';
import {
  COVERAGE_MIN,
  DRONE_PREMIUM_RANGE,
  DRONE_REFRESH_FREE_SEC,
  droneRefreshPrice,
  EASY_PRODUCE_MAX_MIN,
  MAX_DEFICIT_SLOTS,
  TRANSPORT_XP_K,
} from '../config/economy';
import { GOODS } from '../config/goods';
import {
  availableGoodsFor,
  canFulfillNow,
  discardOrder,
  generateOrder,
  loadPosition,
  type OrderPosition,
  orderReward,
  positionCovered,
  positionsCountFor,
  releaseReserved,
  sendOrder,
  slotsAtLevel,
} from '../drone';
import { availableOf, createWarehouse, deposit, qtyOf, reserve, totalQty } from '../warehouse';

const pos = (
  good_id: Parameters<typeof orderReward>[0][number]['good_id'],
  qty: number,
): OrderPosition => ({
  good_id,
  qty,
  filled: false,
});

describe('4.1: число слотов растет с уровнем', () => {
  it.each([
    [2, 3],
    [3, 3],
    [4, 4],
    [6, 5],
    [8, 6],
    [10, 7],
    [12, 8],
    [15, 9],
    [21, 9],
  ])('уровень %d дает %d слотов', (level, expected) => {
    expect(slotsAtLevel(level)).toBe(expected);
  });

  it('потолок девять, выше не растет', () => {
    expect(slotsAtLevel(100)).toBe(9);
  });
});

describe('4.2.1: веса числа позиций', () => {
  it('на втором уровне заказ не бывает длиннее двух позиций', () => {
    for (let roll = 0; roll <= 1; roll += 0.05) {
      expect(positionsCountFor(2, roll)).toBeLessThanOrEqual(2);
    }
  });

  it('на пятнадцатом заказ не бывает короче четырех', () => {
    for (let roll = 0; roll <= 1; roll += 0.05) {
      expect(positionsCountFor(15, roll)).toBeGreaterThanOrEqual(4);
    }
  });

  it('среднее число позиций растет с уровнем', () => {
    const mean = (level: number) => {
      let sum = 0;
      for (let i = 0; i < 100; i++) sum += positionsCountFor(level, i / 100);
      return sum / 100;
    };
    expect(mean(15)).toBeGreaterThan(mean(8));
    expect(mean(8)).toBeGreaterThan(mean(2));
  });
});

describe('4.3: награда', () => {
  it('премия держится в диапазоне каркаса при любом составе', () => {
    const cases: OrderPosition[][] = [
      [pos('algae', 5)],
      [
        pos('algae', 3),
        pos('soy', 3),
        pos('mushrooms', 2),
        pos('tomatoes', 2),
        pos('cotton', 2),
      ],
      [pos('jumpsuit', 1)],
      [pos('protein_bar', 2), pos('fabric', 1)],
    ];
    for (const positions of cases) {
      for (const jitter of [0, 0.5, 1]) {
        const { premium } = orderReward(positions, jitter);
        expect(premium).toBeGreaterThanOrEqual(1 + DRONE_PREMIUM_RANGE.min);
        expect(premium).toBeLessThanOrEqual(1 + DRONE_PREMIUM_RANGE.max);
      }
    }
  });

  it('заказ с фабричным товаром платит премию выше, чем такой же из грядок', () => {
    const factory = orderReward([pos('protein_bar', 2)], 0.5).premium;
    const crops = orderReward([pos('algae', 2)], 0.5).premium;
    expect(factory).toBeGreaterThan(crops);
  });

  it('короткий заказ платит премию выше длинного', () => {
    const short = orderReward([pos('soy', 4), pos('mushrooms', 3)], 0.5).premium;
    const long = orderReward(
      [
        pos('soy', 2),
        pos('mushrooms', 2),
        pos('algae', 2),
        pos('tomatoes', 2),
        pos('cotton', 2),
      ],
      0.5,
    ).premium;
    expect(short).toBeGreaterThan(long);
  });

  it('И-3: XP считается по коэффициенту дрона, а не по производственному', () => {
    const positions = [pos('mushrooms', 3)];
    const { xp } = orderReward(positions, 0.5);
    expect(xp).toBe(GOODS.mushrooms.base_xp * TRANSPORT_XP_K.drone * 3);
  });

  it('награда всегда выше рыночной стоимости — иначе заказ бессмысленен', () => {
    const positions = [pos('soy', 5), pos('mushrooms', 3)];
    const market = positions.reduce((s, p) => s + GOODS[p.good_id].price * p.qty, 0);
    expect(orderReward(positions, 0.5).credits).toBeGreaterThan(market);
  });
});

describe('Генератор: инварианты анти-фрустрации', () => {
  const goods = availableGoodsFor(9, new Set(['food_module']));

  it('заказ никогда не пустой', () => {
    const rng = makeRng(1);
    for (let i = 0; i < 50; i++) {
      const w = createWarehouse();
      deposit(w, 'algae', 20);
      const order = generateOrder(0, {
        level: 9,
        warehouse: w,
        available_goods: goods,
        board: [],
        rng,
      });
      expect(order.positions.length).toBeGreaterThan(0);
    }
  });

  /**
   * Регрессия, найденная замером распределения. При пустом складе генератор
   * считал дефицитной каждую позицию, тратил бюджет дефицита на первой и
   * выбрасывал остальные — вся доска состояла из однопозиционных заказов.
   * Тестов это не роняло: заказ был непустым и инвариант И-8 формально держался.
   */
  it('пустой склад не вырождает заказ в одну позицию', () => {
    const rng = makeRng(3);
    let multi = 0;
    for (let i = 0; i < 60; i++) {
      const order = generateOrder(0, {
        level: 9,
        warehouse: createWarehouse(),
        available_goods: goods,
        board: [],
        rng,
      });
      if (order.positions.length > 1) multi += 1;
    }
    expect(multi).toBeGreaterThan(50);
  });

  it('И-8: не больше одной дефицитной позиции на заказ', () => {
    const rng = makeRng(7);
    for (let i = 0; i < 60; i++) {
      const w = createWarehouse();
      deposit(w, 'algae', 10);
      deposit(w, 'soy', 10);
      const order = generateOrder(0, {
        level: 9,
        warehouse: w,
        available_goods: goods,
        board: [],
        rng,
      });
      // Дефицит по каркасу — это преднамеренный пинч «+1..+3 сверх склада»,
      // а не все, чего нет на полке. Позиция, которую игрок вырастит за
      // полчаса, дефицитом не считается: И-8 прямо пишет «покрыто складом
      // ИЛИ производимо <=30 мин». Прежняя редакция теста меряла первое
      // условие и молчала о втором, поэтому пропустила вырождение заказа.
      const deficit = order.positions.filter(
        (p) =>
          p.qty > availableOf(w, p.good_id) &&
          GOODS[p.good_id].prod_time_sec > EASY_PRODUCE_MAX_MIN.drone * 60,
      ).length;
      expect(deficit).toBeLessThanOrEqual(MAX_DEFICIT_SLOTS);
    }
  });

  it('на полном складе покрытие держит планку И-8', () => {
    const rng = makeRng(3);
    const w = createWarehouse(300);
    for (const id of goods) deposit(w, id, 20);

    let ok = 0;
    const runs = 40;
    for (let i = 0; i < runs; i++) {
      const order = generateOrder(0, {
        level: 9,
        warehouse: w,
        available_goods: goods,
        board: [],
        rng,
      });
      const easy = order.positions.filter((p) => availableOf(w, p.good_id) >= p.qty).length;
      if (easy / order.positions.length >= COVERAGE_MIN.drone) ok += 1;
    }
    // Не требуем ста процентов: попытки генератора конечны намеренно, заказ
    // выдается даже если ни одна не прошла. Пустая доска хуже неидеальной.
    expect(ok / runs).toBeGreaterThan(0.8);
  });

  it('одна и та же позиция не дублируется внутри заказа', () => {
    const rng = makeRng(11);
    for (let i = 0; i < 40; i++) {
      const w = createWarehouse();
      deposit(w, 'algae', 15);
      const order = generateOrder(0, {
        level: 15,
        warehouse: w,
        available_goods: goods,
        board: [],
        rng,
      });
      const ids = order.positions.map((p) => p.good_id);
      expect(new Set(ids).size).toBe(ids.length);
    }
  });
});

describe('Погрузка и отправка', () => {
  it('погрузка резервирует товар, но не убирает его со склада', () => {
    const w = createWarehouse();
    deposit(w, 'soy', 10);
    const order = {
      idx: 0,
      state: 'active' as const,
      npc_name: 'тест',
      positions: [pos('soy', 4)],
      credits_reward: 100,
      xp_reward: 10,
      refresh_at: 0,
    };

    expect(loadPosition(order, 0, w)).toBe(true);
    expect(qtyOf(w, 'soy')).toBe(10); // физически на месте
    expect(availableOf(w, 'soy')).toBe(6); // но недоступно
    expect(order.state).toBe('ready');
  });

  it('без товара погрузка не проходит и состояние не меняется', () => {
    const w = createWarehouse();
    deposit(w, 'soy', 2);
    const order = {
      idx: 0,
      state: 'active' as const,
      npc_name: 'тест',
      positions: [pos('soy', 5)],
      credits_reward: 100,
      xp_reward: 10,
      refresh_at: 0,
    };

    expect(loadPosition(order, 0, w)).toBe(false);
    expect(order.state).toBe('active');
    expect(availableOf(w, 'soy')).toBe(2);
  });

  it('выполнение частями: заказ ждет между визитами', () => {
    const w = createWarehouse();
    deposit(w, 'soy', 5);
    const order = {
      idx: 0,
      state: 'active' as const,
      npc_name: 'тест',
      positions: [pos('soy', 4), pos('mushrooms', 2)],
      credits_reward: 100,
      xp_reward: 10,
      refresh_at: 0,
    };

    loadPosition(order, 0, w);
    expect(order.state).toBe('in_progress'); // первая позиция закрыта, вторая нет

    deposit(w, 'mushrooms', 3); // игрок сходил и вырастил
    loadPosition(order, 1, w);
    expect(order.state).toBe('ready');
  });

  it('отправка убирает зарезервированное и выдает награду', () => {
    const w = createWarehouse();
    deposit(w, 'soy', 10);
    const order = {
      idx: 0,
      state: 'active' as const,
      npc_name: 'тест',
      positions: [pos('soy', 4)],
      credits_reward: 250,
      xp_reward: 30,
      refresh_at: 0,
    };
    loadPosition(order, 0, w);

    const result = sendOrder(order, w);
    expect(result.ok).toBe(true);
    expect(result.credits).toBe(250);
    expect(qtyOf(w, 'soy')).toBe(6);
    expect(totalQty(w)).toBe(6); // место освободилось
  });

  it('недогруженный заказ отправить нельзя', () => {
    const w = createWarehouse();
    const order = {
      idx: 0,
      state: 'active' as const,
      npc_name: 'тест',
      positions: [pos('soy', 4)],
      credits_reward: 250,
      xp_reward: 30,
      refresh_at: 0,
    };
    expect(sendOrder(order, w).ok).toBe(false);
  });
});

describe('Выброс: отказ обязан быть дешевым', () => {
  it('выброс возвращает погруженное на склад', () => {
    // Иначе отказ перестает быть дешевым: игрок теряет товар за то,
    // что передумал, — и перестает пользоваться выбросом вообще.
    const w = createWarehouse();
    deposit(w, 'soy', 10);
    const order = {
      idx: 0,
      state: 'active' as const,
      npc_name: 'тест',
      positions: [pos('soy', 4)],
      credits_reward: 100,
      xp_reward: 10,
      refresh_at: 0,
    };
    loadPosition(order, 0, w);
    expect(availableOf(w, 'soy')).toBe(6);

    releaseReserved(order, w);
    discardOrder(order, 1000);

    expect(availableOf(w, 'soy')).toBe(10);
    expect(order.state).toBe('empty_cooldown');
    expect(order.refresh_at).toBe(1000 + DRONE_REFRESH_FREE_SEC);
  });

  it('цена мгновенного рефреша падает по мере истечения таймера', () => {
    const prices = [22, 12, 5, 1].map((min) => droneRefreshPrice(min * 60));
    for (let i = 1; i < prices.length; i++) {
      expect(prices[i]!).toBeLessThan(prices[i - 1]!);
    }
  });

  it('бесплатный путь существует: по истечении таймера цена ноль', () => {
    expect(droneRefreshPrice(0)).toBe(0);
  });
});

describe('Подсветка доски: можно ли закрыть заказ прямо сейчас', () => {
  const make = (positions: OrderPosition[]) => ({
    idx: 0,
    state: 'active' as const,
    npc_name: 'тест',
    positions,
    credits_reward: 100,
    xp_reward: 10,
    refresh_at: 0,
  });

  it('склад покрывает все позиции — заказ подсвечивается', () => {
    const w = createWarehouse();
    deposit(w, 'soy', 10);
    deposit(w, 'mushrooms', 5);
    expect(canFulfillNow(make([pos('soy', 4), pos('mushrooms', 2)]), w)).toBe(true);
  });

  it('не хватает хотя бы одной позиции — не подсвечивается', () => {
    const w = createWarehouse();
    deposit(w, 'soy', 10);
    deposit(w, 'mushrooms', 1);
    expect(canFulfillNow(make([pos('soy', 4), pos('mushrooms', 2)]), w)).toBe(false);
  });

  it('две позиции одного товара считаются вместе, а не по отдельности', () => {
    // Пять на складе, две позиции по три. Каждая по отдельности проходит,
    // вместе — нет. Проверка по одной позиции подсветила бы заказ, который
    // закрыть невозможно, и это худший вид подсказки: она врет.
    const w = createWarehouse();
    deposit(w, 'soy', 5);
    expect(canFulfillNow(make([pos('soy', 3), pos('soy', 3)]), w)).toBe(false);
  });

  it('зарезервированное под другой заказ своим не считается', () => {
    const w = createWarehouse();
    deposit(w, 'soy', 5);
    reserve(w, 'soy', 3);
    expect(canFulfillNow(make([pos('soy', 4)]), w)).toBe(false);
  });

  it('уже погруженные позиции из проверки исключаются', () => {
    const w = createWarehouse();
    deposit(w, 'soy', 10);
    const order = make([pos('soy', 4), pos('mushrooms', 2)]);
    loadPosition(order, 0, w); // соя погружена, грибов на складе нет
    expect(canFulfillNow(order, w)).toBe(false);

    deposit(w, 'mushrooms', 2);
    expect(canFulfillNow(order, w)).toBe(true);
  });

  it('выброшенный слот не подсвечивается никогда', () => {
    const w = createWarehouse(300);
    deposit(w, 'soy', 100);
    const order = make([pos('soy', 1)]);
    discardOrder(order, 0);
    expect(canFulfillNow(order, w)).toBe(false);
  });
});

describe('Счетчик позиции: цвет обязан совпадать с возможностью погрузить', () => {
  it('хватает — зеленый', () => {
    const w = createWarehouse();
    deposit(w, 'soy', 5);
    expect(positionCovered(pos('soy', 5), w)).toBe(true);
  });

  it('не хватает — не зеленый, даже если чуть-чуть', () => {
    // «3/5» зеленым — противоречие само по себе: три из пяти это нехватка.
    const w = createWarehouse();
    deposit(w, 'soy', 3);
    expect(positionCovered(pos('soy', 5), w)).toBe(false);
  });

  it('погруженная позиция считается покрытой независимо от склада', () => {
    const w = createWarehouse();
    deposit(w, 'soy', 4);
    const order = {
      idx: 0,
      state: 'active' as const,
      npc_name: 'тест',
      positions: [pos('soy', 4)],
      credits_reward: 0,
      xp_reward: 0,
      refresh_at: 0,
    };
    loadPosition(order, 0, w);
    // Товар зарезервирован, available упал до нуля — но позиция уже закрыта.
    expect(availableOf(w, 'soy')).toBe(0);
    expect(positionCovered(order.positions[0]!, w)).toBe(true);
  });

  it('цвет счетчика согласован с подсветкой карточки', () => {
    // Все позиции зеленые тогда и только тогда, когда карточка подсвечена.
    const w = createWarehouse();
    deposit(w, 'soy', 10);
    deposit(w, 'mushrooms', 1);
    const order = {
      idx: 0,
      state: 'active' as const,
      npc_name: 'тест',
      positions: [pos('soy', 4), pos('mushrooms', 3)],
      credits_reward: 0,
      xp_reward: 0,
      refresh_at: 0,
    };
    const all_green = order.positions.every((p) => positionCovered(p, w));
    expect(all_green).toBe(canFulfillNow(order, w));
  });
});
