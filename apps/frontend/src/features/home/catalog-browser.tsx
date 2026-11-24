import { useState } from 'react';
import { Icon } from '../../shared/icons';
import type { ProductSort } from '../catalog/customer/category-query';
import { CATALOG_PAGE_SIZE, useCatalogPages } from './home-queries';
import { useRevealOnScroll } from './motion';
import { ProductGrid, ProductGridSkeleton } from './product-grid';
import { SectionError } from './section-error';

const SORT_OPTIONS: { value: ProductSort; label: string }[] = [
  { value: 'newest', label: 'Mới nhất' },
  { value: 'price_asc', label: 'Giá thấp đến cao' },
  { value: 'price_desc', label: 'Giá cao đến thấp' },
];

// Tách toàn bộ danh sách sản phẩm và bộ sắp xếp khỏi `home-page.tsx` để trang chủ gọn và dễ đọc.
export function CatalogBrowser() {
  const sectionRef = useRevealOnScroll<HTMLElement>();
  const [sort, setSort] = useState<ProductSort>('newest');
  const pages = useCatalogPages(sort);
  const items = pages.data?.pages.flatMap((page) => page.items) ?? [];
  const total = pages.data?.pages[0]?.total ?? 0;
  const nextBatchCount = Math.min(CATALOG_PAGE_SIZE, total - items.length);

  return (
    <section
      ref={sectionRef}
      className="home-section reveal"
      aria-labelledby="home-catalog-heading"
    >
      <h2 id="home-catalog-heading" className="home-section-title">
        Tất cả sản phẩm
      </h2>

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
    </section>
  );
}
