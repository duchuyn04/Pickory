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
**Trạng thái:** đã xác minh ngày 2026-10-11 với token sandbox. Kết quả ở ADR 0003.
**Tiêu chí nghiệm thu:**
- [x] API báo phí: xác định cách gửi địa chỉ 2 cấp (tỉnh/thành và phường/xã mới), kèm request và response thật đã che token
- [x] Danh sách mã trạng thái vận đơn của GHN, đối chiếu với bảng ánh xạ trong spec
- [x] Gửi lại cùng `client_order_code` trả cùng `order_code`, không tạo đơn thứ hai
- [x] API thời gian giao dự kiến (`leadtime`) nhận địa chỉ 2 cấp hay không
- [x] Trạng thái nào còn hủy được: `ready_to_pick` hủy được trên sandbox. Trạng thái khác dựa vào `result: false` của GHN, và có test cho nhánh này
  Người dùng duyệt đổi tiêu chí ngày 2026-10-11. Tài liệu GHN không liệt kê trạng thái hủy được, và sandbox không cho đẩy đơn khỏi `ready_to_pick`.
  - Sandbox: `GhnSandboxContractTests.Sandbox_CreateThenCancelOrder_EndsWithCancelStatus` hủy được đơn `ready_to_pick`.
  - Nhánh `result: false`: `GhnHttpClientOperationsTests.CancelOrder_ResultFalse_ThrowsRejectedWithItemMessage`, `ShipmentCancelTests.Cancel_GhnRejects_KeepsStatusAndRefAndPublishesNothing`.
  - Khi vận hành thật, ghi trạng thái nào GHN từ chối hủy vào ADR 0003.
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
- [x] Admin cấu hình địa chỉ lấy hàng trên giao diện, khách chọn được tỉnh/thành và phường/xã
  Kiểm ngày 2026-10-11 trên `pnpm dev` với dữ liệu GHN sandbox thật.
  - Lệnh `shipping:sync-addresses` đồng bộ 34 tỉnh/thành và 3321 phường/xã.
  - Admin gõ "ha noi" thấy "Hà Nội", gõ "hoan kiem" thấy "Phường Hoàn Kiếm".
  - Admin lưu cấu hình thì trang báo "Đã lưu cấu hình vận chuyển.". Tải lại trang vẫn giữ đủ giá trị.
  - Đổi sang "Hồ Chí Minh" thì ô phường/xã bị xóa. Lưu khi thiếu phường/xã thì lỗi "Cần chọn phường/xã." hiện dưới ô.
  - Ở 375 px, trang không tràn ngang. Console không có lỗi.
  - Ô chọn ở checkout của khách thuộc `commerce`. Màn này dùng cùng component `AddressPicker`.
- [x] Webhook và job đồng bộ phát sự kiện đúng 1 lần cho mỗi lần đổi trạng thái
- [x] Review với người dùng

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
- [x] Review với người dùng, rồi mới bắt đầu `commerce`

---

## Phase 4: Sửa sau nghiệm thu commerce (thêm ngày 2026-10-19)

### SHIP-11: Gửi đúng địa chỉ lấy hàng khi tạo vận đơn GHN
**Mô tả:** Ngày 2026-10-19, COM-24 tạo vận đơn từ trang đơn admin trên sandbox. GHN trả 400 `From address conflict`.
- `ShippingService` gửi địa chỉ lấy hàng của cấu hình bằng các trường `from_*` hai cấp, kèm `is_new_from_address: true`.
- `Sandbox_CreateThenCancelOrder_EndsWithCancelStatus` của SHIP-09 không gửi `from_*`. Vì vậy request thật của trang đơn chưa được kiểm trên sandbox.
- ADR 0003 ghi rằng shop sandbox dùng địa chỉ lấy hàng kiểu cũ (`is_new_address: false`). Đây là nguyên nhân có thể, chưa xác minh.
- `spec/SPEC-shipping.md` cho admin cấu hình địa chỉ lấy hàng. Cách sửa phải giữ cho cấu hình này có hiệu lực trên vận đơn.

