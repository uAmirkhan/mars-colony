/**
 * Подготовка ассетов: зеленый фон -> прозрачность, чистка кадра, размер, имя.
 *
 * Зачем скрипт, а не remove.bg: Gemini не умеет прозрачность вообще, поэтому
 * генерируем на хромакее. Ручной сервис — это десятки заходов с лимитами и
 * загрузкой файлов наружу; здесь то же самое делается одной командой локально.
 *
 * Запуск: node scripts/prep-assets.mjs
 * Вход:  design/etalons/*  (см. JOBS ниже)
 * Выход: public/assets/<категория>/<имя>.png по контракту передачи
 *
 * Переписан 2026-07-31 по итогам приемки (design/etalons-acceptance-2026-07-31.md).
 * Что было не так в первой редакции:
 *   1. Вшитые по углам кадра кнопки интерфейса резались обрезкой по долям кадра
 *      `crop: [0.14, 0, 0.72, 0.78]`. Обрезка не знает, где кончается объект, и
 *      срезала шаттлу носовую опору. Теперь мусор по кадру убирается разбором на
 *      связные области: остается самая большая, все отдельно стоящее выкидывается.
 *      Ручная обрезка больше не нужна ни одному ассету.
 *   2. Альфа была двоичной, край получался ступенчатым. Теперь мягкая.
 *   3. Гашение ореола срабатывало только при перевесе зеленого больше 30, а
 *      реальная кайма держалась на 25-30 и проходила насквозь. Теперь гасится
 *      весь приграничный поясок с порогом 6.
 */

import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import sharp from 'sharp';

const SRC = 'design/etalons';
const OUT = 'public/assets';

const JOBS = [
  {
    src: 'Gemini_Generated_Image_8kda2f8kda2f8kda.png',
    out: 'transport/drone.png',
    width: 512,
  },
  {
    src: 'Gemini_Generated_Image_kpmauokpmauokpma.png',
    out: 'buildings/drone_pad.png',
    width: 512,
    // Звездочка Gemini впечатана в правый нижний ящик. Она лежит на объекте, а
    // не на фоне: хромакей ее не видит, разбор на области не выкинет. Убираем
    // адресно по прямоугольнику в координатах исходника.
    watermark: { roi: [872, 896, 60, 68] },
  },
  {
    // Шаттл на лыжах, генерация 2026-07-31 вечер. Заменил кадр с колесами.
    src: 'main model.png',
    out: 'transport/shuttle.png',
    width: 512,
    // Из сопла бьет струя. Спрайт стоит на площадке постоянно, и вечно
    // работающий двигатель у припаркованного корабля читается как брак; в
    // списке Avoid спеки шаттла факел тоже стоит прямым запретом. Струя
    // касается сопла, значит разбор на области ее не выкинет — режем прямо.
    erase: [[655, 545, 150, 165]],
  },
  {
    // Тот же шаттл с открытым грузовым отсеком — состояние погрузки.
    // ВНИМАНИЕ: это старый кадр, шасси колесное. С лыжами закрытого шаттла не
    // совпадает — два состояния одного объекта выглядят разной техникой.
    src: 'Gemini_Generated_Image_fn52a3fn52a3fn52.png',
    out: 'transport/shuttle_open.png',
    width: 512,
  },
  {
    // Посадочная площадка, вторая генерация. Первая (темная плита
    // «LANDING ZONE») отклонена владельцем как чужой военный стиль.
    src: 'Gemini_Generated_Image_47323a47323a4732.png',
    out: 'buildings/landing_pad.png',
    width: 512,
  },
];

/**
 * Пороги хромакея. Заданы долями от зелени самого фона, а не абсолютными
 * числами: генерации приходят и с чистым #00FF00, и с приглушенным болотным
 * фоном (замер по набору: перевес зеленого 152 против 41). Абсолютный порог,
 * настроенный на первый случай, во втором срезает половину объекта — серые и
 * бежевые тона оказываются к такому фону ближе, чем порог.
 */
