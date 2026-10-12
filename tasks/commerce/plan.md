# Implementation Plan: commerce

## Overview

Module `commerce` biến ý định mua thành đơn hàng đã giao. Module gồm giỏ hàng, checkout, đơn hàng, thanh toán COD và SePay, hoàn tiền, sổ địa chỉ, đánh giá sản phẩm. Đây là phần tạo ra doanh thu, nên module không phụ thuộc AI.

`commerce` phụ thuộc `identity`, `catalog` và `shipping`. Bốn module sau dùng dữ liệu của `commerce`: `activity-tracking`, `personalized-content`, `support-desk`, `admin-console`.

Spec gốc: `spec/SPEC.md`, `spec/SPEC-commerce.md`. Spec còn viết interface theo bản Node. Plan này chuyển interface sang .NET theo `tasks/dotnet-migration/plan.md`.
Danh sách task chi tiết: `tasks/commerce/todo.md`.

## Phần đã phủ bởi plan cũ

Các mục dưới đây đã có ở plan cũ. Plan này không lặp lại task của các mục đó.

| Mục | Plan cũ | Plan này làm gì |
| --- | --- | --- |
| Quyền `orders:*`, `payments:*`, `reviews:moderate`, `commerce-settings:manage`, `refunds:manage` và quyền mặc định của `sales`, `support` | `tasks/identity-ai-gateway/todo.md` (ID-01), `Permissions.cs`, `DefaultRoles.cs` | Chỉ dùng. Không thêm quyền mới |
| `RequirePermission`, `IAuditLogWriter`, actor middleware, kiểm `Origin` | `tasks/identity-ai-gateway/todo.md`, `tasks/dotnet-migration/todo.md` (DN-02, DN-04) | Chỉ dùng |
| `UserLinkedEvent`, `UserDeletedEvent` | `tasks/dotnet-migration/todo.md` (DN-03) | Chỉ đăng ký nghe |
| `ICatalogReader.GetSkusAsync`, `IStockService.ReserveAsync` và `ReleaseAsync`, idempotent theo `orderId` | `tasks/catalog/todo.md` (CAT-08, CAT-10) | Chỉ gọi |
| Chặn xóa cứng sản phẩm đã có đơn | Quyết định 8 của `tasks/catalog/plan.md` | Không thêm task. Mọi đơn đều giữ hàng qua `ReserveAsync`, nên movement `order_reserve` đã đủ làm dấu hiệu. COM-E2E1 kiểm lại bằng cách xóa sản phẩm đã đặt |
| `IShippingService`, `AddressPicker`, `FakeGhnClient` | `tasks/shipping/todo.md` (SHIP-03 đến SHIP-07, UI-S1) | Chỉ gọi và dùng lại |
| Route báo phí cho giỏ hàng | Quyết định 19 của `tasks/shipping/plan.md` | COM-07 làm route này trong `commerce` |
| Webhook GHN, job đồng bộ trạng thái, `RateLimiter` dùng chung | `tasks/shipping/todo.md` (SHIP-08) | Chờ SHIP-08, rồi nghe `ShipmentStatusChangedEvent` và dùng `RateLimiter` |
| Nút thêm giỏ ở trang sản phẩm đang tắt, chú thích "Sắp có" | Open Question 4 của `tasks/catalog/plan.md` | COM-UI1 bật nút này |
| E2E "nhân viên `sales` thấy menu đơn hàng" | Traceability của `tasks/identity-ai-gateway/plan.md`, mục hoãn | COM-E2E3 kiểm `sales` mở được trang đơn hàng thật. Phần menu đầy đủ theo quyền thuộc `admin-console` |

## Điều kiện trước

1. Plan đổi route cũ sang REST đã xong. Plan đó chạy sau Phase 2 của `shipping` và trước `commerce` (quyết định 20 của `tasks/shipping/plan.md`).
2. SHIP-07 đã qua review của người dùng.
3. SHIP-08 đã xong. COM-07 cần `RateLimiter` trong `Pickory.Shared`. COM-11 cần cách miễn kiểm `Origin` cho webhook. COM-16 cần webhook GHN để trạng thái tự cập nhật.
4. SHIP-09 không chặn task nào của plan này. Tiêu chí sandbox GHN của `commerce` (COM-24) mở cùng lúc với SHIP-09.
5. Người dùng đã trả lời Câu hỏi mở 1 đến 9 ngày 2026-10-11.

