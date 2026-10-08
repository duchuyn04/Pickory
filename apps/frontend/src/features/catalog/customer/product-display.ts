import type { ProductListItem } from './catalog-api';

export function formatProductPrice(minPrice: number | null): string {
  // Sản phẩm chưa có giá thì không in số 0 đồng, để khách không hiểu nhầm là miễn phí.
  if (minPrice === null) return 'Liên hệ';
  return `${minPrice.toLocaleString('vi-VN')} đ`;
}

export interface ProductBadge {
  label: string;
  // Lớp badge chung trong `components.css`.
  className: string;
}

export function getProductBadges(product: ProductListItem): ProductBadge[] {
  const badges: ProductBadge[] = [];
  // Hai trạng thái độc lập nhau: sản phẩm ngừng kinh doanh vẫn có thể còn tồn kho.
  if (product.status === 'discontinued') {
    badges.push({ label: 'Ngừng kinh doanh', className: 'badge badge-warn' });
  }
  if (!product.inStock) {
    badges.push({ label: 'Hết hàng', className: 'badge badge-bad' });
  }
  return badges;
}
