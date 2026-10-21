import type { Page } from '@playwright/test';

// Dữ liệu dùng chung của các spec activity-tracking. Gồm bộ ghi request lượt xem của trang.

export interface EventRequest {
  body: string | null;
  status: number | null;
}

// Ghi mọi request `POST /api/events` của trang, kể cả request không có response (bị chặn hoặc mất mạng).
export function trackEventRequests(page: Page): EventRequest[] {
  const events: EventRequest[] = [];
  const byRequest = new Map<unknown, EventRequest>();
  page.on('request', (request) => {
    if (request.method() !== 'POST' || new URL(request.url()).pathname !== '/api/events') return;
    const entry: EventRequest = { body: request.postData(), status: null };
    byRequest.set(request, entry);
    events.push(entry);
  });
  page.on('response', (response) => {
    const entry = byRequest.get(response.request());
    if (entry) entry.status = response.status();
  });
  return events;
}
