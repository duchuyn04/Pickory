# Implementation Plan: admin-console

## Overview

Module `admin-console` là khung back-office chung và trung tâm báo cáo của Pickory. Màn nghiệp vụ (đơn hàng, sản phẩm, CSKH, kho tri thức) do từng module tự làm. Module này làm bốn phần: menu theo quyền, badge việc cần làm, trang chủ và trang Cấu hình, báo cáo kèm xuất file.

Báo cáo đọc từ bảng tổng hợp theo ngày của module này. Báo cáo không đọc bảng sự kiện thô.

`admin-console` là module cuối trong thứ tự build của `spec/CAPABILITY-MAP.md`. Module đọc số liệu của mọi module khác qua cổng đọc trong project Contracts. Cổng đọc là interface công khai mà module nguồn cung cấp cho module khác.

Spec gốc: `spec/SPEC.md`, `spec/SPEC-admin-console.md`. Spec còn viết interface theo bản Node. Plan này chuyển interface sang .NET theo `tasks/dotnet-migration/plan.md`.
Danh sách task chi tiết: `tasks/admin-console/todo.md`.

## Thay đổi phạm vi ngày 2026-10-18

Chatbot chuyển sang dự án riêng `chatbot-rag`. Pickory gỡ `ai-gateway` theo `tasks/ai-gateway-removal/plan.md`. Vì vậy module này bỏ báo cáo hiệu quả AI.

- Bỏ task ADM-02, ADM-07, ADM-09, ADM-13, ADM-UI6. Heading của các task này vẫn nằm trong `todo.md` để tra cứu.
- Bỏ bảng `report_daily_ai` và hai bảng đề xuất `report_daily_chat`, `report_daily_ai_cost`.
- Báo cáo CSKH bỏ chỉ số dùng bản nháp AI, vì `support-desk` không còn AI soạn nháp.
- ADM-E2E2 kiểm báo cáo doanh thu thay cho báo cáo AI.
- Trang Cấu hình không còn mục AI.
- Thêm ADM-UI9: khối phễu chuyển đổi trên trang doanh thu, đọc route phễu của ACT-11.

## Phần đã phủ bởi plan cũ

1. **Khung back-office tối thiểu.** UI-05 và quyết định 14 của `tasks/identity-ai-gateway/plan.md` đã làm trang `/admin/login`, layout, `useActor()`, `useCan()` và menu ẩn theo quyền. Plan này không làm lại các phần đó.
2. **Giao diện khung admin.** DS-02 của `tasks/design/todo.md` đã làm sidebar, nút thu gọn, ngăn kéo menu ở 375 px theo `shop-retail`. Plan này giữ nguyên giao diện và hành vi bàn phím của khung.
3. **Menu tập trung.** `ADMIN_NAV_GROUPS` trong `apps/frontend/src/features/identity/admin/admin-navigation.ts` đang liệt kê mọi mục menu. `filterNavGroupsByPermission` và `isNavItemActive` đã có test. Plan này chỉ đổi nguồn của danh sách menu (ADM-UI1).
4. **Trang giữ chỗ.** `apps/frontend/src/app.tsx` có `AdminPlaceholderPage` cho `/admin/orders` và `/admin/conversations`. Module `commerce` và `support-desk` thay các trang này. Plan này không làm màn nghiệp vụ.
5. **Báo cáo chi phí AI của `ai-gateway`.** Đã bỏ ngày 2026-10-18. `GET /api/admin/ai/usage` và `GET`, `PUT /api/admin/ai/settings` đã có từ AI-09. Lớp `UsageSummaryReader` đã tính chi phí theo ngày, theo feature, tỉ lệ lỗi và p95. Plan này chỉ đưa lớp đó ra Contracts (ADM-07), không viết lại phép tính.
6. **Đếm SKU sắp hết.** `ICatalogReader.CountLowStockAsync` đã có cho badge. Plan này dùng lại hàm đó.
7. **Quyền.** `reports:read`, `reports:export` đã có trong `Permissions.cs`. Bảng quyền mặc định trong `spec/SPEC-identity.md` đã có hai quyền này. Plan này không thêm quyền mới.
8. **Mục hoãn của identity.** Traceability của `tasks/identity-ai-gateway/plan.md` hoãn phần E2E "sales thấy menu đơn hàng" tới `commerce` hoặc `admin-console`. ADM-E2E1 đóng mục này, sau khi `commerce` có route `/admin/orders` thật.

