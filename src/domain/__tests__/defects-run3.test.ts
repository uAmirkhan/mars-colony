/**
 * Доказательства дефектов, найденных на прогоне 3.
 *
 * Файл живет вне гейта на коммите (`npm run test:gate` его исключает) и
 * гоняется отдельно: `npm run defects`. Причина в том, что цикл требует
 * доказывать дефект падающим тестом, а гейт запрещает коммитить красное —
 * вместе они запрещали бы сохранять работу между «нашли» и «починили».
 *
 * Д-12 закрыт исполнителем в тот же день и оставлен здесь сторожем: если
 * порог легкости у дрона снова начнет считаться по одному звену, тест
 * покраснеет первым.
 */

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import fc from 'fast-check';
import { describe, expect, it } from 'vitest';
import { makeRng } from '../../sim/rng';
import {
  COVERAGE_MIN,
  DRONE_PREMIUM_RANGE,
  EASY_PRODUCE_MAX_MIN,
  MAX_DEFICIT_SLOTS,
  plantingCost,
  REPEAT_CAP,
} from '../config/economy';
import { ALL_GOOD_IDS, GOOD_BASE_QTY, GOODS } from '../config/goods';
import {
  availableGoodsFor,
  buyoutPosition,
  generateOrder,
  loadPosition,
  type OrderPosition,
  type OrderSlot,
  orderReward,
  releaseReserved,
  sendOrder,
  slotsAtLevel,
} from '../drone';
import {
  collectField,
  createFactorySlot,
  createField,
  enqueue,
  onWarehouseStockIncreased,
  plant,
  sell,
} from '../production';
import { productionTimeMinutes } from '../rushcost';
import { buyoutSlot, generateTrip, loadSlot, type ShuttleTrip } from '../shuttle';
import type { GoodId } from '../types';
import {
  availableOf,
  createWarehouse,
  deposit,
  qtyOf,
  reserve,
  reservedOf,
  unreserve,
} from '../warehouse';
import { deficitByCanon } from './canon';

const HERE = dirname(fileURLToPath(import.meta.url));

const BUILDINGS = ['food_module', 'mining_site', 'atmospheric_module', 'textile_module'];

/**
 * Один и тот же перебор входов для всех генераторных находок ниже: уровень x
 * набор построек x наполнение склада x зерно. Дефекты генератора живут в
 * узких сочетаниях, и одиночный пример их либо не ловит, либо ловит случайно
 * и перестает ловить после первой же правки субстрата.
 */
function eachGeneratedOrder(
  visit: (
    order: OrderSlot,
    warehouse: ReturnType<typeof createWarehouse>,
    where: string,
  ) => void,
): void {
  for (let level = 2; level <= 21; level++) {
    for (let mask = 0; mask < 16; mask++) {
      for (const stock of [0, 4, 8]) {
        for (let seed = 1; seed <= 25; seed++) {
          const buildings = new Set(BUILDINGS.filter((_, i) => (mask >> i) & 1));
          const pool: GoodId[] = availableGoodsFor(level, buildings);
          if (pool.length === 0) continue;

          const warehouse = createWarehouse(500);
          if (stock > 0) for (const id of pool) deposit(warehouse, id, stock);

          const order = generateOrder(0, {
            level,
            warehouse,
            available_goods: pool,
            board: [],
            rng: makeRng(seed * 7919 + level * 31 + mask),
          });
          visit(order, warehouse, `ур.${level} постройки=${mask} склад=${stock} зерно=${seed}`);
        }
      }
    }
  }
}

const shape = (order: OrderSlot): string =>
  order.positions.map((p) => `${p.good_id}x${p.qty}${p.easy ? '*' : ''}`).join(' ');

/** `easyRatio` канона 1.3, посчитанный снаружи по флагам выданного заказа. */
const easyShare = (order: OrderSlot): number =>
  order.positions.length === 0
    ? 1
    : order.positions.filter((p) => p.easy).length / order.positions.length;

/**
 * Д-12. Флаг `easy` у позиции дрона врет: считает время одного цикла товара,
 * а не время производства нужного количества по всей цепочке рецепта.
 *
 * Тот же дефект был у шаттла (Д-5) и там починен: `shuttle.ts` зовет
 * `productionTimeMinutes`, а `drone.ts:141-144` по-прежнему читает
 * `GOODS[good].prod_time_sec`. Инвариант И-8 у двух механик считается
 * по-разному, хотя движок общий ([[mars-colony-frame]] раздел 8: «ни одно ТЗ
 * механики не переписывает эти системы, только конфигурирует»).
 *
 * Найдено не примером, а перебором: пятнадцать зерен на каждое сочетание
 * уровня, набора построек и наполнения склада. Точка, где ловится, узкая —
 * пятый уровень с построенным пищевым модулем и пустым складом, грибной суп
 * в количестве два: сам цикл супа укладывается в порог, а цепочка с грибами
 * требует пятидесяти минут при пороге тридцать.
 *
 * Цена: И-8 обещает, что не меньше шестидесяти процентов позиций заказа
 * игрок закроет быстро. Заказ, где эта доля посчитана по вранью, проходит
 * проверку и приезжает игроку невыполнимым — ровно та фрустрация, против
 * которой инвариант и написан.
 */
describe('Д-12: у дрона порог легкости не учитывает цепочку рецепта', () => {
  it('ни одна позиция, помеченная easy, не требует больше порога по цепочке', () => {
    const lying: string[] = [];

    // Перебор, а не пример: дефект живет в узком сочетании входов, и одиночный
    // пример его либо не поймает, либо поймает случайно и молча перестанет
    // ловить после любой правки субстрата.
    for (let level = 2; level <= 21; level++) {
      for (let mask = 0; mask < 16; mask++) {
        for (let seed = 1; seed <= 15; seed++) {
          const buildings = new Set(BUILDINGS.filter((_, i) => (mask >> i) & 1));
          const pool: GoodId[] = availableGoodsFor(level, buildings);
          // Склад пуст: проверяем именно ветку «нет на складе, но быстро
          // производится», в которой и живет ошибка.
          const warehouse = createWarehouse(500);

          const order = generateOrder(0, {
            level,
            warehouse,
            available_goods: pool,
            board: [],
            rng: makeRng(seed),
          });

          for (const position of order.positions) {
            if (!position.easy) continue;
            const chain_min = productionTimeMinutes(position.good_id, position.qty, warehouse);
            if (chain_min > EASY_PRODUCE_MAX_MIN.drone) {
              lying.push(
                `ур.${level} постройки=${mask} зерно=${seed} ` +
                  `${position.good_id}x${position.qty}: цепочка ${chain_min.toFixed(0)} мин ` +
                  `при пороге ${EASY_PRODUCE_MAX_MIN.drone}`,
              );
            }
          }
        }
      }
    }

    expect(lying.slice(0, 5).join('\n'), 'позиции, помеченные легкими незаслуженно').toBe('');
  });
});

