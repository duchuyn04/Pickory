# Spec: activity-tracking

> **Đã cập nhật ngày 2026-10-18 theo phạm vi mới.** Plan: `tasks/activity-tracking/plan.md`.

Module id: `activity-tracking` · Phụ thuộc: `identity`, `catalog`, `commerce` · Quy ước chung: `SPEC.md`

## Objective

Ghi lại hành vi mua sắm của người dùng (đã đồng ý) và đếm lượt xem, thêm giỏ, mua theo ngày. Đây là nguồn số đo phễu chuyển đổi trên dashboard của `admin-console`.

**User stories**
- Là khách, tôi biết hệ thống ghi gì về tôi, và xóa lịch sử khi muốn.
- Là khách không đồng ý, hành vi của tôi không bị ghi gắn với tôi.
- Là admin hoặc `sales` (`reports:read`), tôi biết bao nhiêu lượt xem sản phẩm dẫn tới thêm giỏ và mua.

## Thiết kế

### Loại sự kiện

| Sự kiện | Ghi từ | Dữ liệu chính |
|---|---|---|
| `product_view` | frontend | productId, skuId?, source |
| `category_view` | frontend | categoryId, filters đang áp dụng |
| `search` | frontend (khi khách tìm theo từ khóa bằng `GET /api/products?q=`) | câu tìm, số kết quả |
| `add_to_cart` | backend (nghe `onCartItemAdded` của `commerce`) | skuId, quantity, source |
| `remove_from_cart` | backend (nghe `onCartItemRemoved`) | skuId |
| `purchase` | backend (nghe `onOrderPaid`, hoặc `onOrderPlaced` với COD) | orderId, các productId, giá |

- `source` cho biết sự kiện đến từ trang nào, ví dụ `keyword-search`, `category`. Phễu MVP chưa tách theo `source`.
- Sự kiện từ backend đáng tin hơn frontend, vì ad-block không chặn được. Vì vậy các sự kiện liên quan tiền (thêm giỏ, mua) luôn ghi ở backend.

### Đồng ý và quyền riêng tư

| Trạng thái đồng ý | Xử lý |
|---|---|
| `personalization = true` | Ghi sự kiện đầy đủ, gắn với `anonymousId` và `userId` |
| `personalization = false` | Không ghi sự kiện cá nhân. Chỉ tăng bộ đếm tổng hợp theo ngày, không có định danh |

- Backend lưu câu tìm kiếm sau khi qua `PiiRedactor` (`apps/backend/src/Pickory.Shared/Privacy/PiiRedactor.cs`), vì khách có thể gõ SĐT hay email vào ô tìm.
- Rút lại đồng ý: xóa ngay sự kiện của `anonymousId` và `userId` đó.
- `UserDeletedEvent`: xóa sự kiện của người dùng.
- `UserLinkedEvent`: gắn sự kiện của `anonymousId` vào `userId`. Phía chưa đồng ý không có dữ liệu để gắn.
- Sự kiện thô giữ 12 tháng, job hằng ngày xóa phần cũ hơn. Bộ đếm tổng hợp giữ lâu dài.
- Nhân viên đăng nhập back-office không bị ghi sự kiện.

### Thu thập từ frontend

- `POST /api/events` nhận lô tối đa 50 sự kiện. Frontend gom sự kiện, gửi mỗi 5 giây hoặc khi rời trang (`navigator.sendBeacon`).
- Validate từng sự kiện. Sự kiện sai hoặc trỏ tới sản phẩm không tồn tại thì bỏ qua, không làm hỏng cả lô.
- Giới hạn 10 lô mỗi phút theo `anonymousId`. Bỏ qua request từ bot (user agent phổ biến của crawler).
- Ghi DB không chặn response.
- Backend tự lấy `anonymousId`, `userId`, trạng thái đồng ý từ `Actor`, không tin giá trị client gửi.

### Cổng cho module khác

- `IActivityMetrics.GetFunnelAsync(DateRange, CancellationToken)`: phễu xem sản phẩm, thêm giỏ, mua cho `admin-console`.
- `IServerEventTracker.TrackAsync`: module khác gọi để ghi sự kiện backend.

### Đo hiệu quả

- Phễu: xem sản phẩm, thêm giỏ, mua. Mỗi bước có số lượt và tỉ lệ so với bước trước.
- Phễu đọc bộ đếm tổng hợp, gồm cả khách không đồng ý.

## API

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| POST | `/api/events` | công khai | Nhận lô sự kiện từ frontend |
| DELETE | `/api/me/activity` | công khai | Xóa lịch sử của chính mình |
| GET | `/api/admin/analytics/funnel` | `reports:read` | Phễu mua hàng |

## Data Model (rút gọn)

Bản dưới đây viết theo Prisma của bản Node. Module viết bằng .NET và EF Core.

