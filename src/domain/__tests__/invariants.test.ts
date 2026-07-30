/**
 * Инварианты каркаса проверяются тестами, а не глазами.
 * Каждый тест ссылается на номер инварианта или раздел каркаса.
 * Красный тест здесь означает: код разошелся с каркасом, и виноват код.
 */

import { describe, expect, it } from 'vitest';
import {
  DRONE_REFRESH_FREE_SEC,
  domeExpansionCost,
  droneRefreshPrice,
  FACTORY_PRICES,
  FLOOR_GUARANTEE_ALLOWED_TIERS,
  fieldsAtLevel,
  LINER_XP_CAP_PER_CONTAINER,
  LINER_XP_CAP_PER_TRIP,
  MODULE_DROP_WEIGHTS,
  PRODUCTION_XP_K,
  plantingCost,
  shuttleSkipPrice,
  shuttleSlotExpectedValue,
  TRANSPORT_XP_K,
} from '../config/economy';
import {
  ALL_GOOD_IDS,
  GOOD_BASE_QTY,
  GOODS,
  goodsOfKind,
  mechanicMult,
  slotQuantity,
} from '../config/goods';
import {
  cumulativeXpToReach,
  EMPTY_LEVELS,
  freeIsotopeBudget,
  levelUpReward,
  MAX_LEVEL_MVP,
  xpToNext,
} from '../config/levels';
import { buyoutPrice, rushCost } from '../rushcost';

/**
 * Доступ к элементу массива с проверкой. Строгий режим требует явности,
 * и это правильно: молчаливый undefined в сравнении дает ложно зеленый тест.
 */
function at(arr: readonly number[], i: number): number {
  const v = arr[i];
  if (v === undefined) throw new Error(`нет элемента ${i} в массиве длины ${arr.length}`);
  return v;
}

describe('И-2: XP фабричных товаров пропорционален цене (~0.43)', () => {
  it.each(goodsOfKind('factory'))('$name держит пропорцию', (good) => {
    const ratio = good.base_xp / good.price;
    expect(ratio).toBeGreaterThan(0.38);
    expect(ratio).toBeLessThan(0.47);
  });
});

describe('И-3: XP транспорта', () => {
  it('коэффициенты механик зафиксированы', () => {
    expect(TRANSPORT_XP_K).toEqual({ drone: 2, shuttle: 8, liner: 8 });
  });

  it('производство не доминирует доставку', () => {
    for (const k of Object.values(TRANSPORT_XP_K)) {
      expect(k).toBeGreaterThan(PRODUCTION_XP_K);
    }
  });

  it('кэп контейнера строго меньше кэпа рейса', () => {
    expect(LINER_XP_CAP_PER_CONTAINER).toBeLessThan(LINER_XP_CAP_PER_TRIP);
  });

  it('полный рейс из 3 палуб не пробивает кэп рейса', () => {
    // 3 палубы x 3 контейнера x 5% = 45% > 40%, значит кэп рейса реально режет.
    // Тест фиксирует, что он не бутафорский: суммарный потолок контейнеров выше.
    const containers = 9;
    expect(containers * LINER_XP_CAP_PER_CONTAINER).toBeGreaterThan(LINER_XP_CAP_PER_TRIP);
  });
});

describe('И-6: цена скипа шаттла', () => {
  it('полный скип всегда дороже трети shadow-ценности груза', () => {
    const ev_per_slot = shuttleSlotExpectedValue();
    const third = ev_per_slot / 3;
    for (const slots of [3, 4, 5]) {
      const price = shuttleSkipPrice(60, 60, slots);
      expect(price).toBeGreaterThan(third * slots);
    }
  });

  it('EV отсека равен 179 изотопам (основание тарифа 70)', () => {
    expect(shuttleSlotExpectedValue()).toBeCloseTo(179, 0);
  });

  it('3 отсека при полном таймере стоят 210', () => {
    expect(shuttleSkipPrice(90, 90, 3)).toBe(210);
  });

  it('пол в 15 изотопов держит короткий остаток', () => {
    expect(shuttleSkipPrice(0.1, 90, 3)).toBe(15);
  });

  it('цена монотонно падает по мере рейса', () => {
    const prices = [90, 60, 30, 10, 1].map((r) => shuttleSkipPrice(r, 90, 4));
    for (let i = 1; i < prices.length; i++) {
      expect(at(prices, i)).toBeLessThanOrEqual(at(prices, i - 1));
    }
  });
});

