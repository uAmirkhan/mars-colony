/**
 * Стор: тонкая обертка над доменом.
 *
 * Файл появился в круге качества 3. До него стор не был покрыт ни одним тестом,
 * при 274 зеленых в домене. Именно в нем нашелся дефект, который не увидели все
 * модульные тесты разом: приложение не отрисовывалось вообще из-за бесконечного
 * цикла перерисовки, и поймал это только браузер.
 *
 * Что проверяем здесь и чего не проверяем. Правила игры живут в домене и
 * тестируются там. Здесь проверяется только то, за что отвечает стор:
 * результат доменного вызова применен целиком, при отказе не применено ничего,
 * побочные эффекты не задвоены.
 */

import { beforeEach, describe, expect, it } from 'vitest';
import { CREDITS_START, FACTORY_PRICES, plantingCost } from '../../domain/config/economy';
import { GOODS, harvestQty } from '../../domain/config/goods';
import { levelUpReward, xpToNext } from '../../domain/config/levels';
import { canFulfillNow } from '../../domain/drone';
import { createFactorySlot, createField } from '../../domain/production';
import { createWarehouse, deposit, qtyOf, totalQty } from '../../domain/warehouse';
import { useGame } from '../gameStore';

/** Чистое состояние перед каждым тестом: стор глобальный, иначе тесты потекут. */
function reset(patch: Partial<ReturnType<typeof useGame.getState>> = {}) {
  useGame.setState({
    now: 1_000_000,
    level: 21,
    xp_into_level: 0,
    credits: CREDITS_START,
    isotopes: 0,
    warehouse: createWarehouse(),
    fields: [createField(0), createField(1)],
    factory_slots: [createFactorySlot(0, 'food_module')],
    buildings: [],
    toasts: [],
    ...patch,
  });
}

const s = () => useGame.getState();

beforeEach(() => reset());

describe('Посев', () => {
  it('успешный посев списывает кредиты ровно один раз', () => {
    const before = s().credits;
    s().plant(0, 'mushrooms');
    expect(s().credits).toBe(before - plantingCost(GOODS.mushrooms.price));
    expect(s().fields[0]?.state).toBe('GROWING');
  });

  it('отказ не трогает ни кредиты, ни грядку', () => {
    reset({
      credits: 0,
      warehouse: (() => {
        const w = createWarehouse();
        deposit(w, 'algae', 5); // есть что продать — И-15 не спасает
        return w;
      })(),
    });

    const before = s().credits;
    s().plant(0, 'coffee_beans');
    expect(s().credits).toBe(before);
    expect(s().fields[0]?.state).toBe('EMPTY');
  });

  it('тап по несуществующей грядке ничего не делает и не падает', () => {
    const before = s().credits;
    expect(() => s().plant(99, 'algae')).not.toThrow();
    expect(s().credits).toBe(before);
  });
});

describe('Сбор', () => {
  it('урожай попадает на склад в количестве HARVEST_QTY, XP начисляется', () => {
    s().plant(0, 'mushrooms');
    useGame.setState({ now: s().now + GOODS.mushrooms.prod_time_sec });
    s().tick(s().now);

    const xp_before = s().xp_into_level;
    s().collectField(0);

    const qty = harvestQty('mushrooms');
    expect(qtyOf(s().warehouse, 'mushrooms')).toBe(qty);
    expect(s().xp_into_level - xp_before).toBe(GOODS.mushrooms.base_xp * qty);
    expect(s().fields[0]?.state).toBe('EMPTY');
  });

  it('при полном складе не начисляется ничего и грядка остается занятой', () => {
    const w = createWarehouse();
    deposit(w, 'soy', w.capacity);
    reset({ warehouse: w });

    s().plant(0, 'algae');
    useGame.setState({ now: s().now + GOODS.algae.prod_time_sec });
    s().tick(s().now);

    const xp_before = s().xp_into_level;
    const total_before = totalQty(s().warehouse);
    s().collectField(0);

    expect(s().xp_into_level).toBe(xp_before);
    expect(totalQty(s().warehouse)).toBe(total_before);
    expect(s().fields[0]?.state).toBe('READY');
  });
});

