import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const sourceDirectory = fileURLToPath(new URL('../../../../src/', import.meta.url));

// Mã hex và hàm `rgb()`, `rgba()` là màu viết tay. Màu chỉ được khai báo trong `tokens.css`.
const hardCodedColorPattern = /#[0-9a-f]{3,8}\b|\brgba?\(/gi;

function listCssFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const entryPath = join(directory, entry.name);
    if (entry.isDirectory()) return listCssFiles(entryPath);
    return entry.name.endsWith('.css') ? [entryPath] : [];
  });
}

// Bỏ comment trước khi quét. Comment có thể nhắc mã màu của bản mẫu để giải thích nguồn token.
function stripCssComments(css: string): string {
  return css.replace(/\/\*[\s\S]*?\*\//g, '');
}

describe('CSS của component', () => {
  it('không viết mã màu ngoài tokens.css', () => {
    const violations = listCssFiles(sourceDirectory)
      .filter((filePath) => !filePath.endsWith(join('shared', 'styles', 'tokens.css')))
      .flatMap((filePath) => {
        const matches = stripCssComments(readFileSync(filePath, 'utf8')).match(
          hardCodedColorPattern,
        );
        return (matches ?? []).map((color) => `${relative(sourceDirectory, filePath)}: ${color}`);
      });
    expect(violations).toEqual([]);
  });

  it('quét được ít nhất một file CSS ngoài tokens.css', () => {
    // Chặn trường hợp sai đường dẫn khiến test trên luôn xanh vì không quét file nào.
    expect(listCssFiles(sourceDirectory).length).toBeGreaterThan(1);
  });
});
