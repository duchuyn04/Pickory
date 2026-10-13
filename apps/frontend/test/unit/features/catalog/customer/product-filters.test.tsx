// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ProductFacets } from '../../../../../src/features/catalog/customer/catalog-api';
import {
  EMPTY_CATEGORY_QUERY,
  type CategoryQuery,
} from '../../../../../src/features/catalog/customer/category-query';
import { ProductFilters } from '../../../../../src/features/catalog/customer/product-filters';

const facets: ProductFacets = {
  brands: [
    { slug: 'asus', name: 'ASUS', count: 7 },
    { slug: 'msi', name: 'MSI', count: 3 },
  ],
  attributes: [
    {
      key: 'ram_gb',
      label: 'RAM',
      type: 'enum',
      unit: 'GB',
      values: [
        { value: '8', count: 5 },
        { value: '16', count: 12 },
      ],
    },
    {
      key: 'screen',
      label: 'Màn hình',
      type: 'number',
      unit: 'inch',
      min: 13,
      max: 17,
    },
    {
      key: 'touch',
      label: 'Cảm ứng',
      type: 'boolean',
      values: [
        { value: false, count: 4 },
        { value: true, count: 2 },
      ],
    },
  ],
};

let container: HTMLDivElement;
let root: Root;
let onChange: ReturnType<typeof vi.fn<(next: CategoryQuery) => void>>;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  onChange = vi.fn();
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

async function renderFilters(query: CategoryQuery = EMPTY_CATEGORY_QUERY, shown = facets) {
  await act(async () =>
    root.render(<ProductFilters facets={shown} query={query} onChange={onChange} />),
  );
}

function group(legend: string): HTMLFieldSetElement {
  const found = [...container.querySelectorAll('fieldset')].find(
    (fieldset) => fieldset.querySelector('legend')?.textContent === legend,
  );
  if (!found) throw new Error(`Không có nhóm bộ lọc "${legend}".`);
  return found;
}

function labelsOf(legend: string): string[] {
  return [...group(legend).querySelectorAll('label')].map((label) => label.textContent ?? '');
}

function inputByLabel(legend: string, label: string): HTMLInputElement {
  const labelElement = [...group(legend).querySelectorAll('label')].find(
    (candidate) => candidate.textContent === label,
  );
  const inputId = labelElement?.getAttribute('for');
  const input = inputId ? document.getElementById(inputId) : null;
  if (!(input instanceof HTMLInputElement)) throw new Error(`Không có ô "${label}".`);
  return input;
}

