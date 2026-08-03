/**
 * Разрезалка листа ассетов: один PNG с несколькими объектами -> отдельные PNG.
 *
 * Зачем не хромакей из `prep-assets.mjs`: тот снимает фон у ОДНОГО объекта и
 * обрезает кадр по габариту. Здесь на листе восемь-восемнадцать объектов, и
 * задача другая — найти их границы и вырезать каждый отдельно.
 *
 * Два решения, без которых скрипт режет неправильно.
 *
 * 1. **Фон ищется заливкой от края кадра, а не по цвету.** Наивная проверка
 *    «зеленый — значит фон» съедала бы зелень внутри объектов: растения в
 *    грядках, посевы под куполом, зеленые ящики. Заливка от границы до них не
 *    дотягивается — они заперты внутри непрозрачных пикселей.
 *
 * 2. **Перед разметкой маска расширяется.** Иначе антенна, отделенная от
 *    корпуса парой прозрачных пикселей, становится отдельным «объектом», и
 *    здание приезжает без верхушки. Расширение только для поиска связности,
 *    вырезается по исходной альфе.
 *
 * Запуск: node scripts/cut-sheet.mjs <лист.png> [папка_выхода]
 */

import { mkdirSync, readdirSync, statSync } from 'node:fs';
import { basename, extname, join } from 'node:path';
import sharp from 'sharp';

/** Минимальная доля площади кадра, ниже которой находка считается мусором. */
const MIN_AREA_SHARE = 0.0012;
/** Минимальный габарит объекта в пикселях. Отсекает буквы подписей. */
const MIN_SIDE = 46;
/**
 * Радиус расширения маски при поиске связности.
 *
 * Компромисс: большой радиус склеивает антенну с корпусом, но и соседние
 * грядки в один ком. Плотные листы режем с меньшим радиусом, передавая его
 * третьим аргументом.
 */
const DILATE_RADIUS = { value: 7 };

/**
 * Отличает фоновый зеленый от бирюзы корпусов и стекол.
 *
 * Прежнее правило «зеленый больше красного и больше синего» казалось
 * очевидным и было катастрофой: бирюзовый корпус под него подходит — у
 * бирюзы зеленый канал тоже выше красного и на несколько единиц выше синего.
 * Заливка входила в здание и выедала его изнутри. Купола, стекла и пар
 * исчезали кусками, при этом скрипт рапортовал об успехе, а форма объекта
 * в целом сохранялась — поймать это можно было только глазами на контрастной
 * подложке.
 *
 * Разделяет их отношение зеленого к синему. У фона листа синий заметно ниже
 * зеленого (195 против 133), у бирюзы они почти равны (190 против 185).
 * Плюс расстояние до опорного цвета фона, снятого с рамки кадра, — на листах
 * есть градиент, и одной константой его не покрыть.
 */
function makeBackgroundTest(ref) {
  return function isBackgroundish(r, g, b) {
    if (g <= 60) return false;
    if (g < r * 1.12) return false;
    // Ключевая проверка: зеленый обязан заметно превосходить синий.
    if (g < b * 1.2) return false;
    const dist = Math.hypot(r - ref.r, g - ref.g, b - ref.b);
    return dist < 95;
  };
}

/** Опорный цвет фона — медиана по рамке кадра. Устойчива к объектам у края. */
function sampleReference(data, width, height, channels) {
  const rs = [];
  const gs = [];
  const bs = [];
  const take = (x, y) => {
    const o = (y * width + x) * channels;
    const r = data[o];
    const g = data[o + 1];
    const b = data[o + 2];
    if (g > 60 && g > r && g > b) {
      rs.push(r);
      gs.push(g);
      bs.push(b);
    }
  };
  for (let x = 0; x < width; x += 3) {
    take(x, 1);
    take(x, height - 2);
  }
  for (let y = 0; y < height; y += 3) {
    take(1, y);
    take(width - 2, y);
  }
  const mid = (a) => (a.length ? a.sort((p, q) => p - q)[a.length >> 1] : 0);
  return { r: mid(rs), g: mid(gs), b: mid(bs) };
}

