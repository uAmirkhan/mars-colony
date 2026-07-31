/**
 * Страховка от разъезда имен между спецификацией и кодом.
 *
 * Тест читает конфиг-таблицы ВСЕХ пяти ТЗ, вытаскивает имена параметров
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

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const WIKI = resolve(process.cwd(), '..', 'wiki', 'saas', 'projects', 'mars-colony');

/**
 * Читаем ВСЕ технические задания, а не одно.
 *
 * Первая версия теста смотрела только в ТЗ производства — и пропустила
 * двенадцать констант, разъехавшихся с каноном имен в ТЗ общих подсистем.
 * Страховка с областью действия уже проверяемого файла бесполезна ровно там,
 * где нужна: на границе между документами.
 */
const SPECS = [
  'tz-production-mars.md',
  'tz-common-systems-mars.md',
  'tz-drone-mars.md',
  'tz-shuttle-mars.md',
  'tz-liner-mars.md',
];

/**
 * Механики, чьи ТЗ прочитаны, но не реализованы. Параметр, который встречается
 * ТОЛЬКО в таком документе, откладывается вместе со всей механикой — одной
 * строкой вместо семидесяти.
 *
 * Смысл именно в слове «только»: как только параметр появляется еще и в активном
 * ТЗ, поблажка перестает действовать и он требует персонального объяснения.
 * Так группировка экономит список, но не создает дыру.
 */
const DEFERRED_SPECS: Record<string, string> = {
  'tz-drone-mars.md': 'дрон — этап 2 по [[spec-prototype-build]]',
  'tz-shuttle-mars.md': 'шаттл — этап 3',
  'tz-liner-mars.md': 'лайнер вырезан из среза решением приемки',
};

/**
 * Подсистемы внутри активных ТЗ, до которых очередь не дошла. Здесь нужен
 * префикс, а не имя файла: ТЗ общих подсистем реализовано частично — расчет
 * докупки живет в коде, а генератор заказов и соц-граф еще нет.
 */
const DEFERRED_GROUPS: Array<{ match: RegExp; why: string }> = [
  {
    match: /HELP|^(ALLY|FRIEND|ROOM|DECLINE|MAX_FRIENDS)/,
    why: 'соц-граф и помощь союзников вне среза',
  },
  { match: /^IDEMPOTENCY_/, why: 'идемпотентность требует сервера, границы среза' },
  { match: /^(DECK|LINER)_/, why: 'лайнер вырезан из среза решением приемки' },
  {
    match: /^(GEN|POOL|POSITIONS|CATEGORY|ANOMALY|PAIR|LEVEL_QTY|PINCH_MODE|REPEAT_SCOPE)/,
    why: 'генератор заказов — этап 2',
  },
  {
    match:
      /^(CLIENT|UPDATE|POST|WHERE|PAYMENT|PRICE_DISPLAY|QTY_DISPLAY|DEADLINE_SWEEP|ALLOWED_DELTA|CURRENCY_WHITELIST)/,
    why: 'серверный контракт и кошелек вне среза',
  },
  {
    match: /^(SLOT|DEFICIT|SKIP|TIMER|FLIGHT|ROLL)_/,
    why: 'слоты заказов и рейс — этапы 2 и 3',
  },
  {
    match: /^ACHIEVABILITY_/,
    why: 'проверка достижимости заказа — вместе с генератором, этап 2',
  },
  {
    match: /^(PITY|FLOOR_GUARANTEE)_ENABLED$/,
    why: 'фича-флаги дроп-роллера: сами правила реализованы, переключателей нет — этап 3',
  },
  {
    match: /^(INSUFFICIENT_STOCK|ORDER_EXPIRED)$/,
    why: 'коды отказа механик доставки — этап 2',
  },
];
const SRC = resolve(process.cwd(), 'src');

/**
 * Параметры, сознательно не реализованные на текущий день разработки.
 * Список существует, чтобы отложенное было видно, а не тонуло в тишине.
 * Каждая строка — обещание, а не отговорка: при реализации строку удаляют.
 */
const DEFERRED: Record<string, string> = {
  // Ставка ускорения стройки: канон дает два имени одному числу, реализовано
  // длинное (`..._ISO_PER_MIN`), короткое остается синонимом из текста ТЗ.
  CONSTRUCTION_SPEEDUP_RATE: 'синоним CONSTRUCTION_SPEEDUP_RATE_ISO_PER_MIN',

  // Обучение производства — отдельный проход (FTUE шаттла уже реализован)
  FTUE_FIRST_HARVEST_TIME_SEC: 'FTUE производства не реализован, отдельный проход',

  // Пуши и алерты — вне веб-прототипа
  IDLE_READY_PUSH_DELAY_MIN: 'пушей в веб-срезе нет',
  QUEUE_STARVATION_ALERT_MIN: 'health-метрика, нужен сервер',
};

/**
 * Токены, которые извлеклись как параметры, но параметрами не являются.
 * Держим списком, а не правкой регулярки: так видно, что решение осознанное.
 */
const NOT_PARAMETERS: Record<string, string> = {
  POST: 'HTTP-метод в описании контракта, а не параметр конфига',
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
  // Карта «имя параметра → в каких документах встречается». Нужна, чтобы отличить
  // параметр нереализованной механики от параметра активного ТЗ.
  const sources = new Map<string, Set<string>>();
  for (const file of SPECS) {
    for (const name of specParameterNames(readFileSync(join(WIKI, file), 'utf8'))) {
      const set = sources.get(name) ?? new Set<string>();
      set.add(file);
      sources.set(name, set);
    }
  }

  const code = readAllCode(SRC);
  const names = [...sources.keys()].filter((n) => !(n in NOT_PARAMETERS)).sort();

  /** Параметр принадлежит только отложенным механикам — персональной строки не требует. */
  const onlyInDeferredSpecs = (name: string) =>
    [...(sources.get(name) ?? [])].every((f) => f in DEFERRED_SPECS);

  const groupOf = (name: string) => DEFERRED_GROUPS.find((g) => g.match.test(name));

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

  it('все пять ТЗ прочитаны и параметры нашлись', () => {
    expect(SPECS.length).toBe(5);
    expect(names.length).toBeGreaterThan(40);
  });

  it.each(names)('%s есть в коде или явно отложен', (name) => {
    if (codeHas(name)) return;
    if (onlyInDeferredSpecs(name)) return; // механика целиком не реализована
    if (groupOf(name)) return; // подсистема отложена группой

    expect(
      DEFERRED[name],
      `Параметр ${name} есть в активном ТЗ, не найден в коде и не покрыт ни одной ` +
        `причиной отсрочки. Либо реализуй его, либо добавь в DEFERRED с объяснением — ` +
        `молча терять параметры нельзя.`,
    ).toBeTruthy();
  });

  it('в DEFERRED нет мусора: каждая запись все еще отсутствует в коде', () => {
    // Обратная проверка: если параметр реализован, его надо убрать из списка,
    // иначе список превращается в свалку и перестает что-либо значить.
    const stale = Object.keys(DEFERRED).filter((name) => codeHasExact(name));
    expect(
      stale,
      `Эти параметры уже реализованы, убери их из DEFERRED: ${stale.join(', ')}`,
    ).toEqual([]);
  });
});
