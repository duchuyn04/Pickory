import { useNavigate } from 'react-router-dom';
import { ErrorAlert } from '../../../shared/error-alert';
import type { ProductSkuInfo } from '../../catalog/customer/catalog-api';
import { useAddCartItem } from './use-cart';

interface BuyNowButtonProps {
  sku: ProductSkuInfo | null;
  isDiscontinued: boolean;
  quantity?: number;
}

export function BuyNowButton({ sku, isDiscontinued, quantity = 1 }: BuyNowButtonProps) {
  const navigate = useNavigate();
  const addItem = useAddCartItem();
  const isUnavailable = sku === null || !sku.inStock || isDiscontinued;

  function handleClick() {
    // Chặn thao tác lặp trong lúc gửi và truyền đúng số lượng khách chọn.
    if (sku === null || addItem.isPending) return;
    addItem.mutate(
      { skuId: sku.id, quantity },
      {
        // Chỉ chuyển checkout sau khi backend nhận sản phẩm, nên khách không đặt hàng với giỏ thiếu sản phẩm.
        onSuccess: () => navigate('/checkout'),
      },
    );
  }
  return (
    <>
      <button
        type="button"
        className="btn btn-primary btn-block"
        disabled={isUnavailable || addItem.isPending}
        onClick={handleClick}
      >
        Mua ngay
      </button>
      {addItem.isError && <ErrorAlert message={addItem.error.message} />}
    </>
  );
}
