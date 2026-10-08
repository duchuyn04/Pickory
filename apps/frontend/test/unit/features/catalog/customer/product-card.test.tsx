// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ProductListItem } from '../../../../../src/features/catalog/customer/catalog-api';
import { ProductCard } from '../../../../../src/features/catalog/customer/product-card';

let container: HTMLDivElement;
let root: Root;

const product: ProductListItem = {
  id: 'p1',
  slug: 'laptop-1',
  name: 'Laptop 1',
  brand: { id: 'b1', name: 'ASUS', slug: 'asus' },
  minPrice: 21490000,
  inStock: true,
  status: 'active',
  image: {
    publicId: 'pickory/products/a',
    alt: 'Mặt trước',
    url: 'https://res.cloudinary.com/demo/image/upload/pickory/products/a',
  },
};

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

async function renderCard(item: ProductListItem) {
  await act(async () =>
    root.render(
      <MemoryRouter>
        <ProductCard product={item} />
      </MemoryRouter>,
    ),
  );
}

describe('ProductCard', () => {
  it('ảnh có srcset bốn cỡ, sizes, lazy và alt từ API', async () => {
    await renderCard(product);

    const image = container.querySelector('img')!;
    expect(image.getAttribute('alt')).toBe('Mặt trước');
    expect(image.getAttribute('loading')).toBe('lazy');
    expect(image.getAttribute('sizes')).toContain('vw');
    expect(image.getAttribute('srcset')).toContain('f_auto,q_auto,w_1200/pickory/products/a 1200w');
    expect(image.getAttribute('src')).toContain('w_400');
    expect(container.querySelector('a')!.getAttribute('href')).toBe('/p/laptop-1');
  });

  it('không có ảnh thì hiện khung giữ chỗ thay vì thẻ img', async () => {
    await renderCard({ ...product, image: null });

    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('.product-card-placeholder')).not.toBeNull();
  });

  it('giá null hiện "Liên hệ" và hết hàng hiện nhãn', async () => {
    await renderCard({ ...product, minPrice: null, inStock: false });

    expect(container.textContent).toContain('Liên hệ');
    expect(container.textContent).toContain('Hết hàng');
  });
});