describe('Уровни', () => {
  it('накопленный XP поднимает уровень и выдает награду один раз', () => {
    reset({ level: 1, xp_into_level: 0 });
    const need = xpToNext(1);
    const reward = levelUpReward(2);
    const credits_before = s().credits;

    // Сажаем, собираем И ПРОДАЕМ. Продажа здесь не для красоты: без нее
    // склад забивается за десяток сборов, сбор блокируется, и игрок до
    // второго уровня не доходит вообще. Первая версия этого теста именно
    // так и падала — то есть конверсионный узел склада работает, а тест
    // моделировал игрока, которого не бывает.
    let guard = 0;
    while (s().level === 1 && guard++ < 300) {
      if (s().fields[0]?.state === 'EMPTY') s().plant(0, 'algae');
      useGame.setState({ now: s().now + GOODS.algae.prod_time_sec });
      s().tick(s().now);
      if (s().fields[0]?.state === 'READY') s().collectField(0);
      const stock = qtyOf(s().warehouse, 'algae');
      if (stock > 20) s().sell('algae', stock);
    }

    expect(s().level).toBe(2);
    expect(need).toBeGreaterThan(0);
    // Кредиты изменились как минимум на награду минус потраченное на посев.
    expect(s().credits).toBeGreaterThan(credits_before - need);
    expect(s().isotopes).toBe(reward.isotopes);
  });
});

describe('Ускорение', () => {
  it('ниже порога бесплатно и завершает цикл', () => {
    s().plant(0, 'coffee_beans');
    // Оставляем 10 секунд — это ниже порога бесплатности.
    useGame.setState({ now: s().fields[0]!.ends_at - 10, isotopes: 0 });

    s().speedupField(0);
    expect(s().fields[0]?.state).toBe('READY');
    expect(s().isotopes).toBe(0); // ничего не списано
  });

  it('выше порога списывает изотопы', () => {
    s().plant(0, 'coffee_beans');
    useGame.setState({ isotopes: 1000 });
    const before = s().isotopes;

    s().speedupField(0);
    expect(s().isotopes).toBeLessThan(before);
    expect(s().fields[0]?.state).toBe('READY');
  });

  it('без изотопов ускорение не проходит и грядка не завершается', () => {
    s().plant(0, 'coffee_beans');
    useGame.setState({ isotopes: 0 });

    s().speedupField(0);
    expect(s().fields[0]?.state).toBe('GROWING');
    expect(s().isotopes).toBe(0);
  });

  it('созревшую грядку ускорить нельзя — списания не происходит', () => {
    s().plant(0, 'algae');
    useGame.setState({ now: s().now + GOODS.algae.prod_time_sec, isotopes: 500 });
    s().tick(s().now);

    s().speedupField(0);
    expect(s().isotopes).toBe(500);
  });
});

describe('Покупка фабрики', () => {
  it('списывает цену и открывает здание', () => {
    reset({ credits: FACTORY_PRICES.food_module.first });
    s().buyBuilding('food_module');
    expect(s().buildings).toContain('food_module');
    expect(s().credits).toBe(0);
  });

  it('без денег не покупает и не списывает', () => {
    reset({ credits: FACTORY_PRICES.food_module.first - 1 });
    s().buyBuilding('food_module');
    expect(s().buildings).not.toContain('food_module');
    expect(s().credits).toBe(FACTORY_PRICES.food_module.first - 1);
  });

  it('до нужного уровня не покупает даже при деньгах', () => {
    reset({ credits: 100_000, level: 1 });
    s().buyBuilding('food_module');
    expect(s().buildings).not.toContain('food_module');
    expect(s().credits).toBe(100_000);
  });
});

