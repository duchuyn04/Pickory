// @vitest-environment jsdom
import React, { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../../../src/shared/api';
import {
  ProductFormView,
  type ProductDetail,
} from '../../../../../src/features/catalog/admin/product-form';

let container: HTMLDivElement;
let root: Root;
const categories = [
  {
    id: 'parent',
    parentId: null,
    name: 'Máy tính',
    attributes: [
      {
        key: 'origin',
        label: 'Xuất xứ',
        group: 'Chung',
        type: 'text' as const,
        required: true,
        filterable: false,
      },
    ],
  },
  {
    id: 'laptop',
    parentId: 'parent',
    name: 'Laptop',
    attributes: [
      {
        key: 'ram',
        label: 'RAM',
        group: 'Cấu hình',
        type: 'number' as const,
        required: true,
        filterable: false,
      },
    ],
  },
  {
    id: 'desktop',
    parentId: 'parent',
    name: 'Máy bàn',
    attributes: [
      {
        key: 'cpu',
        label: 'CPU',
        group: 'Cấu hình',
        type: 'text' as const,
        required: false,
        filterable: false,
      },
    ],
  },
  {
    id: 'headset',
    parentId: null,
    name: 'Tai nghe',
    attributes: [
      {
        key: 'connection',
        label: 'Kết nối',
        group: 'Chung',
        type: 'text' as const,
        required: true,
        filterable: false,
      },
    ],
  },
];
const brands = [{ id: 'brand', name: 'Thương hiệu' }];

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

async function renderForm(canWrite = true, onSave = vi.fn(), product?: ProductDetail) {
  await act(async () =>
    root.render(
      <MemoryRouter>
        <ProductFormView
          categories={categories}
          brands={brands}
          canWrite={canWrite}
          product={product}
          onSave={onSave}
          onChangeStatus={vi.fn(async () => {})}
          onDelete={vi.fn(async () => {})}
        />
      </MemoryRouter>,
    ),
  );
  return onSave;
}
async function change(selector: string, value: string) {
  const input = container.querySelector<HTMLInputElement | HTMLSelectElement>(selector)!;
  await act(async () => {
    // Dùng setter gốc để React nhận thay đổi như sự kiện nhập thật, không bị value tracker nuốt.
    const prototype =
      input instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(prototype, 'value')!.set!.call(input, value);
    input.dispatchEvent(
      new Event(input instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }),
    );
  });
}

