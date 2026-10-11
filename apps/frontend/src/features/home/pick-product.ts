import type { ProductListItem } from '../catalog/customer/catalog-api';

// Chọn sản phẩm có ảnh thật để đứng trên ô hoặc banner. Sản phẩm đã cấu hình không còn thì dùng sản phẩm mới nhất có ảnh.
export function pickProductWithImage(
  products: ProductListItem[] | undefined,
  preferredSlug: string | null,
): ProductListItem | null {
  const productsWithImage = (products ?? []).filter((product) => product.image !== null);
  return (
    productsWithImage.find((product) => product.slug === preferredSlug) ??
    productsWithImage[0] ??
    null
  );
}
