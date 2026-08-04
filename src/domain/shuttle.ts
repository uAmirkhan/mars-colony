/**
 * Грузовой шаттл: единственный источник строй-модулей (И-1) и главный гейт
 * прогрессии. Источник истины — [[tz-shuttle-mars]], разделы 2-5.
 *
 * Роль в трио: у дрона отказ дешевый, у лайнера заказ сгорает по дедлайну,
 * у шаттла отказа нет вовсе. Поэтому здесь нет ни кнопки «Выбросить», ни
 * кнопки «Отправить»: заполнение последнего отсека само стартует рейс
 * (п.2.3), а стартовавший рейс не отменяется. Это осознанный анти-скип —
 * игрок не может передумать на полпути и тем обесценить ожидание.
 *
 * Награды роллятся в момент ОТПРАВКИ, а не сбора. Иначе переустановка клиента
 * или перевод часов дают бесплатный реролл содержимого контейнеров.
 */

import {
  COLLECT_COOLDOWN_MIN,
  COVERAGE_MIN,
  EASY_PRODUCE_MAX_MIN,
  FTUE_FIRST_TRIP_TIMER_MIN,
  flightTimerMin,
  GEN_MAX_ATTEMPTS,
  MAX_DEFICIT_SLOTS,
  type OrderGenerationDegradedReason,
  PINCH_MAX,
  PINCH_MIN,
  REPEAT_CAP,
  SLOT_COUNT_MIN,
  shuttleSkipPrice,
  slotCountFor,
  TRANSPORT_XP_K,
} from './config/economy';
import { ALL_GOOD_IDS, GOOD_BASE_QTY, GOODS, slotQuantity } from './config/goods';
import { availableGoodsFor } from './drone';
import { type DropContext, rollArrival } from './droproller';
import { buyoutPrice, productionTimeMinutes } from './rushcost';
import type { GoodId, ModuleId } from './types';
import { availableOf, reserve, shipReserved, type WarehouseState } from './warehouse';

export type ShuttleState = 'ORDER' | 'IN_TRANSIT' | 'ARRIVED' | 'COOLDOWN';

export interface ShuttleSlot {
  idx: number;
  good_id: GoodId;
  qty_required: number;
  qty_filled: number;
  /** Чем закрыт отсек. Докупка (И-12) помечена отдельно: она не возвратима. */
  filled_by: 'self' | 'purchase' | null;
  /** Награда, зафиксированная при отправке. До отправки — null. */
  reward: ModuleId | null;
  collected: boolean;
  /** Отсек переписан гарантией И-11. Показывается игроку, не скрывается. */
  floor_forced: boolean;
}

export interface ShuttleTrip {
  state: ShuttleState;
  slots: ShuttleSlot[];
  /** Длина рейса в минутах — нужна формуле скипа И-6 как знаменатель. */
  trip_min: number;
  departed_at: number;
  arrives_at: number;
  cooldown_until: number;
  is_first_trip: boolean;
  /** Порядковый номер прибытия игрока. Вход для FTUE-удачи и И-11. */
  arrival_no: number;
}

export interface ShuttleGenContext {
  level: number;
  warehouse: WarehouseState;
  available_goods: GoodId[];
  /** Прошлый рейс — для анти-повтора (REPEAT_CAP считается к нему). */
  previous?: ShuttleTrip | null;
  /** Первый рейс игрока: форсированный состав и укороченный таймер (п.2.1). */
  is_first_trip: boolean;
  arrival_no: number;
  rng: () => number;
}

function slotShort(slot: ShuttleSlot): number {
  return Math.max(0, slot.qty_required - slot.qty_filled);
}

/** Хватает ли склада закрыть отсек целиком прямо сейчас. */
export function slotCovered(slot: ShuttleSlot, warehouse: WarehouseState): boolean {
  const short = slotShort(slot);
  if (short === 0) return true;
  return availableOf(warehouse, slot.good_id) >= short;
}

/**
 * «Легкая» позиция по И-8. Инвариант написан через ИЛИ: покрыта складом ИЛИ
 * производится не дольше EASY_PRODUCE_MAX_MIN. Вторая половина условия важнее
 * первой — без нее генератор считает пустой склад безвыходным положением и
 * вырождает рейс до одного отсека, хотя игрок собирает водоросли за две минуты.
 *
 * Время берется по всей цепочке рецепта и на все количество отсека — канон
 * ([[tz-common-systems-mars]] 1.3) считает порог через `productionTimeMinutes`,
 * а не через `prod_time_sec` одного звена. Раньше здесь стояло второе:
 * комбинезон проходил порог ровно в 30 минут, хотя до него нужны две ткани и
 * четыре хлопка, то есть два часа, — рейс из таких отсеков объявлялся легким
 * целиком и не пересобирался.
 */
