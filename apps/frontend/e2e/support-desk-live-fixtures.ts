import path from 'node:path';
import {
  expect,
  request as apiRequest,
  type APIRequestContext,
  type Browser,
  type BrowserContext,
  type Locator,
  type Page,
} from '@playwright/test';
import { collectConsoleIssues, type AllowedLoadFailure } from './catalog-admin-flow';
import { stubWebFonts } from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';
import { SUPPORT_API_HEADERS } from './support-desk-canned-api';

// Hàm dựng dữ liệu của màn chăm sóc khách hàng bằng API thật của backend.
// Spec dùng chung các hàm này nên không còn dữ liệu giả cho các route `support-desk`.

export const PRESENCE_PATH = '/api/admin/support/presence';
const SESSIONS_PATH = '/api/admin/support/sessions';
const CALLBACKS_PATH = '/api/admin/support/callback-requests';

export interface StaffSession {
  context: BrowserContext;
  page: Page;
  consoleIssues: string[];
  // Lỗi tải mà một case chủ động gây ra. Case thêm vào rồi gỡ ra, nên mọi case khác vẫn đòi console sạch.
  allowedFailures: AllowedLoadFailure[];
}

export async function openStaffSession(browser: Browser): Promise<StaffSession> {
  const context = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  const page = await context.newPage();
  const allowedFailures: AllowedLoadFailure[] = [];
  const consoleIssues = collectConsoleIssues(page, allowedFailures);
  // Chặn phông chữ ngoài để lỗi mạng ngoài không làm console có lỗi oan.
  await stubWebFonts(page);
  return { context, page, consoleIssues, allowedFailures };
}

// Ảnh chụp vào thư mục ngoài repo khi có biến môi trường. Không có biến thì Playwright giữ ảnh trong thư mục kết quả.
export function screenshotPath(
  testInfo: { outputPath: (name: string) => string },
  fileName: string,
) {
  const directory = process.env.SUPPORT_DESK_SHOTS_DIR;
  return directory ? path.join(directory, fileName) : testInfo.outputPath(fileName);
}

// Khung bao của phần tử. Phần tử không có khung thì case đỏ ngay, thay vì so với 0 cho qua.
export async function readBox(locator: Locator) {
  const box = await locator.boundingBox();
  expect(box).not.toBeNull();
  return box!;
}

// Mã của người đang đăng nhập trong `page`.
export async function readOwnUserId(page: Page): Promise<string> {
  const response = await page.request.get('/api/me');
  expect(response.status(), await response.text()).toBe(200);
  const body: { data: { userId: string } } = await response.json();
  return body.data.userId;
}

// Danh sách nhân viên đang sẵn sàng theo backend thật. Đây là bằng chứng trạng thái đã được ghi vào DB.
export async function readAvailableStaffIds(page: Page): Promise<string[]> {
  const response = await page.request.get(PRESENCE_PATH);
  expect(response.status(), await response.text()).toBe(200);
  const body: { data: { staffId: string }[] } = await response.json();
  return body.data.map((entry) => entry.staffId);
}

// Đổi trạng thái sẵn sàng bằng API. Backend trả 200 khi lưu thành công, nên mã khác là lỗi dựng dữ liệu.
export async function setPresenceByApi(page: Page, status: 'available' | 'away'): Promise<void> {
  const response = await page.request.put(PRESENCE_PATH, {
    headers: SUPPORT_API_HEADERS,
    data: { status },
  });
  expect(response.status(), await response.text()).toBe(200);
}

// ---- Phía khách ----

// Một khách có hội thoại mở. Khách ẩn danh dùng cookie của riêng `request`, khách đã đăng nhập dùng cookie của `page`.
export interface CustomerChat {
  request: APIRequestContext;
  conversationId: string;
  // Gửi một tin của khách. Backend mở phiên chờ ở tin đầu tiên của hội thoại chưa có phiên mở.
  send(body: string): Promise<void>;
  dispose(): Promise<void>;
}

async function openConversation(request: APIRequestContext, ownsRequest: boolean) {
  const conversation = await request.post('/api/support/conversations', {
    headers: SUPPORT_API_HEADERS,
  });
  expect([200, 201], await conversation.text()).toContain(conversation.status());
  // Hình dạng phản hồi theo hợp đồng của route mở hội thoại. Hàm chỉ đọc mã hội thoại.
  const conversationBody: { data: { id: string } } = await conversation.json();
  const conversationId = conversationBody.data.id;
  const chat: CustomerChat = {
    request,
    conversationId,
    async send(body) {
      const message = await request.post(`/api/support/conversations/${conversationId}/messages`, {
        headers: SUPPORT_API_HEADERS,
        data: { body },
      });
      expect(message.status(), await message.text()).toBe(201);
    },
    async dispose() {
      if (ownsRequest) await request.dispose();
    },
  };
  return chat;
}

