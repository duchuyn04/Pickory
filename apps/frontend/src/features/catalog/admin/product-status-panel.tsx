import { useRef, useState } from 'react';
import { ErrorAlert } from '../../../shared/error-alert';
import { toCatalogErrorMessage } from './catalog-errors';
import { DeleteProductDialog } from './delete-product-dialog';
import {
  PRODUCT_STATUS_BADGE_CLASSES,
  PRODUCT_STATUS_NAMES,
  getAllowedTargetStatuses,
  getStatusActionLabel,
  readNotPublishableReasons,
  type ProductStatus,
} from './product-status';
import './products.css';

interface StatusFailure {
  message: string;
  // Lý do đăng bán thất bại của lỗi 422. Các lỗi khác để trống.
  reasons: string[];
}

interface ProductStatusPanelProps {
  productName: string;
  status: string;
  canWrite: boolean;
  // Form còn thay đổi chưa lưu. API kiểm điều kiện đăng bán trên dữ liệu đã lưu nên các nút trạng thái phải khóa.
  hasUnsavedChanges: boolean;
  onChangeStatus: (target: ProductStatus) => Promise<void>;
  onDelete: () => Promise<void>;
}

const UNSAVED_HINT_ID = 'product-status-unsaved-hint';

// Khối trạng thái của trang sửa sản phẩm. Nhãn trạng thái luôn hiện, nút chỉ hiện khi có `products:write`.
export function ProductStatusPanel({
  productName,
  status,
  canWrite,
  hasUnsavedChanges,
  onChangeStatus,
  onDelete,
}: ProductStatusPanelProps) {
  const [pendingTarget, setPendingTarget] = useState<ProductStatus | null>(null);
  const [failure, setFailure] = useState<StatusFailure | null>(null);
  const [successMessage, setSuccessMessage] = useState('');
  const [isDeleteDialogOpen, setIsDeleteDialogOpen] = useState(false);

  // Cờ đổi ngay khi bấm. Biến state chỉ đổi sau lần vẽ lại, nên không chặn được lần bấm thứ hai đến trước đó.
  const isRequestInFlightRef = useRef(false);
  const isChanging = pendingTarget !== null;
  const statusLocked = isChanging || hasUnsavedChanges;

  async function changeStatus(target: ProductStatus) {
    // Form còn thay đổi chưa lưu thì API kiểm điều kiện đăng bán trên dữ liệu cũ, nên không gửi lệnh.
    if (hasUnsavedChanges) return;
    // Lần bấm thứ hai trong cùng lượt vẽ vẫn thấy `isChanging` cũ, nên phải đọc cờ ref để không gửi hai lệnh.
    if (isRequestInFlightRef.current) return;
    isRequestInFlightRef.current = true;
    setPendingTarget(target);
    setFailure(null);
    setSuccessMessage('');
    try {
      await onChangeStatus(target);
      setSuccessMessage(`Đã đổi trạng thái thành "${PRODUCT_STATUS_NAMES[target]}".`);
    } catch (error) {
      // Lỗi 422 mang danh sách lý do. Hiện hết một lần để admin sửa xong mới thử lại.
      setFailure({
        message: toCatalogErrorMessage(error, 'Không đổi được trạng thái sản phẩm.'),
        reasons: readNotPublishableReasons(error),
      });
    } finally {
      // Mở lại cờ sau khi API trả kết quả, kể cả lỗi, để admin thử lại.
      isRequestInFlightRef.current = false;
      setPendingTarget(null);
    }
  }

  return (
    <section className="card product-section status-panel" aria-labelledby="product-status-title">
      <h2 id="product-status-title">Trạng thái</h2>
      <div className="status-current">
        <span className="muted small">Trạng thái hiện tại</span>
        <span className={PRODUCT_STATUS_BADGE_CLASSES[status as ProductStatus] ?? 'badge'}>
          {PRODUCT_STATUS_NAMES[status as ProductStatus] ?? status}
        </span>
      </div>
      {canWrite && (
        <>
          {hasUnsavedChanges && (
            <div id={UNSAVED_HINT_ID} className="muted small">
              Lưu thay đổi trước khi đổi trạng thái.
            </div>
          )}
          <div className="status-actions">
            {getAllowedTargetStatuses(status).map((target) => (
              <button
                key={target}
                type="button"
                className={target === 'active' ? 'btn btn-primary' : 'btn'}
                disabled={statusLocked}
                aria-describedby={hasUnsavedChanges ? UNSAVED_HINT_ID : undefined}
                onClick={() => changeStatus(target)}
              >
                {pendingTarget === target
                  ? 'Đang cập nhật...'
                  : getStatusActionLabel(status, target)}
              </button>
            ))}
          </div>
          {failure && <ErrorAlert title={failure.message} items={failure.reasons} />}
          {/* Vùng thông báo luôn có mặt để trình đọc màn hình đọc được nội dung xuất hiện sau. */}
          <div role="status" className={successMessage ? 'alert alert-ok' : undefined}>
            {successMessage}
          </div>
          {/* Nút xóa tách khỏi các nút trạng thái để không bấm nhầm khi đổi trạng thái. */}
          <div className="status-danger">
            <button
              type="button"
              className="btn btn-danger"
              disabled={isChanging}
              onClick={() => setIsDeleteDialogOpen(true)}
            >
              Xóa sản phẩm
            </button>
          </div>
          {isDeleteDialogOpen && (
            <DeleteProductDialog
              productName={productName}
              onConfirm={onDelete}
              onClose={() => setIsDeleteDialogOpen(false)}
            />
          )}
        </>
      )}
    </section>
  );
}