describe('Дроп модулей', () => {
  it('веса тиров дают в сумме единицу', () => {
    const sum = Object.values(MODULE_DROP_WEIGHTS).reduce((a, b) => a + b, 0);
    expect(sum).toBeCloseTo(1, 5);
  });

  it('И-11: гарантия никогда не выдает гейтовый тир', () => {
    expect(FLOOR_GUARANTEE_ALLOWED_TIERS).not.toContain('gated');
  });
});

describe('И-4: докупка дороже честного производства', () => {
  it.each(ALL_GOOD_IDS)('%s: докупка выгоднее ускорить, чем купить', (id) => {
    expect(buyoutPrice(id, 1)).toBeGreaterThanOrEqual(rushCost(id));
  });

  it('фабричный товар дороже своего сырья', () => {
    expect(rushCost('fabric')).toBeGreaterThan(rushCost('cotton') * 2);
    expect(rushCost('jumpsuit')).toBeGreaterThan(rushCost('fabric') * 2);
  });

  it('цена растет с количеством', () => {
    expect(buyoutPrice('mushroom_soup', 3)).toBeGreaterThan(buyoutPrice('mushroom_soup', 1));
  });
});

describe('XP-кривая (каркас 6)', () => {
  it('монотонно растет', () => {
    for (let n = 1; n < MAX_LEVEL_MVP; n++) {
      expect(xpToNext(n + 1)).toBeGreaterThan(xpToNext(n));
    }
  });

  it('накопленный XP до 5 уровня около 1740', () => {
    expect(cumulativeXpToReach(5)).toBe(1740);
  });

  it('накопленный XP до 12 уровня в районе 15800', () => {
    const value = cumulativeXpToReach(12);
    expect(value).toBeGreaterThan(15000);
    expect(value).toBeLessThan(16500);
  });

  it('пятый уровень достижим в пределах первых двух сессий', () => {
    // Каркас обещает ур.5 за первую-вторую сессию при 700-1200 XP в час.
    const hours_pessimistic = cumulativeXpToReach(5) / 700;
    expect(hours_pessimistic).toBeLessThan(3);
  });
});

describe('Награды за левелап и бюджет изотопов', () => {
  it('бюджет неплатящего за 21 уровень равен обещанным 550', () => {
    expect(freeIsotopeBudget()).toBe(550);
  });

  it('пустые уровни дают надбавку и бесплатное расширение', () => {
    for (const level of EMPTY_LEVELS) {
      const reward = levelUpReward(level);
      expect(reward.isotopes).toBe(45);
      expect(reward.free_dome_expansion).toBe(true);
    }
  });

  it('бюджета хватает на три-четыре премиум-действия, но не на рутину', () => {
    const budget = freeIsotopeBudget();
    const field = 150;
    const queue_slot = 200;
    expect(budget).toBeGreaterThan(field + queue_slot); // попробовать можно
    expect(budget).toBeLessThan(field * 5); // закрывать рутину нельзя
  });

  it('кредиты за левелап растут медленнее стока расширений', () => {
    // Показатель притока 1.2 против показателя стока 1.5: разрыв обязан расширяться.
    const early = levelUpReward(3).credits / domeExpansionCost(3);
    const late = levelUpReward(20).credits / domeExpansionCost(20);
    expect(late).toBeLessThan(early);
  });
});

