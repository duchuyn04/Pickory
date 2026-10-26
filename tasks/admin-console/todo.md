# Task List: admin-console

Plan và quyết định thiết kế: `tasks/admin-console/plan.md`. Spec: `spec/SPEC-admin-console.md`. Quy ước chung: `spec/SPEC.md`, `tasks/dotnet-migration/plan.md`, `CLAUDE.md`.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint && pnpm typecheck && pnpm test` trong `apps/frontend`.
- Task back-office thêm endpoint mới vào `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`.
- Task UI theo `docs/design/design-system.md`. Playwright kiểm console 0 lỗi và 0 cảnh báo.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Kích thước: XS 1 file, S 1-2, M 3-5, L 6-8. Số file không tính file test.

---

## Phase 1: Nền tảng

### ADM-01: Khoảng thời gian và độ chia
**Mô tả:** Hàm thuần `ReportRange` đọc khoảng thời gian (hôm nay, 7 ngày, 30 ngày, tháng này, một tháng chọn được, 6 tháng gần nhất, tùy chọn) và độ chia (ngày, tuần, tháng). Hàm trả biên UTC theo giờ `Asia/Ho_Chi_Minh` và danh sách nhóm thời gian. Ngày 2026-10-26 task thêm `month` và `6m` cho dashboard (quyết định 23 của plan).
**Tiêu chí nghiệm thu:**
- [x] Ngày bắt đầu lúc 00:00 giờ Việt Nam, tức 17:00 UTC của ngày trước
- [x] Đơn tạo lúc 23:59 và 00:00 giờ Việt Nam rơi vào hai ngày khác nhau
- [x] Tuần bắt đầu thứ Hai. Tháng theo lịch
- [x] Khoảng tùy chọn có `from` sau `to`, hoặc dài hơn 366 ngày, trả 400 `VALIDATION_ERROR`
  - HTTP 400 kiểm ở test tích hợp của ADM-12
- [x] Giá trị `range` hoặc `granularity` lạ trả 400 `VALIDATION_ERROR`
  - HTTP 400 kiểm ở test tích hợp của ADM-12
- [x] `range=month&month=2026-09` trả tháng lịch 9/2026. `range=6m` trả 6 tháng lịch gần nhất, gồm tháng này
- [x] `range=month` thiếu `month`, hoặc `month` sai định dạng `YYYY-MM`, trả 400 `VALIDATION_ERROR`
  - HTTP 400 kiểm ở test tích hợp của ADM-12
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.AdminConsole/Reports/ReportRange.cs`, test trong `apps/backend/tests/Pickory.UnitTests/Modules/AdminConsole/`

### ADM-02: Kiểm định hai tỉ lệ
**Mô tả:** Hàm thuần `TwoProportionComparison` so hai nhóm thử nghiệm. Hàm trả chênh lệch, khoảng tin cậy 95% và trạng thái "chưa đủ dữ liệu" theo quyết định 10 của plan.
**Trạng thái:** đã bỏ ngày 2026-10-18. Pickory không còn chatbot hay gợi ý sản phẩm, nên không có nhóm thử nghiệm nào để so.
**Tiêu chí nghiệm thu:**
- [ ] Khoảng tin cậy khớp số tính tay trên ít nhất 3 bộ số mẫu, sai lệch dưới 0,0001
- [ ] Nhóm dưới 200 khách trả trạng thái "chưa đủ dữ liệu", không trả kết luận
- [ ] Nhóm dưới 30 lần chuyển đổi trả trạng thái "chưa đủ dữ liệu"
- [ ] Cỡ mẫu bằng 0 không ném lỗi chia cho 0
- [ ] Hàm nhận danh sách ngày bị đánh dấu hết ngân sách và loại các ngày đó trước khi tính
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.AdminConsole/Reports/TwoProportionComparison.cs`

### ADM-03: Ghi CSV
**Mô tả:** Lớp `CsvReportWriter` ghi bảng số liệu ra stream theo RFC 4180, dạng UTF-8 có BOM.
**Tiêu chí nghiệm thu:**
- [ ] 3 byte đầu của file là `EF BB BF`
- [ ] Ô có dấu phẩy, dấu ngoặc kép hoặc xuống dòng được bọc trong ngoặc kép. Dấu ngoặc kép bên trong được nhân đôi
- [ ] Chữ tiếng Việt có dấu đọc lại đúng từng ký tự
- [ ] Ô bắt đầu bằng `=`, `+`, `-` hoặc `@` được thêm dấu `'` ở đầu, để Excel không chạy công thức
- [ ] Lớp ghi từng dòng vào stream, không giữ cả file trong bộ nhớ
**Kiểm tra:** unit test
**Phụ thuộc:** không · **Quy mô:** S
**File:** `apps/backend/src/Pickory.Modules.AdminConsole/Exports/CsvReportWriter.cs`

