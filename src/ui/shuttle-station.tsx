/**
 * Станция шаттла: четыре состояния одного экрана — [[tz-shuttle-mars]] 6.1-6.4.
 *
 * Кнопки «Отправить» здесь нет и не будет: рейс стартует сам, когда закрылся
 * последний отсек. Это единственное место игры, где действие игрока необратимо,
 * поэтому лента отсеков всегда показывает, сколько осталось до точки невозврата.
 *
 * Картинка площадки меняется вместе с состоянием и несет ту же информацию, что
 * и текст: пустая плита — шаттл в рейсе, шаттл на плите — ждет вас. Игрок,
 * который взглянул на экран мельком, читает состояние по силуэту, не по подписи.
 */

import { useState } from 'react';
import { SKIP_ARRIVING_SOON_SEC, SKIP_HIDE_BELOW_SEC } from '../domain/config/economy';
import { GOODS } from '../domain/config/goods';
import { MODULES } from '../domain/config/modules';
import {
  type ShuttleSlot,
  type ShuttleTrip,
  skipPrice,
  slotBuyoutPrice,
  slotCovered,
  slotXp,
  tripXp,
} from '../domain/shuttle';
import { availableOf, type WarehouseState } from '../domain/warehouse';
import { useGame } from '../state/gameStore';
import { useConfirmSpend } from './confirm-spend';
import { useGoalSpot } from './first-goal';
import { Button, GoodIcon, ISOTOPE_GLYPH, Panel, Timer } from './kit';

const TIER_COLOR: Record<string, string> = {
  basic: 'var(--panel-border)',
  rare: 'var(--secondary-dark)',
  gated: 'var(--xp)',
};

/** Плита с шаттлом или без него — по состоянию рейса. */
function Pad({ docked }: { docked: boolean }) {
  return (
    <div style={{ position: 'relative', height: 150, marginBottom: 10 }}>
      <img
        src="/assets/buildings/landing_pad.png"
        alt=""
        // Декорация не должна перехватывать тапы: она лежит поверх панели.
        style={{
          position: 'absolute',
          inset: 0,
          margin: 'auto',
          maxHeight: 150,
          pointerEvents: 'none',
        }}
      />
      {docked && (
        <img
          src="/assets/transport/shuttle.png"
          alt=""
          style={{
            position: 'absolute',
            left: '50%',
            top: 6,
            transform: 'translateX(-50%)',
            maxHeight: 110,
            pointerEvents: 'none',
          }}
        />
      )}
    </div>
  );
}

/** Лента отсеков внизу экрана — паттерн вагонов поезда из референса. */
function SlotStrip({ trip, onOpen }: { trip: ShuttleTrip; onOpen: (idx: number) => void }) {
  const warehouse = useGame((s) => s.warehouse);

  return (
    <div style={{ display: 'flex', gap: 8, justifyContent: 'center', flexWrap: 'wrap' }}>
      {trip.slots.map((slot) => {
        const good = GOODS[slot.good_id];
        const done = slot.qty_filled >= slot.qty_required;
        // Покрытие считает домен, а не верстка: сравнение с сырым `qty` красило
        // зеленым отсек, который нечем закрыть, — товар уже лежал в чужом слоте.
        const covered = slotCovered(slot, warehouse);

        return (
          <button
            key={slot.idx}
            type="button"
            className="slot"
            onClick={() => onOpen(slot.idx)}
            style={{
              padding: 8,
              minWidth: 74,
              gap: 4,
              border: done ? '3px solid var(--action)' : undefined,
              background: done ? 'var(--panel-warm)' : undefined,
            }}
          >
            <GoodIcon name={good.name} size={30} />
            <div
              style={{
                fontSize: 11,
                fontWeight: 800,
                color: done
                  ? 'var(--action-dark)'
                  : covered
                    ? 'var(--action-dark)'
                    : 'var(--text-muted)',
              }}
            >
              {done ? '✓' : `${slot.qty_filled}/${slot.qty_required}`}
            </div>
          </button>
        );
      })}
    </div>
  );
}

/**
 * Что показывает окно отсека — счетчик и видимость кнопки «Докупить» — как
 * чистая функция без React. Вынесена из `SlotSheet` ради находок Н-3/Н-8
 * (`__tests__/shuttle-station.test.ts`): модульный тест домена верстку не
 * видит, а прогнать браузер здесь нельзя (порт занят соседними агентами).
 * Функция — тот же код, что рендерит экран, а не его пересказ в тесте: JSX
 * ниже только читает эти поля, ничего не считает заново.
 */
