import { GOODS, ALL_GOOD_IDS, harvestQty, bracketMult, slotQuantity } from '../src/domain/config/goods';
import {
  FACTORY_PRICES, FIELD_SLOT_UNLOCK_LEVELS, fieldsAtLevel, NUM_VISIBLE_ORDERS,
  SLOT_COUNT_WEIGHTS, FLIGHT_TIMER_MIN, WAREHOUSE_START_CAPACITY, WAREHOUSE_UPGRADE_STEP,
  WAREHOUSE_MAX_CAPACITY, FACTORY_SECOND_INSTANCE_LEVEL, MODULE_PRICE_ISOTOPES, TIER_WEIGHTS,
  plantingCost,
} from '../src/domain/config/economy';
import { xpToNext, cumulativeXpToReach, levelUpReward, EMPTY_LEVELS, MECHANIC_UNLOCK_LEVEL } from '../src/domain/config/levels';
import { CONSTRUCTION_RECIPE, MODULES } from '../src/domain/config/modules';

const bracketAt = (rows: any[], lvl: number, key: string) => {
  const r = rows.find((x: any) => lvl >= x.from_level);
  return r ? r[key] : null;
};

function contentOf(lvl: number) {
  const goods = ALL_GOOD_IDS.filter((id) => GOODS[id].unlock_level === lvl);
  const buildings = (Object.keys(FACTORY_PRICES) as any[]).filter((k) => (FACTORY_PRICES as any)[k].unlock_level === lvl);
  const misc: string[] = [];
  for (const [m, l] of Object.entries(MECHANIC_UNLOCK_LEVEL)) if (l === lvl) misc.push('механика:' + m);
  if (FIELD_SLOT_UNLOCK_LEVELS.includes(lvl)) misc.push('+грядка');
  for (const def of Object.values(CONSTRUCTION_RECIPE)) if (def.unlock_level === lvl) misc.push('стройка:' + def.kind);
  if (lvl === FACTORY_SECOND_INSTANCE_LEVEL) misc.push('2-й экземпляр фабрик');
  if (NUM_VISIBLE_ORDERS.some((r) => r.from_level === lvl)) misc.push('доска:' + bracketAt(NUM_VISIBLE_ORDERS, lvl, 'orders'));
  if (SLOT_COUNT_WEIGHTS.some((r) => r.from_level === lvl)) misc.push('брекет-отсеков');
  if (FLIGHT_TIMER_MIN.some((r) => r.from_level === lvl)) misc.push('рейс-' + bracketAt(FLIGHT_TIMER_MIN, lvl, 'minutes') + 'мин');
  if (lvl === 10 || lvl === 15 || lvl === 20) misc.push('bracket_mult-' + bracketMult(lvl));
  return { goods, buildings, misc, n: goods.length + buildings.length + misc.length };
}

console.log('=== ТАБЛИЦА ПРОГРЕССИИ ===');
for (let lvl = 1; lvl <= 21; lvl++) {
  const c = contentOf(lvl);
  const rw = levelUpReward(lvl);
  const emptyMark = EMPTY_LEVELS.includes(lvl) ? ' [в EMPTY_LEVELS]' : '';
  console.log(
    'ур.' + String(lvl).padStart(2) +
    ' | xp_to_next ' + String(xpToNext(lvl)).padStart(5) +
    ' | cum ' + String(cumulativeXpToReach(lvl)).padStart(6) +
    ' | награда ' + String(rw.credits).padStart(5) + 'кр/' + String(rw.isotopes).padStart(2) + 'изо' + emptyMark +
    ' | грядок ' + fieldsAtLevel(lvl) +
    ' | заказов ' + (bracketAt(NUM_VISIBLE_ORDERS, lvl, 'orders') ?? 0) +
    ' | CONTENT=' + c.n
  );
  console.log('        товары: ' + (c.goods.map((g) => g + '[' + (GOODS[g].required_building ?? 'грядка') + ']').join(', ') || '-'));
  console.log('        здания: ' + (c.buildings.join(', ') || '-') + ' | прочее: ' + (c.misc.join('; ') || '-'));
}

