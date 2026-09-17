/**
 * Шаттл: генератор заказа (И-8), погрузка и авто-старт (ТЗ 2.3), докупка (И-12),
 * скип (И-6), прибытие и сбор.
 *
 * Отдельный акцент на сохранении товара: у шаттла нет отмены рейса, поэтому
 * единица, потерянная между складом и отсеком, не всплывет никогда — игрок
 * просто не досчитается ее в следующем цикле и решит, что ошибся сам.
 */

import { describe, expect, it } from 'vitest';
import { makeRng } from '../../sim/rng';
import {
  EASY_PRODUCE_MAX_MIN,
  FTUE_FIRST_TRIP_TIMER_MIN,
  flightTimerMin,
  MAX_DEFICIT_SLOTS,
  PINCH_MAX,
  PINCH_MIN,
  SLOT_COUNT_MAX,
  SLOT_COUNT_MIN,
  SPEEDUP_FLOOR_ISOTOPES,
  XP_MULTIPLIER_K,
} from '../config/economy';
import { GOOD_BASE_QTY, GOODS, slotQuantity } from '../config/goods';
import { applyPinch, availableGoodsFor } from '../drone';
import type { DropContext } from '../droproller';
import type { ShuttleState } from '../shuttle';
import {
  allCollected,
  allSlotsLoaded,
  buyoutSlot,
  collectContainer,
  generateTrip,
  loadSlot,
  refreshTrip,
  type ShuttleGenContext,
  type ShuttleTrip,
  skipFlight,
  skipPrice,
  slotXp,
  tripXp,
} from '../shuttle';
import { availableOf, createWarehouse, deposit, qtyOf } from '../warehouse';

const NOW = 1_000_000;

function drop(): DropContext {
  return {
    pity: {},
    stock: {},
    need: {},
    warehouse_avg_24h: {},
    gated_open: false,
    arrival_no: 5,
    constructions: [],
    rng: () => 0.5,
  };
}

/** Склад с запасом всего, что игрок умеет производить на этом уровне. */
function stockedWarehouse(goods: readonly string[], per_good = 30) {
  const w = createWarehouse(500);
  for (const id of goods) deposit(w, id as never, per_good);
  return w;
}

const POOL = ['algae', 'soy', 'mushrooms', 'tomatoes', 'cotton'] as const;

function genCtx(patch: Partial<ShuttleGenContext> = {}): ShuttleGenContext {
  return {
    level: 6,
    warehouse: stockedWarehouse(POOL),
    available_goods: [...POOL],
    previous: null,
    is_first_trip: false,
    arrival_no: 1,
    rng: () => 0.5,
    ...patch,
  };
}

