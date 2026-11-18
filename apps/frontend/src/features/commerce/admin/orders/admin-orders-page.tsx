import { useQuery } from '@tanstack/react-query';
import { useId, useState } from 'react';
import { Link } from 'react-router-dom';
import { ErrorAlert } from '../../../../shared/error-alert';
import { FilteredEmptyState } from '../../../../shared/filtered-empty-state';
import { ForbiddenNotice } from '../../../../shared/forbidden-notice';
import { Pager } from '../../../../shared/pager';
import { rowNumber } from '../../../../shared/row-number';
import { SearchInput } from '../../../../shared/search-input';
import { TabList, tabId } from '../../../../shared/tab-list';
import { useListFilters } from '../../../../shared/use-list-filters';
import { formatVietnamDateTime } from '../../../../shared/vietnam-time';
import { formatProductPrice } from '../../../catalog/customer/product-display';
import { useCan } from '../../../identity/hooks/use-actor';
import { ExportButton } from '../../../../shared/export-button';
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
import {
  buildAdminOrdersExportPath,
  fetchAdminOrders,
  ORDER_FILTER_NAMES,
  type AdminOrderList,
} from './admin-orders-api';
import {
  orderFlagBadgeClass,
  orderStatusBadgeClass,
  paymentStatusBadgeClass,
} from './order-badges';
import { getErrorMessage } from '../../../../shared/error-message';
import { buildOrderPrintPath } from './order-print-path';
import {
  buildOrderListSelectionKey,
  getSelectAllState,
  getVisibleSelectedCodes,
  MAX_ORDERS_PER_PRINT,
  toggleOrderSelection,
  toggleSelectAllOrders,
} from './order-print-selection';
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

  const limitNoteId = useId();
  // Gắn lựa chọn với khóa bộ lọc và số trang để phát hiện lúc cần xóa toàn bộ lựa chọn.
  const listKey = buildOrderListSelectionKey(values, page);
  // Lưu khóa vừa thấy để đặt lại lựa chọn ngay khi bộ lọc hoặc số trang thay đổi.
  const [seenListKey, setSeenListKey] = useState(listKey);
  const [selectedCodes, setSelectedCodes] = useState<Set<string>>(() => new Set());

  if (seenListKey !== listKey) {
    // Xóa lựa chọn ngay trong lúc vẽ lại khi bộ lọc hoặc trang đổi để không in nhầm mã cũ.
    setSeenListKey(listKey);
    setSelectedCodes(new Set());
  }

  const items = data?.items ?? [];
  const visibleCodes = items.map((order) => order.code);
  // Chỉ giữ và đếm các đơn đang hiển thị theo đúng thứ tự bảng để URL in khớp dữ liệu hiện có.
  const visibleSelectedCodes = getVisibleSelectedCodes(selectedCodes, visibleCodes);
  if (data !== undefined && selectedCodes.size > visibleSelectedCodes.length) {
    // Loại hẳn mã đã biến mất khỏi trang để đơn đó không tự chọn lại nếu xuất hiện ở lần nạp sau.
    setSelectedCodes(new Set(visibleSelectedCodes));
  }
  const visibleSelectedSet = new Set(visibleSelectedCodes);
  const selectAllState = getSelectAllState(visibleSelectedCodes.length, visibleCodes.length);
  // Đạt giới hạn 50 đơn thì khóa các ô chưa chọn nhằm chặn tạo URL vượt ngưỡng API.
  const isLimitReached = visibleSelectedCodes.length >= MAX_ORDERS_PER_PRINT;

  // Hook ở trên phải chạy hết trước lệnh return sớm này. Backend vẫn chặn mọi request thật sự.
  if (!canReadOrders) {
    return <ForbiddenNotice permission="orders:read" subject="danh sách đơn hàng" />;
  }

  // Tab đầu là "Tất cả" (không lọc trạng thái), các tab sau là bảy trạng thái đơn.
  const tabs = [
    { code: '', label: ALL_STATUS_TAB_LABEL },
    ...ORDER_STATUS_CODES.map((code) => ({ code, label: formatOrderStatus(code) })),
  ];

  const showFilteredEmpty = data !== undefined && items.length === 0 && (hasFilters || page > 1);
  const showPlainEmpty = data !== undefined && items.length === 0 && !hasFilters && page === 1;

  function handlePrintSelected() {
    // Bỏ qua khi chưa chọn đơn nào để tránh mở trang in thiếu mã đơn.
    if (visibleSelectedCodes.length === 0) {
      return;
    }
    // Mở tab mới ngay trong sự kiện bấm để trình duyệt không chặn tab mới.
    window.open(buildOrderPrintPath(visibleSelectedCodes), '_blank', 'noopener,noreferrer');
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Quản lý đơn hàng</h1>
          <p>Theo dõi đơn theo trạng thái, thanh toán và các cờ cần chú ý.</p>
        </div>
        {/* Nút chỉ hiện khi actor đọc được đơn và có quyền xuất báo cáo. Tab trạng thái cũng là bộ lọc `status`. */}
        <ExportButton
          path={buildAdminOrdersExportPath(values)}
          requiredPermissions={['orders:read', 'reports:export']}
        />
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
            <label htmlFor="order-flag">Ghi chú</label>
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
              message={getErrorMessage(error, 'Tải danh sách đơn hàng thất bại.')}
              className="order-list-alert"
            />
          ) : showFilteredEmpty ? (
            <FilteredEmptyState page={page} onFirstPage={() => setPage(1)} />
          ) : showPlainEmpty ? (
            <p className="empty-state">Chưa có đơn hàng nào.</p>
          ) : (
            <>
              <div className="order-selection-bar">
                <div className="order-selection-info">
                  {/* Đọc số đơn đã chọn bằng vùng thông báo nhẹ để trình đọc màn hình cập nhật ngay. */}
                  <span className="order-selection-count" aria-live="polite" aria-atomic="true">
                    Đã chọn {visibleSelectedCodes.length} đơn
                  </span>
                  {/* Chỉ hiện chú thích khi chạm ngưỡng 50 đơn để giải thích lý do khóa các ô còn lại. */}
                  {isLimitReached && (
                    <span id={limitNoteId} className="order-selection-limit" aria-live="polite">
                      Mỗi lần in tối đa {MAX_ORDERS_PER_PRINT} đơn
                    </span>
                  )}
                </div>
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  // Khóa nút khi chưa chọn đơn nào để không mở trang in rỗng.
                  disabled={visibleSelectedCodes.length === 0}
                  onClick={handlePrintSelected}
                >
                  In hóa đơn
                </button>
              </div>
              <div className="table-wrap">
                <table className="tbl order-table">
                  <thead>
                    <tr>
                      <th scope="col" className="num col-row-number">
                        STT
                      </th>
                      <th scope="col" className="col-order-select">
                        <label className="order-select-hit">
                          <input
                            ref={(element) => {
                              if (element) {
                                // Đặt qua thuộc tính DOM mỗi lần gắn hoặc cập nhật để không mất trạng thái lưng chừng.
                                element.indeterminate = selectAllState === 'some';
                              }
                            }}
                            type="checkbox"
                            className="order-select-checkbox"
                            aria-label="Chọn tất cả đơn trong trang"
                            checked={selectAllState === 'all'}
                            // Đặt aria-checked="mixed" cùng indeterminate để trình đọc màn hình đọc đúng trạng thái lưng chừng.
                            aria-checked={selectAllState === 'some' ? 'mixed' : undefined}
                            // Đảo lựa chọn toàn trang qua hàm thuần để giữ giới hạn 50 đơn.
                            onChange={() =>
                              setSelectedCodes((current) =>
                                toggleSelectAllOrders(current, visibleCodes),
                              )
                            }
                          />
                        </label>
                      </th>
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
                      <th scope="col">Ghi chú</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data &&
                      items.map((order, index) => {
                        const isSelected = visibleSelectedSet.has(order.code);
                        // Chỉ khóa ô chưa chọn khi đủ 50 để người dùng vẫn bỏ chọn bớt được.
                        const isDisabled = isLimitReached && !isSelected;
                        return (
                          <tr key={order.code}>
                            {/* STT theo `page` và `limit` của response đang hiện, không theo URL. */}
                            <td className="num col-row-number">{rowNumber(data, index)}</td>
                            <td className="col-order-select">
                              <label className="order-select-hit">
                                <input
                                  type="checkbox"
                                  className="order-select-checkbox"
                                  aria-label={`Chọn đơn ${order.code}`}
                                  checked={isSelected}
                                  disabled={isDisabled}
                                  aria-describedby={isDisabled ? limitNoteId : undefined}
                                  // Đổi lựa chọn một dòng qua hàm thuần để giữ ngưỡng tối đa và bỏ mã thừa.
                                  onChange={() =>
                                    setSelectedCodes((current) =>
                                      toggleOrderSelection(current, order.code, visibleCodes),
                                    )
                                  }
                                />
                              </label>
                            </td>
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
                        );
                      })}
                  </tbody>
                </table>
              </div>
            </>
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
