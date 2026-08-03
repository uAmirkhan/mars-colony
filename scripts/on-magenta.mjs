/**
 * Кладет вырезанный ассет на пурпурный фон.
 *
 * Единственный надежный способ увидеть, что реально в альфа-канале: под
 * прозрачными пикселями RGB остается прежним, и часть просмотрщиков рисует
 * именно его, а не прозрачность. На пурпурном любая недоснятая зелень видна
 * мгновенно, а честная прозрачность становится пурпурной.
 */
import { mkdirSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import sharp from 'sharp';

const out_dir = 'design/check';
mkdirSync(out_dir, { recursive: true });

for (const src of process.argv.slice(2)) {
  const meta = await sharp(src).metadata();
  const name = `${basename(dirname(src))}__${basename(src)}`;
  const out = join(out_dir, name);
  await sharp({
    create: {
      width: meta.width,
      height: meta.height,
      channels: 3,
      background: { r: 255, g: 0, b: 255 },
    },
  })
    .composite([{ input: src }])
    .png()
    .toFile(out);
  console.log(out);
}
