import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { ErrorAlert } from '../../../../shared/error-alert';
import { ForbiddenNotice } from '../../../../shared/forbidden-notice';
import { Pager } from '../../../../shared/pager';
import { rowNumber } from '../../../../shared/row-number';
import { TabList, tabId } from '../../../../shared/tab-list';
import { useListFilters } from '../../../../shared/use-list-filters';
import { formatVietnamDateTime } from '../../../../shared/vietnam-time';
import { formatProductPrice } from '../../../catalog/customer/product-display';
import { useCan } from '../../../identity/hooks/use-actor';
import { formatOrderFlag } from '../../order-labels';
import { orderFlagBadgeClass } from '../orders/order-badges';
import {
  ALL_MATCH_STATUSES,
  DEFAULT_MATCH_STATUS,
  PAYMENT_FILTER_NAMES,
  fetchCodPendingOrders,
  fetchPaymentTransactions,
  type PaymentTab,
  type PaymentTransaction,
} from './admin-payments-api';
import { AttachOrderDialog, IgnoreTransactionDialog, ReceiveCodDialog } from './payment-dialogs';
import './admin-payments.css';

const PANEL_ID = 'payment-panel';

const MATCH_STATUS_LABELS: Record<string, string> = {
  unmatched: 'Chưa khớp',
  matched: 'Đã khớp',
  ignored: 'Đã bỏ qua',
};

const MATCH_STATUS_BADGES: Record<string, string> = {
  unmatched: 'badge badge-warn',
  matched: 'badge badge-ok',
  ignored: 'badge',
};

// Hộp thoại đang mở. Mỗi hộp thoại giữ giao dịch hoặc mã đơn mà nó thao tác.
type OpenDialog =
  | { kind: 'attach'; transaction: PaymentTransaction }
  | { kind: 'ignore'; transaction: PaymentTransaction }
  | { kind: 'cod'; orderCode: string };