export function slotSheetView(slot: ShuttleSlot, warehouse: WarehouseState) {
  // «Есть N» — это доступное, а не сырое qty: зарезервированное под другой заказ
  // погрузить нельзя (каркас раздел 9, `reserved` отделен от `qty`). Счетчик по
  // `qty` обещал погрузку, которой домен не давал, и тап отвечал «Нет на складе».
  const have = availableOf(warehouse, slot.good_id);
  const short = slot.qty_required - slot.qty_filled;
  /** Отсек набран целиком: действий над ним больше нет (ТЗ 6.2, `loaded`). */
  const closed = short <= 0;
  // Докупки не бывает, когда склад и так закрывает остаток отсека целиком
  // (ТЗ 6.2, `covered_by_stock`: «Докупки нет — незачем»). Один и тот же
  // предикат, что красит ленту отсеков зеленым (SlotStrip выше) — считает
  // домен, а не верстка второй раз.
  const covered = slotCovered(slot, warehouse);
  return {
    have,
    short,
    closed,
    covered,
    // ТЗ шаттла 6.2: счетчик показывает требование ОТСЕКА целиком
    // (`qty_required`), не остаток после частичной погрузки — иначе после
    // «Погрузить {stock}» отсек на вид уменьшается сам собой.
    counterText: closed
      ? `погружено ${slot.qty_filled}`
      : `есть ${have} / нужно ${slot.qty_required}`,
    // ТЗ 6.2, `covered_by_stock` (stock >= qty): «Докупки нет — незачем».
    showBuyout: !closed && !covered,
  };
}

/** Окно отсека: счетчик, XP, бесплатный путь первым, платный вторым. */
function SlotSheet({ slot, onClose }: { slot: ShuttleSlot; onClose: () => void }) {
  const { warehouse, isotopes, loadShuttleSlot, buyoutShuttleSlot } = useGame();
  const good = GOODS[slot.good_id];
  const price = slotBuyoutPrice(slot, warehouse);
  const { have, short, closed, showBuyout, counterText } = slotSheetView(slot, warehouse);
  // Докупка тратит изотопы необратимо (И-12: не изымается) — каркас раздел 13
  // требует подтверждающий попап для любой такой траты, не только для скипа.
  const { ask, dialog } = useConfirmSpend();

  return (
    <>
      <div className="scrim" onClick={onClose}>
        <div onClick={(e) => e.stopPropagation()}>
          <Panel title={good.name} onClose={onClose} style={{ maxWidth: 380, width: '92vw' }}>
            <div style={{ textAlign: 'center', marginBottom: 12 }}>
              <GoodIcon name={good.name} size={54} />
              <div
                style={{ fontSize: 22, fontWeight: 800, color: 'var(--title)', marginTop: 6 }}
              >
                {counterText}
              </div>
              <div style={{ color: 'var(--xp)', fontWeight: 800 }}>
                +{slotXp(slot)} XP за отсек
              </div>
            </div>

            {/*
              Закрытый отсек — только справка, без единой кнопки действия. ТЗ
              шаттла 6.2 задает для состояния `loaded` отдельный вид: «read-only
              "Погружено: {qty}" без кнопок действия», и отдельной строкой в Edge:
              «отмены погруженного отсека UI не предоставляет».

              Дефект Д-29: закрытый отсек открывал то же окно, что и пустой, и
              рисовал «Докупить 0 за 0 ⚛» рядом с «Погрузить», которая отвечала
              «Нет на складе». Игроку предлагали купить ноль за ноль, а склад был
              тут ни при чем.
            */}
            {closed && (
              <div style={{ textAlign: 'center', color: 'var(--text-muted)', fontWeight: 700 }}>
                Отсек закрыт. Ждет отправки рейса.
              </div>
            )}

            {/*
              Кнопки не рисуются вовсе, а не прячутся атрибутом `hidden`: рядом
              стоит `display: grid` инлайном, и он перебивает `[hidden]` из
              таблицы браузера. Скрытая таким образом кнопка остается на экране
              и остается нажимаемой — то есть дефект выглядит починенным, а
              не является.
            */}
            <div style={{ display: closed ? 'none' : 'grid', gap: 8 }}>
              {/* Три состояния кнопки по ТЗ 6.2: `covered_by_stock` — «Погрузить»;
                  `partial` — «Погрузить {stock}», частичная погрузка допустима;
                  `empty` — «Погрузить» задизейблена с подписью «Нет на складе».
                  Подписи «Погрузить 0» ТЗ не знает, и она обещала бы действие,
                  которого домен не выполнит. */}
              <Button
                full
                disabled={have < 1}
                title={have < 1 ? 'Нет на складе' : undefined}
                onClick={() => {
                  loadShuttleSlot(slot.idx);
                  onClose();
                }}
              >
                {have >= short || have < 1 ? 'Погрузить' : `Погрузить ${have}`}
              </Button>

              {/* ТЗ 6.2, `covered_by_stock` (stock >= qty): «Докупки нет —
                  незачем». Кнопка не рисуется вовсе, а не прячется стилем —
                  тот же урок Д-29: скрытая CSS'ом кнопка остается нажимаемой,
                  непоказанная — нет. Цена стоит на самой кнопке: правило
                  каркаса — кнопка без цены считается багом, игрок не должен
                  угадывать, во что ему встанет тап.

                  Тап не тратит изотопы напрямую — открывает подтверждающий
                  попап (каркас раздел 13, Н-7): докупка так же необратима,
                  как скип рейса (И-12), и то же правило защиты от случайного
                  списания действует на нее. */}
              {showBuyout && (
                <Button
                  kind="secondary"
                  full
                  disabled={price > isotopes}
                  onClick={() =>
                    ask({
                      title: 'Докупить остаток отсека?',
                      body: `${short} ед. «${good.name}» зачислится в отсек напрямую, минуя склад, и не вернется обратно (И-12).`,
                      price,
                      onConfirm: () => {
                        buyoutShuttleSlot(slot.idx);
                        onClose();
                      },
                    })
                  }
                >
                  Докупить {short} за {price} {ISOTOPE_GLYPH}
                </Button>
              )}
            </div>
          </Panel>
        </div>
      </div>
      {dialog}
    </>
  );
}

