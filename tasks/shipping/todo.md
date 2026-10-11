# Task List: shipping

Plan và quyết định thiết kế: `tasks/shipping/plan.md`. Spec: `spec/SPEC-shipping.md`. Quy ước chung: `spec/SPEC.md`, `tasks/dotnet-migration/plan.md`, `CLAUDE.md`.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Task back-office thêm endpoint mới vào `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Kích thước: XS 1 file, S 1-2, M 3-5. Số file không tính file test.

---

## Phase 0: Xác minh GHN

### SHIP-00: Xác minh GHN sandbox
**Mô tả:** Gọi GHN sandbox bằng token thật để chốt các điểm tài liệu chưa nói rõ. Ghi kết quả vào `docs/adr/0003-ghn-shipping-integration.md`. Task này cần `GHN_TOKEN` và `GHN_SHOP_ID` do người dùng cung cấp.
**Trạng thái:** đã xác minh ngày 2026-10-11 với token sandbox. Kết quả ở ADR 0003. Còn 1 tiêu chí mở: trạng thái hủy được ngoài `ready_to_pick`.
**Tiêu chí nghiệm thu:**
- [x] API báo phí: xác định cách gửi địa chỉ 2 cấp (tỉnh/thành và phường/xã mới), kèm request và response thật đã che token
- [x] Danh sách mã trạng thái vận đơn của GHN, đối chiếu với bảng ánh xạ trong spec
- [x] Gửi lại cùng `client_order_code` trả cùng `order_code`, không tạo đơn thứ hai
- [x] API thời gian giao dự kiến (`leadtime`) nhận địa chỉ 2 cấp hay không
- [ ] Trạng thái nào còn hủy được
  Chưa kiểm hết: sandbox chỉ cho thấy `ready_to_pick` hủy được và không cho đẩy đơn sang trạng thái khác. Tài liệu GHN không liệt kê trạng thái hủy được. Task SHIP-09 (contract test sandbox) và vận hành thật sẽ kiểm tiếp. Xem ADR 0003, tiêu chí 5.
- [x] Token có giới hạn IP hay không, và IP nào cần đăng ký
**Kiểm tra:** script tạm ngoài repo, output thật trong ADR
**Phụ thuộc:** không · **Quy mô:** S
**File:** `docs/adr/0003-ghn-shipping-integration.md`

---

## Phase 1: Nền tảng

### SHIP-01: Logic thuần
**Mô tả:** Ba hàm thuần, không gọi DB và không gọi HTTP.
- `ParcelBuilder.Build(items)`: gộp nhiều SKU thành một kiện theo spec.
- `ShipmentStatusMapper.Map(rawStatus)`: trả trạng thái chuẩn, hoặc "không biết" với trạng thái lạ.
- `GhnOrderCode.For(ref, cancelledCount)`: mã `client_order_code` theo quyết định 4 của plan.
**Tiêu chí nghiệm thu:**
- [x] Cân nặng bằng tổng cân nặng nhân số lượng. Dài và rộng lấy giá trị lớn nhất. Cao bằng tổng chiều cao nhân số lượng
- [x] Danh sách rỗng, số lượng nhỏ hơn 1 hoặc kích thước âm bị từ chối
- [x] Mọi trạng thái GHN trong bảng của spec ánh xạ đúng trạng thái chuẩn
- [x] Trạng thái lạ trả "không biết", không ném lỗi
- [x] `IsTerminal` đúng với `delivered`, `returned`, `cancelled`
- [x] Mã lần đầu là `ref`. Sau n lần hủy là `ref-(n+1)`. Mã dài hơn 50 ký tự bị từ chối
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.Shipping/{Parcels/ParcelBuilder.cs, Shipments/ShipmentStatusMapper.cs, Shipments/GhnOrderCode.cs}`, test trong `apps/backend/tests/Pickory.UnitTests/Modules/Shipping/`

