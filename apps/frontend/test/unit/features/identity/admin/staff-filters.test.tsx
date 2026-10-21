// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import {
  actorWith,
  createHarness,
  findField,
  queryOf,
  waitFor,
  type AdminListHarness,
} from './admin-list-test-kit';

let harness: AdminListHarness;

afterEach(async () => {
  await harness.cleanup();
});

function staff(id: string, status: 'active' | 'invited' | 'locked') {
  return {
    id,
    email: `${id}@pickory.local`,
    name: `Nhân viên ${id}`,
    phone: null,
    emailVerified: status !== 'invited',
    kind: 'staff',
    lockedAt: status === 'locked' ? '2026-10-02T00:00:00Z' : null,
    createdAt: '2026-10-01T00:00:00Z',
    roles: ['sales'],
    status,
  };
}

const roles = [
  { id: 'r1', code: 'admin', name: 'Quản trị', isSystem: true, permissions: [] },
  { id: 'r2', code: 'sales', name: 'Bán hàng', isSystem: false, permissions: [] },
  { id: 'r3', code: 'customer', name: 'Khách hàng', isSystem: true, permissions: [] },
];

function respondWith(items: unknown[]) {
  return (url: string) => {
    if (url.startsWith('/api/admin/staff'))
      return { items, total: items.length, page: 1, limit: 20 };
    if (url === '/api/admin/roles') return roles;
    return [];
  };
}

function staffRequests() {
  return harness.requestedUrls.filter((url) => url.startsWith('/api/admin/staff'));
}

function rowOf(email: string) {
  return Array.from(harness.container.querySelectorAll('tbody tr')).find((row) =>
    row.textContent!.includes(email),
  )!;
}

describe('màn nhân viên có bộ lọc và phân trang', () => {
  it('mở link có bộ lọc thì gửi đúng tham số và các ô hiện đúng giá trị', async () => {
    harness = createHarness(actorWith(['staff:read']), respondWith([staff('s1', 'invited')]));
    await harness.render('/admin/staff?status=invited&role=sales');
    await waitFor(() => staffRequests().length > 0, 'request nhân viên');
    await waitFor(
      () => (findField(harness.container, 'Vai trò')?.options.length ?? 0) > 1,
      'danh sách role',
    );

    expect(queryOf(staffRequests()[0]!)).toEqual({ status: 'invited', role: 'sales' });
    expect(findField(harness.container, 'Trạng thái')!.value).toBe('invited');
    expect(findField(harness.container, 'Vai trò')!.value).toBe('sales');
  });

  it('không gửi limit cố định mà để API dùng giá trị mặc định', async () => {
    harness = createHarness(actorWith(['staff:read']), respondWith([staff('s1', 'active')]));
    await harness.render('/admin/staff');
    await waitFor(() => staffRequests().length > 0, 'request nhân viên');

    expect(queryOf(staffRequests()[0]!)).toEqual({});
  });

  it('dòng invited hiện Đã mời, dòng locked hiện Đã khóa, dòng active hiện Hoạt động', async () => {
    harness = createHarness(
      actorWith(['staff:read']),
      respondWith([staff('s1', 'invited'), staff('s2', 'locked'), staff('s3', 'active')]),
    );
    await harness.render('/admin/staff');
    await waitFor(() => harness.container.querySelectorAll('tbody tr').length === 3, 'ba dòng');

    // Đọc ô theo tiêu đề cột, không theo vị trí, nên thêm hay bớt cột không làm lệch phép kiểm.
    const statusColumn = Array.from(harness.container.querySelectorAll('thead th')).findIndex(
      (header) => header.textContent === 'Trạng thái',
    );
    const statusBadge = (email: string) => rowOf(email).children[statusColumn]?.textContent;
    expect(statusBadge('s1@pickory.local')).toBe('Đã mời');
    expect(statusBadge('s2@pickory.local')).toBe('Đã khóa');
    expect(statusBadge('s3@pickory.local')).toBe('Hoạt động');
  });

  it('người chỉ có staff:read vẫn thấy danh sách role trong bộ lọc', async () => {
    harness = createHarness(actorWith(['staff:read']), respondWith([staff('s1', 'active')]));
    await harness.render('/admin/staff');
    await waitFor(
      () => (findField(harness.container, 'Vai trò')?.options.length ?? 0) > 1,
      'danh sách role',
    );

    const optionValues = Array.from(findField(harness.container, 'Vai trò')!.options).map(
      (option) => option.value,
    );
    expect(optionValues).toContain('sales');
    expect(harness.requestedUrls).toContain('/api/admin/roles');
  });

  it('danh sách chọn role không có customer', async () => {
    harness = createHarness(actorWith(['staff:read']), respondWith([staff('s1', 'active')]));
    await harness.render('/admin/staff');
    await waitFor(
      () => (findField(harness.container, 'Vai trò')?.options.length ?? 0) > 1,
      'danh sách role',
    );

    const optionValues = Array.from(findField(harness.container, 'Vai trò')!.options).map(
      (option) => option.value,
    );
    expect(optionValues).toEqual(['', 'admin', 'sales']);
  });

  it('danh sách rỗng khi có bộ lọc hiện thông báo không có kết quả phù hợp', async () => {
    harness = createHarness(actorWith(['staff:read']), respondWith([]));
    await harness.render('/admin/staff?status=locked');
    await waitFor(
      () => harness.container.textContent!.includes('Không có kết quả phù hợp với bộ lọc.'),
      'thông báo rỗng',
    );

    expect(harness.container.querySelector('[role="status"]')!.textContent).toContain(
      'Không có kết quả phù hợp với bộ lọc.',
    );
  });
});