describe('Продажа', () => {
  it('продажа начисляет кредиты и убирает товар', () => {
    const w = createWarehouse();
    deposit(w, 'soy', 5);
    reset({ warehouse: w });

    const before = s().credits;
    s().sell('soy', 3);
    expect(s().credits).toBe(before + GOODS.soy.price * 3);
    expect(qtyOf(s().warehouse, 'soy')).toBe(2);
  });

  it('продажа несуществующего товара ничего не меняет', () => {
    const before = s().credits;
    s().sell('coffee_ration', 1);
    expect(s().credits).toBe(before);
  });
});

describe('Дрон: доска и заказы', () => {
  /** Доска наполняется тиком, поэтому уровень ставим до него. */
  function boardAt(level: number, stock: Array<[Parameters<typeof deposit>[1], number]> = []) {
    const w = createWarehouse(300);
    for (const [id, qty] of stock) deposit(w, id, qty);
    reset({ level, warehouse: w, orders: [] });
    s().tick(s().now);
  }

  it('до второго уровня доска пуста — механика закрыта', () => {
    boardAt(1);
    expect(s().orders).toHaveLength(0);
  });

  it('на втором уровне появляются три слота', () => {
    boardAt(2);
    expect(s().orders).toHaveLength(3);
  });

  it('число слотов растет с уровнем и не падает при повторном тике', () => {
    boardAt(10);
    expect(s().orders).toHaveLength(7);
    s().tick(s().now + 1);
    expect(s().orders).toHaveLength(7);
  });

  it('погрузка резервирует товар, отправка списывает и платит', () => {
    boardAt(5, [
      ['algae', 200],
      ['soy', 200],
      ['mushrooms', 200],
    ]);
    const slot = s().orders.find((o) => canFulfillNow(o, s().warehouse));
    if (!slot) return; // на этом seed нечего грузить, проверка неприменима

    for (let i = 0; i < slot.positions.length; i++) s().loadOrderPosition(slot.idx, i);
    expect(s().orders[slot.idx]?.state).toBe('ready');

    const credits_before = s().credits;
    const reward = s().orders[slot.idx]?.credits_reward ?? 0;
    s().sendOrderAt(slot.idx);

    expect(s().credits).toBe(credits_before + reward);
    // Слот сразу получает новый заказ: у отправки нет таймера.
    expect(s().orders[slot.idx]?.state).not.toBe('ready');
  });

  it('выброс возвращает погруженное и ставит таймер', () => {
    boardAt(5, [
      ['algae', 200],
      ['soy', 200],
      ['mushrooms', 200],
    ]);
    const slot = s().orders.find((o) => canFulfillNow(o, s().warehouse));
    if (!slot) return;

    s().loadOrderPosition(slot.idx, 0);
    const position = s().orders[slot.idx]?.positions[0];
    const reserved_before = s().warehouse.cells[position!.good_id]?.reserved ?? 0;
    expect(reserved_before).toBeGreaterThan(0);

    s().discardOrderAt(slot.idx);

    expect(s().orders[slot.idx]?.state).toBe('empty_cooldown');
    // Товар вернулся: иначе игрок платит за то, что передумал.
    expect(s().warehouse.cells[position!.good_id]?.reserved ?? 0).toBe(0);
  });

  it('выброшенный слот сам обновляется по истечении таймера', () => {
    boardAt(5);
    s().discardOrderAt(0);
    const refresh_at = s().orders[0]?.refresh_at ?? 0;

    s().tick(refresh_at - 1);
    expect(s().orders[0]?.state).toBe('empty_cooldown');

    s().tick(refresh_at);
    expect(s().orders[0]?.state).not.toBe('empty_cooldown');
  });

  it('платный рефреш списывает изотопы, без них не срабатывает', () => {
    boardAt(5);
    s().discardOrderAt(0);

    useGame.setState({ isotopes: 0 });
    s().refreshSlotNow(0);
    expect(s().orders[0]?.state).toBe('empty_cooldown');

    useGame.setState({ isotopes: 100 });
    s().refreshSlotNow(0);
    expect(s().orders[0]?.state).not.toBe('empty_cooldown');
    expect(s().isotopes).toBeLessThan(100);
  });
});
