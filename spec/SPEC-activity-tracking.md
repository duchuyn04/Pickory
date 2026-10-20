# Spec: activity-tracking

> Cập nhật ngày 2026-10-20: module chỉ còn đếm tổng theo ngày. Plan: `tasks/activity-tracking/plan.md`.

Module id: `activity-tracking` · Phụ thuộc: `identity`, `commerce` · Quy ước chung: `SPEC.md`

## Tóm tắt dễ hiểu

Module đếm ba việc khách làm trên web, theo từng ngày:

1. Mở trang một sản phẩm (lượt xem).
2. Bấm "Thêm vào giỏ" (lượt thêm giỏ).
3. Đặt đơn thành công (lượt đặt đơn).

Ba con số này tạo thành phễu. Admin xem phễu trên trang báo cáo doanh thu, ví dụ:

```
Hôm nay:  Xem sản phẩm 100  ->  Thêm giỏ 20 (20%)  ->  Đặt đơn 5 (25%)
```

Module chỉ cộng số. Module không lưu ai đã xem gì. Vì vậy module không cần xin phép khách, không cần nút xóa lịch sử và không cần job xóa dữ liệu cũ.

## Objective

Cho admin biết bao nhiêu lượt xem sản phẩm dẫn tới thêm giỏ, và bao nhiêu lượt thêm giỏ dẫn tới đặt đơn.

User story: Là admin hoặc `sales` (`reports:read`), tôi xem phễu theo khoảng ngày để biết khách bỏ đi ở bước nào.

## Thiết kế

### Ba chỉ số

| Chỉ số | Nguồn | Khi nào cộng 1 |
|---|---|---|
| `product_view` | frontend gọi `POST /api/events` | Khách mở trang `/p/:slug` |
| `add_to_cart` | backend nghe `CartItemAddedEvent` của `commerce` | Mỗi lần khách thêm một SKU vào giỏ |
| `order_placed` | backend nghe `OrderPlacedEvent` của `commerce` | Mỗi đơn mới, với mọi cách thanh toán |

- Chỉ số đếm lượt, không đếm số khách khác nhau. Một khách xem một sản phẩm 3 lần tạo 3 lượt xem.
- `add_to_cart` đếm số lần thêm, không đếm số lượng sản phẩm. Thêm 2 chiếc cùng lúc vẫn là 1 lượt.
- Bước cuối là đặt đơn, không phải thanh toán. Báo cáo doanh thu của `commerce` đã có số đơn đã thanh toán.
- Module không dùng `OrderPaidEvent`. Admin xác nhận đã thu tiền COD cũng phát sự kiện này, nên một đơn COD sẽ bị đếm hai lần.
- Ngày của bộ đếm theo giờ `Asia/Ho_Chi_Minh`.

### Quyền riêng tư

- Bộ đếm chỉ có ngày, tên chỉ số và giá trị. Bộ đếm không chứa `anonymousId`, `userId` hay sản phẩm đã xem.
- Bộ đếm không gắn với một người, nên module đếm cả khách đồng ý và khách không đồng ý.
- Trang `/privacy` không đổi. Banner đồng ý không đổi.
- Lượt xem của nhân viên back-office không được cộng.

### Nhận lượt xem từ frontend

- `POST /api/events` nhận một lượt xem mỗi request, với body `{ "type": "product_view" }`.
- `type` khác `product_view` hoặc body sai thì API trả 400 `VALIDATION_ERROR`.
- Body không có `productId`, vì bộ đếm không lưu sản phẩm nào được xem.
- Mỗi `anonymousId` gửi tối đa 60 lượt xem mỗi phút. Vượt giới hạn thì API trả 429 `TOO_MANY_REQUESTS`.
- Backend bỏ qua request của crawler phổ biến, nhận diện theo user agent.
- Backend lấy `anonymousId` và loại người dùng từ `Actor`. Backend không tin giá trị client gửi.
- Backend cộng bộ đếm ngay trong request bằng một câu lệnh SQL, rồi trả 204.

### Cổng cho module khác

- `IActivityMetrics.GetFunnelAsync(DateRange, CancellationToken)` trả phễu cho `admin-console`.

### Phễu