/** Экран рейса: таймер и цена скипа, пересчитанная на каждый тик (И-6). */
function FlightView({ trip }: { trip: ShuttleTrip }) {
  const { now, isotopes, skipShuttle } = useGame();
  const remaining = Math.max(0, trip.arrives_at - now);
  const price = skipPrice(trip, now);
  // Первая цель привела сюда: дальше нажимают ускорение, иначе экран — таймер.
  const point = useGoalSpot('skip');
  // ТЗ шаттла 6.3/11: «Ускорить» требует подтверждающего попапа с ценой —
  // защита от случайного списания необратимой траты (каркас раздел 13).
  const { ask, dialog } = useConfirmSpend();

  return (
    <>
      <Pad docked={false} />
      <div style={{ textAlign: 'center', marginBottom: 12 }}>
        <div style={{ color: 'var(--text-muted)', fontSize: 13 }}>
          {remaining < SKIP_ARRIVING_SOON_SEC ? 'Скоро прибудет' : 'В пути'}
        </div>
        <Timer remaining_sec={remaining} />
      </div>

      <SlotStripReadonly trip={trip} />

      {/* Ниже порога ускорять уже нечего: пол цены съел бы остаток смысла. */}
      {remaining > SKIP_HIDE_BELOW_SEC && (
        <Button
          full
          disabled={price > isotopes}
          pointer={point && price <= isotopes}
          onClick={() =>
            ask({
              title: 'Ускорить рейс?',
              body: 'Шаттл прибудет немедленно. Списанные изотопы не возвращаются.',
              price,
              onConfirm: skipShuttle,
            })
          }
        >
          Ускорить за {price} {ISOTOPE_GLYPH}
        </Button>
      )}
      {dialog}
    </>
  );
}

function SlotStripReadonly({ trip }: { trip: ShuttleTrip }) {
  return (
    <div
      style={{
        display: 'flex',
        gap: 8,
        justifyContent: 'center',
        flexWrap: 'wrap',
        marginBottom: 12,
      }}
    >
      {trip.slots.map((slot) => (
        <div key={slot.idx} style={{ textAlign: 'center', opacity: 0.75 }}>
          <GoodIcon name={GOODS[slot.good_id].name} size={28} />
          <div style={{ fontSize: 11, fontWeight: 800 }}>{slot.qty_required}</div>
        </div>
      ))}
    </div>
  );
}

