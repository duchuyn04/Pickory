# Spec: exports-printing

> Trạng thái: đã duyệt ngày 2026-11-10, theo phương án mặc định của mọi câu hỏi mở. Plan: `tasks/exports-printing/plan.md`.

Mã đợt việc: `exports-printing` · Tiền tố task: `EXP` · Liên quan: `commerce`, `catalog`, `identity`, `admin-console` · Quy ước chung: `SPEC.md`

## Tóm tắt

Đợt việc này thêm hai chức năng cho trang quản trị (back-office):

1. Xuất danh sách ra file Excel.
2. In hóa đơn bán hàng của đơn hàng.

Xuất Excel có ở bốn danh sách: đơn hàng, sản phẩm, khách hàng, giao dịch thanh toán. Nhân viên lọc danh sách như bình thường, rồi bấm nút "Xuất Excel". Trình duyệt tải về file `.xlsx` chứa mọi dòng khớp bộ lọc, không chỉ trang đang xem. Ví dụ: kế toán lọc đơn tháng 10 đã thanh toán, bấm xuất, nhận một file có đủ 1.240 đơn của tháng đó.

File Excel do server tạo bằng thư viện ClosedXML. Thư viện là một gói code có sẵn mà dự án dùng lại. Server kiểm tra quyền trước khi tạo file. Server ghi nhật ký thao tác (audit log) mỗi lần ai đó xuất file có dữ liệu cá nhân của khách.

In hóa đơn tạo một trang khổ A4. Trang có thông tin shop, khách, bảng sản phẩm và tổng tiền. Nhân viên in theo hai cách:

1. Bấm "In hóa đơn" ở trang chi tiết đơn.
2. Tick nhiều đơn ở danh sách đơn, rồi bấm "In hóa đơn". Mỗi đơn in trên một trang giấy riêng.

Trang in dùng hộp thoại in của trình duyệt. Trong hộp thoại đó, nhân viên chọn máy in, hoặc chọn "Lưu dưới dạng PDF" để có file PDF.

Hóa đơn bán hàng này do shop tự in để giao kèm hàng hoặc lưu sổ. Hóa đơn này không thay hóa đơn giá trị gia tăng (VAT). Shop vẫn xuất hóa đơn VAT trên phần mềm hóa đơn điện tử như `SPEC-commerce.md` mô tả.

Đợt việc xong khi:

- File Excel của cả bốn danh sách mở được trong Excel và hiện đúng tiếng Việt.
- Số dòng trong file bằng số dòng mà danh sách báo với cùng bộ lọc.
- Hóa đơn in ra vừa một trang A4 với đơn có tới 15 dòng sản phẩm.
- In 20 đơn một lần cho ra đúng 20 trang hóa đơn.

## Objective

### Người dùng

| Role | Việc làm với đợt việc này |
|---|---|
| `admin` | Xuất cả bốn danh sách. In hóa đơn. Sửa thông tin shop in trên hóa đơn |
| `sales` | In hóa đơn. Xuất file khi admin cấp quyền `reports:export` |
| `support` | Không dùng, vì role này không có quyền xem đơn hàng theo mặc định |

### User stories

- Là kế toán (role có `orders:read` và `reports:export`), tôi xuất danh sách đơn theo khoảng ngày để đối chiếu doanh thu và hóa đơn VAT.
- Là admin, tôi xuất danh sách sản phẩm theo từng SKU để kiểm giá bán và số còn bán được.
- Là admin, tôi xuất danh sách khách hàng để gửi thông báo ngoài hệ thống.
- Là nhân viên bán hàng, tôi xuất giao dịch SePay để đối soát với sao kê ngân hàng.
- Là nhân viên bán hàng, tôi in hóa đơn của một đơn để giao kèm gói hàng.
- Là nhân viên bán hàng, tôi chọn các đơn đã xác nhận trong ngày và in hóa đơn một lần.

### Ngoài phạm vi

