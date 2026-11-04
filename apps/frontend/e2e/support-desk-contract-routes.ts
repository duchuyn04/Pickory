import type { Page, Route } from '@playwright/test';
import {
  findSupportDeskExample,
  selectSupportDeskEvents,
  type ContractEvent,
  type ContractExample,
} from '../test/unit/features/support-desk/contract-fixtures';

const OPENAPI_DOCUMENT_PATH = '/api/openapi/v1.json';

interface OpenApiPathItem {
  [method: string]: unknown;
}

interface OpenApiDocument {
  paths: Record<string, OpenApiPathItem>;
}

export interface SupportDeskRouteMocksOptions {
  page: Page;
  // Origin của API E2E. Tài liệu OpenAPI nằm ở API, không nằm ở web.
  apiOrigin: string;
  // Cho unit test thay hàm đọc tài liệu. Lượt chạy Playwright dùng `fetch` của Node.
  loadOpenApiDocument?: (url: string) => Promise<unknown>;
}

export interface EventStreamMockOptions {
  path: string;
  fixtureFile: string;
  eventNames: readonly string[];
}

export interface SupportDeskRouteMocks {
  mockRoute(fixtureFile: string, exampleName: string): Promise<void>;
  mockEventStream(options: EventStreamMockOptions): Promise<void>;
}

// Tài liệu OpenAPI giống nhau cho mọi route giả trong một lần chạy Playwright. Giữ một lần đọc cho cả lượt.
const openApiDocumentCache = new Map<string, Promise<OpenApiDocument>>();

// Unit test gọi hàm này để mỗi case có cache riêng. Lượt chạy Playwright không gọi.
export function resetSupportDeskOpenApiCache(): void {
  openApiDocumentCache.clear();
}

export function createSupportDeskRouteMocks(
  options: SupportDeskRouteMocksOptions,
): SupportDeskRouteMocks {
  // Bỏ dấu `/` cuối origin để ghép đúng một dấu phân cách với đường dẫn tài liệu.
  const openApiUrl = `${options.apiOrigin.replace(/\/+$/, '')}${OPENAPI_DOCUMENT_PATH}`;
  const loadOpenApiDocument = options.loadOpenApiDocument ?? fetchOpenApiDocument;

  return {
    async mockRoute(fixtureFile, exampleName) {
      const example = findSupportDeskExample(fixtureFile, exampleName);
      // Không gắn dữ liệu giả cho route backend đã có. Test đỏ để người sửa gỡ lời gọi helper.
      await assertRouteIsMissing(openApiUrl, loadOpenApiDocument, example);
      await options.page.route(
        (url) => matchesExampleUrl(url, example),
        (route) => fulfillExample(route, example),
      );
    },

    async mockEventStream(streamOptions) {
      const events = selectSupportDeskEvents(streamOptions.fixtureFile, streamOptions.eventNames);
      const streamPath = normalizePathname(parsePath(streamOptions.path).pathname);
      await options.page.route(
        (url) => normalizePathname(url.pathname) === streamPath,
        (route) => fulfillEventStream(route, events),
      );
    },
  };
}

async function assertRouteIsMissing(
  openApiUrl: string,
  loadOpenApiDocument: (url: string) => Promise<unknown>,
  example: ContractExample,
): Promise<void> {
  const document = await getOpenApiDocument(openApiUrl, loadOpenApiDocument);
  const openApiPath = findOpenApiPath(document.paths, example.method, example.path);
  if (openApiPath === null) return;
  const method = example.method.toUpperCase();
  throw new Error(`Backend đã có \`${method} ${openApiPath}\`. Gỡ dữ liệu giả của route này.`);
}

function getOpenApiDocument(
  openApiUrl: string,
  loadOpenApiDocument: (url: string) => Promise<unknown>,
): Promise<OpenApiDocument> {
  const cached = openApiDocumentCache.get(openApiUrl);
  if (cached !== undefined) return cached;
  const document = loadOpenApiDocument(openApiUrl).then(readOpenApiDocument);
  openApiDocumentCache.set(openApiUrl, document);
  // Bỏ mục lỗi khỏi cache để lần sau đọc lại, không giữ lỗi cũ cho mọi route.
  document.catch(() => openApiDocumentCache.delete(openApiUrl));
  return document;
}

// Tài liệu sai dạng là lỗi của môi trường E2E, nên báo lỗi thay vì coi như không có route nào.
function readOpenApiDocument(raw: unknown): OpenApiDocument {
  const document = raw as OpenApiDocument | null | undefined;
  if (document === null || document === undefined || typeof document !== 'object') {
    throw new Error('Tài liệu OpenAPI không phải một object.');
  }
  if (document.paths === null || typeof document.paths !== 'object') {
    throw new Error('Tài liệu OpenAPI không có danh sách đường dẫn `paths`.');
  }
  return document;
}

async function fetchOpenApiDocument(url: string): Promise<unknown> {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`Không đọc được tài liệu OpenAPI từ ${url}: máy chủ trả ${response.status}.`);
  }
  return response.json();
}

function findOpenApiPath(
  paths: Record<string, OpenApiPathItem>,
  method: string,
  examplePath: string,
): string | null {
  const methodName = method.toLowerCase();
  const pathname = normalizePathname(parsePath(examplePath).pathname);
  for (const [template, operations] of Object.entries(paths)) {
    // Tài liệu OpenAPI ghi tên method bằng chữ thường.
    if (operations[methodName] === undefined) continue;
    if (matchesPathTemplate(template, pathname)) return template;
  }
  return null;
}

function matchesPathTemplate(template: string, pathname: string): boolean {
  const templateSegments = normalizePathname(template).split('/');
  const pathSegments = pathname.split('/');
  if (templateSegments.length !== pathSegments.length) return false;
  return templateSegments.every((segment, index) => {
    // Đoạn `{id}` khớp một đoạn bất kỳ. So nguyên văn thì route thật bị coi là chưa có.
    if (segment.startsWith('{') && segment.endsWith('}')) return pathSegments[index] !== '';
    return segment === pathSegments[index];
  });
}

// Đường dẫn của ví dụ luôn bắt đầu bằng `/`. Origin giả đủ để `URL` tách pathname và query.
function parsePath(path: string): URL {
  return new URL(path, 'http://support-desk.contract');
}

function normalizePathname(pathname: string): string {
  return pathname.length > 1 && pathname.endsWith('/') ? pathname.slice(0, -1) : pathname;
}

function matchesExampleUrl(url: URL, example: ContractExample): boolean {
  const exampleUrl = parsePath(example.path);
  if (normalizePathname(url.pathname) !== normalizePathname(exampleUrl.pathname)) return false;
  // Ví dụ không có query thì route nhận mọi query của cùng đường dẫn.
  for (const [key, value] of exampleUrl.searchParams) {
    if (url.searchParams.get(key) !== value) return false;
  }
  return true;
}

async function fulfillExample(route: Route, example: ContractExample): Promise<void> {
  // Request khác method thì để route kế tiếp hoặc backend thật nhận.
  if (route.request().method().toUpperCase() !== example.method.toUpperCase()) {
    await route.fallback();
    return;
  }
  await route.fulfill({
    status: example.status,
    contentType: 'application/json',
    body: JSON.stringify(example.response),
  });
}

async function fulfillEventStream(route: Route, events: readonly ContractEvent[]): Promise<void> {
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
