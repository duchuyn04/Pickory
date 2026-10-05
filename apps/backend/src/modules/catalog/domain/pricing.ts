// Dữ liệu đầu vào của biến thể dùng để tính giá hiển thị và trạng thái tồn kho.
export interface SkuPricingInput {
  price: number;
  compareAtPrice?: number | null;
  stock: number;
  enabled: boolean;
}

// Kết quả tính toán giá đại diện và trạng thái còn hàng của sản phẩm.
export interface DisplayPriceResult {
  minPrice: number | null;
  inStock: boolean;
}

// Kết quả kiểm tra tính hợp lệ của giá bán và giá niêm yết.
export interface PriceValidationResult {
  valid: boolean;
  error?: string;
}

// Kiểm tra tính hợp lệ của giá bán và giá niêm yết theo quy tắc kinh doanh.
export function validatePrice(
  price: number,
  compareAtPrice?: number | null,
): PriceValidationResult {
  // Chặn giá bán không phải số nguyên dương để đồng nhất tiền tệ VND.
  if (typeof price !== 'number' || !Number.isInteger(price) || price <= 0) {
    return {
      valid: false,
      error: 'Giá bán phải là số nguyên dương lớn hơn 0.',
    };
  }

  // Cho phép để trống giá niêm yết khi sản phẩm không có chương trình giảm giá.
  if (compareAtPrice === undefined || compareAtPrice === null) {
    return { valid: true };
  }

  // Chặn giá niêm yết không phải số nguyên để bảo đảm tính chuẩn xác của số tiền.
  if (typeof compareAtPrice !== 'number' || !Number.isInteger(compareAtPrice)) {
    return {
      valid: false,
      error: 'Giá niêm yết phải là số nguyên.',
    };
  }

  // Chặn giá niêm yết không lớn hơn giá bán để tránh hiển thị giảm giá sai lệch.
  if (compareAtPrice <= price) {
    return {
      valid: false,
      error: 'Giá niêm yết phải lớn hơn giá bán thực tế.',
    };
  }

  return { valid: true };
}

// Tính giá nhỏ nhất hiển thị và trạng thái còn hàng từ danh sách biến thể.
export function computeDisplayPrice(skus: SkuPricingInput[]): DisplayPriceResult {
  // Lọc danh sách biến thể đang bật vì biến thể tắt không được bán cho khách.
  const enabledSkus = skus.filter((sku) => sku.enabled);

  // Không có biến thể bật nào thì không có giá bán và không còn hàng.
  if (enabledSkus.length === 0) {
    return {
      minPrice: null,
      inStock: false,
    };
  }

  // Lọc biến thể còn hàng trong số các biến thể đang bật.
  const inStockSkus = enabledSkus.filter((sku) => sku.stock > 0);

  // Ưu tiên hiển thị giá thấp nhất của biến thể đang bật và còn hàng.
  if (inStockSkus.length > 0) {
    const minPrice = Math.min(...inStockSkus.map((sku) => sku.price));
    return {
      minPrice,
      inStock: true,
    };
  }

  // Hết hàng toàn bộ thì lấy giá thấp nhất trong các biến thể đang bật để khách tham khảo.
  const minPrice = Math.min(...enabledSkus.map((sku) => sku.price));
  return {
    minPrice,
    inStock: false,
  };
}
