# Task List: activity-tracking

Plan và quyết định thiết kế: `tasks/activity-tracking/plan.md`. Spec: `spec/SPEC-activity-tracking.md`. Quy ước chung: `spec/SPEC.md`, `tasks/dotnet-migration/plan.md`, `CLAUDE.md`.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Task back-office thêm endpoint mới vào `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Task UI theo `docs/design/design-system.md` và `tasks/design/plan.md`. Playwright kiểm console có 0 lỗi và 0 cảnh báo.
- Kích thước: XS 1 file, S 1-2, M 3-5, L 6-8. Số file không tính file test.

---

## Phase 1: Nền tảng

### ACT-01: Logic thuần của hồ sơ sở thích
**Mô tả:** Các hàm thuần tính hồ sơ, không gọi DB và không gọi HTTP. Hàm nhận `TimeProvider` để test cố định thời gian.
- `InterestScoring`: trọng số theo loại sự kiện và bán rã 30 ngày.
- `PriceRangeCalculator`: tầm giá theo danh mục bằng phân vị 25 đến 75.
- `AttributeInterest`: thuộc tính quan tâm từ thuộc tính `Filterable` của sản phẩm đã xem và bộ lọc đã dùng.
- `ProfileMerger`: gộp hồ sơ `anon:<id>` vào `user:<id>`.

**Tiêu chí nghiệm thu:**
- [ ] Trọng số đúng: xem 1, xem từ gợi ý 1, thêm giỏ 3, mua 5, bỏ giỏ trừ 1
- [ ] Sự kiện 30 ngày trước còn đúng một nửa trọng số. Sự kiện 60 ngày trước còn một phần tư
- [ ] Top 5 danh mục và top 5 thương hiệu sắp theo điểm giảm dần. Điểm bằng nhau thì sắp theo id để kết quả ổn định
- [ ] Tầm giá bằng phân vị 25 và 75 của giá đã xem và đã mua trong danh mục. Danh mục có 1 giá thì `min` bằng `max`
- [ ] Chỉ thuộc tính `Filterable` vào danh sách thuộc tính quan tâm
- [ ] `recentlyViewed` giữ 20 `productId` gần nhất, không trùng
- [ ] Gộp hồ sơ cộng điểm hai phía, nối `recentlyViewed` theo thời gian và vẫn giữ tối đa 20
- [ ] Gộp với một phía rỗng trả đúng phía còn lại
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.ActivityTracking/Profiles/{InterestScoring.cs, PriceRangeCalculator.cs, AttributeInterest.cs, ProfileMerger.cs}`, test trong `apps/backend/tests/Pickory.UnitTests/Modules/ActivityTracking/`

### ACT-02: Logic thuần của số đo
**Mô tả:** Các hàm thuần tính CTR, quy chuyển đổi và phễu từ danh sách sự kiện và bộ đếm trong bộ nhớ.
**Tiêu chí nghiệm thu:**
- [ ] CTR theo `source` bằng số `rec_click` chia số sản phẩm trong các `rec_impression`
- [ ] `source` có 0 lượt hiển thị trả CTR `null`, không chia cho 0
- [ ] Lần mua được quy cho `source` của lần click gần nhất vào sản phẩm đó trong 7 ngày trước khi mua
- [ ] Click cách lần mua hơn 7 ngày không được quy. Click sau lần mua không được quy
- [ ] Số đo tách theo `variant` khi sự kiện có trường này
- [ ] Phễu đếm xem sản phẩm, thêm giỏ, mua, tách nhóm có cá nhân hóa và không cá nhân hóa
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.ActivityTracking/Metrics/{ClickThroughRate.cs, ConversionAttribution.cs, FunnelCalculator.cs}`

### ACT-03: Bản tóm tắt hồ sơ
**Mô tả:** `ProfileSummaryWriter` dựng câu tóm tắt tiếng Việt từ hồ sơ, theo ví dụ trong spec. Câu chỉ chứa tên danh mục, tên thương hiệu, tầm giá, thuộc tính và tên sản phẩm. Kết quả đi qua `PiiRedactor` lần cuối.
**Tiêu chí nghiệm thu:**
- [ ] Hồ sơ mẫu ra câu có dạng "Quan tâm: Laptop gaming (nhiều nhất), Tai nghe. Thương hiệu hay xem: ASUS, MSI."
- [ ] Giá viết dạng "25 đến 35 triệu"
- [ ] Hồ sơ có câu tìm chứa `0912 345 678` và `a@b.vn`: kết quả không chứa hai giá trị này
- [ ] Kết quả không chứa `userId`, `anonymousId`, mã đơn hay tiền tố `user:` và `anon:`
- [ ] Hồ sơ rỗng trả `null`
**Kiểm tra:** unit test
**Phụ thuộc:** ACT-01 · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.ActivityTracking/Profiles/ProfileSummaryWriter.cs`

