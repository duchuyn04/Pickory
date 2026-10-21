// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { HighlightMatch } from '../../../src/shared/highlight-match';

let container: HTMLDivElement;
let root: Root;

async function renderMatch(text: string, keyword: string) {
  await act(async () => root.render(<HighlightMatch text={text} keyword={keyword} />));
}

function markTexts() {
  return [...container.querySelectorAll('mark')].map((mark) => mark.textContent);
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

describe('HighlightMatch', () => {
  it('bọc đoạn khớp trong mark và giữ nguyên chữ gốc có dấu', async () => {
    await renderMatch('Điện thoại Samsung', 'dien thoai');

    expect(markTexts()).toEqual(['Điện thoại']);
    expect(container.textContent).toBe('Điện thoại Samsung');
  });

  it('tô mọi lần xuất hiện', async () => {
    await renderMatch('Áo thun, áo khoác', 'ao');

    expect(markTexts()).toEqual(['Áo', 'áo']);
    expect(container.textContent).toBe('Áo thun, áo khoác');
  });

  it('chữ ở giữa chuỗi: phần trước và phần sau nằm ngoài mark', async () => {
    await renderMatch('Samsung Galaxy S26', 'galaxy');

    expect(markTexts()).toEqual(['Galaxy']);
    expect(container.textContent).toBe('Samsung Galaxy S26');
  });

  it('từ khóa rỗng thì không có mark và chữ giữ nguyên', async () => {
    await renderMatch('Điện thoại Samsung', '');

    expect(container.querySelector('mark')).toBeNull();
    expect(container.textContent).toBe('Điện thoại Samsung');
  });

  it('từ khóa toàn khoảng trắng thì không có mark', async () => {
    await renderMatch('Điện thoại Samsung', '   ');

    expect(container.querySelector('mark')).toBeNull();
  });

  it('không khớp thì không có mark', async () => {
    await renderMatch('Điện thoại Samsung', 'laptop');

    expect(container.querySelector('mark')).toBeNull();
    expect(container.textContent).toBe('Điện thoại Samsung');
  });
});