describe('Кредитные стоки', () => {
  it('посев по формуле совпадает с таблицей каркаса', () => {
    expect(plantingCost(GOODS.algae.price)).toBe(1);
    expect(plantingCost(GOODS.soy.price)).toBe(1);
    expect(plantingCost(GOODS.mushrooms.price)).toBe(2);
    expect(plantingCost(GOODS.tomatoes.price)).toBe(2);
    expect(plantingCost(GOODS.cotton.price)).toBe(2);
    expect(plantingCost(GOODS.coffee_beans.price)).toBe(4);
  });

  it('каждая культура дает положительную маржу', () => {
    // Первый урок экономики в FTUE не должен врать: продажа излишков обязана быть выгодной.
    for (const crop of goodsOfKind('crop')) {
      expect(crop.price).toBeGreaterThan(plantingCost(crop.price));
    }
  });

  it('переработка выгоднее продажи сырья', () => {
    for (const good of goodsOfKind('factory')) {
      const raw_value = good.inputs.reduce((sum, i) => sum + GOODS[i.good_id].price * i.qty, 0);
      expect(good.price).toBeGreaterThan(raw_value);
    }
  });

  it('расширение купола совпадает с опорными точками каркаса', () => {
    expect(domeExpansionCost(1)).toBe(200);
    expect(domeExpansionCost(5)).toBe(2200);
    expect(domeExpansionCost(10)).toBe(6300);
    expect(domeExpansionCost(15)).toBe(11600);
  });

  it('второй экземпляр фабрики стоит вдвое дороже первого', () => {
    for (const factory of Object.values(FACTORY_PRICES)) {
      expect(factory.second).toBe(factory.first * 2);
    }
  });
});

describe('Дрон: выброс и рефреш', () => {
  it('бесплатный рефреш равен 22 минутам', () => {
    expect(DRONE_REFRESH_FREE_SEC).toBe(1320);
  });

  it('цена падает по мере приближения к бесплатному', () => {
    // По одной точке на ступень: две точки внутри одной ступени дадут равные цены.
    const prices = [22, 12, 5, 1].map((m) => droneRefreshPrice(m * 60));
    for (let i = 1; i < prices.length; i++) {
      expect(at(prices, i)).toBeLessThan(at(prices, i - 1));
    }
  });

  it('верхняя ступень примерно равна половине награды за уровень', () => {
    // Рефреш должен быть решением, а не рефлексом: цена соотнесена с притоком изотопов.
    const top = droneRefreshPrice(DRONE_REFRESH_FREE_SEC);
    expect(top).toBe(10);
    expect(top / levelUpReward(5).isotopes).toBeCloseTo(0.5, 1);
  });

  it('рефреш не превращается в расходник: бюджета хватает менее чем на 60 штук', () => {
    expect(freeIsotopeBudget() / droneRefreshPrice(DRONE_REFRESH_FREE_SEC)).toBeLessThan(60);
  });
});

describe('Каркас 3.1: количества в слоте', () => {
  it('никогда не опускается ниже min', () => {
    for (const id of ALL_GOOD_IDS) {
      for (const level of [1, 5, 10, 15, 21]) {
        for (const roll of [0, 0.5, 1]) {
          expect(slotQuantity(id, 'drone', level, roll)).toBeGreaterThanOrEqual(
            GOOD_BASE_QTY[id].min,
          );
        }
      }
    }
  });

  it('растет с брекетом уровня, а не упирается в потолок', () => {
    const low = slotQuantity('algae', 'drone', 5, 1);
    const mid = slotQuantity('algae', 'drone', 12, 1);
    const high = slotQuantity('algae', 'drone', 21, 1);
    expect(mid).toBeGreaterThan(low);
    expect(high).toBeGreaterThan(mid);
  });

  it('лайнер делит кропы на быстрые и медленные', () => {
    expect(mechanicMult('liner', 'algae')).toBe(6);
    expect(mechanicMult('liner', 'tomatoes')).toBe(3);
    expect(mechanicMult('liner', 'fabric')).toBe(2.5);
    expect(mechanicMult('drone', 'algae')).toBe(1);
  });

  it('объем фабричного товара у лайнера остается реалистичным', () => {
    // Блокер приемки: 2 ткани x 6 x bracket 2.0 = 24 единицы = 9 часов очереди.
    const qty = slotQuantity('fabric', 'liner', 15, 1);
    const hours = (qty * GOODS.fabric.prod_time_sec) / 3600;
    expect(hours).toBeLessThan(4);
  });
});

describe('Мощности', () => {
  it('грядки растут по уровням каркаса до 9 к 17-му', () => {
    expect(fieldsAtLevel(1)).toBe(4);
    expect(fieldsAtLevel(3)).toBe(5);
    expect(fieldsAtLevel(17)).toBe(9);
  });
});
