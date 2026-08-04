import { expect, type Page, test } from '@playwright/test';

/**
 * Два дефекта интерфейса, которых модульный тест не видит по устройству: он
 * не рендерит, а оба живут в верстке.
 *
 * Д-10. Счетчик «есть N» считает `cells[good].qty`, то есть СЫРОЕ количество,
 * включая зарезервированное под другие заказы. Каркас (раздел 9) разводит эти
 * величины прямо: «`Warehouse.reserved` отделен от `qty` — товар, положенный в
 * слот, списывается с доступного немедленно». Игрок видит «есть 9 / нужно 5»,
 * жмет активную кнопку «Погрузить» и получает «Нет на складе»: домен считает
 * по `available`, интерфейс — по `qty`.
 *
 * Д-11. Кнопка «Докупить N за P ⬡» на доске дрона вызывает `loadOrderPosition`
 * — тот же обработчик, что и «Погрузить». Докупки у дрона нет ни в сторе, ни в
 * домене: изотопы не списываются, позиция не закрывается. Кнопка с ценой,
 * которая ничего не покупает, — по правилу каркаса про цену на кнопке это баг,
 * а не заглушка.
 *
 * Состояние ставится через тестовый шов `window.__game`: собрать нужную
 * комбинацию склада и резерва живыми кликами нельзя.
 */

type Store = {
  getState: () => Record<string, unknown>;
  setState: (patch: Record<string, unknown>) => void;
};

async function openGame(page: Page) {
  await page.goto('/');
  await page.getByRole('button', { name: 'Играть' }).click();
}

test('Д-10: счетчик отсека шаттла не выдает зарезервированное за доступное', async ({
  page,
}) => {
  await openGame(page);

  await page.evaluate(() => {
    const store = (window as unknown as { __game: Store }).__game;
    const now = Math.floor(Date.now() / 1000);

    store.setState({
      level: 9,
      credits: 100_000,
      isotopes: 100_000,
      // Девять водорослей есть физически, но все девять уже лежат в чужом
      // слоте: доступного — ноль.
      warehouse: { capacity: 500, cells: { algae: { qty: 9, reserved: 9 } } },
      shuttle_arrivals: 1,
      shuttle: {
        state: 'ORDER',
        slots: [
          {
            idx: 0,
            good_id: 'algae',
            qty_required: 5,
            qty_filled: 0,
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

  // Лист отсека открылся — иначе тест падал бы не по делу.
  const counter = page.getByText(/есть \d+ \/ нужно \d+/);
  await expect(counter).toBeVisible();

  // Доступного нуль — счетчик обязан показывать нуль.
  await expect(counter, 'счетчик показывает qty вместо qty - reserved').toHaveText(
    /есть 0 \/ нужно 5/,
  );

  // ТЗ шаттла 6.2, состояние `empty` (stock == 0): «Погрузить» задизейблена с
  // подписью «Нет на складе». Прежняя редакция теста фиксировала здесь ровно
  // симптом дефекта — активную кнопку, — и после починки счетчика она обязана
  // была перевернуться: интерфейс не должен обещать погрузку, которой нет.
  const load = page.getByRole('button', { name: /^Погрузить/ });
  await expect(load).toBeDisabled();
  await expect(load).toHaveAttribute('title', 'Нет на складе');
});

test('Д-11: кнопка «Докупить» на доске дрона списывает изотопы и закрывает позицию', async ({
  page,
}) => {
  await openGame(page);

  await page.evaluate(() => {
    const store = (window as unknown as { __game: Store }).__game;
    const now = Math.floor(Date.now() / 1000);

    store.setState({
      level: 9,
      credits: 100_000,
      isotopes: 100_000,
      warehouse: { capacity: 500, cells: {} },
      orders: [
        {
          idx: 0,
          state: 'active',
          npc_name: 'Ирина, гидропоника',
          positions: [{ good_id: 'tomatoes', qty: 5, filled: false }],
          credits_reward: 40,
          xp_reward: 10,
          refresh_at: now + 100_000,
        },
      ],
      now,
    });
  });

  await page.getByRole('button', { name: 'Дрон' }).click();
  const board = page.locator('.panel').filter({ hasText: 'Ирина, гидропоника' }).first();
  await board.locator('.slot').first().click();

  const buy = page.getByRole('button', { name: /^Докупить \d+ за \d+/ });
  await expect(buy).toBeVisible();

  const before = await page.evaluate(
    () => (window as unknown as { __game: Store }).__game.getState().isotopes as number,
  );

  await buy.click();
  await page.waitForTimeout(400);

  const after = await page.evaluate(() => {
    const state = (window as unknown as { __game: Store }).__game.getState();
    return {
      isotopes: state.isotopes as number,
      filled: (state.orders as Array<{ positions: Array<{ filled: boolean }> }>)[0]
        ?.positions[0]?.filled,
    };
  });

  expect(after.isotopes, 'докупка обязана списать изотопы').toBeLessThan(before);
  expect(after.filled, 'докупленная позиция обязана закрыться').toBe(true);
});