## Điều kiện trước

1. Plan đổi route cũ sang REST đã xong. Plan đó chạy sau Phase 2 của `shipping` và trước `commerce`.
2. Các module nguồn đã xong và có cổng đọc trong Contracts:
   - `commerce`: `ICommerceReports` (`GetDailySalesAsync`, `GetProductSalesAsync`, `PendingCountsAsync`).
   - `activity-tracking`: `IActivityMetrics.GetFunnelAsync`. Ngày 2026-10-18 `activity-tracking` bỏ `GetFeatureMetricsAsync`.
   - `support-desk`: `ISupportMetrics` (`GetAsync`, `PendingCountAsync`).
   - Ngày 2026-10-14 người dùng bỏ `smart-search`, `recommendations` và `personalized-content`. Plan không còn cần cổng đọc của ba module này.
3. Người dùng duyệt dependency mới: Recharts cho frontend và thư viện ghi `.xlsx` cho backend (Câu hỏi mở 1).

Phase 1 và Phase 2 không cần các module nguồn xong hết. Phase 1 là logic thuần. Phase 2 dùng nguồn đã có (`catalog`) và trả `null` cho nguồn chưa có.

## Đồ thị phụ thuộc

```
Phase 1: logic thuần (không cần module nguồn)
ADM-01 khoảng thời gian, độ chia, giờ Việt Nam
ADM-03 ghi CSV có BOM
 └── ADM-04 khung module, schema `report_daily_*`, migration
ADM-UI1 đăng ký menu theo module (chỉ frontend, chạy song song)

Phase 2: khung back-office
ADM-04 ─┬── ADM-05 API badge ───────────── ADM-UI2 badge trên menu
        ├── ADM-06 API trang chủ ────────── ADM-UI3 trang chủ theo quyền
        └── ADM-UI4 trang Cấu hình (chỉ cần ADM-UI1)

Phase 3: báo cáo (cần module nguồn)
ADM-08 job tổng hợp doanh thu, đơn hàng ──┬── ADM-11 lệnh `reports:rebuild`
ADM-10 job tổng hợp CSKH ─────────────────┘
ADM-08 ── ADM-12 API báo cáo doanh thu ── ADM-UI5 khung trang báo cáo + trang doanh thu
ADM-10 ── ADM-14 API báo cáo CSKH ── ADM-UI7 trang CSKH

Phase 4: xuất file
ADM-03 ── ADM-15 xuất bảng báo cáo CSV và Excel ──┬── ADM-UI8 nút xuất file
          ADM-16 xuất đơn hàng cho kế toán ───────┘

Phase 5: nghiệm thu
ADM-17 lệnh đo p95 báo cáo 30 ngày
ADM-E2E1 menu và trang chủ theo role
ADM-E2E2 báo cáo doanh thu và xuất Excel
ADM-18 nghiệm thu module
```

## Architecture Decisions