/**
 * Д-13. Выброс заказа с ДОКУПЛЕННОЙ позицией снимает чужой резерв, и товар
 * появляется из ничего.
 *
 * `drone.ts:504-511`, `releaseReserved` возвращает на склад КАЖДУЮ закрытую
 * позицию, не глядя на `filled_by`. Докупленная позиция по И-12 склада не
 * касалась вовсе: `buyoutPosition` (`:467-476`) ничего не резервировал.
 * Возврат несуществующего резерва проходит успешно, если тот же товар
 * зарезервирован ДРУГИМ заказом доски: `unreserve` (`warehouse.ts:94`)
 * проверяет только суммарный `reserved` по ячейке и не знает, чей он.
 *
 * Дальше цепочка идет сама: у второго заказа позиция осталась `filled`, но
 * резерва под ней уже нет. `sendOrder` (`:479-491`) зовет `shipReserved`,
 * тот отказывает (`warehouse.ts:105`) — и результат отказа никто не читает.
 * Заказ отправляется, кредиты и XP начисляются, товар остается лежать на
 * складе и продается второй раз.
 *
 * Нарушено: И-12 («докупленный товар не может быть изъят, переиспользован»)
 * и семантика склада `warehouse.ts:10-12`. Класс — тихая порча данных:
 * ничего не падает, ни один тест гейта не краснеет, состояние неверное.
 *
 * Тот же корень, что у Д-1 у шаттла, но зеркальный: там резерв запирался
 * навсегда, здесь освобождается чужой.
 */
describe('Д-13: выброс докупленного заказа снимает резерв соседнего', () => {
  const order = (idx: number, good_id: GoodId, qty: number): OrderSlot => ({
    idx,
    state: 'active',
    npc_name: `npc-${idx}`,
    positions: [{ good_id, qty, qty_filled: 0, filled_by: null, qty_purchased: 0, easy: true }],
    credits_reward: 100,
    xp_reward: 10,
    refresh_at: 0,
  });

  it('чужой резерв переживает выброс заказа, закрытого докупкой', () => {
    const warehouse = createWarehouse(50);
    deposit(warehouse, 'algae', 5);

    const kept = order(0, 'algae', 5);
    const thrown = order(1, 'algae', 5);

    expect(loadPosition(kept, 0, warehouse)).toBe(true);
    expect(reservedOf(warehouse, 'algae')).toBe(5);

    // Склад уже весь под первым заказом, поэтому второй закрывается докупкой —
    // ровно тот путь, который открыла кнопка «Докупить» у дрона.
    expect(buyoutPosition(thrown, 0)).toBe(true);
    releaseReserved(thrown, warehouse);

    expect(reservedOf(warehouse, 'algae'), 'резерв первого заказа снят чужим выбросом').toBe(5);
  });

  it('отправка не платит за груз, который физически не ушел со склада', () => {
    const warehouse = createWarehouse(50);
    deposit(warehouse, 'algae', 5);

    const kept = order(0, 'algae', 5);
    const thrown = order(1, 'algae', 5);

    loadPosition(kept, 0, warehouse);
    buyoutPosition(thrown, 0);
    releaseReserved(thrown, warehouse);

    const before = qtyOf(warehouse, 'algae');
    const result = sendOrder(kept, warehouse);

    // Одно из двух обязано быть верным: либо груз списан, либо отправка не
    // состоялась. Сейчас не верно ни то, ни другое.
    if (result.ok) {
      expect(qtyOf(warehouse, 'algae'), 'заказ оплачен, а товар остался на складе').toBe(
        before - 5,
      );
    }
  });

  it('свойство: сумма товара на складе не растет ни на какой последовательности', () => {
    fc.assert(
      fc.property(
        fc.integer({ min: 5, max: 12 }),
        fc.array(
          fc.record({
            slot: fc.integer({ min: 0, max: 2 }),
            buy: fc.boolean(),
            discard: fc.boolean(),
            send: fc.boolean(),
          }),
          { minLength: 1, maxLength: 20 },
        ),
        (stock, steps) => {
          const warehouse = createWarehouse(200);
          deposit(warehouse, 'algae', stock);
          const board = [order(0, 'algae', 3), order(1, 'algae', 3), order(2, 'algae', 3)];

          let shipped = 0;
          for (const step of steps) {
            const slot = board[step.slot]!;
            if (step.buy) buyoutPosition(slot, 0);
            else loadPosition(slot, 0, warehouse);
            if (step.discard) {
              releaseReserved(slot, warehouse);
              slot.state = 'active';
              for (const p of slot.positions) {
                p.qty_filled = 0;
                p.filled_by = null;
                p.qty_purchased = 0;
              }
            } else if (step.send && slot.state === 'ready') {
              const r = sendOrder(slot, warehouse);
              if (r.ok) {
                // Отправленный заказ обязан был увезти со склада все, что не
                // докуплено. Считаем ожидаемую убыль независимо от склада.
                // Складская часть — qty_filled за вычетом докупленного, а не
                // "весь qty, если последний взнос был self": позиция могла
                // закрыться в два приема (частичная погрузка + докупка
                // остатка, Т3-1), и последний взнос не описывает всю позицию.
                for (const p of slot.positions) {
                  shipped += p.qty_filled - (p.qty_purchased ?? 0);
                }
                slot.state = 'active';
                for (const p of slot.positions) {
                  p.qty_filled = 0;
                  p.filled_by = null;
                  p.qty_purchased = 0;
                }
              }
            }
            // reserved никогда не описывает больше, чем лежит.
            expect(reservedOf(warehouse, 'algae')).toBeLessThanOrEqual(
              qtyOf(warehouse, 'algae'),
            );
          }

          expect(qtyOf(warehouse, 'algae'), 'склад не сходится с числом отправленного').toBe(
            stock - shipped,
          );
        },
      ),
      { numRuns: 300 },
    );
  });
});

/**
 * Д-14. И-15 читает склад по сырому `qty`, а продажа — по `available`.
 *
 * `production.ts:128` считает признак «есть что продать» как
 * `occupiedGoods(warehouse).length > 0`, а `occupiedGoods` (`warehouse.ts:118`)
 * фильтрует по `qtyOf`. Продажа же идет через `consume` (`warehouse.ts:78`),
 * который считает по `availableOf`. Товар, целиком зарезервированный под
 * заказы дрона, склад показывает как «есть», а продать его нельзя.
 *
 * Итог ровно тот, против которого написана И-15 ([[mars-colony-frame]]:150):
 * кредитов ноль, ни одна грядка не занята, продать нечего — и посев не
 * становится бесплатным. Ни одного доступного действия в производстве.
 *
 * Тот же класс, что Д-10 (счетчик показывал `qty` вместо `qty - reserved`):
 * одно понятие, две формулы. Там врал экран, здесь — предохранитель.
 */