### ACT-04: Khung module, Contracts, schema
**Mô tả:** Tạo `Pickory.Modules.ActivityTracking` và `Pickory.Modules.ActivityTracking.Contracts`. Thêm `ActivityTrackingDbContext`, migration đầu tiên và `ActivityTrackingModule.cs`. Contracts có `IProfileReader`, `IActivityMetrics`, `IServerEventTracker`, `InterestProfile`, `TrackingSubject`, `FeatureMetrics`, `Funnel`, `DateRange`.
**Tiêu chí nghiệm thu:**
- [ ] Bảng `activity_events`, `interest_profiles`, `daily_counters` theo Data Model của spec
- [ ] `activity_events` có ba chỉ mục `(userId, occurredAt)`, `(anonymousId, occurredAt)`, `(type, occurredAt)`
- [ ] `interest_profiles.actorKey` và khóa `(date, metric, source, productId)` của `daily_counters` có ràng buộc unique
- [ ] Migration chạy trên DB sạch. Lệnh `e2e:start` và fixture test tích hợp dựng được schema mới
- [ ] `Pickory.ArchitectureTests` chặn `ActivityTracking` tham chiếu module khác ngoài project Contracts
- [ ] Contracts không tham chiếu project Contracts khác (quyết định 3 của plan)
- [ ] `Program.cs` chỉ gọi `AddActivityTrackingModule` và `MapActivityTrackingEndpoints`
**Kiểm tra:** build, test kiến trúc, test tích hợp schema
**Phụ thuộc:** không · **Quy mô:** M

### ACT-05: Sự kiện và cổng đọc đồng ý của identity
**Mô tả:** Thêm `ConsentChangedEvent` và `IConsentReader` vào `Pickory.Modules.Identity.Contracts`. `SaveConsentEndpoint` phát sự kiện sau khi lưu. Task này đổi Contracts của `identity`.
**Trạng thái:** chờ người dùng duyệt Câu hỏi mở 1 và 2 của plan. Không bắt đầu khi chưa có quyết định.
**Tiêu chí nghiệm thu:**
- [ ] `PUT /api/me/consent` phát đúng 1 `ConsentChangedEvent` sau khi commit, kèm `AnonymousId`, `UserId` và lựa chọn mới
- [ ] Lưu lỗi thì không phát sự kiện
- [ ] `IConsentReader` trả lựa chọn mới nhất theo `AnonymousId` hoặc `UserId`, giống cách actor middleware chọn
- [ ] Chưa có bản ghi thì `Personalization` là `false`
- [ ] Test của `identity` hiện có vẫn xanh
**Kiểm tra:** test tích hợp của identity
**Phụ thuộc:** ACT-04 · **Quy mô:** S

### Checkpoint A1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `ActivityTracking` tham chiếu module khác ngoài Contracts
- [ ] Review với người dùng

---

## Phase 2: Thu thập và quyền riêng tư