function isEasy(good_id: GoodId, qty: number, warehouse: WarehouseState): boolean {
  if (availableOf(warehouse, good_id) >= qty) return true;
  return productionTimeMinutes(good_id, qty, warehouse) <= EASY_PRODUCE_MAX_MIN.shuttle;
}

/** Доля легких отсеков в рейсе. Вход инварианта И-8. */
function easyRatio(slots: ShuttleSlot[], warehouse: WarehouseState): number {
  if (slots.length === 0) return 1;
  const easy = slots.filter((s) => isEasy(s.good_id, s.qty_required, warehouse)).length;
  return easy / slots.length;
}

function repeatRatio(slots: ShuttleSlot[], previous: ShuttleTrip | null | undefined): number {
  if (!previous || slots.length === 0) return 0;
  const ids = new Set(slots.map((s) => s.good_id));
  const before = new Set(previous.slots.map((s) => s.good_id));
  const shared = [...ids].filter((id) => before.has(id)).length;
  return shared / ids.size;
}

/**
 * Крайний случай канона ([[tz-common-systems-mars]] 1.6): пул товаров пуст или
 * ни один кандидат не прошел отбор. Канон предписывает `fallbackMinimalOrder` —
 * «одна позиция самого дешевого/быстрого доступного товара (обычно водоросли),
 * количество = GOOD_BASE_QTY.min, форма заказа урезается ниже нормального
 * минимума (для шаттла — допустим 1 отсек вместо 3)».
 *
 * Пул выбирается тремя ступенями: заказанный вызывающим, затем товары без
 * здания на уровне игрока (POOL_MODE канона), затем весь субстрат. Последняя
 * ступень недостижима при level >= 1 (водоросли открыты с первого), но она
 * гарантирует тотальность функции: рейс из нуля отсеков не должен получаться
 * ни на каком входе — его нельзя ни закрыть, ни отменить, а шаттл единственный
 * источник строй-модулей (И-1), поэтому пустой рейс это софтлок насмерть.
 *
 * Ступень берется первая, где есть ЛЕГКИЙ по И-8 кандидат, а не первая
 * непустая. Канон 1.9 требует от фолбэка валидного заказа, а единственный
 * тяжелый отсек дает easyRatio = 0 и нарушает И-8 в одиночку: деградация не
 * должна подсовывать игроку двухчасовой комбинезон вместо трех обычных
 * отсеков. Если легкого нет нигде (недостижимо, пока водоросли открыты с
 * первого уровня), берется первая непустая ступень — тотальность важнее.
 */
function fallbackMinimalSlots(
  ctx: ShuttleGenContext,
  reason: OrderGenerationDegradedReason,
): ShuttleSlot[] {
  const pools: GoodId[][] = [
    ctx.available_goods,
    availableGoodsFor(ctx.level, new Set<string>()),
    ALL_GOOD_IDS,
  ];
  const easy_pools = pools.map((pool) =>
    pool.filter((id) => isEasy(id, GOOD_BASE_QTY[id].min, ctx.warehouse)),
  );
  const candidates =
    easy_pools.find((p) => p.length > 0) ?? pools.find((p) => p.length > 0) ?? ALL_GOOD_IDS;
  const good_id = [...candidates].sort((a, b) => {
    const by_time = GOODS[a].prod_time_sec - GOODS[b].prod_time_sec;
    return by_time !== 0 ? by_time : GOODS[a].price - GOODS[b].price;
  })[0]!;

  // Канон 1.6 и 1.8: деградация генератора логируется как алерт, а не глотается
  // молча. Сервера у прототипа нет, поэтому событие уходит в консоль тем же
  // именем, каким оно заведено в таблице событий общего движка.
  console.warn('order_generation_degraded', {
    mechanic: 'shuttle',
    reason,
    player_level: ctx.level,
  });

  return [
    {
      idx: 0,
      good_id,
      qty_required: GOOD_BASE_QTY[good_id].min,
      qty_filled: 0,
      filled_by: null,
      reward: null,
      collected: false,
      floor_forced: false,
    },
  ];
}

