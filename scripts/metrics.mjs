/**
 * Замер состояния проекта одной командой: npm run metrics
 *
 * Первая стадия круга качества. Смысл — снимать ТОЛЬКО машинные факты.
 * Ни одной оценки, ни одного «выглядит неплохо»: цикл улучшения, заземленный
 * на самооценку агента, уходит в галлюцинации на втором проходе.
 */

import { execSync } from 'node:child_process';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

const run = (cmd) => {
  try {
    return { ok: true, out: execSync(cmd, { encoding: 'utf8', stdio: 'pipe' }) };
  } catch (e) {
    return { ok: false, out: `${e.stdout ?? ''}${e.stderr ?? ''}` };
  }
};

const num = (text, re, fallback = null) => {
  const m = text.match(re);
  return m?.[1] ? Number(m[1]) : fallback;
};

function countLines(dir, filter) {
  let total = 0;
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) total += countLines(full, filter);
    else if (filter(entry)) total += readFileSync(full, 'utf8').split('\n').length;
  }
  return total;
}

const isSource = (f) => /\.(ts|tsx)$/.test(f) && !f.endsWith('.test.ts');
const isTest = (f) => f.endsWith('.test.ts');

console.log('\n=== ЗАМЕР ПРОЕКТА ===\n');

// --- Типы ---
const types = run('npx tsc -b');
console.log('Типы:', types.ok ? 'чисто' : `ОШИБКИ (${(types.out.match(/error TS/g) ?? []).length})`);

// --- Линтер ---
const lint = run('npx biome check src --max-diagnostics=200');
const lint_errors = num(lint.out, /Found (\d+) errors?/, 0);
const lint_warnings = num(lint.out, /Found (\d+) warnings?/, 0);
console.log(`Линтер: ошибок ${lint_errors ?? 0}, предупреждений ${lint_warnings ?? 0}`);

// --- Тесты и покрытие ---
const tests = run('npx vitest run --coverage');
const passed = num(tests.out, /Tests\s+(\d+) passed/);
const failed = num(tests.out, /(\d+) failed/, 0);
console.log(`Тесты: ${passed ?? '?'} зеленых, ${failed ?? 0} красных`);
console.log(
  `Покрытие домена: строки ${num(tests.out, /Statements\s+:\s+([\d.]+)%/) ?? '?'}%, ` +
    `ветви ${num(tests.out, /Branches\s+:\s+([\d.]+)%/) ?? '?'}%`,
);

// --- Отложенное в спеке ---
try {
  const drift = readFileSync('src/domain/__tests__/spec-drift.test.ts', 'utf8');
  const deferred = (drift.match(/^\s{2}[A-Z][A-Z0-9_]+:/gm) ?? []).length;
  console.log(`Отложено параметров спеки: ${deferred}`);
} catch {
  console.log('Отложено параметров спеки: файл не найден');
}

// --- Объем кода ---
const src_lines = countLines('src', isSource);
const test_lines = countLines('src', isTest);
console.log(
  `Строк: код ${src_lines}, тесты ${test_lines} (${((test_lines / src_lines) * 100).toFixed(0)}% от кода)`,
);

// --- Сборка ---
const build = run('npm run build');
const gzip = build.out.match(/index-[\w-]+\.js\s+[\d.]+ kB\s+│ gzip:\s+([\d.]+) kB/);
console.log('Сборка:', build.ok ? `ок, ${gzip?.[1] ?? '?'} КБ в gzip` : 'СЛОМАНА');

// --- Инварианты симулятора на умолчаниях ---
const sim = run('npx tsx src/sim/run.ts');
const target = sim.out.split('\n').find((l) => l.includes('Целевой'));
if (target) console.log('Симулятор, целевой профиль:', target.trim().replace(/\s+/g, ' '));

console.log('\nЗамер снят. Оценок здесь нет и не должно быть — только числа.\n');