## Đồ thị phụ thuộc

```
COM-00 xác minh SePay (tài liệu trước, khóa sau; không chặn task khác)
COM-01 logic thuần: mã đơn, tiền, mã số thuế, tìm mã đơn trong nội dung chuyển khoản
COM-02 máy trạng thái đơn và trạng thái thanh toán
 └── COM-03 khung module, Contracts, schema, migration, cấu hình SEPAY_*
      ├── COM-04 giỏ hàng ─────────────────────────── COM-UI1 giỏ hàng, bật nút thêm giỏ
      │    └── COM-05 gộp giỏ, dọn dữ liệu khi xóa tài khoản
      ├── COM-06 cấu hình commerce ────────────────── COM-UI7 màn cấu hình
      │    └── COM-07 checkout preview (cần SHIP-08)
      │         └── COM-08 đặt đơn ────────────────── COM-UI2 checkout
      │              ├── COM-09 email xác nhận
      │              ├── COM-10 theo dõi đơn, khách tự hủy ── COM-UI3 đơn của tôi
      │              ├── COM-11 webhook SePay ──────── COM-UI4 trang chờ thanh toán
      │              │    ├── COM-12 job hết hạn thanh toán
      │              │    └── COM-13 đối soát SePay tay ── COM-UI6 màn đối soát
      │              ├── COM-14 danh sách và chi tiết đơn (admin) ── COM-UI5 màn đơn hàng
      │              │    ├── COM-15 xác nhận, hủy, đã xuất hóa đơn
      │              │    ├── COM-16 vận đơn GHN, nghe trạng thái giao hàng
      │              │    └── COM-17 nhận COD, đã hoàn tiền
      │              ├── COM-19 đánh giá ──────────── COM-UI9 khối đánh giá
      │              │    └── COM-20 kiểm duyệt ────── COM-UI10 hàng chờ kiểm duyệt
      │              ├── COM-21 gắn đơn vãng lai, ẩn danh khi xóa tài khoản
      │              ├── COM-22 ICommerceReader
      │              └── COM-23 ICommerceReports
      └── COM-18 sổ địa chỉ ─────────────────── COM-UI8 sổ địa chỉ
COM-E2E1 đến COM-E2E5 sau các task UI tương ứng
COM-24 nghiệm thu
```

COM-07 cần COM-06, vì ngưỡng miễn phí ship và hạn mức COD nằm trong cấu hình. COM-08 cần COM-07, vì đặt đơn so tổng tiền với preview. COM-12 cần COM-11, vì job hết hạn và tiền về muộn dùng chung luật khớp đơn.

## Architecture Decisions

1. **Cấu trúc theo `tasks/dotnet-migration/plan.md`.** Hai project mới: `Pickory.Modules.Commerce` và `Pickory.Modules.Commerce.Contracts`. `CommerceModule.cs` có `AddCommerceModule` và `MapCommerceEndpoints`. `Pickory.ArchitectureTests` thêm luật cho module mới. `CommerceDbContext` dùng schema lịch sử `ef_commerce`.
2. **Chuyển interface từ TypeScript sang .NET.** Spec viết `commerceReader`, `commerceReports` và các hàm `on...` theo bản Node. Plan chuyển như sau:
   - `commerceReader` thành `ICommerceReader` trong `Pickory.Modules.Commerce.Contracts/Reading/`. Mỗi hàm trả `Task<...>` và nhận `CancellationToken`.
   - `commerceReports` thành `ICommerceReports` trong `Pickory.Modules.Commerce.Contracts/Reports/`.
   - `onOrderPlaced`, `onOrderPaid`, `onOrderStatusChanged`, `onCartItemAdded`, `onCartItemRemoved` thành `OrderPlacedEvent`, `OrderPaidEvent`, `OrderStatusChangedEvent`, `CartItemAddedEvent`, `CartItemRemovedEvent`. Mỗi sự kiện là `record` cài `IDomainEvent`, đặt trong `Contracts/Events/`.
   - `commercePermissions` không cần. Quyền đã nằm trong `Permissions.cs` của identity.
   - `onUserLinked`, `onUserDeleted`, `onShipmentStatusChanged` thành `IDomainEventHandler<UserLinkedEvent>`, `IDomainEventHandler<UserDeletedEvent>`, `IDomainEventHandler<ShipmentStatusChangedEvent>`.
   - Zod thành FluentValidation. Data Model Prisma thành entity EF Core và `IEntityTypeConfiguration<T>`.
