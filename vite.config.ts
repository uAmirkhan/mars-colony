/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

/**
 * Префикс адреса.
 *
 * GitHub Pages отдает проект по пути `/<имя-репозитория>/`, и сборка с корневым
 * префиксом там просто не находит свои же файлы: страница открывается белой, а
 * консоль полна 404. Локально и на любом другом хостинге префикс корневой,
 * поэтому он не зашит, а включается переменной сборки — иначе `serve-dist.mjs`
 * и браузерные проверки пришлось бы учить чужому пути.
 */
const base = process.env.PAGES_BASE ?? '/';

export default defineConfig({
  base,
  plugins: [react()],
  test: {
    include: ['src/**/*.test.ts'],
    coverage: {
      provider: 'v8',
      reporter: ['text-summary', 'html'],
      // Покрываем то, где живут правила игры. Интерфейс проверяется браузером,
      // а не покрытием: сто процентов покрытия верстки ничего не доказывают.
      include: ['src/domain/**/*.ts'],
      exclude: ['src/domain/**/*.test.ts', 'src/domain/types.ts'],
      // Порог — гейт, а не отчет. Падение покрытия роняет сборку.
      thresholds: {
        statements: 85,
        branches: 80,
        functions: 85,
        lines: 85,
      },
    },
  },
});
