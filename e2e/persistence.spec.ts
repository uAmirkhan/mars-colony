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
  await page.goto('/?fresh=1');
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

/**
 * Ждет подпись показа и, если не дождался, ГОВОРИТ ПОЧЕМУ.
 *
 * Сборка состояния показа может сорваться, и тогда точка входа откатывается на
 * канонический старт — подписи нет, игра работает, ошибок нет. Голое «элемент
 * не найден» про это не сообщает ничего, и такой отказ уже стоил половины
 * прогона: он воспроизводился примерно дважды из семидесяти двух под шестью
 * параллельными браузерами, а причину назвать было нечем.
 *
 * Сама сборка теперь называет причину срыва в консоль. Здесь мы ее слушаем и
 * подставляем в текст падения. Следующий случай объяснит себя сам.
 */
async function expectDemoBadge(page: Page) {
  const said: string[] = [];
  page.on('console', (m) => {
    if (m.text().includes('[показ]')) said.push(m.text());
  });

  try {
    await expect(page.getByTestId('demo-badge')).toBeVisible();
  } catch (e) {
    const why = said.length > 0 ? said.join('; ') : 'сборка показа молчала, причина неизвестна';
    throw new Error(`подписи показа нет. Что сказала сборка: ${why}`, { cause: e });
  }
}

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

  // Игра открывается и играется, а не падает и не показывает пустоту.
  //
  // Открывается она показом, а не первым уровнем, и это осознанно: хранилище,
  // из которого нечего прочитать, неотличимо от хранилища человека, зашедшего
  // впервые. Обоим показывается одно и то же. Проверяется здесь не уровень, а
  // то, ради чего проверка написана: страница жива, мусор из ключа никуда не
  // просочился, играть можно.
  await expectDemoBadge(page);
  await expect(page.getByRole('button', { name: 'Склад' })).toBeVisible();
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

  // Чужая запись в нашем ключе — это не прогресс, поэтому заход считается
  // первым и открывается показом. Главное здесь другое: ни одно поле чужого
  // объекта не должно доехать до состояния игры.
  await expectDemoBadge(page);
  const state = await page.evaluate(
    () =>
      (
        window as unknown as { __game: { getState: () => { user?: unknown; cart?: unknown } } }
      ).__game.getState(),
  );
  expect(state.user, 'чужие поля не имеют права попасть в состояние').toBeUndefined();
  expect(state.cart).toBeUndefined();
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

  // Сейв неизвестной версии — это не прогресс: играть с него нельзя, значит
  // заход считается первым и открывается показом. Проверяется здесь ровно то,
  // ради чего проверка написана: ни одно число из чужой версии не доехало до
  // экрана. Уровень 12 и 999999 кредитов оттуда, показ дает свои.
  await expectDemoBadge(page);
  await expect(page.getByText('999999')).toHaveCount(0);
  await expect(page.getByText('ур. 12')).toHaveCount(0);
});
