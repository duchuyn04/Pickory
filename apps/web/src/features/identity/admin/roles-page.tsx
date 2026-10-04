import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { useCan } from '../use-actor';
import type { PermissionItem } from './permission-grid';
import { RoleForm, type RoleData } from './role-form';

// Role khi đã tạo thì có id, kèm số người đang giữ để biết role nào xóa được.
interface RoleRow extends RoleData {
  id: string;
  userCount: number;
}

// Trang quản lý role: danh sách, tạo, sửa, xóa.
export function RolesPage() {
  const canManageRoles = useCan('roles:manage');
  const queryClient = useQueryClient();

  // formOpen mở hộp thoại; editingRole là null khi tạo mới, có giá trị khi sửa.
  const [formOpen, setFormOpen] = useState(false);
  const [editingRole, setEditingRole] = useState<RoleRow | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  // Lấy cả cờ lỗi để hiện thông báo khi tải thất bại; nếu bỏ qua thì lỗi 403 hay mất mạng sẽ hiện như danh sách rỗng.
  const {
    data: roles = [],
    isLoading: rolesLoading,
    isError: rolesIsError,
    error: rolesError,
  } = useQuery<RoleRow[]>({
    queryKey: ['admin-roles'],
    queryFn: () => apiRequest<RoleRow[]>('/api/admin/roles'),
    // Không gọi API khi không có quyền, vì chắc chắn bị từ chối với 403.
    enabled: canManageRoles,
  });

  // Danh mục quyền để vẽ bảng tick; truy vấn riêng để lỗi của một bên không che mất bên kia.
  const {
    data: permissions = [],
    isError: permissionsIsError,
    error: permissionsError,
  } = useQuery<PermissionItem[]>({
    queryKey: ['admin-permissions'],
    queryFn: () => apiRequest<PermissionItem[]>('/api/admin/permissions'),
    enabled: canManageRoles,
  });

  // Hook ở trên phải chạy hết trước lệnh return sớm này; backend vẫn chặn mọi request thật sự.
  if (!canManageRoles) {
    return (
      <div
        style={{
          padding: 'var(--space-6)',
          backgroundColor: 'var(--color-surface)',
          borderRadius: 'var(--radius-card)',
        }}
      >
        <h2 style={{ color: 'var(--color-danger)', margin: 0 }}>403 - Không có quyền truy cập</h2>
        <p style={{ color: 'var(--color-ink-muted)' }}>
          Bạn cần quyền <code>roles:manage</code> để quản lý vai trò và phân quyền.
        </p>
      </div>
    );
  }

  // Không bắt lỗi ở đây: RoleForm bắt và hiện lỗi trong hộp thoại, để người dùng sửa rồi lưu lại.
  async function handleSaveRole(data: { code: string; name: string; permissions: string[] }) {
    if (editingRole) {
      await apiRequest(`/api/admin/roles/${editingRole.id}`, {
        method: 'PATCH',
        // Khi sửa chỉ gửi tên và quyền, vì mã role không đổi được.
        body: JSON.stringify({
          name: data.name,
          permissions: data.permissions,
        }),
      });
    } else {
      await apiRequest('/api/admin/roles', {
        method: 'POST',
        body: JSON.stringify(data),
      });
    }
    // Nạp lại danh sách để role mới hoặc vừa sửa hiện ngay.
    await queryClient.invalidateQueries({ queryKey: ['admin-roles'] });
  }

  async function handleDeleteRole(role: RoleRow) {
    // Hỏi lại vì xóa không hoàn tác được.
    if (!window.confirm(`Bạn có chắc chắn muốn xóa role "${role.name}"?`)) {
      return;
    }
    // Xóa lỗi của lần thao tác trước để không hiện lẫn.
    setActionError(null);
    try {
      await apiRequest(`/api/admin/roles/${role.id}`, {
        method: 'DELETE',
      });
      // Nạp lại danh sách để role đã xóa biến mất khỏi bảng.
      await queryClient.invalidateQueries({ queryKey: ['admin-roles'] });
    } catch (err: unknown) {
      // Hiện nguyên thông báo của API (role hệ thống, role đang có người giữ...) vì đã là tiếng Việt.
      if (err instanceof Error) {
        setActionError(err.message);
      } else {
        setActionError('Xóa role thất bại.');
      }
    }
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--space-6)',
        fontFamily: 'var(--font-sans)',
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div>
          <h1
            style={{
              fontSize: 'var(--text-xl)',
              fontWeight: 700,
              margin: '0 0 var(--space-1) 0',
              color: 'var(--color-ink)',
            }}
          >
            Quản lý Role & Phân quyền
          </h1>
          <p style={{ fontSize: 'var(--text-sm)', color: 'var(--color-ink-muted)', margin: 0 }}>
            Tạo role mới và thiết lập quyền hạn cho từng nhóm nhân viên trong back-office.
          </p>
        </div>
        <button
          type="button"
          onClick={() => {
            setEditingRole(null);
            setFormOpen(true);
          }}
          style={{
            backgroundColor: 'var(--color-brand)',
            color: '#ffffff',
            border: 'none',
            padding: 'var(--space-2) var(--space-4)',
            borderRadius: 'var(--radius-control)',
            fontSize: 'var(--text-sm)',
            fontWeight: 600,
            cursor: 'pointer',
          }}
        >
          + Thêm role mới
        </button>
      </div>

      {actionError && (
        <div
          role="alert"
          style={{
            backgroundColor: '#fee4e2',
            border: '1px solid var(--color-danger)',
            color: 'var(--color-danger)',
            padding: 'var(--space-3)',
            borderRadius: 'var(--radius-control)',
            fontSize: 'var(--text-sm)',
          }}
        >
          {actionError}
        </div>
      )}

      {permissionsIsError && (
        <div
          role="alert"
          style={{
            backgroundColor: '#fee4e2',
            border: '1px solid var(--color-danger)',
            color: 'var(--color-danger)',
            padding: 'var(--space-3)',
            borderRadius: 'var(--radius-control)',
            fontSize: 'var(--text-sm)',
          }}
        >
          {permissionsError instanceof Error
            ? permissionsError.message
            : 'Tải danh sách quyền hạn thất bại.'}
        </div>
      )}

      {rolesLoading ? (
        <div>Đang nạp danh sách role...</div>
      ) : rolesIsError ? (
        <div
          role="alert"
          style={{
            backgroundColor: '#fee4e2',
            border: '1px solid var(--color-danger)',
            color: 'var(--color-danger)',
            padding: 'var(--space-4)',
            borderRadius: 'var(--radius-control)',
            fontSize: 'var(--text-sm)',
          }}
        >
          {rolesError instanceof Error ? rolesError.message : 'Tải danh sách vai trò thất bại.'}
        </div>
      ) : (
        <div
          style={{
            backgroundColor: 'var(--color-surface)',
            borderRadius: 'var(--radius-card)',
            border: '1px solid var(--color-border)',
            overflow: 'hidden',
          }}
        >
          <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left' }}>
            <thead>
              <tr
                style={{
                  backgroundColor: 'var(--color-surface-muted)',
                  borderBottom: '1px solid var(--color-border)',
                }}
              >
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Mã Role
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Tên hiển thị
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Loại
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Số quyền
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Số người giữ
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                    textAlign: 'right',
                  }}
                >
                  Thao tác
                </th>
              </tr>
            </thead>
            <tbody>
              {roles.length > 0 ? (
                roles.map((role) => (
                  <tr key={role.id} style={{ borderBottom: '1px solid var(--color-border)' }}>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-sm)',
                        fontFamily: 'monospace',
                      }}
                    >
                      {role.code}
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-sm)',
                        fontWeight: 600,
                      }}
                    >
                      {role.name}
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-xs)',
                      }}
                    >
                      {role.isSystem ? (
                        <span
                          style={{
                            backgroundColor: '#fef3f2',
                            color: '#b42318',
                            padding: '2px 8px',
                            borderRadius: 'var(--radius-pill)',
                            fontWeight: 600,
                          }}
                        >
                          Hệ thống (Khóa)
                        </span>
                      ) : (
                        <span
                          style={{
                            backgroundColor: '#ecfdf3',
                            color: '#027a48',
                            padding: '2px 8px',
                            borderRadius: 'var(--radius-pill)',
                            fontWeight: 600,
                          }}
                        >
                          Tùy chỉnh
                        </span>
                      )}
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-sm)',
                      }}
                    >
                      {role.code === 'admin'
                        ? 'Tất cả (ngầm định)'
                        : `${role.permissions.length} quyền`}
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-sm)',
                      }}
                    >
                      {role.userCount} người
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-sm)',
                        textAlign: 'right',
                      }}
                    >
                      {role.isSystem ? (
                        <span
                          style={{ color: 'var(--color-ink-muted)', fontSize: 'var(--text-xs)' }}
                        >
                          Cố định (hệ thống)
                        </span>
                      ) : (
                        <div style={{ display: 'inline-flex', gap: 'var(--space-2)' }}>
                          <button
                            type="button"
                            onClick={() => {
                              setEditingRole(role);
                              setFormOpen(true);
                            }}
                            style={{
                              background: 'none',
                              border: '1px solid var(--color-border)',
                              borderRadius: 'var(--radius-control)',
                              padding: '4px 8px',
                              cursor: 'pointer',
                              fontSize: 'var(--text-xs)',
                            }}
                          >
                            Sửa
                          </button>
                          <button
                            type="button"
                            onClick={() => handleDeleteRole(role)}
                            style={{
                              background: 'none',
                              border: '1px solid var(--color-danger)',
                              color: 'var(--color-danger)',
                              borderRadius: 'var(--radius-control)',
                              padding: '4px 8px',
                              cursor: 'pointer',
                              fontSize: 'var(--text-xs)',
                            }}
                          >
                            Xóa
                          </button>
                        </div>
                      )}
                    </td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td
                    colSpan={6}
                    style={{
                      padding: 'var(--space-6)',
                      textAlign: 'center',
                      color: 'var(--color-ink-muted)',
                    }}
                  >
                    Chưa có vai trò nào trong hệ thống.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      {formOpen && (
        <RoleForm
          role={editingRole}
          allPermissions={permissions}
          onSave={handleSaveRole}
          onClose={() => {
            setFormOpen(false);
            setEditingRole(null);
          }}
        />
      )}
    </div>
  );
}
