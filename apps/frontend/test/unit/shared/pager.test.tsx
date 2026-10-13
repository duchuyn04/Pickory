// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Pager } from '../../../src/shared/pager';

let container: HTMLDivElement;
let root: Root;

interface PagerArgs {
  page: number;
  total: number;
  limit?: number;
  onPageChange?: (page: number) => void;
}

function renderPager({ page, total, limit = 20, onPageChange = vi.fn() }: PagerArgs) {
  return act(async () =>
    root.render(
      <Pager
        page={page}
        total={total}
        limit={limit}
        onPageChange={onPageChange}
        label="Phân trang nhân viên"
      />,
    ),
  );
}

function getButton(name: string) {
  return [...container.querySelectorAll<HTMLButtonElement>('button')].find(
    (button) => button.textContent === name,
  )!;
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

describe('Pager', () => {
  it('không render gì khi chỉ có một trang', async () => {
    await renderPager({ page: 1, total: 20, limit: 20 });

    expect(container.querySelector('nav')).toBeNull();
  });

  it('không render gì khi danh sách rỗng', async () => {
    await renderPager({ page: 1, total: 0 });

    expect(container.querySelector('nav')).toBeNull();
  });

  it('hiện Trang x / y theo total và limit', async () => {
    await renderPager({ page: 2, total: 45, limit: 20 });

    expect(container.querySelector('nav')!.textContent).toContain('Trang 2 / 3');
  });

  it('tắt nút Trang trước ở trang 1 và bật nút Trang sau', async () => {
    await renderPager({ page: 1, total: 45 });

    expect(getButton('Trang trước').disabled).toBe(true);
    expect(getButton('Trang sau').disabled).toBe(false);
  });

  it('tắt nút Trang sau ở trang cuối và bật nút Trang trước', async () => {
    await renderPager({ page: 3, total: 45 });

    expect(getButton('Trang trước').disabled).toBe(false);
    expect(getButton('Trang sau').disabled).toBe(true);
  });

  it('bấm nút gọi onPageChange với số trang đúng', async () => {
    const onPageChange = vi.fn();
    await renderPager({ page: 2, total: 45, onPageChange });

    await act(async () => getButton('Trang sau').click());
    await act(async () => getButton('Trang trước').click());

    expect(onPageChange).toHaveBeenNthCalledWith(1, 3);
    expect(onPageChange).toHaveBeenNthCalledWith(2, 1);
  });

  it('nav có class pager và tên truy cập lấy từ label', async () => {
    await renderPager({ page: 1, total: 45 });

    const nav = container.querySelector('nav')!;

    expect(nav.className).toBe('pager');
    expect(nav.getAttribute('aria-label')).toBe('Phân trang nhân viên');
  });
});
