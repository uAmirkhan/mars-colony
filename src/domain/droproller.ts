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
  WAREHOUSE_AVG_WINDOW_SEC,
} from './config/economy';
import { ALL_MODULE_IDS, MODULE_TIER_POOL, MODULES } from './config/modules';
import type { ModuleId, ModuleTier } from './types';

export type ModuleCounts = Partial<Record<ModuleId, number>>;

/**
 * Скользящее среднее склада модулей за `WAREHOUSE_AVG_WINDOW_SEC` (канон
 * `warehouse_avg_24h`, [[tz-common-systems-mars]] 2.4). Читает анти-стокпайл,
 * а не мгновенный остаток — иначе «продал все перед прибытием» мгновенно
 * снимал бы штраф, хотя весь день склад был затоварен (канон 2.7,
 * `antistockpile_triggered`: «проверка, что "продал перед прибытием" не
 * читерит»).
 *
 * Прототип не гоняет настоящий фоновый job (каркас раздел 11 требует его для
 * прода, здесь сервера нет, [[spec-prototype-build]] эмулирует его тиком
 * стора): вместо периодической job-задачи среднее пересчитывается тем же
 * `tick(now)`, что двигает остальные таймеры игры — это тот же паттерн
 * «ленивый пересчет по времени чтения», которым в проекте уже посчитаны
 * грядки и стройка (`refreshField`, `refreshBuilds`).
 */
export interface WarehouseAvgState {
  /** Текущее значение скользящего среднего по каждому модулю. */
  avg: ModuleCounts;
  /** Метка времени последнего пересчета (секунды, как везде в игре). */
  updated_at: number;
}

/** Новый счетчик среднего: на старте среднее равно текущему остатку. */
export function createWarehouseAvg(stock: ModuleCounts, now: number): WarehouseAvgState {
  return { avg: { ...stock }, updated_at: now };
}

/**
 * Продвигает среднее вперед во времени методом экспоненциального сглаживания
 * (EMA): вес свежего замера растет пропорционально прошедшему времени
 * относительно окна. Дискретизация вида `avg += (cur - avg) * dt/window` —
 * стандартная форма EMA, и при частых мелких шагах (тик стора раз в 500мс,
 * `App.tsx`) она сходится к тому же результату, что непрерывная версия.
 *
 * Почему это закрывает эксплойт «продал перед самым прибытием»: между
 * последним тиком и моментом отправки рейса проходят секунды, а не сутки —
 * `elapsed/window` крошечный, и мгновенная продажа склада почти не двигает
 * среднее. Оффлайн дольше окна (`elapsed >= window`) дает `alpha = 1` —
 * среднее становится равно факту, что и есть честный ответ на вопрос «а что
 * было на складе весь последний день», если день был один и тот же остаток.
 */
export function advanceWarehouseAvg(
  state: WarehouseAvgState,
  stock: ModuleCounts,
  now: number,
): WarehouseAvgState {
  const elapsed = now - state.updated_at;
  if (elapsed <= 0) return state;

  const alpha = Math.min(1, elapsed / WAREHOUSE_AVG_WINDOW_SEC);
  const avg: ModuleCounts = {};
  for (const id of ALL_MODULE_IDS) {
    const prev = state.avg[id] ?? 0;
    const cur = stock[id] ?? 0;
    avg[id] = prev + (cur - prev) * alpha;
  }
  return { avg, updated_at: now };
}

/**
 * Одна активная (доступная к постройке) стройка со своим независимым
 * состоянием floor guarantee. Канон 2.6 п.2 и 2.8: «каждая ведет свой
 * независимый `floor_guarantee_window_counter`/`last_triggered_at`» — счетчик
 * на СТРОЙКУ, не на игрока, иначе две стройки подряд делят один счетчик и
 * гарантия срабатывает не там, где обещана.
 */
