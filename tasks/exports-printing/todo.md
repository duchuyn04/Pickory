# Task List: exports-printing

Plan và quyết định thiết kế: `tasks/exports-printing/plan.md`. Spec: `spec/SPEC-exports-printing.md`. Contract: `tasks/exports-printing/api-contract.md`. Fixtures: `spec/fixtures/exports-printing/`. Quy ước chung: `spec/SPEC.md`, `AGENTS.md`.

Quy ước:
- Mỗi task backend phải qua `dotnet build` và `dotnet test` trong `apps/backend` trước khi báo xong.
- Mỗi task frontend phải qua `pnpm lint`, `pnpm typecheck` và `pnpm test` trong `apps/frontend`.
- Task thêm route admin thêm route đó vào `apps/backend/tests/Pickory.IntegrationTests/Api/PermissionMatrixTests.cs`.
- Task thêm route có contract test so response với ví dụ trong fixtures. Contract test đi qua một phép thử đột biến: xóa một khóa hoặc đổi kiểu, rồi thấy test đỏ.
- Mỗi test mới đi qua ít nhất một phép thử đột biến trên luật nó bảo vệ.
- Task UI theo `docs/design/design-system.md`. Playwright console có 0 lỗi và 0 cảnh báo.
- E2E chạy headless.
- Test frontend không gọi `page.route` trực tiếp cho route của đợt việc. Chỉ helper của EXP-UI1 được cài route giả.
- Kích thước: XS 1 file, S 1-2, M 3-5, L 6-8. Số file không tính file test.
- Gói mới duy nhất là ClosedXML. Không thêm gói npm.
- Chạy `node scripts/check-agent-text.mjs <file...>` trên mọi file chữ đã đổi.

---

## Phase 1: Nền tảng

### EXP-01: Bộ ghi `.xlsx` dùng chung
**Mô tả:** Thêm ClosedXML vào `Directory.Packages.props` và `Pickory.Shared.csproj`. Tạo thư mục `Pickory.Shared/Exports` gồm:
- `ExportColumn<TRow>`: tiêu đề, kiểu ô, độ rộng, hàm lấy giá trị. Có các hàm tạo `Text`, `Integer`, `Money`, `DateTime`, `YesNo`.
- `XlsxWorkbookWriter`: nhận một hoặc nhiều sheet, mỗi sheet có tên, danh sách cột và danh sách dòng. Trả mảng byte của file.
- `ExportFileName.Create(prefix, utcNow)`: tên file `<prefix>-yyyyMMdd-HHmm.xlsx` theo giờ Việt Nam.
- `ExportLimits.MaxRows = 50_000` và lỗi `AppException("EXPORT_TOO_LARGE", 422, ...)`.
- Hàm tạo kết quả HTTP: `Content-Type` và `Content-Disposition` theo contract.

**Tiêu chí nghiệm thu:**
- [x] Dòng 1 là tiêu đề cột, chữ đậm, được khóa khi cuộn và có bộ lọc của Excel
- [x] Ô tiền có kiểu số và định dạng `#,##0`
- [x] Ô ngày giờ hiện giờ Việt Nam, định dạng `dd/MM/yyyy HH:mm`. Thời điểm `2026-11-09T16:59:00Z` hiện `09/11/2026 23:59`, `2026-11-09T17:00:00Z` hiện `10/11/2026 00:00`
- [x] Ô chữ `=1+1` đọc lại có kiểu chữ và giá trị `=1+1`, ô không có công thức
- [x] Ô chữ `0109876543` đọc lại đúng `0109876543`
- [x] Ô `YesNo` ghi `Có` hoặc `Không`. Giá trị `null` để ô trống
- [x] Tên sheet và tiêu đề tiếng Việt đọc lại đúng từng ký tự
- [x] Sheet không có dòng dữ liệu vẫn có dòng tiêu đề
- [x] `ExportFileName.Create("don-hang", 2026-11-10T08:30:00Z)` trả `don-hang-20261110-1530.xlsx`
- [x] Header `Content-Disposition` có `filename` và `filename*` theo contract
- [x] `Pickory.ArchitectureTests` chặn mọi project `Pickory.Modules.*` tham chiếu ClosedXML
**Kiểm tra:** unit test đọc lại file bằng ClosedXML; test kiến trúc
**Phụ thuộc:** không có · **Quy mô:** M

