/**
 * Экраны второго дня: купол (грядки), склад, фабрика.
 * Композиция по [[ux-township-artlanguage]] раздел 5: интерфейс прижат к краям,
 * центр отдан миру; модалка затемняет мир и не закрывает его целиком.
 */

import { useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { FACTORY_PRICES, plantingCost, productionSpeedupCost } from '../domain/config/economy';
import { ALL_GOOD_IDS, GOODS } from '../domain/config/goods';
import type { GoodId } from '../domain/types';
import { availableOf, occupiedGoods, qtyOf } from '../domain/warehouse';
import { selectWarehouseLoad, selectXpProgress, useGame } from '../state/gameStore';
import { Button, Currency, GoodIcon, Panel, ProgressBar, Timer } from './kit';

export function Hud() {
  const { level, credits, isotopes } = useGame();
  const xp = useGame(useShallow(selectXpProgress));
  const load = useGame(useShallow(selectWarehouseLoad));
  const full = load.used >= load.cap;

  return (
    <div
      style={{
        position: 'absolute',
        top: 12,
        left: 12,
        right: 12,
        display: 'flex',
        gap: 10,
        alignItems: 'center',
        flexWrap: 'wrap',
        zIndex: 10,
      }}
    >
      <div className="currency" style={{ paddingRight: 16 }}>
        <div className="star" />
        <span>ур. {level}</span>
      </div>
      <div style={{ width: 130 }}>
        <ProgressBar value={xp.into} max={xp.need} />
      </div>
      <Currency kind="credits" value={credits} />
      <Currency kind="isotopes" value={isotopes} />
      <div
        className="currency"
        style={{
          marginLeft: 'auto',
          borderColor: full ? 'var(--close)' : 'var(--panel-border)',
        }}
      >
        <span style={{ fontSize: 14 }}>
          Склад {load.used}/{load.cap}
        </span>
      </div>
    </div>
  );
}

export function DomeScreen() {
  const { fields, now, plant, collectField, speedupField, level } = useGame();
  const [picker, setPicker] = useState<number | null>(null);

  const crops = ALL_GOOD_IDS.filter(
    (id) => GOODS[id].kind === 'crop' && GOODS[id].unlock_level <= level,
  );

  return (
    <>
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(96px, 108px))',
          gap: 14,
          justifyContent: 'center',
        }}
      >
        {fields.map((field) => {
          const good = field.good_id ? GOODS[field.good_id] : null;
          const remaining = field.ends_at - now;
          const ready = field.state === 'READY';

          return (
            <div
              key={field.idx}
              className={`slot ${ready ? 'slot-ready' : ''}`}
              style={{ height: 104 }}
              onClick={() =>
                ready ? collectField(field.idx) : good ? null : setPicker(field.idx)
              }
            >
              {!good && <span style={{ fontSize: 30, color: 'var(--text-muted)' }}>+</span>}
              {good && (
                <>
                  <GoodIcon name={good.name} />
                  <span style={{ fontSize: 11, fontWeight: 700, color: 'var(--title)' }}>
                    {good.name}
                  </span>
                  {ready ? (
                    <span
                      style={{ fontSize: 12, fontWeight: 800, color: 'var(--action-dark)' }}
                    >
                      Собрать
                    </span>
                  ) : (
                    <>
                      <Timer remaining_sec={remaining} />
                      <button
                        type="button"
                        className="btn btn-secondary"
                        style={{ padding: '3px 10px', fontSize: 11, borderRadius: 12 }}
                        onClick={(e) => {
                          // Слот целиком кликабелен под сбор — ускорение не должно
                          // проваливаться в него и собирать несозревшее.
                          e.stopPropagation();
                          speedupField(field.idx);
                        }}
                      >
                        {(() => {
                          const price = productionSpeedupCost(remaining, good.kind);
                          // Ноль показываем словом, а не «0 ⚛»: бесплатное действие
                          // не должно выглядеть как покупка за ноль.
                          return price === 0 ? 'Готово' : `${price} ⚛`;
                        })()}
                      </button>
                    </>
                  )}
                </>
              )}
            </div>
          );
        })}
      </div>

      {picker !== null && (
        <div className="scrim" onClick={() => setPicker(null)}>
          <div onClick={(e) => e.stopPropagation()}>
            <Panel
              title="Что посадить"
              onClose={() => setPicker(null)}
              style={{ maxWidth: 420 }}
            >
              <div style={{ display: 'grid', gap: 8 }}>
                {crops.map((id) => {
                  const good = GOODS[id];
                  return (
                    <div
                      key={id}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 10,
                        padding: '4px 2px',
                      }}
                    >
                      <GoodIcon name={good.name} />
                      <div style={{ flex: 1 }}>
                        <div style={{ fontWeight: 800, color: 'var(--title)' }}>
                          {good.name}
                        </div>
                        <div style={{ fontSize: 12 }}>
                          продажа {good.price} кр · {Math.round(good.prod_time_sec / 60)} мин
                        </div>
                      </div>
                      <Button
                        onClick={() => {
                          plant(picker, id);
                          setPicker(null);
                        }}
                      >
                        Посеять · {plantingCost(good.price)}
                      </Button>
                    </div>
                  );
                })}
              </div>
            </Panel>
          </div>
        </div>
      )}
    </>
  );
}

