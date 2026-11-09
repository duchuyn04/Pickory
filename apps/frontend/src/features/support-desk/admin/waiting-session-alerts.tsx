import { useEffect, useRef } from 'react';
import { readSoundEnabled } from './alert-preferences';
import { useSupportEvents } from './support-events-provider';
import {
  alertReminderSession,
  alertWaitingSession,
  closeWaitingTone,
  prepareWaitingTone,
} from './waiting-alert';
import { usePresence } from './use-presence';

const GESTURE_EVENTS = ['click', 'keydown'] as const;

// Component này báo âm thanh và thông báo trình duyệt khi khách vào hàng chờ hoặc chờ lâu. Nó không vẽ gì ra trang.
// Nó nằm trong khung quản trị, nên nhân viên "Sẵn sàng" ở trang nào cũng được báo.
export function WaitingSessionAlerts() {
  const { subscribe } = useSupportEvents();
  const { isAvailable } = usePresence();
  // Bộ nhận sự kiện đăng ký một lần. Ref giữ giá trị mới nhất mà không phải đăng ký lại.
  const isAvailableRef = useRef(isAvailable);

  useEffect(() => {
    isAvailableRef.current = isAvailable;
  }, [isAvailable]);

  useEffect(() => {
    return subscribe((eventName) => {
      const isWaitingEvent = eventName === 'session.waiting';
      const isReminderEvent = eventName === 'session.reminded';
      if (!isWaitingEvent && !isReminderEvent) return;
      // Nhân viên "Tạm vắng" không muốn bị làm phiền, nên không báo cả hai loại.
      if (!isAvailableRef.current) return;
      if (isReminderEvent) {
        alertReminderSession();
        return;
      }
      alertWaitingSession();
    });
  }, [subscribe]);

  useEffect(() => {
    // Trình duyệt chỉ cho tạo `AudioContext` sau thao tác của người dùng. Thao tác đầu tiên trên trang mở khóa âm báo.
    function unlockOnFirstGesture() {
      if (readSoundEnabled()) prepareWaitingTone();
      stopListening();
    }
    function stopListening() {
      for (const gestureEvent of GESTURE_EVENTS) {
        document.removeEventListener(gestureEvent, unlockOnFirstGesture, true);
      }
    }
    for (const gestureEvent of GESTURE_EVENTS) {
      document.addEventListener(gestureEvent, unlockOnFirstGesture, true);
    }

    return () => {
      stopListening();
      // Trả tài nguyên âm thanh khi rời khu quản trị, ví dụ lúc đăng xuất.
      closeWaitingTone();
    };
  }, []);

  return null;
}
