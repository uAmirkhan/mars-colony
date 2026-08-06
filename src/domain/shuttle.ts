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
  ACHIEVABILITY_CHECK,
  COLLECT_COOLDOWN_MIN,
  COVERAGE_MIN,
  EASY_PRODUCE_MAX_MIN,
  FTUE_FIRST_TRIP_TIMER_MIN,
  flightTimerMin,
  GEN_MAX_ATTEMPTS,
  MAX_DEFICIT_SLOTS,
  ORDER_FEASIBILITY_DEADLINE_SHARE,
  type OrderGenerationDegradedReason,
  REPEAT_CAP,
  SLOT_COUNT_MAX,
  SLOT_COUNT_MIN,
  shuttleSkipPrice,
  slotCountFor,
  XP_MULTIPLIER_K,
} from './config/economy';
import { ALL_GOOD_IDS, GOOD_BASE_QTY, GOODS, slotQuantity } from './config/goods';
import {
  type DeficitLockState,
  isDeficitLockedByOtherMechanic,
  registerDeficitLock,
  releaseDeficitLock,
} from './deficitlock';
import { applyPinch, availableGoodsFor } from './drone';
import { type DropContext, rollArrival } from './droproller';
import { buyoutPrice, productionTimeMinutes, totalProductionMinutes } from './rushcost';
import type { GoodId, ModuleId } from './types';
import { availableOf, reserve, shipReserved, type WarehouseState } from './warehouse';

export type ShuttleState = 'ORDER' | 'IN_TRANSIT' | 'ARRIVED' | 'COOLDOWN';

export interface ShuttleSlot {
  idx: number;
  good_id: GoodId;
  qty_required: number;
  /** Сколько единиц в отсеке всего: погруженное со склада плюс докупленное. */
  qty_filled: number;
  /**
   * Источник ПОСЛЕДНЕГО, закрывающего взноса — ровно то, что задает ТЗ шаттла
   * 2.3 (`slot.filled_by = fillSource`) и модель данных каркаса п.9. Показывает
   * отсек игроку, но на вопрос «сколько в отсеке складского» не отвечает.
   */
  filled_by: 'self' | 'purchase' | null;
  /**
   * Сколько единиц отсека пришло докупкой за изотопы (И-12), минуя склад.
   * Остальное (`qty_filled - qty_purchased`) склад держит в `reserved`, и при
   * отправке оно обязано физически уехать.
   *
   * Отдельное число, а не флаг, потому что взносов в отсек два, и частичная
   * погрузка разрешена явно (ТЗ шаттла 6.2: «Погрузить {stock}» списывает
   * наличное, отсек остается открытым до догрузки). Одного `filled_by` на два
   * взноса не хватало: докупка перетирала им метку `self`, отправка читала флаг
   * как признак ВСЕГО отсека и пропускала списание целиком — уже
   * зарезервированный товар не уезжал и не возвращался, вместимость склада
   * терялась навсегда (Д-1, Д-18). Тот же разъезд у дрона закрыт тем же
   * приемом: позиция помнит, чем закрыта, и отправка списывает только свое.
   *
   * Признак `has_purchased_units` (ТЗ дрона 6.2) — это `qty_purchased > 0`;
   * отдельным полем не хранится, чтобы два описания одного факта не разошлись.
   *
   * Поле необязательное сознательно: рейс переживает перезагрузку страницы, и
   * сейв, записанный до этой правки, его не несет. Отсутствие читается как ноль
   * — «докупки не было», то есть прежнее поведение обычной погрузки.
   */
  qty_purchased?: number;
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
  /** И-13: локи дефицита от других механик. Опционально — старые вызовы не ломаются. */
  deficit_locks?: DeficitLockState;
  /** Момент генерации — вход TTL И-13. */
  now?: number;
}

function slotShort(slot: ShuttleSlot): number {
  return Math.max(0, slot.qty_required - slot.qty_filled);
}

/** Докупленная часть отсека (И-12). Сейв без поля читается как «докупки не было». */
function purchasedIn(slot: ShuttleSlot): number {
  return slot.qty_purchased ?? 0;
}

/**
 * Складская часть отсека: то, что лежит в `reserved` и обязано уехать при
 * отправке. Считается вычитанием докупленного, а не по метке `filled_by`:
 * метка описывает один взнос, а отсек собирается из двух.
 */