export function WarehousePanel({ onClose }: { onClose: () => void }) {
  const { warehouse, sell } = useGame();
  const goods = occupiedGoods(warehouse);
  const load = useGame(useShallow(selectWarehouseLoad));

  return (
    <div className="scrim" onClick={onClose}>
      <div onClick={(e) => e.stopPropagation()}>
        <Panel title="Склад" onClose={onClose} style={{ maxWidth: 460, width: '92vw' }}>
          <div style={{ marginBottom: 10, fontWeight: 700 }}>
            Занято {load.used} из {load.cap}
          </div>
          {goods.length === 0 && <div style={{ color: 'var(--text-muted)' }}>Пока пусто.</div>}
          <div style={{ display: 'grid', gap: 8, maxHeight: '50vh', overflowY: 'auto' }}>
            {goods.map((id) => {
              const good = GOODS[id];
              const qty = qtyOf(warehouse, id);
              const free = availableOf(warehouse, id);
              return (
                <div key={id} style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  <GoodIcon name={good.name} />
                  <div style={{ flex: 1 }}>
                    <div style={{ fontWeight: 800, color: 'var(--title)' }}>{good.name}</div>
                    <div style={{ fontSize: 12 }}>
                      {qty} шт{qty !== free ? ` · свободно ${free}` : ''} · {good.price} кр/шт
                    </div>
                  </div>
                  <Button kind="secondary" disabled={free < 1} onClick={() => sell(id, 1)}>
                    Продать 1
                  </Button>
                  <Button kind="secondary" disabled={free < 1} onClick={() => sell(id, free)}>
                    Все
                  </Button>
                </div>
              );
            })}
          </div>
        </Panel>
      </div>
    </div>
  );
}

