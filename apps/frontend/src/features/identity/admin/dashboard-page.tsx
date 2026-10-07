import { useActor } from '../hooks/use-actor';

// Trang chào mừng khi vào /admin, hiện vai trò của nhân viên đang đăng nhập.
export function AdminDashboardPage() {
  const { data: actor } = useActor();

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Chào mừng trở lại, {actor?.roles.join(', ') || 'Nhân viên'}!</h1>
          <p>Tổng quan back-office của Pickory.</p>
        </div>
      </div>
      <div className="card">
        <p className="muted">
          Đây là bảng điều khiển back-office tối thiểu của Pickory. Hãy chọn một trang trong menu
          theo quyền được cấp.
        </p>
      </div>
    </div>
  );
}
