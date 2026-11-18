import type { Page, Route } from '@playwright/test';
import { describe, expect, it, vi } from 'vitest';
import {
  assertBackendLacksRoute,
  buildFixtureResponse,
  createOpenApiDocumentCache,
  installFixtureRoute,
} from '../../../../e2e/fixture-route';
import { parseOpenApiDocument, type OpenApiDocument } from '../../../../e2e/openapi-routes';
import { findExportsPrintingExample } from './contract-fixtures';

const OPENAPI_URL = 'http://127.0.0.1:4173/api/openapi/v1.json';
const FIXTURE_ONLY_PATH = '/api/admin/exports/fixture-only-route';

const backendDocument = parseOpenApiDocument(
  {
    paths: {
      '/api/admin/orders': { get: {} },
      '/api/admin/orders/{code}': { get: {}, put: {} },
    },
  },
  OPENAPI_URL,
);

interface FakeFulfilledResponse {
  status?: number;
  headers?: Record<string, string>;
  body?: string | Buffer;
}

interface RegisteredRoute {
  matchesUrl: (url: URL) => boolean;
  handle: (route: Route) => Promise<unknown> | unknown;
}

interface FakeOpenApiResponse {
  ok: boolean;
  status: number;
  body: unknown;
  invalidJson?: boolean;
}

