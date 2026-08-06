/**
 * Дрон-курьер: доход и ритм сессии, дешевый отказ.
 * Источник истины — [[tz-drone-mars]], разделы 3, 4 и 9.
 *
 * Роль механики в трио: дрон — единственная, где отказ от заказа дешев.
 * Поэтому здесь есть выброс с таймером, а платный рефреш стоит дорого
 * НАМЕРЕННО: отказ обязан быть дешевым по бесплатному пути (подождать),
 * а не потому, что платный путь ничего не стоит.
 *
 * Резервирование происходит в момент тапа «Погрузить», а не при наличии
 * товара на складе. Иначе товар оказался бы недоступен производству просто
 * потому, что где-то на доске висит заказ, который игрок и не собирался брать.
 */

import {
  COVERAGE_MIN,
  DRONE_PREMIUM_DEFICIT_BONUS,
  DRONE_PREMIUM_RANGE,
  DRONE_REFRESH_FREE_SEC,
  EASY_PRODUCE_MAX_MIN,
  GEN_MAX_ATTEMPTS,
  MAX_DEFICIT_SLOTS,
  NUM_VISIBLE_ORDERS,
  type OrderGenerationDegradedReason,
  PINCH_MAX,
  PINCH_MIN,
  REPEAT_CAP,
  XP_MULTIPLIER_K,
} from './config/economy';
import { ALL_GOOD_IDS, GOOD_BASE_QTY, GOODS, slotQuantity } from './config/goods';
import {
  buyoutPrice,
  productionTimeMinutes,
  roundToShowcase,
  showcaseCeil,
  showcaseFloor,
} from './rushcost';
import type { GoodId } from './types';
import {
  availableOf,
  qtyOf,
  reserve,
  reservedOf,
  shipReserved,
  unreserve,
  type WarehouseState,
} from './warehouse';

export type OrderSlotState = 'active' | 'in_progress' | 'ready' | 'empty_cooldown';

export interface OrderPosition {
  good_id: GoodId;
  qty: number;
  /** Позиция закрыта: погружена со склада или докуплена. */
  filled: boolean;
  /**
   * ЧЕМ закрыта, а не только закрыта ли. Различие несущее: погрузка резервирует
   * товар на складе и при отправке обязана его списать, а докупка по И-12 кладет
   * товар мимо склада и списывать нечего. Без этого поля `sendOrder` списал бы
   * резерв, которого не было, — ровно тот дефект, который уже случился у шаттла
   * (Д-1) и запер товар на складе навсегда.
   */
  filled_by: 'self' | 'purchase' | null;
  /**
   * И-8 на момент генерации: покрыто складом ИЛИ производится не дольше
   * `EASY_PRODUCE_MAX_MIN`. Поле канона ([[tz-common-systems-mars]] 1.3,
   * `positions.append({good, qty, easy})`) — снимок, а не текущее состояние
   * склада: по нему считается `easy_ratio` заказа и премия за дефицит.
   */
  easy: boolean;
}

export interface OrderSlot {
  idx: number;
  state: OrderSlotState;
  npc_name: string;
  positions: OrderPosition[];
  credits_reward: number;
  xp_reward: number;
  /** Когда истекает бесплатный рефреш. Значимо только в `empty_cooldown`. */
  refresh_at: number;
}

/**
 * ТЗ 4.1: сколько заказов видно на доске. Верхняя граница, не цель наполнения.
 * Кривая живет в конфиге (`NUM_VISIBLE_ORDERS`), здесь только чтение брекета:
 * до дрона игрок доходит на втором уровне, ниже него доска не существует.
 */
export function slotsAtLevel(level: number): number {
  const row = NUM_VISIBLE_ORDERS.find((r) => level >= r.from_level);
  return (row ?? NUM_VISIBLE_ORDERS.at(-1))?.orders ?? 0;
}

