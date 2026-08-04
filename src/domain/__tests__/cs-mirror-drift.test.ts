/**
 * Сверка C#-зеркала (`mars-unity/Assets/Scripts/Domain`) с TypeScript-конфигом.
 *
 * Зачем машиной. Исполнитель Unity-трека сам предупредил: автоматической
 * сверки чисел с оригиналом нет, а на разъезде конфигов этот проект спотыкался
 * шесть раз. Сверка глазами не масштабируется: четырнадцать товаров по семь
 * полей — это девяносто восемь чисел, и разъедется одно.
 *
 * Тестового каркаса для C# в проекте нет (ни `.asmdef` с тестами, ни NUnit,
 * ни запускалки). Поэтому зеркало проверяется отсюда — чтением файла и
 * сравнением значений. Это не замена C#-тестам: логику `Warehouse.cs` и
 * `Production.cs` такой тест не трогает, он держит только числа.
 *
 * Красный тест здесь означает ровно одно: два трека считают разные игры.
 */

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import {
  CREDITS_START,
  FIELD_SLOTS_START,
  PLANT_COST_FLOOR,
  PLANT_COST_PRICE_SHARE,
  PRODUCTION_XP_K,
  plantingCost,
  WAREHOUSE_MAX_CAPACITY,
  WAREHOUSE_START_CAPACITY,
  WAREHOUSE_UPGRADE_STEP,
} from '../config/economy';
import { ALL_GOOD_IDS, FACTORY_OUTPUT_QTY, GOODS, HARVEST_QTY } from '../config/goods';
import type { GoodId } from '../types';

const HERE = dirname(fileURLToPath(import.meta.url));
const CS_ROOT = join(HERE, '../../../../mars-unity/Assets/Scripts/Domain');

const goods_cs = readFileSync(join(CS_ROOT, 'Config/Goods.cs'), 'utf8');
const economy_cs = readFileSync(join(CS_ROOT, 'Config/Economy.cs'), 'utf8');
const numeric_cs = readFileSync(join(CS_ROOT, 'Numeric.cs'), 'utf8');

/** `public const string ALGAE = "algae";` — идентификаторы товаров. */
const ID_OF: Record<string, string> = {};
for (const m of goods_cs.matchAll(/public const string (\w+) = "([^"]+)";/g)) {
  ID_OF[m[1]!] = m[2]!;
}
const resolveId = (token: string) => ID_OF[token] ?? token.replace(/^"|"$/g, '');

/** Разбор блоков `new Good { ... }` в поля. */
function parseGoods(): Record<string, Record<string, string>> {
  const out: Record<string, Record<string, string>> = {};
  for (const block of goods_cs.matchAll(/new Good\s*\{([\s\S]*?)\n\s*\}/g)) {
    const fields: Record<string, string> = {};
    for (const line of block[1]!.split('\n')) {
      const m = line.match(/^\s*(\w+)\s*=\s*(.+?),?\s*$/);
      if (m) fields[m[1]!] = m[2]!.trim().replace(/,$/, '');
    }
    const id = resolveId(fields.id ?? '?');
    out[id] = fields;
  }
  return out;
}

const cs_goods = parseGoods();

const cs_consts: Record<string, string> = {};
for (const src of [economy_cs, goods_cs]) {
  for (const m of src.matchAll(/public const \w+ (\w+) = ([^;]+);/g)) {
    cs_consts[m[1]!] = m[2]!.trim();
  }
}