3. **Sự kiện phát sau commit.** Mọi sự kiện đi qua `DomainEventCollector`, rồi `FlushAsync` sau khi transaction commit. Transaction lỗi thì gọi `Discard`. Handler lỗi của module nghe không làm hỏng luồng đặt đơn.
4. **Đặt đơn và giữ hàng chung một transaction.** `CommerceDbContext` mở transaction. Module truyền `StockCallerTransaction(DbTransaction, DomainEventCollector)` vào `IStockService.ReserveAsync`. Hai `DbContext` phải dùng chung một kết nối PostgreSQL. COM-08 có test chứng minh rollback của `commerce` cũng hoàn lại tồn kho.
5. **Server tính lại mọi số tiền.** Đặt đơn lấy giá từ `ICatalogReader.GetSkusAsync` và phí từ `IShippingService.QuoteAsync`. Server bỏ qua mọi số tiền frontend gửi. Tổng khác preview thì trả 409 `ORDER_TOTAL_CHANGED`.
6. **Chống đặt trùng bằng `Idempotency-Key`.** Cột `orders.idempotency_key` có ràng buộc unique. Gửi lại cùng khóa trả đơn đã tạo với HTTP 200, không tạo đơn thứ hai. Thiếu header trả 400 `IDEMPOTENCY_KEY_REQUIRED`.
7. **Mã đơn là hàm thuần.** Mã có dạng `PK` cộng 8 ký tự trong bảng chữ `A-Z` và `2-9`, bỏ `O` và `I`. Hàm nhận nguồn ngẫu nhiên để test lặp lại được. Trùng mã thì sinh lại, tối đa 5 lần.
8. **Máy trạng thái là hàm thuần.** `OrderTransitions` trả kết quả cho cặp trạng thái cũ, trạng thái mới và người chuyển. Chuyển không hợp lệ trả 409 `INVALID_ORDER_TRANSITION`. Mỗi lần chuyển ghi một dòng `order_status_history`. Nhân viên chuyển thì ghi thêm đúng 1 dòng audit trong cùng transaction.
9. **Trạng thái giao hàng ánh xạ từ `ShipmentStatus`.** `Delivered` chuyển đơn sang `delivered`. `Returned` chuyển đơn sang `returned`. Trạng thái khác giữ đơn ở `shipping` và chỉ lưu vào lịch sử giao hàng. Sự kiện mang `Ref` gốc là mã đơn. Handler bỏ qua sự kiện có `ShipmentId` khác vận đơn hiện tại của đơn.
10. **Webhook SePay xác thực trước, lưu thô trước khi khớp.** Module so API key bằng phép so thời gian cố định. Body tối đa 16 KB, chỉ nhận JSON. Giao dịch lưu vào `payment_transactions` với `provider_tx_id` unique, rồi mới khớp đơn. Định dạng chính xác chờ COM-00.
11. **Khóa SePay chỉ nằm trong biến môi trường.** `SEPAY_WEBHOOK_API_KEY` không ra log, response hay frontend. Production thiếu khóa thì dừng khi khởi động. Dev và test thiếu khóa vẫn chạy, theo mẫu `GhnConfigurationCheck`.
12. **Số tài khoản nhận nằm trong cấu hình admin.** Người dùng chốt cách này ở Câu hỏi mở 6. Số tài khoản và mã ngân hàng nằm trong `commerce_settings`. Mỗi lần sửa ghi 1 dòng audit `commerce.settings.update`.
13. **Giới hạn giỏ nằm trong cấu hình.** Mặc định 5 sản phẩm mỗi SKU và 20 dòng mỗi giỏ. Hai giá trị lưu trong `commerce_settings`, cùng ngưỡng miễn phí ship và hạn mức COD.
14. **Link xem đơn của khách vãng lai dùng token băm.** Token ngẫu nhiên 32 byte. DB lưu SHA-256 của token. Module so hash bằng phép so thời gian cố định. Link chỉ cho xem đơn, xem trạng thái thanh toán và tự hủy đơn.
15. **Job chạy nền theo mẫu `ImageDeletionWorker`.** Job hết hạn chạy mỗi phút. Test gọi thẳng processor, không chờ timer.
16. **Giới hạn tần suất dùng `RateLimiter` của `Pickory.Shared`.** Route checkout preview giới hạn 20 lần mỗi phút cho mỗi khách, theo `spec/SPEC-shipping.md`. Webhook SePay giới hạn theo IP.
17. **Route theo REST mức tài nguyên, không HATEOAS.** Bảng dưới thay cho route đặt theo hành động trong spec cũ. Người dùng chốt bảng này ở Câu hỏi mở 1, và spec đã cập nhật theo bảng. Mỗi route con là một danh từ, nên `RequirePermission` vẫn gắn được theo từng route.

   | Route trong spec cũ | Route đã chốt |
   | --- | --- |
   | `POST /api/checkout/preview` | `POST /api/checkout/previews` |
   | `POST /api/orders/:code/cancel` | `POST /api/orders/{code}/cancellation` |
   | `GET /api/orders/:code/payment-status` | `GET /api/orders/{code}/payment` |
   | `POST /api/admin/orders/:code/confirm` | `POST /api/admin/orders/{code}/confirmation` |
   | `POST /api/admin/orders/:code/cancel` | `POST /api/admin/orders/{code}/cancellation` |
   | `POST /api/admin/orders/:code/invoice-issued` | `PUT /api/admin/orders/{code}/invoice-issuance` |
   | `POST /api/admin/orders/:code/cod-received` | `PUT /api/admin/orders/{code}/cod-receipt` |
   | `POST /api/admin/orders/:code/refunded` | `PUT /api/admin/orders/{code}/refund` |
   | `POST /api/admin/payments/transactions/:id/match` | `PATCH /api/admin/payments/transactions/{id}` |
   | `POST /api/admin/reviews/:id/hide` và `/show` | `PATCH /api/admin/reviews/{id}` với `hidden` và `reason` |
   | `POST /api/reviews/:id/report` | `POST /api/reviews/{id}/reports` |

   Route còn lại của spec đã là danh từ và giữ nguyên, ví dụ `/api/cart/items/{skuId}` và `/api/admin/orders/{code}/shipment`.
