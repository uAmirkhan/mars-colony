/**
 * Тесты производства и склада. Проверяют правила [[tz-production-mars]],
 * а не реализацию: если правило поменяется в спеке, тест обязан покраснеть.
 */

import { beforeEach, describe, expect, it } from 'vitest';
import { CREDITS_START, plantingCost, WAREHOUSE_MAX_CAPACITY } from '../config/economy';
import { GOODS, harvestQty } from '../config/goods';
import {
  collectFactory,
  collectField,
  createFactorySlot,
  createField,
  enqueue,
  onWarehouseStockIncreased,
  type ProductionContext,
  plant,
  refreshField,
  sell,
} from '../production';
import {
  availableOf,
  consume,
  createWarehouse,
  deposit,
  freeSpace,
  qtyOf,
  reserve,
  shipReserved,
  totalQty,
  unreserve,
  upgradeCapacity,
  type WarehouseState,
} from '../warehouse';

let w: WarehouseState;
let ctx: ProductionContext;

beforeEach(() => {
  w = createWarehouse();
  ctx = { now: 0, warehouse: w, credits: CREDITS_START, level: 21 };
});

describe('Склад: семантика qty / reserved / available', () => {
  it('reserve не освобождает капасити', () => {
    deposit(w, 'algae', 10);
    reserve(w, 'algae', 6);
    expect(qtyOf(w, 'algae')).toBe(10);
    expect(availableOf(w, 'algae')).toBe(4);
    expect(totalQty(w)).toBe(10); // место занято все теми же 10 единицами
  });

  it('обход капасити через резерв невозможен', () => {
    // Сценарий эксплойта: забить склад, раздать все по слотам заказов
    // и продолжить собирать урожай сверх лимита.
    deposit(w, 'algae', 50);
    reserve(w, 'algae', 50);
    expect(freeSpace(w)).toBe(0);
    expect(deposit(w, 'soy', 1)).toBe(false);
  });

  it('clear возвращает доступность, не трогая qty', () => {
    deposit(w, 'algae', 10);
    reserve(w, 'algae', 6);
    unreserve(w, 'algae', 6);
    expect(qtyOf(w, 'algae')).toBe(10);
    expect(availableOf(w, 'algae')).toBe(10);
  });

  it('deliver физически убирает товар и освобождает место', () => {
    deposit(w, 'algae', 10);
    reserve(w, 'algae', 6);
    shipReserved(w, 'algae', 6);
    expect(qtyOf(w, 'algae')).toBe(4);
    expect(availableOf(w, 'algae')).toBe(4);
    expect(freeSpace(w)).toBe(46);
  });

  it('нельзя зарезервировать больше доступного', () => {
    deposit(w, 'algae', 5);
    expect(reserve(w, 'algae', 6)).toBe(false);
    reserve(w, 'algae', 5);
    expect(reserve(w, 'algae', 1)).toBe(false);
  });

  it('нельзя потратить зарезервированное', () => {
    deposit(w, 'algae', 5);
    reserve(w, 'algae', 5);
    expect(consume(w, 'algae', 1)).toBe(false);
  });

  it('прием — все или ничего, без частичной укладки', () => {
    deposit(w, 'algae', 48);
    expect(deposit(w, 'soy', 5)).toBe(false);
    expect(qtyOf(w, 'soy')).toBe(0); // ничего не легло
    expect(totalQty(w)).toBe(48);
  });

  it('апгрейд упирается в потолок MVP', () => {
    for (let i = 0; i < 100; i++) upgradeCapacity(w);
    expect(w.capacity).toBe(WAREHOUSE_MAX_CAPACITY);
    expect(upgradeCapacity(w)).toBe(false);
  });
});

