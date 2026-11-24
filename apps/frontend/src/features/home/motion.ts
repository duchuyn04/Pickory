import { useEffect, useState } from 'react';

const REDUCED_MOTION_QUERY = '(prefers-reduced-motion: reduce)';
// Phần tử lộ ra khi 15% của nó vào màn hình. Khách thấy chuyển động mà nội dung không trễ quá lâu.
const REVEAL_VISIBLE_RATIO = 0.15;

export function prefersReducedMotion(): boolean {
  // jsdom và trình duyệt cũ không có `matchMedia`. Khi đó trang chạy chuyển động như mặc định.
  return window.matchMedia?.(REDUCED_MOTION_QUERY).matches ?? false;
}

// Dùng callback ref để gắn IntersectionObserver ngay cả khi phần tử chỉ mount sau lúc dữ liệu tải xong.
export function useRevealOnScroll<Element extends HTMLElement>(): (node: Element | null) => void {
  const [element, setElement] = useState<Element | null>(null);

  useEffect(() => {
    if (!element) return;
    // Trình duyệt không có IntersectionObserver thì hiện ngay. Nội dung không được kẹt ở trạng thái ẩn.
    if (typeof IntersectionObserver === 'undefined') {
      element.classList.add('is-revealed');
      return;
    }
    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (!entry.isIntersecting) continue;
          entry.target.classList.add('is-revealed');
          // Hiệu ứng chỉ chạy một lần. Cuộn lên xuống lại không làm nội dung ẩn rồi hiện lần nữa.
          observer.unobserve(entry.target);
        }
      },
      { threshold: REVEAL_VISIBLE_RATIO },
    );
    observer.observe(element);
    return () => observer.disconnect();
  }, [element]);

  return setElement;
}
