import { useEffect, useRef, useState, type PointerEvent, type ReactNode } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Icon } from '../../shared/icons';
import type {
  BrandFacet,
  CategoryNode,
  ProductDetail,
  ProductSpecEntry,
} from '../catalog/customer/catalog-api';
import { formatProductPrice } from '../catalog/customer/product-display';
import { useCompareList } from '../catalog/customer/use-compare-list';
import { withCloudinaryTransform } from '../catalog/shared/image-url';
import { getBrandLogo } from './brand-logos';
import {
  CATEGORY_PROMOS,
  PRODUCT_DUEL,
  type CategoryPromoContent,
  type PromoEffect,
} from './home-content';
import { useCategorySummary, useProductDetail, useRailProducts } from './home-queries';
import { prefersReducedMotion, useRevealOnScroll } from './motion';
import { pickProductWithImage } from './pick-product';

// Ảnh máy chiếm khoảng 260px trên banner danh mục và 360px trên banner so sánh. Ảnh rộng gấp đôi để nét.
const PROMO_IMAGE_WIDTH = 560;
const DUEL_IMAGE_WIDTH = 720;
// Tên hãng trên banner danh mục đổi sau khoảng này. Đủ để đọc một tên mà dòng chữ vẫn có nhịp.
const BRAND_TICK_MS = 1800;
// Số cột sóng âm sau ảnh tai nghe. Mỗi cột có độ cao và nhịp riêng trong CSS.
const EQUALIZER_BAR_COUNT = 14;
// Thanh chia của banner so sánh không đi sát mép, để máy bên kia vẫn còn một phần trong khung.
const SPLIT_MIN_PERCENT = 22;
const SPLIT_MAX_PERCENT = 78;

// ---------- Cặp banner danh mục ----------

export function CategoryPromoPair({ categories }: { categories: CategoryNode[] }) {
  const sectionRef = useRevealOnScroll<HTMLElement>();
  // Chỉ giữ banner của danh mục còn tồn tại, để banner không dẫn tới trang "Không tìm thấy".
  const promos = CATEGORY_PROMOS.flatMap((promo) => {
    const category = categories.find((node) => node.slug === promo.categorySlug);
    return category ? [{ promo, category }] : [];
  });
  if (promos.length === 0) return null;

  return (
    <section ref={sectionRef} className="promo-pair reveal" aria-labelledby="home-promos-heading">
      {/* Tiêu đề ẩn giữ thứ bậc h2, h3 cho trình đọc màn hình. Hai banner đã nói rõ mình là gì. */}
      <h2 id="home-promos-heading" className="sr">
        Danh mục nổi bật
      </h2>
      {promos.map(({ promo, category }) => (
        <CategoryPromo key={promo.categorySlug} promo={promo} category={category} />
      ))}
    </section>
  );
}

function CategoryPromo({
  promo,
  category,
}: {
  promo: CategoryPromoContent;
  category: CategoryNode;
}) {
  const summary = useCategorySummary(category.slug);
  // Dùng chung khóa với lưới "Hàng mới về". Banner không tạo thêm request.
  const railProducts = useRailProducts(category.slug);
  const product = pickProductWithImage(railProducts.data, promo.productSlug);
  const categoryName = category.name.toLowerCase();

  return (
    <article className={`promo promo-${promo.effect}`}>
      <PromoBackdrop effect={promo.effect} />
      <div className="promo-copy">
        <h3 className="promo-title">{category.name}</h3>
        <p className="promo-text">{promo.text}</p>
        {summary.data && summary.data.brands.length > 0 && (
          <BrandTicker brands={summary.data.brands} />
        )}
        {/* Dòng số liệu giữ chỗ khi đang tải, để nút bên dưới không nhảy. */}
        <p className="promo-meta">
          {summary.data ? (
            <>
              {summary.data.productCount} mẫu
              {summary.data.fromPrice !== null && (
                <>
                  , giá từ <b>{formatProductPrice(summary.data.fromPrice)}</b>
                </>
              )}
            </>
          ) : (
            ' '
          )}
        </p>
        {/* Link phủ cả banner bằng lớp giả `::after`, nên bấm chỗ nào trên banner cũng mở danh mục. */}
        <Link to={`/c/${category.slug}`} className="promo-cta">
          Xem {categoryName}
          <Icon name="arrowRight" />
        </Link>
      </div>
      {product?.image && (
        <img
          className="promo-art"
          src={withCloudinaryTransform(product.image.url, PROMO_IMAGE_WIDTH, { trim: true })}
          alt=""
          width={PROMO_IMAGE_WIDTH}
          height={PROMO_IMAGE_WIDTH}
          loading="lazy"
        />
      )}
    </article>
  );
}