1. **Cấu trúc theo `tasks/dotnet-migration/plan.md`.** Project mới là `Pickory.Modules.AdminConsole`. `AdminConsoleModule.cs` có `AddAdminConsoleModule` và `MapAdminConsoleEndpoints`. Không tạo project `Pickory.Modules.AdminConsole.Contracts`, vì không module nào đọc số liệu của `admin-console`. `Pickory.ArchitectureTests` thêm luật cho module mới.
2. **Chuyển interface từ bản Node sang .NET.** Mỗi hàm trong spec chuyển thành hàm `async` trong Contracts của module nguồn:
   - `commerceReports.getDailySales(range)` thành `ICommerceReports.GetDailySalesAsync(DateRange, CancellationToken)`.
   - `supportMetrics.get(range)` thành `ISupportMetrics.GetAsync(DateRange, CancellationToken)`.
   - `activityMetrics.getFunnel(range)` thành `IActivityMetrics.GetFunnelAsync(DateRange, CancellationToken)`. Ngày 2026-10-18 `activity-tracking` bỏ `getFeatureMetrics`.
   - Kiểu `DateRange` đặt ở `Pickory.Shared`, vì nhiều module dùng chung. Đây là đề xuất, xem Câu hỏi mở 4.
3. **Admin-console chỉ ghi bảng của nó.** Bảng tổng hợp có tiền tố `report_daily_`. Module không đọc `DbContext` của module khác. Test kiến trúc chặn tham chiếu project module khác ngoài Contracts.
4. **Bảng tổng hợp.** Ba bảng theo spec: `report_daily_sales`, `report_daily_products`, `report_daily_support`. Ngày 2026-10-14 plan bỏ `report_daily_search`. Ngày 2026-10-18 plan bỏ `report_daily_ai` và hai bảng AI đề xuất.
5. **Phân vị và số khách không cộng được theo ngày.** p50, p90, p95 của nhiều ngày không suy ra từ phân vị của từng ngày. Số khách khác nhau trong 30 ngày cũng không bằng tổng số khách từng ngày. Plan đề xuất lưu histogram theo nhóm thời gian cố định cho các phân vị. Số khách khác nhau trong nhiều ngày chờ người dùng chọn cách tính, xem Câu hỏi mở 6.
6. **Job tổng hợp theo mẫu `ImageDeletionWorker`.** Một worker chạy mỗi giờ cho hôm nay. Một worker chạy mỗi đêm cho hôm qua. Test gọi thẳng processor, không chờ timer. Processor ghi đè dòng của ngày đó, nên chạy lại cho cùng kết quả.
7. **Đơn hoàn tiền sau ngày thanh toán.** Doanh thu ghi nhận theo ngày thanh toán, trừ đơn `refunded`. Đơn có thể chuyển `refunded` nhiều ngày sau. Job đêm tính lại 35 ngày gần nhất, để báo cáo 30 ngày phản ánh hoàn tiền. Xem Câu hỏi mở 7.
8. **Lệnh `reports:rebuild` dùng chung processor với job.** Lệnh là `dotnet run --project apps/backend/src/Pickory.Api -- reports:rebuild --from 2026-10-01 --to 2026-10-07`. Lệnh thay `pnpm reports:rebuild` của spec. Test so kết quả của lệnh với kết quả của job trên cùng bộ dữ liệu.
9. **Thời gian theo `Asia/Ho_Chi_Minh`.** Ngày bắt đầu lúc 00:00 giờ Việt Nam, tức 17:00 UTC hôm trước. Tuần bắt đầu thứ Hai. Tháng theo lịch. Hàm thuần `ReportRange` tính biên và chia nhóm, có test biên nửa đêm.
10. **Kiểm định hai tỉ lệ là hàm thuần.** Đã bỏ ngày 2026-10-18 cùng ADM-02. Hàm nhận số lần thành công và cỡ mẫu của hai nhóm. Hàm trả chênh lệch, khoảng tin cậy 95% theo xấp xỉ chuẩn, và trạng thái "chưa đủ dữ liệu". Trạng thái này bật khi một nhóm có dưới 200 khách hoặc dưới 30 lần chuyển đổi. Ngày bị đánh dấu do hết ngân sách AI bị loại trước khi tính.
    - Ngày 2026-10-14 báo cáo bỏ bảng so sánh nhóm, vì `recommendations` đã bỏ. Hàm này tạm dừng, chờ Câu hỏi mở 14.
