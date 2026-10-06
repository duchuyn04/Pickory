import { describe, expect, it } from 'vitest';
import type { AttributeDefinition } from '../../../../src/shared/catalog/attributes';
import {
  MAX_AXES,
  MAX_COMBINATIONS,
  buildVariantCombinations,
  getLeafCategoryAttributes,
  mergeProductVariants,
  parseOptionalNumber,
  toCombinationKey,
  toProductVariantRequest,
  type CategoryNode,
  type ProductOption,
  type ProductVariant,
} from '../../../../src/features/catalog/admin/product-logic';

describe('buildVariantCombinations', () => {
  it('không có trục tùy chọn nào sinh đúng một tổ hợp rỗng', () => {
    // Không có trục tùy chọn thì trả về một biến thể mặc định với optionValues rỗng.
    const result = buildVariantCombinations([]);
    expect(result).toEqual([{}]);
  });

  it('một trục tùy chọn sinh các tổ hợp đơn lẻ theo thứ tự', () => {
    // Một trục sinh các biến thể tương ứng với từng giá trị của trục đó.
    const options: ProductOption[] = [{ name: 'Màu', values: ['Đen', 'Trắng', 'Xanh'] }];
    const result = buildVariantCombinations(options);
    expect(result).toEqual([{ Màu: 'Đen' }, { Màu: 'Trắng' }, { Màu: 'Xanh' }]);
  });

  it('hai trục tùy chọn sinh tích Descartes với thứ tự ổn định', () => {
    // Ghép lần lượt từng giá trị của trục trước với từng giá trị của trục sau.
    const options: ProductOption[] = [
      { name: 'Dung lượng', values: ['128GB', '256GB'] },
      { name: 'Màu', values: ['Đen', 'Trắng'] },
    ];
    const result = buildVariantCombinations(options);
    expect(result).toEqual([
      { 'Dung lượng': '128GB', Màu: 'Đen' },
      { 'Dung lượng': '128GB', Màu: 'Trắng' },
      { 'Dung lượng': '256GB', Màu: 'Đen' },
      { 'Dung lượng': '256GB', Màu: 'Trắng' },
    ]);
  });

  it('ba trục tùy chọn sinh đủ tám tổ hợp', () => {
    // Ba trục với mỗi trục hai giá trị sinh đúng 2 x 2 x 2 tổ hợp biến thể.
    const options: ProductOption[] = [
      { name: 'RAM', values: ['8GB', '16GB'] },
      { name: 'Ổ cứng', values: ['256GB', '512GB'] },
      { name: 'Màu', values: ['Đen', 'Bạc'] },
    ];
    const result = buildVariantCombinations(options);
    expect(result).toHaveLength(8);
  });

  it('từ chối khi cấu hình quá ba trục tùy chọn', () => {
    // Chặn vượt quá số trục tối đa để tránh bùng nổ số lượng SKU.
    const options: ProductOption[] = [
      { name: 'Trục 1', values: ['A', 'B'] },
      { name: 'Trục 2', values: ['C', 'D'] },
      { name: 'Trục 3', values: ['E', 'F'] },
      { name: 'Trục 4', values: ['G', 'H'] },
    ];
    expect(() => buildVariantCombinations(options)).toThrow(
      `Sản phẩm chỉ được có tối đa ${MAX_AXES} trục tùy chọn.`,
    );
  });

  it('từ chối tên trục để trống hoặc chỉ có khoảng trắng', () => {
    // Tên trục làm khóa phân biệt nên không được để rỗng.
    const options: ProductOption[] = [{ name: '   ', values: ['A', 'B'] }];
    expect(() => buildVariantCombinations(options)).toThrow(
      'Tên trục tùy chọn không được để trống.',
    );
  });

  it('từ chối các trục có tên trùng lặp sau khi cắt khoảng trắng', () => {
    // Tên trục trùng lặp dẫn đến xung đột khóa trong bảng thuộc tính biến thể.
    const options: ProductOption[] = [
      { name: 'Màu sắc', values: ['Đen'] },
      { name: ' Màu sắc ', values: ['Trắng'] },
    ];
    expect(() => buildVariantCombinations(options)).toThrow(
      'Tên trục tùy chọn "Màu sắc" bị trùng lặp.',
    );
  });

  it('từ chối trục không có giá trị nào', () => {
    // Trục không có giá trị làm rỗng tích Descartes và mất hết biến thể.
    const options: ProductOption[] = [{ name: 'Màu', values: [] }];
    expect(() => buildVariantCombinations(options)).toThrow(
      'Trục tùy chọn "Màu" phải có ít nhất 1 giá trị.',
    );
  });

  it('từ chối giá trị trục để trống hoặc chỉ chứa khoảng trắng', () => {
    // Giá trị biến thể rỗng không hiển thị được cho người mua chọn.
    const options: ProductOption[] = [{ name: 'Màu', values: ['Đen', '   '] }];
    expect(() => buildVariantCombinations(options)).toThrow(
      'Giá trị của trục tùy chọn "Màu" không được để trống.',
    );
  });

  it('từ chối các giá trị trùng lặp trong cùng một trục', () => {
    // Giá trị trùng nhau sinh ra các biến thể giống hệt nhau gây nhầm lẫn.
    const options: ProductOption[] = [{ name: 'Màu', values: ['Đen', ' Đen '] }];
    expect(() => buildVariantCombinations(options)).toThrow(
      'Giá trị "Đen" của trục tùy chọn "Màu" bị trùng lặp.',
    );
  });

  it('từ chối khi tổng số tổ hợp vượt quá một trăm', () => {
    // Giới hạn 100 tổ hợp để bảo đảm hiệu năng hiển thị và lưu trữ dữ liệu.
    const options: ProductOption[] = [
      { name: 'Trục 1', values: Array.from({ length: 11 }, (_, i) => `Val1_${i}`) },
      { name: 'Trục 2', values: Array.from({ length: 10 }, (_, i) => `Val2_${i}`) },
    ];
    expect(() => buildVariantCombinations(options)).toThrow(
      `Tổng số tổ hợp biến thể vượt quá giới hạn tối đa ${MAX_COMBINATIONS}.`,
    );
  });

  it('chấp nhận cấu hình có đúng một trăm tổ hợp', () => {
    // Đủ 100 tổ hợp vẫn nằm trong giới hạn cho phép của hệ thống.
    const options: ProductOption[] = [
      { name: 'Trục 1', values: Array.from({ length: 10 }, (_, i) => `Val1_${i}`) },
      { name: 'Trục 2', values: Array.from({ length: 10 }, (_, i) => `Val2_${i}`) },
    ];
    const result = buildVariantCombinations(options);
    expect(result).toHaveLength(100);
  });
});

