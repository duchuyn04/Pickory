import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { ErrorAlert } from '../../../../shared/error-alert';
import { FilteredEmptyState } from '../../../../shared/filtered-empty-state';
import { ForbiddenNotice } from '../../../../shared/forbidden-notice';
import { Pager } from '../../../../shared/pager';
import { SearchInput } from '../../../../shared/search-input';
import { TabList, tabId } from '../../../../shared/tab-list';
import { useListFilters } from '../../../../shared/use-list-filters';
import { formatVietnamDateTime } from '../../../../shared/vietnam-time';
import { formatProductPrice } from '../../../catalog/customer/product-display';
import { useCan } from '../../../identity/hooks/use-actor';
import {
  formatOrderFlag,
  formatOrderStatus,
  formatPaymentMethod,
  formatPaymentStatus,
  ORDER_FLAG_CODES,
  ORDER_STATUS_CODES,
  PAYMENT_METHOD_CODES,
  PAYMENT_STATUS_CODES,
} from '../../order-labels';
import { fetchAdminOrders, ORDER_FILTER_NAMES, type AdminOrderList } from './admin-orders-api';
import {
  orderFlagBadgeClass,
  orderStatusBadgeClass,
  paymentStatusBadgeClass,
} from './order-badges';
import './admin-orders.css';

const ALL_STATUS_TAB_LABEL = 'Tất cả';