11. **Chi phí AI trên mỗi đơn được quy cho AI.** Đã bỏ ngày 2026-10-18. Plan đề xuất công thức: tổng chi phí AI trong kỳ chia số đơn có quy cho AI trong kỳ. Đơn có quy cho AI là đơn mà `IActivityMetrics` quy cho một `source` AI theo luật 7 ngày. Xem Câu hỏi mở 8.
12. **Badge theo quyền ở backend.** `GET /api/admin/badges` dùng `RequireStaff()`. Mỗi số chỉ được đọc khi người gọi có quyền của mục đó:
    - `orders.pendingConfirmation`: `orders:read`.
    - `payments.unmatched`, `payments.codUnreceived`: `payments:read`.
    - `catalog.lowStock`: `products:read`.
    - `support.waiting`, `support.callbacks`: `conversations:read`.
    Thiếu quyền thì khóa đó không có trong response. Module không gọi cổng đọc của mục thiếu quyền.
13. **Một nguồn lỗi không làm hỏng badge.** Mỗi nguồn badge chạy riêng. Nguồn lỗi hoặc chưa đăng ký trả `null` và ghi log cảnh báo. Menu vẫn hiện các badge còn lại.
14. **Menu đăng ký theo module ở frontend.** Mỗi module có file `apps/frontend/src/features/<module-id>/admin/menu.ts` xuất danh sách `AdminMenuItem`. `features/admin-console/` gom các danh sách, rồi lọc bằng `filterNavGroupsByPermission` sẵn có. Nhãn, icon, đường dẫn và thứ tự menu giữ như `ADMIN_NAV_GROUPS` hiện tại, để E2E cũ không đổi locator.
15. **Layout chuyển sang `features/admin-console/`.** `AdminLayout` và `admin-navigation.ts` chuyển từ `features/identity/admin/` sang `features/admin-console/`. Trang `/admin/login` vẫn thuộc `identity`. Test Vitest của layout chuyển theo đường dẫn mới trong cùng task.
16. **Route theo REST mức tài nguyên.** `GET /api/admin/badges`, `GET /api/admin/home`, `GET /api/admin/reports/sales`, `GET /api/admin/reports/support` đã là danh từ. Spec có `GET /api/admin/reports/:report/export?format=`, trong đó `export` là động từ. Plan đề xuất `GET /api/admin/exports/reports/{report}?format=csv|xlsx` và giữ `GET /api/admin/exports/orders?from=&to=&format=`. Hai route xuất file cùng nằm dưới tài nguyên `exports`. Xem Câu hỏi mở 9.
17. **Xuất file stream trong response.** File không lưu trên server. CSV dùng UTF-8 có BOM và escape dấu phẩy, dấu ngoặc kép, xuống dòng theo RFC 4180. Excel dùng thư viện được duyệt ở Câu hỏi mở 1. Quá 50.000 dòng thì trả 422 `EXPORT_TOO_LARGE`, không ghi file dở dang.
18. **Audit log khi xuất đơn hàng.** `IAuditLogWriter.WriteAsync` cần `DbTransaction`. Module mở transaction trên `AdminConsoleDbContext`, ghi một dòng `admin-console.orders.export`, commit, rồi mới stream. Dòng audit ghi người xuất, thời điểm, khoảng thời gian và số dòng. Lỗi khi stream không xóa dòng audit, vì dữ liệu cá nhân có thể đã rời server.
19. **Quyền kép cho xuất đơn hàng.** Route cần cả `orders:read` và `reports:export`. Thiếu một trong hai quyền thì trả 403. Route xuất bảng báo cáo cần cả `reports:read` và `reports:export`.
20. **Biểu đồ.** Frontend dùng Recharts sau khi người dùng duyệt. Mỗi biểu đồ có bảng số liệu bên dưới. Màu lấy từ token trong `apps/frontend/src/shared/styles/tokens.css`.
21. **Bộ chọn thời gian trên URL.** Khoảng thời gian và độ chia nằm trong query string, ví dụ `?range=30d&granularity=day`. Mở lại link cho cùng báo cáo.
22. **Đo p95 bằng lệnh riêng.** Lệnh `reports:benchmark` theo mẫu `catalog:benchmark`. Lệnh chỉ chạy khi tên DB chứa `bench`. Lệnh sinh 400 ngày tổng hợp, rồi đo p95 của hai route báo cáo với khoảng 30 ngày.