### EXP-02: So response và file Excel với fixtures
**Mô tả:** Thêm hai helper vào `Pickory.IntegrationTests/Infrastructure`:
- `ContractFixture.AssertMatches(fixtureFile, exampleName, HttpResponseMessage)`: so status, tập khóa và kiểu giá trị (chuỗi, số, true/false, object, mảng) ở mọi đường dẫn. Helper không so giá trị. `null` ở một bên khớp mọi kiểu.
- `WorkbookFixture.AssertMatches(fixtureFile, exampleName, byte[] file, HttpResponseMessage)`: so `Content-Type`, mẫu tên file, tên sheet, dòng tiêu đề và kiểu ô của dòng 2 với `sampleRow`.

Helper đọc file qua `spec/fixtures`, giống `SharedFixtures` của `Pickory.UnitTests`.

**Tiêu chí nghiệm thu:**
- [x] Response thiếu một khóa thì helper báo lỗi có đường dẫn khóa, ví dụ `data.invoices[0].vatInvoice`
- [x] Response thừa một khóa thì helper báo lỗi có đường dẫn khóa
- [x] Khóa có kiểu số trong fixture nhưng chuỗi trong response thì helper báo lỗi
- [x] File thiếu một sheet, sai tiêu đề cột, hoặc ô tiền có kiểu chữ thì `WorkbookFixture` báo lỗi
- [x] Tên ví dụ không có trong fixture thì helper báo lỗi rõ tên ví dụ
**Kiểm tra:** test của chính helper với response và file dựng tay
**Phụ thuộc:** EXP-01 · **Quy mô:** S

### EXP-UI1: Đọc fixtures và route giả cho E2E
**Mô tả:** Thêm helper đọc ví dụ từ `spec/fixtures/exports-printing/` cho Vitest. Thêm helper E2E `installFixtureRoute(page, method, path, fixtureFile, exampleName)`. Trước khi cài route giả, helper đọc `/api/openapi/v1.json` một lần mỗi lượt chạy. Route đã có trong backend thì helper làm test đỏ với thông báo "Backend đã có `<method> <path>`. Bỏ dữ liệu giả của route này.". Helper so đường dẫn cụ thể với mẫu có tham số, ví dụ `/api/admin/orders/PK7KQ2M9XA` khớp `/api/admin/orders/{code}`.

Thêm schema `zod` cho response của `GET /api/admin/order-invoices`.

**Tiêu chí nghiệm thu:**
- [x] Schema `zod` đọc được mọi ví dụ 200 trong `order-invoices.json`
- [x] Xóa khóa `vatInvoice` khỏi một ví dụ thì schema báo lỗi
- [x] Helper E2E làm test đỏ khi OpenAPI đã có route. Test dùng một route có thật, ví dụ `GET /api/admin/orders`
- [x] Helper E2E trả đúng status và body của ví dụ khi route chưa có
- [x] Ví dụ xuất file trả body nhị phân giả có 2 byte đầu `PK` và header theo fixture
**Kiểm tra:** Vitest; một spec Playwright nhỏ cho helper
**Phụ thuộc:** không có · **Quy mô:** M

### EXP-UI2: Hàm tải file và nút "Xuất Excel"
**Mô tả:** Thêm `downloadFile(path)` vào `apps/frontend/src/shared/` cạnh `api.ts`. Hàm gọi `fetch` có cookie phiên, đọc tên file từ `Content-Disposition`, tạo `Blob` và thẻ `<a download>` tạm. Status khác 200 thì hàm đọc lỗi JSON và ném lỗi giống `apiRequest`.

Thêm component `ExportButton` nhận `path`, `requiredPermissions` và nhãn. Component chỉ hiện khi người dùng có mọi quyền trong `requiredPermissions` (dùng `useCan`).

**Tiêu chí nghiệm thu:**
- [ ] Đọc đúng tên file từ `filename*` khi header có cả `filename` và `filename*`
- [ ] Header thiếu tên file thì dùng tên dự phòng `pickory-export.xlsx`
- [ ] Lỗi 422 `EXPORT_TOO_LARGE` hiện thông báo của server trong `ErrorAlert`
- [ ] Lỗi mạng hiện "Không tải được file. Vui lòng thử lại."
- [ ] Đang tải thì nút bị khóa và hiện "Đang tạo file...". Bấm hai lần chỉ gửi một request
- [ ] Thiếu một quyền thì nút không hiện
- [ ] Nút có tên truy cập "Xuất Excel" và dùng được bằng bàn phím
**Kiểm tra:** Vitest với `fetch` giả đọc từ `exports.json`
**Phụ thuộc:** EXP-UI1 · **Quy mô:** S