/** Заливка от границы кадра: помечает настоящий фон. */
function floodBackground(data, width, height, channels, isBackgroundish) {
  const bg = new Uint8Array(width * height);
  const stack = [];

  const push = (x, y) => {
    if (x < 0 || y < 0 || x >= width || y >= height) return;
    const i = y * width + x;
    if (bg[i]) return;
    const o = i * channels;
    if (!isBackgroundish(data[o], data[o + 1], data[o + 2])) return;
    bg[i] = 1;
    stack.push(x, y);
  };

  for (let x = 0; x < width; x++) {
    push(x, 0);
    push(x, height - 1);
  }
  for (let y = 0; y < height; y++) {
    push(0, y);
    push(width - 1, y);
  }

  while (stack.length) {
    const y = stack.pop();
    const x = stack.pop();
    push(x + 1, y);
    push(x - 1, y);
    push(x, y + 1);
    push(x, y - 1);
  }
  return bg;
}

/** Расширение маски: склеивает части одного объекта, разнесенные зазором. */
function dilate(mask, width, height, radius) {
  const out = new Uint8Array(mask.length);
  // Раздельно по осям — два прохода дешевле квадрата радиуса.
  const tmp = new Uint8Array(mask.length);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      let on = 0;
      for (let d = -radius; d <= radius && !on; d++) {
        const xx = x + d;
        if (xx >= 0 && xx < width && mask[y * width + xx]) on = 1;
      }
      tmp[y * width + x] = on;
    }
  }
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      let on = 0;
      for (let d = -radius; d <= radius && !on; d++) {
        const yy = y + d;
        if (yy >= 0 && yy < height && tmp[yy * width + x]) on = 1;
      }
      out[y * width + x] = on;
    }
  }
  return out;
}

/** Связные компоненты: каждая — кандидат в отдельный ассет. */
function components(mask, width, height) {
  const seen = new Uint8Array(mask.length);
  const found = [];

  for (let start = 0; start < mask.length; start++) {
    if (!mask[start] || seen[start]) continue;
    const stack = [start];
    seen[start] = 1;
    let minx = width;
    let miny = height;
    let maxx = 0;
    let maxy = 0;
    let area = 0;

    while (stack.length) {
      const i = stack.pop();
      const x = i % width;
      const y = (i / width) | 0;
      area++;
      if (x < minx) minx = x;
      if (x > maxx) maxx = x;
      if (y < miny) miny = y;
      if (y > maxy) maxy = y;

      if (x > 0 && mask[i - 1] && !seen[i - 1]) (seen[i - 1] = 1), stack.push(i - 1);
      if (x + 1 < width && mask[i + 1] && !seen[i + 1]) (seen[i + 1] = 1), stack.push(i + 1);
      if (y > 0 && mask[i - width] && !seen[i - width])
        (seen[i - width] = 1), stack.push(i - width);
      if (y + 1 < height && mask[i + width] && !seen[i + width])
        (seen[i + width] = 1), stack.push(i + width);
    }

    found.push({ minx, miny, maxx, maxy, area });
  }
  return found;
}

