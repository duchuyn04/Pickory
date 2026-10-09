// Bổ sung các API DOM bị thiếu trong jsdom phục vụ ProseMirror và Tiptap.

if (typeof Range !== 'undefined') {
  // ProseMirror gọi getClientRects để tính tọa độ con trỏ và vùng chọn.
  // jsdom không có engine dàn trang nên trả về danh sách rỗng.
  if (!Range.prototype.getClientRects) {
    Range.prototype.getClientRects = () => [] as unknown as DOMRectList;
  }

  // ProseMirror gọi getBoundingClientRect để đo kích thước vùng chọn văn bản.
  // jsdom không dựng hình thật nên trả về khung hình chữ nhật kích thước 0.
  if (!Range.prototype.getBoundingClientRect) {
    Range.prototype.getBoundingClientRect = () => ({
      bottom: 0,
      height: 0,
      left: 0,
      right: 0,
      top: 0,
      width: 0,
      x: 0,
      y: 0,
      toJSON: () => {},
    });
  }
}

if (typeof Element !== 'undefined' && !Element.prototype.scrollIntoView) {
  // ProseMirror gọi scrollIntoView khi di chuyển con trỏ hoặc cuộn trang.
  // jsdom không có giao diện hiển thị nên cung cấp hàm rỗng chống lỗi.
  Element.prototype.scrollIntoView = () => {};
}

if (typeof globalThis !== 'undefined' && !globalThis.requestAnimationFrame) {
  // Tiptap delayedFocus dùng requestAnimationFrame để trì hoãn việc focus vào DOM.
  // jsdom thiếu API này nên giả lập bằng hàm hẹn giờ để kích hoạt sự kiện focus.
  globalThis.requestAnimationFrame = (callback: FrameRequestCallback) => {
    return setTimeout(callback, 0) as unknown as number;
  };
  globalThis.cancelAnimationFrame = (id: number) => {
    clearTimeout(id);
  };
}