/**
 * ТЗ 4.2.1: веса числа позиций по уровню. Явные веса, а не формула, —
 * распределение тюнится отдельно от среднего значения.
 *
 * **Потолок понижен с шести позиций до пяти — решение владельца 2026-08-05.**
 *
 * Причина арифметическая, а не вкусовая. Анти-повтор (ТЗ 4.2) запрещает двум
 * видимым заказам пересекаться больше чем наполовину. На верхних уровнях доска
 * показывает девять заказов, а пул открытых товаров — четырнадцать. Девять
 * заказов по шесть позиций это пятьдесят четыре места на четырнадцать имен:
 * среднее пересечение пары выходит 2.57 при потолке 3.0, и разброс регулярно
 * выносит его за порог. Перебор сорока досок давал двенадцать нарушений; выбор
 * наименее занятого товара сбил их до двух, но оставшиеся не лечатся кодом —
 * это голубиный принцип.
 *
 * При пяти позициях среднее пересечение падает до 1.79 при потолке 2.5, и
 * порог держится честно.
 *
 * Цена решения названа владельцу: заказ платит меньше за слот доски.
 */
const POSITIONS_COUNT_WEIGHTS: Array<{ from_level: number; weights: Record<number, number> }> =
  [
    { from_level: 15, weights: { 4: 0.4, 5: 0.6 } },
    { from_level: 12, weights: { 3: 0.2, 4: 0.4, 5: 0.4 } },
    { from_level: 10, weights: { 3: 0.3, 4: 0.4, 5: 0.3 } },
    { from_level: 8, weights: { 2: 0.2, 3: 0.35, 4: 0.3, 5: 0.15 } },
    { from_level: 6, weights: { 2: 0.35, 3: 0.4, 4: 0.25 } },
    { from_level: 4, weights: { 1: 0.25, 2: 0.45, 3: 0.3 } },
    { from_level: 2, weights: { 1: 0.55, 2: 0.45 } },
  ];

export function positionsCountFor(level: number, roll: number): number {
  const row = POSITIONS_COUNT_WEIGHTS.find((r) => level >= r.from_level);
  const weights = row?.weights ?? { 1: 1 };
  let acc = 0;
  for (const [count, weight] of Object.entries(weights)) {
    acc += weight;
    if (roll <= acc) return Number(count);
  }
  return Number(Object.keys(weights).at(-1) ?? 1);
}

const NPC_NAMES = [
  'Ирина, гидропоника',
  'Марк, столовая',
  'Лу, мастерская',
  'Дана, медблок',
  'Петр, склад',
  'Сати, оранжерея',
  'Олаф, энергоузел',
  'Ева, лаборатория',
  'Ким, шлюз',
];

export interface GeneratorContext {
  level: number;
  warehouse: WarehouseState;
  /** Что игрок уже умеет производить: построенные здания и открытые культуры. */
  available_goods: GoodId[];
  /** Другие активные заказы доски — для анти-повтора (REPEAT_SCOPE=board). */
  board: OrderSlot[];
  rng: () => number;
}

/**
 * «Легкая» позиция по И-8: покрыта складом ИЛИ производится не дольше
 * EASY_PRODUCE_MAX_MIN. Вторая половина условия долго отсутствовала, и это
 * было не придиркой к букве: без нее пустой склад делал дефицитной каждую
 * позицию, бюджет дефицита выедался первой же, а все остальные генератор
 * выбрасывал. Замер показал доску из одних однопозиционных заказов.
 */
function isEasy(good_id: GoodId, qty: number, warehouse: WarehouseState): boolean {
  if (availableOf(warehouse, good_id) >= qty) return true;
  // Время НУЖНОГО количества по всей цепочке рецепта, а не время одного цикла
  // самого товара. Прежняя редакция читала `prod_time_sec` и считала грибной
  // суп легким: сам он варится за порог, а грибы под него растут пятьдесят
  // минут. Тот же дефект был у шаттла и починен там же (`shuttle.ts`).
  return productionTimeMinutes(good_id, qty, warehouse) <= EASY_PRODUCE_MAX_MIN.drone;
}

/**
 * `maxQtyWithin` канона (1.4, `downgradeHardestDeficitSlot`): наибольшее
 * количество, которое покрыто складом ИЛИ производится в пределах порога easy.
 *
 * Тот же предикат И-8, только читаемый в обратную сторону — не «легка ли эта
 * позиция», а «до какого количества ее надо урезать, чтобы стала легкой».
 * Прежняя редакция резала строго до складского остатка, то есть знала лишь
 * первую половину условия: при пустом складе легального количества не
 * находилось вовсе и позиция выбрасывалась целиком.
 *
 * Перебор сверху вниз, а не формула: время цепочки не обратимо аналитически
 * (рецепт ветвится, склад вычитается на каждом звене), а количества позиции
 * измеряются десятками.
 */