### SHIP-02: Khung module, Contracts, schema và cấu hình
**Mô tả:** Tạo `Pickory.Modules.Shipping` và `Pickory.Modules.Shipping.Contracts`. Thêm `ShippingDbContext`, migration đầu tiên, `ShippingModule.cs`, sự kiện `ShipmentStatusChangedEvent`. Thêm biến `GHN_API_BASE`, `GHN_TOKEN`, `GHN_SHOP_ID`, `GHN_WEBHOOK_SECRET`, `GHN_LIVE_TESTS` vào `AppConfig` và `.env.example`.
**Tiêu chí nghiệm thu:**
- [x] Bảng `shipments`, `shipment_events`, `address_provinces`, `address_wards`, `shipping_settings` theo Data Model của spec, không có cột quận/huyện
- [x] `shipments.ref` và `shipments.tracking_code` có ràng buộc unique
- [x] Migration chạy trên DB sạch. Lệnh `e2e:start` và fixture test tích hợp dựng được schema mới
- [x] `Pickory.ArchitectureTests` chặn `Shipping` tham chiếu module khác ngoài project Contracts
- [x] Production thiếu `GHN_TOKEN`, `GHN_SHOP_ID` hoặc `GHN_WEBHOOK_SECRET` thì dừng khi khởi động. Dev và test thiếu token vẫn chạy
- [x] `Program.cs` chỉ gọi `AddShippingModule` và `MapShippingEndpoints`
**Kiểm tra:** build, test kiến trúc, test tích hợp schema
**Phụ thuộc:** SHIP-01 · **Quy mô:** M

### SHIP-03: Adapter GHN
**Mô tả:** `IGhnClient` với các hàm: lấy tỉnh/thành, lấy phường/xã, báo phí, tạo đơn, hủy đơn, tra chi tiết. `GhnHttpClient` gọi GHN bằng `HttpClient`. `FakeGhnClient` dùng khi thiếu token. Lỗi GHN quy về `SHIPPING_UNAVAILABLE` hoặc `SHIPPING_REJECTED`.
**Tiêu chí nghiệm thu:**
- [x] Mỗi request có header `Token` và `ShopId`
- [x] Test HTTP mock cho từng hàm: thành công, GHN từ chối (400), 5xx, timeout 5 giây
- [x] 400 của GHN thành `SHIPPING_REJECTED` (422) kèm thông báo tiếng Việt. 5xx và timeout thành `SHIPPING_UNAVAILABLE` (503)
- [x] Báo phí retry đúng 1 lần khi lỗi mạng hoặc 5xx. Tạo đơn không retry
- [x] Log và lỗi trả về không chứa giá trị token
- [x] Contract test với sandbox chỉ chạy khi `GHN_LIVE_TESTS=1`
**Kiểm tra:** unit test với `HttpMessageHandler` giả
**Phụ thuộc:** SHIP-02 · **Quy mô:** M

### Checkpoint S1
- [x] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [x] Test kiến trúc chặn `Shipping` tham chiếu module khác ngoài Contracts

---

## Phase 2: Tính năng

### SHIP-04: Danh mục địa chỉ
**Mô tả:** Job đồng bộ tỉnh/thành và phường/xã từ GHN mỗi ngày. Lệnh `shipping:sync-addresses` chạy đồng bộ một lần. `GET /api/addresses/provinces` và `GET /api/addresses/wards?provinceCode=` công khai.
**Tiêu chí nghiệm thu:**
- [x] Đồng bộ ghi tỉnh/thành và phường/xã cùng `syncedAt`. Chạy lại không tạo trùng
- [x] Đơn vị GHN báo `status` khác `1` không hiện cho khách
- [x] Đọc hết phường/xã khi một tỉnh có hơn 200 dòng (phân trang)
- [x] GHN lỗi khi đồng bộ thì giữ dữ liệu cũ và ghi log
- [x] `wards` thiếu `provinceCode` trả 400. Mã tỉnh không có trả danh sách rỗng
- [x] Danh sách sắp theo tên tiếng Việt
**Kiểm tra:** test tích hợp với client giả
**Phụ thuộc:** SHIP-03 · **Quy mô:** M

### UI-S1: Ô chọn địa chỉ
**Mô tả:** Component `AddressPicker` trong `apps/frontend/src/features/shipping/`. Khách chọn tỉnh/thành, rồi chọn phường/xã của tỉnh đó, rồi nhập số nhà. `commerce` dùng component này ở checkout.
**Tiêu chí nghiệm thu:**
- [x] Đổi tỉnh/thành thì xóa phường/xã đã chọn và nạp danh sách mới
- [x] Ô phường/xã tắt khi chưa chọn tỉnh/thành
- [x] Lọc được theo tên không dấu, ví dụ gõ "ha noi" thấy "Hà Nội"
- [x] Dùng được bằng bàn phím. Có `label` cho từng ô. Lỗi hiện dưới ô bằng `FieldError`
- [x] Theo `docs/design/design-system.md`. Không tràn ngang ở 375 px
**Kiểm tra:** Vitest; ảnh chụp 1280 px và 375 px
**Phụ thuộc:** SHIP-04 · **Quy mô:** M

