/**
 * Прогон 6 — доказательства дефектов (ломатель). Красные намеренно, в гейт
 * (`test:gate`/`check`) не входят по имени файла (`defects-*.test.ts`).
 *
 * Дефект: `rebalanceForAchievability` (И-10, `shuttle.ts`) меняет товар отсека
 * на "самую быструю доступную альтернативу пула", но у функции нет параметра
 * `deficit_locks` вообще — сигнатура физически не может учесть чужой лок
 * И-13. Это ровно тот стык, о котором предупреждал сам исполнитель прогона 6
 * в `loop/run-6/coder-work.md` («стык остается самым подозрительным местом
 * всей правки») — его собственный интеграционный тест
 * (`deficitlock.test.ts`, сценарий «шаттл не перехватывает дефицит») его не
 * ловит, потому что использует узкий пул (`['jumpsuit','algae','soy']`), в
 * котором И-10 никогда не успевает сработать раньше, чем короткий рейс из
 * двух легких отсеков уже проходит покрытие. Здесь пул специально шире, чтобы
 * дойти до `rebalanceForAchievability`.
 *
 * **Приемка починки (координатор), обновление после фикса.** Первая версия
 * этого теста вызывала `rebalanceForAchievability` четырьмя позиционными
 * аргументами — без пятого/шестого (`deficit_locks`, `now`), которые
 * появились в сигнатуре вместе с фиксом. Чистая функция без доступа к
 * состоянию извне не могла увидеть лок ни при какой реализации: тест был
 * красным по форме вызова, а не по существу проверки. Ниже — тот же сценарий
 * с локами, реально переданными в функцию.
 */

import { describe, expect, it } from 'vitest';
import { EASY_PRODUCE_MAX_MIN } from '../config/economy';
import {
  createDeficitLockState,
  isDeficitLockedByOtherMechanic,
  registerDeficitLock,
} from '../deficitlock';
import { type OrderSlot, releaseOrderDeficitLocks } from '../drone';
import { productionTimeMinutes } from '../rushcost';
import {
  generateTrip,
  rebalanceForAchievability,
  type ShuttleGenContext,
  type ShuttleSlot,
} from '../shuttle';
import { createWarehouse, deposit } from '../warehouse';

function slot(good_id: ShuttleSlot['good_id'], qty_required: number): ShuttleSlot {
  return {
    idx: 0,
    good_id,
    qty_required,
    qty_filled: 0,
    qty_purchased: 0,
    filled_by: null,
    reward: null,
    collected: false,
    floor_forced: false,
  };
}

describe('Дефект: rebalanceForAchievability (И-10) не знает о deficit_locks (И-13)', () => {
  it('замена товара не должна попадать на товар, уже залоченный ДРУГОЙ механикой', () => {
    const locks = createDeficitLockState();
    // Дрон уже держит "грибной суп" как свою дефицитную позицию активного заказа.
    registerDeficitLock(locks, 'mushroom_soup', 'drone', 'drone:0', 0);

    const w = createWarehouse(500);
    // Комбинезон уже на полу количества — резать больше нечем, следующий шаг
    // алгоритма обязан заменить товар (см. shuttle.ts, rebalanceForAchievability).
    const slots = [slot('jumpsuit', 1)];
    // Единственный кандидат на замену в пуле — тот самый залоченный товар.
    // Бюджет подобран так, чтобы замена состоялась и не откатилась обратно
    // (при слишком жестком бюджете функция осциллирует между двумя
    // кандидатами до guard_limit — отдельное наблюдение, не предмет этого теста).
    // Локи и `now` переданы пятым/шестым аргументом — ровно так, как их
    // прокидывает `generateTrip` после фикса.
    const result = rebalanceForAchievability(
      slots,
      w,
      60,
      ['jumpsuit', 'mushroom_soup'],
      locks,
      0,
    );
    const swapped = result[0];
    if (!swapped) throw new Error('rebalanceForAchievability вернула пустой рейс');

    // Правильное поведение (И-13): результат не должен быть залоченным другой
    // механикой дефицитным товаром — единственная незалоченная альтернатива
    // в этом сценарии отсутствует, поэтому корректный ответ — остаться на
    // исходном товаре (`jumpsuit`, сверх бюджета, но задокументированное
    // ограничение реестра 8.24), а не выбрать залоченный `mushroom_soup`.
    const minutes = productionTimeMinutes(swapped.good_id, swapped.qty_required, w);
    if (minutes > EASY_PRODUCE_MAX_MIN.shuttle) {
      expect(isDeficitLockedByOtherMechanic(locks, swapped.good_id, 'shuttle', 0)).toBe(false);
    }
  });

  /**
   * Тот же дефект на уровне полного генератора: собранный рейс шаттла содержит
   * отсек с товаром, уже дефицитно залоченным дроном, — регистрация лока для
   * шаттла тихо не срабатывает (upsert-if-absent — конфликт молча пропускается),
   * но отсек все равно остается в рейсе и просит игрока произвести тот же
   * дефицитный товар, который уже обещан дрону. Никакого сигнала об этом нет:
   * ни исключения, ни отброшенного отсека — ровно тот класс тихой порчи,
   * который проект уже ловил раньше.
   */
  it('generateTrip не должен возвращать рейс с отсеком, дефицитно залоченным другой механикой', () => {
    const locks = createDeficitLockState();
    registerDeficitLock(locks, 'mushroom_soup', 'drone', 'drone:0', 0);

    const warehouse = createWarehouse(500);
    deposit(warehouse, 'algae', 100);
    deposit(warehouse, 'soy', 100);

    const ctx: ShuttleGenContext = {
      level: 20,
      warehouse,
      available_goods: ['jumpsuit', 'algae', 'soy', 'mushroom_soup'],
      previous: null,
      is_first_trip: false,
      arrival_no: 5,
      rng: () => 0,
      deficit_locks: locks,
      now: 0,
    };

    const trip = generateTrip(ctx);
    const conflict = trip.slots.find(
      (s) =>
        isDeficitLockedByOtherMechanic(locks, s.good_id, 'shuttle', 0) &&
        productionTimeMinutes(s.good_id, s.qty_required, warehouse) >
          EASY_PRODUCE_MAX_MIN.shuttle,
    );

    // И-13 требует, чтобы такого отсека не было вовсе — ни одна механика не
    // должна выдавать игроку дефицитную позицию по товару, который уже
    // дефицитно обещан другой механике.
    expect(conflict).toBeUndefined();
    expect(locks.mushroom_soup?.locked_by_mechanic).toBe('drone');
  });
});

