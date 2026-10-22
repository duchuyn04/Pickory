import { SupportSwitch } from './support-switch';
import { usePresence } from './use-presence';

// Công tắc "Sẵn sàng" trên thanh trên cùng. Chỉ nhân viên có `conversations:reply` thấy nó.
export function PresenceToggle() {
  const presence = usePresence();

  // Công tắc bận khi đang đọc trạng thái, đang lưu hoặc không đọc được. Lúc đó trạng thái hiện tại chưa đáng tin.
  const isBusy = presence.isLoading || presence.loadFailed || presence.isSaving;

  return (
    <div className="presence-toggle">
      <SupportSwitch
        label="Sẵn sàng"
        checked={presence.isAvailable}
        onToggle={presence.toggle}
        isBusy={isBusy}
      />
      {presence.saveFailed ? (
        // Lỗi `PUT` báo bằng một dòng ngắn. Công tắc giữ trạng thái cũ để nhân viên biết máy chủ chưa đổi.
        <span role="alert" className="presence-toggle-error">
          Không đổi được trạng thái. Bấm công tắc để thử lại.
        </span>
      ) : (
        <span className="presence-toggle-state">
          {presence.loadFailed
            ? 'Không đọc được trạng thái'
            : presence.isAvailable
              ? 'Đang nhận khách'
              : 'Tạm vắng'}
        </span>
      )}
    </div>
  );
}