### Checkpoint E0
- [ ] `dotnet build` và `dotnet test` sạch
- [ ] `pnpm lint`, `pnpm typecheck`, `pnpm test` sạch
- [ ] Test kiến trúc chặn module tham chiếu ClosedXML

---

## Phase 2: Xuất Excel

### EXP-03: Xuất đơn hàng
**Mô tả:** `GET /api/admin/exports/orders` trong `commerce`. Route đọc tham số bằng `AdminOrderListQuery.Read`. Tách phần dựng câu truy vấn có bộ lọc của `AdminOrderService` thành một hàm dùng chung cho route danh sách và route xuất (quyết định 3 của plan). File có sheet "Đơn hàng" và "Chi tiết sản phẩm" theo spec. Mỗi lần xuất ghi một dòng nhật ký `commerce.orders.export`.

**Tiêu chí nghiệm thu:**
- [x] Contract test đạt với ví dụ `orders export success`, `orders export invalid filter`, `orders export missing reports export permission`
- [x] Có `orders:read` nhưng thiếu `reports:export` trả 403. Có `reports:export` nhưng thiếu `orders:read` trả 403. Route có trong ma trận quyền
- [x] Với 5 bộ lọc mẫu, số dòng sheet "Đơn hàng" bằng `total` của `GET /api/admin/orders` cùng bộ lọc
- [x] Sheet "Chi tiết sản phẩm" có một dòng cho mỗi dòng sản phẩm của các đơn trong sheet "Đơn hàng"
- [x] Nhãn trạng thái, phương thức thanh toán và ghi chú đơn là tiếng Việt, khớp nhãn của danh sách
- [x] Đơn có hóa đơn VAT có đủ 4 cột VAT. Cột "Đã xuất hóa đơn VAT" là `Có` khi đơn có cờ `invoice_issued`
- [x] Tham số `page` và `limit` không đổi số dòng trong file
- [x] Bộ lọc khớp 50.001 đơn trả 422 `EXPORT_TOO_LARGE`, không ghi nhật ký. Test đặt giới hạn nhỏ qua cấu hình test để không phải tạo 50.001 đơn
- [x] Xuất thành công ghi đúng 1 dòng nhật ký với người xuất, `details.filters` và `details.rowCount`
- [x] Đo thời gian và bộ nhớ khi xuất 50.000 đơn mẫu trên DB `bench`. Ghi số đo vào mục ghi chú của task
**Kiểm tra:** test tích hợp với DB thật; lệnh đo một lần trên DB `bench`
**Phụ thuộc:** EXP-01, EXP-02 · **Quy mô:** M
Ghi chú đo: DB `pickory_bench_exp03` có 50.000 đơn và 125.000 dòng hàng, trong đó 10.000 đơn có hóa đơn VAT. Ba lần xuất mất 15,41 s, 10,37 s và 11,10 s. Bộ nhớ cấp phát là 2.407 MB, 2.369 MB và 2.368 MB. Đỉnh bộ nhớ của tiến trình test là 503 MB, 551 MB và 551 MB. File có 8.880.427 byte. Thời gian vượt 10 s, nên reviewer quyết định có tối ưu thêm hay không. Riêng bước ghi file của `XlsxWorkbookWriter` mất 5,8 s tới 7,9 s với dữ liệu giả cùng cỡ.

### EXP-04: Xuất giao dịch SePay
**Mô tả:** `GET /api/admin/exports/payment-transactions` trong `commerce`. Route đọc tham số bằng `AdminPaymentTransactionListQuery.Read`. File có sheet "Giao dịch SePay" theo spec. Mỗi lần xuất ghi một dòng nhật ký `commerce.payment-transactions.export`.

**Tiêu chí nghiệm thu:**
- [ ] Contract test đạt với ví dụ `payment transactions export success`
- [ ] Thiếu `payments:read` hoặc `reports:export` trả 403. Route có trong ma trận quyền
- [ ] Số dòng bằng `total` của `GET /api/admin/payments/transactions` với từng giá trị `matchStatus`
- [ ] Giao dịch chưa khớp đơn để trống bốn cột cuối
- [ ] Xuất thành công ghi đúng 1 dòng nhật ký
**Kiểm tra:** test tích hợp
**Phụ thuộc:** EXP-01, EXP-02 · **Quy mô:** S

