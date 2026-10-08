import { useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { getEmailError, getPasswordError } from '../../../shared/identity-validation';
import { useActor } from '../hooks/use-actor';
import { ErrorAlert } from '../../../shared/error-alert';
import './admin-login.css';

// Trang đăng nhập nhân viên, tách khỏi trang đăng nhập khách và không có nút đăng nhập Google.
export function StaffLoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  // Cho người dùng bật xem mật khẩu để kiểm tra gõ đúng.
  const [showPassword, setShowPassword] = useState(false);
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
      await apiRequest('/api/admin/auth/login', {
        method: 'POST',
        body: JSON.stringify({ email, password }),
      });
      // Nạp lại actor để menu quản trị vẽ theo đúng quyền của nhân viên vừa đăng nhập.
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      // replace để nút Quay lại không đưa người dùng về trang đăng nhập.
      navigate('/admin', { replace: true });
    } catch (err: unknown) {
      // Hiện nguyên thông báo của API: sai email và sai mật khẩu cùng một câu, còn bị khóa thì có câu riêng.
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Đã có lỗi xảy ra. Vui lòng thử lại.');
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="admin-login">
      <form noValidate onSubmit={handleSubmit} className="card admin-login-card">
        <div className="admin-login-brand">
          <span className="logo-mark" aria-hidden="true">
            P
          </span>
          <span>
            <h1>Pickory Back-Office</h1>
            <small>Đăng nhập dành cho nhân viên quản trị</small>
          </span>
        </div>

        {/* `role="alert"` để trình đọc màn hình đọc ngay lỗi đăng nhập vừa xuất hiện. */}
        {error && <ErrorAlert message={error} />}

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
          <div className="admin-login-password">
            <input
              id="password"
              type={showPassword ? 'text' : 'password'}
              required
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="Nhập mật khẩu"
            />
            <button
              type="button"
              className="btn btn-ghost btn-sm admin-login-toggle"
              onClick={() => setShowPassword(!showPassword)}
            >
              {showPassword ? 'Ẩn' : 'Hiện'}
            </button>
          </div>
        </div>

        <button type="submit" className="btn btn-primary btn-block" disabled={loading}>
          {loading ? 'Đang đăng nhập...' : 'Đăng nhập'}
        </button>
      </form>
    </main>
  );
}
