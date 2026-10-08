import { useQueryClient } from '@tanstack/react-query';
import React, { useEffect, useState } from 'react';
import { Link, Navigate } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { ChangePasswordForm } from '../components/change-password-form';
import { DeleteAccountDialog } from '../components/delete-account-dialog';
import { useActor } from '../hooks/use-actor';
import { ErrorAlert } from '../../../shared/error-alert';
import './account.css';

// Trang tài khoản của khách: sửa hồ sơ, đổi mật khẩu, quyền riêng tư, xóa tài khoản.
export function AccountPage() {
  const { data: actor, isLoading } = useActor();
  const queryClient = useQueryClient();

  // Giá trị đang gõ trong form, tách khỏi dữ liệu actor để người dùng sửa dở không bị ghi đè khi actor nạp lại.
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  // saving khóa nút trong lúc gửi để tránh bấm lưu hai lần.
  const [saving, setSaving] = useState(false);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    // Điền sẵn form bằng thông tin hiện tại, vì /api/me trả về tên và số điện thoại.
    if (actor) {
      setName(actor.name || '');
      setPhone(actor.phone || '');
    }
  }, [actor]);

  // Đang hỏi server xem ai đang đăng nhập; chưa biết thì chưa quyết định chuyển hướng, nếu không khách đã đăng nhập bị đẩy ra login oan.
  if (isLoading) {
    return (
      <main className="account-page">
        <p role="status" className="empty-state">
          Đang nạp thông tin...
        </p>
      </main>
    );
  }

  // Nhân viên có khu vực /admin riêng và việc xóa tài khoản ở đây chỉ dành cho khách, nên chỉ khách đã đăng nhập mới vào được.
  if (!actor || actor.kind !== 'customer') {
    return <Navigate to="/login" replace />;
  }

  async function handleUpdateProfile(e: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    e.preventDefault();
    setSaving(true);
    // Xóa kết quả của lần lưu trước để thông báo cũ không còn hiển thị cùng lúc với kết quả mới.
    setError(null);
    setSuccess(false);

    try {
      await apiRequest('/api/me', {
        method: 'PATCH',
        // Ô để trống gửi null để người dùng xóa được thông tin; gửi chuỗi rỗng thì backend cũng coi là xóa nhưng null rõ ràng hơn.
        body: JSON.stringify({
          name: name.trim() === '' ? null : name.trim(),
          phone: phone.trim() === '' ? null : phone.trim(),
        }),
      });
      // Nạp lại actor để header và các nơi khác hiển thị tên mới, thay vì giữ bản cũ trong cache.
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      setSuccess(true);
    } catch (err: unknown) {
      // Lỗi từ API đã có thông báo tiếng Việt nên hiện nguyên văn; lỗi lạ mới dùng câu chung.
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Cập nhật thông tin thất bại.');
      }
    } finally {
      // Luôn mở lại nút dù thành công hay lỗi.
      setSaving(false);
    }
  }

  return (
    <main className="account-page">
      <h1>Tài khoản của tôi</h1>

      {/* Khu vực cập nhật thông tin định danh cá nhân */}
      <section className="card account-section">
        <h2>Thông tin cá nhân</h2>

        {success && (
          <p role="status" className="alert alert-ok">
            Cập nhật thông tin thành công!
          </p>
        )}

        {error && <ErrorAlert message={error} />}

        <form noValidate onSubmit={handleUpdateProfile} className="stack">
          <div className="form-grid form-grid-2">
            <div className="field">
              <label htmlFor="acc-name">Họ và tên</label>
              <input
                id="acc-name"
                type="text"
                autoComplete="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Nhập họ và tên"
              />
            </div>

            <div className="field">
              <label htmlFor="acc-phone">Số điện thoại</label>
              <input
                id="acc-phone"
                type="tel"
                autoComplete="tel"
                value={phone}
                onChange={(e) => setPhone(e.target.value)}
                placeholder="0912 345 678"
              />
            </div>
          </div>

          <div>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? 'Đang lưu...' : 'Lưu thay đổi'}
            </button>
          </div>
        </form>
      </section>

      {/* Khu vực thay đổi mật khẩu đăng nhập */}
      <ChangePasswordForm />

      {/* Khu vực điều hướng tới cài đặt quyền riêng tư và xử lý dữ liệu */}
      <section className="card account-section account-row">
        <div>
          <h2>Quyền riêng tư &amp; Cookie</h2>
          <p className="muted small">
            Tùy chỉnh sự cho phép tính năng cá nhân hóa và thông báo tiếp thị.
          </p>
        </div>
        <Link to="/privacy" className="btn">
          Quản lý
        </Link>
      </section>

      {/* Khu vực thao tác xóa tài khoản và ẩn danh hóa dữ liệu */}
      <DeleteAccountDialog />
    </main>
  );
}
