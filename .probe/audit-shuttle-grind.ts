import { rollArrival, type DropContext, type ModuleCounts } from '../src/domain/droproller';
import { CONSTRUCTION_RECIPE, ALL_MODULE_IDS, MODULES } from '../src/domain/config/modules';
import { WAREHOUSE_START_CAPACITY, WAREHOUSE_MAX_CAPACITY, WAREHOUSE_UPGRADE_STEP, MODULE_STOCK_CAP, flightTimerMin, COLLECT_COOLDOWN_MIN, slotCountFor } from '../src/domain/config/economy';

function mulberry32(a: number) {
  return () => {
    a |= 0; a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// Симуляция: игрок только копит модули под расширения склада.
// Всё в его пользу: он ВСЕГДА мгновенно грузит рейс, склад модулей не переполняется,
// вторая стройка не мешает, кулдаун и полёт идут без пауз игрока.
function simulate(seed: number, targetTiers: number) {
  const rng = mulberry32(seed);
  const recipe = CONSTRUCTION_RECIPE.warehouse_upgrade.recipe as ModuleCounts;
  let stock: ModuleCounts = {};
  let pity: ModuleCounts = {};
  let arrivals = 0;
  let tiers = 0;
  let capacity = WAREHOUSE_START_CAPACITY;
  let minutes = 0;
  let level = 5;
  const perTier: number[] = [];
  let arrivalsThisTier = 0;
  let cn = [{ construction_id: 'warehouse_upgrade', deficit: { ...recipe }, arrivals_without_needed: 0, last_floor_arrival: -999 }];

  while (tiers < targetTiers && arrivals < 200000) {
    const slot_count = slotCountFor(level, rng());
    const need: ModuleCounts = {};
    for (const [id, q] of Object.entries(recipe)) need[id as any] = Math.max(0, (q as number) - (stock[id as any] ?? 0));
    const deficit: ModuleCounts = {};
    for (const [id, q] of Object.entries(recipe)) { const d = (q as number) - (stock[id as any] ?? 0); if (d > 0) deficit[id as any] = d; }
    cn[0]!.deficit = deficit;
    const ctx: DropContext = {
      pity, stock, need,
      warehouse_avg_24h: { ...stock },
      gated_open: false,
      arrival_no: arrivals + 1,
      constructions: cn as any,
      rng,
    } as any;
    const roll = rollArrival(slot_count, ctx);
    for (const m of roll.modules) stock[m] = (stock[m] ?? 0) + 1;
    pity = roll.next_pity;
    cn = roll.next_constructions as any;
    arrivals++; arrivalsThisTier++;
    minutes += flightTimerMin(level) + COLLECT_COOLDOWN_MIN;

    const enough = Object.entries(recipe).every(([id, q]) => (stock[id as any] ?? 0) >= (q as number));
    if (enough) {
      for (const [id, q] of Object.entries(recipe)) stock[id as any] = (stock[id as any] ?? 0) - (q as number);
      tiers++;
      capacity += WAREHOUSE_UPGRADE_STEP;
      minutes += CONSTRUCTION_RECIPE.warehouse_upgrade.build_time_min;
      perTier.push(arrivalsThisTier);
      arrivalsThisTier = 0;
      if (tiers === 4) level = 9;
      if (tiers === 10) level = 15;
    }
  }
  const junk = ALL_MODULE_IDS.reduce((s, id) => s + (MODULES[id].tier !== 'rare' ? (stock[id] ?? 0) : 0), 0);
  return { arrivals, tiers, capacity, hours: minutes / 60, perTier, junkStock: junk, stock };
}

console.log('=== ГРИНД ШАТТЛА ПОД РАСШИРЕНИЕ СКЛАДА ===');
console.log('рецепт тира:', JSON.stringify(CONSTRUCTION_RECIPE.warehouse_upgrade.recipe), 'все три модуля тира rare');
console.log('лимит склада модулей на старте:', MODULE_STOCK_CAP);
for (const seed of [1, 2, 3]) {
  const r5 = simulate(seed, 5);
  console.log('seed ' + seed + ' -> 5 тиров (склад 50->100): ' + r5.arrivals + ' рейсов, ' + r5.hours.toFixed(0) + ' ч непрерывной игры, рейсов на тир: ' + JSON.stringify(r5.perTier));
}
const r25 = simulate(7, 25);
console.log('до потолка ' + WAREHOUSE_MAX_CAPACITY + ' (25 тиров): ' + r25.arrivals + ' рейсов, ' + r25.hours.toFixed(0) + ' ч (= ' + (r25.hours / 24).toFixed(0) + ' суток непрерывно), лишних базовых/гейтовых модулей накопится ' + r25.junkStock + ' при лимите склада модулей ' + MODULE_STOCK_CAP);
console.log('остаток склада модулей на финише:', JSON.stringify(r25.stock));
