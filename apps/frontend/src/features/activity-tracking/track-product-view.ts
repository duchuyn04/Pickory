import { apiRequest } from '../../shared/api';

// Báo cho API biết khách vừa xem một sản phẩm. Hàm không bao giờ reject, vì thống kê hỏng không được làm hỏng trang.
export async function trackProductView(): Promise<void> {
  try {
    await apiRequest<void>('/api/events', {
      method: 'POST',
      body: JSON.stringify({ type: 'product_view' }),
      // Trình duyệt vẫn gửi xong request khi khách rời trang ngay sau khi xem.
      keepalive: true,
    });
  } catch {
    // Nuốt mọi lỗi và không tự ghi console. Một lượt xem bị lỡ không đáng làm phiền khách.
  }
}