### ACT-06: Nhận lô sự kiện từ frontend
**Mô tả:** `POST /api/events` công khai nhận lô tối đa 50 sự kiện. Endpoint kiểm tra lô, đưa sự kiện hợp lệ vào hàng đợi `Channel`, rồi trả 202. `ActivityEventWriter` chạy nền ghi DB theo lô. Loại sự kiện từ frontend: `product_view`, `category_view`, `rec_impression`, `rec_click`, `chat_product_click`.
**Tiêu chí nghiệm thu:**
- [ ] Lô hợp lệ trả 202. Lô hơn 50 sự kiện hoặc body không phải JSON trả 400 `VALIDATION_ERROR`
- [ ] Lô có sự kiện sai: sự kiện đúng vẫn được ghi, sự kiện sai bị bỏ
- [ ] Sự kiện trỏ tới sản phẩm không có trong `catalog` bị bỏ. `ActivityEventWriter` kiểm tra bằng `ICatalogReader.GetProductsAsync`
- [ ] `source` ngoài danh sách cố định và `variant` ngoài `control`, `algo`, `algo_llm`, `none` bị bỏ
- [ ] Trường `anonymousId` và `userId` trong body bị bỏ qua. Giá trị lấy từ `Actor`
- [ ] Khách chưa đồng ý: 0 dòng `activity_events`, `daily_counters` vẫn tăng
- [ ] Khách đã đồng ý: dòng `activity_events` gắn `anonymousId` và `userId`
- [ ] `Actor.Kind` là `Staff`: trả 202, 0 dòng `activity_events`, bộ đếm không tăng
- [ ] User agent của crawler trong danh sách: trả 202, không ghi gì
- [ ] Lô thứ 11 trong 1 phút của cùng `AnonymousId` trả 429 `TOO_MANY_REQUESTS`
- [ ] Ngày của `daily_counters` tính theo giờ `Asia/Ho_Chi_Minh`. Test biên 23:59 và 00:00
- [ ] `product_view` của khách đồng ý cập nhật `recentlyViewed` trong hồ sơ ngay
**Kiểm tra:** test tích hợp; test gọi `ActivityEventWriter` trực tiếp
**Phụ thuộc:** ACT-04 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.ActivityTracking/Features/CollectEvents/`, `apps/backend/src/Pickory.Modules.ActivityTracking/Events/ActivityEventWriter.cs`

### ACT-UI1: Thư viện `tracker.ts` và hook `useImpression`
**Mô tả:** `apps/frontend/src/features/activity-tracking/tracker.ts` có hàm `track(event)`. Thư viện gom lô, gửi mỗi 5 giây, và gửi bằng `navigator.sendBeacon` khi rời trang. Hook `useImpression(ref, payload)` ghi `rec_impression`. Gắn `product_view` vào trang sản phẩm và `category_view` vào trang danh mục.
**Tiêu chí nghiệm thu:**
- [ ] Khách chưa đồng ý: `track` không gửi request nào
- [ ] Nhân viên đăng nhập back-office: `track` không gửi request nào
- [ ] Gom tối đa 50 sự kiện mỗi lô. Lô đầy thì gửi ngay, không chờ 5 giây
- [ ] Rời trang thì gửi phần còn lại bằng `sendBeacon`
- [ ] `useImpression` ghi đúng 1 `rec_impression` khi khối hiện ít nhất 50% trong khung nhìn trên 1 giây
- [ ] Khối hiện dưới 1 giây hoặc dưới 50% thì không ghi
- [ ] Mở `/p/:slug` gửi `product_view` có `productId`. Mở `/c/:slug` gửi `category_view` có bộ lọc đang áp
- [ ] Playwright: console 0 lỗi và 0 cảnh báo trên trang sản phẩm và trang danh mục
**Kiểm tra:** Vitest với `IntersectionObserver` giả và đồng hồ giả; Playwright kiểm request
**Phụ thuộc:** ACT-06 · **Quy mô:** M
**File:** `apps/frontend/src/features/activity-tracking/{tracker.ts, use-impression.ts}`, `apps/frontend/src/features/catalog/customer/{product-page.tsx, category-page.tsx}`, test trong `apps/frontend/test/unit/features/activity-tracking/`

### ACT-07: Sự kiện từ backend
**Mô tả:** `IServerEventTracker.TrackAsync` cho module khác ghi sự kiện backend, ví dụ `search` của `smart-search`. Module nghe `CartItemAddedEvent`, `CartItemRemovedEvent`, `OrderPlacedEvent`, `OrderPaidEvent` của `commerce`. Module đọc đồng ý qua `IConsentReader`.
**Tiêu chí nghiệm thu:**
- [ ] `CartItemAddedEvent` tạo 1 sự kiện `add_to_cart` có `skuId`, `quantity`, `source`, và cập nhật `cartProductIds` ngay
- [ ] `CartItemRemovedEvent` tạo 1 sự kiện `remove_from_cart` và cập nhật `cartProductIds` ngay
- [ ] `OrderPaidEvent` của đơn SePay tạo 1 sự kiện `purchase` có `orderId`, các `productId` và giá
- [ ] `OrderPlacedEvent` của đơn COD tạo 1 sự kiện `purchase`. `OrderPlacedEvent` của đơn SePay không tạo sự kiện
- [ ] Nhận cùng sự kiện đơn 2 lần: vẫn chỉ có 1 sự kiện `purchase` cho `orderId` đó
- [ ] Khách chưa đồng ý: 0 dòng `activity_events`, `daily_counters` tăng `add_to_cart.anonymous` hoặc `purchase.anonymous`
- [ ] `TrackAsync` với `search`: câu tìm lưu sau khi qua `PiiRedactor`. Câu chứa `0912 345 678` không lưu số đó
- [ ] Listener lỗi chỉ ghi log, không làm hỏng luồng của `commerce`
**Kiểm tra:** test tích hợp phát sự kiện qua `IEventDispatcher`
**Phụ thuộc:** ACT-05, ACT-06 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.ActivityTracking/Events/{ServerEventTracker.cs, CommerceEventHandlers.cs}`

