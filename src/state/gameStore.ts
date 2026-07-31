/**
 * Zustand-стор — тонкая обертка над доменом. Здесь нет ни одного правила игры
 * и ни одного числа: только хранение состояния и вызовы доменных функций.
 * Как только тут появится число, конфиг перестанет быть источником истины.
 */

import { create } from 'zustand';
import {
  CREDITS_START,
  droneRefreshPrice,
  FACTORY_PRICES,
  FACTORY_QUEUE_BASE_SLOTS,
  fieldsAtLevel,
  productionSpeedupCost,
} from '../domain/config/economy';
import { GOODS } from '../domain/config/goods';
import {
  levelUpReward,
  MAX_LEVEL_MVP,
  MECHANIC_UNLOCK_LEVEL,
  xpToNext,
} from '../domain/config/levels';
import {
  availableGoodsFor,
  discardOrder,
  generateOrder,
  loadPosition,
  type OrderSlot,
  releaseReserved,
  sendOrder,
  slotsAtLevel,
} from '../domain/drone';
import {
  createFactorySlot,
  createField,
  collectFactory as domainCollectFactory,
  collectField as domainCollectField,
  enqueue as domainEnqueue,
  plant as domainPlant,
  sell as domainSell,
  type FactorySlot,
  type FieldSlot,
  onWarehouseStockIncreased,
  type ProductionContext,
  refreshFactorySlot,
  refreshField,
} from '../domain/production';
import type { GoodId } from '../domain/types';
import { createWarehouse, totalQty, type WarehouseState } from '../domain/warehouse';

export interface Toast {
  id: number;
  text: string;
  kind: 'info' | 'warn' | 'reward';
}

interface GameState {
  now: number;
  level: number;
  xp_into_level: number;
  credits: number;
  isotopes: number;
  warehouse: WarehouseState;
  fields: FieldSlot[];
  factory_slots: FactorySlot[];
  has_food_module: boolean;
  toasts: Toast[];

  tick: (now: number) => void;
  plant: (idx: number, good_id: GoodId) => void;
  collectField: (idx: number) => void;
  enqueue: (idx: number, good_id: GoodId) => void;
  collectFactory: (idx: number) => void;
  sell: (good_id: GoodId, qty: number) => void;
  speedupField: (idx: number) => void;
  speedupFactory: (idx: number) => void;
  orders: OrderSlot[];
  loadOrderPosition: (slot_idx: number, position_idx: number) => void;
  sendOrderAt: (slot_idx: number) => void;
  discardOrderAt: (slot_idx: number) => void;
  refreshSlotNow: (slot_idx: number) => void;
  buyFoodModule: () => void;
  dismissToast: (id: number) => void;
}

let toast_seq = 0;