function PromoBackdrop({ effect }: { effect: PromoEffect }) {
  if (effect === 'shine') {
    return (
      <span className="promo-backdrop" aria-hidden="true">
        <span className="promo-grid" />
        <span className="promo-slash" />
        <span className="promo-shine" />
      </span>
    );
  }
  return (
    <span className="promo-backdrop" aria-hidden="true">
      <span className="promo-eq">
        {Array.from({ length: EQUALIZER_BAR_COUNT }, (_, index) => (
          <span key={index} />
        ))}
      </span>
    </span>
  );
}

// Tên các hãng trong danh mục lật lên lần lượt, như bảng tên ở quầy. Trình đọc màn hình đọc cả danh sách một lần.
function BrandTicker({ brands }: { brands: BrandFacet[] }) {
  const [visibleIndex, setVisibleIndex] = useState(0);
  const brandCount = brands.length;

  useEffect(() => {
    // Một hãng thì không có gì để lật. Khách bật giảm chuyển động thì tên đứng yên.
    if (brandCount < 2 || prefersReducedMotion()) return;
    const timerId = window.setInterval(() => {
      setVisibleIndex((index) => (index + 1) % brandCount);
    }, BRAND_TICK_MS);
    return () => window.clearInterval(timerId);
  }, [brandCount]);

  return (
    <p className="promo-brands">
      <span className="sr">Các hãng: {brands.map((brand) => brand.name).join(', ')}</span>
      <span aria-hidden="true">Có hãng</span>
      <span className="promo-brand-window" aria-hidden="true">
        {brands.map((brand, index) => (
          <span
            key={brand.slug}
            className={index === visibleIndex % brandCount ? 'promo-brand is-shown' : 'promo-brand'}
          >
            {brand.name}
          </span>
        ))}
      </span>
    </p>
  );
}

// ---------- Banner so sánh đối đầu ----------

// Thông số có ở cả hai máy, theo thứ tự của máy bên trái. Ô trống ở một bên thì dòng đó không giúp so sánh.
function pickSharedSpecs(left: ProductDetail, right: ProductDetail, limit: number) {
  const rightByKey = new Map(right.specs.map((spec) => [spec.key, spec]));
  return left.specs
    .filter((spec) => spec.value !== null && rightByKey.get(spec.key)?.value != null)
    .slice(0, limit)
    .map((spec) => ({ left: spec, right: rightByKey.get(spec.key) as ProductSpecEntry }));
}

function formatSpecValue(spec: ProductSpecEntry): string {
  if (spec.value === null) return '';
  if (typeof spec.value === 'boolean') return spec.value ? 'Có' : 'Không';
  // Số thập phân tiếng Việt dùng dấu phẩy: 6,3 inch.
  const text = typeof spec.value === 'number' ? spec.value.toLocaleString('vi-VN') : spec.value;
  return spec.unit ? `${text} ${spec.unit}` : text;
}

export function ProductDuel({ fallback }: { fallback: ReactNode }) {
  const left = useProductDetail(PRODUCT_DUEL.leftSlug);
  const right = useProductDetail(PRODUCT_DUEL.rightSlug);

  // Một trong hai máy không còn, hoặc hai máy khác danh mục: thay banner bằng dải mời so sánh chung.
  if (left.isError || right.isError) return <>{fallback}</>;
  if (!left.data || !right.data) return <div className="duel-skeleton" aria-hidden="true" />;
  if (left.data.category.id !== right.data.category.id) return <>{fallback}</>;

  return <ProductDuelBanner left={left.data} right={right.data} />;
}

