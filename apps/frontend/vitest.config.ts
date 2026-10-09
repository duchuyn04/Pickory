import { defineConfig } from 'vitest/config';

// Vitest chạy test TypeScript của frontend. Test backend chạy bằng `dotnet test`.
export default defineConfig({
  test: {
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['**/*.d.ts'],
    },
    projects: [
      {
        extends: true,
        test: {
          name: 'unit',
          include: ['test/unit/**/*.test.{ts,tsx}'],
          // Nạp polyfill DOM cho môi trường jsdom. Trình soạn thảo Tiptap cần các API này.
          setupFiles: ['test/setup.ts'],
        },
      },
    ],
  },
});
