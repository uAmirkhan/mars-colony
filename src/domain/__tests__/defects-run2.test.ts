/**
 * Находки прогона 2. Каждый тест здесь КРАСНЫЙ и обязан оставаться красным,
 * пока дефект не починен. Зеленый тест в этом файле означает, что дефект
 * закрыт, — тогда тест переезжает в профильный файл механики.
 *
 * Нумерация продолжает `defects-run1.test.ts` (Д-1..Д-6).
 */

import fc from 'fast-check';
import { describe, expect, it, vi } from 'vitest';
import { makeRng } from '../../sim/rng';
import { GOOD_BASE_QTY, GOODS } from '../config/goods';
import { generateOrder } from '../drone';
import type { DropContext } from '../droproller';
import { buyoutPrice } from '../rushcost';
import { buyoutSlot, generateTrip, type ShuttleTrip } from '../shuttle';
import type { GoodId } from '../types';
import { availableOf, createWarehouse, deposit, qtyOf } from '../warehouse';

const NOW = 1_000_000;

function drop(): DropContext {
  return {
    pity: {},
    stock: {},
    need: {},
    warehouse_avg_24h: {},
    gated_open: false,
    arrival_no: 5,
    constructions: [],
    rng: () => 0.5,
  };
}

/**
 * Рейс из одного интересующего нас отсека плюс заведомо незакрываемый второй:
 * второй нужен только чтобы докупка первого не отправила рейс и не увела
 * состояние в IN_TRANSIT посреди проверки цены.
 */
function tripWithSlot(good_id: GoodId, qty: number): ShuttleTrip {
  return {
    state: 'ORDER',
    slots: [
      {
        idx: 0,
        good_id,
        qty_required: qty,
        qty_filled: 0,
        filled_by: null,
        reward: null,
        collected: false,
        floor_forced: false,
      },
      {
        idx: 1,
        good_id: 'cotton',
        qty_required: 999,
        qty_filled: 0,
        filled_by: null,
        reward: null,
        collected: false,
        floor_forced: false,
      },
    ],
    trip_min: 60,
    departed_at: 0,
    arrives_at: 0,
    cooldown_until: 0,
    is_first_trip: false,
    arrival_no: 1,
  };
}

/**
 * Д-7. Докупка получает скидку за товар, который лежит на складе и НИКУДА
 * оттуда не уходит.
 *
 * `rushCost` вычитает `availableStock` (канон 5.3) — это верно для ускорения
 * производства, где склад и есть то, что не надо производить. Но `buyoutSlot`
 * (И-12) кладет товар прямо в отсек, минуя склад, и складского остатка не
 * трогает. В итоге игрок платит за недостачу, а получает полный отсек и
 * сохраняет остаток свободным: его можно продать или скормить дрону.
 *
 * Ровно тот арбитраж, ради закрытия которого написана И-12 («докупить дешево →
 * скормить другой механике»), только достигнутый со стороны цены, а не со
 * стороны товаропотока. ТЗ шаттла 6.2 задает кнопку «Докупить {qty-stock}» —
 * то есть докупается недостача, а складская часть грузится игроком; код
 * закрывает отсек целиком по цене недостачи.
 */
describe('Д-7: докупка платит за недостачу, а закрывает отсек целиком', () => {
  it('водоросли: 5 в отсек за цену одной, четыре остаются на складе', () => {
    const warehouse = createWarehouse(500);
    deposit(warehouse, 'algae', 4);

    const trip = tripWithSlot('algae', 5);
    const result = buyoutSlot(trip, 0, warehouse, NOW, drop());

    // Склад не изменился: докупка идет мимо него по И-12.
    expect(qtyOf(warehouse, 'algae')).toBe(4);
    expect(availableOf(warehouse, 'algae')).toBe(4);
    // В отсек легло пять единиц.
    expect(trip.slots[0]?.qty_filled).toBe(5);

    // Значит и заплатить надо было как за пять из ниоткуда.
    expect(result.price).toBe(buyoutPrice('algae', 5, createWarehouse()));
  });

  it('И-12: скидка за складской остаток недействительна, пока остаток не списан', () => {
    const goods: GoodId[] = [
      'algae',
      'soy',
      'mushrooms',
      'cotton',
      'protein_bar',
      'mushroom_soup',
      'fabric',
      'jumpsuit',
      'oxygen_tank',
    ];

    fc.assert(
      fc.property(
        fc.constantFrom(...goods),
        fc.integer({ min: 2, max: 8 }),
        fc.integer({ min: 1, max: 7 }),
        (good_id, need, have) => {
          fc.pre(have < need);

          const warehouse = createWarehouse(500);
          deposit(warehouse, good_id, have);
          const before = qtyOf(warehouse, good_id);

          const trip = tripWithSlot(good_id, need);
          const result = buyoutSlot(trip, 0, warehouse, NOW, drop());

          // Докупка склада не касается — значит и скидки за него быть не может.
          expect(qtyOf(warehouse, good_id)).toBe(before);
          expect(result.loaded).toBe(need);
          expect(result.price).toBe(buyoutPrice(good_id, need, createWarehouse()));
        },
      ),
      { numRuns: 300 },
    );
  });
});