describe('Д-14: зарезервированный товар считается продаваемым', () => {
  it('склад из одного зарезервированного товара не отменяет спасение И-15', () => {
    const warehouse = createWarehouse(50);
    deposit(warehouse, 'algae', 10);
    reserve(warehouse, 'algae', 10);

    const fields = [createField(0), createField(1)];
    const ctx = { now: 0, warehouse, credits: 0, level: 2 };

    // Предпосылки И-15 налицо: кредитов нет, грядки пусты, продать нечего.
    expect(availableOf(warehouse, 'algae')).toBe(0);
    expect(sell('algae', 1, ctx).ok, 'продажа зарезервированного не должна проходить').toBe(
      false,
    );
    expect(plantingCost(GOODS.algae.price)).toBeGreaterThan(ctx.credits);

    const result = plant(fields[0]!, 'algae', ctx, fields);
    expect(result.ok, 'И-15: посев обязан стать бесплатным, иначе действий не осталось').toBe(
      true,
    );
    expect(result.softlock_rescued).toBe(true);
  });
});

/**
 * Д-15. Генератор дрона не применяет фолбэк, когда И-8 не сошлась.
 * СТАТУС ПОСЛЕ ЧЕСТНЫХ ВОРОТ ДЕФИЦИТА: латентный. Переформулирован.
 *
 * Структурно дыра на месте. `drone.ts:338-352`: валидная попытка выходит через
 * `break`, невалидные копятся в `best` ПО ДЛИНЕ, а на выходе (`:357`)
 * проверяется только пустота (`best.length === 0`). У шаттла в том же месте
 * (`shuttle.ts:290-293`) стоит проверка покрытия — `covered = best.length > 0 &&
 * easyRatio(best) >= COVERAGE_MIN.shuttle`, — и непокрытый набор уходит в
 * `fallbackMinimalSlots` с причиной `unresolvable_invariant`. Канон 1.3 требует
 * `if not isValidOrder -> fallbackMinimalOrder` от ДВИЖКА, канон 1.6/1.8 требует
 * при этом эмитить `order_generation_degraded`. Причина
 * `unresolvable_invariant` заведена в `economy.ts:237-241` и дроном не может
 * быть выдана НИ ПРИ КАКИХ входах: он знает только `empty_pool` и
 * `max_attempts`. Перечисление причин само по себе — свидетельство, что ветка
 * задумана и не написана.
 *
 * Чего больше НЕ происходит. После починки ворот дефицита (`:303` зовет
 * `isEasy`, а не `prod_time_sec`) непокрытый набор перестал встречаться на тех
 * пулах, которые строит игра: `MAX_DEFICIT_SLOTS = 1` дает не больше одной
 * тяжелой позиции на попытку, и покрытие проседает ниже 0.6 только на форме из
 * одной-двух позиций. Перебор 78912 заказов (уровни 2..21 x 16 наборов построек
 * x три наполнения склада x зерна, доска набирается слот за слотом, как в
 * `tick`) не дал НИ ОДНОГО нарушения; худшее покрытие — 0.67. Первый тест ниже
 * это фиксирует и служит сторожем: если нарушение вернется на игровых пулах,
 * он покраснеет.
 *
 * Чем дефект остается. `generateOrder` — экспортированная функция движка, и пул
 * ей передает вызывающий (`GeneratorContext.available_goods`). Канон 1.4
 * определяет `POOL_MODE[drone] = built_only` как
 * `unlockedGoods.filter(g -> g.requiredBuilding in builtBuildings)` — по букве
 * это пул из одних фабричных товаров построенных зданий. На таком пуле
 * (например `[mushroom_soup]` на ур.5) заказ уезжает с покрытием 0.00 молча:
 * ни фолбэка, ни события деградации. Шаттл на том же входе идет в фолбэк.
 *
 * Серьезность понижена до латентной: сегодня `availableGoodsFor` всегда кладет
 * в пул культуры, и они закрывают покрытие. Дыра ждет любой правки пула —
 * контента, POOL_MODE или порогов.
 */
describe('Д-15: заказ дрона уезжает, не пройдя И-8 по покрытию', () => {
  it('латентность подтверждена: на пулах, которые строит игра, нарушения нет', () => {
    const bad: string[] = [];
    let total = 0;
    let worst = 1;

    for (let level = 2; level <= 21; level++) {
      for (let mask = 0; mask < 16; mask++) {
        for (const stock of [0, 6]) {
          const buildings = new Set(BUILDINGS.filter((_, i) => (mask >> i) & 1));
          const pool: GoodId[] = availableGoodsFor(level, buildings);
          if (pool.length === 0) continue;
          const warehouse = createWarehouse(500);
          if (stock > 0) for (const id of pool) deposit(warehouse, id, stock);

          const rng = makeRng(level * 313 + mask * 7 + stock);
          const board: OrderSlot[] = [];
          // Доска набирается слот за слотом, как в `tick`: заказ видит уже
          // выданные, и анти-повтор гонит генератор по всем сорока попыткам.
          for (let i = 0; i < slotsAtLevel(level); i++) {
            const order = generateOrder(i, {
              level,
              warehouse,
              available_goods: pool,
              board,
              rng,
            });
            board.push(order);
            total += 1;
            const ratio = easyShare(order);
            worst = Math.min(worst, ratio);
            if (ratio < COVERAGE_MIN.drone) {
              bad.push(
                `ур.${level} постройки=${mask} склад=${stock} слот=${i}: ${shape(order)}`,
              );
            }
          }
        }
      }
    }

    expect(total).toBeGreaterThan(1000);
    expect(bad.join(' | '), `худшее покрытие ${worst.toFixed(2)}`).toBe('');
  });

  it('движок отдает непокрытый заказ молча: ни фолбэка, ни события деградации', () => {
    // Пул канона `built_only` в буквальном прочтении: фабричные товары
    // построенного здания. Ур.13 + текстильный модуль -> комбинезон. Он не
    // покрыт складом и по цепочке требует двух часов даже в количестве один
    // (две ткани, четыре хлопка) при пороге 30 минут — покрытие недостижимо
    // никаким количеством, и ровно для этого случая канон 1.6 держит фолбэк,
    // а 1.8 — событие `order_generation_degraded`.
    const warehouse = createWarehouse(500);
    const warned: unknown[] = [];
    const original = console.warn;
    console.warn = (...args: unknown[]) => warned.push(args[0]);

    let order: OrderSlot;
    try {
      order = generateOrder(0, {
        level: 13,
        warehouse,
        available_goods: ['jumpsuit'],
        board: [],
        rng: makeRng(1),
      });
    } finally {
      console.warn = original;
    }

    expect(easyShare(order), `выданный заказ: ${shape(order)}`).toBeLessThan(
      COVERAGE_MIN.drone,
    );
    // Одно из двух обязано быть верным: либо покрытие сошлось, либо генератор
    // объявил деградацию. Сейчас не верно ни то, ни другое.
    expect(warned, 'заказ ниже COVERAGE_MIN уехал без события деградации').toContain(
      'order_generation_degraded',
    );
  });

  it('шаттл на том же входе идет в фолбэк и деградацию объявляет', () => {
    // Контроль: движок один, механики две, и расходятся именно конструкции.
    const warehouse = createWarehouse(500);
    const warned: unknown[] = [];
    const original = console.warn;
    console.warn = (...args: unknown[]) => warned.push(args[0]);
    try {
      generateTrip({
        level: 13,
        warehouse,
        available_goods: ['jumpsuit'],
        previous: null,
        is_first_trip: false,
        arrival_no: 2,
        rng: makeRng(1),
      });
    } finally {
      console.warn = original;
    }
    expect(warned).toContain('order_generation_degraded');
  });
});

