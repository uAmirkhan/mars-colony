/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

export default defineConfig({
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
