import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// Ví dụ của một route trong file mẫu dùng chung với backend .NET. Ví dụ JSON có `response`, ví dụ file Excel có `headers` và `workbook`.
export interface ContractExample {
  name: string;
  method: string;
  path?: string;
  request?: unknown;
  status: number;
  response?: unknown;
  headers?: Record<string, string>;
  workbook?: unknown;
}

interface ContractFixture {
  examples: ContractExample[];
}

// Dùng `node:path` thay `new URL` để đọc đúng cả trong môi trường jsdom, nơi `URL` toàn cục bị thay.
const FIXTURE_DIRECTORY = join(
  dirname(fileURLToPath(import.meta.url)),
  '../../../../../../spec/fixtures/exports-printing/',
);

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

// Tên hiển thị trong lỗi. Ví dụ không có tên chuỗi thì dùng số thứ tự để người sửa tìm được vị trí.
function describeExample(example: Record<string, unknown>, index: number): string {
  return typeof example.name === 'string' ? `Ví dụ "${example.name}"` : `Ví dụ thứ ${index + 1}`;
}

// Kiểm tối thiểu hình dạng file mẫu. Ép kiểu không kiểm sẽ để file mẫu hỏng làm test sai ở nơi khó đoán.
export function parseContractFixture(fileName: string, content: string): ContractFixture {
  let parsed: unknown;
  try {
    parsed = JSON.parse(content);
  } catch {
    // `JSON.parse` ném `SyntaxError` không nêu tên file. Báo lại bằng lỗi tiếng Việt có tên file để người sửa tìm đúng nơi.
    throw new Error(`File mẫu "${fileName}" không phải JSON hợp lệ.`);
  }
  if (!isPlainObject(parsed)) {
    throw new Error(`File mẫu "${fileName}" phải có gốc là object.`);
  }
  const { examples } = parsed;
  if (!Array.isArray(examples)) {
    throw new Error(`File mẫu "${fileName}" phải có "examples" là mảng.`);
  }
  examples.forEach((example: unknown, index) => {
    // Phần tử không phải object thì không có trường nào để kiểm, nên báo như thiếu `name`.
    const entry = isPlainObject(example) ? example : {};
    const subject = describeExample(entry, index);
    // Ba trường này là phần helper dùng để chọn ví dụ và dựng response.
    if (typeof entry.name !== 'string') {
      throw new Error(`${subject} trong file mẫu "${fileName}" thiếu "name" hoặc sai kiểu.`);
    }
    if (typeof entry.method !== 'string') {
      throw new Error(`${subject} trong file mẫu "${fileName}" thiếu "method" hoặc sai kiểu.`);
    }
    if (typeof entry.status !== 'number') {
      throw new Error(`${subject} trong file mẫu "${fileName}" thiếu "status" hoặc sai kiểu.`);
    }
  });
  const typedExamples = examples as ContractExample[];
  // Hai ví dụ cùng tên làm `find` chọn ví dụ đầu mà không báo. Ném lỗi để test không dùng nhầm dữ liệu.
  const seenNames = new Set<string>();
  for (const example of typedExamples) {
    if (seenNames.has(example.name)) {
      throw new Error(`Tên ví dụ "${example.name}" bị trùng trong file mẫu "${fileName}".`);
    }
    seenNames.add(example.name);
  }
  return { examples: typedExamples };
}

// Đọc file mẫu theo tên. Hai bên đọc cùng file nên hợp đồng không lệch.
function readFixtureFile(fileName: string): ContractFixture {
  const filePath = join(FIXTURE_DIRECTORY, fileName);
  let content: string;
  try {
    content = readFileSync(filePath, 'utf8');
  } catch {
    // Tên file sai là lỗi của test gọi helper, nên báo tên file ngay.
    throw new Error(`Không tìm thấy file mẫu "${fileName}" trong spec/fixtures/exports-printing.`);
  }
  return parseContractFixture(fileName, content);
}

// Trả mọi ví dụ của file theo thứ tự trong file. Test lọc theo `status` thay vì đánh số cứng.
export function listExportsPrintingExamples(fileName: string): ContractExample[] {
  return readFixtureFile(fileName).examples;
}

// Trả ví dụ theo tên trong file mẫu. Tên ví dụ do file mẫu đặt, không dựa vào thứ tự.
export function findExportsPrintingExample(fileName: string, exampleName: string): ContractExample {
  const example = listExportsPrintingExamples(fileName).find((entry) => entry.name === exampleName);
  // Sai tên ví dụ phải đỏ ngay. Trả ví dụ đầu sẽ khiến test dùng sai dữ liệu mà vẫn xanh.
  if (example === undefined) {
    throw new Error(`Không có ví dụ "${exampleName}" trong file mẫu "${fileName}".`);
  }
  return example;
}