- Xuất CSV. ADM-15 của `admin-console` vẫn giữ CSV cho bảng báo cáo.
- Xuất danh sách khác: nhân viên, nhật ký thao tác, đánh giá, thương hiệu, danh mục.
- Nhập dữ liệu từ file Excel.
- Hóa đơn VAT điện tử, chữ ký số, kết nối phần mềm hóa đơn.
- Phiếu in nhiệt khổ K80 và phiếu giao hàng riêng.
- Server tạo file PDF.
- Cột "số đơn đã mua" trong file khách hàng. Cột này cần dữ liệu của `commerce`, còn danh sách khách thuộc `identity`.

## Thiết kế

### Mỗi module tự xuất danh sách của mình

Mỗi danh sách thuộc một module. Module đó tự thêm route xuất file, dùng lại bộ lọc của route danh sách.

| Danh sách | Module | Route danh sách hiện có | Route xuất mới |
|---|---|---|---|
| Đơn hàng | `commerce` | `GET /api/admin/orders` | `GET /api/admin/exports/orders` |
| Sản phẩm | `catalog` | `GET /api/admin/products` | `GET /api/admin/exports/products` |
| Khách hàng | `identity` | `GET /api/admin/customers` | `GET /api/admin/exports/customers` |
| Giao dịch thanh toán | `commerce` | `GET /api/admin/payments/transactions` | `GET /api/admin/exports/payment-transactions` |

Lý do chọn cách này:

1. Module giữ dữ liệu là module duy nhất đọc bảng của nó. Quy tắc này có trong `spec/CAPABILITY-MAP.md`.
2. Route xuất và route danh sách đọc cùng một lớp tham số lọc. Hai route không lệch nhau khi danh sách thêm bộ lọc mới.

Đường dẫn `/api/admin/exports/...` theo mẫu đã có ở `SPEC-admin-console.md`.

### Bộ ghi Excel dùng chung

`Pickory.Shared` có một bộ ghi file `.xlsx` dùng chung. Chỉ project `Pickory.Shared` tham chiếu gói ClosedXML. Module khai báo danh sách cột, bộ ghi tạo file.

Mỗi cột có:

- tiêu đề tiếng Việt;
- kiểu ô: chữ, số nguyên, tiền VND, ngày giờ, có hoặc không;
- độ rộng.

Quy tắc định dạng chung:

1. Dòng 1 là tiêu đề cột, chữ đậm. Bộ ghi khóa dòng này khi cuộn (freeze) và bật bộ lọc của Excel.
2. Ô tiền là số nguyên, định dạng `#,##0`. Kế toán cộng, trừ được ngay.
3. Ô ngày giờ dùng giờ Việt Nam, định dạng `dd/MM/yyyy HH:mm`.
4. Ô chữ luôn có kiểu chữ. Giá trị `=1+1` hiện đúng là `=1+1`, Excel không chạy như công thức.
5. Ô "có hoặc không" ghi chữ `Có` hoặc `Không`.
6. Giá trị rỗng để ô trống.
7. Tên sheet và tiêu đề cột viết tiếng Việt có dấu.

Tên file theo mẫu `<tên-danh-sách>-<yyyyMMdd-HHmm>.xlsx`, giờ Việt Nam. Ví dụ: `don-hang-20261110-1530.xlsx`. Header `Content-Disposition` mang tên file này.

### Giới hạn và lỗi

1. File có tối đa 50.000 dòng dữ liệu. Danh sách khớp bộ lọc có nhiều dòng hơn thì route trả 422 `EXPORT_TOO_LARGE`. Thông báo lỗi nói người dùng thu hẹp bộ lọc.
2. Bộ lọc sai trả 400 `VALIDATION_ERROR`, giống route danh sách.
3. Danh sách rỗng vẫn trả file. File chỉ có dòng tiêu đề.
4. Route xuất bỏ qua tham số `page` và `limit`. File luôn chứa mọi dòng khớp bộ lọc.

Giới hạn 50.000 dòng lấy từ quyết định 17 của `tasks/admin-console/plan.md`.

### Quyền

