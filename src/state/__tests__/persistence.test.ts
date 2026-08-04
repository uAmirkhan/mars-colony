/**
 * Сохранение прогресса: файл сейва, загрузка, реконсиляция офлайна.
 *
 * Правила берутся из документов, а не из реализации:
 *   - таймеры не замирают офлайн и считаются от абсолютных меток —
 *     [[tz-production-mars]] п.2 и AC 14, [[tz-shuttle-mars]] п.11;
 *   - слот дрона с истекшим рефрешем приходит с новым заказом и без
 *     ретроактивных начислений — [[tz-drone-mars]] AC 15;
 *   - содержимое контейнеров зафиксировано при отправке, переустановка клиента
 *     и смена системного времени его не рероллят — [[tz-shuttle-mars]] п.11;
 *   - прототип живет на localStorage-эмуляции сервера-заглушки — каркас п.11,
 *     [[tz-common-systems-mars]] п.12.
 *
 * Здесь же проверяется класс «тихой порчи»: сейв, разошедшийся с исходными
 * данными (резерв склада против погруженных позиций заказа), и чужое или битое
 * содержимое ключа, принятое за свое.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CREDITS_START } from '../../domain/config/economy';
import { GOODS } from '../../domain/config/goods';
import { MECHANIC_UNLOCK_LEVEL } from '../../domain/config/levels';
import { createWarehouse, deposit, qtyOf, type WarehouseState } from '../../domain/warehouse';
import {
  createInitialState,
  mergeSave,
  SAVE_KEY,
  SAVE_VERSION,
  type SaveBackend,
  saveStorage,
  useGame,
} from '../gameStore';

const T0 = 1_000_000;

let clock = T0;
let file = new Map<string, string>();

/** Хранилище теста: тот же интерфейс, что у localStorage, но под контролем. */
const backend: SaveBackend = {
  getItem: (key) => file.get(key) ?? null,
  setItem: (key, value) => {
    file.set(key, value);
  },
  removeItem: (key) => {
    file.delete(key);
  },
};

const s = () => useGame.getState();
const setNow = (sec: number) => {
  clock = sec;
};

beforeEach(() => {
  file = new Map();
  clock = T0;
  vi.spyOn(Date, 'now').mockImplementation(() => clock * 1000);
  useGame.persist.setOptions({ storage: saveStorage(backend) });
  useGame.setState(createInitialState());
});

afterEach(() => {
  vi.restoreAllMocks();
});

/**
 * Загрузка страницы с заданным содержимым хранилища.
 *
 * Сброс стора здесь — артефакт теста (модуль в процессе один), поэтому файл
 * записывается ПОСЛЕ сброса: иначе сброс затер бы сейв своим же снимком.
 */
async function bootWith(saved: string | null): Promise<void> {
  useGame.setState(createInitialState());
  if (saved === null) backend.removeItem(SAVE_KEY);
  else backend.setItem(SAVE_KEY, saved);
  await useGame.persist.rehydrate();
}

/** Перезагрузка страницы: файл остается тем, что игра записала сама. */
const reload = () => bootWith(backend.getItem(SAVE_KEY));

const savedState = (): Record<string, unknown> => {
  const raw = backend.getItem(SAVE_KEY);
  expect(raw).not.toBeNull();
  return (JSON.parse(raw as string) as { state: Record<string, unknown> }).state;
};

function stocked(pairs: Array<[Parameters<typeof deposit>[1], number]>): WarehouseState {
  const w = createWarehouse(300);
  for (const [id, qty] of pairs) deposit(w, id, qty);
  return w;
}

