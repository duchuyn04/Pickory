import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { FilteredEmptyState } from '../../../shared/filtered-empty-state';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { SearchInput } from '../../../shared/search-input';
import { useListFilters } from '../../../shared/use-list-filters';
import { useCan } from '../hooks/use-actor';
import type { PermissionItem } from '../components/permission-grid';
import { RoleForm, type RoleData } from '../components/role-form';
import { filterRoles } from './role-filters';
import { ErrorAlert } from '../../../shared/error-alert';
import './identity-admin.css';

// Role khi đã tạo thì có id, kèm số người đang giữ để biết role nào xóa được.
interface RoleRow extends RoleData {
  id: string;
  userCount: number;
}

// Tên tham số bộ lọc trên URL. Màn này lọc ở trình duyệt, nên API không nhận tham số này.
const FILTER_NAMES = ['search'] as const;

// Trang quản lý role: danh sách, tạo, sửa, xóa.
export function RolesPage() {
  const canManageRoles = useCan('roles:manage');
  const queryClient = useQueryClient();
  // Từ khóa nằm trên URL. Mở lại link cho đúng danh sách đã lọc.
  const { values, setFilter } = useListFilters(FILTER_NAMES);
  const keyword = values.search;

  // formOpen mở hộp thoại; editingRole là null khi tạo mới, có giá trị khi sửa.
  const [formOpen, setFormOpen] = useState(false);
  const [editingRole, setEditingRole] = useState<RoleRow | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  // Lấy cả cờ lỗi để hiện thông báo khi tải thất bại. Nếu bỏ qua, lỗi 403 hay mất mạng sẽ hiện như danh sách rỗng.
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
    return <ForbiddenNotice permission="roles:manage" subject="vai trò và phân quyền" />;
  }

  // Lọc trên danh sách đã tải. Sau khi tạo, sửa hoặc xóa, danh sách tải lại vẫn áp từ khóa hiện tại.
  const visibleRoles = filterRoles(roles, keyword);

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
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Quản lý Role &amp; Phân quyền</h1>
          <p>Tạo role mới và thiết lập quyền hạn cho từng nhóm nhân viên trong back-office.</p>
        </div>
        <button
          type="button"
          className="btn btn-primary"
          onClick={() => {
            setEditingRole(null);
            setFormOpen(true);
          }}
        >
          + Thêm role mới
        </button>
      </div>

      {actionError && <ErrorAlert message={actionError} />}

      {permissionsIsError && (
        <ErrorAlert
          message={
            permissionsError instanceof Error
              ? permissionsError.message
              : 'Tải danh sách quyền hạn thất bại.'
          }
        />
      )}

      {rolesLoading ? (
        <p role="status" className="empty-state">
          Đang nạp danh sách role...
        </p>
      ) : rolesIsError ? (
        <ErrorAlert
          message={
            rolesError instanceof Error ? rolesError.message : 'Tải danh sách vai trò thất bại.'
          }
        />
      ) : (
        <section className="card card-flush">
          <div className="filters-row">
            <SearchInput
              label="Tìm theo tên hoặc mã role"
              value={keyword}
              onSearch={(text) => setFilter('search', text)}
            />
          </div>
          {roles.length > 0 && visibleRoles.length === 0 ? (
            // Danh sách không phân trang nên nút Về trang 1 không hiện. Prop bắt buộc nên truyền hàm rỗng.
            <FilteredEmptyState page={1} onFirstPage={() => undefined} />
          ) : (
            <div className="table-wrap">
              <table className="tbl identity-table">
                <thead>
                  <tr>
                    <th scope="col" className="num col-row-number">
                      STT
                    </th>
                    <th scope="col">Role</th>
                    <th scope="col">Loại</th>
                    <th scope="col" className="num">
                      Số quyền
                    </th>
                    <th scope="col" className="num">
                      Số người giữ
                    </th>
                    <th scope="col" className="num">
                      Thao tác
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {roles.length > 0 ? (
                    visibleRoles.map((role, index) => (
                      <tr key={role.id}>
                        {/* Danh sách không phân trang, và STT theo danh sách đang hiện. Lọc xong thì số đánh lại từ 1. */}
                        <td className="num col-row-number">{index + 1}</td>
                        <th scope="row">
                          {role.name}
                          <span className="cell-sub mono">{role.code}</span>
                        </th>
                        <td>
                          {role.isSystem ? (
                            <span className="badge badge-info">Hệ thống (Khóa)</span>
                          ) : (
                            <span className="badge">Tùy chỉnh</span>
                          )}
                        </td>
                        <td className="num">
                          {role.code === 'admin'
                            ? 'Tất cả (ngầm định)'
                            : `${role.permissions.length} quyền`}
                        </td>
                        <td className="num">{role.userCount} người</td>
                        <td className="num">
                          {/* Role hệ thống không sửa hay xóa được, nên chỉ hiện chữ thay cho nút. */}
                          {role.isSystem ? (
                            <span className="muted small">Cố định (hệ thống)</span>
                          ) : (
                            <div className="row-actions identity-row-actions">
                              <button
                                type="button"
                                className="btn btn-sm"
                                aria-label={`Sửa role ${role.name}`}
                                onClick={() => {
                                  setEditingRole(role);
                                  setFormOpen(true);
                                }}
                              >
                                Sửa
                              </button>
                              <button
                                type="button"
                                className="btn btn-sm btn-danger"
                                aria-label={`Xóa role ${role.name}`}
                                onClick={() => handleDeleteRole(role)}
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
                      <td colSpan={6} className="empty-state">
                        Chưa có vai trò nào trong hệ thống.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          )}
        </section>
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
