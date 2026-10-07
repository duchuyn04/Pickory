import { useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import {
  FieldError,
  fieldErrorProps,
  hasFieldErrors,
  type FieldErrors,
} from '../../../shared/field-error';
import { getConfirmPasswordError, getPasswordError } from '../../../shared/identity-validation';
import '../customer/account.css';

// Form đổi mật khẩu trong trang tài khoản.
export function ChangePasswordForm() {
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  // Ô nhập lại mật khẩu mới để người dùng không đặt nhầm do gõ sai mà không hay.
  const [confirmPassword, setConfirmPassword] = useState('');
  const [loading, setLoading] = useState(false);
  // Lỗi theo từng ô hiện ngay dưới ô sai; `error` chỉ còn giữ lỗi trả về từ API.
  const [fieldErrors, setFieldErrors] = useState<FieldErrors<'newPassword' | 'confirm'>>({});
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
    // Việc khớp với ô nhập lại chỉ kiểm được ở phía giao diện vì server không nhận ô này.
    const nextFieldErrors = {
      newPassword: getPasswordError(newPassword),
      confirm: getConfirmPasswordError(newPassword, confirmPassword),
    };
    setFieldErrors(nextFieldErrors);
    if (hasFieldErrors(nextFieldErrors)) return;

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
    <section className="card account-section">
      <h2>Đổi mật khẩu</h2>
      <p className="muted small">
        Sau khi đổi mật khẩu thành công, bạn sẽ được đăng xuất khỏi tất cả các thiết bị để đảm bảo
        an toàn.
      </p>

      {success && (
        <p role="status" className="alert alert-ok">
          Đổi mật khẩu thành công! Đang chuyển hướng về trang đăng nhập...
        </p>
      )}

      {error && (
        <p role="alert" className="alert">
          {error}
        </p>
      )}

      <form noValidate onSubmit={handleSubmit} className="stack">
        <div className="field">
          <label htmlFor="current-pw">Mật khẩu hiện tại</label>
          <input
            id="current-pw"
            type="password"
            autoComplete="current-password"
            value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)}
            placeholder="Nhập mật khẩu hiện tại"
          />
        </div>

        <div className="form-grid form-grid-2">
          <div className="field">
            <label htmlFor="new-pw">Mật khẩu mới (tối thiểu 8 ký tự)</label>
            <input
              id="new-pw"
              type="password"
              required
              autoComplete="new-password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              placeholder="Nhập mật khẩu mới"
              {...fieldErrorProps('new-pw', fieldErrors.newPassword)}
            />
            <FieldError inputId="new-pw" message={fieldErrors.newPassword} />
          </div>

          <div className="field">
            <label htmlFor="confirm-new-pw">Xác nhận mật khẩu mới</label>
            <input
              id="confirm-new-pw"
              type="password"
              required
              autoComplete="new-password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              placeholder="Nhập lại mật khẩu mới"
              {...fieldErrorProps('confirm-new-pw', fieldErrors.confirm)}
            />
            <FieldError inputId="confirm-new-pw" message={fieldErrors.confirm} />
          </div>
        </div>

        <div>
          <button type="submit" className="btn btn-primary" disabled={loading}>
            {loading ? 'Đang cập nhật...' : 'Cập nhật mật khẩu'}
          </button>
        </div>
      </form>
    </section>
  );
}
