# Spec: commerce

Module id: `commerce` · Phụ thuộc: `identity`, `catalog`, `shipping` · Quy ước chung: `SPEC.md`

## Objective

Biến ý định mua thành đơn hàng đã giao: giỏ hàng, checkout, đơn hàng, thanh toán COD và chuyển khoản qua SePay, hoàn tiền, đánh giá sau khi mua. Đây là phần tạo ra doanh thu, nên **không được phụ thuộc AI**: API AI lỗi thì luồng mua vẫn chạy đầy đủ.

**User stories**

*Khách hàng (kể cả khách vãng lai)*
- Tôi thêm sản phẩm vào giỏ, đổi số lượng, xóa. Giỏ vẫn còn khi tôi quay lại hoặc đăng nhập.
- Tôi checkout bằng tên, SĐT, email, địa chỉ; thấy phí ship và tổng tiền trước khi đặt.
- Tôi chọn COD hoặc chuyển khoản. Nếu chuyển khoản, tôi quét mã VietQR và trang tự báo khi tiền về, không phải chụp màn hình gửi shop.
- Tôi nhập thông tin xuất hóa đơn VAT nếu cần.
- Tôi xem trạng thái đơn và mã vận đơn; khách vãng lai xem qua link trong email.
- Tôi tự hủy đơn khi shop chưa xác nhận.
- Tôi đánh giá sản phẩm đã nhận.

*Khách có tài khoản*
- Tôi lưu sổ địa chỉ, xem lịch sử đơn hàng.
- Đơn đã đặt khi chưa đăng nhập bằng cùng email được gắn vào tài khoản sau khi tôi xác thực email.

*Nhân viên `sales`*
- Tôi xem danh sách đơn theo trạng thái, xác nhận đơn, tạo vận đơn GHN, hủy đơn.
- Tôi xử lý các giao dịch SePay không tự khớp được (sai nội dung, thiếu tiền, thừa tiền).
- Tôi đánh dấu đã nhận tiền COD từ GHN.

*Nhân viên `support`*
- Tôi xem đơn của khách để trả lời câu hỏi; ẩn review vi phạm kèm lý do.

*Admin*
- Tôi cấu hình ngưỡng miễn phí ship, hạn mức COD, tài khoản nhận chuyển khoản; đánh dấu đã hoàn tiền.

## Thiết kế

### Giỏ hàng

- Giỏ gắn với `anonymousId` (khách vãng lai) hoặc `userId`. Mỗi dòng: `skuId`, số lượng.
- Tối đa 5 sản phẩm mỗi SKU và 20 dòng mỗi giỏ (cấu hình được).
- Giỏ **không lưu giá**. Mỗi lần xem giỏ, giá lấy mới từ `catalogReader.getSkus`. SKU hết hàng, bị tắt hoặc sản phẩm ngừng bán vẫn hiện trong giỏ nhưng có nhãn và không checkout được.
- Khi nhận `UserLinkedEvent`: gộp giỏ vãng lai vào giỏ tài khoản, cùng SKU thì cộng số lượng (không vượt giới hạn).
- Thêm vào giỏ không giữ hàng. Hàng chỉ được giữ khi đặt đơn.

### Checkout và đặt đơn

1. Khách nhập liên hệ (tên, SĐT, email bắt buộc), địa chỉ (từ danh mục của `shipping`), ghi chú, phương thức thanh toán, thông tin hóa đơn (tùy chọn).
2. Frontend gọi `POST /api/checkout/previews` để có tạm tính, phí ship, giảm phí ship, tổng tiền.
3. Khách bấm đặt: `POST /api/orders` kèm header `Idempotency-Key`. Server làm trong 1 transaction:
   - Tính lại toàn bộ giá từ `catalog` và phí từ `shipping.quote`. **Không tin số tiền gửi từ frontend.** Tổng khác preview thì trả 409 để khách xem lại.
   - `stock.reserve(orderId, items)`. Thiếu hàng thì trả 409 kèm danh sách SKU thiếu.
   - Tạo đơn với snapshot: tên sản phẩm, SKU, giá, ảnh, địa chỉ (tên và mã), phí ship.
   - Xóa các dòng đã đặt khỏi giỏ.
