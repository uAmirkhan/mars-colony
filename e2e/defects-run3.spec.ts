import { expect, type Page, test } from '@playwright/test';

/**
 * Браузерные доказательства прогона 3. Здесь живут только те дефекты, у которых
 * важна ДОСТИЖИМОСТЬ кликом: модульный тест доказывает, что домен считает
 * неверно, а этот файл — что игрок доходит до неверного состояния двумя тапами
 * по кнопкам, которые нарисованы рядом.
 *
 * Состояние ставится через тестовый шов `window.__game`: рейс шаттла занимает
 * час игрового времени, живым кликом до нужной комбинации не дойти.
 */

type Store = {
  getState: () => Record<string, unknown>;
  setState: (patch: Record<string, unknown>) => void;
};

async function openGame(page: Page) {
  await page.goto('/');
  await page.getByRole('button', { name: 'Играть' }).click();
}

/**
 * Д-18. «Погрузить 3» и «Докупить 2» стоят на одной карточке отсека. Нажатые
 * подряд, они отправляют рейс и оставляют три погруженные водоросли на складе
 * навсегда: `filled_by` отсека перезаписан на `'purchase'`, и отправка
 * пропускает списание всего отсека целиком (`shuttle.ts:346-350`).
 *
 * Проверяется складом, а не текстом: после отправки `reserved` обязан быть
 * нулем — живого слота, который его держит, больше нет.
 */
test('Д-18: докупка после частичной погрузки запирает товар на складе', async ({ page }) => {
  await openGame(page);

  await page.evaluate(() => {
    const store = (window as unknown as { __game: Store }).__game;
    store.setState({
      level: 9,
      credits: 100_000,
      isotopes: 100_000,
      // Трех водорослей на пять не хватает: отсек закрывается в два приема.
      warehouse: { capacity: 500, cells: { algae: { qty: 3, reserved: 0 } } },
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
        ],
        trip_min: 75,
        departed_at: 0,
        arrives_at: 0,
        cooldown_until: 0,
        is_first_trip: false,
        arrival_no: 2,
      },
    });
  });

  await page.getByRole('button', { name: 'Шаттл' }).click();
  // Карточка отсека раскрывается тапом: кнопки живут внутри нее.
  await page.getByRole('button', { name: /0\/5/ }).click();

  // Шаг 1: положить то, что есть. ТЗ шаттла 6.2 подписывает кнопку количеством.
  await page.getByRole('button', { name: 'Погрузить 3' }).click();
  // Карточка после погрузки схлопывается — открываем ее снова.
  await page.getByRole('button', { name: /3\/5/ }).click();
  // Шаг 2: остаток докупить за изотопы — кнопка нарисована тут же.
  await page.getByRole('button', { name: /Докупить 2 за/ }).click();

  const cell = await page.evaluate(() => {
    const store = (window as unknown as { __game: Store }).__game;
    const w = store.getState().warehouse as {
      cells: Record<string, { qty: number; reserved: number }>;
    };
    return w.cells['algae'] ?? { qty: 0, reserved: 0 };
  });

  // Рейс ушел: либо груз уехал со склада, либо резерв снят. Сейчас ни того,
  // ни другого — три водоросли заперты в `reserved` без владельца.
  expect(cell, 'склад после отправки рейса').toEqual({ qty: 0, reserved: 0 });
});