## Task List

Chi tiết từng task ở `tasks/admin-console/todo.md`.

### Phase 1: Nền tảng
- [ ] ADM-01 · [ ] ADM-03 · [ ] ADM-04 · [ ] ADM-UI1
- Đã bỏ ngày 2026-10-18: ADM-02

### Checkpoint A1
- [ ] `dotnet build` và `dotnet test` sạch, migration chạy trên DB sạch
- [ ] Test kiến trúc chặn `AdminConsole` tham chiếu module khác ngoài Contracts
- [ ] Bộ E2E hiện có của khung admin vẫn đạt sau khi đổi nguồn menu
- [ ] Review với người dùng

### Phase 2: Khung back-office
- [ ] ADM-05 · [ ] ADM-UI2 · [ ] ADM-06 · [ ] ADM-UI3 · [ ] ADM-UI4

### Checkpoint A2
- [ ] Badge và trang chủ không lộ số của mục người gọi không có quyền
- [ ] Review với người dùng

### Phase 3: Báo cáo
- [ ] ADM-08 · [ ] ADM-10 · [ ] ADM-11 · [ ] ADM-12 · [ ] ADM-UI5 · [ ] ADM-UI9 · [ ] ADM-14 · [ ] ADM-UI7
- Đã bỏ ngày 2026-10-18: ADM-07, ADM-09, ADM-13, ADM-UI6

### Checkpoint A3
- [ ] Hai báo cáo doanh thu và CSKH khớp số tính tay trên bộ dữ liệu test
- [ ] `reports:rebuild` cho kết quả giống job
- [ ] Review với người dùng

### Phase 4: Xuất file
- [ ] ADM-15 · [ ] ADM-16 · [ ] ADM-UI8

### Checkpoint A4
- [ ] File CSV và Excel mở đúng tiếng Việt
- [ ] Mỗi lần xuất đơn hàng tạo đúng 1 dòng audit
- [ ] Review với người dùng

### Phase 5: Nghiệm thu
- [ ] ADM-17 · [ ] ADM-E2E1 · [ ] ADM-E2E2 · [ ] ADM-18

### Checkpoint cuối
- [ ] Mọi dòng Traceability đạt
- [ ] Coverage dòng của `Pickory.Modules.AdminConsole` ít nhất 80%, gộp unit và tích hợp, bỏ `Persistence/Migrations/`
- [ ] Bộ lệnh frontend (`pnpm lint && pnpm typecheck && pnpm test && pnpm build && pnpm test:e2e`) và `dotnet test` sạch
- [ ] Review với người dùng, rồi mới bắt đầu `knowledge-base`

## Traceability: Success Criteria → Task

| Success Criteria (spec) | Task | Cách kiểm |
| --- | --- | --- |
| Mỗi role thấy đúng menu và trang chủ theo quyền (E2E cho `admin`, `sales`, `support`) | ADM-UI1, ADM-06, ADM-UI3, ADM-E2E1 | Vitest lọc menu; test tích hợp `GET /api/admin/home`; E2E ba role |
| Badge khớp số thực tế và không hiện số của mục không có quyền | ADM-05, ADM-UI2 | Test tích hợp với dữ liệu dựng sẵn và ba role; Vitest badge |
| Số liệu doanh thu, đơn hàng, CSKH khớp số tính tay trên bộ dữ liệu test. Ngày 2026-10-18 bỏ phần AI | ADM-08, ADM-10, ADM-12, ADM-14, ADM-18 | Test tích hợp so với số tính tay ghi trong fixture |
| Báo cáo AI hiện tỉ lệ chuyển đổi từ chat; mẫu nhỏ hiện "Chưa đủ dữ liệu để kết luận" | Đã bỏ ngày 2026-10-18 | Không kiểm. Pickory không còn chatbot |
| Báo cáo AI hiện chi phí AI trên mỗi đơn được quy cho AI | Đã bỏ ngày 2026-10-18 | Không kiểm. Pickory không còn gọi AI |
| Mở báo cáo 30 ngày dưới 1 giây (p95) trên máy dev | ADM-17 | Output của `reports:benchmark` |
| CSV và Excel mở trong Excel hiện đúng tiếng Việt; xuất đơn hàng tạo 1 dòng audit | ADM-03, ADM-15, ADM-16, ADM-E2E2 | Unit test BOM và escape; test tích hợp đọc lại file `.xlsx`; test audit; E2E tải file; người dùng mở thử bằng Excel |
| Coverage ít nhất 80% | ADM-18 | Coverage gộp unit và tích hợp, cách đo như shipping |

