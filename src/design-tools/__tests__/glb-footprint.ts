/**
 * Независимый (без Blender) счетчик доли пятна застройки для GLB.
 *
 * Повторяет идею `design/tools/plita.py`: доля клеток XZ-сетки, в которых
 * есть геометрия выше `level` от нижней точки, — метрика, которую
 * `design/tools/check-model.mjs` не считает вообще (см. `verdict()` в этом
 * файле: там есть тонкость габарита, но нет счета по сетке).
 *
 * Матрицы узлов (`mulMat`/`applyMat`/`nodeMatrix`) и чтение GLB-контейнера —
 * копия соответствующих функций `check-model.mjs`. Копия, а не импорт: сам
 * скрипт ничего не экспортирует, разбирает `process.argv` и завершает
 * процесс через `process.exit` при импорте как модуля — использовать его как
 * библиотеку нельзя. Формулы побайтово совпадают, так что число сюда не
 * подгоняется руками — это ровно то же наблюдение, что и в исходном
 * инструменте, просто на голом Node без Blender.
 *
 * Проверено на всей партии моделей: числа этого модуля совпадают с выводом
 * `plita.py` (запуск в Blender 5.2, `design/tools/plita.py`) с точностью до
 * округления сетки — см. `tester-work.md`, прогон 7.
 */

import { readFileSync } from 'node:fs';

type Mat4 = number[];
type Vec3 = [number, number, number];

function mulMat(a: Mat4, b: Mat4): Mat4 {
  const out = new Array(16).fill(0);
  for (let c = 0; c < 4; c++) {
    for (let r = 0; r < 4; r++) {
      let sum = 0;
      for (let k = 0; k < 4; k++) sum += (a[k * 4 + r] as number) * (b[c * 4 + k] as number);
      out[c * 4 + r] = sum;
    }
  }
  return out;
}

function applyMat(m: Mat4, p: Vec3): Vec3 {
  const [x, y, z] = p;
  return [
    (m[0] as number) * x + (m[4] as number) * y + (m[8] as number) * z + (m[12] as number),
    (m[1] as number) * x + (m[5] as number) * y + (m[9] as number) * z + (m[13] as number),
    (m[2] as number) * x + (m[6] as number) * y + (m[10] as number) * z + (m[14] as number),
  ];
}

// biome-ignore lint/suspicious/noExplicitAny: разбор произвольного glTF-узла
function nodeMatrix(node: any): Mat4 {
  if (Array.isArray(node.matrix)) return node.matrix.slice();
  const t = node.translation ?? [0, 0, 0];
  const q = node.rotation ?? [0, 0, 0, 1];
  const s = node.scale ?? [1, 1, 1];
  const [x, y, z, w] = q;
  const x2 = x + x;
  const y2 = y + y;
  const z2 = z + z;
  const xx = x * x2;
  const xy = x * y2;
  const xz = x * z2;
  const yy = y * y2;
  const yz = y * z2;
  const zz = z * z2;
  const wx = w * x2;
  const wy = w * y2;
  const wz = w * z2;
  return [
    (1 - (yy + zz)) * s[0],
    (xy + wz) * s[0],
    (xz - wy) * s[0],
    0,
    (xy - wz) * s[1],
    (1 - (xx + zz)) * s[1],
    (yz + wx) * s[1],
    0,
    (xz + wy) * s[2],
    (yz - wx) * s[2],
    (1 - (xx + yy)) * s[2],
    0,
    t[0],
    t[1],
    t[2],
    1,
  ];
}

function loadGlb(path: string) {
  const buf = readFileSync(path);
  if (buf.readUInt32LE(0) !== 0x46546c67) throw new Error(`это не GLB: ${path}`);
  const total = buf.readUInt32LE(8);
  let off = 12;
  // biome-ignore lint/suspicious/noExplicitAny: сырой JSON из GLB-контейнера
  let json: any = null;
  let bin: Buffer | null = null;
  while (off + 8 <= total) {
    const len = buf.readUInt32LE(off);
    const type = buf.readUInt32LE(off + 4);
    const data = buf.subarray(off + 8, off + 8 + len);
    if (type === 0x4e4f534a) json = JSON.parse(new TextDecoder().decode(data));
    else if (type === 0x004e4942) bin = data;
    off += 8 + len;
  }
  if (json === null) throw new Error(`в GLB нет раздела JSON: ${path}`);
  if (bin === null) throw new Error(`в GLB нет двоичного буфера: ${path}`);
  return { json, bin };
}

