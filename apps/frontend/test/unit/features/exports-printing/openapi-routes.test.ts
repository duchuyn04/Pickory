import { describe, expect, it } from 'vitest';
import {
  findOpenApiTemplate,
  openApiPathMatches,
  parseOpenApiDocument,
} from '../../../../e2e/openapi-routes';

const ORDER_DETAIL_TEMPLATE = '/api/admin/orders/{code}';

describe('so đường dẫn cụ thể với mẫu OpenAPI', () => {
  it('khớp mẫu có tham số bằng một đoạn đường dẫn', () => {
    expect(openApiPathMatches('/api/admin/orders/PK7KQ2M9XA', ORDER_DETAIL_TEMPLATE)).toBe(true);
  });

  it('không khớp khi đường dẫn thừa một đoạn', () => {
    expect(openApiPathMatches('/api/admin/orders/PK7KQ2M9XA/items', ORDER_DETAIL_TEMPLATE)).toBe(
      false,
    );
  });

  it('không khớp khi đường dẫn thiếu một đoạn', () => {
    expect(openApiPathMatches('/api/admin/orders', ORDER_DETAIL_TEMPLATE)).toBe(false);
    expect(openApiPathMatches('/api/admin/orders/', ORDER_DETAIL_TEMPLATE)).toBe(false);
  });

  it('bỏ query trước khi so', () => {
    expect(openApiPathMatches('/api/admin/orders?status=confirmed', '/api/admin/orders')).toBe(
      true,
    );
    expect(openApiPathMatches('/api/admin/orders/PK7KQ2M9XA?a=b/c', ORDER_DETAIL_TEMPLATE)).toBe(
      true,
    );
  });

  it('bỏ dấu / cuối ở cả hai phía', () => {
    expect(openApiPathMatches('/api/admin/orders/', '/api/admin/orders')).toBe(true);
    expect(openApiPathMatches('/api/admin/orders', '/api/admin/orders/')).toBe(true);
  });

  it('so hoa thường đúng chữ của đường dẫn', () => {
    expect(openApiPathMatches('/api/Admin/orders', '/api/admin/orders')).toBe(false);
  });

  it('khớp ký tự đặc biệt của regex theo nguyên văn', () => {
    expect(openApiPathMatches('/api/a.b', '/api/a.b')).toBe(true);
    expect(openApiPathMatches('/api/axb', '/api/a.b')).toBe(false);
  });

  it('khớp nhiều tham số trong cùng một mẫu', () => {
    expect(openApiPathMatches('/api/a/1/b/2', '/api/a/{x}/b/{y}')).toBe(true);
    expect(openApiPathMatches('/api/a/1/b', '/api/a/{x}/b/{y}')).toBe(false);
  });
});

describe('tìm thao tác trong tài liệu OpenAPI', () => {
  const document = parseOpenApiDocument(
    {
      paths: {
        '/api/admin/orders': { get: {}, parameters: [] },
        '/api/admin/orders/{code}': { get: {} },
        '/api/admin/orders/{code}/items': { post: {} },
      },
    },
    '/api/openapi/v1.json',
  );

  it('trả mẫu khớp khi đường dẫn và phương thức đều có', () => {
    expect(findOpenApiTemplate(document, 'GET', '/api/admin/orders/PK7KQ2M9XA')).toBe(
      ORDER_DETAIL_TEMPLATE,
    );
  });

  it('so phương thức không phân biệt hoa thường', () => {
    expect(findOpenApiTemplate(document, 'get', '/api/admin/orders')).toBe('/api/admin/orders');
    expect(findOpenApiTemplate(document, 'Get', '/api/admin/orders')).toBe('/api/admin/orders');
  });

  it('không trả mẫu khi đường dẫn có nhưng phương thức khác', () => {
    expect(findOpenApiTemplate(document, 'POST', '/api/admin/orders/PK7KQ2M9XA')).toBeUndefined();
  });

  it('không coi khóa parameters của mục đường dẫn là một phương thức', () => {
    expect(findOpenApiTemplate(document, 'PARAMETERS', '/api/admin/orders')).toBeUndefined();
  });

  it('phân biệt hai mẫu cùng tiền tố', () => {
    expect(findOpenApiTemplate(document, 'GET', '/api/admin/orders/PK7KQ2M9XA/items')).toBe(
      undefined,
    );
    expect(findOpenApiTemplate(document, 'POST', '/api/admin/orders/PK7KQ2M9XA/items')).toBe(
      '/api/admin/orders/{code}/items',
    );
  });

  it('không trả mẫu khi đường dẫn chưa có trong tài liệu', () => {
    expect(findOpenApiTemplate(document, 'GET', '/api/admin/order-invoices')).toBeUndefined();
  });
});

describe('kiểm hình dạng tài liệu OpenAPI', () => {
  it('báo lỗi nêu URL khi tài liệu không có paths', () => {
    expect(() => parseOpenApiDocument({}, '/api/openapi/v1.json')).toThrow(
      'Tài liệu OpenAPI tại /api/openapi/v1.json không có "paths" dạng object.',
    );
  });

  it('báo lỗi nêu URL khi tài liệu không phải object', () => {
    expect(() => parseOpenApiDocument('chu', '/api/openapi/v1.json')).toThrow(
      'Tài liệu OpenAPI tại /api/openapi/v1.json không có "paths" dạng object.',
    );
  });
});

describe('nhánh biên của so đường dẫn và tài liệu', () => {
  it('bỏ fragment sau dấu # trước khi so', () => {
    expect(openApiPathMatches('/api/admin/orders#top', '/api/admin/orders')).toBe(true);
    expect(openApiPathMatches('/api/admin/orders/PK7KQ2M9XA#top', ORDER_DETAIL_TEMPLATE)).toBe(
      true,
    );
  });

  it('coi mục đường dẫn không phải object là mục không có phương thức nào', () => {
    const document = parseOpenApiDocument(
      { paths: { '/api/x': null, '/api/y': { get: {} } } },
      '/api/openapi/v1.json',
    );

    expect(findOpenApiTemplate(document, 'GET', '/api/x')).toBeUndefined();
    expect(findOpenApiTemplate(document, 'GET', '/api/y')).toBe('/api/y');
  });
});
