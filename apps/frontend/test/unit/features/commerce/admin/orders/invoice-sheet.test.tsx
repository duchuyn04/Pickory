// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { OrderInvoicesResponse } from '../../../../../../src/features/commerce/admin/orders/order-invoices-schema';
import { formatVndInWords } from '../../../../../../src/shared/format-vnd-in-words';
import { formatVietnamDateTime } from '../../../../../../src/shared/vietnam-time';
import { findExportsPrintingExample } from '../../../exports-printing/contract-fixtures';
import { actorWith, waitFor } from '../../../identity/admin/admin-list-test-kit';
import { createPrintHarness, jsonResponse, type PrintHarness } from './print-page-test-kit';

let harness: PrintHarness | undefined;

afterEach(async () => {
  vi.useRealTimers();
  await harness?.cleanup();
  harness = undefined;
  vi.restoreAllMocks();
});

describe('trang in hóa đơn - nội dung 10 phần và thông tin shop', () => {
  it('hiển thị đủ 10 phần, đúng thứ tự, VAT, dấu hủy, STT và tiền bằng chữ từ ví dụ hai đơn', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-11-19T03:05:00Z'));

    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices two orders with vat and cancelled',
    );
    const data = (example.response as OrderInvoicesResponse).data;
    const firstOrder = data.invoices[0]!;
    const secondOrder = data.invoices[1]!;

    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse(example.response, example.status),
    );

    await harness.render('/admin/orders/print?codes=PK7KQ2M9XA,PK3HN8RT2B');
    await waitFor(
      () => harness?.container.querySelectorAll('article.invoice-sheet').length === 2,
      'vẽ đủ hai hóa đơn',
    );

    const sheets = Array.from(
      harness.container.querySelectorAll<HTMLElement>('article.invoice-sheet'),
    );
    expect(sheets).toHaveLength(2);
    expect(sheets[0]!.dataset.testid).toBe(`invoice-${firstOrder.code}`);
    expect(sheets[1]!.dataset.testid).toBe(`invoice-${secondOrder.code}`);

    const firstText = sheets[0]!.textContent ?? '';
    const secondText = sheets[1]!.textContent ?? '';

    const readMetaValue = (sheet: HTMLElement, label: string) =>
      Array.from(sheet.querySelectorAll('.invoice-meta > div'))
        .find((row) => row.querySelector('dt')?.textContent === label)
        ?.querySelector('dd')?.textContent;

    const readLabeledLine = (container: Element, label: string) =>
      Array.from(container.querySelectorAll('p')).find((paragraph) =>
        paragraph.textContent?.startsWith(label),
      )?.textContent;

    const expectElementOrder = (elements: (Element | null)[]) => {
      for (let index = 0; index < elements.length - 1; index += 1) {
        const current = elements[index];
        const next = elements[index + 1];
        expect(current).not.toBeNull();
        expect(next).not.toBeNull();
        expect(
          current!.compareDocumentPosition(next!) & Node.DOCUMENT_POSITION_FOLLOWING,
        ).toBeTruthy();
      }
    };

    // Kiểm tra phần 1 và dấu đơn đã hủy chỉ hiện ở đơn cancelled.
    expect(firstText).not.toContain('ĐƠN ĐÃ HỦY');
    expect(secondText).toContain('ĐƠN ĐÃ HỦY');

    // Kiểm tra logo và thông tin shop đầy đủ.
    const firstLogo = sheets[0]!.querySelector<HTMLImageElement>('img.invoice-logo');
    expect(firstLogo?.getAttribute('src')).toBe('/media/brand/pickory-logo.png');
    const firstShop = sheets[0]!.querySelector('.invoice-shop-info')?.textContent ?? '';
    expect(firstShop).toContain('Pickory');
    expect(firstShop).toContain('12 Nguyễn Văn Bảo, Phường 4, Quận Gò Vấp, TP. Hồ Chí Minh');
    expect(firstShop).toContain('028 3894 0390');
    expect(firstShop).toContain('0312345678');

    // Kiểm tra từng cặp nhãn và giá trị cho mã đơn, ngày đặt và ngày in theo giờ giả.
    expect(firstText).toContain('HÓA ĐƠN BÁN HÀNG');
    expect(readMetaValue(sheets[0]!, 'Mã đơn:')).toBe(firstOrder.code);
    expect(readMetaValue(sheets[0]!, 'Ngày đặt:')).toBe(
      formatVietnamDateTime(firstOrder.createdAt),
    );
    expect(readMetaValue(sheets[0]!, 'Ngày in:')).toBe('10:05 19/11/2026');
    expect(readMetaValue(sheets[1]!, 'Mã đơn:')).toBe(secondOrder.code);
    expect(readMetaValue(sheets[1]!, 'Ngày đặt:')).toBe(
      formatVietnamDateTime(secondOrder.createdAt),
    );
    expect(readMetaValue(sheets[1]!, 'Ngày in:')).toBe('10:05 19/11/2026');

    // Kiểm tra thông tin khách theo từng dòng nhãn và bảo đảm không in email khách hàng.
    const firstCustomer = sheets[0]!.querySelectorAll('.invoice-party')[0]!;
    expect(readLabeledLine(firstCustomer, 'Khách hàng:')).toContain(firstOrder.customer.name);
    expect(readLabeledLine(firstCustomer, 'Số điện thoại:')).toContain(firstOrder.customer.phone);
    expect(readLabeledLine(firstCustomer, 'Địa chỉ giao:')).toContain(
      firstOrder.shippingAddressText,
    );
    expect(firstText).not.toContain(firstOrder.customer.email);
    expect(secondText).not.toContain(secondOrder.customer.email);

    // Kiểm tra khối thuế giá trị gia tăng (VAT) theo từng cặp nhãn và vắng mặt ở đơn 2.
    const firstVat = sheets[0]!.querySelector('.invoice-vat')!;
    expect(readLabeledLine(firstVat, 'Tên công ty:')).toContain(firstOrder.vatInvoice!.companyName);
    expect(readLabeledLine(firstVat, 'Mã số thuế:')).toContain(firstOrder.vatInvoice!.taxCode);
    expect(readLabeledLine(firstVat, 'Địa chỉ:')).toContain(firstOrder.vatInvoice!.address);
    expect(readLabeledLine(firstVat, 'Email nhận hóa đơn:')).toContain(
      firstOrder.vatInvoice!.email,
    );
    expect(secondText).not.toContain('Công ty');
    expect(secondText).not.toContain('VAT');

    // Kiểm tra số thứ tự bắt đầu từ 1, số lượng, đơn giá và thành tiền theo từng ô bảng.
    const firstRows = Array.from(sheets[0]!.querySelectorAll('tbody tr'));
    const secondRows = Array.from(sheets[1]!.querySelectorAll('tbody tr'));
    expect(firstRows).toHaveLength(2);
    expect(secondRows).toHaveLength(1);

    const row1Cells = Array.from(firstRows[0]!.querySelectorAll('td'));
    expect(row1Cells[0]!.textContent).toBe('1');
    expect(row1Cells[1]!.textContent).toContain('iPhone 16 256GB');
    expect(row1Cells[1]!.textContent).toContain('Đen, 256 GB · SKU: IP16-256-BLK');
    expect(row1Cells[2]!.textContent).toBe('1');
    expect(row1Cells[3]!.textContent).toBe('24.990.000 đ');
    expect(row1Cells[4]!.textContent).toBe('24.990.000 đ');

    const row2Cells = Array.from(firstRows[1]!.querySelectorAll('td'));
    expect(row2Cells[0]!.textContent).toBe('2');
    expect(row2Cells[1]!.textContent).toContain('Cáp USB-C 1 m');
    expect(row2Cells[1]!.textContent).toContain('SKU: CBL-USBC-1M');
    expect(row2Cells[1]!.textContent).not.toContain('null');
    expect(row2Cells[1]!.textContent).not.toContain(' · ');
    expect(firstRows[1]!.querySelector('.invoice-line-option')).toBeNull();
    expect(row2Cells[2]!.textContent).toBe('2');
    expect(row2Cells[3]!.textContent).toBe('190.000 đ');
    expect(row2Cells[4]!.textContent).toBe('380.000 đ');

    const secondRowCells = Array.from(secondRows[0]!.querySelectorAll('td'));
    expect(secondRowCells[0]!.textContent).toBe('1');
    expect(secondRowCells[1]!.textContent).toContain('AirPods Pro 2');
    expect(secondRowCells[1]!.textContent).toContain('SKU: AIRP-PRO2');
    expect(secondRowCells[2]!.textContent).toBe('1');
    expect(secondRowCells[3]!.textContent).toBe('5.990.000 đ');
    expect(secondRowCells[4]!.textContent).toBe('5.990.000 đ');

    // Kiểm tra phần tổng tiền, giảm phí vận chuyển và tổng bằng chữ.
    const findTotalValue = (sheet: HTMLElement, label: string) =>
      Array.from(sheet.querySelectorAll('.invoice-totals > div'))
        .find((row) => row.querySelector('dt')?.textContent === label)
        ?.querySelector('dd')?.textContent;

    expect(findTotalValue(sheets[0]!, 'Tạm tính')).toBe('25.370.000 đ');
    expect(findTotalValue(sheets[0]!, 'Phí vận chuyển')).toBe('35.000 đ');
    expect(findTotalValue(sheets[0]!, 'Giảm phí vận chuyển')).toBe('-35.000 đ');
    expect(findTotalValue(sheets[0]!, 'Tổng cộng')).toBe('25.370.000 đ');
    expect(readLabeledLine(sheets[0]!, 'Tổng cộng bằng chữ:')).toContain(
      formatVndInWords(firstOrder.total),
    );

    expect(findTotalValue(sheets[1]!, 'Tạm tính')).toBe('5.990.000 đ');
    expect(findTotalValue(sheets[1]!, 'Phí vận chuyển')).toBe('30.000 đ');
    expect(findTotalValue(sheets[1]!, 'Giảm phí vận chuyển')).toBe('0 đ');
    expect(findTotalValue(sheets[1]!, 'Tổng cộng')).toBe('6.020.000 đ');
    expect(readLabeledLine(sheets[1]!, 'Tổng cộng bằng chữ:')).toContain(
      formatVndInWords(secondOrder.total),
    );
    expect(secondText).not.toContain(formatVndInWords(secondOrder.subtotal));

    // Kiểm tra nhãn tiếng Việt của phương thức và trạng thái thanh toán theo từng dòng.
    expect(readLabeledLine(sheets[0]!, 'Phương thức thanh toán:')).toContain(
      'Chuyển khoản ngân hàng',
    );
    expect(readLabeledLine(sheets[0]!, 'Trạng thái thanh toán:')).toContain('Đã thanh toán');
    expect(readLabeledLine(sheets[1]!, 'Phương thức thanh toán:')).toContain(
      'Thanh toán khi nhận hàng (COD)',
    );
    expect(readLabeledLine(sheets[1]!, 'Trạng thái thanh toán:')).toContain('Chưa thanh toán');

    // Kiểm tra hai ô ký tên và dòng chú thích cuối hóa đơn.
    expect(firstText).toContain('Người mua hàng');
    expect(firstText).toContain('Người bán hàng');
    expect(firstText).toContain('(Ký, ghi rõ họ tên)');
    expect(firstText).toContain('Hóa đơn này không thay thế hóa đơn giá trị gia tăng.');

    // Kiểm tra thứ tự cây phần tử khớp đúng 10 phần của tài liệu đặc tả.
    expectElementOrder([
      sheets[0]!.querySelector('.invoice-logo'),
      sheets[0]!.querySelector('.invoice-title'),
      firstCustomer,
      firstVat,
      sheets[0]!.querySelector('.invoice-items-table'),
      sheets[0]!.querySelector('.invoice-totals'),
      sheets[0]!.querySelector('.invoice-amount-words'),
      sheets[0]!.querySelector('.invoice-payment-info'),
      sheets[0]!.querySelector('.invoice-signatures'),
      sheets[0]!.querySelector('.invoice-footer-note'),
    ]);
    expectElementOrder([
      sheets[1]!.querySelector('[data-testid="invoice-cancelled-stamp"]'),
      sheets[1]!.querySelector('.invoice-logo'),
      sheets[1]!.querySelector('.invoice-title'),
      sheets[1]!.querySelector('.invoice-items-table'),
      sheets[1]!.querySelector('.invoice-totals'),
      sheets[1]!.querySelector('.invoice-amount-words'),
      sheets[1]!.querySelector('.invoice-payment-info'),
      sheets[1]!.querySelector('.invoice-signatures'),
      sheets[1]!.querySelector('.invoice-footer-note'),
    ]);
  });

  it('in Pickory và ẩn mọi dòng địa chỉ, điện thoại, mã số thuế khi shop chưa nhập', async () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices shop profile not set',
    );
    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse(example.response, example.status),
    );

    await harness.render('/admin/orders/print?codes=PK3HN8RT2B');
    await waitFor(
      () => harness?.container.querySelectorAll('article.invoice-sheet').length === 1,
      'vẽ hóa đơn shop chưa nhập',
    );

    const shopText = harness.container.querySelector('.invoice-shop-info')?.textContent ?? '';
    expect(shopText).toContain('Pickory');
    expect(shopText).not.toContain('Địa chỉ:');
    expect(shopText).not.toContain('Điện thoại:');
    expect(shopText).not.toContain('Mã số thuế:');
  });

  it.each([
    {
      field: 'name' as const,
      value: null as string | null,
      expectedVisible: ['12 Nguyễn Văn Bảo', '028 3894 0390', '0312345678'],
      expectedHiddenLabel: null,
      expectedFallbackName: 'Pickory',
    },
    {
      field: 'name' as const,
      value: '   ',
      expectedVisible: ['12 Nguyễn Văn Bảo', '028 3894 0390', '0312345678'],
      expectedHiddenLabel: null,
      expectedFallbackName: 'Pickory',
    },
    {
      field: 'address' as const,
      value: null as string | null,
      expectedVisible: ['Shop Thử Nghiệm', '028 3894 0390', '0312345678'],
      expectedHiddenLabel: 'Địa chỉ:',
      expectedFallbackName: null,
    },
    {
      field: 'address' as const,
      value: '  ',
      expectedVisible: ['Shop Thử Nghiệm', '028 3894 0390', '0312345678'],
      expectedHiddenLabel: 'Địa chỉ:',
      expectedFallbackName: null,
    },
    {
      field: 'phone' as const,
      value: null as string | null,
      expectedVisible: ['Shop Thử Nghiệm', '12 Nguyễn Văn Bảo', '0312345678'],
      expectedHiddenLabel: 'Điện thoại:',
      expectedFallbackName: null,
    },
    {
      field: 'taxCode' as const,
      value: null as string | null,
      expectedVisible: ['Shop Thử Nghiệm', '12 Nguyễn Văn Bảo', '028 3894 0390'],
      expectedHiddenLabel: 'Mã số thuế:',
      expectedFallbackName: null,
    },
  ])(
    'khi trường shop.$field là "$value" thì ẩn hoặc dùng tên dự phòng đúng quy tắc',
    async ({ field, value, expectedVisible, expectedHiddenLabel, expectedFallbackName }) => {
      const example = findExportsPrintingExample(
        'order-invoices.json',
        'order invoices shop profile not set',
      );
      const payload = structuredClone(example.response) as OrderInvoicesResponse;
      payload.data.shop = {
        name: 'Shop Thử Nghiệm',
        address: '12 Nguyễn Văn Bảo',
        phone: '028 3894 0390',
        taxCode: '0312345678',
      };
      payload.data.shop[field] = value;

      harness = createPrintHarness(
        () => jsonResponse({ data: actorWith(['orders:read']) }),
        () => jsonResponse(payload, 200),
      );

      await harness.render('/admin/orders/print?codes=PK3HN8RT2B');
      await waitFor(
        () => harness?.container.querySelectorAll('article.invoice-sheet').length === 1,
        'vẽ hóa đơn có trường shop trống',
      );

      const shopText = harness.container.querySelector('.invoice-shop-info')?.textContent ?? '';
      for (const visibleValue of expectedVisible) {
        expect(shopText).toContain(visibleValue);
      }
      if (expectedHiddenLabel) {
        expect(shopText).not.toContain(expectedHiddenLabel);
      }
      if (expectedFallbackName) {
        expect(shopText).toContain(expectedFallbackName);
      }
    },
  );

  it('ẩn phân loại và dấu phân cách khi optionText là chuỗi rỗng', async () => {
    const example = findExportsPrintingExample(
      'order-invoices.json',
      'order invoices shop profile not set',
    );
    const payload = structuredClone(example.response) as OrderInvoicesResponse;
    payload.data.invoices[0]!.lines[0]!.optionText = '   ';

    harness = createPrintHarness(
      () => jsonResponse({ data: actorWith(['orders:read']) }),
      () => jsonResponse(payload, 200),
    );

    await harness.render('/admin/orders/print?codes=PK3HN8RT2B');
    await waitFor(
      () => harness?.container.querySelectorAll('article.invoice-sheet').length === 1,
      'vẽ hóa đơn có optionText khoảng trắng',
    );

    const row = harness.container.querySelector('tbody tr')!;
    expect(row.querySelector('.invoice-line-option')).toBeNull();
    expect(row.textContent).not.toContain(' · ');
  });
});
