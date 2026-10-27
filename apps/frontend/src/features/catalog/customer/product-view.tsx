import { Link, useSearchParams } from 'react-router-dom';
import { Icon } from '../../../shared/icons';
import type { ProductDetail, ProductSkuInfo } from './catalog-api';
import { AddToCartButton } from '../../commerce/customer/add-to-cart-button';
import { ReviewsSection } from '../../commerce/customer/reviews/reviews-section';
import { CompareControl } from './compare-control';
import { DescriptionView } from './description-view';
import { selectGalleryImages } from './gallery-images';
import { formatDiscountPercent, formatProductPrice } from './product-display';
import { ProductGallery } from './product-gallery';
import { SpecTable } from './spec-table';
import { VariantPicker } from './variant-picker';
import { chooseSkuForOptionValue, pickInitialSku } from './variant-selection';

const SKU_PARAM = 'sku';

function Breadcrumb({ product }: { product: ProductDetail }) {
  const categories = [product.parentCategory, product.category].filter(
    (category) => category !== null,
  );
  return (
    <nav className="catalog-crumbs" aria-label="Đường dẫn">
      <ol>
        <li>
          <Link to="/">Trang chủ</Link>
        </li>
        {categories.map((category) => (
          <li key={category.id}>
            <Icon name="chevronRight" className="icon catalog-crumbs-separator" />
            <Link to={`/c/${category.slug}`}>{category.name}</Link>
          </li>
        ))}
        <li>
          <Icon name="chevronRight" className="icon catalog-crumbs-separator" />
          <span aria-current="page">{product.name}</span>
        </li>
      </ol>
    </nav>
  );
}

function PriceBlock({ sku }: { sku: ProductSkuInfo | null }) {
  // Sản phẩm không có SKU bật thì chưa có giá bán, nên hiện "Liên hệ" thay cho số 0.
  if (sku === null) {
    return (
      <p className="product-price">
        <strong className="product-price-contact">{formatProductPrice(null)}</strong>
      </p>
    );
  }
  const discountPercent = formatDiscountPercent(sku.price, sku.compareAtPrice);
  return (
    <p className="product-price">
      <strong>{formatProductPrice(sku.price)}</strong>
      {discountPercent !== null && sku.compareAtPrice !== null && (
        <>
          <s className="product-price-list">{formatProductPrice(sku.compareAtPrice)}</s>
          <span className="product-discount">-{discountPercent}%</span>
        </>
      )}
    </p>
  );
}

export function ProductView({ product }: { product: ProductDetail }) {
  const [searchParams, setSearchParams] = useSearchParams();
  // SKU đang chọn suy ra từ URL, nên link chia sẻ mở đúng biến thể và không cần state riêng.
  const selectedSku = pickInitialSku(product.skus, searchParams.get(SKU_PARAM));
  const isDiscontinued = product.status === 'discontinued';
  const images = selectGalleryImages(product, selectedSku);

  function handleSelectOption(optionName: string, value: string) {
    if (!selectedSku) return;
    const nextSku = chooseSkuForOptionValue(
      product.skus,
      selectedSku.optionValues,
      optionName,
      value,
    );
    // Bộ chọn chỉ cho bấm giá trị có SKU, nên nhánh này chỉ là lưới an toàn.
    if (!nextSku) return;
    // Thay thế mục lịch sử hiện tại, để nút Back không phải bấm qua từng lần đổi biến thể.
    setSearchParams(
      (current) => {
        const next = new URLSearchParams(current);
        next.set(SKU_PARAM, nextSku.id);
        return next;
      },
      { replace: true },
    );
  }

  return (
    <main className="catalog-page product-page">
      <Breadcrumb product={product} />

      <header className="product-title">
        <p className="product-brand muted">{product.brand.name}</p>
        <h1>{product.name}</h1>
        {isDiscontinued && <span className="badge badge-warn">Ngừng kinh doanh</span>}
      </header>

      <div className="product-grid">
        <ProductGallery
          // Danh sách ảnh đổi thì dựng lại gallery, để ảnh đang xem quay về ảnh đầu của biến thể mới.
          key={images.map((image) => image.url).join('|')}
          images={images}
          productName={product.name}
        />

        <section className="product-buy" aria-label="Mua hàng">
          <PriceBlock sku={selectedSku} />

          {selectedSku !== null && (
            <>
              {product.options.length > 0 && (
                <VariantPicker
                  options={product.options}
                  skus={product.skus}
                  selection={selectedSku.optionValues}
                  readOnly={isDiscontinued}
                  onSelect={handleSelectOption}
                />
              )}
              {/* Sản phẩm ngừng kinh doanh không hiện tồn kho, vì "Còn hàng" mâu thuẫn với dòng ngừng kinh doanh. */}
              {!isDiscontinued && (
                <p className="product-stock">
                  {selectedSku.inStock ? (
                    <span className="badge badge-ok">Còn hàng</span>
                  ) : (
                    <span className="badge badge-bad">Hết hàng</span>
                  )}
                  {selectedSku.inStock && (
                    <span className="muted small">
                      {' '}
                      Còn {selectedSku.stock.toLocaleString('vi-VN')} sản phẩm
                    </span>
                  )}
                </p>
              )}
            </>
          )}

          {isDiscontinued && <p className="muted small">Sản phẩm đã ngừng kinh doanh.</p>}

          <div className="product-actions">
            {/* `key` theo SKU: đổi biến thể thì xác nhận và lỗi của lần thêm trước được xóa. */}
            <AddToCartButton
              key={selectedSku?.id ?? 'no-sku'}
              sku={selectedSku}
              isDiscontinued={isDiscontinued}
            />
            <CompareControl categoryId={product.category.id} productId={product.id} />
          </div>

          {product.warrantyMonths > 0 && (
            <p className="muted small">Bảo hành {product.warrantyMonths} tháng</p>
          )}
          {product.shortDesc && <p>{product.shortDesc}</p>}
        </section>
      </div>

      <DescriptionView description={product.description} />
      <SpecTable specs={product.specs} />
      {/* Khối đánh giá tự giữ trạng thái lỗi của nó, nên API đánh giá lỗi không làm hỏng phần mua hàng. */}
      <ReviewsSection productId={product.id} />

      {/* Hai vùng cho module sau. Vùng rỗng không có kích thước, nên khách không thấy khung trống. */}
      <div className="product-slot" data-slot="recommendations" />
      <div className="product-slot" data-slot="personalized-content" />
    </main>
  );
}