/**
 * Д-8. У дрона нет крайнего случая канона 1.6: пул пуст — заказ выдается
 * пустым, и генератор считает его валидным.
 *
 * `easyRatio([])` возвращает 1, `maxRepeatRatio([])` — 0, поэтому проверка
 * инвариантов проходит с первой попытки и цикл выходит по `break`. Канон 1.9
 * требует обратного для ВСЕХ трех механик: «никогда не завершается ошибкой или
 * пустым результатом — только валидным заказом (в т.ч. через фолбэк 1.6)».
 *
 * Шаттлу такой предохранитель добавили (`fallbackMinimalSlots`), дрону — нет,
 * хотя конструкция генератора у них общая. Пустой заказ нельзя ни выполнить
 * (`canFulfillNow` = false), ни довести до `ready`: слот доски занят карточкой,
 * которая ничего не просит и ничего не платит.
 */
describe('Д-8: у дрона нет fallbackMinimalOrder', () => {
  it('пустой пул товаров дает заказ хотя бы из одной позиции', () => {
    const order = generateOrder(0, {
      level: 6,
      warehouse: createWarehouse(500),
      available_goods: [],
      board: [],
      rng: makeRng(7),
    });

    expect(order.positions.length).toBeGreaterThanOrEqual(1);

    // Канон 1.6: одна позиция самого быстрого доступного товара,
    // количество = GOOD_BASE_QTY.min.
    const position = order.positions[0];
    if (position) {
      expect(position.qty).toBe(GOOD_BASE_QTY[position.good_id].min);
    }
  });
});

/**
 * Д-9. `fallbackMinimalSlots` всегда рапортует `reason: 'empty_pool'`.
 *
 * Канон 1.8 задает событию `order_generation_degraded` перечисление
 * `empty_pool | max_attempts | unresolvable_invariant`, и метрика по нему —
 * «частота фолбэков → сигнал бага генератора ИЛИ недостроенного контент-пула».
 * Два разных вывода различаются ровно этим полем. В коде оно зашито литералом
 * в единственной точке вызова: деградация по исчерпанию кандидатов приезжает
 * в аналитику как пустой пул, и сигнал «баг генератора» неотличим от сигнала
 * «контента мало».
 *
 * Серьезность низкая: путь достижим только на узком пуле из одних медленных
 * товаров. Но чинится это одной строкой, а тихо соврать метрике дороже.
 */
describe('Д-9: причина деградации генератора зашита литералом', () => {
  it('непустой пул, из которого никто не прошел отбор, не называется empty_pool', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});

    // Пул из одного медленного товара (томаты, 3600 с > EASY_PRODUCE_MAX_MIN),
    // склад пуст: FTUE-ветка отбраковывает единственного кандидата.
    const trip = generateTrip({
      level: 7,
      warehouse: createWarehouse(500),
      available_goods: ['tomatoes'],
      previous: null,
      is_first_trip: true,
      arrival_no: 1,
      rng: makeRng(3),
    });

    expect(trip.slots.length).toBeGreaterThanOrEqual(1);
    expect(warn).toHaveBeenCalledWith(
      'order_generation_degraded',
      expect.objectContaining({ mechanic: 'shuttle' }),
    );

    const payload = warn.mock.calls[0]?.[1] as { reason?: string } | undefined;
    expect(GOODS.tomatoes.prod_time_sec).toBeGreaterThan(0); // пул был непустым
    expect(payload?.reason).not.toBe('empty_pool');

    warn.mockRestore();
  });
});