// Khách ẩn danh với hội thoại mở và cookie riêng. Mỗi lần gọi là một khách mới.
export async function openAnonymousCustomerChat(): Promise<CustomerChat> {
  const request = await apiRequest.newContext({ baseURL: e2eEnvironment.webOrigin });
  return openConversation(request, true);
}

// Mở hội thoại bằng cookie hiện có của `page`. Khách đã đăng nhập hay vãng lai đều dùng được.
export async function openLoggedInCustomerChat(page: Page): Promise<CustomerChat> {
  return openConversation(page.request, false);
}

// ---- Phía nhân viên ----

export interface QueueSessionRecord {
  id: string;
  conversationId: string;
  preview: string;
}

// Chờ tới khi hàng chờ của nhân viên có phiên với tin đầu chứa `marker`. Backend ghi phiên cùng transaction với tin, nhưng `expect.poll` giữ ổn định khi chậm.
export async function findQueueSession(
  staffPage: Page,
  view: 'waiting' | 'mine',
  marker: string,
): Promise<QueueSessionRecord> {
  let found: QueueSessionRecord | undefined;
  await expect
    .poll(
      async () => {
        const response = await staffPage.request.get(`${SESSIONS_PATH}?view=${view}`);
        expect(response.status(), await response.text()).toBe(200);
        const body: { data: QueueSessionRecord[] } = await response.json();
        found = body.data.find((session) => session.preview.includes(marker));
        return found !== undefined;
      },
      { message: `Hàng chờ ${view} phải có phiên với tin "${marker}"` },
    )
    .toBe(true);
  return found!;
}

// Đếm phiên mà backend trả cho một view. Case so số này với badge và danh sách trên giao diện.
export async function countQueueSessions(staffPage: Page, view: 'waiting' | 'mine') {
  const response = await staffPage.request.get(`${SESSIONS_PATH}?view=${view}`);
  expect(response.status(), await response.text()).toBe(200);
  const body: { data: unknown[] } = await response.json();
  return body.data.length;
}

// Nhận phiên bằng API. Backend trả 201 khi tạo quyền giữ phiên, nên mã khác là lỗi dựng dữ liệu.
export async function claimSessionByApi(staffPage: Page, sessionId: string): Promise<void> {
  const response = await staffPage.request.post(`${SESSIONS_PATH}/${sessionId}/assignment`, {
    headers: SUPPORT_API_HEADERS,
  });
  expect(response.status(), await response.text()).toBe(201);
}

// Nhân viên giữ phiên gửi tin bằng API. Backend trả 201 khi tạo tin, nên mã khác là lỗi dựng dữ liệu.
export async function sendStaffMessageByApi(
  staffPage: Page,
  sessionId: string,
  body: string,
): Promise<void> {
  const response = await staffPage.request.post(`${SESSIONS_PATH}/${sessionId}/messages`, {
    headers: SUPPORT_API_HEADERS,
    data: { body },
  });
  expect(response.status(), await response.text()).toBe(201);
}

// Đóng phiên bằng API cho bước dựng dữ liệu. Backend trả 200 khi đóng thành công, nên mã khác là lỗi dựng dữ liệu.
export async function closeSessionByApi(staffPage: Page, sessionId: string): Promise<void> {
  const response = await staffPage.request.patch(`${SESSIONS_PATH}/${sessionId}`, {
    headers: SUPPORT_API_HEADERS,
    data: { status: 'closed' },
  });
  expect(response.status(), await response.text()).toBe(200);
}

// Dọn phiên cuối case, và phiên chờ cần nhận trước khi đóng.
// Hàm không ném lỗi, vì lỗi dọn dẹp không được che lỗi của case.
export async function closeSessionBestEffort(staffPage: Page, sessionId: string): Promise<void> {
  await staffPage.request
    .post(`${SESSIONS_PATH}/${sessionId}/assignment`, { headers: SUPPORT_API_HEADERS })
    .catch(() => undefined);
  await staffPage.request
    .patch(`${SESSIONS_PATH}/${sessionId}`, {
      headers: SUPPORT_API_HEADERS,
      data: { status: 'closed' },
    })
    .catch(() => undefined);
}