function maxEasyQty(good_id: GoodId, target_qty: number, warehouse: WarehouseState): number {
  for (let qty = target_qty; qty >= 1; qty--) {
    if (isEasy(good_id, qty, warehouse)) return qty;
  }
  return 0;
}

/**
 * Доля позиций, которые игрок может закрыть прямо сейчас или быстро произвести.
 * Считается по флагу позиции, как в каноне 1.3 (`easyRatio(positions)`), а не
 * повторным опросом склада: иначе одно правило И-8 живет в двух местах.
 */
function easyRatio(positions: OrderPosition[]): number {
  if (positions.length === 0) return 1;
  return positions.filter((p) => p.easy).length / positions.length;
}

/**
 * Канон 1.4, `PINCH_MODE=absolute` (дрон и шаттл):
 * `stock + clamp(targetQty - stock, PINCH_MIN, PINCH_MAX)`.
 *
 * Величина дефицита детерминирована составом заказа, а не роллом: пинч — это
 * «чуть больше, чем на складе», привязанное к тому, сколько заказ и так просил.
 * Случайное 1-3 поверх остатка рвет эту связь и стирает `bracket_mult` на
 * дефицитной позиции: на пятнадцатом уровне позиция просила бы столько же,
 * сколько на втором.
 */
export function applyPinch(stock: number, target_qty: number): number {
  const gap = target_qty - stock;
  return stock + Math.min(PINCH_MAX, Math.max(PINCH_MIN, gap));
}

/** Максимальная доля пересечения с любым заказом доски (ТЗ 4.2, REPEAT_SCOPE=board). */
function maxRepeatRatio(positions: OrderPosition[], board: OrderSlot[]): number {
  const ids = new Set(positions.map((p) => p.good_id));
  if (ids.size === 0) return 0;

  let worst = 0;
  for (const slot of board) {
    if (slot.state === 'empty_cooldown') continue;
    const other = new Set(slot.positions.map((p) => p.good_id));
    const shared = [...ids].filter((id) => other.has(id)).length;
    worst = Math.max(worst, shared / ids.size);
  }
  return worst;
}

/**
 * Крайний случай канона ([[tz-common-systems-mars]] 1.6): пул пуст или ни одна
 * попытка не собрала ни одной позиции. Канон предписывает `fallbackMinimalOrder`
 * — «одна позиция самого дешевого/быстрого доступного товара, количество =
 * `GOOD_BASE_QTY.min`, форма урезается ниже нормального минимума», и требует
 * (1.9) чтобы генератор «никогда не завершался ошибкой или пустым результатом».
 * Требование адресовано всем трем механикам, не только шаттлу.
 *
 * Пул выбирается тремя ступенями: заказанный вызывающим, затем товары без
 * здания на уровне игрока (POOL_MODE канона), затем весь субстрат. Заказ из
 * нуля позиций формально проходил обе проверки И-8 (`easyRatio([])` = 1,
 * `maxRepeatRatio([])` = 0) и занимал слот доски карточкой, которая ничего не
 * просит и ничего не платит: ее нельзя ни выполнить, ни довести до `ready`.
 */
function fallbackMinimalPositions(
  ctx: GeneratorContext,
  reason: OrderGenerationDegradedReason,
): OrderPosition[] {
  const pools: GoodId[][] = [
    ctx.available_goods,
    availableGoodsFor(ctx.level, new Set<string>()),
    ALL_GOOD_IDS,
  ];
  const candidates = pools.find((p) => p.length > 0) ?? ALL_GOOD_IDS;
  const good_id = [...candidates].sort((a, b) => {
    const by_time = GOODS[a].prod_time_sec - GOODS[b].prod_time_sec;
    return by_time !== 0 ? by_time : GOODS[a].price - GOODS[b].price;
  })[0]!;

  // Канон 1.6 и 1.8: деградация генератора логируется как алерт, а не глотается
  // молча. Сервера у прототипа нет, поэтому событие уходит в консоль тем же
  // именем и с тем же перечислением причин, что заведены в таблице событий.
  console.warn('order_generation_degraded', {
    mechanic: 'drone',
    reason,
    player_level: ctx.level,
  });

  const qty = GOOD_BASE_QTY[good_id].min;
  return [
    { good_id, qty, filled: false, filled_by: null, easy: isEasy(good_id, qty, ctx.warehouse) },
  ];
}

