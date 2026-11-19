# Hợp đồng API: fulfillment và đánh giá sau giao

Spec: `spec/SPEC-commerce.md`. Fixtures: `spec/fixtures/commerce/`.

File này ghi hình dạng dữ liệu của route fulfillment mới và route đọc điều kiện đánh giá.
Backend và frontend cùng làm theo file này.
Khi đổi hình dạng dữ liệu, sửa file này và fixtures trước khi sửa code.

## Quy ước chung

1. Response thành công có dạng `{ "data": ... }`.
2. Response lỗi có dạng `{ "error": { "code", "message", "details"? } }`.
3. `details` chỉ có trong lỗi `VALIDATION_ERROR`.
4. Mỗi phần tử của `details` có `key` và `message`.
5. Timestamp dùng chuỗi ISO 8601 giờ UTC.
6. Giá trị enum dùng chữ thường và dấu gạch dưới.
7. Trường nullable mới phải luôn có mặt với giá trị `null`.
8. Các bản chụp liên hệ và địa chỉ giữ quy tắc khóa tùy chọn hiện có.
9. Route fulfillment trả `Cache-Control: no-store`.

## Bảng route

| Method | Path | Quyền | Fixture | Có từ |
|---|---|---|---|---|
| PUT | `/api/admin/orders/{code}/fulfillment` | `orders:fulfill` | `manual-fulfillment.json` | COM-29 |
| GET | `/api/products/{id}/reviews/mine` | khách đã đăng nhập | `review-eligibility.json` | đã có |

Route PUT là route mới duy nhất. Route GET giữ nguyên hình dạng response.
Không có realtime event mới. `OrderStatusChangedEvent` giữ nguyên hình dạng.

## Cập nhật fulfillment thủ công

`PUT /api/admin/orders/{code}/fulfillment`

Route dùng cho nhân viên đã đối chiếu giao hàng bên ngoài Pickory.
Route không gọi GHN và không thay đổi vận đơn GHN.

### Request

Body là một object JSON với đúng hai trường:

| Trường | Kiểu | Bắt buộc | Quy tắc |
|---|---|---|---|
| `toStatus` | chuỗi | có | `confirmed`, `shipping`, `delivered` hoặc `returned` |
| `reason` | chuỗi | có | Sau khi cắt khoảng trắng dài 1 đến 500 ký tự |

Trường lạ, thiếu trường hoặc sai kiểu trả 400 `VALIDATION_ERROR`.
Backend kiểm tra request trước khi khóa đơn.

### Chuyển đổi được nhận

| Trạng thái hiện tại | `toStatus` | Điều kiện | Cờ thay đổi |
|---|---|---|---|
| `confirmed` | `shipping` | Không có vận đơn GHN còn hiệu lực | Thêm `manual_shipping` |
| `shipping` | `confirmed` | Có `manual_shipping` và không có vận đơn GHN còn hiệu lực | Bỏ `manual_shipping` |
| `shipping` | `delivered` | Nhân viên đã xác minh khách nhận hàng | Thêm `manual_delivery` |
| `shipping` | `returned` | Nhân viên đã xác minh hàng đã hoàn | Thêm `manual_return` |

Khi chuyển sang `returned`, trạng thái `paid` chuyển `refund_pending`.
Các trạng thái thanh toán khác giữ nguyên.
Mọi chuyển đổi ngoài bảng trả 409 `INVALID_ORDER_TRANSITION`.

### Response 200

Response trả chi tiết đơn sau thay đổi, cùng dạng `GET /api/admin/orders/{code}`.
Route không thêm trường response mới. Chỉ `flags` nhận thêm ba giá trị đã mô tả.

| Trường | Kiểu | Có thể `null` |
|---|---|---|
| `code` | chuỗi | không |
| `status` | chuỗi enum `OrderStatus` | không |
| `paymentStatus` | chuỗi enum `PaymentStatus` | không |
| `paymentMethod` | `cod` hoặc `sepay` | không |
| `createdAt` | timestamp | không |
| `paymentDeadline` | timestamp | có |
| `flags` | mảng chuỗi | không |
| `contact` | object bản chụp liên hệ | không |
| `shippingAddress` | object bản chụp địa chỉ | không |
| `invoice` | object bản chụp hóa đơn | có |
| `items` | mảng `AdminOrderItem` | không |
| `subtotal` | số nguyên VND | không |
| `shippingFee` | số nguyên VND | không |
| `shippingDiscount` | số nguyên VND | không |
| `total` | số nguyên VND | không |
| `shippingFeeSource` | `ghn`, `cached` hoặc `fallback` | không |
| `statusHistory` | mảng `AdminOrderHistoryEntry` | không |
| `shipment` | `AdminOrderShipment` | có |
| `suggestedParcel` | `AdminOrderSuggestedParcel` | có |
| `refundTransactionRef` | chuỗi | có |