| Việc | Quyền cần có |
|---|---|
| Xuất đơn hàng | `orders:read` và `reports:export` |
| Xuất sản phẩm | `products:read` và `reports:export` |
| Xuất khách hàng | `customers:read` và `reports:export` |
| Xuất giao dịch thanh toán | `payments:read` và `reports:export` |
| In hóa đơn | `orders:read` |
| Sửa thông tin shop | `commerce-settings:manage` |

Thiếu một trong hai quyền thì route trả 403. Frontend ẩn nút "Xuất Excel" khi người dùng thiếu quyền. Quyền `reports:export` đã có trong `Permissions.cs`, mặc định chỉ `admin` giữ.

### Nhật ký thao tác

Ba file có dữ liệu cá nhân của khách: đơn hàng, khách hàng, giao dịch thanh toán. Mỗi lần xuất một trong ba file này, server ghi đúng một dòng nhật ký thao tác.

| File | Hành động trong nhật ký |
|---|---|
| Đơn hàng | `commerce.orders.export` |
| Khách hàng | `identity.customers.export` |
| Giao dịch thanh toán | `commerce.payment-transactions.export` |

Dòng nhật ký ghi người xuất, thời điểm, bộ lọc và số dòng. Server tạo xong file trong bộ nhớ, ghi nhật ký, rồi mới gửi file. Tạo file lỗi thì server không ghi nhật ký. File sản phẩm không có dữ liệu cá nhân, nên server không ghi nhật ký.

In hóa đơn không ghi nhật ký. Người in đã có quyền `orders:read` và xem được cùng thông tin ở trang chi tiết đơn.

### Cột của từng file

File đơn hàng có hai sheet.

Sheet "Đơn hàng", mỗi dòng một đơn:

| Cột | Kiểu | Nguồn |
|---|---|---|
| Mã đơn | chữ | `orders.code` |
| Ngày đặt | ngày giờ | `orders.created_at` |
| Trạng thái đơn | chữ | nhãn tiếng Việt của `status` |
| Trạng thái thanh toán | chữ | nhãn tiếng Việt của `payment_status` |
| Phương thức thanh toán | chữ | `COD` hoặc `Chuyển khoản SePay` |
| Tên khách | chữ | `contact.name` |
| Số điện thoại | chữ | `contact.phone` |
| Email | chữ | `contact.email` |
| Địa chỉ giao | chữ | `shipping_address`, ghép thành một dòng |
| Số sản phẩm | số nguyên | tổng `quantity` của các dòng |
| Tạm tính | tiền | `subtotal` |
| Phí vận chuyển | tiền | `shipping_fee` |
| Giảm phí vận chuyển | tiền | `shipping_discount` |
| Tổng tiền | tiền | `total` |
| Tên công ty | chữ | `invoice_info.companyName` |
| Mã số thuế | chữ | `invoice_info.taxCode` |
| Địa chỉ xuất hóa đơn | chữ | `invoice_info.address` |
| Email nhận hóa đơn | chữ | `invoice_info.email` |
| Đã xuất hóa đơn VAT | có hoặc không | cờ `invoice_issued` |
| Ghi chú đơn | chữ | nhãn tiếng Việt của các cờ còn lại, cách nhau bằng dấu phẩy |

Mã số thuế có kiểu chữ. Excel không cắt số 0 ở đầu mã.

Sheet "Chi tiết sản phẩm", mỗi dòng một sản phẩm trong đơn:

| Cột | Kiểu | Nguồn |
|---|---|---|
| Mã đơn | chữ | `orders.code` |
| Ngày đặt | ngày giờ | `orders.created_at` |
| Mã SKU | chữ | `sku_code_snapshot` |
| Tên sản phẩm | chữ | `name_snapshot` |
| Phân loại | chữ | `option_values`, ví dụ `Đen, 256 GB` |
| Số lượng | số nguyên | `quantity` |
| Đơn giá | tiền | `unit_price` |
| Thành tiền | tiền | `unit_price × quantity` |