/**
 * Генерация одного заказа в слот.
 *
 * И-8 (анти-фрустрация) соблюдается перегенерацией с ограниченным числом
 * попыток, а не хитрым подбором: попытки конечны, и если ни одна не прошла,
 * заказ все равно выдается. Заказ, которого нет, хуже неидеального заказа —
 * пустая доска читается как поломка игры.
 */
export function generateOrder(idx: number, ctx: GeneratorContext): OrderSlot {
  let best: OrderPosition[] = [];
  /**
   * Признак ТЗ 4.3 `order.has_deficit_position`, снятый с той самой попытки,
   * чей набор позиций поехал наружу. Хранится рядом с `best`, а не одной
   * переменной цикла: удержанная попытка и последняя — разные наборы, и флаг
   * последней описывал бы заказ, которого игрок не увидит.
   */
  let best_has_deficit = false;

  /**
   * Составы видимых заказов доски. Вход анти-повтора (ТЗ 4.2,
   * REPEAT_SCOPE=board).
   *
   * Множества, а не счетчик занятости: порог считается попарно с каждым
   * заказом, и суммарная нагрузка его не описывает. Товар, редкий на доске в
   * целом, может оказаться общим именно с тем соседом, с которым пересечение
   * уже на пределе.
   */
  const board_sets: Array<Set<GoodId>> = [];
  for (const slot of ctx.board) {
    if (slot.state === 'empty_cooldown') continue;
    board_sets.push(new Set(slot.positions.map((x) => x.good_id)));
  }

  for (let attempt = 0; attempt < GEN_MAX_ATTEMPTS; attempt++) {
    const count = positionsCountFor(ctx.level, ctx.rng());
    const pool = [...ctx.available_goods];
    const positions: OrderPosition[] = [];
    let deficit_used = 0;

    // Цикл идет по числу набранных позиций, а не по счетчику попыток: товар,
    // не прошедший отбор, отбраковывается и заменяется следующим кандидатом из
    // пула (канон 1.3 — `continue` без `append`), а не съедает позицию.
    while (positions.length < count && pool.length > 0) {
      // Анти-повтор (ТЗ 4.2, REPEAT_SCOPE=board) действует при ВЫБОРЕ, а не
      // только проверкой собранного набора. Слепой выбор с последующей
      // отбраковкой на доске из девяти заказов по шесть позиций при пуле в
      // четырнадцать товаров не сходится: пересечение вынуждено арифметикой, и
      // все сорок попыток проваливают порог. Тогда наружу уезжала самая полная
      // попытка — с повтором и без единого сигнала.
      //
      // Выбор с оглядкой на ПАРУ, а не на суммарную занятость доски.
      //
      // Порог анти-повтора считается попарно с каждым видимым заказом, поэтому
      // минимизация общей нагрузки его не держит: товар, редкий на доске в
      // целом, может оказаться пятым общим именно с одним соседом. Для каждого
      // кандидата считаем, каким станет худшее пересечение по всем заказам
      // доски, если его добавить, и берем кандидатов с наименьшим худшим.
      //
      // Один вызов `rng` на выбор, как и раньше: форма расхода случайности не
      // меняется, прогоны остаются воспроизводимыми.
      const chosen = new Set(positions.map((p) => p.good_id));
      let best_worst = Number.POSITIVE_INFINITY;
      const from: number[] = [];
      for (let i = 0; i < pool.length; i++) {
        const candidate = pool[i]!;
        let worst = 0;
        for (const slot_ids of board_sets) {
          let shared = slot_ids.has(candidate) ? 1 : 0;
          for (const id of chosen) if (slot_ids.has(id)) shared += 1;
          worst = Math.max(worst, shared);
        }
        if (worst < best_worst) {
          best_worst = worst;
          from.length = 0;
        }
        if (worst === best_worst) from.push(i);
      }
      const pick_at = from[Math.min(from.length - 1, Math.floor(ctx.rng() * from.length))]!;
      const good_id = pool.splice(pick_at, 1)[0]!;
      let qty = slotQuantity(good_id, 'drone', ctx.level, ctx.rng());

      // И-8: не больше одной дефицитной позиции на заказ. Дефицит — это
      // «чуть больше, чем на складе», а не «недостижимо много». Товар с
      // коротким циклом дефицитом не считается: игрок его просто вырастит.
      //
      // Предикат ровно тот же, что у флага позиции, — канон 1.3 задает его один
      // раз (`isEasy = stock >= qty or produceMin <= EASY_PRODUCE_MAX_MIN`) и
      // тут же вешает на него бюджет дефицита (`if not isEasy: deficitCount++`).
      // Прежняя редакция читала здесь `prod_time_sec` одного цикла: грибной суп
      // проходил ворота «быстрым», бюджета не тратил и не пинчевался, хотя по
      // цепочке с грибами не укладывался в порог. Так в заказ попадало больше
      // одной позиции, которую игрок не может ни взять со склада, ни успеть
      // произвести. Тот же разъезд уже чинили во флаге `easy` и у шаттла.
      const have = availableOf(ctx.warehouse, good_id);

      if (!isEasy(good_id, qty, ctx.warehouse)) {
        if (deficit_used >= MAX_DEFICIT_SLOTS) {
          // Бюджет дефицита исчерпан — позицию надо сделать легкой. Канон
          // (1.4 `downgradeHardestDeficitSlot`): режем количество до
          // `maxQtyWithin` — наибольшего, покрытого складом ИЛИ производимого в
          // пределах порога, — но не ниже пола `GOOD_BASE_QTY[good].min`; если и
          // минимум не легок, МЕНЯЕМ товар (здесь — берем следующего кандидата
          // из пула), а не опускаем количество ниже пола. Позиция из одной
          // водоросли при минимуме пять — это заказ с впятеро заниженным XP и
          // ценой, и падает он молча.
          const easy_qty = maxEasyQty(good_id, qty, ctx.warehouse);
          if (easy_qty < GOOD_BASE_QTY[good_id].min) continue;
          qty = easy_qty;
        } else {
          qty = applyPinch(have, qty);
          // Бюджет тратится за позицию, которая осталась тяжелой ПОСЛЕ пинча, а
          // не за намерение. Пинч режет количество до «склад + 1..3», и этого
          // бывает достаточно, чтобы позиция уложилась в порог производства: в
          // заказ она приезжает легкой, и списывать за нее единственный слот
          // дефицита значит выбросить остаток пула ни за что. И-8 считает
          // позиции, которые игрок не может ни взять, ни быстро произвести, —
          // тем же счетом, что флаг `easy` и проверка покрытия.
          if (!isEasy(good_id, qty, ctx.warehouse)) deficit_used += 1;
        }
      }

      positions.push({
        good_id,
        qty,
        filled: false,
        filled_by: null,
        easy: isEasy(good_id, qty, ctx.warehouse),
      });
    }

    const coverage_ok = easyRatio(positions) >= COVERAGE_MIN.drone;
    const repeat_ok = maxRepeatRatio(positions, ctx.board) <= REPEAT_CAP;
    if (coverage_ok && repeat_ok && positions.length > 0) {
      best = positions;
      best_has_deficit = deficit_used > 0;
      break;
    }
    // Из невалидных попыток удерживается самая полная по форме, а не последняя:
    // короткий заказ — это меньше кредитов за тот же слот доски. Форма та же,
    // что у шаттла: движок один, расходиться конструкциям незачем.
    if (positions.length > best.length) {
      best = positions;
      best_has_deficit = deficit_used > 0;
    }
  }

  // Финальный предохранитель канона 1.3/1.6. Ни один путь наружу не отдает
  // заказ из нуля позиций И не отдает заказ, не прошедший инварианты.
  //
  // Вторая половина этого условия долго отсутствовала, и дефект был латентным:
  // фолбэк срабатывал только на ПУСТОМ наборе, а набор, собранный за сорок
  // попыток и не взявший `COVERAGE_MIN`, уезжал игроку как есть — И-8 нарушена
  // в выданном заказе, и ни одного сигнала об этом. Канон 1.6 требует ровно
  // обратного: «если и меньшая форма не проходит инварианты, срабатывает
  // `fallbackMinimalOrder`». У шаттла эта проверка есть, у дрона не было —
  // движок один, расходиться конструкциям незачем.
  //
  // Причина деградации различается, и различие несущее: пустой пул — это
  // недостроенный контент, исчерпанные попытки — сигнал бага генератора,
  // непройденный инвариант — третья причина канона 1.8, которую дрон до сих
  // пор не эмитил вообще, потому что путь к ней не был написан.
  const empty = best.length === 0;
  const unresolvable = !empty && easyRatio(best) < COVERAGE_MIN.drone;
  const degraded = empty || unresolvable;
  const positions = degraded
    ? fallbackMinimalPositions(
        ctx,
        empty
          ? ctx.available_goods.length === 0
            ? 'empty_pool'
            : 'max_attempts'
          : 'unresolvable_invariant',
      )
    : best;

  // Фолбэк канона 1.6 пинча не делает: он просит `GOOD_BASE_QTY.min` и ничего
  // сверх склада, значит и надбавки за дефицит там быть не может.
  const reward = orderReward(positions, ctx.rng(), degraded ? false : best_has_deficit);
  return {
    idx,
    state: 'active',
    npc_name: NPC_NAMES[idx % NPC_NAMES.length]!,
    positions,
    credits_reward: reward.credits,
    xp_reward: reward.xp,
    refresh_at: 0,
  };
}

