import { expect, type Page, test } from '@playwright/test';

/**
 * Два дефекта верстки, которых модульный тест не видит: он не рендерит.
 *
 * Д-12. У слота фабрики в состоянии `PRODUCING` не было кнопки ускорения.
 * ТЗ производства 9.3 описывает это состояние дословно: «иконка товара,
 * прогресс-бар с таймером и кнопка „Ускорить за {price} ⚛" под баром», а
 * каркас (раздел 13) называет пропуск багом прямым текстом: «Любая механика
 * с таймером обязана иметь экран или состояние с кнопкой ускорения. Ставка
 * ускорения в конфиге без кнопки в интерфейсе — баг ТЗ». Ставка фабрики в
 * конфиге была, действие `speedupFactory` в сторе было, пути из интерфейса
 * не было ни одного — при том, что у грядки и у стройки кнопки стоят.
 *
 * Д-13. Изотопы обозначались двумя разными глифами: ⚛ у грядки и ⬡ у стройки,
 * дрона и шаттла. Каркас (UX-стандарт, раздел 13) допускает ровно один:
 * «Обозначение изотопов — только иконка ⚛». Проверка идет по всем экранам с
 * ценой сразу, а не по одному: разъезд глифа и возник ровно потому, что
 * заметить его можно было лишь сравнив четыре файла глазами.
 */

type Store = {
  getState: () => Record<string, any>;
  setState: (patch: Record<string, unknown>) => void;
};

/** Глиф изотопов по каркасу и единственный запрещенный ему альтернативный. */
const ISOTOPE = '⚛';
const FORBIDDEN = '⬡';

async function openGame(page: Page) {
  await page.goto('/?fresh=1');
  await page.getByRole('button', { name: 'Играть' }).click();
}

/**
 * Закрыть верхнюю панель. Именно последнюю: окно заказа рендерится после своего
 * экрана, и `.first()` закрыл бы экран целиком вместе с окном.
 */
async function close(page: Page) {
  await page.getByRole('button', { name: 'Закрыть' }).last().click();
}

/** Фабрика с одним слотом в производстве: ровно то состояние, у которого не было кнопки. */
async function seedProducingFactory(page: Page) {
  await page.evaluate(() => {
    const store = (window as unknown as { __game: Store }).__game;
    const now = Math.floor(Date.now() / 1000);
    store.setState({
      isotopes: 100_000,
      buildings: ['food_module'],
      factory_slots: [
        {
          idx: 0,
          building_type: 'food_module',
          state: 'PRODUCING',
          good_id: 'protein_bar',
          queued_at: now,
          ends_at: now + 600,
        },
      ],
      now,
    });
  });
}

test('Д-12: слот фабрики в PRODUCING несет кнопку ускорения, и она ускоряет', async ({
  page,
}) => {
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));

  await openGame(page);
  await seedProducingFactory(page);

  await page.getByRole('button', { name: 'Фабрика' }).click();

  // Кнопка обязана нести цену на себе — правило каркаса про цену до тапа.
  const speedup = page.getByRole('button', { name: new RegExp(`Ускорить за \\d+ ${ISOTOPE}`) });
  await expect(speedup, 'у PRODUCING обязана быть кнопка ускорения').toBeVisible();

  const before = await page.evaluate(
    () => (window as unknown as { __game: Store }).__game.getState().isotopes as number,
  );

  await speedup.click();

  const after = await page.evaluate(() => {
    const state = (window as unknown as { __game: Store }).__game.getState();
    return {
      isotopes: state.isotopes as number,
      slot_state: state.factory_slots[0].state as string,
    };
  });

  expect(after.isotopes, 'ускорение обязано списать изотопы').toBeLessThan(before);
  expect(after.slot_state, 'ускоренный слот обязан стать READY').toBe('READY');
  await expect(page.getByText('Забрать')).toBeVisible();
  expect(errors, 'экран производства не должен падать').toEqual([]);
});

test('Д-12: тап по кнопке ускорения не проваливается в сбор слота', async ({ page }) => {
  await openGame(page);
  await seedProducingFactory(page);

  await page.getByRole('button', { name: 'Фабрика' }).click();
  await page.getByRole('button', { name: new RegExp(`Ускорить за \\d+ ${ISOTOPE}`) }).click();

  // Слот целиком кликабелен под сбор. Если ускорение провалится в него, товар
  // уедет на склад тем же тапом и игрок не увидит кнопку «Забрать».
  const state = await page.evaluate(
    () => (window as unknown as { __game: Store }).__game.getState().factory_slots[0],
  );
  expect(state.state, 'ускорение не собирает партию за игрока').toBe('READY');
  expect(state.good_id, 'слот не должен опустеть от одного тапа').toBe('protein_bar');
});

