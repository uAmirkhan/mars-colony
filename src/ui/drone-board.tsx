/**
 * Доска заказов дрона. Композиция и тексты — [[tz-drone-mars]] раздел 7.
 *
 * Главное правило экрана, выведенное из замечания владельца по макету:
 * подсветка несет смысл, а не украшает. Зеленая рамка карточки означает
 * ровно одно — склад покрывает все непогруженные позиции, то есть заказ
 * можно закрыть прямо сейчас. Без этого игроку пришлось бы открывать все
 * девять карточек, чтобы понять, за какую браться.
 *
 * Цвет счетчика позиции берется той же доменной функцией, что и решение
 * о подсветке карточки. Это не педантизм: счетчик, покрашенный по своей
 * формуле, рано или поздно разойдется с кнопкой «Погрузить» рядом с ним.
 *
 * По той же причине и ЧИСЛО в счетчике читается доменным `availableOf`, а не
 * `cells[good].qty`: сырое количество включает зарезервированное под другие
 * заказы, и счетчик обещал товар, которого домен не отдаст.
 */

import { useState } from 'react';
import { droneRefreshPrice } from '../domain/config/economy';
import { GOODS } from '../domain/config/goods';
import {
  canFulfillNow,
  type OrderSlot,
  positionBuyoutPrice,
  positionCovered,
} from '../domain/drone';
import { availableOf } from '../domain/warehouse';
import { useGame } from '../state/gameStore';
import { Button, GoodIcon, ISOTOPE_GLYPH, Panel, Timer } from './kit';

/** Карточка заказа на доске. Открывается тапом, если в ней есть что делать. */
function OrderCard({ slot, onOpen }: { slot: OrderSlot; onOpen: () => void }) {
  const { warehouse, now, refreshSlotNow } = useGame();

  if (slot.state === 'empty_cooldown') {
    const remaining = slot.refresh_at - now;
    const price = droneRefreshPrice(remaining);
    return (
      <div className="slot" style={{ padding: 12, gap: 8, cursor: 'default' }}>
        <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>Слот пуст</span>
        <Timer remaining_sec={remaining} />
        <Button kind="secondary" onClick={() => refreshSlotNow(slot.idx)}>
          Обновить · {price} {ISOTOPE_GLYPH}
        </Button>
      </div>
    );
  }

  const ready = slot.state === 'ready';
  const can_fulfill = canFulfillNow(slot, warehouse);
  // Зеленая рамка — только у того, что можно закрыть. Готовый к отправке
  // выделяется сильнее: он не подсказка, а прямой призыв к действию.
  const highlight = ready || can_fulfill;

  return (
    <button
      type="button"
      className={`slot ${ready ? 'slot-ready' : ''}`}
      onClick={onOpen}
      style={{
        padding: 10,
        gap: 6,
        alignItems: 'stretch',
        border: highlight ? '3px solid var(--action)' : undefined,
        boxShadow: can_fulfill && !ready ? '0 0 0 4px rgba(76, 175, 46, 0.22)' : undefined,
        background: 'var(--panel-warm)',
        textAlign: 'left',
      }}
    >
      <div style={{ fontWeight: 800, color: 'var(--title)', fontSize: 12 }}>
        {slot.npc_name}
      </div>

      <div style={{ display: 'flex', gap: 8, justifyContent: 'center', flexWrap: 'wrap' }}>
        {slot.positions.map((position, i) => {
          const good = GOODS[position.good_id];
          const covered = positionCovered(position, warehouse);
          const have = position.filled
            ? position.qty
            : availableOf(warehouse, position.good_id);
          return (
            <div key={`${position.good_id}-${i}`} style={{ textAlign: 'center' }}>
              <GoodIcon name={good.name} size={30} />
              <div
                style={{
                  fontSize: 11,
                  fontWeight: 800,
                  color: covered ? 'var(--action-dark)' : 'var(--text-muted)',
                }}
              >
                {position.filled ? '✓' : `${have}/${position.qty}`}
              </div>
            </div>
          );
        })}
      </div>

      <div
        style={{
          display: 'flex',
          gap: 10,
          justifyContent: 'center',
          borderTop: '2px solid rgba(168,118,62,0.3)',
          paddingTop: 5,
          fontSize: 12,
          fontWeight: 800,
        }}
      >
        <span style={{ color: 'var(--title)' }}>{slot.credits_reward} кр</span>
        <span style={{ color: 'var(--xp)' }}>{slot.xp_reward} XP</span>
      </div>
    </button>
  );
}

