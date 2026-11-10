import { useEffect, useRef } from 'react';

const FOCUSABLE =
  'button:not([disabled]), a[href], input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

// Các hộp thoại đang mở theo thứ tự mở. Hộp cuối là hộp trên cùng.
const openDialogs: HTMLElement[] = [];

export function useDialogFocus({
  isOpen = true,
  onClose,
}: {
  isOpen?: boolean;
  onClose: () => void;
}) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const onCloseRef = useRef(onClose);
  // Callback mới không được khởi động lại effect và làm mất nút đã mở dialog.
  onCloseRef.current = onClose;

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!isOpen || !dialog) return;
    const opener = document.activeElement;
    const input = dialog.querySelector<HTMLElement>('input:not([disabled]):not([type="hidden"])');
    (input || dialog.querySelector<HTMLElement>(FOCUSABLE) || dialog).focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      // Hộp thoại bị hộp khác che thì bỏ qua phím, nếu không Esc đóng cả hai hộp cùng lúc.
      if (openDialogs[openDialogs.length - 1] !== dialog) return;
      if (event.key === 'Escape') {
        event.preventDefault();
        event.stopPropagation();
        onCloseRef.current();
      } else if (event.key === 'Tab') {
        const elements = dialog.querySelectorAll<HTMLElement>(FOCUSABLE);
        const first = elements[0];
        const last = elements[elements.length - 1];
        const outside = !dialog.contains(document.activeElement);
        if (!first || !last) {
          event.preventDefault();
          dialog.focus();
        } else if (event.shiftKey && (document.activeElement === first || outside)) {
          event.preventDefault();
          last.focus();
        } else if (!event.shiftKey && (document.activeElement === last || outside)) {
          event.preventDefault();
          first.focus();
        }
      }
    };

    // Đăng ký vào chồng hộp thoại để hộp mở sau (ở trên cùng) là hộp duy nhất nhận phím.
    openDialogs.push(dialog);
    window.addEventListener('keydown', handleKeyDown);
    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      // Kiểm vị trí trước khi xóa. `indexOf` trả -1 làm `splice(-1, 1)` xóa nhầm hộp thoại cuối chồng.
      const stackIndex = openDialogs.indexOf(dialog);
      if (stackIndex >= 0) openDialogs.splice(stackIndex, 1);
      // Cleanup của StrictMode cũng trả focus trước khi effect tiếp theo mở lại dialog.
      if (opener instanceof HTMLElement && opener.isConnected) opener.focus();
    };
  }, [isOpen]);

  return dialogRef;
}
