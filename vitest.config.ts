import { defineConfig } from 'vitest/config';

// Hai nhóm test:
// - unit: logic thuần, chạy song song, không cần DB.
// - integration (*.int.test.ts): chạy qua PostgreSQL thật (DB test riêng), chạy tuần tự
//   vì các file dùng chung 1 DB và dọn bảng trước mỗi test.
export default defineConfig({
  test: {
    coverage: {
      provider: 'v8',
      include: ['apps/backend/src/**/*.ts'],
      exclude: ['**/index.ts', '**/*.d.ts', 'apps/backend/src/generated/**'],
      // Ngưỡng 80% dòng cho logic nghiệp vụ của từng module.
      thresholds: {
        'apps/backend/src/modules/**/service.ts': { lines: 80 },
        // Mọi file gateway có logic chạy được đều phải đạt ngưỡng riêng, không lấy file nhiều test bù cho facade thiếu coverage.
        'apps/backend/src/modules/ai-gateway/**/*.ts': { lines: 80, perFile: true },
        // Module lớn tách logic nghiệp vụ thành thư mục services/ (ví dụ identity).
        'apps/backend/src/modules/**/services/*.ts': { lines: 80 },
      },
    },
    projects: [
      {
        extends: true,
        test: {
          name: 'unit',
          include: [
            'apps/*/test/unit/**/*.test.{ts,tsx}',
            'packages/*/test/unit/**/*.test.{ts,tsx}',
            'tools/test/unit/**/*.test.ts',
          ],
        },
      },
      {
        extends: true,
        test: {
          name: 'integration',
          include: [
            'apps/*/test/integration/**/*.int.test.{ts,tsx}',
            'packages/*/test/integration/**/*.int.test.{ts,tsx}',
          ],
          globalSetup: ['apps/backend/test/global-setup.ts'],
          setupFiles: ['apps/backend/test/setup.ts'],
          fileParallelism: false,
        },
      },
    ],
  },
});