### ADM-04: Khung module và schema
**Mô tả:** Tạo `Pickory.Modules.AdminConsole`, `AdminConsoleModule.cs`, `AdminConsoleDbContext` và migration đầu tiên cho các bảng `report_daily_*`.
**Tiêu chí nghiệm thu:**
- [ ] Bảng `report_daily_sales`, `report_daily_products`, `report_daily_support` theo Data Model của spec. Ngày 2026-10-18 bỏ `report_daily_ai`
- [ ] `report_daily_products` có thêm `product_name`, `brand_id`, `brand_name` (quyết định 24 của plan). `brand_id` cho phép rỗng
- [ ] Cột phân vị lưu histogram theo nhóm thời gian cố định (quyết định 5), không lưu một số p50 hoặc p90
- [ ] Tiền VND là `int`
- [ ] Migration chạy trên DB sạch. Lệnh `e2e:start` và fixture test tích hợp dựng được schema mới
- [ ] `Pickory.ArchitectureTests` chặn `AdminConsole` tham chiếu module khác ngoài project Contracts
- [ ] `Program.cs` chỉ gọi `AddAdminConsoleModule` và `MapAdminConsoleEndpoints`
**Kiểm tra:** build, test kiến trúc, test tích hợp schema
**Phụ thuộc:** không · **Quy mô:** M

### ADM-UI1: Đăng ký menu theo module
**Mô tả:** Mỗi module frontend có `features/<module-id>/admin/menu.ts` xuất mục menu của nó. `features/admin-console/` gom danh sách và chuyển `AdminLayout`, `admin-navigation.ts` từ `features/identity/admin/` sang (quyết định 14 và 15 của plan).
**Tiêu chí nghiệm thu:**
- [ ] `identity`, `catalog`, `shipping`, `commerce` có file `menu.ts` riêng. `ADMIN_NAV_GROUPS` không còn liệt kê mục của module khác
- [ ] Mục menu có trường `badge` tùy chọn, ví dụ `orders.pendingConfirmation`
- [ ] Nhãn, icon, đường dẫn, nhóm và thứ tự menu giữ như trước. Ảnh chụp sidebar 1280 px giống bản trước
- [ ] Mục thiếu quyền vẫn ẩn. Test của `filterNavGroupsByPermission` và `isNavItemActive` vẫn đạt
- [ ] `admin-layout.test.tsx` chuyển sang `test/unit/features/admin-console/` và đạt
- [ ] `admin-layout-mobile.spec.ts`, `admin-screens.spec.ts`, `staff-invite.spec.ts` đạt mà không đổi locator
- [ ] Playwright console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; E2E chế độ có giao diện và chế độ ẩn
**Phụ thuộc:** không · **Quy mô:** M

### Checkpoint A1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `AdminConsole` tham chiếu module khác ngoài Contracts
- [ ] Bộ E2E hiện có của khung admin vẫn đạt sau khi đổi nguồn menu
- [ ] Review với người dùng

---

## Phase 2: Khung back-office

### ADM-05: API badge
**Mô tả:** `GET /api/admin/badges` trả số việc cần làm theo quyền (quyết định 12 và 13 của plan). Mỗi nguồn là một lớp cài `IAdminBadgeSource` trong `admin-console`. Nguồn chỉ được đăng ký khi module nguồn đã có.
**Tiêu chí nghiệm thu:**
- [ ] Khách và người chưa đăng nhập nhận 403. Endpoint có trong ma trận quyền
- [ ] `sales` theo quyền mặc định nhận `orders.pendingConfirmation`, `payments.unmatched`, `payments.codUnreceived`, `catalog.lowStock`. Response không có khóa `support.*`
- [ ] `support` theo quyền mặc định nhận `support.waiting`, `support.callbacks`, `orders.pendingConfirmation`, `catalog.lowStock`. Response không có khóa `payments.*`
- [ ] Module không gọi cổng đọc của mục mà người gọi thiếu quyền (test dùng nguồn giả đếm số lần gọi)
- [ ] Số trả về khớp dữ liệu dựng sẵn trong DB test
- [ ] Một nguồn ném lỗi thì khóa đó là `null`, các khóa khác vẫn đúng, response là 200
**Kiểm tra:** test tích hợp với ba role và nguồn giả
**Phụ thuộc:** ADM-04 · **Quy mô:** M

