import { describe, expect, it } from 'vitest';
import { orderInvoicesResponseSchema } from '../../../../src/features/commerce/admin/orders/order-invoices-schema';
import { findExportsPrintingExample, listExportsPrintingExamples } from './contract-fixtures';

// Chọn theo `status` để thêm ví dụ 200 mới vào file mẫu thì test tự đọc thêm.
const successExamples = listExportsPrintingExamples('order-invoices.json').filter(
  (example) => example.status === 200,
);

// Ví dụ có hai hóa đơn, hóa đơn đầu có VAT và hai dòng hàng. Các case sai dùng ví dụ này theo tên, không theo thứ tự.
const TWO_ORDERS_EXAMPLE = 'order invoices two orders with vat and cancelled';

// Dạng sửa được của response. Test cần xóa khóa hoặc đổi kiểu mà kiểu suy ra từ schema cấm việc đó.
type EditableLine = Record<string, unknown>;

interface EditableInvoice {
  [key: string]: unknown;
  lines: [EditableLine, EditableLine, ...EditableLine[]];
}

interface EditableResponse {
  data: {
    shop: Record<string, unknown>;
    invoices: [EditableInvoice, ...EditableInvoice[]];
  };
}

// Bản sao sâu của ví dụ hai hóa đơn để mỗi case sửa trên dữ liệu riêng.
function cloneTwoOrdersResponse(): EditableResponse {
  const example = findExportsPrintingExample('order-invoices.json', TWO_ORDERS_EXAMPLE);
  // Ví dụ đã được schema kiểm ở case đầu, nên ép sang dạng sửa được là an toàn.
  return structuredClone(example.response) as EditableResponse;
}

// Gộp bước parse và lấy `path` của lỗi đầu tiên để mỗi test chỉ nêu chỗ sửa và chỗ lỗi mong đợi.
function firstIssuePath(response: unknown): PropertyKey[] | undefined {
  const result = orderInvoicesResponseSchema.safeParse(response);
  return result.error?.issues[0]?.path;
}

describe('schema response GET /api/admin/order-invoices', () => {
  it('có ít nhất hai ví dụ 200 để test không đỏ giả vì danh sách rỗng', () => {
    expect(successExamples.length).toBeGreaterThanOrEqual(2);
  });

  it.each(successExamples.map((example) => [example.name, example.response] as const))(
    'đọc được ví dụ "%s"',
    (_name, response) => {
      const result = orderInvoicesResponseSchema.safeParse(response);

      expect(result.success).toBe(true);
    },
  );

  it('giữ nguyên giá trị null của shop và vatInvoice', () => {
    const shopNotSet = successExamples.find(
      (example) => example.name === 'order invoices shop profile not set',
    );

    const result = orderInvoicesResponseSchema.parse(shopNotSet?.response);

    expect(result.data.shop).toEqual({ name: null, address: null, phone: null, taxCode: null });
    expect(result.data.invoices[0]?.vatInvoice).toBeNull();
  });

  it('báo lỗi khi xóa khóa vatInvoice khỏi một hóa đơn', () => {
    const response = cloneTwoOrdersResponse();
    Reflect.deleteProperty(response.data.invoices[0], 'vatInvoice');

    expect(firstIssuePath(response)).toEqual(['data', 'invoices', 0, 'vatInvoice']);
  });

  it('báo lỗi khi quantity là chữ', () => {
    const response = cloneTwoOrdersResponse();
    response.data.invoices[0].lines[0].quantity = '1';

    expect(firstIssuePath(response)).toEqual(['data', 'invoices', 0, 'lines', 0, 'quantity']);
  });

  it('báo lỗi khi thiếu lines', () => {
    const response = cloneTwoOrdersResponse();
    Reflect.deleteProperty(response.data.invoices[0], 'lines');

    expect(firstIssuePath(response)).toEqual(['data', 'invoices', 0, 'lines']);
  });

  it('báo lỗi khi shop.name là số', () => {
    const response = cloneTwoOrdersResponse();
    response.data.shop.name = 123;

    expect(firstIssuePath(response)).toEqual(['data', 'shop', 'name']);
  });

  it('báo lỗi khi thiếu lớp data', () => {
    const response = cloneTwoOrdersResponse();

    expect(firstIssuePath(response.data)).toEqual(['data']);
  });

  it('báo lỗi khi tiền không phải số nguyên', () => {
    const response = cloneTwoOrdersResponse();
    response.data.invoices[0].total = 100.5;

    expect(firstIssuePath(response)).toEqual(['data', 'invoices', 0, 'total']);
  });

  it('báo lỗi khi optionText bị xóa khỏi một dòng', () => {
    const response = cloneTwoOrdersResponse();
    Reflect.deleteProperty(response.data.invoices[0].lines[1], 'optionText');

    expect(firstIssuePath(response)).toEqual(['data', 'invoices', 0, 'lines', 1, 'optionText']);
  });
});

describe('createdAt của hóa đơn', () => {
  // Mỗi giá trị đi qua đúng một trường `createdAt` của hóa đơn đầu tiên.
  function responseWithCreatedAt(createdAt: string): EditableResponse {
    const response = cloneTwoOrdersResponse();
    response.data.invoices[0].createdAt = createdAt;
    return response;
  }

  it.each([
    ['hậu tố Z', '2026-11-09T03:15:00Z'],
    ['múi giờ +07:00', '2026-11-09T10:15:00+07:00'],
    ['bảy chữ số lẻ giây của .NET', '2026-11-09T03:15:00.1234567Z'],
  ])('nhận chuỗi có %s', (_label, createdAt) => {
    const result = orderInvoicesResponseSchema.safeParse(responseWithCreatedAt(createdAt));

    expect(result.success).toBe(true);
  });

  it.each([
    ['không có múi giờ', '2026-11-09T03:15:00'],
    ['không phải ISO 8601', '09/11/2026 10:15'],
  ])('báo lỗi khi chuỗi %s', (_label, createdAt) => {
    expect(firstIssuePath(responseWithCreatedAt(createdAt))).toEqual([
      'data',
      'invoices',
      0,
      'createdAt',
    ]);
  });
});
