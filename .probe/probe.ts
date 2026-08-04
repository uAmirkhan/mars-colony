import { orderReward, generateOrder, availableGoodsFor, type OrderPosition } from '../src/domain/drone';
import { GOODS, GOOD_BASE_QTY, ALL_GOOD_IDS } from '../src/domain/config/goods';
import { DRONE_PREMIUM_RANGE, COVERAGE_MIN, EASY_PRODUCE_MAX_MIN } from '../src/domain/config/economy';
import { createWarehouse, deposit } from '../src/domain/warehouse';
import { productionTimeMinutes } from '../src/domain/rushcost';
import type { GoodId } from '../src/domain/types';

function mulberry32(a: number) { return () => { a |= 0; a = (a + 0x6D2B79F5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }

// 1. premium range violation
let worst_lo = 99, worst_lo_desc = '', worst_hi = 0, worst_hi_desc = '';
for (let n = 1; n <= 6; n++) {
  for (let trial = 0; trial < 40000; trial++) {
    const rng = mulberry32(trial * 131 + n);
    const positions: OrderPosition[] = [];
    for (let i = 0; i < n; i++) {
      const gid = ALL_GOOD_IDS[Math.floor(rng() * ALL_GOOD_IDS.length)]!;
      const q = GOOD_BASE_QTY[gid].min + Math.floor(rng() * (GOOD_BASE_QTY[gid].max - GOOD_BASE_QTY[gid].min + 1));
      positions.push({ good_id: gid, qty: q, filled: false, filled_by: null, easy: true });
    }
    const jitter = rng();
    for (const def of [false, true]) {
      const r = orderReward(positions, jitter, def);
      const ms = positions.reduce((s, p) => s + GOODS[p.good_id].price * p.qty, 0);
      const eff = r.credits / ms;
      if (eff < worst_lo) { worst_lo = eff; worst_lo_desc = JSON.stringify(positions.map(p=>p.good_id+'x'+p.qty)) + ` ms=${ms} cr=${r.credits} prem=${r.premium.toFixed(3)} def=${def}`; }
      if (eff > worst_hi) { worst_hi = eff; worst_hi_desc = JSON.stringify(positions.map(p=>p.good_id+'x'+p.qty)) + ` ms=${ms} cr=${r.credits} prem=${r.premium.toFixed(3)} def=${def}`; }
    }
  }
}
console.log('MIN eff premium', worst_lo.toFixed(4), 'floor=', 1 + DRONE_PREMIUM_RANGE.min, worst_lo_desc);
console.log('MAX eff premium', worst_hi.toFixed(4), 'ceil=', 1 + DRONE_PREMIUM_RANGE.max, worst_hi_desc);
