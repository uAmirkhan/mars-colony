/**
 * Балансный симулятор. Гоняет ТУ ЖЕ доменную логику, что и игра, без интерфейса.
 *
 * Важное архитектурное правило: здесь нет ни одного правила игры. Посев, сбор,
 * блокировка переполнением, очередь фабрики, анти-софтлок — все вызывается из
 * `domain/`. В симуляторе живет только ПОЛИТИКА игрока: что сажать, когда
 * продавать, сколько заказов пытаться закрыть. Если правило появится здесь
 * копией — экономика начнет жить в двух местах и разъедется, как уже было
 * с выходом урожая (симулятор клал 1 единицу вместо HARVEST_QTY).
 *
 * Что НЕ смоделировано (день 5+): шаттл и строй-модули, лайнер, помощь
 * союзников, платные ускорения, генератор заказов с инвариантами И-8/И-10.
 * Шаттл платит XP с коэффициентом 8 против 2 у дрона и открывается на ур.5,
 * поэтому темп выше пятого уровня симулятор занижает.
 */

import {
  CREDITS_START,
  DRONE_PREMIUM_RANGE,
  FACTORY_PRICES,
  FACTORY_QUEUE_BASE_SLOTS,
  fieldsAtLevel,
  plantingCost,
  TRANSPORT_XP_K,
} from '../domain/config/economy';
import { ALL_GOOD_IDS, GOODS, slotQuantity } from '../domain/config/goods';
import {
  levelUpReward,
  MAX_LEVEL_MVP,
  MECHANIC_UNLOCK_LEVEL,
  xpToNext,
} from '../domain/config/levels';
import {
  cheapestPlantingCost,
  collectFactory,
  collectField,
  createFactorySlot,
  createField,
  enqueue,
  type FactorySlot,
  type FieldSlot,
  onWarehouseStockIncreased,
  type ProductionContext,
  plant,
  refreshFactorySlot,
  refreshField,
  sell,
} from '../domain/production';
import type { GoodId } from '../domain/types';
import {
  availableOf,
  consume,
  createWarehouse,
  occupiedGoods,
  qtyOf,
  totalQty,
  type WarehouseState,
} from '../domain/warehouse';
import { makeRng, pick } from './rng';

export interface SimConfig {
  days: number;
  /** Минуты начала сессий внутри суток. */
  session_starts_min: number[];
  session_length_min: number;
  /** Сколько заказов дрона игрок пытается закрыть за сессию. */
  orders_per_session: number;
  seed: number;
}

export const DEFAULT_SIM: SimConfig = {
  days: 30,
  session_starts_min: [8 * 60, 13 * 60, 20 * 60],
  session_length_min: 12,
  orders_per_session: 3,
  seed: 20260729,
};

export interface DayRow {
  day: number;
  level: number;
  xp_total: number;
  credits: number;
  isotopes: number;
  orders_done: number;
  /** Сколько раз сбор был отклонен переполнением склада. */
  warehouse_blocks: number;
}

export interface SimResult {
  rows: DayRow[];
  milestones: Record<number, number | null>;
  final_level: number;
  total_hours: number;
  warehouse_blocks: number;
  orders_done: number;
  /** Сколько раз сработал анти-софтлок И-15. Больше нуля = экономика у тупика. */
  softlock_rescues: number;
}

const MILESTONE_LEVELS = [5, 8, 12, 21];
const MINUTE = 60;