// Màn đối soát: giao dịch SePay chưa khớp đơn và đơn COD đã giao chưa nhận tiền.
export function AdminPaymentsPage() {
  const canReadPayments = useCan('payments:read');
  const canReconcile = useCan('payments:reconcile');
  const canReadOrders = useCan('orders:read');
  const { values, page, setFilter, setPage } = useListFilters(PAYMENT_FILTER_NAMES);
  const [openDialog, setOpenDialog] = useState<OpenDialog | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  // Tab COD cần xem danh sách đơn, nên người không có `orders:read` chỉ thấy tab SePay.
  const tabs: { code: PaymentTab; label: string }[] = [{ code: 'sepay', label: 'SePay' }];
  if (canReadOrders) tabs.push({ code: 'cod', label: 'COD' });
  // Tham số `tab` lạ hoặc tab bị ẩn do thiếu quyền thì dùng tab SePay.
  const selectedTab: PaymentTab = values.tab === 'cod' && canReadOrders ? 'cod' : 'sepay';
  const matchStatus = values.matchStatus || DEFAULT_MATCH_STATUS;

  const transactionsQuery = useQuery({
    queryKey: ['admin-payment-transactions', matchStatus, values.orderFlag, page],
    queryFn: () => fetchPaymentTransactions({ matchStatus, orderFlag: values.orderFlag }, page),
    enabled: canReadPayments && selectedTab === 'sepay',
  });

  const codQuery = useQuery({
    queryKey: ['admin-orders', 'cod-pending', page],
    queryFn: () => fetchCodPendingOrders(page),
    enabled: canReadPayments && selectedTab === 'cod',
  });

  // Hook ở trên phải chạy hết trước lệnh return sớm này. Backend vẫn chặn mọi request thật sự.
  if (!canReadPayments) {
    return <ForbiddenNotice permission="payments:read" subject="màn đối soát thanh toán" />;
  }

  function closeDialog() {
    setOpenDialog(null);
  }

  const transactions = transactionsQuery.data;
  const codOrders = codQuery.data;

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Đối soát thanh toán</h1>
          <p>Khớp giao dịch chuyển khoản SePay với đơn và ghi nhận tiền COD đã thu.</p>
        </div>
      </div>

      {notice && (
        <p role="status" className="alert alert-ok">
          {notice}
        </p>
      )}

      <section className="card card-flush">
        <TabList
          label="Nguồn thanh toán"
          idPrefix="payment"
          panelId={PANEL_ID}
          tabs={tabs}
          selected={selectedTab}
          onSelect={(code) => setFilter('tab', code)}
        />

        {selectedTab === 'sepay' && (
          <div className="filters-row">
            <div className="field">
              <label htmlFor="payment-match-status">Trạng thái khớp</label>
              <select
                id="payment-match-status"
                value={matchStatus}
                onChange={(event) => setFilter('matchStatus', event.target.value)}
              >
                <option value="unmatched">Chưa khớp</option>
                <option value="matched">Đã khớp</option>
                <option value="ignored">Đã bỏ qua</option>
                <option value={ALL_MATCH_STATUSES}>Tất cả</option>
              </select>
            </div>
            <div className="field">
              <label htmlFor="payment-order-flag">Ghi chú</label>
              <select
                id="payment-order-flag"
                value={values.orderFlag}
                onChange={(event) => setFilter('orderFlag', event.target.value)}
              >
                <option value="">Tất cả</option>
                <option value="underpaid">Thiếu tiền</option>
                <option value="overpaid">Thừa tiền</option>
              </select>
            </div>
          </div>
        )}

        <div id={PANEL_ID} role="tabpanel" aria-labelledby={tabId('payment', selectedTab)}>
          {selectedTab === 'sepay' ? (
            transactionsQuery.isLoading ? (
              <p role="status" className="empty-state">
                Đang nạp danh sách giao dịch...
              </p>
            ) : transactionsQuery.isError ? (
              <ErrorAlert
                message={
                  transactionsQuery.error instanceof Error
                    ? transactionsQuery.error.message
                    : 'Tải danh sách giao dịch thất bại.'
                }
                className="payment-list-alert"
              />
            ) : transactions && transactions.items.length === 0 ? (
              <p className="empty-state">Không có giao dịch nào phù hợp.</p>
            ) : (
              <div className="table-wrap">
                <table className="tbl payment-table">
                  <thead>
                    <tr>
                      <th scope="col" className="num col-row-number">
                        STT
                      </th>
                      <th scope="col">Nhận lúc</th>
                      <th scope="col">Mã giao dịch</th>
                      <th scope="col" className="num">
                        Số tiền
                      </th>
                      <th scope="col">Nội dung</th>
                      <th scope="col">Khớp</th>
                      <th scope="col">Đơn</th>
                      {canReconcile && (
                        <th scope="col" className="num">
                          Thao tác
                        </th>
                      )}
                    </tr>
                  </thead>
                  <tbody>
                    {transactions &&
                      transactions.items.map((transaction, index) => (
                        <tr key={transaction.id}>
                          {/* STT theo `page` và `limit` của response giao dịch, không lấy của bảng COD. */}
                          <td className="num col-row-number">{rowNumber(transactions, index)}</td>
                          <td className="payment-time">
                            {formatVietnamDateTime(transaction.receivedAt)}
                          </td>
                          <th scope="row" className="wrap-anywhere">
                            {transaction.providerTxId}
                          </th>
                          <td className="num">{formatProductPrice(transaction.amount)}</td>
                          <td className="payment-content wrap-anywhere">{transaction.content}</td>
                          <td>
                            <span
                              className={MATCH_STATUS_BADGES[transaction.matchStatus] ?? 'badge'}
                            >
                              {MATCH_STATUS_LABELS[transaction.matchStatus] ??
                                transaction.matchStatus}
                            </span>
                          </td>
                          <td>
                            {transaction.order && (
                              <>
                                {/* Mã đơn là liên kết sang chi tiết, kèm cờ cho biết khoản tiền thiếu hay thừa. */}
                                <Link to={`/admin/orders/${transaction.order.code}`}>
                                  {transaction.order.code}
                                </Link>
                                <div className="row-actions">
                                  {transaction.order.flags.map((flag) => (
                                    <span key={flag} className={orderFlagBadgeClass(flag)}>
                                      {formatOrderFlag(flag)}
                                    </span>
                                  ))}
                                </div>
                              </>
                            )}
                          </td>
                          {canReconcile && (
                            <td className="num">
                              {/* Chỉ giao dịch chưa khớp mới gắn hoặc bỏ qua được. */}
                              {transaction.matchStatus === 'unmatched' && (
                                <div className="row-actions">
                                  <button
                                    type="button"
                                    className="btn btn-sm btn-primary"
                                    onClick={() => setOpenDialog({ kind: 'attach', transaction })}
                                  >
                                    Gắn vào đơn
                                  </button>
                                  <button
                                    type="button"
                                    className="btn btn-sm"
                                    onClick={() => setOpenDialog({ kind: 'ignore', transaction })}
                                  >
                                    Bỏ qua
                                  </button>
                                </div>
                              )}
                            </td>
                          )}
                        </tr>
                      ))}
                  </tbody>
                </table>
              </div>
            )
          ) : codQuery.isLoading ? (
            <p role="status" className="empty-state">
              Đang nạp danh sách đơn COD...
            </p>
          ) : codQuery.isError ? (
            <ErrorAlert
              message={
                codQuery.error instanceof Error
                  ? codQuery.error.message
                  : 'Tải danh sách đơn COD thất bại.'
              }
              className="payment-list-alert"
            />
          ) : codOrders && codOrders.items.length === 0 ? (
            <p className="empty-state">Không có đơn COD nào đang chờ nhận tiền.</p>
          ) : (
            <div className="table-wrap">
              <table className="tbl cod-table">
                <thead>
                  <tr>
                    <th scope="col" className="num col-row-number">
                      STT
                    </th>
                    <th scope="col">Mã đơn</th>
                    <th scope="col">Ngày đặt</th>
                    <th scope="col">Người nhận</th>
                    <th scope="col" className="num">
                      Tổng tiền
                    </th>
                    {canReconcile && (
                      <th scope="col" className="num">
                        Thao tác
                      </th>
                    )}
                  </tr>
                </thead>
                <tbody>
                  {codOrders &&
                    codOrders.items.map((order, index) => (
                      <tr key={order.code}>
                        {/* STT theo `page` và `limit` của response đơn COD, không lấy của bảng giao dịch. */}
                        <td className="num col-row-number">{rowNumber(codOrders, index)}</td>
                        <th scope="row">
                          <Link className="wrap-anywhere" to={`/admin/orders/${order.code}`}>
                            {order.code}
                          </Link>
                        </th>
                        <td className="payment-time">{formatVietnamDateTime(order.createdAt)}</td>
                        <td>
                          {order.recipientName || 'Chưa có tên'}
                          <span className="cell-sub">{order.recipientPhone || 'Chưa có'}</span>
                        </td>
                        <td className="num">{formatProductPrice(order.total)}</td>
                        {canReconcile && (
                          <td className="num">
                            <button
                              type="button"
                              className="btn btn-sm btn-primary"
                              onClick={() => setOpenDialog({ kind: 'cod', orderCode: order.code })}
                            >
                              Đã nhận tiền COD
                            </button>
                          </td>
                        )}
                      </tr>
                    ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        {selectedTab === 'sepay' && transactions && !transactionsQuery.isError && (
          <Pager
            page={page}
            total={transactions.total}
            limit={transactions.limit}
            onPageChange={setPage}
            label="Phân trang giao dịch"
          />
        )}
        {selectedTab === 'cod' && codOrders && !codQuery.isError && (
          <Pager
            page={page}
            total={codOrders.total}
            limit={codOrders.limit}
            onPageChange={setPage}
            label="Phân trang đơn COD"
          />
        )}
      </section>

      {openDialog?.kind === 'attach' && (
        <AttachOrderDialog
          transaction={openDialog.transaction}
          onClose={closeDialog}
          onDone={setNotice}
        />
      )}
      {openDialog?.kind === 'ignore' && (
        <IgnoreTransactionDialog
          transaction={openDialog.transaction}
          onClose={closeDialog}
          onDone={setNotice}
        />
      )}
      {openDialog?.kind === 'cod' && (
        <ReceiveCodDialog
          orderCode={openDialog.orderCode}
          onClose={closeDialog}
          onDone={setNotice}
        />
      )}
    </div>
  );
}