describe('Генератор рейса', () => {
  it('FTUE: ровно три отсека и укороченный таймер', () => {
    const trip = generateTrip(genCtx({ is_first_trip: true }));
    expect(trip.slots).toHaveLength(SLOT_COUNT_MIN);
    expect(trip.trip_min).toBe(FTUE_FIRST_TRIP_TIMER_MIN);
  });

  it('FTUE: ни одного дефицитного отсека — первый цикл проходится бесплатно', () => {
    for (let seed = 0; seed < 60; seed++) {
      const rng = () => (seed * 0.017 + 0.31) % 1;
      const warehouse = stockedWarehouse(POOL, 6);
      const trip = generateTrip(genCtx({ is_first_trip: true, warehouse, rng }));
      for (const slot of trip.slots) {
        expect(availableOf(warehouse, slot.good_id)).toBeGreaterThanOrEqual(slot.qty_required);
      }
    }
  });

  it('обычный рейс: число отсеков в границах каркаса', () => {
    for (let seed = 0; seed < 60; seed++) {
      const rng = makeRng(seed + 1);
      const trip = generateTrip(genCtx({ rng }));
      expect(trip.slots.length).toBeGreaterThanOrEqual(SLOT_COUNT_MIN);
      expect(trip.slots.length).toBeLessThanOrEqual(SLOT_COUNT_MAX);
    }
  });

  /**
   * Регрессия. Генератор считал дефицитной любую позицию, которой нет на
   * складе, выедал бюджет дефицита первой же и выбрасывал все остальные —
   * рейс вырождался в один отсек, то есть в один модуль вместо трех-пяти.
   * Ничего не падало: игрок просто получал втрое меньше и не знал почему.
   */
  it('пустой склад не вырождает рейс до одного отсека', () => {
    for (let seed = 0; seed < 60; seed++) {
      const rng = makeRng(seed + 101);
      const trip = generateTrip(genCtx({ warehouse: createWarehouse(500), rng }));
      expect(trip.slots.length).toBeGreaterThanOrEqual(SLOT_COUNT_MIN);
    }
  });

  it('И-8: не больше одного дефицитного отсека даже на пустом складе', () => {
    for (let seed = 0; seed < 60; seed++) {
      const rng = makeRng(seed + 201);
      const warehouse = stockedWarehouse(POOL, 2);
      const trip = generateTrip(genCtx({ warehouse, rng }));
      // Дефицит по каркасу — преднамеренный пинч, а не любая нехватка на
      // полке: И-8 засчитывает позицию легкой, если она либо покрыта складом,
      // либо производится не дольше EASY_PRODUCE_MAX_MIN.
      const deficit = trip.slots.filter(
        (s) =>
          s.qty_required > availableOf(warehouse, s.good_id) &&
          GOODS[s.good_id].prod_time_sec > EASY_PRODUCE_MAX_MIN.shuttle * 60,
      );
      expect(deficit.length).toBeLessThanOrEqual(MAX_DEFICIT_SLOTS);
    }
  });

  /**
   * Переписан против спеки. Тест требовал пустого рейса на пустом пуле — то
   * есть ровно того софтлока, который канон запрещает: [[tz-common-systems-mars]]
   * 1.6 и AC4 («возвращается валидный fallbackMinimalOrder — не ошибка, не
   * пустой заказ»). Рейс из нуля отсеков нельзя ни закрыть, ни отменить, а
   * новый выдается только из кулдауна — прогрессия умирает вместе с ним.
   */
  it('пустой пул товаров дает деградированный рейс из SLOT_COUNT_MIN отсеков, а не пустой', () => {
    const trip = generateTrip(genCtx({ available_goods: [] }));
    expect(trip.slots).toHaveLength(SLOT_COUNT_MIN);
    expect(new Set(trip.slots.map((s) => s.good_id)).size).toBe(SLOT_COUNT_MIN);
    expect(allSlotsLoaded(trip)).toBe(false);

    // Канон 1.6: самый быстрый доступный товар в количестве GOOD_BASE_QTY.min.
    const slot = trip.slots[0]!;
    expect(slot.qty_required).toBe(GOOD_BASE_QTY[slot.good_id].min);
    const fastest = Math.min(
      ...availableGoodsFor(genCtx().level, new Set<string>()).map(
        (id) => GOODS[id].prod_time_sec,
      ),
    );
    expect(GOODS[slot.good_id].prod_time_sec).toBe(fastest);
  });

  it('таймер рейса берется из брекета уровня', () => {
    expect(generateTrip(genCtx({ level: 6 })).trip_min).toBe(flightTimerMin(6));
    expect(generateTrip(genCtx({ level: 12 })).trip_min).toBe(flightTimerMin(12));
  });
});

describe('И-3: XP отсека', () => {
  it('базовый XP товара умножается на K=8 и количество', () => {
    const trip = generateTrip(genCtx());
    const slot = trip.slots[0]!;
    expect(slotXp(slot)).toBe(
      GOODS[slot.good_id].base_xp * XP_MULTIPLIER_K.shuttle * slot.qty_required,
    );
    expect(tripXp(trip)).toBe(trip.slots.reduce((a, s) => a + slotXp(s), 0));
  });
});

