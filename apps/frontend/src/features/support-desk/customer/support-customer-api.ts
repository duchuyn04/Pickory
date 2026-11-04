import { apiRequest } from '../../../shared/api';
import {
  parseSupportConversation,
  parseSupportMessage,
  parseSupportMessagePage,
  type SupportConversation,
  type SupportMessage,
  type SupportMessagePage,
} from './support-customer-schemas';

// Trang chỉ tải trang mới nhất của lịch sử. Nút tải tin cũ hơn thuộc task khác.
const MESSAGE_PAGE_LIMIT = 50;

export function supportCustomerEventsPath(conversationId: string): string {
  return `/api/support/conversations/${conversationId}/events`;
}

// Route lấy hoặc tạo: gọi lại sau mỗi lần luồng SSE mở để nhận trạng thái phiên mới nhất.
export async function fetchOrCreateSupportConversation(): Promise<SupportConversation> {
  const data = await apiRequest<unknown>('/api/support/conversations', { method: 'POST' });
  return parseSupportConversation(data);
}

export async function fetchSupportMessages(conversationId: string): Promise<SupportMessagePage> {
  const data = await apiRequest<unknown>(
    `/api/support/conversations/${conversationId}/messages?limit=${MESSAGE_PAGE_LIMIT}`,
  );
  return parseSupportMessagePage(data);
}

export async function sendSupportMessage(
  conversationId: string,
  body: string,
): Promise<SupportMessage> {
  const data = await apiRequest<unknown>(`/api/support/conversations/${conversationId}/messages`, {
    method: 'POST',
    body: JSON.stringify({ body }),
  });
  return parseSupportMessage(data);
}
