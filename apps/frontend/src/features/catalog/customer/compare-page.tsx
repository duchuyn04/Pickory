import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError, apiRequest } from '../../../shared/api';
import { ErrorAlert } from '../../../shared/error-alert';
import { Icon } from '../../../shared/icons';
import { buildCloudinarySrcSet, withCloudinaryTransform } from '../shared/image-url';
import { ProductImage } from '../shared/product-image';
import type { ComparedProduct, ProductCompareResponse } from './catalog-api';
import { buildCompareGroups, findMissingIds } from './compare-rows';
import { formatProductPrice, getProductBadges } from './product-display';
import { useCompareList } from './use-compare-list';
import './category-page.css';
import './compare-page.css';

const MISSING_VALUE_TEXT = 'Chưa có thông tin';
const HEAD_IMAGE_WIDTH = 200;
const HEAD_IMAGE_SIZES = '96px';
// Cắt viền trơn quanh ảnh, để máy trong ảnh có khung xám vẫn lấp đầy ô đầu cột.
const TRIMMED = { trim: true };

function Breadcrumb() {
  return (
    <nav className="catalog-crumbs" aria-label="Đường dẫn">
      <ol>
        <li>
          <Link to="/">Trang chủ</Link>
        </li>
        <li>
          <Icon name="chevronRight" className="icon catalog-crumbs-separator" />
          <span aria-current="page">So sánh sản phẩm</span>
        </li>
      </ol>
    </nav>
  );
}

function EmptyState({ removedCount }: { removedCount: number }) {
  return (
    <>
      {removedCount > 0 && <RemovedNotice count={removedCount} />}
      <div className="card compare-empty">
        <p>Chưa có sản phẩm nào để so sánh</p>
        <Link to="/" className="btn btn-primary">
          Về trang chủ
        </Link>
      </div>
    </>
  );
}

function RemovedNotice({ count }: { count: number }) {
  return (
    <p role="status" className="muted compare-notice">
      {count} sản phẩm không còn hiển thị đã được bỏ khỏi danh sách.
    </p>
  );
}

function LoadingState() {
  return (
    <div aria-busy="true">
      <p role="status" className="sr">
        Đang tải bảng so sánh...
      </p>
      <div className="table-wrap compare-skeleton" aria-hidden="true" />
    </div>
  );
}

interface ProductColumnHeadProps {
  product: ComparedProduct;
  onRemove: (productId: string) => void;
}

function ProductColumnHead({ product, onRemove }: ProductColumnHeadProps) {
  const { summary } = product;
  const badges = getProductBadges(summary);
  return (
    <th scope="col" className="compare-product">
      <div className="compare-product-media">
        <ProductImage
          src={
            summary.image
              ? withCloudinaryTransform(summary.image.url, HEAD_IMAGE_WIDTH, TRIMMED)
              : null
          }
          srcSet={summary.image ? buildCloudinarySrcSet(summary.image.url, TRIMMED) : undefined}
          sizes={HEAD_IMAGE_SIZES}
          alt={summary.image ? summary.image.alt || summary.name : summary.name}
          width={96}
          height={96}
          loading="lazy"
        />
      </div>
      <Link to={`/p/${summary.slug}`} className="compare-product-name">
        {summary.name}
      </Link>
      <p className="compare-product-price">{formatProductPrice(summary.minPrice)}</p>
      {badges.length > 0 && (
        <div className="compare-product-badges">
          {badges.map((badge) => (
            <span key={badge.label} className={badge.className}>
              {badge.label}
            </span>
          ))}
        </div>
      )}
      <button type="button" className="btn btn-sm" onClick={() => onRemove(summary.id)}>
        Bỏ khỏi so sánh
      </button>
    </th>
  );
}

