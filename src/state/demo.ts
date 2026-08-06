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

import {
  fieldsAtLevel,
  PLANT_COST_FLOOR,
  PLANT_COST_PRICE_SHARE,
  plantingCost,
} from '../domain/config/economy';
import { GOODS } from '../domain/config/goods';
import { levelUpReward } from '../domain/config/levels';
import { CONSTRUCTION_RECIPE } from '../domain/config/modules';
import { createField } from '../domain/production';
import type { GoodId, ModuleId } from '../domain/types';
import { totalQty } from '../domain/warehouse';
import {
  createInitialState,
  type GameState,
  type SaveData,
  useGame,
  type VolatileKey,
} from './gameStore';

/* Определения показа держатся здесь, а не в сторе: стор обещает в своем
 * заголовке, что не хранит ни одного числа, и это обещание стоит дороже
 * удобства. Числа показа — параметры витрины, а не правила игры, поэтому им
 * не место и в `domain/config`. */

/**
 * Изотопы, выданные состоянию показа.
 *
 * Полный скип рейса из трех отсеков стоит 210 (И-6), значит тысячи хватает
 * примерно на пять полных скипов плюс ускорения стройки.
 *
 * Почему не «бесконечно»: цена скипа выведена формулой и стоит на кнопке — это
 * единственное место, где видно, как устроена монетизация. Безлимит стирает
 * ровно то, ради чего экран показывают. Почему не «сколько заработал»: к
 * седьмому уровню экономика дает 145 изотопов, а полный скип стоит 210, то
 * есть смотрящему пришлось бы ждать половину рейса.
 *
 * Число живет здесь, а не в `domain/config`: это параметр показа, а не правило
 * игры. И не в сторе — стор обещает в своем заголовке, что чисел не держит.
 */
export const DEMO_ISOTOPES = 1000;

/** Уровень состояния показа: на нем открыты все четыре механики среза. */
export const DEMO_LEVEL = 7;

/**
 * Сколько рейсов колония уже отправила до показа.
 *
 * Не украшение. Первый в жизни рейс идет по правилам FTUE: укороченный таймер
 * и форсированный состав отсеков ([[tz-shuttle-mars]] 2.1). Показ при нуле
 * прибытий получал именно его — то есть объявлял себя серединой сессии, а
 * предъявлял обучающий рейс. Четыре — столько рейсов успевает сделать игрок,
 * дошедший до седьмого уровня.
 */
export const DEMO_ARRIVALS = 4;

/** Сколько минут до прибытия рейса видит открывший ссылку. */
export const DEMO_TRIP_MIN_LEFT = 2;

/**
 * Здания, которые игрок к седьмому уровню уже купил бы. Пищевой открывается на
 * третьем, буровая на шестом; на оба хватает заработанных кредитов, и оба
 * нужны показу — без них половина экрана производства пустая.
 */
const DEMO_BUILDINGS = ['food_module', 'mining_site'] as const;

/**
 * Модули стройки.
 *
 * Комплект на расширение склада ПОЛНЫЙ — это дает открывшему ссылку одно
 * немедленное действие. На жилой блок комплект неполный сознательно: его
 * добирают контейнеры прилетающего рейса, иначе шаттл прилетает в никуда.
 *
 * Считается от рецепта, а не выписан числами. Выписанные числа расходятся с
 * рецептом молча: правка конфига делает «полный комплект» неполным, показ
 * теряет свое единственное немедленное действие, и никто об этом не узнает.
 */
const HABITAT_SHORT_BY = 2;

function demoModules(): Partial<Record<ModuleId, number>> {
  const out: Partial<Record<ModuleId, number>> = {};
  for (const [id, need] of Object.entries(
    CONSTRUCTION_RECIPE.warehouse_upgrade.recipe,
  ) as Array<[ModuleId, number]>) {
    out[id] = need;
  }
  for (const [id, need] of Object.entries(CONSTRUCTION_RECIPE.habitat_block.recipe) as Array<
    [ModuleId, number]
  >) {
    // Складываем, а не заменяем: герметик входит в оба рецепта, и склад у
    // модулей общий. Иначе второй проход обнулил бы первый комплект.
    out[id] = (out[id] ?? 0) + Math.max(0, need - HABITAT_SHORT_BY);
  }
  return out;
}