`AdminOrderItem` có các trường sau:

| Trường | Kiểu | Có thể `null` |
|---|---|---|
| `skuId` | chuỗi | không |
| `name` | chuỗi | không |
| `variantName` | chuỗi | có |
| `quantity` | số nguyên | không |
| `unitPrice` | số nguyên VND | không |
| `lineTotal` | số nguyên VND | không |

`AdminOrderHistoryEntry` có các trường sau:

| Trường | Kiểu | Có thể `null` |
|---|---|---|
| `fromStatus` | chuỗi enum `OrderStatus` | không |
| `toStatus` | chuỗi enum `OrderStatus` | không |
| `actorId` | chuỗi | có |
| `actorName` | chuỗi | có |
| `reason` | chuỗi | có |
| `createdAt` | timestamp | không |

`AdminOrderShipment` có các trường sau. Các trường này không nhận `null`.

| Trường | Kiểu |
|---|---|
| `id` | chuỗi |
| `trackingCode` | chuỗi |
| `status` | chuỗi |
| `fee` | số nguyên VND |

`AdminOrderSuggestedParcel` có các trường sau. Các trường này không nhận `null`.

| Trường | Kiểu |
|---|---|
| `weightGrams` | số nguyên |
| `lengthCm` | số nguyên |
| `widthCm` | số nguyên |
| `heightCm` | số nguyên |

### Gửi lại request

Nếu `toStatus` bằng trạng thái hiện tại, route trả 200 với chi tiết đơn hiện tại.
Trường hợp này không thay đổi cờ, thanh toán hoặc tồn kho.
Route cũng không thêm lịch sử, audit hoặc sự kiện.
Backend vẫn kiểm tra request trước khi áp quy tắc này.

Hai request cùng lúc phải đi qua khóa dòng đơn.
Request sau đọc trạng thái mới nhất trước khi quyết định.

### Lỗi

| Status | `code` | Điều kiện |
|---|---|---|
| 400 | `VALIDATION_ERROR` | Body sai, có trường lạ, `toStatus` không hợp lệ hoặc `reason` sai giới hạn |
| 401 | `UNAUTHORIZED` | Khách chưa đăng nhập |
| 403 | `FORBIDDEN` | Tài khoản khách hoặc nhân viên thiếu `orders:fulfill` |
| 404 | `ORDER_NOT_FOUND` | Không có đơn với `code` |
| 409 | `INVALID_ORDER_TRANSITION` | Chuyển đổi không có trong bảng hoặc không thỏa điều kiện vận đơn |

Fixture `manual-fulfillment.json` chứa các ví dụ thành công và lỗi chính của route.

## Đọc điều kiện đánh giá

`GET /api/products/{id}/reviews/mine`

Route giữ nguyên hình dạng sau:

| Trường | Kiểu | Có thể `null` |
|---|---|---|
| `eligible` | boolean | không |
| `reason` | chuỗi | có |
| `review` | object đánh giá của khách | có |

Khi đơn chứa sản phẩm còn `shipping`, `eligible=false` và `reason=NOT_PURCHASED`.
Khi đơn chuyển `delivered`, `eligible=true`, `reason=null` và `review=null` nếu khách chưa đánh giá.
Thanh toán COD `unpaid` không thay đổi kết quả này.

`POST /api/products/{id}/reviews` giữ nguyên hợp đồng hiện có.
Route vẫn trả 403 `REVIEW_NOT_ALLOWED` khi khách chưa có đơn `delivered` chứa sản phẩm.

Fixtures dùng các ví dụ `order shipping` và `order delivered`.

## Giới hạn đã duyệt

| Giới hạn | Giá trị |
|---|---|
| Độ dài `reason` sau khi cắt khoảng trắng | 1 đến 500 ký tự |
| Các giá trị `toStatus` được nhận | `confirmed`, `shipping`, `delivered`, `returned` |

Chủ shop duyệt hai giới hạn và quyền xác nhận `delivered` thủ công ngày 2026-11-19.

## Lịch sử thay đổi

- 2026-11-19: Tạo bản nháp cho route fulfillment và điều kiện đánh giá sau giao.
- 2026-11-19: Chủ shop duyệt spec, quyền xác nhận `delivered` thủ công và giới hạn request.
