import { useState } from 'react';
import { ErrorAlert } from '../../../shared/error-alert';
import { Icon } from '../../../shared/icons';
import type { ProductSkuInfo } from '../../catalog/customer/catalog-api';
import { useAddCartItem } from './use-cart';
import './add-to-cart-button.css';

interface AddToCartButtonProps {
  // SKU đang chọn. Null khi sản phẩm chưa có SKU bán.
  sku: ProductSkuInfo | null;
  isDiscontinued: boolean;
  quantity?: number;
}

// Nút thêm giỏ của trang sản phẩm, hỗ trợ truyền số lượng SKU đang chọn.
// Nơi dùng đặt `key` theo SKU, nên đổi biến thể thì xác nhận và lỗi của lần trước biến mất.
export function AddToCartButton({ sku, isDiscontinued, quantity = 1 }: AddToCartButtonProps) {
  const addItem = useAddCartItem();
  const [wasAdded, setWasAdded] = useState(false);
  // Sản phẩm ngừng kinh doanh và SKU hết hàng không thêm được. Backend cũng từ chối bằng SKU_UNAVAILABLE.
  const isUnavailable = sku === null || !sku.inStock || isDiscontinued;

  function handleClick() {
    if (sku === null || addItem.isPending) return;
    setWasAdded(false);
    // Thêm đúng số lượng khách đã chọn vào giỏ hàng.
    addItem.mutate({ skuId: sku.id, quantity }, { onSuccess: () => setWasAdded(true) });
  }

  return (
    <>
      <button
        type="button"
        className="btn btn-outline btn-block"
        // Tắt trong lúc gửi để một lần bấm đúp không thêm hai sản phẩm.
        disabled={isUnavailable || addItem.isPending}
        onClick={handleClick}
      >
        <Icon name="cart" />
        <span>Thêm vào giỏ</span>
      </button>
      {/* Vùng trạng thái luôn có trong DOM, để trình đọc màn hình đọc được nội dung khi nó đổi. */}
      <p role="status" aria-live="polite" className={wasAdded ? 'product-cart-added' : 'sr'}>
        {wasAdded
          ? quantity > 1
            ? `Đã thêm ${quantity} sản phẩm vào giỏ`
            : 'Đã thêm vào giỏ'
          : ''}
      </p>
      {addItem.isError && <ErrorAlert message={addItem.error.message} />}
    </>
  );
}
