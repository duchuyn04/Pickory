import js from '@eslint/js';
import globals from 'globals';
import prettier from 'eslint-config-prettier';
import tseslint from 'typescript-eslint';
import { boundaryConfigs } from './eslint.boundaries.js';

export default tseslint.config(
  {
    ignores: [
      '**/node_modules/**',
      '**/dist/**',
      '**/build/**',
      '**/coverage/**',
      '.agents/**',
      '.claude/**',
      'tools/lint-fixtures/**',
    ],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    languageOptions: { globals: { ...globals.node } },
    rules: {
      '@typescript-eslint/no-explicit-any': 'error',
      'no-console': 'error',
    },
  },
  {
    // Code web chạy trong trình duyệt nên dùng globals của browser thay vì node.
    files: ['apps/frontend/**/*.{ts,tsx}'],
    languageOptions: { globals: { ...globals.browser } },
  },
  // Quy tắc ranh giới module (đặt trước prettier, sau các cấu hình chung).
  ...boundaryConfigs,
  prettier,
);