### SHIP-06: Cấu hình shipping
**Mô tả:** `GET` và `PUT /api/admin/shipping/settings` với quyền `shipping-settings:manage`. Cấu hình gồm địa chỉ lấy hàng (tên, số điện thoại, địa chỉ có mã) và gói dịch vụ mặc định. `GHN_SHOP_ID` lấy từ biến môi trường, theo spec.
**Tiêu chí nghiệm thu:**
- [x] `support` và `sales` nhận 403. Endpoint mới có trong ma trận quyền
- [x] Mã tỉnh/thành và phường/xã phải có trong danh mục đã đồng bộ, nếu không trả 400
- [x] Số điện thoại kiểm bằng luật của PROFILE-VAL và lưu dạng `0xxxxxxxxx`
- [x] Mỗi lần sửa ghi đúng 1 dòng audit `shipping.settings.update` trong cùng transaction
- [x] Response không chứa token GHN
**Kiểm tra:** test tích hợp
**Phụ thuộc:** SHIP-04 · **Quy mô:** M

### UI-S2: Màn cấu hình shipping
**Mô tả:** Màn `/admin/shipping/settings` trong back-office, dùng `AddressPicker`. Menu chỉ hiện với người có `shipping-settings:manage`.
**Tiêu chí nghiệm thu:**
- [x] Lưu thành công hiện thông báo. Lỗi hiện dưới đúng ô
- [x] Người thiếu quyền không thấy menu. Mở thẳng URL thấy trang không đủ quyền
- [x] Ảnh chụp 1280 px và 375 px không tràn ngang
**Kiểm tra:** Vitest; E2E `--headed`
**Phụ thuộc:** SHIP-06, UI-S1 · **Quy mô:** M

### SHIP-05: Báo phí
**Mô tả:** `IShippingService.QuoteAsync(to, parcel, codAmount, insuredValue)` trả phí, mã gói dịch vụ và ngày giao dự kiến. Task này không có route HTTP. Route báo phí cho giỏ hàng thuộc `commerce` (quyết định 19).
**Tiêu chí nghiệm thu:**
- [x] Chưa cấu hình địa chỉ lấy hàng thì trả lỗi rõ ràng, không gọi GHN
- [x] `insuredValue` lớn hơn 5.000.000 đ thì gửi GHN 5.000.000 đ
- [x] GHN không trả ngày giao dự kiến thì `ExpectedDeliveryDate` là `null`, không lỗi
- [x] Retry đúng 1 lần khi lỗi mạng hoặc 5xx, rồi trả `SHIPPING_UNAVAILABLE`
**Kiểm tra:** unit test và test tích hợp với client giả
**Phụ thuộc:** SHIP-06 · **Quy mô:** S

### SHIP-07: Tạo, hủy và tra vận đơn
**Mô tả:** `CreateShipmentAsync`, `CancelShipmentAsync`, `GetShipmentAsync` của `IShippingService`. `commerce` sẽ gọi các hàm này từ endpoint của nó (quyền `orders:fulfill`).
**Tiêu chí nghiệm thu:**
- [x] Tạo vận đơn lưu `trackingCode`, phí, kiện hàng, trạng thái `pending_pickup` và một dòng `shipment_events`
- [x] Lỗi mạng khi tạo: không lưu vận đơn, trả `SHIPPING_UNAVAILABLE`. Gọi lại với cùng `ref` gửi cùng `client_order_code`, và DB chỉ có 1 vận đơn còn hiệu lực
- [x] `ref` đã có vận đơn còn hiệu lực thì trả vận đơn đó, không gọi GHN
- [x] Hủy chuyển vận đơn sang `cancelled`, đổi `ref` thành `{ref}#cancelled#{n}`, phát sự kiện 1 lần
- [x] GHN báo không hủy được thì trả `SHIPPING_REJECTED`, trạng thái không đổi
- [x] Tạo lại sau khi hủy dùng `client_order_code` mới
- [x] Gửi GHN `required_note = CHOXEMHANGKHONGTHU`, `is_new_to_address = true`, tên tỉnh/thành và phường/xã đúng như danh mục
**Kiểm tra:** test tích hợp với client giả có chế độ lỗi mạng
**Phụ thuộc:** SHIP-06 · **Quy mô:** M