describe('Грядка', () => {
  it('посадка списывает кредиты по формуле стока', () => {
    const field = createField(0);
    const result = plant(field, 'mushrooms', ctx, [field]);
    expect(result.ok).toBe(true);
    expect(result.credits_delta).toBe(-plantingCost(GOODS.mushrooms.price));
    expect(field.state).toBe('GROWING');
    expect(field.ends_at).toBe(GOODS.mushrooms.prod_time_sec);
  });

  it('нельзя посадить в занятую грядку', () => {
    const field = createField(0);
    plant(field, 'algae', ctx, [field]);
    expect(plant(field, 'soy', ctx, [field]).reason).toBe('slot_busy');
  });

  it('нельзя посадить культуру выше своего уровня', () => {
    const field = createField(0);
    const low = { ...ctx, level: 1 };
    expect(plant(field, 'coffee_beans', low, [field]).reason).toBe('locked');
  });

  it('READY наступает лениво, по времени', () => {
    const field = createField(0);
    plant(field, 'algae', ctx, [field]);
    refreshField(field, GOODS.algae.prod_time_sec - 1);
    expect(field.state).toBe('GROWING');
    refreshField(field, GOODS.algae.prod_time_sec);
    expect(field.state).toBe('READY');
  });

  it('сбор дает выход культуры и XP за каждую единицу', () => {
    const field = createField(0);
    plant(field, 'mushrooms', ctx, [field]);
    const later = { ...ctx, now: GOODS.mushrooms.prod_time_sec };
    const result = collectField(field, later);
    const qty = harvestQty('mushrooms');

    expect(result.ok).toBe(true);
    expect(qty).toBe(3); // значение из раздела 7 ТЗ, не единица
    expect(result.xp_gained).toBe(GOODS.mushrooms.base_xp * qty);
    expect(field.state).toBe('EMPTY');
    expect(qtyOf(w, 'mushrooms')).toBe(qty);
  });

  it.each(['algae', 'soy', 'mushrooms', 'tomatoes', 'cotton', 'coffee_beans'] as const)(
    '%s собирается по HARVEST_QTY, а не по единице',
    (id) => {
      const field = createField(0);
      const fresh = { ...ctx, warehouse: createWarehouse(), credits: 999 };
      plant(field, id, fresh, [field]);
      const later = { ...fresh, now: GOODS[id].prod_time_sec };
      collectField(field, later);
      expect(qtyOf(fresh.warehouse, id)).toBe(harvestQty(id));
      expect(harvestQty(id)).toBeGreaterThan(1);
    },
  );

  it('переполнение блокирует сбор целиком, урожай не пропадает', () => {
    deposit(w, 'soy', 50);
    const field = createField(0);
    plant(field, 'algae', ctx, [field]);
    const later = { ...ctx, now: GOODS.algae.prod_time_sec };
    const result = collectField(field, later);

    expect(result.ok).toBe(false);
    expect(result.reason).toBe('warehouse_full');
    expect(result.xp_gained).toBeUndefined(); // XP не начислен
    expect(field.state).toBe('READY'); // слот занят, пересадить нельзя
    expect(qtyOf(w, 'algae')).toBe(0); // товар не потерян и не положен
  });

  it('места под частичный выход не хватает — сбор отклоняется целиком', () => {
    // AC2: капасити меньше HARVEST_QTY культуры. Частичной выдачи нет.
    deposit(w, 'soy', 48); // свободно 2, водорослям нужно 4
    const field = createField(0);
    plant(field, 'algae', ctx, [field]);
    const later = { ...ctx, now: GOODS.algae.prod_time_sec };
    const result = collectField(field, later);

    expect(result.reason).toBe('warehouse_full');
    expect(qtyOf(w, 'algae')).toBe(0); // ни одной единицы не положено
    expect(totalQty(w)).toBe(48);
  });
});

describe('И-15: анти-софтлок посева', () => {
  it('спасает игрока в полном тупике', () => {
    const field = createField(0);
    const broke = { ...ctx, credits: 0 };
    const result = plant(field, 'algae', broke, [field]);
    expect(result.ok).toBe(true);
    expect(result.credits_delta).toBe(0);
    expect(result.softlock_rescued).toBe(true);
  });

  it('не срабатывает, если на складе есть что продать', () => {
    deposit(w, 'algae', 3);
    const field = createField(0);
    const broke = { ...ctx, credits: 0, warehouse: w };
    expect(plant(field, 'algae', broke, [field]).reason).toBe('insufficient_balance');
  });

  it('не срабатывает, если что-то растет', () => {
    const a = createField(0);
    const b = createField(1);
    plant(a, 'algae', ctx, [a, b]);
    const broke = { ...ctx, credits: 0 };
    expect(plant(b, 'algae', broke, [a, b]).reason).toBe('insufficient_balance');
  });

  it('не делает бесплатной дорогую культуру', () => {
    const field = createField(0);
    const broke = { ...ctx, credits: 0 };
    expect(plant(field, 'coffee_beans', broke, [field]).reason).toBe('insufficient_balance');
  });

  it('стартового баланса хватает на первый круг посадок', () => {
    const fields = [0, 1, 2, 3].map(createField);
    let credits = CREDITS_START;
    for (const f of fields) {
      const r = plant(f, 'algae', { ...ctx, credits }, fields);
      expect(r.ok).toBe(true);
      credits += r.credits_delta ?? 0;
    }
    expect(credits).toBeGreaterThan(0);
  });
});

