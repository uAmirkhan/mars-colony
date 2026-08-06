import { expect, type Page, test } from '@playwright/test';

/**
 * Первые тридцать секунд: понял ли человек, куда нажать.
 *
 * Проверка ходит по игре ВСЛЕПУЮ. Она не трогает `window.__game`, не ставит
 * состояние и вообще ничего не знает про игру, кроме того, что видно на
 * экране: нашла плашку цели, прочитала, куда та показывает, нажала ровно туда
 * и посмотрела, изменился ли экран. Отладочный шов здесь запрещен по существу
 * — он ставит состояние, которого у открывшего ссылку нет, и проверка перестает
 * отвечать на свой единственный вопрос.
 *
 * Красной она становится от трех вещей: подсказки нет, подсказка указывает не
 * туда, по указанному месту нечего нажать.
 */

/** Подсказка обязана читаться взглядом. Абзац здесь означает, что UI не объяснился. */
const MAX_WORDS = 8;

/** Сколько ждем появления цели. Тридцать секунд ТЗ — про человека, не про машину. */
const GOAL_APPEARS_MS = 5000;

async function openLink(page: Page, url = '/') {
  await page.goto(url);
  await page.getByRole('button', { name: 'Играть' }).click();
}

/** Единственный указатель на экране: элемент с кольцом цели. */
function pointers(page: Page) {
  return page.locator('.goal-point');
}

test('показ: первая цель ведет к шаттлу и доводит до груза', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));

  const opened_at = Date.now();
  await openLink(page);

  const bar = page.getByTestId('goal-bar');
  await expect(bar, 'на первом экране показа обязана быть цель').toBeVisible({
    timeout: GOAL_APPEARS_MS,
  });

  // Время до первого осмысленного действия: от открытия ссылки до момента,
  // когда на экране есть цель и по ней можно нажать. Число уходит в отчет.
  const ready_at = Date.now();
  console.log(`время до первой цели: ${ready_at - opened_at} мс`);

  const words = (await bar.getByTestId('goal-open').textContent()) ?? '';
  const line = (await bar.locator('.goal-bar-text').textContent()) ?? '';
  expect(
    `${line} ${words}`.trim().split(/\s+/).length,
    'подсказка длиннее строки — это уже не подсказка',
  ).toBeLessThanOrEqual(MAX_WORDS);

  // Цель показа — шаттл: он центральная вещь среза и он на подлете.
  await expect(bar, 'цель показа обязана вести к шаттлу').toHaveAttribute(
    'data-goal-hub',
    'shuttle',
  );

  // Указатель стоит ровно на одной кнопке, и это кнопка «Шаттл». Проверяется
  // не наличие подсветки, а ее адрес: подсказка, показывающая не туда, хуже
  // отсутствующей.
  await expect(pointers(page), 'указатель на экране должен быть один').toHaveCount(1);
  await expect(pointers(page).first()).toHaveText('Шаттл');

  // Нажимаем ровно туда, куда зовет игра.
  await bar.getByTestId('goal-open').click();
  const station = page.getByText('Орбитальная станция');
  await expect(station, 'кнопка цели обязана открыть станцию').toBeVisible();

  // Внутри станции указатель ведет дальше сам: ускорить рейс.
  const skip = pointers(page).first();
  await expect(skip, 'в станции указатель обязан стоять на следующем действии').toBeVisible();
  await expect(skip).toContainText('Ускорить');
  await skip.click();
  // Между указателем и результатом теперь стоит подтверждение траты изотопов
  // (каркас раздел 13). Цепь целей от этого не рвется: подтверждение — часть
  // того же действия, а не новый шаг, и указателя на нем нет.
  await page.getByRole('button', { name: /^Потратить \d+/ }).click();

  // Отклик: рейс прибыл, на экране контейнеры и указатель на них.
  const collect = pointers(page).first();
  await expect(collect, 'после ускорения указатель ведет к грузу').toContainText('Собрать все');
  await collect.click();

  // Награда пришла: модули с рейса. Читаем по экрану, а не по стору.
  await expect(page.getByTestId('toast-reward').first()).toBeVisible();

  await page.getByRole('button', { name: 'Закрыть' }).first().click();

  // Цепь идет дальше: модули на складе — значит открылась стройка.
  await expect(page.getByTestId('goal-bar')).toHaveAttribute('data-goal-hub', 'construction');

  expect(errors, 'первый экран не имеет права ронять страницу').toEqual([]);
});

test('показ: цель кончается, а не сопровождает игру вечно', async ({ page }) => {
  await openLink(page);

  const bar = page.getByTestId('goal-bar');
  await expect(bar).toBeVisible({ timeout: GOAL_APPEARS_MS });

  // Крестик — единственная кнопка, которой человек говорит «дальше сам».
  await bar.getByRole('button', { name: 'Скрыть подсказку' }).click();
  await expect(bar, 'скрытая подсказка не возвращается').toHaveCount(0);
  await expect(pointers(page), 'вместе с подсказкой уходят и кольца').toHaveCount(0);
});

test('канонический старт: первая цель — посеять', async ({ page }) => {
  await openLink(page, '/?fresh=1');

  const bar = page.getByTestId('goal-bar');
  await expect(bar, 'с нуля цель тоже обязана быть').toBeVisible({ timeout: GOAL_APPEARS_MS });
  await expect(bar).toHaveAttribute('data-goal', 'plant');

  // Цель в куполе: кнопки в плашке нет, показывает стрелка на самой грядке.
  await expect(bar.getByTestId('goal-open'), 'цель купола не дублируется кнопкой').toHaveCount(
    0,
  );
  const field = pointers(page).first();
  await expect(field, 'стрелка обязана стоять на грядке').toBeVisible();

  // Нажимаем туда, куда показывает стрелка, и сеем первое, что предложили.
  await field.click();
  await expect(page.getByText('Что посадить')).toBeVisible();
  await page
    .getByRole('button', { name: /^Посеять/ })
    .first()
    .click();

  // Цель сменилась сама: посеяно, дальше ждем урожай.
  await expect(page.getByTestId('goal-bar')).toHaveAttribute('data-goal', 'grow');
});

test('ни одна кнопка первого экрана не накрыта плашкой цели', async ({ page }) => {
  await openLink(page);
  await expect(page.getByTestId('goal-bar')).toBeVisible({ timeout: GOAL_APPEARS_MS });

  // Плашка цели встала внизу экрана, где уже стоял хаб, а над ней грядки с
  // кнопками ускорения. Спрашиваем у браузера, кто получит касание.
  const covered = await page.evaluate(() => {
    const out: string[] = [];
    for (const el of document.querySelectorAll('button')) {
      const r = el.getBoundingClientRect();
      if (r.width === 0 || r.height === 0) continue;
      const x = r.left + r.width / 2;
      const y = r.top + r.height / 2;
      if (x < 0 || y < 0 || x > window.innerWidth || y > window.innerHeight) continue;
      const hit = document.elementFromPoint(x, y);
      if (hit === null || el.contains(hit) || hit.contains(el)) continue;
      out.push(`«${(el.textContent ?? '').trim().slice(0, 20)}» накрыта «${hit.className}»`);
    }
    return out;
  });
  expect(covered, 'на первом экране показа').toEqual([]);
});
