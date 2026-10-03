# Spec: admin-console

Module id: `admin-console` · Phụ thuộc: `identity`, `catalog`, `commerce`, `activity-tracking`, `smart-search`, `recommendations`, `personalized-content`, `shopping-assistant`, `support-desk`, `ai-gateway` · Quy ước chung: `SPEC.md`

## Objective

Khung back-office chung cho mọi nhân viên và trung tâm báo cáo. Màn hình nghiệp vụ (quản lý sản phẩm, đơn hàng, CSKH, kho tri thức...) do từng module tự làm; `admin-console` lo:
1. **Khung:** đăng nhập, menu theo quyền, trang chủ theo role, badge việc cần làm, trang tổng hợp cấu hình.
2. **Báo cáo:** doanh thu và đơn hàng, hiệu quả AI, CSKH, xuất Excel/CSV.

Báo cáo hiệu quả AI là nơi trả lời câu hỏi quan trọng nhất của dự án: **AI có làm khách mua nhiều hơn không, và chi phí có đáng không.**

**User stories**
- Là nhân viên, tôi đăng nhập và chỉ thấy menu mình có quyền; trang đầu tiên là việc cần làm của tôi.
- Là `sales`, trang chủ của tôi hiện đơn chờ xác nhận, giao dịch SePay chưa khớp, đơn COD chưa nhận tiền, SKU sắp hết hàng.
- Là `support`, trang chủ của tôi hiện hàng chờ hội thoại và yêu cầu gọi lại.
- Là admin, tôi xem doanh thu theo ngày, tuần, tháng; top sản phẩm; tỉ lệ hủy.
- Là admin, tôi so sánh nhóm đối chứng với nhóm có AI về tỉ lệ click, chuyển đổi và doanh thu trên mỗi khách, kèm cảnh báo khi dữ liệu chưa đủ để kết luận.
- Là admin, tôi xem chi phí AI theo tính năng và chi phí AI trên mỗi đơn được quy cho AI.
- Là admin, tôi xuất báo cáo ra Excel hoặc CSV để làm kế toán.

## Thiết kế

### Khung back-office

- Đường dẫn `/admin/*`, đăng nhập bằng `/admin/login` (`identity`).
- **Đăng ký menu:** mỗi module frontend khai báo mục menu của mình kèm quyền cần có:

```ts
// apps/web/src/features/commerce/admin/menu.ts
export const commerceMenu: AdminMenuItem[] = [
  { label: 'Đơn hàng', path: '/admin/orders', permission: 'orders:read', badge: 'orders.pendingConfirmation' },
  { label: 'Đối soát thanh toán', path: '/admin/payments', permission: 'payments:read', badge: 'payments.unmatched' },
];
```

- `admin-console` gom menu của mọi module, chỉ hiện mục người dùng có quyền (`useCan`). Route cũng được chặn theo quyền ở frontend; backend vẫn kiểm tra như mọi nơi khác.
- **Badge:** `GET /api/admin/badges` trả số việc cần làm, mỗi số chỉ được tính nếu người gọi có quyền xem mục đó. Nguồn: `commerce` (đơn chờ xác nhận, giao dịch chưa khớp, COD chưa nhận), `catalog` (SKU sắp hết), `support-desk` (hội thoại chờ, gọi lại), `personalized-content` (banner chờ duyệt). Frontend làm mới mỗi 60 giây.
- **Trang chủ theo quyền:** ghép các thẻ việc cần làm và số liệu nhanh mà người dùng có quyền xem.
- **Trang Cấu hình:** gom link tới cấu hình của từng module (AI, bán hàng, vận chuyển, CSKH, gợi ý), mỗi mục hiện theo quyền.

### Báo cáo

Mọi báo cáo:
- Chọn khoảng thời gian (hôm nay, 7 ngày, 30 ngày, tháng này, tùy chọn) và độ chia (ngày, tuần, tháng).
- Tính theo giờ `Asia/Ho_Chi_Minh`.
- Cần quyền `reports:read`.
- Có nút xuất file (cần `reports:export`).

**1. Doanh thu và đơn hàng** (nguồn: `commerceReports`, *bổ sung vào spec `commerce`*)

