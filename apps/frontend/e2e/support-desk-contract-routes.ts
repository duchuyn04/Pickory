import type { Page, Route } from '@playwright/test';
import {
  selectSupportDeskEvents,
  type ContractEvent,
} from '../test/unit/features/support-desk/contract-fixtures';

// Chỉ còn luồng sự kiện SSE (Server-Sent Events) được giả, vì E2E không điều khiển được thời điểm backend phát sự kiện.
// Mọi route JSON của màn chăm sóc khách hàng và trang `/support` đã có backend thật.

export interface EventStreamMockOptions {
  path: string;
  fixtureFile: string;
  eventNames: readonly string[];
}

export async function mockEventStream(page: Page, options: EventStreamMockOptions): Promise<void> {
  const events = selectSupportDeskEvents(options.fixtureFile, options.eventNames);
  const streamPath = normalizePathname(parsePath(options.path).pathname);
  await page.route(
    (url) => normalizePathname(url.pathname) === streamPath,
    (route) => fulfillEventStream(route, events),
  );
}

// Đường dẫn của ví dụ luôn bắt đầu bằng `/`. Origin giả đủ để `URL` tách pathname và query.
function parsePath(path: string): URL {
  return new URL(path, 'http://support-desk.contract');
}

function normalizePathname(pathname: string): string {
  return pathname.length > 1 && pathname.endsWith('/') ? pathname.slice(0, -1) : pathname;
}

async function fulfillEventStream(route: Route, events: readonly ContractEvent[]): Promise<void> {
  // Request khác GET thì để backend thật nhận.
  if (route.request().method().toUpperCase() !== 'GET') {
    await route.fallback();
    return;
  }
  const body = events
    // Mỗi sự kiện thành một frame SSE. Dòng trống kết thúc frame, nên client nhận sự kiện ngay.
    .map((event) => `event: ${event.name}\ndata: ${JSON.stringify(event.data)}\n\n`)
    .join('');
  await route.fulfill({
    status: 200,
    headers: { 'content-type': 'text/event-stream', 'cache-control': 'no-store' },
    body,
  });
}
