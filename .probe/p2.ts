import { orderReward, generateOrder, availableGoodsFor } from '../src/domain/drone';
import { GOODS } from '../src/domain/config/goods';
import { DRONE_PREMIUM_RANGE, COVERAGE_MIN, EASY_PRODUCE_MAX_MIN, MAX_DEFICIT_SLOTS } from '../src/domain/config/economy';
import { createWarehouse, deposit, availableOf } from '../src/domain/warehouse';
import { productionTimeMinutes } from '../src/domain/rushcost';
import type { GoodId } from '../src/domain/types';

function mulberry32(a: number) { return () => { a |= 0; a = (a + 0x6D2B79F5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
const B = ['food_module','mining_site','atmospheric_module','textile_module'];

let n_total=0, n_low_cov=0, worst_cov=9, worst_cov_desc='';
let n_multi_def=0, multi_desc='';
let n_prem_hi=0, worst_prem=0, prem_desc='';
let n_underpaid=0, underpaid_desc='';
for (let level=2; level<=21; level++) {
 for (let mask=0; mask<16; mask++) {
  for (let stock=0; stock<=8; stock+=4) {
   for (let seed=1; seed<=25; seed++) {
    const buildings = new Set(B.filter((_,i)=>(mask>>i)&1));
    const pool: GoodId[] = availableGoodsFor(level, buildings);
    if (pool.length===0) continue;
    const wh = createWarehouse(500);
    for (const g of pool) if (stock>0) deposit(wh, g, stock);
    const o = generateOrder(0, { level, warehouse: wh, available_goods: pool, board: [], rng: mulberry32(seed*7919+level*31+mask) });
    n_total++;
    const easy_ratio = o.positions.length===0?1:o.positions.filter(p=>p.easy).length/o.positions.length;
    if (easy_ratio < COVERAGE_MIN.drone) { n_low_cov++; if (easy_ratio<worst_cov){worst_cov=easy_ratio; worst_cov_desc=`ур.${level} mask=${mask} stock=${stock} seed=${seed} `+o.positions.map(p=>`${p.good_id}x${p.qty}${p.easy?'*':''}`).join(' ');} }
    const hard = o.positions.filter(p=>!p.easy).length;
    if (hard > MAX_DEFICIT_SLOTS) { n_multi_def++; if(!multi_desc) multi_desc=`ур.${level} mask=${mask} stock=${stock} seed=${seed} `+o.positions.map(p=>`${p.good_id}x${p.qty}${p.easy?'*':''}`).join(' '); }
    const ms = o.positions.reduce((s,p)=>s+GOODS[p.good_id].price*p.qty,0);
    const eff = o.credits_reward/ms;
    if (eff > 1+DRONE_PREMIUM_RANGE.max) { n_prem_hi++; if(eff>worst_prem){worst_prem=eff; prem_desc=`ур.${level} mask=${mask} stock=${stock} seed=${seed} ms=${ms} cr=${o.credits_reward} `+o.positions.map(p=>`${p.good_id}x${p.qty}`).join(' ');} }
    if (eff < 1+DRONE_PREMIUM_RANGE.min) { n_underpaid++; if(!underpaid_desc) underpaid_desc=`ур.${level} ms=${ms} cr=${o.credits_reward}`; }
   }
  }
 }
}
console.log('orders', n_total);
console.log('И-8 покрытие ниже 0.6:', n_low_cov, 'худшее', worst_cov, worst_cov_desc);
console.log('дефицитных позиций > 1:', n_multi_def, multi_desc);
console.log('премия выше потолка:', n_prem_hi, 'худшая', worst_prem.toFixed(4), prem_desc);
console.log('премия ниже пола:', n_underpaid, underpaid_desc);
