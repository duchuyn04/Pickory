// @vitest-environment jsdom
import React, { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  ImageManager,
  type ProductImageDraft,
} from '../../../../../src/features/catalog/admin/image-manager';

let container: HTMLDivElement;
let root: Root;

const images: ProductImageDraft[] = [
  {
    publicId: 'pickory/products/mat-truoc',
    alt: 'Mặt trước',
    skuId: null,
    url: 'https://res.cloudinary.com/demo/image/upload/pickory/products/mat-truoc',
  },
  {
    publicId: 'pickory/products/mat-sau',
    alt: 'Mặt sau',
    skuId: null,
    url: 'https://res.cloudinary.com/demo/image/upload/pickory/products/mat-sau',
  },
];
const variants = [
  { id: 'sku-do', label: 'Đỏ / 128' },
  { id: 'sku-xanh', label: 'Xanh / 128' },
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

interface RenderOptions {
  current?: ProductImageDraft[];
  disabled?: boolean;
  altErrors?: Record<string, string>;
}

// Dựng component có state giống form thật, để test thấy kết quả sau mỗi thao tác.
async function renderManager(options: RenderOptions = {}) {
  const onChange = vi.fn();
  const onAddFiles = vi.fn();
  function Harness() {
    const [state, setState] = React.useState(options.current ?? images);
    return (
      <ImageManager
        images={state}
        variants={variants}
        disabled={options.disabled ?? false}
        isUploading={false}
        uploadErrors={[]}
        altErrors={options.altErrors ?? {}}
        onChange={(next) => {
          onChange(next);
          setState(next);
        }}
        onAddFiles={onAddFiles}
      />
    );
  }
  await act(async () => root.render(<Harness />));
  return { onChange, onAddFiles };
}

function button(name: string): HTMLButtonElement {
  const match = container.querySelector<HTMLButtonElement>(`button[aria-label="${name}"]`);
  if (!match) throw new Error(`Không thấy nút ${name}`);
  return match;
}

function altValues(): string[] {
  return Array.from(container.querySelectorAll<HTMLInputElement>('input[name^="image-alt-"]')).map(
    (input) => input.value,
  );
}

describe('ImageManager', () => {
  it('hiện ảnh thu nhỏ đã biến đổi kích thước và ô alt có nhãn theo vị trí', async () => {
    await renderManager();

    const thumbnails = Array.from(container.querySelectorAll('img'));
    expect(thumbnails.map((image) => image.getAttribute('src'))).toEqual([
      'https://res.cloudinary.com/demo/image/upload/f_auto,q_auto,w_200/pickory/products/mat-truoc',
      'https://res.cloudinary.com/demo/image/upload/f_auto,q_auto,w_200/pickory/products/mat-sau',
    ]);
    expect(thumbnails.map((image) => image.getAttribute('alt'))).toEqual(['Mặt trước', 'Mặt sau']);
    expect(container.querySelector('label[for="image-alt-0"]')?.textContent).toContain(
      'Mô tả ảnh 1',
    );
  });

  it('nút lên và xuống đổi thứ tự, nút ở đầu và cuối danh sách bị khóa', async () => {
    await renderManager();

    expect(button('Đưa ảnh 1 lên').disabled).toBe(true);
    expect(button('Đưa ảnh 2 xuống').disabled).toBe(true);

    await act(async () => button('Đưa ảnh 2 lên').click());

    expect(altValues()).toEqual(['Mặt sau', 'Mặt trước']);
  });

  it('sau khi đưa ảnh lên, focus nằm trên nút của chính ảnh đó để người dùng bàn phím đi tiếp', async () => {
    await renderManager();
    button('Đưa ảnh 2 lên').focus();

    await act(async () => button('Đưa ảnh 2 lên').click());

    const focused = document.activeElement as HTMLElement;
    expect(focused.closest('[data-image-id]')?.getAttribute('data-image-id')).toBe(
      'pickory/products/mat-sau',
    );
  });

  it('đưa ảnh xuống cuối thì focus chuyển sang nút lên của chính ảnh đó, vì nút xuống đã bị khóa', async () => {
    await renderManager();
    button('Đưa ảnh 1 xuống').focus();

    await act(async () => button('Đưa ảnh 1 xuống').click());

    expect(altValues()).toEqual(['Mặt sau', 'Mặt trước']);
    expect((document.activeElement as HTMLElement).getAttribute('aria-label')).toBe(
      'Đưa ảnh 2 lên',
    );
  });

  it('bỏ ảnh xóa ảnh khỏi danh sách', async () => {
    await renderManager();

    await act(async () => button('Bỏ ảnh 1').click());

    expect(altValues()).toEqual(['Mặt sau']);
  });

  it('chọn biến thể gắn `skuId` cho ảnh', async () => {
    const { onChange } = await renderManager();
    const select = container.querySelector<HTMLSelectElement>('#image-variant-1')!;

    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')!.set!.call(
        select,
        'sku-xanh',
      );
      select.dispatchEvent(new Event('change', { bubbles: true }));
    });

    const next = onChange.mock.calls.at(-1)![0] as ProductImageDraft[];
    expect(next[1]!.skuId).toBe('sku-xanh');
    expect(next[0]!.skuId).toBeNull();
  });

  it('lỗi alt hiện ngay dưới ô và nối với ô bằng `aria-describedby`', async () => {
    await renderManager({
      altErrors: { 'pickory/products/mat-sau': 'Nhập mô tả ảnh (alt).' },
    });

    const input = container.querySelector<HTMLInputElement>('#image-alt-1')!;
    expect(input.getAttribute('aria-invalid')).toBe('true');
    const errorId = input.getAttribute('aria-describedby')!;
    expect(container.querySelector(`#${errorId}`)?.textContent).toBe('Nhập mô tả ảnh (alt).');
    expect(container.querySelector('#image-alt-0')!.getAttribute('aria-invalid')).toBeNull();
  });

  it('chọn file gọi `onAddFiles` với đúng các file đã chọn', async () => {
    const { onAddFiles } = await renderManager();
    const input = container.querySelector<HTMLInputElement>('input[type="file"]')!;
    const file = new File(['x'], 'moi.jpg', { type: 'image/jpeg' });

    await act(async () => {
      Object.defineProperty(input, 'files', { value: [file], configurable: true });
      input.dispatchEvent(new Event('change', { bubbles: true }));
    });

    expect(onAddFiles).toHaveBeenCalledWith([file]);
  });

  it('đủ 10 ảnh thì không còn ô chọn file và có thông báo giới hạn', async () => {
    const tenImages = Array.from({ length: 10 }, (_, index) => ({
      ...images[0]!,
      publicId: `pickory/products/anh-${index}`,
    }));
    await renderManager({ current: tenImages });

    expect(container.querySelector('input[type="file"]')).toBeNull();
    expect(container.textContent).toContain('Đã đủ 10 ảnh');
  });

  it('chế độ chỉ xem không có nút sắp xếp, bỏ ảnh, chọn file và khóa ô alt', async () => {
    await renderManager({ disabled: true });

    expect(container.querySelectorAll('button')).toHaveLength(0);
    expect(container.querySelector('input[type="file"]')).toBeNull();
    expect(container.querySelector<HTMLInputElement>('#image-alt-0')!.disabled).toBe(true);
  });
});
