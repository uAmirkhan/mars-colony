/**
 * Отдает собранную игру статикой.
 *
 * Зачем отдельно от `vite dev`: dev-сервер поднимается на IPv6 и из части
 * окружений не открывается, а проверить это изнутри песочницы нельзя. Статика
 * поднимается тем же способом, каким уже работают обе сборки на движке, и
 * ведет себя одинаково везде. Заодно это ровно то, что уедет на хостинг.
 *
 * Запуск: npm run build && node serve-dist.mjs
 */

import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { extname, join, normalize } from 'node:path';

const ROOT = 'dist';
const PORT = 8082;

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'application/javascript',
  '.css': 'text/css',
  '.json': 'application/json',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.svg': 'image/svg+xml',
  '.woff2': 'font/woff2',
  '.ico': 'image/x-icon',
};

if (!existsSync(ROOT)) {
  console.error(`нет папки ${ROOT}: сначала npm run build`);
  process.exit(2);
}

const server = createServer(async (req, res) => {
  const url = decodeURIComponent((req.url ?? '/').split('?')[0]);
  const rel = normalize(url === '/' ? '/index.html' : url).replace(/^([/\\])+/, '');
  try {
    const body = await readFile(join(ROOT, rel));
    res.writeHead(200, { 'Content-Type': MIME[extname(rel)] ?? 'application/octet-stream' });
    res.end(body);
  } catch {
    // Одностраничное приложение: неизвестный путь отдаем índex.html, иначе
    // обновление страницы на внутреннем маршруте дает 404 на пустом месте.
    try {
      const body = await readFile(join(ROOT, 'index.html'));
      res.writeHead(200, { 'Content-Type': MIME['.html'] });
      res.end(body);
    } catch {
      res.writeHead(404).end('not found');
    }
  }
});

server.on('error', (err) => {
  if (err.code === 'EADDRINUSE') {
    console.log(`порт ${PORT} занят — уже поднят другой сервер`);
    return;
  }
  throw err;
});

// Слушаем на всех адресах: dev-сервер садился только на IPv6, и из части
// окружений страница не открывалась при живом процессе.
server.listen(PORT, '0.0.0.0', () => {
  console.log(`  http://localhost:${PORT}/   игра: полная петля кликами`);
});
