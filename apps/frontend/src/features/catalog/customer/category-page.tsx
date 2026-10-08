import { useQuery } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { ErrorAlert } from '../../../shared/error-alert';
import { Icon } from '../../../shared/icons';
import type { BrandSummary, CategoryNode } from './catalog-api';
import {
  buildProductsApiPath,
  clearFilters,
  countActiveFilters,
  goToPage,
  parseCategoryQuery,
  serializeCategoryQuery,
  setSort,
  toSearchString,
  type CategoryQuery,
  type ProductSort,
} from './category-query';
import { findCategoryTrail } from './category-tree';
import { ProductCard } from './product-card';
import { ProductFilters } from './product-filters';
import { useProductList } from './use-product-list';
import './category-page.css';

// Dưới ngưỡng này khối bộ lọc gập sau nút "Bộ lọc". Giá trị khớp `@media` trong `category-page.css`.
const MOBILE_MEDIA_QUERY = '(max-width: 767px)';
const FILTERS_PANEL_ID = 'category-filters';

const SORT_OPTIONS: { value: ProductSort; label: string }[] = [
  { value: 'newest', label: 'Mới nhất' },
  { value: 'price_asc', label: 'Giá tăng dần' },
  { value: 'price_desc', label: 'Giá giảm dần' },
];

export function CategoryPage() {
  const { slug } = useParams();
  // Route luôn có `:slug`. Kiểm ở đây để kiểu dữ liệu của `slug` là chuỗi trong phần còn lại.
  if (!slug) return null;
  // Dựng lại trang khi đổi danh mục. Kết quả và trạng thái gập của danh mục cũ không rò sang danh mục mới.
  return <CategoryContent key={slug} slug={slug} />;
}

function CategoryNotFound() {
  return (
    <main className="catalog-page">
      <div className="card catalog-not-found">
        <h1>Không tìm thấy danh mục</h1>
        <p className="muted">Danh mục này không tồn tại hoặc đã bị xóa.</p>
        <Link to="/" className="btn btn-primary">
          Về trang chủ
        </Link>
      </div>
    </main>
  );
}

