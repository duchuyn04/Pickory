import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { ErrorAlert } from '../../../shared/error-alert';
import { Icon } from '../../../shared/icons';
import { useDialogFocus } from '../../../shared/use-dialog-focus';
import { formatProductPrice } from '../../catalog/customer/product-display';
import type { Cart, CartLine } from './cart-api';
import { ProductThumbnail } from './product-thumbnail';
import { cartButtonLabel, unavailableLabel } from './cart-labels';
import { useCart, useRefreshCartOnActorChange } from './use-cart';
import './mini-cart.css';

function MiniCartLine({ line }: { line: CartLine }) {
  const reason = unavailableLabel(line);
  return (
    <li className="mini-cart-line">
      <ProductThumbnail imageUrl={line.image?.url} size={48} className="cart-line-image" />
      <div className="mini-cart-line-body">
        <p className="mini-cart-line-name">{line.productName ?? 'Sản phẩm đã gỡ'}</p>
        <p className="mini-cart-line-meta">
          <span>Số lượng: {line.quantity}</span>
          {reason === null && line.lineTotal !== null && (
            <strong>{formatProductPrice(line.lineTotal)}</strong>
          )}
        </p>
        {reason !== null && <p className="mini-cart-line-reason">{reason}</p>}
      </div>
    </li>
  );
}

function MiniCartContent({ cart, onNavigate }: { cart: Cart; onNavigate: () => void }) {
  if (cart.items.length === 0) {
    return <p className="mini-cart-empty muted">Giỏ hàng đang trống.</p>;
  }
  return (
    <>
      <ul className="mini-cart-lines">
        {cart.items.map((line) => (
          <MiniCartLine key={line.skuId} line={line} />
        ))}
      </ul>
      <p className="mini-cart-subtotal">
        <span>Tạm tính</span>
        <strong>{formatProductPrice(cart.subtotal)}</strong>
      </p>
      <Link to="/cart" className="btn btn-primary btn-block" onClick={onNavigate}>
        Xem giỏ hàng
      </Link>
    </>
  );
}

// Nút giỏ trên header kèm panel liệt kê dòng. Dữ liệu dùng chung cache với trang giỏ.
export function MiniCart() {
  const [isOpen, setIsOpen] = useState(false);
  const cartQuery = useCart();
  useRefreshCartOnActorChange();
  const wrapperRef = useRef<HTMLDivElement>(null);
  const toggleRef = useRef<HTMLButtonElement>(null);
  const lineCount = cartQuery.data?.items.length ?? 0;

  function closePanel() {
    setIsOpen(false);
    // Trả focus tường minh vì Safari không focus nút khi bấm chuột, nên hook không biết nút mở.
    toggleRef.current?.focus();
  }

  const panelRef = useDialogFocus({ isOpen, onClose: closePanel });

  useEffect(() => {
    if (!isOpen) return;
    // Bấm ra ngoài đóng panel. Không trả focus, vì người dùng đã chủ động chuyển sang chỗ khác.
    function handlePointerDown(event: MouseEvent) {
      if (wrapperRef.current && !wrapperRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    }
    document.addEventListener('mousedown', handlePointerDown);
    return () => document.removeEventListener('mousedown', handlePointerDown);
  }, [isOpen]);

  return (
    <div className="mini-cart" ref={wrapperRef}>
      <button
        ref={toggleRef}
        type="button"
        className="site-action mini-cart-toggle"
        aria-label={cartButtonLabel(lineCount)}
        aria-expanded={isOpen}
        aria-haspopup="dialog"
        onClick={() => setIsOpen((current) => !current)}
      >
        <Icon name="cart" />
        {lineCount > 0 && <span className="mini-cart-badge">{lineCount}</span>}
      </button>

      {isOpen && (
        <div
          ref={panelRef}
          role="dialog"
          aria-label="Giỏ hàng"
          tabIndex={-1}
          className="mini-cart-panel"
        >
          <div className="mini-cart-head">
            <h2>Giỏ hàng</h2>
            <button
              type="button"
              className="icon-btn"
              aria-label="Đóng giỏ hàng nhỏ"
              onClick={closePanel}
            >
              <Icon name="close" />
            </button>
          </div>
          {cartQuery.isPending && <p role="status">Đang tải giỏ hàng...</p>}
          {cartQuery.isError && <ErrorAlert message={cartQuery.error.message} />}
          {cartQuery.data && (
            <MiniCartContent cart={cartQuery.data} onNavigate={() => setIsOpen(false)} />
          )}
        </div>
      )}
    </div>
  );
}