4. Gửi email xác nhận đơn. Email lỗi không làm đặt đơn thất bại.

**Phí ship:**
- Phí lấy từ `shipping.quote`.
- Tạm tính ≥ ngưỡng miễn phí (mặc định 500.000 đ, cấu hình được) thì phí khách trả = 0, shop chịu.
- `shipping` lỗi khi preview: hiện thông báo "Chưa tính được phí vận chuyển, thử lại", không cho đặt đơn với phí ước đoán.

**Hạn mức COD:** đơn có tổng tiền trên hạn mức (mặc định 20.000.000 đ, cấu hình được) chỉ được chọn chuyển khoản, vì giá trị cao dễ bị đặt ảo.

**Hóa đơn VAT:** thu tên công ty, mã số thuế (10 hoặc 13 chữ số, có thể có dấu gạch), địa chỉ, email nhận hóa đơn. Lưu theo đơn, hiện trong back-office. Shop tự xuất trên phần mềm hóa đơn điện tử, `sales` đánh dấu "đã xuất hóa đơn".

**Mã đơn:** dạng `PK` + 8 ký tự chữ hoa và số (bỏ các ký tự dễ nhầm `0 O 1 I`), ví dụ `PK7KX2M9QD`. Dùng làm nội dung chuyển khoản và `ref` cho vận đơn.

### Trạng thái đơn hàng

Đơn có 2 trạng thái độc lập: **trạng thái đơn** và **trạng thái thanh toán**.

```
                 ┌─(SePay, chưa trả)─→ pending_payment ─(tiền về)─┐
đặt đơn ─────────┤                         │ (quá 30 phút)        ▼
                 └─(COD)──────────────────────────────→ pending_confirmation
                                           ▼                      │ sales xác nhận
                                       cancelled ←────────── confirmed
                                           ▲                      │ tạo vận đơn
                                           └──── (hủy vận đơn) ── shipping ──→ delivered
                                                                  │
                                                                  └─→ returned
```

| Trạng thái đơn | Vào khi | Ai chuyển |
|---|---|---|
| `pending_payment` | Đặt đơn SePay | hệ thống |
| `pending_confirmation` | Đặt đơn COD, hoặc SePay đã nhận đủ tiền | hệ thống |
| `confirmed` | Sales xác nhận (đã gọi khách nếu cần) | `orders:update-status` |
| `shipping` | Tạo vận đơn GHN thành công | `orders:fulfill` |
| `delivered` | `shipping` báo `delivered` | hệ thống |
| `returned` | `shipping` báo `returned` | hệ thống |
| `cancelled` | Khách hủy, sales hủy, hoặc quá hạn thanh toán | khách, `orders:cancel`, hệ thống |

| Trạng thái thanh toán | Ý nghĩa |
|---|---|
| `unpaid` | Chưa trả (COD chưa giao, hoặc SePay chưa về) |
| `paid` | SePay đã nhận đủ, hoặc COD đã được `sales` đánh dấu nhận từ GHN |
| `refund_pending` | Đơn đã trả tiền nhưng bị hủy hoặc hoàn, chờ hoàn tiền |
| `refunded` | Admin đánh dấu đã chuyển khoản hoàn tiền, kèm mã giao dịch |

**Quy tắc:**
- Chuyển trạng thái không hợp lệ trả 409. Mọi chuyển đổi do nhân viên làm đều ghi `recordAudit`.
- Khách tự hủy được khi đơn ở `pending_payment` hoặc `pending_confirmation`.
- Nhân viên hủy được tới trước `delivered`. Đơn đang `shipping` thì phải hủy vận đơn GHN thành công trước.
- Hủy đơn thì `stock.release(orderId)`. Đơn đã `paid` khi hủy thì chuyển `refund_pending`.
- `returned` thì hàng **không** tự nhập lại kho. `sales` kiểm hàng rồi nhập lại bằng điều chỉnh tồn kho của `catalog` (loại `return`).
- Job mỗi phút: đơn `pending_payment` quá 30 phút thì hủy và trả hàng.