export function ComparePage() {
  const { list, remove, removeMany, clear } = useCompareList();
  const ids = list.productIds;
  const idsKey = ids.join(',');
  const [onlyDifferences, setOnlyDifferences] = useState(false);
  const [removedCount, setRemovedCount] = useState(0);

  const comparison = useQuery<ProductCompareResponse, ApiError>({
    queryKey: ['compare', idsKey],
    // Id gửi đúng thứ tự trong danh sách, và API giữ thứ tự đó, nên cột khớp thứ tự khách đã thêm.
    queryFn: () => apiRequest(`/api/products/compare?ids=${idsKey}`),
    // Danh sách rỗng thì không có gì để so sánh, nên không gọi API.
    enabled: ids.length > 0,
    // Giữ bảng cũ trong lúc tải bảng mới, để bố cục không nhảy khi khách bỏ một sản phẩm.
    placeholderData: keepPreviousData,
  });
  const data = comparison.data;
  const isFresh = data !== undefined && !comparison.isPlaceholderData;

  useEffect(() => {
    if (!data || !isFresh) return;
    // API bỏ sản phẩm bị ẩn hoặc không tồn tại. Bỏ các id đó khỏi danh sách để lần sau không gửi lại.
    const missingIds = findMissingIds(ids, data.products);
    if (missingIds.length === 0) return;
    removeMany(missingIds);
    setRemovedCount((count) => count + missingIds.length);
  }, [data, isFresh, ids, removeMany]);

  function renderBody() {
    if (ids.length === 0) return <EmptyState removedCount={removedCount} />;

    if (comparison.isError) {
      return (
        <>
          <ErrorAlert message={comparison.error.message} className="compare-alert" />
          {comparison.error.code === 'COMPARE_CATEGORY_MISMATCH' && (
            // Sản phẩm đã đổi danh mục nên danh sách cũ không còn so sánh được. Khách phải bắt đầu lại.
            <button type="button" className="btn compare-alert-action" onClick={clear}>
              Xóa danh sách so sánh
            </button>
          )}
        </>
      );
    }

    if (!data) return <LoadingState />;

    // Chỉ vẽ sản phẩm còn trong danh sách, theo thứ tự danh sách. Bảng cũ đang hiện không giữ cột vừa bỏ.
    const shownProducts = ids
      .map((id) => data.products.find((product) => product.summary.id === id))
      .filter((product) => product !== undefined);
    if (shownProducts.length === 0) return <LoadingState />;

    const groups = buildCompareGroups(data.attributeGroups, shownProducts);
    const visibleGroups = groups
      .map((group) => ({
        ...group,
        rows: onlyDifferences ? group.rows.filter((row) => row.differs) : group.rows,
      }))
      .filter((group) => group.rows.length > 0);
    const columnCount = shownProducts.length + 1;

    return (
      <>
        {removedCount > 0 && <RemovedNotice count={removedCount} />}
        <label className="check compare-toggle">
          <input
            type="checkbox"
            checked={onlyDifferences}
            onChange={(event) => setOnlyDifferences(event.target.checked)}
          />
          Chỉ hiện điểm khác nhau
        </label>
        <div className="table-wrap compare-wrap">
          <table className="compare-table">
            <caption className="sr">So sánh thông số của {shownProducts.length} sản phẩm</caption>
            <thead>
              <tr>
                <th scope="col" className="compare-corner">
                  <span className="sr">Thông số</span>
                </th>
                {shownProducts.map((product) => (
                  <ProductColumnHead key={product.summary.id} product={product} onRemove={remove} />
                ))}
              </tr>
            </thead>
            {visibleGroups.map((group) => (
              <tbody key={group.name}>
                <tr className="compare-group">
                  <th scope="rowgroup" colSpan={columnCount}>
                    <span>{group.name}</span>
                  </th>
                </tr>
                {group.rows.map((row) => (
                  <tr key={row.key} className={row.differs ? 'is-diff' : undefined}>
                    <th scope="row">
                      <span>{row.label}</span>
                      {/* Nhãn chữ đi cùng màu nền, để khách không phân biệt màu và trình đọc màn hình vẫn nhận ra dòng khác nhau. */}
                      {row.differs && <span className="badge badge-warn">Khác nhau</span>}
                    </th>
                    {row.cells.map((cell, index) => (
                      <td key={shownProducts[index]?.summary.id ?? index}>
                        {cell ?? <span className="muted">{MISSING_VALUE_TEXT}</span>}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            ))}
          </table>
        </div>
        {onlyDifferences && visibleGroups.length === 0 && (
          <p className="muted compare-notice">Không có điểm khác nhau.</p>
        )}
      </>
    );
  }

  return (
    <main className="catalog-page compare-page">
      <Breadcrumb />
      <header className="compare-head">
        <h1>So sánh sản phẩm</h1>
        {data?.category && ids.length > 0 && <p className="muted">{data.category.name}</p>}
      </header>
      {renderBody()}
    </main>
  );
}