const BG_CUT = 0.6; // доля фоновой зелени, выше которой пиксель — фон
const BG_KEEP = 0.25; // доля, ниже которой зелень считается своей краской
const DIST_CLEAR = 60; // страховка по расстоянию: ближе этого к фону — фон
const DIST_SOLID = 150; // дальше — объект, даже если зеленоват
const SPILL_BAND = 3; // ширина пояска вдоль края, где гасится зелень, в пикселях
const SPILL_ALLOW = 6; // сколько зеленого перевеса оставляем после гашения

const clamp01 = (v) => (v < 0 ? 0 : v > 1 ? 1 : v);

/** Цвет фона — медиана по четырем углам: один угол может попасть на объект. */
function readBackground(data, w, h, c) {
  const pts = [
    [2, 2],
    [w - 3, 2],
    [2, h - 3],
    [w - 3, h - 3],
  ].map(([x, y]) => {
    const o = (y * w + x) * c;
    return { r: data[o], g: data[o + 1], b: data[o + 2] };
  });
  const mid = (k) => pts.map((p) => p[k]).sort((a, b) => a - b)[1];
  return { r: mid('r'), g: mid('g'), b: mid('b') };
}

/**
 * Альфа по перевесу зеленого над остальными каналами, отмеренному в долях от
 * перевеса самого фона. Это работает и на ярком, и на приглушенном хромакее:
 * у фона перевес всегда максимальный в кадре, у теплых и серых поверхностей он
 * отрицательный, и порог сам подстраивается под конкретную генерацию.
 *
 * Расстояние до цвета фона оставлено вторым голосом на случай объекта, который
 * действительно зеленый: бирюзовое стекло или индикатор далеко от фона по
 * цвету и остается непрозрачным, даже если формально зеленее прочих каналов.
 */
function buildAlpha(data, w, h, c) {
  const bg = readBackground(data, w, h, c);
  const bgExcess = Math.max(12, bg.g - Math.max(bg.r, bg.b));
  const cut = bgExcess * BG_CUT;
  const keep = bgExcess * BG_KEEP;

  for (let i = 0; i < w * h; i++) {
    const o = i * c;
    const r = data[o];
    const g = data[o + 1];
    const b = data[o + 2];

    const dist = Math.hypot(r - bg.r, g - bg.g, b - bg.b);
    const excess = g - Math.max(r, b);

    const byGreen = clamp01((cut - excess) / (cut - keep));
    const byDist = clamp01((dist - DIST_CLEAR) / (DIST_SOLID - DIST_CLEAR));

    data[o + 3] = Math.round(255 * Math.max(byGreen, byDist));
  }
  return bg;
}

/**
 * Восстановление цвета полупрозрачных пикселей.
 *
 * Мягкий край, ореол лампы, дымка — в исходнике это цвет объекта, смешанный с
 * зеленым фоном: `виден = a * свой + (1 - a) * фон`. Просто погасить зеленый
 * канал мало: у оранжевой лампы ореол становится не теплым, а болотным, потому
 * что зелень там не подкрас по кромке, а половина смеси. Разворачиваем формулу
 * и достаем свой цвет обратно.
 */
function unblendBackground(data, w, h, c, bg) {
  let fixed = 0;
  for (let i = 0; i < w * h; i++) {
    const o = i * c;
    const a = data[o + 3] / 255;
    if (a <= 0.12 || a >= 1) continue;
    for (let k = 0; k < 3; k++) {
      const back = k === 0 ? bg.r : k === 1 ? bg.g : bg.b;
      const v = (data[o + k] - (1 - a) * back) / a;
      data[o + k] = v < 0 ? 0 : v > 255 ? 255 : Math.round(v);
    }
    fixed++;
  }
  return fixed;
}

/**
 * Гашение остаточного подкраса по самой кромке. После разворота смеси там
 * может остаться зелень от сглаживания генератора. Гасим только в пояске вдоль
 * края: внутри объекта зелень бывает своя (бирюзовое стекло, индикаторы).
 */
