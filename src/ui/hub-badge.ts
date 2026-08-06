/**
 * Бейджи хаба внимания — каркас раздел 13: «Хаб внимания — ровно 7 иконок...
 * Приоритет бейджей: лайнер > шаттл > дрон > союзники > стройка > фабрика >
 * склад» и «Одновременно горит не более одного индикатора в хабе».
 *
 * В срезе живут шесть механик из семи (лайнера и союзников нет), порядок
 * приоритета для оставшихся сохраняется тем же: шаттл > дрон > стройка >
 * фабрика > склад.
 *
 * Купол в приоритет не входит и бейджа не несет: READY-состояние грядки уже
 * видно прямо на канвасе, который и так открыт (ТЗ производства 9.1,
 * пульсация READY-спрайта) — тем же приемом, каким кольцо первой цели
 * ([[first-goal]]) никогда не встает на кнопку «Купол».
 *
 * Логика вынесена в чистую функцию намеренно: «одновременно горит не более
 * одного индикатора» — утверждение, которое доказывается перебором состояний
 * в тесте (`hub-badge.test.ts`), а не рендером и не взглядом на экран.
 */

import { useShallow } from 'zustand/react/shallow';
import type { ConstructionState } from '../domain/construction';
import { missingFor } from '../domain/construction';
import type { OrderSlot } from '../domain/drone';
import type { FactorySlot } from '../domain/production';
import type { ShuttleTrip } from '../domain/shuttle';
import { totalQty, type WarehouseState } from '../domain/warehouse';
import { useGame } from '../state/gameStore';
import { WAREHOUSE_WARN_RATIO } from './kit';

export type BadgeHub = 'shuttle' | 'drone' | 'construction' | 'factory' | 'warehouse';

/** Порядок каркаса раздела 13, сокращенный до шести реализованных механик. */
const BADGE_PRIORITY: readonly BadgeHub[] = [
  'shuttle',
  'drone',
  'construction',
  'factory',
  'warehouse',
];

/** Минимальный срез состояния игры, нужный для расчета бейджа хаба. */
export interface HubBadgeState {
  shuttle: ShuttleTrip | null;
  orders: OrderSlot[];
  construction: ConstructionState;
  factory_slots: FactorySlot[];
  warehouse: WarehouseState;
}

/**
 * Горит ли бейдж конкретной механики.
 *
 * Триггеры шаттла, дрона и фабрики — дословно из ТЗ (см. докстринг файла и
 * комментарии по месту). Триггеры стройки и склада ТЗ не задает вовсе —
 * решения исполнителя, записаны в реестр spec-prototype-build.md, раздел 8,
 * пункт 21.
 */
function isActive(hub: BadgeHub, s: HubBadgeState): boolean {
  switch (hub) {
    case 'shuttle':
      // ТЗ шаттла 6.1: табло «ПРИБЫТИЕ: Прибыл! Тап для разгрузки» — рейс
      // ждет игрока ровно в состоянии `ARRIVED`, до сбора всех контейнеров.
      return s.shuttle !== null && s.shuttle.state === 'ARRIVED';

    case 'drone':
      // ТЗ дрона 7.1: бейдж карточки заказа «ГОТОВ» — состояние `ready`,
      // заказ ждет отправки одним тапом «Отправить» прямо с доски.
      return s.orders.some((o) => o.state === 'ready');

    case 'construction': {
      // Решение исполнителя (реестр п.21): у класса Б нет отдельного шага
      // сбора — `DONE` наступает автоматически по готовности, аналога READY
      // нет. Единственный момент, где стройка ждет именно тапа игрока, а не
      // времени или ресурсов, — комплект собран полностью (чек-лист закрыт)
      // и кнопка «Строить» уже активна, но стройка еще не запущена.
      return s.construction.builds.some(
        (b) =>
          b.state === 'AVAILABLE' &&
          Object.keys(missingFor(b, s.construction.stock)).length === 0,
      );
    }

    case 'factory':
      // ТЗ производства раздел 13 («мокается/упрощается»): «Push-уведомление
      // о простое READY... заменяется in-app бейджем на иконке хаба,
      // аналогично решению для шаттла» — состояние `READY` слота фабрики.
      return s.factory_slots.some((slot) => slot.state === 'READY');

    case 'warehouse':
      // Решение исполнителя (реестр п.21): самый низкий приоритет, ТЗ не
      // задает отдельного триггера. Взят тот же порог, что красит капасити-
      // бар на самом экране склада (9.2, «>=90%») — «почти полон» единственный
      // сигнал склада, не видный без захода на экран.
      return totalQty(s.warehouse) >= s.warehouse.capacity * WAREHOUSE_WARN_RATIO;
  }
}

/**
 * Ровно один зажженный индикатор хаба — самый приоритетный из активных,
 * либо `null`, если ни одна механика игрока не ждет. Возврат первого
 * совпадения по `BADGE_PRIORITY` и есть весь механизм «не более одного
 * одновременно»: массив короче списка активных бейджей быть не может, а
 * `find` останавливается на первом.
 */
export function topHubBadge(s: HubBadgeState): BadgeHub | null {
  return BADGE_PRIORITY.find((hub) => isActive(hub, s)) ?? null;
}

/** Хук на месте использования — обертка над стором, без своей логики. */
export function useHubBadge(): BadgeHub | null {
  return useGame(useShallow((s) => topHubBadge(s)));
}
