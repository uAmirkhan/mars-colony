/**
 * И-4: цена докупки = rush-cost цепочки x наценка, округление к витринным числам.
 *
 * Источник истины — [[tz-common-systems-mars]] раздел 5, а НЕ каркас раздел 8.
 * Каркас дает бедную формулу («сумма минут звена x ставка»), ТЗ общих подсистем
 * добавляет два правила, меняющие цену: вычитание того, что уже лежит на складе,
 * и пол на каждое звено. Реализация следует ТЗ.
 *
 * Одна функция на все витрины докупки и ускорения — каркас, раздел 8, пункт 5.
 * Второй расчет цены докупки где-либо еще считается багом реализации.
 */

import { SPEEDUP_FLOOR_ISOTOPES, SPEEDUP_RATE_ISOTOPES_PER_MIN } from './config/economy';
import { GOODS } from './config/goods';
import type { GoodId } from './types';
import { availableOf, createWarehouse, type WarehouseState } from './warehouse';

/** И-4: наценка докупки в слот заказа. Имя из конфиг-таблицы ТЗ раздел 5.6. */
export const PURCHASE_MARGIN = 1.2;

/**
 * Потолок наценки любой витрины ускорения (ТЗ 5.5). Дороже — опция экономически
 * мертва, никто не покупает. Дешевле единицы — витрина каннибализирует обычные
 * кнопки ускорения производства.
 */
export const SPEEDUP_MARGIN_CEILING = 1.5;

/**
 * Витринное округление. Лестница задана ТЗ раздел 5.4 псевдокодом.
 *
 * Отступление от спеки ровно одно и осознанное: спека молчит про нулевой случай,
 * а `roundToStep(2, 5)` дает ноль, то есть бесплатную докупку. Ставим пол в один
 * шаг лестницы. Это не интерпретация лестницы, а закрытие дыры в ней.
 */
export function roundToShowcase(value: number): number {
  const step = showcaseStep(value);
  return Math.max(step, Math.round(value / step) * step);
}

/** Шаг лестницы для величины. Вынесен, чтобы округление вниз и вверх брало тот же. */
function showcaseStep(value: number): number {
  return value < 50 ? 5 : value < 200 ? 10 : value < 1000 ? 50 : 100;
}

/**
 * Ближайшая ступень лестницы НЕ ВЫШЕ значения.
 *
 * Нужна там, где витринная красота сталкивается с числовым инвариантом.
 * Округление к ближайшему выносит величину за границу: премия дрона зажата
 * каркасом в коридор, но рынок 34 при потолке 1.70 дает 57.8, лестница
 * округляет до 60, и фактическая премия становится 76.5% при потолке 70.
 *
 * При таком столкновении уступает витрина: коридор — инвариант каркаса,
 * лестница — оформление. Единственное исключение — пол в один шаг: ниже него
 * лестница не опускается по построению, и на очень малых суммах граница
 * коридора может оказаться ниже пола.
 */
export function showcaseFloor(value: number): number {
  const step = showcaseStep(value);
  return Math.max(step, Math.floor(value / step) * step);
}

/** Ближайшая ступень лестницы НЕ НИЖЕ значения. Зеркало `showcaseFloor`. */
export function showcaseCeil(value: number): number {
  const step = showcaseStep(value);
  return Math.max(step, Math.ceil(value / step) * step);
}

interface ChainLink {
  good_id: GoodId;
  qty_needed: number;
  kind: 'crop' | 'factory';
  minutes_per_unit: number;
}

/**
 * Рекурсивный обход графа рецептов (ТЗ 5.3). Комбинезон требует Ткань-синт x2,
 * которая требует Хлопок-синт x2 — три уровня, и считать надо все.
 *
 * `visited` защищает от циклов. По контенту их быть не должно, но защита
 * обязательна: цикл в рецептах уронил бы расчет в бесконечную рекурсию.
 */
function expandProductionChain(
  good_id: GoodId,
  qty: number,
  warehouse: WarehouseState,
  visited: Set<GoodId>,
): ChainLink[] {
  if (visited.has(good_id)) return [];
  visited.add(good_id);

  const good = GOODS[good_id];
  const owned = availableOf(warehouse, good_id);
  const needed = Math.max(0, qty - owned);

  const links: ChainLink[] = [
    {
      good_id,
      qty_needed: qty,
      kind: good.kind,
      minutes_per_unit: good.prod_time_sec / 60,
    },
  ];

  // Во входы спускаемся только за недостающим: то, что уже лежит на складе,
  // производить не нужно, и сырье под него — тоже.
  if (needed > 0) {
    for (const input of good.inputs) {
      links.push(
        ...expandProductionChain(input.good_id, needed * input.qty, warehouse, visited),
      );
    }
  }

  return links;
}

