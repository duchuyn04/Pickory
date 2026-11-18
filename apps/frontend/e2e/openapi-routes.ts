// Phần thuần của việc tra route trong tài liệu OpenAPI. Phần này không cần trình duyệt, nên Vitest kiểm được.

export interface OpenApiDocument {
  // Khóa là mẫu đường dẫn như `/api/admin/orders/{code}`. Giá trị là mục đường dẫn, có khóa phương thức viết thường.
  paths: Record<string, Record<string, unknown>>;
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

// Kiểm tối thiểu để tài liệu hỏng báo lỗi rõ. Nếu không, `paths` thiếu sẽ khiến mọi route giả được cài mà không ai biết.
export function parseOpenApiDocument(value: unknown, url: string): OpenApiDocument {
  if (!isPlainObject(value) || !isPlainObject(value.paths)) {
    throw new Error(`Tài liệu OpenAPI tại ${url} không có "paths" dạng object.`);
  }
  const paths: Record<string, Record<string, unknown>> = {};
  for (const [template, pathItem] of Object.entries(value.paths)) {
    // Mục đường dẫn không phải object thì không khai báo phương thức nào.
    paths[template] = isPlainObject(pathItem) ? pathItem : {};
  }
  return { paths };
}

// Bỏ query, fragment và dấu `/` cuối. Hai cách viết của cùng một route phải cho cùng kết quả so sánh.
export function normalizePath(path: string): string {
  const withoutQuery = path.split(/[?#]/, 1)[0] ?? '';
  return withoutQuery.length > 1 && withoutQuery.endsWith('/')
    ? withoutQuery.slice(0, -1)
    : withoutQuery;
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

// Đổi mẫu OpenAPI sang regex. `{code}` khớp đúng một đoạn nên không chứa `/`, các ký tự khác khớp nguyên văn.
function templateToRegExp(template: string): RegExp {
  const pattern = normalizePath(template)
    .split(/(\{[^}/]+\})/)
    .map((part) => (/^\{[^}/]+\}$/.test(part) ? '[^/]+' : escapeRegExp(part)))
    .join('');
  // Neo hai đầu để `/api/admin/orders/{code}` không khớp phần đầu của đường dẫn dài hơn.
  return new RegExp(`^${pattern}$`);
}

// So sánh phân biệt hoa thường theo đúng chữ của đường dẫn. Test phải truyền đường dẫn giống chữ trong OpenAPI.
export function openApiPathMatches(path: string, template: string): boolean {
  return templateToRegExp(template).test(normalizePath(path));
}

// Chỉ tám phương thức này là phương thức HTTP hợp lệ trong mục đường dẫn của OpenAPI.
const HTTP_METHODS: Record<string, true> = {
  get: true,
  put: true,
  post: true,
  delete: true,
  options: true,
  head: true,
  patch: true,
  trace: true,
};

// Trả mẫu đầu tiên khớp đường dẫn và có khai báo phương thức. Không có thì trả `undefined`.
export function findOpenApiTemplate(
  document: OpenApiDocument,
  method: string,
  path: string,
): string | undefined {
  // OpenAPI ghi phương thức bằng chữ thường, còn test truyền chữ hoa, nên đưa về một dạng.
  const methodKey = method.toLowerCase();
  // Khóa như `parameters` nằm cạnh phương thức trong mục đường dẫn nhưng không phải phương thức HTTP.
  if (!Object.hasOwn(HTTP_METHODS, methodKey)) return undefined;
  return Object.keys(document.paths).find(
    (template) =>
      openApiPathMatches(path, template) &&
      Object.hasOwn(document.paths[template] ?? {}, methodKey),
  );
}