### ACT-08: Xóa dữ liệu, gộp khi đăng nhập, thời gian lưu
**Mô tả:** `DELETE /api/me/activity` xóa dữ liệu của chính khách. Module nghe `ConsentChangedEvent`, `UserDeletedEvent`, `UserLinkedEvent`. Job hằng ngày xóa sự kiện cũ hơn 12 tháng.
**Tiêu chí nghiệm thu:**
- [ ] `DELETE /api/me/activity` trả 204. Sự kiện và hồ sơ của `anonymousId` và `userId` hiện tại bị xóa trong cùng transaction
- [ ] `ConsentChangedEvent` với `Personalization = false` xóa ngay sự kiện và hồ sơ của `anonymousId` và `userId` đó
- [ ] `UserDeletedEvent` xóa sự kiện và hồ sơ của `userId`
- [ ] Xóa dữ liệu không đổi `daily_counters`
- [ ] `UserLinkedEvent`: sự kiện của `anonymousId` được gắn `userId`. Hồ sơ `anon:<id>` gộp vào `user:<id>` rồi bị xóa
- [ ] Khách vãng lai chưa đồng ý đăng nhập: hồ sơ tài khoản không đổi
- [ ] Job xóa sự kiện có `occurredAt` cũ hơn 12 tháng. Sự kiện 11 tháng vẫn còn
- [ ] Chu kỳ job xóa là 1 ngày
**Kiểm tra:** test tích hợp; test gọi processor trực tiếp
**Phụ thuộc:** ACT-05, ACT-07 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.ActivityTracking/Features/DeleteMyActivity/`, `apps/backend/src/Pickory.Modules.ActivityTracking/Privacy/`, `apps/backend/src/Pickory.Modules.ActivityTracking/Retention/`

### Checkpoint A2
- [ ] Khách chưa đồng ý duyệt web: không có dòng `activity_events` gắn với họ, bộ đếm vẫn tăng
- [ ] Rút đồng ý hoặc xóa lịch sử: sự kiện và hồ sơ bị xóa ngay
- [ ] Review với người dùng

---

## Phase 3: Hồ sơ sở thích

### ACT-09: Job hồ sơ và `IProfileReader`
**Mô tả:** Job chạy mỗi 5 phút, tính lại hồ sơ cho khách có sự kiện mới từ lần chạy trước. Job đọc dữ liệu sản phẩm qua `ICatalogReader`. `IProfileReader.GetProfileAsync` và `GetProfileSummaryAsync` đọc hồ sơ bằng 1 truy vấn.
**Tiêu chí nghiệm thu:**
- [ ] Khách đồng ý xem 3 sản phẩm cùng danh mục: sau 1 lần chạy job, danh mục đó đứng đầu `categories`
- [ ] Job chỉ tính lại hồ sơ của khách có sự kiện mới. Khách không có sự kiện mới giữ nguyên `updatedAt`
- [ ] Job giữ dữ liệu sản phẩm trong bộ nhớ của một lần chạy. Mỗi sản phẩm chỉ được đọc 1 lần trong một lần chạy
- [ ] Sản phẩm không còn trong `catalog` bị bỏ khỏi điểm, không làm job lỗi
- [ ] Chu kỳ job là 5 phút
- [ ] `GetProfileAsync` trả `null` khi `TrackingSubject.Personalization` là `false` hoặc chưa có dữ liệu
- [ ] `GetProfileSummaryAsync` trả `null` khi chưa đồng ý. Kết quả không chứa SĐT, email, tên hay id
**Kiểm tra:** test tích hợp gọi processor trực tiếp; test tích hợp `IProfileReader`
**Phụ thuộc:** ACT-01, ACT-03, ACT-08 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.ActivityTracking/Profiles/{ProfileRebuildProcessor.cs, ProfileRebuildWorker.cs, ProfileReader.cs}`

