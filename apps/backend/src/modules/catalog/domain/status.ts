import { validateSpecs, type AttributeDefinition } from '@pickory/shared';

// Bốn trạng thái vòng đời của một sản phẩm điện tử.
export type ProductStatus = 'draft' | 'active' | 'hidden' | 'discontinued';

// Dữ liệu tối thiểu của biến thể cần kiểm tra trước khi đăng bán.
export interface SkuPublishableInput {
  id?: string;
  code?: string;
  enabled: boolean;
  price?: number;
  weightGrams?: number | null;
  lengthCm?: number | null;
  widthCm?: number | null;
  heightCm?: number | null;
}

// Dữ liệu sản phẩm phục vụ kiểm tra điều kiện chuyển sang trạng thái active.
export interface ProductPublishableInput {
  specs: Record<string, unknown>;
  attributeDefinitions: AttributeDefinition[];
  images: Array<{ id?: string; publicId?: string }>;
  skus: SkuPublishableInput[];
}

// Kết quả kiểm tra điều kiện phát hành của sản phẩm.
export interface PublishableCheckResult {
  publishable: boolean;
  reasons: string[];
}

// Bảng các bước chuyển trạng thái hợp lệ trong hệ thống.
const VALID_TRANSITIONS: Record<ProductStatus, ProductStatus[]> = {
  draft: ['draft', 'active'],
  active: ['active', 'hidden', 'discontinued'],
  hidden: ['hidden', 'active', 'discontinued'],
  discontinued: ['discontinued', 'active', 'hidden'],
};

// Kiểm tra tính hợp lệ của việc chuyển đổi trạng thái sản phẩm.
export function canTransition(from: ProductStatus, to: ProductStatus): boolean {
  // Tra cứu danh sách trạng thái đích được phép từ trạng thái hiện tại.
  const allowed = VALID_TRANSITIONS[from];
  return allowed ? allowed.includes(to) : false;
}

// Kiểm tra toàn bộ điều kiện cần thiết để một sản phẩm có thể đăng bán.
export function checkPublishable(product: ProductPublishableInput): PublishableCheckResult {
  const reasons: string[] = [];

  // Xác thực bộ thông số kỹ thuật theo danh mục để tránh thiếu thông tin bắt buộc.
  const specValidation = validateSpecs(product.specs, product.attributeDefinitions);
  if (!specValidation.valid) {
    for (const error of specValidation.errors) {
      reasons.push(`Lỗi thông số kỹ thuật: ${error.message}`);
    }
  }

  // Bắt buộc có ít nhất một ảnh để khách xem được hình dáng sản phẩm.
  if (!product.images || product.images.length === 0) {
    reasons.push('Sản phẩm phải có ít nhất 1 ảnh để đăng bán.');
  }

  // Lọc biến thể đang bật để kiểm tra khả năng phục vụ đơn hàng.
  const enabledSkus = product.skus.filter((sku) => sku.enabled);

  // Chặn đăng bán khi không có biến thể nào được bật.
  if (enabledSkus.length === 0) {
    reasons.push('Sản phẩm phải có ít nhất 1 biến thể đang bật.');
  } else {
    // Kiểm tra giá bán của từng biến thể đang bật.
    const hasInvalidPrice = enabledSkus.some(
      (sku) => typeof sku.price !== 'number' || sku.price <= 0,
    );
    if (hasInvalidPrice) {
      reasons.push('Mọi biến thể đang bật phải có giá bán lớn hơn 0.');
    }

    // Kiểm tra thông số vận chuyển của từng biến thể đang bật.
    const hasMissingShippingInfo = enabledSkus.some((sku) => {
      const invalidWeight =
        sku.weightGrams === null || sku.weightGrams === undefined || sku.weightGrams <= 0;
      const invalidLength =
        sku.lengthCm === null || sku.lengthCm === undefined || sku.lengthCm <= 0;
      const invalidWidth = sku.widthCm === null || sku.widthCm === undefined || sku.widthCm <= 0;
      const invalidHeight =
        sku.heightCm === null || sku.heightCm === undefined || sku.heightCm <= 0;
      return invalidWeight || invalidLength || invalidWidth || invalidHeight;
    });

    if (hasMissingShippingInfo) {
      reasons.push(
        'Mọi biến thể đang bật phải có đầy đủ cân nặng và kích thước đóng gói lớn hơn 0.',
      );
    }
  }

  return {
    publishable: reasons.length === 0,
    reasons,
  };
}
