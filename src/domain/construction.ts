/**
 * Стройка класса Б: здания, которые строятся из строй-модулей и проходят
 * фазу IN_PROGRESS. Источник истины — [[tz-production-mars]] раздел 3
 * («Класс Б») и [[mars-colony-frame]] разделы 4 и 6.
 *
 * Класс А (фабрики, слоты грядок) сюда не относится: он покупается за кредиты
 * и переходит в DONE мгновенно, без таймера и без модулей. Разведение классов
 * не косметическое — если бы фабрики требовали модулей, ядро игры зависело бы
 * от механики, которая открывается позже фабрик (шаттл ур.5, Пищевой ур.3).
 *
 * Активная и доступная стройка — вход для И-7 (pity) и И-11 (floor guarantee):
 * дроп-роллер шаттла читает отсюда, чего игроку не хватает.
 */

import {
  MODULE_STOCK_CAP,
  WAREHOUSE_MAX_CAPACITY,
  WAREHOUSE_UPGRADE_STEP,
} from './config/economy';
import { ALL_MODULE_IDS, type BuildKind, CONSTRUCTION_RECIPE } from './config/modules';
import type { ModuleCounts } from './droproller';
import type { ModuleId } from './types';
import { upgradeCapacity, type WarehouseState } from './warehouse';

export type BuildState = 'LOCKED' | 'AVAILABLE' | 'IN_PROGRESS' | 'DONE';

export interface BuildSlot {
  kind: BuildKind;
  state: BuildState;
  /** Тир повторяемого здания: сколько раз уже построено. У склада растет. */
  tier: number;
  ends_at: number;
}

export interface ConstructionState {
  /** Склад модулей. Лимит общий на все типы (каркас 6), не по типу. */
  stock: ModuleCounts;
  builds: BuildSlot[];
  /** Сколько строек может идти одновременно. Вторая линия — за изотопы. */
  lines: number;
}

export function createConstruction(): ConstructionState {
  return {
    stock: {},
    builds: Object.values(CONSTRUCTION_RECIPE).map((def) => ({
      kind: def.kind,
      state: 'LOCKED' as BuildState,
      tier: 0,
      ends_at: 0,
    })),
    lines: 1,
  };
}

export function moduleTotal(stock: ModuleCounts): number {
  return ALL_MODULE_IDS.reduce((sum, id) => sum + (stock[id] ?? 0), 0);
}

/**
 * Текущий потолок склада модулей.
 *
 * [[mars-colony-frame]] раздел 6: «Строй-модули хранятся отдельным лимитом 100
 * и АПГРЕЙДЯТСЯ ТЕМ ЖЕ ЗДАНИЕМ». То есть 100 — стартовое значение, а не
 * константа на всю игру: каждый построенный тир расширения склада поднимает
 * оба лимита сразу.
 *
 * **Расхождение документов, решает каркас.** [[tz-production-mars]] пишет
 * обратное в трех местах (3.1 «модули — фиксировано 100», конфиг-таблица
 * раздела 7 «фиксировано каркасом», AC 16 «апгрейд товарного склада не
 * увеличивает и не затрагивает лимит модулей») и при этом ссылается на тот же
 * раздел 6 каркаса как на источник — цитирует неточно. `mars-colony/CLAUDE.md`
 * объявляет каркас источником чисел, поэтому взят каркас. Тот же случай, что
 * уже разбирали с клемпом премии дрона ([[spec-prototype-build]] 8.8).
 *
 * Шаг и потолок каркас отдельно для модулей не задает, и выдумывать их нельзя:
 * «то же здание» читается как «тот же тир и тот же шаг» — `WAREHOUSE_UPGRADE_STEP`
 * на каждый построенный тир. Собственного потолка у модульного лимита нет,
 * он ограничен числом тиров: здание закрывается по потолку товарной емкости
 * (`canBuildMoreTiers`), то есть на 25 тирах.
 *
 * Емкость считается от тира, а не хранится полем: сохраненное поле пришлось бы
 * поднимать в `applyBuildEffect`, а тот вызывается из `refreshBuilds`, результат
 * которого стор разворачивает в новый объект — мутация потерялась бы молча.
 */
export function moduleCapacity(state: ConstructionState): number {
  const tier = state.builds.find((b) => b.kind === 'warehouse_upgrade')?.tier ?? 0;
  return MODULE_STOCK_CAP + tier * WAREHOUSE_UPGRADE_STEP;
}

export function moduleSpaceLeft(state: ConstructionState): number {
  return Math.max(0, moduleCapacity(state) - moduleTotal(state.stock));
}

/**
 * Зачисление модуля на склад. Переполнение — отказ целиком, как и у товарного
 * склада: частичный прием превратил бы «контейнер приехал» в «половина
 * контейнера растворилась», а такое игрок читает как потерю, не как правило.
 */
