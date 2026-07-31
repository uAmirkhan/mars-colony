import { expect, type Page, test } from '@playwright/test';

/**
 * Замкнутая петля в браузере: погрузил отсеки → шаттл ушел → прибыл →
 * контейнеры дали модули → модули ушли в стройку → склад вырос.
 *
 * Проверка нужна именно браузерная. Доменные тесты уже гоняют ту же логику
 * четырьмя сотнями кейсов, но однажды в этом проекте приложение не
 * отрисовывалось вообще при ста сорока зеленых тестах: селекторы стора
 * возвращали новые объекты, и перерисовка зациклилась. Модульный тест такого
 * не видит по устройству — он не рендерит.
 *
 * Состояние ставится через тестовый шов `window.__game`: рейс шаттла длится
 * час, живым кликом его в e2e не пройти.
 */

async function openGame(page: Page) {
  await page.goto('/');
  await page.getByRole('button', { name: 'Играть' }).click();
}

/** Игрок шестого уровня с полным складом сырья и запасом изотопов. */
async function seedPlayer(page: Page) {
  await page.evaluate(() => {
    const store = (window as unknown as { __game: { getState: () => Record<string, any> } })
      .__game;
    const state = store.getState();
    const warehouse = { ...state.warehouse, capacity: 500, cells: {} as Record<string, any> };
    for (const id of ['algae', 'soy', 'mushrooms', 'tomatoes', 'cotton']) {
      warehouse.cells[id] = { good_id: id, qty: 60, reserved: 0 };
    }
    (store as unknown as { setState: (p: unknown) => void }).setState({
      level: 6,
      credits: 100_000,
      isotopes: 100_000,
      warehouse,
      shuttle: null,
      shuttle_arrivals: 0,
    });
  });
}

test('петля шаттла замыкается: погрузка, рейс, прибытие, стройка', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));

  await openGame(page);
  await seedPlayer(page);

  // Тик стора генерирует рейс: ждем, пока станция перестанет быть заглушкой.
  await page.getByRole('button', { name: 'Шаттл' }).click();
  await expect(page.getByText(/Погрузка: 0\/\d+ отсеков/)).toBeVisible();

  // Грузим отсеки по одному, пока рейс не стартует сам. Кнопки «Отправить»
  // в интерфейсе нет и быть не должно — это проверяется отдельно ниже.
  const slot_count = await page.evaluate(() => {
    const store = (window as unknown as { __game: { getState: () => any } }).__game;
    return store.getState().shuttle.slots.length as number;
  });

  // Область обязательно сужаем до панели станции: класс `.slot` носят и
  // грядки купола, которые остаются в DOM за затемнением.
  const station = page.locator('.panel').filter({ hasText: 'Орбитальная станция' });
  for (let i = 0; i < slot_count; i++) {
    // Именно i-й, а не первый: первый после погрузки остается на месте, и
    // цикл бесконечно открывал бы уже закрытый отсек.
    await station.locator('.slot').nth(i).click();
    await page.getByRole('button', { name: /^Погрузить/ }).click();
  }

  // Рейс ушел сам по закрытию последнего отсека.
  await expect(page.getByText('В пути')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Отправить' })).toHaveCount(0);

  // Ускоряем: цена стоит на самой кнопке, а не отдельной строкой.
  await page.getByRole('button', { name: /Ускорить за \d+/ }).click();

  // Прибытие: контейнеров ровно столько же, сколько было отсеков.
  await expect(page.getByRole('button', { name: 'Собрать все' })).toBeVisible();
  await page.getByRole('button', { name: 'Собрать все' }).click();

  // Модули зачислены на склад стройки.
  const stock = await page.evaluate(() => {
    const store = (window as unknown as { __game: { getState: () => any } }).__game;
    const s = store.getState();
    return Object.values(s.construction.stock as Record<string, number>).reduce(
      (a, b) => a + b,
      0,
    );
  });
  expect(stock, 'каждый отсек обязан привезти ровно один модуль').toBe(slot_count);

  expect(errors, 'страница не должна падать за всю петлю').toEqual([]);
});

test('стройка расширяет склад и это видно в HUD', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));

  await openGame(page);
  await page.evaluate(() => {
    const store = (window as unknown as { __game: { getState: () => any } }).__game;
    const s = store.getState();
    (store as unknown as { setState: (p: unknown) => void }).setState({
      level: 6,
      isotopes: 100_000,
      construction: {
        ...s.construction,
        stock: { filter: 6, cable: 6, sealant: 6 },
      },
    });
  });

  const cap_before = await page.locator('.currency').last().innerText();

  await page.getByRole('button', { name: 'Стройка' }).click();
  await expect(page.getByText(/Склад модулей: 18 из/)).toBeVisible();

  await page.getByRole('button', { name: 'Строить' }).click();
  await page.getByRole('button', { name: /Ускорить за \d+/ }).click();

  // Емкость склада выросла — конверсионный узел петли отработал.
  await expect(page.locator('.currency').last()).not.toHaveText(cap_before);
  expect(errors).toEqual([]);
});

test('до пятого уровня станция закрыта и не притворяется рабочей', async ({ page }) => {
  await openGame(page);
  await page.getByRole('button', { name: 'Шаттл' }).click();
  await expect(page.getByText(/Шаттл открывается на пятом уровне/)).toBeVisible();
});
