// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import {
  actorWith,
  createHarness,
  waitFor,
  type AdminListHarness,
} from '../../../identity/admin/admin-list-test-kit';

let harness: AdminListHarness;

afterEach(async () => {
  await harness.cleanup();
});

function fullOrder() {
  return {
    code: 'PK-1001',
    status: 'shipping',
    paymentStatus: 'paid',
    paymentMethod: 'sepay',
    createdAt: '2026-10-12T03:30:00Z',
    paymentDeadline: '2026-10-12T04:30:00Z',
    flags: ['underpaid', 'invoice_issued'],
    contact: { name: 'Nguyễn Văn An', phone: '0901234567', email: 'an@example.com' },
    shippingAddress: {
      provinceCode: '79',
      provinceName: 'TP. Hồ Chí Minh',
      wardCode: '1',
      wardName: 'Phường Bến Nghé',
      line: '12 Lê Lợi',
    },
    invoice: {
      companyName: 'Công ty ABC',
      taxCode: '0312345678',
      address: '1 Nguyễn Huệ',
      email: 'ketoan@abc.vn',
    },
    items: [
      {
        skuId: 'sku-1',
        name: 'Áo thun',
        variantName: 'Đỏ / M',
        imagePublicId: null,
        imageUrl: null,
        quantity: 2,
        unitPrice: 250000,
        lineTotal: 500000,
      },
    ],
    subtotal: 500000,
    shippingFee: 30000,
    shippingDiscount: 10000,
    total: 520000,
    shippingFeeSource: 'ghn',
    statusHistory: [
      {
        fromStatus: 'pending_confirmation',
        toStatus: 'confirmed',
        actorId: 'abcdef1234567890',
        actorName: 'Người dùng thử',
        reason: 'Đã gọi xác nhận',
        createdAt: '2026-10-12T05:00:00Z',
      },
      {
        fromStatus: 'pending_payment',
        toStatus: 'pending_confirmation',
        actorId: null,
        actorName: null,
        reason: null,
        createdAt: '2026-10-12T03:35:00Z',
      },
    ],
    shipment: { id: 'ship-1', trackingCode: 'GHN123456', status: 'in_transit', fee: 30000 },
    suggestedParcel: null,
    refundTransactionRef: 'FT26101200001',
  };
}

function minimalOrder() {
  return {
    ...fullOrder(),
    paymentDeadline: null,
    flags: [] as string[],
    invoice: null,
    shipment: null,
    refundTransactionRef: null,
    shippingDiscount: 0,
    statusHistory: [] as unknown[],
  };
}

function open(order: unknown, permissions = ['orders:read']) {
  harness = createHarness(actorWith(permissions), (url) => {
    if (url === '/api/admin/orders/PK-1001') {
      return order instanceof Response ? order : order;
    }
    return [];
  });
  return harness.render('/admin/orders/PK-1001');
}

function notFound() {
  return new Response(
    JSON.stringify({ error: { code: 'ORDER_NOT_FOUND', message: 'Không tìm thấy đơn hàng.' } }),
    { status: 404 },
  );
}

function text() {
  return harness.container.textContent ?? '';
}

// Thẻ có tiêu đề cho trước, ví dụ thẻ "Vận đơn".
function sectionOf(title: string): HTMLElement {
  const heading = [...harness.container.querySelectorAll('.order-section-title')].find(
    (element) => element.textContent === title,
  );
  const section = heading?.closest('section');
  if (!section) throw new Error(`Không thấy thẻ ${title}`);
  return section;
}

// Dòng nhãn và giá trị trong một thẻ, tìm theo chữ của nhãn.
function rowIn(container: HTMLElement, label: string): HTMLElement {
  const term = [...container.querySelectorAll('dt')].find(
    (element) => element.textContent === label,
  );
  const row = term?.parentElement;
  if (!row) throw new Error(`Không thấy dòng ${label}`);
  return row;
}

function feeRow(label: string): HTMLElement {
  return rowIn(sectionOf('Thanh toán'), label);
}

