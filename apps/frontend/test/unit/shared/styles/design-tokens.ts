import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

export const stylesDirectory = fileURLToPath(
  new URL('../../../../src/shared/styles/', import.meta.url),
);

// Đọc token từ chính file CSS mà app nạp, để test không giữ bản sao giá trị có thể lệch.
export function readDesignTokens(): Map<string, string> {
  const tokensCss = readFileSync(`${stylesDirectory}tokens.css`, 'utf8');
  const tokens = new Map<string, string>();
  for (const match of tokensCss.matchAll(/(--[a-z0-9-]+)\s*:\s*([^;]+);/gi)) {
    const [, tokenName, tokenValue] = match;
    if (tokenName && tokenValue) tokens.set(tokenName, tokenValue.trim());
  }
  return tokens;
}

// Báo lỗi rõ tên token khi thiếu. Test sẽ chỉ ra token nào chưa được khai báo.
export function readTokenValue(tokens: Map<string, string>, tokenName: string): string {
  const tokenValue = tokens.get(tokenName);
  if (!tokenValue) throw new Error(`Thiếu token ${tokenName} trong tokens.css`);
  return tokenValue;
}