Giới hạn 50.000 dòng tính trên sheet "Đơn hàng".

File sản phẩm, sheet "Sản phẩm", mỗi dòng một SKU:

| Cột | Kiểu |
|---|---|
| Tên sản phẩm | chữ |
| Mã SKU | chữ |
| Phân loại | chữ |
| Thương hiệu | chữ |
| Danh mục | chữ |
| Giá bán | tiền |
| Số bán được | số nguyên |
| Trạng thái | chữ |
| Ngày tạo | ngày giờ |

Bộ lọc `lowStock` lọc theo SKU. File chỉ chứa SKU sắp hết khi bộ lọc này bật, giống danh sách.

File khách hàng, sheet "Khách hàng":

| Cột | Kiểu |
|---|---|
| Họ tên | chữ |
| Email | chữ |
| Số điện thoại | chữ |
| Đã xác minh email | có hoặc không |
| Trạng thái | `Hoạt động` hoặc `Đã khóa` |
| Ngày khóa | ngày giờ |
| Ngày đăng ký | ngày giờ |

File giao dịch thanh toán, sheet "Giao dịch SePay":

| Cột | Kiểu |
|---|---|
| Mã giao dịch SePay | chữ |
| Thời điểm nhận tiền | ngày giờ |
| Số tiền | tiền |
| Nội dung chuyển khoản | chữ |
| Trạng thái khớp | chữ |
| Mã đơn | chữ |
| Tổng tiền đơn | tiền |
| Trạng thái thanh toán của đơn | chữ |
| Ghi chú đơn | chữ |

Giao dịch chưa khớp đơn nào để trống bốn cột cuối.

Nhãn tiếng Việt của trạng thái và cờ lấy từ cùng nguồn mà danh sách đang dùng. Ví dụ: `OrderLabels.cs` của `commerce`.

### Hóa đơn bán hàng

Route `GET /api/admin/order-invoices?codes=PK...,PK...` trả dữ liệu in cho 1 tới 50 đơn. Frontend dựng trang in từ dữ liệu này. Server không tạo PDF.

Nội dung một hóa đơn, từ trên xuống:

1. Logo Pickory và thông tin shop: tên, địa chỉ, số điện thoại, mã số thuế. Dòng nào chưa có dữ liệu thì không hiện.
2. Tiêu đề "HÓA ĐƠN BÁN HÀNG", mã đơn, ngày đặt, ngày in.
3. Thông tin khách: tên, số điện thoại, địa chỉ giao.
4. Thông tin xuất hóa đơn VAT, chỉ khi đơn có.
5. Bảng sản phẩm: STT, tên sản phẩm kèm phân loại và mã SKU, số lượng, đơn giá, thành tiền.
6. Tạm tính, phí vận chuyển, giảm phí vận chuyển, tổng cộng.
7. Tổng cộng viết bằng chữ, ví dụ "Một triệu hai trăm ba mươi nghìn đồng".
8. Phương thức và trạng thái thanh toán.
9. Hai ô ký tên: "Người mua hàng" và "Người bán hàng".
10. Dòng chú thích: "Hóa đơn này không thay thế hóa đơn giá trị gia tăng."

Đơn đã hủy vẫn in được. Hóa đơn của đơn đã hủy có dấu "ĐƠN ĐÃ HỦY" rõ ở đầu trang.

Trang in nằm ở `/admin/orders/print?codes=...`. Trang không có sidebar và thanh trên của khung admin. Trang có một thanh công cụ nhỏ gồm nút "In" và nút "Đóng". Thanh công cụ không hiện trên giấy.

Quy tắc in:

1. CSS `@page` đặt khổ A4 dọc, lề 12 mm.
2. Mỗi hóa đơn bắt đầu ở trang giấy mới.
3. Đơn có tới 15 dòng sản phẩm vừa một trang A4.
4. Đơn dài hơn thì sang trang sau. Dòng tiêu đề bảng lặp lại ở trang sau. Một dòng sản phẩm không bị cắt đôi giữa hai trang.
5. Trang mở hộp thoại in một lần, sau khi dữ liệu và logo tải xong.
6. Chữ in màu đen trên nền trắng. Trang in không dùng màu nền.

