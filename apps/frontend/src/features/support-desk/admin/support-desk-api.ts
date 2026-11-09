import { apiRequest } from '../../../shared/api';
import {
  parseCallbackRequests,
  parseCannedResponses,
  parseClosedSession,
  parseQueueSessions,
  parseSessionAssignment,
  parseSessionCustomer,
  parseStaffMessagePage,
  parseStaffPresence,
  parseStaffPresenceList,
  parseStaffViewMessage,
  type CallbackRequest,
  type CannedResponse,
  type ClosedSession,
  type PresenceStatus,
  type QueueSession,
  type SessionAssignment,
  type SessionCustomer,
  type StaffMessagePage,
  type StaffPresence,
  type StaffViewMessage,
} from './support-desk-schemas';

export const SUPPORT_EVENTS_URL = '/api/admin/support/events';

// Mọi query của màn CSKH nằm dưới khóa này. Một lệnh `invalidateQueries` đọc lại cả trạng thái và danh sách.
export const SUPPORT_QUERY_KEY = ['support'] as const;
export const PRESENCE_QUERY_KEY = [...SUPPORT_QUERY_KEY, 'presence'] as const;
export const SESSIONS_QUERY_KEY = [...SUPPORT_QUERY_KEY, 'sessions'] as const;
export const CALLBACKS_QUERY_KEY = [...SUPPORT_QUERY_KEY, 'callback-requests'] as const;
export const CANNED_RESPONSES_QUERY_KEY = [...SUPPORT_QUERY_KEY, 'canned-responses'] as const;

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
