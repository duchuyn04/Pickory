// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import {
  actorWith,
  clickButton,
  createHarness,
  findButton,
  findField,
  queryOf,
  selectOption,
  waitFor,
  type AdminListHarness,
} from './admin-list-test-kit';

let harness: AdminListHarness;

afterEach(async () => {
  await harness.cleanup();
});

function customer(id: string) {
  return {
    id,
    email: `${id}@pickory.local`,
    name: `Khách ${id}`,
    phone: null,
    emailVerified: true,
    kind: 'customer',
    lockedAt: null,
    createdAt: '2026-10-01T00:00:00Z',
  };
}

function customersPage(items: unknown[], total = items.length, page = 1) {
  return { items, total, page, limit: 20 };
}

function customerRequests() {
  return harness.requestedUrls.filter((url) => url.startsWith('/api/admin/customers'));
}

const reader = actorWith(['customers:read']);
const searchLabel = 'Tìm theo tên, email hoặc số điện thoại';

describe('màn khách hàng có bộ lọc và phân trang', () => {
  it('mở link có bộ lọc thì gửi đúng bốn tham số và các ô hiện đúng giá trị', async () => {
    harness = createHarness(reader, () => customersPage([customer('c1')]));
    await harness.render('/admin/customers?search=an&status=locked&emailVerified=false&page=2');
    await waitFor(() => customerRequests().length > 0, 'request khách hàng');

    expect(queryOf(customerRequests()[0]!)).toEqual({
      search: 'an',
      status: 'locked',
      emailVerified: 'false',
      page: '2',
    });
    expect(findField(harness.container, searchLabel)!.value).toBe('an');
    expect(findField(harness.container, 'Trạng thái')!.value).toBe('locked');
    expect(findField(harness.container, 'Xác thực email')!.value).toBe('false');
  });

  it('đổi bộ lọc thì request mới không có page', async () => {
    harness = createHarness(reader, () => customersPage([customer('c1')]));
    await harness.render('/admin/customers?page=3');
    await waitFor(() => customerRequests().length > 0, 'request đầu');
    expect(queryOf(customerRequests()[0]!).page).toBe('3');

    await selectOption(findField(harness.container, 'Trạng thái')!, 'active');
    await waitFor(() => customerRequests().length > 1, 'request sau khi đổi bộ lọc');

    expect(queryOf(customerRequests().at(-1)!)).toEqual({ status: 'active' });
  });

  it('đổi chỉ bộ lọc trạng thái vẫn gọi lại API và hiện danh sách mới', async () => {
    // Danh sách trả về theo trạng thái. Khóa truy vấn thiếu `status` sẽ giữ lại danh sách cũ.
    harness = createHarness(reader, (url) => {
      const { status } = queryOf(url);
      return customersPage([customer(status === 'locked' ? 'khoa' : 'mo')]);
    });
    await harness.render('/admin/customers');
    await waitFor(
      () => harness.container.textContent!.includes('mo@pickory.local'),
      'danh sách đầu',
    );

    await selectOption(findField(harness.container, 'Trạng thái')!, 'locked');
    await waitFor(
      () => harness.container.textContent!.includes('khoa@pickory.local'),
      'danh sách khóa',
    );

    expect(queryOf(customerRequests().at(-1)!)).toEqual({ status: 'locked' });
  });

  it('danh sách rỗng khi có bộ lọc hiện thông báo không có kết quả phù hợp', async () => {
    harness = createHarness(reader, () => customersPage([]));
    await harness.render('/admin/customers?status=locked');
    await waitFor(
      () => harness.container.textContent!.includes('Không có kết quả phù hợp với bộ lọc.'),
      'thông báo rỗng',
    );

    const notice = harness.container.querySelector('[role="status"]')!;
    expect(notice.textContent).toContain('Không có kết quả phù hợp với bộ lọc.');
  });

  it('danh sách rỗng khi không có bộ lọc giữ thông báo cũ của màn', async () => {
    harness = createHarness(reader, () => customersPage([]));
    await harness.render('/admin/customers');
    await waitFor(
      () => harness.container.textContent!.includes('Không tìm thấy khách hàng nào phù hợp.'),
      'thông báo rỗng cũ',
    );

    expect(harness.container.textContent).not.toContain('Không có kết quả phù hợp với bộ lọc.');
  });

  it('bấm Trang sau gửi request với page=2', async () => {
    harness = createHarness(reader, () => customersPage([customer('c1')], 45));
    await harness.render('/admin/customers');
    await waitFor(() => findButton(harness.container, 'Trang sau') !== undefined, 'nút Trang sau');

    await clickButton(harness.container, 'Trang sau');
    await waitFor(() => customerRequests().length > 1, 'request trang 2');

    expect(queryOf(customerRequests().at(-1)!)).toEqual({ page: '2' });
  });

  it('mở link vượt quá số trang hiện thông báo rỗng và nút Về trang 1', async () => {
    harness = createHarness(reader, () => customersPage([], 30, 99));
    await harness.render('/admin/customers?page=99');
    await waitFor(
      () => findButton(harness.container, 'Về trang 1') !== undefined,
      'nút Về trang 1',
    );

    expect(harness.container.querySelector('[role="status"]')!.textContent).toContain(
      'Không có kết quả phù hợp với bộ lọc.',
    );
  });

  it('nút Về trang 1 xóa page và gửi request trang đầu', async () => {
    harness = createHarness(reader, () => customersPage([], 30, 99));
    await harness.render('/admin/customers?page=99');
    await waitFor(
      () => findButton(harness.container, 'Về trang 1') !== undefined,
      'nút Về trang 1',
    );

    await clickButton(harness.container, 'Về trang 1');
    await waitFor(() => customerRequests().length > 1, 'request trang 1');

    expect(queryOf(customerRequests().at(-1)!)).toEqual({});
  });
});
