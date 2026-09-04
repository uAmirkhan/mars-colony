import { generateOrder, availableGoodsFor, type OrderSlot } from '../src/domain/drone';
import { generateTrip } from '../src/domain/shuttle';
import { createWarehouse, deposit } from '../src/domain/warehouse';
import { GOODS, ALL_GOOD_IDS } from '../src/domain/config/goods';
import { WAREHOUSE_START_CAPACITY, FACTORY_PRICES, NUM_VISIBLE_ORDERS } from '../src/domain/config/economy';
import type { BuildingType } from '../src/domain/types';

function mulberry32(a: number) {
  return () => {
    a |= 0; a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const ALL_BUILDINGS: BuildingType[] = ['food_module', 'mining_site', 'atmospheric_module', 'textile_module'];

console.log('=== ЗАКАЗ ДРОНА ПРОТИВ СКЛАДА (капасити 50, резерв места НЕ освобождает) ===');
console.log('lvl | заказов на доске | max суммарный заказ | средний | доля заказов > 50 | max одна позиция');
for (let lvl = 2; lvl <= 21; lvl++) {
  const buildings = new Set<string>(ALL_BUILDINGS.filter((b) => (FACTORY_PRICES as any)[b].unlock_level <= lvl));
  const goods = availableGoodsFor(lvl, buildings);
  if (goods.length === 0) continue;
  const rng = mulberry32(1234 + lvl);
  const w = createWarehouse(WAREHOUSE_START_CAPACITY);
  let maxTotal = 0, sum = 0, over = 0, n = 0, maxPos = 0;
  for (let i = 0; i < 2000; i++) {
    const slot: OrderSlot = generateOrder(0, { level: lvl, warehouse: w, available_goods: goods, board: [], rng });
    const total = slot.positions.reduce((a, p) => a + p.qty, 0);
    maxTotal = Math.max(maxTotal, total);
    for (const p of slot.positions) maxPos = Math.max(maxPos, p.qty);
    sum += total; n++;
    if (total > WAREHOUSE_START_CAPACITY) over++;
  }
  const boardOrders = NUM_VISIBLE_ORDERS.find((r) => lvl >= r.from_level)?.orders ?? 0;
  console.log(
    'ур.' + String(lvl).padStart(2) + ' | ' + String(boardOrders).padStart(2) +
    ' | max ' + String(maxTotal).padStart(3) +
    ' | сред ' + (sum / n).toFixed(1).padStart(5) +
    ' | >50: ' + ((over / n) * 100).toFixed(1).padStart(5) + '%' +
    ' | max одна позиция ' + String(maxPos).padStart(3) +
    ' | вся доска сразу max ~' + String(maxTotal * boardOrders).padStart(4)
  );
}

console.log('');
console.log('=== РЕЙС ШАТТЛА ПРОТИВ СКЛАДА ===');
for (let lvl = 5; lvl <= 21; lvl++) {
  const buildings = new Set<string>(ALL_BUILDINGS.filter((b) => (FACTORY_PRICES as any)[b].unlock_level <= lvl));
  const goods = availableGoodsFor(lvl, buildings);
  const rng = mulberry32(999 + lvl);
  const w = createWarehouse(WAREHOUSE_START_CAPACITY);
  let maxTotal = 0, sum = 0, over = 0, n = 0;
  for (let i = 0; i < 1000; i++) {
    const trip = generateTrip({ level: lvl, warehouse: w, available_goods: goods, rng, previous: null, now: 0, is_first_trip: false, arrival_no: 5 } as any);
    const total = trip.slots.reduce((a, s) => a + s.qty_required, 0);
    maxTotal = Math.max(maxTotal, total); sum += total; n++;
    if (total > WAREHOUSE_START_CAPACITY) over++;
  }
  console.log('ур.' + String(lvl).padStart(2) + ': max рейс ' + String(maxTotal).padStart(3) + ', сред ' + (sum / n).toFixed(1).padStart(5) + ', доля >50 склада: ' + ((over / n) * 100).toFixed(1) + '%');
}

console.log('');
console.log('=== ЕДИНОВРЕМЕННАЯ НАГРУЗКА: заказ дрона + рейс шаттла на одном складе 50 ===');
console.log('(резерв не освобождает место, поэтому обе механики делят одни 50 ячеек)');