/**
 * Генерация заказа. Тот же движок, что у дрона, с шаттл-специфичным конфигом:
 * переменное число отсеков 3-5 и собственный анти-повтор (сравнение с прошлым
 * рейсом, а не с доской — рейс у шаттла ровно один).
 *
 * FTUE переопределяет генератор целиком (ТЗ шаттла 2.1): ровно три отсека, все
 * три позиции easy, COVERAGE_MIN=1.0 и MAX_DEFICIT_SLOTS=0 на этот заказ.
 * Первый цикл обязан пройтись бесплатно — иначе игрок знакомится с необратимым
 * таймером и платным выходом одновременно.
 *
 * **Решение исполнителя, требует утверждения владельцем.** ТЗ шаттла 2.1
 * требует «ровно 3 отсека, все easy», но не говорит, что делать, если пул
 * физически не дает трех easy-кандидатов (пустой склад + узкий пул тяжелых
 * товаров). Принято: (1) «easy» в FTUE читается по определению И-8 — покрыто
 * складом ИЛИ производится не дольше EASY_PRODUCE_MAX_MIN, а не «обязательно
 * лежит на складе», как было раньше; на реальном пуле уровня 5 (водоросли,
 * соя, грибы — все быстрее порога) это дает три easy-отсека даже с нуля;
 * (2) если и после этого кандидатов меньше, форма урезается по крайнему случаю
 * канона [[tz-common-systems-mars]] 1.6 вплоть до одного отсека — короткий
 * рейс проходим, пустой рейс это софтлок. Вопрос владельцу: допустим ли
 * укороченный ПЕРВЫЙ рейс, или FTUE должен вместо этого ждать, пока склад
 * наберет три позиции?
 */
export function generateTrip(ctx: ShuttleGenContext): ShuttleTrip {
  const trip_min = ctx.is_first_trip ? FTUE_FIRST_TRIP_TIMER_MIN : flightTimerMin(ctx.level);
  let best: ShuttleSlot[] = [];

  for (let attempt = 0; attempt < GEN_MAX_ATTEMPTS; attempt++) {
    const count = ctx.is_first_trip ? SLOT_COUNT_MIN : slotCountFor(ctx.level, ctx.rng());
    const pool = [...ctx.available_goods];
    const slots: ShuttleSlot[] = [];
    let deficit_used = 0;

    // Цикл идет по числу набранных отсеков, а не по счетчику попыток: товар,
    // не прошедший отбор, отбраковывается и заменяется следующим кандидатом из
    // пула, а не съедает отсек (канон 1.3 — `continue` без append). Раньше
    // здесь стоял `i -= 1; continue` внутри for по i: отбраковка возвращала
    // счетчик, но выход из цикла все равно рвался по опустевшему пулу, и рейс
    // выходил короче формы вплоть до нуля отсеков.
    while (slots.length < count && pool.length > 0) {
      const pick_at = Math.min(pool.length - 1, Math.floor(ctx.rng() * pool.length));
      const good_id = pool.splice(pick_at, 1)[0]!;
      let qty = slotQuantity(good_id, 'shuttle', ctx.level, ctx.rng());
      const have = availableOf(ctx.warehouse, good_id);
      // Тот же предикат И-8, что и в проверке покрытия: склад ИЛИ цепочка
      // рецепта в пределах порога. Две разные формулы «легкого» в одной
      // функции разъезжаются молча — отбор пропускал бы то, что покрытие потом
      // считает тяжелым.
      const easy = isEasy(good_id, qty, ctx.warehouse);

      // FTUE: дефицита нет вовсе (MAX_DEFICIT_SLOTS=0 на первом рейсе). «Easy»
      // читается ровно как в И-8 — склад ИЛИ быстрое производство, — поэтому
      // товар с коротким циклом годится в первый рейс и с пустого склада.
      if (ctx.is_first_trip) {
        if (!easy) {
          if (have === 0) continue;
          qty = have;
        }
      } else if (!easy) {
        // Дефицит по И-8 — это то, что игрок не может ни взять со склада, ни
        // быстро произвести. Товар с коротким циклом дефицитом не считается,
        // иначе пустой склад делал бы дефицитным вообще все.
        if (deficit_used >= MAX_DEFICIT_SLOTS) {
          if (have === 0) continue;
          qty = have;
        } else {
          deficit_used += 1;
          const pinch = PINCH_MIN + Math.floor(ctx.rng() * (PINCH_MAX - PINCH_MIN + 1));
          qty = have + pinch;
        }
      }

      slots.push({
        idx: slots.length,
        good_id,
        qty_required: qty,
        qty_filled: 0,
        filled_by: null,
        reward: null,
        collected: false,
        floor_forced: false,
      });
    }

    const coverage_ok = easyRatio(slots, ctx.warehouse) >= COVERAGE_MIN.shuttle;
    const repeat_ok = repeatRatio(slots, ctx.previous) <= REPEAT_CAP;
    if (coverage_ok && repeat_ok) {
      best = slots;
      break;
    }
    // Из невалидных попыток удерживается самая полная по форме, а не последняя:
    // короткий рейс — это меньше модулей за тот же таймер, а нулевой — софтлок.
    if (slots.length > best.length) best = slots;
  }

  // Финальный предохранитель канона 1.3/1.6. Ни один путь наружу не отдает
  // рейс из нуля отсеков: пустой пул, пул из одних тяжелых товаров при пустом
  // складе, исчерпанные попытки — все сводятся сюда.
  //
  // Сюда же уходит набор, не добравший покрытия ни за одну попытку: канон 1.3
  // считает такой заказ невалидным («if not isValidOrder → fallbackMinimalOrder»),
  // а 1.6 разрешает урезать форму ради И-8. Анти-повтор в проверку не входит —
  // приоритет фолбэков канона ставит покрытие выше повтора.
  const covered = best.length > 0 && easyRatio(best, ctx.warehouse) >= COVERAGE_MIN.shuttle;
  const slots = covered
    ? best
    : fallbackMinimalSlots(ctx, best.length === 0 ? 'empty_pool' : 'unresolvable_invariant');

  return {
    state: 'ORDER',
    slots,
    trip_min,
    departed_at: 0,
    arrives_at: 0,
    cooldown_until: 0,
    is_first_trip: ctx.is_first_trip,
    arrival_no: ctx.arrival_no,
  };
}