export function FactoryPanel({ onClose }: { onClose: () => void }) {
  const {
    factory_slots,
    now,
    level,
    buildings,
    buyBuilding,
    enqueue,
    collectFactory,
    warehouse,
  } = useGame();
  const [picker, setPicker] = useState<number | null>(null);

  const has_food_module = buildings.includes('food_module');
  const price = FACTORY_PRICES.food_module.first;
  const unlock = FACTORY_PRICES.food_module.unlock_level;

  const recipes = ALL_GOOD_IDS.filter(
    (id) =>
      GOODS[id].kind === 'factory' &&
      GOODS[id].required_building === 'food_module' &&
      GOODS[id].unlock_level <= level,
  );

  return (
    <div className="scrim" onClick={onClose}>
      <div onClick={(e) => e.stopPropagation()}>
        <Panel
          title="Пищевой модуль"
          onClose={onClose}
          style={{ maxWidth: 460, width: '92vw' }}
        >
          {!has_food_module ? (
            <div style={{ display: 'grid', gap: 12, textAlign: 'center' }}>
              <div>
                {level < unlock
                  ? `Откроется на уровне ${unlock}.`
                  : 'Перерабатывает сырье в товары подороже.'}
              </div>
              <Button full disabled={level < unlock} onClick={() => buyBuilding('food_module')}>
                Построить за {price} кр
              </Button>
            </div>
          ) : (
            <div style={{ display: 'grid', gap: 10 }}>
              {factory_slots.map((slot) => {
                const good = slot.good_id ? GOODS[slot.good_id] : null;
                const ready = slot.state === 'READY';
                return (
                  <div
                    key={slot.idx}
                    className={`slot ${ready ? 'slot-ready' : ''}`}
                    style={{ flexDirection: 'row', gap: 10, padding: 10, minHeight: 62 }}
                    onClick={() =>
                      ready
                        ? collectFactory(slot.idx)
                        : slot.state === 'EMPTY'
                          ? setPicker(slot.idx)
                          : null
                    }
                  >
                    {!good && <span style={{ color: 'var(--text-muted)' }}>Пустой слот</span>}
                    {good && (
                      <>
                        <GoodIcon name={good.name} />
                        <div style={{ flex: 1, textAlign: 'left' }}>
                          <div style={{ fontWeight: 800, color: 'var(--title)' }}>
                            {good.name}
                          </div>
                          <div style={{ fontSize: 12 }}>
                            {slot.state === 'QUEUED' ? (
                              <span style={{ color: 'var(--secondary-dark)', fontWeight: 700 }}>
                                Ждет:{' '}
                                {good.inputs
                                  .filter((i) => availableOf(warehouse, i.good_id) < i.qty)
                                  .map((i) => GOODS[i.good_id].name)
                                  .join(', ')}
                              </span>
                            ) : ready ? (
                              <span style={{ color: 'var(--action-dark)', fontWeight: 800 }}>
                                Забрать
                              </span>
                            ) : (
                              <Timer remaining_sec={slot.ends_at - now} />
                            )}
                          </div>
                        </div>
                      </>
                    )}
                  </div>
                );
              })}
            </div>
          )}

          {picker !== null && (
            <div
              style={{
                marginTop: 14,
                borderTop: '3px dashed rgba(168,118,62,.4)',
                paddingTop: 12,
              }}
            >
              <div style={{ fontWeight: 800, color: 'var(--title)', marginBottom: 8 }}>
                Рецепт
              </div>
              <div style={{ display: 'grid', gap: 8 }}>
                {recipes.map((id) => {
                  const good = GOODS[id];
                  return (
                    <div key={id} style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                      <GoodIcon name={good.name} />
                      <div style={{ flex: 1 }}>
                        <div style={{ fontWeight: 800, color: 'var(--title)' }}>
                          {good.name}
                        </div>
                        <div style={{ fontSize: 12 }}>
                          {good.inputs
                            .map((i) => `${GOODS[i.good_id].name} x${i.qty}`)
                            .join(' + ')}{' '}
                          → {good.price} кр
                        </div>
                      </div>
                      <Button
                        onClick={() => {
                          enqueue(picker, id as GoodId);
                          setPicker(null);
                        }}
                      >
                        В очередь
                      </Button>
                    </div>
                  );
                })}
              </div>
            </div>
          )}
        </Panel>
      </div>
    </div>
  );
}

export function Toasts() {
  const toasts = useGame((s) => s.toasts);
  const color = { info: 'var(--panel)', warn: '#ffd9c8', reward: '#dff5cf' } as const;

  return (
    <div
      style={{
        position: 'absolute',
        top: 62,
        left: '50%',
        transform: 'translateX(-50%)',
        display: 'grid',
        gap: 8,
        zIndex: 30,
      }}
    >
      {toasts.map((t) => (
        <div
          key={t.id}
          className="panel"
          style={{
            padding: '9px 18px',
            fontWeight: 800,
            color: 'var(--title)',
            background: color[t.kind],
            textAlign: 'center',
          }}
        >
          {t.text}
        </div>
      ))}
    </div>
  );
}
