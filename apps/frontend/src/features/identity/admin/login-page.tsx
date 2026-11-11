import { useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { getEmailError, getPasswordError } from '../../../shared/identity-validation';
import { useActor } from '../hooks/use-actor';
import { ErrorAlert } from '../../../shared/error-alert';
import { getErrorMessage } from '../../../shared/error-message';
import { AuthSplitPage } from '../components/auth-split-page';
import { PasswordInput } from '../customer/auth-ui';

// Trang đăng nhập nhân viên, tách khỏi trang đăng nhập khách và không có nút đăng nhập Google.
export function StaffLoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { data: actor } = useActor();

  // Nhân viên đã đăng nhập mà vào lại trang này thì đưa vào khu quản trị, không hiện lại form đăng nhập vô nghĩa.
  React.useEffect(() => {
    if (actor?.kind === 'staff') {
      navigate('/admin', { replace: true });
    }
  }, [actor, navigate]);

  async function handleSubmit(e: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    e.preventDefault();
    setError(null);
    // Mật khẩu chỉ cần không rỗng (độ dài tối thiểu 1): quy tắc 8 ký tự chỉ áp khi đặt mật khẩu mới.
    const validationError = getEmailError(email) || getPasswordError(password, 1);
    if (validationError) {
      setError(validationError);
      return;
    }
    setLoading(true);

    try {
      await apiRequest('/api/admin/auth/sessions', {
        method: 'POST',
        body: JSON.stringify({ email, password }),
      });
      // Nạp lại actor để menu quản trị vẽ theo đúng quyền của nhân viên vừa đăng nhập.
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      // replace để nút Quay lại không đưa người dùng về trang đăng nhập.
      navigate('/admin', { replace: true });
    } catch (err: unknown) {
      // Hiện nguyên thông báo của API: sai email và sai mật khẩu cùng một câu, còn bị khóa thì có câu riêng.
      setError(getErrorMessage(err, 'Đã có lỗi xảy ra. Vui lòng thử lại.'));
    } finally {
      setLoading(false);
    }
  }

  return (
    <AuthSplitPage
      variant="admin"
      title="Đăng nhập quản trị"
      subtitle="Đăng nhập dành cho nhân viên quản trị"
    >
      {/* `role="alert"` để trình đọc màn hình đọc ngay lỗi đăng nhập vừa xuất hiện. */}
      {error && <ErrorAlert message={error} />}

      <form noValidate onSubmit={handleSubmit} className="auth-form">
        <div className="auth-fields">
          <div className="field">
            <label htmlFor="email">Email nhân viên</label>
            <input
              id="email"
              type="email"
              required
              autoComplete="username"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="nhanvien@pickory.local"
            />
          </div>

          <div className="field">
            <label htmlFor="password">Mật khẩu</label>
            <PasswordInput
              id="password"
              value={password}
              onChange={setPassword}
              autoComplete="current-password"
              placeholder="Nhập mật khẩu"
            />
          </div>
        </div>

        <button type="submit" className="btn btn-primary btn-block" disabled={loading}>
          {loading ? 'Đang đăng nhập...' : 'Đăng nhập'}
        </button>
      </form>

      <p className="auth-note">
        Tài khoản do quản trị viên cấp. Liên hệ quản trị viên nếu bạn quên mật khẩu.
      </p>
    </AuthSplitPage>
  );
}