### ACT-10: API hồ sơ cho khách
**Mô tả:** `GET /api/me/recently-viewed` trả sản phẩm đã xem gần đây. `GET /api/me/interests` trả hồ sơ dạng dễ đọc, gồm tên danh mục, tên thương hiệu, tầm giá, thuộc tính.
**Tiêu chí nghiệm thu:**
- [ ] Khách chưa đồng ý: cả hai route trả 200 với danh sách rỗng
- [ ] `recently-viewed` trả tối đa 20 `ProductSummary` theo thứ tự xem mới nhất, lấy qua `ICatalogReader.GetProductsAsync`
- [ ] Sản phẩm đã ẩn hoặc đã xóa không có trong kết quả
- [ ] `interests` trả tên danh mục và tên thương hiệu, không trả `actorKey` hay id người dùng
- [ ] Response theo dạng `{ data }`
**Kiểm tra:** test tích hợp
**Phụ thuộc:** ACT-09 · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.ActivityTracking/Features/{RecentlyViewed/, MyInterests/}`

### ACT-UI2: Khối "Đã xem gần đây"
**Mô tả:** Component `RecentlyViewedRail` trong `apps/frontend/src/features/activity-tracking/`, đặt ở trang sản phẩm `/p/:slug`. Khối dùng thẻ sản phẩm của catalog.
**Tiêu chí nghiệm thu:**
- [ ] Danh sách rỗng hoặc API lỗi thì ẩn cả khối, không hiện lỗi cho khách
- [ ] Không hiện sản phẩm đang xem trong khối
- [ ] Hiện khung xương khi đang tải
- [ ] Dải sản phẩm cuộn ngang dùng được bằng bàn phím. Khối có tiêu đề làm nhãn
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright: console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright và ảnh chụp
**Phụ thuộc:** ACT-10, ACT-UI1 · **Quy mô:** S
**File:** `apps/frontend/src/features/activity-tracking/{recently-viewed-rail.tsx, use-recently-viewed.ts}`

### ACT-UI3: Mục sở thích trong trang `/privacy`
**Mô tả:** Mở rộng `privacy-settings.tsx` của identity. Thêm phần "Hệ thống đang hiểu bạn thích gì" và nút "Xóa lịch sử". Không dựng trang mới.
**Tiêu chí nghiệm thu:**
- [ ] Khách đã đồng ý thấy danh mục, thương hiệu, tầm giá, thuộc tính quan tâm
- [ ] Khách chưa đồng ý thấy câu giải thích và link bật cá nhân hóa, không thấy dữ liệu
- [ ] Bấm "Xóa lịch sử" mở hộp xác nhận. Xác nhận thì gọi `DELETE /api/me/activity`, rồi phần sở thích hiện trống
- [ ] Lỗi API hiện thông báo bằng `ErrorAlert`
- [ ] Hộp xác nhận dùng được bằng bàn phím, có `label` cho nút
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang
- [ ] Playwright: console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright và ảnh chụp
**Phụ thuộc:** ACT-10 · **Quy mô:** M
**File:** `apps/frontend/src/features/identity/customer/privacy-settings.tsx`, `apps/frontend/src/features/activity-tracking/{interest-summary.tsx, use-my-interests.ts}`

### Checkpoint A3
- [ ] Khách đồng ý xem 3 sản phẩm cùng danh mục: hồ sơ có danh mục đó ở vị trí đầu trong 5 phút
- [ ] Trang `/privacy` hiện sở thích và xóa được lịch sử
- [ ] Review với người dùng

---

## Phase 4: Đo hiệu quả

### ACT-11: `IActivityMetrics` và API phân tích
**Mô tả:** `IActivityMetrics.GetFeatureMetricsAsync` và `GetFunnelAsync` cho `admin-console`. `GET /api/admin/analytics/features` và `GET /api/admin/analytics/funnel` với quyền `reports:read`. Tham số `from` và `to` là ngày theo giờ `Asia/Ho_Chi_Minh`.
**Tiêu chí nghiệm thu:**
- [ ] Bộ dữ liệu dựng sẵn cho ra đúng CTR và tỉ lệ chuyển đổi đã tính tay, theo từng `source`
- [ ] Số đo tách theo `variant` cho `recommendations.for-you`
- [ ] CTR toàn cục đọc `daily_counters`, gồm cả khách không đồng ý
- [ ] Quy chuyển đổi chỉ tính trên sự kiện của khách đã đồng ý
- [ ] Phễu so sánh nhóm có cá nhân hóa và nhóm không cá nhân hóa
- [ ] Thiếu `from` hoặc `to`, `from` sau `to`, hoặc khoảng hơn 366 ngày trả 400 `VALIDATION_ERROR`
- [ ] Người thiếu `reports:read` nhận 403. Hai endpoint có trong ma trận quyền
**Kiểm tra:** test tích hợp với bộ dữ liệu dựng sẵn; ma trận quyền
**Phụ thuộc:** ACT-02, ACT-09 · **Quy mô:** M
**File:** `apps/backend/src/Pickory.Modules.ActivityTracking/Features/AdminAnalytics/`, `apps/backend/src/Pickory.Modules.ActivityTracking/Metrics/ActivityMetrics.cs`

### ACT-12: Lệnh đo hiệu năng `activity:benchmark`
**Mô tả:** Lệnh `dotnet run --project apps/backend/src/Pickory.Api -- activity:benchmark` gửi 100 lô mỗi giây trong 1 phút tới `POST /api/events`. Lệnh in p50, p95 và số phản hồi theo mã HTTP. Lệnh theo mẫu `catalog:benchmark`.
**Tiêu chí nghiệm thu:**
- [ ] Lệnh từ chối chạy khi tên DB trong `DATABASE_URL` không chứa `bench`
- [ ] Mỗi lô dùng `AnonymousId` riêng. Output có 0 phản hồi 429
- [ ] Output ghi p95 dưới 100 ms trên máy dev
- [ ] Báo cáo task có dòng lệnh đề xuất cho mục Commands của `spec/SPEC.md`
**Kiểm tra:** chạy lệnh, lưu output vào báo cáo task
**Phụ thuộc:** ACT-06 · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Api/Benchmark/ActivityBenchmark.cs`

