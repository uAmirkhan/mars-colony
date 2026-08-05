import { test } from '@playwright/test';

/**
 * Снимки экрана для человека, а не для утверждений.
 *
 * Проверки доказывают, что элементы на месте и помещаются в экран. Как это
 * выглядит целиком, ни одно из них не показывает — а решение «читается кадром
 * из игры или нет» принимается глазом. Файлы кладутся в `shots/` и в проверку
 * не входят: тест без единого `expect` ничего не гарантирует и не должен
 * притворяться, что гарантирует.
 */

test('снимки показа', async ({ page }, info) => {
  const tag = info.project.name;

  await page.goto('/');
  await page.getByRole('button', { name: 'Играть' }).click();
  await page.waitForTimeout(700);
  await page.screenshot({ path: `shots/demo-${tag}.png`, fullPage: false });

  await page.getByRole('button', { name: 'Шаттл', exact: true }).click();
  await page.waitForTimeout(400);
  await page.screenshot({ path: `shots/shuttle-${tag}.png` });

  await page.getByRole('button', { name: 'Закрыть' }).first().click();
  await page.getByRole('button', { name: 'Дрон', exact: true }).click();
  await page.waitForTimeout(400);
  await page.screenshot({ path: `shots/drone-${tag}.png` });
});
