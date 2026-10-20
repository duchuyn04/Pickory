# Task List: activity-tracking

Plan và quyết định thiết kế: `tasks/activity-tracking/plan.md`. Spec: `spec/SPEC-activity-tracking.md`. Quy ước chung: `spec/SPEC.md`, `tasks/dotnet-migration/plan.md`, `CLAUDE.md`.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Task back-office thêm endpoint mới vào `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Task UI theo `docs/design/design-system.md` và `tasks/design/plan.md`. Playwright kiểm console có 0 lỗi và 0 cảnh báo.
- Kích thước: XS 1 file, S 1-2, M 3-5, L 6-8. Số file không tính file test.

Ngày 2026-10-20 module chỉ còn đếm tổng theo ngày (xem "Thay đổi phạm vi ngày 2026-10-20" của plan). Task đã bỏ nằm ở cuối file.

---

## Phase 1: Nền tảng

### ACT-02: Hàm tính phễu
**Mô tả:** `FunnelCalculator` là hàm thuần, nhận tổng của ba chỉ số và trả phễu. Hàm không gọi DB.
**Tiêu chí nghiệm thu:**
- [x] Phễu có ba bước theo thứ tự: `product_view`, `add_to_cart`, `order_placed`
- [x] Mỗi bước có số lượt và tỉ lệ so với bước trước. Bước đầu có tỉ lệ `null`
- [x] Bước trước có 0 lượt thì tỉ lệ là `null`, không chia cho 0
- [x] Tỉ lệ có thể lớn hơn 1, ví dụ 3 lượt đặt đơn từ 2 lượt thêm giỏ. Hàm không cắt tỉ lệ về 1
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** XS
**File:** `apps/backend/src/Pickory.Modules.ActivityTracking/Metrics/FunnelCalculator.cs`

### ACT-04: Khung module, Contracts, schema
**Mô tả:** Tạo `Pickory.Modules.ActivityTracking` và `Pickory.Modules.ActivityTracking.Contracts`. Thêm `ActivityTrackingDbContext`, migration đầu tiên và `ActivityTrackingModule.cs`. Contracts có `IActivityMetrics`, `Funnel`, `FunnelStep`, `DateRange`. Module có lớp hằng số cho ba tên chỉ số.
**Tiêu chí nghiệm thu:**
- [x] DB có bảng `daily_counters` với cột `date`, `metric`, `value`. Khóa chính là `(date, metric)`
- [x] Không bảng hay cột nào của module chứa `anonymousId` hoặc `userId`
- [x] Migration chạy trên DB sạch. Lệnh `e2e:start` và fixture test tích hợp dựng được schema mới
- [x] `Pickory.ArchitectureTests` chặn `ActivityTracking` tham chiếu module khác ngoài project Contracts
- [x] Contracts không tham chiếu project Contracts khác
- [x] `Program.cs` chỉ gọi `AddActivityTrackingModule` và `MapActivityTrackingEndpoints`
**Kiểm tra:** build, test kiến trúc, test tích hợp schema
**Phụ thuộc:** không · **Quy mô:** M

### Checkpoint A1
- [x] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [x] Test kiến trúc chặn `ActivityTracking` tham chiếu module khác ngoài Contracts
- [ ] Review với người dùng

---

## Phase 2: Đếm

### ACT-06: API nhận lượt xem
**Mô tả:** `POST /api/events` công khai nhận một lượt xem với body `{ "type": "product_view" }`. Endpoint cộng 1 vào bộ đếm `product_view` của hôm nay bằng một câu lệnh upsert, rồi trả 204.
**Tiêu chí nghiệm thu:**
- [ ] Body hợp lệ trả 204. Bộ đếm `product_view` của hôm nay tăng đúng 1
- [ ] `type` khác `product_view`, thiếu `type` hoặc body không phải JSON: API trả 400 `VALIDATION_ERROR`, bộ đếm không đổi
- [ ] 20 request song song làm bộ đếm tăng đúng 20
- [ ] `Actor.Kind` là `Staff`: API trả 204, bộ đếm không đổi
- [ ] User agent của crawler trong danh sách: API trả 204, bộ đếm không đổi
- [ ] Request thứ 61 trong 1 phút của cùng `AnonymousId` trả 429 `TOO_MANY_REQUESTS`
- [ ] Ngày của bộ đếm theo giờ `Asia/Ho_Chi_Minh`. Test biên 23:59 và 00:00 bằng `TimeProvider` giả
**Kiểm tra:** test tích hợp
**Phụ thuộc:** ACT-04 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.ActivityTracking/Features/CollectEvents/`, `apps/backend/src/Pickory.Modules.ActivityTracking/Counters/DailyCounterWriter.cs`

