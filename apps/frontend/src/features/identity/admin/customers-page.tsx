import { useQuery, useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { useCan } from '../hooks/use-actor';

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
          Bạn cần quyền <code>customers:read</code> để xem danh sách khách hàng.
        </p>
      </div>
    );
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
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--space-6)',
        fontFamily: 'var(--font-sans)',
      }}
    >
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: 'var(--space-4)',
        }}
      >
        <div>
          <h1
            style={{
              fontSize: 'var(--text-xl)',
              fontWeight: 700,
              margin: '0 0 var(--space-1) 0',
              color: 'var(--color-ink)',
            }}
          >
            Quản lý Khách hàng
          </h1>
          <p style={{ fontSize: 'var(--text-sm)', color: 'var(--color-ink-muted)', margin: 0 }}>
            Tra cứu thông tin khách hàng và kiểm soát quyền truy cập tài khoản.
          </p>
        </div>

        <form
          noValidate
          onSubmit={handleSearchSubmit}
          style={{ display: 'flex', gap: 'var(--space-2)' }}
        >
          <input
            type="text"
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            placeholder="Tìm theo email, tên, SĐT..."
            style={{
              padding: 'var(--space-2) var(--space-3)',
              borderRadius: 'var(--radius-control)',
              border: '1px solid var(--color-border)',
              fontSize: 'var(--text-sm)',
              minWidth: 260,
              outline: 'none',
            }}
          />
          <button
            type="submit"
            style={{
              padding: 'var(--space-2) var(--space-4)',
              backgroundColor: 'var(--color-brand)',
              color: '#ffffff',
              border: 'none',
              borderRadius: 'var(--radius-control)',
              fontSize: 'var(--text-sm)',
              fontWeight: 600,
              cursor: 'pointer',
            }}
          >
            Tìm kiếm
          </button>
        </form>
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

      {isLoading ? (
        <div>Đang nạp danh sách khách hàng...</div>
      ) : isError ? (
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
          {error instanceof Error ? error.message : 'Tải danh sách khách hàng thất bại.'}
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
                  Email
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Họ và tên
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Số điện thoại
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Xác thực email
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Trạng thái
                </th>
                {canManageCustomers && (
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
                )}
              </tr>
            </thead>
            <tbody>
              {data?.items && data.items.length > 0 ? (
                data.items.map((cust) => (
                  <tr key={cust.id} style={{ borderBottom: '1px solid var(--color-border)' }}>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-sm)',
                      }}
                    >
                      {cust.email}
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-sm)',
                        fontWeight: 600,
                      }}
                    >
                      {cust.name || 'Chưa có'}
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-sm)',
                      }}
                    >
                      {cust.phone || 'Chưa có'}
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-xs)',
                      }}
                    >
                      {cust.emailVerified ? (
                        <span style={{ color: 'var(--color-success)', fontWeight: 600 }}>
                          Đã xác thực
                        </span>
                      ) : (
                        <span style={{ color: 'var(--color-ink-muted)' }}>Chưa xác thực</span>
                      )}
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-xs)',
                      }}
                    >
                      {cust.lockedAt ? (
                        <span
                          style={{
                            backgroundColor: '#fef3f2',
                            color: '#b42318',
                            padding: '2px 8px',
                            borderRadius: 'var(--radius-pill)',
                            fontWeight: 600,
                          }}
                        >
                          Đã khóa
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
                          Hoạt động
                        </span>
                      )}
                    </td>
                    {canManageCustomers && (
                      <td
                        style={{
                          padding: 'var(--space-3) var(--space-4)',
                          fontSize: 'var(--text-sm)',
                          textAlign: 'right',
                        }}
                      >
                        <button
                          type="button"
                          onClick={() => handleToggleLock(cust)}
                          style={{
                            background: 'none',
                            border: '1px solid',
                            borderColor: cust.lockedAt
                              ? 'var(--color-success)'
                              : 'var(--color-danger)',
                            color: cust.lockedAt ? 'var(--color-success)' : 'var(--color-danger)',
                            borderRadius: 'var(--radius-control)',
                            padding: '4px 8px',
                            cursor: 'pointer',
                            fontSize: 'var(--text-xs)',
                          }}
                        >
                          {cust.lockedAt ? 'Mở khóa' : 'Khóa'}
                        </button>
                      </td>
                    )}
                  </tr>
                ))
              ) : (
                <tr>
                  <td
                    colSpan={canManageCustomers ? 6 : 5}
                    style={{
                      padding: 'var(--space-6)',
                      textAlign: 'center',
                      color: 'var(--color-ink-muted)',
                    }}
                  >
                    Không tìm thấy khách hàng nào phù hợp.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
