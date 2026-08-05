/**
 * Определения канона, выписанные из ДОКУМЕНТА, для тестов.
 *
 * Это не часть игры и не второй домен: файл существует ровно потому, что
 * доказательство обязано быть выведено из спеки, а не из проверяемого кода.
 * Тест, который копирует выражение из `drone.ts`, зеленеет при любой
 * реализации — он сравнивает код с самим собой.
 *
 * Живет отдельным модулем, а не локальной функцией в каждом файле тестов,
 * потому что копий определения уже было три и они разъехались: один тест
 * считал дефицит по цепочке рецепта, два соседних — по `prod_time_sec` одного
 * цикла (Д-21). Одно правило И-8 — одно место, где оно записано для тестов.
 */

import { EASY_PRODUCE_MAX_MIN } from '../config/economy';
import { productionTimeMinutes } from '../rushcost';
import type { GoodId } from '../types';
import { availableOf, type WarehouseState } from '../warehouse';

/**
 * «Легкая» позиция по И-8.
 *
 * [[mars-colony-frame]] раздел 5, И-8: «>=60% позиций заказа покрыто складом
 * или производимо <=30 мин». [[tz-common-systems-mars]] 1.3 записывает тот же
 * предикат формулой: `isEasy = (stock >= qty) or (productionTimeMinutes(good,
 * qty, player) <= EASY_PRODUCE_MAX_MIN[mechanic])`.
 *
 * Два момента, на которых определение уже теряли:
 *   — склад читается по `available`, а не по `qty`: зарезервированное под
 *     другой заказ игрок в этот заказ положить не может;
 *   — время считается по ЦЕПОЧКЕ рецепта и по НУЖНОМУ количеству, а не по
 *     `prod_time_sec` одного цикла самого товара.
 */
export function easyByCanon(
  good_id: GoodId,
  qty: number,
  w: WarehouseState,
  mechanic: keyof typeof EASY_PRODUCE_MAX_MIN = 'drone',
): boolean {
  if (availableOf(w, good_id) >= qty) return true;
  return productionTimeMinutes(good_id, qty, w) <= EASY_PRODUCE_MAX_MIN[mechanic];
}

/** Дефицитная позиция по И-8 — отрицание «легкой». Отдельное имя, чтобы читалось. */
export function deficitByCanon(
  good_id: GoodId,
  qty: number,
  w: WarehouseState,
  mechanic: keyof typeof EASY_PRODUCE_MAX_MIN = 'drone',
): boolean {
  return !easyByCanon(good_id, qty, w, mechanic);
}
