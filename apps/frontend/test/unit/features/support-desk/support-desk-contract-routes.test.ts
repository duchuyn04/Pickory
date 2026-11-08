import type { Page, Route } from '@playwright/test';
import { beforeEach, describe, expect, it } from 'vitest';
import {
  createSupportDeskRouteMocks,
  resetSupportDeskOpenApiCache,
} from '../../../../e2e/support-desk-contract-routes';

// Helper E2E chạy thật trong Playwright. Test ở đây thay `page` và `route` bằng bản giả để kiểm cách gắn route.
const API_ORIGIN = 'http://127.0.0.1:3101';
const OPENAPI_URL = `${API_ORIGIN}/api/openapi/v1.json`;

interface FakeFulfilledResponse {
  status?: number;
  contentType?: string;
  headers?: Record<string, string>;
  body?: string;
}

interface RegisteredRoute {
  matchesUrl: (url: URL) => boolean;
  handle: (route: Route) => Promise<unknown> | unknown;
}

interface FakePage {
  registeredRoutes: RegisteredRoute[];
  page: Page;
}

interface FakeRoute {
  route: Route;
  fulfilled: FakeFulfilledResponse[];
  fallbackCount: () => number;
}

interface OpenApiLoader {
  requestedUrls: string[];
  load: (url: string) => Promise<unknown>;
}

function createOpenApiLoader(document: unknown): OpenApiLoader {
  const requestedUrls: string[] = [];
  return {
    requestedUrls,
    load: async (url: string) => {
      requestedUrls.push(url);
      return document;
    },
  };
}

function createFakePage(): FakePage {
  const registeredRoutes: RegisteredRoute[] = [];
  const page = {
    route: async (matchesUrl: (url: URL) => boolean, handle: (route: Route) => unknown) => {
      registeredRoutes.push({ matchesUrl, handle });
    },
  };
  return { registeredRoutes, page: page as unknown as Page };
}

function createFakeRoute(method: string, url: string): FakeRoute {
  const fulfilled: FakeFulfilledResponse[] = [];
  let fallbackCount = 0;
  const route = {
    request: () => ({ method: () => method, url: () => url }),
    fulfill: async (response: FakeFulfilledResponse) => {
      fulfilled.push(response);
    },
    fallback: async () => {
      fallbackCount += 1;
    },
  };
  return { route: route as unknown as Route, fulfilled, fallbackCount: () => fallbackCount };
}

// Chạy request qua các route đã đăng ký theo thứ tự ngược như Playwright. Route chuyển tiếp thì thử route kế tiếp.
async function dispatchRequest(
  fakePage: FakePage,
  method: string,
  url: string,
): Promise<FakeRoute> {
  const fakeRoute = createFakeRoute(method, url);
  const targetUrl = new URL(url);
  for (const registeredRoute of [...fakePage.registeredRoutes].reverse()) {
    if (!registeredRoute.matchesUrl(targetUrl)) continue;
    const previousFallbackCount = fakeRoute.fallbackCount();
    await registeredRoute.handle(fakeRoute.route);
    if (fakeRoute.fulfilled.length > 0) break;
    if (fakeRoute.fallbackCount() === previousFallbackCount) break;
  }
  return fakeRoute;
}

function readResponseBody(fakeRoute: FakeRoute): unknown {
  const [response] = fakeRoute.fulfilled;
  expect(response?.body).toBeDefined();
  return JSON.parse(response?.body ?? 'null') as unknown;
}

