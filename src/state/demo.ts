/**
 * Состояние показа: колония игрока, который уже поиграл.
 *
 * Зачем оно есть. Ссылку откроет человек на несколько минут, а не игрок на
 * вечер. От канонического старта до шаттла идти часы, и шаттл — центральная
 * вещь среза — в такой сессии недостижим вовсе. Показ поэтому начинается не с
 * начала сессии, а с ее середины.
 *
 * Как оно собрано. Не рисованием состояния руками, а прогоном настоящих
 * действий игры: здания покупаются `buyBuilding`, отсеки рейса грузятся
 * `loadShuttleSlot`, рейс уходит той же доменной отправкой, что и у игрока, и
 * награды роллит тот же дроп-роллер. Поэтому в показе нельзя увидеть
 * состояние, до которого нельзя дойти игрой, — а нарисованный руками сейв
 * ровно это и позволяет.
 *
 * Что выдано, а что заработано:
 *   - уровень, опыт и кредиты — ровно то, что начислила бы экономика к
 *     седьмому уровню, минус потраченное на здания;
 *   - изотопы, товар на складе и модули стройки — ВЫДАНЫ. Они экономят
 *     ожидание, и на экране про это написано прямо ([[demo-badge]]).
 *
 * Единственная подделка времени: рейс отматывается назад так, чтобы до
 * прибытия оставались минуты, а не час. Иначе смотрящий увидит таймер и
 * закроет вкладку, не дойдя до того, ради чего пришел.
 */

import { totalQty } from '../domain/warehouse';
import { createDemoState, DEMO_LEVEL, useGame } from './gameStore';

/** Сколько минут до прибытия рейса видит открывший ссылку. */
export const DEMO_TRIP_MIN_LEFT = 2;

/**
 * Здания, которые игрок к седьмому уровню уже купил бы. Пищевой открывается на
 * третьем, буровая на шестом; на оба хватает заработанных кредитов, и оба
 * нужны показу — без них половина экрана производства пустая.
 */
const DEMO_BUILDINGS = ['food_module', 'mining_site'] as const;

/**
 * Модули стройки. Комплект на расширение склада полный (6/6/6 по рецепту) —
 * это дает открывшему ссылку одно немедленное действие. На жилой блок
 * комплект неполный сознательно: его добирают контейнеры прилетающего рейса,
 * иначе шаттл прилетает в никуда.
 */
const DEMO_MODULES = { filter: 6, cable: 6, sealant: 7, panel: 4, frame: 3 } as const;

/** Запас на полках: чтобы доска дрона и очередь фабрики были не пустыми. */
const DEMO_STOCK = { algae: 12, soy: 10, mushrooms: 8 } as const;

/**
 * Собрать состояние показа и подставить его в стор.
 *
 * Возвращает `false`, если по дороге что-то не сложилось (рейс не выдался,
 * отсеки не закрылись). Молчаливый провал здесь опаснее обычного: витрина
 * открылась бы пустой колонией первого уровня, и это выглядело бы как
 * замысел, а не как поломка.
 */
export function applyDemoState(): boolean {
  const store = useGame;
  const now = Math.floor(Date.now() / 1000);

  store.setState({ ...createDemoState(), now });

  for (const type of DEMO_BUILDINGS) store.getState().buyBuilding(type);

  // Модули кладутся до рейса: `activeNeed` читает их при генерации, и от
  // потребности зависит, что именно привезут контейнеры.
  store.setState({
    construction: {
      ...store.getState().construction,
      stock: { ...store.getState().construction.stock, ...DEMO_MODULES },
    },
  });

  const warehouse = store.getState().warehouse;
  for (const [good, qty] of Object.entries(DEMO_STOCK)) {
    warehouse.cells[good as keyof typeof warehouse.cells] = { qty, reserved: 0 };
  }
  store.setState({ warehouse: { ...warehouse } });

  // Шаг времени выдает рейс и наполняет доску дрона — теми же путями, что у
  // живого игрока.
  store.getState().tick(now);

  const trip = store.getState().shuttle;
  if (trip === null) return false;

  // Отсеки закрываются со склада. Товар докладывается ровно под требование
  // отсека, а не «побольше»: лишнее на полке — это лишний повод для вопроса
  // «откуда», а ответ должен быть один и короткий.
  for (const slot of trip.slots) {
    const w = store.getState().warehouse;
    const have = w.cells[slot.good_id]?.qty ?? 0;
    const short = Math.max(0, slot.qty_required - have);
    if (short > 0) {
      w.cells[slot.good_id] = {
        qty: have + short,
        reserved: w.cells[slot.good_id]?.reserved ?? 0,
      };
      store.setState({ warehouse: { ...w } });
    }
    store.getState().loadShuttleSlot(slot.idx);
  }

  const departed = store.getState().shuttle;
  if (departed === null || departed.state !== 'IN_TRANSIT') return false;

  // Отмотка: рейс улетел давно, до прибытия остались минуты. Отматывается
  // именно ОТПРАВКА, а не прибытие, — цена скипа (И-6) считается от доли
  // пройденного пути, и сдвиг одного конца исказил бы ее.
  const left = DEMO_TRIP_MIN_LEFT * 60;
  store.setState({
    shuttle: {
      ...departed,
      departed_at: now - (departed.trip_min * 60 - left),
      arrives_at: now + left,
    },
  });

  return store.getState().level === DEMO_LEVEL && totalQty(store.getState().warehouse) >= 0;
}
