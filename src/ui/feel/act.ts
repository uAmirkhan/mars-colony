/**
 * Обертка действия, привязанного к месту на экране.
 *
 * Наблюдатель (`watch.ts`) видит все начисления, но не видит, откуда они
 * пришли: кредиты за продажу и кредиты за отправку дрона для него одинаковы, и
 * цифру он может показать только у счетчика. Сбору урожая этого мало — «+4»
 * обязано вылететь из той грядки, по которой нажали, иначе связь действия и
 * результата остается в голове разработчика.
 *
 * Поэтому здесь ровно одно: снять остаток склада до действия, выполнить
 * действие, сравнить. Ни одного игрового числа своими руками — сколько дала
 * грядка, знает домен, а не интерфейс.
 */

import { totalQty } from '../../domain/warehouse';
import { useGame } from '../../state/gameStore';
import { anchor, centerOf, spawnFloat, spawnFly } from './fx';
import { denyNudge } from './press';

/** На сколько цифра поднята над центром элемента, чтобы не спорить с иконкой. */
const FLOAT_LIFT_PX = 22;

export function actWithFx(el: Element | null, run: () => void): void {
  const before = useGame.getState();
  const stock_before = totalQty(before.warehouse);
  const seen = before.toasts.reduce((max, t) => (t.id > max ? t.id : max), 0);

  run();

  const after = useGame.getState();
  if (after.toasts.some((t) => t.id > seen && t.kind === 'warn')) {
    denyNudge(el);
    return;
  }

  const gained = totalQty(after.warehouse) - stock_before;
  if (gained <= 0) return;

  const from = centerOf(el);
  spawnFloat(`+${gained}`, 'gain', from && { x: from.x, y: from.y - FLOAT_LIFT_PX });
  spawnFly(from, anchor('warehouse'), 'gain', gained);
}