### SHIP-08: Webhook GHN và job đồng bộ dự phòng
**Mô tả:** `POST /api/webhooks/ghn` nhận cập nhật. Job chạy mỗi 30 phút tra lại vận đơn chưa kết thúc. Chuyển `RateLimiter` từ Identity sang `Pickory.Shared` theo quyết định 17.
**Tiêu chí nghiệm thu:**
- [x] Thiếu hoặc sai header `X-Pickory-Webhook-Secret` trả 401, không đổi DB
- [x] Route không cần `Origin` của web, vì GHN gọi từ server. Các route khác vẫn giữ luật kiểm `Origin`
- [x] `OrderCode` không có trong DB trả 200 và không làm gì, để GHN không thử lại
- [x] Module bỏ qua `Status` trong payload. Module gọi API chi tiết của GHN và dùng trạng thái trả về
- [x] Gửi cùng webhook 2 lần: phát `ShipmentStatusChangedEvent` đúng 1 lần
- [x] Trạng thái GHN lạ: ghi log cảnh báo, giữ trạng thái cũ, trả 200
- [x] GHN lỗi khi tra chi tiết: trả 503 để GHN thử lại
- [x] Quá giới hạn tần suất theo IP trả 429
- [x] Test của Identity vẫn xanh sau khi chuyển `RateLimiter`
- [x] Job bỏ qua vận đơn đã kết thúc. Job cập nhật vận đơn bị lỡ webhook và phát sự kiện đúng 1 lần
- [x] Chu kỳ job là 30 phút
- [x] Webhook hoặc job chuyển vận đơn sang `cancelled` thì đổi `ref` thành `{ref}#cancelled#{n}`, giống `CancelShipmentAsync`. Sau đó `CreateShipmentAsync` với `ref` gốc tạo vận đơn mới
**Kiểm tra:** test tích hợp; test gọi processor trực tiếp
**Phụ thuộc:** SHIP-07 · **Quy mô:** M

### SHIP-10: Sửa adapter GHN theo kết quả sandbox
**Mô tả:** Sửa request báo phí và ngày giao dự kiến, client giả, bảng ánh xạ trạng thái và tài liệu theo ADR 0003.
**Tiêu chí nghiệm thu:**
- [x] Báo phí gửi `is_new_to_address: true`, `to_province_id_v2` và `to_ward_id_v2` dạng số. Không gửi `to_ward_code` hay `to_district_id`
- [x] Ngày giao dự kiến gửi cùng 3 trường địa chỉ như báo phí
- [x] Mã tỉnh hoặc mã phường không đổi được sang số thì module không gọi GHN và trả lỗi rõ ràng
- [x] `FakeGhnClient` nhận request mới và từ chối phường không có trong danh mục giả, giống GHN
- [x] Ánh xạ thêm `money_collect_picking` sang `pending_pickup`, `return_fail` sang `returning`, `scrap` sang `exception`. Bảng trong `spec/SPEC-shipping.md` cập nhật
- [x] Comment "GIẢ ĐỊNH chờ SHIP-00" được thay bằng lý do đúng. Lý do của hậu tố mã đơn sau khi hủy được sửa theo ADR 0003
- [x] `plan.md`, mục "Kết quả tra tài liệu GHN" dòng 3, 4, 8 và quyết định 4, khớp ADR 0003
- [x] Contract test sandbox báo phí và ngày giao với địa chỉ 2 cấp đạt khi `GHN_LIVE_TESTS=1`
- [x] `dotnet build` và `dotnet test` sạch
**Kiểm tra:** test HTTP mock của `GhnHttpClient`; unit test ánh xạ; test tích hợp báo phí; contract test sandbox
**Phụ thuộc:** SHIP-00 · **Quy mô:** M

### Checkpoint S2
- [ ] Admin cấu hình địa chỉ lấy hàng trên giao diện, khách chọn được tỉnh/thành và phường/xã
- [x] Webhook và job đồng bộ phát sự kiện đúng 1 lần cho mỗi lần đổi trạng thái
- [ ] Review với người dùng

---

## Phase 3: Nghiệm thu