describe('màn chi tiết đơn hàng của nhân viên', () => {
  it('hiện đủ các thẻ khi đơn có đầy đủ dữ liệu', async () => {
    await open(fullOrder());
    await waitFor(() => text().includes('PK-1001'), 'mã đơn');

    const page = text();
    // Phần đầu.
    expect(harness.container.querySelector('h1')!.textContent).toContain('PK-1001');
    expect(page).toContain('Đang giao hàng');
    expect(page).toContain('Đã thanh toán');
    expect(page).toContain('Chuyển khoản ngân hàng');
    expect(page).toContain('Thanh toán thiếu');
    expect(page).toContain('Đã xuất hóa đơn');
    expect(page).toContain('Hạn thanh toán');
    // Người nhận và địa chỉ.
    expect(page).toContain('Nguyễn Văn An');
    expect(page).toContain('0901234567');
    expect(page).toContain('an@example.com');
    expect(page).toContain('12 Lê Lợi');
    expect(page).toContain('Phường Bến Nghé');
    expect(page).toContain('TP. Hồ Chí Minh');
    // Hóa đơn VAT.
    expect(page).toContain('Công ty ABC');
    expect(page).toContain('0312345678');
    // Dòng hàng và tổng.
    expect(page).toContain('Áo thun');
    expect(page).toContain('Đỏ / M');
    expect(page).toContain('500.000');
    expect(page).toContain('30.000');
    expect(page).toContain('10.000');
    expect(page).toContain('520.000');
    // Vận đơn.
    expect(page).toContain('GHN123456');
    expect(page).toContain('Đang vận chuyển');
    // Hoàn tiền.
    expect(page).toContain('FT26101200001');
  });

  it('ẩn thẻ hóa đơn, vận đơn, mã hoàn tiền và hạn thanh toán khi dữ liệu là null', async () => {
    await open(minimalOrder());
    await waitFor(() => text().includes('PK-1001'), 'mã đơn');

    const page = text();
    expect(page).not.toContain('Hóa đơn VAT');
    expect(page).not.toContain('Vận đơn');
    expect(page).not.toContain('Mã giao dịch hoàn tiền');
    expect(page).not.toContain('Hạn thanh toán');
  });

  it('đơn dùng phí GHN thì trang giữ như cũ, chỉ đổi nhãn phí vận đơn thành Phí GHN thực tế', async () => {
    await open(fullOrder());
    await waitFor(() => text().includes('PK-1001'), 'mã đơn');

    const page = text();
    expect(page).not.toContain('Phí ship dự phòng');
    expect(page).not.toContain('Theo bảng phí dự phòng');
    expect(page).not.toContain('Theo phí GHN đã lưu');
    expect(page).not.toContain('Khách đã trả');
    expect(page).toContain('Phí GHN thực tế');
  });

  it('đơn dùng phí dự phòng có nhãn cạnh dòng Phí vận chuyển và câu giải thích', async () => {
    await open({ ...fullOrder(), shippingFeeSource: 'fallback' });
    await waitFor(() => text().includes('PK-1001'), 'mã đơn');

    const row = feeRow('Phí vận chuyển');
    expect(row.textContent).toContain('Phí ship dự phòng');
    expect(row.textContent).toContain('Theo bảng phí dự phòng');
    expect(row.querySelector('.badge')?.textContent).toBe('Phí ship dự phòng');
    // Các dòng tiền khác không có nhãn.
    expect(feeRow('Tạm tính').textContent).not.toContain('Phí ship dự phòng');
  });

  it('đơn dùng phí GHN đã lưu có cùng nhãn và câu giải thích riêng', async () => {
    await open({ ...fullOrder(), shippingFeeSource: 'cached' });
    await waitFor(() => text().includes('PK-1001'), 'mã đơn');

    const row = feeRow('Phí vận chuyển');
    expect(row.textContent).toContain('Phí ship dự phòng');
    expect(row.textContent).toContain('Theo phí GHN đã lưu');
    expect(row.textContent).not.toContain('Theo bảng phí dự phòng');
  });

  it('có vận đơn và nguồn phí khác GHN thì hiện Phí GHN thực tế cạnh Khách đã trả', async () => {
    // Phí GHN thực tế 41.000 đ. Khách trả 30.000 đ trừ giảm 10.000 đ là 20.000 đ.
    await open({
      ...fullOrder(),
      shippingFeeSource: 'fallback',
      shipment: { id: 'ship-1', trackingCode: 'GHN123456', status: 'in_transit', fee: 41000 },
    });
    await waitFor(() => text().includes('PK-1001'), 'mã đơn');

    const shipmentCard = sectionOf('Vận đơn');
    expect(rowIn(shipmentCard, 'Phí GHN thực tế').textContent).toContain('41.000');
    expect(rowIn(shipmentCard, 'Khách đã trả').textContent).toContain('20.000');
  });

  it('có vận đơn và nguồn phí GHN thì không có dòng Khách đã trả', async () => {
    await open(fullOrder());
    await waitFor(() => text().includes('PK-1001'), 'mã đơn');

    const shipmentCard = sectionOf('Vận đơn');
    expect(shipmentCard.textContent).toContain('Phí GHN thực tế');
    expect(shipmentCard.textContent).not.toContain('Khách đã trả');
  });

  it('chưa có vận đơn thì đơn dùng phí dự phòng vẫn có nhãn nhưng không có thẻ Vận đơn', async () => {
    await open({ ...minimalOrder(), shippingFeeSource: 'fallback' });
    await waitFor(() => text().includes('PK-1001'), 'mã đơn');

    expect(text()).toContain('Phí ship dự phòng');
    expect(text()).not.toContain('Vận đơn');
    expect(text()).not.toContain('Khách đã trả');
  });

  const MANUAL_CASES = [
    { flags: ['manual_shipping'], labels: ['Giao hàng thủ công'] },
    { flags: ['manual_delivery'], labels: ['Xác nhận giao thủ công'] },
    { flags: ['manual_return'], labels: ['Ghi nhận hoàn thủ công'] },
    {
      flags: ['manual_shipping', 'manual_delivery', 'manual_return'],
      labels: ['Giao hàng thủ công', 'Xác nhận giao thủ công', 'Ghi nhận hoàn thủ công'],
    },
  ];

  it.each(MANUAL_CASES)(
    'thẻ Vận đơn hiện dòng Ghi nhận thủ công với cờ $flags, trạng thái GHN giữ nguyên',
    async ({ flags, labels }) => {
      await open({
        ...fullOrder(),
        flags,
        shipment: { id: 'ship-1', trackingCode: 'GHN1', status: 'pending_pickup', fee: 30000 },
      });
      await waitFor(() => text().includes('PK-1001'), 'mã đơn');

      const shipmentCard = sectionOf('Vận đơn');
      const badges = [...rowIn(shipmentCard, 'Ghi nhận thủ công').querySelectorAll('dd .badge')];
      expect(badges.map((badge) => badge.textContent)).toEqual(labels);
      expect(badges.every((badge) => badge.className === 'badge badge-warn')).toBe(true);
      // Trạng thái GHN không bị ghi đè bởi nhãn thủ công.
      expect(rowIn(shipmentCard, 'Trạng thái').textContent).toContain('Chờ lấy hàng');
      expect(rowIn(shipmentCard, 'Trạng thái').textContent).not.toContain('Đã giao');
      // Dòng mới nằm ngay dưới dòng Trạng thái.
      expect(rowIn(shipmentCard, 'Trạng thái').nextElementSibling).toBe(
        rowIn(shipmentCard, 'Ghi nhận thủ công'),
      );
    },
  );

  it('dòng Ghi nhận thủ công chỉ liệt kê cờ thủ công, không lấy cờ khác', async () => {
    await open({
      ...fullOrder(),
      flags: ['invoice_issued', 'manual_delivery', 'underpaid'],
    });
    await waitFor(() => text().includes('PK-1001'), 'mã đơn');

    const row = rowIn(sectionOf('Vận đơn'), 'Ghi nhận thủ công');
    expect(row.textContent).toContain('Xác nhận giao thủ công');
    expect(row.textContent).not.toContain('Đã xuất hóa đơn');
    expect(row.textContent).not.toContain('Thanh toán thiếu');
  });

  it('đơn có vận đơn mà không có cờ thủ công không hiện dòng Ghi nhận thủ công', async () => {
    await open({ ...fullOrder(), flags: [] });
    await waitFor(() => text().includes('PK-1001'), 'mã đơn');

    expect(sectionOf('Vận đơn').textContent).not.toContain('Ghi nhận thủ công');
  });

  it('đơn có vận đơn và chỉ cờ invoice_issued không hiện dòng Ghi nhận thủ công', async () => {
    await open({ ...fullOrder(), flags: ['invoice_issued'] });
    await waitFor(() => text().includes('PK-1001'), 'mã đơn');

    expect(sectionOf('Vận đơn').textContent).not.toContain('Ghi nhận thủ công');
  });

  it('đơn giao thủ công không có vận đơn không dựng thẻ Vận đơn, nhãn nằm ở hàng Ghi chú', async () => {
    await open({ ...minimalOrder(), flags: ['manual_shipping'] });
    await waitFor(() => text().includes('PK-1001'), 'mã đơn');

    const titles = [...harness.container.querySelectorAll('.order-section-title')].map(
      (element) => element.textContent,
    );
    expect(titles).not.toContain('Vận đơn');
    expect(text()).not.toContain('Ghi nhận thủ công');
    const overview = harness.container.querySelector<HTMLElement>('.order-overview')!;
    expect(rowIn(overview, 'Ghi chú').textContent).toContain('Giao hàng thủ công');
  });

  it('lịch sử theo thời gian tăng dần, tô màu trạng thái và hiện tên nhân viên hoặc Hệ thống', async () => {
    await open(fullOrder());
    await waitFor(() => text().includes('PK-1001'), 'mã đơn');

    const rows = [...harness.container.querySelectorAll('.order-history-table tbody tr')];
    expect(rows).toHaveLength(2);
    // Mục sớm hơn nằm trên: do hệ thống chuyển, không có lý do.
    expect(rows[0]!.textContent).toContain('Chờ thanh toán');
    expect(rows[0]!.textContent).toContain('Chờ xác nhận');
    expect(rows[0]!.textContent).toContain('Hệ thống');
    // Trạng thái trong lịch sử cũng dùng badge màu như phần tổng quan.
    const firstBadges = [...rows[0]!.querySelectorAll('.badge')].map((badge) => badge.textContent);
    expect(firstBadges).toEqual(['Chờ thanh toán', 'Chờ xác nhận']);
    expect(rows[0]!.querySelector('.badge-warn')?.textContent).toBe('Chờ thanh toán');
    // Mục sau: do nhân viên chuyển kèm lý do và tên nhân viên.
    expect(rows[1]!.textContent).toContain('Đã xác nhận');
    expect(rows[1]!.textContent).toContain('Người dùng thử');
    expect(rows[1]!.textContent).toContain('Đã gọi xác nhận');
  });

  it('mã đơn không tồn tại hiện thông báo tiếng Việt và liên kết về danh sách', async () => {
    await open(notFound());
    await waitFor(() => text().includes('Không tìm thấy đơn hàng.'), 'thông báo 404');

    const link = [...harness.container.querySelectorAll('a')].find(
      (anchor) => anchor.textContent === 'Về danh sách đơn hàng',
    )!;
    expect(link.getAttribute('href')).toBe('/admin/orders');
  });

  it('người chỉ có orders:read không thấy nút thao tác nào', async () => {
    await open(fullOrder());
    await waitFor(() => text().includes('PK-1001'), 'mã đơn');

    expect(harness.container.querySelector('main button, .page button')).toBeNull();
  });

  it('người không có orders:read thấy thông báo không có quyền', async () => {
    await open(fullOrder(), ['products:read']);
    await waitFor(() => text().includes('orders:read'), 'thông báo không có quyền');

    expect(harness.requestedUrls.filter((url) => url.startsWith('/api/admin/orders'))).toHaveLength(
      0,
    );
  });
});
