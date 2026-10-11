import React, { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { FieldError, fieldErrorProps } from '../../../shared/field-error';
import { AdminFormDialog } from './admin-form-dialog';
import { toCatalogErrorMessage } from './catalog-errors';
import {
  MAX_REASON_LENGTH,
  stockAfterAdjustment,
  toAdjustmentBody,
  validateAdjustment,
  type AdjustStockResult,
  type AdjustType,
  type AdjustmentErrors,
} from './inventory-logic';
import './inventory.css';

interface AdjustStockDialogProps {
  skuId: string;
  // Mã SKU hiện trong tiêu đề, để admin biết đang điều chỉnh SKU nào.
  skuLabel: string;
  currentStock: number;
  onClose: () => void;
  onAdjusted: (result: AdjustStockResult) => void;
}

// Hộp thoại điều chỉnh tồn kho bằng tay. Trang cha chỉ render khi có quyền `inventory:write`.
export function AdjustStockDialog({
  skuId,
  skuLabel,
  currentStock,
  onClose,
  onAdjusted,
}: AdjustStockDialogProps) {
  const [type, setType] = useState<AdjustType>('manual');
  const [delta, setDelta] = useState('');
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<AdjustmentErrors>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Ô trống chưa đổi gì nên số tồn sau điều chỉnh bằng số hiện tại. Chuỗi sai mới báo chưa hợp lệ.
  const stockAfter = delta.trim() === '' ? currentStock : stockAfterAdjustment(currentStock, delta);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    // Bấm lần hai trong lúc chờ API không được gửi thêm request.
    if (isSubmitting) return;
    setApiError(null);

    const form = { type, delta, reason };
    const nextErrors = validateAdjustment(form);
    setErrors(nextErrors);
    // Lỗi nhập liệu hiện dưới ô và dừng tại đây, để không gửi request chắc chắn bị backend từ chối.
    if (nextErrors.delta || nextErrors.reason) return;

    setIsSubmitting(true);
    try {
      const result = await apiRequest<AdjustStockResult>(`/api/admin/skus/${skuId}/movements`, {
        method: 'POST',
        body: JSON.stringify(toAdjustmentBody(form)),
      });
      onAdjusted(result);
      onClose();
    } catch (err: unknown) {
      // Giữ nguyên dữ liệu đã nhập. Lỗi 409 cho admin sửa lại số lượng mà không nhập lại lý do.
      setApiError(toCatalogErrorMessage(err, 'Điều chỉnh tồn kho thất bại.'));
      setIsSubmitting(false);
    }
  }

  return (
    <AdminFormDialog
      isOpen
      onClose={onClose}
      title={`Điều chỉnh tồn kho ${skuLabel}`}
      formId="adjust-stock-form"
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitText="Lưu điều chỉnh"
      error={apiError}
    >
      <div className="field">
        <label htmlFor="adjust-type">Loại điều chỉnh</label>
        <select
          id="adjust-type"
          name="adjust-type"
          value={type}
          onChange={(event) => setType(event.target.value as AdjustType)}
        >
          <option value="manual">Điều chỉnh</option>
          <option value="return">Khách trả hàng</option>
        </select>
      </div>
      <div className="field">
        <label htmlFor="adjust-delta">Số lượng thay đổi</label>
        <input
          id="adjust-delta"
          name="adjust-delta"
          type="number"
          step={1}
          value={delta}
          onChange={(event) => setDelta(event.target.value)}
          {...fieldErrorProps('adjust-delta', errors.delta)}
        />
        <FieldError inputId="adjust-delta" message={errors.delta} />
        <small>
          {type === 'return'
            ? 'Nhập số dương: số cái khách trả về kho.'
            : 'Nhập số dương để thêm hàng, số âm để trừ hàng.'}
        </small>
      </div>
      <div className="field">
        <label htmlFor="adjust-reason">Lý do</label>
        <textarea
          id="adjust-reason"
          name="adjust-reason"
          rows={3}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          {...fieldErrorProps('adjust-reason', errors.reason)}
        />
        <FieldError inputId="adjust-reason" message={errors.reason} />
        <small>
          {reason.trim().length}/{MAX_REASON_LENGTH} ký tự
        </small>
      </div>
      {/* Hai con số đặt cạnh nhau để admin thấy trước kết quả trước khi lưu. */}
      <dl className="stock-preview">
        <div>
          <dt>Tồn kho hiện tại</dt>
          <dd>{currentStock}</dd>
        </div>
        <div>
          <dt>Sau điều chỉnh</dt>
          <dd className={stockAfter !== null && stockAfter < 0 ? 'stock-negative' : undefined}>
            {stockAfter === null ? 'Chưa hợp lệ' : stockAfter}
          </dd>
        </div>
      </dl>
    </AdminFormDialog>
  );
}
