import { useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { AdminFormDialog } from '../../../../shared/admin-form-dialog';
import { ErrorAlert } from '../../../../shared/error-alert';
import { formatProductPrice } from '../../../catalog/customer/product-display';
import { formatOrderStatus, formatPaymentStatus } from '../../order-labels';
import type { AdminOrderDetail } from '../orders/admin-orders-api';
import {
  fetchOrderForAttachment,
  receiveCodPayment,
  updatePaymentTransaction,
  type PaymentTransaction,
} from './admin-payments-api';
import './admin-payments.css';

// Thông báo lỗi của API là tiếng Việt và đã nói rõ nguyên nhân, nên hiện nguyên văn.
function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

// Sau thao tác thành công, danh sách giao dịch và danh sách đơn đều có thể đã đổi.
function useRefreshPaymentData() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['admin-payment-transactions'] });
    void queryClient.invalidateQueries({ queryKey: ['admin-orders'] });
    void queryClient.invalidateQueries({ queryKey: ['admin-order'] });
  };
}

interface DialogProps {
  onClose: () => void;
  onDone: (message: string) => void;
}

type LookupState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'found'; order: AdminOrderDetail }
  | { status: 'error'; message: string };

interface AttachDialogProps extends DialogProps {
  transaction: PaymentTransaction;
}

// Gắn giao dịch chưa khớp vào một đơn. Nhân viên phải tra đơn và xem tóm tắt trước khi gắn.
export function AttachOrderDialog({ transaction, onClose, onDone }: AttachDialogProps) {
  const refresh = useRefreshPaymentData();
  const [code, setCode] = useState('');
  const [lookup, setLookup] = useState<LookupState>({ status: 'idle' });
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleLookup() {
    // Mã backend lưu viết hoa, nên chuẩn hóa trước khi tra để `pk-1` tìm được `PK-1`.
    const normalized = code.trim().toUpperCase();
    if (normalized === '') {
      setLookup({ status: 'error', message: 'Nhập mã đơn cần gắn.' });
      return;
    }
    setApiError(null);
    setLookup({ status: 'loading' });
    try {
      const order = await fetchOrderForAttachment(normalized);
      setLookup({ status: 'found', order });
    } catch (error: unknown) {
      setLookup({ status: 'error', message: errorMessage(error, 'Không tra được đơn.') });
    }
  }

  function handleCodeChange(value: string) {
    setCode(value);
    // Mã đã đổi thì tóm tắt cũ không còn đúng. Bắt nhân viên tra lại trước khi gắn.
    setLookup({ status: 'idle' });
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    // Chỉ gắn khi đã tra được đơn. Nút đã tắt, và kiểm lại ở đây khi phím Enter gửi form.
    if (isSubmitting || lookup.status !== 'found') return;
    setApiError(null);
    setIsSubmitting(true);
    try {
      await updatePaymentTransaction(transaction.id, { orderCode: lookup.order.code });
      refresh();
      onDone(`Đã gắn giao dịch vào đơn ${lookup.order.code}.`);
      onClose();
    } catch (error: unknown) {
      // Giữ hộp thoại mở để nhân viên đọc lý do và chọn đơn khác.
      setApiError(errorMessage(error, 'Gắn giao dịch thất bại.'));
      setIsSubmitting(false);
    }
  }

  return (
    <AdminFormDialog
      isOpen
      onClose={onClose}
      title="Gắn giao dịch vào đơn"
      formId="attach-payment-form"
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitDisabled={lookup.status !== 'found'}
      submitText="Gắn"
      error={apiError}
    >
      <p className="muted">
        Giao dịch {transaction.providerTxId}, số tiền {formatProductPrice(transaction.amount)}. Nhập
        mã đơn rồi bấm Tìm để xem đơn trước khi gắn.
      </p>
      <div className="field">
        <label htmlFor="attach-order-code">Mã đơn</label>
        <div className="payment-lookup">
          <input
            id="attach-order-code"
            type="text"
            value={code}
            onChange={(event) => handleCodeChange(event.target.value)}
          />
          <button
            type="button"
            className="btn"
            onClick={handleLookup}
            disabled={lookup.status === 'loading'}
          >
            Tìm
          </button>
        </div>
      </div>
      {lookup.status === 'error' && <ErrorAlert message={lookup.message} />}
      {lookup.status === 'found' && (
        <dl className="payment-order-summary" aria-label="Tóm tắt đơn">
          <div>
            <dt>Mã đơn</dt>
            <dd>{lookup.order.code}</dd>
          </div>
          <div>
            <dt>Trạng thái</dt>
            <dd>{formatOrderStatus(lookup.order.status)}</dd>
          </div>
          <div>
            <dt>Thanh toán</dt>
            <dd>{formatPaymentStatus(lookup.order.paymentStatus)}</dd>
          </div>
          <div>
            <dt>Tổng tiền</dt>
            <dd>{formatProductPrice(lookup.order.total)}</dd>
          </div>
        </dl>
      )}
    </AdminFormDialog>
  );
}

interface IgnoreDialogProps extends DialogProps {
  transaction: PaymentTransaction;
}

// Bỏ qua giao dịch không thuộc đơn nào, ví dụ khách chuyển nhầm hoặc khoản không liên quan.
export function IgnoreTransactionDialog({ transaction, onClose, onDone }: IgnoreDialogProps) {
  const refresh = useRefreshPaymentData();
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    // Bấm lần hai trong lúc chờ API không được gửi thêm request.
    if (isSubmitting) return;
    setApiError(null);
    setIsSubmitting(true);
    try {
      await updatePaymentTransaction(transaction.id, { matchStatus: 'ignored' });
      refresh();
      onDone('Đã bỏ qua giao dịch.');
      onClose();
    } catch (error: unknown) {
      setApiError(errorMessage(error, 'Bỏ qua giao dịch thất bại.'));
      setIsSubmitting(false);
    }
  }

  return (
    <AdminFormDialog
      isOpen
      onClose={onClose}
      title="Bỏ qua giao dịch"
      formId="ignore-payment-form"
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitText="Bỏ qua"
      error={apiError}
    >
      <p className="muted">
        Giao dịch {transaction.providerTxId} sẽ được đánh dấu không thuộc đơn nào. Nội dung chuyển
        khoản: {transaction.content}
      </p>
    </AdminFormDialog>
  );
}

interface CodDialogProps extends DialogProps {
  orderCode: string;
}

// Ghi nhận đã thu tiền COD của một đơn đã giao.
export function ReceiveCodDialog({ orderCode, onClose, onDone }: CodDialogProps) {
  const refresh = useRefreshPaymentData();
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (isSubmitting) return;
    setApiError(null);
    setIsSubmitting(true);
    try {
      await receiveCodPayment(orderCode);
      refresh();
      onDone(`Đã ghi nhận nhận tiền COD của đơn ${orderCode}.`);
      onClose();
    } catch (error: unknown) {
      setApiError(errorMessage(error, 'Ghi nhận tiền COD thất bại.'));
      setIsSubmitting(false);
    }
  }

  return (
    <AdminFormDialog
      isOpen
      onClose={onClose}
      title={`Ghi nhận tiền COD ${orderCode}`}
      formId="receive-cod-form"
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitText="Ghi nhận"
      error={apiError}
    >
      <p className="muted">Ghi nhận đã thu đủ tiền thanh toán khi nhận hàng của đơn {orderCode}.</p>
    </AdminFormDialog>
  );
}
