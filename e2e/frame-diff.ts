import sharp from 'sharp';

/**
 * Сравнение кадров. Тот же прием, что в `mars-unity/check-interaction.mjs`:
 * реакция интерфейса доказывается разницей пикселей до и после, а не отчетом
 * приложения о самом себе. Отчет — ровно тот канал, по которому в проект уже
 * приходил ложный успех.
 */

/** Разница канала, начиная с которой пиксель считается изменившимся. */
const CHANNEL_DELTA = 12;

export async function diffRatio(a: Buffer, b: Buffer): Promise<number> {
  const [ra, rb] = await Promise.all([
    sharp(a).raw().toBuffer({ resolveWithObject: true }),
    sharp(b).raw().toBuffer({ resolveWithObject: true }),
  ]);

  const ch = ra.info.channels;
  const n = Math.min(ra.data.length, rb.data.length);
  let changed = 0;
  let pixels = 0;

  for (let i = 0; i + ch <= n; i += ch) {
    pixels += 1;
    for (let c = 0; c < Math.min(3, ch); c++) {
      if (Math.abs((ra.data[i + c] ?? 0) - (rb.data[i + c] ?? 0)) > CHANNEL_DELTA) {
        changed += 1;
        break;
      }
    }
  }

  return pixels === 0 ? 1 : changed / pixels;
}