/**
 * ТЗ 4.3: награда считается дроном, а не генератором. Движок отдает состав,
 * деньги назначает механика — у шаттла и лайнера они другие при том же составе.
 */
export function orderReward(
  positions: OrderPosition[],
  jitter_roll = 0.5,
  /**
   * ТЗ 4.3, `order.has_deficit_position`: заказ содержит целевую дефицитную
   * позицию (И-8 разрешает не больше одной). Знает об этом только генератор —
   * по составу заказа дефицит не восстанавливается: позиция, урезанная пинчем
   * до складского остатка, от обычной неотличима.
   */
  has_deficit_position = false,
): { credits: number; xp: number; premium: number } {
  const market_sum = positions.reduce((sum, p) => sum + GOODS[p.good_id].price * p.qty, 0);

  let premium = 1.48;
  const has_factory = positions.some((p) => GOODS[p.good_id].kind === 'factory');
  const all_crops =
    positions.length > 0 && positions.every((p) => GOODS[p.good_id].kind === 'crop');

  // Фабричный товар дольше готовить — премия выше. Только грядки — ниже.
  if (has_factory) premium += 0.08;
  else if (all_crops) premium -= 0.05;

  // Мало позиций, крупный лот — премия выше. Замер вертолета: 4 позиции +64%,
  // 6 позиций +37%. Закономерность воспроизведена явным слагаемым.
  if (positions.length <= 2) premium += 0.05;
  else if (positions.length >= 5) premium -= 0.05;

  // Целевой дефицит: заказ просит больше, чем лежит на складе, и стоит игроку
  // производственного цикла. Без надбавки он платит столько же, сколько заказ,
  // который закрывается одним тапом из склада.
  if (has_deficit_position) premium += DRONE_PREMIUM_DEFICIT_BONUS;

  premium += (jitter_roll - 0.5) * 0.04; // джиттер +-0.02
  premium = Math.min(
    1 + DRONE_PREMIUM_RANGE.max,
    Math.max(1 + DRONE_PREMIUM_RANGE.min, premium),
  );

  const xp = positions.reduce(
    (sum, p) => sum + GOODS[p.good_id].base_xp * XP_MULTIPLIER_K.drone * p.qty,
    0,
  );

  // Витринное округление идет к ближайшей ступени и умеет вынести ФАКТИЧЕСКУЮ
  // премию за коридор каркаса: рынок 34 при потолке 1.70 дает 57.8, лестница
  // округляет до 60, и премия становится 76.5%. Коридор — инвариант, лестница —
  // оформление, поэтому при столкновении уступает лестница.
  let credits = roundToShowcase(market_sum * premium);
  if (market_sum > 0) {
    const ceiling = market_sum * (1 + DRONE_PREMIUM_RANGE.max);
    const floor = market_sum * (1 + DRONE_PREMIUM_RANGE.min);
    if (credits > ceiling) credits = showcaseFloor(ceiling);
    if (credits < floor) credits = showcaseCeil(floor);
  }

  return { credits, xp, premium };
}