/** Сколько единиц звена реально придется произвести: склад уже лежит готовым. */
function missingOf(link: ChainLink, warehouse: WarehouseState): number {
  return Math.max(0, link.qty_needed - availableOf(warehouse, link.good_id));
}

/**
 * `productionTimeMinutes` канона ([[tz-common-systems-mars]] 1.4): сколько минут
 * займет получить `qty` единиц товара вместе со всей недостающей цепочкой
 * рецепта. Вход порога «легко произвести» (И-8) и проверки реализуемости (И-10).
 *
 * Цепочка обязательна: комбинезон проходит порог сам по себе (30 мин), но до
 * него нужны две ткани и четыре хлопка, и заказ, собранный по времени одного
 * звена, врет игроку в разы.
 *
 * Склад вычитается на каждом звене — то, что уже лежит, производить не нужно.
 *
 * **Отступление от канона, утверждено владельцем 2026-08-05.** Канон (1.4,
 * псевдокод `productionTimeMinutes`/`productionSlotsFor`) делит выпуск звена на
 * параллельные слоты игрока — грядки и слоты очереди фабрик. Здесь этого нет:
 * функция получает склад, а не `player`, и числа слотов не знает. Оценка
 * сверху (один слот на звено) — единственная безопасная: занизить время значит
 * снова назвать легкой позицию, которую игрок собирает час.
 *
 * Отступление перестало быть косметическим: после перевода ворот дефицита на
 * честный предикат от него зависит СОСТАВ заказов, а не только флаг. Замер:
 * при множителе брекета 2.0 почти любое количество выходит за порог, и заказы
 * схлопывались с 5.3 позиции до 2.9 — 536 коротких из 640. Вытянуто
 * даунгрейдом по канону 1.4 через `maxQtyWithin`.
 *
 * Решение владельца: оставить как есть до отправки ссылки. Приведение к букве
 * канона меняет баланс всех трех механик сразу и требует прогона симулятора с
 * перепроверкой инвариантов — это отдельный день, и в плане его нет.
 * Направление отступления безопасное: время завышается, заказы получаются
 * осторожнее, а не агрессивнее.
 */
export function productionTimeMinutes(
  good_id: GoodId,
  qty = 1,
  warehouse: WarehouseState = createWarehouse(),
): number {
  const chain = expandProductionChain(good_id, qty, warehouse, new Set());
  let minutes = 0;
  for (const link of chain) {
    minutes += missingOf(link, warehouse) * link.minutes_per_unit;
  }
  return minutes;
}

/**
 * Стоимость мгновенно получить `qty` единиц товара со всей недостающей цепочкой,
 * в изотопах, до наценки.
 *
 * Склад учитывается: игрок не платит за то, что у него уже есть. По умолчанию
 * склад пуст — это случай докупки товара, которого нет ни на одном уровне цепочки.
 */
export function rushCost(
  good_id: GoodId,
  qty = 1,
  warehouse: WarehouseState = createWarehouse(),
): number {
  const chain = expandProductionChain(good_id, qty, warehouse, new Set());

  let total = 0;
  for (const link of chain) {
    const missing = missingOf(link, warehouse);
    if (missing === 0) continue;

    const rate = SPEEDUP_RATE_ISOTOPES_PER_MIN[link.kind];
    const minutes = missing * link.minutes_per_unit;
    // Пол на каждое звено, а не на итог: короткий остаток по мелочи не продаем.
    total += Math.max(minutes * rate, SPEEDUP_FLOOR_ISOTOPES[link.kind]);
  }
  return total;
}

/**
 * И-4: цена докупки qty единиц товара в слот заказа.
 *
 * Склад передает только тот вызывающий, который этот склад и спишет. Докупка,
 * идущая мимо склада (И-12: товар зачисляется прямо в слот и не возвратим),
 * складского остатка не трогает — и скидки за него не получает, иначе тот же
 * арбитраж открывается со стороны цены: заплатил за недостачу, закрыл слот
 * целиком, остаток продал.
 */
export function buyoutPrice(good_id: GoodId, qty: number, warehouse?: WarehouseState): number {
  return roundToShowcase(rushCost(good_id, qty, warehouse) * PURCHASE_MARGIN);
}

/**
 * Прямое ускорение производства (ТЗ 5.6, дополнение к API-контракту): та же
 * функция без наценки. Ускоряется конкретная партия, а не путь до сырья,
 * поэтому цепочка не раскрывается — склад считается полным по входам.
 */
export function productionSpeedupPrice(good_id: GoodId, qty = 1): number {
  const good = GOODS[good_id];
  const minutes = qty * (good.prod_time_sec / 60);
  const rate = SPEEDUP_RATE_ISOTOPES_PER_MIN[good.kind];
  return roundToShowcase(Math.max(minutes * rate, SPEEDUP_FLOOR_ISOTOPES[good.kind]));
}
