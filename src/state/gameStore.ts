/**
 * Zustand-стор — тонкая обертка над доменом. Здесь нет ни одного правила игры
 * и ни одного числа: только хранение состояния и вызовы доменных функций.
 * Как только тут появится число, конфиг перестанет быть источником истины.
 */

import { create } from 'zustand';
import { type PersistStorage, persist, type StorageValue } from 'zustand/middleware';
import {
  CREDITS_START,
  constructionSpeedupCost,
  droneRefreshPrice,
  FACTORY_NAMES,
  FACTORY_PRICES,
  FACTORY_QUEUE_BASE_SLOTS,
  fieldsAtLevel,
  productionSpeedupCost,
} from '../domain/config/economy';
import { GOODS } from '../domain/config/goods';
import {
  levelUpReward,
  MAX_LEVEL_MVP,
  MECHANIC_UNLOCK_LEVEL,
  xpToNext,
} from '../domain/config/levels';
import type { BuildKind } from '../domain/config/modules';
import { CONSTRUCTION_RECIPE, MODULES } from '../domain/config/modules';
import {
  activeNeed,
  addModule,
  type ConstructionState,
  createConstruction,
  refreshBuilds,
  startBuild,
} from '../domain/construction';
import {
  availableGoodsFor,
  buyoutPosition,
  discardOrder,
  generateOrder,
  loadPosition,
  type OrderSlot,
  positionBuyoutPrice,
  releaseReserved,
  sendOrder,
  slotsAtLevel,
} from '../domain/drone';
import type { DropContext, ModuleCounts } from '../domain/droproller';
import {
  createFactorySlot,
  createField,
  collectFactory as domainCollectFactory,
  collectField as domainCollectField,
  enqueue as domainEnqueue,
  plant as domainPlant,
  sell as domainSell,
  type FactorySlot,
  type FieldSlot,
  onWarehouseStockIncreased,
  type ProductionContext,
  refreshFactorySlot,
  refreshField,
} from '../domain/production';
import {
  allCollected,
  buyoutSlot,
  collectContainer,
  generateTrip,
  loadSlot,
  refreshTrip,
  type ShuttleTrip,
  skipFlight,
  skipPrice,
  slotBuyoutPrice,
  startCooldown,
  tripXp,
} from '../domain/shuttle';
import type { BuildingType, GoodId } from '../domain/types';
import { createWarehouse, totalQty, type WarehouseState } from '../domain/warehouse';

/** Здания класса А: покупаются за кредиты по достижении уровня. */
export type PurchasableBuilding = keyof typeof FACTORY_PRICES;

export interface Toast {
  id: number;
  text: string;
  kind: 'info' | 'warn' | 'reward';
}

export interface GameState {
  now: number;
  level: number;
  xp_into_level: number;
  credits: number;
  isotopes: number;
  warehouse: WarehouseState;
  fields: FieldSlot[];
  factory_slots: FactorySlot[];
  /**
   * Построенные здания класса А. Раньше здесь стоял булев `has_food_module` —
   * он перестал работать в тот момент, когда зданий стало больше одного:
   * каждое новое требовало бы своего флага и своего ветвления в трех местах.
   */
  buildings: BuildingType[];
  toasts: Toast[];

  tick: (now: number) => void;
  plant: (idx: number, good_id: GoodId) => void;
  collectField: (idx: number) => void;
  enqueue: (idx: number, good_id: GoodId) => void;
  collectFactory: (idx: number) => void;
  sell: (good_id: GoodId, qty: number) => void;
  speedupField: (idx: number) => void;
  speedupFactory: (idx: number) => void;
  orders: OrderSlot[];
  loadOrderPosition: (slot_idx: number, position_idx: number) => void;
  buyoutOrderPosition: (slot_idx: number, position_idx: number) => void;
  sendOrderAt: (slot_idx: number) => void;
  discardOrderAt: (slot_idx: number) => void;
  refreshSlotNow: (slot_idx: number) => void;
  buyBuilding: (type: PurchasableBuilding) => void;

  /** Шаттл: ровно один рейс на игрока, поэтому не массив. */
  shuttle: ShuttleTrip | null;
  /** Сколько прибытий уже случилось. Вход FTUE-удачи и окна И-11. */
  shuttle_arrivals: number;
  drop_pity: ModuleCounts;
  drop_without_needed: number;
  drop_last_floor: number;
  loadShuttleSlot: (idx: number) => void;
  buyoutShuttleSlot: (idx: number) => void;
  skipShuttle: () => void;
  collectContainerAt: (idx: number) => void;
  collectAllContainers: () => void;