function stockedIn(slot: ShuttleSlot): number {
  return Math.max(0, slot.qty_filled - purchasedIn(slot));
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

/**
 * И-10: суммарное время рейса через общую `totalProductionMinutes`
 * (`rushcost.ts`). Отдельная обертка, а не прямой вызов по месту: канон
 * задает `totalProductionMinutes(positions)` над парами `{good_id, qty}`
 * (общая форма для позиции дрона и отсека шаттла), а `ShuttleSlot` несет
 * количество под именем `qty_required`, не `qty` (модель данных каркаса п.9
 * не знает поля `qty_required` — это имя самого отсека). Маппинг в одном
 * месте, а не на каждом вызове: два вызова с разным написанием — тот самый
 * разъезд имен, который проект уже ловил трижды.
 */
export function tripProductionMinutes(slots: ShuttleSlot[], warehouse: WarehouseState): number {
  return totalProductionMinutes(
    slots.map((s) => ({ good_id: s.good_id, qty: s.qty_required })),
    warehouse,
  );
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
      qty_purchased: 0,
      filled_by: null,
      reward: null,
      collected: false,
      floor_forced: false,
    },
  ];
}

/**
 * И-10 (реализуемость заказа): канон 1.4, `rebalanceForAchievability`.
 * Срабатывает, когда `totalProductionMinutes(slots)` превышает бюджет — сумма
 * времени производства по самому долгому зданию (`rushcost.ts`) больше
 * `budget = window_minutes(level) x ORDER_FEASIBILITY_DEADLINE_SHARE`.
 *
 * Приоритет фолбэков канона (1.3) ставит достижимость выше покрытия/анти-
 * повтора — эта функция вправе снова ухудшить `easyRatio`/`repeatRatio`, если
 * иначе в бюджет не уложиться.
 *
 * Экспортирована, а не спрятана: тот же случай, что `applyPinch` — сложный
 * численный алгоритм читается и тестируется как отдельная функция, а не только
 * сквозь стохастику полного генератора.
 *
 * Шаг урезания количества — 1 единица за итерацию. Канон называет
 * `qtyReductionStep`, но не задает ему числа ни в одной конфиг-таблице
 * (раздел 1.7 общих подсистем его не перечисляет) — величина шага оставлена
 * реализации. Единичный шаг ищет минимально достаточное количество перебором,
 * тем же приемом, что `maxEasyQty` дрона, а не додумывает магическое число.
 *
 * Предохранитель `guard` — тот же смысл, что `GEN_MAX_ATTEMPTS` у генератора:
 * при узком пуле (нет ни одной ненайденной альтернативы) урезание доходит до
 * пола и дальше двигаться некуда — цикл обязан завершиться, а не крутиться
 * вечно. Бюджет в этом случае может остаться превышен — это ЗАДОКУМЕНТИРОВАННОЕ
 * ограничение (реестр [[spec-prototype-build]] раздел 8, пункт 24), а не
 * тихий баг: тот же класс компромисса, что уже принят для
 * `productionTimeMinutes` (пункт 9 реестра) — направление безопасное
 * (осторожнее, не агрессивнее), полная гарантия недостижима при узком пуле.
 */
