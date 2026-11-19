import { describe, expect, it } from 'vitest';
import { adminOrderDetailSchema } from '../../../../../../src/features/commerce/admin/orders/admin-order-schemas';
import { findCommerceExample, listCommerceExamples } from '../../contract-fixtures';
import { exampleData } from '../../fixture-response';

// Chọn theo `status` để thêm ví dụ 200 mới vào file mẫu thì test tự đọc thêm.
const successExamples = listCommerceExamples('manual-fulfillment.json').filter(
  (example) => example.status === 200,
);

// Dạng sửa được của chi tiết đơn. Test cần xóa khóa hoặc đổi kiểu mà kiểu suy ra từ schema cấm việc đó.
type EditableDetail = Record<string, unknown> & {
  items: [Record<string, unknown>, ...Record<string, unknown>[]];
  statusHistory: [Record<string, unknown>, ...Record<string, unknown>[]];
  contact: Record<string, unknown>;
  shippingAddress: Record<string, unknown>;
  shipment: Record<string, unknown>;
  suggestedParcel: Record<string, unknown>;
  invoice: Record<string, unknown>;
};

// Bản sao có giá trị ở mọi khóa nullable. Cả hai ví dụ của file mẫu để các khóa này là `null`, nên test cần dựng thêm.
function cloneDetailWithValues(): EditableDetail {
  const detail = cloneDetail();
  detail.paymentDeadline = '2026-11-19T04:00:00Z';
  detail.refundTransactionRef = 'REF-001';
  detail.invoice = {
    companyName: 'Công ty Pickory',
    taxCode: '0312345678',
    address: '45 Lê Lợi',
    email: 'ketoan@example.com',
  };
  detail.shipment = { id: 'ship-1', trackingCode: 'GHN123', status: 'delivering', fee: 30000 };
  detail.suggestedParcel = { weightGrams: 500, lengthCm: 20, widthCm: 15, heightCm: 10 };
  return detail;
}

// Bản sao sâu của ví dụ đầu để mỗi case sửa trên dữ liệu riêng.
function cloneDetail(): EditableDetail {
  const example = findCommerceExample('manual-fulfillment.json', 'start manual shipping');
  // Schema đã kiểm `data` là object. Ép sang dạng sửa được để test xóa khóa hoặc đổi kiểu.
  const editable = structuredClone(exampleData(example)) as EditableDetail;
  return editable;
}

// Trả `path` của lỗi đầu tiên. Mỗi test chỉ nêu chỗ sửa, không lặp bước parse.
function firstIssuePath(detail: unknown): PropertyKey[] | undefined {
  return adminOrderDetailSchema.safeParse(detail).error?.issues[0]?.path;
}