export interface ConstructionNeed {
  /**
   * Идентификатор стройки — непрозрачная строка для роллера (стор передает
   * `BuildKind`, `construction.ts`). Ключ, по которому ведутся отдельные
   * счетчики окна И-11.
   */
  construction_id: string;
  /**
   * Чего этой КОНКРЕТНОЙ стройке не хватает прямо сейчас: рецепт минус склад
   * минус уже докупленное под нее (И-12). Не путать с агрегатным
   * `DropContext.need` — тот валовый и суммирован по всем активным стройкам
   * (канон 2.3, `activeContextNeed`, читает И-7). Здесь — уже дефицит одной
   * стройки (канон 2.6 п.3, `missingFor`), поэтому «стройка покрыта» — это
   * просто «дефицит пуст», без отдельной сверки со складом внутри роллера.
   *
   * Докупленное (`BuildSlot.purchased`, И-12) в этот дефицит уже НЕ входит —
   * `missingFor` его вычитает. Открытый вопрос [[spec-prototype-build]]
   * раздел 8 пункт 19 («считается ли докупленный модуль покрытием для И-7 и
   * И-11») решен здесь в предложенную сторону: покрытие И-11 читает
   * докупленное как полученное (иначе игрок, закрывший рецепт изотопами,
   * продолжал бы получать по гарантии то, за что уже заплатил), а валовая
   * `DropContext.need` (И-7, аналог `activeNeed`) остается БЕЗ вычета
   * докупленного — иначе анти-стокпайл сравнивал бы запас сам с собой, тот же
   * класс дефекта, что Д-24/Д-25.
   */
  deficit: ModuleCounts;
  /** И-11: сколько прибытий подряд ЭТА стройка не получила нужного модуля. */
  arrivals_without_needed: number;
  /** И-11: прибытие, на котором форс-выдача последний раз сработала ДЛЯ ЭТОЙ стройки. 0 — ни разу. */
  last_floor_arrival: number;
}

export interface DropContext {
  /** Счетчик на пару (игрок, модуль): сколько прибытий подряд не выпадал (И-7). */
  pity: ModuleCounts;
  /** Что лежит на складе модулей (мгновенный остаток). */
  stock: ModuleCounts;
  /**
   * Сколько модулей просят рецепты активных и доступных строек: модуль -> сумма
   * рецептов. Валовая величина, склад из нее НЕ вычтен — иначе формулы ниже
   * сравнивали бы запас с самим собой (канон 2.3 и 2.6 п.3, см. `activeNeed`).
   * Читают И-7 pity (нужен ли модуль хоть одной стройке) и порог анти-стокпайла
   * (`activeContextNeed`). Floor guarantee (И-11) это поле не читает —
   * у нее свой, уже частный дефицит на стройку, см. `constructions`.
   */
  need: ModuleCounts;
  /**
   * Скользящее среднее склада за 24ч (канон `warehouse_avg_24h`, 2.3/2.4).
   * Анти-стокпайл читает это поле, а не `stock` — иначе продажа склада прямо
   * перед прибытием мгновенно снимала бы штраф (канон 2.7, АС в
   * [[tz-common-systems-mars]] «Given запас > потребности x2 ... when продает
   * весь склад прямо перед прибытием ... then вес не меняется мгновенно»).
   */
  warehouse_avg_24h: ModuleCounts;
  /** Открыт ли гейтовый тир (буровая, реактор). В MVP обычно false. */
  gated_open: boolean;
  /** Номер прибытия игрока, считая с 1. */
  arrival_no: number;
  /**
   * Активные (доступные к постройке) стройки со своими независимыми окнами
   * И-11. Пустой массив — «стройки нет вообще»: floor guarantee не имеет
   * контекста нужды и не срабатывает (канон 2.8, первый крайний случай).
   */
  constructions: ConstructionNeed[];
  rng: () => number;
}

export interface ArrivalRoll {
  /** По модулю на отсек, в порядке отсеков. */
  modules: ModuleId[];
  /** Индекс отсека, который переписала гарантия. null — гарантия не срабатывала. */
  floor_forced_slot: number | null;
  next_pity: ModuleCounts;
  /** Обновленные счетчики окна И-11, по одной записи на каждую стройку из входа. */
  next_constructions: ConstructionNeed[];
}

