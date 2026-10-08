import { describe, expect, it } from 'vitest';
import {
  EMPTY_CATEGORY_QUERY,
  buildProductsApiPath,
  clearFilters,
  countActiveFilters,
  goToPage,
  parseCategoryQuery,
  serializeCategoryQuery,
  setAttributeRange,
  setAttributeValues,
  setInStock,
  setPriceRange,
  setSort,
  toSearchString,
  toggleAttributeValue,
  toggleBrand,
  type CategoryQuery,
} from '../../../../../src/features/catalog/customer/category-query';

function parse(search: string): CategoryQuery {
  return parseCategoryQuery(new URLSearchParams(search));
}

function serialize(query: CategoryQuery): string {
  return toSearchString(serializeCategoryQuery(query));
}

describe('đọc query string của trang danh mục', () => {
  it('URL rỗng cho trạng thái mặc định', () => {
    expect(parse('')).toEqual(EMPTY_CATEGORY_QUERY);
  });

  it('đọc đủ thương hiệu, giá, còn hàng, thuộc tính, sắp xếp và trang', () => {
    const query = parse(
      'brand=asus,msi&priceMin=1000000&priceMax=30000000&inStock=true' +
        '&attr.ram_gb=16,32&attr.screen_min=15&attr.has_ssd=true&sort=price_asc&page=3&pageSize=12',
    );

    expect(query).toEqual({
      brands: ['asus', 'msi'],
      priceMin: '1000000',
      priceMax: '30000000',
      inStock: true,
      attributes: { ram_gb: ['16', '32'], screen_min: ['15'], has_ssd: ['true'] },
      sort: 'price_asc',
      page: 3,
      pageSize: 12,
    });
  });

  it('giá trị hỏng rơi về mặc định thay vì làm trang lỗi', () => {
    const query = parse('priceMin=abc&priceMax=-5&inStock=1&sort=random&page=0&pageSize=999');

    expect(query).toEqual(EMPTY_CATEGORY_QUERY);
  });

  it('trang không phải số nguyên rơi về trang 1', () => {
    expect(parse('page=2.5').page).toBe(1);
    expect(parse('page=abc').page).toBe(1);
  });

  it('bỏ mục rỗng và trùng trong danh sách', () => {
    const query = parse('brand=asus,,asus, msi&attr.ram_gb=,');

    expect(query.brands).toEqual(['asus', 'msi']);
    expect(query.attributes).toEqual({});
  });

  it('bỏ tham số lạ không thuộc trang', () => {
    expect(parse('utm_source=mail&foo=bar')).toEqual(EMPTY_CATEGORY_QUERY);
  });
});

describe('ghi query string của trang danh mục', () => {
  it('trạng thái mặc định cho chuỗi rỗng', () => {
    expect(serialize(EMPTY_CATEGORY_QUERY)).toBe('');
  });

  it('bỏ sort mặc định, trang 1 và pageSize mặc định', () => {
    const query: CategoryQuery = { ...EMPTY_CATEGORY_QUERY, sort: 'newest', page: 1, pageSize: 24 };

    expect(serialize(query)).toBe('');
  });

  it('ghi tham số attr.* với đúng tên tham số của API', () => {
    const query: CategoryQuery = {
      ...EMPTY_CATEGORY_QUERY,
      attributes: { ram_gb: ['32', '16'], screen_min: ['15'] },
    };

    expect(serialize(query)).toBe('attr.ram_gb=16,32&attr.screen_min=15');
  });

  it('ghi thứ tự cố định để cùng bộ lọc cho cùng một URL', () => {
    const first: CategoryQuery = {
      ...EMPTY_CATEGORY_QUERY,
      brands: ['msi', 'asus'],
      inStock: true,
      sort: 'price_desc',
      page: 2,
    };
    const second: CategoryQuery = { ...first, brands: ['asus', 'msi'] };

    expect(serialize(first)).toBe('brand=asus,msi&inStock=true&sort=price_desc&page=2');
    expect(serialize(second)).toBe(serialize(first));
  });

  it('đọc rồi ghi lại cho URL gốc', () => {
    const search =
      'brand=asus,msi&priceMin=1000000&priceMax=30000000&inStock=true&attr.ram_gb=16,32&sort=price_asc&page=3&pageSize=12';

    expect(serialize(parse(search))).toBe(search);
  });
});