### ADM-UI2: Badge trên menu
**Mô tả:** Menu hiện badge cạnh mục có trường `badge`. Frontend gọi `GET /api/admin/badges` mỗi 60 giây.
**Tiêu chí nghiệm thu:**
- [ ] Badge hiện số khi lớn hơn 0. Số bằng 0 hoặc `null` thì không hiện badge
- [ ] Badge có tên truy cập, ví dụ "3 đơn chờ xác nhận". Màu không phải cách duy nhất để hiểu badge
- [ ] Frontend làm mới sau 60 giây (test dùng đồng hồ giả)
- [ ] API lỗi thì menu vẫn hiện, không có thông báo lỗi
- [ ] Ở 375 px, badge hiện trong ngăn kéo menu và trang không tràn ngang
- [ ] Playwright console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp 1280 px và 375 px
**Phụ thuộc:** ADM-05, ADM-UI1 · **Quy mô:** S

### ADM-06: API trang chủ
**Mô tả:** `GET /api/admin/home` trả thẻ việc cần làm và số liệu nhanh theo quyền. Thẻ việc cần làm dùng cùng nguồn với badge. Số liệu nhanh (doanh thu hôm nay, số đơn hôm nay) đọc từ bảng tổng hợp khi người gọi có `reports:read`.
**Tiêu chí nghiệm thu:**
- [ ] Khách và người chưa đăng nhập nhận 403. Endpoint có trong ma trận quyền
- [ ] Mỗi thẻ có `key`, `label`, `count` và `link` tới màn nghiệp vụ
- [ ] `support` không nhận thẻ doanh thu, vì thiếu `reports:read`
- [ ] Bảng tổng hợp chưa có dữ liệu thì thẻ số liệu nhanh trả `0`, không lỗi
**Kiểm tra:** test tích hợp với ba role
**Phụ thuộc:** ADM-05 · **Quy mô:** S

### ADM-UI3: Trang chủ theo quyền
**Mô tả:** Thay `AdminDashboardPage` bằng trang chủ ghép thẻ từ `GET /api/admin/home`.
**Tiêu chí nghiệm thu:**
- [ ] `sales` thấy thẻ đơn chờ xác nhận, giao dịch SePay chưa khớp, đơn COD chưa nhận tiền, SKU sắp hết hàng
- [ ] `support` thấy thẻ hàng chờ hội thoại và yêu cầu gọi lại
- [ ] Bấm thẻ mở đúng màn nghiệp vụ
- [ ] Đang tải hiện khung xương. API lỗi hiện `ErrorAlert` có nút thử lại
- [ ] Ở 375 px, thẻ xếp một cột và trang không tràn ngang
- [ ] Playwright console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp 1280 px và 375 px
**Phụ thuộc:** ADM-06 · **Quy mô:** M

### ADM-UI4: Trang Cấu hình
**Mô tả:** Màn `/admin/settings` gom link tới cấu hình của từng module: bán hàng, vận chuyển, CSKH. Mỗi mục hiện theo quyền của trang đích.
**Tiêu chí nghiệm thu:**
- [ ] Mục chỉ hiện khi người dùng có quyền của trang đích, ví dụ vận chuyển cần `shipping-settings:manage`
- [ ] Mục của module chưa có route thì không hiện
- [ ] Người không có mục nào thấy thông báo "Bạn chưa có quyền cấu hình", không thấy trang trống
- [ ] Menu có mục "Cấu hình" khi người dùng có ít nhất một mục
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang. Playwright console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp
**Phụ thuộc:** ADM-UI1 · **Quy mô:** S

### Checkpoint A2
- [ ] Badge và trang chủ không lộ số của mục người gọi không có quyền
- [ ] Review với người dùng

---

## Phase 3: Báo cáo

### ADM-07: Cổng đọc chi phí AI
**Mô tả:** Thêm `IAiUsageReader` vào `Pickory.Modules.AiGateway.Contracts` sau khi người dùng duyệt Câu hỏi mở 3. Lớp cài đặt dùng lại `UsageSummaryReader` của `ai-gateway`.
**Trạng thái:** đã bỏ ngày 2026-10-18. Chatbot chuyển sang dự án riêng `chatbot-rag` và Pickory gỡ `ai-gateway`, nên Pickory không còn số liệu AI để báo cáo.
**Tiêu chí nghiệm thu:**
- [ ] `GetDailyUsageAsync` trả chi phí, số bước, số lỗi và histogram độ trễ theo ngày và feature
- [ ] Ngày tính theo giờ Việt Nam, giống `GET /api/admin/ai/usage`
- [ ] `GET /api/admin/ai/usage` trả cùng kết quả như trước (test hợp đồng hiện có vẫn đạt)
- [ ] Kết quả không chứa nội dung prompt hoặc `actorKey`
**Kiểm tra:** test tích hợp
**Phụ thuộc:** ADM-04 · **Quy mô:** S

