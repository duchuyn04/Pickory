// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ProductDetail } from '../../../../../src/features/catalog/customer/catalog-api';
import { ProductView } from '../../../../../src/features/catalog/customer/product-view';
import {
  buildProductDetail,
  imageBlack,
  imageShared,
  imageSilver,
  skuBlack256,
  skuSilver256,
  skuSilver512,
} from './product-fixtures';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.localStorage.clear();
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname + location.search}</output>;
}

async function renderProduct(product: ProductDetail, url = '/p/laptop-1') {
  await act(async () =>
    root.render(
      <MemoryRouter initialEntries={[url]}>
        <ProductView product={product} />
        <LocationProbe />
      </MemoryRouter>,
    ),
  );
}

function radio(optionName: string, value: string): HTMLInputElement {
  const group = [...container.querySelectorAll('fieldset')].find(
    (fieldset) => fieldset.querySelector('legend')?.textContent === optionName,
  );
  const input = [...(group?.querySelectorAll('input') ?? [])].find(
    (candidate) => candidate.value === value,
  );
  if (!input) throw new Error(`Không thấy radio ${optionName}: ${value}`);
  return input;
}

async function choose(optionName: string, value: string) {
  await act(async () => radio(optionName, value).click());
}

const priceText = () => container.querySelector('.product-price strong')?.textContent ?? '';
const listPriceText = () => container.querySelector('.product-price-list')?.textContent ?? null;
const mainImageSrc = () => container.querySelector('.gallery-main img')?.getAttribute('src');
const stockBadgeText = () => container.querySelector('.product-buy .badge')?.textContent;
const cartButton = () =>
  [...container.querySelectorAll('button')].find((button) => button.textContent === 'Thêm vào giỏ');

describe('ProductView: chọn biến thể', () => {
  it('mặc định chọn SKU còn hàng đầu tiên và hiện giá, ảnh của SKU đó', async () => {
    await renderProduct(buildProductDetail());
    expect(radio('Màu', 'Đen').checked).toBe(true);
    expect(radio('Bộ nhớ', '256GB').checked).toBe(true);
    expect(priceText()).toBe('20.000.000 đ');
    expect(listPriceText()).toBeNull();
    expect(stockBadgeText()).toBe('Còn hàng');
    expect(mainImageSrc()).toContain(imageBlack.url.split('/image/upload/')[1]);
  });

  it('đổi biến thể thì giá, giá niêm yết, phần trăm giảm, tồn kho và ảnh đổi theo', async () => {
    await renderProduct(buildProductDetail());
    await choose('Màu', 'Bạc');
    expect(priceText()).toBe('21.000.000 đ');
    expect(listPriceText()).toBe('25.000.000 đ');
    expect(container.querySelector('.product-discount')?.textContent).toBe('-16%');
    expect(mainImageSrc()).toContain(imageSilver.url.split('/image/upload/')[1]);

    // Sang bản 512GB màu Đen: hết hàng, không có giá niêm yết.
    await choose('Màu', 'Đen');
    await choose('Bộ nhớ', '512GB');
    expect(priceText()).toBe('24.000.000 đ');
    expect(listPriceText()).toBeNull();
    expect(stockBadgeText()).toBe('Hết hàng');
  });

  it('ghi SKU đang chọn vào ?sku= bằng replace và giữ tham số khác', async () => {
    await renderProduct(buildProductDetail(), '/p/laptop-1?ref=mail');
    await choose('Màu', 'Bạc');
    const location = container.querySelector('[data-testid="location"]')?.textContent;
    expect(location).toBe(`/p/laptop-1?ref=mail&sku=${skuSilver256.id}`);
  });

  it('mở URL có ?sku= thì chọn đúng biến thể, kể cả khi hết hàng', async () => {
    await renderProduct(buildProductDetail(), '/p/laptop-1?sku=sku-black-512');
    expect(radio('Bộ nhớ', '512GB').checked).toBe(true);
    expect(stockBadgeText()).toBe('Hết hàng');
  });

  it('chỉ tắt giá trị không có trong SKU đang bật nào', async () => {
    const withoutSilver512 = buildProductDetail();
    withoutSilver512.skus = withoutSilver512.skus.filter((sku) => sku.id !== 'sku-silver-512');
    withoutSilver512.options = [
      { name: 'Màu', values: ['Đen', 'Bạc', 'Đỏ'] },
      { name: 'Bộ nhớ', values: ['256GB', '512GB'] },
    ];
    await renderProduct(withoutSilver512, '/p/laptop-1?sku=sku-black-512');
    // Màu Đỏ không có SKU nào nên bị tắt. Bạc có SKU Bạc 256GB nên vẫn bấm được.
    expect(radio('Màu', 'Đỏ').disabled).toBe(true);
    expect(radio('Màu', 'Bạc').disabled).toBe(false);
    expect(radio('Bộ nhớ', '256GB').disabled).toBe(false);
    expect(radio('Bộ nhớ', '512GB').disabled).toBe(false);
  });

  it('tới được SKU không chung giá trị nào với SKU hiện tại (Đen 256GB và Bạc 512GB)', async () => {
    const twoSkus = buildProductDetail();
    twoSkus.skus = [skuBlack256, skuSilver512];
    await renderProduct(twoSkus, `/p/laptop-1?sku=${skuBlack256.id}`);
    // Giá trị phải đổi cả lựa chọn khác thì có lớp nhận biết và chữ ẩn cho trình đọc màn hình.
    const silverLabel = radio('Màu', 'Bạc').closest('label');
    expect(silverLabel?.classList.contains('is-switching')).toBe(true);
    expect(silverLabel?.querySelector('.sr')?.textContent).toContain('đổi cả lựa chọn khác');
    expect(radio('Màu', 'Đen').closest('label')?.classList.contains('is-switching')).toBe(false);

    await choose('Màu', 'Bạc');
    expect(radio('Màu', 'Bạc').checked).toBe(true);
    expect(radio('Bộ nhớ', '512GB').checked).toBe(true);
    expect(priceText()).toBe('26.000.000 đ');
    expect(container.querySelector('[data-testid="location"]')?.textContent).toBe(
      `/p/laptop-1?sku=${skuSilver512.id}`,
    );
  });

  it('hiện ảnh chung sau ảnh của biến thể', async () => {
    await renderProduct(buildProductDetail());
    const thumbs = [...container.querySelectorAll('.gallery-thumbs button')];
    expect(thumbs.map((button) => button.getAttribute('aria-label'))).toEqual([
      `Xem ảnh 1: ${imageBlack.alt}`,
      `Xem ảnh 2: ${imageShared.alt}`,
    ]);
    expect(thumbs[0]?.getAttribute('aria-pressed')).toBe('true');
  });

  it('bấm ảnh nhỏ thì đổi ảnh chính', async () => {
    await renderProduct(buildProductDetail());
    const secondThumb = container.querySelectorAll<HTMLButtonElement>('.gallery-thumbs button')[1];
    await act(async () => secondThumb?.click());
    expect(mainImageSrc()).toContain(imageShared.url.split('/image/upload/')[1]);
    expect(secondThumb?.getAttribute('aria-pressed')).toBe('true');
  });
});

