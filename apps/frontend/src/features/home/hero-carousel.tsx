import { useEffect, useRef, useState, type FocusEvent, type KeyboardEvent } from 'react';
import { Link } from 'react-router-dom';
import { Icon } from '../../shared/icons';
import { formatProductPrice } from '../catalog/customer/product-display';
import { AUTOPLAY_INTERVAL_MS, getTabIndexForKey, wrapSlideIndex } from './carousel-index';
import { getCategoryVisual, type HeroSlideContent } from './home-content';
import { useCategorySummary } from './home-queries';

const REDUCED_MOTION_QUERY = '(prefers-reduced-motion: reduce)';

function prefersReducedMotion(): boolean {
  // jsdom và trình duyệt cũ không có `matchMedia`. Khi đó banner tự chuyển như mặc định.
  return window.matchMedia?.(REDUCED_MOTION_QUERY).matches ?? false;
}

function slideElementId(index: number): string {
  return `hero-slide-${index}`;
}

function tabElementId(index: number): string {
  return `hero-tab-${index}`;
}

export function HeroCarousel({ slides }: { slides: HeroSlideContent[] }) {
  const [activeIndex, setActiveIndex] = useState(0);
  // Khách bật giảm chuyển động thì banner đứng yên từ đầu. Nút phát vẫn cho khách tự bật lại.
  const [isPausedByUser, setIsPausedByUser] = useState(prefersReducedMotion);
  const [isHovered, setIsHovered] = useState(false);
  const [hasFocusWithin, setHasFocusWithin] = useState(false);
  const tabRefs = useRef<(HTMLButtonElement | null)[]>([]);

  const slideCount = slides.length;
  // Danh sách banner có thể ngắn lại khi cây danh mục tải xong. Chỉ số cũ không được trỏ ra ngoài danh sách.
  const visibleIndex = Math.min(activeIndex, Math.max(slideCount - 1, 0));
  // Dừng khi khách đang rê chuột hoặc đang thao tác bàn phím. Banner không đổi dưới tay khách (WCAG 2.2.2).
  const isRotating = slideCount > 1 && !isPausedByUser && !isHovered && !hasFocusWithin;

  useEffect(() => {
    if (!isRotating) return;
    const timerId = window.setInterval(() => {
      setActiveIndex((currentIndex) => wrapSlideIndex(currentIndex + 1, slideCount));
    }, AUTOPLAY_INTERVAL_MS);
    // Hủy hẹn giờ khi dừng hoặc khi rời trang, để không đổi state của component đã gỡ.
    return () => window.clearInterval(timerId);
  }, [isRotating, slideCount]);

  if (slideCount === 0) return null;

  function showSlide(index: number) {
    setActiveIndex(wrapSlideIndex(index, slideCount));
  }

  function handleTabKeyDown(event: KeyboardEvent<HTMLButtonElement>) {
    const nextIndex = getTabIndexForKey(event.key, visibleIndex, slideCount);
    if (nextIndex === null) return;
    // Chặn cuộn trang bằng phím mũi tên khi focus đang ở tab.
    event.preventDefault();
    showSlide(nextIndex);
    // Focus đi theo tab được chọn, để trình đọc màn hình đọc tên tab mới.
    tabRefs.current[nextIndex]?.focus();
  }

  function handleBlur(event: FocusEvent<HTMLElement>) {
    // Focus chuyển giữa các nút trong carousel không tính là rời carousel.
    if (event.currentTarget.contains(event.relatedTarget)) return;
    setHasFocusWithin(false);
  }

  return (
    <section
      className="hero-carousel"
      aria-roledescription="carousel"
      aria-label="Banner danh mục"
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
      onFocus={() => setHasFocusWithin(true)}
      onBlur={handleBlur}
    >
      <div className="hero-carousel-viewport">
        {/* Banner tự chuyển thì không đọc từng lần đổi. Khách tự đổi thì trình đọc màn hình báo banner mới. */}
        <div id="hero-slides" className="hero-slides" aria-live={isRotating ? 'off' : 'polite'}>
          {slides.map((slide, index) => (
            <HeroSlide
              key={slide.categorySlug}
              slide={slide}
              index={index}
              slideCount={slideCount}
              isActive={index === visibleIndex}
            />
          ))}
        </div>

        {slideCount > 1 && (
          <>
            <button
              type="button"
              className="hero-carousel-arrow hero-carousel-prev"
              aria-label="Banner trước"
              aria-controls="hero-slides"
              onClick={() => showSlide(visibleIndex - 1)}
            >
              <Icon name="chevronLeft" />
            </button>
            <button
              type="button"
              className="hero-carousel-arrow hero-carousel-next"
              aria-label="Banner tiếp theo"
              aria-controls="hero-slides"
              onClick={() => showSlide(visibleIndex + 1)}
            >
              <Icon name="chevronRight" />
            </button>
            <button
              type="button"
              className="hero-carousel-toggle"
              aria-label={isPausedByUser ? 'Bật tự chuyển banner' : 'Dừng tự chuyển banner'}
              onClick={() => setIsPausedByUser((isPaused) => !isPaused)}
            >
              <Icon name={isPausedByUser ? 'play' : 'pause'} />
            </button>
          </>
        )}
      </div>

      {slideCount > 1 && (
        <div className="hero-carousel-tabs" role="tablist" aria-label="Chọn banner">
          {slides.map((slide, index) => {
            const isSelected = index === visibleIndex;
            return (
              <button
                key={slide.categorySlug}
                ref={(element) => {
                  tabRefs.current[index] = element;
                }}
                id={tabElementId(index)}
                type="button"
                role="tab"
                aria-selected={isSelected}
                aria-controls={slideElementId(index)}
                // Chỉ tab đang chọn nhận Tab. Các tab còn lại đi bằng phím mũi tên theo mẫu tablist.
                tabIndex={isSelected ? 0 : -1}
                onClick={() => showSlide(index)}
                onKeyDown={handleTabKeyDown}
              >
                {slide.tabLabel}
              </button>
            );
          })}
        </div>
      )}
    </section>
  );
}