describe('toCombinationKey', () => {
  it('tạo chuỗi khóa đồng nhất không phụ thuộc vào thứ tự thuộc tính', () => {
    // Sắp xếp khóa chữ cái giúp nhận diện biến thể bất kể thứ tự khai báo.
    const key1 = toCombinationKey({ 'Dung lượng': '128GB', Màu: 'Đen' });
    const key2 = toCombinationKey({ Màu: 'Đen', 'Dung lượng': '128GB' });
    expect(key1).toBe(key2);
  });

  it('xử lý đối tượng rỗng hoặc không xác định một cách an toàn', () => {
    // Đối tượng rỗng đại diện cho biến thể của sản phẩm không phân nhánh.
    expect(toCombinationKey({})).toBe(toCombinationKey(undefined));
  });
});

describe('mergeProductVariants', () => {
  it('khớp biến thể hiện có theo giá trị tùy chọn không phụ thuộc vị trí', () => {
    // Giữ nguyên toàn bộ thông tin của biến thể cũ khi tổ hợp khớp nội dung.
    const combinations = [
      { 'Dung lượng': '128GB', Màu: 'Đen' },
      { 'Dung lượng': '256GB', Màu: 'Trắng' },
    ];
    const existingVariants: ProductVariant[] = [
      {
        id: 'var-2',
        code: 'IP-256-WHT',
        optionValues: { Màu: 'Trắng', 'Dung lượng': '256GB' },
        price: 25000000,
        compareAtPrice: 27000000,
        stock: 5,
        enabled: false,
        weightGrams: 200,
        lengthCm: 16,
        widthCm: 8,
        heightCm: 1,
      },
      {
        id: 'var-1',
        code: 'IP-128-BLK',
        optionValues: { 'Dung lượng': '128GB', Màu: 'Đen' },
        price: 20000000,
        compareAtPrice: 22000000,
        stock: 12,
        enabled: true,
        weightGrams: 190,
        lengthCm: 15,
        widthCm: 7,
        heightCm: 1,
      },
    ];

    const merged = mergeProductVariants(combinations, existingVariants);

    expect(merged).toHaveLength(2);
    expect(merged[0]).toEqual({
      id: 'var-1',
      code: 'IP-128-BLK',
      optionValues: { 'Dung lượng': '128GB', Màu: 'Đen' },
      price: 20000000,
      compareAtPrice: 22000000,
      stock: 12,
      enabled: true,
      weightGrams: 190,
      lengthCm: 15,
      widthCm: 7,
      heightCm: 1,
    });
    expect(merged[1]).toEqual({
      id: 'var-2',
      code: 'IP-256-WHT',
      optionValues: { 'Dung lượng': '256GB', Màu: 'Trắng' },
      price: 25000000,
      compareAtPrice: 27000000,
      stock: 5,
      enabled: false,
      weightGrams: 200,
      lengthCm: 16,
      widthCm: 8,
      heightCm: 1,
    });
  });

  it('khởi tạo ô giá trống và kích thước rỗng cho tổ hợp mới sinh', () => {
    // Tổ hợp mới cần để trống giá để người quản trị chủ động nhập số tiền.
    const combinations = [{ Màu: 'Đỏ' }, { Màu: 'Xanh' }];
    const existingVariants: ProductVariant[] = [
      {
        id: 'var-red',
        code: 'SKU-RED',
        optionValues: { Màu: 'Đỏ' },
        price: 150000,
        enabled: true,
        stock: 3,
      },
    ];

    const merged = mergeProductVariants(combinations, existingVariants);

    expect(merged).toHaveLength(2);
    expect(merged[0]!.id).toBe('var-red');
    expect(merged[0]!.price).toBe(150000);
    expect(merged[0]!.stock).toBe(3);

    expect(merged[1]!.id).toBeUndefined();
    expect(merged[1]!.code).toBe('');
    expect(merged[1]!.price).toBe('');
    expect(merged[1]!.compareAtPrice).toBeNull();
    expect(merged[1]!.stock).toBe(0);
    expect(merged[1]!.enabled).toBe(true);
    expect(merged[1]!.weightGrams).toBeNull();
    expect(merged[1]!.lengthCm).toBeNull();
    expect(merged[1]!.widthCm).toBeNull();
    expect(merged[1]!.heightCm).toBeNull();
    expect(merged[1]!.optionValues).toEqual({ Màu: 'Xanh' });
  });

  it('giữ nguyên giá trị bằng không khi biến thể cũ có giá số không', () => {
    // Giá 0 phải giữ nguyên khi ghép để bước thẩm định giá từ chối rõ ràng, không coi là ô trống.
    const combinations = [{ Cỡ: 'S' }];
    const existingVariants: ProductVariant[] = [
      {
        id: 'var-free',
        code: 'SKU-FREE',
        optionValues: { Cỡ: 'S' },
        price: 0,
        enabled: true,
      },
    ];

    const merged = mergeProductVariants(combinations, existingVariants);
    expect(merged[0]!.price).toBe(0);
  });

  it('xử lý biến thể mặc định của sản phẩm không có tùy chọn', () => {
    // Sản phẩm đơn không tùy chọn vẫn duy trì một biến thể duy nhất.
    const combinations = [{}];
    const existingVariants: ProductVariant[] = [
      {
        id: 'var-default',
        code: 'SKU-SINGLE',
        optionValues: {},
        price: 99000,
        stock: 10,
        enabled: true,
      },
    ];

    const merged = mergeProductVariants(combinations, existingVariants);
    expect(merged).toHaveLength(1);
    expect(merged[0]!.id).toBe('var-default');
    expect(merged[0]!.price).toBe(99000);
  });
});

