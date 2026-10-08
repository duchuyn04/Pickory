// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ErrorAlert } from '../../../src/shared/error-alert';

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

async function renderAlert(props: { title?: string; items?: string[] }) {
  await act(async () => root.render(<ErrorAlert {...props} />));
  return container.querySelector<HTMLElement>('[role="alert"]')!;
}

describe('ErrorAlert', () => {
  it('có role="alert", class mẫu chung và icon ẩn với trình đọc màn hình', async () => {
    const alert = await renderAlert({ title: 'Lỗi chung.', items: ['Lý do một.'] });
    expect(alert.classList.contains('alert')).toBe(true);
    expect(alert.classList.contains('alert-list')).toBe(true);
    const icon = alert.querySelector('svg');
    expect(icon?.getAttribute('aria-hidden')).toBe('true');
    expect(icon?.classList.contains('alert-list-icon')).toBe(true);
  });

  it('tiêu đề nằm riêng, mỗi lý do là một mục của danh sách', async () => {
    const alert = await renderAlert({ title: 'Lỗi chung.', items: ['Lý do một.', 'Lý do hai.'] });
    expect(alert.querySelector('.alert-list-title')?.textContent).toBe('Lỗi chung.');
    const list = alert.querySelector('ul.alert-list-items')!;
    expect([...list.querySelectorAll('li')].map((item) => item.textContent)).toEqual([
      'Lý do một.',
      'Lý do hai.',
    ]);
  });

  it('không có lý do thì không vẽ danh sách rỗng, chữ chỉ gồm tiêu đề', async () => {
    const alert = await renderAlert({ title: 'Chỉ có tiêu đề.' });
    expect(alert.querySelector('ul')).toBeNull();
    expect(alert.textContent).toBe('Chỉ có tiêu đề.');
  });

  it('không có tiêu đề thì không vẽ dòng tiêu đề trống', async () => {
    const alert = await renderAlert({ items: ['Chỉ có lý do.'] });
    expect(alert.querySelector('.alert-list-title')).toBeNull();
    expect(alert.textContent).toBe('Chỉ có lý do.');
  });
});