export function simulate(config: SimConfig = DEFAULT_SIM): SimResult {
  const rng = makeRng(config.seed);

  let level = 1;
  let xp_total = 0;
  let xp_into_level = 0;
  let credits = CREDITS_START;
  let isotopes = 0;
  let softlock_rescues = 0;
  let current_day = 0;

  const warehouse: WarehouseState = createWarehouse();
  let fields: FieldSlot[] = Array.from({ length: fieldsAtLevel(level) }, (_, i) =>
    createField(i),
  );
  const factory: FactorySlot[] = Array.from({ length: FACTORY_QUEUE_BASE_SLOTS }, (_, i) =>
    createFactorySlot(i, 'food_module'),
  );
  let has_food_module = false;

  const rows: DayRow[] = [];
  const milestones: Record<number, number | null> = {};
  for (const m of MILESTONE_LEVELS) milestones[m] = null;

  let orders_done_total = 0;
  let warehouse_blocks_total = 0;

  /** Контекст для доменных вызовов. Пересобирается каждый шаг — состояние меняется. */
  const ctx = (now: number): ProductionContext => ({ now, warehouse, credits, level });

  const unlockedCrops = (): GoodId[] =>
    ALL_GOOD_IDS.filter((id) => GOODS[id].kind === 'crop' && GOODS[id].unlock_level <= level);

  const unlockedRecipes = (): GoodId[] =>
    has_food_module
      ? ALL_GOOD_IDS.filter(
          (id) =>
            GOODS[id].kind === 'factory' &&
            GOODS[id].required_building === 'food_module' &&
            GOODS[id].unlock_level <= level,
        )
      : [];

  function gainXp(amount: number) {
    xp_total += amount;
    xp_into_level += amount;
    while (level < MAX_LEVEL_MVP && xp_into_level >= xpToNext(level)) {
      xp_into_level -= xpToNext(level);
      level += 1;
      const reward = levelUpReward(level);
      credits += reward.credits;
      isotopes += reward.isotopes;
      if (MILESTONE_LEVELS.includes(level) && milestones[level] === null) {
        milestones[level] = current_day;
      }
      const target = fieldsAtLevel(level);
      while (fields.length < target) fields.push(createField(fields.length));
    }
  }

  for (let day = 1; day <= config.days; day++) {
    current_day = day;
    const day_start_sec = (day - 1) * 24 * 60 * MINUTE;
    let orders_today = 0;
    let blocks_today = 0;

    for (const start_min of config.session_starts_min) {
      const session_start = day_start_sec + start_min * MINUTE;

      // Сессия проживается по минутам: быстрые культуры снимаются несколько раз
      // за один заход, и это заметно меняет ранний темп.
      for (let t = 0; t <= config.session_length_min; t++) {
        const now = session_start + t * MINUTE;

        fields = fields.map((f) => refreshField(f, now));
        for (const slot of factory) refreshFactorySlot(slot, now);

        // 1. Сбор урожая. Домен сам решает, проходит ли сбор при переполнении.
        for (const field of fields) {
          if (field.state !== 'READY') continue;
          const result = collectField(field, ctx(now));
          if (result.ok) {
            gainXp(result.xp_gained ?? 0);
            onWarehouseStockIncreased(factory, ctx(now));
          } else if (result.reason === 'warehouse_full') {
            blocks_today += 1;
          }
        }

        // 2. Сбор с фабрики — та же логика, тот же домен.
        for (const slot of factory) {
          if (slot.state !== 'READY') continue;
          const result = collectFactory(slot, ctx(now));
          if (result.ok) {
            gainXp(result.xp_gained ?? 0);
            onWarehouseStockIncreased(factory, ctx(now));
          } else if (result.reason === 'warehouse_full') {
            blocks_today += 1;
          }
        }

        // 3. Политика продажи: держим запас под рецепты, остальное продаем,
        //    когда нужны кредиты на посев или когда склад близок к пределу.
        const replant_budget = fields.length * 4;
        // близко к пределу
        const near_full = totalQty(warehouse) > warehouse.capacity * 0.8;
        if (credits < replant_budget || near_full) {
          const keep = has_food_module ? 6 : 2;
          for (const id of occupiedGoods(warehouse)) {
            const surplus = Math.min(availableOf(warehouse, id), qtyOf(warehouse, id) - keep);
            if (surplus <= 0) continue;
            const result = sell(id, surplus, ctx(now));
            if (result.ok) credits += result.credits_delta ?? 0;
          }
        }

        // 4. Покупка пищевого модуля, как только хватает кредитов.
        if (
          !has_food_module &&
          level >= FACTORY_PRICES.food_module.unlock_level &&
          credits >= FACTORY_PRICES.food_module.first
        ) {
          credits -= FACTORY_PRICES.food_module.first;
          has_food_module = true;
        }

        // 5. Загрузка фабрики: самый дорогой доступный рецепт.
        const recipes = unlockedRecipes().sort((a, b) => GOODS[b].price - GOODS[a].price);
        for (const slot of factory) {
          if (slot.state !== 'EMPTY' || recipes.length === 0) continue;
          const affordable = recipes.find((id) =>
            GOODS[id].inputs.every((inp) => availableOf(warehouse, inp.good_id) >= inp.qty),
          );
          if (!affordable) break;
          enqueue(slot, affordable, ctx(now));
        }

        // 6. Посев. Политика: самая доходная культура, на которую хватает кредитов.
        const crops = unlockedCrops().sort((a, b) => GOODS[b].price - GOODS[a].price);
        for (const field of fields) {
          if (field.state !== 'EMPTY') continue;
          const choice =
            crops.find((id) => credits >= plantingCost(GOODS[id].price)) ??
            // Кредитов не хватает ни на что: пробуем самую дешевую — домен решит,
            // тупик это (И-15, посев бесплатен) или обычный отказ.
            crops.find((id) => plantingCost(GOODS[id].price) === cheapestPlantingCost(level));
          if (!choice) break;

          const result = plant(field, choice, ctx(now), fields);
          if (!result.ok) break;
          credits += result.credits_delta ?? 0;
          if (result.softlock_rescued) softlock_rescues += 1;
        }
      }

      // 7. Заказы дрона в конце сессии, когда склад наполнен урожаем.
      if (level >= MECHANIC_UNLOCK_LEVEL.drone) {
        const now = session_start + config.session_length_min * MINUTE;
        for (let i = 0; i < config.orders_per_session; i++) {
          const pool = [...unlockedCrops(), ...unlockedRecipes()];
          if (pool.length === 0) break;

          const slot_count = 1 + Math.floor(rng() * 2);
          const wanted: Array<{ id: GoodId; qty: number }> = [];
          for (let s = 0; s < slot_count; s++) {
            const id = pick(rng, pool);
            wanted.push({ id, qty: slotQuantity(id, 'drone', level, rng()) });
          }
          if (!wanted.every((w) => availableOf(warehouse, w.id) >= w.qty)) continue;

          let payout = 0;
          let xp = 0;
          let all_shipped = true;
          for (const w of wanted) {
            const good = GOODS[w.id];
            const premium =
              DRONE_PREMIUM_RANGE.min +
              rng() * (DRONE_PREMIUM_RANGE.max - DRONE_PREMIUM_RANGE.min);
            payout += good.price * w.qty * (1 + premium);
            xp += good.base_xp * TRANSPORT_XP_K.drone * w.qty;
            // Отправка заказа физически убирает товар со склада, но кредиты
            // платит не рынок, а заказ — поэтому consume, а не sell.
            if (!consume(warehouse, w.id, w.qty)) all_shipped = false;
          }
          if (!all_shipped) continue;

          credits += Math.round(payout);
          gainXp(xp);
          orders_today += 1;
          onWarehouseStockIncreased(factory, ctx(now));
        }
      }
    }

    orders_done_total += orders_today;
    warehouse_blocks_total += blocks_today;

    rows.push({
      day,
      level,
      xp_total,
      credits,
      isotopes,
      orders_done: orders_today,
      warehouse_blocks: blocks_today,
    });
  }

  return {
    rows,
    milestones,
    final_level: level,
    total_hours:
      (config.days * config.session_starts_min.length * config.session_length_min) / 60,
    warehouse_blocks: warehouse_blocks_total,
    orders_done: orders_done_total,
    softlock_rescues,
  };
}
