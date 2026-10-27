import { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { ErrorAlert } from '../../../shared/error-alert';
import { ResultPanel } from '../../../shared/result-panel';
import { formatProductPrice } from '../../catalog/customer/product-display';
import type { Cart } from './cart-api';
import { CartLineRow } from './cart-line-row';
import { canCheckout, checkoutHint } from './cart-labels';
import { useCart, useRemoveCartItem, useSetCartQuantity } from './use-cart';
import './cart-page.css';
import { PurchaseProgress } from './purchase-progress';

function EmptyCart() {
  return (
    <ResultPanel
      framed
      tone="info"
      icon="cart"
      title="Giỏ hàng của bạn đang trống"
      description="Hãy chọn sản phẩm bạn thích rồi thêm vào giỏ."
      actions={
        <Link to="/" className="btn btn-primary">
          Tiếp tục mua sắm
        </Link>
      }
    />
  );
}

function CartSummary({ cart }: { cart: Cart }) {
  const isCheckoutEnabled = canCheckout(cart);
  const hint = checkoutHint(cart);
  return (
    <aside className="card cart-summary" aria-label="Tổng giỏ hàng">
      <div className="cart-summary-head">
        <h2>Tóm tắt đơn hàng</h2>
        <p className="muted small">Kiểm tra lại sản phẩm trước khi tiếp tục.</p>
      </div>

      <div className="cart-summary-figures">
        <p className="cart-summary-row">
          <span>Tạm tính</span>
          <strong>{formatProductPrice(cart.subtotal)}</strong>
        </p>
        <p className="cart-summary-shipping muted small">
          <span>Phí vận chuyển</span>
          <span>Tính ở bước tiếp theo</span>
        </p>
      </div>

      <div className="cart-summary-actions">
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
        <Link to="/" className="btn btn-outline btn-block">
          Tiếp tục mua sắm
        </Link>
      </div>
    </aside>
  );
}

export function CartPage() {
  const location = useLocation();
  const reorderFailedCount =
    (location.state as { reorderFailedCount?: number } | null)?.reorderFailedCount ?? 0;
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
      <PurchaseProgress stage="cart" />

      <header className="cart-page-header">
        <h1>Giỏ hàng</h1>
        <p className="muted">Kiểm tra sản phẩm và số lượng trước khi đặt hàng.</p>
      </header>

      {reorderFailedCount > 0 && (
        <p role="status" className="alert alert-warn">
          {reorderFailedCount} dòng hàng không thêm được vào giỏ. Sản phẩm có thể đã hết hàng hoặc
          ngừng bán.
        </p>
      )}

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
            <section className="card cart-items-card" aria-labelledby="cart-items-title">
              <div className="cart-items-head">
                <h2 id="cart-items-title">Sản phẩm trong giỏ</h2>
                <span className="cart-items-count">
                  {cartQuery.data.items.length} dòng sản phẩm
                </span>
              </div>
              <ul className="cart-lines">
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
            </section>
            <CartSummary cart={cartQuery.data} />
          </div>
        ))}
    </main>
  );
}