function despill(data, w, h, c) {
  const edge = new Uint8Array(w * h);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const i = y * w + x;
      if (data[i * c + 3] === 0) continue;
      let border = data[i * c + 3] < 250;
      for (let dy = -1; dy <= 1 && !border; dy++) {
        for (let dx = -1; dx <= 1 && !border; dx++) {
          const nx = x + dx;
          const ny = y + dy;
          if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
          if (data[(ny * w + nx) * c + 3] < 40) border = true;
        }
      }
      if (border) edge[i] = 1;
    }
  }

  // Расширяем поясок: подкрас уходит вглубь на пару пикселей.
  for (let pass = 1; pass < SPILL_BAND; pass++) {
    const grown = edge.slice();
    for (let y = 1; y < h - 1; y++) {
      for (let x = 1; x < w - 1; x++) {
        const i = y * w + x;
        if (edge[i] || data[i * c + 3] === 0) continue;
        if (
          edge[i - 1] ||
          edge[i + 1] ||
          edge[i - w] ||
          edge[i + w]
        )
          grown[i] = 1;
      }
    }
    edge.set(grown);
  }

  let fixed = 0;
  for (let i = 0; i < w * h; i++) {
    if (!edge[i]) continue;
    const o = i * c;
    const cap = Math.max(data[o], data[o + 2]) + SPILL_ALLOW;
    if (data[o + 1] > cap) {
      data[o + 1] = cap;
      fixed++;
    }
  }
  return fixed;
}

/**
 * Разбор на связные области. Вшитые по углам кнопки интерфейса и звездочка
 * генератора отделены от объекта прозрачностью, значит это отдельные области —
 * выкидываем все, кроме самой большой. Так кадр чистится по факту, а не по
 * заранее угаданным долям, и опоры объекта остаются на месте.
 */
function keepLargestBlob(data, w, h, c) {
  const label = new Int32Array(w * h).fill(-1);
  const sizes = [];
  const queue = new Int32Array(w * h);

  for (let start = 0; start < w * h; start++) {
    if (label[start] !== -1 || data[start * c + 3] < 32) continue;
    const id = sizes.length;
    let head = 0;
    let tail = 0;
    queue[tail++] = start;
    label[start] = id;
    let size = 0;
    while (head < tail) {
      const p = queue[head++];
      size++;
      const x = p % w;
      const y = (p - x) / w;
      for (const [dx, dy] of [
        [1, 0],
        [-1, 0],
        [0, 1],
        [0, -1],
      ]) {
        const nx = x + dx;
        const ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
        const q = ny * w + nx;
        if (label[q] !== -1 || data[q * c + 3] < 32) continue;
        label[q] = id;
        queue[tail++] = q;
      }
    }
    sizes.push(size);
  }

  if (sizes.length === 0) return { dropped: 0, blobs: 0 };
  const main = sizes.indexOf(Math.max(...sizes));
  let dropped = 0;
  for (let i = 0; i < w * h; i++) {
    if (data[i * c + 3] === 0) continue;
    if (label[i] !== main) {
      data[i * c + 3] = 0;
      dropped++;
    }
  }
  return { dropped, blobs: sizes.length };
}

/**
 * Заплатка на водяной знак. Звездочка лежит поперек двух граней ящика, и
 * размазать ее усреднением нельзя — уедет ребро. Но обе грани вертикальные
 * полосы: цвет меняется по x резко, по y плавно. Значит каждый столбик
 * достраивается по своим же соседям сверху и снизу, и ребро остается на месте.
 */