// Đóng mọi phiên có tin đầu chứa `marker`, để phiên của case đỏ không sót lại hàng chờ.
// Hàm không ném lỗi, vì lỗi dọn dẹp không được che lỗi của case.
export async function closeSessionsByMarkerBestEffort(
  staffPage: Page,
  marker: string,
): Promise<void> {
  try {
    for (const view of ['waiting', 'mine'] as const) {
      const response = await staffPage.request.get(`${SESSIONS_PATH}?view=${view}`);
      // Phản hồi lỗi thì bỏ qua view này. Lỗi dọn dẹp không được che lỗi của case.
      if (!response.ok()) continue;
      const body: { data: QueueSessionRecord[] } = await response.json();
      for (const session of body.data) {
        if (session.preview.includes(marker)) await closeSessionBestEffort(staffPage, session.id);
      }
    }
  } catch {
    // Phản hồi không phải JSON hoặc mất kết nối không được che lỗi của case, và `finally` của case vẫn đóng context.
  }
}

// ---- Yêu cầu gọi lại ----

export interface CallbackRecord {
  id: string;
  phone: string | null;
  note: string | null;
  status: 'pending' | 'done' | 'unreachable';
}

// Số điện thoại hợp lệ và khác nhau giữa các case, để dòng của case tìm được bằng số.
export function makeUniquePhone(): string {
  const tail = String(Math.floor(Math.random() * 90_000_000) + 10_000_000);
  return `09${tail}`;
}

// Khách ẩn danh để lại số điện thoại. Hội thoại chưa có phiên chờ nên backend không đóng phiên nào.
export async function createCallbackByApi(phone: string, note: string | null): Promise<void> {
  const customer = await openAnonymousCustomerChat();
  try {
    const response = await customer.request.post('/api/support/callback-requests', {
      headers: SUPPORT_API_HEADERS,
      data: { conversationId: customer.conversationId, phone, note },
    });
    expect(response.status(), await response.text()).toBe(201);
  } finally {
    await customer.dispose();
  }
}

// Đọc danh sách yêu cầu gọi lại của backend. Backend trả 200 khi đọc thành công.
export async function listCallbacksByApi(staffPage: Page): Promise<CallbackRecord[]> {
  const response = await staffPage.request.get(CALLBACKS_PATH);
  expect(response.status(), await response.text()).toBe(200);
  const body: { data: CallbackRecord[] } = await response.json();
  return body.data;
}

// Đánh dấu yêu cầu bằng API. Backend trả 200 và dòng đã cập nhật, nên mã khác là lỗi dựng dữ liệu.
export async function markCallbackByApi(
  staffPage: Page,
  callbackId: string,
  status: 'done' | 'unreachable',
): Promise<void> {
  const response = await staffPage.request.patch(`${CALLBACKS_PATH}/${callbackId}`, {
    headers: SUPPORT_API_HEADERS,
    data: { status },
  });
  expect(response.status(), await response.text()).toBe(200);
}

// Đưa mọi yêu cầu đang chờ về `done`. Sau đó danh sách "Cần gọi lại" chỉ còn dòng mà case dựng, nên số dòng đếm được.
export async function resolveAllPendingCallbacks(staffPage: Page): Promise<void> {
  const callbacks = await listCallbacksByApi(staffPage);
  for (const callback of callbacks) {
    if (callback.status === 'pending') await markCallbackByApi(staffPage, callback.id, 'done');
  }
}

// ---- Giờ làm việc ----

const SETTINGS_PATH = '/api/admin/support/settings';

// Dạng body của `PUT /api/admin/support/settings`. Trường `updatedAt` của phản hồi `GET` không nằm trong body.
export interface SupportSettingsBody {
  opensAt: string;
  closesAt: string;
  holidays: string[];
  waitNudgeMinutes: number;
  autoCloseMinutes: number;
}

// Đọc cấu hình hiện tại của backend để spec trả đúng giá trị cũ khi dọn dẹp.
export async function readSupportSettingsByApi(page: Page): Promise<SupportSettingsBody> {
  const response = await page.request.get(SETTINGS_PATH);
  expect(response.status(), await response.text()).toBe(200);
  const body: { data: SupportSettingsBody & { updatedAt: string } } = await response.json();
  const { opensAt, closesAt, holidays, waitNudgeMinutes, autoCloseMinutes } = body.data;
  return { opensAt, closesAt, holidays, waitNudgeMinutes, autoCloseMinutes };
}

// Lưu cấu hình bằng API. Backend trả 200 khi lưu thành công, nên mã khác là lỗi dựng dữ liệu.
export async function saveSupportSettingsByApi(
  page: Page,
  settings: SupportSettingsBody,
): Promise<void> {
  const response = await page.request.put(SETTINGS_PATH, {
    headers: SUPPORT_API_HEADERS,
    data: settings,
  });
  expect(response.status(), await response.text()).toBe(200);
}
