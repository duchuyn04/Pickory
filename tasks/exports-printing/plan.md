# Implementation Plan: exports-printing

> Trạng thái: người dùng duyệt plan ngày 2026-11-10. Code chưa bắt đầu.

## Tóm tắt

Plan này chia spec `spec/SPEC-exports-printing.md` thành các task nhỏ. Mỗi task làm xong trong một phiên và có tiêu chí nghiệm thu riêng.

Đợt việc có hai phần:

1. Nút "Xuất Excel" ở bốn danh sách admin: đơn hàng, sản phẩm, khách hàng, giao dịch SePay.
2. In hóa đơn bán hàng khổ A4, từng đơn hoặc nhiều đơn một lần.

Thứ tự làm:

1. Phase 1 dựng phần dùng chung: bộ ghi file Excel ở backend, nút tải file ở frontend, công cụ so dữ liệu với fixtures. Fixtures là các file JSON mẫu trong `spec/fixtures/exports-printing/`. Backend và frontend cùng test theo các file này.
2. Phase 2 làm route xuất của từng danh sách, rồi gắn nút vào bốn trang.
3. Phase 3 làm thông tin shop, route dữ liệu in và trang in.
4. Phase 4 chạy E2E (kiểm thử từ đầu tới cuối trên trình duyệt) với backend thật.

Phía frontend không phải chờ phía backend. Trong lúc backend chưa có route, test frontend dùng dữ liệu giả lấy từ fixtures. Phase 4 bỏ dữ liệu giả và chạy với route thật.

Người dùng thấy kết quả đầu tiên sau Checkpoint E1: tải được file Excel đơn hàng từ trang danh sách đơn.

## Overview

Spec: `spec/SPEC-exports-printing.md`. Contract: `tasks/exports-printing/api-contract.md`. Fixtures: `spec/fixtures/exports-printing/`. Task chi tiết: `tasks/exports-printing/todo.md`.

Đợt việc không tạo module mới. Mỗi module (`commerce`, `catalog`, `identity`) tự thêm route xuất cho danh sách của mình. Phần dùng chung nằm trong `Pickory.Shared` và `apps/frontend/src/shared/`.

## Điều kiện trước

1. Người dùng đã duyệt spec ngày 2026-11-10, theo phương án mặc định của 4 câu hỏi mở.
2. Người dùng đã duyệt gói ClosedXML. Gói này là gói NuGet mới duy nhất của đợt việc.
3. Người dùng đã duyệt 4 cột mới trong `commerce_settings` (Câu hỏi mở 1 của spec).
4. Route danh sách của bốn màn đã chạy và có test. Đợt việc dùng lại lớp đọc tham số lọc của các route đó.

## Đồ thị phụ thuộc

```
Backend                                   Frontend
-------                                   --------
EXP-01 bộ ghi .xlsx                       EXP-UI1 đọc fixtures, route giả cho E2E
EXP-02 so response với fixtures             │
  │                                         ├── EXP-UI2 hàm tải file, nút "Xuất Excel"
  ├── EXP-03 xuất đơn hàng                  │     └── EXP-UI3 gắn nút vào 4 danh sách
  ├── EXP-04 xuất giao dịch SePay           │
  ├── EXP-05 xuất sản phẩm                  ├── EXP-UI4 đọc số tiền thành chữ
  ├── EXP-06 xuất khách hàng                │     └── EXP-UI5 trang in hóa đơn
  │                                         │           └── EXP-UI6 nút in, chọn nhiều đơn
  ├── EXP-07 thông tin shop                 └── EXP-UI7 form thông tin shop
  └── EXP-08 route dữ liệu in

                 Điểm gặp nhau (backend thật, không route giả)
                 EXP-E2E1 xuất Excel  ← EXP-03..06, EXP-UI3
                 EXP-E2E2 in hóa đơn  ← EXP-07, EXP-08, EXP-UI5..7
```

EXP-01 cần có trước EXP-03 tới EXP-06. EXP-02 cần có trước mọi task backend thêm route. EXP-08 dùng cột của EXP-07, nên EXP-07 làm trước.

## Architecture Decisions

