/**
 * Приемка модели без Unity: `node check-model.mjs путь\к\модели.glb`
 *
 * Отдельный самодостаточный измеритель для машины, где стоит только генератор
 * моделей и нет ни игрового движка, ни витрины. TRELLIS отдает GLB, а его
 * спецификация ОБЯЗЫВАЕТ хранить габарит позиций прямо в заголовке — значит
 * замер получается точный, а не приблизительный, и никакого редактора для
 * него не нужно.
 *
 * Зачем вообще мерить. Сгенерированная модель почти всегда приходит в чужом
 * масштабе и с основанием не в нуле: генератор не знает ни про клетку сцены,
 * ни про уровень грунта. На глаз это не ловится — «основание примерно в нуле»
 * на картинке выглядит нормально, а в сцене объект висит над землей на два
 * сантиметра, и видно это только в упор.
 *
 * Полная версия с примеркой кадром и чтением FBX через Unity живет в
 * `mars-unity/fit-model.mjs`. Здесь ровно то, что работает на голом Node.
 */

import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { basename, dirname, extname, join, resolve } from 'node:path';

/** Клетка набора KayKit. По ней считает раскладку вся сцена витрины. */
const GRID = 2.0;

/**
 * Чем покрашена сцена сейчас.
 *
 * В кадре живут два набора и ровно два способа покраски: KayKit сидит на одном
 * общем атласе `spacebits_texture`, Kenney — на плоских цветах. Модель с третьим
 * способом не сломается, но кадр от нее читается склейкой, а рубрика цикла
 * ставит за склейку потолок в шестьдесят баллов по шкале сцены.
 */
const PALETTE = [
  'metal', 'metalDark', 'metalRed', 'dark', 'crystal',
  'rock', 'rockDark', 'rockTrack', 'bone', 'skin', 'spacebits_texture',
];

const KIT_ATLAS = 'spacebits_texture.png';

const input = process.argv[2];
if (!input) {
  console.error('нужен путь к модели: node check-model.mjs путь\к\модели.glb');
  process.exit(2);
}
const source = resolve(input);
if (!existsSync(source)) {
  console.error(`файла нет: ${source}`);
  process.exit(2);
}
const ext = extname(source).toLowerCase();
if (ext !== '.glb' && ext !== '.gltf') {
  console.error(`формат ${ext} без Unity не читается. Экспортируй в GLB или glTF.`);
  process.exit(2);
}
const name = basename(source, ext);

function loadGltf(path) {
  if (extname(path).toLowerCase() === '.glb') {
    const buf = readFileSync(path);
    if (buf.readUInt32LE(0) !== 0x46546c67) throw new Error('это не GLB: неверная подпись файла');
    const total = buf.readUInt32LE(8);
    let off = 12;
    let json = null;
    let bin = null;
    while (off + 8 <= total) {
      const len = buf.readUInt32LE(off);
      const type = buf.readUInt32LE(off + 4);
      const data = buf.subarray(off + 8, off + 8 + len);
      if (type === 0x4e4f534a) json = JSON.parse(new TextDecoder().decode(data));
      else if (type === 0x004e4942) bin = data;
      off += 8 + len;
    }
    if (json === null) throw new Error('в GLB нет раздела с описанием сцены');
    return { json, bin, dir: dirname(path) };
  }
  return { json: JSON.parse(readFileSync(path, 'utf8')), bin: null, dir: dirname(path) };
}

/** Матрицы glTF — по столбцам, как в OpenGL. Умножение и перенос точки. */
function mulMat(a, b) {
  const out = new Array(16).fill(0);
  for (let c = 0; c < 4; c++) {
    for (let r = 0; r < 4; r++) {
      let sum = 0;
      for (let k = 0; k < 4; k++) sum += a[k * 4 + r] * b[c * 4 + k];
      out[c * 4 + r] = sum;
    }
  }
  return out;
}

function applyMat(m, p) {
  return [
    m[0] * p[0] + m[4] * p[1] + m[8] * p[2] + m[12],
    m[1] * p[0] + m[5] * p[1] + m[9] * p[2] + m[13],
    m[2] * p[0] + m[6] * p[1] + m[10] * p[2] + m[14],
  ];
}