/** Базовый вес модуля ВНУТРИ своего тира: тир уже выбран, делим его вес поровну между предметами пула. */
function baseItemWeight(module_id: ModuleId): number {
  const tier = MODULES[module_id].tier;
  const pool = MODULE_TIER_POOL[tier];
  if (pool.length === 0) return 0;
  return 1 / pool.length;
}

/**
 * Вес модуля с поправками И-7, ВНУТРИ своего тира (тир весит отдельно, см.
 * `pickTier`). Экспортирован ради тестов: правило «pity поднимает вес»
 * проверяется числом, а не наблюдением за тысячей роллов.
 *
 * Раньше сюда был вшит и вес тира (`TIER_WEIGHTS[tier] / pool.length`) — из-за
 * этого модификаторы pity/анти-стокпайла плоско взвешивались вместе со всеми
 * модулями сразу, и pity/анти-стокпайл ОДНОГО предмета внутри тира двигали
 * долю самого ТИРА в общем ролле, ломая заявленные каркасом веса тиров
 * (basic 0.62, rare 0.33, gated 0.05, раздел 4). Канон (2.3, `rollReward`)
 * задает двухэтапный выбор: сперва тир по `TIER_WEIGHTS`, потом предмет ВНУТРИ
 * тира — модификаторы участвуют только на втором шаге.
 */
export function moduleWeight(module_id: ModuleId, ctx: DropContext): number {
  if (MODULES[module_id].tier === 'gated' && !ctx.gated_open) return 0;

  let weight = baseItemWeight(module_id);
  const needed = ctx.need[module_id] ?? 0;

  // Pity — только для того, что реально требуется хотя бы одной стройке
  // (канон 2.3, `isNeededForActiveContext`): модуль входит в рецепт доступной
  // стройки. Иначе счетчик разгонял бы вес модуля, который игроку некуда девать.
  if (needed > 0 && (ctx.pity[module_id] ?? 0) >= PITY_K) {
    weight *= PITY_MULTIPLIER;
  }

  // Анти-стокпайл: каркас И-7 «запас > потребность x2 → вес /2», где
  // потребность — валовый рецепт, а запас — скользящее среднее за 24ч
  // (`warehouse_avg_24h`, не мгновенный остаток `stock` — канон 2.3/2.7).
  // Охрана `needed > 0` обязательна: канон 2.8 («активных строек нет вообще»)
  // требует, чтобы при нулевой потребности веса падали к чистым TIER_WEIGHTS
  // без модификаторов — резать вес модуля, который никому не нужен, значило
  // бы наказывать игрока за запас, которому некуда деться.
  const avg = ctx.warehouse_avg_24h[module_id] ?? 0;
  if (needed > 0 && avg > needed * ANTISTOCKPILE_THRESHOLD) {
    weight *= ANTISTOCKPILE_FACTOR;
  }

  return weight;
}

/** Открытые тиры и их веса — гейтовый тир весит ноль, пока гейт закрыт. */
function tierWeights(ctx: DropContext): Record<ModuleTier, number> {
  return {
    basic: TIER_WEIGHTS.basic,
    rare: TIER_WEIGHTS.rare,
    gated: ctx.gated_open ? TIER_WEIGHTS.gated : 0,
  };
}

const TIER_ORDER: ModuleTier[] = ['basic', 'rare', 'gated'];

/**
 * Этап 1 двухэтапного выбора (канон 2.3): какой тир выпадает. Веса тиров
 * фиксированы каркасом и не зависят от pity/анти-стокпайла — те двигают только
 * распределение ВНУТРИ уже выбранного тира (`pickWeighted`).
 */
function pickTier(ctx: DropContext): ModuleTier {
  const weights = tierWeights(ctx);
  const total = TIER_ORDER.reduce((sum, t) => sum + weights[t], 0);
  if (total <= 0) return 'basic';

  let roll = ctx.rng() * total;
  // Запасной вариант — последний тир с ненулевым весом (см. пояснение у
  // `pickWeighted` ниже): гейтовый закрытый весит 0 и не может стать
  // запасным, иначе краевой rng=1 выдавал бы закрытый контент.
  let last_eligible: ModuleTier = 'basic';
  for (const tier of TIER_ORDER) {
    const w = weights[tier];
    if (w <= 0) continue;
    last_eligible = tier;
    roll -= w;
    if (roll <= 0) return last_eligible;
  }
  return last_eligible;
}