describe('Файл сейва', () => {
  it('пишется сам, без отдельной команды сохранения', () => {
    s().plant(0, 'algae');
    expect(backend.getItem(SAVE_KEY)).not.toBeNull();
  });

  it('несет версию схемы', () => {
    s().plant(0, 'algae');
    const raw = JSON.parse(backend.getItem(SAVE_KEY) as string) as { version: number };
    expect(raw.version).toBe(SAVE_VERSION);
  });

  it('содержит данные состояния', () => {
    useGame.setState({ credits: 777, level: 4, isotopes: 12 });
    expect(savedState().credits).toBe(777);
    expect(savedState().level).toBe(4);
    expect(savedState().isotopes).toBe(12);
  });

  it('не содержит производного: ни времени, ни тостов', () => {
    useGame.setState({ toasts: [{ id: 1, text: 'тост', kind: 'info' }] });
    expect(savedState()).not.toHaveProperty('now');
    expect(savedState()).not.toHaveProperty('toasts');
  });

  it('не содержит функций', () => {
    s().plant(0, 'algae');
    for (const value of Object.values(savedState())) {
      expect(typeof value).not.toBe('function');
    }
  });
});

describe('Загрузка', () => {
  it('уровень, валюты и склад переживают перезагрузку', async () => {
    useGame.setState({
      level: 6,
      xp_into_level: 42,
      credits: 1234,
      isotopes: 9,
      warehouse: stocked([['algae', 17]]),
    });

    await reload();

    expect(s().level).toBe(6);
    expect(s().xp_into_level).toBe(42);
    expect(s().credits).toBe(1234);
    expect(s().isotopes).toBe(9);
    expect(qtyOf(s().warehouse, 'algae')).toBe(17);
    expect(s().warehouse.capacity).toBe(300);
  });

  it('построенные здания и слоты фабрики переживают перезагрузку', async () => {
    useGame.setState({ level: 21, credits: 1_000_000 });
    s().buyBuilding('food_module');
    const slots = s().factory_slots.length;

    await reload();

    expect(s().buildings).toContain('food_module');
    expect(s().factory_slots).toHaveLength(slots);
  });

  it('стройка и склад модулей переживают перезагрузку', async () => {
    useGame.setState({
      level: 21,
      construction: {
        ...s().construction,
        stock: { filter: 6, cable: 6, sealant: 6 },
      },
    });
    s().tick(T0);
    s().startConstruction('warehouse_upgrade');
    const build = s().construction.builds.find((b) => b.kind === 'warehouse_upgrade');
    expect(build?.state).toBe('IN_PROGRESS');

    await reload();

    const after = s().construction.builds.find((b) => b.kind === 'warehouse_upgrade');
    expect(after?.state).toBe('IN_PROGRESS');
    expect(after?.ends_at).toBe(build?.ends_at);
  });

  it('время берется от часов, а не из файла', async () => {
    useGame.setState({ now: T0 });
    setNow(T0 + 86_400);

    await reload();

    expect(s().now).toBe(T0 + 86_400);
  });

  it('тосты не переживают перезагрузку: их некому было бы погасить', async () => {
    useGame.setState({ toasts: [{ id: 1, text: 'тост', kind: 'info' }] });
    await reload();
    expect(s().toasts).toHaveLength(0);
  });
});

describe('Офлайн: таймеры считаются от абсолютных меток', () => {
  it('грядка, дозревшая без игрока, приходит READY (ТЗ производства AC 14)', async () => {
    s().plant(0, 'algae');
    const ends_at = s().fields[0]?.ends_at ?? 0;
    expect(ends_at).toBeGreaterThan(T0);

    setNow(ends_at + 60);
    await reload();

    expect(s().fields[0]?.state).toBe('READY');
  });

  it('недозревшая грядка остается GROWING и хранит свою метку', async () => {
    s().plant(0, 'algae');
    const ends_at = s().fields[0]?.ends_at ?? 0;

    setNow(ends_at - 1);
    await reload();

    expect(s().fields[0]?.state).toBe('GROWING');
    expect(s().fields[0]?.ends_at).toBe(ends_at);
  });

  it('за офлайн не начисляется и не списывается ничего (ТЗ дрона AC 15)', async () => {
    useGame.setState({ ...createInitialState(), level: MECHANIC_UNLOCK_LEVEL.drone });
    s().tick(T0);
    s().discardOrderAt(0);
    const refresh_at = s().orders[0]?.refresh_at ?? 0;

    const credits = s().credits;
    const xp = s().xp_into_level;
    const isotopes = s().isotopes;

    // Ушел на десять кулдаунов вперед: заказ обязан обновиться ровно один раз.
    setNow(refresh_at + 10 * (refresh_at - T0) + 3600);
    await reload();

    expect(s().orders[0]?.state).not.toBe('empty_cooldown');
    expect(s().credits).toBe(credits);
    expect(s().xp_into_level).toBe(xp);
    expect(s().isotopes).toBe(isotopes);
  });

  it('слот дрона с неистекшим таймером остается в кулдауне', async () => {
    useGame.setState({ ...createInitialState(), level: MECHANIC_UNLOCK_LEVEL.drone });
    s().tick(T0);
    s().discardOrderAt(0);
    const refresh_at = s().orders[0]?.refresh_at ?? 0;

    setNow(refresh_at - 1);
    await reload();

    expect(s().orders[0]?.state).toBe('empty_cooldown');
  });
});

