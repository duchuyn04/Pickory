import { useRef, type PointerEvent } from 'react';
import { Link } from 'react-router-dom';
import { Icon } from '../../../shared/icons';
import { buildCloudinarySrcSet, withCloudinaryTransform } from '../shared/image-url';
import { ProductImage } from '../shared/product-image';
import type { ProductListItem } from './catalog-api';
import { formatProductPrice, getProductBadges } from './product-display';
// Thẻ mang CSS riêng, vì trang danh mục và trang chủ cùng dùng thẻ này.
import './product-card.css';

// Ô lưới có 2 cột ở điện thoại, 2 đến 4 cột ở màn rộng. `sizes` cho trình duyệt chọn ảnh vừa ô.
const IMAGE_SIZES = '(max-width: 767px) 45vw, (max-width: 1023px) 236px, 240px';
const IMAGE_FALLBACK_WIDTH = 400;
// Cắt viền trơn quanh ảnh, để máy trong ảnh có khung xám vẫn lấp đầy ô như các thẻ khác.
const TRIMMED = { trim: true };

export function ProductCard({ product }: { product: ProductListItem }) {
  const badges = getProductBadges(product);
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
          {/* Nhãn chỉ hiện khi rê chuột. Tên link đã là tên sản phẩm, nên trình đọc màn hình bỏ qua nhãn. */}
          <span className="product-card-cta" aria-hidden="true">
            Xem chi tiết
            <Icon name="arrowRight" />
          </span>
        </div>
        <p className="product-card-brand">{product.brand.name}</p>
        <h3 className="product-card-name">{product.name}</h3>
        <p
          className={
            product.minPrice === null
              ? 'product-card-price product-card-price-contact'
              : 'product-card-price'
          }
        >
          {formatProductPrice(product.minPrice)}
        </p>
        {badges.length > 0 && (
          <div className="product-card-badges">
            {badges.map((badge) => (
              <span key={badge.label} className={badge.className}>
                {badge.label}
              </span>
            ))}
          </div>
        )}
      </Link>
    </article>
  );
}