### Thanh toán SePay

**Hiển thị:** trang thanh toán hiện ảnh VietQR, số tài khoản, ngân hàng, số tiền, nội dung chuyển khoản (đúng mã đơn), và đồng hồ đếm ngược 30 phút. Ảnh QR lấy từ dịch vụ QR của SePay theo số tài khoản, mã ngân hàng, số tiền, nội dung. Frontend hỏi trạng thái đơn mỗi 3 giây và chuyển trang khi đã thanh toán.

**Webhook** `POST /api/webhooks/sepay`:
1. Xác thực header API key do SePay gửi (so sánh constant-time). Sai thì trả 401.
2. Validate payload bằng Zod. Chỉ xử lý giao dịch tiền vào, đúng số tài khoản đã cấu hình.
3. **Idempotent theo id giao dịch SePay**: id đã có thì trả thành công, không xử lý lại.
4. Lưu giao dịch thô vào `payment_transactions` trước, rồi mới khớp đơn.
5. Tìm mã đơn trong nội dung chuyển khoản (regex `PK[A-Z2-9]{8}`, không phân biệt hoa thường, bỏ qua ký tự ngân hàng tự chèn).
6. Khớp và xử lý:

| Trường hợp | Xử lý |
|---|---|
| Đúng mã, đơn `pending_payment`, đủ tiền | `paid`, chuyển `pending_confirmation`, phát `onOrderPaid` |
| Đúng mã, thiếu tiền | Giữ `pending_payment`, gắn cờ `underpaid`, báo `sales` xử lý |
| Đúng mã, thừa tiền | `paid` như bình thường, gắn cờ `overpaid` để hoàn phần thừa |
| Đúng mã nhưng đơn đã hủy vì quá hạn | Thử giữ hàng lại. Được thì khôi phục đơn thành `paid`; không đủ hàng thì đơn giữ `cancelled`, thanh toán `refund_pending` |
| Không tìm thấy mã đơn | Lưu là giao dịch chưa khớp, hiện trong màn hình đối soát |

7. Trả response thành công theo định dạng SePay yêu cầu, trong dưới 2 giây.

**Đối soát tay** (`payments:reconcile`): gắn giao dịch chưa khớp vào một đơn, hoặc đánh dấu "không thuộc đơn nào". Ghi audit log.

**Bảo mật:** API key webhook nằm trong biến môi trường của server. Số tài khoản nhận nằm trong `CommerceSettings`, admin sửa trên giao diện, mỗi lần sửa ghi audit log. Endpoint webhook chỉ nhận JSON, giới hạn kích thước body 16 KB.

### Thanh toán COD

- Đơn COD tạo vận đơn với `codAmount` = tổng tiền.
- Khi `delivered`, đơn COD vẫn `unpaid` cho tới khi `sales` đánh dấu đã nhận tiền COD từ GHN (đối soát theo bảng kê GHN). Màn hình đối soát lọc được đơn COD đã giao chưa nhận tiền.

### Theo dõi đơn

- Khách có tài khoản: trang "Đơn hàng của tôi".
- Khách vãng lai: email xác nhận có link `/orders/<mã>?t=<token>`. Token ngẫu nhiên, DB lưu hash. Link chỉ xem, không sửa thông tin giao hàng.
- Khi khách xác thực email, các đơn vãng lai có cùng email được gắn vào tài khoản.

### Sổ địa chỉ

Khách có tài khoản lưu tối đa 10 địa chỉ, chọn 1 địa chỉ mặc định. Địa chỉ dùng mã của danh mục `shipping`.

### Đánh giá sản phẩm

- Chỉ khách có tài khoản, đã có đơn `delivered` chứa sản phẩm đó, mới đánh giá được. Mỗi người 1 đánh giá cho mỗi sản phẩm, sửa được trong 30 ngày.
- Nội dung: số sao 1 đến 5 (bắt buộc), nhận xét tối đa 2.000 ký tự. Không có ảnh trong MVP.
- Hiện ngay. `support` (quyền `reviews:moderate`) ẩn hoặc hiện lại, bắt buộc chọn lý do. Ghi audit log.
- Điểm trung bình và phân bố số sao tính lại khi có đánh giá mới hoặc bị ẩn, cung cấp cho `catalog` qua interface đọc để hiện trên trang sản phẩm.
- Khách báo cáo được review vi phạm; review có từ 3 báo cáo thì đưa lên đầu hàng chờ của `support`.

