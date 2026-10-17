// @vitest-environment jsdom
import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { renderToStaticMarkup } from 'react-dom/server';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Icon } from '../../../src/shared/icons';
import { ResultPanel, type ResultTone } from '../../../src/shared/result-panel';

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

async function renderPanel(element: ReactNode) {
  await act(async () => root.render(element));
}

describe('ResultPanel', () => {
  it.each<[ResultTone, string, 'check' | 'info' | 'alertCircle']>([
    ['success', 'result-panel-icon-success', 'check'],
    ['info', 'result-panel-icon-info', 'info'],
    ['error', 'result-panel-icon-error', 'alertCircle'],
  ])('dùng đúng màu và icon mặc định cho loại %s', async (tone, toneClass, iconName) => {
    await renderPanel(<ResultPanel tone={tone} title="Tiêu đề" />);

    const iconCircle = container.querySelector('.result-panel-icon');
    expect(iconCircle?.classList.contains(toneClass)).toBe(true);
    expect(iconCircle?.innerHTML).toBe(renderToStaticMarkup(<Icon name={iconName} />));
  });

  it('cho đổi icon bằng thuộc tính icon', async () => {
    await renderPanel(<ResultPanel tone="success" icon="mail" title="Đã gửi" />);

    expect(container.querySelector('.result-panel-icon')?.innerHTML).toBe(
      renderToStaticMarkup(<Icon name="mail" />),
    );
  });

  it('ẩn vòng tròn icon với trình đọc màn hình', async () => {
    await renderPanel(<ResultPanel tone="success" title="Xong" />);

    expect(container.querySelector('.result-panel-icon')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('dùng tiêu đề cấp 2 mặc định và cấp 1 khi được yêu cầu', async () => {
    await renderPanel(<ResultPanel tone="info" title="Tiêu đề phụ" />);
    expect(container.querySelector('h2')?.textContent).toBe('Tiêu đề phụ');
    expect(container.querySelector('h1')).toBeNull();

    await renderPanel(<ResultPanel tone="info" title="Tiêu đề trang" headingLevel={1} />);
    expect(container.querySelector('h1')?.textContent).toBe('Tiêu đề trang');
    expect(container.querySelector('h2')).toBeNull();
  });

  it('hiện mô tả không có role khi không bật vùng thông báo', async () => {
    await renderPanel(<ResultPanel tone="info" title="Trống" description="Chưa có gì." />);

    const description = container.querySelector('.result-panel-description');
    expect(description?.textContent).toBe('Chưa có gì.');
    expect(description?.getAttribute('role')).toBeNull();
  });

  it('mô tả của loại thành công và thông tin là vùng role="status"', async () => {
    await renderPanel(
      <ResultPanel tone="success" title="Xong" description="Đã lưu." announceDescription />,
    );

    expect(container.querySelector('[role="status"]')?.textContent).toBe('Đã lưu.');
  });

  it('mô tả của loại lỗi là vùng role="alert"', async () => {
    await renderPanel(
      <ResultPanel tone="error" title="Lỗi" description="Liên kết hết hạn." announceDescription />,
    );

    expect(container.querySelector('[role="alert"]')?.textContent).toBe('Liên kết hết hạn.');
    expect(container.querySelector('[role="status"]')).toBeNull();
  });

  it('đặt khu nút dưới mô tả và không tạo khu nút khi không có nút', async () => {
    await renderPanel(<ResultPanel tone="success" title="Xong" />);
    expect(container.querySelector('.result-panel-actions')).toBeNull();

    await renderPanel(
      <ResultPanel tone="success" title="Xong" actions={<button type="button">Tiếp tục</button>} />,
    );
    expect(container.querySelector('.result-panel-actions button')?.textContent).toBe('Tiếp tục');
  });

  it('bọc trong thẻ khi framed', async () => {
    await renderPanel(<ResultPanel tone="info" title="Trống" framed />);

    expect(container.querySelector('.card.result-panel')).not.toBeNull();
  });
});
