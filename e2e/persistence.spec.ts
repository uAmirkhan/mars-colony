import { expect, type Page, test } from '@playwright/test';

/**
 * Сохранение прогресса в браузере.
 *
 * Проверка обязана быть браузерной: модульные тесты подсовывают стору свое
 * хранилище и свои часы, а здесь работает настоящий localStorage настоящей
 * сборки, и перезагрузка — настоящая. Ровно на этом стыке ломается «у меня
 * все зеленое»: сейв может писаться и не читаться, читаться и не доезжать до
 * первого кадра, доезжать и падать на битом содержимом.
 */

async function openGame(page: Page) {
  await page.goto('/');
  await page.getByRole('button', { name: 'Играть' }).click();
}

/** Ключ и версия сейва спрашиваются у самой игры, а не дублируются здесь. */
async function saveMeta(page: Page): Promise<{ name: string; version: number }> {
  return page.evaluate(() => {
    const store = (
      window as unknown as {
        __game: { persist: { getOptions: () => { name: string; version: number } } };
      }
    ).__game;
    const options = store.persist.getOptions();
    return { name: options.name, version: options.version };
  });
}

const emptyFields = (page: Page) => page.getByText('+', { exact: true });

test('прогресс переживает перезагрузку страницы', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));

  await openGame(page);
  await expect(emptyFields(page)).toHaveCount(4);

  // Ход игрока: посев занимает грядку и списывает кредиты.
  await emptyFields(page).first().click();
  await page.getByRole('button', { name: /Посеять/ }).first().click();
  await expect(emptyFields(page)).toHaveCount(3);
  const credits = await page.locator('.currency').nth(1).innerText();

  await page.reload();
  await page.getByRole('button', { name: 'Играть' }).click();

  // Грядка все еще занята, кредиты все еще списаны.
  await expect(emptyFields(page)).toHaveCount(3);
  expect(await page.locator('.currency').nth(1).innerText()).toBe(credits);
  expect(errors, 'перезагрузка не должна ронять страницу').toEqual([]);
});

test('уровень и валюты переживают перезагрузку', async ({ page }) => {
  await openGame(page);
  await page.evaluate(() => {
    const store = (window as unknown as { __game: { setState: (p: unknown) => void } }).__game;
    store.setState({ level: 6, credits: 4321, isotopes: 77 });
  });

  await page.reload();
  await page.getByRole('button', { name: 'Играть' }).click();

  await expect(page.getByText('ур. 6')).toBeVisible();
  await expect(page.getByText('4321')).toBeVisible();
});

test('грядка, дозревшая при закрытой вкладке, открывается готовой', async ({ page }) => {
  await openGame(page);
  await emptyFields(page).first().click();
  await page.getByRole('button', { name: /Посеять/ }).first().click();
  await expect(emptyFields(page)).toHaveCount(3);

  // Отматываем метку созревания в прошлое прямо в файле сейва: так выглядит
  // вкладка, закрытая на время роста. ТЗ производства (п.2, AC 14) требует
  // отдать такую грядку сразу READY, без догоняющего роста.
  const { name } = await saveMeta(page);
  await page.evaluate((key) => {
    const raw = localStorage.getItem(key);
    if (raw === null) throw new Error('сейв не найден');
    const save = JSON.parse(raw);
    for (const field of save.state.fields) {
      if (field.ends_at > 0) field.ends_at = Math.floor(Date.now() / 1000) - 60;
    }
    localStorage.setItem(key, JSON.stringify(save));
  }, name);

  await page.reload();
  await page.getByRole('button', { name: 'Играть' }).click();

  await expect(page.getByText('Собрать').first()).toBeVisible();
});

test('битое хранилище не мешает игре открыться', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));

  await openGame(page);
  const { name } = await saveMeta(page);

  await page.evaluate((key) => localStorage.setItem(key, '{"state":{"credits":1'), name);
  await page.reload();
  await page.getByRole('button', { name: 'Играть' }).click();

  // Игра стартует с нуля, а не падает и не показывает пустоту.
  await expect(page.getByText('ур. 1')).toBeVisible();
  await expect(emptyFields(page)).toHaveCount(4);
  expect(errors, 'битый сейв не должен ронять страницу').toEqual([]);
});

test('чужое содержимое ключа игнорируется', async ({ page }) => {
  await openGame(page);
  const { name } = await saveMeta(page);

  await page.evaluate(
    (key) => localStorage.setItem(key, '{"user":{"name":"кто-то"},"cart":[1,2,3]}'),
    name,
  );
  await page.reload();
  await page.getByRole('button', { name: 'Играть' }).click();

  await expect(page.getByText('ур. 1')).toBeVisible();
  await expect(emptyFields(page)).toHaveCount(4);
});

test('сейв другой версии выбрасывается, игра начинается заново', async ({ page }) => {
  await openGame(page);
  const { name, version } = await saveMeta(page);

  await page.evaluate(
    ({ key, next }) => {
      const raw = localStorage.getItem(key);
      if (raw === null) throw new Error('сейв не найден');
      const save = JSON.parse(raw);
      save.state.level = 12;
      save.state.credits = 999_999;
      save.version = next;
      localStorage.setItem(key, JSON.stringify(save));
    },
    { key: name, next: version + 1 },
  );

  await page.reload();
  await page.getByRole('button', { name: 'Играть' }).click();

  await expect(page.getByText('ур. 1')).toBeVisible();
  await expect(page.getByText('999999')).toHaveCount(0);
});
