import { useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { FieldError, fieldErrorProps } from '../../../../shared/field-error';
import { AdminFormDialog } from '../../../../shared/admin-form-dialog';
import { ApiError } from '../../../../shared/api';
import { checkPermission, useActor } from '../../../identity/hooks/use-actor';
import {
  runOrderMutation,
  type AdminOrderDetail,
  type FulfillmentTargetStatus,
  type OrderMutation,
} from './admin-orders-api';
import {
  CANCEL_REASON_MAX_LENGTH,
  FULFILLMENT_REASON_MAX_LENGTH,
  TRANSACTION_REF_MAX_LENGTH,
  initialParcelForm,
  toParcelBody,
  validateCancelReason,
  validateFulfillmentReason,
  validateParcelForm,
  validateTransactionRef,
  type ParcelErrors,
  type ParcelField,
  type ParcelForm,
} from './order-action-forms';
import { availableOrderActions, type OrderActionKey } from './order-actions';
import { getErrorMessage } from '../../../../shared/error-message';

interface ActionCopy {
  // Chữ trên nút mở hộp thoại.
  buttonLabel: string;
  title: string;
  description: string;
  submitText: string;
  successMessage: string;
  // Nút hủy dùng màu nguy hiểm để nhân viên không bấm nhầm.
  danger?: boolean;
  // Đường dự phòng và việc ghi sổ dùng nút viền, để nút chính của đơn nổi bật.
  secondary?: boolean;
}

const ACTION_COPY: Record<OrderActionKey, ActionCopy> = {
  confirm: {
    buttonLabel: 'Xác nhận đơn',
    title: 'Xác nhận đơn hàng',
    description: 'Xác nhận đơn để chuyển sang bước chuẩn bị giao hàng.',
    submitText: 'Xác nhận',
    successMessage: 'Đã xác nhận đơn.',
  },
  createShipment: {
    buttonLabel: 'Tạo vận đơn',
    title: 'Tạo vận đơn',
    description: 'Nhập cân nặng và kích thước kiện hàng gửi cho đơn vị vận chuyển.',
    submitText: 'Tạo vận đơn',
    successMessage: 'Đã tạo vận đơn.',
  },
  startManualShipping: {
    buttonLabel: 'Giao hàng thủ công',
    title: 'Giao hàng thủ công',
    description:
      'Dùng khi GHN không nhận vận đơn và shop tự giao. Đơn chuyển sang đang giao và hệ thống không tạo vận đơn GHN. Lý do được lưu vào lịch sử đơn.',
    submitText: 'Bắt đầu giao thủ công',
    successMessage: 'Đã chuyển đơn sang giao hàng thủ công.',
    secondary: true,
  },
  cancelShipment: {
    buttonLabel: 'Hủy vận đơn',
    title: 'Hủy vận đơn',
    description: 'Hủy vận đơn đang có của đơn này.',
    submitText: 'Hủy vận đơn',
    successMessage: 'Đã hủy vận đơn.',
    danger: true,
  },
  returnToConfirmed: {
    buttonLabel: 'Quay về đã xác nhận',
    title: 'Quay về đã xác nhận',
    description:
      'Đơn quay về đã xác nhận để tạo vận đơn GHN hoặc giao thủ công lại. Cờ giao thủ công của đơn bị bỏ. Lý do được lưu vào lịch sử đơn.',
    submitText: 'Quay về đã xác nhận',
    successMessage: 'Đã đưa đơn về đã xác nhận.',
    secondary: true,
  },
  issueInvoice: {
    buttonLabel: 'Đã xuất hóa đơn',
    title: 'Đánh dấu đã xuất hóa đơn',
    description: 'Ghi nhận hóa đơn VAT của đơn này đã được xuất.',
    submitText: 'Đánh dấu',
    successMessage: 'Đã đánh dấu đã xuất hóa đơn.',
    secondary: true,
  },
  receiveCod: {
    buttonLabel: 'Đã nhận tiền COD',
    title: 'Ghi nhận đã nhận tiền COD',
    description: 'Ghi nhận đã thu đủ tiền thanh toán khi nhận hàng của đơn này.',
    submitText: 'Ghi nhận',
    successMessage: 'Đã ghi nhận nhận tiền COD.',
  },
  refund: {
    buttonLabel: 'Đã hoàn tiền',
    title: 'Ghi nhận đã hoàn tiền',
    description: 'Nhập mã giao dịch ngân hàng của lần hoàn tiền cho khách.',
    submitText: 'Ghi nhận hoàn tiền',
    successMessage: 'Đã ghi nhận hoàn tiền.',
  },
  cancel: {
    buttonLabel: 'Hủy đơn',
    title: 'Hủy đơn hàng',
    description: 'Lý do hủy được lưu vào lịch sử trạng thái của đơn.',
    submitText: 'Xác nhận hủy đơn',
    successMessage: 'Đã hủy đơn.',
    danger: true,
  },
};

// Nhãn và id của bốn ô kiện hàng.
const PARCEL_FIELD_INPUTS: { field: ParcelField; id: string; label: string }[] = [
  { field: 'weightGrams', id: 'parcel-weight', label: 'Cân nặng (g)' },
  { field: 'lengthCm', id: 'parcel-length', label: 'Dài (cm)' },
  { field: 'widthCm', id: 'parcel-width', label: 'Rộng (cm)' },
  { field: 'heightCm', id: 'parcel-height', label: 'Cao (cm)' },
];

// Đích của từng thao tác giao hàng thủ công, gửi lên route `fulfillment`. Thao tác mới cùng nhóm thêm vào đây.
const FULFILLMENT_TARGET_STATUS = {
  startManualShipping: 'shipping',
  returnToConfirmed: 'confirmed',
} satisfies Partial<Record<OrderActionKey, FulfillmentTargetStatus>>;

type FulfillmentActionKey = keyof typeof FULFILLMENT_TARGET_STATUS;

// Các thao tác trong nhóm này dùng chung ô lý do. Chúng cũng chung cách báo lỗi 409.
function isFulfillmentAction(action: OrderActionKey): action is FulfillmentActionKey {
  return action in FULFILLMENT_TARGET_STATUS;
}

// Nút nguy hiểm và nút phụ dùng kiểu khác nút chính. Nhờ vậy nút chính ngoài cùng bên phải nổi bật.
function buttonClassName(copy: ActionCopy): string {
  if (copy.danger) return 'btn btn-danger';
  if (copy.secondary) return 'btn btn-outline';
  return 'btn btn-primary';
}

interface ActionDialogProps {
  action: OrderActionKey;
  order: AdminOrderDetail;
  onClose: () => void;
  onDone: (message: string) => void;
}

// Hộp thoại xác nhận của một thao tác. Mỗi thao tác tự kiểm form trước khi gọi API.
function OrderActionDialog({ action, order, onClose, onDone }: ActionDialogProps) {
  const queryClient = useQueryClient();
  const copy = ACTION_COPY[action];
  const [reason, setReason] = useState('');
  const [transactionRef, setTransactionRef] = useState('');
  const [parcel, setParcel] = useState<ParcelForm>(() => initialParcelForm(order.suggestedParcel));
  const [reasonError, setReasonError] = useState<string | null>(null);
  const [transactionRefError, setTransactionRefError] = useState<string | null>(null);
  const [parcelErrors, setParcelErrors] = useState<ParcelErrors>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Dựng thao tác từ form. Trả `null` khi form sai, và đã đặt lỗi dưới ô.
  function buildMutation(): OrderMutation | null {
    switch (action) {
      case 'cancel': {
        const error = validateCancelReason(reason);
        setReasonError(error);
        return error ? null : { kind: 'cancel', reason: reason.trim() };
      }
      case 'refund': {
        const error = validateTransactionRef(transactionRef);
        setTransactionRefError(error);
        return error ? null : { kind: 'refund', transactionRef: transactionRef.trim() };
      }
      case 'createShipment': {
        const errors = validateParcelForm(parcel);
        setParcelErrors(errors);
        if (Object.keys(errors).length > 0) return null;
        return { kind: 'createShipment', parcel: toParcelBody(parcel).parcel };
      }
      case 'confirm':
        return { kind: 'confirm' };
      case 'issueInvoice':
        return { kind: 'issueInvoice' };
      case 'cancelShipment':
        return { kind: 'cancelShipment' };
      case 'startManualShipping':
      case 'returnToConfirmed': {
        const error = validateFulfillmentReason(reason);
        setReasonError(error);
        return error
          ? null
          : {
              kind: 'fulfillment',
              toStatus: FULFILLMENT_TARGET_STATUS[action],
              reason: reason.trim(),
            };
      }
      case 'receiveCod':
        return { kind: 'receiveCod' };
    }
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    // Bấm lần hai trong lúc chờ API không được gửi thêm request.
    if (isSubmitting) return;
    setApiError(null);

    const mutation = buildMutation();
    // Form sai dừng tại đây, để không gửi request chắc chắn bị backend từ chối.
    if (mutation === null) return;

    setIsSubmitting(true);
    try {
      const updated = await runOrderMutation(order.code, mutation);
      // Response là chi tiết đơn mới, nên ghi thẳng vào cache thay cho một lần tải lại.
      queryClient.setQueryData(['admin-order', order.code], updated);
      // Dòng của đơn trong mọi danh sách đã lọc có thể đổi trạng thái, nên đánh dấu cũ để tải lại.
      void queryClient.invalidateQueries({ queryKey: ['admin-orders'] });
      onDone(copy.successMessage);
      onClose();
    } catch (error: unknown) {
      // Giữ hộp thoại mở cùng lý do đã nhập. Nhân viên đọc lỗi, sau đó sửa lý do.
      setApiError(getErrorMessage(error, 'Thao tác thất bại.'));
      // Lỗi 409 nghĩa là trạng thái đơn đã đổi. Tải lại chi tiết để nút trên màn khớp với đơn thật.
      if (isFulfillmentAction(action) && error instanceof ApiError && error.status === 409) {
        void queryClient.invalidateQueries({ queryKey: ['admin-order', order.code] });
      }
      setIsSubmitting(false);
    }
  }

  function setParcelField(field: ParcelField, value: string) {
    setParcel((current) => ({ ...current, [field]: value }));
  }

  return (
    <AdminFormDialog
      isOpen
      onClose={onClose}
      title={`${copy.title} ${order.code}`}
      formId={`order-action-${action}`}
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitText={copy.submitText}
      error={apiError}
    >
      <p className="muted">{copy.description}</p>

      {action === 'cancel' && (
        <div className="field">
          <label htmlFor="cancel-reason">Lý do hủy</label>
          <textarea
            id="cancel-reason"
            rows={3}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            {...fieldErrorProps('cancel-reason', reasonError)}
          />
          <FieldError inputId="cancel-reason" message={reasonError} />
          <small>
            {reason.trim().length}/{CANCEL_REASON_MAX_LENGTH} ký tự
          </small>
        </div>
      )}

      {isFulfillmentAction(action) && (
        <div className="field">
          <label htmlFor="fulfillment-reason">Lý do</label>
          <textarea
            id="fulfillment-reason"
            rows={3}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            {...fieldErrorProps('fulfillment-reason', reasonError)}
          />
          <FieldError inputId="fulfillment-reason" message={reasonError} />
          <small>
            {reason.trim().length}/{FULFILLMENT_REASON_MAX_LENGTH} ký tự
          </small>
        </div>
      )}

      {action === 'refund' && (
        <div className="field">
          <label htmlFor="refund-ref">Mã giao dịch hoàn tiền</label>
          <input
            id="refund-ref"
            type="text"
            value={transactionRef}
            onChange={(event) => setTransactionRef(event.target.value)}
            {...fieldErrorProps('refund-ref', transactionRefError)}
          />
          <FieldError inputId="refund-ref" message={transactionRefError} />
          <small>
            {transactionRef.trim().length}/{TRANSACTION_REF_MAX_LENGTH} ký tự
          </small>
        </div>
      )}

      {action === 'createShipment' &&
        PARCEL_FIELD_INPUTS.map(({ field, id, label }) => (
          <div className="field" key={field}>
            <label htmlFor={id}>{label}</label>
            <input
              id={id}
              type="text"
              inputMode="numeric"
              value={parcel[field]}
              onChange={(event) => setParcelField(field, event.target.value)}
              {...fieldErrorProps(id, parcelErrors[field])}
            />
            <FieldError inputId={id} message={parcelErrors[field]} />
          </div>
        ))}
    </AdminFormDialog>
  );
}

interface OrderActionsProps {
  order: AdminOrderDetail;
  // Gọi khi thao tác thành công, để màn hiện thông báo ở trên cùng.
  onDone: (message: string) => void;
}

// Các nút thao tác của một đơn. Nút chỉ hiện khi nhân viên có quyền và đơn ở trạng thái route chấp nhận.
export function OrderActions({ order, onDone }: OrderActionsProps) {
  const { data: actor } = useActor();
  const [openAction, setOpenAction] = useState<OrderActionKey | null>(null);
  const actions = availableOrderActions(order, (permission) => checkPermission(actor, permission));

  return (
    <>
      <div className="order-actions row-actions">
        {actions.map((action) => (
          <button
            key={action}
            type="button"
            className={buttonClassName(ACTION_COPY[action])}
            onClick={() => setOpenAction(action)}
          >
            {ACTION_COPY[action].buttonLabel}
          </button>
        ))}
      </div>
      {openAction && (
        <OrderActionDialog
          action={openAction}
          order={order}
          onClose={() => setOpenAction(null)}
          onDone={onDone}
        />
      )}
    </>
  );
}