// Danh mục lá quyết định chính xác các ô thông số của sản phẩm.
describe('ProductFormView', () => {
  it('đổi danh mục thì thay ô thông số và gộp thuộc tính cha', async () => {
    await renderForm();
    await change('select[name="categoryId"]', 'laptop');
    expect(container.querySelector('[name="spec-origin"]')).not.toBeNull();
    expect(container.querySelector('[name="spec-ram"]')).not.toBeNull();
    await change('select[name="categoryId"]', 'headset');
    expect(container.querySelector('[name="spec-origin"]')).toBeNull();
    expect(container.querySelector('[name="spec-connection"]')).not.toBeNull();
  });

  it('đổi danh mục cùng cha giữ thông số kế thừa và bỏ thông số riêng của lá', async () => {
    await renderForm();
    await change('select[name="categoryId"]', 'laptop');
    await change('input[name="spec-origin"]', 'Việt Nam');
    await change('input[name="spec-ram"]', '16');
    await change('select[name="categoryId"]', 'desktop');
    expect(container.querySelector<HTMLInputElement>('[name="spec-origin"]')?.value).toBe(
      'Việt Nam',
    );
    expect(container.querySelector('[name="spec-ram"]')).toBeNull();
    await change('select[name="categoryId"]', 'laptop');
    expect(container.querySelector<HTMLInputElement>('[name="spec-ram"]')?.value).toBe('');
    await change('select[name="categoryId"]', 'headset');
    await change('select[name="categoryId"]', 'laptop');
    expect(container.querySelector<HTMLInputElement>('[name="spec-origin"]')?.value).toBe('');
  });

  it('hai trục với hai giá trị mỗi trục sinh bốn dòng biến thể', async () => {
    await renderForm();
    await act(async () =>
      container.querySelector<HTMLButtonElement>('button[name="add-option"]')!.click(),
    );
    await change('input[name="option-name-0"]', 'Màu');
    await change('input[name="option-values-0"]', 'Đỏ, Xanh');
    await act(async () =>
      container.querySelector<HTMLButtonElement>('button[name="add-option"]')!.click(),
    );
    await change('input[name="option-name-1"]', 'Cỡ');
    await change('input[name="option-values-1"]', 'S, M');
    expect(container.querySelectorAll('[data-variant-row]')).toHaveLength(4);
  });
  it('gõ từng ký tự của giá trị có khoảng trắng vẫn giữ tên tổ hợp', async () => {
    await renderForm();
    await act(async () =>
      container.querySelector<HTMLButtonElement>('button[name="add-option"]')!.click(),
    );
    await change('input[name="option-name-0"]', 'Màu');
    for (const character of 'Xanh dương') {
      const input = container.querySelector<HTMLInputElement>('input[name="option-values-0"]')!;
      await change('input[name="option-values-0"]', input.value + character);
    }
    expect(container.querySelector<HTMLInputElement>('input[name="option-values-0"]')?.value).toBe(
      'Xanh dương',
    );
    expect(container.querySelector('[data-variant-row] th')?.textContent).toBe('Xanh dương');
  });

  it('trục đang nhập chưa hiện cảnh báo trước khi gửi form', async () => {
    await renderForm();
    await act(async () =>
      container.querySelector<HTMLButtonElement>('button[name="add-option"]')!.click(),
    );
    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(container.querySelectorAll('[data-variant-row]')).toHaveLength(1);
    await change('input[name="option-name-0"]', 'Màu');
    await change('input[name="option-values-0"]', 'Đỏ, ');
    expect(container.querySelector('[role="alert"]')).toBeNull();
    await change('input[name="option-values-0"]', 'Đỏ, Xanh');
    expect(container.querySelectorAll('[data-variant-row]')).toHaveLength(2);
  });

  it('chặn thông số sai trước khi gửi và nối thông báo lỗi với ô nhập', async () => {
    const onSave = await renderForm();
    await change('select[name="categoryId"]', 'laptop');
    await change('select[name="brandId"]', 'brand');
    await change('input[name="name"]', 'Laptop thử nghiệm');
    await act(async () =>
      container.querySelector<HTMLButtonElement>('button[type="submit"]')!.click(),
    );
    const input = container.querySelector<HTMLInputElement>('[name="spec-ram"]')!;
    const errorId = input.getAttribute('aria-describedby');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(errorId).toBeTruthy();
    expect(document.getElementById(errorId!)?.textContent).toBeTruthy();
    expect(onSave).not.toHaveBeenCalled();
  });

  it('gắn lỗi giá bán với đúng biến thể và xóa lỗi khi sửa ô đó', async () => {
    const onSave = await renderForm();
    await change('select[name="categoryId"]', 'headset');
    await change('select[name="brandId"]', 'brand');
    await change('input[name="name"]', 'Tai nghe thử nghiệm');
    await change('input[name="spec-connection"]', 'Bluetooth');
    await act(async () =>
      container.querySelector<HTMLButtonElement>('button[name="add-option"]')!.click(),
    );
    await change('input[name="option-name-0"]', 'Màu');
    await change('input[name="option-values-0"]', 'Đỏ, Xanh');
    await change('input[name="variant-price-0"]', '100000');
    await act(async () =>
      container.querySelector<HTMLButtonElement>('button[type="submit"]')!.click(),
    );
    const validPrice = container.querySelector<HTMLInputElement>('[name="variant-price-0"]')!;
    const invalidPrice = container.querySelector<HTMLInputElement>('[name="variant-price-1"]')!;
    expect(validPrice.getAttribute('aria-invalid')).toBeNull();
    expect(invalidPrice.getAttribute('aria-invalid')).toBe('true');
    // Id câu lỗi đứng đầu danh sách. Id đơn vị `đồng` của ô tiền đứng sau.
    const errorId = invalidPrice.getAttribute('aria-describedby')?.split(' ')[0];
    expect(document.getElementById(errorId!)?.textContent).toContain('giá bán lớn hơn 0');
    expect(onSave).not.toHaveBeenCalled();

    await change('input[name="variant-price-1"]', '150000');
    expect(invalidPrice.getAttribute('aria-invalid')).toBeNull();
    await act(async () =>
      container.querySelector<HTMLButtonElement>('button[type="submit"]')!.click(),
    );
    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({
        variants: [
          expect.objectContaining({ price: 100000 }),
          expect.objectContaining({ price: 150000 }),
        ],
      }),
    );
  });

  it('giá bán bằng 0 báo lỗi ngay dưới ô và không gửi sản phẩm', async () => {
    const onSave = await renderForm();
    await change('select[name="categoryId"]', 'headset');
    await change('select[name="brandId"]', 'brand');
    await change('input[name="name"]', 'Tai nghe giá không');
    await change('input[name="spec-connection"]', 'Bluetooth');
    await change('input[name="variant-price-0"]', '0');
    await act(async () =>
      container.querySelector<HTMLButtonElement>('button[type="submit"]')!.click(),
    );

    const priceInput = container.querySelector<HTMLInputElement>('[name="variant-price-0"]')!;
    // Id câu lỗi đứng đầu danh sách. Id đơn vị `đồng` của ô tiền đứng sau.
    const priceErrorId = priceInput.getAttribute('aria-describedby')?.split(' ')[0];
    expect(priceInput.getAttribute('aria-invalid')).toBe('true');
    expect(priceErrorId).toBeTruthy();
    expect(document.getElementById(priceErrorId!)?.textContent).toBe('Nhập giá bán lớn hơn 0.');
    expect(onSave).not.toHaveBeenCalled();
  });

  it('lỗi SPECS_INVALID từ API gắn vào đúng ô mà không xóa dữ liệu đang soạn', async () => {
    const onSave = vi
      .fn()
      .mockRejectedValue(
        new ApiError('SPECS_INVALID', 400, 'Thông số không hợp lệ.', [
          { key: 'ram', message: 'RAM không hợp lệ.' },
        ]),
      );
    await renderForm(true, onSave);
    await change('select[name="categoryId"]', 'laptop');
    await change('select[name="brandId"]', 'brand');
    await change('input[name="name"]', 'Laptop thử nghiệm');
    await change('input[name="spec-origin"]', 'Việt Nam');
    await change('input[name="spec-ram"]', '16');
    await change('input[name="variant-price-0"]', '100000');
    await act(async () =>
      container.querySelector<HTMLButtonElement>('button[type="submit"]')!.click(),
    );
    expect(
      container
        .querySelector<HTMLInputElement>('[name="spec-ram"]')
        ?.getAttribute('aria-describedby'),
    ).toBeTruthy();
    expect(container.textContent).toContain('RAM không hợp lệ.');
    expect(container.querySelector<HTMLInputElement>('input[name="name"]')?.value).toBe(
      'Laptop thử nghiệm',
    );
  });

  it('thiếu quyền ghi thì không có nút lưu và tất cả ô nhập đều bị khóa', async () => {
    await renderForm(false);
    expect(container.querySelector('button[type="submit"]')).toBeNull();
    expect(
      Array.from(container.querySelectorAll('input, select, textarea')).every(
        (input) => (input as HTMLInputElement).disabled,
      ),
    ).toBe(true);
  });
  it('tồn kho của biến thể chỉ đọc và không gửi trong payload lưu', async () => {
    const existingProduct: ProductDetail = {
      id: 'prod-1',
      categoryId: 'headset',
      brandId: 'brand',
      name: 'Tai nghe có sẵn',
      shortDesc: 'Mô tả ngắn',
      description: { type: 'doc', content: [{ type: 'paragraph' }] },
      specs: { connection: 'Bluetooth' },
      options: [{ name: 'Màu', values: ['Đen'] }],
      variants: [
        {
          id: 'var-1',
          code: 'EAR-DEN',
          optionValues: { Màu: 'Đen' },
          price: 150000,
          compareAtPrice: 200000,
          stock: 42,
          enabled: true,
          weightGrams: 200,
          lengthCm: 15,
          widthCm: 10,
          heightCm: 5,
        },
      ],
      warrantyMonths: 12,
      status: 'active',
      minPrice: 150000,
      images: [],
    };
    const onSave = vi.fn();
    await renderForm(true, onSave, existingProduct);

    const row = container.querySelector('[data-variant-row]')!;
    expect(row).not.toBeNull();
    expect(row.textContent).toContain('42');

    const stockInputs = container.querySelectorAll('input[aria-label^="Tồn kho"]');
    expect(stockInputs).toHaveLength(0);

    await act(async () =>
      container.querySelector<HTMLButtonElement>('button[type="submit"]')!.click(),
    );
    expect(onSave).toHaveBeenCalledTimes(1);
    const payload = onSave.mock.calls[0]![0] as { variants: Record<string, unknown>[] };
    expect(payload.variants).toHaveLength(1);
    expect('stock' in payload.variants[0]!).toBe(false);
  });

  function productWithImages(alts: string[]): ProductDetail {
    return {
      id: 'prod-anh',
      categoryId: 'headset',
      brandId: 'brand',
      name: 'Tai nghe có ảnh',
      shortDesc: '',
      description: { type: 'doc', content: [{ type: 'paragraph' }] },
      specs: { connection: 'Bluetooth' },
      options: [],
      variants: [
        {
          id: 'var-mac-dinh',
          code: 'EAR-1',
          optionValues: {},
          price: 150000,
          compareAtPrice: null,
          stock: 0,
          enabled: true,
          weightGrams: null,
          lengthCm: null,
          widthCm: null,
          heightCm: null,
        },
      ],
      warrantyMonths: 12,
      status: 'draft',
      minPrice: 150000,
      // API trả ảnh theo `position`. Form phải giữ đúng thứ tự này.
      images: alts.map((alt, index) => ({
        id: `img-${index}`,
        publicId: `pickory/products/anh-${index}`,
        alt,
        skuId: index === 1 ? 'var-mac-dinh' : null,
        position: index,
        url: `https://res.cloudinary.com/demo/image/upload/pickory/products/anh-${index}`,
      })),
    };
  }

  it('gửi ảnh theo thứ tự hiển thị kèm alt đã cắt khoảng trắng và biến thể', async () => {
    const onSave = vi.fn();
    await renderForm(true, onSave, productWithImages(['  Mặt trước  ', 'Mặt sau']));

    await act(async () =>
      container.querySelector<HTMLButtonElement>('button[aria-label="Đưa ảnh 2 lên"]')!.click(),
    );
    await act(async () =>
      container.querySelector<HTMLButtonElement>('button[type="submit"]')!.click(),
    );

    expect(onSave).toHaveBeenCalledTimes(1);
    const payload = onSave.mock.calls[0]![0] as { images: Record<string, unknown>[] };
    expect(payload.images).toEqual([
      { publicId: 'pickory/products/anh-1', alt: 'Mặt sau', skuId: 'var-mac-dinh' },
      { publicId: 'pickory/products/anh-0', alt: 'Mặt trước', skuId: null },
    ]);
  });

  it('ảnh thiếu alt chặn gửi và báo lỗi ngay dưới ô alt', async () => {
    const onSave = vi.fn();
    await renderForm(true, onSave, productWithImages(['Mặt trước', '   ']));

    await act(async () =>
      container.querySelector<HTMLButtonElement>('button[type="submit"]')!.click(),
    );

    expect(onSave).not.toHaveBeenCalled();
    const input = container.querySelector<HTMLInputElement>('#image-alt-1')!;
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(container.querySelector(`#${input.getAttribute('aria-describedby')}`)?.textContent).toBe(
      'Nhập mô tả ảnh (alt).',
    );
    expect(container.querySelector('#image-alt-0')!.getAttribute('aria-invalid')).toBeNull();
  });
});
