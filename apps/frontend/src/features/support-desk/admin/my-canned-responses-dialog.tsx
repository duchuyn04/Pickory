import { Icon } from '../../../shared/icons';
import { useBodyScrollLock } from '../../../shared/use-body-scroll-lock';
import { useDialogFocus } from '../../../shared/use-dialog-focus';
import { CannedResponsePanel } from './canned-response-panel';

interface MyCannedResponsesDialogProps {
  onClose: () => void;
}

// Hộp thoại "Mẫu của tôi": nhân viên thêm, sửa và xóa mẫu cá nhân mà không rời màn chăm sóc khách hàng.
export function MyCannedResponsesDialog({ onClose }: MyCannedResponsesDialogProps) {
  const dialogRef = useDialogFocus({ onClose });
  useBodyScrollLock(true);

  return (
    <div className="dialog-backdrop">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="my-canned-title"
        tabIndex={-1}
        className="dialog canned-dialog"
      >
        <div className="dialog-head">
          <h2 id="my-canned-title">Mẫu của tôi</h2>
          <button type="button" className="icon-btn" onClick={onClose} aria-label="Đóng">
            <Icon name="close" className="icon" />
          </button>
        </div>
        <div className="dialog-body">
          {/* Hộp này chỉ mở từ nút "Quản lý mẫu", và nút đó chỉ hiện khi có `conversations:reply`. */}
          <CannedResponsePanel scope="personal" canUse />
        </div>
        <div className="dialog-foot">
          <button type="button" className="btn btn-outline" onClick={onClose}>
            Đóng
          </button>
        </div>
      </div>
    </div>
  );
}
