import { Link } from 'react-router-dom';
import { APP_NAME } from '../../shared/app-name';
import { ErrorAlert } from '../../shared/error-alert';
import { Icon } from '../../shared/icons';
import type { CategoryNode } from '../catalog/customer/catalog-api';
import { formatProductPrice } from '../catalog/customer/product-display';
import { HeroCarousel } from './hero-carousel';
import {
  getCategoryVisual,
  HERO_SLIDES,
  SIDE_BANNERS,
  type HeroSlideContent,
  type SideBannerContent,
} from './home-content';
import { useCategorySummary, useCategoryTree } from './home-queries';
import { ProductRail } from './product-rail';
import './home-page.css';

// Số dòng giữ chỗ của menu danh mục khi đang tải. Gần với số danh mục gốc hiện có.
const MENU_SKELETON_ROW_COUNT = 5;

// Chỉ giữ nội dung của danh mục còn tồn tại. Admin xóa danh mục thì banner không dẫn tới trang "Không tìm thấy".
function keepExistingCategories<Content extends { categorySlug: string }>(
  contents: Content[],
  categories: CategoryNode[] | undefined,
): Content[] {
  // Cây danh mục lỗi thì vẫn hiện nội dung đã cấu hình, để hero không trống.
  if (!categories) return contents;
  const existingSlugs = new Set(categories.map((category) => category.slug));
  return contents.filter((content) => existingSlugs.has(content.categorySlug));
}

export function HomePage() {
  const categoriesQuery = useCategoryTree();
  const categories = categoriesQuery.data;
  const heroSlides: HeroSlideContent[] = keepExistingCategories(HERO_SLIDES, categories);
  const sideBanners: SideBannerContent[] = keepExistingCategories(SIDE_BANNERS, categories);

  return (
    <main className="home-page">
      {/* Hero không có tiêu đề hiện ra. Tiêu đề ẩn giữ thứ bậc h1, h2 cho trình đọc màn hình. */}
      <h1 className="sr">{APP_NAME}: điện thoại, laptop, màn hình, tai nghe và phụ kiện</h1>

      <div className="home-hero">
        <CategoryMenu categories={categories} isPending={categoriesQuery.isPending} />
        <div className="home-hero-main">
          {/* Chờ cây danh mục để banner không hiện rồi biến mất. Khung giữ chỗ giữ nguyên chiều cao. */}
          {categoriesQuery.isPending ? (
            <div className="hero-skeleton" aria-hidden="true" />
          ) : (
            <HeroCarousel slides={heroSlides} />
          )}
        </div>
        {sideBanners.length > 0 && (
          <div className="home-hero-side">
            {sideBanners.map((banner) => (
              <SideBanner key={banner.categorySlug} banner={banner} />
            ))}
          </div>
        )}
      </div>

      {categoriesQuery.isError && (
        <div className="home-error">
          <ErrorAlert message="Không tải được danh mục sản phẩm." />
          <button type="button" className="btn" onClick={() => void categoriesQuery.refetch()}>
            Thử lại
          </button>
        </div>
      )}

      {categories && categories.length > 0 && (
        <>
          <CategoryTiles categories={categories} />
          {categories.map((category) => (
            <ProductRail key={category.id} category={category} />
          ))}
        </>
      )}
    </main>
  );
}

interface CategoryMenuProps {
  categories: CategoryNode[] | undefined;
  isPending: boolean;
}

function CategoryMenu({ categories, isPending }: CategoryMenuProps) {
  if (isPending) {
    return (
      <div className="home-category-menu" aria-hidden="true">
        {Array.from({ length: MENU_SKELETON_ROW_COUNT }, (_, index) => (
          <div key={index} className="home-category-menu-skeleton" />
        ))}
      </div>
    );
  }
  // Lỗi tải cây đã hiện ở thông báo chung bên dưới hero. Menu rỗng không cần khung riêng.
  if (!categories || categories.length === 0) return null;

  return (
    <nav className="home-category-menu" aria-label="Danh mục sản phẩm">
      <ul>
        {categories.map((category) => (
          <li key={category.id}>
            <Link to={`/c/${category.slug}`}>
              <Icon name={getCategoryVisual(category.slug).icon} />
              <span>{category.name}</span>
              <Icon name="chevronRight" className="icon home-category-menu-chevron" />
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}

function SideBanner({ banner }: { banner: SideBannerContent }) {
  const summary = useCategorySummary(banner.categorySlug);
  const visual = getCategoryVisual(banner.categorySlug);
  const fromPrice = summary.data?.fromPrice ?? null;

  return (
    <Link to={`/c/${banner.categorySlug}`} className="side-banner">
      <span className="side-banner-copy">
        <b>{banner.title}</b>
        {fromPrice !== null && <span>Giá từ {formatProductPrice(fromPrice)}</span>}
      </span>
      {visual.artSrc && (
        <img src={visual.artSrc} alt="" width={400} height={400} className="side-banner-art" />
      )}
    </Link>
  );
}

function CategoryTiles({ categories }: { categories: CategoryNode[] }) {
  return (
    <section className="home-section" aria-labelledby="home-categories-heading">
      <div className="home-section-head">
        <h2 id="home-categories-heading">Danh mục nổi bật</h2>
      </div>
      <ul className="category-tiles">
        {categories.map((category) => (
          <li key={category.id}>
            <CategoryTile category={category} />
          </li>
        ))}
      </ul>
    </section>
  );
}

function CategoryTile({ category }: { category: CategoryNode }) {
  const summary = useCategorySummary(category.slug);
  const visual = getCategoryVisual(category.slug);

  return (
    <Link to={`/c/${category.slug}`} className="category-tile">
      {visual.artSrc ? (
        <img src={visual.artSrc} alt="" width={84} height={84} loading="lazy" />
      ) : (
        <span className="category-tile-icon">
          <Icon name={visual.icon} />
        </span>
      )}
      <span className="category-tile-name">{category.name}</span>
      {/* Dòng đếm giữ chỗ khi đang tải, để các ô cùng hàng cao bằng nhau. */}
      <small className="category-tile-count">
        {summary.data ? `${summary.data.productCount} sản phẩm` : ' '}
      </small>
    </Link>
  );
}
