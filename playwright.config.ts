import { defineConfig, devices } from '@playwright/test';

/**
 * Браузерная проверка существует ради одного: агент может честно ошибиться
 * и сказать «готово». Браузер не ошибается — он либо видит игру, либо нет.
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  fullyParallel: true,
  reporter: [['list']],
  use: {
    baseURL: 'http://127.0.0.1:4173',
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'] } },
    // Рекрутер откроет ссылку с телефона — узкий экран проверяется всегда.
    { name: 'mobile', use: { ...devices['Pixel 7'] } },
  ],
  webServer: {
    command: 'npm run build && npx vite preview --port 4173 --host 127.0.0.1',
    url: 'http://127.0.0.1:4173',
    reuseExistingServer: true,
    timeout: 120_000,
  },
});