async function type(input: HTMLInputElement, value: string) {
  await act(async () => {
    // React theo dõi giá trị qua setter gốc, nên gán trực tiếp `value` không kích hoạt `onChange`.
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
    setter.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

async function submitRange(legend: string) {
  const form = group(legend).querySelector('form')!;
  await act(async () => {
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  });
}

describe('nhãn bộ lọc có số đếm', () => {
  it('thương hiệu và thuộc tính enum hiện giá trị, đơn vị và số đếm', async () => {
    await renderFilters();

    expect(labelsOf('Thương hiệu')).toEqual(['ASUS (7)', 'MSI (3)']);
    expect(labelsOf('RAM (GB)')).toEqual(['8 GB (5)', '16 GB (12)']);
  });

  it('thuộc tính boolean hiện "Tất cả", "Có (n)", "Không (n)"', async () => {
    await renderFilters();

    expect(labelsOf('Cảm ứng')).toEqual(['Tất cả', 'Có (2)', 'Không (4)']);
  });

  it('mỗi nhóm là một fieldset có legend', async () => {
    await renderFilters();

    const legends = [...container.querySelectorAll('fieldset > legend')].map(
      (legend) => legend.textContent,
    );
    expect(legends).toEqual([
      'Mức giá',
      'Tình trạng',
      'Thương hiệu',
      'RAM (GB)',
      'Màn hình (inch)',
      'Cảm ứng',
    ]);
  });

  it('ô số lấy min và max của facets làm placeholder', async () => {
    await renderFilters();

    expect(inputByLabel('Màn hình (inch)', 'Từ').placeholder).toBe('13');
    expect(inputByLabel('Màn hình (inch)', 'Đến').placeholder).toBe('17');
  });
});

describe('chọn bộ lọc', () => {
  it('chọn một thương hiệu gọi cập nhật với thương hiệu đó và về trang 1', async () => {
    await renderFilters({ ...EMPTY_CATEGORY_QUERY, page: 3 });

    await act(async () => inputByLabel('Thương hiệu', 'ASUS (7)').click());

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_CATEGORY_QUERY, brands: ['asus'], page: 1 });
  });

  it('chọn một giá trị RAM giữ thương hiệu đã chọn', async () => {
    await renderFilters({ ...EMPTY_CATEGORY_QUERY, brands: ['msi'] });

    await act(async () => inputByLabel('RAM (GB)', '16 GB (12)').click());

    expect(onChange).toHaveBeenCalledWith({
      ...EMPTY_CATEGORY_QUERY,
      brands: ['msi'],
      attributes: { ram_gb: ['16'] },
    });
  });

  it('bỏ chọn giá trị đã chọn xóa khóa khỏi bộ lọc', async () => {
    await renderFilters({ ...EMPTY_CATEGORY_QUERY, attributes: { ram_gb: ['16'] } });

    expect(inputByLabel('RAM (GB)', '16 GB (12)').checked).toBe(true);
    await act(async () => inputByLabel('RAM (GB)', '16 GB (12)').click());

    expect(onChange).toHaveBeenCalledWith(EMPTY_CATEGORY_QUERY);
  });

  it('chọn "Có" của thuộc tính boolean đặt giá trị true, chọn "Tất cả" xóa bộ lọc', async () => {
    await renderFilters();
    await act(async () => inputByLabel('Cảm ứng', 'Có (2)').click());
    expect(onChange).toHaveBeenLastCalledWith({
      ...EMPTY_CATEGORY_QUERY,
      attributes: { touch: ['true'] },
    });

    await renderFilters({ ...EMPTY_CATEGORY_QUERY, attributes: { touch: ['true'] } });
    await act(async () => inputByLabel('Cảm ứng', 'Tất cả').click());
    expect(onChange).toHaveBeenLastCalledWith(EMPTY_CATEGORY_QUERY);
  });

  it('bật "Chỉ hiện hàng còn" đặt inStock', async () => {
    await renderFilters();

    await act(async () => inputByLabel('Tình trạng', 'Chỉ hiện hàng còn').click());

    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_CATEGORY_QUERY, inStock: true });
  });
});

describe('giá trị đang chọn không còn trong facets', () => {
  it('vẫn hiện với số đếm 0 và bỏ chọn được', async () => {
    const query: CategoryQuery = {
      ...EMPTY_CATEGORY_QUERY,
      brands: ['lenovo'],
      attributes: { ram_gb: ['32'] },
    };
    await renderFilters(query);

    expect(labelsOf('Thương hiệu')).toEqual(['ASUS (7)', 'MSI (3)', 'lenovo (0)']);
    expect(labelsOf('RAM (GB)')).toEqual(['8 GB (5)', '16 GB (12)', '32 GB (0)']);
    expect(inputByLabel('RAM (GB)', '32 GB (0)').checked).toBe(true);

    await act(async () => inputByLabel('RAM (GB)', '32 GB (0)').click());
    expect(onChange).toHaveBeenCalledWith({ ...query, attributes: {} });
  });

  it('thuộc tính chỉ còn trong bộ lọc, không còn trong facets, vẫn hiện', async () => {
    const withoutRam: ProductFacets = { ...facets, attributes: [] };
    await renderFilters(
      { ...EMPTY_CATEGORY_QUERY, attributes: { ram_gb: ['16'] } },
      {
        ...withoutRam,
        attributes: [{ key: 'ram_gb', label: 'RAM', type: 'enum', unit: 'GB', values: [] }],
      },
    );

    expect(labelsOf('RAM (GB)')).toEqual(['16 GB (0)']);
  });
});