**Tiêu chí nghiệm thu:**
- [x] Thử trên sandbox các cách gửi `from_*`. ADR 0003 có mục mới ghi request đã che dữ liệu, response và kết luận
  Bảy biến thể đã thử ngày 2026-10-19. Xem mục "Bổ sung SHIP-11" của `docs/adr/0003-ghn-shipping-integration.md`.
- [x] Mọi vận đơn thử trên sandbox kết thúc ở trạng thái `cancel`
  Kiểm lúc 12:10 giờ Việt Nam ngày 2026-10-19 (05:10 UTC): cả 51 đơn của shop trong 8 giờ gần nhất ở `cancel`. Hai đơn bị kẹt do lỗi gateway đã hủy tay. Xem ADR 0003.
- [x] Contract test sandbox tạo rồi hủy vận đơn bằng request do `ShippingService` dựng, với địa chỉ lấy hàng của cấu hình. Test đạt khi `GHN_LIVE_TESTS=1`
  `GhnSandboxContractTests.Sandbox_CreateThenCancelOrder_WithShippingServiceRequest_KeepsConfiguredPickupAddress` đạt khi `GHN_LIVE_TESTS=1`. Test chập chờn khi gateway GHN báo gián đoạn ở bước hủy.
- [x] Chi tiết vận đơn trên sandbox có tên, số điện thoại và địa chỉ lấy hàng đúng như cấu hình
  Cùng test trên kiểm `from_name`, `from_phone`, `from_address_v2`, `from_ward_id_v2` và `is_new_from_address`.
- [x] Nếu GHN không nhận địa chỉ lấy hàng của cấu hình theo cách nào: dừng và hỏi người dùng trước khi sửa code
  Không cần hỏi. Biến thể B, tức request hiện tại của app, được sandbox nhận và lưu đúng địa chỉ.
- [x] Test HTTP mock của `GhnHttpClient` kiểm body tạo đơn gửi các trường `from_*` theo cách đã chốt
  `GhnHttpClientOperationsTests.CreateOrder_WithPickupAddress_PostsTwoLevelPickupNamesAndNewAddressFlag` và `CreateOrder_WithoutPickupAddress_OmitsEveryPickupField`.
- [x] `FakeGhnClient` từ chối request tạo đơn có `from_*` sai luật, giống lỗi sandbox đã thấy
  `FakeGhnClientTests.CreateOrder_PickupWardOrProvinceNotInCatalog_IsRejectedWithTranslatedMessage`, `FakeGhnClientTests.CreateOrder_PickupAddressWithoutNewAddressFlag_IsRejectedByFakeOnly` và `ShipmentCreateTests.Create_SenderWardUnknownToGhn_ThrowsRejectedAndStoresNothing`.
- [x] `dotnet build` và `dotnet test` sạch
  Reviewer chạy ngày 2026-10-19 trong `apps/backend`: `dotnet build` 0 lỗi, 0 cảnh báo. `Pickory.UnitTests` 1576 đạt. `Pickory.ArchitectureTests` 14 đạt. `Pickory.IntegrationTests` toàn bộ 2057 đạt, 11 bỏ qua, 1 lỗi, 53 phút 21 giây. Test lỗi là `AdminStaffTests.TwoLastAdmins_DemotingEachOtherConcurrentlyKeepsOneAdmin` của `identity`, test chập chờn không thuộc SHIP-11. Chạy riêng 3 lần, cả 3 lần đạt. Trong `apps/frontend`, `pnpm test:e2e --headed` 110 đạt, 14.2 phút.
**Kiểm tra:** contract test sandbox; test HTTP mock; test tích hợp tạo vận đơn của `shipping` và `commerce`
**Phụ thuộc:** SHIP-10 · **Quy mô:** S