describe('Шаттл: сейв не перекатывает награду', () => {
  /** Отправленный рейс: отсеки закрыты со склада, награды зафиксированы. */
  function departedTrip() {
    useGame.setState({ ...createInitialState(), level: MECHANIC_UNLOCK_LEVEL.shuttle });
    s().tick(T0);

    const trip = s().shuttle;
    expect(trip).not.toBeNull();
    const w = createWarehouse(300);
    for (const slot of trip?.slots ?? []) deposit(w, slot.good_id, slot.qty_required);
    useGame.setState({ warehouse: w });

    const count = s().shuttle?.slots.length ?? 0;
    for (let i = 0; i < count; i++) s().loadShuttleSlot(i);
    expect(s().shuttle?.state).toBe('IN_TRANSIT');
  }

  it('содержимое контейнеров после перезагрузки то же (ТЗ шаттла п.11)', async () => {
    departedTrip();
    const rewards = s().shuttle?.slots.map((sl) => sl.reward);
    const arrives_at = s().shuttle?.arrives_at;

    await reload();

    expect(s().shuttle?.slots.map((sl) => sl.reward)).toEqual(rewards);
    expect(s().shuttle?.arrives_at).toBe(arrives_at);
    expect(s().shuttle?.state).toBe('IN_TRANSIT');
  });

  it('рейс, прилетевший без игрока, приходит ARRIVED', async () => {
    departedTrip();
    const arrives_at = s().shuttle?.arrives_at ?? 0;

    setNow(arrives_at + 60);
    await reload();

    expect(s().shuttle?.state).toBe('ARRIVED');
  });

  it('счетчики дроп-роллера переживают перезагрузку: F5 не сбрасывает pity', async () => {
    useGame.setState({
      drop_pity: { sealant: 3 },
      drop_without_needed: 2,
      drop_last_floor: 4,
      shuttle_arrivals: 5,
    });

    await reload();

    expect(s().drop_pity).toEqual({ sealant: 3 });
    expect(s().drop_without_needed).toBe(2);
    expect(s().drop_last_floor).toBe(4);
    expect(s().shuttle_arrivals).toBe(5);
  });
});

describe('Тихая порча', () => {
  it('резерв склада не расходится с погруженными позициями заказа', async () => {
    useGame.setState({ ...createInitialState(), level: 5 });
    s().tick(T0);

    // Склад набирается ПОД сгенерированный заказ, а не наоборот: иначе тест
    // зависел бы от того, что выпало генератору, и тихо пропускал проверку.
    const position = s().orders[0]?.positions[0];
    expect(position).toBeDefined();
    const good_id = position?.good_id as Parameters<typeof deposit>[1];
    useGame.setState({ warehouse: stocked([[good_id, position?.qty ?? 0]]) });

    s().loadOrderPosition(0, 0);
    const reserved = s().warehouse.cells[good_id]?.reserved ?? 0;
    expect(reserved).toBe(position?.qty);

    await reload();

    expect(s().warehouse.cells[good_id]?.reserved).toBe(reserved);
    // Обе половины пары на месте: резерв склада и позиция, которая его держит.
    expect(s().orders[0]?.positions[0]?.filled).toBe(true);
    expect(s().orders[0]?.positions[0]?.filled_by).toBe('self');
  });
});