Nút in ở hai nơi:

1. Trang chi tiết đơn có nút "In hóa đơn". Nút mở trang in của đơn đó trong tab mới.
2. Danh sách đơn có cột ô chọn. Thanh trên danh sách hiện "Đã chọn N đơn" và nút "In hóa đơn". Nút mở trang in của các đơn đã chọn trong tab mới.

Ô chọn chỉ giữ đơn của trang danh sách đang xem. Đổi trang hoặc đổi bộ lọc thì danh sách bỏ chọn hết. Người dùng chọn tối đa 50 đơn. Khi đã chọn 50 đơn, các ô chọn còn lại bị khóa và có chú thích lý do.

### Thông tin shop

Hóa đơn cần tên, địa chỉ, số điện thoại và mã số thuế của shop. Hệ thống chưa lưu các thông tin này.

Đề xuất: bảng `commerce_settings` có thêm 4 cột, đều cho phép rỗng:

| Cột | Giới hạn |
|---|---|
| `shop_name` | tối đa 120 ký tự |
| `shop_address` | tối đa 255 ký tự |
| `shop_phone` | tối đa 20 ký tự |
| `shop_tax_code` | 10 hoặc 13 chữ số, theo `TaxCodeRule` |

Trang `/admin/commerce/settings` có thêm nhóm trường "Thông tin in trên hóa đơn". Khi `shop_name` rỗng, hóa đơn in tên "Pickory". Đây là thay đổi schema, cần người dùng duyệt (Câu hỏi mở 1).

## API Contract

Chi tiết từng route nằm ở `tasks/exports-printing/api-contract.md`. Ví dụ request và response nằm ở `spec/fixtures/exports-printing/`.

| Method | Route | Quyền | Module |
|---|---|---|---|
| GET | `/api/admin/exports/orders` | `orders:read` + `reports:export` | `commerce` |
| GET | `/api/admin/exports/products` | `products:read` + `reports:export` | `catalog` |
| GET | `/api/admin/exports/customers` | `customers:read` + `reports:export` | `identity` |
| GET | `/api/admin/exports/payment-transactions` | `payments:read` + `reports:export` | `commerce` |
| GET | `/api/admin/order-invoices?codes=` | `orders:read` | `commerce` |
| GET | `/api/admin/commerce/settings` | `commerce-settings:manage` | `commerce`, thêm khối `shop` |
| PUT | `/api/admin/commerce/settings` | `commerce-settings:manage` | `commerce`, nhận thêm khối `shop` |

Route xuất trả file khi thành công, không bọc trong `{ data }`. Khi lỗi, route xuất trả JSON `{ error: { code, message, details? } }` như mọi route khác.

## Tech stack

| Phần | Công nghệ |
|---|---|
| Backend | .NET 10, ASP.NET Core Minimal API, EF Core, PostgreSQL |
| Thư viện Excel | ClosedXML (giấy phép MIT), thêm vào `Directory.Packages.props` |
| Frontend | React, Vite, TypeScript, TanStack Query |
| In | CSS `@media print` và `@page`, hàm `window.print()` của trình duyệt |
| Test | xUnit, Vitest, Playwright |

Người dùng đã chọn ClosedXML ngày 2026-11-10. Lựa chọn này cũng trả lời phần thư viện `.xlsx` của Câu hỏi mở 1 trong `tasks/admin-console/plan.md`.

## Commands

```
Build backend:      dotnet build apps/backend/Pickory.sln
Test backend:       dotnet test apps/backend/Pickory.sln
Chạy API:           dotnet run --project apps/backend/src/Pickory.Api
Chạy frontend:      pnpm --dir apps/frontend dev
Unit test frontend: pnpm --dir apps/frontend test
Kiểu TypeScript:    pnpm --dir apps/frontend typecheck
Lint frontend:      pnpm --dir apps/frontend lint
E2E:                pnpm --dir apps/frontend test:e2e
Kiểm chữ:           node scripts/check-agent-text.mjs <file...>
```

