import type { ProductDetail } from '../catalog/customer/catalog-api';
import {
  useEffect,
  useRef,
  useState,
  type FocusEvent,
  type KeyboardEvent,
  type PointerEvent,
} from 'react';
import { Link } from 'react-router-dom';
import { Icon } from '../../shared/icons';
import { formatProductPrice } from '../catalog/customer/product-display';
import { withCloudinaryTransform } from '../catalog/shared/image-url';
import { BannerIcon } from './banner-icons';
import { AUTOPLAY_INTERVAL_MS, getTabIndexForKey, wrapSlideIndex } from './carousel-index';
import type { BannerContent, BannerTheme } from './home-banners';
import { useHomeProductDetails } from './home-queries';
import { prefersReducedMotion } from './motion';

// Ảnh máy chiếm khoảng nửa banner rộng 1200px. Ảnh rộng hơn khung để nét trên màn mật độ cao.
const CUTOUT_IMAGE_WIDTH = 900;
const TAB_THUMB_WIDTH = 120;
// Đồng hồ đếm ngược chỉ hiện ngày, giờ, phút. Cập nhật mỗi 30 giây là đủ để phút không trễ.
const COUNTDOWN_TICK_MS = 30_000;
const MS_PER_MINUTE = 60_000;
const MS_PER_HOUR = 3_600_000;
const MS_PER_DAY = 86_400_000;

function slideElementId(bannerId: string): string {
  return `banner-slide-${bannerId}`;
}