/** Окно заказа: позиции с кнопками «Погрузить», внизу «Отправить» и «Выбросить». */
function OrderWindow({ slot, onClose }: { slot: OrderSlot; onClose: () => void }) {
  const {
    warehouse,
    loadOrderPosition,
    buyoutOrderPosition,
    sendOrderAt,
    discardOrderAt,
    isotopes,
  } = useGame();
  const ready = slot.state === 'ready';

  return (
    <div className="scrim" onClick={onClose}>
      <div onClick={(e) => e.stopPropagation()}>
        <Panel title={slot.npc_name} onClose={onClose} style={{ maxWidth: 440, width: '92vw' }}>
          <div style={{ display: 'grid', gap: 8, marginBottom: 12 }}>
            {slot.positions.map((position, i) => {
              const good = GOODS[position.good_id];
              const have = availableOf(warehouse, position.good_id);
              const covered = positionCovered(position, warehouse);
              // Докупка закрывает позицию ЦЕЛИКОМ и по И-12 кладет товар мимо
              // склада, не трогая остаток. Поэтому и платить надо за всю
              // позицию, а не за недостачу: скидка за остаток, который никуда
              // не делся, открывает арбитраж «докупить дешево, продать
              // сэкономленное». Тот же разбор — у отсека шаттла.
              const price = positionBuyoutPrice(position);

              return (
                <div
                  key={`${position.good_id}-${i}`}
                  style={{ display: 'flex', alignItems: 'center', gap: 10 }}
                >
                  <GoodIcon name={good.name} />
                  <div style={{ flex: 1 }}>
                    <div style={{ fontWeight: 800, color: 'var(--title)' }}>{good.name}</div>
                    <div style={{ fontSize: 12 }}>
                      на складе {have} из {position.qty}
                    </div>
                  </div>

                  {position.filled ? (
                    <span style={{ fontSize: 22, color: 'var(--action-dark)' }}>✓</span>
                  ) : covered ? (
                    <Button onClick={() => loadOrderPosition(slot.idx, i)}>Погрузить</Button>
                  ) : (
                    // Цена стоит на кнопке всегда — правило каркаса: кнопка
                    // без цены считается багом, игрок не должен угадывать.
                    <Button
                      kind="secondary"
                      disabled={price > isotopes}
                      onClick={() => buyoutOrderPosition(slot.idx, i)}
                    >
                      Докупить {position.qty} за {price} {ISOTOPE_GLYPH}
                    </Button>
                  )}
                </div>
              );
            })}
          </div>

          <div
            style={{
              display: 'flex',
              gap: 10,
              justifyContent: 'center',
              fontWeight: 800,
              marginBottom: 10,
            }}
          >
            <span style={{ color: 'var(--title)' }}>Награда: {slot.credits_reward} кр</span>
            <span style={{ color: 'var(--xp)' }}>{slot.xp_reward} XP</span>
          </div>

          <Button
            full
            disabled={!ready}
            onClick={() => {
              sendOrderAt(slot.idx);
              onClose();
            }}
          >
            Отправить
          </Button>
          {!ready && (
            <div
              style={{
                fontSize: 12,
                color: 'var(--text-muted)',
                textAlign: 'center',
                marginTop: 4,
              }}
            >
              Погрузите все позиции
            </div>
          )}

          <div style={{ marginTop: 10, textAlign: 'center' }}>
            <Button
              kind="secondary"
              onClick={() => {
                discardOrderAt(slot.idx);
                onClose();
              }}
            >
              Выбросить
            </Button>
          </div>
        </Panel>
      </div>
    </div>
  );
}

export function DroneBoard({ onClose }: { onClose: () => void }) {
  const { orders } = useGame();
  const [open_idx, setOpenIdx] = useState<number | null>(null);
  const open = open_idx === null ? null : (orders[open_idx] ?? null);

  return (
    <>
      <div className="scrim" onClick={onClose}>
        <div onClick={(e) => e.stopPropagation()}>
          <Panel
            title="Площадка дрона"
            onClose={onClose}
            style={{ maxWidth: 720, width: '94vw' }}
          >
            {orders.length === 0 ? (
              <div style={{ color: 'var(--text-muted)', textAlign: 'center', padding: 20 }}>
                Дрон открывается на втором уровне.
              </div>
            ) : (
              <div
                style={{
                  display: 'grid',
                  gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))',
                  gap: 10,
                  maxHeight: '60vh',
                  overflowY: 'auto',
                }}
              >
                {orders.map((slot) => (
                  <OrderCard key={slot.idx} slot={slot} onOpen={() => setOpenIdx(slot.idx)} />
                ))}
              </div>
            )}
          </Panel>
        </div>
      </div>

      {open && open.state !== 'empty_cooldown' && (
        <OrderWindow slot={open} onClose={() => setOpenIdx(null)} />
      )}
    </>
  );
}