18. **Mã lỗi đề xuất.** Thông báo viết tiếng Việt.

   | Mã | HTTP | Khi nào |
   | --- | --- | --- |
   | `VALIDATION_ERROR` | 400 | Body hoặc query sai |
   | `IDEMPOTENCY_KEY_REQUIRED` | 400 | Đặt đơn thiếu `Idempotency-Key` |
   | `CART_LIMIT_EXCEEDED` | 422 | Vượt số lượng mỗi SKU hoặc số dòng mỗi giỏ |
   | `SKU_UNAVAILABLE` | 422 | SKU tắt, hết hàng hoặc sản phẩm ngừng bán khi checkout |
   | `COD_NOT_ALLOWED` | 422 | Chọn COD cho đơn trên hạn mức |
   | `ORDER_TOTAL_CHANGED` | 409 | Tổng khi đặt khác tổng của preview |
   | `INSUFFICIENT_STOCK` | 409 | Giữ hàng thất bại, kèm danh sách SKU thiếu |
   | `INVALID_ORDER_TRANSITION` | 409 | Chuyển trạng thái không hợp lệ |
   | `ORDER_NOT_FOUND` | 404 | Không có đơn, hoặc người gọi không phải chủ đơn |
   | `REVIEW_NOT_ALLOWED` | 403 | Người viết chưa nhận sản phẩm |
   | `REVIEW_EDIT_EXPIRED` | 409 | Sửa đánh giá sau 30 ngày |
   | `SHIPPING_UNAVAILABLE` | 503 | Lỗi từ `shipping`, giữ nguyên mã |

19. **Điểm đánh giá không đi vào `catalog`.** `catalog` không được phụ thuộc `commerce`, vì `commerce` đã phụ thuộc `catalog`. Trang sản phẩm gọi `GET /api/products/{id}/reviews`. Response gồm điểm trung bình, phân bố số sao và trang đánh giá. Người dùng chốt cách này ở Câu hỏi mở 5.
20. **Audit log.** Mọi thao tác nhân viên trên đơn, thanh toán, đánh giá và cấu hình ghi đúng 1 dòng qua `IAuditLogWriter` trong cùng transaction.