describe('Битое и чужое хранилище', () => {
  const garbage: Array<[string, string]> = [
    ['обрезанный JSON', '{"state":{"credits":1'],
    ['не объект', '42'],
    ['массив', '[1,2,3]'],
    ['null', 'null'],
    ['пустая строка', ''],
    ['чужое приложение', '{"user":{"name":"кто-то"},"cart":[]}'],
    ['наш конверт с пустым состоянием', `{"state":{},"version":${SAVE_VERSION}}`],
    ['состояние без половины полей', `{"state":{"credits":10},"version":${SAVE_VERSION}}`],
    ['кредиты строкой', `{"state":{"credits":"много"},"version":${SAVE_VERSION}}`],
  ];

  for (const [name, payload] of garbage) {
    it(`${name}: запуск не падает и состояние стартовое`, async () => {
      await expect(bootWith(payload)).resolves.toBeUndefined();

      expect(s().level).toBe(1);
      expect(s().credits).toBe(CREDITS_START);
      expect(Number.isFinite(s().credits)).toBe(true);
      expect(s().fields.length).toBeGreaterThan(0);
    });
  }

  it('битый ключ не остается битым: первый же ход перезаписывает его', async () => {
    await bootWith('{"state":{"credits":1');
    s().plant(0, 'algae');

    await reload();
    expect(s().fields[0]?.state).toBe('GROWING');
  });

  it('сейв чужой версии выбрасывается целиком', async () => {
    const alien = JSON.stringify({
      state: { ...createInitialState(), credits: 999_999, level: 12 },
      version: SAVE_VERSION + 1,
    });

    await bootWith(alien);

    expect(s().level).toBe(1);
    expect(s().credits).toBe(CREDITS_START);
  });

  it('сейв чужой версии не остается в хранилище', async () => {
    await bootWith(JSON.stringify({ state: createInitialState(), version: SAVE_VERSION + 1 }));
    const raw = JSON.parse(backend.getItem(SAVE_KEY) as string) as { version: number };
    expect(raw.version).toBe(SAVE_VERSION);
  });

  it('своя версия принимается', async () => {
    await bootWith(
      JSON.stringify({
        state: { ...createInitialState(), credits: 4321 },
        version: SAVE_VERSION,
      }),
    );
    expect(s().credits).toBe(4321);
  });

  it('слияние с неузнаваемым сейвом возвращает текущее состояние', () => {
    const current = s();
    expect(mergeSave(undefined, current).credits).toBe(current.credits);
    expect(mergeSave({ credits: 1 }, current).credits).toBe(current.credits);
    expect(mergeSave('строка', current).credits).toBe(current.credits);
  });

  it('склад без ячеек не считается сейвом', () => {
    const broken = { ...createInitialState(), warehouse: { capacity: 50 } };
    expect(mergeSave(broken, s()).warehouse.capacity).toBe(s().warehouse.capacity);
  });
});

describe('Отказ хранилища', () => {
  it('исключение на записи не отменяет ход игрока', () => {
    useGame.persist.setOptions({
      storage: saveStorage({
        getItem: () => null,
        setItem: () => {
          throw new Error('квота исчерпана');
        },
        removeItem: () => {
          throw new Error('удалять нельзя');
        },
      }),
    });

    expect(() => s().plant(0, 'algae')).not.toThrow();
    expect(s().fields[0]?.state).toBe('GROWING');
    expect(s().credits).toBeLessThan(CREDITS_START);
  });

  it('исключение на чтении не роняет запуск', async () => {
    useGame.persist.setOptions({
      storage: saveStorage({
        getItem: () => {
          throw new Error('читать нельзя');
        },
        setItem: () => undefined,
        removeItem: () => undefined,
      }),
    });

    useGame.setState(createInitialState());
    await expect(useGame.persist.rehydrate()).resolves.toBeUndefined();
    expect(s().credits).toBe(CREDITS_START);
    expect(GOODS.algae.unlock_level).toBe(1); // игра осталась играбельной с нуля
  });
});
