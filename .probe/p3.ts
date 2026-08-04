import { plant, sell, createField } from '../src/domain/production';
import { createWarehouse, deposit, reserve, availableOf, qtyOf } from '../src/domain/warehouse';
import { plantingCost } from '../src/domain/config/economy';
import { GOODS } from '../src/domain/config/goods';

const wh = createWarehouse(50);
deposit(wh, 'algae', 10);
reserve(wh, 'algae', 10);
const fields = [createField(0), createField(1), createField(2), createField(3)];
const ctx = { now: 0, warehouse: wh, credits: 0, level: 2 };
console.log('qty', qtyOf(wh,'algae'), 'available', availableOf(wh,'algae'));
console.log('sell 10:', JSON.stringify(sell('algae', 10, ctx)));
console.log('sell 1:', JSON.stringify(sell('algae', 1, ctx)));
console.log('plant algae (cost', plantingCost(GOODS.algae.price), '):', JSON.stringify(plant(fields[0]!, 'algae', ctx, fields)));
console.log('plant soy:', JSON.stringify(plant(fields[1]!, 'soy', ctx, fields)));
