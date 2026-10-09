// @vitest-environment jsdom
import { act, type ReactNode } from 'react';
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

async function renderAlert(props: { message?: ReactNode; items?: string[]; className?: string }) {
  await act(async () => root.render(<ErrorAlert {...props} />));
  return container.querySelector<HTMLElement>('[role="alert"]')!;
}

describe('ErrorAlert', () => {
  it('có role="alert", class mẫu chung và icon ẩn với trình đọc màn hình', async () => {
    const alert = await renderAlert({ message: 'Lỗi chung.', items: ['Lý do một.'] });
    expect(alert.classList.contains('alert')).toBe(true);
    expect(alert.classList.contains('alert-list')).toBe(true);
    const icon = alert.querySelector('svg');
    expect(icon?.getAttribute('aria-hidden')).toBe('true');
    expect(icon?.classList.contains('alert-list-icon')).toBe(true);
  });

  it('tiêu đề nằm riêng, mỗi lý do là một mục của danh sách', async () => {
    const alert = await renderAlert({ message: 'Lỗi chung.', items: ['Lý do một.', 'Lý do hai.'] });
    expect(alert.querySelector('.alert-list-title')?.textContent).toBe('Lỗi chung.');
    const list = alert.querySelector('ul.alert-list-items')!;
    expect([...list.querySelectorAll('li')].map((item) => item.textContent)).toEqual([
      'Lý do một.',
      'Lý do hai.',
    ]);
  });

  it('không có lý do thì không vẽ danh sách rỗng, chữ chỉ gồm tiêu đề', async () => {
    const alert = await renderAlert({ message: 'Chỉ có tiêu đề.' });
    expect(alert.querySelector('ul')).toBeNull();
    expect(alert.textContent).toBe('Chỉ có tiêu đề.');
  });

  it('lỗi một dòng không in đậm', async () => {
    const alert = await renderAlert({ message: 'Lỗi một dòng.' });
    expect(alert.querySelector('.alert-list-title')?.classList.contains('alert-list-heading')).toBe(
      false,
    );
  });

  it('có danh sách lý do thì dòng tóm tắt in đậm', async () => {
    const alert = await renderAlert({ message: 'Lỗi chung.', items: ['Lý do.'] });
    expect(alert.querySelector('.alert-list-title')?.classList.contains('alert-list-heading')).toBe(
      true,
    );
  });

  it('giữ class bố cục riêng bên cạnh class của mẫu chung', async () => {
    const alert = await renderAlert({ message: 'Lỗi.', className: 'identity-table-alert' });
    expect(alert.classList.contains('identity-table-alert')).toBe(true);
    expect(alert.classList.contains('alert-list')).toBe(true);
  });

  it('nhận phần tử con làm nội dung ghép', async () => {
    const alert = await renderAlert({
      message: (
        <>
          Lỗi tải cài đặt: <em>máy chủ tắt</em>
        </>
      ),
    });
    expect(alert.querySelector('.alert-list-title em')?.textContent).toBe('máy chủ tắt');
    expect(alert.textContent).toBe('Lỗi tải cài đặt: máy chủ tắt');
  });

  it('không có tiêu đề thì không vẽ dòng tiêu đề trống', async () => {
    const alert = await renderAlert({ items: ['Chỉ có lý do.'] });
    expect(alert.querySelector('.alert-list-title')).toBeNull();
    expect(alert.textContent).toBe('Chỉ có lý do.');
  });
});
