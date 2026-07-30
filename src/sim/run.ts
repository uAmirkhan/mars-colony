/** Запуск симулятора: npm run sim */

import { DEFAULT_SIM, type SimConfig, simulate } from './simulate';

interface Profile {
  name: string;
  config: SimConfig;
}

const PROFILES: Profile[] = [
  {
    name: 'Легкий (3 захода по 12 мин)',
    config: {
      ...DEFAULT_SIM,
      session_starts_min: [8 * 60, 13 * 60, 20 * 60],
      session_length_min: 12,
    },
  },
  {
    name: 'Целевой каркаса (4 захода по 20 мин)',
    config: {
      ...DEFAULT_SIM,
      session_starts_min: [8 * 60, 12 * 60, 17 * 60, 21 * 60],
      session_length_min: 20,
    },
  },
  {
    name: 'Плотный (6 заходов по 25 мин)',
    config: {
      ...DEFAULT_SIM,
      session_starts_min: [8 * 60, 11 * 60, 14 * 60, 17 * 60, 20 * 60, 22 * 60],
      session_length_min: 25,
    },
  },
];

const hoursPerWeek = (c: SimConfig) =>
  (c.session_starts_min.length * c.session_length_min * 7) / 60;

console.log('\nБалансный прогон, %d дней, seed %d', DEFAULT_SIM.days, DEFAULT_SIM.seed);
console.log(
  'Обещание каркаса: ур.5 — первая-вторая сессия; ур.12 — 2-3 неделя при 5-10 ч/нед.\n',
);

const widths = [34, 10, 8, 8, 9, 10, 10];
const line = (cells: (string | number)[]) =>
  cells.map((c, i) => String(c).padEnd(widths[i] ?? 10)).join('');

console.log(line(['Профиль', 'ч/неделю', 'ур.5', 'ур.8', 'ур.12', 'ур. на 30', 'кредиты']));
console.log('-'.repeat(widths.reduce((a, b) => a + b, 0)));

for (const profile of PROFILES) {
  const result = simulate(profile.config);
  const last = result.rows.at(-1);
  if (!last) continue;
  const day = (level: number) =>
    result.milestones[level] === null ? '—' : `д.${result.milestones[level]}`;

  console.log(
    line([
      profile.name,
      hoursPerWeek(profile.config).toFixed(1),
      day(5),
      day(8),
      day(12),
      last.level,
      last.credits,
    ]),
  );

  if (result.softlock_rescues > 0) {
    console.log('   ВНИМАНИЕ: анти-софтлок И-15 сработал %d раз.', result.softlock_rescues);
  }
}

console.log('\nПоденная динамика целевого профиля:\n');
const target_profile = PROFILES[1];
if (!target_profile) throw new Error('нет целевого профиля');
const target = simulate(target_profile.config);
const dw = [6, 5, 9, 10, 9, 9];
const dline = (cells: (string | number)[]) =>
  cells.map((c, i) => String(c).padStart(dw[i] ?? 8)).join(' ');
console.log(dline(['День', 'Ур.', 'XP', 'Кредиты', 'Изотопы', 'Заказов']));
console.log(dw.map((w) => '-'.repeat(w)).join(' '));
for (const row of target.rows) {
  if (row.day % 3 !== 0 && row.day !== 1) continue;
  console.log(
    dline([row.day, row.level, row.xp_total, row.credits, row.isotopes, row.orders_done]),
  );
}

console.log('\nЧего в модели нет: шаттл и строй-модули, лайнер, помощь союзников,');
console.log('платные ускорения, генератор заказов с инвариантами И-8/И-10.');
console.log('Шаттл и лайнер платят XP с K=8 против K=2 у дрона, но открываются');
console.log('на ур.5 и ур.12 — на путь до пятого уровня они не влияют вообще.');
console.log('');
console.log('ВНИМАНИЕ, главный пробел: моделируется ОДИН кредитный сток из трех.');
console.log('Атмосферный (4000), Текстильный (5500) и расширения купола не');
console.log('покупаются никогда, поэтому колонка «кредиты» показывает мертвый');
console.log('запас, а не баланс. Вывод о достаточности кредитов из этого прогона');
console.log('делать нельзя — именно этот вопрос ТЗ и поручало симулятору.\n');
