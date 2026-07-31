/**
 * Дроп-роллер строй-модулей. Общий движок ([[tz-common-systems-mars]]),
 * конфиг шаттла — [[tz-shuttle-mars]] раздел 5, инварианты И-7 и И-11.
 *
 * Роллер вызывается по разу НА ОТСЕК, а не один раз на рейс: 1 отсек = 1
 * контейнер = 1 модуль. Исход фиксируется в момент отправки, не в момент сбора —
 * иначе переустановка клиента или перевод часов дают бесплатный реролл.
 *
 * Три поправки к чистому рандому, и все три заявлены игроку, а не спрятаны:
 * pity (И-7) поднимает вес того, что давно не выпадало; анти-стокпайл (И-7)
 * режет вес того, чего и так навалом; floor guarantee (И-11) чинит худший
 * случай. Скрытый pity генерирует волны негатива, когда игроки его вычисляют, —
 * поэтому он часть спеки, а не секрет.
 */

import {
  ANTISTOCKPILE_FACTOR,
  ANTISTOCKPILE_THRESHOLD,
  FLOOR_GUARANTEE_ALLOWED_TIERS,
  FLOOR_GUARANTEE_MIN_GAP,
  FLOOR_GUARANTEE_WINDOW,
  FRONT_LOADED_LUCK_ARRIVALS,
  PITY_K,
  PITY_MULTIPLIER,
  TIER_WEIGHTS,
} from './config/economy';
import { ALL_MODULE_IDS, MODULE_TIER_POOL, MODULES } from './config/modules';
import type { ModuleId } from './types';

export type ModuleCounts = Partial<Record<ModuleId, number>>;

export interface DropContext {
  /** Счетчик на пару (игрок, модуль): сколько прибытий подряд не выпадал (И-7). */
  pity: ModuleCounts;
  /** Что лежит на складе модулей. */
  stock: ModuleCounts;
  /** Чего не хватает активным и доступным стройкам: модуль -> дефицит. */
  need: ModuleCounts;
  /** Открыт ли гейтовый тир (буровая, реактор). В MVP обычно false. */
  gated_open: boolean;
  /** Номер прибытия игрока, считая с 1. */
  arrival_no: number;
  /** Сколько прибытий подряд ДО этого не дали ни одного нужного модуля (И-11). */
  arrivals_without_needed: number;
  /** Номер прибытия, на котором гарантия срабатывала в прошлый раз. 0 — ни разу. */
  last_floor_arrival: number;
  rng: () => number;
}

export interface ArrivalRoll {
  /** По модулю на отсек, в порядке отсеков. */
  modules: ModuleId[];
  /** Индекс отсека, который переписала гарантия. null — гарантия не срабатывала. */
  floor_forced_slot: number | null;
  next_pity: ModuleCounts;
  next_arrivals_without_needed: number;
  next_last_floor_arrival: number;
}

/** Базовый вес модуля: вес тира, поделенный между модулями пула поровну. */
function baseWeight(module_id: ModuleId): number {
  const tier = MODULES[module_id].tier;
  const pool = MODULE_TIER_POOL[tier];
  if (pool.length === 0) return 0;
  return TIER_WEIGHTS[tier] / pool.length;
}

/**
 * Вес модуля с поправками И-7. Экспортирован ради тестов: правило «pity
 * поднимает вес» проверяется числом, а не наблюдением за тысячей роллов.
 */
export function moduleWeight(module_id: ModuleId, ctx: DropContext): number {
  if (MODULES[module_id].tier === 'gated' && !ctx.gated_open) return 0;

  let weight = baseWeight(module_id);
  const needed = ctx.need[module_id] ?? 0;
  const owned = ctx.stock[module_id] ?? 0;

  // Pity — только для того, что реально требуется хотя бы одной стройке.
  // Иначе счетчик разгонял бы вес модуля, который игроку некуда девать.
  if (needed > 0 && (ctx.pity[module_id] ?? 0) >= PITY_K) {
    weight *= PITY_MULTIPLIER;
  }

  // Анти-стокпайл. При нулевой потребности порог равен нулю, и любой запас
  // считается излишком — это и есть смысл правила.
  if (owned > needed * ANTISTOCKPILE_THRESHOLD) {
    weight *= ANTISTOCKPILE_FACTOR;
  }

  return weight;
}

