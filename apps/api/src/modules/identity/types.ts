import type { Permission } from './permissions';

// Lựa chọn đồng ý thu thập dữ liệu của người dùng.
export interface ConsentState {
  // Cho phép ghi hành vi và cá nhân hóa; mặc định tắt cho tới khi người dùng chọn.
  personalization: boolean;
  // Cho phép gửi email khuyến mãi; mặc định tắt.
  marketing: boolean;
}

// Người đang gửi request, do actorMiddleware gắn vào req.actor.
export interface Actor {
  // Luôn có, lấy từ cookie pk_anon, để gộp dữ liệu khách vãng lai khi họ đăng nhập sau này.
  anonymousId: string;
  // null khi chưa đăng nhập.
  userId: string | null;
  // Phân loại để module khác quyết định cách kiểm tra quyền (khách kiểm theo quyền sở hữu, nhân viên theo permission).
  kind: 'guest' | 'customer' | 'staff';
  // Mã các role người này giữ, dùng cho quy tắc riêng của admin.
  roles: string[];
  // Hợp quyền của mọi role, nạp lại mỗi request nên đổi quyền có hiệu lực ngay.
  permissions: ReadonlySet<Permission>;
  consent: ConsentState;
  // Thời điểm tạo phiên hiện tại, dùng kiểm tra "vừa đăng nhập lại" khi xóa tài khoản chỉ dùng Google.
  sessionCreatedAt?: Date;
}

/* eslint-disable @typescript-eslint/no-namespace */
// Express không cho khai báo thêm thuộc tính cho Request bằng cách khác ngoài mở rộng namespace toàn cục.
declare global {
  namespace Express {
    interface Request {
      // Gắn bởi actorMiddleware nên mọi handler phía sau đều có actor.
      actor: Actor;
    }
  }
}
/* eslint-enable @typescript-eslint/no-namespace */