### Interface public

Đường dẫn và mã TypeScript dưới đây thuộc bản Node. Module này sẽ viết bằng .NET.

```ts
// apps/backend/src/modules/commerce/index.ts
export const commerceReader: {
  getRatingSummary(productIds: string[]): Promise<Map<string, RatingSummary>>;   // cho catalog và tool get_review_insights của shopping-assistant
  getVisibleReviews(productId: string, opts: { limit: number; cursor?: string }): Promise<ReviewPage>; // cho tool get_review_insights
  getPurchasedProductIds(userId: string): Promise<string[]>;            // từng phục vụ recommendations (đã bỏ ngày 2026-10-14), chưa có bên dùng
  getOrdersForCustomer(userId: string, limit: number): Promise<OrderSummary[]>; // cho support-desk
  getOrderByCode(code: string): Promise<OrderDetail | null>;
};

// cho admin-console (bổ sung khi viết spec admin-console)
export const commerceReports: {
  getDailySales(range: DateRange): Promise<DailySales[]>;           // theo định nghĩa chỉ số trong SPEC-admin-console.md
  getProductSales(range: DateRange): Promise<ProductSales[]>;
  pendingCounts(): Promise<{ pendingConfirmation: number; unmatchedPayments: number; codUnreceived: number }>;
};

export { onOrderPlaced, onOrderPaid, onOrderStatusChanged, onCartItemAdded, onCartItemRemoved } from './events';
export { commercePermissions } from './permissions';
```

`commerce` đăng ký `onUserLinked`, `onUserDeleted` (từ `identity`) và `onShipmentStatusChanged` (từ `shipping`).

Khi `UserDeletedEvent`: xóa giỏ, sổ địa chỉ; ẩn danh hóa đánh giá (giữ nội dung, bỏ tên). Đơn hàng giữ lại vì nghĩa vụ kế toán, nhưng không còn gắn với tài khoản.

## API

**Khách hàng**

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/cart` | công khai | Giỏ hiện tại, giá mới nhất |
| POST · PATCH · DELETE | `/api/cart/items[/:skuId]` | công khai | Thêm, đổi số lượng, xóa. Thêm nhận `source` tùy chọn (ví dụ `shopping-assistant`) để đo hiệu quả AI |
| POST | `/api/checkout/previews` | công khai | Tạm tính, phí ship, tổng |
| POST | `/api/orders` | công khai | Đặt đơn, bắt buộc `Idempotency-Key` |
| GET | `/api/orders` | khách đã đăng nhập | Lịch sử đơn |
| GET | `/api/orders/:code` | chủ đơn, hoặc có token | Chi tiết đơn |
| GET | `/api/orders/:code/payment` | chủ đơn, hoặc có token | Cho trang chờ thanh toán hỏi định kỳ |
| POST | `/api/orders/:code/cancellation` | chủ đơn, hoặc có token | Khách tự hủy |
| GET · POST · PATCH · DELETE | `/api/me/addresses[/:id]` | khách đã đăng nhập | Sổ địa chỉ |
| GET | `/api/products/:id/reviews` | công khai | Đánh giá đang hiện |
| POST · PATCH | `/api/products/:id/reviews[/mine]` | người đã mua | Viết, sửa đánh giá |
| POST | `/api/reviews/:id/reports` | khách đã đăng nhập | Báo cáo vi phạm |
| POST | `/api/webhooks/sepay` | SePay (API key) | Nhận giao dịch |

**Back-office**

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/admin/orders[/:code]` | `orders:read` | Danh sách, lọc theo trạng thái, thanh toán, ngày, cờ |
| POST | `/api/admin/orders/:code/confirmation` | `orders:update-status` | Xác nhận đơn |
| POST | `/api/admin/orders/:code/shipment` | `orders:fulfill` | Tạo vận đơn (sửa được kích thước kiện) |
| DELETE | `/api/admin/orders/:code/shipment` | `orders:fulfill` | Hủy vận đơn |
| POST | `/api/admin/orders/:code/cancellation` | `orders:cancel` | Hủy đơn, bắt buộc lý do |
| PUT | `/api/admin/orders/:code/invoice-issuance` | `orders:update-status` | Đánh dấu đã xuất hóa đơn VAT |
| GET | `/api/admin/payments/transactions` | `payments:read` | Giao dịch SePay, lọc chưa khớp |
| PATCH | `/api/admin/payments/transactions/:id` | `payments:reconcile` | Khớp tay vào đơn |
| PUT | `/api/admin/orders/:code/cod-receipt` | `payments:reconcile` | Đánh dấu đã nhận COD |
| PUT | `/api/admin/orders/:code/refund` | `refunds:manage` | Đánh dấu đã hoàn tiền, kèm mã giao dịch |
| GET | `/api/admin/reviews` | `reviews:moderate` | Hàng chờ kiểm duyệt |
| PATCH | `/api/admin/reviews/:id` | `reviews:moderate` | Ẩn hoặc hiện lại bằng `hidden` và `reason` |
| GET · PUT | `/api/admin/commerce/settings` | `commerce-settings:manage` | Ngưỡng miễn phí ship, hạn mức COD, tài khoản nhận |

