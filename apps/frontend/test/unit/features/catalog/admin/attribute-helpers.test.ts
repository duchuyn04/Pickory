import { describe, expect, it } from 'vitest';
import type { AttributeDefinition } from '../../../../../src/shared/catalog/attributes';
import {
  parseEnumOptions,
  splitCategoryAttributes,
} from '../../../../../src/features/catalog/admin/attribute-helpers';

describe('attribute-helpers', () => {
  it('tách thuộc tính kế thừa của cha và thuộc tính riêng của con chính xác', () => {
    // Ngăn chặn trường hợp thuộc tính của cha bị gộp nhầm vào danh sách sửa của con.
    const parentAttrs: AttributeDefinition[] = [
      {
        key: 'brand_origin',
        label: 'Xuất xứ',
        group: 'Chung',
        type: 'text',
        required: false,
        filterable: true,
      },
    ];

    const ownAttrs: AttributeDefinition[] = [
      {
        key: 'ram_gb',
        label: 'RAM',
        group: 'Cấu hình',
        type: 'number',
        unit: 'GB',
        required: true,
        filterable: true,
      },
    ];

    const result = splitCategoryAttributes(parentAttrs, ownAttrs);

    expect(result.inherited).toHaveLength(1);
    expect(result.inherited[0]?.key).toBe('brand_origin');
    expect(result.own).toHaveLength(1);
    expect(result.own[0]?.key).toBe('ram_gb');
  });

  it('danh mục gốc không có cha thì danh sách kế thừa luôn là mảng rỗng', () => {
    // Danh mục gốc không kế thừa từ bất kỳ danh mục nào khác trong cây phân cấp.
    const ownAttrs: AttributeDefinition[] = [
      {
        key: 'screen_size',
        label: 'Màn hình',
        group: 'Màn hình',
        type: 'number',
        unit: 'inch',
        required: false,
        filterable: true,
      },
    ];

    const result = splitCategoryAttributes(null, ownAttrs);

    expect(result.inherited).toEqual([]);
    expect(result.own).toHaveLength(1);
  });

  it('danh mục chưa có thuộc tính riêng trả về mảng rỗng thay vì null', () => {
    // Tránh lỗi null pointer khi component duyệt danh sách thuộc tính riêng.
    const result = splitCategoryAttributes(undefined, undefined);

    expect(result.inherited).toEqual([]);
    expect(result.own).toEqual([]);
  });

  it('chuyển chuỗi phân cách bằng dấu phẩy thành mảng tùy chọn hợp lệ', () => {
    // Loại bỏ khoảng trắng thừa để dữ liệu enum luôn sạch sẽ.
    const raw = ' Đỏ , Xanh dương , Vàng ';
    const parsed = parseEnumOptions(raw);

    expect(parsed).toEqual(['Đỏ', 'Xanh dương', 'Vàng']);
  });

  it('chuỗi rỗng hoặc giá trị không hợp lệ trả về mảng rỗng để validator bắt lỗi', () => {
    // Enum không có tùy chọn nào sẽ bị chặn lại ở bước thẩm định.
    expect(parseEnumOptions('')).toEqual([]);
    expect(parseEnumOptions(' , , ')).toEqual([]);
    expect(parseEnumOptions(undefined)).toEqual([]);
  });
});
