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
  discardImpact,
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
          // Позиция закрыта, когда недостачи не осталось — частичная погрузка
          // (qty_filled между 0 и qty) держит счетчик открытым до докупки или
          // повторного «Погрузить» (Т3-1).
          const done = position.qty_filled >= position.qty;
          const have = done ? position.qty : availableOf(warehouse, position.good_id);
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
                {done ? '✓' : `${have}/${position.qty}`}
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

/**
 * Confirm-диалог выброса (ТЗ дрона 7.2, решение консилиума: один
 * универсальный текст, а не «мягкий/жесткий» развилка). Тело собирается из
 * фактического состояния заказа — игрок видит точную цену решения.
 */
function DiscardConfirm({
  slot,
  onCancel,
  onConfirm,
}: {
  slot: OrderSlot;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const impact = discardImpact(slot);

  return (
    <div className="scrim" onClick={onCancel}>
      <div onClick={(e) => e.stopPropagation()}>
        <Panel title="Выбросить заказ?" style={{ maxWidth: 380, width: '90vw' }}>
          <div style={{ display: 'grid', gap: 8, marginBottom: 14, fontSize: 13 }}>
            {impact.stock_positions > 0 && (
              <div>Товар со склада ({impact.stock_positions} поз.) вернется на склад.</div>
            )}
            {impact.purchased_isotopes > 0 && (
              <div>
                Докупленное на {impact.purchased_isotopes} {ISOTOPE_GLYPH} сгорит — изотопы не
                возвращаются (И-12).
              </div>
            )}
          </div>
          <div style={{ display: 'flex', gap: 10 }}>
            <Button kind="secondary" full onClick={onCancel}>
              Отмена
            </Button>
            <Button full onClick={onConfirm}>
              Выбросить
            </Button>
          </div>
        </Panel>
      </div>
    </div>
  );
}

/** Окно заказа: позиции с кнопками «Погрузить»/«Докупить», внизу «Отправить» и «Выбросить». */
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
  // Confirm нужен только когда есть что терять (АС 10): заказ, которого
  // никто не трогал, выбрасывается мгновенно, без диалога.
  const [confirming, setConfirming] = useState(false);

  return (
    <>
      <div className="scrim" onClick={onClose}>
        <div onClick={(e) => e.stopPropagation()}>
          <Panel
            title={slot.npc_name}
            onClose={onClose}
            style={{ maxWidth: 440, width: '92vw' }}
          >
            <div style={{ display: 'grid', gap: 8, marginBottom: 12 }}>
              {slot.positions.map((position, i) => {
                const good = GOODS[position.good_id];
                const have = availableOf(warehouse, position.good_id);
                // Недостача — сколько еще нужно закрыть погрузкой или докупкой.
                // Частичная погрузка (Т3-1): позиция может быть закрыта в два
                // приема, «Погрузить» и «Докупить» показываются ОДНОВРЕМЕННО,
                // пока недостача не обнулилась.
                const short = position.qty - position.qty_filled;
                const done = short <= 0;
                // Складом покрывается остаток целиком — тогда докупка не нужна
                // вовсе (ТЗ дрона 7.2, строка «на складе, хватает»).
                const covered = positionCovered(position, warehouse);
                // Цена и докупаемое количество — по НЕДОСТАЧЕ, а не по полной
                // величине позиции: докупка добирает только то, чего не хватает.
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
                        {done
                          ? `погружено ${position.qty_filled}`
                          : `есть ${have} / нужно ${position.qty}`}
                      </div>
                    </div>

                    {done ? (
                      <span style={{ fontSize: 22, color: 'var(--action-dark)' }}>✓</span>
                    ) : (
                      <div style={{ display: 'grid', gap: 6 }}>
                        <Button
                          disabled={have < 1}
                          title={have < 1 ? 'Нет на складе' : undefined}
                          onClick={() => loadOrderPosition(slot.idx, i)}
                        >
                          {have >= short || have < 1 ? 'Погрузить' : `Погрузить ${have}`}
                        </Button>
                        {/* Докупка не рисуется, когда склад и так закрывает
                          остаток целиком — «Докупки нет — незачем» (тот же
                          паттерн, что у отсека шаттла). Цена стоит на кнопке
                          всегда — правило каркаса: кнопка без цены считается
                          багом, игрок не должен угадывать. */}
                        {!covered && (
                          <Button
                            kind="secondary"
                            disabled={price > isotopes}
                            onClick={() => buyoutOrderPosition(slot.idx, i)}
                          >
                            Докупить {short} за {price} {ISOTOPE_GLYPH}
                          </Button>
                        )}
                      </div>
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
                  // АС 10: ничего не тронуто — выброс мгновенный, без диалога.
                  // АС 11: есть что терять (склад и/или докупка) — confirm
                  // обязателен, цена решения показана явно.
                  if (discardImpact(slot).needs_confirm) {
                    setConfirming(true);
                  } else {
                    discardOrderAt(slot.idx);
                    onClose();
                  }
                }}
              >
                Выбросить
              </Button>
            </div>
          </Panel>
        </div>
      </div>

      {confirming && (
        <DiscardConfirm
          slot={slot}
          onCancel={() => setConfirming(false)}
          onConfirm={() => {
            discardOrderAt(slot.idx);
            setConfirming(false);
            onClose();
          }}
        />
      )}
    </>
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