### ADM-08: Job tổng hợp doanh thu và đơn hàng
**Mô tả:** Processor đọc `ICommerceReports` và ghi `report_daily_sales`, `report_daily_products`. Worker chạy mỗi giờ cho hôm nay, mỗi đêm cho hôm qua, và tính lại 35 ngày gần nhất (quyết định 7 của plan).
**Tiêu chí nghiệm thu:**
- [ ] Doanh thu ghi nhận bằng tổng `total` của đơn `paid` theo ngày thanh toán, trừ đơn `refunded`
- [ ] Giá trị đơn đặt không tính đơn hủy vì quá hạn thanh toán
- [ ] Tỉ lệ hủy tách theo lý do: khách hủy, shop hủy, quá hạn
- [ ] Mỗi dòng `report_daily_products` có tên sản phẩm và thương hiệu lấy từ `ICatalogReader`. Sản phẩm catalog không trả về có `brand_id` rỗng
- [ ] Số liệu khớp số tính tay trên bộ dữ liệu dựng sẵn, ghi trong fixture của test
- [ ] Chạy processor 2 lần cho cùng ngày cho cùng kết quả, không tạo dòng trùng
- [ ] Đơn chuyển `refunded` 10 ngày sau ngày thanh toán làm giảm doanh thu của ngày thanh toán sau lần chạy đêm kế tiếp
- [ ] `commerce` lỗi thì giữ dữ liệu cũ, ghi log, lần chạy sau thử lại
**Kiểm tra:** test tích hợp gọi processor trực tiếp
**Phụ thuộc:** ADM-01, ADM-04 · **Quy mô:** M

### ADM-09: Job tổng hợp AI
**Mô tả:** Processor đọc `IActivityMetrics`, cổng đọc của `shopping-assistant` (Câu hỏi mở 2) và `IAiUsageReader`. Processor ghi `report_daily_ai` và các bảng AI được duyệt. Ngày 2026-10-14 task bỏ nguồn `recommendations` và `smart-search`.
**Trạng thái:** đã bỏ ngày 2026-10-18. Chatbot chuyển sang dự án riêng `chatbot-rag` và Pickory gỡ `ai-gateway`, nên Pickory không còn số liệu AI để báo cáo.
**Tiêu chí nghiệm thu:**
- [ ] Mỗi dòng có đủ cột của `ReportDailyAi` trong Data Model của `spec/SPEC-admin-console.md`
- [ ] Ngày `IAiUsageReader` có lời gọi `blocked` do ngân sách thì có `budgetFlagged = true`
- [ ] Chi phí AI theo feature khớp `IAiUsageReader` trên dữ liệu dựng sẵn
- [ ] Số liệu chatbot khớp số tính tay trên dữ liệu dựng sẵn
- [ ] Một nguồn lỗi không chặn các nguồn khác. Nguồn lỗi giữ dữ liệu cũ và ghi log
**Kiểm tra:** test tích hợp với cổng đọc giả
**Phụ thuộc:** ADM-07, ADM-08 · **Quy mô:** M

### ADM-10: Job tổng hợp CSKH
**Mô tả:** Processor đọc `ISupportMetrics` và ghi `report_daily_support`.
**Tiêu chí nghiệm thu:**
- [ ] Mỗi dòng có các cột sau:
  - số hội thoại chuyển sang người và số hội thoại trong giờ;
  - số yêu cầu gọi lại;
  - tổng điểm và số lượt CSAT.
- [ ] Thời gian phản hồi đầu tiên và thời gian hỗ trợ một phiên lưu dạng histogram (quyết định 5 của plan)
- [ ] Số liệu khớp số tính tay trên dữ liệu dựng sẵn
- [ ] Ngày không có hội thoại ghi dòng có giá trị `0`, không bỏ trống ngày
**Kiểm tra:** test tích hợp với cổng đọc giả
**Phụ thuộc:** ADM-08 · **Quy mô:** S

### ADM-11: Lệnh `reports:rebuild`
**Mô tả:** Lệnh `dotnet run --project apps/backend/src/Pickory.Api -- reports:rebuild --from <ngày> --to <ngày>` tính lại mọi bảng tổng hợp cho khoảng ngày. Lệnh dùng chung processor với job.
**Tiêu chí nghiệm thu:**
- [ ] Kết quả của lệnh giống hệt kết quả của job trên cùng bộ dữ liệu
- [ ] Thiếu `--from` hoặc `--to`, hoặc ngày sai định dạng, thì lệnh in lỗi tiếng Việt và trả mã thoát khác 0
- [ ] Lệnh in số ngày đã tính và số ngày lỗi
**Kiểm tra:** test tích hợp gọi hàm của lệnh
**Phụ thuộc:** ADM-08, ADM-10 · **Quy mô:** S

