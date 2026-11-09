import { useId, useState } from 'react';
import { Link } from 'react-router-dom';
import type { UseQueryResult } from '@tanstack/react-query';
import { ErrorAlert } from '../../../shared/error-alert';
import { Icon } from '../../../shared/icons';
import { formatVietnamDateTime } from '../../../shared/vietnam-time';
import { formatOrderStatus } from '../../commerce/order-labels';
import { formatVnd } from '../../admin-console/reports/report-format';
import type { SessionCustomer } from './support-desk-schemas';
import type { OpenSupportSession } from './support-desk-session';
import './customer-panel.css';

interface CustomerPanelProps {
  session: OpenSupportSession | null;
  customerQuery: UseQueryResult<SessionCustomer>;
}

// Bảng thông tin khách hàng ở cột phải màn CSKH. Cột thu gọn thành ngăn bấm ở màn hẹp.
export function CustomerPanel({ session, customerQuery }: CustomerPanelProps) {
  const [isOpen, setIsOpen] = useState(false);
  const contentId = useId();

  const customerData = customerQuery.data;

  return (
    <aside
      className="card card-flush support-pane support-customer-panel"
      aria-labelledby="support-customer-title"
    >
      {/* Nút điều khiển ngăn thu gọn hiển thị ở màn hình dưới 1180px. */}
      <button
        type="button"
        className="support-customer-toggle"
        aria-expanded={isOpen}
        aria-controls={contentId}
        onClick={() => setIsOpen((open) => !open)}
      >
        <span>Thông tin khách</span>
        <Icon
          name="chevronDown"
          className={`icon support-customer-toggle-icon ${isOpen ? 'is-open' : ''}`}
        />
      </button>

      <h2 id="support-customer-title" className="support-pane-title support-customer-desktop-title">
        Thông tin khách
      </h2>

      {/* Thuộc tính hidden ẩn vùng nội dung ở màn hẹp khi ngăn đang đóng. */}
      <div id={contentId} className="support-customer-body" hidden={!isOpen}>
        {session === null ? (
          <p className="support-pane-empty">Tên, email và đơn gần đây của khách sẽ hiện ở đây.</p>
        ) : customerQuery.isError ? (
          <div className="support-queue-alert">
            <ErrorAlert message="Không tải được thông tin khách." />
            <button
              type="button"
              className="btn btn-sm"
              disabled={customerQuery.isFetching}
              onClick={() => void customerQuery.refetch()}
            >
              Thử lại
            </button>
          </div>
        ) : !customerData ? (
          <p role="status" className="empty-state">
            Đang tải thông tin khách...
          </p>
        ) : (
          <>
            <div className="support-customer-section">
              <dl className="support-customer-profile">
                <div className="support-customer-field">
                  <dt className="support-customer-label">Họ tên:</dt>
                  <dd className="support-customer-value wrap-anywhere">
                    {/* Chuỗi rỗng hoặc khoảng trắng được coi là chưa có thông tin. */}
                    {customerData.customer.name?.trim() || 'Chưa có thông tin'}
                  </dd>
                </div>
                <div className="support-customer-field">
                  <dt className="support-customer-label">Email:</dt>
                  <dd className="support-customer-value wrap-anywhere">
                    {/* Chuỗi rỗng hoặc khoảng trắng được coi là chưa có thông tin. */}
                    {customerData.customer.email?.trim() || 'Chưa có thông tin'}
                  </dd>
                </div>
                {customerData.customer.isGuest && (
                  <div className="support-customer-badge-row">
                    <span className="badge badge-info">Khách vãng lai</span>
                  </div>
                )}
              </dl>
            </div>

            <div className="support-customer-section">
              <h3 className="support-customer-subtitle">Đơn hàng gần đây</h3>
              {customerData.orders.length === 0 ? (
                <p className="empty-state">Khách chưa có đơn hàng.</p>
              ) : (
                <ul className="support-customer-orders">
                  {/* Giới hạn hiển thị 5 đơn mới nhất vì API có thể trả nhiều đơn hơn. */}
                  {customerData.orders.slice(0, 5).map((order) => (
                    <li key={order.code} className="support-customer-order-item">
                      <div className="support-customer-order-top">
                        {/* Mã hóa ký tự đặc biệt trong mã đơn để tránh làm hỏng đường dẫn URL. */}
                        <Link
                          to={`/admin/orders/${encodeURIComponent(order.code)}`}
                          className="wrap-anywhere support-customer-order-code"
                        >
                          {order.code}
                        </Link>
                        <span className="badge">{formatOrderStatus(order.status)}</span>
                      </div>
                      <div className="support-customer-order-details">
                        <span>{formatVnd(order.total)}</span>
                        <span aria-hidden="true"> · </span>
                        <span>{order.itemCount} sản phẩm</span>
                      </div>
                      <div className="support-customer-order-date">
                        <time dateTime={order.createdAt}>
                          {formatVietnamDateTime(order.createdAt)}
                        </time>
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </>
        )}
      </div>
    </aside>
  );
}