describe('schema chi tiết đơn của route fulfillment', () => {
  it('lọc ra đúng hai ví dụ 200 để test không đỏ giả vì danh sách rỗng', () => {
    expect(successExamples.map((example) => example.name)).toEqual(
      expect.arrayContaining(['start manual shipping', 'confirm manual delivery']),
    );
  });

  it.each(successExamples.map((example) => [example.name, exampleData(example)] as const))(
    'đọc được ví dụ "%s"',
    (_name, data) => {
      expect(adminOrderDetailSchema.safeParse(data).success).toBe(true);
    },
  );

  it('đọc được khi có thêm năm khóa tùy chọn mà fixture không ghi', () => {
    const detail = cloneDetail();
    detail.contact.note = 'Giao giờ hành chính';
    detail.shippingAddress.provinceCode = '79';
    detail.shippingAddress.wardCode = '26734';
    detail.items[0].imageUrl = 'https://example.com/a.png';
    detail.items[0].imagePublicId = 'a';

    expect(adminOrderDetailSchema.safeParse(detail).success).toBe(true);
  });

  it.each(['status', 'total', 'flags', 'statusHistory', 'shippingFeeSource'])(
    'báo lỗi đúng khóa khi thiếu "%s"',
    (key) => {
      const detail = cloneDetail();
      delete detail[key];

      expect(firstIssuePath(detail)).toEqual([key]);
    },
  );

  it.each(['paymentDeadline', 'shipment'])('báo lỗi khi thiếu hẳn khóa nullable "%s"', (key) => {
    const detail = cloneDetail();
    delete detail[key];

    expect(firstIssuePath(detail)).toEqual([key]);
  });

  it('báo lỗi khi total là chuỗi', () => {
    const detail = cloneDetail();
    detail.total = '130000';

    expect(firstIssuePath(detail)).toEqual(['total']);
  });

  it('báo lỗi khi flags là chuỗi thay vì mảng', () => {
    const detail = cloneDetail();
    detail.flags = 'manual_shipping';

    expect(firstIssuePath(detail)).toEqual(['flags']);
  });

  it('báo lỗi khi createdAt không phải giờ hợp lệ', () => {
    const detail = cloneDetail();
    detail.createdAt = 'hôm qua';

    expect(firstIssuePath(detail)).toEqual(['createdAt']);
  });

  it('báo lỗi khi shippingFeeSource ngoài ba giá trị', () => {
    const detail = cloneDetail();
    detail.shippingFeeSource = 'khac';

    expect(firstIssuePath(detail)).toEqual(['shippingFeeSource']);
  });

  it('báo lỗi khi quantity không phải số nguyên', () => {
    const detail = cloneDetail();
    detail.items[0].quantity = 1.5;

    expect(firstIssuePath(detail)).toEqual(['items', 0, 'quantity']);
  });

  it('đọc được response có đủ giá trị ở mọi khóa nullable', () => {
    const detail = cloneDetailWithValues();

    expect(adminOrderDetailSchema.safeParse(detail).success).toBe(true);
  });

  it.each(['ghn', 'cached', 'fallback'])('đọc được shippingFeeSource "%s"', (source) => {
    const detail = cloneDetail();
    detail.shippingFeeSource = source;

    expect(adminOrderDetailSchema.safeParse(detail).success).toBe(true);
  });

  it('đọc được dòng lịch sử do hệ thống chuyển, có actorId, actorName, reason là null', () => {
    const detail = cloneDetail();
    detail.statusHistory[0].actorId = null;
    detail.statusHistory[0].actorName = null;
    detail.statusHistory[0].reason = null;

    expect(adminOrderDetailSchema.safeParse(detail).success).toBe(true);
  });

  it('báo lỗi khi shipment.fee là chuỗi', () => {
    const detail = cloneDetailWithValues();
    detail.shipment = { ...detail.shipment, fee: '30000' };

    expect(firstIssuePath(detail)).toEqual(['shipment', 'fee']);
  });

  it('báo lỗi khi shipment thiếu trackingCode', () => {
    const detail = cloneDetailWithValues();
    delete detail.shipment.trackingCode;

    expect(firstIssuePath(detail)).toEqual(['shipment', 'trackingCode']);
  });

  it('báo lỗi khi shipment.status là số', () => {
    const detail = cloneDetailWithValues();
    detail.shipment = { ...detail.shipment, status: 1 };

    expect(firstIssuePath(detail)).toEqual(['shipment', 'status']);
  });

  it('báo lỗi khi suggestedParcel.weightGrams là chuỗi', () => {
    const detail = cloneDetailWithValues();
    detail.suggestedParcel = { ...detail.suggestedParcel, weightGrams: '500' };

    expect(firstIssuePath(detail)).toEqual(['suggestedParcel', 'weightGrams']);
  });

  it('báo lỗi khi invoice.companyName là số', () => {
    const detail = cloneDetailWithValues();
    detail.invoice = { ...detail.invoice, companyName: 123 };

    expect(firstIssuePath(detail)).toEqual(['invoice', 'companyName']);
  });

  it('báo lỗi khi paymentDeadline không phải giờ hợp lệ', () => {
    const detail = cloneDetailWithValues();
    detail.paymentDeadline = 'mai';

    expect(firstIssuePath(detail)).toEqual(['paymentDeadline']);
  });

  it.each(['skuId', 'name', 'unitPrice', 'lineTotal'])(
    'báo lỗi đúng đường dẫn khi dòng hàng thiếu "%s"',
    (key) => {
      const detail = cloneDetail();
      delete detail.items[0][key];

      expect(firstIssuePath(detail)).toEqual(['items', 0, key]);
    },
  );

  it.each(['fromStatus', 'toStatus', 'createdAt'])(
    'báo lỗi đúng đường dẫn khi dòng lịch sử thiếu "%s"',
    (key) => {
      const detail = cloneDetail();
      delete detail.statusHistory[0][key];

      expect(firstIssuePath(detail)).toEqual(['statusHistory', 0, key]);
    },
  );
});
