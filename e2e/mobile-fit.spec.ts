import { expect, type Page, test } from '@playwright/test';

/**
 * Ни одна кнопка не имеет права стоять за краем экрана.
 *
 * Дефект Д-23 нашелся случайно: проверка ощущения не смогла нажать «Склад» на
 * телефоне, потому что кнопка стояла на координате x = -149 при ширине экрана
 * 412. Хаб из шести кнопок не влезал в строку, а блок не переносился — левые
 * кнопки уезжали за край, и склад с телефона не открывался вовсе.
 *
 * Почему это не поймали раньше: браузерные проверки нажимают через локатор, а
 * Playwright перед кликом сам прокручивает элемент в видимую область. Для
 * машины кнопка была доступна, для пальца — нет. Поэтому проверка меряет
 * геометрию, а не кликабельность.
 *
 * Проверка идет на обоих экранах: узкий ловит переполнение, широкий держит
 * гарантию, что лечение узкого не сломало обычный вид.
 */

/** Полтора пикселя списываем на округление раскладки, больше — уже за краем. */
const EDGE_TOLERANCE = 1.5;

interface Offender {
  text: string;
  x: number;
  right: number;
  width: number;
}

async function offscreenButtons(page: Page): Promise<Offender[]> {
  return page.evaluate((tolerance) => {
    const out: Offender[] = [];
    for (const el of document.querySelectorAll('button')) {
      const r = el.getBoundingClientRect();
      // Скрытое и схлопнутое не проверяем: это не «за краем», это «не на экране».
      if (r.width === 0 || r.height === 0) continue;
      if (getComputedStyle(el).visibility === 'hidden') continue;
      if (r.left < -tolerance || r.right > window.innerWidth + tolerance) {
        out.push({
          text: (el.textContent ?? '').trim().slice(0, 24),
          x: Math.round(r.left),
          right: Math.round(r.right),
          width: Math.round(r.width),
        });
      }
    }
    return out;
  }, EDGE_TOLERANCE);
}

/**
 * Кнопки, накрытые чем-то другим.
 *
 * Проверка «помещается в экран» этого не ловит: элемент может лежать целиком
 * внутри экрана и при этом быть недоступен пальцу, потому что поверх него
 * стоит другой. Ровно так выключатель звука накрыл кнопку «Играть» на узком
 * экране — главный вход в игру был не нажимаем, а все проверки зеленые.
 *
 * Спрашиваем у браузера, кто на самом деле получит касание в центре кнопки.
 * Свой же потомок считается своим: у кнопки внутри есть текст и иконки.
 */
async function coveredButtons(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const out: string[] = [];
    for (const el of document.querySelectorAll('button')) {
      const r = el.getBoundingClientRect();
      if (r.width === 0 || r.height === 0) continue;
      const x = r.left + r.width / 2;
      const y = r.top + r.height / 2;
      if (x < 0 || y < 0 || x > window.innerWidth || y > window.innerHeight) continue;
      const hit = document.elementFromPoint(x, y);
      if (hit === null || el.contains(hit) || hit.contains(el)) continue;
      const label = (el.textContent ?? '').trim().slice(0, 24);
      const over = (hit.textContent ?? '').trim().slice(0, 24);
      out.push(`«${label}» накрыта «${over}» (${hit.className || hit.tagName})`);
    }
    return out;
  });
}

async function openGame(page: Page) {
  await page.goto('/?fresh=1');
  await page.getByRole('button', { name: 'Играть' }).click();
}

test('кнопки игры помещаются в экран', async ({ page }) => {
  await openGame(page);
  await expect(page.getByRole('button', { name: 'Склад' })).toBeVisible();

  const bad = await offscreenButtons(page);
  expect(bad, `кнопки за краем экрана: ${JSON.stringify(bad)}`).toEqual([]);
});

test('ни одна кнопка не накрыта другой', async ({ page }) => {
  // Первый экран проверяется до входа в игру: именно там висит «Играть», и
  // именно ее накрывал выключатель звука.
  await page.goto('/?fresh=1');
  await expect(page.getByRole('button', { name: 'Играть' })).toBeVisible();
  expect(await coveredButtons(page), 'на первом экране').toEqual([]);

  await page.getByRole('button', { name: 'Играть' }).click();
  await expect(page.getByRole('button', { name: 'Склад' })).toBeVisible();
  expect(await coveredButtons(page), 'в игре').toEqual([]);
});

test('кнопки каждой панели помещаются в экран', async ({ page }) => {
  await openGame(page);

  // Панели проверяются по очереди: у каждой своя раскладка, и переполнение
  // одной ничего не говорит про остальные.
  for (const name of ['Склад', 'Фабрика', 'Дрон', 'Шаттл', 'Стройка']) {
    await page.getByRole('button', { name, exact: true }).click();
    const bad = await offscreenButtons(page);
    expect(bad, `панель «${name}»: кнопки за краем ${JSON.stringify(bad)}`).toEqual([]);
    await page.getByRole('button', { name: 'Закрыть' }).first().click();
  }
});