/** Этап 2: какой предмет внутри уже выбранного тира. */
function pickWeighted(ctx: DropContext): ModuleId {
  const tier = pickTier(ctx);
  const pool = MODULE_TIER_POOL[tier];
  const weights = pool.map((id) => moduleWeight(id, ctx));
  const total = weights.reduce((a, b) => a + b, 0);

  // Все веса ноль возможно только при пустом пуле — берем первый базовый,
  // чтобы отсек не приехал пустым. Контейнер без модуля читается как поломка.
  if (total <= 0) return MODULE_TIER_POOL.basic[0] ?? 'panel';

  let roll = ctx.rng() * total;
  let last_eligible: ModuleId = pool[0] ?? MODULE_TIER_POOL.basic[0] ?? 'panel';
  for (let i = 0; i < pool.length; i++) {
    const weight = weights[i] ?? 0;
    if (weight <= 0) continue; // нулевой вес не может выпасть ни при каком ролле
    last_eligible = pool[i]!;
    roll -= weight;
    if (roll <= 0) return last_eligible;
  }
  // Сюда попадаем только при rng ровно 1 или накопленной ошибке округления.
  // Запасной вариант обязан быть последним НЕНУЛЕВЫМ, а не последним в списке.
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
 * Разрешено ли гарантии сработать на этом прибытии ДЛЯ ОДНОЙ КОНКРЕТНОЙ
 * стройки.
 *
 * Раньше окно И-11 (`arrivals_without_needed`/`last_floor_arrival`) жило одним
 * скаляром на игрока в `DropContext`. Канон 2.6 п.2 и 2.8 требуют счетчик НА
 * СТРОЙКУ: «каждая ведет свой независимый `floor_guarantee_window_counter`/
 * `last_triggered_at`». Общий скаляр ломался ровно там, где спека обещала
 * защиту — при переключении между двумя активными стройками один делил окно
 * с другим, и гарантия срабатывала не по той стройке, у которой действительно
 * истекло окно, либо не срабатывала там, где обязана была.
 *
 * Отдельная функция, потому что условий четыре и каждое закрывает свой
 * эксплойт. Слепить их в одно `if` внутри роллера означало бы, что при
 * следующей правке ни один из них нельзя проверить тестом по отдельности.
 */
export function floorGuaranteeAllowedFor(
  construction: ConstructionNeed,
  ctx: DropContext,
  rolled: ModuleId[],
): boolean {
  const needed_ids = ALL_MODULE_IDS.filter(
    (id) =>
      (construction.deficit[id] ?? 0) > 0 &&
      FLOOR_GUARANTEE_ALLOWED_TIERS.includes(MODULES[id].tier),
  );
  // Дефицита из разрешенных тиров нет — либо стройка уже покрыта складом
  // (канон 2.6 п.3, `warehouseCoversConstruction`), либо не хватает только
  // гейтового, которого гарантия не трогает никогда. Чинить нечего.
  if (needed_ids.length === 0) return false;

  // Прибытие уже дало нужное этой стройке — чинить нечего.
  if (rolled.some((id) => (construction.deficit[id] ?? 0) > 0)) return false;

  // Front-loaded удача: первые прибытия форсируют гарантию поверх И-11,
  // не спрашивая ни серию неудач, ни разрыв между срабатываниями.
  if (ctx.arrival_no <= FRONT_LOADED_LUCK_ARRIVALS) return true;

  // Окно И-11: два прибытия подряд без нужного модуля, третье форсирует.
  if (construction.arrivals_without_needed < FLOOR_GUARANTEE_WINDOW - 1) return false;

  // Анти-эксплойт «держи стройку голодной»: не чаще раза на пять прибытий —
  // считается ОТ ЭТОЙ ЖЕ стройки (канон 2.6 п.2), а не глобально по игроку.
  if (
    construction.last_floor_arrival > 0 &&
    ctx.arrival_no - construction.last_floor_arrival < FLOOR_GUARANTEE_MIN_GAP
  )
    return false;

  return true;
}

/**
 * Прибытие рейса: по модулю на каждый отсек, плюс возможная форс-выдача.
 *
 * Гарантия переписывает результат одного уже сгенерированного отсека, а не
 * добавляет бонусный: иначе число контейнеров перестало бы совпадать с числом
 * отсеков, и обещание «1 отсек = 1 контейнер» превратилось бы в ложь. На одно
 * прибытие форсится не больше одной стройки: ТЗ шаттла 2.3/8 описывает форс
 * как переписывание ОДНОГО слота, канон не задает порядок между несколькими
 * одновременно созревшими стройками (2.8 упоминает его расплывчато, «менее
 * прогретая нужда») — берем порядок следования `ctx.constructions`, тот же,
 * что передает стор.
 */
export function rollArrival(slot_count: number, ctx: DropContext): ArrivalRoll {
  const modules: ModuleId[] = [];
  for (let i = 0; i < slot_count; i++) modules.push(pickWeighted(ctx));

  let floor_forced_slot: number | null = null;
  let forced_construction_id: string | null = null;

  // Пустому рейсу гарантия переписать нечего: канон ([[tz-common-systems-mars]]
  // 2.3, `enforceFloorGuarantee`) берет слот через `pickLeastImpactfulSlot(rewards)`,
  // а в пустом наборе слота нет.
  if (modules.length > 0) {
    for (const construction of ctx.constructions) {
      if (!floorGuaranteeAllowedFor(construction, ctx, modules)) continue;

      const candidates = ALL_MODULE_IDS.filter(
        (id) =>
          (construction.deficit[id] ?? 0) > 0 &&
          FLOOR_GUARANTEE_ALLOWED_TIERS.includes(MODULES[id].tier),
      );
      // Из нужного выдаем самое дефицитное — так гарантия закрывает узкое
      // место, а не самый частый базовый модуль, которого и без нее нападает.
      const forced = candidates.reduce((worst, id) =>
        (construction.deficit[id] ?? 0) > (construction.deficit[worst] ?? 0) ? id : worst,
      );
      floor_forced_slot = modules.length - 1;
      modules[floor_forced_slot] = forced;
      forced_construction_id = construction.construction_id;
      break; // одно прибытие форсит не больше одной стройки
    }
  }

  // Канон 2.3 (`applyPityAndStockUpdates`) задает ровно три исхода на модуль:
  //
  //     if item == rolledItem:                  pity[item] = 0
  //     elif isNeededForActiveContext(item):    pity[item] += 1
  //     (иначе — не трогаем)
  //
  // «Не трогаем» — это пауза, а не сброс: ТЗ шаттла 8.1 разбирает этот случай
  // на герметике дословно — счетчик «НЕ обнуляется, а лишь перестает расти,
  // пока модуль не нужен активной стройке, и продолжает расти дальше с того же
  // значения, как только снова становится нужен». Сброс обесценил бы ожидание
  // при каждом переключении между стройками.
  const dropped = new Set(modules);
  const next_pity: ModuleCounts = {};
  for (const id of ALL_MODULE_IDS) {
    const before = ctx.pity[id] ?? 0;
    if (dropped.has(id)) next_pity[id] = 0;
    else next_pity[id] = (ctx.need[id] ?? 0) > 0 ? before + 1 : before;
  }

  // Счетчик окна И-11 у каждой стройки — свой, и растет только на прибытие,
  // которое не закрыло ЕЕ дефицит (канон 2.4: «+1 на прибытие без нужного
  // предмета» для этой стройки, 0 при выпадении или форс-выдаче ей же).
  const next_constructions: ConstructionNeed[] = ctx.constructions.map((construction) => {
    const gave_needed = modules.some((id) => (construction.deficit[id] ?? 0) > 0);
    return {
      ...construction,
      arrivals_without_needed: gave_needed ? 0 : construction.arrivals_without_needed + 1,
      last_floor_arrival:
        construction.construction_id === forced_construction_id
          ? ctx.arrival_no
          : construction.last_floor_arrival,
    };
  });

  return {
    modules,
    floor_forced_slot,
    next_pity,
    next_constructions,
  };
}