### Checkpoint A4
- [ ] API phân tích trả đúng CTR và chuyển đổi theo `source` và `variant` với bộ dữ liệu dựng sẵn
- [ ] `POST /api/events` p95 dưới 100 ms ở 100 lô mỗi giây
- [ ] Review với người dùng

---

## Phase 5: E2E và nghiệm thu

### ACT-E2E1: Luồng E2E của activity-tracking
**Mô tả:** Playwright chạy luồng của spec trong `apps/frontend/e2e/activity-tracking-profile.spec.ts`. Test gọi endpoint chỉ có ở môi trường E2E để chạy job hồ sơ ngay, không chờ 5 phút. Seed hiện chưa có danh mục "Laptop gaming", xem Câu hỏi mở 8 của plan.
**Tiêu chí nghiệm thu:**
- [ ] Khách bấm đồng ý trên banner
- [ ] Khách xem 3 laptop gaming của dữ liệu seed
- [ ] Trang `/privacy` hiện "Laptop gaming" trong sở thích
- [ ] Khách bấm "Xóa lịch sử" và xác nhận. Phần sở thích hiện trống
- [ ] Khách chưa đồng ý xem 3 sản phẩm: trang `/privacy` không có dữ liệu sở thích
- [ ] Console 0 lỗi và 0 cảnh báo trong cả luồng
- [ ] Suite đạt ở chế độ có giao diện và chế độ ẩn
**Kiểm tra:** `pnpm test:e2e`
**Phụ thuộc:** ACT-UI1, ACT-UI3 · **Quy mô:** M

### ACT-13: Nghiệm thu activity-tracking
**Mô tả:** Kiểm toàn module trước khi sang `smart-search`.
**Tiêu chí nghiệm thu:**
- [ ] Ma trận quyền có mọi endpoint back-office của `activity-tracking`
- [ ] Test tích hợp: khách chưa đồng ý duyệt web qua `POST /api/events` và sự kiện `commerce`. Kết quả có 0 dòng `activity_events` gắn với họ
- [ ] Coverage dòng của `Pickory.Modules.ActivityTracking` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] Output `activity:benchmark` mới nhất có p95 dưới 100 ms
- [ ] Mọi dòng Traceability trong plan đạt hoặc ghi rõ lý do còn mở
**Kiểm tra:** `dotnet test`, script gộp coverage, output benchmark
**Phụ thuộc:** ACT-E2E1, ACT-11, ACT-12 · **Quy mô:** S

## Checkpoint cuối
- [ ] Mọi dòng Traceability đạt
- [ ] Coverage dòng của `Pickory.Modules.ActivityTracking` ít nhất 80%, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `smart-search`