console.log('');
console.log('=== УРОВНИ БЕЗ НОВОГО КОНТЕНТА ===');
for (let lvl = 1; lvl <= 21; lvl++) {
  const c = contentOf(lvl);
  if (c.n === 0) console.log('  ур.' + lvl + ': ПУСТО. числится в EMPTY_LEVELS? ' + EMPTY_LEVELS.includes(lvl));
}
console.log('EMPTY_LEVELS в конфиге = ' + JSON.stringify(EMPTY_LEVELS));
console.log('НЕпустые уровни, помеченные EMPTY_LEVELS:');
for (const lvl of EMPTY_LEVELS) {
  const c = contentOf(lvl);
  if (c.n > 0) console.log('  ур.' + lvl + ': контента ' + c.n + ' -> ' + [...c.goods, ...c.buildings, ...c.misc].join(', '));
}

console.log('');
console.log('=== ЗАВАЛЫ: 3+ открытия на одном уровне ===');
for (let lvl = 1; lvl <= 21; lvl++) {
  const c = contentOf(lvl);
  if (c.n >= 3) console.log('  ур.' + lvl + ': ' + c.n + ' -> ' + [...c.goods, ...c.buildings, ...c.misc].join(', '));
}

console.log('');
console.log('=== СКЛАД ===');
console.log('старт ' + WAREHOUSE_START_CAPACITY + ', шаг ' + WAREHOUSE_UPGRADE_STEP + ', потолок ' + WAREHOUSE_MAX_CAPACITY);
const tiersNeeded = (WAREHOUSE_MAX_CAPACITY - WAREHOUSE_START_CAPACITY) / WAREHOUSE_UPGRADE_STEP;
console.log('тиров до потолка: ' + tiersNeeded);
const wr = CONSTRUCTION_RECIPE.warehouse_upgrade;
const perTier = Object.values(wr.recipe).reduce((a: number, b: any) => a + b, 0);
console.log('рецепт тира: ' + JSON.stringify(wr.recipe) + ' = ' + perTier + ' модулей, время ' + wr.build_time_min + ' мин');
console.log('модулей на весь путь 50->300: ' + perTier * tiersNeeded + ', чистого времени стройки ' + (wr.build_time_min * tiersNeeded / 60).toFixed(0) + ' ч на одной линии');
console.log('тиры модулей рецепта: ' + Object.entries(wr.recipe).map(([id, q]) => id + '(' + (MODULES as any)[id].tier + ')x' + q).join(', '));
console.log('веса дропа: ' + JSON.stringify(TIER_WEIGHTS));
const rarePerTrip = 4 * TIER_WEIGHTS.rare;
console.log('редких модулей за рейс при 4 отсеках = ' + rarePerTrip.toFixed(2));
console.log('рейсов на ОДИН тир склада: ' + (perTier / rarePerTrip).toFixed(1) + '; на все ' + tiersNeeded + ' тиров: ' + (tiersNeeded * perTier / rarePerTrip).toFixed(0));
const isoPerTier = Object.entries(wr.recipe).reduce((s, [id, q]: any) => s + MODULE_PRICE_ISOTOPES[(MODULES as any)[id].tier] * q, 0);
console.log('докупить один тир за изотопы: ' + isoPerTier + ' изо; все тиры: ' + isoPerTier * tiersNeeded + ' изо');
const hb = CONSTRUCTION_RECIPE.habitat_block;
const hbIso = Object.entries(hb.recipe).reduce((s, [id, q]: any) => s + MODULE_PRICE_ISOTOPES[(MODULES as any)[id].tier] * q, 0);
console.log('жилой блок: ' + JSON.stringify(hb.recipe) + ' = ' + hbIso + ' изо докупкой, эффект: (см. applyBuildEffect)');

console.log('');
console.log('--- сколько места просит уровень ---');
for (let lvl = 1; lvl <= 21; lvl++) {
  const unlocked = ALL_GOOD_IDS.filter((id) => GOODS[id].unlock_level <= lvl);
  const maxSlot = Math.max(...unlocked.map((id) => slotQuantity(id, 'drone', lvl, 1)));
  const sumMaxDrone = unlocked.map((id) => slotQuantity(id, 'drone', lvl, 1)).sort((a, b) => b - a).slice(0, 5).reduce((a, b) => a + b, 0);
  const maxLiner = lvl >= MECHANIC_UNLOCK_LEVEL.liner ? Math.max(...unlocked.map((id) => slotQuantity(id, 'liner', lvl, 1))) : 0;
  const boardOrders = bracketAt(NUM_VISIBLE_ORDERS, lvl, 'orders') ?? 0;
  console.log('ур.' + String(lvl).padStart(2) + ': товаров ' + String(unlocked.length).padStart(2) +
    ', max позиция дрона ' + String(maxSlot).padStart(3) +
    ', один заказ дрона 5 позиций max ' + String(sumMaxDrone).padStart(3) +
    ', max позиция лайнера ' + String(maxLiner).padStart(3) +
    ', заказов на доске ' + boardOrders);
}

