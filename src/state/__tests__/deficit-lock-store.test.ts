/**
 * И-13 (изоляция дефицита между механиками) на уровне стора: не повтор
 * доменных тестов, а проверка ПРОВОДКИ. Домен уже доказывает, что генератор
 * шаттла/дрона правильно читает и пишет лок ([[deficitlock.test.ts]]); здесь
 * проверяется другое — что `gameStore.ts` действительно прокидывает
 * `s.deficit_locks` в контекст генератора и действительно вызывает
 * `releaseDeficitLock`/`releaseOrderDeficitLocks` на терминальных событиях, а
 * не просто компилируется.
 *
 * Ровно тот класс риска, который эта сессия обязана закрыть целиком, а не
 * наполовину: тихая проводка, которую ни один доменный тест не увидит,
 * потому что домен вызывается напрямую, в обход стора.
 */

import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { MECHANIC_UNLOCK_LEVEL } from '../../domain/config/levels';
import type { DeficitLockState } from '../../domain/deficitlock';
import type { OrderSlot } from '../../domain/drone';
import { createField } from '../../domain/production';
import { createWarehouse } from '../../domain/warehouse';
import { useGame } from '../gameStore';

const NOW = 1_000_000;
const s = () => useGame.getState();

function reset(patch: Partial<ReturnType<typeof useGame.getState>> = {}) {
  useGame.setState({
    now: NOW,
    level: 9,
    xp_into_level: 0,
    credits: 0,
    isotopes: 0,
    warehouse: createWarehouse(300),
    fields: [createField(0)],
    factory_slots: [],
    buildings: [],
    toasts: [],
    orders: [],
    shuttle: null,
    // Не первый рейс: FTUE (arrivals===0) форсирует свой отдельный движок
    // (COVERAGE_MIN=1.0, MAX_DEFICIT_SLOTS=0, «если не easy — исключить»,
    // см. `shuttle.ts`), который исключает любой тяжелый товар НЕЗАВИСИМО
    // от локов И-13. Тесты этого файла проверяют штатный движок.
    shuttle_arrivals: 5,
    deficit_locks: {},
    ...patch,
  });
}

beforeEach(() => reset());