describe('Фабрика', () => {
  it('стартует сразу, если входы есть, и списывает их атомарно', () => {
    deposit(w, 'soy', 5);
    const slot = createFactorySlot(0, 'food_module');
    const result = enqueue(slot, 'protein_bar', ctx);
    expect(result.ok).toBe(true);
    expect(slot.state).toBe('PRODUCING');
    expect(qtyOf(w, 'soy')).toBe(3);
  });

  it('уходит в QUEUED без входов и НЕ резервирует товар', () => {
    deposit(w, 'soy', 1);
    const slot = createFactorySlot(0, 'food_module');
    enqueue(slot, 'protein_bar', ctx);
    expect(slot.state).toBe('QUEUED');
    // Ключевое правило анти-голодания: единственная соя осталась доступной заказам.
    expect(availableOf(w, 'soy')).toBe(1);
  });

  it('стартует по событию пополнения склада', () => {
    const slot = createFactorySlot(0, 'food_module');
    enqueue(slot, 'protein_bar', ctx);
    expect(slot.state).toBe('QUEUED');

    deposit(w, 'soy', 2);
    onWarehouseStockIncreased([slot], ctx);
    expect(slot.state).toBe('PRODUCING');
  });

  it('обход кандидатов не прерывается на первом провале', () => {
    // Слот A ждет кофе-паек (кофе + соя), слот B ждет протеин (только соя).
    // Пришла соя. A стартовать не может, B — может, и обязан.
    const a = createFactorySlot(0, 'food_module');
    const b = createFactorySlot(1, 'food_module');
    enqueue(a, 'coffee_ration', ctx);
    enqueue(b, 'protein_bar', { ...ctx, now: 1 });
    expect(a.state).toBe('QUEUED');
    expect(b.state).toBe('QUEUED');

    deposit(w, 'soy', 2);
    const started = onWarehouseStockIncreased([a, b], ctx);

    expect(started).toBe(1);
    expect(a.state).toBe('QUEUED');
    expect(b.state).toBe('PRODUCING'); // не голодает за спиной у A
  });

  it('FIFO между слотами, ждущими один и тот же товар', () => {
    const first = createFactorySlot(0, 'food_module');
    const second = createFactorySlot(1, 'food_module');
    enqueue(first, 'protein_bar', { ...ctx, now: 10 });
    enqueue(second, 'protein_bar', { ...ctx, now: 20 });

    deposit(w, 'soy', 2); // хватает ровно на один запуск
    onWarehouseStockIncreased([second, first], ctx);

    expect(first.state).toBe('PRODUCING'); // встал в очередь раньше
    expect(second.state).toBe('QUEUED');
  });

  it('нельзя поставить рецепт в чужое здание', () => {
    const slot = createFactorySlot(0, 'food_module');
    expect(enqueue(slot, 'fabric', ctx).reason).toBe('locked');
  });

  it('переполнение блокирует сбор с фабрики', () => {
    deposit(w, 'soy', 2);
    const slot = createFactorySlot(0, 'food_module');
    enqueue(slot, 'protein_bar', ctx);
    deposit(w, 'algae', 50); // склад под завязку: входы уже списаны, места ноль
    const later = { ...ctx, now: GOODS.protein_bar.prod_time_sec };
    const result = collectFactory(slot, later);
    expect(result.reason).toBe('warehouse_full');
    expect(slot.state).toBe('READY'); // слот занят, новую позицию не поставить
  });
});

describe('Продажа', () => {
  it('дает рыночную цену и освобождает место', () => {
    deposit(w, 'mushroom_soup', 3);
    const result = sell('mushroom_soup', 2, ctx);
    expect(result.credits_delta).toBe(GOODS.mushroom_soup.price * 2);
    expect(qtyOf(w, 'mushroom_soup')).toBe(1);
  });

  it('нельзя продать зарезервированное под заказ', () => {
    deposit(w, 'algae', 5);
    reserve(w, 'algae', 5);
    expect(sell('algae', 1, ctx).ok).toBe(false);
  });
});