describe('route giả theo ví dụ trong file mẫu', () => {
  beforeEach(() => {
    resetSupportDeskOpenApiCache();
  });

  it('trả status và response của ví dụ cho route khớp method và đường dẫn', async () => {
    const fakePage = createFakePage();
    const loader = createOpenApiLoader({ paths: {} });
    const routes = createSupportDeskRouteMocks({
      page: fakePage.page,
      apiOrigin: API_ORIGIN,
      loadOpenApiDocument: loader.load,
    });
    const callbackPath =
      '/api/admin/support/callback-requests/cb000000-0000-4000-8000-000000000001';

    await routes.mockRoute('admin-callbacks.json', 'đánh dấu đã gọi');

    const response = await dispatchRequest(fakePage, 'PATCH', `${API_ORIGIN}${callbackPath}`);

    expect(response.fulfilled).toHaveLength(1);
    expect(response.fulfilled[0]?.status).toBe(200);
    expect(readResponseBody(response)).toMatchObject({
      data: { id: 'cb000000-0000-4000-8000-000000000001', status: 'done' },
    });
  });

  it('chuyển tiếp request khác method, để route khác hoặc backend thật nhận', async () => {
    const fakePage = createFakePage();
    const routes = createSupportDeskRouteMocks({
      page: fakePage.page,
      apiOrigin: API_ORIGIN,
      loadOpenApiDocument: createOpenApiLoader({ paths: {} }).load,
    });

    await routes.mockRoute('admin-callbacks.json', 'đánh dấu đã gọi');

    const response = await dispatchRequest(
      fakePage,
      'DELETE',
      `${API_ORIGIN}/api/admin/support/callback-requests/cb000000-0000-4000-8000-000000000001`,
    );

    expect(response.fulfilled).toEqual([]);
    expect(response.fallbackCount()).toBe(1);
  });

  it('chọn đúng ví dụ khi hai ví dụ cùng đường dẫn nhưng khác query', async () => {
    const fakePage = createFakePage();
    const routes = createSupportDeskRouteMocks({
      page: fakePage.page,
      apiOrigin: API_ORIGIN,
      loadOpenApiDocument: createOpenApiLoader({ paths: {} }).load,
    });

    await routes.mockRoute('admin-sessions.json', 'hàng chờ');
    await routes.mockRoute('admin-sessions.json', 'phiên của tôi sau khi đồng nghiệp chuyển');

    const waiting = await dispatchRequest(
      fakePage,
      'GET',
      `${API_ORIGIN}/api/admin/support/sessions?view=waiting`,
    );
    const mine = await dispatchRequest(
      fakePage,
      'GET',
      `${API_ORIGIN}/api/admin/support/sessions?view=mine`,
    );

    expect(readResponseBody(waiting)).toMatchObject({
      data: [{ preview: 'Đơn PK123 của mình giao tới đâu rồi?' }],
    });
    expect(readResponseBody(mine)).toMatchObject({
      data: [{ transferNote: 'Khách cần đổi size, đơn PK098 đã giao.' }],
    });
  });

  it('báo lỗi khi tài liệu OpenAPI đã có route, kèm câu gỡ dữ liệu giả', async () => {
    const fakePage = createFakePage();
    const routes = createSupportDeskRouteMocks({
      page: fakePage.page,
      apiOrigin: API_ORIGIN,
      loadOpenApiDocument: createOpenApiLoader({
        paths: { '/api/support/conversations': { post: {} } },
      }).load,
    });

    await expect(routes.mockRoute('customer-api.json', 'tạo hội thoại lần đầu')).rejects.toThrow(
      'Backend đã có `POST /api/support/conversations`. Gỡ dữ liệu giả của route này.',
    );
    expect(fakePage.registeredRoutes).toHaveLength(0);
  });

  it('dùng đúng câu của tiêu chí cho route gọi lại khi tài liệu đã có route', async () => {
    const fakePage = createFakePage();
    const routes = createSupportDeskRouteMocks({
      page: fakePage.page,
      apiOrigin: API_ORIGIN,
      loadOpenApiDocument: createOpenApiLoader({
        paths: { '/api/support/callback-requests': { post: {} } },
      }).load,
    });

    await expect(
      routes.mockRoute('customer-api.json', 'khách để lại số điện thoại'),
    ).rejects.toThrow(
      'Backend đã có `POST /api/support/callback-requests`. Gỡ dữ liệu giả của route này.',
    );
  });

  it('so đường dẫn cụ thể với đường dẫn mẫu của OpenAPI', async () => {
    const fakePage = createFakePage();
    const routes = createSupportDeskRouteMocks({
      page: fakePage.page,
      apiOrigin: API_ORIGIN,
      loadOpenApiDocument: createOpenApiLoader({
        paths: { '/api/support/conversations/{id}/csat': { put: {} } },
      }).load,
    });

    await expect(
      routes.mockRoute('customer-api.json', 'khách chấm điểm phiên vừa kết thúc'),
    ).rejects.toThrow(
      'Backend đã có `PUT /api/support/conversations/{id}/csat`. Gỡ dữ liệu giả của route này.',
    );
  });

  it('chỉ chặn route trùng method, không chặn method khác của cùng đường dẫn', async () => {
    const fakePage = createFakePage();
    const routes = createSupportDeskRouteMocks({
      page: fakePage.page,
      apiOrigin: API_ORIGIN,
      loadOpenApiDocument: createOpenApiLoader({
        paths: { '/api/admin/support/callback-requests': { post: {} } },
      }).load,
    });

    await routes.mockRoute('admin-callbacks.json', 'danh sách yêu cầu gọi lại');

    expect(fakePage.registeredRoutes).toHaveLength(1);
  });

  it('đọc tài liệu OpenAPI một lần cho mọi route giả trong cùng lần chạy', async () => {
    const firstPage = createFakePage();
    const secondPage = createFakePage();
    const loader = createOpenApiLoader({ paths: {} });

    const firstRoutes = createSupportDeskRouteMocks({
      page: firstPage.page,
      apiOrigin: API_ORIGIN,
      loadOpenApiDocument: loader.load,
    });
    const secondRoutes = createSupportDeskRouteMocks({
      page: secondPage.page,
      apiOrigin: API_ORIGIN,
      loadOpenApiDocument: loader.load,
    });

    await firstRoutes.mockRoute('admin-sessions.json', 'hàng chờ');
    await firstRoutes.mockRoute('admin-callbacks.json', 'danh sách yêu cầu gọi lại');
    await secondRoutes.mockRoute('admin-sessions.json', 'phiên của tôi sau khi đồng nghiệp chuyển');

    expect(loader.requestedUrls).toEqual([OPENAPI_URL]);
  });
});

