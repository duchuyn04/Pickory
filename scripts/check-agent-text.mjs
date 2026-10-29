import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export function findAgentTextViolations(text) {
  const violations = [];
  const lines = text.split(/\r?\n/);

  // Kiểm tra từng dòng để vị trí lỗi không bị lệch khi nội dung có nhiều dòng.
  lines.forEach((lineText, lineIndex) => {
    const separatedIPattern = /[áàảãạắằẳẵặấầẩẫậéèẻẽệếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵ][\t ]+i\b/giu;
    for (const match of lineText.matchAll(separatedIPattern)) {
      violations.push({
        line: lineIndex + 1,
        column: match.index + 1,
        found: match[0],
      });
    }
  });

  return violations;
}

function runAgentTextCheck(filePaths) {
  if (filePaths.length === 0) {
    console.error('Cú pháp: node scripts/check-agent-text.mjs <file...>');
    return 2;
  }

  let violationCount = 0;
  for (const filePath of filePaths) {
    const text = readFileSync(filePath, 'utf8');
    const violations = findAgentTextViolations(text);

    // Báo từng vị trí để agent sửa đúng lỗi trước khi chạy lại kiểm tra.
    for (const violation of violations) {
      console.error(`${filePath}:${violation.line}:${violation.column}: Khoảng trắng sai trước i.`);
      violationCount++;
    }
  }

  return violationCount === 0 ? 0 : 1;
}

const isMainModule = process.argv[1]
  && resolve(process.argv[1]) === fileURLToPath(import.meta.url);

if (isMainModule) {
  process.exitCode = runAgentTextCheck(process.argv.slice(2));
}
