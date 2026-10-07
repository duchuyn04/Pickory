import React, { useState } from 'react';
import { Icon } from '../../../shared/icons';
import { useDialogFocus } from '../../../shared/use-dialog-focus';
import { PermissionGrid, type PermissionItem } from './permission-grid';

export interface RoleData {
  // Không có id nghĩa là role chưa được tạo.
  id?: string;
  code: string;
  name: string;
  // Role hệ thống (admin, customer) không sửa hay xóa được.
  isSystem: boolean;
  permissions: string[];
}

interface RoleFormProps {
  // Không truyền role nghĩa là đang tạo mới, có role nghĩa là đang sửa.
  role?: RoleData | null;
  allPermissions: PermissionItem[];
  // Cha thực hiện việc gọi API; ném lỗi thì form hiện lỗi và không đóng.
  onSave: (data: { code: string; name: string; permissions: string[] }) => Promise<void>;
  onClose: () => void;
}

// Hộp thoại tạo hoặc sửa role, kèm bảng tick quyền.
export function RoleForm({ role, allPermissions, onSave, onClose }: RoleFormProps) {
  // Hook đưa focus vào hộp thoại khi mở, đóng bằng phím Esc, và trả focus về nút đã mở.
  const dialogRef = useDialogFocus({ onClose });
  const isEditing = Boolean(role?.id);
  // Giá trị ban đầu lấy từ role đang sửa; tạo mới thì để trống.
  const [code, setCode] = useState(role?.code || '');
  const [name, setName] = useState(role?.name || '');
  const [selectedPermissions, setSelectedPermissions] = useState<string[]>(role?.permissions || []);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    e.preventDefault();
    setError(null);

    const trimmedCode = code.trim();
    const trimmedName = name.trim();

    // Mã chỉ kiểm khi tạo mới, vì mã không đổi được sau khi đã tạo; kiểm ở đây để báo lỗi tiếng Việt ngay, server vẫn kiểm lại.
    if (!isEditing) {
      if (!trimmedCode) {
        setError('Mã vai trò không được để trống.');
        return;
      }
      if (!/^[a-z0-9_-]+$/.test(trimmedCode)) {
        setError('Mã vai trò chỉ được chứa chữ thường, số, gạch dưới và gạch nối.');
        return;
      }
    }

    // Tên là thứ người dùng thấy trong danh sách nên không được trống.
    if (!trimmedName) {
      setError('Tên vai trò không được để trống.');
      return;
    }

    setLoading(true);

    try {
      await onSave({
        // Đang sửa thì giữ mã cũ, vì ô mã bị khóa và server không đổi mã.
        code: isEditing ? role?.code || trimmedCode : trimmedCode,
        name: trimmedName,
        permissions: selectedPermissions,
      });
      // Chỉ đóng khi lưu thành công; lỗi thì ở lại để người dùng sửa.
      onClose();
    } catch (err: unknown) {
      // Hiện nguyên thông báo của API (không đủ quyền để cấp, mã trùng...) vì đã là tiếng Việt.
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Lưu thông tin role thất bại.');
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
        aria-labelledby="role-form-title"
        tabIndex={-1}
        className="dialog dialog-lg"
      >
        <div className="dialog-head">
          <h2 id="role-form-title">{isEditing ? `Sửa Role: ${role?.name}` : 'Thêm Role Mới'}</h2>
          <button type="button" className="icon-btn" aria-label="Đóng" onClick={onClose}>
            <Icon name="close" />
          </button>
        </div>

        <div className="dialog-body">
          <form id="role-form" noValidate onSubmit={handleSubmit} className="stack">
            {error && (
              <p role="alert" className="alert">
                {error}
              </p>
            )}

            <div className="form-grid form-grid-2">
              {/* Mã role khóa khi sửa, vì server không cho đổi mã sau khi tạo. */}
              <div className="field">
                <label htmlFor="role-code">Mã Role (code) *</label>
                <input
                  id="role-code"
                  type="text"
                  required
                  disabled={isEditing}
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  placeholder="vi_du: warehouse_manager"
                />
              </div>

              <div className="field">
                <label htmlFor="role-name">Tên hiển thị *</label>
                <input
                  id="role-name"
                  type="text"
                  required
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="Quản lý kho"
                />
              </div>
            </div>

            <div className="stack-sm">
              <h3 className="role-form-permissions-title">Phân quyền theo nhóm chức năng</h3>
              <PermissionGrid
                permissions={allPermissions}
                selected={selectedPermissions}
                onChange={setSelectedPermissions}
              />
            </div>
          </form>
        </div>

        <div className="dialog-foot">
          <button type="button" className="btn" onClick={onClose}>
            Hủy
          </button>
          {/* Khóa nút lưu khi đang gửi để một lần bấm không tạo hai role. */}
          <button type="submit" form="role-form" className="btn btn-primary" disabled={loading}>
            {loading ? 'Đang lưu...' : isEditing ? 'Lưu thay đổi' : 'Tạo Role'}
          </button>
        </div>
      </div>
    </div>
  );
}