## Data Model (Prisma, rút gọn)

```prisma
model Cart     { /* id, anonymousId?, userId? @unique, updatedAt */ }
model CartItem { /* cartId, skuId, quantity; @@id([cartId, skuId]) */ }

model Order {
  id                String        @id @default(uuid())
  code              String        @unique
  userId            String?
  anonymousId       String
  contact           Json          // tên, SĐT, email
  shippingAddress   Json          // mã và tên tại thời điểm đặt
  invoiceInfo       Json?
  paymentMethod     PaymentMethod // cod | sepay
  status            OrderStatus
  paymentStatus     PaymentStatus
  flags             String[]      // underpaid, overpaid, invoice_requested, ...
  subtotal          Int
  shippingFee       Int           // phí GHN báo
  shippingDiscount  Int           // phần shop chịu
  total             Int
  paymentDeadline   DateTime?
  accessTokenHash   String        // cho link xem đơn của khách vãng lai
  idempotencyKey    String        @unique
  createdAt         DateTime      @default(now())
  @@index([status, createdAt])
  @@map("orders")
}

model OrderItem          { /* orderId, skuId, productId, nameSnapshot, skuCodeSnapshot, optionValues, imagePublicId, unitPrice, quantity */ }
model OrderStatusHistory { /* orderId, from, to, actorId?, reason?, createdAt */ }
model PaymentTransaction {
  id            String   @id @default(uuid())
  provider      String   // 'sepay'
  providerTxId  String   @unique   // id giao dịch của SePay, chống xử lý trùng
  amount        Int
  content       String
  raw           Json
  orderId       String?
  matchStatus   String   // matched | unmatched | ignored
  receivedAt    DateTime @default(now())
  @@map("payment_transactions")
}
model SavedAddress  { /* userId, label, name, phone, address Json, isDefault */ }
model Review        { /* productId, userId, orderId, rating, body, hiddenAt?, hiddenReason?, reportCount, createdAt, updatedAt; @@unique([productId, userId]) */ }
model CommerceSettings { /* freeShippingThreshold, codMaxAmount, sepayAccountNumber, sepayBankCode */ }
```

## Frontend

*Khách hàng* (`features/commerce/`): giỏ hàng (mini cart và trang giỏ), checkout một trang, trang chờ thanh toán VietQR có đếm ngược, trang đặt hàng thành công, đơn hàng của tôi, chi tiết đơn có tiến trình giao hàng, sổ địa chỉ, khối đánh giá trên trang sản phẩm.

*Back-office* (`features/commerce/admin/`): danh sách đơn có tab theo trạng thái và bộ lọc cờ; chi tiết đơn có các nút thao tác theo quyền và lịch sử trạng thái; màn hình đối soát SePay và COD; hàng chờ kiểm duyệt review; cấu hình.