test('Д-13: изотопы на всех экранах обозначены одним глифом ⚛', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));

  await openGame(page);

  // Один посев, одна стройка, одно производство, доска дрона и рейс шаттла —
  // все места, где в интерфейсе стоит цена в изотопах.
  await page.evaluate(() => {
    const store = (window as unknown as { __game: Store }).__game;
    const now = Math.floor(Date.now() / 1000);
    const state = store.getState();
    store.setState({
      level: 9,
      credits: 100_000,
      isotopes: 100_000,
      buildings: ['food_module'],
      fields: [{ idx: 0, state: 'GROWING', good_id: 'algae', ends_at: now + 600 }],
      factory_slots: [
        {
          idx: 0,
          building_type: 'food_module',
          state: 'PRODUCING',
          good_id: 'protein_bar',
          queued_at: now,
          ends_at: now + 600,
        },
      ],
      construction: { ...state.construction, stock: { filter: 6, cable: 6, sealant: 6 } },
      orders: [
        {
          idx: 0,
          state: 'empty_cooldown',
          npc_name: 'Ирина, гидропоника',
          positions: [],
          credits_reward: 0,
          xp_reward: 0,
          refresh_at: now + 3600,
        },
      ],
      now,
    });
  });

  const seen: string[] = [];

  /** Текст всего документа: хаб и купол остаются в DOM за затемнением модалки. */
  const sweep = async (screen: string) => {
    const text = await page.locator('body').innerText();
    expect(text, `${screen}: запрещенный глиф изотопов`).not.toContain(FORBIDDEN);
    seen.push(text);
  };

  // Купол: цена ускорения грядки.
  await sweep('купол');

  await page.getByRole('button', { name: 'Фабрика' }).click();
  await expect(
    page.getByRole('button', { name: new RegExp(`Ускорить за \\d+ ${ISOTOPE}`) }),
  ).toBeVisible();
  await sweep('фабрика');
  await close(page);

  // Стройка: цена ускорения появляется после старта.
  await page.getByRole('button', { name: 'Стройка' }).click();
  await page.getByRole('button', { name: 'Строить' }).first().click();
  await expect(
    page.getByRole('button', { name: new RegExp(`Ускорить за \\d+ ${ISOTOPE}`) }),
  ).toBeVisible();
  await sweep('стройка');
  await close(page);

  // Дрон: цена платного рефреша на доске и цена докупки в окне заказа.
  await page.getByRole('button', { name: 'Дрон' }).click();
  const board = page.locator('.panel').filter({ hasText: 'Площадка дрона' });
  await expect(
    page.getByRole('button', { name: new RegExp(`Обновить · \\d+ ${ISOTOPE}`) }),
  ).toBeVisible();
  await sweep('доска дрона');
  // Нулевая карточка — заведомо пустой слот с рефрешем, у нее нет окна заказа.
  await board.locator('.slot').nth(1).click();
  await expect(page.getByRole('button', { name: /^Докупить \d+ за \d+/ }).first()).toBeVisible();
  await sweep('окно заказа дрона');
  await close(page);
  await close(page);

  // Шаттл: цена докупки отсека и цена скипа рейса.
  await page.getByRole('button', { name: 'Шаттл' }).click();
  const station = page.locator('.panel').filter({ hasText: 'Орбитальная станция' });
  await station.locator('.slot').first().click();
  await expect(page.getByRole('button', { name: /^Докупить \d+ за \d+/ })).toBeVisible();
  await sweep('отсек шаттла');
  await close(page);

  await page.evaluate(() => {
    const store = (window as unknown as { __game: Store }).__game;
    const now = Math.floor(Date.now() / 1000);
    const trip = store.getState().shuttle;
    store.setState({
      shuttle: { ...trip, state: 'IN_TRANSIT', departed_at: now, arrives_at: now + 3600 },
    });
  });
  await expect(
    page.getByRole('button', { name: new RegExp(`Ускорить за \\d+ ${ISOTOPE}`) }),
  ).toBeVisible();
  await sweep('рейс шаттла');

  // Проверка не должна быть пустой: если бы цены нигде не рендерились, отсутствие
  // запрещенного глифа ничего не доказывало бы.
  expect(
    seen.some((text) => text.includes(ISOTOPE)),
    'ни на одном экране не нашлось цены в изотопах — проверка вырождена',
  ).toBe(true);
  expect(errors).toEqual([]);
});