/**
 * Хватает ли склада на конкретную позицию. Ровно это и красит счетчик «3/5»
 * на карточке: зеленый — хватает, приглушенный — нет.
 *
 * Отдельная функция, а не сравнение по месту в верстке: правило одно, и оно
 * обязано жить в одном месте. Счетчик, покрашенный по своей формуле, рано или
 * поздно разойдется с кнопкой «Погрузить», которая рядом.
 */
export function positionCovered(position: OrderPosition, warehouse: WarehouseState): boolean {
  if (position.filled) return true;
  return availableOf(warehouse, position.good_id) >= position.qty;
}

/**
 * Может ли игрок закрыть заказ прямо сейчас: склад покрывает все непогруженные
 * позиции. Это подсказка доски, а не состояние слота.
 *
 * Без нее игроку пришлось бы открывать все девять карточек, чтобы понять, какую
 * он способен выполнить. Референс подсвечивает такие заказы именно для этого —
 * доска обязана отвечать на вопрос «куда смотреть» без единого тапа.
 *
 * Не путать с состоянием `ready`: `ready` — все уже погружено и ждет отправки,
 * а это — «еще ничего не грузил, но хватит на все». Разные смыслы и разные
 * визуальные состояния карточки.
 */
export function canFulfillNow(slot: OrderSlot, warehouse: WarehouseState): boolean {
  if (slot.state === 'empty_cooldown') return false;
  if (slot.positions.length === 0) return false;

  // Считаем суммарную потребность по товару: две позиции одного товара внутри
  // заказа должны покрываться вместе, а не каждая по отдельности.
  const needed = new Map<GoodId, number>();
  for (const position of slot.positions) {
    if (position.filled) continue;
    needed.set(position.good_id, (needed.get(position.good_id) ?? 0) + position.qty);
  }

  for (const [good_id, qty] of needed) {
    if (availableOf(warehouse, good_id) < qty) return false;
  }
  return true;
}

