import { Link } from 'react-router-dom';
import { Icon } from '../../shared/icons';
import { useCatalogOverview } from './home-queries';
import { useRevealOnScroll } from './motion';
import './featured-brands.css';

// Thiết kế trưng bày tối đa tám thương hiệu phổ biến nhất trên một hàng ở màn máy tính.
const FEATURED_BRAND_LIMIT = 8;

interface FeaturedBrandsProps {
  firstCategorySlug?: string;
  isCategoriesPending: boolean;
  isCategoriesError: boolean;
}

// Khối "Thương hiệu nổi bật" dẫn tới trang danh mục đầu tiên đã lọc theo hãng.
export function FeaturedBrands({
  firstCategorySlug,
  isCategoriesPending,
  isCategoriesError,
}: FeaturedBrandsProps) {
  const sectionRef = useRevealOnScroll<HTMLElement>();
  const overviewQuery = useCatalogOverview();

  if (overviewQuery.isPending || isCategoriesPending) {
    return <div className="featured-brands-skeleton" aria-hidden="true" />;
  }

  const brands = (overviewQuery.data?.brands ?? []).slice(0, FEATURED_BRAND_LIMIT);
  // Ẩn khối khi tải lỗi, chưa có hãng hoặc thiếu danh mục để tránh tạo liên kết hỏng.
  if (overviewQuery.isError || isCategoriesError || brands.length === 0 || !firstCategorySlug) {
    return null;
  }

  return (
    <section
      ref={sectionRef}
      className="home-section featured-brands reveal"
      aria-labelledby="home-featured-brands-heading"
    >
      <div className="featured-brands-head">
        <h2 id="home-featured-brands-heading" className="featured-brands-title">
          Thương hiệu nổi bật
        </h2>
        <Link
          to={`/c/${firstCategorySlug}`}
          className="featured-brands-more"
          aria-label="Xem tất cả thương hiệu"
        >
          Xem tất cả
          <Icon name="chevronRight" />
        </Link>
      </div>

      <ul className="featured-brands-list">
        {brands.map((brand) => (
          <li key={brand.slug}>
            <Link
              to={`/c/${firstCategorySlug}?brand=${encodeURIComponent(brand.slug)}`}
              className="featured-brand-tile"
            >
              {brand.name}
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}
