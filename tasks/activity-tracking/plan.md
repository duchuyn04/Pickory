# Implementation Plan: activity-tracking

## Tóm tắt dễ hiểu

Module đếm ba chỉ số theo ngày: lượt xem sản phẩm, lượt thêm giỏ, lượt đặt đơn. Admin xem ba số này dưới dạng phễu trên trang báo cáo doanh thu (ADM-UI9 của `tasks/admin-console/todo.md`).

Module chỉ cộng số, không lưu ai đã làm gì. Vì vậy module không cần kiểm tra đồng ý, không cần xóa dữ liệu và không đổi Contracts của `identity`.

Việc cần làm gồm 6 task, rồi E2E và nghiệm thu:

1. ACT-02: hàm tính phễu.
2. ACT-04: khung module và bảng `daily_counters`.
3. ACT-06: API nhận lượt xem.
4. ACT-UI1: trang sản phẩm gửi lượt xem.
5. ACT-07: nghe sự kiện thêm giỏ và đặt đơn của `commerce`.
6. ACT-11: API phễu cho admin.

## Thay đổi phạm vi ngày 2026-10-20

Người dùng chọn bản rút gọn: chỉ giữ bộ đếm tổng hợp cho phễu.

Module bỏ các phần sau:
- bảng `activity_events` lưu sự kiện theo từng khách;
- kiểm tra đồng ý, `ConsentChangedEvent`, `IConsentReader`;
- `DELETE /api/me/activity`, nút "Xóa lịch sử" ở `/privacy`, gắn sự kiện khi đăng nhập;
- job xóa sự kiện sau 12 tháng;
- sự kiện `category_view`, `search`, `remove_from_cart`;
- gom lô, hàng đợi `Channel`, `IServerEventTracker`, lệnh `activity:benchmark`.

Task bỏ thêm: ACT-05, ACT-08, ACT-UI3, ACT-12. Task thu hẹp: ACT-02, ACT-04, ACT-06, ACT-UI1, ACT-07, ACT-11, ACT-E2E1, ACT-13.

## Overview

`activity-tracking` phụ thuộc `identity` và `commerce`. Module đọc `Actor` của `identity` để bỏ qua nhân viên. Module nghe sự kiện giỏ và đơn của `commerce`. `admin-console` đọc phễu của module này.

Module không phụ thuộc `catalog` nữa. Bộ đếm không lưu sản phẩm, nên module không cần kiểm tra sản phẩm có tồn tại.

Spec: `spec/SPEC-activity-tracking.md`. Danh sách task: `tasks/activity-tracking/todo.md`. Cấu trúc .NET theo `tasks/dotnet-migration/plan.md`.

## Điều kiện trước

Mọi điều kiện đã có trong code ngày 2026-10-20:

1. `CartItemAddedEvent` và `OrderPlacedEvent` có trong `Pickory.Modules.Commerce.Contracts/Events/`. Hai sự kiện phát sau khi transaction commit.
2. `Actor` và `ActorKind` có trong `Pickory.Modules.Identity.Contracts/Actors/`.
3. `RateLimiter` có trong `Pickory.Shared/Throttling/`.
4. `VietnamCalendar` có trong `Pickory.Shared/Calendar/`.
5. `InProcessEventDispatcher` bắt lỗi của từng handler và ghi log. Handler lỗi không làm hỏng luồng của `commerce`.

## Đồ thị phụ thuộc

```
ACT-02 hàm tính phễu
ACT-04 khung module, Contracts, bảng daily_counters
 ├── ACT-06 POST /api/events ─── ACT-UI1 trang sản phẩm gửi lượt xem
 ├── ACT-07 nghe CartItemAddedEvent và OrderPlacedEvent
 └── ACT-11 IActivityMetrics và API phễu (cần ACT-02)
          └── ACT-E2E1 → ACT-13 nghiệm thu
```

ACT-06, ACT-07 và ACT-11 chỉ cần ACT-04, nên ba task chạy song song được.

## Architecture Decisions

1. **Hai project mới.** `Pickory.Modules.ActivityTracking` và `Pickory.Modules.ActivityTracking.Contracts`. `ActivityTrackingModule.cs` có `AddActivityTrackingModule` và `MapActivityTrackingEndpoints`. `Pickory.ArchitectureTests` thêm luật cho module mới.
2. **Contracts nhỏ.** Contracts chỉ có `IActivityMetrics`, `Funnel`, `FunnelStep` và `DateRange`. Mọi hàm trả `Task` và nhận `CancellationToken`. Contracts không tham chiếu project Contracts khác.
3. **Một bảng `daily_counters`.** Bảng có ba cột `date`, `metric`, `value`. Khóa chính là `(date, metric)`. Bảng không có cột định danh khách.
4. **Ba tên chỉ số cố định trong code.** `product_view`, `add_to_cart`, `order_placed`. Một lớp hằng số giữ ba tên này, để API, handler và phễu dùng cùng một giá trị.
5. **Cộng bộ đếm bằng một câu lệnh.** `INSERT ... ON CONFLICT (date, metric) DO UPDATE SET value = daily_counters.value + 1`. Hai request cùng lúc không làm mất lượt, vì Postgres khóa dòng khi cập nhật.
6. **`POST /api/events` ghi ngay rồi trả 204.** Một câu lệnh SQL đủ nhanh cho web demo. Plan không dùng hàng đợi nền, nên API dừng đột ngột không mất lượt nào.
7. **Body chỉ có `type`.** `type` phải là `product_view`. Body không có `productId`, vì bộ đếm không lưu sản phẩm.
8. **Bỏ qua nhân viên và crawler.** `Actor.Kind` là `Staff` thì API trả 204 và không cộng. User agent khớp danh sách crawler trong code thì API cũng trả 204 và không cộng.
9. **Giới hạn 60 lượt xem mỗi phút theo `AnonymousId`.** API dùng `RateLimiter` của `Pickory.Shared`. Vượt giới hạn thì API trả 429 `TOO_MANY_REQUESTS`. Một khách mở 60 trang sản phẩm trong 1 phút là trường hợp hiếm.
10. **Thêm giỏ đếm theo `CartItemAddedEvent`.** Mỗi sự kiện cộng 1, không cộng theo `Quantity`. `commerce` phát sự kiện cả khi cộng dồn số lượng vào dòng có sẵn.
11. **Đặt đơn đếm theo `OrderPlacedEvent`.** Mỗi đơn mới cộng 1, với COD và SePay. ADM-UI9 gọi bước cuối là "đặt đơn".
12. **Không dùng `OrderPaidEvent`.** `AdminOrderActionService` phát `OrderPaidEvent` khi admin xác nhận đã thu tiền COD. Đếm sự kiện này sẽ tính một đơn COD hai lần.
13. **Ngày theo giờ Việt Nam.** Ngày của bộ đếm lấy bằng `VietnamCalendar` từ `TimeProvider`. Test cố định thời gian ở biên 23:59 và 00:00.
14. **Không đo hiệu năng riêng.** Web demo chưa có vấn đề hiệu năng đo được. Plan không có lệnh `activity:benchmark`.