export function rebalanceForAchievability(
  slots: ShuttleSlot[],
  warehouse: WarehouseState,
  budget: number,
  available_goods: GoodId[],
): ShuttleSlot[] {
  const used = new Set(slots.map((s) => s.good_id));
  const guard_limit = GEN_MAX_ATTEMPTS * SLOT_COUNT_MAX;

  for (let guard = 0; guard < guard_limit; guard++) {
    if (slots.length === 0) break;
    if (tripProductionMinutes(slots, warehouse) <= budget) break;

    // Самая долгая по времени позиция — канон: `argmax(positions,
    // p -> productionTimeMinutes(...))`.
    let target_idx = 0;
    let target_minutes = -1;
    for (let i = 0; i < slots.length; i++) {
      const minutes = productionTimeMinutes(
        slots[i]!.good_id,
        slots[i]!.qty_required,
        warehouse,
      );
      if (minutes > target_minutes) {
        target_minutes = minutes;
        target_idx = i;
      }
    }
    const target = slots[target_idx]!;
    const floor_qty = GOOD_BASE_QTY[target.good_id].min;

    if (target.qty_required > floor_qty) {
      target.qty_required -= 1;
      continue;
    }

    // Дальше некуда резать количество — меняем сам товар на самую быструю
    // доступную альтернативу пула, которой еще нет в рейсе.
    const candidates = available_goods.filter((id) => id !== target.good_id && !used.has(id));
    if (candidates.length === 0) break; // пул не дает альтернативы — решает реестр 8.24

    const replacement = [...candidates].sort((a, b) => {
      const by_time = GOODS[a].prod_time_sec - GOODS[b].prod_time_sec;
      return by_time !== 0 ? by_time : GOODS[a].price - GOODS[b].price;
    })[0]!;

    used.delete(target.good_id);
    used.add(replacement);
    target.good_id = replacement;
    target.qty_required = GOOD_BASE_QTY[replacement].min;
  }

  return slots;
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
        //
        // И-13: товар, уже держащий дефицитную позицию у ДРУГОЙ механики (в
        // этом коде — у дрона), не может стать дефицитным отсеком и здесь.
        // Тот же путь, что исчерпанный бюджет дефицита: канон 1.3 ставит
        // изоляцию дефицита последней из фолбэков — она просто не создает
        // дефицитную позицию, ничего дальше не даунгрейдит.
        const locked_elsewhere = isDeficitLockedByOtherMechanic(
          ctx.deficit_locks ?? {},
          good_id,
          'shuttle',
          ctx.now ?? 0,
        );
        if (deficit_used >= MAX_DEFICIT_SLOTS || locked_elsewhere) {
          if (have === 0) continue;
          qty = have;
        } else {
          deficit_used += 1;
          // Канон [[tz-common-systems-mars]] 1.4, `PINCH_MODE = "absolute"` —
          // строка помечена «дрон, шаттл», то есть правило одно на обе
          // механики: `stock + clamp(targetQty - stock, PINCH_MIN, PINCH_MAX)`.
          // Здесь стояло `have + random(PINCH_MIN..PINCH_MAX)`: количество,
          // посчитанное `slotQuantity` (каркас 3.1), выбрасывалось целиком, и
          // дефицитный отсек терял `bracket_mult` — на двадцатом уровне просил
          // бы столько же, сколько на втором. Реализация одна (`applyPinch`
          // дрона), а не копия: две копии одного правила и разъехались.
          qty = applyPinch(have, qty);
        }
      }

      slots.push({
        idx: slots.length,
        good_id,
        qty_required: qty,
        qty_filled: 0,
        qty_purchased: 0,
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
  //
  // Причина деградации различается, и различие несущее: канон 1.8 держит для
  // события `order_generation_degraded` перечисление
  // `empty_pool | max_attempts | unresolvable_invariant`, и метрика по нему
  // отвечает на два разных вопроса — «контент недостроен» или «сломался
  // генератор». Раньше здесь стоял литерал `empty_pool` на любой пустой набор:
  // непустой пул, из которого никто не прошел отбор за все `GEN_MAX_ATTEMPTS`
  // попыток (канон 1.6, «все попытки цикла исчерпаны»), приезжал в метрику
  // пустым пулом, и сигнал бага генератора был неотличим от нехватки контента.
  // Форма та же, что у дрона: движок один, расходиться конструкциям незачем.
  const empty = best.length === 0;
  const covered = !empty && easyRatio(best, ctx.warehouse) >= COVERAGE_MIN.shuttle;
  const reason: OrderGenerationDegradedReason = empty
    ? ctx.available_goods.length === 0
      ? 'empty_pool'
      : 'max_attempts'
    : 'unresolvable_invariant';
  let slots = covered ? best : fallbackMinimalSlots(ctx, reason);

  // И-10 (реализуемость): канон 1.3, шаг после покрытия/анти-повтора.
  //
  // **Решение исполнителя, требует утверждения владельцем** (реестр
  // [[spec-prototype-build]] раздел 8, пункт 24): проверка НЕ применяется к
  // `is_first_trip`. FTUE — явный «override обычного генератора» (ТЗ шаттла
  // 2.1: свои `COVERAGE_MIN`/`MAX_DEFICIT_SLOTS`), достижимость — часть того
  // же штатного движка (канон 1.3, тот же шаг). На пятом уровне пул из трех
  // доступных кропов (водоросли/соя/грибы) структурно не укладывается в
  // укороченный FTUE-таймер (12 мин x 0.6 = 7.2) даже на полу количества —
  // урезать/заменить нечем, все три уже в рейсе.
  //
  // Бюджет считается от `flightTimerMin(level)` — обычного таймера брекета
  // ПО УРОВНЮ, а не от фактического `trip_min` рейса. Для не-FTUE рейсов это
  // одно и то же число (`trip_min` там и есть `flightTimerMin(level)`),
  // расхождение проявляется только у FTUE, откуда и берется чистота развязки.
  if (ACHIEVABILITY_CHECK.shuttle && !ctx.is_first_trip) {
    const budget = flightTimerMin(ctx.level) * ORDER_FEASIBILITY_DEADLINE_SHARE;
    if (tripProductionMinutes(slots, ctx.warehouse) > budget) {
      slots = rebalanceForAchievability(slots, ctx.warehouse, budget, ctx.available_goods);
    }
  }

  // И-13: регистрация — ПОСЛЕ того, как состав рейса окончательно выбран
  // (после ребаланса И-10: он мог поменять, какой именно товар остался
  // дефицитным). Тот же порядок, что у дрона: лочим то, что реально уехало
  // игроку, а не кандидатов промежуточных попыток.
  for (const s of slots) {
    if (!isEasy(s.good_id, s.qty_required, ctx.warehouse)) {
      registerDeficitLock(
        ctx.deficit_locks ?? {},
        s.good_id,
        'shuttle',
        'shuttle:trip',
        ctx.now ?? 0,
      );
    }
  }

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
  return GOODS[slot.good_id].base_xp * XP_MULTIPLIER_K.shuttle * slot.qty_required;
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
 *
 * И-13: отправка — терминальное событие заказа-владельца для шаттла (канон
 * 1.5, перечисление «deliver/discard/depart/deliverContainer»). Состав рейса
 * зафиксирован окончательно в момент отправки, и дальше локи, поставленные
 * при генерации ЭТОГО рейса, снимаются здесь же — новый рейс появится не
 * раньше кулдауна и заново решит, что ему нужно.
 */
function depart(
  trip: ShuttleTrip,
  warehouse: WarehouseState,
  now: number,
  drop: DropContext,
  deficit_locks: DeficitLockState = {},
) {
  for (const slot of trip.slots) {
    releaseDeficitLock(deficit_locks, slot.good_id, 'shuttle');
  }
  for (const slot of trip.slots) {
    // Уезжает ровно складская часть отсека: докупленное (И-12) в склад не
    // заходило, списывать оттуда нечего. Отсек мог быть закрыт в два приема —
    // сколько нашлось на полке плюс докупка остатка, — поэтому считается
    // количество, а не читается метка последнего взноса. Пока здесь стояло
    // `if (filled_by === 'purchase') continue`, докупка поверх частичной
    // погрузки уводила отсек мимо списания ЦЕЛИКОМ: рейс улетал, отсека больше
    // не было, а резерв оставался висеть на складе без владельца — снять его
    // нечем, вместимость терялась до конца игры (Д-1, Д-18).
    const from_stock = stockedIn(slot);
    if (from_stock > 0) shipReserved(warehouse, slot.good_id, from_stock);
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
  /** И-13: снимается при отправке (см. `depart`). Опционально — старые вызовы не ломаются. */
  deficit_locks: DeficitLockState = {},
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
  const drop_state = departed ? depart(trip, warehouse, now, drop, deficit_locks) : null;
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
  /** И-13: снимается при отправке (см. `depart`). Опционально — старые вызовы не ломаются. */
  deficit_locks: DeficitLockState = {},
): LoadResult & { price: number } {
  const empty = { ok: false, loaded: 0, departed: false, drop_state: null, price: 0 };
  if (trip.state !== 'ORDER') return empty;

  const slot = trip.slots[idx];
  if (!slot) return empty;

  const short = slotShort(slot);
  if (short === 0) return empty;

  const price = slotBuyoutPrice(slot);
  slot.qty_filled += short;
  // Взнос запоминается количеством и НЕ затирает уже погруженное со склада:
  // метка `filled_by` меняется (закрывающий взнос действительно докупка, ТЗ
  // 2.3), а списание при отправке идет по `qty_purchased`.
  slot.qty_purchased = purchasedIn(slot) + short;
  slot.filled_by = 'purchase';

  const departed = allSlotsLoaded(trip);
  const drop_state = departed ? depart(trip, warehouse, now, drop, deficit_locks) : null;
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
