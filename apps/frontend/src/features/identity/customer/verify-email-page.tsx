import React, { useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { AuthCard } from './auth-ui';
import { ResultPanel } from '../../../shared/result-panel';

// Trang xác thực email, mở từ link trong thư đăng ký.
export function VerifyEmailPage() {
  const [searchParams] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();

  // Giữ token trong state vì effect bên dưới xóa token khỏi URL. State lấy token một lần lúc khởi tạo.
  const [token] = useState(() => searchParams.get('token') || '');

  // Ba trạng thái loại trừ nhau của lần xác thực: đang chờ, thành công, thất bại.
  const [loading, setLoading] = useState(false);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Token chỉ dùng được một lần, mà StrictMode chạy effect hai lần khi dev, nên ref giúp chỉ gửi một lần.
  // Không dùng cờ hủy kiểu cleanup, vì lần chạy đầu bị hủy mất kết quả mà lần hai bị ref chặn.
  const calledTokenRef = useRef<string | null>(null);

  // Xóa token khỏi thanh địa chỉ. Token không nằm lại trong lịch sử trình duyệt và không đi qua header Referer.
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

    apiRequest('/api/auth/email-verifications', {
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

  // Ba trạng thái kết quả tự có tiêu đề `h1` riêng. Chỉ lúc đang xác thực mới cần tiêu đề của thẻ.
  const showsResult = !token || success || Boolean(error);

  return (
    <AuthCard title={showsResult ? undefined : 'Xác thực email'}>
      <div className="auth-result">
        {!token && (
          <ResultPanel
            headingLevel={1}
            tone="error"
            title="Liên kết không hợp lệ"
            description="Liên kết xác thực thiếu mã token. Vui lòng kiểm tra lại liên kết trong email của bạn."
            announceDescription
            actions={
              <Link to="/" className="btn btn-outline btn-block">
                Quay lại trang chủ
              </Link>
            }
          />
        )}

        {loading && (
          <p role="status" className="empty-state">
            Đang xác thực địa chỉ email của bạn...
          </p>
        )}

        {!loading && success && (
          <ResultPanel
            headingLevel={1}
            tone="success"
            title="Email đã được xác thực"
            description="Chúc mừng! Địa chỉ email của bạn đã được xác thực thành công."
            announceDescription
            actions={
              <Link to="/" className="btn btn-primary btn-block">
                Về trang chủ mua sắm
              </Link>
            }
          />
        )}

        {!loading && error && (
          <ResultPanel
            headingLevel={1}
            tone="error"
            title="Xác thực không thành công"
            description={error}
            announceDescription
            actions={
              <Link to="/" className="btn btn-outline btn-block">
                Quay lại trang chủ
              </Link>
            }
          />
        )}
      </div>
    </AuthCard>
  );
}