/**
 * Д-16. ЗАКРЫТ исполнителем, блок оставлен сторожем.
 *
 * Было: `drone.ts` тратил бюджет дефицита по условию
 * `GOODS[good].prod_time_sec > EASY_PRODUCE_MAX_MIN.drone * 60` — сырое время
 * ОДНОГО цикла ОДНОЙ единицы, без цепочки рецепта и без количества, — а флаг
 * `easy` той же позиции считал рядом через `productionTimeMinutes`. Одно
 * правило И-8, две формулы. Стало: обе точки зовут `isEasy` (`drone.ts:303`
 * и `:334`).
 *
 * Оба теста ниже зеленые и держат границу с ДВУХ сторон, каждый по
 * определению, а не по реализации: первый считает дефицитные позиции формулой
 * И-8 из каркаса (склад ИЛИ цепочка), второй сверяет с той же формулой флаг
 * каждой выданной позиции. Вернется любая из двух прежних редакций — один из
 * них покраснеет.
 *
 * Прежний второй тест снят: он сравнивал две константы, обе выведенные из
 * конфига, генератор в проверке не участвовал. Подробности — в комментарии на
 * месте замены.
 */
describe('Д-16 (закрыт): бюджет дефицита и флаг easy отвечают одной формулой', () => {
  it('дефицитных позиций в заказе не больше MAX_DEFICIT_SLOTS', () => {
    const bad: string[] = [];
    let total = 0;

    eachGeneratedOrder((order, warehouse, where) => {
      total += 1;
      // Определение И-8 дословно и НЕЗАВИСИМО от флага позиции: не покрыта
      // складом И не производима за порог. Читать здесь `p.easy` значило бы
      // выводить доказательство из той же реализации, которую проверяем.
      const deficit = order.positions.filter(
        (p) =>
          availableOf(warehouse, p.good_id) < p.qty &&
          productionTimeMinutes(p.good_id, p.qty, warehouse) > EASY_PRODUCE_MAX_MIN.drone,
      );
      if (deficit.length > MAX_DEFICIT_SLOTS) {
        bad.push(`${where}: дефицитных ${deficit.length} — ${shape(order)}`);
      }
    });

    expect(
      `${bad.length} из ${total}\n${bad.slice(0, 3).join('\n')}`,
      'заказы с числом дефицитных позиций выше И-8',
    ).toBe(`0 из ${total}\n`);
  });

  it('флаг easy позиции совпадает с определением И-8, посчитанным заново', () => {
    // Замена прежнего второго теста Д-16. Тот сравнивал две КОНСТАНТЫ
    // (`GOODS.fabric.prod_time_sec <= EASY_PRODUCE_MAX_MIN.drone * 60` против
    // порога по цепочке) и позеленеть не мог в принципе: обе выведены из
    // конфига, генератор в проверке не участвовал, и правка кода на результат
    // не влияла. Проверять надо не то, что два числа разные, а то, что
    // генератор отвечает на вопрос «легка ли позиция» одним способом.
    //
    // Сторож против возврата Д-12/Д-16: если хоть одно из двух мест снова
    // начнет читать `prod_time_sec` одного цикла, флаг разойдется с
    // определением И-8 и этот тест покраснеет первым.
    const lying: string[] = [];
    eachGeneratedOrder((order, warehouse, where) => {
      for (const p of order.positions) {
        const by_definition =
          availableOf(warehouse, p.good_id) >= p.qty ||
          productionTimeMinutes(p.good_id, p.qty, warehouse) <= EASY_PRODUCE_MAX_MIN.drone;
        if (p.easy !== by_definition) {
          lying.push(
            `${where}: ${p.good_id}x${p.qty} флаг=${p.easy} определение=${by_definition}`,
          );
        }
      }
    });
    expect(lying.slice(0, 3).join(' | '), 'флаг easy разошелся с И-8').toBe('');
  });
});

/**
 * Д-17. Витринная лестница цен применена к НАГРАДЕ, и обещанный коридор
 * премии перестает держаться.
 *
 * `drone.ts:385`: `credits: roundToShowcase(market_sum * premium)`.
 * `roundToShowcase` (`rushcost.ts:35`) — лестница ЦЕН И-4 («округление к
 * витринным числам 50/100/150»), у нее шаг до сотни и пол в один шаг. Строкой
 * выше премия аккуратно клемпится в `DRONE_PREMIUM_RANGE` (0.25..0.70), а
 * округление этот клемп отменяет: шаг лестницы не соотносится ни с рыночной
 * суммой, ни с границами коридора.
 *
 * Каркас раздел 7: дрон «платит кредиты с премией +25-70% к рынку». Перебором
 * находятся заказы с фактической премией и выше потолка, и ниже пола. Это не
 * косметика: премия выше потолка — незапланированный приток кредитов в
 * экономику, где кредитные стоки прописаны отдельным разделом каркаса.
 *
 * Общая функция, позванная из двух мест с разным смыслом: там цена в
 * изотопах, здесь награда в кредитах. Тот же класс, что дал Д-7.
 */