### EXP-05: Xuất sản phẩm
**Mô tả:** `GET /api/admin/exports/products` trong `catalog`. Route đọc tham số như `GET /api/admin/products`. Mỗi dòng là một SKU. Route không ghi nhật ký.

**Tiêu chí nghiệm thu:**
- [ ] Contract test đạt với ví dụ `products export success`
- [ ] Thiếu `products:read` hoặc `reports:export` trả 403. Route có trong ma trận quyền
- [ ] Sản phẩm có 3 SKU tạo 3 dòng. Cột "Phân loại" ghép giá trị phân loại bằng `, `
- [ ] Bật `lowStock=true` thì file chỉ có SKU sắp hết, khớp danh sách
- [ ] Lọc theo `search`, `categoryId`, `status` cho cùng tập sản phẩm với danh sách
- [ ] Xuất thành công không tạo dòng nhật ký nào
**Kiểm tra:** test tích hợp
**Phụ thuộc:** EXP-01, EXP-02 · **Quy mô:** M

### EXP-06: Xuất khách hàng
**Mô tả:** `GET /api/admin/exports/customers` trong `identity`. Route đọc tham số bằng `CustomerListQuery.Read`. Mỗi lần xuất ghi một dòng nhật ký `identity.customers.export`.

**Tiêu chí nghiệm thu:**
- [ ] Contract test đạt với ví dụ `customers export success`, `customers export invalid status`
- [ ] Thiếu `customers:read` hoặc `reports:export` trả 403. Route có trong ma trận quyền
- [ ] Số dòng bằng `total` của `GET /api/admin/customers` với từng bộ lọc `status` và `emailVerified`
- [ ] Khách bị khóa có "Đã khóa" và ngày khóa. Khách hoạt động để trống ô ngày khóa
- [ ] File không có tài khoản nhân viên
- [ ] Xuất thành công ghi đúng 1 dòng nhật ký
**Kiểm tra:** test tích hợp
**Phụ thuộc:** EXP-01, EXP-02 · **Quy mô:** S

### EXP-UI3: Gắn nút "Xuất Excel" vào 4 danh sách
**Mô tả:** Đặt `ExportButton` trên thanh công cụ của trang đơn hàng, sản phẩm, khách hàng và giao dịch thanh toán. Nút gửi bộ lọc đang chọn, không gửi `page`.

**Tiêu chí nghiệm thu:**
- [ ] Mỗi trang gọi đúng route xuất với đúng bộ lọc trên URL của trang. Test dùng route giả của EXP-UI1
- [ ] Đổi bộ lọc rồi bấm xuất thì request mang bộ lọc mới
- [ ] Người dùng có quyền xem danh sách nhưng thiếu `reports:export` không thấy nút
- [ ] Ở 375 px, nút nằm trong thanh công cụ và trang không tràn ngang
- [ ] Playwright console 0 lỗi và 0 cảnh báo
- [ ] File tải về với backend thật: kiểm ở EXP-E2E1
**Kiểm tra:** Vitest; Playwright với route giả; ảnh chụp 1280 px và 375 px
**Phụ thuộc:** EXP-UI2 · **Quy mô:** M

### Checkpoint E1
- [ ] File của 4 danh sách mở được trong Excel, tiếng Việt đúng dấu
- [ ] Số dòng trong file bằng `total` của route danh sách với cùng bộ lọc
- [ ] Review với người dùng

---

## Phase 3: In hóa đơn

### EXP-07: Thông tin shop trong cấu hình bán hàng
**Mô tả:** Migration thêm `shop_name`, `shop_address`, `shop_phone`, `shop_tax_code` vào `commerce_settings`, đều cho phép rỗng. `GET` và `PUT /api/admin/commerce/settings` có thêm khối `shop` theo contract. Nhật ký sửa cấu hình hiện có ghi cả khối `shop`.

**Tiêu chí nghiệm thu:**
- [ ] Migration chạy trên DB sạch và trên DB có sẵn một dòng cấu hình
- [ ] `GET` trả khối `shop` với 4 trường `null` khi shop chưa nhập
- [ ] `PUT` có khối `shop` lưu đủ 4 trường. Chuỗi chỉ có khoảng trắng lưu thành `null`
- [ ] `PUT` thiếu khối `shop` giữ thông tin shop cũ. Test và E2E hiện có của trang cấu hình vẫn đạt
- [ ] Trường quá dài, số điện thoại có chữ cái, mã số thuế sai `TaxCodeRule` trả 400 `VALIDATION_ERROR` với `details[].key` theo contract
- [ ] Nhật ký sửa cấu hình có giá trị cũ và mới của khối `shop`
**Kiểm tra:** test tích hợp; migration trên DB test
**Phụ thuộc:** EXP-02 · **Quy mô:** M

