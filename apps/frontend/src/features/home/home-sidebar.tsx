import { Link } from 'react-router-dom';
import { Icon } from '../../shared/icons';
import { COMPARE_LIST_LIMIT } from '../catalog/customer/compare-list';
import { formatProductPrice } from '../catalog/customer/product-display';
import { withCloudinaryTransform } from '../catalog/shared/image-url';
import { ProductImage } from '../catalog/shared/product-image';
import { NEWEST_PRODUCT_LIMIT, useCatalogOverview, useNewestProducts } from './home-queries';
import './home-sidebar.css';

// Ảnh trên cột phải hiển thị 40px nên tải rộng 120px để nét trên màn mật độ cao.
const NEWEST_THUMB_WIDTH = 120;
const NEWEST_THUMB_SIZE = 40;
// Cột phải rộng 224px chỉ đủ hai cột hãng với ba hàng cân đối.
const SIDEBAR_BRAND_LIMIT = 6;

interface HomeSidebarProps {
  firstCategorySlug?: string;
  isCategoriesPending: boolean;
}

// Cột phải khối đầu trang chủ gồm sản phẩm mới, hãng nổi bật và lối vào so sánh.
export function HomeSidebar({ firstCategorySlug, isCategoriesPending }: HomeSidebarProps) {
  return (
    <aside className="home-sidebar" aria-label="Khám phá nhanh">
      <NewestSidebarCard firstCategorySlug={firstCategorySlug} />
      <BrandSidebarCard
        firstCategorySlug={firstCategorySlug}
        isCategoriesPending={isCategoriesPending}
      />
      <Link to="/compare" className="home-sidebar-compare">
        <span>So sánh tối đa {COMPARE_LIST_LIMIT} sản phẩm cạnh nhau</span>
        <Icon name="chevronRight" className="icon home-sidebar-compare-icon" />
      </Link>
    </aside>
  );
}

function NewestSidebarCard({ firstCategorySlug }: { firstCategorySlug?: string }) {
  const newestQuery = useNewestProducts();

  // Giữ khung chờ đúng chiều cao khi đang tải để khối bên dưới không bị nhảy bố cục.
  if (newestQuery.isPending) {
    return <div className="home-sidebar-card home-sidebar-newest-skeleton" aria-hidden="true" />;
  }

  const items = (newestQuery.data ?? []).slice(0, NEWEST_PRODUCT_LIMIT);
  // Ẩn khối phụ khi tải lỗi hoặc chưa có sản phẩm thay vì hiện thông báo lỗi lớn.
  if (newestQuery.isError || items.length === 0) return null;

  return (
    <section
      className="home-sidebar-card home-sidebar-newest"
      aria-labelledby="sidebar-newest-heading"
    >
      <div className="home-sidebar-card-head">
        <h2 id="sidebar-newest-heading" className="home-sidebar-title">
          Sản phẩm mới
        </h2>
        {/* Chỉ hiện liên kết xem tất cả khi có danh mục đầu tiên để tránh dẫn tới đường dẫn rỗng. */}
        {firstCategorySlug && (
          <Link to={`/c/${firstCategorySlug}`} className="home-sidebar-more">
            <span>Xem tất cả</span>
            <Icon name="chevronRight" className="icon home-sidebar-more-icon" />
          </Link>
        )}
      </div>

      <ul className="home-sidebar-product-list">
        {items.map((item) => (
          <li key={item.id}>
            <Link to={`/p/${item.slug}`} className="home-sidebar-product">
              <span className="home-sidebar-thumb">
                <ProductImage
                  src={
                    item.image
                      ? withCloudinaryTransform(item.image.url, NEWEST_THUMB_WIDTH, { trim: true })
                      : null
                  }
                  alt=""
                  width={NEWEST_THUMB_SIZE}
                  height={NEWEST_THUMB_SIZE}
                  loading="lazy"
                />
              </span>
              <span className="home-sidebar-product-info">
                <span className="home-sidebar-product-name">{item.name}</span>
                <span className="home-sidebar-product-price">
                  {formatProductPrice(item.minPrice)}
                </span>
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}

function BrandSidebarCard({
  firstCategorySlug,
  isCategoriesPending,
}: {
  firstCategorySlug?: string;
  isCategoriesPending: boolean;
}) {
  const overviewQuery = useCatalogOverview();

  // Chờ cả cây danh mục và hãng để có đường dẫn lọc hợp lệ trước khi dựng lưới hãng.
  if (overviewQuery.isPending || isCategoriesPending) {
    return <div className="home-sidebar-card home-sidebar-brands-skeleton" aria-hidden="true" />;
  }

  const brands = (overviewQuery.data?.brands ?? []).slice(0, SIDEBAR_BRAND_LIMIT);
  // Ẩn khối khi lỗi, rỗng hoặc không có danh mục gốc để tránh tạo liên kết hỏng.
  if (overviewQuery.isError || brands.length === 0 || !firstCategorySlug) return null;

  return (
    <section
      className="home-sidebar-card home-sidebar-brands"
      aria-labelledby="sidebar-brands-heading"
    >
      <h2 id="sidebar-brands-heading" className="home-sidebar-title">
        Mua theo thương hiệu
      </h2>
      <nav aria-label="Mua theo thương hiệu">
        <ul className="home-sidebar-brand-grid">
          {brands.map((brand) => (
            <li key={brand.slug}>
              <Link
                to={`/c/${firstCategorySlug}?brand=${encodeURIComponent(brand.slug)}`}
                className="home-sidebar-brand-link"
              >
                {brand.name}
              </Link>
            </li>
          ))}
        </ul>
      </nav>
    </section>
  );
}
