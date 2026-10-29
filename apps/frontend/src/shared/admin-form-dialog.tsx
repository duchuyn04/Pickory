import React from 'react';
import { Icon } from './icons';
import { useDialogFocus } from './use-dialog-focus';
import { ErrorAlert } from './error-alert';

// Thuộc tính đầu vào cho khung hộp thoại biểu mẫu quản trị dùng chung.
export interface AdminFormDialogProps {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  formId: string;
  onSubmit: (event: React.FormEvent) => void;
  isSubmitting?: boolean;
  // Tắt nút gửi khi form chưa đủ điều kiện, ví dụ chưa tra được đơn cần gắn.
  submitDisabled?: boolean;
  // Chặn đóng khi tác vụ đang chạy có thể ghi kết quả cũ vào lần mở form tiếp theo.
  closeDisabled?: boolean;
  submitText?: string;
  cancelText?: string;
  error?: string | null;
  // Hộp thoại có trình sửa thuộc tính cần bản rộng `lg`. Form ngắn dùng bản mặc định.
  size?: 'md' | 'lg';
  children: React.ReactNode;
}

// Khung hộp thoại biểu mẫu chuẩn quản trị với bẫy focus và phím tắt đóng Esc.
export function AdminFormDialog({
  isOpen,
  onClose,
  title,
  formId,
  onSubmit,
  isSubmitting = false,
  submitDisabled = false,
  closeDisabled = false,
  submitText = 'Lưu',
  cancelText = 'Hủy',
  error,
  size = 'md',
  children,
}: AdminFormDialogProps) {
  // Bẫy focus đưa con trỏ vào ô nhập liệu đầu tiên khi mở hộp thoại.
  const dialogRef = useDialogFocus({
    isOpen,
    onClose,
  });

  // Không hiển thị cây DOM khi trạng thái mở đang là false.
  if (!isOpen) {
    return null;
  }

  return (
    <div className="dialog-backdrop">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={`${formId}-dialog-title`}
        tabIndex={-1}
        className={size === 'lg' ? 'dialog dialog-lg' : 'dialog'}
      >
        <div className="dialog-head">
          <h2 id={`${formId}-dialog-title`}>{title}</h2>
          <button
            type="button"
            className="icon-btn"
            onClick={onClose}
            aria-label="Đóng"
            disabled={closeDisabled}
          >
            <Icon name="close" />
          </button>
        </div>

        {/* Thân hộp thoại tự cuộn, để nút lưu ở chân hộp thoại luôn nhìn thấy. */}
        <div className="dialog-body">
          {error && <ErrorAlert message={error} />}

          <form id={formId} noValidate onSubmit={onSubmit} className="stack">
            {children}
          </form>
        </div>

        <div className="dialog-foot">
          <button
            type="button"
            className="btn"
            onClick={onClose}
            disabled={isSubmitting || closeDisabled}
          >
            {cancelText}
          </button>
          {/* Khóa nút gửi khi đang lưu để một lần bấm không tạo hai bản ghi. */}
          <button
            type="submit"
            className="btn btn-primary"
            form={formId}
            disabled={isSubmitting || submitDisabled}
          >
            {isSubmitting ? 'Đang lưu...' : submitText}
          </button>
        </div>
      </div>
    </div>
  );
}
