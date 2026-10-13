// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FilteredEmptyState } from '../../../src/shared/filtered-empty-state';

let container: HTMLDivElement;
let root: Root;

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

function renderState(page: number, onFirstPage = vi.fn()) {
  return act(async () => root.render(<FilteredEmptyState page={page} onFirstPage={onFirstPage} />));
}

describe('FilteredEmptyState', () => {
  it('hiện thông báo trong vùng role status', async () => {
    await renderState(1);

    const notice = container.querySelector('[role="status"]')!;

    expect(notice.textContent).toBe('Không có kết quả phù hợp với bộ lọc.');
  });

  it('không hiện nút Về trang 1 ở trang 1', async () => {
    await renderState(1);

    expect(container.querySelector('button')).toBeNull();
  });

  it('hiện nút Về trang 1 khi đang ở trang sau và gọi onFirstPage khi bấm', async () => {
    const onFirstPage = vi.fn();
    await renderState(3, onFirstPage);

    const button = container.querySelector('button')!;
    expect(button.textContent).toBe('Về trang 1');

    await act(async () => button.click());

    expect(onFirstPage).toHaveBeenCalledTimes(1);
  });
});
