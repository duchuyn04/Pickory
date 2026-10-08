import { Link } from 'react-router-dom';
import { Icon } from '../../../shared/icons';
import { buildCloudinarySrcSet, withCloudinaryTransform } from '../shared/image-url';
import type { ProductListItem } from './catalog-api';
import { formatProductPrice, getProductBadges } from './product-display';

// Ô lưới có 2 cột ở điện thoại, 2 đến 4 cột ở màn rộng. `sizes` cho trình duyệt chọn ảnh vừa ô.
const IMAGE_SIZES = '(max-width: 767px) 45vw, (max-width: 1023px) 236px, 240px';
const IMAGE_FALLBACK_WIDTH = 400;

export function ProductCard({ product }: { product: ProductListItem }) {
  const badges = getProductBadges(product);
  return (
    <article className="product-card">
      <Link to={`/p/${product.slug}`} className="product-card-link">
        <div className="product-card-media">
          {product.image ? (
            <img
              src={withCloudinaryTransform(product.image.url, IMAGE_FALLBACK_WIDTH)}
              srcSet={buildCloudinarySrcSet(product.image.url)}
              sizes={IMAGE_SIZES}
              alt={product.image.alt}
              width={IMAGE_FALLBACK_WIDTH}
              height={IMAGE_FALLBACK_WIDTH}
              loading="lazy"
            />
          ) : (
            // Khung giữ chỗ cùng tỉ lệ với ảnh, để lưới không nhảy khi sản phẩm chưa có ảnh.
            <div className="product-card-placeholder" aria-hidden="true">
              <Icon name="image" className="icon product-card-placeholder-icon" />
            </div>
          )}
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
