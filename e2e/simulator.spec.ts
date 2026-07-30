import { expect, test } from '@playwright/test';

/**
 * Симулятор — главный артефакт, и главное в нем не графики, а реакция.
 * Эти проверки существуют против одного молчаливого дефекта: ползунок
 * двигается, картинка перерисовывается, а прогон считается на старых числах.
 */

const summary = /уровень \d+ за \d+ дней/;
const banner = /Экономика держится|Сломано пунктов/;

test('симулятор открывается первым экраном', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByText('Модель игрока')).toBeVisible();
  await expect(page.getByText('Параметры игры')).toBeVisible();
  await expect(page.getByText(banner).first()).toBeVisible();
  await expect(page.getByText('Инварианты')).toBeVisible();
});

test('крутизна XP-кривой меняет исход прогона', async ({ page }) => {
  await page.goto('/');
  const before = await page.getByText(summary).first().innerText();

  const curve = page.locator('input[type=range]').nth(4);
  await curve.focus();
  for (let i = 0; i < 8; i++) await page.keyboard.press('ArrowRight');

  const after = await page.getByText(summary).first().innerText();
  expect(after, 'прогон обязан пересчитаться на новых числах').not.toBe(before);
});

test('мягкая кривая чинит инварианты, жесткая ломает', async ({ page }) => {
  await page.goto('/');
  const curve = page.locator('input[type=range]').nth(4);
  await curve.focus();

  for (let i = 0; i < 14; i++) await page.keyboard.press('ArrowLeft');
  await expect(page.getByText('Экономика держится')).toBeVisible();

  for (let i = 0; i < 20; i++) await page.keyboard.press('ArrowRight');
  await expect(page.getByText(/Сломано пунктов/)).toBeVisible();
});

test('прогон воспроизводится по seed', async ({ page }) => {
  await page.goto('/');
  const first = await page.getByText(summary).first().innerText();
  await page.reload();
  const second = await page.getByText(summary).first().innerText();
  expect(second).toBe(first);
});

test('можно переключиться в игру и обратно', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Играть' }).click();
  await expect(page.getByText('ур. 1')).toBeVisible();

  await page.getByRole('button', { name: 'Балансный симулятор' }).click();
  await expect(page.getByText('Инварианты')).toBeVisible();
});