/** И-3: XP за отсек = базовый XP товара x K x количество. K шаттла = 8. */
export function slotXp(slot: ShuttleSlot): number {
  return GOODS[slot.good_id].base_xp * TRANSPORT_XP_K.shuttle * slot.qty_required;
}

export function tripXp(trip: ShuttleTrip): number {
  return trip.slots.reduce((sum, s) => sum + slotXp(s), 0);
}

/**
 * Цена докупки остатка отсека: И-4, rush-cost цепочки x 1.2 с округлением.
 *
 * Складской остаток в цену НЕ входит, хотя `rushCost` умеет его вычитать. Так
 * считается ускорение производства, где склад и есть то, что производить не
 * надо. Докупка отсека — другой случай: по И-12 она кладет товар прямо в отсек
 * мимо склада (`buyoutSlot` ниже), закрывает отсек ЦЕЛИКОМ и складского
 * остатка не трогает. Скидка за остаток, который никуда не делся, открывает
 * ровно тот арбитраж, против которого написана И-12, только со стороны цены:
 * комбинезон при нужде 2 и одном на складе стоил 800 вместо 1600, а
 * сэкономленная штука оставалась свободной и продавалась.
 *
 * Склад остается в сигнатуре: цена считается по состоянию отсека, а не по
 * полке, и вызывающему не нужно знать об этом различии.
 */
export function slotBuyoutPrice(slot: ShuttleSlot, _warehouse?: WarehouseState): number {
  const short = slotShort(slot);
  if (short === 0) return 0;
  return buyoutPrice(slot.good_id, short);
}

export function allSlotsLoaded(trip: ShuttleTrip): boolean {
  return trip.slots.length > 0 && trip.slots.every((s) => slotShort(s) === 0);
}

/**
 * Отправка. Не действие игрока: вызывается из погрузки, когда закрылся
 * последний отсек (п.2.3). Здесь же роллятся и фиксируются награды.
 */
function depart(trip: ShuttleTrip, warehouse: WarehouseState, now: number, drop: DropContext) {
  for (const slot of trip.slots) {
    // Докупленное (И-12) в склад не заходило — списывать оттуда нечего.
    if (slot.filled_by === 'purchase') continue;
    shipReserved(warehouse, slot.good_id, slot.qty_required);
  }

  const roll = rollArrival(trip.slots.length, drop);
  trip.slots.forEach((slot, i) => {
    slot.reward = roll.modules[i] ?? null;
    slot.floor_forced = roll.floor_forced_slot === i;
  });

  trip.state = 'IN_TRANSIT';
  trip.departed_at = now;
  trip.arrives_at = now + trip.trip_min * 60;
  return roll;
}

export interface LoadResult {
  ok: boolean;
  loaded: number;
  departed: boolean;
  /** Обновленные счетчики дропа — их хранит игрок, а не рейс. */
  drop_state: ReturnType<typeof rollArrival> | null;
}

/**
 * Погрузка отсека со склада. Частичная допустима (п.6.2): списывает столько,
 * сколько есть, отсек остается открытым до догрузки. Запрет частичной погрузки
 * заставлял бы игрока копить весь объем целиком, а это ровно та фрустрация,
 * против которой написан И-8.
 */
