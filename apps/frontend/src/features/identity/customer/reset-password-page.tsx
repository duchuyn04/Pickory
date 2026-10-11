import React, { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '../../../shared/api';
import {
  FieldError,
  fieldErrorProps,
  hasFieldErrors,
  type FieldErrors,
} from '../../../shared/field-error';
import { getConfirmPasswordError, getPasswordError } from '../../../shared/identity-validation';
import { AuthCard, PasswordInput } from './auth-ui';
import { ErrorAlert } from '../../../shared/error-alert';

// Trang đặt mật khẩu mới từ link trong email; dùng chung cho quên mật khẩu và lời mời nhân viên.
export function ResetPasswordPage() {
  const [searchParams] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  // Giữ token trong state vì effect bên dưới xóa token khỏi URL. State lấy token một lần lúc khởi tạo.
  const [token] = useState(() => searchParams.get('token') || '');

  const [newPassword, setNewPassword] = useState('');
  // Ô nhập lại để người dùng không đặt nhầm mật khẩu do gõ sai.
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  // Server cho biết đây là khách hay nhân viên, để nút "Đăng nhập" dẫn tới đúng trang đăng nhập.
  const [userKind, setUserKind] = useState<'customer' | 'staff' | null>(null);
  // Lỗi theo từng ô hiện ngay dưới ô sai; `error` chỉ còn giữ lỗi trả về từ API.
  const [fieldErrors, setFieldErrors] = useState<FieldErrors<'password' | 'confirm'>>({});
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  // Xóa token khỏi thanh địa chỉ. Token không nằm lại trong lịch sử trình duyệt và không đi qua header Referer.
  useEffect(() => {
    if (searchParams.has('token')) {
      navigate(location.pathname, { replace: true });
    }
  }, [searchParams, location.pathname, navigate]);

  async function handleSubmit(e: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    e.preventDefault();
    setError(null);

    // Kiểm tra ở trình duyệt trước khi gửi. Token chỉ dùng được một lần, nên lỗi nhập liệu không được làm mất token.
    const nextFieldErrors = {
      password: getPasswordError(newPassword, 8),
      confirm: getConfirmPasswordError(newPassword, confirmPassword),
    };
    setFieldErrors(nextFieldErrors);
    if (hasFieldErrors(nextFieldErrors)) return;

    setLoading(true);

    try {
      const res = await apiRequest<{ success: boolean; userKind: 'customer' | 'staff' }>(
        '/api/auth/password',
        {
          method: 'PUT',
          body: JSON.stringify({ token, newPassword }),
        },
      );

      // Server đã xóa mọi phiên, nên xóa hẳn actor khỏi cache. Bản cũ còn đăng nhập sẽ làm trang đăng nhập tự chuyển đi.
      await queryClient.resetQueries({ queryKey: ['actor'], exact: true });

      setUserKind(res.userKind);
      setSubmitted(true);
    } catch (err: unknown) {
      // Hiện nguyên thông báo của API (link hết hạn, đã dùng...) vì đã là tiếng Việt.
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Đã có lỗi xảy ra. Vui lòng thử lại.');
      }
    } finally {
      setLoading(false);
    }
  }

  // Không có token thì không có gì để đặt lại, báo ngay thay vì hiện form không bao giờ thành công.
  if (!token) {
    return (
      <AuthCard title="Đặt lại mật khẩu">
        <div className="auth-result">
          <ErrorAlert message="Mã token không hợp lệ hoặc thiếu trong liên kết. Vui lòng kiểm tra lại liên kết trong email của bạn." />
          <Link to="/forgot-password" className="auth-link">
            ← Yêu cầu đặt lại mật khẩu mới
          </Link>
        </div>
      </AuthCard>
    );
  }

  return (
    <AuthCard title="Đặt lại mật khẩu" subtitle="Nhập mật khẩu mới cho tài khoản của bạn">
      {submitted ? (
        <div className="auth-result">
          <p role="status" className="alert alert-ok">
            Mật khẩu của bạn đã được cập nhật thành công! Mọi phiên đăng nhập cũ đã được đăng xuất
            an toàn.
          </p>
          {/* Nhân viên đặt mật khẩu từ lời mời nên quay về trang đăng nhập quản trị, không về trang của khách. */}
          <Link to={userKind === 'staff' ? '/admin/login' : '/login'} className="btn btn-primary">
            {userKind === 'staff' ? 'Đăng nhập trang quản trị' : 'Đăng nhập ngay'}
          </Link>
        </div>
      ) : (
        <>
          {error && <ErrorAlert message={error} />}

          <form noValidate onSubmit={handleSubmit} className="stack">
            <div className="field">
              <label htmlFor="reset-password">Mật khẩu mới (tối thiểu 8 ký tự)</label>
              {/* Một nút Hiện điều khiển cả hai ô, để người dùng so được hai lần gõ. */}
              <PasswordInput
                id="reset-password"
                value={newPassword}
                onChange={setNewPassword}
                autoComplete="new-password"
                placeholder="Nhập mật khẩu mới"
                visible={showPassword}
                onVisibleChange={setShowPassword}
                errorMessage={fieldErrors.password}
              />
              <FieldError inputId="reset-password" message={fieldErrors.password} />
            </div>

            <div className="field">
              <label htmlFor="reset-confirm">Xác nhận mật khẩu mới</label>
              <input
                id="reset-confirm"
                type={showPassword ? 'text' : 'password'}
                required
                autoComplete="new-password"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                placeholder="Nhập lại mật khẩu mới"
                {...fieldErrorProps('reset-confirm', fieldErrors.confirm)}
              />
              <FieldError inputId="reset-confirm" message={fieldErrors.confirm} />
            </div>

            <button type="submit" className="btn btn-primary btn-block" disabled={loading}>
              {loading ? 'Đang cập nhật...' : 'Cập nhật mật khẩu'}
            </button>
          </form>
        </>
      )}
    </AuthCard>
  );
}
