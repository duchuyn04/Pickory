// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { VariantTable } from '../../../../../src/features/catalog/admin/variant-table';
import {
  toCombinationKey,
  type ProductVariant,
} from '../../../../../src/features/catalog/admin/product-logic';

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

describe('VariantTable ô tiền VND', () => {
  const pricedVariants: ProductVariant[] = [
    {
      id: 'sku-do',
      optionValues: { Màu: 'Đỏ' },
      price: 1250000,
      compareAtPrice: 2000000,
      weightGrams: 300,
      stock: 3,
      enabled: true,
    },
  ];

  async function renderPriced(
    onChange: (next: ProductVariant[]) => void,
    priceErrors: Record<string, string> = {},
  ) {
    await act(async () =>
      root.render(
        <MemoryRouter>
          <VariantTable
            variants={pricedVariants}
            disabled={false}
            priceErrors={priceErrors}
            onChange={onChange}
            onPriceChange={() => {}}
          />
        </MemoryRouter>,
      ),
    );
  }

  function field(name: string): HTMLInputElement {
    return container.querySelector<HTMLInputElement>(`input[name="${name}"]`)!;
  }

  async function enter(input: HTMLInputElement, text: string) {
    await act(async () => {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
      input.focus();
      setter.call(input, text);
      input.dispatchEvent(new Event('input', { bubbles: true }));
    });
  }

  it('giá bán và giá niêm yết hiện dấu chấm nghìn, cân nặng giữ ô số', async () => {
    await renderPriced(() => {});

    expect(field('variant-price-0').value).toBe('1.250.000');
    expect(field('variant-compare-at-price-0').value).toBe('2.000.000');
    expect(field('variant-weight-grams-0').type).toBe('number');
    expect(field('variant-weight-grams-0').value).toBe('300');
  });

  it('gõ giá trả số nguyên đồng cho form cha', async () => {
    const onChange = vi.fn();
    await renderPriced(onChange);

    await enter(field('variant-price-0'), '1.500.000');

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange.mock.calls[0]![0][0].price).toBe(1500000);
  });

  it('xóa giá bán giữ chuỗi rỗng để người dùng nhận lỗi thiếu giá', async () => {
    const onChange = vi.fn();
    await renderPriced(onChange);

    await enter(field('variant-price-0'), '');

    expect(onChange.mock.calls[0]![0][0].price).toBe('');
  });

  it('xóa giá niêm yết trả null vì API phân biệt chưa có giá với giá 0', async () => {
    const onChange = vi.fn();
    await renderPriced(onChange);

    await enter(field('variant-compare-at-price-0'), '');

    expect(onChange.mock.calls[0]![0][0].compareAtPrice).toBeNull();
  });

  it('lỗi giá hiện dưới ô, đứng đầu aria-describedby và đi kèm aria-invalid', async () => {
    await renderPriced(() => {}, {
      [toCombinationKey({ Màu: 'Đỏ' })]: 'Nhập giá bán lớn hơn 0.',
    });

    const price = field('variant-price-0');
    expect(price.getAttribute('aria-invalid')).toBe('true');
    const describedBy = (price.getAttribute('aria-describedby') ?? '').split(' ');
    expect(describedBy[0]).toBe('variant-price-0-error');
    expect(document.getElementById('variant-price-0-error')?.textContent).toBe(
      'Nhập giá bán lớn hơn 0.',
    );
  });
});

describe('VariantTable căn lề ô tiền', () => {
  it('giá bán và giá niêm yết căn phải để số xếp thẳng cột', async () => {
    await act(async () =>
      root.render(
        <MemoryRouter>
          <VariantTable
            variants={[
              {
                optionValues: { Màu: 'Đỏ' },
                price: 100,
                compareAtPrice: 200,
                stock: 0,
                enabled: true,
              },
            ]}
            disabled={false}
            priceErrors={{}}
            onChange={() => {}}
            onPriceChange={() => {}}
          />
        </MemoryRouter>,
      ),
    );

    const frames = [...container.querySelectorAll('.money-input')];
    expect(frames).toHaveLength(2);
    expect(frames.every((frame) => frame.classList.contains('money-input-end'))).toBe(true);
  });
});
