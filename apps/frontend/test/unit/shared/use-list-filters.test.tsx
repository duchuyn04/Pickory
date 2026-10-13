// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useListFilters } from '../../../src/shared/use-list-filters';

type ListFilters = ReturnType<typeof useListFilters>;

let container: HTMLDivElement;
let root: Root;
let filters: ListFilters;
let location: ReturnType<typeof useLocation>;
let goBack: () => void;

const filterNames = ['search', 'status'];

// Component thăm dò: lưu kết quả hook và vị trí hiện tại ra biến của test để assert.
function Probe() {
  filters = useListFilters(filterNames);
  location = useLocation();
  const navigate = useNavigate();
  goBack = () => navigate(-1);
  return null;
}

async function renderAt(entries: string[], index = entries.length - 1) {
  await act(async () =>
    root.render(
      <MemoryRouter initialEntries={entries} initialIndex={index}>
        <Probe />
      </MemoryRouter>,
    ),
  );
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

describe('useListFilters', () => {
  it('đọc bộ lọc và số trang từ query string ban đầu', async () => {
    await renderAt(['/admin/staff?search=an&status=active&page=3']);

    expect(filters.values).toEqual({ search: 'an', status: 'active' });
    expect(filters.page).toBe(3);
  });

  it('trả chuỗi rỗng cho bộ lọc thiếu và trang 1 khi không có page', async () => {
    await renderAt(['/admin/staff']);

    expect(filters.values).toEqual({ search: '', status: '' });
    expect(filters.page).toBe(1);
  });

  it('bỏ qua tham số không có trong danh sách khi đọc', async () => {
    await renderAt(['/admin/staff?role=admin']);

    expect(filters.values).toEqual({ search: '', status: '' });
  });

  it('đổi bộ lọc thì ghi query string và xóa page để về trang 1', async () => {
    await renderAt(['/admin/staff?status=active&page=4']);

    await act(async () => filters.setFilter('search', 'an'));

    const params = new URLSearchParams(location.search);
    expect(params.get('search')).toBe('an');
    expect(params.get('status')).toBe('active');
    expect(params.has('page')).toBe(false);
    expect(filters.page).toBe(1);
  });

  it('xóa tham số khi giá trị rỗng hoặc chỉ có khoảng trắng', async () => {
    await renderAt(['/admin/staff?search=an&status=active']);

    await act(async () => filters.setFilter('search', '   '));
    expect(new URLSearchParams(location.search).has('search')).toBe(false);

    await act(async () => filters.setFilter('status', ''));
    expect(new URLSearchParams(location.search).has('status')).toBe(false);
  });

  it('bỏ khoảng trắng hai đầu của giá trị khi ghi', async () => {
    await renderAt(['/admin/staff']);

    await act(async () => filters.setFilter('search', '  an  '));

    expect(new URLSearchParams(location.search).get('search')).toBe('an');
  });

  it.each(['abc', '0', '-2', '1.5'])('đọc page=%s là trang 1', async (raw) => {
    await renderAt([`/admin/staff?page=${raw}`]);

    expect(filters.page).toBe(1);
  });

  it('đổi sang trang 1 xóa tham số page', async () => {
    await renderAt(['/admin/staff?page=3']);

    await act(async () => filters.setPage(1));

    expect(new URLSearchParams(location.search).has('page')).toBe(false);
  });

  it('đổi sang trang khác ghi page', async () => {
    await renderAt(['/admin/staff?search=an']);

    await act(async () => filters.setPage(2));

    const params = new URLSearchParams(location.search);
    expect(params.get('page')).toBe('2');
    expect(params.get('search')).toBe('an');
  });

  it('giữ nguyên tham số ngoài danh sách khi ghi', async () => {
    await renderAt(['/admin/staff?role=admin&page=2']);

    await act(async () => filters.setFilter('search', 'an'));
    expect(new URLSearchParams(location.search).get('role')).toBe('admin');

    await act(async () => filters.setPage(3));
    expect(new URLSearchParams(location.search).get('role')).toBe('admin');
  });

  it('đổi bộ lọc thay mục lịch sử, nút Back về màn trước danh sách', async () => {
    await renderAt(['/dashboard', '/admin/staff']);

    await act(async () => filters.setFilter('search', 'a'));
    await act(async () => filters.setFilter('search', 'an'));
    await act(async () => goBack());

    expect(location.pathname).toBe('/dashboard');
  });

  it('đổi trang thêm mục lịch sử, nút Back về trang trước', async () => {
    await renderAt(['/dashboard', '/admin/staff']);

    await act(async () => filters.setPage(2));
    await act(async () => goBack());

    expect(location.pathname).toBe('/admin/staff');
    expect(new URLSearchParams(location.search).has('page')).toBe(false);
  });
});
