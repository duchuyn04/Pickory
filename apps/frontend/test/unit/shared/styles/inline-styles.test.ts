import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const sourceDirectory = fileURLToPath(new URL('../../../../src/', import.meta.url));

function listTsxFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const entryPath = join(directory, entry.name);
    if (entry.isDirectory()) return listTsxFiles(entryPath);
    return entry.name.endsWith('.tsx') ? [entryPath] : [];
  });
}

describe('design system', () => {
  it('không component nào trong `src` viết style trực tiếp', () => {
    // Style trực tiếp không đổi theo token và không viết được `@media`, nên mọi màn chỉ dùng lớp CSS.
    const violations = listTsxFiles(sourceDirectory).flatMap((filePath) => {
      const lines = readFileSync(filePath, 'utf8').split('\n');
      return lines
        .map((line, index) => ({ line, lineNumber: index + 1 }))
        .filter(({ line }) => line.includes('style={'))
        .map(({ lineNumber }) => `${relative(sourceDirectory, filePath)}:${lineNumber}`);
    });
    expect(violations).toEqual([]);
  });

  it('quét được các component trong `src`', () => {
    // Chặn trường hợp sai đường dẫn khiến test trên luôn xanh vì không quét file nào.
    expect(listTsxFiles(sourceDirectory).length).toBeGreaterThan(20);
  });
});