### ADM-12: API báo cáo doanh thu
**Mô tả:** `GET /api/admin/reports/sales?range=&from=&to=&granularity=` đọc `report_daily_sales` và `report_daily_products`.
**Tiêu chí nghiệm thu:**
- [ ] Thiếu `reports:read` trả 403. Endpoint có trong ma trận quyền
- [ ] Tham số sai trả 400 `VALIDATION_ERROR`
- [ ] Response có các chỉ số sau:
  - doanh thu ghi nhận và giá trị đơn đặt;
  - số đơn theo trạng thái;
  - tỉ lệ hủy theo lý do;
  - giá trị trung bình đơn;
  - top sản phẩm và top danh mục;
  - tỉ lệ COD và SePay;
  - phí ship shop chịu.
- [ ] Response có chuỗi theo nhóm thời gian của `granularity` cho dashboard (quyết định 23 của plan). Mỗi nhóm có:
  - doanh thu ghi nhận;
  - số đơn đã thanh toán, tách COD và SePay;
  - số đơn hủy theo lý do.
- [ ] Response có doanh thu và số lượng theo thương hiệu. Mỗi thương hiệu có danh sách sản phẩm kèm tên, doanh thu, số lượng. Sản phẩm có `brand_id` rỗng gộp vào nhóm `null`
- [ ] Gộp tuần và tháng khớp số tính tay, kể cả biên nửa đêm giờ Việt Nam
- [ ] Route không đọc bảng của module khác (test đếm truy vấn chỉ thấy bảng `report_daily_*`)
**Kiểm tra:** test tích hợp
**Phụ thuộc:** ADM-08 · **Quy mô:** M

### ADM-UI5: Khung trang báo cáo và hai trang chi tiết dạng cột
**Mô tả:** Task này làm bộ chọn thời gian lưu trên URL theo quyết định 21 của plan. Task làm component biểu đồ cột có bảng số liệu. Task làm trang `/admin/reports/revenue` và `/admin/reports/orders` theo quyết định 23. Ngày 2026-10-26 hai trang này thay trang `/admin/reports/sales`. Người dùng đã duyệt Recharts.
**Tiêu chí nghiệm thu:**
- [ ] Trang doanh thu hiện cột doanh thu ghi nhận cho mỗi kỳ. Trang đơn hàng hiện cột chồng COD và SePay cho mỗi kỳ
- [ ] Mặc định là 6 tháng gần nhất, chia theo tháng
- [ ] Nhấn một cột thì trang chuyển sang khoảng của kỳ đó, chia theo ngày. URL đổi theo
- [ ] Đổi khoảng thời gian cập nhật URL. Mở lại URL cho cùng báo cáo
- [ ] Mỗi biểu đồ có bảng số liệu bên dưới, đọc được bằng trình đọc màn hình
- [ ] Bộ chọn thời gian dùng được bằng bàn phím, có `label`
- [ ] Giá hiện dạng VND, ngày hiện theo giờ Việt Nam
- [ ] Người thiếu `reports:read` không thấy menu báo cáo. Mở thẳng URL thấy trang không đủ quyền
- [ ] Ảnh chụp 1280 px và 768 px không tràn ngang. Ở 375 px trang không tràn ngang
- [ ] Playwright console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp 1280 px, 768 px và 375 px
**Phụ thuộc:** ADM-12, ADM-UI1 · **Quy mô:** M

### ADM-UI9: Phễu chuyển đổi trên trang doanh thu
**Mô tả:** Thêm khối phễu vào trang `/admin/reports/revenue`. Ngày 2026-10-26 trang này thay trang `/admin/reports/sales`. Khối đọc `GET /api/admin/analytics/funnel` của ACT-11 với khoảng thời gian của bộ chọn chung. Task thêm ngày 2026-10-18, vì `activity-tracking` chỉ còn phần đo chuyển đổi.
**Tiêu chí nghiệm thu:**
- [ ] Khối hiện số lượt từng bước của phễu: xem sản phẩm, thêm giỏ, đặt đơn
- [ ] Khối hiện tỉ lệ chuyển giữa hai bước liền nhau. Bước trước có 0 lượt thì khối hiện "Chưa có dữ liệu"
- [ ] Đổi khoảng thời gian thì khối tải lại theo khoảng mới
- [ ] Khối có bảng số liệu bên dưới biểu đồ, đọc được bằng trình đọc màn hình
- [ ] API phễu lỗi thì khối hiện thông báo lỗi. Các biểu đồ doanh thu vẫn hiện
- [ ] Ảnh chụp 1280 px và 375 px không tràn ngang. Playwright console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp 1280 px và 375 px
**Phụ thuộc:** ADM-UI5, ACT-11 · **Quy mô:** S