async function cutSheet(src, out_dir) {
  const name = basename(src, extname(src));
  const { data, info } = await sharp(src).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  const { width, height, channels } = info;

  const ref = sampleReference(data, width, height, channels);
  const isBackgroundish = makeBackgroundTest(ref);
  const bg = floodBackground(data, width, height, channels, isBackgroundish);

  // Второй проход по цвету, без связности: фон, запертый в карманах между
  // частями здания, заливкой от рамки недостижим и оставался зелеными пятнами
  // внутри силуэта. Делать это можно только потому, что проверка фона стала
  // строгой и привязана к опорному цвету листа — растения в грядках и под
  // куполом от него достаточно далеки и под нее не подпадают.
  for (let i = 0; i < width * height; i++) {
    if (bg[i]) continue;
    const o = i * channels;
    if (isBackgroundish(data[o], data[o + 1], data[o + 2])) bg[i] = 1;
  }

  // Кайма живет ТОЛЬКО у самого края объекта, поэтому и гасится только там.
  //
  // Первая редакция глушила зеленый канал у любого зеленоватого пикселя, и это
  // был тихий разгром: бирюзовое стекло куполов, теплиц и кабин обесцвечивалось
  // целиком, зелень посевов в грядках уходила в серость. Объект вырезался
  // правильно по форме и приезжал без половины цвета — самая скверная порода
  // ошибки, потому что скрипт при этом рапортует об успехе.
  const near_bg = new Uint8Array(width * height);
  const HALO = 2;
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const i = y * width + x;
      if (bg[i]) continue;
      let touches = false;
      for (let dy = -HALO; dy <= HALO && !touches; dy++) {
        for (let dx = -HALO; dx <= HALO && !touches; dx++) {
          const xx = x + dx;
          const yy = y + dy;
          if (xx < 0 || yy < 0 || xx >= width || yy >= height) continue;
          if (bg[yy * width + xx]) touches = true;
        }
      }
      near_bg[i] = touches ? 1 : 0;
    }
  }

  const rgba = Buffer.alloc(width * height * 4);
  for (let i = 0; i < width * height; i++) {
    const o = i * channels;
    const t = i * 4;
    rgba[t] = data[o];
    rgba[t + 1] = data[o + 1];
    rgba[t + 2] = data[o + 2];
    rgba[t + 3] = bg[i] ? 0 : 255;
    if (near_bg[i] && isBackgroundish(data[o], data[o + 1], data[o + 2])) {
      rgba[t + 1] = Math.max(data[o], data[o + 2]);
    }
  }

  const solid = new Uint8Array(width * height);
  for (let i = 0; i < width * height; i++) solid[i] = bg[i] ? 0 : 1;

  const merged = dilate(solid, width, height, DILATE_RADIUS.value);
  const parts = components(merged, width, height)
    .filter(
      (c) =>
        c.area / (width * height) >= MIN_AREA_SHARE &&
        c.maxx - c.minx >= MIN_SIDE &&
        c.maxy - c.miny >= MIN_SIDE,
    )
    // Сверху вниз, слева направо — порядок чтения листа.
    .sort((a, b) => (Math.abs(a.miny - b.miny) > 60 ? a.miny - b.miny : a.minx - b.minx));

  const dir = join(out_dir, name);
  mkdirSync(dir, { recursive: true });

  let saved = 0;
  for (const [idx, c] of parts.entries()) {
    const pad = 6;
    const left = Math.max(0, c.minx - pad);
    const top = Math.max(0, c.miny - pad);
    const w = Math.min(width - left, c.maxx - c.minx + 1 + pad * 2);
    const h = Math.min(height - top, c.maxy - c.miny + 1 + pad * 2);
    if (w < 2 || h < 2) continue;

    // Два прохода, а не цепочка. sharp применяет операции в СВОЕМ порядке,
    // и `trim` там стоит раньше `extract`: обрезка съедала кадр целиком до
    // того, как из него что-то вырезали, и второй же объект падал с
    // «bad extract area». Порядок вызовов в коде на это не влияет никак.
    const file = join(dir, `${String(idx + 1).padStart(2, '0')}.png`);
    const piece = await sharp(Buffer.from(rgba), { raw: { width, height, channels: 4 } })
      .extract({ left, top, width: w, height: h })
      .png()
      .toBuffer();
    await sharp(piece).trim({ threshold: 1 }).png().toFile(file);
    saved++;
  }
  console.log(`${name}: ${saved} объектов -> ${dir}`);
  return saved;
}

const [, , input, out = 'design/cut', dilate_arg] = process.argv;
if (dilate_arg) DILATE_RADIUS.value = Number(dilate_arg);
if (!input) {
  console.error('нужен путь к листу или папке');
  process.exit(1);
}

const targets = statSync(input).isDirectory()
  ? readdirSync(input)
      .filter((f) => f.toLowerCase().endsWith('.png'))
      .map((f) => join(input, f))
  : [input];

let total = 0;
for (const t of targets) total += await cutSheet(t, out);
console.log(`\nВсего вырезано: ${total}. Проверь глазами — скрипт не видит, что получилось.`);