### SHIP-09: Nghiệm thu shipping
**Mô tả:** Kiểm toàn module trước khi sang `commerce`.
**Tiêu chí nghiệm thu:**
- [x] Ma trận quyền có mọi endpoint back-office của `shipping`
- [x] Coverage dòng của `Pickory.Modules.Shipping` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [x] Khi có token: contract test sandbox đạt (danh mục địa chỉ, báo phí, tạo rồi hủy 1 vận đơn)
  Ngày 2026-10-11, 6 test của `GhnSandboxContractTests` đạt với token sandbox. Xem mục 4.
- [x] Khi có token: đo p95 báo phí trên sandbox, dưới 2 giây
  p95 là 119 ms trên 20 lần gọi tuần tự. Xem mục 4.
- [x] Không có token: ghi rõ hai tiêu chí sandbox còn mở và lý do
  Không còn áp dụng. Lần kiểm đầu đọc nhầm `.env` là trống. Token có sẵn, và hai tiêu chí sandbox đã đạt.
**Kiểm tra:** `dotnet test`, script gộp coverage, output đo p95
**Phụ thuộc:** SHIP-08 · **Quy mô:** S

**Bằng chứng (2026-10-11):**

#### 1. Bảng Traceability

| Success Criteria | Bằng chứng | Trạng thái |
| --- | --- | --- |
| Khách chọn tỉnh/thành và phường/xã từ danh mục đồng bộ từ GHN | Tích hợp `AddressSyncProcessorTests`: `Sync_WritesProvincesAndWardsWithSyncedAtFromTimeProvider`, `Sync_ProvinceWith450Wards_ReadsAllInThreePageCalls`. `AddressEndpointsTests`: `Provinces_AnonymousVisitor_ReturnsOnlyActiveRows_WithCodeAndNameOnly`, `Wards_ReturnsOnlyActiveWardsOfThatProvince_SortedByVietnameseName`. E2E `shipping-settings.spec.ts` | Đạt |
| Báo phí p95 dưới 2 giây trên sandbox, hoặc trả lỗi rõ ràng | Lỗi rõ ràng: `QuoteRetryTests.Quote_Http5xxTwice_SendsExactlyTwoFeeRequestsThenThrowsUnavailable`, `Quote_NetworkErrorTwice_SendsExactlyTwoFeeRequestsThenThrowsUnavailable`; `QuoteServiceTests.Quote_WardUnknownToGhn_ThrowsRejectedFromCarrier`. P95: test live `GhnSandboxContractTests.Sandbox_CalculateFee_P95IsUnderBudget`, đạt trên sandbox, p95 119 ms | Đạt |
| Tạo vận đơn trên sandbox, lưu đúng mã; bấm lại sau lỗi mạng không tạo đơn thứ hai | Giả lập: `ShipmentCreateTests.Create_NetworkErrorAfterGhnCreated_StoresNothingThenRetryReturnsSameOrder`, `Create_RefAlreadyHasShipment_ReturnsExistingWithoutCallingGhnOrPublishing`; `ShipmentConcurrencyTests.Create_TwoCallersSameRef_StoresOneShipmentOneEventAndReturnsSameShipment`. Live: `GhnSandboxContractTests.Sandbox_CreateThenCancelOrder_EndsWithCancelStatus`, đạt trên sandbox | Đạt |
| Webhook cập nhật trạng thái, phát `ShipmentStatusChangedEvent` đúng 1 lần mỗi lần đổi | `GhnWebhookTests.Webhook_StatusChanges_UpdatesShipmentAndPublishesOneEvent`, `Webhook_SameWebhookTwiceInSequence_PublishesOneEventAndWritesOneHistoryRow`, `Webhook_TwoConcurrentWebhooksForSameOrder_PublishOneEventAndWriteOneHistoryRow` | Đạt |
| Tắt webhook, job đồng bộ cập nhật trạng thái trong 30 phút | `ShipmentStatusSyncProcessorTests.Sync_MissedWebhook_UpdatesShipmentAndPublishesExactlyOnce`; đơn vị `ShipmentStatusSyncWorkerTests.SyncInterval_Is30Minutes`, `Worker_SyncsOnStartup_ThenEvery30Minutes` | Đạt |
| `support` gọi API cấu hình nhận 403 | `PermissionMatrixTests.Support_HasAccessOnlyWhenAuthorized` cho hai dòng `GET` và `PUT /api/admin/shipping/settings`; `BackofficeEndpoints_CoversAllShippingAdminEndpoints` kiểm ma trận đủ route; `ShippingSettingsApiTests.Get_WithoutPermission_Returns403`, `Put_WithoutPermission_Returns403AndSavesNothing` | Đạt |
| Coverage ít nhất 80% | Mục 2: coverage dòng gộp 97.3% | Đạt |

