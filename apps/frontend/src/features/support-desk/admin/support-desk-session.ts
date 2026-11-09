// Phiên đang mở ở khung chat của màn CSKH, do `SupportDeskPage` giữ và truyền xuống hai cột.
export interface OpenSupportSession {
  sessionId: string;
  conversationId: string;
  // Phiên mở từ tab Của tôi đang do người dùng giữ; mở từ tab Đang chờ thì chưa.
  isHeld: boolean;
  // Phiên đã kết thúc thì ô soạn tắt với chú thích riêng, dù trước đó từng giữ phiên.
  isClosed: boolean;
}