export const useGame = create<GameState>((set, get) => {
  const ctx = (): ProductionContext => {
    const s = get();
    return { now: s.now, warehouse: s.warehouse, credits: s.credits, level: s.level };
  };

  const pushToast = (text: string, kind: Toast['kind'] = 'info') => {
    const toast = { id: ++toast_seq, text, kind };
    set((s) => ({ toasts: [...s.toasts, toast] }));
    setTimeout(() => get().dismissToast(toast.id), 2600);
  };

  /** Начисление XP с обработкой левелапов. Награда берется из конфига, не отсюда. */
  const applyXp = (amount: number) => {
    const s = get();
    let level = s.level;
    let xp = s.xp_into_level + amount;
    let credits = s.credits;
    let isotopes = s.isotopes;
    let fields = s.fields;

    while (level < MAX_LEVEL_MVP && xp >= xpToNext(level)) {
      xp -= xpToNext(level);
      level += 1;
      const reward = levelUpReward(level);
      credits += reward.credits;
      isotopes += reward.isotopes;
      pushToast(
        `Уровень ${level}! +${reward.credits} кр, +${reward.isotopes} изотопов`,
        'reward',
      );

      const target = fieldsAtLevel(level);
      if (fields.length < target) {
        fields = [...fields];
        while (fields.length < target) fields.push(createField(fields.length));
        pushToast('Открыта новая грядка', 'reward');
      }
    }

    set({ level, xp_into_level: xp, credits, isotopes, fields });
  };

  /** Новый заказ в слот. Пул товаров — то, что игрок реально умеет производить. */
  const makeOrder = (idx: number, now: number): OrderSlot => {
    const s = get();
    const buildings = new Set<string>(s.has_food_module ? ['food_module'] : []);
    const order = generateOrder(idx, {
      level: s.level,
      warehouse: s.warehouse,
      available_goods: availableGoodsFor(s.level, buildings),
      board: s.orders,
      rng: Math.random,
    });
    order.refresh_at = now;
    return order;
  };

  /** Пополнение склада будит слоты фабрики, ждущие входов. */
  const notifyStockIncreased = () => {
    const slots = [...get().factory_slots];
    const started = onWarehouseStockIncreased(slots, ctx());
    if (started > 0) set({ factory_slots: slots });
  };

  return {
    now: Math.floor(Date.now() / 1000),
    level: 1,
    xp_into_level: 0,
    credits: CREDITS_START,
    isotopes: 0,
    warehouse: createWarehouse(),
    fields: Array.from({ length: fieldsAtLevel(1) }, (_, i) => createField(i)),
    factory_slots: Array.from({ length: FACTORY_QUEUE_BASE_SLOTS }, (_, i) =>
      createFactorySlot(i, 'food_module'),
    ),
    has_food_module: false,
    toasts: [],
    orders: [],

    tick: (now) => {
      const s = get();
      const fields = s.fields.map((f) => refreshField({ ...f }, now));
      const factory_slots = s.factory_slots.map((sl) => refreshFactorySlot({ ...sl }, now));

      // Доска наполняется лениво, по тику: слот с истекшим таймером получает
      // новый заказ, а недостающие слоты (после левелапа) — свои первые.
      let orders = s.orders;
      if (s.level >= MECHANIC_UNLOCK_LEVEL.drone) {
        const target = slotsAtLevel(s.level);
        orders = [...orders];
        while (orders.length < target) orders.push(makeOrder(orders.length, now));
        orders = orders.map((slot) =>
          slot.state === 'empty_cooldown' && now >= slot.refresh_at
            ? makeOrder(slot.idx, now)
            : slot,
        );
      }

      set({ now, fields, factory_slots, orders });
    },

    /** Погрузка позиции: резерв со склада, состояние слота пересчитывается доменом. */
    loadOrderPosition: (slot_idx, position_idx) => {
      const orders = get().orders.map((o) => ({
        ...o,
        positions: o.positions.map((p) => ({ ...p })),
      }));
      const slot = orders[slot_idx];
      if (!slot) return;

      if (!loadPosition(slot, position_idx, get().warehouse)) {
        pushToast('Не хватает товара на складе', 'warn');
        return;
      }
      set({ orders, warehouse: { ...get().warehouse } });
    },

    sendOrderAt: (slot_idx) => {
      const s = get();
      const orders = s.orders.map((o) => ({ ...o }));
      const slot = orders[slot_idx];
      if (!slot) return;

      const result = sendOrder(slot, s.warehouse);
      if (!result.ok) return;

      // Слот сразу уходит в новый заказ: у отправки нет таймера, платой за
      // скорость служит сам заказ, а не ожидание.
      orders[slot_idx] = makeOrder(slot_idx, s.now);
      set({ orders, warehouse: { ...s.warehouse }, credits: s.credits + result.credits });
      applyXp(result.xp);
      pushToast(`Дрон улетел: +${result.credits} кр, +${result.xp} XP`, 'reward');
    },

    discardOrderAt: (slot_idx) => {
      const s = get();
      const orders = s.orders.map((o) => ({
        ...o,
        positions: o.positions.map((p) => ({ ...p })),
      }));
      const slot = orders[slot_idx];
      if (!slot) return;

      // Порядок важен: сначала вернуть резерв, потом гасить слот. Иначе товар
      // остался бы заперт навсегда — заказа уже нет, а резерв на нем висит.
      releaseReserved(slot, s.warehouse);
      discardOrder(slot, s.now);
      set({ orders, warehouse: { ...s.warehouse } });
    },

    refreshSlotNow: (slot_idx) => {
      const s = get();
      const orders = s.orders.map((o) => ({ ...o }));
      const slot = orders[slot_idx];
      if (slot?.state !== 'empty_cooldown') return;

      const price = droneRefreshPrice(slot.refresh_at - s.now);
      if (price > s.isotopes) {
        pushToast(`Нужно ${price} изотопов`, 'warn');
        return;
      }
      orders[slot_idx] = makeOrder(slot_idx, s.now);
      set({ orders, isotopes: s.isotopes - price });
    },

    plant: (idx, good_id) => {
      const fields = get().fields.map((f) => ({ ...f }));
      const field = fields[idx];
      if (!field) return; // тап по несуществующей грядке — не ошибка, просто ничего
      const result = domainPlant(field, good_id, ctx(), fields);
      if (!result.ok) {
        if (result.reason === 'insufficient_balance') pushToast('Не хватает кредитов', 'warn');
        return;
      }
      set((s) => ({ fields, credits: s.credits + (result.credits_delta ?? 0) }));
      if (result.softlock_rescued) pushToast('Посев за счет колонии — запас пуст', 'info');
    },

    collectField: (idx) => {
      const fields = get().fields.map((f) => ({ ...f }));
      const field = fields[idx];
      if (!field) return;
      const result = domainCollectField(field, ctx());
      if (!result.ok) {
        if (result.reason === 'warehouse_full')
          pushToast('Склад полон — продай излишки', 'warn');
        return;
      }
      set({ fields, warehouse: { ...get().warehouse } });
      applyXp(result.xp_gained ?? 0);
      notifyStockIncreased();
    },

    enqueue: (idx, good_id) => {
      const slots = get().factory_slots.map((s) => ({ ...s }));
      const slot = slots[idx];
      if (!slot) return;
      const result = domainEnqueue(slot, good_id, ctx());
      if (!result.ok) return;
      set({ factory_slots: slots, warehouse: { ...get().warehouse } });
      if (result.reason === 'no_inputs') pushToast('Ждет сырье на складе', 'info');
    },

    collectFactory: (idx) => {
      const slots = get().factory_slots.map((s) => ({ ...s }));
      const slot = slots[idx];
      if (!slot) return;
      const result = domainCollectFactory(slot, ctx());
      if (!result.ok) {
        if (result.reason === 'warehouse_full')
          pushToast('Склад полон — продай излишки', 'warn');
        return;
      }
      set({ factory_slots: slots, warehouse: { ...get().warehouse } });
      applyXp(result.xp_gained ?? 0);
      notifyStockIncreased();
    },

    sell: (good_id, qty) => {
      const result = domainSell(good_id, qty, ctx());
      if (!result.ok) return;
      set((s) => ({
        credits: s.credits + (result.credits_delta ?? 0),
        warehouse: { ...s.warehouse },
      }));
    },

    /**
     * Ускорение грядки. Ниже порога бесплатно (AC7) — кнопка не исчезает,
     * а отдает действие даром: игрок, который уже тянулся к ней, получает
     * результат, и заодно видит механику, если пользуется ей впервые.
     */
    speedupField: (idx) => {
      const s = get();
      const fields = s.fields.map((f) => ({ ...f }));
      const field = fields[idx];
      if (field?.state !== 'GROWING') return;

      const good = field.good_id ? GOODS[field.good_id] : null;
      if (!good) return;

      const price = productionSpeedupCost(field.ends_at - s.now, good.kind);
      if (price > s.isotopes) {
        pushToast(`Нужно ${price} изотопов`, 'warn');
        return;
      }

      field.ends_at = s.now; // цикл завершен, собирать игрок будет сам
      field.state = 'READY';
      set({ fields, isotopes: s.isotopes - price });
      if (price === 0) pushToast('Дозрело', 'info');
    },

    speedupFactory: (idx) => {
      const s = get();
      const slots = s.factory_slots.map((sl) => ({ ...sl }));
      const slot = slots[idx];
      if (slot?.state !== 'PRODUCING' || !slot.good_id) return;

      const price = productionSpeedupCost(slot.ends_at - s.now, 'factory');
      if (price > s.isotopes) {
        pushToast(`Нужно ${price} изотопов`, 'warn');
        return;
      }

      slot.ends_at = s.now;
      slot.state = 'READY';
      set({ factory_slots: slots, isotopes: s.isotopes - price });
      if (price === 0) pushToast('Готово', 'info');
    },

    buyFoodModule: () => {
      const s = get();
      const price = FACTORY_PRICES.food_module.first;
      if (s.level < FACTORY_PRICES.food_module.unlock_level) return;
      if (s.credits < price) {
        pushToast(`Нужно ${price} кредитов`, 'warn');
        return;
      }
      set({ credits: s.credits - price, has_food_module: true });
      pushToast('Пищевой модуль построен', 'reward');
    },

    dismissToast: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
  };
});

/** Селекторы для UI, чтобы компоненты не считали ничего сами. */
export const selectWarehouseLoad = (s: GameState) => ({
  used: totalQty(s.warehouse),
  cap: s.warehouse.capacity,
});

export const selectXpProgress = (s: GameState) => ({
  into: s.xp_into_level,
  need: xpToNext(s.level),
});
