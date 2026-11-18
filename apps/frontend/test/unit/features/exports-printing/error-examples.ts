import { findExportsPrintingExample } from './contract-fixtures';

// Phần `error` của ví dụ lỗi trong file mẫu `exports.json`.
export interface ServerErrorExample {
  code: string;
  message: string;
}

// Đọc phần `error` của ví dụ và kiểm dạng, để file mẫu đổi dạng thì test báo rõ thay vì so sánh sai.
export function readServerErrorExample(exampleName: string): ServerErrorExample {
  const example = findExportsPrintingExample('exports.json', exampleName);
  const response = example.response;
  if (typeof response !== 'object' || response === null || !('error' in response)) {
    throw new Error(`Ví dụ "${exampleName}" không có phần error.`);
  }
  const error = response.error;
  if (
    typeof error !== 'object' ||
    error === null ||
    !('code' in error) ||
    typeof error.code !== 'string' ||
    !('message' in error) ||
    typeof error.message !== 'string'
  ) {
    throw new Error(`Ví dụ "${exampleName}" có error sai dạng.`);
  }
  return { code: error.code, message: error.message };
}

// Tạo phản hồi giả từ đúng `status` và thân của ví dụ, để test không chép tay thân lỗi.
export function buildExampleResponse(exampleName: string): Response {
  const example = findExportsPrintingExample('exports.json', exampleName);
  return new Response(JSON.stringify(example.response), {
    status: example.status,
    headers: { 'Content-Type': 'application/json' },
  });
}
