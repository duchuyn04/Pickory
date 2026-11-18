import type { Page } from '@playwright/test';
import {
  findExportsPrintingExample,
  type ContractExample,
} from '../test/unit/features/exports-printing/contract-fixtures';
import {
  findOpenApiTemplate,
  normalizePath,
  parseOpenApiDocument,
  type OpenApiDocument,
} from './openapi-routes';

// Helper duy nhất của đợt xuất Excel và in hóa đơn được cài route giả. Spec không gọi `page.route` trực tiếp cho các route này.

const OPENAPI_PATH = '/api/openapi/v1.json';

// Hai byte đầu của mọi file `.xlsx`, vì file này dùng định dạng nén ZIP. Test chỉ cần chứng minh nhận được dữ liệu nhị phân, không cần file Excel thật.
const FAKE_WORKBOOK_BODY = Buffer.from([0x50, 0x4b]);

export interface FixtureResponse {
  status: number;
  headers: Record<string, string>;
  body: string | Buffer;
}

export interface OpenApiDocumentCache {
  load: (read: () => Promise<OpenApiDocument>) => Promise<OpenApiDocument>;
}

export interface InstallFixtureRouteOptions {
  // Chỉ test của chính helper truyền giá trị này để mỗi case có bộ nhớ đệm riêng. Spec Playwright dùng mặc định.
  openApiCache?: OpenApiDocumentCache;
  // Cho phép test biến đổi dữ liệu JSON của ví dụ trước khi trả để tạo hóa đơn nhiều dòng.
  transformResponse?: (response: unknown) => unknown;
}

// Giữ lời hứa đọc tài liệu thay cho kết quả. Hai lời gọi chạy cùng lúc nhờ vậy chỉ tạo một request.
export function createOpenApiDocumentCache(): OpenApiDocumentCache {
  let pendingDocument: Promise<OpenApiDocument> | undefined;
  return {
    load(read) {
      if (pendingDocument === undefined) {
        const attempt = read();
        pendingDocument = attempt;
        // Lần đọc lỗi mà được giữ lại thì mọi route giả sau đó cũng lỗi theo. Xóa để lần gọi sau thử lại.
        attempt.catch(() => {
          if (pendingDocument === attempt) pendingDocument = undefined;
        });
      }
      return pendingDocument;
    },
  };
}

// Bộ nhớ đệm của cả lượt chạy. Mỗi worker Playwright là một tiến trình, nên bộ nhớ này sống đến hết lượt.
const sharedOpenApiCache = createOpenApiDocumentCache();

// Route đã có trong backend thì dữ liệu giả che mất hành vi thật. Ném lỗi để test đỏ và người viết bỏ dữ liệu giả.
export function assertBackendLacksRoute(
  document: OpenApiDocument,
  method: string,
  path: string,
): void {
  if (findOpenApiTemplate(document, method, path) !== undefined) {
    throw new Error(
      `Backend đã có \`${method.toUpperCase()} ${path}\`. Bỏ dữ liệu giả của route này.`,
    );
  }
}

// Dựng response từ ví dụ trong file mẫu. Ví dụ JSON và ví dụ file Excel có hình dạng khác nhau.
export function buildFixtureResponse(example: ContractExample): FixtureResponse {
  if (example.response !== undefined) {
    return {
      status: example.status,
      headers: { 'content-type': 'application/json; charset=utf-8' },
      body: JSON.stringify(example.response),
    };
  }
  if (example.workbook !== undefined) {
    // Ví dụ file Excel không có `headers` thì response thiếu `Content-Type` và `Content-Disposition`. Ném lỗi để test không xanh giả.
    if (example.headers === undefined) {
      throw new Error(`Ví dụ "${example.name}" có "workbook" nhưng thiếu "headers".`);
    }
    return {
      status: example.status,
      // Giữ đúng `Content-Type` và `Content-Disposition` của ví dụ để test kiểm tên file tải về.
      headers: { ...example.headers },
      body: FAKE_WORKBOOK_BODY,
    };
  }
  // Ví dụ thiếu cả hai thì không có gì để trả. Ném lỗi thay vì trả body rỗng làm test xanh giả.
  throw new Error(`Ví dụ "${example.name}" không có "response" và không có "workbook".`);
}

