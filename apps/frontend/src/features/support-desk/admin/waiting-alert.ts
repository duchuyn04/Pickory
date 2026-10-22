import { readSoundEnabled } from './alert-preferences';

// `AudioContext` chỉ được tạo sau thao tác của người dùng. Tạo lúc tải trang làm Chrome ghi cảnh báo vào console.
let audioContext: AudioContext | null = null;

// Gọi ngay lúc người dùng thao tác, ví dụ bấm công tắc âm báo.
export function prepareWaitingTone(): void {
  if (audioContext !== null) {
    // Trình duyệt có thể tạm dừng ngữ cảnh âm thanh. Bấm thêm lần nữa thì đánh thức lại.
    void audioContext.resume();
    return;
  }
  // Trình duyệt không có Web Audio thì bỏ qua. Thông báo trình duyệt vẫn chạy.
  if (typeof AudioContext === 'undefined') return;
  audioContext = new AudioContext();
}

// Trả tài nguyên âm thanh khi rời khu quản trị, ví dụ lúc đăng xuất.
export function closeWaitingTone(): void {
  void audioContext?.close();
  audioContext = null;
}

// Hai nốt ngắn, nốt sau thấp hơn. Âm lượng tăng giảm mềm để không bị tiếng click ở đầu và cuối.
function playWaitingTone(): void {
  // Chưa có thao tác nào của người dùng thì chưa có ngữ cảnh âm thanh, nên im lặng.
  if (audioContext === null) return;
  if (audioContext.state === 'suspended') void audioContext.resume();

  const startTime = audioContext.currentTime;
  const gain = audioContext.createGain();
  gain.gain.setValueAtTime(0.0001, startTime);
  gain.gain.linearRampToValueAtTime(0.2, startTime + 0.03);
  gain.gain.exponentialRampToValueAtTime(0.0001, startTime + 0.5);
  gain.connect(audioContext.destination);

  const oscillator = audioContext.createOscillator();
  oscillator.type = 'sine';
  oscillator.frequency.setValueAtTime(880, startTime);
  oscillator.frequency.setValueAtTime(660, startTime + 0.22);
  oscillator.connect(gain);
  oscillator.start(startTime);
  oscillator.stop(startTime + 0.55);
}

export type NotificationSupport = NotificationPermission | 'unsupported';

export function readNotificationPermission(): NotificationSupport {
  return typeof Notification === 'undefined' ? 'unsupported' : Notification.permission;
}

// Chỉ gọi từ nút "Bật thông báo". Hỏi quyền lúc tải trang bị trình duyệt coi là làm phiền.
export async function requestNotificationPermission(): Promise<NotificationSupport> {
  if (typeof Notification === 'undefined') return 'unsupported';
  return Notification.requestPermission();
}

function showWaitingNotification(): void {
  // Chưa được cấp quyền thì không hiện. Hỏi quyền là việc của nút "Bật thông báo".
  if (readNotificationPermission() !== 'granted') return;
  try {
    // `tag` gộp nhiều thông báo liên tiếp thành một, để hàng chờ đông không làm ngập màn hình.
    new Notification('Có khách đang chờ', {
      body: 'Một hội thoại mới vừa vào hàng chờ CSKH.',
      tag: 'support-waiting',
    });
  } catch {
    // Một số trình duyệt di động chặn `new Notification`. Âm báo vẫn đủ để nhân viên biết.
    return;
  }
}

// Báo nhân viên có khách vào hàng chờ. Nơi gọi đã kiểm nhân viên đang "Sẵn sàng".
export function alertWaitingSession(): void {
  // Đọc lựa chọn tại lúc báo. Nhân viên tắt âm báo ở màn CSKH thì mọi trang quản trị đều im.
  if (readSoundEnabled()) playWaitingTone();
  showWaitingNotification();
}