describe('màn nhân viên có cột STT', () => {
  function pagedResponse(count: number, paging: { page: number; limit: number }) {
    const items = Array.from({ length: count }, (_, index) => staff(`s${index + 1}`, 'active'));
    return (url: string) => {
      if (url.startsWith('/api/admin/staff')) return { items, total: 100, ...paging };
      if (url === '/api/admin/roles') return roles;
      return [];
    };
  }

  function rowNumbers() {
    return [...harness.container.querySelectorAll('tbody tr')].map(
      (row) => row.firstElementChild!.textContent,
    );
  }

  it('cột đầu có tiêu đề STT, tiêu đề và ô đều có class num', async () => {
    harness = createHarness(actorWith(['staff:read']), pagedResponse(2, { page: 1, limit: 20 }));
    await harness.render('/admin/staff');
    await waitFor(() => rowNumbers().length > 0, 'danh sách nhân viên');

    const headerCell = harness.container.querySelector('thead th')!;
    expect(headerCell.textContent).toBe('STT');
    expect(headerCell.getAttribute('scope')).toBe('col');
    expect(headerCell.classList.contains('num')).toBe(true);
    expect(headerCell.classList.contains('col-row-number')).toBe(true);
    const firstCell = harness.container.querySelector('tbody tr')!.firstElementChild!;
    expect(firstCell.tagName).toBe('TD');
    expect(firstCell.classList.contains('num')).toBe(true);
    expect(harness.container.querySelector('tbody tr th[scope="row"]')).not.toBeNull();
  });

  it('response page 2, limit 20 thì STT là 21 và 22', async () => {
    harness = createHarness(actorWith(['staff:read']), pagedResponse(2, { page: 2, limit: 20 }));
    await harness.render('/admin/staff?page=2');
    await waitFor(() => rowNumbers().length > 0, 'danh sách nhân viên');

    expect(rowNumbers()).toEqual(['21', '22']);
  });

  it('response page 3, limit 5 thì STT bắt đầu từ 11, không theo hằng số của màn', async () => {
    harness = createHarness(actorWith(['staff:read']), pagedResponse(2, { page: 3, limit: 5 }));
    await harness.render('/admin/staff');
    await waitFor(() => rowNumbers().length > 0, 'danh sách nhân viên');

    expect(rowNumbers()).toEqual(['11', '12']);
  });

  it.each([
    ['người chỉ xem', ['staff:read'], 4],
    ['người quản lý', ['staff:read', 'staff:manage'], 5],
  ])('ô rỗng của %s trải hết các cột của bảng', async (_name, permissions, columns) => {
    harness = createHarness(actorWith(permissions), respondWith([]));
    await harness.render('/admin/staff');
    await waitFor(() => harness.container.querySelector('td.empty-state') !== null, 'ô rỗng');

    const emptyCell = harness.container.querySelector<HTMLTableCellElement>('td.empty-state')!;
    expect(emptyCell.colSpan).toBe(columns);
    expect(harness.container.querySelectorAll('thead th')).toHaveLength(columns);
  });
});