// Đọc tài liệu OpenAPI của backend thật qua `baseURL` của Playwright.
async function readOpenApiDocument(page: Page): Promise<OpenApiDocument> {
  const response = await page.request.get(OPENAPI_PATH);
  // Backend không trả 200 thì không có tài liệu để so. Báo URL và status để người chạy biết API chưa sẵn sàng.
  if (!response.ok()) {
    throw new Error(`Không đọc được OpenAPI tại ${response.url()} (status ${response.status()}).`);
  }
  let body: unknown;
  try {
    body = await response.json();
  } catch {
    // Proxy hoặc trang lỗi có thể trả HTML với status 200. Báo URL thay cho `SyntaxError` không nêu nguồn.
    throw new Error(`Tài liệu OpenAPI tại ${response.url()} không phải JSON hợp lệ.`);
  }
  return parseOpenApiDocument(body, response.url());
}

// Chạy hàm biến đổi dữ liệu cho ví dụ JSON và chặn trường hợp truyền hàm này cho ví dụ file Excel.
function applyResponseTransform(
  example: ContractExample,
  exampleName: string,
  transformResponse?: (response: unknown) => unknown,
): ContractExample {
  if (transformResponse === undefined) {
    return example;
  }
  if (example.response === undefined) {
    throw new Error(`Ví dụ "${exampleName}" không có "response" để biến đổi.`);
  }
  // Mỗi lần gọi `findExportsPrintingExample` đã đọc lại tệp từ đĩa nên không cần nhân bản sâu đối tượng.
  return {
    ...example,
    response: transformResponse(example.response),
  };
}

// Cài route giả cho một route backend chưa có. Hàm không nuốt lỗi, nên mọi lỗi làm test gọi nó đỏ.
export async function installFixtureRoute(
  page: Page,
  method: string,
  path: string,
  fixtureFile: string,
  exampleName: string,
  options: InstallFixtureRouteOptions = {},
): Promise<void> {
  const openApiCache = options.openApiCache ?? sharedOpenApiCache;
  // Kiểm OpenAPI trước khi đọc file mẫu và trước `page.route`, để route đã có không bao giờ bị che.
  const document = await openApiCache.load(() => readOpenApiDocument(page));
  assertBackendLacksRoute(document, method, path);

  const example = findExportsPrintingExample(fixtureFile, exampleName);
  const mockedMethod = method.toUpperCase();
  // Ví dụ của route khác phương thức sẽ trả dữ liệu sai cho route này. Ném lỗi để người viết test chọn lại ví dụ.
  if (example.method.toUpperCase() !== mockedMethod) {
    throw new Error(
      `Ví dụ "${exampleName}" trong file mẫu "${fixtureFile}" dùng phương thức ${example.method.toUpperCase()}, không khớp ${mockedMethod}.`,
    );
  }
  // Biến đổi dữ liệu JSON của ví dụ khi ca kiểm thử yêu cầu trước khi dựng response giả.
  const preparedExample = applyResponseTransform(example, exampleName, options.transformResponse);
  // Dựng response một lần ở đây để ví dụ hỏng làm test đỏ ngay, không đợi tới lúc có request.
  const response = buildFixtureResponse(preparedExample);
  // Bỏ query khỏi đường dẫn đã truyền, vì route khớp theo pathname.
  const mockedPathname = normalizePath(path);

  await page.route(
    // So pathname, không so query, để cùng một route giả phục vụ mọi bộ lọc trên URL.
    (url) => normalizePath(url.pathname) === mockedPathname,
    async (route) => {
      // Request khác phương thức thì để backend thật nhận.
      if (route.request().method().toUpperCase() !== mockedMethod) {
        await route.fallback();
        return;
      }
      await route.fulfill(response);
    },
  );
}
