import { apiRequest } from '../../../shared/api';
import {
  parseCallbackRequests,
  parseCallbackRequest,
  parseCannedResponse,
  parseCannedResponses,
  parseClosedSession,
  parseQueueSessions,
  parseSessionAssignment,
  parseSessionCustomer,
  parseStaffMessagePage,
  parseStaffPresence,
  parseStaffPresenceList,
  parseStaffViewMessage,
  parseSupportSettings,
  type CallbackRequest,
  type CallbackStatus,
  type CannedResponse,
  type ClosedSession,
  type PresenceStatus,
  type QueueSession,
  type SessionAssignment,
  type SessionCustomer,
  type StaffMessagePage,
  type StaffPresence,
  type StaffViewMessage,
  type SupportSettings,
} from './support-desk-schemas';

export const SUPPORT_EVENTS_URL = '/api/admin/support/events';

// Mọi query của màn CSKH nằm dưới khóa này. Một lệnh `invalidateQueries` đọc lại cả trạng thái và danh sách.
export const SUPPORT_QUERY_KEY = ['support'] as const;
export const PRESENCE_QUERY_KEY = [...SUPPORT_QUERY_KEY, 'presence'] as const;
export const SESSIONS_QUERY_KEY = [...SUPPORT_QUERY_KEY, 'sessions'] as const;
export const CALLBACKS_QUERY_KEY = [...SUPPORT_QUERY_KEY, 'callback-requests'] as const;
export const CANNED_RESPONSES_QUERY_KEY = [...SUPPORT_QUERY_KEY, 'canned-responses'] as const;
export const SUPPORT_SETTINGS_QUERY_KEY = [...SUPPORT_QUERY_KEY, 'settings'] as const;

// Khóa query của lịch sử tin theo hội thoại. Sự kiện `message.created` chỉ đọc lại đúng khóa này.
export function staffMessagesQueryKey(conversationId: string): readonly unknown[] {
  return [...SUPPORT_QUERY_KEY, 'messages', conversationId];
}

// Trang đầu của lịch sử tin. Nút tải tin cũ hơn bằng `cursor` thuộc task khác.
const MESSAGE_PAGE_LIMIT = 50;

export type SessionView = 'waiting' | 'mine';

export async function fetchAvailableStaff(): Promise<StaffPresence[]> {
  return parseStaffPresenceList(await apiRequest<unknown>('/api/admin/support/presence'));
}

export async function putPresence(status: PresenceStatus): Promise<StaffPresence> {
  const data = await apiRequest<unknown>('/api/admin/support/presence', {
    method: 'PUT',
    body: JSON.stringify({ status }),
  });
  return parseStaffPresence(data);
}

export async function fetchSessions(view: SessionView): Promise<QueueSession[]> {
  return parseQueueSessions(await apiRequest<unknown>(`/api/admin/support/sessions?view=${view}`));
}

export async function fetchCallbackRequests(): Promise<CallbackRequest[]> {
  return parseCallbackRequests(await apiRequest<unknown>('/api/admin/support/callback-requests'));
}

export async function fetchStaffMessages(conversationId: string): Promise<StaffMessagePage> {
  const data = await apiRequest<unknown>(
    `/api/admin/support/conversations/${conversationId}/messages?limit=${MESSAGE_PAGE_LIMIT}`,
  );
  return parseStaffMessagePage(data);
}

// Route nhận phiên không có body. Chống bấm đôi nằm ở component gọi hàm này.
export async function claimSupportSession(sessionId: string): Promise<SessionAssignment> {
  const data = await apiRequest<unknown>(`/api/admin/support/sessions/${sessionId}/assignment`, {
    method: 'POST',
  });
  return parseSessionAssignment(data);
}

export async function sendStaffMessage(sessionId: string, body: string): Promise<StaffViewMessage> {
  const data = await apiRequest<unknown>(`/api/admin/support/sessions/${sessionId}/messages`, {
    method: 'POST',
    body: JSON.stringify({ body }),
  });
  return parseStaffViewMessage(data);
}