function CategoryContent({ slug }: { slug: string }) {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const query = useMemo(() => parseCategoryQuery(searchParams), [searchParams]);
  const list = useProductList(buildProductsApiPath(slug, query));
  const categoriesQuery = useQuery<CategoryNode[]>({
    queryKey: ['categories'],
    queryFn: () => apiRequest('/api/categories'),
  });

  // Hãng đang chọn mà facets không còn thì cần tên để hiện cho khách. Chỉ gọi route khi thật sự thiếu tên.
  const facetBrandSlugs = new Set(list.data?.facets.brands.map((brand) => brand.slug));
  const hasUnlistedBrand =
    list.data !== null && query.brands.some((slug) => !facetBrandSlugs.has(slug));
  const brandsQuery = useQuery<BrandSummary[]>({
    queryKey: ['brands'],
    queryFn: () => apiRequest('/api/brands'),
    enabled: hasUnlistedBrand,
  });
  const brandNames = Object.fromEntries(
    (brandsQuery.data ?? []).map((brand) => [brand.slug, brand.name]),
  );

  const [filtersOpen, setFiltersOpen] = useState(false);
  const toggleButtonRef = useRef<HTMLButtonElement>(null);
  const filtersPanelRef = useRef<HTMLElement>(null);
  const resultsRef = useRef<HTMLElement>(null);
  const previousPageRef = useRef(query.page);

  useEffect(() => {
    // Mở khối bộ lọc thì đưa focus vào khối, để người dùng bàn phím và trình đọc màn hình nhận ra nội dung mới.
    if (filtersOpen) filtersPanelRef.current?.focus();
  }, [filtersOpen]);

  useEffect(() => {
    // Chuyển trang thì cuộn về đầu kết quả, để khách không đứng ở cuối trang cũ.
    if (previousPageRef.current !== query.page) {
      resultsRef.current?.scrollIntoView({ block: 'start' });
    }
    previousPageRef.current = query.page;
  }, [query.page]);

  function applyQuery(next: CategoryQuery) {
    // Mỗi lần đổi là một mục lịch sử mới, nên nút Back trả về bộ lọc trước.
    navigate({ search: toSearchString(serializeCategoryQuery(next)) });
  }

  function handleFiltersKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (event.key !== 'Escape' || !filtersOpen) return;
    // Màn rộng luôn hiện bộ lọc và ẩn nút gập, nên Escape chỉ có tác dụng khi nút gập đang hiện.
    if (!window.matchMedia?.(MOBILE_MEDIA_QUERY).matches) return;
    setFiltersOpen(false);
    // Trả focus về nút đã mở khối, để người dùng bàn phím không mất vị trí.
    toggleButtonRef.current?.focus();
  }

  if (list.status === 'error' && list.error.code === 'CATEGORY_NOT_FOUND') {
    return <CategoryNotFound />;
  }

  const trail = categoriesQuery.data ? findCategoryTrail(categoriesQuery.data, slug) : null;
  const categoryName = trail?.at(-1)?.name ?? 'Sản phẩm';
  const activeFilterCount = countActiveFilters(query);
  const data = list.data;
  const pageCount = data ? Math.ceil(data.total / data.pageSize) : 0;

  return (
    <main className="catalog-page">
      <nav className="catalog-crumbs" aria-label="Đường dẫn">
        <ol>
          <li>
            <Link to="/">Trang chủ</Link>
          </li>
          {trail?.map((node, index) => {
            const isCurrent = index === trail.length - 1;
            return (
              <li key={node.id}>
                <Icon name="chevronRight" className="icon catalog-crumbs-separator" />
                {isCurrent ? (
                  <span aria-current="page">{node.name}</span>
                ) : (
                  <Link to={`/c/${node.slug}`}>{node.name}</Link>
                )}
              </li>
            );
          })}
        </ol>
      </nav>

      <header className="catalog-head">
        <h1>{categoryName}</h1>
        {data && (
          <p role="status" className="muted">
            {data.total} sản phẩm
          </p>
        )}
      </header>

      {categoriesQuery.isError && (
        <ErrorAlert message="Không tải được danh mục." className="catalog-alert" />
      )}

      <div className="catalog-body">
        <div className="catalog-toolbar">
          <button
            ref={toggleButtonRef}
            type="button"
            className="btn catalog-filter-toggle"
            aria-expanded={filtersOpen}
            aria-controls={FILTERS_PANEL_ID}
            onClick={() => setFiltersOpen((isOpen) => !isOpen)}
          >
            <Icon name="filter" />
            Bộ lọc
            {activeFilterCount > 0 && <span className="badge badge-info">{activeFilterCount}</span>}
          </button>
          <label className="catalog-sort">
            <span>Sắp xếp</span>
            <select
              className="input-sm"
              value={query.sort}
              onChange={(event) => applyQuery(setSort(query, event.target.value as ProductSort))}
            >
              {SORT_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
        </div>

        <aside
          ref={filtersPanelRef}
          id={FILTERS_PANEL_ID}
          className={filtersOpen ? 'catalog-filters is-open' : 'catalog-filters'}
          aria-label="Bộ lọc sản phẩm"
          tabIndex={-1}
          onKeyDown={handleFiltersKeyDown}
        >
          <div className="catalog-filters-head">
            <h2>Bộ lọc</h2>
            {activeFilterCount > 0 && (
              <button
                type="button"
                className="link-btn"
                onClick={() => applyQuery(clearFilters(query))}
              >
                Xóa bộ lọc
              </button>
            )}
          </div>
          {data ? (
            <ProductFilters
              facets={data.facets}
              query={query}
              brandNames={brandNames}
              onChange={applyQuery}
            />
          ) : (
            list.status === 'loading' && <p className="muted small">Đang tải bộ lọc...</p>
          )}
          <button
            type="button"
            className="btn btn-primary btn-block catalog-filters-close"
            onClick={() => {
              setFiltersOpen(false);
              toggleButtonRef.current?.focus();
            }}
          >
            Xem kết quả
          </button>
        </aside>

        <section ref={resultsRef} className="catalog-results" aria-label="Kết quả">
          {/* Tiêu đề ẩn để thứ bậc đi từ h1 qua h2 tới tên sản phẩm h3, không nhảy cấp. */}
          <h2 className="sr">Danh sách sản phẩm</h2>
          {list.status === 'error' && (
            <>
              <ErrorAlert
                message={list.error.message}
                items={list.error.details?.map((detail) => detail.message)}
                className="catalog-alert"
              />
              {activeFilterCount > 0 && (
                <button
                  type="button"
                  className="btn catalog-alert-action"
                  onClick={() => applyQuery(clearFilters(query))}
                >
                  Xóa bộ lọc
                </button>
              )}
            </>
          )}

          {list.status === 'loading' && !data && (
            <p role="status" className="empty-state">
              Đang tải sản phẩm...
            </p>
          )}

          {data && data.items.length === 0 && (
            <div className="empty-state catalog-empty">
              {data.total === 0 ? (
                <>
                  <p>Không có sản phẩm phù hợp.</p>
                  {activeFilterCount > 0 && (
                    <button
                      type="button"
                      className="btn"
                      onClick={() => applyQuery(clearFilters(query))}
                    >
                      Xóa bộ lọc
                    </button>
                  )}
                </>
              ) : (
                <>
                  <p>Trang {data.page} không có sản phẩm.</p>
                  <Link
                    className="btn"
                    to={{ search: toSearchString(serializeCategoryQuery(goToPage(query, 1))) }}
                  >
                    Về trang 1
                  </Link>
                </>
              )}
            </div>
          )}

          {data && data.items.length > 0 && (
            <ul
              className={list.status === 'loading' ? 'catalog-grid is-refreshing' : 'catalog-grid'}
              aria-busy={list.status === 'loading'}
            >
              {data.items.map((product) => (
                <li key={product.id}>
                  <ProductCard product={product} />
                </li>
              ))}
            </ul>
          )}

          {data && pageCount > 1 && (
            <nav className="pager catalog-pager" aria-label="Phân trang sản phẩm">
              <button
                type="button"
                className="btn btn-sm"
                disabled={query.page <= 1}
                onClick={() => applyQuery(goToPage(query, query.page - 1))}
              >
                Trang trước
              </button>
              <span>
                Trang {data.page} / {pageCount} ({data.total} sản phẩm)
              </span>
              <button
                type="button"
                className="btn btn-sm"
                disabled={query.page >= pageCount}
                onClick={() => applyQuery(goToPage(query, query.page + 1))}
              >
                Trang sau
              </button>
            </nav>
          )}
        </section>
      </div>
    </main>
  );
}
