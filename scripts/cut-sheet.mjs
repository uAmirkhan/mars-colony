/**
 * Разрезалка листа ассетов: один PNG с несколькими объектами -> отдельные PNG.
 *
 * Зачем не хромакей из `prep-assets.mjs`: тот снимает фон у ОДНОГО объекта и
 * обрезает кадр по габариту. Здесь на листе восемь-восемнадцать объектов, и
 * задача другая — найти их границы и вырезать каждый отдельно.
 *
 * Четыре решения, без которых скрипт режет неправильно.
 *
 * 1. **Фон ищется заливкой от края кадра, а не по цвету.** Наивная проверка
 *    «зеленый — значит фон» съедала бы зелень внутри объектов: растения в
 *    грядках, посевы под куполом, зеленые ящики. Заливка от границы до них не
 *    дотягивается — они заперты внутри непрозрачных пикселей.
 *
 * 2. **Карман судится по среднему цвету кармана, а не по своим пикселям.**
 *    См. `POCKET_GB_MIN` — на этом месте лист терял стеклянные купола.
 *
 * 3. **Подписи снимаются до разметки объектов.** См. `findGlyphs`.
 *
 * 4. **Перед разметкой маска расширяется.** Иначе антенна, отделенная от
 *    корпуса парой прозрачных пикселей, становится отдельным «объектом», и
 *    здание приезжает без верхушки. Расширение только для поиска связности,
 *    вырезается по исходной альфе.
 *
 * Имя выходного файла берется из `sheets.json` по содержимому объекта, а не по
 * его номеру в обходе листа. См. `assignNames`.
 *
 * Запуск: node scripts/cut-sheet.mjs <лист.png|папка> [папка_выхода] [радиус]
 */

import { mkdirSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { basename, dirname, extname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
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
 * Заливка входила в здание и выедала его изнутри.
 *
 * Разделяет их отношение зеленого к синему. У фона листа синий заметно ниже
 * зеленого (216 против 120), у бирюзы они ближе (214 против 169).
 * Плюс расстояние до опорного цвета фона, снятого с рамки кадра, — на листах
 * есть градиент, и одной константой его не покрыть.
 *
 * ВАЖНО: этот тест применяется только к заливке ОТ РАМКИ. Отдельным пикселем
 * стекло от фона не отличается, порог 1.2 стеклянный купол пропускает
 * (214/169 = 1.27). Спасает не он, а связность: до запертого стекла заливка
 * не доходит.
 */
function makeBackgroundTest(ref) {
  return function isBackgroundish(r, g, b) {
    if (g <= 60) return false;
    if (g < r * 1.12) return false;
    if (g < b * 1.2) return false;
    const dist = Math.hypot(r - ref.r, g - ref.g, b - ref.b);
    return dist < 95;
  };
}

/**
 * Порог для КАРМАНОВ — областей фонового цвета, до которых заливка от рамки не
 * дотянулась. Судится среднее по карману, а не отдельный пиксель.
 *
 * Прежняя редакция гасила такие карманы одним проходом по цвету, без всякой
 * связности, «раз цвет фоновый — значит фон». Это и съело свод купола: стекло
 * оранжереи нарисовано как подкрашенный синевой фон листа, попиксельно от него
 * почти неотличимо, и весь купол уходил в дырку 21800 px. Тот же проход выедал
 * бирюзовый обод стартовой тумбы дрона.
 *
 * Замер по листу NABOR-12: у карманов настоящего фона (просветы между балками
 * стройплощадки, между гусеницами буровой) среднее g/b = 1.57-1.79, у стекол —
 * 1.23-1.30. Разрыв вдвое шире любого разумного порога, ставлю посередине.
 * Кайма и антиалиас на границе кармана в среднее входят и его не сдвигают:
 * у стекла площадь много больше периметра.
 */
const POCKET_GB_MIN = 1.45;

/**
 * Подпись под объектом — это связная мелочь, темная и выстроенная в строку.
 *
 * Три признака вместе, потому что поодиночке каждый ошибается: «мелкое» — это
 * еще и струя пара над трубой пищевого завода (226 px), «темное» — это цвет
 * обводки (10,67,24), тот же, что у букв, «в строку» — это и ряд иллюминаторов.
 * Замер по листу NABOR-12: у глифов высота 15-28 px и средняя светлота 37-79,
 * у пара светлота 198, у самого мелкого объекта площадь 5573 px.
 *
 * Снимать подписи надо ДО расширения маски: радиус 7 склеивает строку в полосу,
 * полоса дотягивается до объекта соседнего ряда, и «Стройплощадка» уезжала
 * запеченной в грузовой шаттл. Плюс подпись раздувала габарит на 17% высоты, а
 * из габарита берется масштаб в сцене.
 */
const GLYPH_MAX_H = 34;
const GLYPH_MAX_W = 60;
const GLYPH_MAX_AREA = 900;
const GLYPH_MAX_LUM = 110;
/** Строкой считается ряд от трех глифов с пересекающимися вертикалями. */
const GLYPH_ROW_MIN = 3;

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

/**
 * Карманы фонового цвета, запертые внутри силуэта. Гасим только те, у которых
 * средний цвет — чистая зелень листа. Все, что подкрашено синевой, — стекло.
 * Возвращает отчет, чтобы решение было видно в логе, а не только на картинке.
 */
function closePockets(data, width, height, channels, bg, isBackgroundish) {
  const seen = new Uint8Array(width * height);
  const report = { killed: 0, kept: 0, killed_px: 0, kept_px: 0, glass: [] };

  for (let start = 0; start < width * height; start++) {
    if (seen[start] || bg[start]) continue;
    const o0 = start * channels;
    if (!isBackgroundish(data[o0], data[o0 + 1], data[o0 + 2])) continue;

    const stack = [start];
    const cell = [start];
    seen[start] = 1;
    let sg = 0;
    let sb = 0;
    let minx = width;
    let miny = height;
    let maxx = 0;
    let maxy = 0;

    while (stack.length) {
      const i = stack.pop();
      const o = i * channels;
      sg += data[o + 1];
      sb += data[o + 2];
      const x = i % width;
      const y = (i / width) | 0;
      if (x < minx) minx = x;
      if (x > maxx) maxx = x;
      if (y < miny) miny = y;
      if (y > maxy) maxy = y;
      const step = (q) => {
        if (q < 0 || seen[q] || bg[q]) return;
        const oq = q * channels;
        if (!isBackgroundish(data[oq], data[oq + 1], data[oq + 2])) return;
        seen[q] = 1;
        stack.push(q);
        cell.push(q);
      };
      if (x > 0) step(i - 1);
      if (x + 1 < width) step(i + 1);
      if (y > 0) step(i - width);
      if (y + 1 < height) step(i + width);
    }

    const gb = sb > 0 ? sg / sb : 99;
    if (gb >= POCKET_GB_MIN) {
      for (const i of cell) bg[i] = 1;
      report.killed++;
      report.killed_px += cell.length;
    } else {
      report.kept++;
      report.kept_px += cell.length;
      if (cell.length >= 200) {
        report.glass.push({
          px: cell.length,
          gb: Number(gb.toFixed(2)),
          box: `x${minx}-${maxx} y${miny}-${maxy}`,
        });
      }
    }
  }
  return report;
}

/** Связные компоненты по маске. Каждая — кандидат в объект или в глиф. */
function components(mask, width, height, want_pixels = false) {
  const seen = new Uint8Array(mask.length);
  const found = [];

  for (let start = 0; start < mask.length; start++) {
    if (!mask[start] || seen[start]) continue;
    const stack = [start];
    const pixels = want_pixels ? [start] : null;
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

      const step = (q) => {
        if (!mask[q] || seen[q]) return;
        seen[q] = 1;
        stack.push(q);
        if (pixels) pixels.push(q);
      };
      if (x > 0) step(i - 1);
      if (x + 1 < width) step(i + 1);
      if (y > 0) step(i - width);
      if (y + 1 < height) step(i + width);
    }

    found.push({ minx, miny, maxx, maxy, area, pixels });
  }
  return found;
}

/**
 * Глифы подписей на нерасширенной маске. Возвращает индексы пикселей строк.
 * Что не попало в строку из трех глифов — не подпись и не трогается.
 */
function findGlyphs(solid, data, width, height, channels) {
  const parts = components(solid, width, height, true);
  const candidates = [];

  for (const p of parts) {
    const w = p.maxx - p.minx + 1;
    const h = p.maxy - p.miny + 1;
    if (h > GLYPH_MAX_H || w > GLYPH_MAX_W || p.area > GLYPH_MAX_AREA) continue;
    let lum = 0;
    for (const i of p.pixels) {
      const o = i * channels;
      lum += 0.299 * data[o] + 0.587 * data[o + 1] + 0.114 * data[o + 2];
    }
    if (lum / p.area > GLYPH_MAX_LUM) continue;
    candidates.push(p);
  }

  // Группируем в строки по пересечению вертикальных отрезков.
  candidates.sort((a, b) => a.miny - b.miny);
  const rows = [];
  for (const p of candidates) {
    const row = rows.find((r) => p.miny <= r.maxy && p.maxy >= r.miny);
    if (row) {
      row.items.push(p);
      row.miny = Math.min(row.miny, p.miny);
      row.maxy = Math.max(row.maxy, p.maxy);
    } else {
      rows.push({ miny: p.miny, maxy: p.maxy, items: [p] });
    }
  }

  const doomed = [];
  let lines = 0;
  for (const row of rows) {
    if (row.items.length < GLYPH_ROW_MIN) continue;
    lines++;
    for (const p of row.items) doomed.push(...p.pixels);
  }
  return { pixels: doomed, lines };
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

// ---------------------------------------------------------------------------
// Имя файла из содержимого
// ---------------------------------------------------------------------------

/**
 * Как имя разъезжалось с объектом.
 *
 * Файлы назывались по индексу в отсортированном списке, а сортировка была
 * `Math.abs(a.miny - b.miny) > 60 ? a.miny - b.miny : a.minx - b.minx` —
 * компаратор по ВЕРХУ силуэта. У грузового шаттла киль поднят на 80 px выше
 * стартовой тумбы дрона, порог 60 сработал, шаттл встал в списке раньше тумбы,
 * и вся третья строка листа сдвинулась на одну позицию. Итог: в `shuttle.png`
 * лежала тумба, в `drone_pad.png` — шаттл с чужой подписью. Компаратор к тому
 * же не транзитивен, то есть результат зависел и от порядка обхода.
 *
 * Здесь имя не назначается порядком. Объекты разбиваются на ряды по ЦЕНТРУ
 * (центр не прыгает от высокого киля), а внутри ряда имя выбирается перебором
 * всех сочетаний по цене признаков: доля площади, пропорция, средний цвет.
 * Позиция входит в цену слабым слагаемым — только чтобы развести близнецов.
 * Если победившее сочетание дороже `MATCH_MAX_COST`, скрипт падает, а не
 * пишет файл с чужим содержимым: молча неверное имя дороже сломанной сборки.
 */
const MATCH_MAX_COST = 0.55;
const POSITION_WEIGHT = 0.06;

function signatureOf(part, data, width, channels, sheet_area, solid) {
  const w = part.maxx - part.minx + 1;
  const h = part.maxy - part.miny + 1;
  let r = 0;
  let g = 0;
  let b = 0;
  let n = 0;
  // Только пиксели самого объекта. Среднее по габаритному прямоугольнику
  // размывается фоном, и четыре тайла поверхности становятся неразличимы:
  // песок (162,157,108) и реголит (148,144,100) расходились на 20 единиц,
  // по своим пикселям — на 60.
  for (let y = part.miny; y <= part.maxy; y++) {
    for (let x = part.minx; x <= part.maxx; x++) {
      const i = y * width + x;
      if (!solid[i]) continue;
      const o = i * channels;
      r += data[o];
      g += data[o + 1];
      b += data[o + 2];
      n++;
    }
  }
  if (n === 0) n = 1;
  return {
    area: Number((part.area / sheet_area).toFixed(5)),
    aspect: Number((w / h).toFixed(3)),
    rgb: [Math.round(r / n), Math.round(g / n), Math.round(b / n)],
  };
}

function matchCost(found, want, col_found, col_want) {
  const byArea = Math.abs(Math.log(found.area / want.area));
  const byAspect = Math.abs(Math.log(found.aspect / want.aspect));
  const byColor =
    Math.hypot(
      found.rgb[0] - want.rgb[0],
      found.rgb[1] - want.rgb[1],
      found.rgb[2] - want.rgb[2],
    ) / 160;
  return byArea + byAspect + byColor + POSITION_WEIGHT * Math.abs(col_found - col_want);
}

function permutations(n) {
  if (n === 0) return [[]];
  const out = [];
  for (const tail of permutations(n - 1)) {
    for (let i = 0; i <= tail.length; i++) {
      out.push([...tail.slice(0, i), n - 1, ...tail.slice(i)]);
    }
  }
  return out;
}

/**
 * Раскладывает найденные объекты на ряды по вертикальному центру.
 * Порог — половина медианной высоты объекта: ряды на листах разделены
 * подписью, то есть заметно больше, чем разброс центров внутри ряда.
 */
function splitRows(parts) {
  const hs = parts.map((p) => p.maxy - p.miny + 1).sort((a, b) => a - b);
  const gap = hs[hs.length >> 1] * 0.5;
  const sorted = [...parts].sort((a, b) => (a.miny + a.maxy) / 2 - (b.miny + b.maxy) / 2);
  const rows = [];
  let current = [];
  let last = null;
  for (const p of sorted) {
    const cy = (p.miny + p.maxy) / 2;
    if (last !== null && cy - last > gap) {
      rows.push(current);
      current = [];
    }
    current.push(p);
    last = cy;
  }
  if (current.length) rows.push(current);
  for (const r of rows) r.sort((a, b) => (a.minx + a.maxx) / 2 - (b.minx + b.maxx) / 2);
  return rows;
}

function assignNames(parts, plan, data, width, channels, sheet_area, solid) {
  const rows = splitRows(parts);
  if (rows.length !== plan.length) {
    throw new Error(
      `рядов на листе ${rows.length}, в sheets.json описано ${plan.length} — план устарел`,
    );
  }

  const result = new Map();
  const log = [];
  for (const [ri, row] of rows.entries()) {
    const want = plan[ri];
    if (row.length !== want.length) {
      throw new Error(
        `ряд ${ri + 1}: найдено объектов ${row.length}, в плане ${want.length}`,
      );
    }
    const sigs = row.map((p) => signatureOf(p, data, width, channels, sheet_area, solid));
    let best = null;
    let second = Infinity;
    for (const perm of permutations(row.length)) {
      let cost = 0;
      for (let i = 0; i < row.length; i++) cost += matchCost(sigs[i], want[perm[i]], i, perm[i]);
      cost /= row.length;
      if (best === null || cost < best.cost) {
        second = best ? best.cost : second;
        best = { cost, perm };
      } else if (cost < second) second = cost;
    }
    if (best.cost > MATCH_MAX_COST) {
      throw new Error(
        `ряд ${ri + 1}: лучшее сопоставление стоит ${best.cost.toFixed(2)} > ${MATCH_MAX_COST}. ` +
          'Содержимое листа не похоже на описанное в sheets.json — резать вслепую нельзя.',
      );
    }
    for (let i = 0; i < row.length; i++) result.set(row[i], want[best.perm[i]].name);
    log.push(
      `ряд ${ri + 1}: ${row.map((p, i) => want[best.perm[i]].name).join(', ')}` +
        ` (цена ${best.cost.toFixed(2)}, следующий вариант ${second === Infinity ? '—' : second.toFixed(2)})`,
    );
  }
  return { result, log };
}

// ---------------------------------------------------------------------------

const HERE = dirname(fileURLToPath(import.meta.url));
let SHEETS = {};
try {
  SHEETS = JSON.parse(readFileSync(join(HERE, 'sheets.json'), 'utf8'));
} catch {
  SHEETS = {};
}

async function cutSheet(src, out_dir) {
  const name = basename(src, extname(src));
  const { data, info } = await sharp(src).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  const { width, height, channels } = info;

  const ref = sampleReference(data, width, height, channels);
  const isBackgroundish = makeBackgroundTest(ref);
  const bg = floodBackground(data, width, height, channels, isBackgroundish);
  const pockets = closePockets(data, width, height, channels, bg, isBackgroundish);

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

  const solid = new Uint8Array(width * height);
  for (let i = 0; i < width * height; i++) solid[i] = bg[i] ? 0 : 1;

  // Подписи — до расширения маски и до вырезания, иначе они и склеиваются с
  // объектом, и остаются в его альфе, и раздувают габарит.
  const glyphs = findGlyphs(solid, data, width, height, channels);
  for (const i of glyphs.pixels) {
    solid[i] = 0;
    bg[i] = 1;
  }

  const rgba = Buffer.alloc(width * height * 4);
  for (let i = 0; i < width * height; i++) {
    const o = i * channels;
    const t = i * 4;
    rgba[t] = data[o];
    rgba[t + 1] = data[o + 1];
    rgba[t + 2] = data[o + 2];
    rgba[t + 3] = bg[i] ? 0 : 255;
    if (near_bg[i] && !bg[i] && isBackgroundish(data[o], data[o + 1], data[o + 2])) {
      rgba[t + 1] = Math.max(data[o], data[o + 2]);
    }
  }

  const merged = dilate(solid, width, height, DILATE_RADIUS.value);
  const parts = components(merged, width, height).filter(
    (c) =>
      c.area / (width * height) >= MIN_AREA_SHARE &&
      c.maxx - c.minx >= MIN_SIDE &&
      c.maxy - c.miny >= MIN_SIDE,
  );

  // Габарит берем по ИСХОДНОЙ маске, а не по расширенной: дилатация раздувает
  // рамку на радиус, и в масштаб сцены уходил бы лишний поясок.
  for (const c of parts) {
    let minx = width;
    let miny = height;
    let maxx = 0;
    let maxy = 0;
    for (let y = c.miny; y <= c.maxy; y++) {
      for (let x = c.minx; x <= c.maxx; x++) {
        if (!solid[y * width + x]) continue;
        if (x < minx) minx = x;
        if (x > maxx) maxx = x;
        if (y < miny) miny = y;
        if (y > maxy) maxy = y;
      }
    }
    if (maxx >= minx) Object.assign(c, { minx, miny, maxx, maxy });
  }

  // Заготовка плана для sheets.json: печатает признаки найденных объектов в
  // порядке рядов. Так план пишется по замеру листа, а не на глаз.
  if (process.env.SHEET_SIG) {
    for (const [ri, row] of splitRows(parts).entries()) {
      console.log(`  ряд ${ri + 1}:`);
      for (const p of row) {
        const s = signatureOf(p, data, width, channels, width * height, solid);
        console.log(
          `    { "name": "?", "area": ${s.area}, "aspect": ${s.aspect}, "rgb": [${s.rgb}] },` +
            `  // ${p.maxx - p.minx + 1}x${p.maxy - p.miny + 1} @ x${p.minx} y${p.miny}`,
        );
      }
    }
  }

  const plan = SHEETS[name];
  let names = null;
  let match_log = [];
  if (plan) {
    const assigned = assignNames(parts, plan, data, width, channels, width * height, solid);
    names = assigned.result;
    match_log = assigned.log;
  } else {
    // Без плана — прежний порядок чтения листа, файлы нумеруются.
    parts.sort((a, b) =>
      Math.abs((a.miny + a.maxy) / 2 - (b.miny + b.maxy) / 2) > 60
        ? a.miny - b.miny
        : a.minx - b.minx,
    );
  }

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
    const stem = names ? names.get(c) : String(idx + 1).padStart(2, '0');
    const file = join(dir, `${stem}.png`);
    const piece = await sharp(Buffer.from(rgba), { raw: { width, height, channels: 4 } })
      .extract({ left, top, width: w, height: h })
      .png()
      .toBuffer();
    await sharp(piece).trim({ threshold: 1 }).png().toFile(file);
    saved++;
  }
  const notes = [
    `${saved} объектов -> ${dir}`,
    `карманы: погашено ${pockets.killed} (${pockets.killed_px} px), сохранено как стекло ${pockets.kept} (${pockets.kept_px} px)`,
    `подписи: строк ${glyphs.lines}, снято ${glyphs.pixels.length} px`,
  ];
  console.log(`${name}: ${notes.join(' · ')}`);
  for (const g of pockets.glass) console.log(`    стекло сохранено: ${g.px} px, g/b ${g.gb}, ${g.box}`);
  for (const l of match_log) console.log(`    ${l}`);
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
console.log(`\nВсего вырезано: ${total}. Проверь: node scripts/check-sprites.mjs <папка>`);