```prisma
model ActivityEvent {
  id          String   @id @default(uuid())
  type        String
  anonymousId String
  userId      String?
  productId   String?
  source      String?
  payload     Json
  occurredAt  DateTime
  receivedAt  DateTime @default(now())
  @@index([userId, occurredAt])
  @@index([anonymousId, occurredAt])
  @@index([type, occurredAt])
  @@map("activity_events")
}

model DailyCounter    { /* date, metric, source?, productId?, value; @@unique([date, metric, source, productId]) */ }
```

## Frontend

- Thư viện `features/activity-tracking/tracker.ts`: hàm `track(event)`, tự gom lô, tự bỏ qua khi chưa đồng ý, dùng `sendBeacon` khi rời trang. Các feature khác chỉ gọi `track`.
- Trang `/privacy`: câu giải thích hệ thống ghi gì, nút "Xóa lịch sử".

## Testing

- Unit: công thức phễu, gồm bước có 0 lượt.
- Integration:
  - Chưa đồng ý: `/api/events` không tạo dòng `activity_events`, chỉ tăng bộ đếm.
  - Rút đồng ý thì sự kiện bị xóa.
  - Lô có sự kiện sai: các sự kiện đúng vẫn được ghi.
  - Vượt giới hạn tần suất bị chặn.
  - `onOrderPaid` tạo sự kiện `purchase`.
- Hiệu năng: nhận 100 lô mỗi giây trong 1 phút trên máy dev mà p95 của `/api/events` vẫn dưới 100 ms.
- E2E: khách xem sản phẩm, thêm giỏ, đặt đơn COD. Phễu hôm nay tăng đúng 1 ở mỗi bước. Khách xóa lịch sử thì phễu không đổi.

## Boundaries (riêng module)

- **Always:** kiểm tra đồng ý ở backend trước khi ghi sự kiện cá nhân; lọc thông tin định danh khỏi câu tìm.
- **Ask first:** thêm loại sự kiện mới chứa dữ liệu nhạy cảm hơn; kéo dài thời gian lưu sự kiện; chia sẻ dữ liệu hành vi ra ngoài hệ thống.
- **Never:**
  - Ghi sự kiện cá nhân khi khách chưa đồng ý.
  - Dùng script theo dõi của bên thứ ba khi chưa có quyết định riêng, ví dụ Facebook Pixel hay Google Analytics.

## Success Criteria

- [ ] Khách chưa đồng ý: không có dòng nào trong `activity_events` gắn với họ sau khi duyệt web.
- [ ] Khách vãng lai đã đồng ý đăng nhập: sự kiện trước đó được gắn vào tài khoản.
- [ ] Xóa lịch sử hoặc rút đồng ý: sự kiện bị xóa ngay.
- [ ] Dashboard trả đúng phễu xem, thêm giỏ, mua với bộ dữ liệu test dựng sẵn.
- [ ] `/api/events` p95 dưới 100 ms ở 100 lô mỗi giây.
- [ ] Coverage dòng của module ít nhất 80%.

## Phần đã bỏ ngày 2026-10-18

Người dùng chỉ giữ phần đo chuyển đổi. Các phần sau bị bỏ:

| Phần | Lý do |
|---|---|
| Hồ sơ sở thích (`InterestProfile`, bảng `interest_profiles`, job tính hồ sơ 5 phút, điểm theo trọng số và bán rã) | Không còn tính năng nào đọc hồ sơ |
| `profileReader.getProfile` và `getProfileSummary` (bản tóm tắt cho prompt AI) | Chatbot `shopping-assistant` đã bỏ. Chatbot mới chạy ở dự án riêng `chatbot-rag` |
| `GET /api/me/recently-viewed`, `GET /api/me/interests`, khối "Đã xem gần đây", mục sở thích trong `/privacy` | Các phần này hiện dữ liệu của hồ sơ sở thích |
| Sự kiện `rec_impression`, `rec_click`, `chat_product_click` và hook `useImpression` | Không còn khối gợi ý hay chatbot để đo click |
| CTR theo `source`, quy chuyển đổi 7 ngày, `GET /api/admin/analytics/features` | Không còn tính năng AI để đo hiệu quả |
| Phễu so sánh nhóm có cá nhân hóa và không cá nhân hóa | Pickory không còn cá nhân hóa |
| Tiêu chí "hồ sơ có danh mục ở vị trí đầu trong 5 phút" và "`getProfileSummary` không chứa PII" | Phần được kiểm đã bỏ |

## Open Questions

1. Thời gian lưu sự kiện thô 12 tháng có phù hợp chính sách quyền riêng tư của shop không?
2. Có muốn dùng thêm Google Analytics hoặc Facebook Pixel cho marketing không? Nếu có thì cần thêm vào nhóm đồng ý `marketing` và cập nhật spec `identity`. Mặc định: không.
3. Còn cần `activity_events` theo từng khách không, khi phễu chỉ đọc bộ đếm? Xem Câu hỏi mở 12 của plan.
4. Banner vẫn gọi nhóm đồng ý là cá nhân hóa. Xem Câu hỏi mở 13 của plan.