### ADM-UI10: Hai trang chi tiết dạng tròn
**Mô tả:** Trang `/admin/reports/brands` và `/admin/reports/cancellations` dùng khung của ADM-UI5 (quyết định 23 của plan). Task làm component biểu đồ tròn có bảng số liệu. Task thêm ngày 2026-10-26.
**Tiêu chí nghiệm thu:**
- [ ] Mặc định là tháng này. Bộ chọn tháng đổi `month` trên URL
- [ ] Trang thương hiệu hiện doanh thu theo thương hiệu. Quá 6 thương hiệu thì gộp phần còn lại vào lát "Khác"
- [ ] Nhấn lát thương hiệu thì bảng hiện sản phẩm của thương hiệu đó, kèm doanh thu và số lượng
- [ ] Trang đơn hủy hiện ba lát: khách hủy, shop hủy, quá hạn thanh toán
- [ ] Nhấn lát lý do thì bảng hiện số đơn hủy vì lý do đó theo từng ngày của tháng
- [ ] Lát chọn được bằng bàn phím. Mỗi lát có nhãn phần trăm, màu không phải cách duy nhất để phân biệt lát
- [ ] Một thương hiệu giữ cùng màu trên dashboard và trang chi tiết
- [ ] Tháng không có số liệu thì trang hiện "Chưa có dữ liệu trong tháng này", không vẽ biểu đồ rỗng
- [ ] Ảnh chụp 1280 px và 768 px không tràn ngang. Ở 375 px trang không tràn ngang
- [ ] Playwright console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp 1280 px, 768 px và 375 px
**Phụ thuộc:** ADM-UI5 · **Quy mô:** M

### ADM-UI11: Dashboard báo cáo
**Mô tả:** Trang `/admin/reports` có 4 thẻ biểu đồ theo quyết định 23 của plan. Mục menu Báo cáo mở trang này. Task thêm ngày 2026-10-26.
**Tiêu chí nghiệm thu:**
- [ ] Trang có 4 thẻ: doanh thu theo thương hiệu, đơn hủy theo lý do, doanh thu theo kỳ, số đơn đã thanh toán theo kỳ
- [ ] Hai thẻ tròn dùng tháng này. Hai thẻ cột dùng 6 tháng gần nhất, chia theo tháng
- [ ] Nhấn tiêu đề thẻ mở trang chi tiết của báo cáo đó với cùng khoảng thời gian
- [ ] Nhấn lát hoặc cột mở trang chi tiết đã chọn sẵn lát hoặc kỳ đó
- [ ] Mỗi thẻ có tổng của kỳ, ví dụ tổng doanh thu tháng này
- [ ] Một thẻ lỗi API thì thẻ đó hiện thông báo lỗi. Các thẻ khác vẫn hiện
- [ ] Người thiếu `reports:read` không thấy menu Báo cáo. Mở thẳng URL thấy trang không đủ quyền
- [ ] Ảnh chụp 1280 px và 768 px không tràn ngang. Ở 375 px các thẻ xếp một cột và trang không tràn ngang
- [ ] Playwright console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; E2E nhấn từng thẻ; ảnh chụp 1280 px, 768 px và 375 px
**Phụ thuộc:** ADM-UI5, ADM-UI10 · **Quy mô:** M

### ADM-13: API báo cáo AI
**Mô tả:** `GET /api/admin/reports/ai` trả số liệu chatbot và chi phí AI. Ngày 2026-10-14 task bỏ phần gợi ý, bảng so sánh nhóm và tìm kiếm, vì `recommendations` và `smart-search` đã bỏ.
**Trạng thái:** đã bỏ ngày 2026-10-18. Chatbot chuyển sang dự án riêng `chatbot-rag` và Pickory gỡ `ai-gateway`, nên Pickory không còn số liệu AI để báo cáo.
**Tiêu chí nghiệm thu:**
- [ ] Thiếu `reports:read` trả 403. Endpoint có trong ma trận quyền
- [ ] Response có đủ các chỉ số chatbot trong mục "Hiệu quả AI" của `spec/SPEC-admin-console.md`
- [ ] Kỳ có dưới 200 hội thoại hoặc dưới 30 lần chuyển đổi từ chat trả trạng thái "chưa đủ dữ liệu"
- [ ] Response liệt kê các ngày có `budgetFlagged = true`
- [ ] Response có chi phí AI theo ngày và feature, phần trăm ngân sách tháng, tỉ lệ lỗi, độ trễ p95
- [ ] Response có chi phí AI trên mỗi đơn được quy cho AI. Kỳ không có đơn quy cho AI trả `null`, không chia cho 0
**Kiểm tra:** test tích hợp với dữ liệu dựng sẵn
**Phụ thuộc:** ADM-09 · **Quy mô:** M

