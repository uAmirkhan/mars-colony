/**
 * Доказательства дефектов, найденных на прогоне 3.
 *
 * Файл живет вне гейта на коммите (`npm run test:gate` его исключает) и
 * гоняется отдельно: `npm run defects`. Причина в том, что цикл требует
 * доказывать дефект падающим тестом, а гейт запрещает коммитить красное —
 * вместе они запрещали бы сохранять работу между «нашли» и «починили».
 */

import { describe, expect, it } from 'vitest';
import { makeRng } from '../../sim/rng';
import { EASY_PRODUCE_MAX_MIN } from '../config/economy';
import { availableGoodsFor, generateOrder } from '../drone';
import { productionTimeMinutes } from '../rushcost';
import type { GoodId } from '../types';
import { createWarehouse } from '../warehouse';

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
    const BUILDINGS = ['food_module', 'mining_site', 'atmospheric_module', 'textile_module'];
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