describe('Д-17: округление награды выносит премию дрона за коридор', () => {
  it('фактическая премия выданного заказа остается внутри DRONE_PREMIUM_RANGE', () => {
    const out: string[] = [];
    let total = 0;

    eachGeneratedOrder((order, _warehouse, where) => {
      total += 1;
      const market = order.positions.reduce(
        (sum, p) => sum + GOODS[p.good_id].price * p.qty,
        0,
      );
      if (market === 0) return;
      const effective = order.credits_reward / market - 1;
      if (
        effective > DRONE_PREMIUM_RANGE.max + 1e-9 ||
        effective < DRONE_PREMIUM_RANGE.min - 1e-9
      ) {
        out.push(
          `${where}: рынок ${market}, награда ${order.credits_reward}, ` +
            `премия ${(effective * 100).toFixed(1)}% — ${shape(order)}`,
        );
      }
    });

    expect(
      `${out.length} из ${total}\n${out.slice(0, 3).join('\n')}`,
      'заказы с премией вне коридора каркаса',
    ).toBe(`0 из ${total}\n`);
  });

  it('свойство: премия внутри коридора на любом наборе позиций субстрата', () => {
    const position = fc
      .tuple(
        fc.integer({ min: 0, max: ALL_GOOD_IDS.length - 1 }),
        fc.double({ min: 0, max: 1, noNaN: true }),
      )
      .map(([i, r]) => {
        const good_id = ALL_GOOD_IDS[i]!;
        const { min, max } = GOOD_BASE_QTY[good_id];
        return {
          good_id,
          qty: min + Math.round(r * (max - min)),
          qty_filled: 0,
          filled_by: null,
          qty_purchased: 0,
          easy: true,
        } as const;
      });

    fc.assert(
      fc.property(
        fc.array(position, { minLength: 1, maxLength: 6 }),
        fc.double({ min: 0, max: 1, noNaN: true }),
        fc.boolean(),
        (positions, jitter, has_deficit) => {
          const reward = orderReward(
            [...positions] as OrderPosition[],
            jitter,
            has_deficit,
          ).credits;
          const market = positions.reduce((s, p) => s + GOODS[p.good_id].price * p.qty, 0);
          const effective = reward / market - 1;
          expect(effective, `рынок ${market}, награда ${reward}`).toBeLessThanOrEqual(
            DRONE_PREMIUM_RANGE.max + 1e-9,
          );
          expect(effective).toBeGreaterThanOrEqual(DRONE_PREMIUM_RANGE.min - 1e-9);
        },
      ),
      { numRuns: 500 },
    );
  });
});

/**
 * Д-13-бис. Второе следствие Д-13, доказанное отдельно от его причины:
 * `sendOrder` не читает результат `shipReserved`.
 *
 * Д-13 показывает, КАК резерв расходится с составом заказа (чужой выброс), это
 * доказательство — что отправка не замечает расхождения ни при какой причине.
 * `shipReserved` (`warehouse.ts:102-109`) отказывает молча, возвращая `false`,
 * а `drone.ts:485-489` вызывает его ради побочного эффекта и в любом случае
 * отдает `ok: true` с полной наградой.
 *
 * Правило спеки: [[tz-drone-mars]] AC 8 — награда начисляется за ОТПРАВЛЕННЫЙ
 * заказ, и раздел 10 — «Погрузить» кладет товар в `Warehouse.reserved`,
 * `deliver` его физически уводит. Кредиты и XP без списания товара — приток,
 * не обеспеченный производством: экономика каркаса считает кредиты только от
 * проданного и отвезенного.
 *
 * Форма фикса — все-или-ничего, как у `deposit` в складе: половина отправки
 * (первая позиция уехала, вторая нет) — состояние, из которого нет корректного
 * выхода, заказ уже нельзя ни отправить, ни выбросить без потери товара.
 */
describe('Д-13-бис: отправка платит за груз, которого не списала', () => {
  const readyOrder = (positions: Array<[GoodId, number]>): OrderSlot => ({
    idx: 0,
    state: 'active',
    npc_name: 'тест',
    positions: positions.map(([good_id, qty]) => ({
      good_id,
      qty,
      qty_filled: 0,
      filled_by: null,
      qty_purchased: 0,
      easy: true,
    })),
    credits_reward: 250,
    xp_reward: 30,
    refresh_at: 0,
  });

  it('заказ без подтвержденного резерва не выдает ни кредитов, ни XP', () => {
    const warehouse = createWarehouse(50);
    deposit(warehouse, 'algae', 5);

    const order = readyOrder([['algae', 5]]);
    expect(loadPosition(order, 0, warehouse)).toBe(true);
    expect(order.state).toBe('ready');

    // Резерв под заказом исчез. В игре сюда приводит выброс соседнего заказа с
    // докупкой (Д-13); отправку не должно интересовать, что именно случилось.
    warehouse.cells['algae']!.reserved = 0;

    const result = sendOrder(order, warehouse);
    expect(result.ok, 'отправка без подтвержденного резерва').toBe(false);
    expect(result.credits, 'кредиты за неотгруженный заказ').toBe(0);
    expect(result.xp, 'XP за неотгруженный заказ').toBe(0);
    expect(qtyOf(warehouse, 'algae'), 'склад остался нетронутым').toBe(5);
  });

  it('расхождение по одной позиции не увозит остальные', () => {
    const warehouse = createWarehouse(50);
    deposit(warehouse, 'algae', 5);
    deposit(warehouse, 'soy', 4);

    const order = readyOrder([
      ['algae', 5],
      ['soy', 4],
    ]);
    expect(loadPosition(order, 0, warehouse)).toBe(true);
    expect(loadPosition(order, 1, warehouse)).toBe(true);
    warehouse.cells['soy']!.reserved = 0;

    expect(sendOrder(order, warehouse).ok).toBe(false);
    expect(qtyOf(warehouse, 'algae'), 'первая позиция не уезжает одна').toBe(5);
    expect(reservedOf(warehouse, 'algae'), 'резерв первой позиции сохранен').toBe(5);
  });

  it('нормальная отправка по-прежнему списывает и платит', () => {
    // Сторож против фикса, который просто запретил бы отправку.
    const warehouse = createWarehouse(50);
    deposit(warehouse, 'algae', 5);

    const order = readyOrder([['algae', 5]]);
    loadPosition(order, 0, warehouse);
    const result = sendOrder(order, warehouse);

    expect(result.ok).toBe(true);
    expect(result.credits).toBe(250);
    expect(qtyOf(warehouse, 'algae')).toBe(0);
    expect(reservedOf(warehouse, 'algae')).toBe(0);
  });

  it('докупленная позиция отправке не мешает: списывать нечего (И-12)', () => {
    const warehouse = createWarehouse(50);
    deposit(warehouse, 'algae', 5);

    const order = readyOrder([
      ['algae', 5],
      ['soy', 4],
    ]);
    loadPosition(order, 0, warehouse);
    expect(buyoutPosition(order, 1)).toBe(true);

    const result = sendOrder(order, warehouse);
    expect(result.ok, 'докупка не должна блокировать отправку').toBe(true);
    expect(qtyOf(warehouse, 'soy'), 'докупленное на складе не появляется').toBe(0);
  });
});