export function BannerCarousel({ banners }: { banners: BannerContent[] }) {
  const products = useHomeProductDetails();
  // Bỏ banner chỉ khi danh sách tải xong: slug thiếu không được dẫn tới trang không tồn tại.
  const visibleBanners = products.isSuccess
    ? banners.filter((banner) => !banner.productSlug || products.data[banner.productSlug])
    : banners;

  const [activeIndex, setActiveIndex] = useState(0);
  // Khách bật giảm chuyển động thì banner đứng yên từ đầu. Nút phát vẫn cho khách tự bật lại.
  const [isPausedByUser, setIsPausedByUser] = useState(prefersReducedMotion);
  const [isHovered, setIsHovered] = useState(false);
  const [hasFocusWithin, setHasFocusWithin] = useState(false);
  const stageRef = useRef<HTMLDivElement>(null);
  const tabListRef = useRef<HTMLDivElement>(null);
  const tabRefs = useRef<(HTMLButtonElement | null)[]>([]);
  const progressRefs = useRef<(HTMLSpanElement | null)[]>([]);

  const slideCount = visibleBanners.length;
  // Danh sách có thể ngắn lại khi một sản phẩm bị xóa. Chỉ số cũ không được trỏ ra ngoài danh sách.
  const visibleIndex = Math.min(activeIndex, Math.max(slideCount - 1, 0));
  // Dừng khi khách đang rê chuột hoặc đang thao tác bàn phím. Banner không đổi dưới tay khách (WCAG 2.2.2).
  const isRotating = slideCount > 1 && !isPausedByUser && !isHovered && !hasFocusWithin;

  useEffect(() => {
    if (!isRotating) return;
    // Hẹn giờ mới luôn đếm lại từ đầu. Thanh tiến trình cũng chạy lại từ đầu để khớp lúc đổi banner.
    for (const animation of progressRefs.current[visibleIndex]?.getAnimations?.() ?? []) {
      animation.currentTime = 0;
    }
    const timerId = window.setInterval(() => {
      setActiveIndex((currentIndex) => wrapSlideIndex(currentIndex + 1, slideCount));
    }, AUTOPLAY_INTERVAL_MS);
    // Hủy hẹn giờ khi dừng hoặc khi rời trang, để không đổi state của component đã gỡ.
    return () => window.clearInterval(timerId);
  }, [isRotating, slideCount, visibleIndex]);

  useEffect(() => {
    const tabList = tabListRef.current;
    const activeTab = tabRefs.current[visibleIndex];
    // jsdom không có `scrollTo`. Dải tab vẫn bấm được, chỉ không tự cuộn.
    if (!tabList || !activeTab || typeof tabList.scrollTo !== 'function') return;
    // Cuộn riêng dải tab để tab đang chọn luôn nằm trong khung.
    // `scrollIntoView` cũng cuộn cả trang, nên khách đang đọc phía dưới sẽ bị kéo lên đầu trang.
    const tabStart = activeTab.offsetLeft;
    const tabEnd = tabStart + activeTab.offsetWidth;
    const viewStart = tabList.scrollLeft;
    const viewEnd = viewStart + tabList.clientWidth;
    if (tabStart >= viewStart && tabEnd <= viewEnd) return;
    const nextScrollLeft = tabStart < viewStart ? tabStart : tabEnd - tabList.clientWidth;
    tabList.scrollTo({
      left: nextScrollLeft,
      behavior: prefersReducedMotion() ? 'auto' : 'smooth',
    });
  }, [visibleIndex]);

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

  function handlePointerMove(event: PointerEvent<HTMLDivElement>) {
    // Chỉ chuột tạo chiều sâu. Ngón tay trên màn cảm ứng không có trạng thái rê.
    if (event.pointerType !== 'mouse') return;
    const stage = stageRef.current;
    if (!stage) return;
    const bounds = stage.getBoundingClientRect();
    // Đổi vị trí chuột về khoảng [-1, 1] quanh tâm banner. CSS nhân giá trị này với độ sâu của từng ảnh.
    const pointerX = ((event.clientX - bounds.left) / bounds.width) * 2 - 1;
    const pointerY = ((event.clientY - bounds.top) / bounds.height) * 2 - 1;
    stage.style.setProperty('--pointer-x', pointerX.toFixed(3));
    stage.style.setProperty('--pointer-y', pointerY.toFixed(3));
  }

  function handlePointerLeave() {
    // Đưa ảnh về giữa khi chuột rời banner, để máy không đứng lệch.
    stageRef.current?.style.setProperty('--pointer-x', '0');
    stageRef.current?.style.setProperty('--pointer-y', '0');
  }

  return (
    <section
      className="banner-carousel"
      aria-roledescription="carousel"
      aria-label="Sản phẩm nổi bật"
      // CSS dùng thuộc tính này để dừng thanh tiến trình cùng lúc với hẹn giờ.
      data-rotating={isRotating}
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
      onFocus={() => setHasFocusWithin(true)}
      onBlur={handleBlur}
    >
      <div
        ref={stageRef}
        className="banner-stage"
        onPointerMove={handlePointerMove}
        onPointerLeave={handlePointerLeave}
      >
        {/* Banner tự chuyển thì không đọc từng lần đổi. Khách tự đổi thì trình đọc màn hình báo banner mới. */}
        <div id="banner-slides" className="banner-slides" aria-live={isRotating ? 'off' : 'polite'}>
          {visibleBanners.map((banner, index) => (
            <BannerSlide
              key={banner.id}
              banner={banner}
              product={banner.productSlug ? products.data?.[banner.productSlug] : undefined}
              index={index}
              slideCount={slideCount}
              isActive={index === visibleIndex}
            />
          ))}
        </div>

        {slideCount > 1 && (
          <div className="banner-controls">
            <button
              type="button"
              className="banner-control"
              aria-label="Banner trước"
              aria-controls="banner-slides"
              onClick={() => showSlide(visibleIndex - 1)}
            >
              <Icon name="chevronLeft" />
            </button>
            <button
              type="button"
              className="banner-control"
              aria-label={isPausedByUser ? 'Bật tự chuyển banner' : 'Dừng tự chuyển banner'}
              onClick={() => setIsPausedByUser((isPaused) => !isPaused)}
            >
              <Icon name={isPausedByUser ? 'play' : 'pause'} />
            </button>
            <button
              type="button"
              className="banner-control"
              aria-label="Banner tiếp theo"
              aria-controls="banner-slides"
              onClick={() => showSlide(visibleIndex + 1)}
            >
              <Icon name="chevronRight" />
            </button>
          </div>
        )}
      </div>

      {slideCount > 1 && (
        <div ref={tabListRef} className="banner-tabs" role="tablist" aria-label="Chọn banner">
          {visibleBanners.map((banner, index) => {
            const isSelected = index === visibleIndex;
            return (
              <button
                key={banner.id}
                ref={(element) => {
                  tabRefs.current[index] = element;
                }}
                type="button"
                role="tab"
                className="banner-tab"
                aria-selected={isSelected}
                aria-controls={slideElementId(banner.id)}
                // Chỉ tab đang chọn nhận Tab. Các tab còn lại đi bằng phím mũi tên theo mẫu tablist.
                tabIndex={isSelected ? 0 : -1}
                onClick={() => showSlide(index)}
                onKeyDown={handleTabKeyDown}
              >
                {/* Thanh chạy hết trong một chu kỳ tự chuyển, để khách biết khi nào banner đổi. */}
                <span
                  ref={(element) => {
                    progressRefs.current[index] = element;
                  }}
                  className="banner-tab-progress"
                  aria-hidden="true"
                />
                <img
                  className="banner-tab-thumb"
                  src={withCloudinaryTransform(banner.tabThumbSrc, TAB_THUMB_WIDTH)}
                  alt=""
                  width={42}
                  height={42}
                  loading="lazy"
                />
                <span className="banner-tab-text">
                  <span className="banner-tab-name">{banner.tabName}</span>
                  <span className="banner-tab-note">
                    {banner.tabNote}
                    {banner.launchNote && <span className="banner-tab-new">Mới</span>}
                  </span>
                </span>
              </button>
            );
          })}
        </div>
      )}
    </section>
  );
}

