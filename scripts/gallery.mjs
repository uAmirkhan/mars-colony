/**
 * Галерея вырезанных ассетов: одна страница со всем, что дала разрезалка.
 *
 * Нужна потому, что проверять сто с лишним файлов по одному невозможно, а
 * ошибки резки видны только глазами: слипшиеся объекты, обрезанные края,
 * зеленый ореол, подписи, уехавшие в кадр вместе со зданием.
 *
 * Переключатель фона обязателен: зеленая кайма по кромке не видна на темном
 * и сразу бросается в глаза на светлом.
 *
 * Запуск: node scripts/gallery.mjs [папка] [выход.html]
 */

import { readdirSync, statSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const root = process.argv[2] ?? 'design/cut';
const out = process.argv[3] ?? 'cut-gallery.html';

const STYLE = `
:root { color-scheme: dark; }
body { margin:0; padding:24px 26px 60px; background:#7a4227;
       font:14px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif; color:#ffeede; }
h1 { font-size:20px; margin:0 0 4px; }
.sub { opacity:.7; margin:0 0 18px; font-size:13px; }
h2 { font-size:13px; text-transform:uppercase; letter-spacing:.05em; opacity:.85;
     margin:26px 0 8px; border-bottom:1px solid rgba(255,230,200,.2); padding-bottom:5px; }
.row { display:flex; flex-wrap:wrap; gap:10px; align-items:flex-end; }
.c { background:rgba(0,0,0,.18); border:1px solid rgba(255,230,200,.15);
     border-radius:10px; padding:6px; text-align:center; }
.c img { display:block; width:150px; height:150px; object-fit:contain; }
.c span { font-size:10px; opacity:.55; }
button { position:fixed; right:16px; top:14px; z-index:5; background:rgba(0,0,0,.5);
         color:#ffeede; border:1px solid rgba(255,230,200,.3); border-radius:9px;
         padding:7px 13px; cursor:pointer; font:inherit; font-size:12px; }
body.plain { background:#ced3d9; color:#232830; }
body.plain .c { background:rgba(255,255,255,.62); border-color:rgba(0,0,0,.13); }
`;

const dirs = readdirSync(root)
  .filter((d) => statSync(join(root, d)).isDirectory())
  .sort();

const parts = [];
let total = 0;

for (const dir of dirs) {
  const files = readdirSync(join(root, dir))
    .filter((f) => f.endsWith('.png'))
    .sort();
  if (files.length === 0) continue;
  total += files.length;

  const cards = files
    .map(
      (f) =>
        `<div class="c"><img src="${root}/${dir}/${f}" alt=""><span>${f}</span></div>`,
    )
    .join('');
  parts.push(`<h2>${dir} — ${files.length}</h2><div class="row">${cards}</div>`);
}

const html = `<!doctype html>
<html lang="ru"><head><meta charset="utf-8">
<title>Вырезанные ассеты</title><style>${STYLE}</style></head>
<body>
<button onclick="document.body.classList.toggle('plain')">Сменить фон</button>
<h1>Вырезанные ассеты</h1>
<p class="sub">Всего ${total} объектов. Кнопка справа меняет фон — на светлом видно зеленую кайму по кромке, на темном она незаметна.</p>
${parts.join('\n')}
</body></html>`;

writeFileSync(out, html);
console.log(`${out}: ${total} объектов из ${dirs.length} листов`);
