import React, { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { Icon } from '../../../shared/icons';
import { FieldError, fieldErrorProps } from '../../../shared/field-error';
import { getEmailError, getNameError } from '../../../shared/identity-validation';
import { useDialogFocus } from '../../../shared/use-dialog-focus';
import type { RoleData } from './role-form';
import { ErrorAlert } from '../../../shared/error-alert';

interface InviteDialogProps {
  availableRoles: RoleData[];
  // Cha nạp lại danh sách nhân viên sau khi mời xong.
  onSuccess: () => void;
  onClose: () => void;
}

// Hộp thoại mời nhân viên mới bằng email, tên và role.
export function InviteDialog({ availableRoles, onSuccess, onClose }: InviteDialogProps) {
  // Hook đưa focus vào hộp thoại khi mở, đóng bằng phím Esc, và trả focus về nút đã mở.
  const dialogRef = useDialogFocus({ onClose });
  const [email, setEmail] = useState('');
  const [name, setName] = useState('');
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Lỗi của ô tên hiện ngay dưới ô. Các lỗi còn lại hiện ở thông báo chung đầu form.
  const [nameError, setNameError] = useState<string | null>(null);

  // Ẩn role customer vì tài khoản khách và nhân viên tách riêng, server cũng từ chối gán.
  const staffAssignableRoles = availableRoles.filter((r) => r.code !== 'customer');

  // Bấm một role thì chọn, bấm lần nữa thì bỏ chọn.
  function toggleRole(code: string) {
    if (selectedRoles.includes(code)) {
      setSelectedRoles(selectedRoles.filter((r) => r !== code));
    } else {
      setSelectedRoles([...selectedRoles, code]);
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    e.preventDefault();
    setError(null);
    setNameError(null);

    // Kiểm tra ở trình duyệt để báo lỗi tiếng Việt ngay; server vẫn kiểm tra lại mọi thứ.
    const emailErr = getEmailError(email);
    if (emailErr) {
      setError(emailErr);
      return;
    }

    // Tên bắt buộc khi mời nhân viên, khác với hồ sơ khách. Luật độ dài và ký tự dùng chung với backend.
    const nextNameError = name.trim() ? getNameError(name) : 'Họ và tên không được để trống.';
    if (nextNameError) {
      setNameError(nextNameError);
      return;
    }

    // Nhân viên không có role nào thì không làm được gì, nên bắt chọn ít nhất một.
    if (selectedRoles.length === 0) {
      setError('Vui lòng chọn ít nhất một vai trò cho nhân viên.');
      return;
    }

    setLoading(true);

    try {
      await apiRequest('/api/admin/staff/invitations', {
        method: 'POST',
        // trim để khoảng trắng thừa khi dán email không làm email sai.
        body: JSON.stringify({
          email: email.trim(),
          name: name.trim(),
          roleCodes: selectedRoles,
        }),
      });
      // onSuccess để trang cha nạp lại danh sách, rồi mới đóng.
      onSuccess();
      onClose();
    } catch (err: unknown) {
      // Hiện nguyên thông báo của API (email đã dùng, không đủ quyền để mời vào role này...) vì đã là tiếng Việt.
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Gửi lời mời thất bại.');
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="dialog-backdrop">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="invite-dialog-title"
        tabIndex={-1}
        className="dialog"
      >
        <div className="dialog-head">
          <h2 id="invite-dialog-title">Mời nhân viên mới</h2>
          <button type="button" className="icon-btn" aria-label="Đóng" onClick={onClose}>
            <Icon name="close" />
          </button>
        </div>

        <div className="dialog-body">
          <form id="invite-form" noValidate onSubmit={handleSubmit} className="stack">
            <p className="muted small">
              Hệ thống sẽ gửi email chứa liên kết thiết lập mật khẩu (có hiệu lực 72 giờ) tới nhân
              viên.
            </p>

            {error && <ErrorAlert message={error} />}

            <div className="field">
              <label htmlFor="inv-email">Email nhân viên *</label>
              <input
                id="inv-email"
                type="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="nhanvien@pickory.local"
              />
            </div>

            <div className="field">
              <label htmlFor="inv-name">Họ và tên *</label>
              <input
                id="inv-name"
                type="text"
                required
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Nguyễn Văn A"
                {...fieldErrorProps('inv-name', nameError)}
              />
              <FieldError inputId="inv-name" message={nameError} />
            </div>

            {/* `fieldset` và `legend` đặt tên cho cả nhóm checkbox, để trình đọc màn hình đọc đúng câu hỏi. */}
            <fieldset className="role-picker">
              <legend>Vai trò (Role) *</legend>
              {staffAssignableRoles.map((role) => (
                <label key={role.code} className="check">
                  <input
                    type="checkbox"
                    checked={selectedRoles.includes(role.code)}
                    onChange={() => toggleRole(role.code)}
                  />
                  <span>
                    <strong>{role.name}</strong> ({role.code})
                  </span>
                </label>
              ))}
            </fieldset>
          </form>
        </div>

        <div className="dialog-foot">
          <button type="button" className="btn" onClick={onClose}>
            Hủy
          </button>
          {/* Khóa nút gửi khi đang gửi để một lần bấm không tạo hai lời mời. */}
          <button type="submit" form="invite-form" className="btn btn-primary" disabled={loading}>
            {loading ? 'Đang gửi lời mời...' : 'Gửi lời mời'}
          </button>
        </div>
      </div>
    </div>
  );
}
