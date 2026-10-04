import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { useCan } from '../use-actor';
import type { PermissionItem } from './permission-grid';
import { RoleForm, type RoleData } from './role-form';

interface RoleRow extends RoleData {
  id: string;
  userCount: number;
}

export function RolesPage() {
  const canManageRoles = useCan('roles:manage');
  const queryClient = useQueryClient();

  const [formOpen, setFormOpen] = useState(false);
  const [editingRole, setEditingRole] = useState<RoleRow | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  // Kiểm tra cờ lỗi từ React Query để hiển thị thông báo lỗi tường minh thay vì giao diện trống khi truy vấn vai trò thất bại.
  const {
    data: roles = [],
    isLoading: rolesLoading,
    isError: rolesIsError,
    error: rolesError,
  } = useQuery<RoleRow[]>({
    queryKey: ['admin-roles'],
    queryFn: () => apiRequest<RoleRow[]>('/api/admin/roles'),
    enabled: canManageRoles,
  });

  // Truy vấn danh mục quyền hạn để nạp vào form phân quyền, đồng thời xử lý trạng thái lỗi độc lập với danh sách vai trò.
  const {
    data: permissions = [],
    isError: permissionsIsError,
    error: permissionsError,
  } = useQuery<PermissionItem[]>({
    queryKey: ['admin-permissions'],
    queryFn: () => apiRequest<PermissionItem[]>('/api/admin/permissions'),
    enabled: canManageRoles,
  });

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

  async function handleSaveRole(data: { code: string; name: string; permissions: string[] }) {
    if (editingRole) {
      await apiRequest(`/api/admin/roles/${editingRole.id}`, {
        method: 'PATCH',
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
    // Làm mới danh sách vai trò trong bộ nhớ cache để cập nhật ngay lập tức các thay đổi lên giao diện.
    await queryClient.invalidateQueries({ queryKey: ['admin-roles'] });
  }

  async function handleDeleteRole(role: RoleRow) {
    if (!window.confirm(`Bạn có chắc chắn muốn xóa role "${role.name}"?`)) {
      return;
    }
    setActionError(null);
    try {
      await apiRequest(`/api/admin/roles/${role.id}`, {
        method: 'DELETE',
      });
      // Cập nhật lại danh sách vai trò sau khi xóa để bảng dữ liệu phản ánh chính xác trạng thái hiện tại.
      await queryClient.invalidateQueries({ queryKey: ['admin-roles'] });
    } catch (err: unknown) {
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