## Task List

Chi tiết từng task ở `tasks/commerce/todo.md`.

### Phase 0: Xác minh SePay
- [ ] COM-00 (tài liệu làm ngay; phần cần khóa chờ người dùng, không chặn task khác)

### Phase 1: Nền tảng
- [x] COM-01 · [x] COM-02 · [x] COM-03

### Checkpoint M1
- [x] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [x] Test kiến trúc chặn `Commerce` tham chiếu module khác ngoài Contracts
- [ ] Review với người dùng

### Phase 2: Giỏ hàng và đặt đơn
- [x] COM-04 · [x] COM-05 · [x] COM-UI1 · [x] COM-06 · [x] COM-UI7 · [x] COM-07 · [x] COM-08 · [x] COM-UI2 · [x] COM-09 · [x] COM-10 · [x] COM-UI3

### Checkpoint M2
- [ ] Khách vãng lai thêm giỏ, chọn địa chỉ, thấy phí ship và đặt được đơn COD trên `pnpm dev`
  - Chưa đạt (2026-10-12): GHN sandbox trả 404 cho mọi đường dẫn, nên preview trả `503 SHIPPING_UNAVAILABLE`. Chạy lại M2 khi sandbox hoạt động.
- [ ] Gửi cùng `Idempotency-Key` 2 lần chỉ tạo 1 đơn
  - Chưa kiểm: luồng COD dừng ở bước tính phí ship. Kiểm sau khi GHN sandbox hoạt động.
- [ ] Review với người dùng

### Phase 3: Thanh toán SePay
- [ ] COM-11 · [ ] COM-12 · [ ] COM-UI4 · [ ] COM-13 · [ ] COM-UI6

### Checkpoint M3
- [ ] Webhook giả lập đủ 5 trường hợp của spec. Gửi cùng giao dịch 3 lần chỉ ghi nhận 1 lần
- [ ] Đơn SePay quá 30 phút bị hủy và trả hàng
- [ ] Review với người dùng

### Phase 4: Back-office đơn hàng và vận chuyển
- [ ] COM-14 · [ ] COM-15 · [ ] COM-16 · [ ] COM-17 · [ ] COM-UI5

### Checkpoint M4
- [ ] `sales` xác nhận đơn và tạo vận đơn bằng `FakeGhnClient`. Trạng thái giao hàng tự cập nhật vào đơn
- [ ] `support` xem được đơn, gọi API hủy hoặc tạo vận đơn nhận 403
- [ ] Review với người dùng

### Phase 5: Sổ địa chỉ, đánh giá, liên kết tài khoản
- [ ] COM-18 · [ ] COM-UI8 · [ ] COM-19 · [ ] COM-UI9 · [ ] COM-20 · [ ] COM-UI10 · [ ] COM-21

### Checkpoint M5
- [ ] Chỉ người đã nhận hàng đánh giá được. Đánh giá bị ẩn biến mất và điểm trung bình đổi theo
- [ ] Review với người dùng

### Phase 6: Interface cho module sau
- [ ] COM-22 · [ ] COM-23

### Checkpoint M6
- [ ] `ICommerceReader` và `ICommerceReports` chỉ trả kiểu trong `Pickory.Modules.Commerce.Contracts`
- [ ] Review với người dùng

### Phase 7: E2E và nghiệm thu
- [ ] COM-E2E1 · [ ] COM-E2E2 · [ ] COM-E2E3 · [ ] COM-E2E4 · [ ] COM-E2E5 · [ ] COM-24

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt, trừ dòng ghi chờ khóa
- [ ] Coverage dòng của `Pickory.Modules.Commerce` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `activity-tracking`

## Traceability: Success Criteria → Task