// Danh sách đơn hàng cho nhân viên: tab theo trạng thái, bộ lọc trên URL, phân trang.
export function AdminOrdersPage() {
  const canReadOrders = useCan('orders:read');
  // Bộ lọc và trang nằm trên URL. Mở lại link cho đúng danh sách đã lọc.
  const { values, page, setFilter, setPage } = useListFilters(ORDER_FILTER_NAMES);
  const hasFilters = ORDER_FILTER_NAMES.some((name) => values[name] !== '');

  // Mọi bộ lọc và số trang nằm trong khóa truy vấn. Mỗi tổ hợp có cache riêng và đổi giá trị thì tự gọi lại.
  const { data, isLoading, isError, error } = useQuery<AdminOrderList>({
    queryKey: ['admin-orders', values, page],
    queryFn: () => fetchAdminOrders(values, page),
    // Không gọi API khi không có quyền, vì chắc chắn bị từ chối với 403.
    enabled: canReadOrders,
  });

  // Hook ở trên phải chạy hết trước lệnh return sớm này. Backend vẫn chặn mọi request thật sự.
  if (!canReadOrders) {
    return <ForbiddenNotice permission="orders:read" subject="danh sách đơn hàng" />;
  }

  // Tab đầu là "Tất cả" (không lọc trạng thái), các tab sau là bảy trạng thái đơn.
  const tabs = [
    { code: '', label: ALL_STATUS_TAB_LABEL },
    ...ORDER_STATUS_CODES.map((code) => ({ code, label: formatOrderStatus(code) })),
  ];

  const items = data?.items ?? [];
  const showFilteredEmpty = data !== undefined && items.length === 0 && (hasFilters || page > 1);
  const showPlainEmpty = data !== undefined && items.length === 0 && !hasFilters && page === 1;

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Quản lý đơn hàng</h1>
          <p>Theo dõi đơn theo trạng thái, thanh toán và các cờ cần chú ý.</p>
        </div>
      </div>

      <section className="card card-flush">
        <TabList
          label="Lọc theo trạng thái đơn"
          idPrefix="order"
          panelId="order-list-panel"
          tabs={tabs}
          selected={values.status}
          onSelect={(code) => setFilter('status', code)}
        />

        <div className="filters-row order-filters">
          <SearchInput
            label="Tìm theo mã đơn, tên, số điện thoại hoặc email"
            value={values.search}
            onSearch={(text) => setFilter('search', text)}
          />
          <div className="field">
            <label htmlFor="order-payment-status">Thanh toán</label>
            <select
              id="order-payment-status"
              value={values.paymentStatus}
              onChange={(event) => setFilter('paymentStatus', event.target.value)}
            >
              <option value="">Tất cả</option>
              {PAYMENT_STATUS_CODES.map((code) => (
                <option key={code} value={code}>
                  {formatPaymentStatus(code)}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor="order-payment-method">Phương thức</label>
            <select
              id="order-payment-method"
              value={values.paymentMethod}
              onChange={(event) => setFilter('paymentMethod', event.target.value)}
            >
              <option value="">Tất cả</option>
              {PAYMENT_METHOD_CODES.map((code) => (
                <option key={code} value={code}>
                  {formatPaymentMethod(code)}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor="order-flag">Cờ đơn</label>
            <select
              id="order-flag"
              value={values.flag}
              onChange={(event) => setFilter('flag', event.target.value)}
            >
              <option value="">Tất cả</option>
              {ORDER_FLAG_CODES.map((code) => (
                <option key={code} value={code}>
                  {formatOrderFlag(code)}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor="order-from">Từ ngày</label>
            <input
              id="order-from"
              type="date"
              value={values.from}
              onChange={(event) => setFilter('from', event.target.value)}
            />
          </div>
          <div className="field">
            <label htmlFor="order-to">Đến ngày</label>
            <input
              id="order-to"
              type="date"
              value={values.to}
              onChange={(event) => setFilter('to', event.target.value)}
            />
          </div>
        </div>

        <div id="order-list-panel" role="tabpanel" aria-labelledby={tabId('order', values.status)}>
          {isLoading ? (
            <p role="status" className="empty-state">
              Đang nạp danh sách đơn hàng...
            </p>
          ) : isError ? (
            // Lỗi 400 của API là tiếng Việt và nói rõ ô sai, nên hiện nguyên văn.
            <ErrorAlert
              message={error instanceof Error ? error.message : 'Tải danh sách đơn hàng thất bại.'}
              className="order-list-alert"
            />
          ) : showFilteredEmpty ? (
            <FilteredEmptyState page={page} onFirstPage={() => setPage(1)} />
          ) : showPlainEmpty ? (
            <p className="empty-state">Chưa có đơn hàng nào.</p>
          ) : (
            <div className="table-wrap">
              <table className="tbl order-table">
                <thead>
                  <tr>
                    <th scope="col">Mã đơn</th>
                    <th scope="col">Ngày đặt</th>
                    <th scope="col">Người nhận</th>
                    <th scope="col" className="num">
                      Số dòng
                    </th>
                    <th scope="col" className="num">
                      Tổng tiền
                    </th>
                    <th scope="col">Trạng thái</th>
                    <th scope="col">Thanh toán</th>
                    <th scope="col">Cờ</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((order) => (
                    <tr key={order.code}>
                      <th scope="row">
                        {/* Mã đơn là liên kết, nên bảng dùng được bằng phím Tab và Enter. */}
                        <Link className="wrap-anywhere" to={`/admin/orders/${order.code}`}>
                          {order.code}
                        </Link>
                      </th>
                      <td className="order-time">{formatVietnamDateTime(order.createdAt)}</td>
                      <td>
                        {order.recipientName || 'Chưa có tên'}
                        <span className="cell-sub">{order.recipientPhone || 'Chưa có'}</span>
                      </td>
                      <td className="num">{order.lineCount}</td>
                      <td className="num">{formatProductPrice(order.total)}</td>
                      <td>
                        <span className={orderStatusBadgeClass(order.status)}>
                          {formatOrderStatus(order.status)}
                        </span>
                      </td>
                      <td>
                        <span className={paymentStatusBadgeClass(order.paymentStatus)}>
                          {formatPaymentStatus(order.paymentStatus)}
                        </span>
                      </td>
                      <td>
                        <div className="row-actions">
                          {order.flags.map((flag) => (
                            <span key={flag} className={orderFlagBadgeClass(flag)}>
                              {formatOrderFlag(flag)}
                            </span>
                          ))}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        {data && !isError && (
          <Pager
            page={page}
            total={data.total}
            limit={data.limit}
            onPageChange={setPage}
            label="Phân trang đơn hàng"
          />
        )}
      </section>
    </div>
  );
}
