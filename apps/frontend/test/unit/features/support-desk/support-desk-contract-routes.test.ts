import type { Page, Route } from '@playwright/test';
import { describe, expect, it } from 'vitest';
import { mockEventStream } from '../../../../e2e/support-desk-contract-routes';

// Helper E2E chạy thật trong Playwright. Test ở đây thay `page` và `route` bằng bản giả để kiểm cách gắn route.
const API_ORIGIN = 'http://127.0.0.1:3101';
const ADMIN_EVENTS_PATH = '/api/admin/support/events';

interface FakeFulfilledResponse {
  status?: number;
  headers?: Record<string, string>;
  body?: string;
}

interface RegisteredRoute {
  matchesUrl: (url: URL) => boolean;
  handle: (route: Route) => Promise<unknown> | unknown;
}

function createFakePage(): { registeredRoutes: RegisteredRoute[]; page: Page } {
  const registeredRoutes: RegisteredRoute[] = [];
  const page = {
    route: async (matchesUrl: (url: URL) => boolean, handle: (route: Route) => unknown) => {
      registeredRoutes.push({ matchesUrl, handle });
    },
  };
  return { registeredRoutes, page: page as unknown as Page };
}

function createFakeRoute(method: string) {
  const fulfilled: FakeFulfilledResponse[] = [];
  let fallbackCount = 0;
  const route = {
    request: () => ({ method: () => method }),
    fulfill: async (response: FakeFulfilledResponse) => {
      fulfilled.push(response);
    },
    fallback: async () => {
      fallbackCount += 1;
    },
  };
  return { route: route as unknown as Route, fulfilled, fallbackCount: () => fallbackCount };
}

describe('route stream SSE', () => {
  it('trả text/event-stream chứa frame của sự kiện được chọn', async () => {
    const fakePage = createFakePage();
    await mockEventStream(fakePage.page, {
      path: ADMIN_EVENTS_PATH,
      fixtureFile: 'admin-events.json',
      eventNames: ['session.waiting', 'session.closed'],
    });

    const [registered] = fakePage.registeredRoutes;
    expect(registered?.matchesUrl(new URL(`${API_ORIGIN}${ADMIN_EVENTS_PATH}`))).toBe(true);
    const fakeRoute = createFakeRoute('GET');
    await registered?.handle(fakeRoute.route);

    expect(fakeRoute.fulfilled).toHaveLength(1);
    expect(fakeRoute.fulfilled[0]?.status).toBe(200);
    expect(fakeRoute.fulfilled[0]?.headers?.['content-type']).toContain('text/event-stream');
    expect(fakeRoute.fulfilled[0]?.body).toBe(
      'event: session.waiting\ndata: {"sessionId":"5e550000-0000-4000-8000-000000000001"}\n\n' +
        'event: session.closed\ndata: {"sessionId":"5e550000-0000-4000-8000-000000000001"}\n\n',
    );
  });

  it('chuyển request khác GET cho backend thật và không khớp đường dẫn khác', async () => {
    const fakePage = createFakePage();
    await mockEventStream(fakePage.page, {
      path: ADMIN_EVENTS_PATH,
      fixtureFile: 'admin-events.json',
      eventNames: ['heartbeat'],
    });

    const [registered] = fakePage.registeredRoutes;
    expect(registered?.matchesUrl(new URL(`${API_ORIGIN}/api/admin/support/sessions`))).toBe(false);
    const fakeRoute = createFakeRoute('POST');
    await registered?.handle(fakeRoute.route);

    expect(fakeRoute.fulfilled).toHaveLength(0);
    expect(fakeRoute.fallbackCount()).toBe(1);
  });

  it('báo lỗi khi tên sự kiện không có trong file mẫu', async () => {
    const fakePage = createFakePage();

    await expect(
      mockEventStream(fakePage.page, {
        path: ADMIN_EVENTS_PATH,
        fixtureFile: 'admin-events.json',
        eventNames: ['khong.co'],
      }),
    ).rejects.toThrow('Không có sự kiện "khong.co" trong file mẫu "admin-events.json".');
  });
});