/** Экран прибытия: ряд контейнеров, каждый вскрывается тапом. */
function ArrivalView({ trip }: { trip: ShuttleTrip }) {
  const { collectContainerAt, collectAllContainers } = useGame();
  // Груз прилетел — это и есть первая награда, и указатель стоит на ней.
  const point = useGoalSpot('containers');

  return (
    <>
      <Pad docked />
      <div
        style={{
          display: 'flex',
          gap: 10,
          justifyContent: 'center',
          flexWrap: 'wrap',
          marginBottom: 12,
        }}
      >
        {trip.slots.map((slot) => {
          const module = slot.reward ? MODULES[slot.reward] : null;
          return (
            <button
              key={slot.idx}
              type="button"
              className="slot"
              onClick={() => collectContainerAt(slot.idx)}
              disabled={slot.collected}
              style={{
                minWidth: 88,
                padding: 8,
                gap: 4,
                opacity: slot.collected ? 0.5 : 1,
                border: module ? `3px solid ${TIER_COLOR[module.tier]}` : undefined,
              }}
            >
              <div style={{ fontSize: 28 }}>{slot.collected ? '✓' : '📦'}</div>
              {slot.collected && module && (
                <div style={{ fontSize: 11, fontWeight: 800, color: 'var(--title)' }}>
                  {module.name}
                </div>
              )}
              {/* Форс-выдачу не скрываем: pity, о котором игрок узнает сам,
                  порождает волну недоверия ко всей механике дропа. */}
              {slot.collected && slot.floor_forced && (
                <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>
                  Гарантированная поставка
                </div>
              )}
            </button>
          );
        })}
      </div>

      <Button full kind="secondary" pointer={point} onClick={collectAllContainers}>
        Собрать все
      </Button>
    </>
  );
}

export function ShuttleStation({ onClose }: { onClose: () => void }) {
  const { shuttle, now, level } = useGame();
  const [open_idx, setOpenIdx] = useState<number | null>(null);
  const open = open_idx === null ? null : (shuttle?.slots[open_idx] ?? null);

  return (
    <>
      <div className="scrim" onClick={onClose}>
        <div onClick={(e) => e.stopPropagation()}>
          <Panel
            title="Орбитальная станция"
            onClose={onClose}
            style={{ maxWidth: 520, width: '94vw' }}
          >
            {!shuttle ? (
              <div style={{ color: 'var(--text-muted)', textAlign: 'center', padding: 20 }}>
                Шаттл открывается на пятом уровне. Сейчас {level}-й.
              </div>
            ) : shuttle.state === 'IN_TRANSIT' ? (
              <FlightView trip={shuttle} />
            ) : shuttle.state === 'ARRIVED' ? (
              <ArrivalView trip={shuttle} />
            ) : shuttle.state === 'COOLDOWN' ? (
              <>
                <Pad docked={false} />
                <div style={{ textAlign: 'center' }}>
                  <div style={{ color: 'var(--text-muted)', fontSize: 13 }}>
                    Новый рейс через
                  </div>
                  <Timer remaining_sec={Math.max(0, shuttle.cooldown_until - now)} />
                </div>
              </>
            ) : (
              <>
                <Pad docked />
                <div style={{ textAlign: 'center', marginBottom: 10 }}>
                  <div style={{ fontWeight: 800, color: 'var(--title)' }}>
                    Погрузка:{' '}
                    {shuttle.slots.filter((s) => s.qty_filled >= s.qty_required).length}/
                    {shuttle.slots.length} отсеков
                  </div>
                  <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                    Закроете последний — шаттл уйдет сам. Отменить рейс нельзя.
                  </div>
                  <div style={{ color: 'var(--xp)', fontWeight: 800, marginTop: 4 }}>
                    +{tripXp(shuttle)} XP за рейс
                  </div>
                </div>
                <SlotStrip trip={shuttle} onOpen={setOpenIdx} />
              </>
            )}
          </Panel>
        </div>
      </div>

      {open && shuttle?.state === 'ORDER' && (
        <SlotSheet slot={open} onClose={() => setOpenIdx(null)} />
      )}
    </>
  );
}