Tên script lấy từ `apps/frontend/package.json`.

## Project structure

```
apps/backend/src/Pickory.Shared/Exports/                 bộ ghi .xlsx dùng chung, định nghĩa cột
apps/backend/src/Pickory.Modules.Commerce/Orders/Export/       route xuất đơn hàng
apps/backend/src/Pickory.Modules.Commerce/Orders/Invoices/     route dữ liệu in hóa đơn
apps/backend/src/Pickory.Modules.Commerce/Payments/Export/     route xuất giao dịch
apps/backend/src/Pickory.Modules.Catalog/Features/Products/    route xuất sản phẩm
apps/backend/src/Pickory.Modules.Identity/Features/AdminCustomers/  route xuất khách hàng
apps/backend/tests/                                       test xUnit theo project hiện có
apps/frontend/src/shared/                                 nút "Xuất Excel" dùng chung, hàm đọc số thành chữ
apps/frontend/src/features/commerce/admin/orders/         ô chọn đơn, nút in, trang in hóa đơn
apps/frontend/test/unit/                                  test Vitest
apps/frontend/e2e/                                        test Playwright
spec/fixtures/exports-printing/                           fixtures dùng chung
tasks/exports-printing/                                   plan, todo, api-contract
```

Plan chốt vị trí cuối cùng sau khi đọc cấu trúc thư mục của từng module.

## Code style

Code theo `AGENTS.md`: tên biến nói rõ trách nhiệm, chú thích tiếng Việt trước mỗi bước có lý do riêng. Ví dụ khai báo cột của file đơn hàng:

```csharp
// Mã số thuế ghi kiểu chữ. Excel cắt số 0 ở đầu nếu ô có kiểu số.
var taxCodeColumn = ExportColumn.Text("Mã số thuế", order => order.VatInvoice?.TaxCode, width: 16);

// Tiền ghi kiểu số nguyên. Kế toán cộng, trừ trực tiếp trong Excel.
var totalColumn = ExportColumn.Money("Tổng tiền", order => order.Total);
```

## Testing strategy

| Mức test | Phạm vi | Công cụ |
|---|---|---|
| Unit backend | bộ ghi `.xlsx`: kiểu ô, định dạng tiền và ngày, ô chữ bắt đầu bằng `=`, tiếng Việt, file rỗng | xUnit, đọc lại file bằng ClosedXML |
| Tích hợp backend | 4 route xuất và route in: quyền, bộ lọc, số dòng, giới hạn 50.000, nhật ký thao tác, contract | xUnit, DB test thật |
| Kiến trúc | `ClosedXML` chỉ nằm trong `Pickory.Shared`. Module không đọc bảng của module khác | `Pickory.ArchitectureTests` |
| Unit frontend | đọc số thành chữ, nút xuất (tên file, lỗi), chọn đơn tối đa 50, schema đọc fixtures | Vitest |
| E2E | xuất từng danh sách và mở file tải về; in 1 đơn và in nhiều đơn; ảnh chụp trang in | Playwright, headless |

Test E2E của trang in dùng `page.emulateMedia({ media: 'print' })` và `page.pdf({ format: 'A4' })`. Test đếm số trang của file PDF.

## Boundaries

- Luôn làm:
  - Kiểm quyền ở server cho mọi route. Ẩn nút ở frontend chỉ để giao diện gọn.
  - Dùng chung lớp tham số lọc giữa route danh sách và route xuất.
  - Ghi nhật ký thao tác cho file có dữ liệu cá nhân.
  - Chạy `node scripts/check-agent-text.mjs` trên mọi file chữ đã đổi.
- Hỏi trước:
  - Thêm 4 cột thông tin shop vào `commerce_settings` (Câu hỏi mở 1).
  - Thêm gói NuGet hay npm nào khác ngoài ClosedXML.
  - Đổi giới hạn 50.000 dòng hoặc 50 đơn mỗi lần in.
  - Đổi quyền mặc định của role.
