import { useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { APP_NAME } from '../../shared/app-name';
import { ErrorAlert } from '../../shared/error-alert';
import { Icon } from '../../shared/icons';
import type { BrandFacet, CategoryNode } from '../catalog/customer/catalog-api';
import type { ProductSort } from '../catalog/customer/category-query';
import { COMPARE_LIST_LIMIT } from '../catalog/customer/compare-list';
import { formatProductPrice } from '../catalog/customer/product-display';
import { withCloudinaryTransform } from '../catalog/shared/image-url';
import { BannerCarousel } from './banner-carousel';
import { getBrandLogo } from './brand-logos';
import { getTabIndexForKey } from './carousel-index';
import { HOME_BANNERS } from './home-banners';
import { getCategoryVisual } from './home-content';
import {
  CATALOG_PAGE_SIZE,
  useBrandProducts,
  useCatalogOverview,
  useCatalogPages,
  useCategorySummary,
  useCategoryTree,
  useRailProducts,
} from './home-queries';
import { useRevealOnScroll } from './motion';
import { pickProductWithImage } from './pick-product';
import { CategoryPromoPair, ProductDuel } from './promo-banners';
import { ProductGrid, ProductGridSkeleton } from './product-grid';
import './home-page.css';

// Số ô giữ chỗ của lưới danh mục khi đang tải. Bằng số danh mục gốc hiện có.
const CATEGORY_SKELETON_COUNT = 5;
// Ô danh mục hiện ảnh khoảng 160px. Ảnh rộng gấp đôi để nét trên màn mật độ cao.
const TILE_IMAGE_WIDTH = 320;
// Khối thương hiệu có một hàng nút chọn hãng. Hơn sáu hãng thì hàng nút xuống dòng ở màn 1200px.
const BRAND_PILL_LIMIT = 6;
// Khối thương hiệu hiện trước một hàng sáu thẻ. Nút "Xem thêm" mở hàng thứ hai.
const BRAND_PREVIEW_COUNT = 6;

const SORT_OPTIONS: { value: ProductSort; label: string }[] = [
  { value: 'newest', label: 'Mới nhất' },
  { value: 'price_asc', label: 'Giá thấp đến cao' },
  { value: 'price_desc', label: 'Giá cao đến thấp' },
];

export function HomePage() {
  const categoriesQuery = useCategoryTree();
  const categories = categoriesQuery.data;

  return (
    <main className="home-page">
      <h1 className="sr">{APP_NAME}: điện thoại, laptop, màn hình, tai nghe và phụ kiện</h1>

      <div className="home-container">
        <BannerCarousel banners={HOME_BANNERS} />
        <ServiceStrip />

        {categoriesQuery.isError && (
          <div className="home-error">
            <ErrorAlert message="Không tải được danh mục sản phẩm. Kiểm tra kết nối rồi thử lại." />
            <button type="button" className="btn" onClick={() => void categoriesQuery.refetch()}>
              Thử lại
            </button>
          </div>
        )}

        {categoriesQuery.isPending && <CategoryShelfSkeleton />}

        {categories && categories.length > 0 && (
          <>
            <CategoryShelf categories={categories} />
            <NewArrivals categories={categories} />
          </>
        )}

        <BrandDeals />
        {categories && categories.length > 0 && <CategoryPromoPair categories={categories} />}
        <CatalogBrowser />
        <BrandMarquee />
        <ProductDuel fallback={<CompareBand />} />
        {categories && categories.length > 0 && <QuickLinks categories={categories} />}
      </div>
    </main>
  );
}

// Dải cam kết ngay dưới banner, giống dòng ưu đãi thanh toán của thegioididong.com.
// Chỉ ghi điều cửa hàng làm được thật. Pickory chưa có trả góp hay thu cũ, nên dải không nhắc tới.
function ServiceStrip() {
  const overview = useCatalogOverview();

  return (
    <ul className="service-strip" aria-label={`Mua sắm tại ${APP_NAME}`}>
      <li>
        <Icon name="package" />
        {/* Số liệu lấy từ catalog. Khi chưa tải xong thì ghi chung, không in số 0. */}
        {overview.data
          ? `${overview.data.productCount} sản phẩm từ ${overview.data.brands.length} thương hiệu`
          : 'Điện thoại, laptop, màn hình, tai nghe và phụ kiện'}
      </li>
      <li>
        <Icon name="shield" />
        Thời hạn bảo hành ghi rõ trên từng sản phẩm
      </li>
      <li>
        <Link to="/compare">
          <Icon name="filter" />
          So sánh tối đa {COMPARE_LIST_LIMIT} sản phẩm cạnh nhau
          <Icon name="chevronRight" />
        </Link>
      </li>
    </ul>
  );
}

interface HomeSectionProps {
  id: string;
  title: string;
  className?: string;
  children: ReactNode;
}

// Khối trắng bo góc, tiêu đề 24px đậm: khung chung của mọi section theo thegioididong.com.
// Khối hiện dần khi cuộn tới, giữ chuyển động của Pickory.
function HomeSection({ id, title, className = '', children }: HomeSectionProps) {
  const sectionRef = useRevealOnScroll<HTMLElement>();
  const headingId = `home-${id}-heading`;

  return (
    <section
      ref={sectionRef}
      className={`home-section reveal ${className}`.trim()}
      aria-labelledby={headingId}
    >
      <h2 id={headingId} className="home-section-title">
        {title}
      </h2>
      {children}
    </section>
  );
}

function CategoryShelfSkeleton() {
  return (
    <div className="home-section" aria-hidden="true">
      <div className="category-shelf">
        {Array.from({ length: CATEGORY_SKELETON_COUNT }, (_, index) => (
          <div key={index} className="category-tile category-tile-skeleton" />
        ))}
      </div>
    </div>
  );
}

function CategoryShelf({ categories }: { categories: CategoryNode[] }) {
  return (
    <HomeSection id="categories" title="Danh mục sản phẩm">
      <ul className="category-shelf">
        {categories.map((category) => (
          <li key={category.id}>
            <CategoryTile category={category} />
          </li>
        ))}
      </ul>
    </HomeSection>
  );
}

function CategoryTile({ category }: { category: CategoryNode }) {
  const summary = useCategorySummary(category.slug);
  // Dùng chung khóa với lưới "Hàng mới về". Ô danh mục không tạo thêm request.
  const railProducts = useRailProducts(category.slug);
  const visual = getCategoryVisual(category.slug);
  const fromPrice = summary.data?.fromPrice ?? null;
  const tileProduct = pickProductWithImage(railProducts.data, visual.tileProductSlug);

  return (
    <Link to={`/c/${category.slug}`} className="category-tile">
      <span className="category-tile-media">
        {tileProduct?.image ? (
          <img
            src={withCloudinaryTransform(tileProduct.image.url, TILE_IMAGE_WIDTH, { trim: true })}
            alt=""
            width={TILE_IMAGE_WIDTH}
            height={TILE_IMAGE_WIDTH}
            loading="lazy"
          />
        ) : (
          // Danh mục chưa có sản phẩm có ảnh thì ô dùng icon, không để khung trống.
          <Icon name={visual.icon} />
        )}
      </span>
      <span className="category-tile-name">{category.name}</span>
      {/* Dòng đếm giữ chỗ khi đang tải, để các ô cùng hàng cao bằng nhau. */}
      <small className="category-tile-count">
        {summary.data ? `${summary.data.productCount} sản phẩm` : ' '}
        {fromPrice !== null && <>, từ {formatProductPrice(fromPrice)}</>}
      </small>
    </Link>
  );
}

// Lưới có tab chọn danh mục, theo khối "Khuyến mãi online" của thegioididong.com.
function NewArrivals({ categories }: { categories: CategoryNode[] }) {
  const [activeIndex, setActiveIndex] = useState(0);
  const tabRefs = useRef<(HTMLButtonElement | null)[]>([]);
  // Cây danh mục có thể ngắn lại sau khi tải lại. Chỉ số cũ không được trỏ ra ngoài danh sách.
  const visibleIndex = Math.min(activeIndex, categories.length - 1);
  const activeCategory = categories[visibleIndex];
  const products = useRailProducts(activeCategory?.slug ?? '');

  if (!activeCategory) return null;

  function handleTabKeyDown(event: KeyboardEvent<HTMLButtonElement>) {
    const nextIndex = getTabIndexForKey(event.key, visibleIndex, categories.length);
    if (nextIndex === null) return;
    event.preventDefault();
    setActiveIndex(nextIndex);
    tabRefs.current[nextIndex]?.focus();
  }

  const panelId = 'new-arrivals-panel';

  return (
    <HomeSection id="new-arrivals" title="Hàng mới về" className="new-arrivals">
      <div className="section-tabs" role="tablist" aria-label="Chọn danh mục">
        {categories.map((category, index) => {
          const isSelected = index === visibleIndex;
          return (
            <button
              key={category.id}
              ref={(element) => {
                tabRefs.current[index] = element;
              }}
              type="button"
              role="tab"
              className="section-tab"
              aria-selected={isSelected}
              aria-controls={panelId}
              tabIndex={isSelected ? 0 : -1}
              onClick={() => setActiveIndex(index)}
              onKeyDown={handleTabKeyDown}
            >
              {category.name}
            </button>
          );
        })}
      </div>

      {/* `key` đổi theo danh mục, nên lưới mới chạy lại hiệu ứng hiện dần từng thẻ. */}
      <div key={activeCategory.slug} id={panelId} role="tabpanel" className="section-panel">
        {products.isPending && <ProductGridSkeleton />}
        {products.isError && (
          <SectionError
            message={`Không tải được sản phẩm ${activeCategory.name.toLowerCase()}. Thử lại sau ít phút.`}
            onRetry={() => void products.refetch()}
          />
        )}
        {products.isSuccess && products.data.length === 0 && (
          <p className="section-empty">
            Danh mục {activeCategory.name.toLowerCase()} chưa có sản phẩm. Chọn danh mục khác để xem
            hàng mới.
          </p>
        )}
        {products.isSuccess && products.data.length > 0 && (
          <ProductGrid
            products={products.data}
            label={`Sản phẩm ${activeCategory.name.toLowerCase()} mới về`}
          />
        )}
      </div>

      <Link to={`/c/${activeCategory.slug}`} className="section-more">
        Xem tất cả {activeCategory.name.toLowerCase()}
        <Icon name="chevronRight" />
      </Link>
    </HomeSection>
  );
}

function SectionError({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="section-error">
      <ErrorAlert message={message} />
      <button type="button" className="btn btn-sm" onClick={onRetry}>
        Thử lại
      </button>
    </div>
  );
}

// Khối nền đỏ có nút chọn hãng, theo khối "Deal đẹp có hạn" của thegioididong.com.
// Pickory không có giảm giá, nên khối gom sản phẩm theo hãng thay vì theo mức giảm.
function BrandDeals() {
  const overview = useCatalogOverview();
  const brands = overview.data?.brands.slice(0, BRAND_PILL_LIMIT) ?? [];
  const [chosenBrandSlug, setChosenBrandSlug] = useState<string | null>(null);
  const [isExpanded, setIsExpanded] = useState(false);
  const activeBrand = brands.find((brand) => brand.slug === chosenBrandSlug) ?? brands[0] ?? null;
  const products = useBrandProducts(activeBrand?.slug ?? null);
  const sectionRef = useRevealOnScroll<HTMLElement>();

  // Hãng chỉ là lối vào phụ. Lỗi hoặc chưa có hãng thì bỏ khối, không báo lỗi riêng.
  if (overview.isError || (overview.isSuccess && brands.length === 0)) return null;

  const items = products.data?.items ?? [];
  const shownItems = isExpanded ? items : items.slice(0, BRAND_PREVIEW_COUNT);
  const hiddenCount = items.length - shownItems.length;

  function chooseBrand(brand: BrandFacet) {
    setChosenBrandSlug(brand.slug);
    // Hãng mới bắt đầu ở một hàng thẻ, để khối không dài ra bất ngờ.
    setIsExpanded(false);
  }

  return (
    <section
      ref={sectionRef}
      className="brand-deals reveal"
      aria-labelledby="home-brand-deals-heading"
    >
      <div className="brand-deals-head">
        <h2 id="home-brand-deals-heading">Mua theo thương hiệu</h2>
        <p>Chọn hãng để xem các mẫu đang bán.</p>
      </div>

      <div className="brand-pills" role="group" aria-label="Chọn thương hiệu">
        {brands.map((brand) => (
          <button
            key={brand.slug}
            type="button"
            className="brand-pill"
            aria-pressed={brand.slug === activeBrand?.slug}
            onClick={() => chooseBrand(brand)}
          >
            {brand.name}
            <span className="brand-pill-count">{brand.count}</span>
          </button>
        ))}
      </div>

      <div key={activeBrand?.slug ?? 'loading'} className="section-panel">
        {(overview.isPending || products.isPending) && <ProductGridSkeleton />}
        {products.isError && activeBrand && (
          <SectionError
            message={`Không tải được sản phẩm ${activeBrand.name}. Thử lại sau ít phút.`}
            onRetry={() => void products.refetch()}
          />
        )}
        {products.isSuccess && activeBrand && (
          <ProductGrid products={shownItems} label={`Sản phẩm ${activeBrand.name}`} />
        )}
      </div>

      {activeBrand && (hiddenCount > 0 || isExpanded) && (
        <button
          type="button"
          className="brand-deals-more"
          aria-expanded={isExpanded}
          onClick={() => setIsExpanded((expanded) => !expanded)}
        >
          {isExpanded ? 'Thu gọn' : `Xem thêm ${hiddenCount} sản phẩm ${activeBrand.name}`}
          <Icon name={isExpanded ? 'chevronUp' : 'chevronDown'} />
        </button>
      )}
    </section>
  );
}

// Toàn bộ catalog, tải thêm từng trang, theo khối "Gợi ý cho bạn" của thegioididong.com.
// Pickory chưa gợi ý theo khách, nên khối ghi đúng là tất cả sản phẩm và cho đổi cách sắp xếp.
function CatalogBrowser() {
  const [sort, setSort] = useState<ProductSort>('newest');
  const pages = useCatalogPages(sort);
  const items = pages.data?.pages.flatMap((page) => page.items) ?? [];
  const total = pages.data?.pages[0]?.total ?? 0;
  const nextBatchCount = Math.min(CATALOG_PAGE_SIZE, total - items.length);

  return (
    <HomeSection id="catalog" title="Tất cả sản phẩm">
      <div className="sort-chips" role="group" aria-label="Sắp xếp sản phẩm">
        {SORT_OPTIONS.map((option) => (
          <button
            key={option.value}
            type="button"
            className="sort-chip"
            aria-pressed={option.value === sort}
            onClick={() => setSort(option.value)}
          >
            {option.label}
          </button>
        ))}
      </div>

      <div key={sort} className="section-panel">
        {pages.isPending && <ProductGridSkeleton />}
        {pages.isError && !pages.data && (
          <SectionError
            message="Không tải được danh sách sản phẩm. Thử lại sau ít phút."
            onRetry={() => void pages.refetch()}
          />
        )}
        {items.length > 0 && <ProductGrid products={items} label="Tất cả sản phẩm" />}
      </div>

      {pages.hasNextPage ? (
        <button
          type="button"
          className="section-more section-more-button"
          disabled={pages.isFetchingNextPage}
          onClick={() => void pages.fetchNextPage()}
        >
          {pages.isFetchingNextPage ? 'Đang tải thêm' : `Xem thêm ${nextBatchCount} sản phẩm`}
          <Icon name="chevronDown" />
        </button>
      ) : (
        items.length > 0 && <p className="section-end">Bạn đã xem hết {total} sản phẩm.</p>
      )}
    </HomeSection>
  );
}

function BrandMarquee() {
  const overview = useCatalogOverview();
  const brands = overview.data?.brands ?? [];
  // Dải thương hiệu chỉ trang trí thêm. Lỗi hoặc chưa có hãng thì bỏ dải, không báo lỗi riêng.
  if (brands.length === 0) return null;

  return (
    <HomeSection id="brands" title={`Thương hiệu tại ${APP_NAME}`} className="brand-marquee">
      {/* Danh sách đầu cho trình đọc màn hình. Bản sao thứ hai chỉ nối dải chạy vòng, nên ẩn khỏi cây truy cập. */}
      <div className="brand-marquee-viewport">
        <div className="brand-marquee-track">
          <BrandList brands={brands} />
          <BrandList brands={brands} isDuplicate />
        </div>
      </div>
    </HomeSection>
  );
}

function BrandList({
  brands,
  isDuplicate = false,
}: {
  brands: BrandFacet[];
  isDuplicate?: boolean;
}) {
  return (
    <ul className="brand-marquee-list" aria-hidden={isDuplicate || undefined}>
      {brands.map((brand) => {
        const logo = getBrandLogo(brand.slug);
        return (
          <li key={brand.slug}>
            {logo ? (
              <img
                className="brand-marquee-logo"
                src={logo.src}
                alt={brand.name}
                width={logo.width}
                height={logo.height}
                loading="lazy"
              />
            ) : (
              // Hãng chưa có file logo thì hiện tên bằng chữ đậm, cùng độ cao với logo bên cạnh.
              <span className="brand-marquee-name">{brand.name}</span>
            )}
          </li>
        );
      })}
    </ul>
  );
}

function CompareBand() {
  const bandRef = useRevealOnScroll<HTMLElement>();

  return (
    <section ref={bandRef} className="compare-band reveal" aria-labelledby="home-compare-heading">
      <div className="compare-band-copy">
        <h2 id="home-compare-heading">Chưa chọn được máy?</h2>
        <p>
          Đặt tối đa {COMPARE_LIST_LIMIT} sản phẩm cạnh nhau. Bảng xếp cấu hình theo từng dòng để
          bạn thấy ngay điểm khác.
        </p>
      </div>
      <Link to="/compare" className="btn btn-primary compare-band-cta">
        Mở bảng so sánh
        <Icon name="arrowRight" />
      </Link>
    </section>
  );
}

// Lối tắt tới mọi danh mục, theo khối "Mọi người cũng tìm kiếm" của thegioididong.com.
// Pickory chưa đo lượt tìm, nên khối không gọi đây là từ khóa phổ biến.
function QuickLinks({ categories }: { categories: CategoryNode[] }) {
  const links = categories.flatMap((category) => [category, ...category.children]);

  return (
    <HomeSection id="quick-links" title="Tìm nhanh theo danh mục">
      <ul className="quick-links">
        {links.map((category) => (
          <li key={category.id}>
            <Link to={`/c/${category.slug}`}>{category.name}</Link>
          </li>
        ))}
      </ul>
    </HomeSection>
  );
}
