import { useState } from 'react';
import { Link } from 'react-router-dom';
import { ErrorAlert } from '../../../shared/error-alert';
import { formatProductPrice } from '../../catalog/customer/product-display';
import type { Cart } from './cart-api';
import { CartLineRow } from './cart-line-row';
import { canCheckout, checkoutHint } from './cart-labels';
import { useCart, useRemoveCartItem, useSetCartQuantity } from './use-cart';
import './cart-page.css';

function EmptyCart() {
  return (
    <div className="card cart-empty">
      <h2>Giỏ hàng của bạn đang trống</h2>
      <p className="muted">Hãy chọn sản phẩm bạn thích rồi thêm vào giỏ.</p>
      <Link to="/" className="btn btn-primary">
        Tiếp tục mua sắm
      </Link>
    </div>
  );
}

function CartSummary({ cart }: { cart: Cart }) {
  const isCheckoutEnabled = canCheckout(cart);
  const hint = checkoutHint(cart);
  return (
    <aside className="card cart-summary" aria-label="Tổng giỏ hàng">
      <p className="cart-summary-row">
        <span>Tạm tính</span>
        <strong>{formatProductPrice(cart.subtotal)}</strong>
      </p>
      <p className="muted small">Phí vận chuyển được tính ở bước đặt hàng.</p>
      <Link
        to="/checkout"
        className="btn btn-primary btn-block cart-checkout"
        aria-disabled={isCheckoutEnabled ? undefined : true}
        aria-describedby={hint ? 'cart-checkout-hint' : undefined}
        onClick={(event) => {
          // Nút tắt là liên kết nên phải chặn điều hướng bằng tay. Thuộc tính `aria-disabled` không tự chặn.
          if (!isCheckoutEnabled) event.preventDefault();
        }}
      >
        Tiến hành đặt hàng
      </Link>
      {hint && (
        <p id="cart-checkout-hint" className="cart-checkout-hint muted small">
          {hint}
        </p>
      )}
    </aside>
  );
}

export function CartPage() {
  const cartQuery = useCart();
  const setQuantity = useSetCartQuantity();
  const removeItem = useRemoveCartItem();
  const [lineErrors, setLineErrors] = useState<Record<string, string>>({});
  const [busySkuId, setBusySkuId] = useState<string | null>(null);

  function setLineError(skuId: string, message: string | null) {
    setLineErrors((current) => {
      const next = { ...current };
      if (message === null) delete next[skuId];
      else next[skuId] = message;
      return next;
    });
  }

  async function handleSetQuantity(skuId: string, quantity: number): Promise<boolean> {
    setBusySkuId(skuId);
    setLineError(skuId, null);
    try {
      await setQuantity.mutateAsync({ skuId, quantity });
      return true;
    } catch (error) {
      // Thông báo của API đã là tiếng Việt, nên hiện nguyên văn cạnh dòng bị lỗi.
      setLineError(skuId, error instanceof Error ? error.message : 'Không đổi được số lượng.');
      return false;
    } finally {
      setBusySkuId(null);
    }
  }

  async function handleRemove(skuId: string) {
    setBusySkuId(skuId);
    setLineError(skuId, null);
    try {
      await removeItem.mutateAsync(skuId);
    } catch (error) {
      setLineError(skuId, error instanceof Error ? error.message : 'Không xóa được sản phẩm.');
    } finally {
      setBusySkuId(null);
    }
  }

  return (
    <main className="catalog-page cart-page">
      <h1>Giỏ hàng</h1>

      {cartQuery.isPending && (
        <p role="status" aria-busy="true">
          Đang tải giỏ hàng...
        </p>
      )}
      {cartQuery.isError && (
        <ErrorAlert message={cartQuery.error.message} className="catalog-alert" />
      )}

      {cartQuery.data &&
        (cartQuery.data.items.length === 0 ? (
          <EmptyCart />
        ) : (
          <div className="cart-layout">
            <ul className="cart-lines card">
              {cartQuery.data.items.map((line) => (
                <CartLineRow
                  key={line.skuId}
                  line={line}
                  errorMessage={lineErrors[line.skuId] ?? null}
                  isBusy={busySkuId === line.skuId}
                  onSetQuantity={(quantity) => handleSetQuantity(line.skuId, quantity)}
                  onRemove={() => void handleRemove(line.skuId)}
                />
              ))}
            </ul>
            <CartSummary cart={cartQuery.data} />
          </div>
        ))}
    </main>
  );
}