describe('route stream SSE', () => {
  beforeEach(() => {
    resetSupportDeskOpenApiCache();
  });

  it('trả text/event-stream chứa frame của sự kiện được chọn', async () => {
    const fakePage = createFakePage();
    const loader = createOpenApiLoader({ paths: {} });
    const routes = createSupportDeskRouteMocks({
      page: fakePage.page,
      apiOrigin: API_ORIGIN,
      loadOpenApiDocument: loader.load,
    });

    await routes.mockEventStream({
      path: '/api/admin/support/events',
      fixtureFile: 'admin-events.json',
      eventNames: ['session.waiting', 'session.closed'],
    });

    const response = await dispatchRequest(
      fakePage,
      'GET',
      `${API_ORIGIN}/api/admin/support/events`,
    );

    expect(response.fulfilled).toHaveLength(1);
    expect(response.fulfilled[0]?.status).toBe(200);
    expect(response.fulfilled[0]?.headers?.['content-type']).toContain('text/event-stream');
    expect(response.fulfilled[0]?.body).toBe(
      'event: session.waiting\ndata: {"sessionId":"5e550000-0000-4000-8000-000000000001"}\n\n' +
        'event: session.closed\ndata: {"sessionId":"5e550000-0000-4000-8000-000000000001"}\n\n',
    );
  });

  it('không đọc tài liệu OpenAPI, vì route events đã có ở backend', async () => {
    const fakePage = createFakePage();
    const loader = createOpenApiLoader({ paths: {} });
    const routes = createSupportDeskRouteMocks({
      page: fakePage.page,
      apiOrigin: API_ORIGIN,
      loadOpenApiDocument: loader.load,
    });

    await routes.mockEventStream({
      path: '/api/admin/support/events',
      fixtureFile: 'admin-events.json',
      eventNames: ['heartbeat'],
    });

    expect(loader.requestedUrls).toEqual([]);
  });

  it('báo lỗi khi tên sự kiện không có trong file mẫu', async () => {
    const fakePage = createFakePage();
    const routes = createSupportDeskRouteMocks({
      page: fakePage.page,
      apiOrigin: API_ORIGIN,
      loadOpenApiDocument: createOpenApiLoader({ paths: {} }).load,
    });

    await expect(
      routes.mockEventStream({
        path: '/api/admin/support/events',
        fixtureFile: 'admin-events.json',
        eventNames: ['khong.co'],
      }),
    ).rejects.toThrow('Không có sự kiện "khong.co" trong file mẫu "admin-events.json".');
  });
});

describe('mockRoute với responseOverride', () => {
  it('trả thân phản hồi ghi đè khi được cung cấp', async () => {
    const fakePage = createFakePage();
    const routes = createSupportDeskRouteMocks({
      page: fakePage.page,
      apiOrigin: API_ORIGIN,
      loadOpenApiDocument: createOpenApiLoader({ paths: {} }).load,
    });

    const customData = { items: [{ id: 'custom-1' }], nextCursor: null };
    await routes.mockRoute('admin-sessions.json', 'lịch sử tin của hội thoại cho nhân viên', {
      responseOverride: { data: customData },
    });

    const dispatched = await dispatchRequest(
      fakePage,
      'GET',
      'http://localhost/api/admin/support/conversations/c0a80000-0000-4000-8000-000000000001/messages?limit=50',
    );

    expect(dispatched.fulfilled).toEqual([
      {
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: customData }),
      },
    ]);
  });
});
