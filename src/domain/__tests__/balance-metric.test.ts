/**
 * Сторож метрики баланса: кредиты за час занятости узкого места.
 *
 * Существует потому, что цена товара в отрыве от времени не значит ничего, а
 * проверка «маржа за штуку» показывает переработку прибыльной там, где за час
 * она убыточна. Эта подмена однажды уже пропустила дефект комбинезона мимо
 * зелёного теста: цену подняли с 45 до 100, тест позеленел, шаг цепочки
 * остался убыточным по времени ([[tz-rebalans-ekonomiki]] раздел 7).
 *
 * Числа здесь записаны ТОЧНО, а не отношениями. Причина та же, по которой
 * появился `rushcost.test.ts`: отношения переживают смену лестницы, и ошибка
 * выживает вместе с ними.
 */

import { describe, expect, it } from 'vitest';
import {
  bestCropPerHour,
  creditsPerHourPot,
  creditsPerHourSlot,
  PROCESSING_ADVANTAGE,
  recipeInputCrops,
} from '../balance';
import { ALL_GOOD_IDS, GOODS } from '../config/goods';
import type { GoodId } from '../types';

/** Замер 11.09 на лестнице времён Khan'а от 08.09. Цены не менялись. */
const KULTURY: Record<string, number> = {
  soy: 264.0,
  potato: 84.0,
  mushrooms: 78.0,
  tomatoes: 30.0,
  algae: 28.0,
  cotton: 15.0,
  coffee_beans: 14.0,
};

const PERERABOTKA: Record<string, number> = {
  fabric: 140.0,
  protein_bar: 132.0,
  water: 100.8,
  methane: 100.0,
  jumpsuit: 96.0,
  mushroom_soup: 92.0,
  iron_ore: 90.0,
  water_ice: 84.0,
  coffee_ration: 75.0,
  regolith: 75.0,
  oxygen_tank: 72.0,
};

describe('Метрика: кредиты за час занятости узкого места', () => {
  it('каждая культура даёт записанный доход за час грядки', () => {
    for (const [id, expected] of Object.entries(KULTURY)) {
      expect(creditsPerHourPot(id as GoodId)).toBeCloseTo(expected, 1);
    }
  });

  it('каждый фабричный товар даёт записанный доход за час слота', () => {
    for (const [id, expected] of Object.entries(PERERABOTKA)) {
      expect(creditsPerHourSlot(id as GoodId)).toBeCloseTo(expected, 1);
    }
  });

  it('замер покрывает весь субстрат — новый товар не проскочит мимо', () => {
    const zamereno = new Set([...Object.keys(KULTURY), ...Object.keys(PERERABOTKA)]);
    for (const id of ALL_GOOD_IDS) {
      expect(zamereno.has(id)).toBe(true);
    }
  });
});

describe('Записанные факты баланса, которые ждут решения владельца', () => {
  it('лестница культур перевёрнута: самая быстрая приносит в 18.9 раза больше самой долгой', () => {
    // НЕ проверка требования, а запись факта — по образцу тестов достижимости
    // в симуляторе. Соя открыта на втором уровне и даёт 264 кредита в час
    // грядки, кофе открыт на двенадцатом и даёт 14. Выгодно сеять только сою,
    // прогрессия работает в минус.
    //
    // Причина: лестница времени роста (решение Khan'а 08.09) растянула времена
    // в 2-7.5 раза, а цены остались от прежних времён. Лечится пересчётом цен
    // по разделу 5.2 ТЗ ребаланса — решение владельца, потому что подъём цен
    // тянет за собой стоимость посева и бьёт по лёгкому профилю.
    const bystraya = creditsPerHourPot('soy');
    const dolgaya = creditsPerHourPot('coffee_beans');
    expect(bystraya / dolgaya).toBeCloseTo(18.9, 1);
  });

  it('И-Э1 не выполняется ни одним товаром, и это записано числом', () => {
    // И-Э1 требует: переработка не меньше 1.5 x лучшей культуры за час.
    // Лучшая культура сейчас соя, 264 -> порог 396. Лучшая переработка —
    // ткань, 140. Инвариант недостижим, пока соя стоит наверху лестницы.
    //
    // Выполнить его подъёмом цен переработки нельзя: это раздует верх ценовой
    // лестницы и цену докупки. Выполняется он опусканием сои, то есть тем же
    // пересчётом цен культур.
    const porog = bestCropPerHour() * PROCESSING_ADVANTAGE;
    expect(porog).toBeCloseTo(396.0, 1);
    const luchshaya_pererabotka = Math.max(
      ...ALL_GOOD_IDS.filter((id) => GOODS[id].kind === 'factory').map(creditsPerHourSlot),
    );
    expect(luchshaya_pererabotka).toBeCloseTo(140.0, 1);
    expect(luchshaya_pererabotka).toBeLessThan(porog);
  });

  it('хлопок и кофе судятся по цепочке, а не по продаже сырья (И-Э2)', () => {
    const vhody = recipeInputCrops();
    expect(vhody.has('cotton' as GoodId)).toBe(true);
    expect(vhody.has('coffee_beans' as GoodId)).toBe(true);
    // Оба внизу лестницы по сырью, и это нормально: их ценность в ткани и пайке.
    expect(creditsPerHourPot('cotton')).toBeLessThan(20);
    expect(creditsPerHourSlot('fabric')).toBeGreaterThan(100);
  });
});
