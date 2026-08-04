import { loadPosition, buyoutPosition, releaseReserved, sendOrder, positionBuyoutPrice, type OrderSlot } from '../src/domain/drone';
import { createWarehouse, deposit, qtyOf, reservedOf, availableOf } from '../src/domain/warehouse';

function mk(idx: number): OrderSlot {
  return { idx, state: 'active', npc_name: 'n', positions: [{ good_id: 'algae', qty: 5, filled: false, filled_by: null, easy: true }], credits_reward: 100, xp_reward: 10, refresh_at: 0 };
}
const wh = createWarehouse(50);
deposit(wh, 'algae', 5);
const A = mk(0), B = mk(1);
console.log('load A:', loadPosition(A, 0, wh), 'reserved=', reservedOf(wh,'algae'));
console.log('buyout price B:', positionBuyoutPrice(B.positions[0]!));
console.log('buyout B:', buyoutPosition(B, 0), 'reserved=', reservedOf(wh,'algae'));
releaseReserved(B, wh);
console.log('after discard B: qty=', qtyOf(wh,'algae'), 'reserved=', reservedOf(wh,'algae'), 'available=', availableOf(wh,'algae'));
console.log('A filled:', A.positions[0]!.filled, 'state=', A.state);
const r = sendOrder(A, wh);
console.log('send A:', JSON.stringify(r), '-> qty=', qtyOf(wh,'algae'), 'reserved=', reservedOf(wh,'algae'));