/**
 * Д-18. Докупка отсека ПОСЛЕ частичной погрузки запирает погруженный товар на
 * складе навсегда.
 *
 * `shuttle.ts:399` при погрузке ставит `slot.filled_by = 'self'`, `:438` при
 * докупке ПЕРЕЗАПИСЫВАЕТ его на `'purchase'`. Поле одно, а взносов в отсек два:
 * частичная погрузка разрешена явно (`:372-377`, ТЗ шаттла 6.2 «списывает
 * столько, сколько есть, отсек остается открытым до догрузки»). Отправка
 * (`:346-350`) читает этот флаг как признак ВСЕГО отсека:
 *
 *     if (slot.filled_by === 'purchase') continue;   // списывать нечего
 *     shipReserved(warehouse, slot.good_id, slot.qty_required);
 *
 * и пропускает отсек целиком. Товар, который игрок физически положил и который
 * склад держит в `reserved`, не уезжает и не возвращается: рейс улетел, отсека
 * больше нет, снять резерв нечем. `qty` остается занимать вместимость, а
 * `available` навсегда меньше `qty`.
 *
 * Класс тот же, что просили искать третьим: флаг записан по одному смыслу
 * («чем сделан последний взнос») и прочитан по другому («чем закрыт весь
 * отсек»). Предшественники — Д-12 (easy считался одной формулой, читался
 * другой) и Д-14 (склад «есть что продать» по `qty`, продажа по `available`).
 *
 * Нарушено: семантика склада `warehouse.ts:10-12` (`deliver: qty -= n,
 * reserved -= n`) и И-12 наизнанку — там докупка не должна касаться склада,
 * здесь она СЪЕДАЕТ уже погруженное. Это ровно тот дефект Д-1, который у
 * шаттла уже чинили: резерв заперт навсегда. Тяжесть высокая: потеря товара
 * молчаливая и необратимая, вместимость склада уменьшается на каждый такой
 * отсек, и вернуть ее в прототипе нечем.
 */
