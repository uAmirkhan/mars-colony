/**
 * Подготовка ассетов: зеленый фон -> прозрачность, обрезка, размер, имя.
 *
 * Зачем скрипт, а не remove.bg: Gemini не умеет прозрачность вообще, поэтому
 * генерируем на хромакее. Ручной сервис — это десятки заходов с лимитами и
 * загрузкой файлов наружу; здесь то же самое делается одной командой локально.
 *
 * Запуск: node scripts/prep-assets.mjs
 * Вход:  design/etalons/*  (см. JOBS ниже)
 * Выход: public/assets/<категория>/<имя>.png по контракту передачи
 */

import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import sharp from 'sharp';

const SRC = 'design/etalons';
const OUT = 'public/assets';

/**
 * Что резать. crop задается долями кадра [left, top, width, height] и нужен,
 * когда генерация принесла мусор по краям (вшитые кнопки интерфейса).
 */
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
  },
  {
    // Закрытый шаттл. По углам вшиты кнопки интерфейса — режем центр.
    src: 'Gemini_Generated_Image_q1b8ieq1b8ieq1b8.png',
    out: 'transport/shuttle.png',
    width: 512,
    crop: [0.14, 0.0, 0.72, 0.78],
  },
  {
    // Тот же шаттл с открытым грузовым отсеком — состояние погрузки.
    src: 'Gemini_Generated_Image_fn52a3fn52a3fn52.png',
    out: 'transport/shuttle_open.png',
    width: 512,
    crop: [0.14, 0.0, 0.72, 0.78],
  },
];

/**
 * Хромакей: не «ровно #00FF00», а расстояние до фонового цвета, снятого с угла
 * кадра. Генератор гуляет оттенком зеленого от картинки к картинке, жесткая
 * константа оставляла бы ореол. Порог подобран по факту на этом наборе.
 */
function removeChroma(data, info, bg) {
  const { width, height, channels } = info;
  for (let i = 0; i < width * height; i++) {
    const o = i * channels;
    const r = data[o];
    const g = data[o + 1];
    const b = data[o + 2];

    const dist = Math.hypot(r - bg.r, g - bg.g, b - bg.b);
    const green_dominant = g > r + 30 && g > b + 30;

    if (dist < 90 || (green_dominant && dist < 140)) {
      data[o + 3] = 0;
    } else if (green_dominant) {
      // Ореол по кромке: гасим зелень до уровня соседних каналов, иначе
      // вокруг объекта останется салатовая кайма на любом фоне.
      data[o + 1] = Math.max(r, b);
    }
  }
  return data;
}

async function processOne(job) {
  const src_path = join(SRC, job.src);
  let img = sharp(src_path);
  const meta = await img.metadata();

  if (job.crop) {
    const [l, t, w, h] = job.crop;
    img = img.extract({
      left: Math.round(meta.width * l),
      top: Math.round(meta.height * t),
      width: Math.round(meta.width * w),
      height: Math.round(meta.height * h),
    });
  }

  const { data, info } = await img
    .ensureAlpha()
    .raw()
    .toBuffer({ resolveWithObject: true });

  // Цвет фона — с левого верхнего угла обрезанного кадра.
  const bg = { r: data[0], g: data[1], b: data[2] };
  const cleaned = removeChroma(data, info, bg);

  const out_path = join(OUT, job.out);
  mkdirSync(dirname(out_path), { recursive: true });

  await sharp(cleaned, {
    raw: { width: info.width, height: info.height, channels: info.channels },
  })
    .trim() // обрезка прозрачных полей по габариту объекта
    .resize({ width: job.width, withoutEnlargement: true })
    .png()
    .toFile(out_path);

  const final = await sharp(out_path).metadata();
  console.log(`${job.out}: ${final.width}x${final.height}, фон снят (${bg.r},${bg.g},${bg.b})`);
}

for (const job of JOBS) await processOne(job);
console.log('\nГотово. Проверь глазами каждый файл: скрипт не видит, красиво ли.');
