import { Link } from 'react-router-dom';
import { buildCloudinarySrcSet, withCloudinaryTransform } from '../shared/image-url';
import { ProductImage } from '../shared/product-image';
import type { ProductListItem } from './catalog-api';
import { formatProductPrice, getProductBadges } from './product-display';
// Thẻ mang CSS riêng, vì trang danh mục và trang chủ cùng dùng thẻ này.
import './product-card.css';

// Ô lưới có 2 cột ở điện thoại, 2 đến 4 cột ở màn rộng. `sizes` cho trình duyệt chọn ảnh vừa ô.
const IMAGE_SIZES = '(max-width: 767px) 45vw, (max-width: 1023px) 236px, 240px';
const IMAGE_FALLBACK_WIDTH = 400;

export function ProductCard({ product }: { product: ProductListItem }) {
  const badges = getProductBadges(product);
  return (
    <article className="product-card">
      <Link to={`/p/${product.slug}`} className="product-card-link">
        <div className="product-card-media">
          <ProductImage
            src={
              product.image
                ? withCloudinaryTransform(product.image.url, IMAGE_FALLBACK_WIDTH)
                : null
            }
            srcSet={product.image ? buildCloudinarySrcSet(product.image.url) : undefined}
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