function nodeMatrix(node) {
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
    (1 - (yy + zz)) * s[0], (xy + wz) * s[0], (xz - wy) * s[0], 0,
    (xy - wz) * s[1], (1 - (xx + zz)) * s[1], (yz + wx) * s[1], 0,
    (xz + wy) * s[2], (yz - wx) * s[2], (1 - (xx + yy)) * s[2], 0,
    t[0], t[1], t[2], 1,
  ];
}

/**
 * Габарит одной позиции.
 *
 * Спецификация glTF обязывает хранить `min` и `max` у аксессора позиций,
 * поэтому обычно габарит читается прямо из заголовка, без разбора двоичных
 * данных. Экспортеры это правило иногда нарушают — тогда считаем по вершинам
 * сами. Молча вернуть «нет габарита» нельзя: приемка отчитается «встает как
 * есть» на модели, которую вообще не мерила.
 */
function positionBox(json, bin, dir, accessor_idx) {
  const acc = json.accessors?.[accessor_idx];
  if (!acc) return null;
  if (Array.isArray(acc.min) && Array.isArray(acc.max)) return { min: acc.min, max: acc.max };

  const view = json.bufferViews?.[acc.bufferView];
  if (!view || acc.componentType !== 5126 || acc.type !== 'VEC3') return null;
  const raw = bufferBytes(json, bin, dir, view.buffer);
  if (raw === null) return null;

  const stride = view.byteStride ?? 12;
  const start = (view.byteOffset ?? 0) + (acc.byteOffset ?? 0);
  const min = [Infinity, Infinity, Infinity];
  const max = [-Infinity, -Infinity, -Infinity];
  for (let i = 0; i < acc.count; i++) {
    const at = start + i * stride;
    for (let k = 0; k < 3; k++) {
      const v = raw.readFloatLE(at + k * 4);
      if (v < min[k]) min[k] = v;
      if (v > max[k]) max[k] = v;
    }
  }
  return { min, max };
}

function bufferBytes(json, bin, dir, buffer_idx) {
  const buffer = json.buffers?.[buffer_idx];
  if (!buffer) return null;
  if (buffer.uri === undefined) return bin;
  if (buffer.uri.startsWith('data:')) {
    return Buffer.from(buffer.uri.slice(buffer.uri.indexOf(',') + 1), 'base64');
  }
  const path = join(dir, decodeURIComponent(buffer.uri));
  return existsSync(path) ? readFileSync(path) : null;
}

function measureGltf(path) {
  const { json, bin, dir } = loadGltf(path);
  const nodes = json.nodes ?? [];

  // Корни ищем вычитанием детей, а не доверяем полю сцены: файлы без сцены
  // встречаются, и обход всех узлов подряд посчитал бы вложенные дважды.
  const child = new Set();
  for (const n of nodes) for (const c of n.children ?? []) child.add(c);
  const declared = json.scenes?.[json.scene ?? 0]?.nodes;
  const roots = declared ?? nodes.map((_, i) => i).filter((i) => !child.has(i));

  const min = [Infinity, Infinity, Infinity];
  const max = [-Infinity, -Infinity, -Infinity];
  let tris = 0;
  let meshes = 0;
  let submeshes = 0;
  let unmeasured = 0;
  const materials = [];

  const walk = (idx, parent) => {
    const node = nodes[idx];
    if (!node) return;
    const world = mulMat(parent, nodeMatrix(node));

    if (node.mesh !== undefined) {
      const mesh = json.meshes?.[node.mesh];
      if (mesh) {
        meshes += 1;
        for (const prim of mesh.primitives ?? []) {
          submeshes += 1;

          const mode = prim.mode ?? 4;
          const count =
            prim.indices !== undefined
              ? (json.accessors?.[prim.indices]?.count ?? 0)
              : (json.accessors?.[prim.attributes?.POSITION]?.count ?? 0);
          if (mode === 4) tris += Math.floor(count / 3);

          if (prim.material !== undefined) {
            const mat = json.materials?.[prim.material]?.name ?? `материал_${prim.material}`;
            if (!materials.includes(mat)) materials.push(mat);
          }

          const box = positionBox(json, bin, dir, prim.attributes?.POSITION);
          if (box === null) {
            unmeasured += 1;
            continue;
          }
          for (let cx = 0; cx < 2; cx++) {
            for (let cy = 0; cy < 2; cy++) {
              for (let cz = 0; cz < 2; cz++) {
                const p = applyMat(world, [
                  cx ? box.max[0] : box.min[0],
                  cy ? box.max[1] : box.min[1],
                  cz ? box.max[2] : box.min[2],
                ]);
                for (let k = 0; k < 3; k++) {
                  if (p[k] < min[k]) min[k] = p[k];
                  if (p[k] > max[k]) max[k] = p[k];
                }
              }
            }
          }
        }
      }
    }

    for (const c of node.children ?? []) walk(c, world);
  };

  const identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
  for (const r of roots) walk(r, identity);

  if (!Number.isFinite(min[0])) {
    return { ok: false, why: 'в файле нет ни одной измеримой поверхности' };
  }

  const size = [max[0] - min[0], max[1] - min[1], max[2] - min[2]];
  const center = [(max[0] + min[0]) / 2, (max[1] + min[1]) / 2, (max[2] + min[2]) / 2];

  const textures = (json.images ?? []).map((i) => i.uri ?? '(встроенная)');

  return {
    ok: true,
    file: basename(path),
    size,
    min,
    max,
    center,
    // Пивот в glTF — начало координат сцены: узлы уже приведены к нему обходом.
    pivot_offset: center,
    cells: [size[0] / GRID, size[2] / GRID],
    tris,
    meshes,
    submeshes,
    file_scale: 1,
    materials,
    textures,
    unmeasured,
  };
}

