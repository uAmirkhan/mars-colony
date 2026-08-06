import { expect, type Page, test } from '@playwright/test';

/**
 * Находки судьи по механике шаттла (run-4/spec-review.md, вычет 45).
 *
 * Н-3. Кнопка «Докупить» рисуется на отсеке, который полностью покрыт
 * складом. ТЗ шаттла 6.2 для состояния `covered_by_stock` (stock >= qty)
 * прямо запрещает докупку: «Докупки нет — незачем». Окно отсека рисовало
 * пару кнопок «Погрузить»/«Докупить» для любого незакрытого отсека, не
 * сверяясь с тем, хватает ли склада закрыть его целиком.
 *
 * Н-8. Счетчик отсека «есть {stock} / нужно {qty}» (ТЗ 6.2, композиция
 * bottom sheet) обязан показывать полное требование отсека, а не остаток
 * после уже погруженного. Код считал «нужно» как `qty_required - qty_filled`
 * — после частичной погрузки счетчик врал о размере отсека.
 */

type Store = {
  getState: () => Record<string, unknown>;
  setState: (patch: Record<string, unknown>) => void;
};

async function openGame(page: Page) {
  await page.goto('/?fresh=1');
  await page.getByRole('button', { name: 'Играть' }).click();
}

test('Н-3: отсек, покрытый складом целиком, не предлагает докупку', async ({ page }) => {
  await openGame(page);

  await page.evaluate(() => {
    const store = (window as unknown as { __game: Store }).__game;
    const now = Math.floor(Date.now() / 1000);

    store.setState({
      level: 9,
      credits: 100_000,
      isotopes: 100_000,
      // На складе десять водорослей, отсеку нужно пять — стока хватает
      // целиком, докупать нечего (covered_by_stock, ТЗ 6.2).
      warehouse: { capacity: 500, cells: { algae: { qty: 10, reserved: 0 } } },
      shuttle_arrivals: 1,
      shuttle: {
        state: 'ORDER',
        slots: [
          {
            idx: 0,
            good_id: 'algae',
            qty_required: 5,
            qty_filled: 0,
            qty_purchased: 0,
            filled_by: null,
            reward: null,
            collected: false,
            floor_forced: false,
          },
          {
            idx: 1,
            good_id: 'soy',
            qty_required: 4,
            qty_filled: 0,
            qty_purchased: 0,
            filled_by: null,
            reward: null,
            collected: false,
            floor_forced: false,
          },
        ],
        trip_min: 75,
        departed_at: 0,
        arrives_at: 0,
        cooldown_until: 0,
        is_first_trip: false,
        arrival_no: 2,
      },
      now,
    });
  });

  await page.getByRole('button', { name: 'Шаттл' }).click();
  const station = page.locator('.panel').filter({ hasText: 'Орбитальная станция' });
  await station.locator('.slot').nth(0).click();

  await expect(page.getByRole('button', { name: /^Погрузить/ }), 'бесплатный путь остается').toBeVisible();
  await expect(
    page.getByRole('button', { name: /^Докупить/ }),
    'ТЗ 6.2: covered_by_stock — докупки нет, незачем',
  ).toHaveCount(0);
});

test('Н-8: счетчик отсека показывает полное требование, а не остаток', async ({ page }) => {
  await openGame(page);

  await page.evaluate(() => {
    const store = (window as unknown as { __game: Store }).__game;
    const now = Math.floor(Date.now() / 1000);

    store.setState({
      level: 9,
      credits: 100_000,
      isotopes: 100_000,
      // Две единицы уже погружены (частичная погрузка, ТЗ 6.2), на складе
      // еще три доступных.
      warehouse: { capacity: 500, cells: { algae: { qty: 3, reserved: 0 } } },
      shuttle_arrivals: 1,
      shuttle: {
        state: 'ORDER',
        slots: [
          {
            idx: 0,
            good_id: 'algae',
            qty_required: 5,
            qty_filled: 2,
            qty_purchased: 0,
            filled_by: 'self',
            reward: null,
            collected: false,
            floor_forced: false,
          },
          {
            idx: 1,
            good_id: 'soy',
            qty_required: 4,
            qty_filled: 0,
            qty_purchased: 0,
            filled_by: null,
            reward: null,
            collected: false,
            floor_forced: false,
          },
        ],
        trip_min: 75,
        departed_at: 0,
        arrives_at: 0,
        cooldown_until: 0,
        is_first_trip: false,
        arrival_no: 2,
      },
      now,
    });
  });

  await page.getByRole('button', { name: 'Шаттл' }).click();
  const station = page.locator('.panel').filter({ hasText: 'Орбитальная станция' });
  await station.locator('.slot').nth(0).click();

  const counter = page.getByText(/есть \d+ \/ нужно \d+/);
  await expect(counter).toBeVisible();

  // Отсеку нужно 5 по условию заказа — независимо от того, что 2 уже
  // погружены. ТЗ 6.2 требует «нужно {qty}» по отсеку, не остаток.
  await expect(counter, 'счетчик обязан показывать qty_required, а не остаток').toHaveText(
    /есть 3 \/ нужно 5/,
  );
});
