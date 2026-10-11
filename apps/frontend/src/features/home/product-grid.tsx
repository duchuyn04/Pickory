import type { ProductListItem } from '../catalog/customer/catalog-api';
import { ProductCard } from '../catalog/customer/product-card';

// Số thẻ giữ chỗ khi đang tải. Bằng số thẻ trên một hàng ở màn rộng.
const SKELETON_CARD_COUNT = 6;

interface ProductGridProps {
  products: ProductListItem[];
  label: string;
}

// Lưới thẻ sản phẩm sáu cột theo thegioididong.com. Thẻ hiện dần lần lượt, mỗi thẻ trễ hơn thẻ trước một nhịp.
export function ProductGrid({ products, label }: ProductGridProps) {
  return (
    <ul className="home-product-grid" aria-label={label}>
      {products.map((product) => (
        <li key={product.id}>
          <ProductCard product={product} />
        </li>
      ))}
    </ul>
  );
}

export function ProductGridSkeleton() {
  return (
    <div className="home-product-grid" aria-hidden="true">
      {Array.from({ length: SKELETON_CARD_COUNT }, (_, index) => (
        <div key={index} className="home-grid-skeleton" />
      ))}
    </div>
  );
}
