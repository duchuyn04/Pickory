import React from 'react';
import { useDialogFocus } from '../../../shared/use-dialog-focus';

// Thuộc tính đầu vào cho khung hộp thoại biểu mẫu quản trị dùng chung.
export interface AdminFormDialogProps {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  formId: string;
  onSubmit: (event: React.FormEvent) => void;
  isSubmitting?: boolean;
  submitText?: string;
  cancelText?: string;
  error?: string | null;
  maxWidth?: number;
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
  submitText = 'Lưu',
  cancelText = 'Hủy',
  error,
  maxWidth = 600,
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
    <div
      style={{
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: 'rgba(0, 0, 0, 0.4)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 'var(--space-4)',
        zIndex: 10000,
        fontFamily: 'var(--font-sans)',
      }}
    >
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={`${formId}-dialog-title`}
        tabIndex={-1}
        style={{
          width: '100%',
          maxWidth,
          maxHeight: '90vh',
          backgroundColor: 'var(--color-surface)',
          borderRadius: 'var(--radius-card)',
          border: '1px solid var(--color-border)',
          display: 'flex',
          flexDirection: 'column',
          boxShadow: '0 8px 30px rgba(0, 0, 0, 0.15)',
          outline: 'none',
        }}
      >
        {/* Tiêu đề hộp thoại */}
        <div
          style={{
            padding: 'var(--space-4) var(--space-6)',
            borderBottom: '1px solid var(--color-border)',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
          }}
        >
          <h2
            id={`${formId}-dialog-title`}
            style={{
              fontSize: 'var(--text-lg)',
              fontWeight: 700,
              margin: 0,
              color: 'var(--color-ink)',
            }}
          >
            {title}
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng"
            style={{
              background: 'none',
              border: 'none',
              fontSize: 'var(--text-lg)',
              cursor: 'pointer',
              color: 'var(--color-ink-muted)',
            }}
          >
            ✕
          </button>
        </div>

        {/* Thân hộp thoại chứa form và cảnh báo lỗi */}
        <div
          style={{
            padding: 'var(--space-6)',
            overflowY: 'auto',
            flex: 1,
          }}
        >
          {error && (
            <div
              role="alert"
              style={{
                backgroundColor: 'var(--color-surface-muted)',
                border: '1px solid var(--color-danger)',
                color: 'var(--color-danger)',
                padding: 'var(--space-3)',
                borderRadius: 'var(--radius-control)',
                fontSize: 'var(--text-sm)',
                marginBottom: 'var(--space-4)',
              }}
            >
              {error}
            </div>
          )}

          <form
            id={formId}
            noValidate
            onSubmit={onSubmit}
            style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-4)' }}
          >
            {children}
          </form>
        </div>

        {/* Chân hộp thoại chứa các nút hành động */}
        <div
          style={{
            padding: 'var(--space-4) var(--space-6)',
            borderTop: '1px solid var(--color-border)',
            display: 'flex',
            justifyContent: 'flex-end',
            gap: 'var(--space-3)',
          }}
        >
          <button
            type="button"
            onClick={onClose}
            disabled={isSubmitting}
            style={{
              padding: 'var(--space-2) var(--space-4)',
              backgroundColor: 'transparent',
              border: '1px solid var(--color-border)',
              borderRadius: 'var(--radius-control)',
              fontSize: 'var(--text-sm)',
              cursor: 'pointer',
              color: 'var(--color-ink)',
            }}
          >
            {cancelText}
          </button>
          <button
            type="submit"
            form={formId}
            disabled={isSubmitting}
            style={{
              padding: 'var(--space-2) var(--space-4)',
              backgroundColor: 'var(--color-brand)',
              color: 'var(--color-surface)',
              border: 'none',
              borderRadius: 'var(--radius-control)',
              fontSize: 'var(--text-sm)',
              fontWeight: 600,
              cursor: isSubmitting ? 'not-allowed' : 'pointer',
              opacity: isSubmitting ? 0.7 : 1,
            }}
          >
            {isSubmitting ? 'Đang lưu...' : submitText}
          </button>
        </div>
      </div>
    </div>
  );
}
