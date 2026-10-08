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

// Phần trăm giảm so với giá niêm yết. Trả null khi không có giá niêm yết cao hơn giá bán.
export function formatDiscountPercent(price: number, compareAtPrice: number | null): number | null {
  // Giá niêm yết bằng hoặc thấp hơn giá bán không phải khuyến mãi, nên không gạch giá và không hiện phần trăm.
  if (compareAtPrice === null || compareAtPrice <= price) return null;
  return Math.round(((compareAtPrice - price) / compareAtPrice) * 100);
}