describe('Дефект: лок держится на (good_id, mechanic), а не на владеющий заказ — сиблинг открывает дыру', () => {
  /**
   * Канон и докстринг `registerDeficitLock` явно разрешают ДВУМ заказам ОДНОЙ
   * механики законно делить один дефицитный товар («может законно повторно
   * просить тот же дефицитный товар в другом своем заказе» — deficitlock.ts).
   * До фикса объект-лок был один на `good_id`, без списка владельцев:
   * терминация ПЕРВОГО заказа снимала лок целиком, не проверяя, жив ли
   * ВТОРОЙ заказ той же механики, все еще держащий тот же товар как свою
   * дефицитную позицию.
   */
  it('release одного заказа не должен снимать защиту с дефицита ЖИВОГО заказа-соседа той же механики', () => {
    const locks = createDeficitLockState();

    // Два дрон-заказа независимо получили "комбинезон" как свою дефицитную
    // позицию (легально по И-13 — одна механика не блокирует сама себя).
    registerDeficitLock(locks, 'jumpsuit', 'drone', 'drone:A', 0);
    registerDeficitLock(locks, 'jumpsuit', 'drone', 'drone:B', 0);

    const order_a: OrderSlot = {
      idx: 0,
      state: 'active',
      npc_name: 'A',
      positions: [
        {
          good_id: 'jumpsuit',
          qty: 1,
          qty_filled: 0,
          filled_by: null,
          qty_purchased: 0,
          easy: false,
        },
      ],
      credits_reward: 100,
      xp_reward: 40,
      refresh_at: 0,
    };
    // order_b — ЖИВОЙ заказ-сосед, тоже требует комбинезон, никто его не трогал.
    const order_b: OrderSlot = {
      idx: 1,
      state: 'active',
      npc_name: 'B',
      positions: [
        {
          good_id: 'jumpsuit',
          qty: 1,
          qty_filled: 0,
          filled_by: null,
          qty_purchased: 0,
          easy: false,
        },
      ],
      credits_reward: 100,
      xp_reward: 40,
      refresh_at: 0,
    };

    // Терминальное событие ЗАКАЗА A (выброшен/сдан) — order_b остается активным.
    releaseOrderDeficitLocks(order_a, locks);

    const warehouse = createWarehouse(500);
    deposit(warehouse, 'algae', 100);
    deposit(warehouse, 'soy', 100);

    const trip = generateTrip({
      level: 20,
      warehouse,
      available_goods: ['jumpsuit', 'algae', 'soy'],
      previous: null,
      is_first_trip: false,
      arrival_no: 5,
      rng: () => 0.01,
      deficit_locks: locks,
      now: 0,
    });

    // И-13: пока order_b (дрон) жив и держит "комбинезон" как дефицит, шаттл
    // не должен получить право на тот же товар.
    expect(trip.slots.some((s) => s.good_id === 'jumpsuit')).toBe(false);

    // На всякий случай подтверждаем условие сценария: order_b все еще
    // числится дефицитным по jumpsuit — его никто не освобождал.
    expect(order_b.positions[0]?.easy).toBe(false);
  });
});
