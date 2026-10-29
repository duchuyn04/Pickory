import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { ApiError } from '../../../../shared/api';
import { ErrorAlert } from '../../../../shared/error-alert';
import {
  FieldError,
  fieldErrorProps,
  hasFieldErrors,
  type FieldErrors,
} from '../../../../shared/field-error';
import { AuthCard } from '../../../identity/customer/auth-ui';
import {
  getLookupEmailError,
  getOrderCodeError,
  normalizeOrderCode,
} from './order-lookup-validation';
import { requestOrderLookup } from './orders-api';

type LookupField = 'orderCode' | 'email';

const RATE_LIMIT_MESSAGE = 'Bạn đã tra cứu quá nhiều lần. Vui lòng thử lại sau 15 phút.';
const NOT_FOUND_MESSAGE = 'Không tìm thấy đơn hàng với mã và email này.';
const GENERIC_ERROR_MESSAGE = 'Đã có lỗi xảy ra. Vui lòng thử lại.';

// Đọc lỗi theo ô từ `details` của lỗi 400. Khóa lạ bị bỏ qua vì không có ô nào để gắn.
function readServerFieldErrors(error: ApiError): FieldErrors<LookupField> {
  const serverFieldErrors: FieldErrors<LookupField> = {};
  for (const detail of error.details ?? []) {
    if (detail.key === 'orderCode' || detail.key === 'email') {
      serverFieldErrors[detail.key] = detail.message;
    }
  }
  return serverFieldErrors;
}

// Trang công khai để khách tra cứu đơn bằng mã đơn và email, rồi mở đơn ngay.
export function OrderLookupPage() {
  const navigate = useNavigate();
  const [orderCode, setOrderCode] = useState('');
  const [email, setEmail] = useState('');
  const [fieldErrors, setFieldErrors] = useState<FieldErrors<LookupField>>({});
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(event: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    event.preventDefault();
    // Đang có request chạy thì bỏ qua lần gửi thứ hai, kể cả khi gửi bằng phím Enter.
    if (loading) return;
    setError(null);

    // Kiểm định dạng ở trình duyệt. Request sai định dạng không tốn lượt trong giới hạn tần suất của server.
    const nextFieldErrors = {
      orderCode: getOrderCodeError(orderCode),
      email: getLookupEmailError(email),
    };
    setFieldErrors(nextFieldErrors);
    if (hasFieldErrors(nextFieldErrors)) return;

    setLoading(true);
    try {
      // Gửi mã đơn đã chuẩn hóa chữ hoa và email đã cắt khoảng trắng, giống cách server so khớp.
      const result = await requestOrderLookup(normalizeOrderCode(orderCode), email.trim());
      // Token chỉ trả một lần. Chuyển ngay tới trang chi tiết đơn thay vì chờ email.
      navigate(`/orders/${encodeURIComponent(result.code)}?t=${encodeURIComponent(result.accessToken)}`);
    } catch (caughtError: unknown) {
      handleRequestError(caughtError);
    } finally {
      // Mở lại nút sau lỗi để khách sửa thông tin hoặc thử lại.
      setLoading(false);
    }
  }

  function handleRequestError(caughtError: unknown) {
    if (!(caughtError instanceof ApiError)) {
      setError(GENERIC_ERROR_MESSAGE);
      return;
    }
    // Giới hạn tần suất: hiện câu hướng dẫn thời gian chờ thay cho thông điệp của server.
    if (caughtError.status === 429) {
      setError(RATE_LIMIT_MESSAGE);
      return;
    }
    // Không tìm thấy đơn: server không nói mã đơn hay email sai. Trang cũng giữ câu chung.
    if (caughtError.status === 404) {
      setError(NOT_FOUND_MESSAGE);
      return;
    }
    // Server vẫn kiểm định dạng lần nữa. Gắn từng lỗi vào đúng ô thay vì hiện một lỗi chung.
    if (caughtError.status === 400 && caughtError.code === 'VALIDATION_ERROR') {
      const serverFieldErrors = readServerFieldErrors(caughtError);
      if (hasFieldErrors(serverFieldErrors)) {
        setFieldErrors(serverFieldErrors);
        return;
      }
    }
    // Lỗi mạng và lỗi 5xx hiện chung bằng thông điệp của `apiRequest`.
    setError(caughtError.message);
  }

  return (
    <AuthCard
      title="Tra cứu đơn hàng"
      subtitle="Nhập mã đơn và email đặt hàng để xem đơn ngay."
    >
      {error && <ErrorAlert message={error} />}

      <form noValidate onSubmit={handleSubmit} className="stack">
        <div className="field">
          <label htmlFor="order-lookup-code">Mã đơn hàng</label>
          <input
            id="order-lookup-code"
            type="text"
            required
            autoComplete="off"
            autoCapitalize="characters"
            spellCheck={false}
            value={orderCode}
            onChange={(event) => setOrderCode(event.target.value)}
            placeholder="PKXXXXXXXX"
            {...fieldErrorProps('order-lookup-code', fieldErrors.orderCode)}
          />
          <FieldError inputId="order-lookup-code" message={fieldErrors.orderCode} />
        </div>

        <div className="field">
          <label htmlFor="order-lookup-email">Email đặt hàng</label>
          <input
            id="order-lookup-email"
            type="email"
            required
            autoComplete="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            placeholder="ban@example.com"
            {...fieldErrorProps('order-lookup-email', fieldErrors.email)}
          />
          <FieldError inputId="order-lookup-email" message={fieldErrors.email} />
        </div>

        <button type="submit" className="btn btn-primary btn-block" disabled={loading}>
          {loading ? 'Đang tra cứu...' : 'Xem đơn hàng'}
        </button>
      </form>

      <p className="auth-footer">
        <Link to="/">Về trang chủ</Link>
      </p>
    </AuthCard>
  );
}
