const SOUND_STORAGE_KEY = 'pickory.support.soundEnabled';

// Trình duyệt có thể chặn localStorage (chế độ riêng tư). Khi đó âm báo bật như mặc định.
export function readSoundEnabled(): boolean {
  try {
    return window.localStorage.getItem(SOUND_STORAGE_KEY) !== 'false';
  } catch {
    return true;
  }
}

// Lỗi ghi chỉ làm mất lựa chọn ở lần tải sau, không được làm hỏng thao tác bật tắt hiện tại.
export function storeSoundEnabled(isEnabled: boolean): void {
  try {
    window.localStorage.setItem(SOUND_STORAGE_KEY, String(isEnabled));
  } catch {
    return;
  }
}
