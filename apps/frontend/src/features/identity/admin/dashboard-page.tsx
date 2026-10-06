import { useActor } from '../hooks/use-actor';

// Trang chào mừng khi vào /admin, hiện vai trò của nhân viên đang đăng nhập.
export function AdminDashboardPage() {
  const { data: actor } = useActor();

  return (
    <div>
      <h1
        style={{
          fontSize: 'var(--text-xl)',
          color: 'var(--color-ink)',
          margin: '0 0 var(--space-4) 0',
        }}
      >
        Chào mừng trở lại, {actor?.roles.join(', ') || 'Nhân viên'}!
      </h1>
      <div
        style={{
          backgroundColor: 'var(--color-surface)',
          border: '1px solid var(--color-border)',
          borderRadius: 'var(--radius-card)',
          padding: 'var(--space-5)',
        }}
      >
        <p style={{ margin: 0, color: 'var(--color-ink-muted)', fontSize: 'var(--text-sm)' }}>
          Đây là bảng điều khiển back-office tối thiểu của Pickory. Hãy chọn một trang trong menu
          theo quyền được cấp.
        </p>
      </div>
    </div>
  );
}
