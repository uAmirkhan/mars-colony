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

import type { GoodId } from './types';
import { WAREHOUSE_START_CAPACITY, WAREHOUSE_UPGRADE_STEP, WAREHOUSE_MAX_CAPACITY } from './config/economy';

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

/** Апгрейд склада: +10 к капасити, потолок MVP — 300. */
export function upgradeCapacity(w: WarehouseState): boolean {
  if (w.capacity >= WAREHOUSE_MAX_CAPACITY) return false;
  w.capacity = Math.min(WAREHOUSE_MAX_CAPACITY, w.capacity + WAREHOUSE_UPGRADE_STEP);
  return true;
}

export function occupiedGoods(w: WarehouseState): GoodId[] {
  return (Object.keys(w.cells) as GoodId[]).filter((id) => qtyOf(w, id) > 0);
}