| Success Criteria (spec) | Task | Cách kiểm |
| --- | --- | --- |
| Khách vãng lai đặt được đơn COD và đơn SePay từ đầu đến cuối, nhận email xác nhận | COM-08, COM-09, COM-11, COM-UI2, COM-UI4, COM-E2E1, COM-E2E2 | E2E đọc email trong outbox dev. Email thật chờ Open Question 2 của `SPEC.md` |
| Tiền SePay về thì trang chờ chuyển sang thành công trong 5 giây | COM-11, COM-UI4, COM-E2E2 | E2E bắn webhook giả lập rồi đo thời gian chuyển trang. Webhook thật chờ khóa SePay (COM-00, COM-24) |
| Gửi cùng webhook SePay 3 lần thì đơn chỉ ghi nhận thanh toán 1 lần | COM-11 | Test tích hợp gửi 3 lần, đếm `OrderPaidEvent` và dòng `payment_transactions` |
| Đơn SePay quá 30 phút bị hủy, tồn kho được trả lại | COM-12 | Test tích hợp dùng `TimeProvider` giả, gọi processor, kiểm movement `order_release` |
| Thiếu tiền, thừa tiền, sai nội dung hiện trong màn đối soát và khớp tay được | COM-11, COM-13, COM-UI6 | Test tích hợp; Vitest; E2E `--headed` |
| Đơn trên hạn mức COD không chọn được COD; tạm tính trên ngưỡng thì phí ship khách trả bằng 0 | COM-01, COM-07, COM-08, COM-UI2 | Unit test tính tiền; test tích hợp preview và đặt đơn; Vitest form checkout |
| Sales tạo vận đơn GHN (sandbox) từ trang đơn; trạng thái giao hàng tự cập nhật | COM-16, COM-UI5, COM-E2E3, COM-24 | Test tích hợp với `FakeGhnClient`; E2E. Sandbox chờ token GHN |
| `support` xem được đơn nhưng gọi API hủy hoặc tạo vận đơn nhận 403 | COM-14, COM-15, COM-16, COM-24 | Ma trận quyền; COM-E2E3 |
| Chỉ người đã nhận hàng đánh giá được; review bị ẩn biến mất khỏi trang sản phẩm và điểm trung bình được tính lại | COM-19, COM-20, COM-UI9, COM-E2E4 | Test tích hợp; E2E |
| Tắt API AI, toàn bộ E2E của module vẫn pass | COM-E2E5 | Chạy bộ E2E commerce với khóa AI sai |
| Coverage ít nhất 80% | COM-24 | Coverage gộp unit và tích hợp, cách đo như shipping |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| Định dạng webhook SePay khác giả định | Phải sửa parser và test | COM-00 đối chiếu tài liệu trước COM-11. Parser nằm trong một lớp riêng |
| Chưa có khóa SePay và token GHN | Không kiểm được tiền về thật và vận đơn thật | Làm bằng webhook giả lập và `FakeGhnClient`. Tiêu chí cần khóa để mở ở COM-24 |
| Hai `DbContext` không chung kết nối khi giữ hàng | Giữ hàng không rollback cùng đơn, gây lệch tồn kho | Quyết định 4. Test rollback trong COM-08 |
| Tiền về sau khi đơn đã hủy và hàng đã bán cho người khác | Khách mất tiền nếu không có ai để ý | Đơn giữ `cancelled`, thanh toán `refund_pending`, đơn hiện trong màn đối soát |
| Admin đổi số tài khoản nhận trên giao diện | Người chiếm quyền admin chuyển hướng tiền | Audit log mỗi lần sửa. Đã chốt ở Câu hỏi mở 6 |
| Sự kiện của vận đơn cũ đến sau khi tạo vận đơn mới | Đơn bị chuyển sai trạng thái | Quyết định 9: so `ShipmentId` với vận đơn hiện tại |
| Email lỗi khi đặt đơn | Khách không nhận link xem đơn | Email lỗi không làm hỏng đặt đơn. Trang đặt hàng thành công hiện link xem đơn |

## Câu hỏi mở

1. **Route REST cho `commerce`.** Spec dùng route theo hành động, mâu thuẫn với quyết định 20 của `tasks/shipping/plan.md`. Plan đề xuất bảng ở quyết định 17. Người dùng chốt bảng này, hoặc sửa spec trước khi làm COM-10.
   - **Đã chốt (2026-10-11):** dùng bảng của quyết định 17. Spec đã cập nhật theo bảng.
2. **Cổng gửi email.** `IEmailSender` nằm trong `Pickory.Modules.Identity/Emails/`, nên `commerce` chưa dùng được. Đề xuất: chuyển `IEmailSender` và `DevOutboxEmailSender` sang `Pickory.Shared/Email/`. Việc này đổi cấu trúc identity, thuộc nhóm "Ask first". COM-09 chờ quyết định này.
   - **Đã chốt (2026-10-11):** chuyển `IEmailSender` và `DevOutboxEmailSender` sang `Pickory.Shared/Email/`.
