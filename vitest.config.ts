import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    include: ['**/*.test.ts', '**/*.test.tsx'],
    exclude: ['**/node_modules/**', '**/dist/**', 'e2e/**'],
    coverage: {
      provider: 'v8',
      include: ['apps/api/src/**/*.ts'],
      exclude: ['**/*.test.ts', '**/*.int.test.ts', '**/index.ts', '**/*.d.ts'],
      thresholds: {
        'apps/api/src/modules/**/service.ts': { lines: 80 },
      },
    },
  },
});
