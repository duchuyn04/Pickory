// Luật kiểm form của các hộp thoại thao tác đơn. Cùng giới hạn với backend để lỗi hiện ngay dưới ô.

export const CANCEL_REASON_MAX_LENGTH = 500;
export const TRANSACTION_REF_MAX_LENGTH = 100;

// Backend đọc cân nặng và kích thước vào `int`, nên giá trị lớn hơn bị từ chối.
const MAX_PARCEL_VALUE = 2_147_483_647;

// Trả câu lỗi, hoặc `null` khi lý do hợp lệ.
export function validateCancelReason(reason: string): string | null {
  const trimmed = reason.trim();
  // Lý do chỉ có khoảng trắng coi như trống, giống cách backend cắt khoảng trắng trước khi kiểm.
  if (trimmed.length === 0) return 'Nhập lý do hủy đơn.';
  if (trimmed.length > CANCEL_REASON_MAX_LENGTH) {
    return `Lý do hủy đơn không được dài quá ${CANCEL_REASON_MAX_LENGTH} ký tự.`;
  }
  return null;
}

export function validateTransactionRef(transactionRef: string): string | null {
  const trimmed = transactionRef.trim();
  if (trimmed.length === 0) return 'Nhập mã giao dịch hoàn tiền.';
  if (trimmed.length > TRANSACTION_REF_MAX_LENGTH) {
    return `Mã giao dịch hoàn tiền không được dài quá ${TRANSACTION_REF_MAX_LENGTH} ký tự.`;
  }
  return null;
}

export interface ParcelForm {
  weightGrams: string;
  lengthCm: string;
  widthCm: string;
  heightCm: string;
}

export type ParcelField = keyof ParcelForm;

export type ParcelErrors = Partial<Record<ParcelField, string>>;

export const PARCEL_FIELDS: ParcelField[] = ['weightGrams', 'lengthCm', 'widthCm', 'heightCm'];

interface SuggestedParcel {
  weightGrams: number;
  lengthCm: number;
  widthCm: number;
  heightCm: number;
}

// Điền sẵn từ kiện gợi ý của API để nhân viên chỉ sửa khi cần. Không có gợi ý thì để trống bốn ô.
export function initialParcelForm(suggested: SuggestedParcel | null): ParcelForm {
  if (suggested === null) {
    return { weightGrams: '', lengthCm: '', widthCm: '', heightCm: '' };
  }
  return {
    weightGrams: String(suggested.weightGrams),
    lengthCm: String(suggested.lengthCm),
    widthCm: String(suggested.widthCm),
    heightCm: String(suggested.heightCm),
  };
}

// Chỉ nhận chuỗi chữ số. `Number()` nhận cả `1e3` hay chuỗi rỗng nên không đủ chặt.
const POSITIVE_INTEGER_PATTERN = /^\d+$/;

function isPositiveInteger(value: string): boolean {
  const trimmed = value.trim();
  if (!POSITIVE_INTEGER_PATTERN.test(trimmed)) return false;
  const number = Number(trimmed);
  return number > 0 && number <= MAX_PARCEL_VALUE;
}

export function validateParcelForm(form: ParcelForm): ParcelErrors {
  const errors: ParcelErrors = {};
  for (const field of PARCEL_FIELDS) {
    if (!isPositiveInteger(form[field])) errors[field] = 'Nhập số nguyên dương.';
  }
  return errors;
}

// Body của `POST /api/admin/orders/{code}/shipment`. Gọi sau khi form đã qua `validateParcelForm`.
export function toParcelBody(form: ParcelForm) {
  return {
    parcel: {
      weightGrams: Number(form.weightGrams.trim()),
      lengthCm: Number(form.lengthCm.trim()),
      widthCm: Number(form.widthCm.trim()),
      heightCm: Number(form.heightCm.trim()),
    },
  };
}
