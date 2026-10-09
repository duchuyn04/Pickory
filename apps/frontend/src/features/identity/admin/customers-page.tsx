import { useQuery, useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { useCan } from '../hooks/use-actor';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { ErrorAlert } from '../../../shared/error-alert';
import './identity-admin.css';

// Một dòng khách hàng như API trả về; không có passwordHash.
export interface CustomerRow {
  id: string;
  email: string;
  name: string | null;
  phone: string | null;
  emailVerified: boolean;
  kind: string;
  lockedAt: string | null;
  createdAt: string;
}

interface CustomersResponse {
  items: CustomerRow[];
  total: number;
  page: number;
  limit: number;
}

// Trang tra cứu khách hàng, khóa và mở khóa tài khoản.
export function CustomersPage() {
  // Hai quyền tách nhau: sales và support xem được danh sách nhưng không thấy nút khóa.
  const canReadCustomers = useCan('customers:read');
  const canManageCustomers = useCan('customers:manage');
  const queryClient = useQueryClient();

  // searchTerm là chữ đang gõ; debouncedSearch chỉ đổi khi bấm tìm, để không gọi API sau mỗi phím gõ.
  const [searchTerm, setSearchTerm] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [actionError, setActionError] = useState<string | null>(null);

  // Lấy cả cờ lỗi để hiện thông báo khi tải thất bại; nếu bỏ qua thì lỗi 403 hay mất mạng sẽ hiện như danh sách rỗng.
  const { data, isLoading, isError, error } = useQuery<CustomersResponse>({
    // Từ khóa nằm trong khóa truy vấn để mỗi từ khóa có cache riêng và đổi từ khóa thì tự gọi lại.
    queryKey: ['admin-customers', debouncedSearch],
    queryFn: () => {
      // encodeURIComponent để ký tự đặc biệt trong từ khóa (như & hay #) không làm sai URL.
      const url = debouncedSearch
        ? `/api/admin/customers?search=${encodeURIComponent(debouncedSearch)}`
        : '/api/admin/customers';
      return apiRequest<CustomersResponse>(url);
    },
    // Không gọi API khi không có quyền, vì chắc chắn bị từ chối với 403.
    enabled: canReadCustomers,
  });

  // Hook ở trên phải chạy hết trước lệnh return sớm này; backend vẫn chặn mọi request thật sự.
  if (!canReadCustomers) {
    return <ForbiddenNotice permission="customers:read" subject="danh sách khách hàng" />;
  }

  function handleSearchSubmit(e: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    e.preventDefault();
    setDebouncedSearch(searchTerm);
  }

  // Một nút dùng cho cả khóa và mở khóa, tùy trạng thái hiện tại của khách.
  async function handleToggleLock(customer: CustomerRow) {
    const isLocking = !customer.lockedAt;
    const confirmMsg = isLocking
      ? `Bạn có chắc chắn muốn khóa tài khoản khách hàng "${customer.email}"?`
      : `Bạn có chắc chắn muốn mở khóa tài khoản khách hàng "${customer.email}"?`;

    // Hỏi lại trước khi khóa hay mở khóa vì thao tác này ảnh hưởng ngay tới khách.
    if (!window.confirm(confirmMsg)) return;

    // Xóa lỗi của lần thao tác trước để không hiện lẫn.
    setActionError(null);
    try {
      const endpoint = isLocking
        ? `/api/admin/customers/${customer.id}/lock`
        : `/api/admin/customers/${customer.id}/unlock`;
      await apiRequest(endpoint, { method: 'POST' });
      // Nạp lại danh sách (mọi từ khóa) để dòng của khách hiện trạng thái khóa mới.
      await queryClient.invalidateQueries({ queryKey: ['admin-customers'] });
    } catch (err: unknown) {
      // Hiện nguyên thông báo của API (không đủ quyền, không tìm thấy khách...) vì đã là tiếng Việt.
      if (err instanceof Error) {
        setActionError(err.message);
      } else {
        setActionError('Thao tác khóa/mở khóa khách hàng thất bại.');
      }
    }
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Quản lý Khách hàng</h1>
          <p>Tra cứu thông tin khách hàng và kiểm soát quyền truy cập tài khoản.</p>
        </div>
      </div>

      {actionError && <ErrorAlert message={actionError} />}

      <section className="card card-flush">
        <form noValidate onSubmit={handleSearchSubmit} className="filters-row">
          <div className="field">
            <label htmlFor="customer-search" className="sr">
              Tìm khách hàng
            </label>
            <input
              id="customer-search"
              type="search"
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              placeholder="Tìm theo email, tên, SĐT..."
            />
          </div>
          <button type="submit" className="btn btn-primary">
            Tìm kiếm
          </button>
        </form>

        {isLoading ? (
          <p role="status" className="empty-state">
            Đang nạp danh sách khách hàng...
          </p>
        ) : isError ? (
          <ErrorAlert
            message={error instanceof Error ? error.message : 'Tải danh sách khách hàng thất bại.'}
            className="identity-table-alert"
          />
        ) : (
          <div className="table-wrap">
            <table className="tbl identity-table">
              <thead>
                <tr>
                  <th scope="col">Khách hàng</th>
                  <th scope="col">Số điện thoại</th>
                  <th scope="col">Xác thực email</th>
                  <th scope="col">Trạng thái</th>
                  {canManageCustomers && (
                    <th scope="col" className="num">
                      Thao tác
                    </th>
                  )}
                </tr>
              </thead>
              <tbody>
                {data?.items && data.items.length > 0 ? (
                  data.items.map((cust) => (
                    <tr key={cust.id}>
                      <th scope="row">
                        {cust.name || 'Chưa có tên'}
                        <span className="cell-sub wrap-anywhere">{cust.email}</span>
                      </th>
                      <td>{cust.phone || 'Chưa có'}</td>
                      <td>
                        {cust.emailVerified ? (
                          <span className="badge badge-ok">Đã xác thực</span>
                        ) : (
                          <span className="badge">Chưa xác thực</span>
                        )}
                      </td>
                      <td>
                        {cust.lockedAt ? (
                          <span className="badge badge-bad">Đã khóa</span>
                        ) : (
                          <span className="badge badge-ok">Hoạt động</span>
                        )}
                      </td>
                      {canManageCustomers && (
                        <td className="num">
                          {/* Nút khóa dùng màu nguy hiểm; nút mở khóa là thao tác thường. */}
                          <button
                            type="button"
                            className={cust.lockedAt ? 'btn btn-sm' : 'btn btn-sm btn-danger'}
                            onClick={() => handleToggleLock(cust)}
                          >
                            {cust.lockedAt ? 'Mở khóa' : 'Khóa'}
                          </button>
                        </td>
                      )}
                    </tr>
                  ))
                ) : (
                  <tr>
                    <td colSpan={canManageCustomers ? 5 : 4} className="empty-state">
                      Không tìm thấy khách hàng nào phù hợp.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
