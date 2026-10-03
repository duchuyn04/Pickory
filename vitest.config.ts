import { defineConfig } from 'vitest/config';

// Hai nhóm test:
// - unit: logic thuần, chạy song song, không cần DB.
// - integration (*.int.test.ts): chạy qua PostgreSQL thật (DB test riêng), chạy tuần tự
//   vì các file dùng chung 1 DB và dọn bảng trước mỗi test.
export default defineConfig({
  test: {
    coverage: {
      provider: 'v8',
      include: ['apps/api/src/**/*.ts'],
      exclude: [
        '**/*.test.ts',
        '**/*.int.test.ts',
        '**/index.ts',
        '**/*.d.ts',
        'apps/api/src/generated/**',
      ],
      // Ngưỡng 80% dòng cho logic nghiệp vụ của từng module.
      thresholds: {
        'apps/api/src/modules/**/service.ts': { lines: 80 },
      },
    },
    projects: [
      {
        extends: true,
        test: {
          name: 'unit',
          include: ['**/*.test.{ts,tsx}'],
          exclude: ['**/node_modules/**', '**/dist/**', 'e2e/**', '**/*.int.test.ts'],
        },
      },
      {
        extends: true,
        test: {
          name: 'integration',
          include: ['**/*.int.test.ts'],
          exclude: ['**/node_modules/**', '**/dist/**'],
          globalSetup: ['apps/api/test/global-setup.ts'],
          setupFiles: ['apps/api/test/setup.ts'],
          fileParallelism: false,
        },
      },
    ],
  },
});