interface BannerSlideProps {
  banner: BannerContent;
  index: number;
  slideCount: number;
  isActive: boolean;
  product?: ProductDetail;
}

function BannerSlide({ banner, index, slideCount, isActive, product }: BannerSlideProps) {
  const themeClass = `banner-${banner.theme}`;
  const className = [
    'banner-slide',
    themeClass,
    banner.isDark ? 'is-dark' : '',
    isActive ? 'is-active' : '',
  ]
    .filter(Boolean)
    .join(' ');

  return (
    <article
      id={slideElementId(banner.id)}
      role="tabpanel"
      aria-roledescription="banner"
      aria-label={`${index + 1} trên ${slideCount}: ${banner.tabName}`}
      // Banner ẩn vẫn nằm trong DOM để chuyển mờ dần. `inert` bỏ banner ẩn khỏi thứ tự Tab và cây truy cập.
      inert={!isActive}
      className={className}
    >
      <div className="banner-backdrop" aria-hidden="true">
        {banner.scenePhotoSrc && (
          <img
            className="banner-scene"
            src={banner.scenePhotoSrc}
            alt=""
            // Banner đầu nằm trên màn hình đầu tiên. Các banner sau tải chậm để không tranh băng thông.
            loading={index === 0 ? 'eager' : 'lazy'}
          />
        )}
        <BannerDecor theme={banner.theme} />
        {/* Ảnh cảnh của hãng đã có bối cảnh riêng. Logo mờ chỉ in sau ảnh máy tách nền. */}
        {!banner.scenePhotoSrc && (
          <span className={`banner-watermark banner-logo-${banner.brand}`} />
        )}
      </div>

      <div className="banner-copy">
        {banner.launchNote && <p className="banner-new">{banner.launchNote}</p>}
        <span
          className={`banner-logo banner-logo-${banner.brand}`}
          role="img"
          aria-label={banner.brandName}
        />
        <h2 className="banner-title">
          {banner.title}
          {banner.subtitle && <small>{banner.subtitle}</small>}
        </h2>
        <p className="banner-tagline">{banner.tagline}</p>
        <ul className="banner-specs">
          {banner.specs.map((spec) => (
            <li key={spec.label}>
              <BannerIcon name={spec.icon} />
              <span>
                <small>{spec.label}</small>
                <b>{spec.value}</b>
              </span>
            </li>
          ))}
        </ul>
        {banner.swatches.length > 0 && (
          <p className="banner-colors">
            {banner.swatches.map((swatch) => (
              <span
                key={swatch}
                className={`banner-swatch banner-swatch-${swatch}`}
                aria-hidden="true"
              />
            ))}
            {banner.colorNote}
          </p>
        )}
        {banner.productSlug ? (
          <CatalogBuyRow banner={banner} productSlug={banner.productSlug} product={product} />
        ) : (
          <UpcomingBuyRow banner={banner} />
        )}
      </div>

      <div className="banner-art" aria-hidden="true">
        {banner.cutoutSrcs.map((src, imageIndex) => (
          <img
            key={src}
            src={withCloudinaryTransform(src, CUTOUT_IMAGE_WIDTH)}
            alt=""
            loading={index === 0 && imageIndex === 0 ? 'eager' : 'lazy'}
          />
        ))}
      </div>
    </article>
  );
}

