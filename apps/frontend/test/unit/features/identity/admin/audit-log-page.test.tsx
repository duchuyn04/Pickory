// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { actorWith, createHarness, waitFor, type AdminListHarness } from './admin-list-test-kit';

let harness: AdminListHarness;

afterEach(async () => {
  await harness.cleanup();
});

function auditLog(id: string) {
  return {
    id,
    actorId: 'actor-1',
    actorEmail: 'admin@pickory.local',
    actorName: 'Quản trị',
    action: 'role.create',
    targetType: 'role',
    targetId: `target-${id}`,
    before: null,
    after: { name: id },
    createdAt: '2026-10-01T03:00:00Z',
  };
}

function open(url: string, items: unknown[], paging: { page: number; limit: number }) {
  harness = createHarness(actorWith(['audit-logs:read']), () => ({
    items,
    total: 100,
    ...paging,
  }));
  return harness.render(url);
}

function rowNumbers() {
  return [...harness.container.querySelectorAll('tbody tr')].map(
    (row) => row.firstElementChild!.textContent,
  );
}

describe('màn nhật ký thao tác có cột STT', () => {
  it('cột đầu có tiêu đề STT, tiêu đề và ô đều có class num', async () => {
    await open('/admin/audit-logs', [auditLog('a'), auditLog('b')], { page: 1, limit: 50 });
    await waitFor(() => rowNumbers().length > 0, 'danh sách nhật ký');

    const headerCell = harness.container.querySelector('thead th')!;
    expect(headerCell.textContent).toBe('STT');
    expect(headerCell.getAttribute('scope')).toBe('col');
    expect(headerCell.classList.contains('num')).toBe(true);
    expect(headerCell.classList.contains('col-row-number')).toBe(true);
    const firstCell = harness.container.querySelector('tbody tr')!.firstElementChild!;
    expect(firstCell.tagName).toBe('TD');
    expect(firstCell.classList.contains('num')).toBe(true);
  });

  it('response page 2, limit 20 thì STT là 21 và 22', async () => {
    await open('/admin/audit-logs', [auditLog('a'), auditLog('b')], { page: 2, limit: 20 });
    await waitFor(() => rowNumbers().length > 0, 'danh sách nhật ký');

    expect(rowNumbers()).toEqual(['21', '22']);
  });

  it('response page 3, limit 5 thì STT bắt đầu từ 11, không theo limit 50 của màn', async () => {
    await open('/admin/audit-logs', [auditLog('a'), auditLog('b')], { page: 3, limit: 5 });
    await waitFor(() => rowNumbers().length > 0, 'danh sách nhật ký');

    expect(rowNumbers()).toEqual(['11', '12']);
  });

  it('trang đầu của limit 50 đánh STT liền nhau từ 1', async () => {
    const items = Array.from({ length: 3 }, (_, index) => auditLog(`log-${index}`));
    await open('/admin/audit-logs', items, { page: 1, limit: 50 });
    await waitFor(() => rowNumbers().length > 0, 'danh sách nhật ký');

    expect(rowNumbers()).toEqual(['1', '2', '3']);
  });

  it('ô rỗng khi chưa có dòng nhật ký trải hết sáu cột', async () => {
    await open('/admin/audit-logs', [], { page: 1, limit: 50 });
    await waitFor(() => harness.container.querySelector('td.empty-state') !== null, 'ô rỗng');

    const emptyCell = harness.container.querySelector<HTMLTableCellElement>('td.empty-state')!;
    expect(emptyCell.colSpan).toBe(6);
    expect(harness.container.querySelectorAll('thead th')).toHaveLength(6);
  });
});
