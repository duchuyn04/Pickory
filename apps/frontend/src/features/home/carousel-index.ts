// Banner tự chuyển sau khoảng này. Banner có tên máy, bốn thông số và giá, nên khách cần 7 giây để đọc.
// `--banner-autoplay` trong `home-page.css` phải bằng giá trị này để thanh tiến trình chạy hết đúng lúc đổi banner.
export const AUTOPLAY_INTERVAL_MS = 7000;

// Đưa chỉ số về trong khoảng [0, slideCount). Nút "tiếp theo" ở banner cuối quay về banner đầu, và ngược lại.
export function wrapSlideIndex(index: number, slideCount: number): number {
  if (slideCount <= 0) return 0;
  return ((index % slideCount) + slideCount) % slideCount;
}

// Phím mũi tên, Home và End di chuyển giữa các tab theo mẫu tablist của WAI-ARIA. Phím khác trả null.
export function getTabIndexForKey(
  key: string,
  currentIndex: number,
  slideCount: number,
): number | null {
  switch (key) {
    case 'ArrowRight':
      return wrapSlideIndex(currentIndex + 1, slideCount);
    case 'ArrowLeft':
      return wrapSlideIndex(currentIndex - 1, slideCount);
    case 'Home':
      return 0;
    case 'End':
      return wrapSlideIndex(slideCount - 1, slideCount);
    default:
      return null;
  }
}
