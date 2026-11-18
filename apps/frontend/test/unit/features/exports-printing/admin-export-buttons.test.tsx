// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest';
import type * as DownloadFileModule from '../../../../src/shared/download-file';
import * as downloadFileModule from '../../../../src/shared/download-file';
import {
  actorWith,
  clickButton,
  createHarness,
  findButton,
  findField,
  selectOption,
  waitFor,
  type AdminListHarness,
} from '../identity/admin/admin-list-test-kit';

// Giả `downloadFile` để test bắt được đường dẫn mà nút gửi đi. Các export khác giữ nguyên.
vi.mock('../../../../src/shared/download-file', async (importOriginal) => {
  const original = await importOriginal<typeof DownloadFileModule>();
  return { ...original, downloadFile: vi.fn(async () => undefined) };
});

const EXPORT_LABEL = 'Xuất Excel';

let harness: AdminListHarness;

afterEach(async () => {
  await harness.cleanup();
  vi.mocked(downloadFileModule.downloadFile).mockClear();
});

// Trả danh sách rỗng đúng dạng của từng route, để trang vẽ xong mà không lỗi.
function respondWithEmptyLists(url: string) {
  if (url.startsWith('/api/admin/categories')) return [];
  return { items: [], total: 0, page: 1, limit: 20 };
}

async function open(url: string, permissions: string[]) {
  harness = createHarness(actorWith(permissions), respondWithEmptyLists);
  await harness.render(url);
}

// Chờ trang vẽ xong tiêu đề, rồi mới kiểm nút. Nút phụ thuộc actor nạp qua API.
async function waitForHeading(text: string) {
  await waitFor(
    () => harness.container.querySelector('h1')?.textContent === text,
    `tiêu đề ${text}`,
  );
}

function exportButton() {
  return findButton(harness.container, EXPORT_LABEL);
}

// Bấm nút và trả đường dẫn gửi cho `downloadFile` ở lần gọi mới nhất.
async function clickExportAndReadPath() {
  await clickButton(harness.container, EXPORT_LABEL);
  const calls = vi.mocked(downloadFileModule.downloadFile).mock.calls;
  return new URL(calls.at(-1)![0], 'http://localhost');
}

const PAGES = [
  {
    name: 'đơn hàng',
    url: '/admin/orders',
    heading: 'Quản lý đơn hàng',
    readPermission: 'orders:read',
    hasReadGate: true,
  },
  {
    name: 'sản phẩm',
    url: '/admin/products',
    heading: 'Sản phẩm',
    readPermission: 'products:read',
    hasReadGate: false,
  },
  {
    name: 'khách hàng',
    url: '/admin/customers',
    heading: 'Quản lý Khách hàng',
    readPermission: 'customers:read',
    hasReadGate: true,
  },
  {
    name: 'giao dịch thanh toán',
    url: '/admin/payments',
    heading: 'Đối soát thanh toán',
    readPermission: 'payments:read',
    hasReadGate: true,
  },
];

describe.each(PAGES)(
  'nút Xuất Excel của trang $name',
  ({ url, heading, readPermission, hasReadGate }) => {
    it('hiện khi actor có đủ quyền đọc và reports:export', async () => {
      await open(url, [readPermission, 'reports:export']);
      await waitForHeading(heading);
      await waitFor(() => exportButton() !== undefined, 'nút Xuất Excel');

      // Nút nằm trong khối đầu trang, không nằm lẫn trong bảng.
      expect(exportButton()!.closest('.page-head')).not.toBeNull();
    });

    it('ẩn khi actor thiếu reports:export', async () => {
      await open(url, [readPermission]);
      await waitForHeading(heading);

      expect(exportButton()).toBeUndefined();
    });

    it('ẩn khi actor thiếu quyền đọc của trang', async () => {
      await open(url, ['reports:export']);
      // Trang có cổng quyền đọc hiện thông báo 403. Trang sản phẩm không có cổng nên vẫn hiện tiêu đề.
      if (hasReadGate) {
        await waitFor(
          () => harness.container.textContent?.includes('403 - Không có quyền truy cập') === true,
          'thông báo 403',
        );
      } else {
        await waitForHeading(heading);
      }

      expect(exportButton()).toBeUndefined();
    });
  },
);

