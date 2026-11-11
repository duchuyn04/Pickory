import { expect, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

// Hàm dựng và dọn mẫu trả lời nhanh bằng API thật của backend. Spec dùng chung để dữ liệu E2E không đến từ route giả.

const CANNED_RESPONSES_PATH = '/api/admin/support/canned-responses';

// Backend kiểm header `Origin` của request ghi dữ liệu, nên mọi lời gọi ghi phải gửi header này.
export const SUPPORT_API_HEADERS = { Origin: e2eEnvironment.webOrigin };

export interface CannedResponseRecord {
  id: string;
  shortcut: string;
  body: string;
  shared: boolean;
  editable: boolean;
}

export interface NewCannedResponse {
  shortcut: string;
  body: string;
  shared: boolean;
}

// Hậu tố riêng cho mỗi lần chạy. Phím tắt có hậu tố này không đụng lỗi 409 với dữ liệu của lần chạy khác.
export function makeRunSuffix(): string {
  return `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 5)}`;
}

// Tạo mẫu bằng API với phiên đăng nhập của `page`. Mẫu dùng chung cần `support-settings:manage`, mẫu cá nhân cần `conversations:reply`.
export async function createCannedResponseByApi(
  page: Page,
  input: NewCannedResponse,
): Promise<CannedResponseRecord> {
  const response = await page.request.post(CANNED_RESPONSES_PATH, {
    headers: SUPPORT_API_HEADERS,
    data: input,
  });
  // Trả 201 mới chứng tỏ backend đã lưu mẫu. Mã khác làm case đỏ ngay ở bước dựng dữ liệu.
  expect(response.status(), await response.text()).toBe(201);
  const body = (await response.json()) as { data: CannedResponseRecord };
  return body.data;
}

export async function listCannedResponsesByApi(page: Page): Promise<CannedResponseRecord[]> {
  const response = await page.request.get(CANNED_RESPONSES_PATH);
  expect(response.status(), await response.text()).toBe(200);
  const body = (await response.json()) as { data: CannedResponseRecord[] };
  return body.data;
}

export async function deleteCannedResponseByApi(page: Page, id: string): Promise<void> {
  const response = await page.request.delete(`${CANNED_RESPONSES_PATH}/${id}`, {
    headers: SUPPORT_API_HEADERS,
  });
  expect(response.status(), await response.text()).toBe(204);
}

// Xóa mọi mẫu mà người gọi sửa được và có phím tắt chứa hậu tố của lần chạy. Mẫu của người khác không bị đụng tới.
export async function deleteCannedResponsesWithSuffix(page: Page, suffix: string): Promise<void> {
  const responses = await listCannedResponsesByApi(page);
  for (const response of responses) {
    if (!response.editable || !response.shortcut.includes(suffix)) continue;
    await deleteCannedResponseByApi(page, response.id);
  }
}
