import { GOODS, GOOD_BASE_QTY, slotQuantity, ALL_GOOD_IDS } from '../src/domain/config/goods';
import { availableGoodsFor } from '../src/domain/drone';
import type { GoodId } from '../src/domain/types';
import { WAREHOUSE_START_CAPACITY } from '../src/domain/config/economy';

const buildingsAt = (lvl: number) => new Set<string>([
  ...(lvl >= 3 ? ['food_module'] : []),
  ...(lvl >= 6 ? ['mining_site'] : []),
  ...(lvl >= 8 ? ['atmospheric_module'] : []),
  ...(lvl >= 9 ? ['textile_module'] : []),
]);

console.log('lvl | goods | worst 5-slot shuttle order (max qty) | worst 5-slot LINER order | sum of max qty over all goods');
for (let lvl = 1; lvl <= 15; lvl++) {
  const pool = availableGoodsFor(lvl, buildingsAt(lvl));
  const maxQ = (g: GoodId, mech: 'shuttle'|'liner') => slotQuantity(g, mech, lvl, 1);
  const top5 = (mech: 'shuttle'|'liner') => [...pool].map(g => maxQ(g, mech)).sort((a,b)=>b-a).slice(0,5).reduce((a,b)=>a+b,0);
  const sumAll = pool.reduce((s,g)=>s+maxQ(g,'shuttle'),0);
  console.log(String(lvl).padStart(3), '|', String(pool.length).padStart(5), '|', String(top5('shuttle')).padStart(4), '|', String(top5('liner')).padStart(4), '|', String(sumAll).padStart(4), '| pool:', pool.join(','));
}
console.log('\nstart capacity', WAREHOUSE_START_CAPACITY);
console.log('\nDEAD goods (никем не потребляются, только продажа):');
const consumed = new Set<string>();
for (const id of ALL_GOOD_IDS) for (const i of GOODS[id].inputs) consumed.add(i.good_id);
for (const id of ALL_GOOD_IDS) if (!consumed.has(id) && GOODS[id].kind === 'crop') console.log('  crop', id, 'lvl', GOODS[id].unlock_level);
for (const id of ALL_GOOD_IDS) if (!consumed.has(id) && GOODS[id].kind === 'factory') console.log('  factory', id, 'lvl', GOODS[id].unlock_level, 'building', GOODS[id].required_building);