describe('ProductView: trạng thái đặc biệt', () => {
  it('sản phẩm ngừng kinh doanh hiện nhãn, tắt nút giỏ và không cho đổi biến thể', async () => {
    await renderProduct(buildProductDetail({ status: 'discontinued' }));
    expect(container.querySelector('h1 ~ .badge, .product-title .badge')?.textContent).toBe(
      'Ngừng kinh doanh',
    );
    expect(container.textContent).toContain('Sản phẩm đã ngừng kinh doanh.');
    // Còn hàng hay hết hàng đều mâu thuẫn với dòng ngừng kinh doanh, nên không hiện nhãn tồn kho.
    expect(container.textContent).not.toContain('Còn hàng');
    expect(container.textContent).not.toContain('Hết hàng');
    expect(container.querySelector('.product-stock')).toBeNull();
    expect(cartButton()?.disabled).toBe(true);
    const radios = [...container.querySelectorAll<HTMLInputElement>('fieldset input')];
    expect(radios.length).toBeGreaterThan(0);
    expect(radios.every((input) => input.disabled)).toBe(true);
    // Bấm vào lựa chọn tắt không đổi SKU.
    await act(async () => radio('Màu', 'Bạc').click());
    expect(radio('Màu', 'Đen').checked).toBe(true);
    expect(container.querySelector('[data-testid="location"]')?.textContent).toBe('/p/laptop-1');
  });

  it('sản phẩm không có SKU hiện Liên hệ và không hiện bộ chọn biến thể', async () => {
    await renderProduct(buildProductDetail({ skus: [], minPrice: null, inStock: false }));
    expect(priceText()).toBe('Liên hệ');
    expect(container.querySelector('fieldset')).toBeNull();
    expect(cartButton()?.disabled).toBe(true);
  });

  it('nút thêm giỏ luôn tắt kèm chú thích Sắp có', async () => {
    await renderProduct(buildProductDetail());
    expect(cartButton()?.disabled).toBe(true);
    expect(container.textContent).toContain('Sắp có');
    const describedBy = cartButton()?.getAttribute('aria-describedby');
    expect(describedBy).toBeTruthy();
    expect(document.getElementById(describedBy ?? '')?.textContent).toBe('Sắp có');
  });

  it('không có ảnh nào thì hiện khung giữ chỗ', async () => {
    await renderProduct(buildProductDetail({ images: [] }));
    expect(container.querySelector('.gallery-main img')).toBeNull();
    expect(container.querySelector('.gallery-placeholder')).not.toBeNull();
  });

  it('chỉ có đúng một h1 là tên sản phẩm', async () => {
    await renderProduct(buildProductDetail());
    const headings = container.querySelectorAll('h1');
    expect(headings).toHaveLength(1);
    expect(headings[0]?.textContent).toBe('Laptop 1');
  });

  it('breadcrumb gồm Trang chủ, danh mục cha, danh mục và tên sản phẩm', async () => {
    await renderProduct(buildProductDetail());
    const items = [...container.querySelectorAll('.catalog-crumbs li')];
    expect(items.map((item) => item.textContent)).toEqual([
      'Trang chủ',
      'Máy tính',
      'Laptop',
      'Laptop 1',
    ]);
    expect(container.querySelector('.catalog-crumbs a[href="/c/laptop"]')).not.toBeNull();
  });

  it('không hiện khung giữ chỗ cho vùng gợi ý và tóm tắt review', async () => {
    await renderProduct(buildProductDetail());
    const slots = container.querySelectorAll('[data-slot]');
    expect([...slots].map((slot) => slot.getAttribute('data-slot'))).toEqual([
      'recommendations',
      'personalized-content',
    ]);
    expect([...slots].every((slot) => slot.textContent === '')).toBe(true);
  });
});