### SHIP-12: Báo rõ lỗi địa chỉ của GHN và kiểm địa chỉ nhận trước khi tạo vận đơn
**Mô tả:** Ngày 2026-10-19, người dùng duyệt cách sửa lỗi `To address conflict` (ADR 0003, mục "Bổ sung SHIP-11").
- GHN từ chối địa chỉ khi tên đường không thuộc phường đã chọn. Khách vẫn đặt đơn được, nhưng nhân viên không tạo được vận đơn. Admin không sửa được địa chỉ nhận, nên đơn bị kẹt.
- `GhnHttpClient` chuyển nguyên văn thông báo tiếng Anh của GHN ra trang đơn, ví dụ `To address conflict`.
- GHN từng ghi nhãn `From address conflict` cho lỗi của địa chỉ nhận. Thông báo mới không khẳng định chắc lỗi nằm ở địa chỉ nào.
- Task này có phần thử trên sandbox. Kết quả thử quyết định có làm phần kiểm địa chỉ nhận hay không.
- Hàm kiểm địa chỉ nhận là đổi Contracts của `Pickory.Modules.Shipping.Contracts`. Người dùng duyệt ngày 2026-10-19.

**Tiêu chí nghiệm thu:**
- [x] Lỗi `To address conflict` và `From address conflict` của GHN thành thông báo tiếng Việt. Thông báo nói tên đường không khớp phường, và nhắc kiểm cả địa chỉ nhận lẫn địa chỉ lấy hàng
  `GhnRejectionMessageTests` trong `Pickory.UnitTests/Modules/Shipping/Ghn/`: `AddressConflict_IsTranslatedWithoutCarrierPrefixOrEnglish` (8 ca, kể cả khác chữ hoa và chữ thường, cho tạo đơn và xem trước) và `AddressConflict_KeepsKindSoCallersCanTellToFromFrom`.
- [x] Lỗi `ward not found` và `province not found` của địa chỉ lấy hàng thành thông báo tiếng Việt, nhắc cập nhật trang cấu hình vận chuyển
  `GhnRejectionMessageTests.PickupPlaceNotFound_IsTranslatedToConfigurationHint` (5 ca) và `ShipmentCreateTests.Create_SenderWardUnknownToGhn_ThrowsRejectedAndStoresNothing`.
- [x] Lỗi khác của GHN giữ cách báo hiện tại
  `GhnRejectionMessageTests.OtherCarrierMessage_KeepsCurrentFormat` và `ToWardNotFoundOfRecipient_IsNotMistakenForPickupConfiguration`. Cả hai giữ `Đơn vị vận chuyển từ chối yêu cầu: <thông báo>`.
- [x] Thử API `shipping-order/preview` của GHN trên sandbox với địa chỉ nhận bị từ chối và địa chỉ hợp lệ. ADR 0003 có mục mới ghi request đã che dữ liệu, response và kết luận
  Mục "Bổ sung SHIP-12" của `docs/adr/0003-ghn-shipping-integration.md`. Mỗi request gửi 3 lần: địa chỉ hợp lệ 3/3 trả 200, địa chỉ nhận bị từ chối 0/3 trả 200, địa chỉ lấy hàng bị từ chối 0/3 trả 200. Không đơn nào được tạo.
- [x] Nếu `preview` không bắt được lỗi địa chỉ: dừng phần kiểm địa chỉ, giữ các tiêu chí bên dưới `[ ]` và báo người dùng
  Không áp dụng. `preview` bắt được lỗi địa chỉ ổn định ở cả 3 lần gửi, nên phần kiểm địa chỉ đã làm.
- [x] `IShippingService` có hàm kiểm địa chỉ nhận qua `preview`. Hàm trả một trong ba kết quả: hợp lệ, bị từ chối kèm thông báo, hoặc không kiểm được vì GHN lỗi
  `IShippingService.CheckDeliveryAddressAsync` trả `AddressCheckResult` (`Valid`, `Rejected` kèm `AddressRejectionReason`, `Unverifiable`). Kiểu trả về nêu loại lý do, không nêu câu chữ, để `commerce` tự viết câu cho khách. Test: `ShipmentAddressCheckTests`.
- [x] Hàm kiểm không tạo vận đơn trên GHN và không ghi gì vào DB
  `ShipmentAddressCheckTests.Check_AddressAcceptedByGhn_ReturnsValidAndCreatesNothing` và `Check_StreetNotInWard_ReturnsRejectedWithStreetReasonAndCreatesNothing` kiểm 0 lần `CreateOrderAsync`, 0 vận đơn và 0 dòng lịch sử.