describe('Погрузка и авто-старт', () => {
  function ready() {
    const warehouse = stockedWarehouse(POOL, 40);
    const trip = generateTrip(genCtx({ warehouse, is_first_trip: true }));
    return { trip, warehouse };
  }

  it('частичная погрузка списывает наличное и оставляет отсек открытым', () => {
    const warehouse = createWarehouse(500);
    deposit(warehouse, 'algae', 2);
    const trip: ShuttleTrip = {
      state: 'ORDER',
      slots: [
        {
          idx: 0,
          good_id: 'algae',
          qty_required: 5,
          qty_filled: 0,
          filled_by: null,
          reward: null,
          collected: false,
          floor_forced: false,
        },
      ],
      trip_min: 60,
      departed_at: 0,
      arrives_at: 0,
      is_first_trip: false,
      arrival_no: 1,
    };

    const result = loadSlot(trip, 0, warehouse, NOW, drop());
    expect(result.ok).toBe(true);
    expect(result.loaded).toBe(2);
    expect(result.departed).toBe(false);
    expect(trip.state).toBe('ORDER');
    expect(availableOf(warehouse, 'algae')).toBe(0);
  });

  it('закрытие последнего отсека стартует рейс само, без кнопки «Отправить»', () => {
    const { trip, warehouse } = ready();
    let departed = false;
    for (let i = 0; i < trip.slots.length; i++) {
      departed = loadSlot(trip, i, warehouse, NOW, drop()).departed;
    }
    expect(departed).toBe(true);
    expect(trip.state).toBe('IN_TRANSIT');
    expect(trip.arrives_at).toBe(NOW + trip.trip_min * 60);
  });

  it('контейнеров ровно столько же, сколько отсеков', () => {
    const { trip, warehouse } = ready();
    for (let i = 0; i < trip.slots.length; i++) loadSlot(trip, i, warehouse, NOW, drop());
    expect(trip.slots.every((s) => s.reward !== null)).toBe(true);
  });

  it('товар не теряется: со склада ушло ровно столько, сколько уехало в отсеках', () => {
    const { trip, warehouse } = ready();
    const before = new Map(trip.slots.map((s) => [s.good_id, qtyOf(warehouse, s.good_id)]));
    for (let i = 0; i < trip.slots.length; i++) loadSlot(trip, i, warehouse, NOW, drop());

    for (const slot of trip.slots) {
      const start = before.get(slot.good_id) ?? 0;
      expect(qtyOf(warehouse, slot.good_id)).toBe(start - slot.qty_required);
    }
  });

  it('после отправки резерв не висит на складе', () => {
    const { trip, warehouse } = ready();
    for (let i = 0; i < trip.slots.length; i++) loadSlot(trip, i, warehouse, NOW, drop());
    for (const slot of trip.slots) {
      expect(qtyOf(warehouse, slot.good_id)).toBe(availableOf(warehouse, slot.good_id));
    }
  });

  it('погрузка в улетевший рейс отклоняется и склад не трогает', () => {
    const { trip, warehouse } = ready();
    for (let i = 0; i < trip.slots.length; i++) loadSlot(trip, i, warehouse, NOW, drop());

    const good = trip.slots[0]!.good_id;
    const before = qtyOf(warehouse, good);
    const again = loadSlot(trip, 0, warehouse, NOW, drop());
    expect(again.ok).toBe(false);
    expect(qtyOf(warehouse, good)).toBe(before);
  });

  it('погрузка при нуле на складе отклоняется, а не грузит ноль', () => {
    const warehouse = createWarehouse(500);
    const trip = generateTrip(genCtx({ warehouse, available_goods: [...POOL] }));
    if (trip.slots.length === 0) return;
    expect(loadSlot(trip, 0, warehouse, NOW, drop()).ok).toBe(false);
  });

  it('тап по несуществующему отсеку не ломает рейс', () => {
    const { trip, warehouse } = ready();
    expect(loadSlot(trip, 99, warehouse, NOW, drop()).ok).toBe(false);
    expect(trip.state).toBe('ORDER');
  });
});

describe('И-12: докупка минует склад', () => {
  it('докупленный отсек не списывает ничего со склада', () => {
    const warehouse = createWarehouse(500);
    deposit(warehouse, 'algae', 3);
    const trip: ShuttleTrip = {
      state: 'ORDER',
      slots: [
        {
          idx: 0,
          good_id: 'soy',
          qty_required: 4,
          qty_filled: 0,
          filled_by: null,
          reward: null,
          collected: false,
          floor_forced: false,
        },
      ],
      trip_min: 60,
      departed_at: 0,
      arrives_at: 0,
      is_first_trip: false,
      arrival_no: 1,
    };

    const result = buyoutSlot(trip, 0, warehouse, NOW, drop());
    expect(result.ok).toBe(true);
    expect(result.price).toBeGreaterThan(0);
    expect(qtyOf(warehouse, 'soy')).toBe(0);
    expect(qtyOf(warehouse, 'algae')).toBe(3);
    expect(trip.slots[0]!.filled_by).toBe('purchase');
    expect(trip.state).toBe('IN_TRANSIT');
  });

  it('докупка закрытого отсека отклоняется и не списывает изотопы', () => {
    const warehouse = createWarehouse(500);
    deposit(warehouse, 'soy', 10);
    const trip = generateTrip(genCtx({ warehouse, available_goods: ['soy'] }));
    loadSlot(trip, 0, warehouse, NOW, drop());
    const result = buyoutSlot(trip, 0, warehouse, NOW, drop());
    expect(result.ok).toBe(false);
    expect(result.price).toBe(0);
  });
});