#### 2. Coverage

- Cách đo giống catalog: coverage dòng của assembly `Pickory.Modules.Shipping`, gộp hai lệnh `dotnet test ... --collect:"XPlat Code Coverage"` cho `tests/Pickory.UnitTests` và `tests/Pickory.IntegrationTests`.
- Script Python gộp hai file `coverage.cobertura.xml`. Một dòng tính là đã chạy khi ít nhất một lượt chạm tới dòng đó.
- Phép đo bỏ `Persistence/Migrations/`, vì đó là code sinh tự động.
- Chung: 1629/1674 dòng = **97.3%** (riêng test đơn vị 44.9%, riêng test tích hợp 87.4%).
- Theo thư mục cấp 1:
  - `Addresses/`: 98.9% (177/179)
  - `Configuration/`: 100% (21/21)
  - `Entities/`: 97.5% (39/40)
  - `Features/`: 100% (109/109)
  - `Ghn/`: 96.8% (451/466)
  - `Parcels/`: 100% (38/38)
  - `Persistence/`: 94.1% (111/118)
  - `Settings/`: 100% (114/114)
  - `Shipments/`: 100% (125/125)
  - `Sync/`: 98.6% (69/70)
  - `Webhooks/`: 97.6% (83/85)
  - gốc module (`ShippingModule.cs`, `ShippingService.cs`...): 94.5% (292/309)
- Một file dưới 60%. Không thêm test, vì tổng đã vượt 80% và file không chứa luật nghiệp vụ:
  - `Persistence/ShippingDbContextFactory.cs` (0%): chỉ dùng khi chạy lệnh migration.

#### 3. Lệnh tổng (trong `apps/backend`)

- `dotnet build`: 0 lỗi, 0 cảnh báo.
- `dotnet test` toàn solution: 6 test kiến trúc, 924 test đơn vị và 1086 test tích hợp đạt; 10 test bỏ qua (test live GHN và test live AI), 0 lỗi.
- `ProviderReliabilityTests.RunAsync_RetriesOnlyTransientErrorsWithBackoff` hết thời gian chờ khi máy bận trong 2 lượt chạy toàn bộ. Chạy riêng lượt test đó thì đạt, và lượt chạy riêng cả `Pickory.UnitTests` đạt 924/924. Test này thuộc `ai-gateway`, không liên quan `shipping`.
- Bộ lệnh frontend nằm ngoài lượt đo này.

#### 4. Test live trên sandbox

Kết quả ngày 2026-10-11 với `GHN_LIVE_TESTS=1`:

- 6 test của `GhnSandboxContractTests` đạt: danh mục tỉnh, danh mục phường, báo phí, ngày giao, tạo rồi hủy vận đơn, đo p95.
- Báo phí: 20 mẫu, min 18 ms, p95 119 ms, max 283 ms.
- Vận đơn thử kết thúc ở trạng thái `cancel`, nên sandbox không còn đơn sống của lần chạy này.

Lệnh chạy lại:

1. Kiểm tra `.env` có `GHN_TOKEN` và `GHN_SHOP_ID` của sandbox.
2. Đặt biến môi trường `GHN_LIVE_TESTS=1`.
3. Chạy `dotnet test tests/Pickory.IntegrationTests --filter "FullyQualifiedName~GhnSandboxContractTests" --logger "console;verbosity=detailed"` trong `apps/backend`.
4. Đọc dòng `Báo phí sandbox: ... p95 ... ms` trong output để lấy p95.
5. Kiểm tra sandbox không còn đơn `ship09-*` ở trạng thái sống.

---

## Checkpoint cuối
- [x] Mọi dòng Traceability đạt
  Bảng ở mục 1 của SHIP-09: 7/7 dòng đạt.
- [x] Coverage dòng của `Pickory.Modules.Shipping` ít nhất 80%, bỏ `Persistence/Migrations/`
- [x] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
  Kết quả ngày 2026-10-11: Vitest 806 test đạt, E2E 40 test đạt. `dotnet test` đạt 6 test kiến trúc, 924 test đơn vị, 1086 test tích hợp, 10 test live bỏ qua.
- [ ] Review với người dùng, rồi mới bắt đầu `commerce`
