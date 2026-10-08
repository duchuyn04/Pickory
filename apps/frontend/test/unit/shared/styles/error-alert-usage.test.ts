import { readdirSync, readFileSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const sourceDirectory = fileURLToPath(new URL('../../../../src/', import.meta.url));
const errorAlertFile = 'shared/error-alert.tsx';

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
  for (const match of source.matchAll(/role="alert"/g)) {
    const roleIndex = match.index ?? 0;
    // Chữ `role="alert"` trong comment không phải thẻ JSX, nên bỏ qua dòng có dấu mở comment đứng trước.
    const lineStart = source.lastIndexOf('\n', roleIndex) + 1;
    const textBeforeRole = source.slice(lineStart, roleIndex);
    if (textBeforeRole.includes('/*') || textBeforeRole.includes('//')) continue;
    const tagStart = source.lastIndexOf('<', roleIndex);
    const tagEnd = source.indexOf('>', roleIndex);
    if (tagStart < 0 || tagEnd < 0) continue;
    tags.push({
      tag: source.slice(tagStart, tagEnd),
      lineNumber: source.slice(0, roleIndex).split('\n').length,
    });
  }
  return tags;
}

// Thông báo lỗi là thẻ có class `alert` đứng riêng. `alert-ok` và `alert-warn` là thông báo khác.
function isErrorAlertTag(tag: string): boolean {
  const classNameMatch = tag.match(/className=(?:"([^"]*)"|\{([^}]*)\})/);
  const classText = classNameMatch ? (classNameMatch[1] ?? classNameMatch[2] ?? '') : '';
  const hasPlainAlertClass = /(^|[\s'"`])alert([\s'"`]|$)/.test(classText);
  const isOtherKind = /alert-ok|alert-warn/.test(classText);
  return hasPlainAlertClass && !isOtherKind;
}

describe('thông báo lỗi dùng chung', () => {
  it('không component nào ngoài `shared/error-alert.tsx` tự viết thông báo lỗi bằng `role="alert"` và class `alert`', () => {
    // Mọi thông báo lỗi đi qua `ErrorAlert` để có cùng icon, chữ và danh sách không chấm tròn.
    const violations = listTsxFiles(sourceDirectory)
      .filter(
        (filePath) => relative(sourceDirectory, filePath).split(sep).join('/') !== errorAlertFile,
      )
      .flatMap((filePath) =>
        findAlertOpeningTags(readFileSync(filePath, 'utf8'))
          .filter(({ tag }) => isErrorAlertTag(tag))
          .map(({ lineNumber }) => `${relative(sourceDirectory, filePath)}:${lineNumber}`),
      );
    expect(violations).toEqual([]);
  });

  it('bộ nhận diện bắt đúng thẻ lỗi và bỏ qua thông báo thành công, cảnh báo', () => {
    expect(isErrorAlertTag('<p role="alert" className="alert">')).toBe(true);
    expect(isErrorAlertTag('<p role="alert" className="alert product-list-alert">')).toBe(true);
    expect(isErrorAlertTag('<p role="alert" className="alert alert-ok ai-settings-note">')).toBe(
      false,
    );
    expect(isErrorAlertTag('<p role="alert" className="alert alert-warn">')).toBe(false);
    expect(isErrorAlertTag('<div role="alert" className="form-error">')).toBe(false);
    expect(isErrorAlertTag('<div className="card" role="alert">')).toBe(false);
  });

  it('quét được `error-alert.tsx` và các component trong `src`', () => {
    // Chặn trường hợp sai đường dẫn khiến test trên luôn xanh vì không quét file nào.
    const files = listTsxFiles(sourceDirectory).map((filePath) =>
      relative(sourceDirectory, filePath).split(sep).join('/'),
    );
    expect(files).toContain(errorAlertFile);
    expect(files.length).toBeGreaterThan(20);
  });
});
