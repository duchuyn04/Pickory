import { useCallback, useEffect, useRef, useState } from 'react';
import { Icon } from '../../../shared/icons';
import type { ProductListItem } from './catalog-api';
import { ProductCard } from './product-card';
import './product-rail.css';

// Số thẻ giữ chỗ khi đang tải, khớp năm thẻ hiển thị trên một khung màn máy tính.
const SKELETON_CARD_COUNT = 5;
// Khoảng cuộn dự phòng khi môi trường test không có kích thước khung nhìn thật.
const FALLBACK_SCROLL_STEP = 400;
// Sai số 1px tránh kẹt trạng thái nút do trình duyệt làm tròn độ rộng lẻ.
const SCROLL_EPSILON_PX = 1;

interface ProductRailProps {
  products: ProductListItem[];
  label: string;
  categoryId?: string;
}

// Dải sản phẩm cuộn ngang tái dùng cho trang chủ và khối sản phẩm cùng danh mục.
export function ProductRail({ products, label, categoryId }: ProductRailProps) {
  const trackRef = useRef<HTMLUListElement>(null);
  const [canScrollLeft, setCanScrollLeft] = useState(false);
  const [canScrollRight, setCanScrollRight] = useState(products.length > 1);

  const syncScrollState = useCallback(() => {
    const track = trackRef.current;
    if (!track) return;
    const { scrollLeft, clientWidth, scrollWidth } = track;
    setCanScrollLeft(scrollLeft > SCROLL_EPSILON_PX);
    // Khi không có layout thật trong môi trường test thì vẫn cho phép cuộn nếu có từ hai thẻ.
    if (clientWidth === 0) {
      setCanScrollRight(products.length > 1);
      return;
    }
    setCanScrollRight(scrollLeft + clientWidth < scrollWidth - SCROLL_EPSILON_PX);
  }, [products.length]);

  useEffect(() => {
    const track = trackRef.current;
    if (!track) return;
    // Đưa dải về đầu khi đổi danh sách sản phẩm để khách luôn thấy thẻ đầu tiên.
    track.scrollLeft = 0;
    syncScrollState();
    if (typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(syncScrollState);
    observer.observe(track);
    return () => observer.disconnect();
  }, [products, syncScrollState]);

  function scrollRail(direction: -1 | 1) {
    const track = trackRef.current;
    if (!track) return;
    // Cuộn đúng một khung nhìn để khách lướt qua trọn nhóm thẻ tiếp theo.
    const step = track.clientWidth > 0 ? track.clientWidth : FALLBACK_SCROLL_STEP;
    track.scrollBy({ left: direction * step, behavior: 'smooth' });
  }

  return (
    <div className="product-rail">
      <button
        type="button"
        className="product-rail-nav product-rail-nav-prev"
        aria-label="Cuộn sang trái"
        disabled={!canScrollLeft}
        onClick={() => scrollRail(-1)}
      >
        <Icon name="chevronLeft" />
      </button>

      <ul
        ref={trackRef}
        className="product-rail-track"
        aria-label={label}
        onScroll={syncScrollState}
      >
        {products.map((product) => (
          <li key={product.id} className="product-rail-item">
            <ProductCard product={product} categoryId={categoryId} />
          </li>
        ))}
      </ul>

      <button
        type="button"
        className="product-rail-nav product-rail-nav-next"
        aria-label="Cuộn sang phải"
        disabled={!canScrollRight}
        onClick={() => scrollRail(1)}
      >
        <Icon name="chevronRight" />
      </button>
    </div>
  );
}

// Khung giữ chỗ giữ đúng chiều rộng và chiều cao dải để bố cục không nhảy khi đang tải.
export function ProductRailSkeleton() {
  return (
    <div className="product-rail-track product-rail-skeleton" aria-hidden="true">
      {Array.from({ length: SKELETON_CARD_COUNT }, (_, index) => (
        <div key={index} className="product-rail-item product-rail-skeleton-card" />
      ))}
    </div>
  );
}