describe('getLeafCategoryAttributes', () => {
  const commonAttribute: AttributeDefinition = {
    key: 'origin',
    label: 'Xuất xứ',
    group: 'Chung',
    type: 'text',
    required: true,
    filterable: false,
  };

  const laptopAttribute: AttributeDefinition = {
    key: 'ram',
    label: 'Dung lượng RAM',
    group: 'Cấu hình',
    type: 'number',
    required: true,
    filterable: false,
  };

  const headsetAttribute: AttributeDefinition = {
    key: 'wireless',
    label: 'Không dây',
    group: 'Kết nối',
    type: 'boolean',
    required: false,
    filterable: true,
  };

  const categories: CategoryNode[] = [
    {
      id: 'electronics',
      parentId: null,
      name: 'Điện tử',
      attributes: [commonAttribute],
    },
    {
      id: 'laptop',
      parentId: 'electronics',
      name: 'Laptop',
      attributes: [laptopAttribute],
    },
    {
      id: 'headset',
      parentId: null,
      name: 'Tai nghe',
      attributes: [headsetAttribute],
    },
  ];

  it('trả về mảng rỗng khi mã danh mục rỗng hoặc không tồn tại', () => {
    // Không chọn danh mục thì không hiển thị thông số kỹ thuật nào.
    expect(getLeafCategoryAttributes('', categories)).toEqual([]);
    expect(getLeafCategoryAttributes('unknown', categories)).toEqual([]);
    expect(getLeafCategoryAttributes(null, categories)).toEqual([]);
  });

  it('lấy thuộc tính của danh mục gốc không có cha', () => {
    // Danh mục cấp cao nhất chỉ dùng các thông số của chính danh mục đó.
    const result = getLeafCategoryAttributes('headset', categories);
    expect(result).toEqual([headsetAttribute]);
  });

  it('kế thừa thuộc tính của danh mục cha và giữ thứ tự cha trước con sau', () => {
    // Thuộc tính dùng chung của cha hiển thị trước thuộc tính chuyên biệt của con.
    const result = getLeafCategoryAttributes('laptop', categories);
    expect(result).toEqual([commonAttribute, laptopAttribute]);
  });

  it('báo lỗi khi danh mục con trùng khóa với thuộc tính của cha', () => {
    // Trùng khóa gây xung đột lưu trữ và không phân biệt được thông số.
    const conflictedCategories: CategoryNode[] = [
      {
        id: 'root',
        parentId: null,
        attributes: [commonAttribute],
      },
      {
        id: 'child',
        parentId: 'root',
        attributes: [{ ...commonAttribute, label: 'Xuất xứ trùng' }],
      },
    ];
    expect(() => getLeafCategoryAttributes('child', conflictedCategories)).toThrow(
      'Trùng khóa thuộc tính "origin" giữa danh mục cha và danh mục con.',
    );
  });
});