- [x] `FakeGhnClient` có cách giả lập địa chỉ nhận bị từ chối, để test của `commerce` dùng được
  Dòng địa chỉ nhận chứa `FakeGhnClient.RejectedAddressMarker` (`#ghn-tu-choi`) thì bản giả báo `To address conflict`. Quy ước nằm trong comment của hằng số. Test: `FakeGhnClientTests.PreviewOrder_ToAddressWithRejectedMarker_IsRejectedAsAddressMismatch`.
- [x] Contract test sandbox cho `preview` đạt khi `GHN_LIVE_TESTS=1`
  `GhnSandboxContractTests.Sandbox_PreviewOrder_AcceptsValidAddressAndRejectsMismatchedStreetWithoutCreatingOrder` đạt khi `GHN_LIVE_TESTS=1`. Test chập chờn khi gateway GHN báo gián đoạn.
- [x] `dotnet build` và `dotnet test` sạch
  Reviewer chạy ngày 2026-10-19 trong `apps/backend`: `dotnet build` 0 lỗi, 0 cảnh báo. `Pickory.UnitTests` 1606 đạt. `Pickory.ArchitectureTests` 14 đạt. `Pickory.IntegrationTests` toàn bộ 2067 đạt, 12 bỏ qua, 1 lỗi, 41 phút 24 giây. Test lỗi là `AdminStaffTests.TwoLastAdmins_DemotingEachOtherConcurrentlyKeepsOneAdmin` của `identity`, lỗi khi chạy toàn bộ nhưng đạt cả 3 lần chạy riêng. Trong `apps/frontend`, `pnpm test:e2e --headed` 93 đạt, 17 lỗi do mạng của máy gián đoạn (`ERR_NETWORK_CHANGED`, `ERR_NAME_NOT_RESOLVED`). Chạy lại bằng `npx playwright test --last-failed --headed`, cả 17 test đạt.
**Kiểm tra:** test HTTP mock của `GhnHttpClient`; test tích hợp của `shipping`; contract test sandbox
**Phụ thuộc:** SHIP-11 · **Quy mô:** M

---

## Phase 5: Phí vận chuyển dự phòng (thêm ngày 2026-10-19)

**Bối cảnh:** GHN lỗi lúc tính phí thì khách không đặt được đơn. Trang thanh toán hiện "Chưa tính được phí vận chuyển, thử lại". Người dùng duyệt các quyết định sau ngày 2026-10-19:

1. Thứ tự dùng phí khi GHN không phản hồi: phí GHN đã lưu trong 24 giờ, rồi tới bảng phí dự phòng.
2. Bảng phí chia 3 tuyến theo vùng của GHN: nội tỉnh, nội vùng, liên vùng.
3. Khối lượng tính cước giống GHN. Khối lượng là số lớn hơn giữa cân thật và `Dài × Rộng × Cao (cm) / 5000`, làm tròn lên theo nấc 500 g.
4. Mỗi tuyến có giá gốc, khối lượng đã gồm trong giá gốc, và giá cho mỗi 500 g tiếp theo. Admin nhập giá đã gồm phụ phí.
5. Chỉ dùng phí dự phòng khi GHN không phản hồi: lỗi mạng, hết thời gian chờ, hoặc 5xx. GHN từ chối yêu cầu thì vẫn báo lỗi.
6. Admin bật hoặc tắt phí dự phòng. Tắt thì giữ cách báo lỗi hiện tại.
7. Khách không thấy nhãn nào khác. Trang đơn admin có nhãn "Phí ship dự phòng".
8. Phí dự phòng không có ngày giao dự kiến.

Nguồn tra cứu ngày 2026-10-19:

