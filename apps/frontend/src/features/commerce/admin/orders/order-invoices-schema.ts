import { z } from 'zod';

// Hợp đồng dữ liệu của `GET /api/admin/order-invoices` trong `tasks/exports-printing/api-contract.md`.

// Trước khi cấu hình cửa hàng có dữ liệu, bốn trường của `shop` là `null`. Hóa đơn khi đó in tên "Pickory".
const invoiceShopSchema = z.object({
  name: z.string().nullable(),
  address: z.string().nullable(),
  phone: z.string().nullable(),
  taxCode: z.string().nullable(),
});

const invoiceCustomerSchema = z.object({
  name: z.string(),
  phone: z.string(),
  email: z.string(),
});

// Thông tin xuất hóa đơn thuế giá trị gia tăng (VAT) của một đơn hàng.
const invoiceVatSchema = z.object({
  companyName: z.string(),
  taxCode: z.string(),
  address: z.string(),
  email: z.string(),
});

const invoiceLineSchema = z.object({
  skuCode: z.string(),
  name: z.string(),
  // Sản phẩm không có phân loại thì `optionText` là `null`.
  optionText: z.string().nullable(),
  quantity: z.number().int(),
  // Tiền là số nguyên đồng, không có phần thập phân.
  unitPrice: z.number().int(),
  lineTotal: z.number().int(),
});

const orderInvoiceSchema = z.object({
  code: z.string(),
  // Chuỗi giờ của API luôn có múi giờ. Schema loại chuỗi hỏng để bộ định dạng ngày không ném lỗi.
  createdAt: z.string().datetime({ offset: true }),
  // Ba trường dưới là giá trị enum của backend. Frontend đổi sang nhãn tiếng Việt bằng `order-labels.ts`.
  status: z.string(),
  paymentMethod: z.string(),
  paymentStatus: z.string(),
  customer: invoiceCustomerSchema,
  shippingAddressText: z.string(),
  // Khách không yêu cầu hóa đơn VAT thì giá trị là `null`. Backend luôn gửi khóa này, nên khóa bị mất phải báo lỗi.
  vatInvoice: invoiceVatSchema.nullable(),
  lines: z.array(invoiceLineSchema),
  subtotal: z.number().int(),
  shippingFee: z.number().int(),
  shippingDiscount: z.number().int(),
  total: z.number().int(),
});

// Response bọc trong `data`, giống các route admin khác.
export const orderInvoicesResponseSchema = z.object({
  data: z.object({
    shop: invoiceShopSchema,
    invoices: z.array(orderInvoiceSchema),
  }),
});

export type OrderInvoicesResponse = z.infer<typeof orderInvoicesResponseSchema>;
export type OrderInvoice = OrderInvoicesResponse['data']['invoices'][number];
