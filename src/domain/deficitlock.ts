/**
 * И-13 (изоляция дефицита между механиками) — контракт лока. Источник истины
 * — [[tz-common-systems-mars]] раздел 1.5, [[mars-colony-frame]] строка И-13.
 *
 * Смысл: одна и та же позиция целевого дефицита (И-8, `MAX_DEFICIT_SLOTS=1`)
 * не выдается ОДНОВРЕМЕННО двумя механиками. Товар, уже обещанный одной
 * механике в качестве дефицитной позиции, не должен одновременно обещаться
 * другой — иначе игрок закрывает два заказа одним и тем же будущим уловом.
 *
 * До прогона 6 механик доставки было две реализовано (дрон, шаттл), и
 * инвариант был сознательно вырезан из скоупа: «при одной механике доставки
 * он пустой» (реестр [[spec-prototype-build]], решение прогона 2). Сейчас
 * механик две, поэтому конфликт снова возможен и инвариант снова в скоупе.
 *
 * **Упрощение относительно канона, зафиксировано здесь.** Канон держит лок
 * на пару (`player_id`, `good_id`) — здесь состояние уже принадлежит одному
 * игроку (весь `src/domain` работает с состоянием одного игрока, `player_id`
 * нигде не хранится), поэтому лок в коде — просто `good_id -> DeficitLock`.
 */

import {
  DEFICIT_LOCK_TTL as DEFICIT_LOCK_TTL_DEFAULT,
  DEFICIT_LOCK_TTL_MAX as DEFICIT_LOCK_TTL_MAX_SEC,
} from './config/economy';
import type { GoodId, Mechanic } from './types';

export interface DeficitLock {
  good_id: GoodId;
  locked_by_mechanic: Mechanic;
  /**
   * Заказы СВОЕЙ механики, все еще держащие этот товар как дефицитную
   * позицию. Канон 1.5 разрешает нескольким заказам одной механики законно
   * делить один дефицитный товар — лок в коде это отражает счетчиком
   * владельцев (по ссылке заказа), а не единственным `order_ref`.
   *
   * Массив, а не `Set`: значение уходит в сейв как есть (`JSON.stringify`
   * не сохраняет содержимое `Set`, только пустой объект) — раньше это уже
   * ловилось на самом поле `deficit_locks` версией сейва 5.
   *
   * Снимается только тогда, когда владельцев не осталось вовсе
   * (`releaseDeficitLock` убирает ровно одного) — терминация ОДНОГО заказа
   * не должна снимать защиту с живого заказа-соседа той же механики,
   * все еще требующего тот же товар (найдено прогоном 6, реестр
   * [[spec-prototype-build]] раздел 8, пункт 26).
   */
  owners: string[];
  created_at: number;
  expires_at: number;
}

/** Состояние лока на игрока: товар -> действующий лок. Персистентно (сейв). */
export type DeficitLockState = Partial<Record<GoodId, DeficitLock>>;

export function createDeficitLockState(): DeficitLockState {
  return {};
}

/**
 * Лок «жив» — не истек по TTL. Отдельная функция, а не инлайн: та же
 * проверка нужна и `isDeficitLockedByOtherMechanic`, и релизу устаревших
 * записей (см. `pruneExpiredDeficitLocks`) — разъехаться им нельзя.
 */
function isAlive(lock: DeficitLock, now: number): boolean {
  return lock.expires_at > now;
}

/**
 * Канон 1.5: `isDeficitLockedByOtherMechanic`. Товар залочен ДРУГОЙ
 * механикой прямо сейчас — своя механика конфликта не создает (может
 * законно повторно просить тот же дефицитный товар в другом своем заказе,
 * И-13 запрещает конфликт МЕЖДУ механиками, не внутри одной).
 */
export function isDeficitLockedByOtherMechanic(
  locks: DeficitLockState,
  good_id: GoodId,
  mechanic: Mechanic,
  now: number,
): boolean {
  const lock = locks[good_id];
  return lock !== undefined && lock.locked_by_mechanic !== mechanic && isAlive(lock, now);
}

/**
 * Канон 1.5: `registerDeficitLock` — атомарный upsert-if-absent
 * (`INSERT ... ON CONFLICT(player_id, good_id) DO NOTHING`, раздел 0.1:
 * check-then-act запрещен). В однопоточном клиентском домене «атомарность»
 * — это просто одна синхронная функция без промежуточного чтения снаружи:
 * вызывающий код обязан пропустить товар при конфликте, а не считать лок
 * своим.
 *
 * Живой лок другой механики НЕ перезаписывается (конфликт — молчаливый
 * no-op, ровно как `DO NOTHING`). Живой лок ТОЙ ЖЕ механики принимает
 * НОВОГО владельца (`order_ref` добавляется в `owners`, если его там еще
 * нет) — второй заказ той же механики на тот же дефицитный товар легален
 * (канон 1.5), и лок обязан знать об обоих, иначе терминация первого
 * заказа снимет защиту со второго, живого (прогон 6, реестр
 * [[spec-prototype-build]] раздел 8, пункт 26). TTL первого владельца при
 * этом не трогается — тот же принцип, что и раньше: секундная регистрация
 * не переустанавливает срок жизни лока, снимает его владелец на своем
 * терминальном событии. Истекший лок (чей бы ни был) перезаписывается
 * целиком — место свободно, прежние владельцы уже не считаются.
 */
export function registerDeficitLock(
  locks: DeficitLockState,
  good_id: GoodId,
  mechanic: Mechanic,
  order_ref: string,
  now: number,
  ttl: number = DEFICIT_LOCK_TTL_DEFAULT,
): void {
  const existing = locks[good_id];
  if (existing !== undefined && isAlive(existing, now)) {
    if (existing.locked_by_mechanic !== mechanic) return; // конфликт — DO NOTHING
    if (!existing.owners.includes(order_ref)) existing.owners.push(order_ref);
    return;
  }

  locks[good_id] = {
    good_id,
    locked_by_mechanic: mechanic,
    owners: [order_ref],
    created_at: now,
    expires_at: now + Math.min(ttl, DEFICIT_LOCK_TTL_MAX_SEC),
  };
}

/**
 * Канон 1.5: `releaseDeficitLock` — вызывается при терминальном событии
 * заказа-владельца (deliver/discard/depart/deliverContainer последней
 * позиции). Снимает лок ТОЛЬКО если он принадлежит вызывающей механике:
 * защита от того, что терминация одного заказа снимет лок, оставленный
 * ДРУГОЙ механикой на тот же товар (могло случиться только если конфликт
 * был проигнорирован выше по стеку — защита на всякий случай, не рабочий путь).
 *
 * `order_ref` обязателен и снимает ровно ОДНОГО владельца — терминация
 * конкретного заказа освобождает только его собственную долю дефицита.
 * Объект-лок удаляется целиком, только когда владельцев не осталось: пока
 * жив хотя бы один заказ той же механики, требующий тот же товар, защита
 * от других механик обязана сохраняться (прогон 6, реестр
 * [[spec-prototype-build]] раздел 8, пункт 26).
 */
export function releaseDeficitLock(
  locks: DeficitLockState,
  good_id: GoodId,
  mechanic: Mechanic,
  order_ref: string,
): void {
  const lock = locks[good_id];
  if (lock === undefined || lock.locked_by_mechanic !== mechanic) return;

  lock.owners = lock.owners.filter((ref) => ref !== order_ref);
  if (lock.owners.length === 0) delete locks[good_id];
}