## Rủi ro

| Rủi ro | Ảnh hưởng | Cách giảm |
| --- | --- | --- |
| Module nguồn chưa có cổng đọc báo cáo | Phase 3 bị chặn | Phase 1 và Phase 2 làm trước. Ghi cổng đọc cần có vào Câu hỏi mở 2 để plan của module nguồn thêm task |
| Phân vị và số khách cộng sai qua nhiều ngày | Báo cáo 30 ngày sai | Quyết định 5: lưu histogram; chờ người dùng chọn cách đếm khách |
| Đổi nguồn menu làm hỏng locator E2E | Bộ E2E cũ đỏ | Giữ nhãn, icon, thứ tự. Chạy `admin-layout-mobile.spec.ts`, `admin-screens.spec.ts`, `staff-invite.spec.ts` trong ADM-UI1 |
| Xuất đơn hàng lộ dữ liệu cá nhân | Vi phạm quyền riêng tư | Quyền kép, audit log trước khi stream, giới hạn 50.000 dòng |
| Hoàn tiền muộn làm sai doanh thu ngày cũ | Doanh thu cao hơn thực tế | Quyết định 7: job đêm tính lại 35 ngày |
| Thư viện Excel chưa được duyệt | ADM-15 bị chặn | CSV làm trước. Excel chờ Câu hỏi mở 1 |

## Câu hỏi mở

1. **Dependency mới (Ask first).** Plan đề xuất:
   - Frontend: `recharts` cho biểu đồ, theo spec.
   - Backend: `ClosedXML` để ghi `.xlsx`. Phương án khác là `DocumentFormat.OpenXml`, nhẹ hơn nhưng phải viết nhiều code hơn.
2. **Cổng đọc báo cáo còn thiếu trong Contracts của module khác.** Ngày 2026-10-18 plan bỏ cổng đọc của `shopping-assistant`. Plan đề xuất:
   - `ICommerceReports` cần thêm hàm đọc đơn cho file kế toán, ví dụ `ReadOrdersForExportAsync(DateRange, int maxRows, CancellationToken)` trả `IAsyncEnumerable`. Spec `commerce` chưa có hàm này.
   - `ICommerceReports.GetDailySalesAsync` cần trả tỉ lệ hủy tách theo lý do (khách hủy, shop hủy, quá hạn). Spec `commerce` chưa nói đơn lưu loại lý do hủy.
