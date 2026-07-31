/**
 * Строй-модули и рецепты стройки.
 * Источник истины — [[mars-colony-frame]] раздел 4 и [[tz-production-mars]] раздел 7.
 *
 * Модули — единственная валюта, которую нельзя купить за кредиты (И-1). Поэтому
 * таблица тиров живет отдельным файлом, а не строкой внутри экономики: она задает
 * гейт прогрессии целиком, и любая правка здесь двигает темп игры.
 */

import type { BuildingType, ModuleId, ModuleTier } from '../types';

export interface ModuleDef {
  id: ModuleId;
  name: string;
  tier: ModuleTier;
}

export const MODULES: Record<ModuleId, ModuleDef> = {
  panel: { id: 'panel', name: 'Панель', tier: 'basic' },
  frame: { id: 'frame', name: 'Каркас', tier: 'basic' },
  sealant: { id: 'sealant', name: 'Герметик', tier: 'rare' },
  filter: { id: 'filter', name: 'Фильтр', tier: 'rare' },
  cable: { id: 'cable', name: 'Кабель', tier: 'rare' },
  drill_head: { id: 'drill_head', name: 'Буровая головка', tier: 'gated' },
  reactor_cell: { id: 'reactor_cell', name: 'Реактор-элемент', tier: 'gated' },
};

export const ALL_MODULE_IDS = Object.keys(MODULES) as ModuleId[];

/**
 * Пулы тиров. Вес тира из `TIER_WEIGHTS` делится между модулями пула поровну:
 * каркас задает вес тира, а не отдельного модуля, и придумывать модулю
 * собственный вес значило бы завести число, которого нет ни в одном документе.
 */
export const MODULE_TIER_POOL: Record<ModuleTier, ModuleId[]> = {
  basic: ALL_MODULE_IDS.filter((id) => MODULES[id].tier === 'basic'),
  rare: ALL_MODULE_IDS.filter((id) => MODULES[id].tier === 'rare'),
  gated: ALL_MODULE_IDS.filter((id) => MODULES[id].tier === 'gated'),
};

/** Здания класса Б: строятся из модулей и проходят фазу IN_PROGRESS. */
export type BuildKind = 'warehouse_upgrade' | 'habitat_block';

export interface BuildDef {
  kind: BuildKind;
  name: string;
  unlock_level: number;
  recipe: Partial<Record<ModuleId, number>>;
  /**
   * Длительность стройки в минутах.
   *
   * НЕ ЗАДАНО НИ ОДНИМ ТЗ. Каркас (раздел 6) говорит «рецепт + время постройки»,
   * конфиг-таблица производства (раздел 7) перечисляет ставку и пол ускорения
   * стройки, но самой длительности не называет. Значения ниже выбраны здесь по
   * единственной опоре, которая в документах есть: «стройка — часы, а не минуты»
   * (обоснование `SPEEDUP_FLOOR_ISO_CONSTRUCTION`). Открытый вопрос владельцу,
   * см. `spec-prototype-build.md`.
   */
  build_time_min: number;
  /** Повторяемое здание (склад строится тирами) или одноразовое. */
  repeatable: boolean;
}

export const CONSTRUCTION_RECIPE: Record<BuildKind, BuildDef> = {
  warehouse_upgrade: {
    kind: 'warehouse_upgrade',
    name: 'Расширение склада',
    unlock_level: 5,
    recipe: { filter: 6, cable: 6, sealant: 6 },
    build_time_min: 120,
    repeatable: true,
  },
  habitat_block: {
    kind: 'habitat_block',
    name: 'Жилой блок',
    unlock_level: 7,
    recipe: { panel: 6, frame: 5, sealant: 7 },
    build_time_min: 240,
    repeatable: false,
  },
};

export const ALL_BUILD_KINDS = Object.keys(CONSTRUCTION_RECIPE) as BuildKind[];

/**
 * Класс Б хранится отдельно от `BuildingType` каркаса намеренно: `BuildingType`
 * отвечает на вопрос «какое здание производит этот товар», а `BuildKind` — на
 * вопрос «что можно построить за модули». Пересечение по слову «склад» случайно.
 */
export const BUILD_KIND_TO_BUILDING: Partial<Record<BuildKind, BuildingType>> = {
  warehouse_upgrade: 'warehouse',
};
