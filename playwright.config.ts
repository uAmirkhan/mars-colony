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
    /**
     * Не переиспользуем уже поднятый сервер. Он однажды отдал сборку, сделанную
     * до правок: тесты искали кнопку, которой в том бандле не было, и падали не
     * по делу. Опаснее обратный случай — зеленый прогон по устаревшему коду.
     * Лишняя сборка на прогон дешевле такой проверки.
     */
    reuseExistingServer: false,
    timeout: 120_000,
    /**
     * Тестовый шов стора включается флагом сборки, а не режимом: preview
     * собирается как прод, и `import.meta.env.DEV` там уже false. В настоящем
     * деплое переменная не задана, и ветка вырезается сборщиком.
     */
    env: { VITE_E2E: '1' },
  },
});
