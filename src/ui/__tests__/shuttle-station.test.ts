/**
 * Н-3/Н-8: доказательство настоящим кодом экрана, не пересказом ТЗ словами.
 *
 * `slotSheetView` — та самая функция, которую `SlotSheet` вызывает для своей
 * разметки (см. `shuttle-station.tsx`); JSX только читает ее поля. Тест дергает
 * этот код напрямую и не рендерит DOM — но и не изобретает копию правила: он
 * прогоняет ровно то, что видит игрок, без второй реализации внутри теста.
 *
 * Прогнать браузером (e2e) здесь нельзя — порт занят соседними агентами
 * (задание). Компромисс сознательный: e2e/defects-run5.spec.ts остается на
 * прогон оркестратору как второй, более полный слой доказательства (реальный
 * клик, реальный DOM), этот тест — единственный, что можно прогнать отсюда
 * сейчас.
 */

import { describe, expect, it } from 'vitest';
import type { ShuttleSlot } from '../../domain/shuttle';
import { createWarehouse, deposit } from '../../domain/warehouse';
import { slotSheetView } from '../shuttle-station';

function baseSlot(overrides: Partial<ShuttleSlot>): ShuttleSlot {
  return {
    idx: 0,
    good_id: 'algae',
    qty_required: 5,
    qty_filled: 0,
    qty_purchased: 0,
    filled_by: null,
    reward: null,
    collected: false,
    floor_forced: false,
    ...overrides,
  };
}

describe('slotSheetView', () => {
  it('Н-3: склад покрывает отсек целиком — «Докупить» скрыта', () => {
    const warehouse = createWarehouse();
    deposit(warehouse, 'algae', 10);
    const slot = baseSlot({ qty_required: 5, qty_filled: 0 });

    const view = slotSheetView(slot, warehouse);

    expect(view.showBuyout).toBe(false);
  });

  it('Н-3 (контроль): склада не хватает — «Докупить» показана', () => {
    const warehouse = createWarehouse();
    deposit(warehouse, 'algae', 2);
    const slot = baseSlot({ qty_required: 5, qty_filled: 0 });

    const view = slotSheetView(slot, warehouse);

    expect(view.showBuyout).toBe(true);
  });

  it('Н-8: счетчик показывает qty_required, а не остаток после частичной погрузки', () => {
    const warehouse = createWarehouse();
    deposit(warehouse, 'algae', 3);
    const slot = baseSlot({ qty_required: 5, qty_filled: 2 });

    const view = slotSheetView(slot, warehouse);

    expect(view.counterText).toBe('есть 3 / нужно 5');
  });
});
