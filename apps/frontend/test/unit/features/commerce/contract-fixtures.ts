import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseContractFixture, type ContractExample } from '../exports-printing/contract-fixtures';

// Dùng `node:path` thay `new URL` để đọc đúng cả trong môi trường jsdom, nơi `URL` toàn cục bị thay.
const FIXTURE_DIRECTORY = join(
  dirname(fileURLToPath(import.meta.url)),
  '../../../../../../spec/fixtures/commerce/',
);

// Đọc file mẫu theo tên. Backend .NET đọc cùng file nên hợp đồng hai bên không lệch.
function readFixtureFile(fileName: string): ContractExample[] {
  const filePath = join(FIXTURE_DIRECTORY, fileName);
  let content: string;
  try {
    content = readFileSync(filePath, 'utf8');
  } catch {
    // Tên file sai là lỗi của test gọi helper, nên báo tên file ngay.
    throw new Error(`Không tìm thấy file mẫu "${fileName}" trong spec/fixtures/commerce.`);
  }
  return parseContractFixture(fileName, content).examples;
}

// Trả mọi ví dụ của file theo thứ tự trong file. Test lọc theo `status` thay vì đánh số cứng.
export function listCommerceExamples(fileName: string): ContractExample[] {
  return readFixtureFile(fileName);
}

// Trả ví dụ theo tên trong file mẫu. Tên ví dụ do file mẫu đặt, không dựa vào thứ tự.
export function findCommerceExample(fileName: string, exampleName: string): ContractExample {
  const example = listCommerceExamples(fileName).find((entry) => entry.name === exampleName);
  // Sai tên ví dụ phải đỏ ngay. Trả ví dụ đầu sẽ khiến test dùng sai dữ liệu mà vẫn xanh.
  if (example === undefined) {
    throw new Error(`Không có ví dụ "${exampleName}" trong file mẫu "${fileName}".`);
  }
  return example;
}