- [Cách tính khối lượng hàng hóa của GHN](https://ghn.vn/blogs/thong-tin-giao-hang/cach-tinh-khoi-luong-hang-hoa)
- [Bảng giá GHN](https://ghn.vn/pages/bang-gia-moi-sieu-tiet-kiem): 3 vùng, làm tròn khối lượng, phụ phí xăng dầu
- [Bảng giá GHTK](https://ghtk.vn/blog/giao-hang-tieu-chuan/): giá gốc theo tuyến và giá mỗi 500 g
- [Backup shipping rates của Shopify](https://help.shopify.com/en/manual/fulfillment/setup/shipping-rates/backup-rates)

Ba vùng theo bảng giá GHN:

- Vùng 3: từ Hà Tĩnh trở ra miền Bắc.
- Vùng 2: từ Quảng Ngãi đến Quảng Trị.
- Vùng 1: từ Gia Lai trở vào miền Nam.

### SHIP-13: Cấu hình phí dự phòng và cách tính phí
**Mô tả:** Lưu bảng phí dự phòng. Thêm route quản trị `GET` và `PUT /api/admin/shipping/fallback-fee` với quyền `ShippingSettingsManage`. Thêm hàm tính phí thuần theo tuyến và khối lượng. Task cần bảng DB mới, người dùng duyệt ngày 2026-10-19.
**Tiêu chí nghiệm thu:**
- [x] Bảng 34 tỉnh thuộc 3 vùng nằm trong code, theo mã tỉnh của danh mục. Unit test kiểm đủ 34 tỉnh, mỗi tỉnh đúng một vùng
  `FallbackFeeRegions` (mã lấy từ `province/all` v3 của sandbox ngày 2026-10-19). `FallbackFeeRegionsTests` kiểm 34 mã khác nhau, 18, 4 và 12 tỉnh theo vùng, và vùng của từng mã.
- [x] Tuyến là nội tỉnh khi cùng tỉnh với địa chỉ lấy hàng, nội vùng khi cùng vùng, còn lại là liên vùng
  `FallbackFeeRouteRulesTests` kiểm cùng mã (kể cả mã ngoài bảng), cùng vùng, khác vùng và tỉnh ngoài bảng trả "không xác định".
- [x] Khối lượng tính cước lấy số lớn hơn giữa cân thật và cân quy đổi, rồi làm tròn lên bội số của 500 g
  `FallbackFeeCalculatorTests.ChargeableWeight_*` kiểm 499, 500, 501 g, hộp 30 x 20 x 10 cm (1500 g), phần lẻ của cân quy đổi và kiện lớn không tràn số.
- [x] Phí bằng giá gốc khi khối lượng không vượt khối lượng gốc. Mỗi 500 g vượt, kể cả phần lẻ, cộng thêm một lần giá bước
  `FallbackFeeCalculatorTests.Fee_*` kiểm khối lượng gốc 1000 g và 1200 g, vượt 1 g, vượt 2000 g (4 bước) và giá lớn nhất cho kiện 30 kg.
- [x] `GET` trả cấu hình hiện tại, hoặc giá trị mặc định đang tắt khi chưa có
  `FallbackFeeApiTests.Get_WhenNotConfigured_ReturnsDisabledWithNullPrices` và `Get_AfterPut_ReturnsStoredValues`.
- [x] `PUT` từ chối giá âm, giá rỗng, khối lượng gốc không dương và số quá lớn, với lỗi gắn vào từng ô
  `FallbackFeeApiTests.Put_InvalidPriceValue_Returns400OnThatField` (9 trường, mỗi trường nhiều giá trị sai), cùng test giới hạn, trường lạ và nhiều trường sai trong một phản hồi.
- [x] Bật phí dự phòng mà thiếu giá của một tuyến thì `PUT` từ chối
  `FallbackFeeApiTests.Put_EnabledWithMissingField_Returns422OnThatField` (9 trường, thiếu hoặc `null`) trả 422 `FALLBACK_FEE_INCOMPLETE` và không ghi gì.
- [x] Mỗi lần lưu ghi một dòng audit log có giá trị trước và sau
  `FallbackFeeApiTests.Put_FirstTime_WritesOneAuditRowWithDefaultBeforeAndNewAfter`, `Put_SameValuesTwice_WritesOneAuditRowPerRequest` và hai test audit lỗi giữ nguyên cấu hình.
- [x] Ma trận quyền có hai route mới. `support` và `sales` nhận 403
  `PermissionMatrixTests` có `GET` và `PUT /api/admin/shipping/fallback-fee`. `FallbackFeeApiTests` kiểm thêm 403 cho `support`, `sales` và 401 cho khách.
- [x] `spec/SPEC-shipping.md` có mục phí dự phòng theo các quyết định ở đầu Phase 5
  Mục "Phí dự phòng" trong phần Thiết kế, hai route trong bảng API, mô hình dữ liệu và phần Testing của `spec/SPEC-shipping.md`.
- [x] `dotnet build` và `dotnet test` sạch
  Reviewer chạy ngày 2026-10-19. `dotnet build` 0 lỗi, 0 cảnh báo. `Pickory.UnitTests` 1680 đạt (lần đầu 1679 đạt và 1 lỗi khi máy bận, hai lần chạy lại đều đạt 1680). `Pickory.ArchitectureTests` 14 đạt. Bộ tích hợp lọc theo handoff, thêm `PlaceOrderTests`: 875 đạt, 12 bỏ qua, 0 lỗi. Toàn bộ `Pickory.IntegrationTests`: 2197 đạt, 12 bỏ qua, 1 lỗi sau 46 phút 28 giây. Test lỗi là `AdminStaffTests.TwoLastAdmins_DemotingEachOtherConcurrentlyKeepsOneAdmin` của `identity`, không thuộc SHIP-13. Test này lỗi khi chạy toàn bộ và đạt cả 3 lần chạy riêng. E2E `shipping-settings.spec.ts` đạt, spec chọn Hà Nội nên kiểm được mã Hà Nội mới của bản giả.
**Kiểm tra:** unit test hàm tính phí và bảng vùng; test tích hợp route; ma trận quyền
**Phụ thuộc:** SHIP-12 · **Quy mô:** M

### SHIP-14: Báo phí khi GHN không phản hồi
**Mô tả:** `QuoteAsync` lưu mỗi phí GHN trả thành công. GHN không phản hồi thì hàm dùng phí đã lưu còn hạn. Không có phí đã lưu thì hàm dùng bảng phí dự phòng. `Quote` trong Contracts có thêm nguồn phí. Task cần bảng DB lưu phí, người dùng duyệt ngày 2026-10-19.
**Tiêu chí nghiệm thu:**
- [x] Phí GHN trả thành công được lưu theo khóa: phường nhận, phường lấy hàng, loại dịch vụ, khối lượng tính cước và giá trị khai giá
  `QuoteFallbackTests.Quote_GhnSucceeds_ReturnsGhnSourceAndSavesTheFee` kiểm khóa. Khóa có thêm số tiền thu hộ (reviewer chốt), nên `Quote_GhnSucceeds_SavesClampedInsuredValueAndCodAmountInKey` kiểm giá trị khai giá đã kẹp trần và tiền thu hộ. `QuoteCacheStoreTests.Find_OneKeyComponentDiffers_ReturnsNull` kiểm từng thành phần của khóa.
- [x] GHN không phản hồi và có phí đã lưu dưới 24 giờ cùng khóa: trả phí đó, nguồn là phí đã lưu
  `QuoteFallbackTests.Quote_GhnDownAndFreshCachedFee_ReturnsCachedFeeWithoutDeliveryDate` và `Quote_GhnDownWithCachedFeeAndEnabledTable_PrefersCachedFee`.
- [x] Phí đã lưu từ 24 giờ trở lên không được dùng. Test dùng `TimeProvider` giả để kiểm mốc 24 giờ
  `QuoteCacheStoreTests.Find_JustUnder24Hours_ReturnsFee_AndExactly24Hours_ReturnsNull` và `QuoteFallbackTests.Quote_GhnDown_CachedFeeExpiresAt24Hours_ThenFallbackTableApplies` dùng `FakeTimeProvider` ở mốc 23:59:59 và 24:00:00.
- [x] GHN không phản hồi, không có phí đã lưu, phí dự phòng đang bật: trả phí theo bảng, nguồn là dự phòng
  `QuoteFallbackTests.Quote_GhnDownNoCachedFee*` cho 3 tuyến (nội tỉnh 25.000, nội vùng 36.000, liên vùng 47.000 đồng), `Quote_GhnDownWithHeavyGoodsSetting_FallbackKeepsConfiguredServiceType` và `FallbackFeeQuoterTests` (unit).
- [x] GHN không phản hồi, không có phí đã lưu, phí dự phòng đang tắt: giữ lỗi `SHIPPING_UNAVAILABLE` như hiện tại
  `QuoteFallbackTests.Quote_GhnDownAndTableDisabled_ThrowsUnavailable`, `Quote_GhnDownAndNoTableRow_ThrowsUnavailable` và `Quote_GhnDownAndReceiverProvinceOutsideRegionTable_ThrowsUnavailable`.
- [x] GHN từ chối yêu cầu thì không dùng phí đã lưu hay phí dự phòng
  `QuoteFallbackTests.Quote_GhnRejects_ThrowsRejectedEvenWithCachedFeeAndEnabledTable` có phí đã lưu và bảng đang bật, vẫn ném `SHIPPING_REJECTED`.
- [x] Phí đã lưu và phí dự phòng không có ngày giao dự kiến
  Các test `Cached` và `Fallback` của `QuoteFallbackTests` kiểm `ExpectedDeliveryDate` là `null`, dù GHN giả vẫn trả ngày giao khi báo phí thành công.
- [x] Đổi địa chỉ lấy hàng thì phí đã lưu theo địa chỉ cũ không còn khớp khóa
  `QuoteFallbackTests.Quote_GhnDownAfterPickupWardChanged_DoesNotUseCachedFee`.
- [x] Dòng phí đã lưu quá hạn bị xóa, để bảng không lớn mãi
  `QuoteCacheStoreTests.Save_DeletesRowsOlderThanOrExactly24Hours_KeepsFreshRows` kiểm dòng 25 giờ và đúng 24 giờ bị xóa, dòng 23:59:59 còn. Migration có chỉ mục `shipping_quote_cache_savedAt_idx`.
- [x] Mỗi lần dùng phí đã lưu hoặc phí dự phòng ghi log cảnh báo, không có dữ liệu khách
  `QuoteFallbackTests.Quote_GhnDownUsingCachedFee_LogsOneWarningWithoutAddressData` và `Quote_GhnDownUsingFallbackTable_LogsOneWarningWithoutAddressData` kiểm đúng một log cảnh báo, chỉ có nguồn phí, tuyến và khối lượng tính cước.
- [x] `FakeGhnClient` có cách giả lập GHN không phản hồi lúc báo phí, để E2E dùng được
  `FakeGhnClient.SetFailure` có sẵn và task thêm `ClearFailure`. Route `PUT` và `DELETE /api/e2e/ghn/failures/calculate-fee` do `GhnE2eFailureRouteTests` kiểm: bật lỗi thì báo phí trả `SHIPPING_UNAVAILABLE` hoặc phí dự phòng, tắt lỗi thì trả phí GHN, host thường và host có `GHN_TOKEN` trả 404.
- [x] `dotnet build` và `dotnet test` sạch
  Reviewer chạy ngày 2026-10-19 trong `apps/backend`, sau lần dọn review 1. `dotnet build` 0 lỗi, 0 cảnh báo. `Pickory.UnitTests` 1696 đạt. `Pickory.ArchitectureTests` 14 đạt. `Pickory.IntegrationTests` toàn bộ: 2235 đạt, 12 bỏ qua, 1 lỗi sau 45 phút 13 giây. Test lỗi là `AdminStaffTests.TwoLastAdmins_DemotingEachOtherConcurrentlyKeepsOneAdmin` của `identity`, không thuộc SHIP-14. Test này lỗi khi chạy toàn bộ và đạt cả 3 lần chạy riêng. Reviewer chạy E2E toàn bộ một lần sau UI-S3, lần chạy đó phủ cả phần báo phí của SHIP-14: 112 đạt, 0 lỗi.
**Kiểm tra:** test tích hợp báo phí với client giả lỗi và `TimeProvider` giả
**Phụ thuộc:** SHIP-13 · **Quy mô:** M

### UI-S3: Trang cấu hình phí dự phòng
**Mô tả:** Trang `/admin/shipping/fallback-fee` nằm cạnh trang cấu hình vận chuyển trong menu. Trang có công tắc bật hoặc tắt và 3 nhóm giá theo tuyến.
**Tiêu chí nghiệm thu:**
- [x] Menu có mục mới cạnh cấu hình vận chuyển. Chỉ tài khoản có quyền `ShippingSettingsManage` thấy mục này
  Mục "Phí ship dự phòng" nằm ngay sau "Vận chuyển". `admin-navigation.test.ts` (nhóm "mục menu Phí ship dự phòng") và `admin-layout.test.tsx` kiểm hiện với quyền `shipping-settings:manage`, ẩn với `support` và `sales`. `e2e/shipping-fallback-fee.spec.ts` mở trang qua menu. `e2e/staff-invite.spec.ts` có thêm nhãn vào danh sách ẩn của `sales`, builder chưa chạy spec này.
- [x] Mỗi tuyến có 3 ô: giá gốc, khối lượng gốc, giá mỗi 500 g tiếp theo. Ô tiền hiện đơn vị đồng, ô khối lượng hiện đơn vị gram
  `fallback-fee-page.test.tsx`: "có ba nhóm đúng thứ tự..." và "ô tiền hiện đơn vị đồng, ô khối lượng nêu đơn vị gram". E2E kiểm tên ba nhóm `fieldset`.
- [x] Trang có một dòng giải thích ngắn: phí này chỉ dùng khi GHN không phản hồi
  `fallback-fee-page.test.tsx`: "hiện tiêu đề, dòng giải thích". E2E kiểm câu "GHN không phản hồi lúc báo phí".
- [x] Trang có ví dụ tính phí cho một gói hàng mẫu, cập nhật theo giá đang nhập
  `fallback-fee-page.test.tsx` nhóm "ví dụ tính phí" (gói mẫu 1.500 g, nhập giá thì phí đổi, thiếu ô thì "Chưa đủ giá để tính") và `fallback-fee-calculator.test.ts` (cùng giá trị biên với `FallbackFeeCalculatorTests.cs`). E2E kiểm 25.000, 36.000 và 40.000 đồng.
- [x] Lỗi từ API hiện dưới đúng ô. Lưu xong trang báo đã lưu, và tải lại vẫn giữ giá trị
  `fallback-fee-page.test.tsx`: lỗi 400 và 422 hiện dưới đúng ô, câu "Đã lưu phí ship dự phòng.". E2E dùng backend thật: 422 khi bỏ một giá trong body, 400 khi khối lượng gốc là 0, rồi lưu và tải lại thấy đủ chín giá cùng công tắc bật.
- [x] Trang không tràn ngang ở 375 px. Console 0 lỗi và 0 cảnh báo
  E2E đo `scrollWidth` không lớn hơn `clientWidth` ở 375 px cho trang mới, trang có lỗi, trang đã lưu và trang tải lại. Console 0 lỗi và 0 cảnh báo, chỉ bỏ hai dòng của request 400 và 422 cố ý. Ảnh chụp ở `ui-s3/` trong thư mục trao đổi.
- [x] E2E: admin bật phí dự phòng, nhập giá, lưu, tải lại thấy đúng giá. Spec xanh ở chế độ ẩn và `--headed`
  `e2e/shipping-fallback-fee.spec.ts` cùng `shipping-settings.spec.ts`: 2 đạt ở chế độ ẩn và 2 đạt ở `--headed`. Spec lưu lại trạng thái tắt và trống khi kết thúc. Reviewer chạy `pnpm test:e2e --headed` toàn bộ ngày 2026-10-19, sau lần dọn: 112 đạt, 0 lỗi, 12,9 phút, có cả `shipping-fallback-fee.spec.ts` và `staff-invite.spec.ts`.
- [x] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test`) sạch
  `pnpm lint`, `pnpm typecheck` và `pnpm test` (130 file, 1674 test) đạt. `pnpm build` đạt.
**Kiểm tra:** Vitest; E2E
**Phụ thuộc:** SHIP-13 · **Quy mô:** M