describe('C#-зеркало: товарный субстрат', () => {
  it('набор товаров совпадает', () => {
    expect(Object.keys(cs_goods).sort()).toEqual([...ALL_GOOD_IDS].sort());
  });

  it.each(ALL_GOOD_IDS)('%s: все поля совпадают с TypeScript-оригиналом', (id) => {
    const ts = GOODS[id as GoodId];
    const cs = cs_goods[id];
    expect(cs, `товара ${id} нет в Goods.cs`).toBeDefined();
    if (!cs) return;

    expect(cs.name?.replace(/^"|"$/g, '')).toBe(ts.name);
    expect(cs.kind?.replace('GoodKind.', '')).toBe(ts.kind);
    expect(Number(cs.unlock_level)).toBe(ts.unlock_level);
    expect(Number(cs.price)).toBe(ts.price);
    expect(Number(cs.base_xp)).toBe(ts.base_xp);
    expect(Number(cs.prod_time_sec)).toBe(ts.prod_time_sec);
    expect((cs.required_building ?? 'null').replace('BuildingType.', '')).toBe(
      ts.required_building ?? 'null',
    );

    const raw_inputs = cs.inputs ?? 'NoInputs';
    const cs_inputs =
      raw_inputs === 'NoInputs'
        ? []
        : [...raw_inputs.matchAll(/new GoodInput\((\w+),\s*(\d+)\)/g)].map((m) => ({
            good_id: resolveId(m[1]!),
            qty: Number(m[2]),
          }));
    expect(cs_inputs).toEqual(ts.inputs.map((i) => ({ good_id: i.good_id, qty: i.qty })));
  });

  it('HARVEST_QTY совпадает', () => {
    const block = goods_cs.match(/HARVEST_QTY[\s\S]*?\{([\s\S]*?)\n\s*\};/)?.[1] ?? '';
    const cs_harvest: Record<string, number> = {};
    for (const m of block.matchAll(/\{\s*(\w+),\s*(\d+)\s*\}/g)) {
      cs_harvest[resolveId(m[1]!)] = Number(m[2]);
    }
    expect(cs_harvest).toEqual(HARVEST_QTY);
  });
});

describe('C#-зеркало: экономические константы', () => {
  const scalars: Array<[string, number]> = [
    ['PRODUCTION_XP_K', PRODUCTION_XP_K],
    ['FIELD_SLOTS_START', FIELD_SLOTS_START],
    ['WAREHOUSE_START_CAPACITY', WAREHOUSE_START_CAPACITY],
    ['WAREHOUSE_UPGRADE_STEP', WAREHOUSE_UPGRADE_STEP],
    ['WAREHOUSE_MAX_CAPACITY', WAREHOUSE_MAX_CAPACITY],
    ['PLANT_COST_PRICE_SHARE', PLANT_COST_PRICE_SHARE],
    ['PLANT_COST_FLOOR', PLANT_COST_FLOOR],
    ['CREDITS_START', CREDITS_START],
    ['FACTORY_OUTPUT_QTY', FACTORY_OUTPUT_QTY],
  ];

  it.each(scalars)('%s', (name, ts_value) => {
    expect(cs_consts[name], `${name} не найдена в C#-конфиге`).toBeDefined();
    expect(Number(cs_consts[name])).toBe(ts_value);
  });
});

describe('C#-зеркало: округление', () => {
  /**
   * `Math.Round` в C# по умолчанию округляет половину к четному, `Math.round`
   * в JavaScript — вверх. Формула, посчитанная мимо `Numeric.RoundHalfUp`,
   * разъедется с каркасом на единицу и ничего при этом не уронит.
   */
  it('RoundHalfUp реализован как floor(x + 0.5), а не через Math.Round', () => {
    expect(numeric_cs).toMatch(
      /RoundHalfUp\(double value\)\s*=>\s*Math\.Floor\(value \+ 0\.5\)/,
    );
    expect(numeric_cs).not.toMatch(/=>\s*Math\.Round\(/);
  });

  it('ни одна формула C#-домена не считает мимо Numeric', () => {
    const suspicious = [economy_cs, goods_cs].flatMap((src) =>
      src.split('\n').filter((line) => /Math\.Round|Mathf\.Round|RoundToInt/.test(line)),
    );
    expect(suspicious).toEqual([]);
  });

  it('PlantingCost совпадает с plantingCost на всем диапазоне цен субстрата', () => {
    const cs_share = Number(cs_consts.PLANT_COST_PRICE_SHARE);
    const cs_floor = Number(cs_consts.PLANT_COST_FLOOR);
    for (let price = 0; price <= 200; price++) {
      // Копия формулы Economy.cs: Max(floor, RoundHalfUpToInt(share * price)).
      const cs_cost = Math.max(cs_floor, Math.floor(cs_share * price + 0.5));
      expect(cs_cost, `цена ${price}`).toBe(plantingCost(price));
    }
  });
});