function ProductDuelBanner({ left, right }: { left: ProductDetail; right: ProductDetail }) {
  const sectionRef = useRevealOnScroll<HTMLElement>();
  const stageRef = useRef<HTMLDivElement>(null);
  const [isSteering, setIsSteering] = useState(false);
  const navigate = useNavigate();
  const { startNew, add } = useCompareList();
  const rows = pickSharedSpecs(left, right, PRODUCT_DUEL.specRowLimit);

  function handlePointerMove(event: PointerEvent<HTMLDivElement>) {
    // Chỉ chuột kéo thanh chia. Trên màn cảm ứng banner tự đung đưa, khách vuốt trang như thường.
    if (event.pointerType !== 'mouse') return;
    const stage = stageRef.current;
    if (!stage) return;
    const bounds = stage.getBoundingClientRect();
    const rawPercent = ((event.clientX - bounds.left) / bounds.width) * 100;
    const splitPercent = Math.min(Math.max(rawPercent, SPLIT_MIN_PERCENT), SPLIT_MAX_PERCENT);
    // Độ nghiêng trong khoảng [-1, 1]: âm khi thanh chia lệch trái, tức máy bên phải chiếm nhiều chỗ hơn.
    const bias = (splitPercent - 50) / (SPLIT_MAX_PERCENT - 50);
    stage.style.setProperty('--split', `${splitPercent.toFixed(1)}%`);
    stage.style.setProperty('--duel-bias', bias.toFixed(3));
    setIsSteering(true);
  }

  function handlePointerLeave() {
    // Bỏ giá trị ghi bằng JS, để thanh chia quay lại nhịp đung đưa của CSS.
    stageRef.current?.style.removeProperty('--split');
    stageRef.current?.style.setProperty('--duel-bias', '0');
    setIsSteering(false);
  }

  function openComparison() {
    // Bảng so sánh chỉ nhận máy cùng danh mục. Bắt đầu danh sách mới với đúng hai máy này.
    startNew(left.category.id, left.id);
    add(left.category.id, right.id);
    navigate('/compare');
  }

  return (
    <section ref={sectionRef} className="duel reveal" aria-labelledby="home-duel-heading">
      <div className="duel-head">
        <h2 id="home-duel-heading" className="home-section-title">
          So sánh nhanh
        </h2>
        <p>Rê chuột trên ảnh để nghiêng về từng máy.</p>
      </div>

      <div
        ref={stageRef}
        className={isSteering ? 'duel-stage is-steering' : 'duel-stage'}
        aria-hidden="true"
        onPointerMove={handlePointerMove}
        onPointerLeave={handlePointerLeave}
      >
        <DuelSide product={left} side="left" />
        <DuelSide product={right} side="right" />
        <span className="duel-handle">
          <span>VS</span>
        </span>
      </div>

      <table className="duel-table">
        <caption className="sr">
          Thông số của {left.name} và {right.name}
        </caption>
        <thead>
          <tr>
            <th scope="col">
              <Link to={`/p/${left.slug}`}>{left.name}</Link>
              <span className="duel-price">{formatProductPrice(left.minPrice)}</span>
            </th>
            <td aria-hidden="true" />
            <th scope="col">
              <Link to={`/p/${right.slug}`}>{right.name}</Link>
              <span className="duel-price">{formatProductPrice(right.minPrice)}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map(({ left: leftSpec, right: rightSpec }) => (
            <tr key={leftSpec.key}>
              <td>{formatSpecValue(leftSpec)}</td>
              <th scope="row">{leftSpec.label}</th>
              <td>{formatSpecValue(rightSpec)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <button type="button" className="btn btn-primary duel-cta" onClick={openComparison}>
        So sánh chi tiết hai máy
        <Icon name="arrowRight" />
      </button>
    </section>
  );
}

function DuelSide({ product, side }: { product: ProductDetail; side: 'left' | 'right' }) {
  const image = product.images[0];
  const logo = getBrandLogo(product.brand.slug, 30);

  return (
    <div className={`duel-side duel-${side}`}>
      {logo && (
        <img className="duel-logo" src={logo.src} alt="" width={logo.width} height={logo.height} />
      )}
      {image && (
        <img
          className="duel-art"
          src={withCloudinaryTransform(image.url, DUEL_IMAGE_WIDTH, { trim: true })}
          alt=""
          width={DUEL_IMAGE_WIDTH}
          height={DUEL_IMAGE_WIDTH}
          loading="lazy"
        />
      )}
      <span className="duel-name">{product.name}</span>
    </div>
  );
}