/** Запас на полках: чтобы доска дрона и очередь фабрики были не пустыми. */
const DEMO_STOCK = { algae: 12, soy: 10, mushrooms: 8 } as const;

/**
 * Чем засеяны грядки и сколько осталось расти.
 *
 * Пустое поле — главное, что портило первый кадр: колония «игрока, который уже
 * поиграл», встречала семью пустыми клетками с плюсом. Поэтому грядки засеяны,
 * одна созрела (немедленное действие, отклик в первую секунду), остальные
 * растут вразнобой — ровно то, как выглядит поле в середине сессии, а не
 * ровный ряд из одного действия.
 *
 * Отрицательный остаток означает «созрело столько секунд назад».
 */
const DEMO_FIELDS: Array<{ good: GoodId; left_sec: number }> = [
  { good: 'algae', left_sec: -30 },
  { good: 'mushrooms', left_sec: 45 },
  { good: 'soy', left_sec: 90 },
  { good: 'algae', left_sec: 150 },
  { good: 'mushrooms', left_sec: 240 },
];

/**
 * Основание состояния показа: уровень, кошелек, грядки.
 *
 * **Ни одно число здесь не выдумано, кроме изотопов.** Уровень, кредиты и опыт
 * — ровно то, что начислила бы экономика за семь уровней; число грядок — то,
 * что дает уровень. Изотопы выданы и подписаны на экране отдельно.
 */
export function createDemoState(): SaveData & Pick<GameState, VolatileKey> {
  const base = createInitialState();

  let credits = base.credits;
  let isotopes = 0;
  for (let lvl = 1; lvl < DEMO_LEVEL; lvl++) {
    const reward = levelUpReward(lvl + 1);
    credits += reward.credits ?? 0;
    isotopes += reward.isotopes ?? 0;
  }

  return {
    ...base,
    level: DEMO_LEVEL,
    credits,
    // Заработанное складываем с выданным, а не заменяем: так число на экране
    // остается объяснимым до последней единицы.
    isotopes: isotopes + DEMO_ISOTOPES,
    fields: Array.from({ length: fieldsAtLevel(DEMO_LEVEL) }, (_, i) => createField(i)),
    // Рейсы, уже сделанные колонией. Без этого числа генератор считает рейс
    // первым в жизни и выдает обучающий: укороченный таймер и форсированный
    // состав отсеков (ТЗ шаттла 2.1). Показ объявляет себя серединой сессии —
    // и обязан ею быть.
    shuttle_arrivals: DEMO_ARRIVALS,
  };
}

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
      stock: { ...store.getState().construction.stock, ...demoModules() },
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

  // Грядки засеваются последними: посев стоит кредитов, и списывать их надо с
  // остатка после зданий, а не до. Состояния ставятся напрямую, а не вызовом
  // `plant`: посев назначает время созревания от текущего момента, а показу
  // нужны разные стадии роста — то есть ровно то, чего действие сделать не
  // может. Цена при этом честно списывается по конфигу.
  const fields = store.getState().fields.map((f) => ({ ...f }));
  let credits = store.getState().credits;
  DEMO_FIELDS.forEach((seed, i) => {
    const field = fields[i];
    if (!field) return;
    field.good_id = seed.good;
    field.ends_at = now + seed.left_sec;
    field.state = seed.left_sec <= 0 ? 'READY' : 'GROWING';
    credits -= plantingCost(GOODS[seed.good].price, PLANT_COST_PRICE_SHARE, PLANT_COST_FLOOR);
  });
  store.setState({ fields, credits: Math.max(0, credits) });

  // Плашки, накопленные сборкой, стираются. Человек ничего не строил и никуда
  // не отправлял шаттл — это делал код показа, а плашка «Шаттл ушел на орбиту»
  // рассказывает человеку про его собственное действие. Три таких сообщения
  // закрывали половину первого экрана и врали о происходящем.
  store.setState({ toasts: [] });

  return store.getState().level === DEMO_LEVEL && totalQty(store.getState().warehouse) >= 0;
}
