import { useRef, type PointerEvent } from 'react';
import { Link } from 'react-router-dom';
import { buildCloudinarySrcSet, withCloudinaryTransform } from '../shared/image-url';
import { ProductImage } from '../shared/product-image';
import { useLiveStock } from '../shared/stock-live';
import type { ProductListItem } from './catalog-api';
import { CompareIconButton } from './compare-control';
import { formatProductPrice, getProductBadges } from './product-display';
// Thẻ mang CSS riêng, vì trang danh mục và trang chủ cùng dùng thẻ này.
import './product-card.css';

// Ô lưới có 2 cột ở điện thoại, 2 đến 4 cột ở màn rộng. `sizes` cho trình duyệt chọn ảnh vừa ô.
const IMAGE_SIZES = '(max-width: 767px) 45vw, (max-width: 1023px) 236px, 240px';
const IMAGE_FALLBACK_WIDTH = 400;
// Cắt viền trơn quanh ảnh, để máy trong ảnh có khung xám vẫn lấp đầy ô như các thẻ khác.
const TRIMMED = { trim: true };

interface ProductCardProps {
  product: ProductListItem;
  categoryId?: string;
}

export function ProductCard({ product, categoryId }: ProductCardProps) {
  const liveStock = useLiveStock();
  const liveProduct = liveStock.products[product.id];
  // Có sự kiện mới thì nhãn hết hàng và giá thấp nhất đổi ngay, không chờ tải lại danh sách.
  const shownProduct = liveProduct
    ? { ...product, inStock: liveProduct.inStock, minPrice: liveProduct.minPrice }
    : product;
  const badges = getProductBadges(shownProduct);
  // Chỉ hiện nhãn xanh khi sản phẩm đang bán và còn tồn kho để khách nhận biết ngay trên thẻ.
  const statusBadges =
    badges.length > 0
      ? badges
      : shownProduct.status === 'active'
        ? [{ label: 'Còn hàng', className: 'badge badge-ok' }]
        : [];
  const mediaRef = useRef<HTMLDivElement>(null);

  function handlePointerMove(event: PointerEvent<HTMLElement>) {
    // Chỉ chuột làm ảnh nghiêng theo. Ngón tay trên màn cảm ứng không có trạng thái rê.
    if (event.pointerType !== 'mouse') return;
    const media = mediaRef.current;
    if (!media) return;
    const bounds = event.currentTarget.getBoundingClientRect();
    // Đổi vị trí chuột về khoảng [-1, 1] quanh tâm thẻ. CSS nhân giá trị này với độ dời của ảnh.
    const pointerX = ((event.clientX - bounds.left) / bounds.width) * 2 - 1;
    const pointerY = ((event.clientY - bounds.top) / bounds.height) * 2 - 1;
    media.style.setProperty('--pointer-x', pointerX.toFixed(3));
    media.style.setProperty('--pointer-y', pointerY.toFixed(3));
  }

  function handlePointerLeave() {
    // Đưa ảnh về giữa khi chuột rời thẻ, để thẻ không đứng lệch sau khi rê qua.
    mediaRef.current?.style.setProperty('--pointer-x', '0');
    mediaRef.current?.style.setProperty('--pointer-y', '0');
  }

  return (
    <article
      className="product-card"
      onPointerMove={handlePointerMove}
      onPointerLeave={handlePointerLeave}
    >
      <Link to={`/p/${product.slug}`} className="product-card-link">
        <div ref={mediaRef} className="product-card-media">
          <ProductImage
            src={
              product.image
                ? withCloudinaryTransform(product.image.url, IMAGE_FALLBACK_WIDTH, TRIMMED)
                : null
            }
            srcSet={product.image ? buildCloudinarySrcSet(product.image.url, TRIMMED) : undefined}
            sizes={IMAGE_SIZES}
            alt={product.image?.alt ?? ''}
            width={IMAGE_FALLBACK_WIDTH}
            height={IMAGE_FALLBACK_WIDTH}
            loading="lazy"
          />
        </div>
        <p className="product-card-brand">{product.brand.name}</p>
        <h3 className="product-card-name">{product.name}</h3>
        <p
          className={
            shownProduct.minPrice === null
              ? 'product-card-price product-card-price-contact'
              : 'product-card-price'
          }
        >
          {formatProductPrice(shownProduct.minPrice)}
        </p>
      </Link>
      {/* Nút so sánh đặt ngoài thẻ `<a>` để không lồng phần tử tương tác theo chuẩn HTML. */}
      <div className="product-card-footer">
        <div className="product-card-badges">
          {statusBadges.map((badge) => (
            <span key={badge.label} className={badge.className}>
              {badge.label}
            </span>
          ))}
        </div>
        {categoryId && (
          <CompareIconButton
            categoryId={categoryId}
            productId={product.id}
            productName={product.name}
          />
        )}
      </div>
    </article>
  );
}