describe('Д-18: докупка поверх частичной погрузки съедает погруженное', () => {
  const drop = () => ({
    pity: {},
    stock: {},
    need: {},
    warehouse_avg_24h: {},
    gated_open: false,
    arrival_no: 5,
    constructions: [],
    rng: makeRng(3),
  });

  /** Рейс с одним отсеком: минимальная форма, где виден весь путь. */
  const tripWithSlot = (good_id: GoodId, qty_required: number): ShuttleTrip => ({
    state: 'ORDER',
    slots: [
      {
        idx: 0,
        good_id,
        qty_required,
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
    is_first_trip: false,
    arrival_no: 5,
  });

  it('погруженное со склада уезжает вместе с докупленным остатком', () => {
    const warehouse = createWarehouse(50);
    deposit(warehouse, 'algae', 3);
    const trip = tripWithSlot('algae', 5);

    // Игрок кладет все, что есть: три из пяти. ТЗ 6.2 это разрешает.
    expect(loadSlot(trip, 0, warehouse, 0, drop()).loaded).toBe(3);
    expect(reservedOf(warehouse, 'algae')).toBe(3);

    // Остаток докупает за изотопы. Рейс уходит сам, отсек закрыт.
    const result = buyoutSlot(trip, 0, warehouse, 0, drop());
    expect(result.departed, 'рейс обязан уйти: отсек закрыт целиком').toBe(true);

    expect(qtyOf(warehouse, 'algae'), 'погруженные три водоросли уехали со склада').toBe(0);
    expect(reservedOf(warehouse, 'algae'), 'резерв не остался висеть на улетевшем рейсе').toBe(
      0,
    );
  });

  it('свойство: после отправки склад не держит резерва ни при каком порядке', () => {
    fc.assert(
      fc.property(
        fc.integer({ min: 1, max: 8 }),
        fc.integer({ min: 0, max: 8 }),
        fc.boolean(),
        (need, stock, buy_first) => {
          const warehouse = createWarehouse(50);
          if (stock > 0) deposit(warehouse, 'algae', stock);
          const trip = tripWithSlot('algae', need);

          if (buy_first) {
            buyoutSlot(trip, 0, warehouse, 0, drop());
            loadSlot(trip, 0, warehouse, 0, drop());
          } else {
            loadSlot(trip, 0, warehouse, 0, drop());
            buyoutSlot(trip, 0, warehouse, 0, drop());
          }

          // Инвариант склада: reserved описывает товар, лежащий в ЖИВОМ слоте.
          // Рейс улетел — живых слотов нет, резерва быть не может.
          if (trip.state !== 'ORDER') {
            expect(
              reservedOf(warehouse, 'algae'),
              `запас ${stock}, нужно ${need}, порядок ${buy_first ? 'докупка-погрузка' : 'погрузка-докупка'}`,
            ).toBe(0);
          }
        },
      ),
      { numRuns: 200 },
    );
  });
});

/**
 * Д-19. ЗАКРЫТ в сторе. Доказательство переписано: прежняя редакция требовала
 * того, что архитектура запрещает сознательно.
 *
 * Было: освобождение резерва не будило очередь фабрики. `gameStore` звал
 * `notifyStockIncreased` только из `collectField` и `collectFactory`, а
 * доступный остаток растет и на другом пути — `discardOrderAt` зовет
 * `releaseReserved`, тот `unreserve`, и `availableOf` увеличивается ровно так
 * же, как от урожая. Слот в QUEUED этого не видел и голодал бессрочно, имея
 * нужный товар физически на полке (ТЗ производства 3.4: слот стартует, как
 * только входы появились на складе).
 *
 * Стало: `discardOrderAt` поднимает событие, и путь закрыт зеленым сторожем
 * `state/__tests__/factory-queue-wake.test.ts` — он живет в гейте, то есть
 * гоняется на каждом коммите.
 *
 * **Решение по существу: доменного контракта здесь нет, и вводить его нельзя.**
 * Прежняя редакция первого теста требовала, чтобы будил сам `unreserve`. Это
 * означало бы, что склад знает про фабрику: `warehouse.ts` — чистая структура
 * данных над ячейками, у `unreserve` нет и не может быть списка слотов
 * производства, а появление такой зависимости развернуло бы стрелку
 * `production -> warehouse` в цикл. Связывание двух подсистем — работа тонкой
 * обертки состояния (`mars-colony/CLAUDE.md`, раздел «Архитектура»), и она эту
 * работу делает. Решение записано в реестр [[spec-prototype-build]] 8.15.
 *
 * Что осталось здесь сторожем: домен обязан давать будильник, который поднимает
 * слот на резерве, снятом ЛЮБЫМ путем, а не только на урожае. Класс уже
 * возвращался однажды внутри самой `onWarehouseStockIncreased` (ранняя
 * остановка цикла), поэтому проверка не лишняя.
 */
describe('Д-19 (закрыт): домен будит очередь на любом росте доступного остатка', () => {
  it('событие поднимает слот, чьи входы вернулись из резерва, а не из урожая', () => {
    const warehouse = createWarehouse(50);
    deposit(warehouse, 'soy', 2);
    // Соя занята заказом дрона: доступного остатка нет.
    reserve(warehouse, 'soy', 2);

    const slot = createFactorySlot(0, 'food_module');
    const ctx = { now: 0, warehouse, credits: 0, level: 21 };
    expect(enqueue(slot, 'protein_bar', ctx).reason).toBe('no_inputs');
    expect(slot.state).toBe('QUEUED');

    // Игрок выбросил заказ — соя вернулась в доступные. Для склада это ровно
    // то же событие, что и урожай: `available` вырос.
    unreserve(warehouse, 'soy', 2);
    expect(availableOf(warehouse, 'soy')).toBe(2);

    expect(onWarehouseStockIncreased([slot], ctx), 'будильник не поднял ни одного слота').toBe(
      1,
    );
    expect(slot.state, 'слот голодает при сырье на полке').toBe('PRODUCING');
  });

  it('склад остается чистой структурой: про производство он не знает', () => {
    // Сторож против «починки» в неверную сторону. Доказательство Д-19 требовало
    // будильника внутри `unreserve`; если кто-то решит его туда положить,
    // `warehouse.ts` придется научить про слоты фабрики — и зависимость
    // `production -> warehouse` станет циклом.
    const source = readFileSync(join(HERE, '..', 'warehouse.ts'), 'utf8');
    const leaks = source.match(/^import .*\bproduction\b.*$/gm) ?? [];
    expect(leaks.join('\n'), 'склад импортирует производство').toBe('');
  });
});

/**
 * Д-20. Анти-повтор доски дрона не выполняется: на доске стоят карточки с
 * одинаковым товарным составом.
 *
 * Каркас раздел 8 п.1 и [[tz-common-systems-mars]] 1.3: `REPEAT_SCOPE=board` —
 * «анти-повтор считается ПОПАРНО между всеми одновременно видимыми заказами
 * (дрон, где видны все 9)». Каркас 5, И-8: «повтор <=50% позиций».
 *
 * Два независимых слома, оба ведут к одному экрану.
 *
 *   1. `drone.ts:199-211`, `maxRepeatRatio` считает `shared / ids.size` — долю
 *      от НОВОГО заказа. Пара, где старый заказ целиком лежит внутри нового,
 *      проходит: 2 общих товара из 6 = 0.33 при пороге 0.5, хотя у старой
 *      карточки повторяется 100% состава. Попарность в спеке симметрична,
 *      формула — нет.
 *   2. `gameStore.tick` наполняет пустые слоты циклом
 *      `while (orders.length < target) orders.push(makeOrder(...))`, а
 *      `makeOrder` берет доску из `get().orders` — состояния, которое в этом
 *      тике еще не записано. Заказы, выданные одним тиком (первое открытие
 *      доски, каждый левелап, добавляющий слот), друг друга не видят вовсе.
 *
 * Замер на девятислотовой доске 15 уровня: 60 из 60 досок содержат пару,
 * нарушающую порог; 1120 пар нарушают его В ОБЕ стороны (то есть при любом
 * прочтении знаменателя); на 16 досках из 60 нашлись пары с ПОЛНОСТЬЮ
 * совпадающим составом товаров. Последнее не зависит ни от какого толкования:
 * две карточки просят одно и то же.
 *
 * Тяжесть средняя: экономику не рвет, но именно анти-повтор отвечает за то,
 * ради чего доска из девяти слотов существует, — за выбор. Девять карточек,
 * половина которых про водоросли, читаются как одна.
 */
describe('Д-20: доска дрона выдает повторяющиеся заказы', () => {
  const goodsOf = (slot: OrderSlot) => new Set(slot.positions.map((p) => p.good_id));

  it('ни одна пара видимых заказов не повторяется больше REPEAT_CAP', () => {
    const LEVEL = 15;
    const pool: GoodId[] = availableGoodsFor(LEVEL, new Set(BUILDINGS));
    const bad: string[] = [];
    let boards = 0;
    let identical = 0;

    for (let seed = 1; seed <= 40; seed++) {
      const warehouse = createWarehouse(300);
      const rng = makeRng(seed * 7919);
      const board: OrderSlot[] = [];
      for (let i = 0; i < slotsAtLevel(LEVEL); i++) {
        board.push(
          generateOrder(i, { level: LEVEL, warehouse, available_goods: pool, board, rng }),
        );
      }
      boards += 1;

      for (let i = 0; i < board.length; i++) {
        for (let j = i + 1; j < board.length; j++) {
          const a = goodsOf(board[i]!);
          const b = goodsOf(board[j]!);
          if (a.size === 0 || b.size === 0) continue;
          const shared = [...a].filter((id) => b.has(id)).length;
          // Нарушение считаем только двустороннее: тогда оно не зависит от
          // того, чей состав стоит в знаменателе.
          if (shared / a.size > REPEAT_CAP && shared / b.size > REPEAT_CAP) {
            if (a.size === b.size && shared === a.size) identical += 1;
            if (bad.length < 3) {
              bad.push(
                `зерно ${seed}, слоты ${i} и ${j}: ${shape(board[i]!)} // ${shape(board[j]!)}`,
              );
            }
          }
        }
      }
    }

    expect(
      `${bad.length ? 'есть' : 'нет'}, из них с совпадающим составом ${identical}, на ${boards} досках | ${bad.join(' | ')}`,
      'пары видимых заказов сверх REPEAT_CAP',
    ).toBe(`нет, из них с совпадающим составом 0, на ${boards} досках | `);
  });
});

/**
 * Д-21. ЗАКРЫТ. Блок оставлен сторожем.
 *
 * Было: три теста гейта в `drone.test.ts` определяли дефицитную позицию как
 * `GOODS[good].prod_time_sec > EASY_PRODUCE_MAX_MIN.drone * 60` — копию строки,
 * которая СТОЯЛА в генераторе до починки ворот дефицита. Определение И-8 —
 * другое: «покрыто складом ИЛИ производимо <=30 мин», где время считается по
 * цепочке рецепта и по нужному количеству (`productionTimeMinutes`).
 *
 * На субстрате MVP условие `prod_time_sec > 1800` выполняет РОВНО ОДИН товар из
 * четырнадцати — томаты. Замер этого блока: копия старой реализации отбирала
 * 2563 позиции там, где определение И-8 отбирает 8952. Фильтр был почти всегда
 * пуст, и `expect(deficit).toBeLessThanOrEqual(MAX_DEFICIT_SLOTS)` проверял, что
 * ноль не больше единицы: верни в генератор прежнюю формулу — тест остался бы
 * зеленым.
 *
 * Стало: определение И-8 вынесено в `canon.ts` одним местом и позвано из всех
 * трех точек (`drone.test.ts` — бюджет дефицита, дозировка пинча, признак
 * `has_deficit_position`). Тесты ниже — сторож с двух сторон: первый сверяет
 * общий предикат с определением, выписанным здесь заново из каркаса, второй не
 * дает завести четвертую копию формулы в тестах дрона.
 */
describe('Д-21 (закрыт): дефицит в тестах гейта считается определением И-8', () => {
  it('общий предикат канона и определение И-8 выбирают одно множество позиций', () => {
    let by_gate_test = 0;
    let by_definition = 0;
    let positions = 0;

    eachGeneratedOrder((order, warehouse) => {
      for (const p of order.positions) {
        positions += 1;
        // Ровно тот предикат, которым дефицит меряют тесты гейта.
        if (deficitByCanon(p.good_id, p.qty, warehouse)) by_gate_test += 1;
        // Определение И-8 из каркаса 5 и канона 1.3, выписанное здесь заново:
        // сверять `canon.ts` с самим собой смысла бы не было.
        if (
          availableOf(warehouse, p.good_id) < p.qty &&
          productionTimeMinutes(p.good_id, p.qty, warehouse) > EASY_PRODUCE_MAX_MIN.drone
        ) {
          by_definition += 1;
        }
      }
    });

    expect(positions).toBeGreaterThan(1000);
    // Множество непустое: сверка двух пустых фильтров ничего не доказывает.
    expect(by_definition).toBeGreaterThan(1000);
    expect(
      `гейт видит ${by_gate_test}, определение видит ${by_definition}`,
      'два определения дефицита на одних и тех же заказах',
    ).toBe(`гейт видит ${by_definition}, определение видит ${by_definition}`);
  });

  it('в тестах дрона не заведено собственной формулы дефицита', () => {
    // Структурный сторож: числовая сверка выше поймает только правку
    // `canon.ts`, а дефект был в том, что рядом дописали ЧЕТВЕРТУЮ копию
    // формулы. Ищем связку «сырое время одного цикла против порога easy» —
    // именно она отличала копию старой реализации от определения И-8.
    const source = readFileSync(join(HERE, 'drone.test.ts'), 'utf8');
    const copies = source.match(/prod_time_sec[^\n]*EASY_PRODUCE_MAX_MIN/g) ?? [];
    expect(copies.join('\n'), 'дефицит снова считается копией реализации').toBe('');
  });
});

/**
 * Д-22. ЗАКРЫТ вместе с Д-18 и Д-14. Блок переписан в сторожа.
 *
 * Было: необратимый тупик, у игрока не оставалось ни одного действия. Он
 * складывался из двух дефектов, каждый из которых сам по себе выглядел терпимо.
 *
 *   Д-18 запирал товар в `reserved` навсегда: `qty` продолжал занимать
 *   вместимость, `available` был равен нулю, владельца у резерва не было.
 *   Д-14 читал «есть что продать» по `qty` (через `occupiedGoods`), а продажа
 *   шла по `available`. Запертый товар отменял спасение И-15, продавая при этом
 *   ничего.
 *
 * Дальше состояние собиралось само: вместимость выедена запертым товаром,
 * поэтому сбор урожая отклонялся целиком (`collectField` -> `warehouse_full`);
 * продать было нечего, потому что доступного нет; посеять не на что, потому что
 * кредитов нет, а И-15 считала, что склад полон продаваемого.
 *
 * Проверено руками после починки Д-18: та же серия из двенадцати рейсов
 * оставляет склад ПУСТЫМ (`qty` = 0, `reserved` = 0) — погруженное уезжает
 * вместе с докупленным остатком, и запирать нечего. Мир, который описывало
 * прежнее доказательство, больше не собирается: оно падало на собственной
 * предпосылке (`qty` = 0 вместо ожидаемых 40+), то есть врало так же, как
 * зеленый тест на пустом месте.
 *
 * Сторож гоняет ту же серию рейсов и держит обе стороны И-15
 * ([[mars-colony-frame]] раздел 5): у игрока обязано остаться хотя бы одно
 * действие, и на складе не должно висеть резерва без владельца. Вторая
 * проверка — прямой сторож против возврата Д-18: он вернется как утечка
 * резерва раньше, чем как тупик.
 */
describe('Д-22 (закрыт): серия рейсов с докупкой не запирает игрока', () => {
  const drop = () => ({
    pity: {},
    stock: {},
    need: {},
    warehouse_avg_24h: {},
    gated_open: false,
    arrival_no: 5,
    constructions: [],
    rng: makeRng(11),
  });

  const tripWithSlot = (good_id: GoodId, qty_required: number): ShuttleTrip => ({
    state: 'ORDER',
    slots: [
      {
        idx: 0,
        good_id,
        qty_required,
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
    is_first_trip: false,
    arrival_no: 5,
  });

  it('после серии рейсов с докупкой у игрока остается хотя бы одно действие', () => {
    const warehouse = createWarehouse(50);

    // Обычная игра: собрал урожай, отвез шаттлом, остаток отсека докупил.
    // Именно этот путь и запирал товар на складе (Д-18).
    const leaks: string[] = [];
    for (let trip_no = 0; trip_no < 12; trip_no++) {
      if (!deposit(warehouse, 'algae', 4)) break;
      const trip = tripWithSlot('algae', 5);
      loadSlot(trip, 0, warehouse, 0, drop());
      buyoutSlot(trip, 0, warehouse, 0, drop());

      // Рейс ушел — живых отсеков больше нет, снять резерв нечем. Все, что
      // осталось в `reserved`, принадлежит никому и не уйдет ни по какому
      // таймеру: это и есть утечка, из которой рос тупик.
      if (trip.state !== 'ORDER' && reservedOf(warehouse, 'algae') > 0) {
        leaks.push(
          `рейс ${trip_no}: резерв ${reservedOf(warehouse, 'algae')} ` +
            `при запасе ${qtyOf(warehouse, 'algae')}`,
        );
      }
    }
    expect(leaks.join(' | '), 'резерв без владельца на улетевшем рейсе').toBe('');

    // Кредиты и изотопы ушли на те самые докупки — худший случай для И-15.
    const fields = [createField(0), createField(1)];
    const ctx = { now: 0, warehouse, credits: 0, level: 9 };

    // Действие 1 — продать.
    const sold = sell('algae', 1, ctx);
    // Действие 2 — собрать урожай.
    fields[0]!.state = 'READY';
    fields[0]!.good_id = 'algae';
    const collected = collectField(fields[0]!, ctx);
    fields[0]!.state = 'READY'; // отказ слот не тронул
    // Действие 3 — посеять. Кредитов нет, значит обязана сработать И-15.
    const planted = plant(fields[1]!, 'algae', ctx, [fields[1]!]);

    const available_actions = [
      sold.ok ? 'продажа' : '',
      collected.ok ? 'сбор' : '',
      planted.ok ? 'посев' : '',
    ].filter(Boolean);

    expect(
      available_actions.join(', ') ||
        `ни одного: продажа ${sold.reason}, сбор ${collected.reason}, посев ${planted.reason}`,
      'И-15: игрок не может остаться без единого действия',
    ).not.toBe('');
  });
});