/**
 * Правила посадки.
 *
 * Три уровня, и они не равны по смыслу. `стоп` — модель нельзя ставить в
 * сцену вообще. `правка` — встанет, но будет читаться неверно. `заметка` —
 * повод посмотреть глазами, а не обязанность что-то менять.
 */
function verdict(m) {
  const notes = [];
  const say = (level, text) => notes.push({ level, text });

  if (!m.ok) {
    say('стоп', m.why ?? 'модель не измерена');
    return notes;
  }

  const [sx, sy, sz] = m.size;
  const biggest = Math.max(sx, sy, sz);

  if (biggest < 0.2) {
    say('стоп', `габарит ${fmt(biggest)} — модель в чужих единицах, похоже на сантиметры. Экспортируй в метрах.`);
  } else if (biggest > 24) {
    say('стоп', `габарит ${fmt(biggest)} — это двенадцать клеток, модель в чужих единицах. Экспортируй в метрах.`);
  }

  const base = m.min[1];
  if (Math.abs(base) > 0.01) {
    const where = base > 0 ? 'повиснет над грунтом' : 'утонет в грунте';
    say('правка', `основание не в нуле: y = ${fmt(base)}. В сцене модель ${where} на ${Math.round(Math.abs(base) * 100)} см. Сдвинь геометрию так, чтобы низ лежал ровно в нуле.`);
  }

  // Порог смещения пивота — ДОЛЯ от габарита, а не абсолютные сантиметры.
  // Первая редакция ставила жесткие 5 см и краснела на `basemodule_A` из
  // самого набора (смещение 0.052 и 0.088 при габарите 2.25) — то есть
  // объявляла дефектной эталонную модель. Проверка, которая заворачивает
  // образец, не проверка: ее выключат на второй присланной модели.
  for (const [axis, off, size] of [
    ['X', m.pivot_offset[0], sx],
    ['Z', m.pivot_offset[2], sz],
  ]) {
    const limit = Math.max(0.08, size * 0.06);
    if (Math.abs(off) > limit) {
      say('правка', `пивот смещен по ${axis} на ${fmt(off)} при габарите ${fmt(size)} — это ${Math.round((Math.abs(off) / size) * 100)}% размера. Раскладка ставит объекты по центру клетки, и такая модель встанет мимо своей.`);
    }
  }

  // Свес за клетку — не дефект сам по себе: у половины моделей набора габарит
  // больше 2.0 из-за козырьков и выступов (basemodule_D — 2.444). Но раскладка
  // ставит объекты через 2.0, и знать про свес надо ДО того, как соседи
  // окажутся вплотную. Поэтому заметка с числом, а не запрет.
  for (const [axis, value] of [['X', sx], ['Z', sz]]) {
    const reserved = Math.max(0.5, Math.round((value / GRID) * 2) / 2);
    const over = value - reserved * GRID;
    if (over > 0.12) {
      say('заметка', `по ${axis} модель шире своей клетки на ${Math.round(over * 100)} см (${fmt(value)} при ${reserved} клетки). Соседний объект встанет вплотную или внахлест.`);
    } else if (over < -0.5) {
      say('заметка', `по ${axis} модель уже своей клетки на ${Math.round(-over * 100)} см (${fmt(value)} при ${reserved} клетки). Между ней и соседями останется дыра.`);
    }
  }

  if (m.tris > 12000) {
    say('стоп', `${m.tris} треугольников. Набор держится на низкополигональных моделях в двести-две тысячи, и веб-плеер платит за каждую. Упрости.`);
  } else if (m.tris > 4000) {
    say('правка', `${m.tris} треугольников — втрое тяжелее самой сложной модели набора. Проверь, не осталось ли фаски и подразделения от моделирования.`);
  }

  if (m.materials.length > 6) {
    say('правка', `${m.materials.length} материалов. Весь набор Kenney обходится одиннадцатью на сто пятьдесят три модели, по три-четыре на объект.`);
  }

  const foreign = m.materials.filter((x) => !PALETTE.includes(x));
  if (foreign.length > 0 && m.materials.length > 0) {
    say('заметка', `имена материалов не из палитры набора: ${foreign.join(', ')}. Модель встанет, но прочитается пришлой. Палитра: ${PALETTE.join(', ')}.`);
  }

  const own = (m.textures ?? []).filter((t) => basename(t) !== KIT_ATLAS);
  if (own.length > 0) {
    say('заметка', `модель несет свою текстуру (${own.join(', ')}). В кадре уже два способа покраски: атлас ${KIT_ATLAS} и плоские цвета. Третий читается склейкой. Сядь либо на атлас, либо на палитру.`);
  }

  if (m.unmeasured > 0) {
    say('заметка', `${m.unmeasured} поверхностей не измерено: у них нет габарита в заголовке и не читаются вершины. Габарит ниже посчитан по остальным.`);
  }

  return notes;
}