describe('đường dẫn mà nút gửi theo bộ lọc của trang', () => {
  it('đơn hàng: gửi status của tab và bộ lọc, rồi theo bộ lọc mới sau khi đổi', async () => {
    await open('/admin/orders?status=delivered&search=PK', ['orders:read', 'reports:export']);
    await waitFor(() => exportButton() !== undefined, 'nút Xuất Excel');

    const before = await clickExportAndReadPath();
    expect(before.pathname).toBe('/api/admin/exports/orders');
    expect(Object.fromEntries(before.searchParams)).toEqual({ status: 'delivered', search: 'PK' });

    await selectOption(findField(harness.container, 'Phương thức')!, 'cod');
    const after = await clickExportAndReadPath();
    expect(Object.fromEntries(after.searchParams)).toEqual({
      status: 'delivered',
      search: 'PK',
      paymentMethod: 'cod',
    });
  });

  it('sản phẩm: lowStock trên URL của trang thành lowStock=true, và theo bộ lọc mới', async () => {
    await open('/admin/products?lowStock=true&page=2', ['products:read', 'reports:export']);
    await waitFor(() => exportButton() !== undefined, 'nút Xuất Excel');

    const before = await clickExportAndReadPath();
    expect(before.pathname).toBe('/api/admin/exports/products');
    // Đường dẫn xuất không mang số trang của danh sách.
    expect(Object.fromEntries(before.searchParams)).toEqual({ lowStock: 'true' });

    await selectOption(findField(harness.container, 'Trạng thái')!, 'active');
    const after = await clickExportAndReadPath();
    expect(Object.fromEntries(after.searchParams)).toEqual({
      status: 'active',
      lowStock: 'true',
    });
  });

  it('khách hàng: gửi bộ lọc trên URL, rồi theo bộ lọc mới sau khi đổi', async () => {
    await open('/admin/customers?search=an&page=2', ['customers:read', 'reports:export']);
    await waitFor(() => exportButton() !== undefined, 'nút Xuất Excel');

    const before = await clickExportAndReadPath();
    expect(before.pathname).toBe('/api/admin/exports/customers');
    expect(Object.fromEntries(before.searchParams)).toEqual({ search: 'an' });

    await selectOption(findField(harness.container, 'Trạng thái')!, 'locked');
    const after = await clickExportAndReadPath();
    expect(Object.fromEntries(after.searchParams)).toEqual({ search: 'an', status: 'locked' });
  });

  it('giao dịch: URL không có matchStatus thì nút gửi unmatched như danh sách', async () => {
    await open('/admin/payments', ['payments:read', 'reports:export']);
    await waitFor(() => exportButton() !== undefined, 'nút Xuất Excel');

    const path = await clickExportAndReadPath();
    expect(path.pathname).toBe('/api/admin/exports/payment-transactions');
    expect(Object.fromEntries(path.searchParams)).toEqual({ matchStatus: 'unmatched' });
  });

  it('giao dịch: chọn Tất cả thì bỏ matchStatus, chọn orderFlag thì gửi orderFlag', async () => {
    await open('/admin/payments', ['payments:read', 'reports:export']);
    await waitFor(() => exportButton() !== undefined, 'nút Xuất Excel');

    await selectOption(findField(harness.container, 'Trạng thái khớp')!, 'all');
    await selectOption(findField(harness.container, 'Ghi chú')!, 'underpaid');
    const path = await clickExportAndReadPath();

    expect(Object.fromEntries(path.searchParams)).toEqual({ orderFlag: 'underpaid' });
  });

  it('giao dịch: tab COD không có nút Xuất Excel', async () => {
    await open('/admin/payments?tab=cod', ['payments:read', 'orders:read', 'reports:export']);
    await waitFor(
      () => harness.container.querySelector('[role="tab"][aria-selected="true"]') !== null,
      'tab đã chọn',
    );

    expect(harness.container.querySelector('[role="tab"][aria-selected="true"]')!.textContent).toBe(
      'COD',
    );
    expect(exportButton()).toBeUndefined();
  });
});
