/**
 * Перекладка нарезанных ассетов в проект Unity.
 *
 * Отдельный шаг, потому что ручное копирование и было местом, где ассет менял
 * имя. Здесь список берется из `sheets.json` — того же файла, по которому
 * резалка называет файлы, — и нет способа положить в `shuttle.png` что-то,
 * кроме объекта, опознанного как шаттл.
 *
 * Перед копированием прогоняются проверки. Красный ассет в проект не попадает:
 * выеденный купол один раз уже доехал до сборки и стоил прогона.
 *
 * .meta не трогаем: в них GUID и настройки импортера (пивот в основании).
 *
 * Запуск: node scripts/sync-sprites.mjs [--force]
 */

import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, readFileSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = join(HERE, '..');
const CUT = join(ROOT, 'design', 'cut');
const DEST = join(ROOT, '..', 'mars-unity', 'Assets', 'Sprites');

const sheets = JSON.parse(readFileSync(join(HERE, 'sheets.json'), 'utf8'));
const force = process.argv.includes('--force');

const plan = [];
for (const [sheet, rows] of Object.entries(sheets)) {
  if (sheet === '_') continue;
  for (const row of rows) {
    for (const o of row) plan.push({ name: o.name, from: join(CUT, sheet, `${o.name}.png`) });
  }
}

const missing = plan.filter((p) => !existsSync(p.from));
if (missing.length) {
  console.error(
    `нет нарезки для: ${missing.map((m) => m.name).join(', ')}\n` +
      'сначала: node scripts/cut-sheet.mjs design/Asset_forCheck_v2/<лист>.png design/cut',
  );
  process.exit(1);
}

// Проверки по каждой папке нарезки, а не по проекту: смысл в том, чтобы
// красное не доехало до Unity, а не в том, чтобы узнать об этом после.
const dirs = [...new Set(plan.map((p) => dirname(p.from)))];
let red = 0;
for (const d of dirs) {
  const r = spawnSync(process.execPath, [join(HERE, 'check-sprites.mjs'), d], { stdio: 'inherit' });
  if (r.status !== 0) red++;
}
if (red && !force) {
  console.error(`\nпроверки красные в ${red} папке(ах) — в Assets/Sprites не кладу. --force, если осознанно.`);
  process.exit(1);
}

if (!statSync(DEST).isDirectory()) {
  console.error(`нет папки ${DEST}`);
  process.exit(1);
}

for (const p of plan) {
  copyFileSync(p.from, join(DEST, `${p.name}.png`));
}
console.log(`\nперенесено ${plan.length} спрайтов -> ${DEST}`);