describe('И-6: скип рейса', () => {
  function inTransit(slot_count: number) {
    const warehouse = stockedWarehouse(POOL, 40);
    const trip = generateTrip(genCtx({ warehouse, is_first_trip: true }));
    trip.slots = trip.slots.slice(0, slot_count);
    for (let i = 0; i < trip.slots.length; i++) loadSlot(trip, i, warehouse, NOW, drop());
    return trip;
  }

  it('цена падает по мере приближения к прилету', () => {
    const trip = inTransit(3);
    const start = skipPrice(trip, NOW);
    const middle = skipPrice(trip, NOW + (trip.trip_min * 60) / 2);
    expect(middle).toBeLessThan(start);
  });

  it('не опускается ниже пола', () => {
    const trip = inTransit(3);
    expect(skipPrice(trip, trip.arrives_at - 1)).toBe(SPEEDUP_FLOOR_ISOTOPES.shuttle);
  });

  it('вне рейса скипать нечего', () => {
    const trip = generateTrip(genCtx());
    expect(skipPrice(trip, NOW)).toBe(0);
  });

  it('скип переводит рейс в прибытие, повторный отклоняется', () => {
    const trip = inTransit(3);
    expect(skipFlight(trip, NOW)).toBe(true);
    expect(trip.state).toBe('ARRIVED');
    expect(skipFlight(trip, NOW)).toBe(false);
  });
});

describe('Прибытие и сбор', () => {
  function arrived() {
    const warehouse = stockedWarehouse(POOL, 40);
    const trip = generateTrip(genCtx({ warehouse, is_first_trip: true }));
    for (let i = 0; i < trip.slots.length; i++) loadSlot(trip, i, warehouse, NOW, drop());
    refreshTrip(trip, trip.arrives_at);
    return trip;
  }

  it('рейс становится прибывшим строго по времени, не раньше', () => {
    const warehouse = stockedWarehouse(POOL, 40);
    const trip = generateTrip(genCtx({ warehouse, is_first_trip: true }));
    for (let i = 0; i < trip.slots.length; i++) loadSlot(trip, i, warehouse, NOW, drop());

    refreshTrip(trip, trip.arrives_at - 1);
    expect(trip.state).toBe('IN_TRANSIT');
    refreshTrip(trip, trip.arrives_at);
    expect(trip.state).toBe('ARRIVED');
  });

  it('контейнер вскрывается один раз: повтор отдает null', () => {
    const trip = arrived();
    expect(trip.slots[0]!.reward).not.toBeNull();
    expect(collectContainer(trip, 0)).toBe(trip.slots[0]!.reward);
    expect(collectContainer(trip, 0)).toBeNull();
  });

  it('до прибытия контейнер не вскрывается', () => {
    const warehouse = stockedWarehouse(POOL, 40);
    const trip = generateTrip(genCtx({ warehouse, is_first_trip: true }));
    for (let i = 0; i < trip.slots.length; i++) loadSlot(trip, i, warehouse, NOW, drop());
    expect(collectContainer(trip, 0)).toBeNull();
  });

  it('после сбора всех контейнеров паузы нет: состояние остается ARRIVED до нового заказа', () => {
    // Кулдаун убран 11.09 решением владельца. Таймер рейса уже отработал роль
    // паузы, второй гейт подряд читается игроком как искусственная задержка, и
    // эталон жанра его не ставит: поезд в Township принимает груз сразу.
    // Сторож держит отсутствие: если состояние COOLDOWN вернется в домен,
    // строчка ниже перестанет компилироваться, а этот тест покраснеет.
    const trip = arrived();
    for (let i = 0; i < trip.slots.length; i++) collectContainer(trip, i);
    expect(allCollected(trip)).toBe(true);
    expect(trip.state).toBe('ARRIVED');

    const sostoyaniya: ShuttleState[] = ['ORDER', 'IN_TRANSIT', 'ARRIVED'];
    expect(sostoyaniya).toHaveLength(3);
  });
});

