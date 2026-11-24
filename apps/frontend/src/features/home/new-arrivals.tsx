import { useRef, useState, type KeyboardEvent } from 'react';
import { Link } from 'react-router-dom';
import { ErrorAlert } from '../../shared/error-alert';
import { Icon } from '../../shared/icons';
import type { CategoryNode } from '../catalog/customer/catalog-api';
import { ProductRail, ProductRailSkeleton } from '../catalog/customer/product-rail';
import { getTabIndexForKey } from './carousel-index';
import { useRailProducts } from './home-queries';
import { useRevealOnScroll } from './motion';
import './new-arrivals.css';

// Khối "Hàng mới về" lọc dải sản phẩm theo chip danh mục và liên kết sang trang danh mục đang chọn.
export function NewArrivals({ categories }: { categories: CategoryNode[] }) {
  const [activeIndex, setActiveIndex] = useState(0);
  const tabRefs = useRef<(HTMLButtonElement | null)[]>([]);
  const sectionRef = useRevealOnScroll<HTMLElement>();
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
  const activeTabId = `new-arrivals-tab-${activeCategory.slug}`;

  return (
    <section
      ref={sectionRef}
      className="home-section new-arrivals reveal"
      aria-labelledby="home-new-arrivals-heading"
    >
      <div className="new-arrivals-head">
        <h2 id="home-new-arrivals-heading" className="new-arrivals-title">
          Hàng mới về
        </h2>
        <Link
          to={`/c/${activeCategory.slug}`}
          className="new-arrivals-more"
          aria-label={`Xem tất cả ${activeCategory.name.toLowerCase()}`}
        >
          Xem tất cả
          <Icon name="chevronRight" />
        </Link>
      </div>

      <div className="new-arrivals-chips" role="tablist" aria-label="Chọn danh mục">
        {categories.map((category, index) => {
          const isSelected = index === visibleIndex;
          const tabId = `new-arrivals-tab-${category.slug}`;
          return (
            <button
              key={category.id}
              id={tabId}
              ref={(element) => {
                tabRefs.current[index] = element;
              }}
              type="button"
              role="tab"
              className="new-arrivals-chip"
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

      <div
        key={activeCategory.slug}
        id={panelId}
        role="tabpanel"
        aria-labelledby={activeTabId}
        className="new-arrivals-panel"
      >
        {products.isPending && <ProductRailSkeleton />}
        {products.isError && (
          <div className="section-error">
            <ErrorAlert
              message={`Không tải được sản phẩm ${activeCategory.name.toLowerCase()}. Thử lại sau ít phút.`}
            />
            <button type="button" className="btn btn-sm" onClick={() => void products.refetch()}>
              Thử lại
            </button>
          </div>
        )}
        {products.isSuccess && products.data.length === 0 && (
          <p className="section-empty">
            Danh mục {activeCategory.name.toLowerCase()} chưa có sản phẩm. Chọn danh mục khác để xem
            hàng mới.
          </p>
        )}
        {products.isSuccess && products.data.length > 0 && (
          <ProductRail
            products={products.data}
            label={`Sản phẩm ${activeCategory.name.toLowerCase()} mới về`}
            categoryId={activeCategory.children.length === 0 ? activeCategory.id : undefined}
          />
        )}
      </div>
    </section>
  );
}