describe('И-13 на сторе: чтение лока (makeTrip читает s.deficit_locks)', () => {
  const original_random = Math.random;
  afterEach(() => {
    Math.random = original_random;
  });

  it('шаттл не берет в рейс товар, залоченный дроном, даже когда склад его не покрывает', () => {
    expect(MECHANIC_UNLOCK_LEVEL.shuttle).toBeLessThanOrEqual(7);
    const deficit_locks: DeficitLockState = {
      tomatoes: {
        good_id: 'tomatoes',
        locked_by_mechanic: 'drone',
        order_ref: 'drone:0',
        created_at: NOW,
        expires_at: NOW + 3600,
      },
    };

    // Без построек пул шаттла на 7 уровне — РОВНО четыре кропа (водоросли,
    // соя, грибы, томаты). Константный ролл 0.7 в SLOT_COUNT_WEIGHTS
    // (level>=5: {3:0.6, 4:0.3, 5:0.1}) детерминированно дает count=4 — весь
    // пул целиком. Это важно не только для того, чтобы томаты стали
    // кандидатом: пул, использованный без остатка, не оставляет И-10
    // (реализуемость, `rebalanceForAchievability`) запасного товара для
    // замены — иначе достижимость сама вытеснила бы томаты (слишком долгий
    // цикл) НЕЗАВИСИМО от лока, и тест зеленел бы по чужой причине. Ровно в
    // эту ловушку я и попал на первом черновике этого теста: с более широким
    // пулом (девятый уровень, пять кропов) достижимость подменяла томаты на
    // хлопок сама, до всякой проверки лока.
    const warehouse = createWarehouse(300);
    warehouse.cells.algae = { qty: 100, reserved: 0 };
    warehouse.cells.soy = { qty: 100, reserved: 0 };
    warehouse.cells.mushrooms = { qty: 100, reserved: 0 };
    Math.random = () => 0.7;
    reset({ level: 7, buildings: [], warehouse, shuttle: null, deficit_locks });

    s().tick(NOW);

    expect(s().shuttle).not.toBeNull();
    expect(s().shuttle?.slots.length).toBeGreaterThan(0);
    expect(s().shuttle?.slots.some((sl) => sl.good_id === 'tomatoes')).toBe(false);
    // Стор не тронул чужой лок — все еще за дроном, с тем же order_ref.
    expect(s().deficit_locks.tomatoes?.locked_by_mechanic).toBe('drone');
    expect(s().deficit_locks.tomatoes?.order_ref).toBe('drone:0');
  });

  it('контрольный прогон: без чужого лока томаты остаются доступным кандидатом (иначе предыдущая проверка ничего не доказывает)', () => {
    // Тот же уровень/склад/ролл, что в проверке выше, но без стартового лока.
    // Дрон генерирует доску РАНЬШЕ шаттла в том же тике (`tick`, порядок
    // кода) и почти наверняка возьмет томаты первым — это ожидаемо и не
    // противоречит И-13 (лок ставит и держит одна из механик, какая
    // окажется первой). Важно другое: без стартового блока томаты хоть
    // ГДЕ-то оказываются кандидатом и лок на них появляется — контраст с
    // тестом выше, где их не взял НИКТО, потому что чужой лок стоял ДО тика.
    const warehouse = createWarehouse(300);
    warehouse.cells.algae = { qty: 100, reserved: 0 };
    warehouse.cells.soy = { qty: 100, reserved: 0 };
    warehouse.cells.mushrooms = { qty: 100, reserved: 0 };
    Math.random = () => 0.7;
    reset({ level: 7, buildings: [], warehouse, shuttle: null, deficit_locks: {} });

    s().tick(NOW);

    const taken_by_drone = s().orders.some((o) =>
      o.positions.some((p) => p.good_id === 'tomatoes'),
    );
    const taken_by_shuttle =
      s().shuttle?.slots.some((sl) => sl.good_id === 'tomatoes') ?? false;
    expect(taken_by_drone || taken_by_shuttle).toBe(true);
    expect(s().deficit_locks.tomatoes).toBeDefined();
  });
});

describe('И-13 на сторе: снятие лока на терминальном событии дрона', () => {
  /** Заказ с готовой дефицитной позицией — собран вручную, генератор не нужен. */
  function deficitOrderSlot(): OrderSlot {
    return {
      idx: 0,
      state: 'ready',
      npc_name: 'Тест',
      positions: [
        {
          good_id: 'tomatoes',
          qty: 3,
          qty_filled: 3,
          filled_by: 'self',
          qty_purchased: 0,
          easy: false,
        },
      ],
      credits_reward: 42,
      xp_reward: 7,
      refresh_at: 0,
    };
  }

  function lockedState(): DeficitLockState {
    return {
      tomatoes: {
        good_id: 'tomatoes',
        locked_by_mechanic: 'drone',
        order_ref: 'drone:0',
        created_at: NOW,
        expires_at: NOW + 3600,
      },
    };
  }

  it('discardOrderAt снимает лок вместе с резервом', () => {
    reset({ orders: [deficitOrderSlot()], deficit_locks: lockedState() });
    expect(s().deficit_locks.tomatoes).toBeDefined();

    s().discardOrderAt(0);

    expect(s().deficit_locks.tomatoes).toBeUndefined();
  });

  it('sendOrderAt снимает лок при отправке (склад держит зарезервированное)', () => {
    const warehouse = createWarehouse(300);
    warehouse.cells.tomatoes = { qty: 3, reserved: 3 };
    reset({ orders: [deficitOrderSlot()], deficit_locks: lockedState(), warehouse });

    s().sendOrderAt(0);

    expect(s().deficit_locks.tomatoes).toBeUndefined();
  });
});