1. **Mỗi module tự xuất danh sách của mình.** Route xuất nằm cạnh route danh sách, trong cùng module. Module không đọc bảng của module khác. Quy tắc này theo `spec/CAPABILITY-MAP.md`.
2. **Route xuất và route danh sách dùng chung lớp đọc tham số.** Ví dụ: hai route đơn hàng cùng gọi `AdminOrderListQuery.Read`. Route xuất bỏ qua `page` và `limit`. Danh sách thêm bộ lọc thì file xuất tự nhận bộ lọc đó.
3. **Câu truy vấn lọc viết một lần.** Route danh sách và route xuất cùng gọi một hàm dựng `IQueryable` có bộ lọc. Route danh sách thêm phân trang, route xuất thêm `Take(50_001)`. Kết quả có 50.001 dòng thì route trả 422 `EXPORT_TOO_LARGE`. Cách này tránh một câu `COUNT` riêng.
4. **ClosedXML chỉ nằm trong `Pickory.Shared`.** Module khai báo cột bằng `ExportColumn`, rồi gọi `XlsxWorkbookWriter`. `Pickory.ArchitectureTests` chặn module tham chiếu ClosedXML trực tiếp. Sau này đổi thư viện thì chỉ sửa một project.
5. **Ô chữ luôn ghi kiểu chữ.** Bộ ghi đặt kiểu ô trước khi gán giá trị. Excel không chạy chuỗi `=1+1` như công thức. Mã số thuế và số điện thoại không mất số 0 ở đầu.
6. **Tạo file trong bộ nhớ, ghi nhật ký, rồi gửi file.** File tối đa 50.000 dòng nên vừa bộ nhớ. Tạo file lỗi thì server không ghi nhật ký. Ghi nhật ký lỗi thì server không gửi file.
7. **Nhật ký thao tác dùng `IAuditLogWriter` của `identity`.** Module gọi qua project `Pickory.Modules.Identity.Contracts`, giống các thao tác admin khác. `WriteAsync` cần `DbTransaction`, nên module mở transaction trên `DbContext` của mình, ghi nhật ký, rồi commit.
8. **Tên file tạo ở server.** Header `Content-Disposition` có cả `filename` (chữ ASCII) và `filename*` (UTF-8). Tên file chỉ dùng chữ không dấu, nên hai giá trị giống nhau.
9. **Frontend tải file bằng `fetch`, không mở link trực tiếp.** Hàm tải file đọc lỗi JSON khi status khác 200 và hiện thông báo tiếng Việt. Link trực tiếp không đọc được lỗi 422. Hàm tạo `Blob`, rồi tạo thẻ `<a download>` tạm để trình duyệt lưu file.
10. **Trang in là route riêng ngoài khung admin.** `/admin/orders/print?codes=` không có sidebar. Trang vẫn kiểm đăng nhập và quyền `orders:read` như trang admin khác. React Router xếp đoạn tĩnh `print` trước tham số `:code`, nên route không trùng với trang chi tiết đơn. Mã đơn luôn bắt đầu bằng `PK`, nên không có đơn tên `print`.
11. **In bằng CSS của trình duyệt.** `@page { size: A4 portrait; margin: 12mm }`. Mỗi hóa đơn có `break-before: page`, trừ hóa đơn đầu. Dòng bảng có `break-inside: avoid`. `thead` có `display: table-header-group` để lặp tiêu đề bảng ở trang sau.
12. **Hộp thoại in mở một lần.** Trang gọi `window.print()` sau khi dữ liệu tải xong và logo đã `decode()`. Một biến `ref` chặn lần gọi thứ hai khi React render lại.
13. **Chọn nhiều đơn giữ trong state của trang danh sách.** State không lưu vào URL. Đổi trang hoặc đổi bộ lọc thì state về rỗng. Nút "In hóa đơn" mở `/admin/orders/print?codes=...` trong tab mới.
14. **Đọc số tiền thành chữ là hàm thuần ở frontend.** Hàm `formatVndInWords` đọc theo cách thường dùng trên hóa đơn: "linh" cho hàng chục bằng 0, "mốt", "tư", "lăm" theo vị trí. Hàm có test với các số mẫu trong spec.
15. **Thông tin shop lưu trong `commerce_settings`.** Migration thêm 4 cột cho phép rỗng. Body `PUT` thiếu khối `shop` thì server giữ giá trị cũ. Test và E2E hiện có gửi body cũ vẫn đạt.
16. **Kiểm E2E file Excel không thêm gói npm.** E2E kiểm tên file, `Content-Type` và 2 byte đầu `PK` của file zip. Nội dung từng ô do test tích hợp backend kiểm, vì backend đọc lại file bằng ClosedXML.
17. **Kiểm số trang PDF không thêm gói npm.** E2E gọi `page.pdf({ format: 'A4' })`, rồi đếm đối tượng `/Type /Page` trong file PDF. Cách đếm chỉ dùng biểu thức chính quy.

## Task List

Mỗi task một dòng. Tick dòng khi mọi tiêu chí của task trong `todo.md` đã đạt.

### Phase 1: Nền tảng

Backend:
- [x] EXP-01 bộ ghi `.xlsx` dùng chung
- [x] EXP-02 so response và file Excel với fixtures

Frontend:
- [x] EXP-UI1 đọc fixtures và route giả cho E2E
- [x] EXP-UI2 hàm tải file và nút "Xuất Excel"

### Checkpoint E0
- [x] `dotnet build` và `dotnet test` sạch
- [x] `pnpm lint`, `pnpm typecheck`, `pnpm test` sạch
- [x] Test kiến trúc chặn module tham chiếu ClosedXML

### Phase 2: Xuất Excel

Backend:
- [x] EXP-03 xuất đơn hàng
- [x] EXP-04 xuất giao dịch SePay
- [x] EXP-05 xuất sản phẩm
- [ ] EXP-06 xuất khách hàng

Frontend:
- [ ] EXP-UI3 gắn nút "Xuất Excel" vào 4 danh sách

