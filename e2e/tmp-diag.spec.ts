import { expect, test } from '@playwright/test';
import { diffRatio } from './frame-diff';

test('диагностика покоя', async ({ page }) => {
  await page.goto('/?fresh=1');
  await page.getByRole('button', { name: 'Играть' }).click();
  const button = page.getByRole('button', { name: 'Склад' });
  await expect(button).toBeVisible();
  const box = await button.boundingBox();
  if (!box) throw new Error('нет кнопки');
  const clip = {
    x: Math.max(0, box.x - 44),
    y: Math.max(0, box.y - 44),
    width: box.width + 88,
    height: box.height + 88,
  };
  for (let i = 0; i < 4; i++) {
    const a = await page.screenshot({ clip, path: `diag-${i}-a.png` });
    await page.waitForTimeout(320);
    const b = await page.screenshot({ clip, path: `diag-${i}-b.png` });
    console.log(`заход ${i}: ${(await diffRatio(a, b)).toFixed(4)}`);
  }
});