### ADM-UI6: Trang hiệu quả AI
**Mô tả:** Trang `/admin/reports/ai` dùng khung của ADM-UI5.
**Trạng thái:** đã bỏ ngày 2026-10-18. Chatbot chuyển sang dự án riêng `chatbot-rag` và Pickory gỡ `ai-gateway`, nên Pickory không còn số liệu AI để báo cáo.
**Tiêu chí nghiệm thu:**
- [ ] Mỗi feature AI giữ một màu ở mọi biểu đồ
- [ ] Tỉ lệ chuyển đổi từ chat với mẫu nhỏ hiện "Chưa đủ dữ liệu để kết luận"
- [ ] Trang ghi rõ các ngày bị đánh dấu hết ngân sách AI
- [ ] Ảnh chụp 1280 px và 768 px không tràn ngang. Playwright console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp
**Phụ thuộc:** ADM-13, ADM-UI5 · **Quy mô:** M

### ADM-14: API báo cáo CSKH
**Mô tả:** `GET /api/admin/reports/support` đọc `report_daily_support`.
**Tiêu chí nghiệm thu:**
- [ ] Thiếu `reports:read` trả 403. `support` theo quyền mặc định nhận 403
- [ ] p50 và p90 của thời gian phản hồi đầu tiên tính từ histogram của cả khoảng. Route không lấy trung bình phân vị theo ngày
- [ ] Trả CSAT trung bình, số lượt chấm, yêu cầu gọi lại đã xong
- [ ] Số liệu khớp số tính tay trên dữ liệu dựng sẵn
**Kiểm tra:** test tích hợp
**Phụ thuộc:** ADM-10 · **Quy mô:** S

### ADM-UI7: Trang CSKH
**Mô tả:** Trang `/admin/reports/support` dùng khung của ADM-UI5.
**Tiêu chí nghiệm thu:**
- [ ] Hiện số hội thoại chuyển sang người, trong giờ và ngoài giờ, p50 và p90, CSAT, gọi lại
- [ ] Mỗi biểu đồ có bảng số liệu bên dưới
- [ ] Ảnh chụp 1280 px và 768 px không tràn ngang. Playwright console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; ảnh chụp
**Phụ thuộc:** ADM-14, ADM-UI5 · **Quy mô:** S

### Checkpoint A3
- [ ] Hai báo cáo doanh thu và CSKH khớp số tính tay trên bộ dữ liệu test
- [ ] `reports:rebuild` cho kết quả giống job
- [ ] Review với người dùng

---

## Phase 4: Xuất file

### ADM-15: Xuất bảng báo cáo
**Mô tả:** Route xuất bảng số liệu đang xem ra CSV hoặc Excel. Route theo Câu hỏi mở 9, đề xuất `GET /api/admin/exports/reports/{report}?format=csv|xlsx`. Excel dùng thư viện được duyệt ở Câu hỏi mở 1.
**Tiêu chí nghiệm thu:**
- [ ] Thiếu `reports:read` hoặc `reports:export` trả 403. `sales` theo quyền mặc định nhận 403. Endpoint có trong ma trận quyền
- [ ] `format` lạ trả 400 `VALIDATION_ERROR`. Tên báo cáo lạ trả 404
- [ ] Response có `Content-Type` và `Content-Disposition` đúng định dạng, tên file có ngày
- [ ] Test đọc lại file `.xlsx` bằng cùng thư viện và thấy đúng tiêu đề cột tiếng Việt và số liệu
- [ ] Quá 50.000 dòng trả 422 `EXPORT_TOO_LARGE` kèm thông báo chia nhỏ khoảng thời gian
- [ ] Server không ghi file tạm ra đĩa
**Kiểm tra:** test tích hợp
**Phụ thuộc:** ADM-03, ADM-12, ADM-14 · **Quy mô:** M

### ADM-16: Xuất đơn hàng cho kế toán
**Mô tả:** `GET /api/admin/exports/orders?from=&to=&format=` xuất mã đơn, ngày, khách, sản phẩm, tiền, phương thức, trạng thái, thông tin hóa đơn VAT. Dữ liệu đọc qua hàm mới của `ICommerceReports` (Câu hỏi mở 2).
**Tiêu chí nghiệm thu:**
- [ ] Thiếu `orders:read` hoặc `reports:export` trả 403. Endpoint có trong ma trận quyền
- [ ] Mỗi lần xuất ghi đúng 1 dòng audit `admin-console.orders.export` với người xuất, thời điểm, khoảng thời gian, số dòng
- [ ] Dòng audit được commit trước khi stream bắt đầu (quyết định 18 của plan)
- [ ] Bị 403 hoặc 422 thì không ghi dòng audit
- [ ] Quá 50.000 dòng trả 422 `EXPORT_TOO_LARGE`
- [ ] Log không chứa SĐT, email hoặc địa chỉ của khách
**Kiểm tra:** test tích hợp
**Phụ thuộc:** ADM-15 · **Quy mô:** M

