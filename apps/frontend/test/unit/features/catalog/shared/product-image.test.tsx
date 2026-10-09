// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ProductImage } from '../../../../../src/features/catalog/shared/product-image';

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

async function render(element: React.ReactElement) {
  await act(async () => root.render(element));
}

async function failToLoad() {
  const image = container.querySelector('img');
  await act(async () => {
    image?.dispatchEvent(new Event('error'));
  });
}

describe('ProductImage', () => {
  it('hiện thẻ ảnh với đủ thuộc tính khi có src', async () => {
    await render(
      <ProductImage
        src="https://img.test/a.png"
        srcSet="https://img.test/a.png 200w"
        sizes="200px"
        alt="Mặt trước"
        loading="lazy"
        width={400}
        height={400}
        className="khung"
      />,
    );
    const image = container.querySelector('img');
    expect(image?.getAttribute('src')).toBe('https://img.test/a.png');
    expect(image?.getAttribute('srcset')).toBe('https://img.test/a.png 200w');
    expect(image?.getAttribute('sizes')).toBe('200px');
    expect(image?.getAttribute('alt')).toBe('Mặt trước');
    expect(image?.getAttribute('loading')).toBe('lazy');
    expect(image?.getAttribute('width')).toBe('400');
    expect(image?.className).toBe('khung');
    expect(container.querySelector('.product-image-placeholder')).toBeNull();
  });

  it('thay ảnh bằng khung giữ chỗ có aria-label khi ảnh lỗi', async () => {
    await render(
      <ProductImage src="https://img.test/hong.png" alt="Laptop Kaze" className="khung" />,
    );

    await failToLoad();

    expect(container.querySelector('img')).toBeNull();
    const placeholder = container.querySelector('.product-image-placeholder');
    expect(placeholder?.getAttribute('role')).toBe('img');
    expect(placeholder?.getAttribute('aria-label')).toBe('Laptop Kaze');
    expect(placeholder?.classList.contains('khung')).toBe(true);
    expect(placeholder?.querySelector('svg')).not.toBeNull();
  });

  it('hiện khung giữ chỗ ngay khi sản phẩm chưa có ảnh', async () => {
    await render(<ProductImage src={null} alt="Laptop Kaze" />);

    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('.product-image-placeholder')?.getAttribute('aria-label')).toBe(
      'Laptop Kaze',
    );
  });

  it('ẩn khung giữ chỗ của ảnh trang trí khỏi trình đọc màn hình', async () => {
    await render(<ProductImage src="https://img.test/hong.png" alt="" />);

    await failToLoad();

    const placeholder = container.querySelector('.product-image-placeholder');
    expect(placeholder?.getAttribute('aria-hidden')).toBe('true');
    expect(placeholder?.getAttribute('role')).toBeNull();
    expect(placeholder?.getAttribute('aria-label')).toBeNull();
  });

  it('thử tải lại khi src đổi sau một lần lỗi', async () => {
    await render(<ProductImage src="https://img.test/hong.png" alt="Laptop" />);
    await failToLoad();
    expect(container.querySelector('img')).toBeNull();

    await render(<ProductImage src="https://img.test/moi.png" alt="Laptop" />);

    expect(container.querySelector('img')?.getAttribute('src')).toBe('https://img.test/moi.png');
    expect(container.querySelector('.product-image-placeholder')).toBeNull();
  });

  it('giữ khung giữ chỗ khi src không đổi sau lỗi', async () => {
    await render(<ProductImage src="https://img.test/hong.png" alt="Laptop" />);
    await failToLoad();

    await render(<ProductImage src="https://img.test/hong.png" alt="Laptop" className="khac" />);

    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('.product-image-placeholder')).not.toBeNull();
  });
});