describe('toProductVariantRequest', () => {
  it('loại bỏ trường tồn kho và định dạng giá trị số cho payload tạo biến thể', () => {
    // Nghiệp vụ tồn kho thuộc kho vận nên không gửi kèm trong request cập nhật catalog.
    const variant: ProductVariant = {
      id: 'var-123',
      code: '  IP-15-PRO  ',
      optionValues: { Màu: 'Titan' },
      price: '25000000',
      compareAtPrice: '28000000',
      stock: 100,
      enabled: true,
      weightGrams: '210',
      lengthCm: '16',
      widthCm: '8',
      heightCm: '1',
    };

    const request = toProductVariantRequest(variant);

    expect(request).toEqual({
      optionValues: { Màu: 'Titan' },
      code: 'IP-15-PRO',
      price: 25000000,
      compareAtPrice: 28000000,
      enabled: true,
      weightGrams: 210,
      lengthCm: 16,
      widthCm: 8,
      heightCm: 1,
    });
    expect('stock' in request).toBe(false);
  });

  it('xử lý các trường kích thước và giá niêm yết để trống thành null', () => {
    // Thuộc tính không bắt buộc khi để trống cần chuyển về null để gửi lên API.
    const variant: ProductVariant = {
      optionValues: {},
      price: '',
      compareAtPrice: '',
      enabled: true,
      weightGrams: '',
      lengthCm: null,
      widthCm: undefined,
      heightCm: '',
    };

    const request = toProductVariantRequest(variant);

    expect(request.price).toBe(0);
    expect(request.compareAtPrice).toBeNull();
    expect(request.weightGrams).toBeNull();
    expect(request.lengthCm).toBeNull();
    expect(request.widthCm).toBeNull();
    expect(request.heightCm).toBeNull();
  });
});

describe('parseOptionalNumber', () => {
  it('chuyển đổi chuỗi số hợp lệ thành số', () => {
    // Chuỗi số từ ô nhập form được chuyển sang kiểu số học.
    expect(parseOptionalNumber('123')).toBe(123);
    expect(parseOptionalNumber(456)).toBe(456);
  });

  it('trả về null khi giá trị rỗng hoặc không hợp lệ', () => {
    // Các giá trị không có số thực tế đều quy về null.
    expect(parseOptionalNumber('')).toBeNull();
    expect(parseOptionalNumber(null)).toBeNull();
    expect(parseOptionalNumber(undefined)).toBeNull();
    expect(parseOptionalNumber('abc')).toBeNull();
  });
});
