import { expect, type Page, test } from '@playwright/test';

/**
 * Состояние показа: то, что увидит человек, открывший ссылку.
 *
 * Проверка нужна ровно потому, что показ — единственное состояние, которого
 * никто не соберет руками при отладке. Его строит код, и сломаться он может
 * молча: колония откроется первым уровнем без шаттла, и это будет выглядеть
 * как замысел, а не как поломка.
 *
 * Второй смысл — граница честности. Экран обязан говорить, какие числа
 * заработаны экономикой, а какие выданы. Проверка держит подпись на месте.
 */

const DEMO_LEVEL = 7;
/** Рейс отматывается на две минуты; допуск на время самого прогона. */
const MAX_LEFT_SEC = 3 * 60;

interface Snapshot {
  level: number;
  credits: number;
  isotopes: number;
  shuttle_state: string | null;
  left_sec: number | null;
  slots: number;
  buildings: string[];
  orders: number;
}

async function snapshot(page: Page): Promise<Snapshot> {
  return page.evaluate(() => {
    const s = (
      window as unknown as { __game: { getState: () => Record<string, never> } }
    ).__game.getState() as unknown as {
      level: number;
      credits: number;
      isotopes: number;
      now: number;
      buildings: string[];
      orders: unknown[];
      shuttle: { state: string; arrives_at: number; slots: unknown[] } | null;
    };
    return {
      level: s.level,
      credits: s.credits,
      isotopes: s.isotopes,
      shuttle_state: s.shuttle?.state ?? null,
      left_sec: s.shuttle ? s.shuttle.arrives_at - s.now : null,
      slots: s.shuttle?.slots.length ?? 0,
      buildings: s.buildings,
      orders: s.orders.length,
    };
  });
}

test('ссылка открывается колонией, в которой есть что потрогать', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));

  // Без параметров — так, как ссылку откроет получатель.
  await page.goto('/');
  await page.getByRole('button', { name: 'Играть' }).click();

  const state = await snapshot(page);

  expect(state.level, 'показ открывается седьмым уровнем').toBe(DEMO_LEVEL);
  expect(state.buildings, 'здания показа куплены настоящей покупкой').toEqual(
    expect.arrayContaining(['food_module', 'mining_site']),
  );
  expect(state.orders, 'доска дрона не должна быть пустой').toBeGreaterThan(0);

  // Шаттл — центральная вещь среза, и он обязан быть в рейсе, а не в заказе:
  // иначе открывший ссылку увидит форму погрузки и ни одного прилета.
  expect(state.shuttle_state, 'рейс обязан быть в пути').toBe('IN_TRANSIT');
  expect(state.slots, 'рейс без отсеков — это не рейс').toBeGreaterThan(0);
  expect(state.left_sec ?? 0, 'до прибытия минуты, а не час').toBeLessThanOrEqual(MAX_LEFT_SEC);
  expect(state.left_sec ?? 0, 'рейс не должен быть уже прибывшим').toBeGreaterThan(0);

  expect(errors, 'показ не имеет права ронять страницу').toEqual([]);
});

test('подпись объясняет, что выдано, и уводит на честный старт', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Играть' }).click();

  const badge = page.getByTestId('demo-badge');
  await expect(badge, 'подпись показа обязана быть на экране').toBeVisible();
  await expect(badge).toContainText('Выданы');

  // Кнопка «с нуля» обязана привести к каноническому старту, а не к тому же
  // показу с другой стороны.
  // Кнопка перезагружает страницу: сброс обязан пройти тот же путь, что и
  // первый заход. Дожидаемся конца перехода явно, иначе следующий клик уходит
  // в старый документ и тест мигает без всякой связи с игрой.
  await Promise.all([
    page.waitForLoadState('load'),
    badge.getByRole('button', { name: 'Начать с нуля' }).click(),
  ]);
  await page.getByRole('button', { name: 'Играть' }).click();

  const state = await snapshot(page);
  expect(state.level, 'начало с нуля — это первый уровень').toBe(1);
  expect(state.shuttle_state, 'на первом уровне шаттла нет').toBeNull();
  await expect(page.getByTestId('demo-badge')).toHaveCount(0);
});

test('показ переживает перезагрузку и не пересобирается заново', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Играть' }).click();
  const before = await snapshot(page);

  await page.reload();
  await page.getByRole('button', { name: 'Играть' }).click();
  const after = await snapshot(page);

  // Ключевое: после перезагрузки это ТОТ ЖЕ рейс, а не новый. Пересборка
  // показа на каждом заходе стирала бы все, что человек успел сделать.
  expect(after.level).toBe(before.level);
  expect(after.credits).toBe(before.credits);
  expect(after.shuttle_state).toBe('IN_TRANSIT');
  expect(
    (after.left_sec ?? 0) <= (before.left_sec ?? 0),
    'таймер рейса обязан идти вперед, а не начинаться заново',
  ).toBe(true);
});
