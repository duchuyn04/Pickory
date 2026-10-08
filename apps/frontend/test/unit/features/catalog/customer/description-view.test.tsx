// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DescriptionView } from '../../../../../src/features/catalog/customer/description-view';

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

async function renderDescription(description: unknown) {
  await act(async () => root.render(<DescriptionView description={description} />));
}

const text = (value: string, marks?: { type: string }[]) => ({
  type: 'text',
  text: value,
  ...(marks ? { marks } : {}),
});

describe('DescriptionView', () => {
  it('đổi tiêu đề cấp 1, 2, 3 thành h2, h3, h4', async () => {
    await renderDescription({
      type: 'doc',
      content: [
        { type: 'heading', attrs: { level: 1 }, content: [text('Cấp một')] },
        { type: 'heading', attrs: { level: 2 }, content: [text('Cấp hai')] },
        { type: 'heading', attrs: { level: 3 }, content: [text('Cấp ba')] },
      ],
    });
    expect(container.querySelector('h2.description-heading')?.textContent).toBe('Cấp một');
    expect(container.querySelector('h3')?.textContent).toBe('Cấp hai');
    expect(container.querySelector('h4')?.textContent).toBe('Cấp ba');
    // Trang chỉ có một h1 là tên sản phẩm, nên mô tả không được tạo h1.
    expect(container.querySelector('h1')).toBeNull();
  });

  it('đổi mark bold và italic thành strong và em, kể cả khi text có cả hai mark', async () => {
    await renderDescription({
      type: 'doc',
      content: [
        {
          type: 'paragraph',
          content: [
            text('đậm', [{ type: 'bold' }]),
            text(' nghiêng', [{ type: 'italic' }]),
            text(' cả hai', [{ type: 'bold' }, { type: 'italic' }]),
          ],
        },
      ],
    });
    const strongTexts = [...container.querySelectorAll('strong')].map((node) => node.textContent);
    const emTexts = [...container.querySelectorAll('em')].map((node) => node.textContent);
    expect(strongTexts).toEqual(['đậm', ' cả hai']);
    expect(emTexts).toEqual([' nghiêng', ' cả hai']);
    // Text có hai mark nằm trong cả strong lẫn em.
    expect(container.querySelector('strong em, em strong')?.textContent).toBe(' cả hai');
  });

  it('không hiện nút script và không hiện nội dung con của nút đó', async () => {
    await renderDescription({
      type: 'doc',
      content: [
        { type: 'script', content: [text('alert(1)')] },
        { type: 'paragraph', content: [text('Đoạn còn lại')] },
      ],
    });
    expect(container.querySelector('script')).toBeNull();
    expect(container.textContent).not.toContain('alert(1)');
    // Nút lạ không làm mất phần mô tả hợp lệ còn lại.
    expect(container.querySelector('p')?.textContent).toBe('Đoạn còn lại');
  });

  it('không hiện nút image và không để thuộc tính onerror vào DOM', async () => {
    await renderDescription({
      type: 'doc',
      content: [
        { type: 'image', attrs: { src: 'x', onerror: 'alert(1)' } },
        { type: 'paragraph', content: [text('Có chữ')] },
      ],
    });
    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('[onerror]')).toBeNull();
    expect(container.innerHTML).not.toContain('onerror');
  });

  it('hiện nguyên văn chuỗi có dạng thẻ HTML, không tạo thẻ img', async () => {
    const payload = '<img src=x onerror=alert(1)>';
    await renderDescription({
      type: 'doc',
      content: [{ type: 'paragraph', content: [text(payload)] }],
    });
    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('p')?.textContent).toBe(payload);
  });

  it('giữ text của mark lạ và không tạo thẻ a', async () => {
    await renderDescription({
      type: 'doc',
      content: [
        {
          type: 'paragraph',
          content: [
            text('liên kết', [{ type: 'link', attrs: { href: 'javascript:alert(1)' } } as never]),
          ],
        },
      ],
    });
    expect(container.querySelector('a')).toBeNull();
    expect(container.querySelector('p')?.textContent).toBe('liên kết');
  });

  it('không hiện chữ của nút lạ dạng inline có trường text', async () => {
    await renderDescription({
      type: 'doc',
      content: [
        {
          type: 'paragraph',
          content: [text('Trước '), { type: 'mention', text: 'nội dung ẩn' }, text('sau')],
        },
      ],
    });
    expect(container.textContent).not.toContain('nội dung ẩn');
    expect(container.querySelector('p')?.textContent).toBe('Trước sau');
  });

  it('bỏ nút lạ nằm trong đoạn văn cùng nội dung con của nó', async () => {
    await renderDescription({
      type: 'doc',
      content: [
        {
          type: 'paragraph',
          content: [text('Trước '), { type: 'hardBreak', content: [text('ẩn')] }, text('sau')],
        },
      ],
    });
    expect(container.querySelector('p')?.textContent).toBe('Trước sau');
  });

  it.each([
    ['null', null],
    ['chuỗi', 'không phải tài liệu'],
    ['doc không có content', { type: 'doc' }],
    ['doc rỗng', { type: 'doc', content: [] }],
    ['chỉ có đoạn rỗng', { type: 'doc', content: [{ type: 'paragraph' }] }],
    ['chỉ có nút lạ', { type: 'doc', content: [{ type: 'script', content: [text('x')] }] }],
  ])('ẩn cả mục mô tả khi dữ liệu là %s', async (_label, description) => {
    await renderDescription(description);
    expect(container.innerHTML).toBe('');
  });

  it('bỏ tiêu đề có cấp ngoài 1 đến 3', async () => {
    await renderDescription({
      type: 'doc',
      content: [
        { type: 'heading', attrs: { level: 6 }, content: [text('Cấp sáu')] },
        { type: 'paragraph', content: [text('Nội dung')] },
      ],
    });
    expect(container.querySelector('h6')).toBeNull();
    expect(container.textContent).not.toContain('Cấp sáu');
  });
});