function patchWatermark(data, w, h, c, roi) {
  const [rx, ry, rw, rh] = roi;
  const masked = new Uint8Array(w * h);
  let count = 0;

  // Звездочка бледнее и холоднее картона: светлее по яркости, слабее по
  // разнице красного и синего.
  for (let y = ry; y < ry + rh; y++) {
    for (let x = rx; x < rx + rw; x++) {
      const o = (y * w + x) * c;
      const lum = 0.299 * data[o] + 0.587 * data[o + 1] + 0.114 * data[o + 2];
      if (lum > 190 && data[o] - data[o + 2] < 80) {
        masked[y * w + x] = 1;
        count++;
      }
    }
  }
  // Расширяем на два пикселя: у знака мягкий край, он бледнее порога.
  for (let pass = 0; pass < 2; pass++) {
    const grown = masked.slice();
    for (let y = ry; y < ry + rh; y++) {
      for (let x = rx; x < rx + rw; x++) {
        const i = y * w + x;
        if (masked[i]) continue;
        if (masked[i - 1] || masked[i + 1] || masked[i - w] || masked[i + w]) {
          grown[i] = 1;
          count++;
        }
      }
    }
    masked.set(grown);
  }

  for (let x = rx; x < rx + rw; x++) {
    let y = ry;
    while (y < ry + rh) {
      if (!masked[y * w + x]) {
        y++;
        continue;
      }
      let end = y;
      while (end < ry + rh && masked[end * w + x]) end++;

      const top = y - 1;
      const bottom = end;
      const readPix = (yy) => {
        const o = (yy * w + x) * c;
        return [data[o], data[o + 1], data[o + 2]];
      };
      const a = top >= 0 ? readPix(top) : readPix(bottom);
      const b = bottom < h ? readPix(bottom) : readPix(top);

      for (let yy = y; yy < end; yy++) {
        const t = (yy - top) / (bottom - top);
        const o = (yy * w + x) * c;
        for (let k = 0; k < 3; k++) {
          data[o + k] = Math.round(a[k] + (b[k] - a[k]) * t);
        }
      }
      y = end;
    }
  }
  return count;
}

/** Замер каймы после обработки: сколько краевых пикселей осталось зелеными. */
function measureFringe(data, w, h, c) {
  let edge = 0;
  let green = 0;
  const alpha = (x, y) => data[(y * w + x) * c + 3];
  for (let y = 1; y < h - 1; y++) {
    for (let x = 1; x < w - 1; x++) {
      const o = (y * w + x) * c;
      if (data[o + 3] < 200) continue;
      if (
        alpha(x - 1, y) >= 40 &&
        alpha(x + 1, y) >= 40 &&
        alpha(x, y - 1) >= 40 &&
        alpha(x, y + 1) >= 40
      )
        continue;
      edge++;
      if (data[o + 1] - Math.max(data[o], data[o + 2]) > 12) green++;
    }
  }
  return { edge, green };
}

async function processOne(job) {
  const src_path = join(SRC, job.src);
  const { data, info } = await sharp(src_path)
    .ensureAlpha()
    .raw()
    .toBuffer({ resolveWithObject: true });
  const { width: w, height: h, channels: c } = info;

  const patched = job.watermark
    ? patchWatermark(data, w, h, c, job.watermark.roi)
    : 0;
  const bg = buildAlpha(data, w, h, c);

  // Прямоугольники, которые надо стереть до разбора на области: то, что
  // касается объекта и потому не отвалится само.
  for (const [ex, ey, ew, eh] of job.erase ?? []) {
    for (let y = ey; y < Math.min(ey + eh, h); y++) {
      for (let x = ex; x < Math.min(ex + ew, w); x++) {
        data[(y * w + x) * c + 3] = 0;
      }
    }
  }

  const unblended = unblendBackground(data, w, h, c, bg);
  const cleaned = despill(data, w, h, c);
  const { dropped, blobs } = keepLargestBlob(data, w, h, c);
  const fringe = measureFringe(data, w, h, c);

  const out_path = join(OUT, job.out);
  mkdirSync(dirname(out_path), { recursive: true });

  await sharp(data, { raw: { width: w, height: h, channels: c } })
    .trim() // обрезка прозрачных полей по габариту объекта
    .resize({ width: job.width, withoutEnlargement: true })
    .png()
    .toFile(out_path);

  const final = await sharp(out_path).metadata();
  const parts = [
    `${final.width}x${final.height}`,
    `фон (${bg.r},${bg.g},${bg.b})`,
    `областей ${blobs}, выкинуто ${dropped} px`,
    `смесь развернута на ${unblended} px, подкрас погашен на ${cleaned} px`,
    `кайма ${fringe.green}/${fringe.edge}`,
  ];
  if (patched) parts.push(`водяной знак закрашен, ${patched} px`);
  console.log(`${job.out}: ${parts.join(' · ')}`);
}

for (const job of JOBS) await processOne(job);
console.log('\nГотово. Проверь глазами каждый файл: скрипт не видит, красиво ли.');