  construction: ConstructionState;
  startConstruction: (kind: BuildKind) => void;
  speedupConstruction: (kind: BuildKind) => void;

  dismissToast: (id: number) => void;
}

let toast_seq = 0;

/* ---------------------------------------------------------------------------
 * Сохранение прогресса
 *
 * Каркас (раздел 11) держит состояние на сервере, прототип по тому же разделу
 * живет на «localStorage-эмуляции сервера-заглушки» ([[tz-common-systems-mars]]
 * 12). Здесь и есть эта эмуляция: сейв — снимок того, что в бою отдавал бы
 * `GET /state`, а загрузка страницы — тот самый запрос.
 *
 * Три правила, которые middleware сам не решает.
 *
 * 1. ВРЕМЯ. Таймеры считаются от абсолютных меток и офлайн не замирают
 *    ([[tz-production-mars]] 2 и AC 14, [[tz-shuttle-mars]] 11): грядка,
 *    дозревшая без игрока, приходит READY, слот дрона с истекшим рефрешем —
 *    с новым заказом ([[tz-drone-mars]] AC 15), и все это без штрафа, бонуса
 *    и ретроактивных начислений. Поэтому загрузка заканчивается одним шагом
 *    времени (`tick`) — реконсиляцией, а не догоняющим отыгрышем пропущенных
 *    тиков: пропущенный кулдаун выдает один новый заказ, а не десять.
 * 2. СОВМЕСТИМОСТЬ. У сейва есть версия. Сейв другой версии, битый или чужой
 *    по форме выбрасывается целиком и игра начинается с нуля: миграций нет.
 *    Тихо принять состояние неизвестной формы хуже потери прогресса — данные
 *    поедут, а узнается это через несколько сессий.
 * 3. ПРОИЗВОДНОЕ НЕ СОХРАНЯЕТСЯ. `now` берется от часов, а не из файла;
 *    тосты — экранный мусор с таймером на dismiss, переживший перезагрузку
 *    тост висел бы вечно. Состояния слотов (GROWING/READY, LOCKED/AVAILABLE)
 *    в файле лежат, но им не верят: они пересчитываются реконсиляционным
 *    тиком из `ends_at` и уровня.
 *
 * Чего здесь сознательно нет — кап на офлайн-прогресс и защита от перевода
 * системных часов. Античит серверный и в срез не входит (spec-prototype-build
 * раздел 2), а требование ТЗ шаттла, проверяемое на клиенте, выполняется и
 * так: содержимое контейнеров зафиксировано при отправке и сейв его не
 * перекатывает. Решение записано в реестр (spec-prototype-build 8.11).
 */

/** Ключ хранилища. Не игровое число — инженерная константа. */
export const SAVE_KEY = 'mars-colony-save';

/**
 * Версия схемы сейва. Растет на любой правке формы состояния, после которой
 * старый файл больше не описывает игру целиком.
 */
export const SAVE_VERSION = 1;

const SAVED_NUMBER_KEYS = [
  'level',
  'xp_into_level',
  'credits',
  'isotopes',
  // Счетчики дроп-роллера: без них И-7 (pity) и И-11 (гарантия пола)
  // обнулялись бы каждой перезагрузкой, то есть F5 стал бы рычагом экономики.
  'shuttle_arrivals',
  'drop_without_needed',
  'drop_last_floor',
] as const;

const SAVED_ARRAY_KEYS = ['fields', 'factory_slots', 'buildings', 'orders'] as const;

const SAVED_OBJECT_KEYS = ['warehouse', 'construction', 'drop_pity'] as const;

/**
 * Что попадает в сейв. Список один и тот же для записи и для проверки формы
 * при чтении — разъехаться им негде.
 *
 * `warehouse` и `orders` сохраняются только вместе: `reserved` склада — это
 * ровно то, что лежит в погруженных позициях заказов. Сохранить одно без
 * другого значит запереть товар навсегда или выдать его дважды.
 */
const SAVED_KEYS = [
  ...SAVED_NUMBER_KEYS,
  ...SAVED_ARRAY_KEYS,
  ...SAVED_OBJECT_KEYS,
  // Единственное поле сейва, у которого допустим null.
  'shuttle',
] as const;

type SavedKey = (typeof SAVED_KEYS)[number];
export type SaveData = Pick<GameState, SavedKey>;

/** Производное состояние: пересчитывается при загрузке, а не восстанавливается. */
export type VolatileKey = 'now' | 'toasts';

