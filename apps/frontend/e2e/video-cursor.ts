import type { BrowserContext } from '@playwright/test';

// Màu và cỡ của vòng đỏ quanh con trỏ. Vòng đủ lớn để thấy rõ trong video 1280 px mà không che nút.
const RING_SIZE_PX = 40;
const RING_COLOR = 'rgba(230, 30, 30, 0.95)';
const RING_FILL = 'rgba(230, 30, 30, 0.22)';
// Vòng gợn sóng nở ra khi bấm. Thời gian đủ dài để một khung hình video bắt được.
const RIPPLE_DURATION_MS = 650;

// Gắn vòng tròn đỏ đi theo con trỏ và vòng gợn sóng khi bấm, giống phần mềm quay màn hình.
// Chỉ gọi cho context quay video. Không gọi cho test có assert đếm phần tử hoặc so ảnh chụp.
export async function installClickHighlight(context: BrowserContext): Promise<void> {
  await context.addInitScript(
    ({ ringSize, ringColor, ringFill, rippleDuration }) => {
      // Mỗi tài liệu chỉ gắn một lần. Script này chạy lại ở mỗi lần điều hướng và mỗi khung con.
      const marker = '__clickHighlightInstalled';
      const host = window as unknown as Record<string, boolean>;
      if (host[marker]) return;
      host[marker] = true;

      let layer: HTMLElement | null = null;
      let ring: HTMLElement | null = null;

      // Tạo lớp phủ khi có sự kiện chuột đầu tiên, vì lúc script chạy trang chưa có `documentElement`.
      function ensureLayer(): { layer: HTMLElement; ring: HTMLElement } | null {
        if (layer && ring && layer.isConnected) return { layer, ring };
        const root = document.documentElement;
        if (!root) return null;

        // Dùng shadow root đóng để locator và selector của test không thấy lớp phủ này.
        const shadowHost = document.createElement('div');
        shadowHost.setAttribute('aria-hidden', 'true');
        shadowHost.style.cssText = 'position:fixed;inset:0;pointer-events:none;z-index:2147483647;';
        const shadow = shadowHost.attachShadow({ mode: 'closed' });

        // `pointer-events:none` giữ để lớp phủ không chặn thao tác bấm của test.
        layer = document.createElement('div');
        layer.style.cssText = 'position:fixed;inset:0;pointer-events:none;';
        ring = document.createElement('div');
        ring.style.cssText =
          `position:absolute;left:0;top:0;width:${ringSize}px;height:${ringSize}px;` +
          `margin:${-ringSize / 2}px 0 0 ${-ringSize / 2}px;border-radius:50%;` +
          `border:4px solid ${ringColor};background:${ringFill};` +
          'box-sizing:border-box;opacity:0;';
        layer.appendChild(ring);
        shadow.appendChild(layer);
        root.appendChild(shadowHost);
        return { layer, ring };
      }

      // Đưa vòng đến vị trí con trỏ. Vòng ẩn cho tới khi chuột di chuyển lần đầu.
      function moveRing(x: number, y: number) {
        const parts = ensureLayer();
        if (!parts) return;
        parts.ring.style.opacity = '1';
        parts.ring.style.transform = `translate(${x}px, ${y}px)`;
      }

      // Vòng gợn sóng nở ra rồi mờ dần ở chỗ bấm. Phần tử tự xóa khi hoạt ảnh kết thúc.
      function showRipple(x: number, y: number) {
        const parts = ensureLayer();
        if (!parts) return;
        const ripple = document.createElement('div');
        ripple.style.cssText =
          `position:absolute;left:${x}px;top:${y}px;width:${ringSize}px;height:${ringSize}px;` +
          `margin:${-ringSize / 2}px 0 0 ${-ringSize / 2}px;border-radius:50%;` +
          `border:5px solid ${ringColor};box-sizing:border-box;`;
        parts.layer.appendChild(ripple);
        const animation = ripple.animate(
          [
            { transform: 'scale(0.6)', opacity: 0.95 },
            { transform: 'scale(2.4)', opacity: 0 },
          ],
          { duration: rippleDuration, easing: 'ease-out' },
        );
        animation.onfinish = () => ripple.remove();
      }

      // Bắt ở pha capture để trang không chặn được sự kiện bằng `stopPropagation`.
      document.addEventListener(
        'mousemove',
        (event) => moveRing(event.clientX, event.clientY),
        true,
      );
      document.addEventListener(
        'mousedown',
        (event) => {
          moveRing(event.clientX, event.clientY);
          showRipple(event.clientX, event.clientY);
        },
        true,
      );
    },
    {
      ringSize: RING_SIZE_PX,
      ringColor: RING_COLOR,
      ringFill: RING_FILL,
      rippleDuration: RIPPLE_DURATION_MS,
    },
  );
}
