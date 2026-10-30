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
  deletedAt: string | null;
  canWrite: boolean;
  // Form còn thay đổi chưa lưu. Panel gọi `onSaveChanges` trước khi gửi lệnh trạng thái.
  hasUnsavedChanges: boolean;
  // Trả true khi dữ liệu đã lưu thành công. Lỗi thẩm định và lỗi API do form hiển thị.
  onSaveChanges: () => Promise<boolean>;
  onChangeStatus: (target: ProductStatus) => Promise<void>;
  onDelete: () => Promise<unknown>;
  onRestore: () => Promise<void>;
}

const UNSAVED_HINT_ID = 'product-status-unsaved-hint';

// Khối trạng thái của trang sửa sản phẩm. Nhãn trạng thái luôn hiện, nút chỉ hiện khi có `products:write`.
export function ProductStatusPanel({
  productName,
  status,
  deletedAt,
  canWrite,
  hasUnsavedChanges,
  onSaveChanges,
  onChangeStatus,
  onDelete,
  onRestore,
}: ProductStatusPanelProps) {
  const [pendingTarget, setPendingTarget] = useState<ProductStatus | null>(null);
  const [failure, setFailure] = useState<StatusFailure | null>(null);
  const [successMessage, setSuccessMessage] = useState('');
  const [isDeleteDialogOpen, setIsDeleteDialogOpen] = useState(false);
  const [isRestoring, setIsRestoring] = useState(false);

  // Cờ đổi ngay khi bấm. Biến state chỉ đổi sau lần vẽ lại, nên không chặn được lần bấm thứ hai đến trước đó.
  const isRequestInFlightRef = useRef(false);
  const isDeleted = deletedAt !== null;
  const isChanging = pendingTarget !== null || isRestoring;
  const statusLocked = isChanging;

  async function changeStatus(target: ProductStatus) {
    // Lần bấm thứ hai trong cùng lượt vẽ vẫn thấy `isChanging` cũ, nên phải đọc cờ ref để không gửi hai lệnh.
    if (isRequestInFlightRef.current) return;
    isRequestInFlightRef.current = true;
    setPendingTarget(target);
    setFailure(null);
    setSuccessMessage('');
    try {
      // API kiểm điều kiện đăng bán trên dữ liệu đã lưu, nên bản nháp phải được lưu trước.
      if (hasUnsavedChanges && !(await onSaveChanges())) {
        return;
      }
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

  async function confirmDelete() {
    // Xóa thành công có thể chuyển sản phẩm sang xóa mềm, nên đóng hộp thoại để lộ trạng thái mới.
    setFailure(null);
    setSuccessMessage('');
    await onDelete();
    setIsDeleteDialogOpen(false);
  }

  async function restoreProduct() {
    if (isRequestInFlightRef.current) return;
    isRequestInFlightRef.current = true;
    setIsRestoring(true);
    setFailure(null);
    setSuccessMessage('');
    try {
      await onRestore();
      setSuccessMessage('Đã khôi phục sản phẩm.');
    } catch (error) {
      setFailure({
        message: toCatalogErrorMessage(error, 'Không khôi phục được sản phẩm.'),
        reasons: [],
      });
    } finally {
      // Mở lại nút sau khi API trả kết quả, kể cả lỗi, để admin thử lại.
      isRequestInFlightRef.current = false;
      setIsRestoring(false);
    }
  }

  return (
    <section className="card product-section status-panel" aria-labelledby="product-status-title">
      <h2 id="product-status-title">Trạng thái</h2>
      <div className="status-current">
        <span className="muted small">Trạng thái hiện tại</span>
        <span
          className={
            isDeleted
              ? 'badge badge-bad'
              : (PRODUCT_STATUS_BADGE_CLASSES[status as ProductStatus] ?? 'badge')
          }
        >
          {isDeleted ? 'Đã xóa mềm' : (PRODUCT_STATUS_NAMES[status as ProductStatus] ?? status)}
        </span>
      </div>
      {isDeleted ? (
        <>
          <p className="muted small">
            Sản phẩm đang ẩn khỏi cửa hàng. Trạng thái trước khi xóa:{' '}
            {PRODUCT_STATUS_NAMES[status as ProductStatus] ?? status}.
          </p>
          {canWrite && (
            <div className="status-restore">
              <button
                type="button"
                className="btn btn-primary"
                disabled={isRestoring}
                onClick={() => void restoreProduct()}
              >
                {isRestoring ? 'Đang khôi phục...' : 'Khôi phục sản phẩm'}
              </button>
            </div>
          )}
        </>
      ) : (
        canWrite && (
          <>
            {hasUnsavedChanges && (
              <div id={UNSAVED_HINT_ID} className="muted small">
                Thay đổi sẽ được lưu trước khi đổi trạng thái.
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
                  onClick={() => void changeStatus(target)}
                >
                  {pendingTarget === target
                    ? 'Đang cập nhật...'
                    : getStatusActionLabel(status, target)}
                </button>
              ))}
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
                onConfirm={confirmDelete}
                onClose={() => setIsDeleteDialogOpen(false)}
              />
            )}
          </>
        )
      )}
      {canWrite && failure && <ErrorAlert message={failure.message} items={failure.reasons} />}
      {canWrite && (
        /* Vùng thông báo luôn có mặt để trình đọc màn hình đọc được nội dung xuất hiện sau. */
        <div role="status" className={successMessage ? 'alert alert-ok' : undefined}>
          {successMessage}
        </div>
      )}
    </section>
  );
}