console.log('');
console.log('=== ЦЕНЫ ЗДАНИЙ ПРОТИВ КУМУЛЯТИВНЫХ НАГРАД ===');
for (const [k, v] of Object.entries(FACTORY_PRICES)) {
  let sumTo = 0;
  for (let lvl = 2; lvl <= (v as any).unlock_level; lvl++) sumTo += levelUpReward(lvl).credits;
  console.log('  ' + k.padEnd(20) + ' ур.' + (v as any).unlock_level + ' цена ' + (v as any).first + '; награды за левелапы до этого уровня = ' + sumTo + ' (' + ((v as any).first / Math.max(1, sumTo)).toFixed(2) + 'x)');
}

console.log('');
console.log('=== ЦЕПОЧКИ ===');
const buildable = new Set(Object.keys(FACTORY_PRICES));
for (const id of ALL_GOOD_IDS) {
  const g = GOODS[id];
  if (g.required_building && !buildable.has(g.required_building)) {
    console.log('  ' + id + ': required_building=' + g.required_building + ' — ЗДАНИЕ НЕЛЬЗЯ КУПИТЬ (нет в FACTORY_PRICES)');
  }
}
for (const id of ALL_GOOD_IDS) {
  const consumers = ALL_GOOD_IDS.filter((c) => GOODS[c].inputs.some((i) => i.good_id === id));
  if (consumers.length === 0) console.log('  тупик: ' + id + ' (ур.' + GOODS[id].unlock_level + ', цена ' + GOODS[id].price + ') — потребителей нет');
}
console.log('--- вход открывается позже выхода / здание входа недоступно ---');
for (const id of ALL_GOOD_IDS) {
  for (const i of GOODS[id].inputs) {
    if (GOODS[i.good_id].unlock_level > GOODS[id].unlock_level) console.log('  ' + id + ' (ур.' + GOODS[id].unlock_level + ') требует ' + i.good_id + ' (ур.' + GOODS[i.good_id].unlock_level + ')');
    const inB = GOODS[i.good_id].required_building;
    if (inB && !buildable.has(inB)) console.log('  ' + id + ' требует ' + i.good_id + ', здание входа ' + inB + ' недоступно');
  }
}

console.log('');
console.log('=== И-2: base_xp / price ===');
for (const id of ALL_GOOD_IDS) {
  const g = GOODS[id];
  const r = g.base_xp / g.price;
  const flag = (r < 0.38 || r > 0.48) ? '   <<ВНЕ 0.38-0.48>>' : '';
  console.log('  ' + id.padEnd(14) + ' price ' + String(g.price).padStart(3) + ' xp ' + String(g.base_xp).padStart(3) + ' ratio ' + r.toFixed(3) + flag);
}

console.log('');
console.log('=== ДОХОД И XP В ЧАС ПО ИСТОЧНИКАМ ===');
const rows = ALL_GOOD_IDS.map((id) => {
  const g = GOODS[id];
  const qty = g.kind === 'crop' ? harvestQty(id) : 1;
  return {
    id, lvl: g.unlock_level, sec: g.prod_time_sec, qty,
    xpH: (g.base_xp * qty) * 3600 / g.prod_time_sec,
    crH: (g.price * qty) * 3600 / g.prod_time_sec,
    seed: g.kind === 'crop' ? plantingCost(g.price) : 0,
    bld: g.required_building ?? 'грядка',
  };
}).sort((a, b) => a.lvl - b.lvl);
for (const r of rows) {
  console.log('  ур.' + String(r.lvl).padStart(2) + ' ' + r.id.padEnd(14) + '[' + r.bld.padEnd(18) + '] цикл ' + String(r.sec).padStart(4) + 'с x' + r.qty + ': ' + r.xpH.toFixed(1).padStart(6) + ' xp/ч, ' + r.crH.toFixed(0).padStart(4) + ' кр/ч, посев ' + r.seed);
}
