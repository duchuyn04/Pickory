import type { ProductDetail, ProductImageInfo, ProductSkuInfo } from './catalog-api';

// Tùy chọn màu có tên bắt đầu bằng "Màu", không phân biệt hoa thường (Decision của UI-C2).
function findColorOptionName(product: ProductDetail): string | null {
  const colorOption = product.options.find((option) =>
    option.name.toLocaleLowerCase('vi-VN').startsWith('màu'),
  );
  return colorOption?.name ?? null;
}

// Ảnh riêng của một SKU hiện cho mọi biến thể cùng màu. Không có tùy chọn màu thì chỉ hiện cho đúng SKU đó.
function belongsToSelectedVariant(
  product: ProductDetail,
  image: ProductImageInfo,
  selectedSku: ProductSkuInfo,
  colorOptionName: string | null,
): boolean {
  if (colorOptionName === null) return image.skuId === selectedSku.id;

  // Ảnh của SKU đã tắt không còn SKU để suy ra màu, nên ẩn thay vì đoán.
  const owner = product.skus.find((sku) => sku.id === image.skuId);
  if (!owner) return false;
  return owner.optionValues[colorOptionName] === selectedSku.optionValues[colorOptionName];
}

export function selectGalleryImages(
  product: ProductDetail,
  selectedSku: ProductSkuInfo | null,
): ProductImageInfo[] {
  const sharedImages = product.images.filter((image) => image.skuId === null);
  // Chưa chọn SKU thì không biết biến thể nào, nên chỉ hiện ảnh chung.
  if (selectedSku === null) return sharedImages;

  const colorOptionName = findColorOptionName(product);
  const variantImages = product.images.filter(
    (image) =>
      image.skuId !== null &&
      belongsToSelectedVariant(product, image, selectedSku, colorOptionName),
  );
  // Ảnh của biến thể đứng trước để ảnh chính khớp với màu khách vừa chọn.
  return [...variantImages, ...sharedImages];
}
