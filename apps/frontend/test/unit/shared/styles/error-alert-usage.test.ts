import { readdirSync, readFileSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const sourceDirectory = fileURLToPath(new URL('../../../../src/', import.meta.url));
const sharedAlertFiles: Record<string, true> = {
  'shared/error-alert.tsx': true,
  'shared/warning-alert.tsx': true,
};
// Khung từ chối và thông báo lưu thành công có bố cục riêng, không phải lỗi trang.
const allowedAlerts: Record<string, RegExp> = {
  'shared/forbidden-notice.tsx': /className="card"/,
  'features/ai-gateway/admin/ai-settings-page.tsx': /className="alert alert-ok(?:\s[^"]*)?"/,
  // Lỗi cạnh ô nhập giữ `form-error` để `aria-describedby` trỏ đúng thông báo của ô.
  'features/catalog/admin/attribute-editor.tsx': /className="form-error"/,
  'features/catalog/admin/description-editor.tsx':
    /className="form-error description-editor-error"/,
};

function listTsxFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const entryPath = join(directory, entry.name);
    if (entry.isDirectory()) return listTsxFiles(entryPath);
    return entry.name.endsWith('.tsx') ? [entryPath] : [];
  });
}

// Lấy thẻ mở chứa mỗi `role="alert"`, từ ký tự `<` gần nhất phía trước đến dấu `>` kết thúc thẻ.
function findAlertOpeningTags(source: string): { tag: string; lineNumber: number }[] {
  const tags: { tag: string; lineNumber: number }[] = [];
  // Xóa comment JSX nhưng giữ dấu xuống dòng để vị trí lỗi vẫn trỏ đúng file nguồn.
  const uncommentedSource = source.replace(/\{\/\*[\s\S]*?\*\/\}/g, (comment) =>
    comment.replace(/[^\n]/g, ' '),
  );
  for (const match of uncommentedSource.matchAll(/role="alert"/g)) {
    const roleIndex = match.index ?? 0;
    const lineStart = uncommentedSource.lastIndexOf('\n', roleIndex) + 1;
    const textBeforeRole = uncommentedSource.slice(lineStart, roleIndex);
    // Chữ `role="alert"` trong comment một dòng không phải thẻ JSX.
    if (textBeforeRole.includes('/*') || textBeforeRole.includes('//')) continue;
    const tagStart = uncommentedSource.lastIndexOf('<', roleIndex);
    const tagEnd = uncommentedSource.indexOf('>', roleIndex);
    if (tagStart < 0 || tagEnd < 0) continue;
    tags.push({
      tag: uncommentedSource.slice(tagStart, tagEnd),
      lineNumber: uncommentedSource.slice(0, roleIndex).split('\n').length,
    });
  }
  return tags;
}

// Chỉ các thông báo có bố cục riêng và lý do ghi trong bảng trên được tự khai báo `role="alert"`.
function isUnapprovedAlert(filePath: string, tag: string): boolean {
  const allowedPattern = allowedAlerts[filePath];
  return !allowedPattern?.test(tag);
}

describe('thông báo lỗi dùng chung', () => {
  it('mọi `role="alert"` ngoài component dùng chung đều có ngoại lệ rõ lý do', () => {
    // Mọi lỗi và cảnh báo đi qua component dùng chung để giữ icon, màu và bố cục thống nhất.
    const violations = listTsxFiles(sourceDirectory)
      .filter((filePath) => {
        const relativePath = relative(sourceDirectory, filePath).split(sep).join('/');
        return !sharedAlertFiles[relativePath];
      })
      .flatMap((filePath) => {
        const relativePath = relative(sourceDirectory, filePath).split(sep).join('/');
        return findAlertOpeningTags(readFileSync(filePath, 'utf8'))
          .filter(({ tag }) => isUnapprovedAlert(relativePath, tag))
          .map(({ lineNumber }) => `${relativePath}:${lineNumber}`);
      });
    expect(violations).toEqual([]);
  });

  it('chặn thẻ không class và chỉ cho phép ngoại lệ đã ghi lý do', () => {
    expect(isUnapprovedAlert('features/catalog/admin/product-form.tsx', '<p role="alert"')).toBe(
      true,
    );
    expect(
      isUnapprovedAlert('shared/forbidden-notice.tsx', '<div className="card" role="alert"'),
    ).toBe(false);
    expect(
      isUnapprovedAlert(
        'features/ai-gateway/admin/ai-settings-page.tsx',
        '<p role="alert" className="alert alert-ok ai-settings-note"',
      ),
    ).toBe(false);
    expect(
      isUnapprovedAlert('features/ai-gateway/admin/ai-settings-page.tsx', '<p role="alert"'),
    ).toBe(true);
  });

  it('bỏ qua `role="alert"` trong comment JSX nhiều dòng', () => {
    const commentedSource = '{/*\n  <p role="alert">Không hiển thị</p>\n*/}';
    expect(findAlertOpeningTags(commentedSource)).toEqual([]);
  });

  it('quét được `error-alert.tsx` và các component trong `src`', () => {
    // Chặn trường hợp sai đường dẫn khiến test trên luôn xanh vì không quét file nào.
    const files = listTsxFiles(sourceDirectory).map((filePath) =>
      relative(sourceDirectory, filePath).split(sep).join('/'),
    );
    expect(files).toContain('shared/error-alert.tsx');
    expect(files.length).toBeGreaterThan(20);
  });
});