| Chỉ số | Định nghĩa |
|---|---|
| Doanh thu ghi nhận | Tổng `total` của đơn có thanh toán `paid`, theo ngày thanh toán, trừ đơn `refunded` |
| Giá trị đơn đặt | Tổng `total` của mọi đơn tạo trong kỳ, trừ đơn hủy vì quá hạn thanh toán |
| Số đơn theo trạng thái | Đếm theo trạng thái hiện tại |
| Tỉ lệ hủy | Đơn `cancelled` / đơn tạo trong kỳ, tách theo lý do (khách hủy, shop hủy, quá hạn) |
| Giá trị trung bình đơn | Doanh thu ghi nhận / số đơn `paid` |
| Top sản phẩm, danh mục | Theo doanh thu và số lượng |
| Phương thức thanh toán | Tỉ lệ COD và SePay |
| Phí ship shop chịu | Tổng `shippingDiscount` |

**2. Hiệu quả AI** (nguồn: `activityMetrics`, báo cáo của `recommendations`, `smart-search`, `shopping-assistant`, `ai-gateway`)

- **Gợi ý sản phẩm:** CTR và tỉ lệ chuyển đổi theo vị trí; bảng so sánh 3 nhóm `control`, `algo`, `algo_llm` gồm số khách, CTR, tỉ lệ chuyển đổi, **doanh thu trên mỗi khách**.
- **Kiểm định thống kê:** với mỗi cặp nhóm, hiện chênh lệch kèm khoảng tin cậy 95% (kiểm định hai tỉ lệ). Nhóm nào dưới 200 khách hoặc dưới 30 lần chuyển đổi thì hiện "Chưa đủ dữ liệu để kết luận" thay vì kết luận thắng thua. Khoảng thời gian đã bị đánh dấu do hết ngân sách AI thì bị loại khỏi phép so sánh.
- **Tìm kiếm:** số lượt tìm, tỉ lệ không có kết quả, tỉ lệ click từ kết quả, tỉ lệ câu tìm phải gọi LLM.
- **Chatbot:** số hội thoại, tỉ lệ thích, tỉ lệ chuyển CSKH, tỉ lệ tin bị gắn cờ grounding, tỉ lệ chuyển đổi từ chat.
- **Chi phí AI:** theo ngày và theo feature, % ngân sách tháng, **chi phí AI trên mỗi đơn được quy cho AI**, tỉ lệ lỗi và độ trễ p95 của provider.

**3. CSKH** (nguồn: `supportMetrics`): số hội thoại chuyển sang người, xử lý trong giờ và ngoài giờ, thời gian phản hồi đầu tiên p50/p90, thời gian xử lý, CSAT, yêu cầu gọi lại đã xử lý, tỉ lệ dùng bản nháp AI.

**Hiệu năng:**
- Báo cáo đọc từ bảng tổng hợp theo ngày (`report_daily_*`), job tính mỗi giờ cho hôm nay và mỗi đêm cho hôm qua. Không truy vấn bảng sự kiện thô khi người dùng mở báo cáo.
- Mỗi module tự cung cấp số liệu qua interface của mình. `admin-console` ghép lại và lưu vào bảng tổng hợp của nó.

### Xuất file

- Định dạng CSV (UTF-8 có BOM để Excel đọc đúng tiếng Việt) và Excel (.xlsx).
- Mỗi báo cáo xuất được bảng số liệu đang xem.
- **Xuất danh sách đơn hàng** cho kế toán: mã đơn, ngày, khách, sản phẩm, tiền, phương thức, trạng thái, thông tin hóa đơn VAT. Có dữ liệu cá nhân nên cần cả `orders:read` và `reports:export`, và mỗi lần xuất được ghi audit log (ai, khi nào, khoảng thời gian, số dòng).
- Tối đa 50.000 dòng mỗi lần xuất. Lớn hơn thì yêu cầu chia nhỏ khoảng thời gian.
- File tạo trực tiếp trong response (stream), không lưu lại trên server.

### Quyền mới

*(bổ sung vào danh mục quyền của `identity`)*
- `reports:export` (mặc định: admin): xuất file báo cáo.
- `support-settings:manage` (mặc định: admin): cấu hình CSKH, thuộc `support-desk`.

