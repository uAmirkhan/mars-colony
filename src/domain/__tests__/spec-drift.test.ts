/**
 * Страховка от разъезда имен между спецификацией и кодом.
 *
 * Тест читает конфиг-таблицу [[tz-production-mars]], вытаскивает имена параметров
 * и требует, чтобы каждое либо встречалось в коде, либо было явно записано в
 * DEFERRED как отложенное. Третьего состояния нет: параметр не может просто
 * потеряться молча.
 *
 * Зачем так строго. Именно этот класс ошибок в проекте повторялся шесть раз, и
 * последний случай — пропущенный `HARVEST_QTY` — держался сутки и успел испортить
 * вывод балансного симулятора: сбор давал одну единицу вместо двух-четырех,
 * из-за чего темп прогрессии был занижен втрое, а вывод «каркас промахивается
 * по пятому уровню» оказался ложным.
 */

import { describe, expect, it } from 'vitest';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';

const SPEC = resolve(
  process.cwd(),
  '..',
  'wiki',
  'saas',
  'projects',
  'mars-colony',
  'tz-production-mars.md',
);
const SRC = resolve(process.cwd(), 'src');

/**
 * Параметры, сознательно не реализованные на текущий день разработки.
 * Список существует, чтобы отложенное было видно, а не тонуло в тишине.
 * Каждая строка — обещание, а не отговорка: при реализации строку удаляют.
 */
const DEFERRED: Record<string, string> = {
  // Стройка — день 5+, вместе с шаттлом и строй-модулями
  CONSTRUCTION_ACTIVE_LINES_BASE: 'стройка не реализована, день 5+',
  CONSTRUCTION_RECIPE: 'стройка не реализована, день 5+',
  CONSTRUCTION_SPEEDUP_RATE: 'стройка не реализована, день 5+',
  CONSTRUCTION_SPEEDUP_RATE_ISO_PER_MIN: 'стройка не реализована, день 5+',
  SPEEDUP_FLOOR_ISO_CONSTRUCTION: 'стройка не реализована, день 5+',

  // Обучение — отдельный проход
  FTUE_FIRST_HARVEST_TIME_SEC: 'FTUE не реализован, отдельный проход',

  // Пуши и алерты — вне веб-прототипа
  IDLE_READY_PUSH_DELAY_MIN: 'пушей в веб-срезе нет',
  QUEUE_STARVATION_ALERT_MIN: 'health-метрика, нужен сервер',

  // Ускорения за изотопы — день 5, вместе с рейсом шаттла
  SPEEDUP_HIDE_THRESHOLD_SEC: 'платные ускорения не реализованы, день 5',

  // Состояния стройки как строковые литералы появятся вместе со стройкой
  LOCKED: 'состояние стройки, день 5+',
  AVAILABLE: 'состояние стройки, день 5+',
  IN_PROGRESS: 'состояние стройки, день 5+',
  DONE: 'состояние стройки, день 5+',
};

/**
 * Токены, которые извлеклись как параметры, но параметрами не являются.
 * Держим списком, а не правкой регулярки: так видно, что решение осознанное.
 */
const NOT_PARAMETERS: Record<string, string> = {
  CROP: 'значение перечисления Good.kind; каркас раздел 9 требует нижний регистр',
  FACTORY: 'значение перечисления Good.kind; каркас раздел 9 требует нижний регистр',
};

function readAllCode(dir: string): string {
  let out = '';
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      out += readAllCode(full);
    } else if (/\.(ts|tsx)$/.test(entry) && !entry.endsWith('.test.ts')) {
      out += readFileSync(full, 'utf8');
    }
  }
  return out;
}

/** Имена параметров из спеки: UPPER_SNAKE в обратных кавычках, минимум 4 символа. */
function specParameterNames(spec: string): string[] {
  const found = spec.match(/`([A-Z][A-Z0-9_]{3,}(?:\[[a-z_]+\])?)`/g) ?? [];
  const names = found.map((m) => m.replace(/`/g, '').split('[')[0] ?? '').filter(Boolean);
  return [...new Set(names)].sort();
}

describe('Спецификация против кода: разъезд имен', () => {
  const spec = readFileSync(SPEC, 'utf8');
  const code = readAllCode(SRC);
  const names = specParameterNames(spec).filter((n) => !(n in NOT_PARAMETERS));

  /**
   * Проверка регистронезависима: конфиг-константы пишутся UPPER_SNAKE,
   * а коды отказа в рантайме — lower_snake. Это разные конвенции для разных
   * сущностей, и совпадение слов важнее совпадения регистра.
   */
  const codeHas = (name: string) =>
    new RegExp(`(?<![A-Za-z0-9_])${name}(?![A-Za-z0-9_])`, 'i').test(code);

  /**
   * Строгая проверка по регистру — только для отлова мусора в DEFERRED.
   * Регистронезависимая здесь дает ложные срабатывания: слово `available`
   * встречается в комментариях и совпадает с состоянием стройки `AVAILABLE`.
   */
  const codeHasExact = (name: string) =>
    new RegExp(`(?<![A-Za-z0-9_])${name}(?![A-Za-z0-9_])`).test(code);

  it('в спеке вообще нашлись параметры (тест не пустой)', () => {
    expect(names.length).toBeGreaterThan(20);
  });

  it.each(names)('%s есть в коде или явно отложен', (name) => {
    if (codeHas(name)) return;

    // Не найден — значит обязан быть в списке отложенных, с причиной.
    expect(
      DEFERRED[name],
      `Параметр ${name} есть в ТЗ, но не найден в коде и не записан в DEFERRED. ` +
        `Либо реализуй его, либо добавь в DEFERRED с причиной — молча терять параметры нельзя.`,
    ).toBeTruthy();
  });

  it('в DEFERRED нет мусора: каждая запись все еще отсутствует в коде', () => {
    // Обратная проверка: если параметр реализован, его надо убрать из списка,
    // иначе список превращается в свалку и перестает что-либо значить.
    const stale = Object.keys(DEFERRED).filter((name) => codeHasExact(name));
    expect(stale, `Эти параметры уже реализованы, убери их из DEFERRED: ${stale.join(', ')}`).toEqual(
      [],
    );
  });
});