## Testing

- Unit: tính tiền (tạm tính, miễn phí ship, hạn mức COD); máy trạng thái đơn (mọi chuyển đổi hợp lệ và không hợp lệ); sinh mã đơn; tìm mã đơn trong nội dung chuyển khoản (có khoảng trắng, chữ thường, ngân hàng chèn thêm chữ); validate mã số thuế.
- Integration:
  - Đặt đơn với giá frontend gửi sai: server dùng giá đúng và trả 409 nếu khác preview.
  - Gửi cùng `Idempotency-Key` 2 lần chỉ tạo 1 đơn.
  - Webhook SePay đủ 5 trường hợp trong bảng; gửi lại cùng giao dịch không xử lý 2 lần; sai API key trả 401.
  - Job hết hạn hủy đơn và trả hàng; tiền về sau khi hủy được xử lý đúng.
  - Gộp giỏ khi đăng nhập; gắn đơn vãng lai sau khi xác thực email.
  - Ma trận quyền cho mọi endpoint back-office.
  - `onShipmentStatusChanged` cập nhật đúng trạng thái đơn.
- `shipping` và `catalog` dùng implementation thật trong integration test, GHN dùng HTTP mock.
- E2E:
  - Khách vãng lai mua COD.
  - Khách mua SePay (bắn webhook giả lập), trang tự chuyển khi đã thanh toán.
  - Sales xác nhận và tạo vận đơn.
  - Khách đánh giá sau khi giao, support ẩn đánh giá.

## Boundaries (riêng module)

- **Always:** tính lại giá và phí ở server khi đặt đơn; lưu giao dịch SePay thô trước khi xử lý; ghi audit log cho mọi thao tác nhân viên trên đơn và thanh toán.
- **Ask first:** thêm phương thức thanh toán; thêm mã giảm giá; đổi thời hạn giữ hàng; cho sửa đơn sau khi đặt.
- **Never:** tin số tiền từ frontend; xóa đơn hàng hoặc giao dịch; tự động hoàn tiền mà không có người xác nhận; để API key SePay ra frontend hoặc log.

## Success Criteria

- [ ] Khách vãng lai đặt được đơn COD và đơn SePay từ đầu đến cuối, nhận email xác nhận.
- [ ] Tiền SePay về (webhook) thì trang chờ thanh toán chuyển sang thành công trong vòng 5 giây.
- [ ] Gửi cùng một webhook SePay 3 lần thì đơn chỉ được ghi nhận thanh toán 1 lần.
- [ ] Đơn SePay quá 30 phút chưa trả bị hủy, tồn kho được trả lại.
- [ ] Thiếu tiền, thừa tiền, sai nội dung đều hiện trong màn hình đối soát và xử lý tay được.
- [ ] Đơn trên hạn mức COD không chọn được COD; tạm tính trên ngưỡng thì phí ship khách trả bằng 0.
- [ ] Sales tạo vận đơn GHN (sandbox) từ trang đơn; trạng thái giao hàng tự cập nhật vào đơn.
- [ ] Nhân viên `support` xem được đơn nhưng gọi API hủy hoặc tạo vận đơn nhận 403.
- [ ] Chỉ người đã nhận hàng mới đánh giá được; review bị ẩn biến mất khỏi trang sản phẩm và điểm trung bình được tính lại.
- [ ] Tắt API AI, toàn bộ E2E của module vẫn pass.
- [ ] Coverage `service.ts` ≥ 80%.

## Open Questions

1. Định dạng chính xác của webhook SePay (tên trường, cách gửi API key, response mong đợi) và URL ảnh QR: cần xác minh theo tài liệu SePay hiện tại trước khi code.
2. Ngưỡng miễn phí ship 500.000 đ và hạn mức COD 20.000.000 đ có hợp lý không?
3. Có cần chính sách đổi trả (khách gửi yêu cầu đổi trả trên web) trong MVP không? Mặc định: chưa, khách liên hệ CSKH.
4. Email thông báo đơn hàng dùng chung dịch vụ email với `identity` (câu hỏi mở bên đó).
