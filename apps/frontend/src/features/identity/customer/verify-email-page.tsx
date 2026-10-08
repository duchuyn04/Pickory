import React, { useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { AuthCard } from './auth-ui';
import { ErrorAlert } from '../../../shared/error-alert';

// Trang xác thực email, mở từ link trong thư đăng ký.
export function VerifyEmailPage() {
  const [searchParams] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();

  // Giữ token trong state vì effect bên dưới sẽ xóa nó khỏi URL; lấy một lần lúc khởi tạo nên không đổi theo URL nữa.
  const [token] = useState(() => searchParams.get('token') || '');

  // Ba trạng thái loại trừ nhau của lần xác thực: đang chờ, thành công, thất bại.
  const [loading, setLoading] = useState(false);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Token chỉ dùng được một lần mà StrictMode chạy effect hai lần khi dev; ref (không mất khi effect chạy lại) giúp chỉ gửi một lần.
  // Không dùng cờ "đã hủy" kiểu cleanup, vì lần chạy đầu bị hủy sẽ làm mất kết quả mà lần chạy thứ hai lại bị ref chặn.
  const calledTokenRef = useRef<string | null>(null);

  // Xóa token khỏi thanh địa chỉ để nó không nằm lại trong lịch sử trình duyệt hay bị gửi đi qua header Referer.
  useEffect(() => {
    if (searchParams.has('token')) {
      navigate(location.pathname, { replace: true });
    }
  }, [searchParams, location.pathname, navigate]);

  // Tự gọi xác thực ngay khi mở trang, vì người dùng đã bấm link nên không cần thêm nút xác nhận.
  useEffect(() => {
    // Không có token, hoặc token này đã được gửi rồi (lần chạy thứ hai của StrictMode) thì không gọi nữa.
    if (!token || calledTokenRef.current === token) return;
    calledTokenRef.current = token;

    setLoading(true);
    setError(null);

    apiRequest('/api/auth/verify-email', {
      method: 'POST',
      body: JSON.stringify({ token }),
    })
      .then(() => {
        setSuccess(true);
      })
      .catch((err: unknown) => {
        // Hiện nguyên thông báo của API (link hết hạn, đã dùng...) vì đã là tiếng Việt.
        if (err instanceof Error) {
          setError(err.message);
        } else {
          setError('Xác thực email thất bại hoặc liên kết đã hết hạn.');
        }
      })
      .finally(() => {
        setLoading(false);
      });
  }, [token]);

  return (
    <AuthCard title="Xác thực email">
      <div className="auth-result">
        {!token && (
          <ErrorAlert message="Liên kết xác thực thiếu mã token. Vui lòng kiểm tra lại liên kết trong email của bạn." />
        )}

        {loading && (
          <p role="status" className="empty-state">
            Đang xác thực địa chỉ email của bạn...
          </p>
        )}

        {!loading && success && (
          <>
            <p role="status" className="alert alert-ok">
              Chúc mừng! Địa chỉ email của bạn đã được xác thực thành công.
            </p>
            <Link to="/" className="btn btn-primary">
              Về trang chủ mua sắm
            </Link>
          </>
        )}

        {!loading && error && (
          <>
            <ErrorAlert message={error} />
            <Link to="/" className="auth-link">
              ← Quay lại trang chủ
            </Link>
          </>
        )}
      </div>
    </AuthCard>
  );
}