### ADM-UI8: Nút xuất file
**Mô tả:** Mỗi trang báo cáo có nút xuất CSV và Excel. Màn đơn hàng của `commerce` dùng component này cho xuất kế toán.
**Tiêu chí nghiệm thu:**
- [ ] Nút chỉ hiện khi người dùng có `reports:export`
- [ ] Bấm nút tải file với khoảng thời gian đang chọn trên URL
- [ ] Lỗi 422 hiện thông báo chia nhỏ khoảng thời gian bằng `ErrorAlert`
- [ ] Nút dùng được bằng bàn phím và có tên truy cập rõ định dạng
- [ ] Playwright console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright tải file
**Phụ thuộc:** ADM-15, ADM-UI5 · **Quy mô:** S

### Checkpoint A4
- [ ] File CSV và Excel mở đúng tiếng Việt
- [ ] Mỗi lần xuất đơn hàng tạo đúng 1 dòng audit
- [ ] Review với người dùng

---

## Phase 5: Nghiệm thu

### ADM-17: Đo p95 báo cáo 30 ngày
**Mô tả:** Lệnh `dotnet run --project apps/backend/src/Pickory.Api -- reports:benchmark` theo mẫu `catalog:benchmark` (quyết định 22 của plan).
**Tiêu chí nghiệm thu:**
- [ ] Lệnh từ chối chạy khi tên DB không chứa `bench`
- [ ] Lệnh sinh 400 ngày dữ liệu tổng hợp, rồi gọi mỗi route báo cáo ít nhất 100 lần với khoảng 30 ngày
- [ ] p95 của `GET /api/admin/reports/sales` và `/support` đều dưới 1 giây trên máy dev
- [ ] Output in p50, p95 và số lần gọi của từng route
**Kiểm tra:** output của lệnh ghi vào báo cáo task
**Phụ thuộc:** ADM-12, ADM-14 · **Quy mô:** S

### ADM-E2E1: Menu và trang chủ theo role
**Mô tả:** E2E đăng nhập `admin`, `sales`, `support` và kiểm menu, badge, trang chủ. Task này đóng mục "sales thấy menu đơn hàng" đang hoãn trong `tasks/identity-ai-gateway/plan.md`. Danh sách menu kiểm theo câu trả lời của Câu hỏi mở 10.
**Tiêu chí nghiệm thu:**
- [ ] `sales` thấy Đơn hàng, Đối soát thanh toán, Sản phẩm, Báo cáo. `sales` không thấy CSKH, Nhân viên, Quản lý Role, Nhật ký thao tác
- [ ] `support` thấy CSKH. `support` không thấy Báo cáo
- [ ] `admin` thấy mọi mục có route
- [ ] Trang chủ của `sales` và `support` hiện đúng thẻ việc cần làm
- [ ] Phần hoãn của `staff-invite.spec.ts` được bật lại: `sales` thấy Đơn hàng
- [ ] Playwright console 0 lỗi và 0 cảnh báo
**Kiểm tra:** `pnpm test:e2e` chế độ có giao diện và chế độ ẩn
**Phụ thuộc:** ADM-UI3, route `/admin/orders` thật của `commerce` · **Quy mô:** M

### ADM-E2E2: Báo cáo doanh thu và xuất Excel
**Mô tả:** Admin mở báo cáo doanh thu, đổi khoảng thời gian, xuất Excel và CSV. Ngày 2026-10-18 task đổi từ báo cáo AI sang báo cáo doanh thu, vì báo cáo AI đã bỏ.
**Tiêu chí nghiệm thu:**
- [ ] Đổi khoảng thời gian cập nhật URL và bảng số liệu
- [ ] File Excel tải về có tên đúng mẫu, kích thước lớn hơn 0 và `Content-Type` của `.xlsx`
- [ ] File CSV tải về bắt đầu bằng BOM và có tiêu đề cột tiếng Việt đúng
- [ ] Người dùng mở thử file Excel bằng Microsoft Excel và xác nhận tiếng Việt đúng (kiểm thủ công, ghi vào báo cáo task)
**Kiểm tra:** `pnpm test:e2e`; kiểm thủ công bằng Excel
**Phụ thuộc:** ADM-UI5, ADM-UI8 · **Quy mô:** S

### ADM-18: Nghiệm thu admin-console
**Mô tả:** Kiểm toàn module trước khi đóng MVP.
**Tiêu chí nghiệm thu:**
- [ ] Ma trận quyền có mọi endpoint của `admin-console`
- [ ] Bộ dữ liệu test có số tính tay cho doanh thu, sản phẩm và CSKH, và mọi test so số đều đạt
- [ ] Coverage dòng của `Pickory.Modules.AdminConsole` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
**Kiểm tra:** `dotnet test`, script gộp coverage
**Phụ thuộc:** ADM-17, ADM-E2E1, ADM-E2E2 · **Quy mô:** S

## Checkpoint cuối
- [ ] Mọi dòng Traceability đạt
- [ ] Coverage dòng của `Pickory.Modules.AdminConsole` ít nhất 80%, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `knowledge-base`
