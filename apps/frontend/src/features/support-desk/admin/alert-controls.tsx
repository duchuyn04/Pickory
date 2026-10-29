import { useState } from 'react';
import { readSoundEnabled, storeSoundEnabled } from './alert-preferences';
import { SupportSwitch } from './support-switch';
import {
  prepareWaitingTone,
  readNotificationPermission,
  requestNotificationPermission,
  type NotificationSupport,
} from './waiting-alert';

// Hai lựa chọn báo khách vào hàng chờ: công tắc âm báo và nút bật thông báo của trình duyệt.
export function AlertControls() {
  const [isSoundEnabled, setIsSoundEnabled] = useState(readSoundEnabled);
  const [permission, setPermission] = useState<NotificationSupport>(readNotificationPermission);

  function toggleSound() {
    const nextEnabled = !isSoundEnabled;
    setIsSoundEnabled(nextEnabled);
    // Lưu lựa chọn để lần tải trang sau còn nguyên.
    storeSoundEnabled(nextEnabled);
    // Bấm công tắc là thao tác của người dùng, nên được phép tạo `AudioContext` ở đây.
    if (nextEnabled) prepareWaitingTone();
  }

  async function enableNotifications() {
    // Hỏi quyền chỉ khi nhân viên bấm nút. Kết quả quyết định nút hay dòng chữ được hiện.
    setPermission(await requestNotificationPermission());
  }

  return (
    <div className="support-alert-controls">
      <SupportSwitch label="Âm báo" checked={isSoundEnabled} onToggle={toggleSound} />
      {permission === 'default' && (
        <button type="button" className="btn btn-sm" onClick={() => void enableNotifications()}>
          Bật thông báo
        </button>
      )}
      {permission === 'granted' && <span className="badge badge-ok">Thông báo đã bật</span>}
      {permission === 'denied' && (
        <span className="muted small">
          Trình duyệt đang chặn thông báo. Hãy cho phép trong cài đặt của trang.
        </span>
      )}
    </div>
  );
}
