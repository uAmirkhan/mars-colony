/**
 * Zustand-стор — тонкая обертка над доменом. Здесь нет ни одного правила игры
 * и ни одного числа: только хранение состояния и вызовы доменных функций.
 * Как только тут появится число, конфиг перестанет быть источником истины.
 */

import { create } from 'zustand';
import {
  CREDITS_START,
  FACTORY_PRICES,
  FACTORY_QUEUE_BASE_SLOTS,
  fieldsAtLevel,
} from '../domain/config/economy';
import { levelUpReward, MAX_LEVEL_MVP, xpToNext } from '../domain/config/levels';
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

    tick: (now) => {
      const s = get();
      const fields = s.fields.map((f) => refreshField({ ...f }, now));
      const factory_slots = s.factory_slots.map((sl) => refreshFactorySlot({ ...sl }, now));
      set({ now, fields, factory_slots });
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