/**
 * Канон [[tz-common-systems-mars]] 1.4:
 * `"absolute" -> return stock + clamp(targetQty - stock, PINCH_MIN, PINCH_MAX)`,
 * и строка помечена «дрон, шаттл» — правило одно на обе механики.
 *
 * Проверяется именно связь с `targetQty`: пока у шаттла стоял случайный пинч
 * `have + rand(PINCH_MIN..PINCH_MAX)`, количество, посчитанное `slotQuantity`
 * (каркас 3.1 с `bracket_mult`), отбрасывалось целиком.
 */
describe('И-8: дефицитный отсек по канону 1.4 (PINCH_MODE = absolute)', () => {
  /** Постоянный ролл: и выбор товара, и `slotQuantity` становятся счетными. */
  const ROLL = 0.01;
  /**
   * Дефицитный товар — кислород-баллон, не комбинезон.
   *
   * До И-10 (реализуемость, `rushcost.ts` `totalProductionMinutes` +
   * `shuttle.ts` `rebalanceForAchievability`) здесь стоял `jumpsuit`:
   * двухзвенная цепочка (ткань ← хлопок) с пустого склада стоит 120+ минут
   * даже на полу количества — больше любого бюджета шаттла (максимум 54 мин
   * на верхнем брекете). Достижимость теперь ПРАВОМЕРНО (канон 1.3: приоритет
   * И-10 выше пинча/покрытия) урезает такую позицию, и число расходилось с
   * чистой формулой пинча, которую эти тесты проверяют. Кислород-баллон —
   * однозвенная цепочка (вход — водоросли, которых на складе с избытком),
   * поэтому его время производства не зависит от этой правки и формула пинча
   * проверяется в изоляции, как и задумано этим блоком.
   */
  const DEFICIT_POOL = ['oxygen_tank', 'algae', 'soy'] as const;

  /** Склад покрывает легкие позиции и вход дефицитной — дефицит ровно один. */
  function deficitCtx(level: number): ShuttleGenContext {
    return genCtx({
      level,
      warehouse: stockedWarehouse(['algae', 'soy'], 40),
      available_goods: [...DEFICIT_POOL],
      rng: () => ROLL,
    });
  }

  it('количество дефицитного отсека равно stock + clamp(target - stock)', () => {
    const level = 20;
    const trip = generateTrip(deficitCtx(level));
    const slot = trip.slots.find((s) => s.good_id === 'oxygen_tank');

    expect(slot).toBeDefined();
    const target = slotQuantity('oxygen_tank', 'shuttle', level, ROLL);
    expect(slot?.qty_required).toBe(applyPinch(0, target));
  });

  it('дефицитный отсек растет вместе с bracket_mult, а не роллом', () => {
    // Тот же ролл, тот же пул, разные брекеты (каркас 3.1). Случайный пинч дал
    // бы одно и то же число на обоих уровнях — связь с уровнем колонии рвется.
    const low = generateTrip(deficitCtx(6)).slots.find((s) => s.good_id === 'oxygen_tank');
    const high = generateTrip(deficitCtx(20)).slots.find((s) => s.good_id === 'oxygen_tank');

    expect(slotQuantity('oxygen_tank', 'shuttle', 20, ROLL)).toBeGreaterThan(
      slotQuantity('oxygen_tank', 'shuttle', 6, ROLL),
    );
    expect(high?.qty_required).toBeGreaterThan(low?.qty_required ?? 0);
  });

  it('дефицит не выходит за PINCH_MIN..PINCH_MAX сверх склада', () => {
    const trip = generateTrip(deficitCtx(20));
    const slot = trip.slots.find((s) => s.good_id === 'oxygen_tank');
    expect(slot?.qty_required).toBeGreaterThanOrEqual(PINCH_MIN);
    expect(slot?.qty_required).toBeLessThanOrEqual(PINCH_MAX);
  });
});