interface HeroSlideProps {
  slide: HeroSlideContent;
  index: number;
  slideCount: number;
  isActive: boolean;
}

function HeroSlide({ slide, index, slideCount, isActive }: HeroSlideProps) {
  const summary = useCategorySummary(slide.categorySlug);
  const visual = getCategoryVisual(slide.categorySlug);
  const fromPrice = summary.data?.fromPrice ?? null;

  return (
    <div
      id={slideElementId(index)}
      role="tabpanel"
      aria-roledescription="banner"
      aria-label={`${index + 1} trên ${slideCount}`}
      // Banner ẩn vẫn nằm trong DOM để chuyển mờ dần. `inert` bỏ banner ẩn khỏi thứ tự Tab và cây truy cập.
      inert={!isActive}
      className={
        isActive
          ? `hero-slide hero-tone-${slide.tone} is-active`
          : `hero-slide hero-tone-${slide.tone}`
      }
    >
      <Link to={`/c/${slide.categorySlug}`} className="hero-slide-link">
        <div className="hero-slide-copy">
          <h2 className="hero-slide-title">{slide.title}</h2>
          <p className="hero-slide-text">{slide.text}</p>
          {/* Dòng giá giữ chỗ khi đang tải, để nút bên dưới không nhảy khi giá về. */}
          <p className="hero-slide-price">
            {fromPrice !== null && (
              <>
                Giá chỉ từ <strong>{formatProductPrice(fromPrice)}</strong>
              </>
            )}
          </p>
          <span className="btn hero-slide-cta">
            {slide.ctaLabel}
            <Icon name="arrowRight" />
          </span>
        </div>
        {visual.artSrc && (
          <img
            className="hero-slide-art"
            src={visual.artSrc}
            alt=""
            width={400}
            height={400}
            // Banner đầu nằm trên màn hình đầu tiên. Các banner sau tải chậm để không tranh băng thông.
            loading={index === 0 ? 'eager' : 'lazy'}
          />
        )}
      </Link>
    </div>
  );
}
