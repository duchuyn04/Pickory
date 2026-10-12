import { z } from 'zod';
import type { FieldErrors } from '../../../../shared/field-error';
import { getNameError, getPhoneError } from '../../../../shared/identity-validation';
import type { AddressValue } from '../../../shipping/address-picker';
import type { PaymentMethod } from './checkout-api';

// Giới hạn khớp `PlaceOrderRequests` và `CheckoutPreviewRequests` của backend.
export const NOTE_MAX_LENGTH = 500;
const EMAIL_MAX_LENGTH = 254;
export const ADDRESS_LINE_MAX_LENGTH = 200;
const COMPANY_NAME_MAX_LENGTH = 200;
const INVOICE_ADDRESS_MAX_LENGTH = 300;

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
// Chỉ nhận chữ số ASCII và dấu gạch ngang, giống `TaxCodeRule` của backend.
const TAX_CODE_PATTERN = /^[0-9-]+$/;
const TAX_CODE_DIGIT_COUNTS = [10, 13];

// Tên ô trùng với `key` mà backend trả trong `error.details`, nên lỗi từ server gán thẳng vào ô.
export type CheckoutFieldName =
  | 'contact.name'
  | 'contact.phone'
  | 'contact.email'
  | 'address'
  | 'address.provinceCode'
  | 'address.wardCode'
  | 'address.line'
  | 'note'
  | 'paymentMethod'
  | 'invoice'
  | 'invoice.companyName'
  | 'invoice.taxCode'
  | 'invoice.address'
  | 'invoice.email';

export type CheckoutFieldErrors = FieldErrors<CheckoutFieldName>;

export interface CheckoutFormValues {
  contact: { name: string; phone: string; email: string };
  address: AddressValue;
  note: string;
  paymentMethod: PaymentMethod;
  wantsInvoice: boolean;
  invoice: { companyName: string; taxCode: string; address: string; email: string };
}

function isValidTaxCode(taxCode: string): boolean {
  if (!TAX_CODE_PATTERN.test(taxCode)) return false;
  // Dấu gạch chỉ để ngăn nhóm số, nên không tính vào số chữ số.
  const digitCount = taxCode.replaceAll('-', '').length;
  return TAX_CODE_DIGIT_COUNTS.includes(digitCount);
}

function requiredEmail(requiredMessage: string, invalidMessage: string) {
  return z
    .string()
    .trim()
    .min(1, requiredMessage)
    .max(EMAIL_MAX_LENGTH, invalidMessage)
    .regex(EMAIL_PATTERN, invalidMessage);
}

const contactSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'Cần nhập họ và tên.')
    .superRefine((name, context) => {
      // Dùng chung luật của hồ sơ, vì backend cũng kiểm tên đặt hàng bằng `ContactFieldRules`.
      const message = getNameError(name);
      if (message) context.addIssue({ code: 'custom', message });
    }),
  phone: z
    .string()
    .trim()
    .min(1, 'Cần nhập số điện thoại.')
    .superRefine((phone, context) => {
      const message = getPhoneError(phone);
      if (message) context.addIssue({ code: 'custom', message });
    }),
  email: requiredEmail('Cần nhập email.', 'Email không đúng định dạng.'),
});

// Chỉ kiểm mã và số nhà. Tên tỉnh và tên phường do ô chọn địa chỉ giữ, backend tra lại từ danh mục.
const addressSchema = z.object({
  provinceCode: z.string().min(1, 'Cần chọn tỉnh/thành phố.'),
  wardCode: z.string().min(1, 'Cần chọn phường/xã.'),
  line: z
    .string()
    .trim()
    .min(1, 'Cần nhập số nhà, tên đường.')
    .max(ADDRESS_LINE_MAX_LENGTH, `Số nhà, tên đường tối đa ${ADDRESS_LINE_MAX_LENGTH} ký tự.`),
});

const noteSchema = z
  .string()
  .trim()
  .max(NOTE_MAX_LENGTH, `Ghi chú tối đa ${NOTE_MAX_LENGTH} ký tự.`);

const invoiceSchema = z.object({
  companyName: z
    .string()
    .trim()
    .min(1, 'Cần nhập tên công ty.')
    .max(COMPANY_NAME_MAX_LENGTH, `Tên công ty tối đa ${COMPANY_NAME_MAX_LENGTH} ký tự.`),
  taxCode: z
    .string()
    .trim()
    .min(1, 'Cần nhập mã số thuế.')
    .refine(isValidTaxCode, 'Mã số thuế gồm 10 hoặc 13 chữ số, có thể có dấu gạch ngang.'),
  address: z
    .string()
    .trim()
    .min(1, 'Cần nhập địa chỉ hóa đơn.')
    .max(INVOICE_ADDRESS_MAX_LENGTH, `Địa chỉ hóa đơn tối đa ${INVOICE_ADDRESS_MAX_LENGTH} ký tự.`),
  email: requiredEmail('Cần nhập email nhận hóa đơn.', 'Email nhận hóa đơn không đúng định dạng.'),
});

// Kiểm một schema con. Ghi lỗi đầu tiên của từng ô vào `errors`, với khóa là đường dẫn đầy đủ.
function collectErrors(
  prefix: string,
  schema: z.ZodType,
  value: unknown,
  errors: CheckoutFieldErrors,
): void {
  const result = schema.safeParse(value);
  if (result.success) return;

  for (const issue of result.error.issues) {
    const fieldName = [prefix, ...issue.path].filter((part) => part !== '').join('.');
    const key = fieldName as CheckoutFieldName;
    // Giữ lỗi đầu tiên của ô, vì lỗi sau thường là hệ quả của lỗi trước.
    if (!errors[key]) errors[key] = issue.message;
  }
}

// Kiểm cả form bằng Zod. Trả object rỗng khi form hợp lệ.
export function validateCheckoutForm(values: CheckoutFormValues): CheckoutFieldErrors {
  const errors: CheckoutFieldErrors = {};

  collectErrors('contact', contactSchema, values.contact, errors);
  collectErrors('address', addressSchema, values.address, errors);
  collectErrors('note', noteSchema, values.note, errors);

  // Ô hóa đơn chỉ bắt buộc khi khách chọn xuất hóa đơn. Giá trị cũ còn trong ô ẩn không được gây lỗi.
  if (values.wantsInvoice) {
    collectErrors('invoice', invoiceSchema, values.invoice, errors);
  }

  return errors;
}