- Không làm:
  - Lưu file xuất trên server.
  - Đặt dữ liệu cá nhân của khách vào URL, trừ mã đơn.
  - Đọc bảng của module khác để thêm cột vào file.
  - Gọi dịch vụ ngoài để tạo PDF hay hóa đơn.

## Success criteria

- [ ] Bốn danh sách có nút "Xuất Excel". Nút chỉ hiện với người có đủ hai quyền.
- [ ] File tải về mở được trong Excel. Tiêu đề cột và dữ liệu tiếng Việt hiện đúng dấu.
- [ ] Số dòng trong file bằng `total` của route danh sách với cùng bộ lọc.
- [ ] Ô tiền là số có dấu phân cách nghìn. Ô ngày giờ theo giờ Việt Nam.
- [ ] Ô chữ có giá trị `=1+1` hiện đúng chữ `=1+1`, không thành công thức.
- [ ] Bộ lọc khớp hơn 50.000 dòng trả 422 `EXPORT_TOO_LARGE`. Frontend hiện thông báo tiếng Việt.
- [ ] Thiếu một trong hai quyền thì route xuất trả 403.
- [ ] Xuất đơn hàng, khách hàng, giao dịch tạo đúng 1 dòng nhật ký thao tác. Xuất sản phẩm không tạo dòng nào.
- [ ] Trang chi tiết đơn có nút "In hóa đơn". Trang in hiện đủ 10 phần của hóa đơn.
- [ ] Đơn 15 dòng sản phẩm in vừa 1 trang A4.
- [ ] In 20 đơn đã chọn ra file PDF 20 trang, mỗi trang một mã đơn.
- [ ] Tổng cộng viết bằng chữ đúng với các số mẫu: 0, 1.000.005, 12.990.000, 1.000.000.000.
- [ ] Đơn đã hủy in ra có dấu "ĐƠN ĐÃ HỦY".
- [ ] Trang in và bốn trang danh sách không có lỗi hay cảnh báo trong console. Trang danh sách ở 375 px không tràn ngang.

## Liên hệ với plan `admin-console`

Đợt việc này đổi ba task trong `tasks/admin-console/todo.md`. Plan sẽ sửa các file đó sau khi spec được duyệt.

| Task cũ | Thay đổi |
|---|---|
| ADM-16: xuất đơn hàng cho kế toán | Chuyển sang đợt việc này. Route đổi từ `?from=&to=&format=` sang bộ lọc của danh sách đơn. Chỉ xuất `.xlsx` |
| ADM-UI8: nút xuất file | Nút "Xuất Excel" dùng chung của đợt việc này thay phần nút cho màn đơn hàng. ADM-15 dùng lại nút đó |
| ADM-15: xuất bảng báo cáo | Giữ ở `admin-console`. Phần `.xlsx` dùng bộ ghi chung của đợt việc này |

## Open questions

Ngày 2026-11-10 người dùng duyệt phương án mặc định của cả 4 câu dưới đây. Các câu giữ lại để tra cứu.

1. Lưu thông tin shop trong 4 cột mới của `commerce_settings`? Câu này đổi hình dạng dữ liệu. Mặc định: có, sửa ở trang cấu hình bán hàng. Trong lúc chờ, route in trả `shop` với mọi trường là `null`, hóa đơn in tên "Pickory".
2. Role `sales` có quyền `reports:export` theo mặc định không? Câu này chỉ đổi giá trị. Mặc định: không, chỉ `admin` có. Admin tự cấp cho role khác ở trang Role.
3. Hóa đơn có hiện logo không? Câu này chỉ đổi giá trị. Mặc định: có, dùng `pickory-logo.png` đen trắng khi in.
4. Giới hạn đề xuất cần duyệt: 50.000 dòng mỗi file, 50 đơn mỗi lần in. Câu này chỉ đổi giá trị.
