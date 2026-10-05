import { readdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { ESLint } from 'eslint';
import { describe, expect, it } from 'vitest';

// Kiểm tra quy tắc ranh giới module trong eslint.boundaries.js bằng các file mẫu.
//
// Mỗi file trong tools/lint-fixtures/ có 2 dòng đầu:
//   // @lint-as <đường dẫn giả lập>   file được lint như thể nằm ở đường dẫn này
//   // @expect error | none            mong đợi `no-restricted-imports` báo lỗi hay không
// Dùng đường dẫn giả lập vì quy tắc phụ thuộc vị trí file (ví dụ chỉ providers/ được import `ai`).

const REPO_ROOT = fileURLToPath(new URL('../../..', import.meta.url));
const FIXTURES_DIR = new URL('../../lint-fixtures/', import.meta.url);

// Đọc một file mẫu: lấy đường dẫn giả lập, kết quả mong đợi và mã nguồn.
function readFixture(name: string) {
  const code = readFileSync(new URL(name, FIXTURES_DIR), 'utf-8');
  const lintAs = /^\/\/ @lint-as (.+)$/m.exec(code)?.[1]?.trim();
  const expected = /^\/\/ @expect (error|none)$/m.exec(code)?.[1];
  if (!lintAs || !expected) throw new Error(`File mẫu ${name} thiếu @lint-as hoặc @expect`);
  return { name, code, lintAs, expected };
}

const fixtures = readdirSync(FIXTURES_DIR)
  .filter((name) => name.endsWith('.ts'))
  .map(readFixture);

describe('quy tắc ranh giới module (ESLint)', () => {
  // Dùng chung một instance ESLint (tự nạp eslint.config.js ở gốc repo).
  const eslint = new ESLint({ cwd: REPO_ROOT });

  it('có cả file mẫu import sai và import hợp lệ', () => {
    expect(fixtures.some((f) => f.expected === 'error')).toBe(true);
    expect(fixtures.some((f) => f.expected === 'none')).toBe(true);
  });

  it.each(fixtures)(
    '$name ($expected)',
    async ({ code, lintAs, expected }) => {
      const [result] = await eslint.lintText(code, { filePath: `${REPO_ROOT}${lintAs}` });
      // Chỉ xét rule ranh giới; các rule khác (unused var...) không liên quan tới file mẫu.
      const violations = result?.messages.filter((m) => m.ruleId === 'no-restricted-imports') ?? [];

      if (expected === 'error') {
        expect(violations).toHaveLength(1);
      } else {
        expect(violations).toEqual([]);
      }
    },
    // Đặt timeout 15 giây vì kiểm tra toàn bộ fixture qua ESLint trên Windows chạy song song có thể mất hơn 5 giây.
    15000,
  );
});
