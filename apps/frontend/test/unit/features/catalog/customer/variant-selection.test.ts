import { describe, expect, it } from 'vitest';
import {
  chooseSkuForOptionValue,
  findSkuBySelection,
  isOptionValueAvailable,
  isOptionValueCombinable,
  pickInitialSku,
  selectOptionValue,
} from '../../../../../src/features/catalog/customer/variant-selection';
import {
  buildProductDetail,
  skuBlack256,
  skuBlack512,
  skuSilver256,
  skuSilver512,
} from './product-fixtures';

const { skus } = buildProductDetail();

describe('pickInitialSku', () => {
  it('chọn SKU trong ?sku= khi SKU hợp lệ, kể cả khi SKU hết hàng', () => {
    expect(pickInitialSku(skus, skuBlack512.id)?.id).toBe(skuBlack512.id);
  });

  it('bỏ qua ?sku= không thuộc sản phẩm và chọn SKU còn hàng đầu tiên', () => {
    const unavailableFirst = [skuBlack512, skuSilver256];
    expect(pickInitialSku(unavailableFirst, 'khong-co')?.id).toBe(skuSilver256.id);
  });

  it('chọn SKU đầu tiên khi mọi SKU hết hàng', () => {
    const allOut = [skuBlack512, { ...skuSilver256, inStock: false }];
    expect(pickInitialSku(allOut, null)?.id).toBe(skuBlack512.id);
  });

  it('trả null khi sản phẩm không có SKU', () => {
    expect(pickInitialSku([], null)).toBeNull();
  });
});

describe('findSkuBySelection', () => {
  it('tìm SKU khớp mọi tùy chọn đã chọn', () => {
    expect(findSkuBySelection(skus, { Màu: 'Bạc', 'Bộ nhớ': '512GB' })?.id).toBe(skuSilver512.id);
  });

  it('trả null khi tổ hợp không có SKU', () => {
    const partialSkus = [skuBlack256, skuSilver512];
    expect(findSkuBySelection(partialSkus, { Màu: 'Đen', 'Bộ nhớ': '512GB' })).toBeNull();
  });
});

// Hai SKU không chung giá trị nào: Đen 256GB và Bạc 512GB. Khách ở Đen 256GB phải tới được Bạc 512GB.
const twoSkus = [skuBlack256, skuSilver512];

describe('isOptionValueAvailable', () => {
  it('giữ giá trị bật khi có SKU đang bật mang giá trị đó', () => {
    expect(isOptionValueAvailable(twoSkus, 'Màu', 'Bạc')).toBe(true);
    expect(isOptionValueAvailable(twoSkus, 'Bộ nhớ', '512GB')).toBe(true);
  });

  it('tắt giá trị không có trong SKU đang bật nào', () => {
    expect(isOptionValueAvailable(twoSkus, 'Màu', 'Đỏ')).toBe(false);
    expect(isOptionValueAvailable([skuBlack256], 'Bộ nhớ', '512GB')).toBe(false);
  });

  it('giữ bật giá trị của SKU hết hàng vì khách vẫn được chọn để xem', () => {
    expect(isOptionValueAvailable(skus, 'Bộ nhớ', '512GB')).toBe(true);
  });
});

describe('isOptionValueCombinable', () => {
  const selection = { Màu: 'Đen', 'Bộ nhớ': '256GB' };

  it('báo ghép được khi giá trị ghép với lựa chọn hiện tại thành một SKU', () => {
    expect(isOptionValueCombinable(skus, selection, 'Màu', 'Bạc')).toBe(true);
  });

  it('báo không ghép được khi phải đổi lựa chọn khác mới tới SKU', () => {
    expect(isOptionValueCombinable(twoSkus, selection, 'Màu', 'Bạc')).toBe(false);
    expect(isOptionValueCombinable(twoSkus, selection, 'Bộ nhớ', '512GB')).toBe(false);
  });
});

describe('chooseSkuForOptionValue', () => {
  const blackSelection = { Màu: 'Đen', 'Bộ nhớ': '256GB' };

  it('tới được SKU chỉ ghép được bằng cách đổi cả hai tùy chọn (ví dụ hai SKU)', () => {
    expect(chooseSkuForOptionValue(twoSkus, blackSelection, 'Màu', 'Bạc')?.id).toBe(
      skuSilver512.id,
    );
    expect(chooseSkuForOptionValue(twoSkus, blackSelection, 'Bộ nhớ', '512GB')?.id).toBe(
      skuSilver512.id,
    );
  });

  it('giữ nguyên các giá trị khác khi tổ hợp tồn tại', () => {
    expect(chooseSkuForOptionValue(skus, blackSelection, 'Màu', 'Bạc')?.id).toBe(skuSilver256.id);
  });

  it('chọn SKU giữ được nhiều giá trị đang chọn nhất', () => {
    const threeOptionSkus = [
      { ...skuBlack256, optionValues: { Màu: 'Đen', 'Bộ nhớ': '256GB', Chip: 'M1' } },
      { ...skuSilver256, optionValues: { Màu: 'Bạc', 'Bộ nhớ': '512GB', Chip: 'M2' } },
      { ...skuSilver512, optionValues: { Màu: 'Bạc', 'Bộ nhớ': '256GB', Chip: 'M2' } },
    ];
    const selection = { Màu: 'Đen', 'Bộ nhớ': '256GB', Chip: 'M1' };
    // Bạc + 256GB giữ được 1 giá trị khác (256GB), Bạc + 512GB giữ được 0.
    expect(chooseSkuForOptionValue(threeOptionSkus, selection, 'Màu', 'Bạc')?.optionValues).toEqual(
      {
        Màu: 'Bạc',
        'Bộ nhớ': '256GB',
        Chip: 'M2',
      },
    );
  });

  it('hòa nhau thì ưu tiên SKU còn hàng', () => {
    const outOfStock = { ...skuSilver256, inStock: false };
    const inStock = { ...skuSilver512, inStock: true };
    const selection = { Màu: 'Đen', 'Bộ nhớ': '1TB' };
    expect(chooseSkuForOptionValue([outOfStock, inStock], selection, 'Màu', 'Bạc')?.id).toBe(
      inStock.id,
    );
  });

  it('vẫn hòa thì lấy SKU đứng trước trong danh sách', () => {
    const selection = { Màu: 'Đen', 'Bộ nhớ': '1TB' };
    expect(chooseSkuForOptionValue([skuSilver512, skuSilver256], selection, 'Màu', 'Bạc')?.id).toBe(
      skuSilver512.id,
    );
  });

  it('trả null khi không SKU nào mang giá trị đó', () => {
    expect(chooseSkuForOptionValue(twoSkus, blackSelection, 'Màu', 'Đỏ')).toBeNull();
  });
});

describe('selectOptionValue', () => {
  it('đổi một tùy chọn và giữ nguyên các tùy chọn còn lại', () => {
    expect(selectOptionValue({ Màu: 'Đen', 'Bộ nhớ': '256GB' }, 'Màu', 'Bạc')).toEqual({
      Màu: 'Bạc',
      'Bộ nhớ': '256GB',
    });
  });
});
