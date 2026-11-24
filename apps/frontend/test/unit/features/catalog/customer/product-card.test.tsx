// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ProductListItem } from '../../../../../src/features/catalog/customer/catalog-api';
import {
  COMPARE_LIST_LIMIT,
  COMPARE_STORAGE_KEY,
} from '../../../../../src/features/catalog/customer/compare-list';
import { ProductCard } from '../../../../../src/features/catalog/customer/product-card';
import { compareListStore } from '../../../../../src/features/catalog/customer/use-compare-list';

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
  window.localStorage.clear();
  compareListStore.reload();
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  window.localStorage.clear();
  compareListStore.reload();
  vi.unstubAllGlobals();
});

async function renderCard(item: ProductListItem, categoryId?: string) {
  await act(async () =>
    root.render(
      <MemoryRouter>
        <ProductCard product={item} categoryId={categoryId} />
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

  it('ảnh không tải được thì hiện khung giữ chỗ có aria-label là alt', async () => {
    await renderCard(product);

    await act(async () => {
      container.querySelector('img')?.dispatchEvent(new Event('error'));
    });

    expect(container.querySelector('img')).toBeNull();
    const placeholder = container.querySelector('.product-image-placeholder');
    expect(placeholder?.getAttribute('role')).toBe('img');
    expect(placeholder?.getAttribute('aria-label')).toBe('Mặt trước');
  });

  it('không có ảnh thì hiện khung giữ chỗ thay vì thẻ img', async () => {
    await renderCard({ ...product, image: null });

    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('.product-image-placeholder')).not.toBeNull();
  });

  it('hiện hãng, tên, giá, chip Còn hàng và không có giá gạch hay sao đánh giá', async () => {
    await renderCard(product);

    expect(container.textContent).toContain('ASUS');
    expect(container.textContent).toContain('Laptop 1');
    expect(container.textContent).toContain('21.490.000 đ');
    expect(container.textContent).toContain('Còn hàng');
    expect(container.querySelector('del, s')).toBeNull();
    expect(container.textContent).not.toMatch(/[★⭐]/);
  });

  it('giá null hiện "Liên hệ" và hết hàng hiện chip "Hết hàng"', async () => {
    await renderCard({ ...product, minPrice: null, inStock: false });

    expect(container.textContent).toContain('Liên hệ');
    expect(container.textContent).toContain('Hết hàng');
    expect(container.textContent).not.toContain('Còn hàng');
  });

  it('ngừng kinh doanh hiện chip "Ngừng kinh doanh"', async () => {
    await renderCard({ ...product, status: 'discontinued' });

    expect(container.textContent).toContain('Ngừng kinh doanh');
    expect(container.textContent).not.toContain('Còn hàng');
  });

  it('không có categoryId thì không vẽ nút so sánh nhưng vẫn giữ chip trạng thái', async () => {
    await renderCard(product);

    expect(container.querySelector('button[aria-label*="so sánh"]')).toBeNull();
    expect(container.textContent).toContain('Còn hàng');
  });

  it('có categoryId thì vẽ nút so sánh ngoài thẻ link, lưu đúng danh mục và khóa kèm lý do khi đủ giới hạn', async () => {
    await renderCard(product, 'cat-laptop');

    const link = container.querySelector('a')!;
    const compareButton = container.querySelector<HTMLButtonElement>(
      'button[aria-label*="so sánh"]',
    )!;
    expect(compareButton).not.toBeNull();
    expect(link.contains(compareButton)).toBe(false);
    expect(compareButton.getAttribute('aria-pressed')).toBe('false');

    await act(async () => compareButton.click());
    expect(compareButton.getAttribute('aria-pressed')).toBe('true');
    expect(JSON.parse(window.localStorage.getItem(COMPARE_STORAGE_KEY) ?? 'null')).toEqual({
      categoryId: 'cat-laptop',
      productIds: ['p1'],
    });

    await act(async () => compareButton.click());
    expect(compareButton.getAttribute('aria-pressed')).toBe('false');

    // Khi danh sách đã đủ COMPARE_LIST_LIMIT thì nút bị khóa và aria-label nêu rõ lý do.
    const fullIds = Array.from({ length: COMPARE_LIST_LIMIT }, (_, index) => `other-${index + 1}`);
    await act(async () => {
      window.localStorage.setItem(
        COMPARE_STORAGE_KEY,
        JSON.stringify({ categoryId: 'cat-laptop', productIds: fullIds }),
      );
      compareListStore.reload();
    });
    await renderCard(product, 'cat-laptop');

    const disabledCompareButton = container.querySelector<HTMLButtonElement>(
      'button[aria-label*="so sánh"]',
    )!;
    expect(disabledCompareButton.disabled).toBe(true);
    expect(disabledCompareButton.getAttribute('aria-label')).toContain(
      `đã đủ ${COMPARE_LIST_LIMIT} sản phẩm`,
    );
  });

  it('khác danh mục với danh sách hiện có thì khóa nút so sánh và nêu lý do trong aria-label', async () => {
    await act(async () => {
      window.localStorage.setItem(
        COMPARE_STORAGE_KEY,
        JSON.stringify({ categoryId: 'cat-phone', productIds: ['p0'] }),
      );
      compareListStore.reload();
    });

    await renderCard(product, 'cat-laptop');

    const compareButton = container.querySelector<HTMLButtonElement>(
      'button[aria-label*="so sánh"]',
    )!;
    expect(compareButton.disabled).toBe(true);
    expect(compareButton.getAttribute('aria-label')).toContain(
      'chỉ so sánh được sản phẩm cùng danh mục',
    );
  });
});