3. **Sự kiện xác thực email.** Spec cần gắn đơn vãng lai khi khách xác thực email. Identity chưa phát sự kiện nào ở bước này. Đề xuất: thêm `EmailVerifiedEvent(UserId, Email)` vào `Pickory.Modules.Identity.Contracts/Events/`. Đây là đổi Contracts, thuộc nhóm "Ask first". COM-21 chờ quyết định này.
   - **Đã chốt (2026-10-11):** thêm `EmailVerifiedEvent(UserId, Email)` vào `Pickory.Modules.Identity.Contracts/Events/`.
4. **Kiểm mã địa chỉ.** Sổ địa chỉ và checkout cần biết mã tỉnh/thành và phường/xã có trong danh mục. `IShippingService` chưa có hàm tra danh mục. Đề xuất: thêm `IAddressDirectory.FindAsync(provinceCode, wardCode, cancellationToken)` vào `Pickory.Modules.Shipping.Contracts`, trả tên chuẩn hoặc `null`. Đây là đổi Contracts. COM-07 và COM-18 chờ quyết định này.
   - **Đã chốt (2026-10-11):** thêm `IAddressDirectory.FindAsync` như đề xuất. Hàm trả tên chuẩn hoặc `null`.
5. **Điểm đánh giá trên trang sản phẩm.** Spec ghi điểm trung bình được cung cấp cho `catalog` qua interface đọc. `catalog` không phụ thuộc `commerce` được, vì sẽ tạo vòng phụ thuộc. Đề xuất ở quyết định 19: frontend đọc thẳng từ route của `commerce`. Thẻ sản phẩm trong danh sách chưa hiện số sao. Người dùng xác nhận cách này.
   - **Đã chốt (2026-10-11):** frontend gọi `GET /api/products/{id}/reviews` của `commerce`. Thẻ sản phẩm chưa hiện số sao.
6. **Số tài khoản nhận nằm ở đâu.** Mục Bảo mật của spec ghi số tài khoản nằm trong cấu hình server. User story của admin và `CommerceSettings` cho admin sửa trên giao diện. Plan theo `CommerceSettings`. Người dùng chọn một trong hai.
   - **Đã chốt (2026-10-11):** admin sửa trong `CommerceSettings`. Mỗi lần sửa ghi 1 dòng audit. Spec đã cập nhật mục Bảo mật.
7. **Sự kiện đánh giá cho `personalized-content`.** `personalized-content` tạo lại tóm tắt khi có review mới hoặc review bị ẩn. Spec `commerce` chưa có sự kiện này. Đề xuất: thêm `ReviewChangedEvent(ProductId, ReviewId, Change)` vào `Pickory.Modules.Commerce.Contracts/Events/`, phát ở COM-19 và COM-20. Người dùng duyệt thêm sự kiện ngoài spec.
   - **Đã chốt (2026-10-11):** thêm `ReviewChangedEvent(ProductId, ReviewId, Change)`, phát ở COM-19 và COM-20.
8. **GHN tự hủy vận đơn khi đơn đang `shipping`.** Spec không nói đơn chuyển sang đâu. Đề xuất: đơn về `confirmed`, gắn cờ `shipment_cancelled`, để `sales` tạo lại vận đơn hoặc hủy đơn.
   - **Đã chốt (2026-10-11):** đơn về `confirmed` và có cờ `shipment_cancelled`.
9. **Báo cáo vi phạm đánh giá.** Spec chỉ có `reportCount`. Một khách có thể báo cáo 3 lần một đánh giá. Đề xuất: thêm bảng `review_reports` với khóa unique `(review_id, user_id)`.
   - **Đã chốt (2026-10-11):** thêm bảng `review_reports`. Migration đầu tiên của COM-03 đã xong, nên bảng cần một migration riêng.
   - **Sửa (2026-10-12):** migration của COM-19 tạo bảng này. COM-19 có route báo cáo đánh giá, và route này cần bảng trước COM-20.
10. Các Open Questions của spec: ngưỡng 500.000 đ và 20.000.000 đ giữ mặc định, cấu hình được. Chưa làm yêu cầu đổi trả trên web. Định dạng SePay chờ COM-00.
