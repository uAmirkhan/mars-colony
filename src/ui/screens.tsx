/**
 * Экраны второго дня: купол (грядки), склад, фабрика.
 * Композиция по [[ux-township-artlanguage]] раздел 5: интерфейс прижат к краям,
 * центр отдан миру; модалка затемняет мир и не закрывает его целиком.
 */

import { useEffect, useRef, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import {
  FACTORY_HINTS,
  FACTORY_NAMES,
  FACTORY_PRICES,
  plantingCost,
  productionSpeedupCost,
} from '../domain/config/economy';
import { ALL_GOOD_IDS, GOODS } from '../domain/config/goods';
import { MECHANIC_UNLOCK_LEVEL } from '../domain/config/levels';
import { ALL_MODULE_IDS, MODULES } from '../domain/config/modules';
import {
  type BuildSlot,
  type ConstructionState,
  missingFor,
  moduleCapacity,
  moduleTotal,
} from '../domain/construction';
import type { ModuleCounts } from '../domain/droproller';
import type { GoodId, ModuleId } from '../domain/types';
import { availableOf, qtyOf, totalQty, type WarehouseState } from '../domain/warehouse';
import {
  type PurchasableBuilding,
  selectWarehouseLoad,
  selectXpProgress,
  type Toast,
  useGame,
} from '../state/gameStore';
import { actWithFx } from './feel/act';
import { useGoalFieldIdx, useGoalSpot } from './first-goal';

/** Порядок карточек — порядок открытия по уровню, он же порядок покупки. */
const PURCHASABLE_BUILDINGS: PurchasableBuilding[] = [
  'food_module',
  'mining_site',
  'atmospheric_module',
  'textile_module',
];

import {
  Button,
  Currency,
  GoodIcon,
  ISOTOPE_GLYPH,
  Panel,
  ProgressBar,
  Timer,
  WAREHOUSE_WARN_RATIO,
} from './kit';

export function Hud() {
  const { level, credits, isotopes } = useGame();
  const xp = useGame(useShallow(selectXpProgress));
  const load = useGame(useShallow(selectWarehouseLoad));
  const full = load.used >= load.cap;

  return (
    // Счетчики стоят в ПОТОКЕ, а не поверх мира.
    //
    // Дефект Д-31, увиденный на выложенной странице: абсолютный HUD не занимал
    // высоты, купол центрировался по всему экрану и наезжал на счетчики. На
    // телефоне ряд счетчиков переносится на две строки, и карточки грядок
    // уходили прямо под «Склад 16/50» и под счетчик изотопов. Проверки этого
    // не видели: перекрытие меряется по кнопкам, а тут див наехал на див.
    //
    // Композиция от этого не страдает: HUD и так прижат к верхнему краю, а
    // центр по-прежнему отдан миру ([[ux-township-artlanguage]] раздел 5).
    <div
      style={{
        flex: '0 0 auto',
        width: '100%',
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
      <div style={{ width: 130 }} data-fx-anchor="xp">
        <ProgressBar value={xp.into} max={xp.need} />
      </div>
      <Currency kind="credits" value={credits} />
      <Currency kind="isotopes" value={isotopes} />
      <div
        className="currency"
        data-fx-anchor="warehouse"
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
  // Грядка, на которую показывает первая цель. Спрашиваем один раз на экран:
  // из цикла по грядкам хук не вызвать.
  const goal_field = useGoalFieldIdx();

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
          // Ширина обязательна. `auto-fit` раскладывает по ширине блока, а блок
          // стоит внутри колонки с центрированием и без ширины схлопывается по
          // содержимому — то есть в ОДИН столбец. Грядки выстраивались в
          // вертикальную ленту, и на телефоне на первый экран попадали две
          // штуки из семи: колония читалась как пустая страница.
          width: 'min(560px, 92vw)',
        }}
      >
        {fields.map((field) => {
          const good = field.good_id ? GOODS[field.good_id] : null;
          const remaining = field.ends_at - now;
          const ready = field.state === 'READY';

          return (
            <div
              key={field.idx}
              className={`slot ${ready ? 'slot-ready' : ''}${
                goal_field === field.idx ? ' goal-point' : ''
              }`}
              style={{ height: 104 }}
              onClick={(e) =>
                ready
                  ? // Цифра «+N» вылетает из той грядки, по которой нажали, —
                    // поэтому сбор идет через обертку, знающую элемент.
                    actWithFx(e.currentTarget, () => collectField(field.idx))
                  : good
                    ? null
                    : setPicker(field.idx)
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
                          return price === 0 ? 'Готово' : `${price} ${ISOTOPE_GLYPH}`;
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

type WarehouseTab = 'goods' | 'modules';

export interface WarehouseGoodsRow {
  id: GoodId;
  name: string;
  price: number;
  qty: number;
  free: number;
  /** Позиция с нулевым остатком приглушена, но не скрыта (ТЗ 9.2). */
  dimmed: boolean;
}

export interface WarehouseModuleRow {
  id: ModuleId;
  name: string;
  qty: number;
  /** Гейтовый тир: в этом срезе гейт всегда закрыт (нет построенных гейтовых
   *  зданий) — рисуется заблокированным безусловно (spec-prototype-build 16). */
  locked: boolean;
  /** ТЗ 9.2: нужна ли позиция активной стройке (бейдж-иконка кирки). */
  needed: boolean;
}

export interface WarehouseCapacityView {
  used: number;
  cap: number;
  /** ТЗ 9.2: капасити-бар красится предупреждающим цветом при заполнении >= 90%. */
  warn: boolean;
}

export interface WarehousePanelView {
  goodsCapacity: WarehouseCapacityView;
  modulesCapacity: WarehouseCapacityView;
  goods: WarehouseGoodsRow[];
  /** Первая ПРОДАВАЕМАЯ строка — куда указывает первая цель FTUE, если активна. */
  firstSellableGoodId: GoodId | null;
  /** ТЗ 9.2 edge: вкладка «Модули» видна всегда, но до открытия шаттла — заглушка. */
  modulesUnlocked: boolean;
  modules: WarehouseModuleRow[];
}

function capacityView(used: number, cap: number): WarehouseCapacityView {
  return { used, cap, warn: cap > 0 && used / cap >= WAREHOUSE_WARN_RATIO };
}

/**
 * Строка модуля на вкладке «Модули»: нужна ли она хотя бы одной доступной
 * стройке прямо сейчас. Считается по `missingFor` — тому же предикату,
 * которым карточка стройки решает, показывать ли «Докупить»: если позиция
 * ЕЩЕ значится дефицитной у стройки в `AVAILABLE`, склад и стройка реально
 * конкурируют за эту единицу (ТЗ производства 9.2, п.3.4). Стройка в
 * `IN_PROGRESS` модули уже списала при старте — конкурировать ей больше не за
 * что, повторный интерес к тому же модулю был бы двойным счетом.
 */
function neededByActiveConstruction(
  module_id: ModuleId,
  builds: BuildSlot[],
  stock: ModuleCounts,
): boolean {
  return builds.some(
    (b) => b.state === 'AVAILABLE' && (missingFor(b, stock)[module_id] ?? 0) > 0,
  );
}

/**
 * Вся композиция экрана склада как чистая функция состояния — без JSX и без
 * рендера. `WarehousePanel` только читает это дерево и расставляет разметку;
 * доказывается перебором состояний (`warehouse-panel.test.ts`), а не
 * рендером DOM, которого в этом проекте для `screens.tsx` нет вовсе.
 */
export function warehousePanelView(state: {
  level: number;
  warehouse: WarehouseState;
  construction: ConstructionState;
}): WarehousePanelView {
  // ТЗ 9.2: «сортировка — по категории (грядка/фабрика), внутри категории —
  // по порядку открытия уровня». `ALL_GOOD_IDS` уже в этом порядке (раздел
  // объявления `config/goods.ts`: сперва все `crop`, затем все `factory`,
  // внутри — по возрастанию `unlock_level`) — отдельной сортировки не нужно.
  const goods: WarehouseGoodsRow[] = ALL_GOOD_IDS.filter(
    (id) => GOODS[id].unlock_level <= state.level,
  ).map((id) => {
    const good = GOODS[id];
    const qty = qtyOf(state.warehouse, id);
    const free = availableOf(state.warehouse, id);
    return { id, name: good.name, price: good.price, qty, free, dimmed: qty === 0 };
  });
  const firstSellableGoodId = goods.find((g) => g.free >= 1)?.id ?? null;

  const modulesUnlocked = state.level >= MECHANIC_UNLOCK_LEVEL.shuttle;
  const modules: WarehouseModuleRow[] = ALL_MODULE_IDS.map((id) => {
    const module = MODULES[id];
    const locked = module.tier === 'gated';
    return {
      id,
      name: module.name,
      qty: state.construction.stock[id] ?? 0,
      locked,
      needed:
        !locked &&
        neededByActiveConstruction(id, state.construction.builds, state.construction.stock),
    };
  });

  return {
    goodsCapacity: capacityView(totalQty(state.warehouse), state.warehouse.capacity),
    modulesCapacity: capacityView(
      moduleTotal(state.construction.stock),
      moduleCapacity(state.construction),
    ),
    goods,
    firstSellableGoodId,
    modulesUnlocked,
    modules,
  };
}

/**
 * Склад — [[tz-production-mars]] 9.2: «заголовок с двумя вкладками — Товары
 * и Модули... здесь объединены в один вход намеренно — у игрока один
 * ментальный объект "мой склад"».
 *
 * До этой правки вкладка «Модули» существовала только внутри экрана стройки
 * (`ConstructionPanel` → `ModuleStock`) — находка Н-6: со склада ее было
 * не увидеть вовсе, хотя это тот же физический склад со своим лимитом (каркас
 * раздел 6, «строй-модули хранятся отдельным лимитом»). `ModuleStock` внутри
 * стройки не тронут — он читает то же состояние (`construction.stock`), но
 * живет в чужом файле, который сейчас правит другой агент.
 */
export function WarehousePanel({
  onClose,
  onOpenConstruction,
}: {
  onClose: () => void;
  /**
   * ТЗ 9.2: «Апгрейд емкости»: кнопка «Расширить склад»... тап ведет на экран
   * стройки (9.4) с подсветкой соответствующего рецепта». Подсветка рецепта
   * не реализована этой правкой — она требует состояния внутри экрана
   * стройки, а этот файл сознательно не трогает `construction.tsx` (там
   * параллельно работает другой агент); переход на экран стройки без
   * подсветки уже выполняет буквальное требование «тап ведет на экран
   * стройки», просто без последнего слоя полировки.
   */
  onOpenConstruction?: () => void;
}) {
  const { warehouse, sell, level, construction } = useGame();
  // Первая цель довела до склада — здесь она показывает на продажу.
  const point_sell = useGoalSpot('sell');
  const [tab, setTab] = useState<WarehouseTab>('goods');

  const view = warehousePanelView({ level, warehouse, construction });
  const capacity = tab === 'goods' ? view.goodsCapacity : view.modulesCapacity;

  return (
    <div className="scrim" onClick={onClose}>
      <div onClick={(e) => e.stopPropagation()}>
        <Panel title="Склад" onClose={onClose} style={{ maxWidth: 460, width: '92vw' }}>
          <div style={{ display: 'flex', gap: 8, marginBottom: 12 }}>
            <button
              type="button"
              className={`btn btn-${tab === 'goods' ? 'primary' : 'secondary'}`}
              style={{ padding: '6px 16px', fontSize: 13 }}
              onClick={() => setTab('goods')}
            >
              Товары
            </button>
            <button
              type="button"
              className={`btn btn-${tab === 'modules' ? 'primary' : 'secondary'}`}
              style={{ padding: '6px 16px', fontSize: 13 }}
              onClick={() => setTab('modules')}
            >
              Модули
            </button>
          </div>

          {/* Капасити-бар ТЕКУЩЕЙ вкладки — у товаров и модулей разные лимиты
              (каркас раздел 6: емкости растут вместе, но считаются раздельно). */}
          <div style={{ marginBottom: 10 }}>
            <div style={{ fontWeight: 700, marginBottom: 4 }}>
              {capacity.used} / {capacity.cap}
            </div>
            <ProgressBar value={capacity.used} max={capacity.cap} warn={capacity.warn} />
          </div>

          {tab === 'goods' && (
            <div style={{ display: 'grid', gap: 8, maxHeight: '46vh', overflowY: 'auto' }}>
              {view.goods.map((row) => (
                <div
                  key={row.id}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 10,
                    // Позиция с нулевым остатком приглушена, но не скрыта
                    // (ТЗ 9.2): игрок видит полный ассортимент открытого,
                    // а не только то, что уже успел собрать.
                    opacity: row.dimmed ? 0.45 : 1,
                  }}
                >
                  <GoodIcon name={row.name} />
                  <div style={{ flex: 1 }}>
                    <div style={{ fontWeight: 800, color: 'var(--title)' }}>{row.name}</div>
                    <div style={{ fontSize: 12 }}>
                      {row.qty} шт{row.qty !== row.free ? ` · свободно ${row.free}` : ''} ·{' '}
                      {row.price} кр/шт
                    </div>
                  </div>
                  <Button
                    kind="secondary"
                    disabled={row.free < 1}
                    onClick={() => sell(row.id, 1)}
                  >
                    Продать 1
                  </Button>
                  <Button
                    kind="secondary"
                    disabled={row.free < 1}
                    // Указатель стоит на первой ПРОДАВАЕМОЙ строке, а не на
                    // всех сразу и не на первой позиции списка вообще:
                    // список теперь показывает и пустые позиции, а цель
                    // должна вести туда, где есть что продать.
                    pointer={point_sell && row.id === view.firstSellableGoodId}
                    onClick={() => sell(row.id, row.free)}
                  >
                    Все
                  </Button>
                </div>
              ))}
            </div>
          )}

          {tab === 'modules' &&
            (view.modulesUnlocked ? (
              <div style={{ display: 'grid', gap: 8, maxHeight: '46vh', overflowY: 'auto' }}>
                {view.modules.map((row) => (
                  <div
                    key={row.id}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: 10,
                      opacity: row.locked || row.qty === 0 ? 0.45 : 1,
                    }}
                  >
                    <GoodIcon name={row.name} />
                    <div style={{ flex: 1 }}>
                      <div style={{ fontWeight: 800, color: 'var(--title)' }}>
                        {row.name}
                        {row.locked ? ' 🔒' : ''}
                      </div>
                      <div style={{ fontSize: 12 }}>
                        {row.locked ? 'Требует гейтового здания' : `${row.qty} шт`}
                        {/* ТЗ 9.2: бейдж-иконка кирки — требуется ли позиция
                            активной стройке, видимость конкуренции без
                            захода на экран стройки (п.3.4). */}
                        {row.needed && (
                          <span style={{ color: 'var(--secondary-dark)', fontWeight: 800 }}>
                            {' '}
                            · ⛏ нужен стройке
                          </span>
                        )}
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              // ТЗ 9.2 edge: вкладка не скрывается вовсе — «модули физически
              // не могут появиться раньше, но вкладка не скрывается полностью,
              // чтобы не создавать впечатление отсутствующей фичи».
              <div style={{ color: 'var(--text-muted)' }}>
                Появится после открытия Грузового шаттла
              </div>
            ))}

          {onOpenConstruction && (
            <Button
              kind="secondary"
              full
              onClick={() => {
                onClose();
                onOpenConstruction();
              }}
              // ТЗ 9.2: «Расширить склад» переводит на экран стройки (9.4),
              // не открывает стройку inline здесь же.
            >
              Расширить склад
            </Button>
          )}
        </Panel>
      </div>
    </div>
  );
}

/**
 * Производство: все здания класса А на одном экране.
 *
 * Раньше экран знал ровно про Пищевой модуль, а Буровая, Атмосферный и
 * Текстильный имели цены в конфиге и ни одной кнопки в игре — часть рецептов
 * была недостижима. Правило каркаса: механика, у которой есть цена и нет
 * кнопки, считается багом, а не незаконченной работой.
 */
function BuildingCard({
  type,
  onPick,
}: {
  type: PurchasableBuilding;
  onPick: (slot_idx: number) => void;
}) {
  const {
    factory_slots,
    now,
    level,
    buildings,
    buyBuilding,
    collectFactory,
    speedupFactory,
    warehouse,
  } = useGame();

  const def = FACTORY_PRICES[type];
  const owned = buildings.includes(type);
  const slots = factory_slots.filter((s) => s.building_type === type);

  if (!owned) {
    return (
      <div className="slot" style={{ padding: 12, gap: 8, alignItems: 'stretch' }}>
        <div style={{ fontWeight: 800, color: 'var(--title)' }}>{FACTORY_NAMES[type]}</div>
        <div style={{ fontSize: 12, color: 'var(--text-muted)', textAlign: 'left' }}>
          {level < def.unlock_level
            ? `Откроется на уровне ${def.unlock_level}. Сейчас ${level}-й.`
            : FACTORY_HINTS[type]}
        </div>
        <Button full disabled={level < def.unlock_level} onClick={() => buyBuilding(type)}>
          Построить за {def.first} кр
        </Button>
      </div>
    );
  }

  return (
    <div style={{ display: 'grid', gap: 8 }}>
      <div style={{ fontWeight: 800, color: 'var(--title)' }}>{FACTORY_NAMES[type]}</div>
      {slots.map((slot) => {
        const good = slot.good_id ? GOODS[slot.good_id] : null;
        const ready = slot.state === 'READY';
        return (
          <div
            key={slot.idx}
            className={`slot ${ready ? 'slot-ready' : ''}`}
            style={{ flexDirection: 'row', gap: 10, padding: 10, minHeight: 62 }}
            onClick={(e) =>
              ready
                ? actWithFx(e.currentTarget, () => collectFactory(slot.idx))
                : slot.state === 'EMPTY'
                  ? onPick(slot.idx)
                  : null
            }
          >
            {!good && <span style={{ color: 'var(--text-muted)' }}>Пустой слот</span>}
            {good && (
              <>
                <GoodIcon name={good.name} />
                <div style={{ flex: 1, textAlign: 'left' }}>
                  <div style={{ fontWeight: 800, color: 'var(--title)' }}>{good.name}</div>
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
                {/* ТЗ производства 9.3: у слота в PRODUCING под таймером стоит
                    «Ускорить за {price} ⚛». Ставка ускорения фабрики жила в
                    конфиге без единой кнопки в игре, а каркас (раздел 13)
                    называет ровно это багом: «Любая механика с таймером обязана
                    иметь экран или состояние с кнопкой ускорения». */}
                {slot.state === 'PRODUCING' && (
                  <button
                    type="button"
                    className="btn btn-secondary"
                    style={{ padding: '3px 10px', fontSize: 11, borderRadius: 12 }}
                    onClick={(e) => {
                      // Слот целиком кликабелен под сбор — ускорение не должно
                      // проваливаться в него и забирать несозревшее.
                      e.stopPropagation();
                      speedupFactory(slot.idx);
                    }}
                  >
                    {(() => {
                      // Контекст цены — тот же, которым платит стор
                      // (`speedupFactory`): показанное число обязано совпадать
                      // со списанным, иначе кнопка врет о цене.
                      const price = productionSpeedupCost(slot.ends_at - now, 'factory');
                      // AC7 ТЗ производства: ниже порога цена ноль, кнопка не
                      // исчезает и подписана «Готово».
                      return price === 0 ? 'Готово' : `Ускорить за ${price} ${ISOTOPE_GLYPH}`;
                    })()}
                  </button>
                )}
              </>
            )}
          </div>
        );
      })}
    </div>
  );
}

export function FactoryPanel({ onClose }: { onClose: () => void }) {
  const { factory_slots, level, enqueue } = useGame();
  const [picker, setPicker] = useState<number | null>(null);

  // Рецепты берутся у здания, которому принадлежит выбранный слот: иначе в
  // очередь буровой можно было бы поставить ткань.
  const picked = picker === null ? null : (factory_slots.find((s) => s.idx === picker) ?? null);
  const recipes = ALL_GOOD_IDS.filter(
    (id) =>
      GOODS[id].kind === 'factory' &&
      picked !== null &&
      GOODS[id].required_building === picked.building_type &&
      GOODS[id].unlock_level <= level,
  );

  return (
    <div className="scrim" onClick={onClose}>
      <div onClick={(e) => e.stopPropagation()}>
        <Panel title="Производство" onClose={onClose} style={{ maxWidth: 460, width: '92vw' }}>
          <div style={{ display: 'grid', gap: 14, maxHeight: '58vh', overflowY: 'auto' }}>
            {PURCHASABLE_BUILDINGS.map((type) => (
              <BuildingCard key={type} type={type} onPick={setPicker} />
            ))}
          </div>

          {picked && (
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
                          {good.inputs.length === 0
                            ? 'Без сырья'
                            : good.inputs
                                .map((i) => `${GOODS[i.good_id].name} x${i.qty}`)
                                .join(' + ')}{' '}
                          → {good.price} кр
                        </div>
                      </div>
                      <Button
                        onClick={() => {
                          enqueue(picked.idx, id as GoodId);
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

/**
 * Тосты — единственное место, где игра объясняет отказ словами.
 *
 * Плашка держится на экране 180 мс после того, как стор ее убрал: без этого
 * сообщение исчезало мгновенным пропаданием, а мгновенное пропадание глаз
 * читает как «мигнуло», а не как «ушло». Уход — из [[ux-motion-spec]] раздел 2,
 * ease-in, без перелета.
 */
const TOAST_LEAVE_MS = 180;

export function Toasts() {
  const toasts = useGame((s) => s.toasts);
  const color = { info: 'var(--panel)', warn: '#ffd9c8', reward: '#dff5cf' } as const;

  // Зеркало списка стора: тост, уже удаленный из состояния, доигрывает уход.
  const [leaving, setLeaving] = useState<Toast[]>([]);
  const previous = useRef<Toast[]>(toasts);

  useEffect(() => {
    const gone = previous.current.filter((p) => !toasts.some((t) => t.id === p.id));
    previous.current = toasts;
    if (gone.length === 0) return;

    setLeaving((list) => [...list, ...gone]);
    const timer = setTimeout(() => {
      setLeaving((list) => list.filter((t) => !gone.some((g) => g.id === t.id)));
    }, TOAST_LEAVE_MS);
    return () => clearTimeout(timer);
  }, [toasts]);

  const shown: Array<Toast & { leaving: boolean }> = [
    ...toasts.map((t) => ({ ...t, leaving: false })),
    ...leaving.map((t) => ({ ...t, leaving: true })),
  ];

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
      {shown.map((t) => (
        <div
          key={t.id}
          data-testid={`toast-${t.kind}`}
          className={`panel toast toast-${t.kind}${t.leaving ? ' toast-leaving' : ''}`}
          style={{ background: color[t.kind] }}
        >
          {t.text}
        </div>
      ))}
    </div>
  );
}