export async function fetchCannedResponses(): Promise<CannedResponse[]> {
  return parseCannedResponses(await apiRequest<unknown>('/api/admin/support/canned-responses'));
}

// Đọc dữ liệu bảng thông tin khách theo mã phiên.
export async function fetchSessionCustomer(sessionId: string): Promise<SessionCustomer> {
  const data = await apiRequest<unknown>(`/api/admin/support/sessions/${sessionId}/customer`);
  return parseSessionCustomer(data);
}

// Chuyển phiên hỗ trợ cho nhân viên khác. Ghi chú nội bộ là chuỗi hoặc `null`.
export async function transferSupportSession(
  sessionId: string,
  staffId: string,
  note: string | null,
): Promise<SessionAssignment> {
  const data = await apiRequest<unknown>(`/api/admin/support/sessions/${sessionId}/assignment`, {
    method: 'PUT',
    body: JSON.stringify({ staffId, note }),
  });
  return parseSessionAssignment(data);
}

// Kết thúc phiên hỗ trợ đang mở. Thao tác gửi yêu cầu đóng phiên với trạng thái `closed`.
export async function closeSupportSession(sessionId: string): Promise<ClosedSession> {
  const data = await apiRequest<unknown>(`/api/admin/support/sessions/${sessionId}`, {
    method: 'PATCH',
    body: JSON.stringify({ status: 'closed' }),
  });
  return parseClosedSession(data);
}

// Hai trạng thái nhân viên được đặt. `pending` chỉ do máy chủ tạo ra.
export type HandledCallbackStatus = Exclude<CallbackStatus, 'pending'>;

// Đánh dấu một yêu cầu gọi lại đã gọi hoặc không liên lạc được. Chống bấm đôi nằm ở component gọi hàm này.
export async function updateCallbackRequestStatus(
  requestId: string,
  status: HandledCallbackStatus,
): Promise<CallbackRequest> {
  const data = await apiRequest<unknown>(`/api/admin/support/callback-requests/${requestId}`, {
    method: 'PATCH',
    body: JSON.stringify({ status }),
  });
  return parseCallbackRequest(data);
}

// Cấu hình chăm sóc khách hàng gửi lên: mọi trường của cấu hình trừ `updatedAt`, vì thời điểm cập nhật do máy chủ đặt.
export type SupportSettingsInput = Omit<SupportSettings, 'updatedAt'>;

export async function fetchSupportSettings(): Promise<SupportSettings> {
  return parseSupportSettings(await apiRequest<unknown>('/api/admin/support/settings'));
}

// `PUT` thay toàn bộ cấu hình bằng một lần gửi. Chống bấm đôi nằm ở component gọi hàm này.
export async function saveSupportSettings(input: SupportSettingsInput): Promise<SupportSettings> {
  const data = await apiRequest<unknown>('/api/admin/support/settings', {
    method: 'PUT',
    body: JSON.stringify(input),
  });
  return parseSupportSettings(data);
}

export interface CreateCannedResponseInput {
  shortcut: string;
  body: string;
  // `true` cho mẫu dùng chung, `false` cho mẫu cá nhân của người gọi.
  shared: boolean;
}

export async function createCannedResponse(
  input: CreateCannedResponseInput,
): Promise<CannedResponse> {
  const data = await apiRequest<unknown>('/api/admin/support/canned-responses', {
    method: 'POST',
    body: JSON.stringify(input),
  });
  return parseCannedResponse(data);
}

// `PATCH` chỉ nhận trường đã đổi. Hợp đồng cho phép gửi `shortcut`, `body` hoặc cả hai.
export interface UpdateCannedResponseInput {
  shortcut?: string;
  body?: string;
}

export async function updateCannedResponse(
  id: string,
  changes: UpdateCannedResponseInput,
): Promise<CannedResponse> {
  const data = await apiRequest<unknown>(`/api/admin/support/canned-responses/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(changes),
  });
  return parseCannedResponse(data);
}

// Route trả 204 không có thân. `apiRequest` coi 204 là thành công.
export async function deleteCannedResponse(id: string): Promise<void> {
  await apiRequest<void>(`/api/admin/support/canned-responses/${id}`, { method: 'DELETE' });
}
