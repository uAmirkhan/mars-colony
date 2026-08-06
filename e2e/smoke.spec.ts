import { expect, type Page, test } from '@playwright/test';

/** Игра теперь второй экран: первым открывается симулятор. */
async function openGame(page: Page) {
  await page.goto('/?fresh=1');
  await page.getByRole('button', { name: 'Играть' }).click();
}

/**
 * Дымовая проверка: игра открывается и первая петля проходится.
 * Проверяем не верстку, а то, что игрок может сделать первый ход.
 */

test('игра открывается и показывает купол с грядками', async ({ page }) => {
  await openGame(page);

  // HUD на месте: уровень и обе валюты.
  await expect(page.getByText('ур. 1')).toBeVisible();
  await expect(page.getByText(/Склад 0\//)).toBeVisible();

  // Стартовые грядки: четыре пустых слота с плюсом.
  await expect(page.getByText('+', { exact: true })).toHaveCount(4);

  // Хаб из трех кнопок.
  for (const label of ['Купол', 'Склад', 'Фабрика']) {
    await expect(page.getByRole('button', { name: label })).toBeVisible();
  }
});

test('первый ход проходит: посев списывает кредиты и занимает грядку', async ({ page }) => {
  await openGame(page);

  const credits_before = await page.locator('.currency').nth(1).innerText();

  await page.getByText('+', { exact: true }).first().click();
  await expect(page.getByText('Что посадить')).toBeVisible();
  await page.getByRole('button', { name: /Посеять/ }).first().click();

  // Грядка занята: пустых слотов стало на один меньше.
  await expect(page.getByText('+', { exact: true })).toHaveCount(3);

  // Кредиты списались.
  const credits_after = await page.locator('.currency').nth(1).innerText();
  expect(credits_after).not.toBe(credits_before);
});

test('склад открывается и сообщает, что пуст', async ({ page }) => {
  await openGame(page);
  await page.getByRole('button', { name: 'Склад' }).click();

  // Пустой склад больше не отвечает строкой «Пока пусто»: ТЗ производства 9.2
  // требует показывать ВЕСЬ ассортимент открытого, приглушая нулевые позиции,
  // а не прятать их. Игрок видит, что вообще бывает, а не только то, что уже
  // успел собрать. Поэтому проверяем то же утверждение по-другому: позиции на
  // экране есть, продавать нечего.
  const panel = page.locator('.panel').filter({ hasText: 'Склад' }).first();
  await expect(panel.getByRole('button', { name: 'Продать 1' }).first()).toBeVisible();

  const sell_all = panel.getByRole('button', { name: 'Все', exact: true });
  const count = await sell_all.count();
  expect(count, 'ассортимент обязан быть виден целиком, а не спрятан').toBeGreaterThan(0);
  for (let i = 0; i < count; i++) {
    await expect(sell_all.nth(i), 'на пустом складе продавать нечего').toBeDisabled();
  }
});

test('на узком экране интерфейс не разъезжается', async ({ page }) => {
  await openGame(page);
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth > window.innerWidth,
  );
  expect(overflow, 'по горизонтали ничего не должно выходить за экран').toBe(false);
});
