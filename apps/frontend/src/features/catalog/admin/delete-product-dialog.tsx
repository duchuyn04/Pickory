import { useRef, useState } from 'react';
import { useDialogFocus } from '../../../shared/use-dialog-focus';
import { toCatalogErrorMessage } from './catalog-errors';
import { ErrorAlert } from '../../../shared/error-alert';
import './products.css';

interface DeleteProductDialogProps {
  productName: string;
  // Trang cha gọi API xóa và chuyển trang khi thành công. Hộp thoại chỉ cần biết lệnh xóa có lỗi hay không.
  onConfirm: () => Promise<void>;
  onClose: () => void;
}

// Hộp thoại xác nhận xóa. Backend quyết định xóa cứng hoặc xóa mềm dựa trên lịch sử đơn hàng.
export function DeleteProductDialog({ productName, onConfirm, onClose }: DeleteProductDialogProps) {
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isDeleting, setIsDeleting] = useState(false);
  // Cờ đổi ngay khi bấm. `isDeleting` chỉ đổi sau lần vẽ lại, nên không chặn được lần bấm thứ hai đến trước đó.
  const isDeleteInFlightRef = useRef(false);

  // Esc và nút Hủy không được đóng hộp thoại khi lệnh xóa đã gửi, vì kết quả chưa biết.
  function closeUnlessDeleting() {
    if (isDeleting) return;
    onClose();
  }

  // Focus rơi vào nút Hủy vì hộp thoại không có ô nhập. Phím Enter vì thế không xóa nhầm.
  const dialogRef = useDialogFocus({ onClose: closeUnlessDeleting });

  async function confirmDelete() {
    // Lần bấm thứ hai trong cùng lượt vẽ vẫn thấy `isDeleting` cũ, nên phải đọc cờ ref để không gửi hai lệnh xóa.
    if (isDeleteInFlightRef.current) return;
    isDeleteInFlightRef.current = true;
    setErrorMessage(null);
    setIsDeleting(true);
    try {
      await onConfirm();
      // Giữ trạng thái đang xóa sau khi thành công. Trang cha đang chuyển đi nên nút không được bấm lại.
    } catch (error) {
      // Mở lại cờ để admin xác nhận lần nữa sau khi lỗi tạm thời, ví dụ mất mạng.
      isDeleteInFlightRef.current = false;
      // Giữ hộp thoại mở để admin đọc lý do, ví dụ sản phẩm đã có đơn hàng.
      setErrorMessage(toCatalogErrorMessage(error, 'Không xóa được sản phẩm.'));
      setIsDeleting(false);
    }
  }

  return (
    <div className="dialog-backdrop">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="delete-product-title"
        aria-describedby="delete-product-description"
        tabIndex={-1}
        className="dialog"
      >
        <div className="dialog-head">
          <h2 id="delete-product-title">Xóa sản phẩm</h2>
        </div>
        <div className="dialog-body stack">
          {errorMessage && <ErrorAlert message={errorMessage} />}
          <p id="delete-product-description" className="delete-product-text">
            Bạn sắp xóa sản phẩm <strong className="wrap-anywhere">{productName}</strong>. Sản phẩm
            chưa có đơn sẽ bị xóa vĩnh viễn. Sản phẩm đã có đơn được xóa mềm và có thể khôi phục.
          </p>
        </div>
        <div className="dialog-foot">
          <button type="button" className="btn" onClick={closeUnlessDeleting} disabled={isDeleting}>
            Hủy
          </button>
          <button
            type="button"
            className="btn btn-danger"
            onClick={confirmDelete}
            disabled={isDeleting}
          >
            Xác nhận xóa
          </button>
        </div>
      </div>
    </div>
  );
}