// Máy đã bán tại Pickory: giá và bảo hành lấy từ catalog, nút mở trang chi tiết.
function CatalogBuyRow({
  banner,
  productSlug,
  product,
}: {
  banner: BannerContent;
  productSlug: string;
  product?: ProductDetail;
}) {
  return (
    <div className="banner-buy">
      <Link to={`/p/${productSlug}`} className="banner-cta">
        Xem sản phẩm
        <Icon name="arrowRight" />
      </Link>
      {/* Dòng giá giữ chỗ khi đang tải, để nút bên cạnh không nhảy khi giá về. */}
      <p className="banner-price">
        <small>
          {banner.priceLabel}
          {/* Bảo hành đi cùng dòng giá, để banner tên dài hai dòng vẫn không tràn khung. */}
          {product && product.warrantyMonths > 0 && <> · Bảo hành {product.warrantyMonths} tháng</>}
        </small>
        <b>{product ? formatProductPrice(product.minPrice) : ' '}</b>
      </p>
    </div>
  );
}

// Máy chưa có trong catalog: hiện giá niêm yết của hãng và nhãn "Sắp có", không có nút mua.
function UpcomingBuyRow({ banner }: { banner: BannerContent }) {
  return (
    <div className="banner-buy">
      {banner.preorderAt && <PreorderCountdown targetIso={banner.preorderAt} />}
      <p className="banner-price">
        <small>{banner.priceLabel}</small>
        <b>{formatProductPrice(banner.listedPrice)}</b>
      </p>
      <p className="banner-soon">
        <BannerIcon name="clock" />
        Sắp có tại Pickory
      </p>
    </div>
  );
}

function PreorderCountdown({ targetIso }: { targetIso: string }) {
  const targetTime = new Date(targetIso).getTime();
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timerId = window.setInterval(() => setNow(Date.now()), COUNTDOWN_TICK_MS);
    return () => window.clearInterval(timerId);
  }, []);

  const remaining = targetTime - now;
  // Đã tới ngày đặt trước thì bỏ đồng hồ. Đồng hồ đứng ở số 0 làm khách tưởng trang bị lỗi.
  if (remaining <= 0) return null;
  const days = Math.floor(remaining / MS_PER_DAY);
  const hours = Math.floor((remaining % MS_PER_DAY) / MS_PER_HOUR);
  const minutes = Math.floor((remaining % MS_PER_HOUR) / MS_PER_MINUTE);

  return (
    <p
      className="banner-countdown"
      aria-label={`Còn ${days} ngày ${hours} giờ ${minutes} phút tới ngày đặt trước`}
    >
      <span>
        <b>{days}</b>ngày
      </span>
      <span>
        <b>{String(hours).padStart(2, '0')}</b>giờ
      </span>
      <span>
        <b>{String(minutes).padStart(2, '0')}</b>phút
      </span>
    </p>
  );
}

// Họa tiết nền của từng máy: ánh sáng, đường cong, cánh hoa. Chỉ trang trí, nên nằm trong lớp `aria-hidden`.
function BannerDecor({ theme }: { theme: BannerTheme }) {
  switch (theme) {
    case 'duo':
      return (
        <>
          <span className="banner-glow" />
          <span className="banner-dune" />
          <span className="banner-dune banner-dune-far" />
        </>
      );
    case 's26':
      return (
        <>
          <span className="banner-glow" />
          <span className="banner-arc" />
          <span className="banner-arc banner-arc-far" />
        </>
      );
    case 'fold':
      return (
        <>
          {Array.from({ length: 5 }, (_, petalIndex) => (
            <span key={petalIndex} className="banner-petal" />
          ))}
        </>
      );
    case 'flip':
      return (
        <>
          <span className="banner-glow" />
          <span className="banner-glow banner-glow-soft" />
          <svg className="banner-silk" viewBox="0 0 1200 500" preserveAspectRatio="none">
            <path d="M500 520C640 360 760 300 1220 260" />
            <path d="M540 520C690 380 820 330 1220 300" />
            <path d="M600 520C760 420 900 380 1220 350" />
            <path d="M420 -20C620 120 820 140 1220 60" />
          </svg>
        </>
      );
    case 'rog':
      return (
        <>
          <span className="banner-glow" />
          <span className="banner-glow banner-glow-soft" />
          <span className="banner-slash" />
          <span className="banner-slash banner-slash-accent" />
          <span className="banner-slash banner-slash-far" />
        </>
      );
    case 'sony':
      return (
        <>
          <span className="banner-glow" />
          <span className="banner-rings">
            <span />
            <span />
            <span />
            <span />
          </span>
        </>
      );
    case 'xiaomi':
      return (
        <>
          <span className="banner-beam" />
          <span className="banner-glow" />
        </>
      );
    case 'dell':
      return (
        <>
          <span className="banner-glow" />
          <span className="banner-grid" />
        </>
      );
    default:
      return null;
  }
}
