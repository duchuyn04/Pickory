import { z } from 'zod';

// Schema kiểm phần `data` đã bóc khỏi response, không dùng `.strict()`.
// Khóa lạ như `imageUrl` bị bỏ qua, nên khóa tùy chọn mới của backend không làm hỏng trang.

// Chuỗi giờ của API luôn có múi giờ. Schema loại chuỗi hỏng để bộ định dạng ngày không ném lỗi.
const timestampSchema = z.string().datetime({ offset: true });

// Backend không ghi trường rỗng của người nhận, nên các trường con đều tùy chọn.
const contactSchema = z.object({
  name: z.string().optional(),
  phone: z.string().optional(),
  email: z.string().optional(),
});

const shippingAddressSchema = z.object({
  provinceName: z.string().optional(),
  wardName: z.string().optional(),
  line: z.string().optional(),
});

const invoiceSchema = z.object({
  companyName: z.string().optional(),
  taxCode: z.string().optional(),
  address: z.string().optional(),
  email: z.string().optional(),
});

const itemSchema = z.object({
  skuId: z.string(),
  name: z.string(),
  // Sản phẩm không có phân loại thì `variantName` là `null`. Backend luôn gửi khóa này.
  variantName: z.string().nullable(),
  quantity: z.number().int(),
  // Tiền là số nguyên đồng, không có phần thập phân.
  unitPrice: z.number().int(),
  lineTotal: z.number().int(),
});

const historyEntrySchema = z.object({
  fromStatus: z.string(),
  toStatus: z.string(),
  // Hệ thống hoặc khách vãng lai chuyển trạng thái thì ba trường dưới là `null`.
  actorId: z.string().nullable(),
  actorName: z.string().nullable(),
  reason: z.string().nullable(),
  createdAt: timestampSchema,
});

const shipmentSchema = z.object({
  id: z.string(),
  trackingCode: z.string(),
  status: z.string(),
  fee: z.number().int(),
});

const suggestedParcelSchema = z.object({
  weightGrams: z.number().int(),
  lengthCm: z.number().int(),
  widthCm: z.number().int(),
  heightCm: z.number().int(),
});

export const adminOrderDetailSchema = z.object({
  code: z.string(),
  // Ba trường dưới là giá trị enum của backend. Frontend đổi sang nhãn tiếng Việt bằng `order-labels.ts`.
  status: z.string(),
  paymentStatus: z.string(),
  paymentMethod: z.string(),
  createdAt: timestampSchema,
  // Đơn không có hạn thanh toán thì giá trị là `null`. Khóa bị mất phải báo lỗi.
  paymentDeadline: timestampSchema.nullable(),
  flags: z.array(z.string()),
  contact: contactSchema,
  shippingAddress: shippingAddressSchema,
  // Khách không yêu cầu hóa đơn thì giá trị là `null`. Backend luôn gửi khóa này.
  invoice: invoiceSchema.nullable(),
  items: z.array(itemSchema),
  subtotal: z.number().int(),
  shippingFee: z.number().int(),
  shippingDiscount: z.number().int(),
  total: z.number().int(),
  shippingFeeSource: z.enum(['ghn', 'cached', 'fallback']),
  statusHistory: z.array(historyEntrySchema),
  // Đơn chưa có vận đơn GHN thì `shipment` là `null`. Giao thủ công cũng không tạo vận đơn.
  shipment: shipmentSchema.nullable(),
  suggestedParcel: suggestedParcelSchema.nullable(),
  refundTransactionRef: z.string().nullable(),
});