describe('đường dẫn gọi API danh sách sản phẩm', () => {
  it('thêm category từ path và giữ tên tham số của API', () => {
    const query: CategoryQuery = {
      ...EMPTY_CATEGORY_QUERY,
      brands: ['asus'],
      attributes: { ram_gb: ['16'] },
      page: 2,
    };

    expect(buildProductsApiPath('laptop', query)).toBe(
      '/api/products?category=laptop&brand=asus&attr.ram_gb=16&page=2',
    );
  });

  it('mã hóa slug có ký tự đặc biệt', () => {
    expect(buildProductsApiPath('a&b', EMPTY_CATEGORY_QUERY)).toBe('/api/products?category=a%26b');
  });
});

describe('đổi bộ lọc', () => {
  const onPageThree: CategoryQuery = { ...EMPTY_CATEGORY_QUERY, page: 3 };

  it('chọn và bỏ chọn thương hiệu, về trang 1', () => {
    const selected = toggleBrand(onPageThree, 'asus');
    expect(selected.brands).toEqual(['asus']);
    expect(selected.page).toBe(1);

    expect(toggleBrand(selected, 'asus').brands).toEqual([]);
  });

  it('chọn và bỏ chọn giá trị thuộc tính, xóa khóa khi hết giá trị', () => {
    const selected = toggleAttributeValue(onPageThree, 'ram_gb', '16');
    expect(selected.attributes).toEqual({ ram_gb: ['16'] });
    expect(selected.page).toBe(1);

    expect(toggleAttributeValue(selected, 'ram_gb', '16').attributes).toEqual({});
  });

  it('đặt và xóa giá trị đơn của thuộc tính boolean', () => {
    const selected = setAttributeValues(onPageThree, 'has_ssd', ['true']);
    expect(selected.attributes).toEqual({ has_ssd: ['true'] });
    expect(selected.page).toBe(1);

    expect(setAttributeValues(selected, 'has_ssd', []).attributes).toEqual({});
  });

  it('đặt cận dưới và cận trên của thuộc tính số bằng hậu tố _min và _max, về trang 1', () => {
    const ranged = setAttributeRange(onPageThree, 'screen', '14', '16');

    expect(ranged.attributes).toEqual({ screen_min: ['14'], screen_max: ['16'] });
    expect(ranged.page).toBe(1);
  });

  it('ô trống của thuộc tính số xóa cận tương ứng', () => {
    const ranged = setAttributeRange(onPageThree, 'screen', '14', '16');

    expect(setAttributeRange(ranged, 'screen', '', '16').attributes).toEqual({
      screen_max: ['16'],
    });
    expect(setAttributeRange(ranged, 'screen', '', '').attributes).toEqual({});
  });

  it('đặt khoảng giá, cắt khoảng trắng, về trang 1', () => {
    const ranged = setPriceRange(onPageThree, ' 1000000 ', '');

    expect(ranged.priceMin).toBe('1000000');
    expect(ranged.priceMax).toBe('');
    expect(ranged.page).toBe(1);
  });

  it('bật còn hàng và đổi sắp xếp đều về trang 1', () => {
    expect(setInStock(onPageThree, true)).toMatchObject({ inStock: true, page: 1 });
    expect(setSort(onPageThree, 'price_asc')).toMatchObject({ sort: 'price_asc', page: 1 });
  });

  it('chuyển trang giữ nguyên bộ lọc', () => {
    const filtered = toggleBrand(EMPTY_CATEGORY_QUERY, 'asus');

    expect(goToPage(filtered, 4)).toMatchObject({ brands: ['asus'], page: 4 });
  });

  it('xóa bộ lọc giữ sắp xếp và kích thước trang, về trang 1', () => {
    const filtered: CategoryQuery = {
      brands: ['asus'],
      priceMin: '1',
      priceMax: '2',
      inStock: true,
      attributes: { ram_gb: ['16'] },
      sort: 'price_desc',
      page: 4,
      pageSize: 12,
    };

    expect(clearFilters(filtered)).toEqual({
      ...EMPTY_CATEGORY_QUERY,
      sort: 'price_desc',
      pageSize: 12,
    });
  });

  it('đếm mỗi giá trị đã chọn và mỗi cận giá là một bộ lọc', () => {
    expect(countActiveFilters(EMPTY_CATEGORY_QUERY)).toBe(0);
    expect(
      countActiveFilters({
        ...EMPTY_CATEGORY_QUERY,
        brands: ['asus', 'msi'],
        priceMin: '1',
        inStock: true,
        attributes: { ram_gb: ['16', '32'], has_ssd: ['true'] },
        sort: 'price_asc',
      }),
    ).toBe(7);
  });
});