describe('ô "Từ" và "Đến"', () => {
  it('gõ phím chưa gọi cập nhật, chỉ áp khi gửi form', async () => {
    await renderFilters();

    await type(inputByLabel('Mức giá', 'Từ'), '1000000');
    await type(inputByLabel('Mức giá', 'Đến'), '30000000');
    expect(onChange).not.toHaveBeenCalled();

    await submitRange('Mức giá');
    expect(onChange).toHaveBeenCalledWith({
      ...EMPTY_CATEGORY_QUERY,
      priceMin: '1000000',
      priceMax: '30000000',
    });
  });

  it('thuộc tính số ghi cận vào khóa _min và _max', async () => {
    await renderFilters();

    await type(inputByLabel('Màn hình (inch)', 'Từ'), '14');
    await type(inputByLabel('Màn hình (inch)', 'Đến'), '15.6');
    await submitRange('Màn hình (inch)');

    expect(onChange).toHaveBeenCalledWith({
      ...EMPTY_CATEGORY_QUERY,
      attributes: { screen_min: ['14'], screen_max: ['15.6'] },
    });
  });

  it('khoảng ngược hiện lỗi dưới ô "Từ" và không áp bộ lọc', async () => {
    await renderFilters();

    await type(inputByLabel('Mức giá', 'Từ'), '50');
    await type(inputByLabel('Mức giá', 'Đến'), '10');
    await submitRange('Mức giá');

    expect(onChange).not.toHaveBeenCalled();
    const minInput = inputByLabel('Mức giá', 'Từ');
    expect(minInput.getAttribute('aria-invalid')).toBe('true');
    expect(group('Mức giá').textContent).toContain('Giá trị "Từ" không được lớn hơn "Đến".');
  });

  it('ô nạp lại giá trị khi bộ lọc đổi từ bên ngoài', async () => {
    await renderFilters({ ...EMPTY_CATEGORY_QUERY, priceMin: '500' });
    expect(inputByLabel('Mức giá', 'Từ').value).toBe('500');

    await renderFilters({ ...EMPTY_CATEGORY_QUERY, priceMin: '900' });
    expect(inputByLabel('Mức giá', 'Từ').value).toBe('900');
  });
});

describe('ô khoảng giá theo VND', () => {
  it('ô giá là ô tiền chung có hậu tố đ, ô thuộc tính số giữ ô số', async () => {
    await renderFilters();

    const priceMin = inputByLabel('Mức giá', 'Từ');
    expect(priceMin.type).toBe('text');
    expect(priceMin.inputMode).toBe('numeric');
    expect(priceMin.parentElement?.classList.contains('money-input')).toBe(true);

    const screenMin = inputByLabel('Màn hình (inch)', 'Từ');
    expect(screenMin.type).toBe('number');
    expect(screenMin.parentElement?.classList.contains('money-input')).toBe(false);
  });

  it('gõ giá có đơn vị hiện dấu chấm nghìn và áp chuỗi chữ số', async () => {
    await renderFilters();

    await type(inputByLabel('Mức giá', 'Từ'), '1.000.000 đ');
    await type(inputByLabel('Mức giá', 'Đến'), '30000000');
    expect(inputByLabel('Mức giá', 'Từ').value).toBe('1.000.000');
    expect(inputByLabel('Mức giá', 'Đến').value).toBe('30.000.000');

    await submitRange('Mức giá');
    expect(onChange).toHaveBeenCalledWith({
      ...EMPTY_CATEGORY_QUERY,
      priceMin: '1000000',
      priceMax: '30000000',
    });
  });

  it('giá lấy từ URL hiện có dấu chấm nghìn', async () => {
    await renderFilters({ ...EMPTY_CATEGORY_QUERY, priceMin: '1250000' });

    expect(inputByLabel('Mức giá', 'Từ').value).toBe('1.250.000');
  });
});

describe('căn lề ô khoảng giá', () => {
  it('ô giá căn trái như ô nhập khác của form', async () => {
    await renderFilters();

    const frame = inputByLabel('Mức giá', 'Từ').parentElement;
    expect(frame?.classList.contains('money-input')).toBe(true);
    expect(frame?.classList.contains('money-input-end')).toBe(false);
  });
});
