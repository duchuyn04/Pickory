import { useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiRequest } from '../../shared/api';
import { getPasswordError } from '../../shared/identity-validation';

// Form đổi mật khẩu trong trang tài khoản.
export function ChangePasswordForm() {
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  // Ô nhập lại mật khẩu mới để người dùng không đặt nhầm do gõ sai mà không hay.
  const [confirmPassword, setConfirmPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

  const queryClient = useQueryClient();
  const navigate = useNavigate();

  async function handleSubmit(e: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    e.preventDefault();
    setError(null);
    setSuccess(false);

    // Kiểm tra ở trình duyệt để báo lỗi tiếng Việt ngay, không tốn một lượt gọi API; server vẫn kiểm tra lại.
    const validationError = getPasswordError(newPassword);
    if (validationError) {
      setError(validationError);
      return;
    }

    // Việc khớp với ô nhập lại chỉ kiểm được ở phía giao diện vì server không nhận ô này.
    if (newPassword !== confirmPassword) {
      setError('Mật khẩu xác nhận không khớp.');
      return;
    }

    setLoading(true);

    try {
      await apiRequest('/api/me/password', {
        method: 'POST',
        // Chuỗi rỗng đổi thành undefined để không gửi trường này, vì tài khoản chỉ dùng Google không có mật khẩu cũ.
        body: JSON.stringify({
          currentPassword: currentPassword || undefined,
          newPassword,
        }),
      });

      setSuccess(true);
      // Xóa các ô mật khẩu khỏi bộ nhớ trang ngay sau khi gửi xong.
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');

      // Server đã xóa mọi phiên, nên nạp lại actor để giao diện biết mình không còn đăng nhập.
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      // Đổi mật khẩu thành công sẽ thu hồi toàn bộ phiên trong DB, chuyển hướng về trang đăng nhập sau 1.5 giây để người dùng kịp đọc thông báo thành công.
      setTimeout(() => {
        navigate('/login', { replace: true });
      }, 1500);
    } catch (err: unknown) {
      // Hiện nguyên thông báo của API (ví dụ sai mật khẩu hiện tại) vì đã là tiếng Việt.
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Đổi mật khẩu thất bại. Vui lòng thử lại.');
      }
    } finally {
      // Luôn mở lại nút dù thành công hay lỗi.
      setLoading(false);
    }
  }

  return (
    <div
      style={{
        backgroundColor: 'var(--color-surface)',
        borderRadius: 'var(--radius-card)',
        border: '1px solid var(--color-border)',
        padding: 'var(--space-6)',
      }}
    >
      <h2
        style={{
          fontSize: 'var(--text-lg)',
          fontWeight: 700,
          margin: '0 0 var(--space-2) 0',
          color: 'var(--color-ink)',
        }}
      >
        Đổi mật khẩu
      </h2>
      <p
        style={{
          fontSize: 'var(--text-sm)',
          color: 'var(--color-ink-muted)',
          margin: '0 0 var(--space-4) 0',
        }}
      >
        Sau khi đổi mật khẩu thành công, bạn sẽ được đăng xuất khỏi tất cả các thiết bị để đảm bảo
        an toàn.
      </p>

      {success && (
        <div
          style={{
            backgroundColor: '#e6f4ea',
            border: '1px solid var(--color-success)',
            color: 'var(--color-success)',
            padding: 'var(--space-3)',
            borderRadius: 'var(--radius-control)',
            fontSize: 'var(--text-sm)',
            marginBottom: 'var(--space-4)',
          }}
        >
          Đổi mật khẩu thành công! Đang chuyển hướng về trang đăng nhập...
        </div>
      )}

      {error && (
        <div
          style={{
            backgroundColor: '#fee4e2',
            border: '1px solid var(--color-danger)',
            color: 'var(--color-danger)',
            padding: 'var(--space-3)',
            borderRadius: 'var(--radius-control)',
            fontSize: 'var(--text-sm)',
            marginBottom: 'var(--space-4)',
          }}
        >
          {error}
        </div>
      )}

      <form
        noValidate
        onSubmit={handleSubmit}
        style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-4)' }}
      >
        <div>
          <label
            htmlFor="current-pw"
            style={{
              display: 'block',
              fontSize: 'var(--text-sm)',
              fontWeight: 600,
              color: 'var(--color-ink)',
              marginBottom: 'var(--space-1)',
            }}
          >
            Mật khẩu hiện tại
          </label>
          <input
            id="current-pw"
            type="password"
            value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)}
            placeholder="Nhập mật khẩu hiện tại"
            style={{
              width: '100%',
              boxSizing: 'border-box',
              padding: 'var(--space-3)',
              borderRadius: 'var(--radius-control)',
              border: '1px solid var(--color-border)',
              fontSize: 'var(--text-md)',
              outline: 'none',
            }}
          />
        </div>

        <div>
          <label
            htmlFor="new-pw"
            style={{
              display: 'block',
              fontSize: 'var(--text-sm)',
              fontWeight: 600,
              color: 'var(--color-ink)',
              marginBottom: 'var(--space-1)',
            }}
          >
            Mật khẩu mới (tối thiểu 8 ký tự)
          </label>
          <input
            id="new-pw"
            type="password"
            required
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            placeholder="Nhập mật khẩu mới"
            style={{
              width: '100%',
              boxSizing: 'border-box',
              padding: 'var(--space-3)',
              borderRadius: 'var(--radius-control)',
              border: '1px solid var(--color-border)',
              fontSize: 'var(--text-md)',
              outline: 'none',
            }}
          />
        </div>

        <div>
          <label
            htmlFor="confirm-new-pw"
            style={{
              display: 'block',
              fontSize: 'var(--text-sm)',
              fontWeight: 600,
              color: 'var(--color-ink)',
              marginBottom: 'var(--space-1)',
            }}
          >
            Xác nhận mật khẩu mới
          </label>
          <input
            id="confirm-new-pw"
            type="password"
            required
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            placeholder="Nhập lại mật khẩu mới"
            style={{
              width: '100%',
              boxSizing: 'border-box',
              padding: 'var(--space-3)',
              borderRadius: 'var(--radius-control)',
              border: '1px solid var(--color-border)',
              fontSize: 'var(--text-md)',
              outline: 'none',
            }}
          />
        </div>

        <button
          type="submit"
          disabled={loading}
          style={{
            alignSelf: 'flex-start',
            backgroundColor: 'var(--color-brand)',
            color: '#ffffff',
            border: 'none',
            padding: 'var(--space-3) var(--space-5)',
            borderRadius: 'var(--radius-control)',
            fontSize: 'var(--text-md)',
            fontWeight: 600,
            cursor: loading ? 'not-allowed' : 'pointer',
            opacity: loading ? 0.7 : 1,
          }}
        >
          {loading ? 'Đang cập nhật...' : 'Cập nhật mật khẩu'}
        </button>
      </form>
    </div>
  );
}