export function addModule(state: ConstructionState, module_id: ModuleId, qty = 1): boolean {
  if (qty <= 0) return false;
  if (moduleSpaceLeft(state) < qty) return false;
  state.stock[module_id] = (state.stock[module_id] ?? 0) + qty;
  return true;
}

/**
 * Рецепт тира. Повторяемые здания по ТЗ «шкалируются по тиру», но формулы
 * шкалирования ни один документ не задает. Рецепт плоский: тир на цену не
 * влияет — решение владельца 2026-08-01.
 */
export function recipeFor(kind: BuildKind): Partial<Record<ModuleId, number>> {
  return CONSTRUCTION_RECIPE[kind].recipe;
}

/** Чего не хватает на постройку: модуль -> дефицит. Пустой объект = хватает. */
export function missingFor(kind: BuildKind, stock: ModuleCounts): ModuleCounts {
  const missing: ModuleCounts = {};
  for (const [id, need] of Object.entries(recipeFor(kind)) as Array<[ModuleId, number]>) {
    const short = need - (stock[id] ?? 0);
    if (short > 0) missing[id] = short;
  }
  return missing;
}

/**
 * Пересчет состояний по уровню и времени. Ленивый, как остальные таймеры игры:
 * состояние вычисляется при чтении, а не будильником на каждое здание.
 */
export function refreshBuilds(
  state: ConstructionState,
  level: number,
  now: number,
  warehouse: WarehouseState,
): BuildSlot[] {
  return state.builds.map((build) => {
    const def = CONSTRUCTION_RECIPE[build.kind];
    const next = { ...build };

    if (next.state === 'IN_PROGRESS' && now >= next.ends_at) {
      applyBuildEffect(next, warehouse);
      // Повторяемое здание сразу открывает следующий тир — иначе после
      // расширения склада игроку было бы непонятно, почему кнопка исчезла.
      next.tier += 1;
      next.state = def.repeatable && canBuildMoreTiers(next, warehouse) ? 'AVAILABLE' : 'DONE';
      next.ends_at = 0;
      return next;
    }

    if (next.state === 'LOCKED' && level >= def.unlock_level) {
      next.state = 'AVAILABLE';
    }
    return next;
  });
}

/** У повторяемого склада тиры кончаются на потолке емкости MVP. */
function canBuildMoreTiers(build: BuildSlot, warehouse: WarehouseState): boolean {
  if (build.kind !== 'warehouse_upgrade') return true;
  return warehouse.capacity < WAREHOUSE_MAX_CAPACITY;
}

/** Эффект завершения. Пока эффект один — емкость склада; жилой блок косметика. */
function applyBuildEffect(build: BuildSlot, warehouse: WarehouseState): void {
  if (build.kind === 'warehouse_upgrade') upgradeCapacity(warehouse);
}

export function activeLinesUsed(state: ConstructionState): number {
  return state.builds.filter((b) => b.state === 'IN_PROGRESS').length;
}

export type StartRefusal = 'not_available' | 'no_free_line' | 'missing_modules';

export interface StartResult {
  ok: boolean;
  reason?: StartRefusal;
}

/**
 * Старт стройки: рецепт списывается одной транзакцией, частичного списания нет.
 * Проверка и списание рядом намеренно — между ними не должно быть ни одного
 * шага, на котором склад мог бы измениться.
 */
export function startBuild(
  state: ConstructionState,
  kind: BuildKind,
  now: number,
): StartResult {
  const build = state.builds.find((b) => b.kind === kind);
  if (build?.state !== 'AVAILABLE') return { ok: false, reason: 'not_available' };
  if (activeLinesUsed(state) >= state.lines) return { ok: false, reason: 'no_free_line' };

  const recipe = recipeFor(kind);
  for (const [id, need] of Object.entries(recipe) as Array<[ModuleId, number]>) {
    if ((state.stock[id] ?? 0) < need) return { ok: false, reason: 'missing_modules' };
  }
  for (const [id, need] of Object.entries(recipe) as Array<[ModuleId, number]>) {
    state.stock[id] = (state.stock[id] ?? 0) - need;
  }

  build.state = 'IN_PROGRESS';
  build.ends_at = now + CONSTRUCTION_RECIPE[kind].build_time_min * 60;
  return { ok: true };
}

/**
 * Потребность в модулях для дроп-роллера: что нужно стройкам, которые игрок
 * может начать прямо сейчас, за вычетом склада.
 *
 * Читается только AVAILABLE: у IN_PROGRESS рецепт уже списан, и считать его
 * потребностью значило бы разгонять pity по модулям, которые уже не нужны.
 */
export function activeNeed(state: ConstructionState): ModuleCounts {
  const need: ModuleCounts = {};
  for (const build of state.builds) {
    if (build.state !== 'AVAILABLE') continue;
    for (const [id, qty] of Object.entries(missingFor(build.kind, state.stock)) as Array<
      [ModuleId, number]
    >) {
      need[id] = Math.max(need[id] ?? 0, qty);
    }
  }
  return need;
}