// Trang giả ghi lại số lần đọc OpenAPI và các route đã cài. Playwright thật không chạy trong Vitest.
function createFakePage(openApiResponse: FakeOpenApiResponse) {
  const registeredRoutes: RegisteredRoute[] = [];
  const requestedUrls: string[] = [];
  const page = {
    request: {
      get: async (url: string) => {
        requestedUrls.push(url);
        return {
          ok: () => openApiResponse.ok,
          status: () => openApiResponse.status,
          url: () => `http://127.0.0.1:4173${url}`,
          json: async () => {
            // Trang HTML của proxy làm `response.json()` ném `SyntaxError` giống `JSON.parse` thật.
            if (openApiResponse.invalidJson) throw new SyntaxError('Unexpected token <');
            return openApiResponse.body;
          },
        };
      },
    },
    route: async (matchesUrl: (url: URL) => boolean, handle: (route: Route) => unknown) => {
      registeredRoutes.push({ matchesUrl, handle });
    },
  };
  return { registeredRoutes, requestedUrls, page: page as unknown as Page };
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

const documentWithOrdersPath = { paths: { '/api/admin/orders': { get: {} } } };

describe('kiểm OpenAPI trước khi cài route giả', () => {
  it('ném lỗi đúng chữ khi backend đã có route', () => {
    expect(() => assertBackendLacksRoute(backendDocument, 'GET', '/api/admin/orders')).toThrow(
      'Backend đã có `GET /api/admin/orders`. Bỏ dữ liệu giả của route này.',
    );
  });

  it('ghi phương thức viết hoa và giữ nguyên đường dẫn đã truyền trong thông báo', () => {
    expect(() =>
      assertBackendLacksRoute(backendDocument, 'get', '/api/admin/orders/PK7KQ2M9XA?a=1'),
    ).toThrow(
      'Backend đã có `GET /api/admin/orders/PK7KQ2M9XA?a=1`. Bỏ dữ liệu giả của route này.',
    );
  });

  it('không ném lỗi khi cùng đường dẫn nhưng backend chưa có phương thức đó', () => {
    expect(() =>
      assertBackendLacksRoute(backendDocument, 'POST', '/api/admin/orders'),
    ).not.toThrow();
  });

  it('không ném lỗi khi đường dẫn chưa có trong backend', () => {
    expect(() => assertBackendLacksRoute(backendDocument, 'GET', FIXTURE_ONLY_PATH)).not.toThrow();
  });
});

describe('bộ nhớ đệm tài liệu OpenAPI', () => {
  it('chỉ đọc một lần dù gọi nhiều lần', async () => {
    const cache = createOpenApiDocumentCache();
    let readCount = 0;
    const read = async (): Promise<OpenApiDocument> => {
      readCount += 1;
      return backendDocument;
    };

    const first = await cache.load(read);
    const second = await cache.load(read);

    expect(readCount).toBe(1);
    expect(second).toBe(first);
  });

  it('chỉ đọc một lần khi hai lời gọi chạy cùng lúc', async () => {
    const cache = createOpenApiDocumentCache();
    let readCount = 0;
    const read = async (): Promise<OpenApiDocument> => {
      readCount += 1;
      return backendDocument;
    };

    await Promise.all([cache.load(read), cache.load(read)]);

    expect(readCount).toBe(1);
  });

  it('không giữ kết quả lỗi, lần gọi sau đọc lại', async () => {
    const cache = createOpenApiDocumentCache();
    let readCount = 0;
    const read = async (): Promise<OpenApiDocument> => {
      readCount += 1;
      if (readCount === 1) throw new Error('mạng lỗi');
      return backendDocument;
    };

    await expect(cache.load(read)).rejects.toThrow('mạng lỗi');
    const document = await cache.load(read);

    expect(readCount).toBe(2);
    expect(document).toBe(backendDocument);
  });
});

describe('dựng response từ ví dụ', () => {
  it('ví dụ có response: giữ status, trả JSON UTF-8', () => {
    const example = findExportsPrintingExample('exports.json', 'orders export too large');

    const built = buildFixtureResponse(example);

    expect(built.status).toBe(422);
    expect(built.headers).toEqual({ 'content-type': 'application/json; charset=utf-8' });
    expect(built.body).toBe(JSON.stringify(example.response));
  });

  it('ví dụ file Excel: giữ header của ví dụ, body là đúng hai byte PK', () => {
    const example = findExportsPrintingExample('exports.json', 'orders export success');

    const built = buildFixtureResponse(example);

    expect(built.status).toBe(200);
    expect(built.headers).toEqual(example.headers);
    expect(Buffer.isBuffer(built.body)).toBe(true);
    expect([...(built.body as Buffer)]).toEqual([0x50, 0x4b]);
  });

  it('ví dụ không có response lẫn workbook thì báo lỗi nêu tên ví dụ', () => {
    const example = {
      name: 'vi du rong',
      method: 'GET',
      path: '/api/x',
      request: null,
      status: 204,
    };

    expect(() => buildFixtureResponse(example)).toThrow(
      'Ví dụ "vi du rong" không có "response" và không có "workbook".',
    );
  });

  it('ví dụ có workbook nhưng thiếu headers thì báo lỗi nêu tên ví dụ', () => {
    const example = {
      name: 'vi du thieu header',
      method: 'GET',
      status: 200,
      workbook: { sheets: [] },
    };

    expect(() => buildFixtureResponse(example)).toThrow(
      'Ví dụ "vi du thieu header" có "workbook" nhưng thiếu "headers".',
    );
  });
});

describe('installFixtureRoute', () => {
  it('từ chối khi backend đã có route, trước khi đọc fixture và trước khi gọi page.route', async () => {
    const fake = createFakePage({ ok: true, status: 200, body: documentWithOrdersPath });

    await expect(
      installFixtureRoute(
        fake.page,
        'GET',
        '/api/admin/orders',
        // Tên file sai chủ ý. Lỗi OpenAPI phải đến trước lỗi đọc fixture.
        'khong-co.json',
        'khong co',
        { openApiCache: createOpenApiDocumentCache() },
      ),
    ).rejects.toThrow('Backend đã có `GET /api/admin/orders`. Bỏ dữ liệu giả của route này.');
    expect(fake.registeredRoutes).toHaveLength(0);
  });

  it('báo lỗi nêu URL và status khi không đọc được OpenAPI', async () => {
    const fake = createFakePage({ ok: false, status: 503, body: null });

    await expect(
      installFixtureRoute(
        fake.page,
        'GET',
        FIXTURE_ONLY_PATH,
        'exports.json',
        'orders export too large',
        { openApiCache: createOpenApiDocumentCache() },
      ),
    ).rejects.toThrow(
      'Không đọc được OpenAPI tại http://127.0.0.1:4173/api/openapi/v1.json (status 503).',
    );
    expect(fake.registeredRoutes).toHaveLength(0);
  });

  it('route chưa có: cài route khớp pathname, bỏ qua query, trả status và body của ví dụ', async () => {
    const fake = createFakePage({ ok: true, status: 200, body: documentWithOrdersPath });

    await installFixtureRoute(
      fake.page,
      'GET',
      FIXTURE_ONLY_PATH,
      'exports.json',
      'orders export too large',
      { openApiCache: createOpenApiDocumentCache() },
    );

    expect(fake.requestedUrls).toEqual(['/api/openapi/v1.json']);
    const [registered] = fake.registeredRoutes;
    const origin = 'http://127.0.0.1:4173';
    expect(registered?.matchesUrl(new URL(`${origin}${FIXTURE_ONLY_PATH}?status=x`))).toBe(true);
    expect(registered?.matchesUrl(new URL(`${origin}${FIXTURE_ONLY_PATH}/`))).toBe(true);
    expect(registered?.matchesUrl(new URL(`${origin}/api/admin/exports/orders`))).toBe(false);
    const fakeRoute = createFakeRoute('GET');
    await registered?.handle(fakeRoute.route);
    expect(fakeRoute.fulfilled).toHaveLength(1);
    expect(fakeRoute.fulfilled[0]?.status).toBe(422);
    expect(fakeRoute.fulfilled[0]?.body).toContain('EXPORT_TOO_LARGE');
  });

  it('chuyển request khác phương thức cho backend thật', async () => {
    const fake = createFakePage({ ok: true, status: 200, body: documentWithOrdersPath });

    await installFixtureRoute(
      fake.page,
      'GET',
      FIXTURE_ONLY_PATH,
      'exports.json',
      'orders export too large',
      { openApiCache: createOpenApiDocumentCache() },
    );

    const fakeRoute = createFakeRoute('POST');
    await fake.registeredRoutes[0]?.handle(fakeRoute.route);
    expect(fakeRoute.fulfilled).toHaveLength(0);
    expect(fakeRoute.fallbackCount()).toBe(1);
  });

  it('dùng chung bộ nhớ đệm: lần cài thứ hai không đọc OpenAPI nữa', async () => {
    const fake = createFakePage({ ok: true, status: 200, body: documentWithOrdersPath });
    const openApiCache = createOpenApiDocumentCache();

    await installFixtureRoute(
      fake.page,
      'GET',
      FIXTURE_ONLY_PATH,
      'exports.json',
      'orders export too large',
      { openApiCache },
    );
    await installFixtureRoute(
      fake.page,
      'GET',
      '/api/admin/exports/another-fixture-only-route',
      'exports.json',
      'orders export success',
      { openApiCache },
    );

    expect(fake.requestedUrls).toHaveLength(1);
    expect(fake.registeredRoutes).toHaveLength(2);
  });

  it('báo lỗi khi tài liệu OpenAPI thiếu paths', async () => {
    const fake = createFakePage({ ok: true, status: 200, body: {} });

    await expect(
      installFixtureRoute(
        fake.page,
        'GET',
        FIXTURE_ONLY_PATH,
        'exports.json',
        'orders export too large',
        { openApiCache: createOpenApiDocumentCache() },
      ),
    ).rejects.toThrow('không có "paths" dạng object');
  });

  it('không truyền options: hai trang dùng chung bộ nhớ đệm của module, chỉ đọc OpenAPI một lần', async () => {
    // Import tĩnh giữ bộ nhớ đệm đã dùng ở test khác. Nạp lại module để bộ nhớ đệm chung bắt đầu trống.
    vi.resetModules();
    const freshModule = await import('../../../../e2e/fixture-route');
    const firstPage = createFakePage({ ok: true, status: 200, body: documentWithOrdersPath });
    const secondPage = createFakePage({ ok: true, status: 200, body: documentWithOrdersPath });

    await freshModule.installFixtureRoute(
      firstPage.page,
      'GET',
      FIXTURE_ONLY_PATH,
      'exports.json',
      'orders export too large',
    );
    await freshModule.installFixtureRoute(
      secondPage.page,
      'GET',
      '/api/admin/exports/another-fixture-only-route',
      'exports.json',
      'orders export success',
    );

    expect(firstPage.requestedUrls).toEqual(['/api/openapi/v1.json']);
    expect(secondPage.requestedUrls).toHaveLength(0);
    expect(secondPage.registeredRoutes).toHaveLength(1);
  });

  it('báo lỗi khi phương thức truyền vào khác phương thức của ví dụ, trước khi gọi page.route', async () => {
    const fake = createFakePage({ ok: true, status: 200, body: documentWithOrdersPath });

    await expect(
      installFixtureRoute(
        fake.page,
        'POST',
        FIXTURE_ONLY_PATH,
        'exports.json',
        'orders export success',
        { openApiCache: createOpenApiDocumentCache() },
      ),
    ).rejects.toThrow(
      'Ví dụ "orders export success" trong file mẫu "exports.json" dùng phương thức GET, không khớp POST.',
    );
    expect(fake.registeredRoutes).toHaveLength(0);
  });

  it('so phương thức với ví dụ không phân biệt hoa thường', async () => {
    const fake = createFakePage({ ok: true, status: 200, body: documentWithOrdersPath });

    await installFixtureRoute(
      fake.page,
      'get',
      FIXTURE_ONLY_PATH,
      'exports.json',
      'orders export success',
      { openApiCache: createOpenApiDocumentCache() },
    );

    expect(fake.registeredRoutes).toHaveLength(1);
  });

  it('báo lỗi nêu URL khi OpenAPI trả 200 nhưng không phải JSON', async () => {
    const fake = createFakePage({ ok: true, status: 200, body: null, invalidJson: true });

    await expect(
      installFixtureRoute(
        fake.page,
        'GET',
        FIXTURE_ONLY_PATH,
        'exports.json',
        'orders export too large',
        { openApiCache: createOpenApiDocumentCache() },
      ),
    ).rejects.toThrow(
      'Tài liệu OpenAPI tại http://127.0.0.1:4173/api/openapi/v1.json không phải JSON hợp lệ.',
    );
    expect(fake.registeredRoutes).toHaveLength(0);
  });
});
