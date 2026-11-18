# API contract: exports-printing

Spec: `spec/SPEC-exports-printing.md`. Fixtures: `spec/fixtures/exports-printing/`.

File này ghi hình dạng dữ liệu của mọi route mà đợt việc thêm hoặc đổi. Backend và frontend cùng làm theo file này. Đổi hình dạng dữ liệu thì sửa file này và fixtures trong cùng một commit trước, rồi mới sửa code.

## Quy ước chung

1. Route JSON trả `{ "data": ... }` khi thành công.
2. Mọi route trả lỗi dạng `{ "error": { "code", "message", "details"? } }`. `details` chỉ có ở lỗi `VALIDATION_ERROR`.
3. Trường cho phép rỗng luôn có mặt với giá trị `null`. Backend không bỏ khóa.
4. Thời điểm ghi theo ISO 8601, giờ UTC, ví dụ `2026-11-10T08:30:00Z`. Frontend đổi sang giờ Việt Nam khi hiện.
5. Tiền là số nguyên VND.
6. Route xuất trả file nhị phân khi thành công, không bọc trong `{ data }`.

## Route xuất file

Bốn route xuất có cùng cách trả kết quả.

Thành công:

| Mục | Giá trị |
|---|---|
| Status | 200 |
| `Content-Type` | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` |
| `Content-Disposition` | `attachment; filename="<tên-file>"; filename*=UTF-8''<tên-file>` |
| Body | file `.xlsx` |

Lỗi chung của bốn route:

| Status | `code` | Khi nào |
|---|---|---|
| 400 | `VALIDATION_ERROR` | Tham số lọc sai. `details` giống route danh sách |
| 403 | `FORBIDDEN` | Chưa đăng nhập, thiếu quyền đọc danh sách hoặc thiếu `reports:export` |
| 422 | `EXPORT_TOO_LARGE` | Hơn 50.000 dòng khớp bộ lọc. `message`: "Danh sách có hơn 50.000 dòng. Hãy thu hẹp bộ lọc rồi xuất lại." |

Người chưa đăng nhập nhận 403 `FORBIDDEN`, giống các route admin hiện có.

Route xuất bỏ qua `page` và `limit` nếu request có hai tham số này. Hai tham số này không gây lỗi 400.

### `GET /api/admin/exports/orders`

| Mục | Giá trị |
|---|---|
| Module | `commerce` |
| Quyền | `orders:read` và `reports:export` |
| Tham số | giống `GET /api/admin/orders`: `status`, `paymentStatus`, `paymentMethod`, `flag`, `from`, `to`, `search` |
| Tên file | `don-hang-yyyyMMdd-HHmm.xlsx` |
| Sheet | "Đơn hàng", "Chi tiết sản phẩm" |
| Nhật ký thao tác | `commerce.orders.export` |
| Fixture | `exports.json`, các ví dụ tên `orders ...` |

### `GET /api/admin/exports/products`

| Mục | Giá trị |
|---|---|
| Module | `catalog` |
| Quyền | `products:read` và `reports:export` |
| Tham số | giống `GET /api/admin/products`: `search`, `categoryId`, `status`, `lowStock` |
| Tên file | `san-pham-yyyyMMdd-HHmm.xlsx` |
| Sheet | "Sản phẩm" |
| Nhật ký thao tác | không ghi |
| Fixture | `exports.json`, các ví dụ tên `products ...` |

### `GET /api/admin/exports/customers`

| Mục | Giá trị |
|---|---|
| Module | `identity` |
| Quyền | `customers:read` và `reports:export` |
| Tham số | giống `GET /api/admin/customers`: `search`, `status`, `emailVerified` |
| Tên file | `khach-hang-yyyyMMdd-HHmm.xlsx` |
| Sheet | "Khách hàng" |
| Nhật ký thao tác | `identity.customers.export` |
| Fixture | `exports.json`, các ví dụ tên `customers ...` |

### `GET /api/admin/exports/payment-transactions`

| Mục | Giá trị |
|---|---|
| Module | `commerce` |
| Quyền | `payments:read` và `reports:export` |
| Tham số | giống `GET /api/admin/payments/transactions`: `matchStatus`, `orderFlag` |
| Tên file | `giao-dich-sepay-yyyyMMdd-HHmm.xlsx` |
| Sheet | "Giao dịch SePay" |
| Nhật ký thao tác | `commerce.payment-transactions.export` |
| Fixture | `exports.json`, các ví dụ tên `payment transactions ...` |

Tham số của từng route lấy theo route danh sách tại thời điểm làm task. Danh sách thêm bộ lọc thì route xuất nhận bộ lọc đó, vì hai route dùng chung lớp đọc tham số.

### Dòng nhật ký thao tác của route xuất

| Trường | Giá trị |
|---|---|
| `action` | tên hành động trong bảng trên |
| `actorId` | id nhân viên xuất file |
| `details.filters` | object chứa các tham số lọc có giá trị, ví dụ `{ "status": "confirmed", "from": "2026-10-01" }` |
| `details.rowCount` | số dòng dữ liệu của sheet chính |

## Route dữ liệu in hóa đơn

### `GET /api/admin/order-invoices?codes=`

| Mục | Giá trị |
|---|---|
| Module | `commerce` |
| Quyền | `orders:read` |
| Tham số | `codes`: mã đơn cách nhau bằng dấu phẩy |
| Fixture | `order-invoices.json` |

Quy tắc của `codes`:

1. Có từ 1 tới 50 mã sau khi bỏ mã trùng.
2. Mỗi mã đúng định dạng mã đơn: `PK` và 8 ký tự trong bảng chữ của `OrderCodeGenerator`.
3. Thứ tự hóa đơn trong response giống thứ tự mã trong `codes`.

Response 200:

```json
{
  "data": {
    "shop": {
      "name": "string | null",
      "address": "string | null",
      "phone": "string | null",
      "taxCode": "string | null"
    },
    "invoices": [
      {
        "code": "string",
        "createdAt": "string (ISO 8601)",
        "status": "string (giá trị enum OrderStatus)",
        "paymentMethod": "string (giá trị enum PaymentMethod)",
        "paymentStatus": "string (giá trị enum PaymentStatus)",
        "customer": {
          "name": "string",
          "phone": "string",
          "email": "string"
        },
        "shippingAddressText": "string",
        "vatInvoice": {
          "companyName": "string",
          "taxCode": "string",
          "address": "string",
          "email": "string"
        },
        "lines": [
          {
            "skuCode": "string",
            "name": "string",
            "optionText": "string | null",
            "quantity": "number",
            "unitPrice": "number",
            "lineTotal": "number"
          }
        ],
        "subtotal": "number",
        "shippingFee": "number",
        "shippingDiscount": "number",
        "total": "number"
      }
    ]
  }
}
```

Ghi chú về trường:

- `shop` luôn có mặt. Trước EXP-07, bốn trường của `shop` là `null`. Hóa đơn in tên "Pickory" khi `shop.name` là `null`.
- `vatInvoice` là `null` khi khách không yêu cầu hóa đơn VAT.
- `optionText` ghép giá trị phân loại bằng `, `, ví dụ `Đen, 256 GB`. Sản phẩm không có phân loại thì `optionText` là `null`.
- `shippingAddressText` ghép địa chỉ thành một dòng: số nhà và đường, phường hoặc xã, quận hoặc huyện, tỉnh hoặc thành phố.
- Frontend hiện nhãn tiếng Việt của `status`, `paymentMethod`, `paymentStatus` bằng `order-labels.ts`.
- Response không có giờ in. Frontend lấy giờ hiện tại của trình duyệt khi in.

Lỗi:

| Status | `code` | Khi nào |
|---|---|---|
| 400 | `VALIDATION_ERROR` | `codes` rỗng, hơn 50 mã, hoặc có mã sai định dạng. `details[].key` là `codes` |
| 403 | `FORBIDDEN` | Chưa đăng nhập hoặc thiếu `orders:read` |
| 404 | `ORDER_NOT_FOUND` | Có mã không tồn tại. `details` liệt kê từng mã thiếu, `key` là `codes`, `message` chứa mã đó |

## Route cấu hình bán hàng (đổi)

Người dùng duyệt phần này ngày 2026-11-10 (Câu hỏi mở 1 của spec). EXP-07 làm phần backend.

### `GET /api/admin/commerce/settings`

Response giữ mọi trường hiện có và thêm khối `shop`:

```json
{
  "data": {
    "freeShippingThreshold": 500000,
    "codMaxAmount": 20000000,
    "maxQuantityPerSku": 5,
    "maxCartLines": 30,
    "sepayAccountNumber": null,
    "sepayBankCode": null,
    "updatedAt": null,
    "shop": {
      "name": null,
      "address": null,
      "phone": null,
      "taxCode": null
    }
  }
}
```

### `PUT /api/admin/commerce/settings`

Body nhận thêm khối `shop` với bốn trường như trên. Khối `shop` không bắt buộc. Body thiếu `shop` thì server giữ thông tin shop cũ.

Kiểm tra từng trường sau khi cắt khoảng trắng. Chuỗi rỗng lưu thành `null`.

| Trường | Giới hạn | `details[].key` khi sai |
|---|---|---|
| `shop.name` | tối đa 120 ký tự | `shop.name` |
| `shop.address` | tối đa 255 ký tự | `shop.address` |
| `shop.phone` | tối đa 20 ký tự, chỉ chữ số, dấu cách, `+`, `.`, `-` | `shop.phone` |
| `shop.taxCode` | đúng `TaxCodeRule` | `shop.taxCode` |

## Giới hạn

Người dùng duyệt các giới hạn này ngày 2026-11-10.

| Giới hạn | Giá trị |
|---|---|
| Số dòng tối đa mỗi file | 50.000 |
| Số đơn tối đa mỗi lần in | 50 |
| Độ dài `shop.name` | 120 ký tự |
| Độ dài `shop.address` | 255 ký tự |
| Độ dài `shop.phone` | 20 ký tự |

## Bảng theo dõi route

| Route | Task backend | Task frontend | Có từ |
|---|---|---|---|
| `GET /api/admin/exports/orders` | EXP-03 | EXP-UI3 | chưa có |
| `GET /api/admin/exports/products` | EXP-05 | EXP-UI3 | chưa có |
| `GET /api/admin/exports/customers` | EXP-06 | EXP-UI3 | chưa có |
| `GET /api/admin/exports/payment-transactions` | EXP-04 | EXP-UI3 | chưa có |
| `GET /api/admin/order-invoices` | EXP-08 | EXP-UI5, EXP-UI6 | chưa có |
| `GET`, `PUT /api/admin/commerce/settings` (khối `shop`) | EXP-07 | EXP-UI7 | chưa có |

Cột "Có từ" ghi mã task backend làm route chạy thật. Builder điền cột này khi task đạt.

## Nhật ký thay đổi

- 2026-11-10: Bản đầu.
- 2026-11-10: Người dùng duyệt spec và giới hạn. Điền mã task vào bảng theo dõi route.
