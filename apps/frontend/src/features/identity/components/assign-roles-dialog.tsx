import React, { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { Icon } from '../../../shared/icons';
import { useDialogFocus } from '../../../shared/use-dialog-focus';
import type { RoleData } from './role-form';

// Một dòng nhân viên như GET /api/admin/staff trả về; roles là mã role chứ không phải đối tượng.
export interface StaffRow {
  id: string;
  email: string;
  name: string | null;
  phone: string | null;
  emailVerified: boolean;
  kind: string;
  // Có giá trị nghĩa là đang bị khóa.
  lockedAt: string | null;
  createdAt: string;
  roles: string[];
}

interface AssignRolesDialogProps {
  // null nghĩa là hộp thoại đang đóng.
  staff: StaffRow | null;
  availableRoles: RoleData[];
  onSuccess: () => void;
  onClose: () => void;
}

// Hộp thoại đổi role của một nhân viên.
export function AssignRolesDialog({
  staff,
  availableRoles,
  onSuccess,
  onClose,
}: AssignRolesDialogProps) {
  // Hook đưa focus vào hộp thoại khi mở, đóng bằng phím Esc, và trả focus về nút đã mở.
  const dialogRef = useDialogFocus({
    isOpen: Boolean(staff),
    onClose,
  });

  // Bắt đầu bằng role hiện có của nhân viên để người dùng chỉ phải thêm hoặc bớt.
  const [selectedRoles, setSelectedRoles] = useState<string[]>(() => staff?.roles || []);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Hook ở trên phải chạy trước dòng này, vì không được gọi hook sau một lệnh return sớm.
  if (!staff) return null;
  // Ẩn role customer vì tài khoản khách và nhân viên tách riêng, server cũng từ chối gán.
  const staffAssignableRoles = availableRoles.filter((r) => r.code !== 'customer');

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
    setLoading(true);

    try {
      // PUT đặt lại toàn bộ role bằng danh sách này; danh sách rỗng nghĩa là gỡ hết role (phiên của người đó bị xóa).
      await apiRequest(`/api/admin/staff/${staff!.id}/roles`, {
        method: 'PUT',
        body: JSON.stringify({
          roleCodes: selectedRoles,
        }),
      });
      // onSuccess để trang cha nạp lại danh sách, rồi mới đóng.
      onSuccess();
      onClose();
    } catch (err: unknown) {
      // Hiện nguyên thông báo của API (tự sửa role mình, admin cuối cùng, không đủ quyền...) vì đã là tiếng Việt.
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Gán vai trò thất bại.');
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
        aria-labelledby="assign-roles-dialog-title"
        tabIndex={-1}
        className="dialog"
      >
        <div className="dialog-head">
          <h2 id="assign-roles-dialog-title">Gán vai trò cho: {staff.name || staff.email}</h2>
          <button type="button" className="icon-btn" aria-label="Đóng" onClick={onClose}>
            <Icon name="close" />
          </button>
        </div>

        <div className="dialog-body">
          <form id="assign-roles-form" noValidate onSubmit={handleSubmit} className="stack">
            <p className="muted small">
              Tài khoản: <strong>{staff.email}</strong>. Chọn các vai trò áp dụng cho nhân viên này.
            </p>

            {error && (
              <p role="alert" className="alert">
                {error}
              </p>
            )}

            {/* `fieldset` và `legend` đặt tên cho cả nhóm checkbox, để trình đọc màn hình đọc đúng câu hỏi. */}
            <fieldset className="role-picker">
              <legend>Vai trò (Role)</legend>
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
          {/* Khóa nút lưu khi đang gửi để một lần bấm không gửi hai yêu cầu đổi role. */}
          <button
            type="submit"
            form="assign-roles-form"
            className="btn btn-primary"
            disabled={loading}
          >
            {loading ? 'Đang lưu...' : 'Lưu vai trò'}
          </button>
        </div>
      </div>
    </div>
  );
}
