import { expect, test } from '@playwright/test';

/**
 * Симулятор — главный артефакт, и главное в нем не графики, а реакция.
 * Эти проверки существуют против одного молчаливого дефекта: ползунок
 * двигается, картинка перерисовывается, а прогон считается на старых числах.
 */

const summary = /уровень \d+ за \d+ дней/;
const banner = /Экономика держится|Сломано пунктов/;

/**
 * Открывается первой ИГРА (решение владельца 2026-08-06), поэтому симулятор
 * во всех проверках ниже открывается кнопкой. Раньше он стоял первым экраном,
 * и `goto` хватало.
 */
async function openSim(page: import('@playwright/test').Page) {
  await page.goto('/?fresh=1');
  await page.getByRole('button', { name: 'Балансный симулятор' }).click();
}

test('симулятор открывается второй кнопкой', async ({ page }) => {
  await openSim(page);
  await expect(page.getByText('Модель игрока')).toBeVisible();
  await expect(page.getByText('Параметры игры')).toBeVisible();
  await expect(page.getByText(banner).first()).toBeVisible();
  await expect(page.getByText('Инварианты')).toBeVisible();
});

test('крутизна XP-кривой меняет исход прогона', async ({ page }) => {
  await openSim(page);
  const before = await page.getByText(summary).first().innerText();

  const curve = page.locator('input[type=range]').nth(4);
  await curve.focus();
  for (let i = 0; i < 8; i++) await page.keyboard.press('ArrowRight');

  const after = await page.getByText(summary).first().innerText();
  expect(after, 'прогон обязан пересчитаться на новых числах').not.toBe(before);
});

/**
 * Края кривой ломают РАЗНОЕ, и проверять надо именно это.
 *
 * Прежняя редакция требовала от мягкого края полного нуля поломок и была
 * зеленой лишь потому, что прогон шел по устаревшей сборке с переиспользованного
 * сервера. На актуальном коде мягкий край ломает свой пункт: игрок один раз за
 * прогон остается без кредитов на посев — быстрая прогрессия открывает грядки
 * раньше, чем экономика успевает их прокормить. Записать это в ожидания
 * заглушкой значило бы спрятать находку, ради которой симулятор и написан.
 *
 * Жесткий край ломает свой: двенадцатый уровень за месяц не достигается. Это
 * прямая, читаемая связь «параметр -> последствие», и она проверяется числом.
 */
test('края XP-кривой ломают разные инварианты', async ({ page }) => {
  await openSim(page);
  const curve = page.locator('input[type=range]').nth(4);
  await curve.focus();

  for (let i = 0; i < 14; i++) await page.keyboard.press('ArrowLeft');
  await expect(
    page.getByText(/^день \d+$/).last(),
    'на мягкой кривой двенадцатый уровень обязан достигаться',
  ).toBeVisible();

  for (let i = 0; i < 20; i++) await page.keyboard.press('ArrowRight');
  await expect(page.getByText(/Сломано пунктов/)).toBeVisible();
  await expect(
    page.getByText('не достигнут'),
    'на жесткой кривой прогрессия обязана упираться',
  ).toBeVisible();
});

test('прогон воспроизводится по seed', async ({ page }) => {
  await openSim(page);
  const first = await page.getByText(summary).first().innerText();
  await page.reload();
  // После перезагрузки первой открывается игра, а не симулятор: возвращаемся
  // в него кнопкой. Сам прогон от этого не меняется — проверяется, что он
  // повторяется по seed, а не что страница помнит выбранный экран.
  await page.getByRole('button', { name: 'Балансный симулятор' }).click();
  const second = await page.getByText(summary).first().innerText();
  expect(second).toBe(first);
});

test('можно переключиться в игру и обратно', async ({ page }) => {
  await openSim(page);
  await page.getByRole('button', { name: 'Играть' }).click();
  await expect(page.getByText('ур. 1')).toBeVisible();

  await page.getByRole('button', { name: 'Балансный симулятор' }).click();
  await expect(page.getByText('Инварианты')).toBeVisible();
});