type DataKey = {
  [K in keyof GameState]: GameState[K] extends (...args: never[]) => unknown ? never : K;
}[keyof GameState];

type AssertNever<T extends never> = T;

/**
 * Страховка компилятора. Новое поле данных в состоянии обязано быть либо
 * сохраняемым, либо явно объявленным производным: иначе этот тип перестает
 * быть `never` и сборка падает. Молча не сохраниться поле не может.
 *
 * Экспортируется, чтобы его видел `tsc` и читал тот, кто добавляет поле.
 */
export type UnclassifiedStateKey = AssertNever<Exclude<DataKey, SavedKey | VolatileKey>>;

const nowSec = () => Math.floor(Date.now() / 1000);

function volatileState(): Pick<GameState, VolatileKey> {
  return { now: nowSec(), toasts: [] };
}

/** Стартовое состояние новой колонии. Отдельной функцией — его же читает загрузка. */
export function createInitialState(): SaveData & Pick<GameState, VolatileKey> {
  return {
    ...volatileState(),
    level: 1,
    xp_into_level: 0,
    credits: CREDITS_START,
    isotopes: 0,
    warehouse: createWarehouse(),
    fields: Array.from({ length: fieldsAtLevel(1) }, (_, i) => createField(i)),
    // Слотов нет, пока не куплено ни одного здания: очередь без здания —
    // это интерфейс, обещающий производство, которого игрок не покупал.
    factory_slots: [],
    buildings: [],
    orders: [],
    shuttle: null,
    shuttle_arrivals: 0,
    drop_pity: {},
    drop_without_needed: 0,
    drop_last_floor: 0,
    construction: createConstruction(),
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/**
 * Форма сейва. Проверяется именно форма, а не игровые инварианты: задача
 * отличить наш файл от чужого и от поехавшего, а не пересчитать экономику.
 */
function isSave(value: unknown): value is SaveData {
  if (!isRecord(value)) return false;
  for (const key of SAVED_KEYS) if (!(key in value)) return false;
  if (!SAVED_NUMBER_KEYS.every((key) => Number.isFinite(value[key]))) return false;
  if (!SAVED_ARRAY_KEYS.every((key) => Array.isArray(value[key]))) return false;
  if (!SAVED_OBJECT_KEYS.every((key) => isRecord(value[key]))) return false;
  if (value.shuttle !== null && !isRecord(value.shuttle)) return false;

  const warehouse = value.warehouse as Record<string, unknown>;
  return isRecord(warehouse.cells) && Number.isFinite(warehouse.capacity);
}

/** Хранилище строк. Абстракция нужна, чтобы тест мог подсунуть свое. */
export interface SaveBackend {
  getItem: (key: string) => string | null;
  setItem: (key: string, value: string) => void;
  removeItem: (key: string) => void;
}

/**
 * Хранилище сейва поверх строкового бэкенда.
 *
 * Своя обертка вместо `createJSONStorage` ради двух отказов, которые та не
 * гасит: разбор битой строки бросает (мусор в ключе ронял бы запуск), и
 * запись бросает при исчерпанной квоте — а пишем мы из каждого игрового
 * действия, то есть игрок терял бы ход из-за переполненного хранилища.
 */
export function saveStorage(backend: SaveBackend): PersistStorage<SaveData | undefined> {
  return {
    getItem: (name) => {
      try {
        const raw = backend.getItem(name);
        if (raw === null) return null;
        const parsed: unknown = JSON.parse(raw);
        return isRecord(parsed) ? (parsed as StorageValue<SaveData | undefined>) : null;
      } catch {
        return null; // не разобралось — значит это не наш сейв, а мусор
      }
    },
    setItem: (name, value) => {
      try {
        backend.setItem(name, JSON.stringify(value));
      } catch {
        // Нет места или запись запрещена: игра продолжается без сохранения.
      }
    },
    removeItem: (name) => {
      try {
        backend.removeItem(name);
      } catch {
        // Удалить не дали — не повод ронять игру.
      }
    },
  };
}

const memory = new Map<string, string>();

/**
 * Запасное хранилище в памяти. Нужно там, где `localStorage` нет вовсе
 * (тесты в node) или запрещен настройками приватности: без него middleware
 * ругался бы в консоль на каждое действие игрока.
 */
const memoryBackend: SaveBackend = {
  getItem: (key) => memory.get(key) ?? null,
  setItem: (key, value) => {
    memory.set(key, value);
  },
  removeItem: (key) => {
    memory.delete(key);
  },
};

function defaultBackend(): SaveBackend {
  try {
    if (typeof window !== 'undefined' && window.localStorage) return window.localStorage;
  } catch {
    // Обращение к localStorage запрещено настройками браузера.
  }
  return memoryBackend;
}

let save_accepted = false;

/**
 * Поднялся ли из хранилища НАШ сейв на этом запуске.
 *
 * Спрашивать об этом `localStorage` бесполезно: загрузка страницы делает шаг
 * времени (реконсиляция офлайна), шаг пишет состояние, и к моменту любой
 * внешней проверки запись существует всегда — даже у того, кто зашел впервые
 * в жизни. Именно поэтому состояние показа не подставлялось никогда.
 *
 * Ответ дает единственное место, где чтение настоящее, — слияние. И отвечает
 * оно на правильный вопрос: не «лежало ли что-то в ключе», а «был ли у
 * человека прогресс». Мусор, чужой объект и сейв неизвестной версии дают
 * одинаковое «нет» — все три случая означают, что играть не с чего.
 */
export function saveAccepted(): boolean {
  return save_accepted;
}

/**
 * Слияние сейва с текущим состоянием. Сейв неузнаваемой формы игнорируется
 * целиком: лучше начать заново, чем поехать тихо.
 */
export function mergeSave(persisted: unknown, current: GameState): GameState {
  if (!isSave(persisted)) return { ...current, ...volatileState() };
  save_accepted = true;
  return { ...current, ...persisted, ...volatileState() };
}

export const useGame = create<GameState>()(
  persist<GameState, [], [], SaveData | undefined>(
    (set, get) => {
      const ctx = (): ProductionContext => {
        const s = get();
        return { now: s.now, warehouse: s.warehouse, credits: s.credits, level: s.level };
      };

      const pushToast = (text: string, kind: Toast['kind'] = 'info') => {
        const toast = { id: ++toast_seq, text, kind };
        set((s) => ({ toasts: [...s.toasts, toast] }));
        setTimeout(() => get().dismissToast(toast.id), 2600);
      };

      /** Начисление XP с обработкой левелапов. Награда берется из конфига, не отсюда. */
      const applyXp = (amount: number) => {
        const s = get();
        let level = s.level;
        let xp = s.xp_into_level + amount;
        let credits = s.credits;
        let isotopes = s.isotopes;
        let fields = s.fields;

        while (level < MAX_LEVEL_MVP && xp >= xpToNext(level)) {
          xp -= xpToNext(level);
          level += 1;
          const reward = levelUpReward(level);
          credits += reward.credits;
          isotopes += reward.isotopes;
          pushToast(
            `Уровень ${level}! +${reward.credits} кр, +${reward.isotopes} изотопов`,
            'reward',
          );

          const target = fieldsAtLevel(level);
          if (fields.length < target) {
            fields = [...fields];
            while (fields.length < target) fields.push(createField(fields.length));
            pushToast('Открыта новая грядка', 'reward');
          }
        }

        set({ level, xp_into_level: xp, credits, isotopes, fields });
      };

      /** Новый заказ в слот. Пул товаров — то, что игрок реально умеет производить. */
      const makeOrder = (idx: number, now: number): OrderSlot => {
        const s = get();
        const buildings = new Set<string>(s.buildings);
        const order = generateOrder(idx, {
          level: s.level,
          warehouse: s.warehouse,
          available_goods: availableGoodsFor(s.level, buildings),
          board: s.orders,
          rng: Math.random,
        });
        order.refresh_at = now;
        return order;
      };

      /** Пополнение склада будит слоты фабрики, ждущие входов. */
      const notifyStockIncreased = () => {
        const slots = [...get().factory_slots];
        const started = onWarehouseStockIncreased(slots, ctx());
        if (started > 0) set({ factory_slots: slots });
      };

      /**
       * Контекст дроп-роллера. Собирается на каждый вызов, а не хранится: половина
       * его полей — производные от склада модулей и списка доступных строек, и
       * копия этих полей разъехалась бы с оригиналом на первой же постройке.
       */
      const dropCtx = (): DropContext => {
        const s = get();
        return {
          pity: s.drop_pity,
          stock: s.construction.stock,
          need: activeNeed(s.construction),
          // Гейтовый тир требует построенных зданий, которых в MVP нет (ТЗ 7).
          gated_open: false,
          arrival_no: s.shuttle_arrivals + 1,
          arrivals_without_needed: s.drop_without_needed,
          last_floor_arrival: s.drop_last_floor,
          rng: Math.random,
        };
      };

      /** Новый рейс шаттла. Первый в жизни игрока идет по FTUE-правилам. */
      const makeTrip = (): ShuttleTrip => {
        const s = get();
        return generateTrip({
          level: s.level,
          warehouse: s.warehouse,
          available_goods: availableGoodsFor(s.level, new Set<string>(s.buildings)),
          previous: s.shuttle,
          is_first_trip: s.shuttle_arrivals === 0,
          arrival_no: s.shuttle_arrivals + 1,
          rng: Math.random,
        });
      };

      /** Отправка случилась внутри домена — стор фиксирует ее последствия. */
      const applyDeparture = (
        trip: ShuttleTrip,
        drop_state: NonNullable<ReturnType<typeof loadSlot>['drop_state']>,
      ) => {
        const s = get();
        set({
          shuttle: trip,
          warehouse: { ...s.warehouse },
          shuttle_arrivals: s.shuttle_arrivals + 1,
          drop_pity: drop_state.next_pity,
          drop_without_needed: drop_state.next_arrivals_without_needed,
          drop_last_floor: drop_state.next_last_floor_arrival,
        });
        applyXp(tripXp(trip));
        pushToast('Шаттл ушел на орбиту', 'reward');
      };

      return {
        ...createInitialState(),

        tick: (now) => {
          const s = get();
          const fields = s.fields.map((f) => refreshField({ ...f }, now));
          const factory_slots = s.factory_slots.map((sl) => refreshFactorySlot({ ...sl }, now));

          // Доска наполняется лениво, по тику: слот с истекшим таймером получает
          // новый заказ, а недостающие слоты (после левелапа) — свои первые.
          let orders = s.orders;
          if (s.level >= MECHANIC_UNLOCK_LEVEL.drone) {
            const target = slotsAtLevel(s.level);
            orders = [...orders];
            while (orders.length < target) orders.push(makeOrder(orders.length, now));
            orders = orders.map((slot) =>
              slot.state === 'empty_cooldown' && now >= slot.refresh_at
                ? makeOrder(slot.idx, now)
                : slot,
            );
          }

          // Шаттл: прибытие по времени и новый заказ после кулдауна. Заказ не
          // выдается, пока не собран прошлый груз, — иначе контейнеры прошлого
          // рейса молча исчезли бы вместе с рейсом.
          let shuttle = s.shuttle;
          if (s.level >= MECHANIC_UNLOCK_LEVEL.shuttle) {
            if (shuttle === null) shuttle = makeTrip();
            else if (shuttle.state === 'COOLDOWN' && now >= shuttle.cooldown_until)
              shuttle = makeTrip();
            else shuttle = refreshTrip({ ...shuttle }, now);
          }

          const construction = {
            ...s.construction,
            builds: refreshBuilds(s.construction, s.level, now, s.warehouse),
          };

          set({
            now,
            fields,
            factory_slots,
            orders,
            shuttle,
            construction,
            warehouse: { ...s.warehouse },
          });
        },

        /**
         * Погрузка отсека шаттла. Кнопки «Отправить» нет: домен сам стартует рейс,
         * когда закрылся последний отсек (ТЗ 2.3). Стор об этом узнает по флагу.
         */
        loadShuttleSlot: (idx) => {
          const s = get();
          if (!s.shuttle) return;
          const trip = { ...s.shuttle, slots: s.shuttle.slots.map((sl) => ({ ...sl })) };

          const result = loadSlot(trip, idx, s.warehouse, s.now, dropCtx());
          if (!result.ok) {
            pushToast('Нет на складе', 'warn');
            return;
          }
          if (result.departed && result.drop_state) applyDeparture(trip, result.drop_state);
          else set({ shuttle: trip, warehouse: { ...s.warehouse } });
        },

        buyoutShuttleSlot: (idx) => {
          const s = get();
          if (!s.shuttle) return;
          const trip = { ...s.shuttle, slots: s.shuttle.slots.map((sl) => ({ ...sl })) };
          const slot = trip.slots[idx];
          if (!slot) return;

          const price = slotBuyoutPrice(slot, s.warehouse);
          if (price > s.isotopes) {
            pushToast(`Нужно ${price} изотопов`, 'warn');
            return;
          }

          const result = buyoutSlot(trip, idx, s.warehouse, s.now, dropCtx());
          if (!result.ok) return;

          set({ isotopes: s.isotopes - result.price });
          if (result.departed && result.drop_state) applyDeparture(trip, result.drop_state);
          else set({ shuttle: trip });
        },

        skipShuttle: () => {
          const s = get();
          if (s.shuttle?.state !== 'IN_TRANSIT') return;
          const price = skipPrice(s.shuttle, s.now);
          if (price > s.isotopes) {
            pushToast(`Нужно ${price} изотопов`, 'warn');
            return;
          }
          const trip = { ...s.shuttle, slots: s.shuttle.slots.map((sl) => ({ ...sl })) };
          if (!skipFlight(trip, s.now)) return;
          set({ shuttle: trip, isotopes: s.isotopes - price });
        },

        /**
         * Вскрытие контейнера. Переполнение склада модулей отказывает целиком:
         * контейнер остается закрытым и ждет, а не растворяется наполовину.
         */
        collectContainerAt: (idx) => {
          const s = get();
          if (s.shuttle?.state !== 'ARRIVED') return;
          const trip = { ...s.shuttle, slots: s.shuttle.slots.map((sl) => ({ ...sl })) };
          const stock: ModuleCounts = { ...s.construction.stock };
          const construction = { ...s.construction, stock };

          const module_id = collectContainer(trip, idx);
          if (module_id === null) return;
          if (!addModule(construction, module_id)) {
            pushToast('Склад модулей полон', 'warn');
            return;
          }

          if (allCollected(trip)) startCooldown(trip, s.now);
          set({ shuttle: trip, construction });
          pushToast(`+1 ${MODULES[module_id].name}`, 'reward');
        },

        collectAllContainers: () => {
          const count = get().shuttle?.slots.length ?? 0;
          for (let i = 0; i < count; i++) get().collectContainerAt(i);
        },

        startConstruction: (kind) => {
          const s = get();
          const construction = {
            ...s.construction,
            stock: { ...s.construction.stock },
            builds: s.construction.builds.map((b) => ({ ...b })),
          };
          const result = startBuild(construction, kind, s.now);
          if (!result.ok) {
            if (result.reason === 'missing_modules') pushToast('Не хватает модулей', 'warn');
            if (result.reason === 'no_free_line') pushToast('Линия стройки занята', 'warn');
            return;
          }
          set({ construction });
          pushToast(`Стройка начата: ${CONSTRUCTION_RECIPE[kind].name}`, 'reward');
        },

        speedupConstruction: (kind) => {
          const s = get();
          const builds = s.construction.builds.map((b) => ({ ...b }));
          const build = builds.find((b) => b.kind === kind);
          if (build?.state !== 'IN_PROGRESS') return;

          const price = constructionSpeedupCost(build.ends_at - s.now);
          if (price > s.isotopes) {
            pushToast(`Нужно ${price} изотопов`, 'warn');
            return;
          }
          build.ends_at = s.now;
          set({
            construction: { ...s.construction, builds },
            isotopes: s.isotopes - price,
          });
        },

        /** Погрузка позиции: резерв со склада, состояние слота пересчитывается доменом. */
        loadOrderPosition: (slot_idx, position_idx) => {
          const orders = get().orders.map((o) => ({
            ...o,
            positions: o.positions.map((p) => ({ ...p })),
          }));
          const slot = orders[slot_idx];
          if (!slot) return;

          if (!loadPosition(slot, position_idx, get().warehouse)) {
            pushToast('Не хватает товара на складе', 'warn');
            return;
          }
          set({ orders, warehouse: { ...get().warehouse } });
        },

        /**
         * Докупка позиции заказа за изотопы.
         *
         * До этой правки кнопка «Докупить» на доске дрона показывала цену и
         * вызывала обработчик погрузки: изотопы не списывались, позиция не
         * закрывалась, игрок получал тост «Не хватает товара на складе». Цена за
         * действие, которого не существует, хуже отсутствия кнопки.
         */
        buyoutOrderPosition: (slot_idx, position_idx) => {
          const s = get();
          const orders = s.orders.map((o) => ({
            ...o,
            positions: o.positions.map((p) => ({ ...p })),
          }));
          const slot = orders[slot_idx];
          if (!slot) return;

          const position = slot.positions[position_idx];
          if (!position) return;

          const price = positionBuyoutPrice(position);
          if (price > s.isotopes) {
            pushToast(`Нужно ${price} изотопов`, 'warn');
            return;
          }

          if (!buyoutPosition(slot, position_idx)) return;

          // Склад не трогаем сознательно: по И-12 докупленный товар приходит извне
          // и на полке не появляется. Отправка отличит его по `filled_by`.
          set({ orders, isotopes: s.isotopes - price });
        },

        sendOrderAt: (slot_idx) => {
          const s = get();
          const orders = s.orders.map((o) => ({ ...o }));
          const slot = orders[slot_idx];
          if (!slot) return;

          const result = sendOrder(slot, s.warehouse);
          if (!result.ok) return;

          // Слот сразу уходит в новый заказ: у отправки нет таймера, платой за
          // скорость служит сам заказ, а не ожидание.
          orders[slot_idx] = makeOrder(slot_idx, s.now);
          set({ orders, warehouse: { ...s.warehouse }, credits: s.credits + result.credits });
          applyXp(result.xp);
          pushToast(`Дрон улетел: +${result.credits} кр, +${result.xp} XP`, 'reward');
        },

        discardOrderAt: (slot_idx) => {
          const s = get();
          const orders = s.orders.map((o) => ({
            ...o,
            positions: o.positions.map((p) => ({ ...p })),
          }));
          const slot = orders[slot_idx];
          if (!slot) return;

          // Порядок важен: сначала вернуть резерв, потом гасить слот. Иначе товар
          // остался бы заперт навсегда — заказа уже нет, а резерв на нем висит.
          releaseReserved(slot, s.warehouse);
          discardOrder(slot, s.now);
          set({ orders, warehouse: { ...s.warehouse } });
          // Освобожденный резерв — такое же пополнение доступного остатка, как
          // урожай, и слот фабрики в очереди обязан на него проснуться. Раньше
          // будильник звали только сбор с грядки и сбор с фабрики, поэтому слот
          // мог голодать бессрочно, имея нужный товар физически на складе.
          notifyStockIncreased();
        },

        refreshSlotNow: (slot_idx) => {
          const s = get();
          const orders = s.orders.map((o) => ({ ...o }));
          const slot = orders[slot_idx];
          if (slot?.state !== 'empty_cooldown') return;

          const price = droneRefreshPrice(slot.refresh_at - s.now);
          if (price > s.isotopes) {
            pushToast(`Нужно ${price} изотопов`, 'warn');
            return;
          }
          orders[slot_idx] = makeOrder(slot_idx, s.now);
          set({ orders, isotopes: s.isotopes - price });
        },

        plant: (idx, good_id) => {
          const fields = get().fields.map((f) => ({ ...f }));
          const field = fields[idx];
          if (!field) return; // тап по несуществующей грядке — не ошибка, просто ничего
          const result = domainPlant(field, good_id, ctx(), fields);
          if (!result.ok) {
            if (result.reason === 'insufficient_balance')
              pushToast('Не хватает кредитов', 'warn');
            return;
          }
          set((s) => ({ fields, credits: s.credits + (result.credits_delta ?? 0) }));
          if (result.softlock_rescued) pushToast('Посев за счет колонии — запас пуст', 'info');
        },

        collectField: (idx) => {
          const fields = get().fields.map((f) => ({ ...f }));
          const field = fields[idx];
          if (!field) return;
          const result = domainCollectField(field, ctx());
          if (!result.ok) {
            if (result.reason === 'warehouse_full')
              pushToast('Склад полон — продай излишки', 'warn');
            return;
          }
          set({ fields, warehouse: { ...get().warehouse } });
          applyXp(result.xp_gained ?? 0);
          notifyStockIncreased();
        },

        enqueue: (idx, good_id) => {
          const slots = get().factory_slots.map((s) => ({ ...s }));
          const slot = slots[idx];
          if (!slot) return;
          const result = domainEnqueue(slot, good_id, ctx());
          if (!result.ok) return;
          set({ factory_slots: slots, warehouse: { ...get().warehouse } });
          if (result.reason === 'no_inputs') pushToast('Ждет сырье на складе', 'info');
        },

        collectFactory: (idx) => {
          const slots = get().factory_slots.map((s) => ({ ...s }));
          const slot = slots[idx];
          if (!slot) return;
          const result = domainCollectFactory(slot, ctx());
          if (!result.ok) {
            if (result.reason === 'warehouse_full')
              pushToast('Склад полон — продай излишки', 'warn');
            return;
          }
          set({ factory_slots: slots, warehouse: { ...get().warehouse } });
          applyXp(result.xp_gained ?? 0);
          notifyStockIncreased();
        },

        sell: (good_id, qty) => {
          const result = domainSell(good_id, qty, ctx());
          if (!result.ok) return;
          set((s) => ({
            credits: s.credits + (result.credits_delta ?? 0),
            warehouse: { ...s.warehouse },
          }));
        },

        /**
         * Ускорение грядки. Ниже порога бесплатно (AC7) — кнопка не исчезает,
         * а отдает действие даром: игрок, который уже тянулся к ней, получает
         * результат, и заодно видит механику, если пользуется ей впервые.
         */
        speedupField: (idx) => {
          const s = get();
          const fields = s.fields.map((f) => ({ ...f }));
          const field = fields[idx];
          if (field?.state !== 'GROWING') return;

          const good = field.good_id ? GOODS[field.good_id] : null;
          if (!good) return;

          const price = productionSpeedupCost(field.ends_at - s.now, good.kind);
          if (price > s.isotopes) {
            pushToast(`Нужно ${price} изотопов`, 'warn');
            return;
          }

          field.ends_at = s.now; // цикл завершен, собирать игрок будет сам
          field.state = 'READY';
          set({ fields, isotopes: s.isotopes - price });
          if (price === 0) pushToast('Дозрело', 'info');
        },

        speedupFactory: (idx) => {
          const s = get();
          const slots = s.factory_slots.map((sl) => ({ ...sl }));
          const slot = slots[idx];
          if (slot?.state !== 'PRODUCING' || !slot.good_id) return;

          const price = productionSpeedupCost(slot.ends_at - s.now, 'factory');
          if (price > s.isotopes) {
            pushToast(`Нужно ${price} изотопов`, 'warn');
            return;
          }

          slot.ends_at = s.now;
          slot.state = 'READY';
          set({ factory_slots: slots, isotopes: s.isotopes - price });
          if (price === 0) pushToast('Готово', 'info');
        },

        /**
         * Покупка здания класса А за кредиты. Мгновенно, без фазы стройки и без
         * модулей: обязательный прогрессионный контент не должен зависеть от
         * дропа механики, которая открывается позже него.
         */
        buyBuilding: (type) => {
          const s = get();
          const def = FACTORY_PRICES[type];
          if (s.level < def.unlock_level) return;
          if (s.buildings.includes(type)) return;
          if (s.credits < def.first) {
            pushToast(`Нужно ${def.first} кредитов`, 'warn');
            return;
          }
          // Здание без очереди — здание, в котором нечего делать. Слоты заводятся
          // вместе с покупкой, иначе игрок платит и не видит никакой разницы.
          const slots = [...s.factory_slots];
          for (let i = 0; i < FACTORY_QUEUE_BASE_SLOTS; i++) {
            slots.push(createFactorySlot(slots.length, type));
          }

          set({
            credits: s.credits - def.first,
            buildings: [...s.buildings, type],
            factory_slots: slots,
          });
          // Название впереди сказуемого не ставим: «Буровая площадка построен»
          // — здания в игре разного рода, а одна строка на все не согласуется
          // ни с одним. Двоеточие снимает вопрос целиком.
          pushToast(`Построено: ${FACTORY_NAMES[type]}`, 'reward');
        },

        dismissToast: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
      };
    },
    {
      name: SAVE_KEY,
      version: SAVE_VERSION,
      storage: saveStorage(defaultBackend()),
      /** В файл уходит только то, что перечислено, — функции и производное нет. */
      partialize: (state) =>
        Object.fromEntries(SAVED_KEYS.map((key) => [key, state[key]])) as SaveData,
      /**
       * Миграций нет: сейв чужой версии выбрасывается и перезаписывается новым.
       * Пока схема одна, любая миграция была бы кодом без входных данных.
       */
      migrate: () => undefined,
      merge: mergeSave,
      /**
       * Реконсиляция после офлайна одним шагом времени — клиентский аналог
       * `GET /state` ([[tz-production-mars]] 2): состояния приходят уже
       * пересчитанными, без догоняющей анимации и без отыгрыша пропущенных
       * тиков.
       */
      onRehydrateStorage: () => (state) => {
        state?.tick(nowSec());
      },
    },
  ),
);

/** Селекторы для UI, чтобы компоненты не считали ничего сами. */
export const selectWarehouseLoad = (s: GameState) => ({
  used: totalQty(s.warehouse),
  cap: s.warehouse.capacity,
});

export const selectXpProgress = (s: GameState) => ({
  into: s.xp_into_level,
  need: xpToNext(s.level),
});

/**
 * Тестовый шов для браузерных проверок. Петля шаттла занимает час игрового
 * времени, и пройти ее живым кликом в e2e невозможно — состояние приходится
 * ставить снаружи. Шов открыт только в dev-сборке: в прод-бандле этой ветки
 * нет, ее вырезает сборщик по константе.
 */
if (
  (import.meta.env.DEV || import.meta.env.VITE_E2E === '1') &&
  typeof window !== 'undefined'
) {
  (window as unknown as { __game?: typeof useGame }).__game = useGame;
}
