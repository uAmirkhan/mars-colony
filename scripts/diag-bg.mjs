/** Диагностика распознавания фона: почему заливка не снимает зеленое. */
import sharp from 'sharp';

const files = process.argv.slice(2);

for (const f of files) {
  const { data, info } = await sharp(f).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  const { width, height, channels } = info;
  const px = (x, y) => {
    const o = (y * width + x) * channels;
    return [data[o], data[o + 1], data[o + 2]];
  };

  console.log(`\n${f.split(/[\\/]/).pop()}  ${width}x${height}`);
  const points = [
    ['левый верх', 2, 2],
    ['правый верх', width - 3, 2],
    ['левый низ', 2, height - 3],
    ['правый низ', width - 3, height - 3],
    ['центр верха', Math.floor(width / 2), 2],
    ['центр низа', Math.floor(width / 2), height - 3],
  ];
  for (const [label, x, y] of points) {
    const [r, g, b] = px(x, y);
    const mx = Math.max(r, g, b);
    const mn = Math.min(r, g, b);
    const sat = mx === 0 ? 0 : (mx - mn) / mx;
    const ok = g >= r && g > b && sat > 0.12 && g > 60;
    console.log(
      `  ${label.padEnd(13)} rgb(${r},${g},${b}) sat=${sat.toFixed(3)} -> ${ok ? 'ФОН' : 'НЕ ФОН'}`,
    );
  }
}
