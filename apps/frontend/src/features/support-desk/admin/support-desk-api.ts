import { apiRequest } from '../../../shared/api';
import {
  parseCallbackRequests,
  parseStaffPresence,
  parseStaffPresenceList,
  parseQueueSessions,
  type CallbackRequest,
  type PresenceStatus,
  type QueueSession,
  type StaffPresence,
} from './support-desk-schemas';

export const SUPPORT_EVENTS_URL = '/api/admin/support/events';

// Mọi query của màn CSKH nằm dưới khóa này. Một lệnh `invalidateQueries` đọc lại cả trạng thái và danh sách.
export const SUPPORT_QUERY_KEY = ['support'] as const;
export const PRESENCE_QUERY_KEY = [...SUPPORT_QUERY_KEY, 'presence'] as const;
export const SESSIONS_QUERY_KEY = [...SUPPORT_QUERY_KEY, 'sessions'] as const;
export const CALLBACKS_QUERY_KEY = [...SUPPORT_QUERY_KEY, 'callback-requests'] as const;

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
