/**
 * Опорная модель данных. Канон полей — каркас, раздел 9.
 * Синонимы запрещены: kind, prod_time_sec, required_building, inputs[] = {good_id, qty}.
 * Константы конфига — UPPER_SNAKE_CASE, поля рантайм-структур — snake_case.
 */

export type GoodKind = 'crop' | 'factory';

export type BuildingType =
  | 'hydroponics'
  | 'mining_site'
  | 'food_module'
  | 'atmospheric_module'
  | 'textile_module'
  | 'warehouse'
  | 'construction';

export type Mechanic = 'drone' | 'shuttle' | 'liner';

export type ModuleTier = 'basic' | 'rare' | 'gated';

export type ModuleId =
  | 'panel'
  | 'frame'
  | 'sealant'
  | 'filter'
  | 'cable'
  | 'drill_head'
  | 'reactor_cell';

export interface GoodInput {
  good_id: GoodId;
  qty: number;
}

export type GoodId =
  | 'algae'
  | 'soy'
  | 'mushrooms'
  | 'tomatoes'
  | 'cotton'
  | 'coffee_beans'
  | 'protein_bar'
  | 'mushroom_soup'
  | 'fabric'
  | 'jumpsuit'
  | 'coffee_ration'
  | 'oxygen_tank'
  // Добываемые: у них нет входов, их не выращивают и не перерабатывают.
  | 'regolith'
  | 'water_ice';

export interface Good {
  id: GoodId;
  name: string;
  kind: GoodKind;
  unlock_level: number;
  /** Цена продажи в кредитах. */
  price: number;
  base_xp: number;
  prod_time_sec: number;
  /** Пусто для кропов. */
  inputs: GoodInput[];
  /** Пусто для кропов: они растут в грядке, а не в здании. */
  required_building: BuildingType | null;
}

export interface Player {
  id: string;
  level: number;
  xp: number;
  credits: number;
  isotopes: number;
  league_points: number;
  payer_flag: boolean;
}

export interface WarehouseCell {
  good_id: GoodId;
  qty: number;
  /** Отделено от qty: положенное в слот списано с доступного, но возвратимо до отправки (И-12 — кроме докупки). */
  reserved: number;
}

export interface OrderSlot {
  idx: number;
  good_id: GoodId;
  qty_required: number;
  qty_filled: number;
  filled_by: 'self' | 'ally' | 'purchase' | null;
}

export type OrderState = 'active' | 'partial' | 'ready' | 'sent' | 'empty_cooldown';

export interface Order {
  id: string;
  mechanic: Mechanic;
  state: OrderState;
  created_at: number;
  deadline_at: number | null;
  slots: OrderSlot[];
}
