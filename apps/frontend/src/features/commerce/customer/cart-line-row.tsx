import { useEffect, useId, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { ErrorAlert } from '../../../shared/error-alert';
import { Icon } from '../../../shared/icons';
import { formatProductPrice } from '../../catalog/customer/product-display';
import type { CartLine } from './cart-api';
import { ProductThumbnail } from './product-thumbnail';
import { canIncreaseQuantity, parseQuantityInput, unavailableLabel } from './cart-labels';

interface CartLineRowProps {
  line: CartLine;
  // Thông báo lỗi của lần ghi gần nhất trên dòng này, hiện ngay cạnh dòng.
  errorMessage: string | null;
  isBusy: boolean;
  // Trả true khi backend nhận số lượng mới. False khi lỗi, để ô nhập quay về số trong giỏ.
  onSetQuantity: (quantity: number) => Promise<boolean>;
  onRemove: () => void;
}

function OptionValues({ optionValues }: { optionValues: Record<string, string> }) {
  const entries = Object.entries(optionValues);
  if (entries.length === 0) return null;
  return (
    <p className="cart-line-options muted">
      {entries.map(([name, value]) => `${name}: ${value}`).join(' · ')}
    </p>
  );
}

// Một dòng của trang giỏ. Dòng không mua được hiện nhãn lý do và chỉ cho xóa khi sản phẩm đã bị gỡ.
export function CartLineRow({
  line,
  errorMessage,
  isBusy,
  onSetQuantity,
  onRemove,
}: CartLineRowProps) {
  const inputId = useId();
  const [draft, setDraft] = useState(String(line.quantity));
  // Chặn gửi hai lần khi Enter và blur đến liên tiếp trước khi giỏ kịp cập nhật.
  const isCommitting = useRef(false);
  const latestQuantity = useRef(line.quantity);
  latestQuantity.current = line.quantity;

  useEffect(() => {
    // Giỏ đổi từ nơi khác (mini cart, lần gửi thành công) thì ô nhập theo số lượng mới.
    setDraft(String(line.quantity));
  }, [line.quantity]);

  const reason = unavailableLabel(line);
  const isRemoved = line.unavailableReason === 'removed';
  const productName = line.productName ?? 'Sản phẩm đã gỡ';

  async function commit(next: number | null) {
    if (isCommitting.current) return;
    // Giá trị sai hoặc không đổi thì không gửi request, ô quay về số lượng trong giỏ.
    if (next === null || next === line.quantity) {
      setDraft(String(line.quantity));
      return;
    }
    isCommitting.current = true;
    try {
      const accepted = await onSetQuantity(next);
      if (!accepted) setDraft(String(latestQuantity.current));
    } finally {
      isCommitting.current = false;
    }
  }

  return (
    <li className="cart-line" data-sku-id={line.skuId} aria-busy={isBusy}>
      <ProductThumbnail imageUrl={line.image?.url} size={96} className="cart-line-image" />

      <div className="cart-line-main">
        <p className="cart-line-name">
          {line.productSlug ? (
            <Link to={`/p/${line.productSlug}`}>{productName}</Link>
          ) : (
            productName
          )}
        </p>
        {line.optionValues && <OptionValues optionValues={line.optionValues} />}
        {line.unitPrice !== null && (
          <p className="cart-line-price">
            <span className="cart-line-price-label">Đơn giá</span>
            <span>{formatProductPrice(line.unitPrice)}</span>
            {line.compareAtPrice !== null && line.compareAtPrice > line.unitPrice && (
              <s className="muted">{formatProductPrice(line.compareAtPrice)}</s>
            )}
          </p>
        )}
        {reason !== null && <p className="cart-line-reason">{reason}</p>}
      </div>

      {!isRemoved && (
        <div className="cart-line-quantity">
          <span className="cart-line-quantity-label" aria-hidden="true">
            Số lượng
          </span>
          <label htmlFor={inputId} className="sr">
            Số lượng của {productName}
          </label>
          <div className="quantity-stepper">
            <button
              type="button"
              className="btn btn-outline btn-sm"
              aria-label={`Giảm số lượng ${productName}`}
              disabled={isBusy || line.quantity <= 1}
              onClick={() => void commit(line.quantity - 1)}
            >
              −
            </button>
            <input
              id={inputId}
              type="number"
              inputMode="numeric"
              min={1}
              step={1}
              value={draft}
              disabled={isBusy}
              onChange={(event) => setDraft(event.target.value)}
              onBlur={() => void commit(parseQuantityInput(draft))}
              onKeyDown={(event) => {
                if (event.key !== 'Enter') return;
                event.preventDefault();
                void commit(parseQuantityInput(draft));
              }}
            />
            <button
              type="button"
              className="btn btn-outline btn-sm"
              aria-label={`Tăng số lượng ${productName}`}
              disabled={isBusy || !canIncreaseQuantity(line)}
              onClick={() => void commit(line.quantity + 1)}
            >
              +
            </button>
          </div>
        </div>
      )}

      {!isRemoved && line.lineTotal !== null && (
        <div className="cart-line-total-wrap">
          <span className="cart-line-total-label">Thành tiền</span>
          <p
            className={
              line.purchasable ? 'cart-line-total' : 'cart-line-total cart-line-total-muted'
            }
          >
            {formatProductPrice(line.lineTotal)}
          </p>
        </div>
      )}

      <button
        type="button"
        className="btn btn-ghost btn-sm cart-line-remove"
        aria-label={`Xóa ${productName} khỏi giỏ hàng`}
        disabled={isBusy}
        onClick={onRemove}
      >
        <Icon name="trash" />
        <span>Xóa</span>
      </button>

      {errorMessage && <ErrorAlert message={errorMessage} className="cart-line-alert" />}
    </li>
  );
}