function readPositions(
  // biome-ignore lint/suspicious/noExplicitAny: сырой JSON из GLB-контейнера
  json: any,
  bin: Buffer,
  accessorIdx: number,
): Vec3[] {
  const acc = json.accessors[accessorIdx];
  const view = json.bufferViews[acc.bufferView];
  const stride = view.byteStride ?? 12;
  const start = (view.byteOffset ?? 0) + (acc.byteOffset ?? 0);
  const out: Vec3[] = new Array(acc.count);
  for (let i = 0; i < acc.count; i++) {
    const at = start + i * stride;
    out[i] = [bin.readFloatLE(at), bin.readFloatLE(at + 4), bin.readFloatLE(at + 8)];
  }
  return out;
}

export interface FootprintResult {
  size: Vec3;
  /** Наименьший габарит / наибольший — та же метрика, что "тонкость" в plita.py. */
  thinness: number;
  /** Доля клеток XZ-сетки с геометрией выше `level` от низа — метрика "плита". */
  dolya: number;
  verts: number;
  lowCells: number;
  highCells: number;
}

/**
 * Считает долю пятна застройки, занятую геометрией выше `level` от нижней
 * точки модели. GRID и LEVEL по умолчанию — те же, что в `plita.py` (48 и
 * 0.25), чтобы число было сравнимо один к одному.
 */
export function footprintOccupancy(path: string, grid = 48, level = 0.25): FootprintResult {
  const { json, bin } = loadGlb(path);
  const nodes = json.nodes ?? [];
  const declared: number[] =
    json.scenes?.[json.scene ?? 0]?.nodes ?? nodes.map((_: unknown, i: number) => i);
  const identity: Mat4 = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

  const world: Vec3[] = [];
  const walk = (idx: number, parent: Mat4) => {
    const node = nodes[idx];
    if (!node) return;
    const w = mulMat(parent, nodeMatrix(node));
    if (node.mesh !== undefined) {
      const mesh = json.meshes[node.mesh];
      // biome-ignore lint/suspicious/noExplicitAny: сырой JSON из GLB-контейнера
      for (const prim of mesh.primitives ?? ([] as any[])) {
        const posIdx = prim.attributes?.POSITION;
        if (posIdx === undefined) continue;
        for (const p of readPositions(json, bin, posIdx)) world.push(applyMat(w, p));
      }
    }
    for (const c of node.children ?? []) walk(c, w);
  };
  for (const r of declared) walk(r, identity);

  const lo: Vec3 = [Infinity, Infinity, Infinity];
  const hi: Vec3 = [-Infinity, -Infinity, -Infinity];
  for (const p of world) {
    for (let k = 0; k < 3; k++) {
      const v = p[k] as number;
      if (v < (lo[k] as number)) lo[k] = v;
      if (v > (hi[k] as number)) hi[k] = v;
    }
  }
  const size: Vec3 = [hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]];
  // Y — высота в glTF (Y-up), как и в остальном check-model.mjs.
  const cut = lo[1] + size[1] * level;

  const low = new Set<number>();
  const high = new Set<number>();
  for (const p of world) {
    const cx = Math.min(grid - 1, Math.floor(((p[0] - lo[0]) / size[0]) * grid));
    const cz = Math.min(grid - 1, Math.floor(((p[2] - lo[2]) / size[2]) * grid));
    const key = cx * grid + cz;
    low.add(key);
    if (p[1] >= cut) high.add(key);
  }

  const dims = [...size].sort((a, b) => a - b);
  const thinness = (dims[0] as number) / (dims[2] as number);
  const dolya = low.size ? high.size / low.size : 0;
  return {
    size,
    thinness,
    dolya,
    verts: world.length,
    lowCells: low.size,
    highCells: high.size,
  };
}