### EXP-08: Route dữ liệu in hóa đơn
**Mô tả:** `GET /api/admin/order-invoices?codes=` trong `commerce`. Route trả thông tin shop và dữ liệu in của 1 tới 50 đơn, theo thứ tự trong `codes`.

**Tiêu chí nghiệm thu:**
- [ ] Contract test đạt với mọi ví dụ trong `order-invoices.json`
- [ ] Thiếu `orders:read` trả 403. Route có trong ma trận quyền
- [ ] Mã trùng trong `codes` chỉ tạo một hóa đơn
- [ ] `codes` rỗng, 51 mã, hoặc mã sai định dạng trả 400 với `details[].key` là `codes`
- [ ] Một mã không tồn tại trả 404 `ORDER_NOT_FOUND`, `details` có đúng mã đó
- [ ] `lineTotal` bằng `unitPrice × quantity`. `subtotal` bằng tổng `lineTotal`
- [ ] `shop` lấy từ khối `shop` của EXP-07
- [ ] Route chạy đúng 2 câu truy vấn DB với 50 mã: một câu đọc đơn kèm dòng sản phẩm, một câu đọc cấu hình
**Kiểm tra:** test tích hợp
**Phụ thuộc:** EXP-02, EXP-07 · **Quy mô:** M

### EXP-UI4: Đọc số tiền thành chữ
**Mô tả:** Hàm thuần `formatVndInWords(amount)` trong `apps/frontend/src/shared/`. Hàm trả câu viết hoa chữ đầu, kết thúc bằng "đồng".

**Tiêu chí nghiệm thu:**
- [ ] `0` thành "Không đồng"
- [ ] `1000005` thành "Một triệu không trăm linh năm đồng"
- [ ] `12990000` thành "Mười hai triệu chín trăm chín mươi nghìn đồng"
- [ ] `1000000000` thành "Một tỷ đồng"
- [ ] `21`, `15`, `25`, `104`, `1001` đọc đúng "hai mươi mốt", "mười lăm", "hai mươi lăm", "một trăm linh tư", "một nghìn không trăm linh một"
- [ ] Số âm hoặc số không nguyên ném lỗi
**Kiểm tra:** Vitest
**Phụ thuộc:** không có · **Quy mô:** XS

### EXP-UI5: Trang in hóa đơn
**Mô tả:** Route `/admin/orders/print?codes=` ngoài khung admin, vẫn kiểm đăng nhập và `orders:read`. Trang dựng một hóa đơn cho mỗi đơn theo 10 phần của spec. CSS in theo quyết định 11 của plan. Trang mở hộp thoại in một lần theo quyết định 12.

**Tiêu chí nghiệm thu:**
- [ ] Hóa đơn hiện đủ 10 phần của spec với ví dụ `order invoices two orders with vat and cancelled`
- [ ] `shop.name` là `null` thì hóa đơn in "Pickory". Dòng shop có giá trị `null` không hiện
- [ ] Đơn không có `vatInvoice` không hiện khối VAT
- [ ] Đơn `cancelled` có dấu "ĐƠN ĐÃ HỦY" ở đầu trang
- [ ] `window.print` được gọi đúng 1 lần, sau khi dữ liệu và logo tải xong
- [ ] Thanh công cụ "In" và "Đóng" không hiện khi in (`emulateMedia({ media: 'print' })`)
- [ ] Đơn 15 dòng sản phẩm cho PDF A4 1 trang. Đơn 40 dòng cho PDF 2 trang và tiêu đề bảng lặp lại ở trang 2
- [ ] API lỗi 404 hiện `ErrorAlert` có mã đơn thiếu. Thiếu quyền hiện `ForbiddenNotice`
- [ ] Ảnh chụp bản in đen trắng, không có màu nền
- [ ] Playwright console 0 lỗi và 0 cảnh báo
- [ ] Trang in với backend thật: kiểm ở EXP-E2E2
**Kiểm tra:** Vitest; Playwright với route giả, `page.pdf`; ảnh chụp
**Phụ thuộc:** EXP-UI1, EXP-UI4 · **Quy mô:** M

