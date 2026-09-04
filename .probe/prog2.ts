import { DEFAULT_SIM, simulate } from '../src/sim/simulate';
const cfg = { ...DEFAULT_SIM, session_starts_min: [8*60,12*60,17*60,21*60], session_length_min: 20, days: 60 };
const r = simulate(cfg);
let peak = 0, first4000: number|null = null, first5500: number|null = null;
console.log('day lvl credits warehouse_blocks');
for (const row of r.rows) {
  peak = Math.max(peak, row.credits);
  if (first4000 === null && row.credits >= 4000) first4000 = row.day;
  if (first5500 === null && row.credits >= 5500) first5500 = row.day;
  if (row.day % 5 === 0 || row.day <= 3) console.log(row.day, row.level, row.credits, row.warehouse_blocks);
}
console.log('peak credits over 60 days:', peak, '| first day with >=4000:', first4000, '| >=5500:', first5500);
console.log('buildings owned:', r.buildings_owned, '| dome_expansions', r.dome_expansions, '| spent on buildings', r.credits_spent_on_buildings);
console.log('warehouse_blocks total:', r.warehouse_blocks, '| planting_starved', r.planting_starved, '| softlock_rescues', r.softlock_rescues);
console.log('final level', r.final_level, 'milestones', r.milestones);
