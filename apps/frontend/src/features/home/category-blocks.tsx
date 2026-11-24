import { Link } from 'react-router-dom';
import { Icon } from '../../shared/icons';
import type { CategoryNode } from '../catalog/customer/catalog-api';
import { ProductCard } from '../catalog/customer/product-card';
import { formatProductPrice } from '../catalog/customer/product-display';
import { withCloudinaryTransform } from '../catalog/shared/image-url';
import { CATEGORY_SLUGS, HERO_CATEGORY_LIMIT } from './home-content';
import { useCategorySummary, useRailProducts } from './home-queries';
import { useRevealOnScroll } from './motion';
import { pickProductWithImage } from './pick-product';
import { SectionError } from './section-error';
import './category-blocks.css';

// Mỗi khối danh mục hiện tối đa hai hàng bốn thẻ trên màn máy tính.
const CATEGORY_GRID_LIMIT = 8;
// Giới hạn năm chip hãng để giữ hàng chip gọn trên một dòng ở màn rộng.
const CATEGORY_BRAND_LIMIT = 5;
// Ảnh banner hiển thị tối đa khoảng 200px nên tải rộng 400px để nét trên màn mật độ cao.
const BANNER_IMAGE_WIDTH = 400;

const BANNER_THEME_BY_SLUG: Record<string, string> = {
  [CATEGORY_SLUGS.phone]: 'dien-thoai',
  [CATEGORY_SLUGS.laptop]: 'laptop',
  [CATEGORY_SLUGS.monitor]: 'man-hinh',
  [CATEGORY_SLUGS.headphones]: 'tai-nghe',
  [CATEGORY_SLUGS.accessory]: 'phu-kien',
};

interface CategoryBlocksProps {
  categories: CategoryNode[];
}

// Dựng tối đa năm khối danh mục gốc theo thứ tự trên thanh menu trái.
export function CategoryBlocks({ categories }: CategoryBlocksProps) {
  const rootCategories = categories.slice(0, HERO_CATEGORY_LIMIT);

  return (
    <>
      {rootCategories.map((category) => (
        <CategoryBlock key={category.id} category={category} />
      ))}
    </>
  );
}

function CategoryBlock({ category }: { category: CategoryNode }) {
  const sectionRef = useRevealOnScroll<HTMLElement>();
  const railQuery = useRailProducts(category.slug);
  const summaryQuery = useCategorySummary(category.slug);
  const headingId = `home-cat-${category.slug}-heading`;

  if (railQuery.isPending) {
    return <div className="category-block-skeleton" aria-hidden="true" />;
  }

  // Ẩn khối danh mục khi chưa có sản phẩm để trang chủ không xuất hiện khung trống.
  if (railQuery.isSuccess && railQuery.data.length === 0) {
    return null;
  }

  const products = (railQuery.data ?? []).slice(0, CATEGORY_GRID_LIMIT);
  const bannerProduct = pickProductWithImage(railQuery.data, null);
  const brands = !summaryQuery.isError
    ? (summaryQuery.data?.brands ?? []).slice(0, CATEGORY_BRAND_LIMIT)
    : [];
  const themeKey = BANNER_THEME_BY_SLUG[category.slug] ?? 'neutral';
  const bannerPrice = bannerProduct?.minPrice ?? summaryQuery.data?.fromPrice ?? null;
  const bannerClassName = bannerProduct?.image
    ? `category-banner category-banner--${themeKey}`
    : `category-banner category-banner--${themeKey} category-banner--no-image`;

  return (
    <section
      ref={sectionRef}
      className="category-block reveal"
      data-category={category.slug}
      aria-labelledby={headingId}
    >
      {railQuery.isError ? (
        <div className="category-block-error-wrap">
          <div className="category-block-head">
            <h2 id={headingId} className="category-block-title">
              {category.name}
            </h2>
            <Link
              to={`/c/${category.slug}`}
              className="category-block-more"
              aria-label={`Xem tất cả ${category.name.toLowerCase()}`}
            >
              Xem tất cả
              <Icon name="chevronRight" />
            </Link>
          </div>
          <SectionError
            message={`Không tải được sản phẩm ${category.name.toLowerCase()}. Thử lại sau ít phút.`}
            onRetry={() => void railQuery.refetch()}
          />
        </div>
      ) : (
        <div className="category-block-layout">
          <aside
            className={bannerClassName}
            aria-label={`Sản phẩm nổi bật của ${category.name.toLowerCase()}`}
          >
            <div className="category-banner-copy">
              <span className="category-banner-label">{category.name}</span>
              <p className="category-banner-title">
                {bannerProduct ? bannerProduct.name : category.name}
              </p>
              {bannerPrice !== null && (
                <p className="category-banner-price">Giá từ {formatProductPrice(bannerPrice)}</p>
              )}
              {/* Khi chưa có sản phẩm có ảnh thì nút dẫn thẳng tới trang danh mục. */}
              <Link
                to={bannerProduct ? `/p/${bannerProduct.slug}` : `/c/${category.slug}`}
                className="category-banner-cta"
              >
                Xem ngay
              </Link>
            </div>

            {bannerProduct?.image && (
              <div className="category-banner-media" aria-hidden="true">
                <img
                  src={withCloudinaryTransform(bannerProduct.image.url, BANNER_IMAGE_WIDTH, {
                    trim: true,
                  })}
                  alt=""
                  width={BANNER_IMAGE_WIDTH}
                  height={BANNER_IMAGE_WIDTH}
                  loading="lazy"
                />
              </div>
            )}
          </aside>

          <div className="category-block-content">
            <div className="category-block-head">
              <h2 id={headingId} className="category-block-title">
                {category.name}
              </h2>
              <Link
                to={`/c/${category.slug}`}
                className="category-block-more"
                aria-label={`Xem tất cả ${category.name.toLowerCase()}`}
              >
                Xem tất cả
                <Icon name="chevronRight" />
              </Link>
            </div>

            {/* Ẩn hàng chip hãng khi API tóm tắt lỗi hoặc danh mục chưa có hãng nào. */}
            {brands.length > 0 && (
              <nav
                className="category-brand-chips"
                aria-label={`Thương hiệu ${category.name.toLowerCase()}`}
              >
                {brands.map((brand) => (
                  <Link
                    key={brand.slug}
                    to={`/c/${category.slug}?brand=${encodeURIComponent(brand.slug)}`}
                    className="category-brand-chip"
                  >
                    {brand.name}
                  </Link>
                ))}
              </nav>
            )}

            {/* Không truyền categoryId vì danh mục gốc có danh mục con, thẻ ẩn nút so sánh. */}
            <ul
              className="category-block-grid"
              aria-label={`Sản phẩm ${category.name.toLowerCase()}`}
            >
              {products.map((product) => (
                <li key={product.id}>
                  <ProductCard product={product} />
                </li>
              ))}
            </ul>
          </div>
        </div>
      )}
    </section>
  );
}
