import { defineConfig } from 'vitest/config';

// Vitest chỉ còn chạy test TypeScript của frontend, `packages/shared` và `tools`.
// Test backend chạy bằng `pnpm dotnet:test` vì backend đã chuyển sang .NET.
export default defineConfig({
  test: {
    coverage: {
      provider: 'v8',
      include: ['apps/frontend/src/**/*.{ts,tsx}', 'packages/shared/src/**/*.ts'],
      exclude: ['**/index.ts', '**/*.d.ts'],
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
          // Nạp polyfill DOM cho môi trường jsdom. Trình soạn thảo Tiptap cần các API này.
          setupFiles: ['apps/frontend/test/setup.ts'],
        },
      },
    ],
  },
});