function pickWeighted(ctx: DropContext): ModuleId {
  const weights = ALL_MODULE_IDS.map((id) => moduleWeight(id, ctx));
  const total = weights.reduce((a, b) => a + b, 0);

  // Все веса ноль возможно только при пустом пуле — берем первый базовый,
  // чтобы отсек не приехал пустым. Контейнер без модуля читается как поломка.
  if (total <= 0) return MODULE_TIER_POOL.basic[0] ?? 'panel';

  let roll = ctx.rng() * total;
  let last_eligible: ModuleId = MODULE_TIER_POOL.basic[0] ?? 'panel';
  for (let i = 0; i < ALL_MODULE_IDS.length; i++) {
    const weight = weights[i] ?? 0;
    if (weight <= 0) continue; // нулевой вес не может выпасть ни при каком ролле
    last_eligible = ALL_MODULE_IDS[i]!;
    roll -= weight;
    if (roll <= 0) return last_eligible;
  }
  // Сюда попадаем только при rng ровно 1 или накопленной ошибке округления.
  // Запасной вариант обязан быть последним НЕНУЛЕВЫМ, а не последним в списке:
  // хвост списка — гейтовые модули, и запасная ветка выдавала бы закрытый
  // контент по краевому значению. Падения нет, поломка тихая.
  return last_eligible;
}

/** Покрывает ли склад модулей всю потребность активных строек. */
export function stockCoversNeed(stock: ModuleCounts, need: ModuleCounts): boolean {
  for (const id of ALL_MODULE_IDS) {
    if ((need[id] ?? 0) > (stock[id] ?? 0)) return false;
  }
  return true;
}

/**
 * Разрешено ли гарантии сработать на этом прибытии.
 *
 * Отдельная функция, потому что условий пять и каждое закрывает свой эксплойт.
 * Слепить их в одно `if` внутри роллера означало бы, что при следующей правке
 * ни один из них нельзя проверить тестом по отдельности.
 */
export function floorGuaranteeAllowed(ctx: DropContext, rolled: ModuleId[]): boolean {
  const needed_ids = ALL_MODULE_IDS.filter(
    (id) => (ctx.need[id] ?? 0) > 0 && FLOOR_GUARANTEE_ALLOWED_TIERS.includes(MODULES[id].tier),
  );
  if (needed_ids.length === 0) return false;

  // Прибытие уже дало нужное — чинить нечего.
  if (rolled.some((id) => (ctx.need[id] ?? 0) > 0)) return false;

  // Front-loaded удача: первые прибытия форсируют гарантию поверх И-11,
  // не спрашивая ни серию неудач, ни разрыв между срабатываниями.
  if (ctx.arrival_no <= FRONT_LOADED_LUCK_ARRIVALS) return true;

  // Гарантия чинит нехватку дропа, а не выдает модули впрок.
  if (stockCoversNeed(ctx.stock, ctx.need)) return false;

  // Окно И-11: два прибытия подряд без нужного модуля, третье форсирует.
  if (ctx.arrivals_without_needed < FLOOR_GUARANTEE_WINDOW - 1) return false;

  // Анти-эксплойт «держи стройку голодной»: не чаще раза на пять прибытий.
  if (
    ctx.last_floor_arrival > 0 &&
    ctx.arrival_no - ctx.last_floor_arrival < FLOOR_GUARANTEE_MIN_GAP
  )
    return false;

  return true;
}

/**
 * Прибытие рейса: по модулю на каждый отсек, плюс возможная форс-выдача.
 *
 * Гарантия переписывает результат одного уже сгенерированного отсека, а не
 * добавляет бонусный: иначе число контейнеров перестало бы совпадать с числом
 * отсеков, и обещание «1 отсек = 1 контейнер» превратилось бы в ложь.
 */
export function rollArrival(slot_count: number, ctx: DropContext): ArrivalRoll {
  const modules: ModuleId[] = [];
  for (let i = 0; i < slot_count; i++) modules.push(pickWeighted(ctx));

  let floor_forced_slot: number | null = null;
  let last_floor_arrival = ctx.last_floor_arrival;

  if (floorGuaranteeAllowed(ctx, modules)) {
    const candidates = ALL_MODULE_IDS.filter(
      (id) =>
        (ctx.need[id] ?? 0) > 0 && FLOOR_GUARANTEE_ALLOWED_TIERS.includes(MODULES[id].tier),
    );
    // Из нужного выдаем самое дефицитное — так гарантия закрывает узкое место,
    // а не самый частый базовый модуль, которого и без нее нападает.
    const forced = candidates.reduce((worst, id) =>
      (ctx.need[id] ?? 0) > (ctx.need[worst] ?? 0) ? id : worst,
    );
    floor_forced_slot = modules.length - 1;
    modules[floor_forced_slot] = forced;
    last_floor_arrival = ctx.arrival_no;
  }

  const dropped = new Set(modules);
  const next_pity: ModuleCounts = {};
  for (const id of ALL_MODULE_IDS) {
    next_pity[id] = dropped.has(id) ? 0 : (ctx.pity[id] ?? 0) + 1;
  }

  const gave_needed = modules.some((id) => (ctx.need[id] ?? 0) > 0);

  return {
    modules,
    floor_forced_slot,
    next_pity,
    next_arrivals_without_needed: gave_needed ? 0 : ctx.arrivals_without_needed + 1,
    next_last_floor_arrival: last_floor_arrival,
  };
}
