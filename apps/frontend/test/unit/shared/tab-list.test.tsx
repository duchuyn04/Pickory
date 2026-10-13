// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TabList } from '../../../src/shared/tab-list';

let container: HTMLDivElement;
let root: Root;

const tabs = [
  { code: 'sepay', label: 'SePay' },
  { code: 'cod', label: 'COD' },
  { code: 'other', label: 'Khác' },
];

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

function renderTabs(selected: string, onSelect = vi.fn()) {
  return act(async () =>
    root.render(
      <TabList
        label="Nguồn thanh toán"
        idPrefix="pay"
        panelId="pay-panel"
        tabs={tabs}
        selected={selected}
        onSelect={onSelect}
      />,
    ),
  );
}

function tab(label: string) {
  return [...container.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find(
    (element) => element.textContent === label,
  )!;
}

function press(element: HTMLElement, key: string) {
  return act(async () => {
    element.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
  });
}

describe('TabList', () => {
  it('có vai trò tablist với tên truy cập và một tab cho mỗi mục', async () => {
    await renderTabs('sepay');

    const list = container.querySelector('[role="tablist"]')!;
    expect(list.getAttribute('aria-label')).toBe('Nguồn thanh toán');
    expect(container.querySelectorAll('[role="tab"]')).toHaveLength(3);
  });

  it('đánh dấu tab đang chọn bằng aria-selected và chỉ tab đó nằm trong thứ tự Tab', async () => {
    await renderTabs('cod');

    expect(tab('COD').getAttribute('aria-selected')).toBe('true');
    expect(tab('COD').tabIndex).toBe(0);
    expect(tab('SePay').getAttribute('aria-selected')).toBe('false');
    expect(tab('SePay').tabIndex).toBe(-1);
  });

  it('nối tab với vùng nội dung bằng aria-controls và id theo tiền tố', async () => {
    await renderTabs('sepay');

    expect(tab('SePay').id).toBe('pay-tab-sepay');
    expect(tab('SePay').getAttribute('aria-controls')).toBe('pay-panel');
  });

  it('bấm tab gọi onSelect với mã tab', async () => {
    const onSelect = vi.fn();
    await renderTabs('sepay', onSelect);

    await act(async () => tab('COD').click());

    expect(onSelect).toHaveBeenCalledWith('cod');
  });

  it('mũi tên phải và trái chuyển focus, quay vòng ở hai đầu, không chọn tab', async () => {
    const onSelect = vi.fn();
    await renderTabs('sepay', onSelect);
    tab('SePay').focus();

    await press(tab('SePay'), 'ArrowRight');
    expect(document.activeElement).toBe(tab('COD'));

    await press(tab('COD'), 'ArrowLeft');
    expect(document.activeElement).toBe(tab('SePay'));

    await press(tab('SePay'), 'ArrowLeft');
    expect(document.activeElement).toBe(tab('Khác'));

    await press(tab('Khác'), 'ArrowRight');
    expect(document.activeElement).toBe(tab('SePay'));
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('Home và End đưa focus về tab đầu và tab cuối', async () => {
    await renderTabs('cod');
    tab('COD').focus();

    await press(tab('COD'), 'End');
    expect(document.activeElement).toBe(tab('Khác'));

    await press(tab('Khác'), 'Home');
    expect(document.activeElement).toBe(tab('SePay'));
  });
});
