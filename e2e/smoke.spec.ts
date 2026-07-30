import { expect, test } from '@playwright/test';

/**
 * Дымовая проверка: игра открывается и первая петля проходится.
 * Проверяем не верстку, а то, что игрок может сделать первый ход.
 */

test('игра открывается и показывает купол с грядками', async ({ page }) => {
  await page.goto('/');

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
  await page.goto('/');

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
  await page.goto('/');
  await page.getByRole('button', { name: 'Склад' }).click();
  await expect(page.getByText('Пока пусто.')).toBeVisible();
});

test('на узком экране интерфейс не разъезжается', async ({ page }) => {
  await page.goto('/');
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth > window.innerWidth,
  );
  expect(overflow, 'по горизонтали ничего не должно выходить за экран').toBe(false);
});