### ACT-UI1: Trang sản phẩm gửi lượt xem
**Mô tả:** `trackProductView()` gửi `POST /api/events` bằng `fetch` với `keepalive`. Trang sản phẩm gọi hàm này một lần sau khi tải xong sản phẩm.
**Tiêu chí nghiệm thu:**
- [ ] Mở `/p/:slug` gửi đúng 1 request `POST /api/events` với body `{ "type": "product_view" }`
- [ ] React StrictMode chạy effect hai lần ở bản dev: trang vẫn chỉ gửi 1 request
- [ ] Đổi biến thể trên cùng trang không gửi thêm request
- [ ] Chuyển sang sản phẩm khác qua link nội bộ gửi thêm đúng 1 request
- [ ] Sản phẩm không tồn tại (trang 404) không gửi request
- [ ] API lỗi hoặc mất mạng: trang không hiện lỗi cho khách và vẫn dùng được
- [ ] Playwright: console 0 lỗi và 0 cảnh báo trên trang sản phẩm
**Kiểm tra:** Vitest; Playwright kiểm request
**Phụ thuộc:** ACT-06 · **Quy mô:** S
**File:** `apps/frontend/src/features/activity-tracking/track-product-view.ts`, `apps/frontend/src/features/catalog/customer/product-page.tsx`, test trong `apps/frontend/test/unit/features/activity-tracking/`

### ACT-07: Nghe sự kiện thêm giỏ và đặt đơn
**Mô tả:** Hai handler nghe sự kiện của `commerce` và cộng bộ đếm qua `DailyCounterWriter` của ACT-06.
**Tiêu chí nghiệm thu:**
- [ ] `CartItemAddedEvent` cộng 1 vào `add_to_cart`, kể cả khi `Quantity` lớn hơn 1
- [ ] Thêm SKU đã có trong giỏ cũng cộng 1
- [ ] `OrderPlacedEvent` của đơn COD cộng 1 vào `order_placed`
- [ ] `OrderPlacedEvent` của đơn SePay cộng 1 vào `order_placed`
- [ ] `OrderPaidEvent` không đổi bộ đếm nào
- [ ] Ghi bộ đếm lỗi: luồng thêm giỏ và đặt đơn của `commerce` vẫn thành công
**Kiểm tra:** test tích hợp gọi API giỏ và đặt đơn thật, rồi đọc `daily_counters`
**Phụ thuộc:** ACT-04, ACT-06 · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.ActivityTracking/Counters/{CartItemAddedCounterHandler.cs, OrderPlacedCounterHandler.cs}`

### Checkpoint A2
- [ ] Xem sản phẩm, thêm giỏ, đặt đơn: mỗi bộ đếm của hôm nay tăng đúng 1
- [ ] Nhân viên và crawler không làm bộ đếm tăng
- [ ] Review với người dùng

---

## Phase 3: Phễu

### ACT-11: `IActivityMetrics` và API phễu
**Mô tả:** `IActivityMetrics.GetFunnelAsync` đọc `daily_counters` trong khoảng ngày và gọi `FunnelCalculator`. `GET /api/admin/analytics/funnel` yêu cầu quyền `reports:read`. Tham số `from` và `to` là ngày theo giờ `Asia/Ho_Chi_Minh`, tính cả hai đầu.
**Tiêu chí nghiệm thu:**
- [ ] Bộ dữ liệu dựng sẵn cho ra đúng số lượt và tỉ lệ đã tính tay
- [ ] Ngày nằm ngoài khoảng `from` đến `to` không được cộng vào phễu
- [ ] Khoảng không có dữ liệu trả ba bước với 0 lượt và tỉ lệ `null`
- [ ] Thiếu `from` hoặc `to`, `from` sau `to`, hoặc khoảng hơn 366 ngày: API trả 400 `VALIDATION_ERROR`
- [ ] Người thiếu `reports:read` nhận 403. Endpoint có trong ma trận quyền
- [ ] Response theo dạng `{ data }`
**Kiểm tra:** test tích hợp với bộ dữ liệu dựng sẵn; ma trận quyền
**Phụ thuộc:** ACT-02, ACT-04 · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.ActivityTracking/Features/AdminAnalytics/`, `apps/backend/src/Pickory.Modules.ActivityTracking/Metrics/ActivityMetrics.cs`