### Checkpoint E1
- [ ] File của 4 danh sách mở được trong Excel, tiếng Việt đúng dấu
- [ ] Số dòng trong file bằng `total` của route danh sách với cùng bộ lọc
- [ ] Review với người dùng

### Phase 3: In hóa đơn

Backend:
- [ ] EXP-07 thông tin shop trong cấu hình bán hàng
- [ ] EXP-08 route dữ liệu in hóa đơn

Frontend:
- [ ] EXP-UI4 đọc số tiền thành chữ
- [ ] EXP-UI5 trang in hóa đơn
- [ ] EXP-UI6 nút in ở chi tiết đơn và chọn nhiều đơn ở danh sách
- [ ] EXP-UI7 form thông tin shop

### Checkpoint E2
- [ ] Hóa đơn của đơn 15 dòng sản phẩm vừa 1 trang A4
- [ ] Review với người dùng

### Phase 4: E2E và nghiệm thu
- [ ] EXP-E2E1 xuất Excel với backend thật
- [ ] EXP-E2E2 in hóa đơn với backend thật

### Checkpoint cuối
- [ ] Mọi mục Success criteria của spec đạt
- [ ] Không còn route giả cho route của đợt việc
- [ ] Review với người dùng

## Traceability: Success criteria → Task

| Tiêu chí trong spec | Task |
|---|---|
| Bốn danh sách có nút "Xuất Excel", nút chỉ hiện khi đủ hai quyền | EXP-UI2, EXP-UI3, EXP-E2E1 |
| File mở được trong Excel, tiếng Việt đúng dấu | EXP-01, EXP-03..06, EXP-E2E1 |
| Số dòng trong file bằng `total` của danh sách | EXP-03..06 |
| Ô tiền có dấu phân cách nghìn, ngày giờ theo giờ Việt Nam | EXP-01 |
| Ô `=1+1` không thành công thức | EXP-01 |
| Hơn 50.000 dòng trả 422, frontend hiện thông báo | EXP-01, EXP-03, EXP-UI2 |
| Thiếu một trong hai quyền trả 403 | EXP-03..06 |
| Đúng 1 dòng nhật ký cho 3 file có dữ liệu cá nhân, 0 dòng cho sản phẩm | EXP-03, EXP-04, EXP-05, EXP-06 |
| Nút "In hóa đơn" ở chi tiết đơn, trang in đủ 10 phần | EXP-08, EXP-UI5, EXP-UI6 |
| Đơn 15 dòng vừa 1 trang A4 | EXP-UI5, EXP-E2E2 |
| In 20 đơn ra PDF 20 trang | EXP-UI6, EXP-E2E2 |
| Tổng tiền bằng chữ đúng với số mẫu | EXP-UI4 |
| Đơn đã hủy có dấu "ĐƠN ĐÃ HỦY" | EXP-UI5, EXP-E2E2 |
| Console sạch, danh sách không tràn ngang ở 375 px | EXP-UI3, EXP-UI5, EXP-UI6 |

## Ảnh hưởng tới plan `admin-console`

Ngày 2026-11-10 plan này ghi chú vào `tasks/admin-console/todo.md` và `tasks/admin-console/plan.md`:

- ADM-16 chuyển sang EXP-03 và EXP-UI3.
- ADM-UI8 dùng nút "Xuất Excel" của EXP-UI2 cho trang báo cáo.
- ADM-15 dùng bộ ghi `.xlsx` của EXP-01.
- Câu hỏi mở 1 của plan `admin-console` có câu trả lời cho thư viện `.xlsx`: ClosedXML.

## Rủi ro

| Rủi ro | Mức | Cách giảm |
|---|---|---|
| File 50.000 dòng làm API tốn nhiều bộ nhớ | Trung bình | Test hiệu năng trong EXP-03 đo thời gian và bộ nhớ với 50.000 đơn mẫu. Vượt 10 giây hoặc 300 MB thì báo người dùng trước khi giảm giới hạn |
| Bộ lọc của route xuất lệch với route danh sách | Trung bình | Quyết định 2 và 3. Test so số dòng file với `total` của danh sách trên cùng bộ lọc |
| Trình duyệt chặn cửa sổ mới khi in nhiều đơn | Thấp | Nút gọi `window.open` ngay trong sự kiện bấm, không sau `await` |
| Chrome và Edge chia trang khác nhau | Thấp | E2E đo trên Chromium. Người dùng kiểm thêm bằng máy in thật ở Checkpoint E2 |
| Logo tải chậm làm hộp thoại in mở khi chưa có logo | Thấp | Quyết định 12: chờ `decode()` của ảnh trước khi gọi `window.print()` |
| `DateTime` ghi sai múi giờ trong Excel | Trung bình | Bộ ghi đổi sang giờ Việt Nam bằng `VietnamCalendar` có sẵn trong `Pickory.Shared/Calendar`. Test có đơn lúc 23:59 và 00:00 giờ Việt Nam |

## Câu hỏi mở

Không còn câu hỏi mở. Người dùng đã duyệt 4 câu của spec ngày 2026-11-10.
