/**
 * Склад: qty, reserved, капасити, блокировка сбора при переполнении.
 * Источник истины — [[tz-production-mars]] раздел 3.
 *
 * Семантика, которую нельзя перепутать:
 *   qty       — физически занимает место, независимо от резерва
 *   reserved  — подмножество qty, положенное в слот заказа, но еще не отправленное
 *   available — qty - reserved, то, что можно потратить прямо сейчас
 *
 * fill:    reserved += n           (капасити НЕ освобождается)
 * clear:   reserved -= n           (qty не меняется)
 * deliver: qty -= n, reserved -= n (капасити освобождается)
 */

import {
  WAREHOUSE_MAX_CAPACITY,
  WAREHOUSE_START_CAPACITY,
  WAREHOUSE_UPGRADE_PRICE_PER_CAPACITY,
  WAREHOUSE_UPGRADE_STEP,
} from './config/economy';
import type { GoodId } from './types';

export interface WarehouseState {
  cells: Record<string, { qty: number; reserved: number }>;
  capacity: number;
}

export function createWarehouse(capacity = WAREHOUSE_START_CAPACITY): WarehouseState {
  return { cells: {}, capacity };
}

export function qtyOf(w: WarehouseState, good_id: GoodId): number {
  return w.cells[good_id]?.qty ?? 0;
}

export function reservedOf(w: WarehouseState, good_id: GoodId): number {
  return w.cells[good_id]?.reserved ?? 0;
}

export function availableOf(w: WarehouseState, good_id: GoodId): number {
  return qtyOf(w, good_id) - reservedOf(w, good_id);
}

export function totalQty(w: WarehouseState): number {
  return Object.values(w.cells).reduce((sum, c) => sum + c.qty, 0);
}

export function freeSpace(w: WarehouseState): number {
  return w.capacity - totalQty(w);
}

export function isFull(w: WarehouseState): boolean {
  return freeSpace(w) <= 0;
}

/** Хватает ли места, чтобы принять qty единиц целиком. Частичного приема нет. */
export function canAccept(w: WarehouseState, qty: number): boolean {
  return freeSpace(w) >= qty;
}

function cell(w: WarehouseState, good_id: GoodId) {
  if (!w.cells[good_id]) w.cells[good_id] = { qty: 0, reserved: 0 };
  return w.cells[good_id];
}

/**
 * Положить товар на склад. Все или ничего: при нехватке места возвращает false
 * и НЕ кладет ничего. Сервер никогда не обрезает qty до остатка капасити
 * и не продает излишек автоматически ([[tz-production-mars]] 3.2).
 */
export function deposit(w: WarehouseState, good_id: GoodId, qty: number): boolean {
  if (qty <= 0) return false;
  if (!canAccept(w, qty)) return false;
  cell(w, good_id).qty += qty;
  return true;
}

/** Потратить свободный товар (продажа, вход фабрики). Считает по available, не по qty. */
export function consume(w: WarehouseState, good_id: GoodId, qty: number): boolean {
  if (qty <= 0) return false;
  if (availableOf(w, good_id) < qty) return false;
  cell(w, good_id).qty -= qty;
  return true;
}

/** fill: положить в слот заказа. Товар остается на складе, капасити не освобождается. */
export function reserve(w: WarehouseState, good_id: GoodId, qty: number): boolean {
  if (qty <= 0) return false;
  if (availableOf(w, good_id) < qty) return false;
  cell(w, good_id).reserved += qty;
  return true;
}

/** clear: вернуть из слота заказа до отправки. qty не меняется. */
export function unreserve(w: WarehouseState, good_id: GoodId, qty: number): boolean {
  if (qty <= 0) return false;
  if (reservedOf(w, good_id) < qty) return false;
  cell(w, good_id).reserved -= qty;
  return true;
}

/** deliver: заказ отправлен, зарезервированное физически покидает склад. */
export function shipReserved(w: WarehouseState, good_id: GoodId, qty: number): boolean {
  if (qty <= 0) return false;
  const c = w.cells[good_id];
  if (!c || c.reserved < qty || c.qty < qty) return false;
  c.qty -= qty;
  c.reserved -= qty;
  return true;
}

/** Цена следующего расширения в кредитах; 0 — потолок достигнут. */
export function upgradePrice(w: WarehouseState): number {
  if (w.capacity >= WAREHOUSE_MAX_CAPACITY) return 0;
  return w.capacity * WAREHOUSE_UPGRADE_PRICE_PER_CAPACITY;
}

/** Апгрейд склада: +10 к капасити, потолок MVP — 300. */
export function upgradeCapacity(w: WarehouseState): boolean {
  if (w.capacity >= WAREHOUSE_MAX_CAPACITY) return false;
  w.capacity = Math.min(WAREHOUSE_MAX_CAPACITY, w.capacity + WAREHOUSE_UPGRADE_STEP);
  return true;
}

/**
 * Что физически лежит на складе, включая зарезервированное под заказы.
 * Это вопрос «что занимает вместимость», а не «что можно потратить».
 */
export function occupiedGoods(w: WarehouseState): GoodId[] {
  return (Object.keys(w.cells) as GoodId[]).filter((id) => qtyOf(w, id) > 0);
}

/**
 * Что игрок может продать или потратить прямо сейчас.
 *
 * Отдельная функция, а не флаг у предыдущей, потому что смысл разный и путать
 * их дорого. Инвариант И-15 (спасение от тупика) спрашивает именно это: если
 * весь склад зарезервирован под заказы дрона, продать нечего, и посев обязан
 * стать бесплатным. Раньше он спрашивал `occupiedGoods` и получал «есть что
 * продать» на складе, где продать было нечего, — спасение не срабатывало, и
 * игрок оставался без единого доступного действия.
 *
 * Тот же класс ошибки, что и у счетчика отсека шаттла: одно правило считается
 * по сырому `qty` в одном месте и по `available` в другом.
 */
export function sellableGoods(w: WarehouseState): GoodId[] {
  return (Object.keys(w.cells) as GoodId[]).filter((id) => availableOf(w, id) > 0);
}
