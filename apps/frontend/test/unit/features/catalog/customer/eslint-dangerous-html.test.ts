// @vitest-environment node
import { ESLint } from 'eslint';
import { describe, expect, it } from 'vitest';

// Đoạn code vi phạm được lint từ chuỗi, không tồn tại thành file trong `src/`, nên `pnpm lint` vẫn sạch.
const violatingSource = `
export function Unsafe({ html }: { html: string }) {
  return <div dangerouslySetInnerHTML={{ __html: html }} />;
}
`;

const eslint = new ESLint({ cwd: process.cwd() });

async function lintAs(filePath: string) {
  const [result] = await eslint.lintText(violatingSource, { filePath });
  if (!result) throw new Error('ESLint không trả kết quả.');
  return result;
}

// Lần lint đầu nạp toàn bộ cấu hình và có thể chậm khi cả bộ test chạy song song, nên nới thời hạn.
describe('quy tắc chặn dangerouslySetInnerHTML', { timeout: 30_000 }, () => {
  it('báo đúng 1 lỗi cho code trong features/catalog', async () => {
    const result = await lintAs('src/features/catalog/customer/fixture-unsafe.tsx');
    expect(result.errorCount).toBe(1);
    expect(result.messages[0]?.ruleId).toBe('no-restricted-syntax');
    expect(result.messages[0]?.message).toContain('dangerouslySetInnerHTML');
  });

  it('báo lỗi cả với thư mục admin của catalog', async () => {
    const result = await lintAs('src/features/catalog/admin/fixture-unsafe.tsx');
    expect(result.messages.map((message) => message.ruleId)).toEqual(['no-restricted-syntax']);
  });

  it('không báo cùng đoạn code nằm ngoài features/catalog', async () => {
    const result = await lintAs('src/features/identity/customer/fixture-unsafe.tsx');
    expect(result.messages.filter((message) => message.ruleId === 'no-restricted-syntax')).toEqual(
      [],
    );
    expect(result.errorCount).toBe(0);
  });
});