function fmt(v) {
  return Number(v).toFixed(3);
}


// ---- прогон -----------------------------------------------------------------

const measured = measureGltf(source);
const notes = verdict(measured);
const stops = notes.filter((n) => n.level === 'стоп');
const fixes = notes.filter((n) => n.level === 'правка');
const hints = notes.filter((n) => n.level === 'заметка');

console.log('');
console.log(`### ${name}${measured.ok ? '' : ' — НЕ ИЗМЕРЕНА'}`);
console.log('');

if (measured.ok) {
  const [sx, sy, sz] = measured.size;
  console.log(`габарит       ${fmt(sx)} x ${fmt(sy)} x ${fmt(sz)}`);
  console.log(`в клетках 2.0 ${measured.cells[0].toFixed(2)} x ${measured.cells[1].toFixed(2)}`);
  console.log(`основание     y = ${fmt(measured.min[1])}`);
  console.log(`пивот от цент x = ${fmt(measured.pivot_offset[0])}, z = ${fmt(measured.pivot_offset[2])}`);
  console.log(`треугольники  ${measured.tris}`);
  console.log(`сетки/подсети ${measured.meshes} / ${measured.submeshes}`);
  console.log(`материалы     ${measured.materials.length > 0 ? measured.materials.join(', ') : '(нет имен)'}`);
  console.log(`текстуры      ${(measured.textures ?? []).length > 0 ? measured.textures.join(', ') : 'нет, плоские цвета'}`);
  console.log('');
}

for (const n of [...stops, ...fixes, ...hints]) console.log(`[${n.level}] ${n.text}`);

console.log('');
if (stops.length > 0) console.log('ВЕРДИКТ: ставить нельзя, сначала правка выше.');
else if (fixes.length > 0) console.log(`ВЕРДИКТ: встанет, но ${fixes.length === 1 ? 'одну вещь' : `${fixes.length} вещи`} надо поправить.`);
else console.log('ВЕРДИКТ: встает как есть.');

const report = join(dirname(source), `${name}-intake.json`);
writeFileSync(report, `${JSON.stringify({ name, source, measured, notes }, null, 2)}
`);
console.log(`числа записаны: ${report}`);

process.exit(stops.length > 0 ? 1 : 0);
