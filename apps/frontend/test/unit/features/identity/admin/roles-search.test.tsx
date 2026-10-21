// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import {
  actorWith,
  createHarness,
  findField,
  waitFor,
  type AdminListHarness,
} from './admin-list-test-kit';

let harness: AdminListHarness;

afterEach(async () => {
  await harness.cleanup();
});

function role(id: string, code: string, name: string) {
  return { id, code, name, isSystem: false, permissions: [], userCount: 0 };
}

const roles = [
  role('1', 'admin', 'Quản trị viên'),
  role('2', 'sales', 'Nhân viên bán hàng'),
  role('3', 'support', 'Chăm sóc khách hàng'),
];

function open(url: string, data = roles) {
  harness = createHarness(actorWith(['roles:manage']), (requested) =>
    requested === '/api/admin/roles' ? data : [],
  );
  return harness.render(url);
}

function roleCodes() {
  return [...harness.container.querySelectorAll('tbody tr th .mono')].map(
    (element) => element.textContent,
  );
}

const searchLabel = 'Tìm theo tên hoặc mã role';

describe('màn role có ô tìm kiếm', () => {
  it('mở link có từ khóa theo mã thì chỉ hiện role khớp và ô hiện đúng từ khóa', async () => {
    await open('/admin/roles?search=sales');
    await waitFor(() => roleCodes().length > 0, 'danh sách role');

    expect(roleCodes()).toEqual(['sales']);
    expect(findField(harness.container, searchLabel)!.value).toBe('sales');
  });

  it('khớp theo tên hiển thị không dấu', async () => {
    await open('/admin/roles?search=cham%20soc');
    await waitFor(() => roleCodes().length > 0, 'danh sách role');

    expect(roleCodes()).toEqual(['support']);
  });

  it('không có từ khóa thì hiện mọi role', async () => {
    await open('/admin/roles');
    await waitFor(() => roleCodes().length > 0, 'danh sách role');

    expect(roleCodes()).toEqual(['admin', 'sales', 'support']);
  });

  it('từ khóa không khớp hiện thông báo không có kết quả phù hợp', async () => {
    await open('/admin/roles?search=kho');
    await waitFor(
      () => harness.container.textContent!.includes('Không có kết quả phù hợp với bộ lọc.'),
      'thông báo rỗng',
    );

    expect(harness.container.querySelector('[role="status"]')!.textContent).toBe(
      'Không có kết quả phù hợp với bộ lọc.',
    );
  });

  it('chưa có role nào và không có từ khóa thì giữ thông báo cũ', async () => {
    await open('/admin/roles', []);
    await waitFor(
      () => harness.container.textContent!.includes('Chưa có vai trò nào trong hệ thống.'),
      'thông báo rỗng cũ',
    );

    expect(harness.container.textContent).not.toContain('Không có kết quả phù hợp với bộ lọc.');
  });
});

describe('màn role có cột STT', () => {
  function rowNumbers() {
    return [...harness.container.querySelectorAll('tbody tr')].map(
      (row) => row.firstElementChild!.textContent,
    );
  }

  it('cột đầu có tiêu đề STT, tiêu đề và ô đều có class num', async () => {
    await open('/admin/roles');
    await waitFor(() => rowNumbers().length > 0, 'danh sách role');

    const headerCell = harness.container.querySelector('thead th')!;
    expect(headerCell.textContent).toBe('STT');
    expect(headerCell.getAttribute('scope')).toBe('col');
    expect(headerCell.classList.contains('num')).toBe(true);
    expect(headerCell.classList.contains('col-row-number')).toBe(true);
    const firstCell = harness.container.querySelector('tbody tr')!.firstElementChild!;
    expect(firstCell.tagName).toBe('TD');
    expect(firstCell.classList.contains('num')).toBe(true);
    // Ô role vẫn là tiêu đề dòng.
    expect(harness.container.querySelector('tbody tr th[scope="row"]')).not.toBeNull();
  });

  it('ba dòng có STT 1, 2, 3', async () => {
    await open('/admin/roles');
    await waitFor(() => rowNumbers().length > 0, 'danh sách role');

    expect(rowNumbers()).toEqual(['1', '2', '3']);
  });

  it('từ khóa chỉ giữ dòng thứ ba ban đầu thì dòng còn lại có STT 1', async () => {
    await open('/admin/roles?search=support');
    await waitFor(() => rowNumbers().length > 0, 'danh sách role');

    expect(rowNumbers()).toEqual(['1']);
    expect(roleCodes()).toEqual(['support']);
  });

  it('chưa có role nào thì ô rỗng trải hết sáu cột', async () => {
    await open('/admin/roles', []);
    await waitFor(
      () => harness.container.textContent!.includes('Chưa có vai trò nào trong hệ thống.'),
      'thông báo rỗng cũ',
    );

    const emptyCell = harness.container.querySelector<HTMLTableCellElement>('td.empty-state')!;
    expect(emptyCell.colSpan).toBe(6);
  });
});
