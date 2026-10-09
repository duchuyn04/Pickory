// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { VariantTable } from '../../../../../src/features/catalog/admin/variant-table';
import type { ProductVariant } from '../../../../../src/features/catalog/admin/product-logic';

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

const variants: ProductVariant[] = [
  {
    id: 'sku-do',
    code: 'AIR-DO',
    optionValues: { Màu: 'Đỏ' },
    price: 100,
    stock: 3,
    enabled: true,
  },
  // Tổ hợp mới thêm chưa lưu nên chưa có `id` từ máy chủ.
  { optionValues: { Màu: 'Xanh' }, price: 100, stock: 0, enabled: true },
];

async function renderTable(productId: string | undefined) {
  await act(async () =>
    root.render(
      <MemoryRouter>
        <VariantTable
          productId={productId}
          variants={variants}
          disabled={false}
          priceErrors={{}}
          onChange={() => {}}
          onPriceChange={() => {}}
        />
      </MemoryRouter>,
    ),
  );
}

describe('VariantTable liên kết tồn kho', () => {
  it('SKU đã lưu có liên kết Tồn kho tới đúng trang SKU', async () => {
    await renderTable('prod-1');
    const links = [...container.querySelectorAll('a')];
    expect(links).toHaveLength(1);
    expect(links[0]!.textContent).toBe('Tồn kho');
    expect(links[0]!.getAttribute('aria-label')).toBe('Tồn kho Đỏ');
    expect(links[0]!.getAttribute('href')).toBe('/admin/products/prod-1/skus/sku-do/inventory');
  });

  it('sản phẩm chưa lưu thì không có liên kết nào', async () => {
    await renderTable(undefined);
    expect(container.querySelectorAll('a')).toHaveLength(0);
  });
});
