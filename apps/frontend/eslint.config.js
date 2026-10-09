import js from '@eslint/js';
import globals from 'globals';
import prettier from 'eslint-config-prettier';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  {
    ignores: [
      '**/node_modules/**',
      '**/dist/**',
      '**/coverage/**',
      // Bỏ artifact Playwright vì runner tạo chúng, không phải source của dự án.
      'playwright-report/**',
      'test-results/**',
    ],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    // Code web chạy trong trình duyệt nên dùng globals của browser.
    languageOptions: { globals: { ...globals.browser } },
    rules: {
      '@typescript-eslint/no-explicit-any': 'error',
      'no-console': 'error',
    },
  },
  {
    // Cấu hình build và test E2E chạy trên Node, không chạy trong trình duyệt.
    files: ['*.config.{js,ts}', 'e2e/**/*.ts'],
    languageOptions: { globals: { ...globals.node } },
  },
  {
    // Mô tả sản phẩm do nhân viên soạn, nên module catalog không được chèn HTML thô vào trang khách.
    files: ['src/features/catalog/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector: "JSXAttribute[name.name='dangerouslySetInnerHTML']",
          message:
            'Không dùng dangerouslySetInnerHTML trong features/catalog. Nội dung do nhân viên soạn có thể chứa mã script. Hãy vẽ bằng thẻ React.',
        },
      ],
    },
  },
  prettier,
);
