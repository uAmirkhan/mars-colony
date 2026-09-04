import { rollArrival, type DropContext, type ConstructionNeed } from '../src/domain/droproller';
import { CONSTRUCTION_RECIPE } from '../src/domain/config/modules';
import { slotCountFor, flightTimerMin, COLLECT_COOLDOWN_MIN } from '../src/domain/config/economy';
import type { ModuleId } from '../src/domain/types';

function mulberry(seed: number) { let a = seed >>> 0; return () => { a |= 0; a = (a + 0x6D2B79F5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }

function tripsForBuild(kind: 'warehouse_upgrade' | 'habitat_block', level: number, seed: number) {
  const rng = mulberry(seed);
  const recipe = CONSTRUCTION_RECIPE[kind].recipe as Partial<Record<ModuleId, number>>;
  const stock: Partial<Record<ModuleId, number>> = {};
  const pity: Partial<Record<ModuleId, number>> = {};
  let arrival_no = 0;
  let containers = 0;
  const deficit = () => {
    const d: Partial<Record<ModuleId, number>> = {};
    for (const [id, need] of Object.entries(recipe) as Array<[ModuleId, number]>) {
      const s = need - (stock[id] ?? 0); if (s > 0) d[id] = s;
    }
    return d;
  };
  let cons: ConstructionNeed = { construction_id: 'c1', deficit: deficit(), arrivals_without_needed: 0, last_floor_arrival: 0 };
  while (Object.keys(deficit()).length > 0 && arrival_no < 5000) {
    arrival_no++;
    const slots = slotCountFor(level, rng());
    const ctx: DropContext = { pity, stock, need: recipe, warehouse_avg_24h: {}, gated_open: false, constructions: [cons], arrival_no, rng };
    const roll = rollArrival(slots, ctx);
    for (const m of roll.modules) stock[m] = (stock[m] ?? 0) + 1;
    containers += slots;
    Object.assign(pity, roll.next_pity);
    cons = { ...roll.next_constructions[0]!, deficit: deficit() };
  }
  return { arrivals: arrival_no, containers, hours: (arrival_no * (flightTimerMin(level) + COLLECT_COOLDOWN_MIN)) / 60 };
}

for (const [kind, level] of [['warehouse_upgrade', 5], ['warehouse_upgrade', 9], ['warehouse_upgrade', 15], ['habitat_block', 7]] as const) {
  const runs = Array.from({ length: 200 }, (_, i) => tripsForBuild(kind, level, i + 1));
  const med = (a: number[]) => a.sort((x, y) => x - y)[Math.floor(a.length / 2)]!;
  console.log(kind, 'lvl', level, '| median arrivals', med(runs.map(r => r.arrivals)), '| median containers', med(runs.map(r => r.containers)), '| median hours of shuttle uptime', med(runs.map(r => r.hours)).toFixed(1));
}