describe('ProductView: so sánh', () => {
  const compareButton = () =>
    [...container.querySelectorAll('button')].find((button) =>
      /so sánh$/.test(button.textContent ?? ''),
    );
  const countText = () => container.querySelector('.compare-count')?.textContent;

  it('thêm và bỏ sản phẩm, số lượng đổi và còn nguyên sau khi dựng lại trang', async () => {
    await renderProduct(buildProductDetail());
    expect(compareButton()?.textContent).toBe('Thêm vào so sánh');
    expect(countText()).toBe('Danh sách so sánh: 0/4');
    expect(compareButton()?.getAttribute('aria-pressed')).toBe('false');

    await act(async () => compareButton()?.click());
    expect(compareButton()?.textContent).toBe('Bỏ khỏi so sánh');
    expect(countText()).toBe('Danh sách so sánh: 1/4');
    expect(compareButton()?.getAttribute('aria-pressed')).toBe('true');

    // Dựng lại cây React mô phỏng tải lại trang: trạng thái đọc từ localStorage.
    await act(async () => root.unmount());
    root = createRoot(container);
    await renderProduct(buildProductDetail());
    expect(compareButton()?.textContent).toBe('Bỏ khỏi so sánh');
    expect(countText()).toBe('Danh sách so sánh: 1/4');

    await act(async () => compareButton()?.click());
    expect(compareButton()?.textContent).toBe('Thêm vào so sánh');
    expect(countText()).toBe('Danh sách so sánh: 0/4');
  });

  it('tắt nút kèm chú thích khi danh sách đã đủ 4 sản phẩm', async () => {
    window.localStorage.setItem(
      'pickory.compare',
      JSON.stringify({ categoryId: 'cat-2', productIds: ['a', 'b', 'c', 'd'] }),
    );
    await renderProduct(buildProductDetail());
    expect(compareButton()?.disabled).toBe(true);
    expect(container.textContent).toContain('Đã đủ 4 sản phẩm');
  });

  it('báo khác danh mục và cho bắt đầu danh sách mới', async () => {
    window.localStorage.setItem(
      'pickory.compare',
      JSON.stringify({ categoryId: 'cat-khac', productIds: ['a', 'b'] }),
    );
    await renderProduct(buildProductDetail());
    expect(compareButton()?.disabled).toBe(true);
    expect(container.textContent).toContain('sản phẩm của danh mục khác');
    const startNew = [...container.querySelectorAll('button')].find(
      (button) => button.textContent === 'Bắt đầu danh sách mới',
    );
    await act(async () => startNew?.click());
    expect(countText()).toBe('Danh sách so sánh: 1/4');
    expect(compareButton()?.getAttribute('aria-pressed')).toBe('true');
  });

  it('vẫn hoạt động khi localStorage ném lỗi', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('bị chặn');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('bị chặn');
    });
    await renderProduct(buildProductDetail());
    expect(countText()).toBe('Danh sách so sánh: 0/4');
    await act(async () => compareButton()?.click());
    expect(countText()).toBe('Danh sách so sánh: 1/4');
    vi.restoreAllMocks();
  });
});