/** Погрузка одной позиции: резерв со склада в слот заказа. */
export function loadPosition(
  slot: OrderSlot,
  position_idx: number,
  warehouse: WarehouseState,
): boolean {
  const position = slot.positions[position_idx];
  if (!position || position.filled) return false;
  if (slot.state !== 'active' && slot.state !== 'in_progress') return false;
  if (!reserve(warehouse, position.good_id, position.qty)) return false;

  position.filled = true;
  position.filled_by = 'self';
  slot.state = slot.positions.every((p) => p.filled) ? 'ready' : 'in_progress';
  return true;
}

/**
 * Цена докупки позиции: И-4, rush-cost цепочки с наценкой.
 *
 * Складской остаток в цену не входит по той же причине, что и у отсека шаттла:
 * докупка по И-12 кладет товар мимо склада и остатка не трогает, поэтому скидка
 * за него открывала бы арбитраж «докупить дешево, продать сэкономленное».
 */
export function positionBuyoutPrice(position: OrderPosition): number {
  if (position.filled) return 0;
  return buyoutPrice(position.good_id, position.qty);
}

/**
 * Докупка позиции за изотопы (ТЗ дрона 4.5, И-12).
 *
 * Товар приходит извне и на складе не появляется ни на секунду: ни резерва, ни
 * прихода. Поэтому отправка обязана отличать такую позицию от погруженной, иначе
 * спишет резерв, которого не было.
 */
export function buyoutPosition(slot: OrderSlot, position_idx: number): boolean {
  const position = slot.positions[position_idx];
  if (!position || position.filled) return false;
  if (slot.state !== 'active' && slot.state !== 'in_progress') return false;

  position.filled = true;
  position.filled_by = 'purchase';
  slot.state = slot.positions.every((p) => p.filled) ? 'ready' : 'in_progress';
  return true;
}

