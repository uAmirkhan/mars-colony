import { rollArrival, type DropContext, type ModuleCounts } from '../src/domain/droproller';
import { CONSTRUCTION_RECIPE, ALL_MODULE_IDS, MODULES } from '../src/domain/config/modules';
import { MODULE_STOCK_CAP, WAREHOUSE_UPGRADE_STEP, flightTimerMin, COLLECT_COOLDOWN_MIN, slotCountFor } from '../src/domain/config/economy';

function mulberry32(a: number) {
  return () => {
    a |= 0; a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// Честная модель: склад модулей ИМЕЕТ потолок (moduleCapacity = 100 + 10 * тир),
// addModule отказывает целиком, collectContainerAt при отказе выходит,
// allCollected никогда не true -> startCooldown не вызывается -> нового рейса нет.
function run(seed: number, buildHabitatOnce: boolean) {
  const rng = mulberry32(seed);
  const wr = CONSTRUCTION_RECIPE.warehouse_upgrade.recipe as ModuleCounts;
  const hb = CONSTRUCTION_RECIPE.habitat_block.recipe as ModuleCounts;
  let stock: ModuleCounts = {};
  let pity: ModuleCounts = {};
  let tiers = 0, arrivals = 0, minutes = 0;
  let habitatDone = !buildHabitatOnce;
  let cn: any[] = [];

  const total = () => ALL_MODULE_IDS.reduce((s, id) => s + (stock[id] ?? 0), 0);
  const cap = () => MODULE_STOCK_CAP + tiers * WAREHOUSE_UPGRADE_STEP;

  for (let i = 0; i < 5000; i++) {
    const builds: any[] = [{ kind: 'warehouse_upgrade', recipe: wr }];
    if (!habitatDone) builds.push({ kind: 'habitat_block', recipe: hb });

    const need: ModuleCounts = {};
    for (const b of builds) for (const [id, q] of Object.entries(b.recipe)) need[id as any] = ((need[id as any] ?? 0) + (q as number));
    cn = builds.map((b) => {
      const deficit: ModuleCounts = {};
      for (const [id, q] of Object.entries(b.recipe)) { const d = (q as number) - (stock[id as any] ?? 0); if (d > 0) deficit[id as any] = d; }
      const prev = cn.find((c) => c.construction_id === b.kind);
      return { construction_id: b.kind, deficit, arrivals_without_needed: prev?.arrivals_without_needed ?? 0, last_floor_arrival: prev?.last_floor_arrival ?? 0 };
    });

    const slot_count = slotCountFor(9, rng());
    const ctx = { pity, stock, need, warehouse_avg_24h: { ...stock }, gated_open: false, arrival_no: arrivals + 1, constructions: cn, rng } as DropContext;
    const roll = rollArrival(slot_count, ctx);
    pity = roll.next_pity; cn = roll.next_constructions as any;
    arrivals++; minutes += flightTimerMin(9) + COLLECT_COOLDOWN_MIN;

    // сбор контейнеров с потолком, как в gameStore.collectContainerAt
    let blocked = false;
    for (const m of roll.modules) {
      if (total() + 1 > cap()) { blocked = true; break; }
      stock[m] = (stock[m] ?? 0) + 1;
    }
    if (blocked) {
      return { locked: true, arrivals, tiers, hours: minutes / 60, stock: { ...stock }, cap: cap(), total: total() };
    }

    if (!habitatDone && Object.entries(hb).every(([id, q]) => (stock[id as any] ?? 0) >= (q as number))) {
      for (const [id, q] of Object.entries(hb)) stock[id as any] = (stock[id as any] ?? 0) - (q as number);
      habitatDone = true;
    }
    if (Object.entries(wr).every(([id, q]) => (stock[id as any] ?? 0) >= (q as number))) {
      for (const [id, q] of Object.entries(wr)) stock[id as any] = (stock[id as any] ?? 0) - (q as number);
      tiers++; minutes += CONSTRUCTION_RECIPE.warehouse_upgrade.build_time_min;
    }
  }
  return { locked: false, arrivals, tiers, hours: minutes / 60, stock: { ...stock }, cap: cap(), total: total() };
}

console.log('=== ПЕРЕПОЛНЕНИЕ СКЛАДА МОДУЛЕЙ -> ОСТАНОВКА ШАТТЛА ===');
console.log('склад модулей: ' + MODULE_STOCK_CAP + ' + ' + WAREHOUSE_UPGRADE_STEP + ' за построенный тир');
console.log('расширение склада: только rare (filter/cable/sealant). жилой блок: panel 6, frame 5, sealant 7 — ОДИН раз.');
console.log('веса дропа: basic 0.62 (panel, frame), rare 0.33, gated 0.05\n');
for (const seed of [1, 2, 3, 4, 5]) {
  const r = run(seed, true);
  console.log('seed ' + seed + ': ' + (r.locked ? 'ЗАКЛИНИЛО' : 'дошел до конца окна') +
    ' на прибытии ' + r.arrivals + ' (~' + r.hours.toFixed(0) + ' ч), тиров склада построено ' + r.tiers +
    ', склад модулей ' + r.total + '/' + r.cap + ' -> ' + JSON.stringify(r.stock));
}
