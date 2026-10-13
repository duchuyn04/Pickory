import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { FilteredEmptyState } from '../../../shared/filtered-empty-state';
import { Pager } from '../../../shared/pager';
import { SearchInput } from '../../../shared/search-input';
import { useListFilters } from '../../../shared/use-list-filters';
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

// Tên tham số bộ lọc trên URL. Trùng tên tham số của `GET /api/admin/customers`.
const FILTER_NAMES = ['search', 'status', 'emailVerified'] as const;

// Trang tra cứu khách hàng, khóa và mở khóa tài khoản.
export function CustomersPage() {
  // Hai quyền tách nhau: sales và support xem được danh sách nhưng không thấy nút khóa.
  const canReadCustomers = useCan('customers:read');
  const canManageCustomers = useCan('customers:manage');
  const queryClient = useQueryClient();

  // Bộ lọc và trang nằm trên URL. Mở lại link cho đúng danh sách đã lọc.
  const { values, page, setFilter, setPage } = useListFilters(FILTER_NAMES);
  const hasFilters = FILTER_NAMES.some((name) => values[name] !== '');
  const [actionError, setActionError] = useState<string | null>(null);

  // Lấy cả cờ lỗi để hiện thông báo khi tải thất bại. Nếu bỏ qua, lỗi 403 hay mất mạng sẽ hiện như danh sách rỗng.
  const { data, isLoading, isError, error } = useQuery<CustomersResponse>({
    // Mọi bộ lọc và số trang nằm trong khóa truy vấn. Mỗi tổ hợp có cache riêng và đổi giá trị thì tự gọi lại.
    queryKey: ['admin-customers', values, page],
    queryFn: () => {
      // `URLSearchParams` mã hóa ký tự đặc biệt trong từ khóa (như & hay #) để URL không sai.
      const params = new URLSearchParams();
      for (const name of FILTER_NAMES) {
        if (values[name] !== '') params.set(name, values[name]);
      }
      // Trang 1 là mặc định của API nên không gửi.
      if (page > 1) params.set('page', String(page));
      const queryString = params.toString();
      return apiRequest<CustomersResponse>(
        queryString ? `/api/admin/customers?${queryString}` : '/api/admin/customers',
      );
    },
    // Không gọi API khi không có quyền, vì chắc chắn bị từ chối với 403.
    enabled: canReadCustomers,
  });

  // Hook ở trên phải chạy hết trước lệnh return sớm này; backend vẫn chặn mọi request thật sự.
  if (!canReadCustomers) {
    return <ForbiddenNotice permission="customers:read" subject="danh sách khách hàng" />;
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
      // Một route cho cả hai thao tác. Trường `locked` cho API biết cần khóa hay mở khóa.
      await apiRequest(`/api/admin/customers/${customer.id}`, {
        method: 'PATCH',
        body: JSON.stringify({ locked: isLocking }),
      });
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
        <div className="filters-row">
          <SearchInput
            label="Tìm theo tên, email hoặc số điện thoại"
            value={values.search}
            onSearch={(text) => setFilter('search', text)}
          />
          <div className="field">
            <label htmlFor="customer-status">Trạng thái</label>
            <select
              id="customer-status"
              value={values.status}
              onChange={(event) => setFilter('status', event.target.value)}
            >
              <option value="">Tất cả</option>
              <option value="active">Hoạt động</option>
              <option value="locked">Đã khóa</option>
            </select>
          </div>
          <div className="field">
            <label htmlFor="customer-email-verified">Xác thực email</label>
            <select
              id="customer-email-verified"
              value={values.emailVerified}
              onChange={(event) => setFilter('emailVerified', event.target.value)}
            >
              <option value="">Tất cả</option>
              <option value="true">Đã xác thực</option>
              <option value="false">Chưa xác thực</option>
            </select>
          </div>
        </div>

        {isLoading ? (
          <p role="status" className="empty-state">
            Đang nạp danh sách khách hàng...
          </p>
        ) : isError ? (
          <ErrorAlert
            message={error instanceof Error ? error.message : 'Tải danh sách khách hàng thất bại.'}
            className="identity-table-alert"
          />
        ) : data && data.items.length === 0 && (hasFilters || page > 1) ? (
          <FilteredEmptyState page={page} onFirstPage={() => setPage(1)} />
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
        {data && !isError && (
          <Pager
            page={page}
            total={data.total}
            limit={data.limit}
            onPageChange={setPage}
            label="Phân trang khách hàng"
          />
        )}
      </section>
    </div>
  );
}