/**
 * Отправка: зарезервированное физически уходит со склада, слот пустеет.
 *
 * Все-или-ничего, как `deposit` у склада. Сначала проверяется, что резерв
 * подтверждает каждую погруженную позицию, и только потом идет списание: заказ,
 * у которого уехала одна позиция из двух, — состояние без корректного выхода.
 *
 * Награда выдается только за фактически ушедший груз (ТЗ дрона AC 8 и раздел
 * 10: `deliver` уводит со склада то, что «Погрузить» положило в `reserved`).
 * Прежняя редакция звала `shipReserved` ради побочного эффекта и не читала
 * ответ: склад отказывал молча, товар оставался на полке, а кредиты и XP
 * начислялись — доход из ниоткуда, и ни одна проверка не падала.
 */
export function sendOrder(
  slot: OrderSlot,
  warehouse: WarehouseState,
): { ok: boolean; credits: number; xp: number } {
  if (slot.state !== 'ready') return { ok: false, credits: 0, xp: 0 };

  // Потребность считается по товару, а не по позиции: две позиции одного товара
  // делят одну ячейку склада, и резерв под ними тоже общий.
  const shipping = new Map<GoodId, number>();
  for (const position of slot.positions) {
    // Докупленное на складе не лежало — списывать нечего (И-12).
    if (position.filled_by === 'purchase') continue;
    shipping.set(position.good_id, (shipping.get(position.good_id) ?? 0) + position.qty);
  }

  for (const [good_id, qty] of shipping) {
    if (reservedOf(warehouse, good_id) < qty || qtyOf(warehouse, good_id) < qty) {
      return { ok: false, credits: 0, xp: 0 };
    }
  }
  for (const [good_id, qty] of shipping) {
    shipReserved(warehouse, good_id, qty);
  }
  return { ok: true, credits: slot.credits_reward, xp: slot.xp_reward };
}

/**
 * Выброс заказа. Уже погруженное возвращается на склад — игрок не должен
 * терять товар за отказ от заказа, иначе выброс перестает быть дешевым.
 */
export function discardOrder(slot: OrderSlot, now: number): void {
  if (slot.state === 'empty_cooldown') return;
  slot.state = 'empty_cooldown';
  slot.refresh_at = now + DRONE_REFRESH_FREE_SEC;
}

/**
 * Возврат резерва при выбросе (действие `clear` каркаса). Вызывается до смены
 * состояния слота.
 *
 * Докупленная позиция сюда не попадает: по И-12 товар за изотопы приходит мимо
 * склада, `clear` для нее недоступен (ТЗ дрона раздел 10), и при выбросе она
 * аннулируется безвозвратно — «не возвращается ни на склад, ни рефандом
 * изотопов» (AC 11).
 *
 * Различие несущее, а не формальное: резерв в складе — один скаляр на товар и к
 * заказу не привязан. `unreserve` за докупку не «ничего не делает», он снимает
 * резерв СОСЕДНЕГО заказа, который легально делит тот же товар при REPEAT_CAP.
 * Дальше отправка соседа не находит своего резерва, товар остается на складе и
 * продается второй раз.
 */
export function releaseReserved(slot: OrderSlot, warehouse: WarehouseState): void {
  for (const position of slot.positions) {
    if (!position.filled || position.filled_by === 'purchase') continue;
    unreserve(warehouse, position.good_id, position.qty);
    position.filled = false;
    // Позиция снова открыта — «чем закрыта» обязано обнулиться вместе с
    // признаком закрытия, иначе состояние противоречит само себе.
    position.filled_by = null;
  }
}

/** Пул товаров, доступных игроку: открытые культуры и рецепты построенных зданий. */
export function availableGoodsFor(level: number, buildings: Set<string>): GoodId[] {
  return ALL_GOOD_IDS.filter((id) => {
    const good = GOODS[id];
    if (good.unlock_level > level) return false;
    if (good.kind === 'crop') return true;
    return good.required_building !== null && buildings.has(good.required_building);
  });
}
