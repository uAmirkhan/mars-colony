/**
 * Проверки нарезанных спрайтов. Красное = ассет нельзя класть в сцену.
 *
 * Зачем отдельно от `cut-sheet.mjs`: резалка не видит, что получилось. Она
 * одинаково бодро рапортует «12 объектов вырезано» и на выеденном изнутри
 * куполе, и на шаттле с чужой подписью поперек носа. Здесь три теста, каждый —
 * численная формулировка того, что арт-инспектор поймал глазами на кадре
 * сборки (`loop/run-1/art-inspector.md`).
 *
 * Запуск: node scripts/check-sprites.mjs [папка|файл]
 * По умолчанию — ../mars-unity/Assets/Sprites. Код выхода 1, если есть красное.
 */

import { readFileSync, readdirSync, statSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';

/** Порог альфы, выше которого пиксель считается своим. */
const A = 128;

/**
 * ТЕСТ 1. Вырез не должен терять цвет внутри объекта.
 *
 * Меряется крупнейшей дырой — областью прозрачности, запертой внутри силуэта и
 * недостижимой от рамки кадра. Именно так выглядит заливка, прошедшая сквозь
 * стекло: снаружи форма цела, внутри пусто.
 *
 * Почему крупнейшая, а не сумма: у разрезного макета стройплощадки просветы
 * между балками — законный фон, их там 1946 px суммой, но по отдельности
 * не больше 531 px (2.2% тела). У выеденного купола одна дыра на 21922 px —
 * 141% собственного тела объекта. Между 2.2% и 141% порог можно ставить где
 * угодно; ставлю 6%, вшестеро выше худшего законного случая.
 *
 * Доля непрозрачных пикселей (инспектор: 0.64-0.71 у здоровых, 0.30 у купола)
 * оставлена вторым, грубым порогом. Первым ее делать нельзя: у дрона законные
 * 0.43, у пищевого завода 0.49 — оба тонкие силуэты в широком габарите, и
 * порог 0.55 назвал бы их браком. Она ловит только катастрофу.
 */
const HOLE_MAX_SHARE = 0.06;
const FILL_MIN = 0.4;

/**
 * ТЕСТ 2. На ассете не должно быть остатков подписи.
 *
 * Подпись — это отдельные от объекта темные обломки размером с букву. Все три
 * признака нужны разом: струя пара над трубой пищевого завода тоже отдельный
 * мелкий обломок (219 px), но светлая — средняя светлота 198 против 37-79 у
 * глифов. Замер по старым спрайтам: `drone_pad.png` 14 таких обломков,
 * `construction.png` 1, `dome.png` 1.
 */
const GLYPH_MAX_H = 34;
const GLYPH_MAX_W = 60;
const GLYPH_MAX_AREA = 900;
const GLYPH_MAX_LUM = 110;
/** Ниже этого — крапина антиалиаса, а не буква. Замер: у обломков букв 15-282 px. */
const GLYPH_MIN_AREA = 10;

/**
 * ТЕСТ 3. Содержимое файла должно соответствовать его имени.
 *
 * Файлы `shuttle.png` и `drone_pad.png` приехали в сцену перепутанными, и
 * поймать это можно было только глазами: оба валидные PNG, оба без дыр и
 * подписей. Здесь имя сверяется с признаками содержимого из `sheets.json` —
 * пропорция габарита и средний цвет по пикселям объекта.
 *
 * Мало сказать «похоже на себя»: тумба дрона и дрон похожи друг на друга.
 * Поэтому проверяется еще и то, что никакое ЧУЖОЕ имя не подходит лучше
 * собственного. Замер: у правильно названного файла своя цена 0.02-0.15,
 * у перепутанной пары своя цена уходит за 0.7.
 */
const NAME_MAX_COST = 0.35;

async function readRGBA(file) {
  const { data, info } = await sharp(file).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  return { data, w: info.width, h: info.height, c: info.channels };
}

/** Связные компоненты по альфе, от крупной к мелкой. */
function blobs(data, w, h, c) {
  const seen = new Uint8Array(w * h);
  const out = [];
  for (let start = 0; start < w * h; start++) {
    if (seen[start] || data[start * c + 3] < A) continue;
    const stack = [start];
    seen[start] = 1;
    let area = 0;
    let minx = w;
    let miny = h;
    let maxx = 0;
    let maxy = 0;
    let lum = 0;
    while (stack.length) {
      const i = stack.pop();
      area++;
      const x = i % w;
      const y = (i / w) | 0;
      const o = i * c;
      lum += 0.299 * data[o] + 0.587 * data[o + 1] + 0.114 * data[o + 2];
      if (x < minx) minx = x;
      if (x > maxx) maxx = x;
      if (y < miny) miny = y;
      if (y > maxy) maxy = y;
      const step = (q) => {
        if (seen[q] || data[q * c + 3] < A) return;
        seen[q] = 1;
        stack.push(q);
      };
      if (x > 0) step(i - 1);
      if (x + 1 < w) step(i + 1);
      if (y > 0) step(i - w);
      if (y + 1 < h) step(i + w);
    }
    out.push({ area, minx, miny, maxx, maxy, lum: lum / area });
  }
  return out.sort((p, q) => q.area - p.area);
}

/** Крупнейшая дыра: прозрачность, запертая внутри силуэта. */
function largestHole(data, w, h, c) {
  const outside = new Uint8Array(w * h);
  const stack = [];
  const push = (x, y) => {
    if (x < 0 || y < 0 || x >= w || y >= h) return;
    const i = y * w + x;
    if (outside[i] || data[i * c + 3] >= A) return;
    outside[i] = 1;
    stack.push(x, y);
  };
  for (let x = 0; x < w; x++) {
    push(x, 0);
    push(x, h - 1);
  }
  for (let y = 0; y < h; y++) {
    push(0, y);
    push(w - 1, y);
  }
  while (stack.length) {
    const y = stack.pop();
    const x = stack.pop();
    push(x + 1, y);
    push(x - 1, y);
    push(x, y + 1);
    push(x, y - 1);
  }

  const seen = new Uint8Array(w * h);
  let big = 0;
  let box = null;
  let total = 0;
  const free = (i) => !outside[i] && data[i * c + 3] < A;
  for (let start = 0; start < w * h; start++) {
    if (seen[start] || !free(start)) continue;
    const st = [start];
    seen[start] = 1;
    let area = 0;
    let minx = w;
    let miny = h;
    let maxx = 0;
    let maxy = 0;
    while (st.length) {
      const i = st.pop();
      area++;
      const x = i % w;
      const y = (i / w) | 0;
      if (x < minx) minx = x;
      if (x > maxx) maxx = x;
      if (y < miny) miny = y;
      if (y > maxy) maxy = y;
      const step = (q) => {
        if (seen[q] || !free(q)) return;
        seen[q] = 1;
        st.push(q);
      };
      if (x > 0) step(i - 1);
      if (x + 1 < w) step(i + 1);
      if (y > 0) step(i - w);
      if (y + 1 < h) step(i + w);
    }
    total += area;
    if (area > big) {
      big = area;
      box = `x${minx}-${maxx} y${miny}-${maxy}`;
    }
  }
  return { big, box, total };
}

/** Эталонные признаки из плана листов: имя -> пропорция и средний цвет. */
const HERE = dirname(fileURLToPath(import.meta.url));
const WANT = new Map();
try {
  const sheets = JSON.parse(readFileSync(join(HERE, 'sheets.json'), 'utf8'));
  for (const [key, rows] of Object.entries(sheets)) {
    if (key === '_') continue;
    for (const row of rows) for (const o of row) WANT.set(o.name, o);
  }
} catch {
  /* без плана тест имени просто не запускается */
}

function nameCost(found, want) {
  return (
    Math.abs(Math.log(found.aspect / want.aspect)) +
    Math.hypot(
      found.rgb[0] - want.rgb[0],
      found.rgb[1] - want.rgb[1],
      found.rgb[2] - want.rgb[2],
    ) /
      160
  );
}

async function checkOne(file) {
  const name = basename(file, '.png');
  const { data, w, h, c } = await readRGBA(file);

  const parts = blobs(data, w, h, c);
  const main = parts[0] ?? { area: 1 };
  let body = 0;
  let sr = 0;
  let sg = 0;
  let sb = 0;
  for (let i = 0; i < w * h; i++) {
    if (data[i * c + 3] < A) continue;
    body++;
    sr += data[i * c];
    sg += data[i * c + 1];
    sb += data[i * c + 2];
  }
  const fill = body / (w * h);
  const mine = {
    aspect: w / h,
    rgb: [Math.round(sr / body), Math.round(sg / body), Math.round(sb / body)],
  };
  const hole = largestHole(data, w, h, c);

  const glyphs = parts.filter((p, i) => {
    if (i === 0) return false;
    const pw = p.maxx - p.minx + 1;
    const ph = p.maxy - p.miny + 1;
    return (
      ph <= GLYPH_MAX_H &&
      pw <= GLYPH_MAX_W &&
      p.area >= GLYPH_MIN_AREA &&
      p.area <= GLYPH_MAX_AREA &&
      p.lum <= GLYPH_MAX_LUM
    );
  });

  const fails = [];
  const hole_share = hole.big / Math.max(1, main.area);
  if (hole_share > HOLE_MAX_SHARE) {
    fails.push(
      `дыра внутри силуэта ${hole.big} px = ${(hole_share * 100).toFixed(1)}% тела` +
        ` (${hole.box}), потолок ${(HOLE_MAX_SHARE * 100).toFixed(0)}% — вырез съел цвет внутри объекта`,
    );
  }
  if (fill < FILL_MIN) {
    fails.push(`доля непрозрачных ${fill.toFixed(2)} < ${FILL_MIN} — от объекта осталась оболочка`);
  }
  if (glyphs.length) {
    const worst = glyphs[0];
    fails.push(
      `обломков подписи ${glyphs.length}, крупнейший ${worst.area} px` +
        ` (x${worst.minx}-${worst.maxx}, y${worst.miny}-${worst.maxy}, светлота ${worst.lum.toFixed(0)})`,
    );
  }

  let cost = null;
  if (WANT.has(name)) {
    cost = nameCost(mine, WANT.get(name));
    let best = name;
    let best_cost = cost;
    for (const [other, sig] of WANT) {
      const cc = nameCost(mine, sig);
      if (cc < best_cost) {
        best_cost = cc;
        best = other;
      }
    }
    if (best !== name) {
      fails.push(
        `содержимое не соответствует имени: похоже на ${best} (цена ${best_cost.toFixed(2)})` +
          `, своя цена ${cost.toFixed(2)}`,
      );
    } else if (cost > NAME_MAX_COST) {
      fails.push(
        `содержимое отошло от эталона ${name}: цена ${cost.toFixed(2)} > ${NAME_MAX_COST}` +
          ` (габарит ${w}x${h}, средний цвет ${mine.rgb})`,
      );
    }
  }

  return {
    name,
    size: `${w}x${h}`,
    fill,
    hole: hole.big,
    hole_share,
    glyphs: glyphs.length,
    cost,
    blobs: parts.length,
    fails,
  };
}

const dir = process.argv[2] ?? join(process.cwd(), '..', 'mars-unity', 'Assets', 'Sprites');
const files = statSync(dir).isDirectory()
  ? readdirSync(dir)
      .filter((f) => f.toLowerCase().endsWith('.png'))
      .map((f) => join(dir, f))
  : [dir];

const pad = (s, n) => String(s).padEnd(n);
console.log(
  pad('спрайт', 15) +
    pad('размер', 10) +
    pad('заполн', 8) +
    pad('дыра', 14) +
    pad('глифы', 7) +
    pad('имя', 7) +
    'вердикт',
);

let red = 0;
for (const f of files) {
  const r = await checkOne(f);
  if (r.fails.length) red++;
  console.log(
    pad(r.name, 15) +
      pad(r.size, 10) +
      pad(r.fill.toFixed(2), 8) +
      pad(`${r.hole} (${(r.hole_share * 100).toFixed(1)}%)`, 14) +
      pad(r.glyphs, 7) +
      pad(r.cost === null ? '—' : r.cost.toFixed(2), 7) +
      (r.fails.length ? 'КРАСНОЕ' : 'ок'),
  );
  for (const m of r.fails) console.log(`${' '.repeat(15)}  ! ${m}`);
}

console.log(`\nвсего ${files.length}, красных ${red}`);
process.exit(red ? 1 : 0);
