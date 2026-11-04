import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// Ví dụ request và response của một route trong file mẫu dùng chung với backend .NET.
export interface ContractExample {
  name: string;
  method: string;
  path: string;
  request: unknown;
  status: number;
  response: unknown;
}

// Một sự kiện SSE trong file mẫu dùng chung với backend .NET.
export interface ContractEvent {
  name: string;
  data: unknown;
}

interface ContractFixture {
  examples?: ContractExample[];
  events?: ContractEvent[];
}

// Dùng `node:path` thay `new URL` để đọc đúng cả trong môi trường jsdom, nơi `URL` toàn cục bị thay.
const FIXTURE_DIRECTORY = join(
  dirname(fileURLToPath(import.meta.url)),
  '../../../../../../spec/fixtures/support-desk/',
);

// Đọc file mẫu theo tên. Hai bên đọc cùng file nên hợp đồng không lệch.
function readFixtureFile(fileName: string): ContractFixture {
  const filePath = join(FIXTURE_DIRECTORY, fileName);
  let content: string;
  try {
    content = readFileSync(filePath, 'utf8');
  } catch {
    // Tên file sai là lỗi của test gọi helper, nên báo tên file ngay.
    throw new Error(`Không tìm thấy file mẫu "${fileName}" trong spec/fixtures/support-desk.`);
  }
  return JSON.parse(content) as ContractFixture;
}

// Trả ví dụ theo tên trong file mẫu. Tên ví dụ do file mẫu đặt, không dựa vào thứ tự.
export function findSupportDeskExample(fileName: string, exampleName: string): ContractExample {
  const { examples } = readFixtureFile(fileName);
  // File chỉ chứa sự kiện thì không có ví dụ nào để trả.
  if (examples === undefined) {
    throw new Error(`File mẫu "${fileName}" không có danh sách ví dụ "examples".`);
  }
  const example = examples.find((entry) => entry.name === exampleName);
  // Sai tên ví dụ phải đỏ ngay. Trả ví dụ đầu sẽ khiến test dùng sai dữ liệu mà vẫn xanh.
  if (example === undefined) {
    throw new Error(`Không có ví dụ "${exampleName}" trong file mẫu "${fileName}".`);
  }
  return example;
}

// Chọn sự kiện SSE theo tên. Một tên có thể lặp lại, nên giữ mọi sự kiện cùng tên theo thứ tự file.
export function selectSupportDeskEvents(
  fileName: string,
  eventNames: readonly string[],
): ContractEvent[] {
  const { events } = readFixtureFile(fileName);
  if (events === undefined) {
    throw new Error(`File mẫu "${fileName}" không có danh sách sự kiện "events".`);
  }
  const selected = events.filter((event) => eventNames.includes(event.name));
  // Thiếu một tên thì báo lỗi, để luồng SSE của test không rỗng mà case vẫn xanh.
  const missingNames = eventNames.filter(
    (eventName) => !selected.some((event) => event.name === eventName),
  );
  if (missingNames.length > 0) {
    throw new Error(
      `Không có sự kiện "${missingNames.join('", "')}" trong file mẫu "${fileName}".`,
    );
  }
  return selected;
}