## API

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/admin/badges` | nhân viên | Số việc cần làm theo quyền |
| GET | `/api/admin/home` | nhân viên | Thẻ trang chủ theo quyền |
| GET | `/api/admin/reports/sales` | `reports:read` | Doanh thu và đơn hàng |
| GET | `/api/admin/reports/ai` | `reports:read` | Hiệu quả AI và chi phí |
| GET | `/api/admin/reports/support` | `reports:read` | CSKH |
| GET | `/api/admin/reports/:report/export?format=csv\|xlsx` | `reports:read` + `reports:export` | Xuất bảng số liệu |
| GET | `/api/admin/exports/orders?from=&to=&format=` | `orders:read` + `reports:export` | Xuất đơn hàng cho kế toán |

## Data Model (Prisma, rút gọn)

```prisma
model ReportDailySales   { /* date @id, revenue, orderValue, ordersCreated, ordersPaid, cancelled Json, byPaymentMethod Json, shippingDiscount */ }
model ReportDailyProduct { /* date, productId, revenue, quantity; @@id([date, productId]) */ }
model ReportDailyAi      { /* date, source, variant, impressions, clicks, conversions, revenue, users, aiCostUsd, budgetFlagged; @@id([date, source, variant]) */ }
model ReportDailySupport { /* date @id, handoffs, inHours, callbacks, firstResponseP50, firstResponseP90, csatSum, csatCount, draftUsed */ }
```

- Tính lại một ngày bất kỳ được (`pnpm reports:rebuild -- --from --to`), dùng khi sửa cách tính.

## Frontend

- `features/admin-console/`: layout (thanh bên theo quyền, badge, công tắc trạng thái CSKH nếu có quyền), trang chủ, trang Cấu hình, 3 trang báo cáo.
- Biểu đồ dùng thư viện Recharts, theo quy tắc trực quan hóa chung: màu nhất quán cho từng nhóm, có bảng số liệu bên dưới mỗi biểu đồ, đọc được trong chế độ sáng và tối.
- Mỗi trang báo cáo có bộ chọn thời gian trên URL để chia sẻ link.
- Dùng được trên tablet. Trên điện thoại chỉ cần trang chủ và badge.

## Testing

- Unit:
  - Định nghĩa chỉ số (doanh thu ghi nhận loại đơn hoàn tiền, tỉ lệ hủy theo lý do).
  - Kiểm định hai tỉ lệ và ngưỡng "chưa đủ dữ liệu".
  - Gộp theo tuần, tháng theo giờ Việt Nam (biên nửa đêm).
  - Lọc menu và badge theo quyền.
  - CSV có BOM, escape dấu phẩy và xuống dòng.
- Integration:
  - Job tổng hợp trên bộ dữ liệu dựng sẵn ra đúng số đã tính tay.
  - `reports:rebuild` cho kết quả giống job.
  - Xuất đơn hàng ghi audit log.
  - Người thiếu `reports:export` nhận 403.
  - Badge không lộ số của mục người gọi không có quyền.
- E2E:
  - Đăng nhập `sales` chỉ thấy menu đơn hàng, thanh toán, sản phẩm (xem), báo cáo.
  - Đăng nhập `support` thấy CSKH, không thấy báo cáo.
  - Admin mở báo cáo AI, đổi khoảng thời gian, xuất Excel mở được và đúng tiếng Việt.

## Boundaries (riêng module)

- **Always:** đọc báo cáo từ bảng tổng hợp; ghi audit log mỗi lần xuất file có dữ liệu cá nhân; hiện cảnh báo "chưa đủ dữ liệu" thay vì kết luận khi mẫu nhỏ.
- **Ask first:** đổi định nghĩa chỉ số doanh thu; thêm báo cáo có dữ liệu cá nhân; gửi báo cáo tự động qua email.
- **Never:** ghi vào bảng của module khác; truy vấn bảng sự kiện thô khi người dùng mở báo cáo; lưu file xuất trên server.

## Success Criteria

- [ ] Mỗi role thấy đúng menu và trang chủ theo quyền (test E2E cho `admin`, `sales`, `support`).
- [ ] Badge khớp số thực tế và không hiện số của mục không có quyền.
- [ ] Số liệu doanh thu, đơn hàng, AI, CSKH khớp với số tính tay trên bộ dữ liệu test.
- [ ] Bảng so sánh nhóm đối chứng hiện khoảng tin cậy 95%; mẫu nhỏ hiện "Chưa đủ dữ liệu để kết luận".
- [ ] Báo cáo AI hiện chi phí AI trên mỗi đơn được quy cho AI.
- [ ] Mở báo cáo 30 ngày dưới 1 giây (p95) trên máy dev.
- [ ] File CSV và Excel mở trong Excel hiển thị đúng tiếng Việt; xuất đơn hàng tạo 1 dòng audit log.
- [ ] Coverage `service.ts` ≥ 80%.

## Open Questions

1. "Doanh thu ghi nhận" tính theo ngày thanh toán như trên có khớp cách kế toán của shop không? Một số nơi tính theo ngày giao hàng thành công.
2. Có cần gửi báo cáo tự động (ví dụ báo cáo tuần qua email) không? Mặc định: chưa.