### EXP-UI6: Nút in ở chi tiết đơn và chọn nhiều đơn ở danh sách
**Mô tả:** Trang chi tiết đơn có nút "In hóa đơn". Danh sách đơn có cột ô chọn, ô "chọn tất cả trong trang", dòng "Đã chọn N đơn" và nút "In hóa đơn". Hai nút mở trang in trong tab mới.

**Tiêu chí nghiệm thu:**
- [ ] Nút ở trang chi tiết mở `/admin/orders/print?codes=<mã đơn>` trong tab mới
- [ ] Chọn 3 đơn rồi bấm in mở trang in với 3 mã theo thứ tự trong danh sách
- [ ] Đổi trang hoặc đổi bộ lọc thì danh sách bỏ chọn hết
- [ ] Đã chọn 50 đơn thì các ô còn lại bị khóa và có chú thích "Mỗi lần in tối đa 50 đơn"
- [ ] Chưa chọn đơn nào thì nút "In hóa đơn" của danh sách bị khóa
- [ ] Ô chọn có tên truy cập theo mã đơn, ví dụ "Chọn đơn PK7KQ2M9XA"
- [ ] Ở 375 px, ô chọn và nút in dùng được, trang không tràn ngang
- [ ] Playwright console 0 lỗi và 0 cảnh báo
**Kiểm tra:** Vitest; Playwright với route giả; ảnh chụp 1280 px và 375 px
**Phụ thuộc:** EXP-UI5 · **Quy mô:** M

### EXP-UI7: Form thông tin shop
**Mô tả:** Trang `/admin/commerce/settings` có thêm nhóm trường "Thông tin in trên hóa đơn": tên shop, địa chỉ, số điện thoại, mã số thuế.

**Tiêu chí nghiệm thu:**
- [ ] Form hiện giá trị từ khối `shop` của `GET`. Giá trị `null` hiện ô trống
- [ ] Lưu gửi khối `shop` trong body `PUT`
- [ ] Lỗi 400 của từng trường `shop.*` hiện dưới đúng ô
- [ ] Ô có giới hạn độ dài theo contract
- [ ] Ở 375 px, nhóm trường không tràn ngang. Playwright console 0 lỗi và 0 cảnh báo
- [ ] Lưu với backend thật: kiểm ở EXP-E2E2
**Kiểm tra:** Vitest; Playwright với route giả
**Phụ thuộc:** EXP-UI1 · **Quy mô:** S

### Checkpoint E2
- [ ] Hóa đơn của đơn 15 dòng sản phẩm vừa 1 trang A4
- [ ] Review với người dùng

---

## Phase 4: E2E và nghiệm thu

### EXP-E2E1: Xuất Excel với backend thật
**Mô tả:** Spec `apps/frontend/e2e/admin-exports.spec.ts` đăng nhập admin, mở bốn danh sách, chọn một bộ lọc và bấm "Xuất Excel".

**Tiêu chí nghiệm thu:**
- [ ] Mỗi danh sách tải về một file có tên đúng mẫu, `Content-Type` đúng và 2 byte đầu `PK`
- [ ] Nhân viên thiếu `reports:export` không thấy nút ở cả bốn trang
- [ ] Spec chạy với backend thật và không có route giả cho route của đợt việc
**Kiểm tra:** Playwright headless
**Phụ thuộc:** EXP-03, EXP-04, EXP-05, EXP-06, EXP-UI3 · **Quy mô:** S

### EXP-E2E2: In hóa đơn với backend thật
**Mô tả:** Spec `apps/frontend/e2e/admin-order-invoices.spec.ts` nhập thông tin shop, in một đơn từ trang chi tiết và in 20 đơn từ danh sách.

**Tiêu chí nghiệm thu:**
- [ ] Thông tin shop vừa lưu hiện ở đầu hóa đơn
- [ ] In một đơn cho PDF A4 1 trang có đúng mã đơn
- [ ] In 20 đơn cho PDF 20 trang, mỗi trang có một mã đơn khác nhau
- [ ] Đơn đã hủy có dấu "ĐƠN ĐÃ HỦY"
- [ ] Spec chạy với backend thật và không có route giả cho route của đợt việc
**Kiểm tra:** Playwright headless, `page.pdf`
**Phụ thuộc:** EXP-07, EXP-08, EXP-UI5, EXP-UI6, EXP-UI7 · **Quy mô:** S

### Checkpoint cuối
- [ ] Mọi mục Success criteria của spec đạt
- [ ] Không còn route giả cho route của đợt việc
- [ ] Review với người dùng