- Phễu có ba bước: xem sản phẩm, thêm giỏ, đặt đơn.
- Mỗi bước có số lượt trong khoảng ngày và tỉ lệ so với bước trước.
- Bước trước có 0 lượt thì tỉ lệ là `null`.

## API

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| POST | `/api/events` | công khai | Nhận một lượt xem sản phẩm |
| GET | `/api/admin/analytics/funnel?from=&to=` | `reports:read` | Phễu trong khoảng ngày |

## Data Model

```prisma
model DailyCounter {
  date   DateTime @db.Date
  metric String   // product_view, add_to_cart, order_placed
  value  Int
  @@id([date, metric])
  @@map("daily_counters")
}
```

Bản trên viết theo cú pháp Prisma để dễ đọc. Module viết bằng .NET và EF Core.

## Frontend

- `features/activity-tracking/track-product-view.ts`: hàm gửi một lượt xem bằng `fetch` với `keepalive`.
- Trang sản phẩm gọi hàm này một lần sau khi tải xong sản phẩm.
- Lỗi mạng hoặc lỗi API không hiện cho khách và không chặn trang.

## Testing

- Unit: công thức phễu, gồm bước có 0 lượt.
- Integration:
  - `POST /api/events` cộng 1 vào `product_view` của hôm nay.
  - Nhân viên và crawler gửi lượt xem: bộ đếm không đổi.
  - Vượt giới hạn tần suất: API trả 429.
  - `CartItemAddedEvent` cộng 1 vào `add_to_cart`.
  - `OrderPlacedEvent` cộng 1 vào `order_placed`.
  - API phễu trả đúng số với bộ dữ liệu dựng sẵn.
- E2E: khách xem sản phẩm, thêm giỏ, đặt đơn COD. Phễu hôm nay tăng đúng 1 ở mỗi bước.

## Boundaries (riêng module)

- **Always:** giữ bộ đếm không có định danh.
- **Ask first:**
  - Lưu dữ liệu theo từng khách, ví dụ `anonymousId` hay lịch sử xem.
  - Thêm chỉ số mới.
  - Chia sẻ số liệu ra ngoài hệ thống.
- **Never:** dùng script theo dõi của bên thứ ba khi chưa có quyết định riêng, ví dụ Facebook Pixel hay Google Analytics.

## Success Criteria

- [ ] Khách xem sản phẩm, thêm giỏ, đặt đơn: phễu hôm nay tăng đúng 1 ở mỗi bước.
- [ ] Khách chưa đồng ý làm cùng luồng: phễu cũng tăng đúng 1 ở mỗi bước.
- [ ] DB không có bảng hay cột nào của module chứa `anonymousId` hoặc `userId`.
- [ ] API phễu trả đúng số và tỉ lệ với bộ dữ liệu dựng sẵn.
- [ ] Coverage dòng của module ít nhất 80%.

## Lịch sử phạm vi

| Ngày | Phần bỏ | Lý do |
|---|---|---|
| 2026-10-14 | Trường `variant`, các `source` của `recommendations` và `smart-search` | Hai module đó đã bỏ |
| 2026-10-18 | Hồ sơ sở thích, "Đã xem gần đây", CTR, sự kiện `rec_impression` và `rec_click` | Không còn tính năng AI nào đọc các dữ liệu này |
| 2026-10-20 | Bảng `activity_events` theo từng khách | Phễu chỉ đọc bộ đếm. Không còn tính năng nào đọc bảng này |
| 2026-10-20 | Kiểm tra đồng ý, `DELETE /api/me/activity`, gắn sự kiện khi đăng nhập, job xóa sau 12 tháng | Các phần này chỉ phục vụ bảng `activity_events` |
| 2026-10-20 | Sự kiện `category_view`, `search`, `remove_from_cart` | Phễu không dùng ba sự kiện này |
| 2026-10-20 | Gom lô, hàng đợi nền, lệnh đo hiệu năng `activity:benchmark` | Mỗi request chỉ chạy một câu lệnh SQL. Web demo chưa có vấn đề hiệu năng đo được |

## Open Questions

1. Có muốn dùng Google Analytics hoặc Facebook Pixel cho marketing không? Nếu có, nhóm đồng ý `marketing` và spec `identity` cần cập nhật. Mặc định: không.
