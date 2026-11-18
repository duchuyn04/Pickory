import { expect, test, type Page } from '@playwright/test';
import { findExportsPrintingExample } from '../test/unit/features/exports-printing/contract-fixtures';
import { installFixtureRoute } from './fixture-route';

// Đường dẫn này không bao giờ có trong backend, nên spec không đỏ khi các task backend thêm route thật.
const FIXTURE_ONLY_PATH = '/api/admin/exports/fixture-only-route';

interface FetchedResponse {
  status: number;
  contentType: string | null;
  contentDisposition: string | null;
  bodyText: string;
  bodyBytes: number[];
}

// Gọi `fetch` từ trong trang để request đi qua cùng đường với code frontend thật, gồm cả route giả.
async function fetchFromPage(
  page: Page,
  path: string,
  method: string = 'GET',
): Promise<FetchedResponse> {
  return page.evaluate(
    async ({ requestPath, requestMethod }) => {
      const response = await fetch(requestPath, { method: requestMethod });
      const buffer = await response.arrayBuffer();
      return {
        status: response.status,
        contentType: response.headers.get('content-type'),
        contentDisposition: response.headers.get('content-disposition'),
        bodyText: new TextDecoder().decode(buffer),
        bodyBytes: Array.from(new Uint8Array(buffer)),
      };
    },
    { requestPath: path, requestMethod: method },
  );
}

test.describe('helper installFixtureRoute', () => {
  test('từ chối khi backend đã có route', async ({ page }) => {
    await page.goto('/');

    await expect(
      installFixtureRoute(
        page,
        'GET',
        '/api/admin/orders',
        'exports.json',
        'orders export success',
      ),
    ).rejects.toThrow('Backend đã có `GET /api/admin/orders`. Bỏ dữ liệu giả của route này.');
  });

  test('route chưa có: trả status và body của ví dụ JSON', async ({ page }) => {
    await page.goto('/');
    const example = findExportsPrintingExample('exports.json', 'orders export too large');

    await installFixtureRoute(page, 'GET', FIXTURE_ONLY_PATH, 'exports.json', example.name);
    const fetched = await fetchFromPage(page, FIXTURE_ONLY_PATH);

    expect(fetched.status).toBe(422);
    expect(fetched.contentType).toBe('application/json; charset=utf-8');
    expect(JSON.parse(fetched.bodyText)).toEqual(example.response);
  });

  test('route chưa có: trả header của ví dụ và body nhị phân giả bắt đầu bằng PK', async ({
    page,
  }) => {
    await page.goto('/');
    const example = findExportsPrintingExample('exports.json', 'orders export success');

    await installFixtureRoute(page, 'GET', FIXTURE_ONLY_PATH, 'exports.json', example.name);
    const fetched = await fetchFromPage(page, `${FIXTURE_ONLY_PATH}?status=confirmed`);

    expect(fetched.status).toBe(200);
    expect(fetched.contentType).toBe(example.headers?.['Content-Type']);
    expect(fetched.contentDisposition).toBe(example.headers?.['Content-Disposition']);
    expect(fetched.bodyBytes).toEqual([0x50, 0x4b]);
  });

  test('route chưa có: request khác phương thức đi tới backend thật, không nhận dữ liệu giả', async ({
    page,
  }) => {
    await page.goto('/');
    const example = findExportsPrintingExample('exports.json', 'orders export too large');

    await installFixtureRoute(page, 'GET', FIXTURE_ONLY_PATH, 'exports.json', example.name);
    const fetched = await fetchFromPage(page, FIXTURE_ONLY_PATH, 'POST');

    // Backend không có route này, nên status không thể là 422 của ví dụ giả.
    expect(fetched.status).not.toBe(422);
    expect(fetched.bodyText).not.toContain('EXPORT_TOO_LARGE');
  });
});
