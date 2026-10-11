import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ErrorAlert } from '../../shared/error-alert';
import { Icon } from '../../shared/icons';
import type { CategoryNode } from '../catalog/customer/catalog-api';
import { ProductCard } from '../catalog/customer/product-card';
import { useRailProducts } from './home-queries';

// Số thẻ giữ chỗ khi đang tải. Bằng số thẻ hiện trên một hàng ở màn rộng.
const SKELETON_CARD_COUNT = 5;
// Mỗi lần bấm nút cuộn gần hết một khung. Thẻ cuối của khung cũ vẫn hiện để khách không mất mạch.
const SCROLL_STEP_RATIO = 0.85;
// Sai số làm tròn của `scrollLeft` trên màn có tỉ lệ điểm ảnh lẻ.
const SCROLL_EDGE_TOLERANCE_PX = 2;

export function ProductRail({ category }: { category: CategoryNode }) {
  const products = useRailProducts(category.slug);
  const headingId = `rail-${category.slug}`;

  // Danh mục chưa có sản phẩm thì bỏ cả dải. Một dải trống chỉ làm khách cuộn thêm.
  if (products.isSuccess && products.data.length === 0) return null;

  return (
    <section className="home-section" aria-labelledby={headingId}>
      <div className="home-section-head">
        <h2 id={headingId}>{category.name}</h2>
        <Link to={`/c/${category.slug}`} className="home-section-more">
          Xem tất cả
          <span className="sr"> {category.name}</span>
          <Icon name="arrowRight" />
        </Link>
      </div>

      {products.isPending && <RailSkeleton />}
      {products.isError && (
        <ErrorAlert message={`Không tải được sản phẩm ${category.name.toLowerCase()}.`} />
      )}
      {products.isSuccess && (
        <RailTrack label={`Sản phẩm ${category.name.toLowerCase()}`}>
          {products.data.map((product) => (
            <ProductCard key={product.id} product={product} />
          ))}
        </RailTrack>
      )}
    </section>
  );
}

function RailSkeleton() {
  return (
    <div className="rail-track" aria-hidden="true">
      {Array.from({ length: SKELETON_CARD_COUNT }, (_, index) => (
        <div key={index} className="rail-skeleton-card" />
      ))}
    </div>
  );
}

interface RailTrackProps {
  label: string;
  children: ReactNode;
}

function RailTrack({ label, children }: RailTrackProps) {
  const trackRef = useRef<HTMLDivElement>(null);
  const [canScrollBack, setCanScrollBack] = useState(false);
  const [canScrollForward, setCanScrollForward] = useState(false);

  const updateScrollButtons = useCallback(() => {
    const track = trackRef.current;
    if (!track) return;
    const maxScrollLeft = track.scrollWidth - track.clientWidth;
    setCanScrollBack(track.scrollLeft > SCROLL_EDGE_TOLERANCE_PX);
    setCanScrollForward(track.scrollLeft < maxScrollLeft - SCROLL_EDGE_TOLERANCE_PX);
  }, []);

  useEffect(() => {
    updateScrollButtons();
    // Đổi cỡ cửa sổ làm đổi số thẻ trên một hàng, nên trạng thái nút cuộn phải tính lại.
    window.addEventListener('resize', updateScrollButtons);
    return () => window.removeEventListener('resize', updateScrollButtons);
  }, [updateScrollButtons]);

  function scrollByStep(direction: 1 | -1) {
    const track = trackRef.current;
    if (!track) return;
    track.scrollBy({ left: direction * track.clientWidth * SCROLL_STEP_RATIO, behavior: 'smooth' });
  }

  return (
    <div className="rail">
      {/* Nút cuộn chỉ phục vụ chuột. Bàn phím và màn cảm ứng cuộn thẳng trên dải, nên nút không nhận Tab. */}
      <button
        type="button"
        className="rail-arrow rail-arrow-back"
        aria-hidden="true"
        tabIndex={-1}
        disabled={!canScrollBack}
        onClick={() => scrollByStep(-1)}
      >
        <Icon name="chevronLeft" />
      </button>
      <div
        ref={trackRef}
        className="rail-track"
        role="region"
        aria-label={label}
        // Dải cuộn ngang nhận focus, để người dùng bàn phím cuộn bằng phím mũi tên.
        tabIndex={0}
        onScroll={updateScrollButtons}
      >
        {children}
      </div>
      <button
        type="button"
        className="rail-arrow rail-arrow-forward"
        aria-hidden="true"
        tabIndex={-1}
        disabled={!canScrollForward}
        onClick={() => scrollByStep(1)}
      >
        <Icon name="chevronRight" />
      </button>
    </div>
  );
}