## Task List

Chi tiết từng task ở `tasks/activity-tracking/todo.md`.

### Phase 1: Nền tảng
- [ ] ACT-02 · [ ] ACT-04

### Checkpoint A1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `ActivityTracking` tham chiếu module khác ngoài Contracts
- [ ] Review với người dùng

### Phase 2: Đếm
- [ ] ACT-06 · [ ] ACT-UI1 · [ ] ACT-07

### Checkpoint A2
- [ ] Xem sản phẩm, thêm giỏ, đặt đơn: mỗi bộ đếm của hôm nay tăng đúng 1
- [ ] Nhân viên và crawler không làm bộ đếm tăng
- [ ] Review với người dùng

### Phase 3: Phễu
- [ ] ACT-11

### Checkpoint A3
- [ ] API phễu trả đúng số và tỉ lệ với bộ dữ liệu dựng sẵn
- [ ] Review với người dùng

### Phase 4: E2E và nghiệm thu
- [ ] ACT-E2E1 · [ ] ACT-13

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt
- [ ] Coverage dòng của `Pickory.Modules.ActivityTracking` ít nhất 80%, không tính `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `support-desk`

## Traceability: Success Criteria → Task

| Success Criteria (spec) | Task | Cách kiểm |
| --- | --- | --- |
| Xem sản phẩm, thêm giỏ, đặt đơn: phễu hôm nay tăng đúng 1 ở mỗi bước | ACT-06, ACT-UI1, ACT-07, ACT-E2E1 | Test tích hợp từng bộ đếm; E2E |
| Khách chưa đồng ý: phễu cũng tăng đúng 1 ở mỗi bước | ACT-E2E1 | E2E chạy luồng với khách chưa đồng ý |
| DB không có bảng hay cột nào của module chứa `anonymousId` hoặc `userId` | ACT-04, ACT-13 | Test tích hợp đọc schema của `ActivityTrackingDbContext` |
| API phễu trả đúng số và tỉ lệ với bộ dữ liệu dựng sẵn | ACT-02, ACT-11 | Unit test công thức; test tích hợp API phễu |
| Coverage ít nhất 80% | ACT-13 | Coverage gộp unit và tích hợp, cách đo như shipping |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| Một người gọi `POST /api/events` liên tục để tăng lượt xem | Phễu sai | Giới hạn 60 lượt mỗi phút theo `AnonymousId`; bỏ qua crawler. Web demo chấp nhận mức chặn này |
| Trình chặn quảng cáo chặn `POST /api/events` | Lượt xem thấp hơn thực tế | Thêm giỏ và đặt đơn đếm ở backend, nên hai bước này không bị ảnh hưởng |
| `RateLimiter` giữ bộ đếm trong bộ nhớ | Giới hạn chỉ đúng khi API chạy một instance | MVP chạy một instance trên VPS |

## Câu hỏi mở

1. **Google Analytics hoặc Facebook Pixel.** Mặc định: không dùng. Nếu dùng, nhóm đồng ý `marketing` và spec `identity` cần cập nhật.

## Lịch sử phạm vi

- 2026-10-14: bỏ trường `variant` và các `source` của `recommendations`, `smart-search`. Hai module đó đã bỏ.
- 2026-10-18: bỏ hồ sơ sở thích, "Đã xem gần đây", CTR, quy chuyển đổi 7 ngày, `rec_impression`, `rec_click`. Task bỏ: ACT-01, ACT-03, ACT-09, ACT-10, ACT-UI2.
- 2026-10-20: bỏ bảng `activity_events` và mọi phần chỉ phục vụ bảng đó. Task bỏ: ACT-05, ACT-08, ACT-UI3, ACT-12.

Câu hỏi mở của bản trước không còn áp dụng sau ngày 2026-10-20:
- `ConsentChangedEvent` và `IConsentReader`: module không kiểm tra đồng ý.
- Mã trả về khi vượt giới hạn: plan chọn 429.
- Xóa sự kiện khi liên kết tài khoản: module không lưu sự kiện theo khách.
- Thời gian lưu 12 tháng: bộ đếm không có định danh, nên module giữ bộ đếm lâu dài.