3. **`IAiUsageReader` trong `Pickory.Modules.AiGateway.Contracts` (Ask first).** Đã bỏ ngày 2026-10-18. Hàm đề xuất là `GetDailyUsageAsync(DateRange, CancellationToken)`. Hàm trả chi phí, số bước, số lỗi và histogram độ trễ theo ngày và feature. Hàm dùng lại `UsageSummaryReader` hiện có.
4. **Kiểu `DateRange` dùng chung.** Đặt ở `Pickory.Shared` hay mỗi Contracts tự khai báo? Plan đề xuất `Pickory.Shared`, vì năm module cùng dùng.
5. **Hai bảng tổng hợp ngoài spec.** Đã bỏ ngày 2026-10-18. Spec chỉ có bốn bảng `report_daily_*`. Báo cáo chatbot và chi phí theo feature không vừa bốn bảng đó. Người dùng đồng ý thêm `report_daily_chat` và `report_daily_ai_cost` không?
6. **Đếm số khách khác nhau trong nhiều ngày.** Cộng số khách theo ngày sẽ đếm trùng. Hai phương án:
   - `IActivityMetrics` trả số khách khác nhau cho đúng khoảng được hỏi. Kết quả lưu cache 1 giờ trong bảng của `admin-console`.
   - Lưu theo ngày tập mã băm của `anonymousId` theo `source`. Báo cáo đếm số mã khác nhau khi mở. Bảng này chứa mã định danh giả danh, nên cần thời hạn lưu.
   Plan đề xuất phương án đầu. Phương án đầu lệch luật "không truy vấn bảng sự kiện thô" nếu `activity-tracking` đọc bảng thô.
7. **Doanh thu khi hoàn tiền muộn.** Job đêm tính lại 35 ngày gần nhất có đủ không? Hoàn tiền sau 35 ngày cần chạy `reports:rebuild` bằng tay.
8. **Công thức chi phí AI trên mỗi đơn được quy cho AI.** Đã bỏ ngày 2026-10-18. Tử số là tổng chi phí AI mọi feature, hay chỉ feature gắn với `source` có quy chuyển đổi (chatbot)? Plan đề xuất tổng chi phí mọi feature, vì đó là chi phí thật của shop.
9. **Mâu thuẫn route với spec.** Spec có `GET /api/admin/reports/:report/export?format=`. Plan đề xuất `GET /api/admin/exports/reports/{report}?format=` theo quyết định 16. Người dùng chốt route nào?
10. **Mâu thuẫn E2E menu của `sales` với bảng quyền mặc định.** Spec nói `sales` chỉ thấy menu đơn hàng, thanh toán, sản phẩm (xem), báo cáo. Bảng quyền mặc định trong `spec/SPEC-identity.md` cho `sales` thêm `customers:read`, `ai-usage:read`, `knowledge:read`. Vì vậy `sales` còn thấy Khách hàng, Cấu hình AI và mục kho tri thức. `staff-invite.spec.ts` hiện cũng kiểm `sales` thấy Khách hàng và Cấu hình AI. ADM-E2E1 kiểm theo bảng quyền hay theo câu trong spec? Sau AIR-05 của `tasks/ai-gateway-removal/`, `sales` không còn `ai-usage:read` và mục Cấu hình AI.
11. **Chế độ tối.** Spec yêu cầu biểu đồ đọc được trong chế độ sáng và tối. `docs/design/design-system.md` chưa có chế độ tối. Plan đề xuất chỉ kiểm chế độ sáng, cho tới khi design system có chế độ tối.
12. **Trang chủ trên điện thoại.** Spec chỉ yêu cầu trang chủ và badge dùng được trên điện thoại. Plan kiểm trang báo cáo ở 768 px (tablet) và 1280 px. Ở 375 px, plan chỉ kiểm không tràn ngang.
13. Câu hỏi mở của spec giữ mặc định: doanh thu ghi nhận theo ngày thanh toán; chưa gửi báo cáo tự động qua email.
14. **Nhóm đối chứng cho chatbot và số phận ADM-02.** Đã bỏ ngày 2026-10-18 cùng ADM-02. Ngày 2026-10-14 báo cáo bỏ bảng so sánh nhóm `control`, `algo`, `algo_llm`, vì `recommendations` đã bỏ. Chatbot chưa có nhóm đối chứng. Có thêm nhóm đối chứng cho chatbot không, ví dụ ẩn widget với một phần khách chọn ngẫu nhiên? Có thì ADM-02 làm tiếp. Không thì bỏ ADM-02. Mặc định: ADM-02 tạm dừng.