### Checkpoint A3
- [ ] API phễu trả đúng số và tỉ lệ với bộ dữ liệu dựng sẵn
- [ ] Review với người dùng

---

## Phase 4: E2E và nghiệm thu

### ACT-E2E1: Luồng E2E của activity-tracking
**Mô tả:** Playwright chạy luồng đếm trong `apps/frontend/e2e/activity-tracking.spec.ts`. Spec tự tạo sản phẩm qua API admin, như spec checkout của `commerce`.
**Tiêu chí nghiệm thu:**
- [ ] Spec tạo 1 sản phẩm đang bán qua API admin, rồi đọc phễu hôm nay bằng `GET /api/admin/analytics/funnel`
- [ ] Khách bấm đồng ý trên banner, xem sản phẩm, thêm giỏ, đặt đơn COD
- [ ] Phễu hôm nay tăng đúng 1 ở mỗi bước xem, thêm giỏ, đặt đơn
- [ ] Khách chưa đồng ý làm cùng luồng: phễu vẫn tăng đúng 1 ở mỗi bước
- [ ] Console 0 lỗi và 0 cảnh báo trong cả luồng
- [ ] Suite đạt ở chế độ có giao diện và chế độ ẩn
**Kiểm tra:** `pnpm test:e2e`
**Phụ thuộc:** ACT-UI1, ACT-07, ACT-11 · **Quy mô:** M

### ACT-13: Nghiệm thu activity-tracking
**Mô tả:** Kiểm toàn module trước khi sang `support-desk`.
**Tiêu chí nghiệm thu:**
- [ ] Ma trận quyền có mọi endpoint back-office của `activity-tracking`
- [ ] Test tích hợp đọc schema: không bảng hay cột nào của module chứa `anonymousId` hoặc `userId`
- [ ] Coverage dòng của `Pickory.Modules.ActivityTracking` ít nhất 80%, gộp unit và tích hợp, không tính `Persistence/Migrations/`
- [ ] Mọi dòng Traceability trong plan đạt hoặc ghi rõ lý do còn mở
**Kiểm tra:** `dotnet test`, script gộp coverage
**Phụ thuộc:** ACT-E2E1 · **Quy mô:** S

## Checkpoint cuối
- [ ] Mọi dòng Traceability đạt
- [ ] Coverage dòng của `Pickory.Modules.ActivityTracking` ít nhất 80%, không tính `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `support-desk`

---

## Task đã bỏ

Không làm các task dưới đây. Mã task giữ lại để không dùng lại cho việc khác.

| Task | Ngày bỏ | Nội dung | Lý do |
|---|---|---|---|
| ACT-01 | 2026-10-18 | Logic thuần của hồ sơ sở thích | Không còn tính năng nào đọc hồ sơ sở thích |
| ACT-03 | 2026-10-18 | Bản tóm tắt hồ sơ cho prompt AI | Chatbot `shopping-assistant` đã bỏ |
| ACT-09 | 2026-10-18 | Job hồ sơ và `IProfileReader` | Hồ sơ sở thích đã bỏ |
| ACT-10 | 2026-10-18 | `GET /api/me/recently-viewed` và `GET /api/me/interests` | Hồ sơ sở thích đã bỏ |
| ACT-UI2 | 2026-10-18 | Khối "Đã xem gần đây" | Hồ sơ sở thích đã bỏ |
| ACT-05 | 2026-10-20 | `ConsentChangedEvent` và `IConsentReader` ở `identity` | Module không kiểm tra đồng ý |
| ACT-08 | 2026-10-20 | Xóa dữ liệu, gắn sự kiện khi đăng nhập, job xóa sau 12 tháng | Bảng `activity_events` đã bỏ |
| ACT-UI3 | 2026-10-20 | Nút "Xóa lịch sử" ở `/privacy` | Module không lưu lịch sử theo khách |
| ACT-12 | 2026-10-20 | Lệnh đo hiệu năng `activity:benchmark` | Web demo chưa có vấn đề hiệu năng đo được |