export function loadSlot(
  trip: ShuttleTrip,
  idx: number,
  warehouse: WarehouseState,
  now: number,
  drop: DropContext,
): LoadResult {
  const empty: LoadResult = { ok: false, loaded: 0, departed: false, drop_state: null };
  if (trip.state !== 'ORDER') return empty;

  const slot = trip.slots[idx];
  if (!slot) return empty;

  const short = slotShort(slot);
  if (short === 0) return empty;

  const take = Math.min(short, availableOf(warehouse, slot.good_id));
  if (take <= 0) return empty;
  if (!reserve(warehouse, slot.good_id, take)) return empty;

  slot.qty_filled += take;
  if (slot.filled_by === null) slot.filled_by = 'self';

  const departed = allSlotsLoaded(trip);
  const drop_state = departed ? depart(trip, warehouse, now, drop) : null;
  return { ok: true, loaded: take, departed, drop_state };
}

/**
 * Докупка остатка за изотопы. И-12: товар зачисляется прямо в отсек, минуя
 * склад, и не может быть изъят обратно. Это закрывает арбитраж «докупить
 * дешево тут, скормить другой механике там».
 *
 * Единица докупки — весь незакрытый остаток отсека (`slotShort`), а не разница
 * с полкой. ТЗ шаттла 6.2 подписывает кнопку «Докупить {qty-stock}», и это
 * место расходилось само с собой: домен закрывал остаток целиком, а цену брал
 * за разницу. Из двух прочтений выбрано «остаток», потому что второе половину
 * И-12 отменяет: чтобы {qty-stock} было честной единицей, докупка обязана
 * дополнительно списать складскую часть, то есть пройти через склад. Тот же
 * абзац ТЗ описывает действие как «мгновенное заполнение остатка», а соседняя
 * кнопка «Погрузить {stock}» и есть способ отдать полку самому.
 */
export function buyoutSlot(
  trip: ShuttleTrip,
  idx: number,
  warehouse: WarehouseState,
  now: number,
  drop: DropContext,
): LoadResult & { price: number } {
  const empty = { ok: false, loaded: 0, departed: false, drop_state: null, price: 0 };
  if (trip.state !== 'ORDER') return empty;

  const slot = trip.slots[idx];
  if (!slot) return empty;

  const short = slotShort(slot);
  if (short === 0) return empty;

  const price = slotBuyoutPrice(slot);
  slot.qty_filled += short;
  slot.filled_by = 'purchase';

  const departed = allSlotsLoaded(trip);
  const drop_state = departed ? depart(trip, warehouse, now, drop) : null;
  return { ok: true, loaded: short, departed, drop_state, price };
}

/** И-6: цена скипа рейса. Растет с числом отсеков, падает с остатком таймера. */
export function skipPrice(trip: ShuttleTrip, now: number): number {
  if (trip.state !== 'IN_TRANSIT') return 0;
  const remaining_min = Math.max(0, (trip.arrives_at - now) / 60);
  return shuttleSkipPrice(remaining_min, trip.trip_min, trip.slots.length);
}

/** Перевод рейса в прибытие по времени. Ленивый, как и остальные таймеры. */
export function refreshTrip(trip: ShuttleTrip, now: number): ShuttleTrip {
  if (trip.state === 'IN_TRANSIT' && now >= trip.arrives_at) {
    trip.state = 'ARRIVED';
  }
  return trip;
}

/** Скип: рейс считается прибывшим немедленно. Цену списывает вызывающий. */
export function skipFlight(trip: ShuttleTrip, now: number): boolean {
  if (trip.state !== 'IN_TRANSIT') return false;
  trip.arrives_at = now;
  trip.state = 'ARRIVED';
  return true;
}

/** Вскрытие одного контейнера. Возвращает модуль или null, если брать нечего. */
export function collectContainer(trip: ShuttleTrip, idx: number): ModuleId | null {
  if (trip.state !== 'ARRIVED') return null;
  const slot = trip.slots[idx];
  if (!slot || slot.collected || slot.reward === null) return null;
  slot.collected = true;
  return slot.reward;
}

export function allCollected(trip: ShuttleTrip): boolean {
  return trip.slots.every((s) => s.collected);
}

/** Все контейнеры вскрыты — станция уходит в кулдаун перед новым заказом. */
export function startCooldown(trip: ShuttleTrip, now: number): void {
  trip.state = 'COOLDOWN';
  trip.cooldown_until = now + COLLECT_COOLDOWN_MIN * 60;
}
